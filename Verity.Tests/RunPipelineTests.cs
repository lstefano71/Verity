namespace Verity.Tests;

public class RunPipelineTests
{
  private static RunContext NewRun(bool useVss = false)
  {
    var dir = Path.GetTempPath();
    var options = new CliOptions(new FileInfo(Path.Combine(dir, "m.sha256")), new DirectoryInfo(dir), "SHA256", UseVss: useVss);
    return new RunContext(options, threads: 1, CancellationToken.None);
  }

  private static PhaseHandler Goto(RunPhase next, List<RunPhase>? visited = null, RunPhase? self = null) =>
    _ => { visited?.Add(self!.Value); return Task.FromResult(next); };

  [Fact]
  public async Task RunAsync_WalksAllPhasesInOrder_AndReturnsExitCode()
  {
    var visited = new List<RunPhase>();
    var run = NewRun();

    var code = await RunPipeline.RunAsync(run, new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = Goto(RunPhase.Header, visited, RunPhase.VssSetup),
      [RunPhase.Header] = Goto(RunPhase.Prepare, visited, RunPhase.Header),
      [RunPhase.Prepare] = Goto(RunPhase.Process, visited, RunPhase.Prepare),
      [RunPhase.Process] = Goto(RunPhase.Report, visited, RunPhase.Process),
      [RunPhase.Report] = r => { visited.Add(RunPhase.Report); r.ExitCode = 7; return Task.FromResult(RunPhase.Finished); },
    });

    Assert.Equal([RunPhase.VssSetup, RunPhase.Header, RunPhase.Prepare, RunPhase.Process, RunPhase.Report], visited);
    Assert.Equal(7, code);
    Assert.Equal(RunPhase.Finished, run.Phase);
  }

  [Fact]
  public async Task RunAsync_EarlyExit_SkipsRemainingPhases()
  {
    var visited = new List<RunPhase>();

    var code = await RunPipeline.RunAsync(NewRun(), new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = Goto(RunPhase.Header),
      [RunPhase.Header] = Goto(RunPhase.Prepare),
      [RunPhase.Prepare] = r => { visited.Add(RunPhase.Prepare); return Task.FromResult(RunPipeline.Exit(r, ExitCodes.Warnings)); },
      // Process/Report intentionally absent: reaching them would throw KeyNotFoundException.
    });

    Assert.Equal(ExitCodes.Warnings, code);
    Assert.Equal([RunPhase.Prepare], visited);
  }

  [Fact]
  public async Task RunAsync_BackwardTransition_Throws()
  {
    var handlers = new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = Goto(RunPhase.Prepare),
      [RunPhase.Prepare] = Goto(RunPhase.Header), // going back is illegal
    };

    await Assert.ThrowsAsync<InvalidOperationException>(() => RunPipeline.RunAsync(NewRun(), handlers));
  }

  [Fact]
  public async Task RunAsync_SelfTransition_Throws()
  {
    var handlers = new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = Goto(RunPhase.VssSetup),
    };

    await Assert.ThrowsAsync<InvalidOperationException>(() => RunPipeline.RunAsync(NewRun(), handlers));
  }

  [Fact]
  public async Task RunAsync_HandlerException_Propagates()
  {
    var handlers = new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = _ => throw new OperationCanceledException(),
    };

    await Assert.ThrowsAsync<OperationCanceledException>(() => RunPipeline.RunAsync(NewRun(), handlers));
  }

  [Fact]
  public async Task RunAsync_DisposesVssManager_WhenPipelineLeavesNormally()
  {
    var run = NewRun();
    var manager = new Verity.Utilities.VssSnapshotManager("C:\\");
    run.VssManager = manager;

    await RunPipeline.RunAsync(run, new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = r => Task.FromResult(RunPipeline.Exit(r, 0)),
    });

    Assert.Equal(Verity.Utilities.VssSnapshotState.Disposed, manager.State);
  }

  [Fact]
  public async Task RunAsync_DisposesVssManager_WhenHandlerThrows()
  {
    var run = NewRun();
    var manager = new Verity.Utilities.VssSnapshotManager("C:\\");
    run.VssManager = manager;

    await Assert.ThrowsAsync<InvalidOperationException>(() => RunPipeline.RunAsync(run, new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = _ => throw new InvalidOperationException("boom"),
    }));

    Assert.Equal(Verity.Utilities.VssSnapshotState.Disposed, manager.State);
  }

  [Fact]
  public async Task VssSetup_NotRequested_GoesStraightToHeader_WithoutManager()
  {
    var run = NewRun(useVss: false);

    var next = await RunPipeline.VssSetupAsync(run);

    Assert.Equal(RunPhase.Header, next);
    Assert.Null(run.VssManager);
  }

  [Fact]
  public void Stopwatch_IsNotRunning_UntilProcessStartsIt()
  {
    // All commands time only the Process phase, so setup/scan time is never counted.
    var run = NewRun();

    Assert.False(run.Stopwatch.IsRunning);
    Assert.Equal(TimeSpan.Zero, run.Stopwatch.Elapsed);
  }

  [Theory]
  [InlineData(0, 0, 0, ExitCodes.Success)]
  [InlineData(5, 2, 0, ExitCodes.Warnings)]
  [InlineData(5, 0, 1, ExitCodes.Errors)]
  [InlineData(5, 3, 1, ExitCodes.Errors)] // errors beat warnings
  public void ExitCodes_FromSummary_ErrorsBeatWarningsBeatSuccess(int success, int warnings, int errors, int expected)
  {
    var summary = new FinalSummary(success + warnings + errors, success, warnings, errors, 0, []);

    Assert.Equal(expected, ExitCodes.FromSummary(summary));
  }
}
