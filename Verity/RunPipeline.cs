using Spectre.Console;

using System.Diagnostics;

using Verity.Utilities;

/// <summary>
/// Phases of one command run (verify / create / add). Transitions are forward-only:
/// <code>
/// VssSetup ──► Header ──► Prepare ──► Process ──► Report ──► Finished
///    │            │          │           │           │
///    └────────────┴──────────┴───────────┴───────────┴──► Finished   (early exit: ExitCode already set)
/// </code>
/// VSS cleanup always runs when the pipeline leaves, whichever way it leaves.
/// </summary>
internal enum RunPhase
{
  VssSetup,
  Header,
  Prepare,
  Process,
  Report,
  Finished
}

/// <summary>Process exit codes of the CLI (VSS-specific codes live in <see cref="VssErrorCode"/>).</summary>
internal static class ExitCodes
{
  public const int Success = 0;
  public const int Warnings = 1;
  public const int Errors = -1;
  public const int Interrupted = -2;

  /// <summary>Errors beat warnings beat success.</summary>
  public static int FromSummary(FinalSummary summary) =>
    summary.ErrorCount > 0 ? Errors
    : summary.WarningCount > 0 ? Warnings
    : Success;
}

/// <summary>Mutable state shared by the phases of a single run.</summary>
internal sealed class RunContext(CliOptions options, int threads, CancellationToken cancellationToken)
{
  public CliOptions Options { get; } = options;
  public int Threads { get; } = threads;
  public CancellationToken CancellationToken { get; } = cancellationToken;

  public string RootPath { get; } = options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!;
  public DateTime StartTime { get; } = DateTime.Now;

  /// <summary>Times the Process phase only (started there), so all commands report the same thing.</summary>
  public Stopwatch Stopwatch { get; } = new();

  public VssSnapshotManager? VssManager { get; set; }
  public VssPathResolver? VssResolver { get; set; }

  /// <summary>
  /// Where to enumerate files: <see cref="RootPath"/>, or its snapshot equivalent in VSS mode so
  /// the scan sees the same point-in-time view as the hashing.
  /// </summary>
  public string ScanRoot => VssResolver?.ResolveDirectoryPath(RootPath) ?? RootPath;

  public FinalSummary Summary { get; set; } = new(0, 0, 0, 0, 0, []);

  /// <summary>Exit code to return; set by the Report phase or by a phase that ends the run early.</summary>
  public int ExitCode { get; set; } = ExitCodes.Success;

  public RunPhase Phase { get; set; } = RunPhase.VssSetup;
}

/// <summary>A phase: does its work and names the phase to run next.</summary>
internal delegate Task<RunPhase> PhaseHandler(RunContext run);

internal static class RunPipeline
{
  /// <summary>
  /// Drives <paramref name="run"/> through the handlers until <see cref="RunPhase.Finished"/>.
  /// Exceptions (including cancellation) propagate to the caller after VSS cleanup.
  /// </summary>
  public static async Task<int> RunAsync(RunContext run, IReadOnlyDictionary<RunPhase, PhaseHandler> handlers)
  {
    try {
      while (run.Phase != RunPhase.Finished) {
        var next = await handlers[run.Phase](run);
        if (next <= run.Phase)
          throw new InvalidOperationException($"Illegal phase transition {run.Phase} -> {next}.");
        run.Phase = next;
      }
      return run.ExitCode;
    } finally {
      if (run.VssManager != null) {
        // A failed creation has already rolled itself back; only announce real cleanup.
        if (run.VssManager.IsSnapshotActive)
          AnsiConsole.MarkupLine("[yellow]Cleaning up VSS snapshot...[/]");
        run.VssManager.Dispose();
      }
    }
  }

  /// <summary>Ends the run early with <paramref name="exitCode"/>.</summary>
  public static RunPhase Exit(RunContext run, int exitCode)
  {
    run.ExitCode = exitCode;
    return RunPhase.Finished;
  }

  /// <summary>Creates the VSS snapshot when requested; otherwise a no-op.</summary>
  public static async Task<RunPhase> VssSetupAsync(RunContext run)
  {
    if (!run.Options.UseVss)
      return RunPhase.Header;

    var volumeRoot = Path.GetPathRoot(run.RootPath);
    if (string.IsNullOrEmpty(volumeRoot)) {
      AnsiConsole.MarkupLine("[red]Error: Unable to determine volume root for VSS snapshot.[/]");
      return Exit(run, (int)VssErrorCode.VolumeNotSupported);
    }

    AnsiConsole.MarkupLine($"[yellow]Creating VSS snapshot for volume {Markup.Escape(volumeRoot)}...[/]");

    try {
      var manager = new VssSnapshotManager(volumeRoot);
      run.VssManager = manager; // assigned first so the pipeline disposes it even if creation fails
      await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("yellow"))
          .StartAsync("Creating volume snapshot...", async _ => {
            await manager.CreateSnapshotAsync(run.CancellationToken);
          });

      run.VssResolver = manager.CreatePathResolver();
      AnsiConsole.MarkupLine("[green]VSS snapshot created successfully.[/]");
      return RunPhase.Header;
    } catch (OperationCanceledException) {
      throw; // user cancellation is handled by the command, not reported as a VSS failure
    } catch (VssSnapshotException ex) {
      AnsiConsole.MarkupLine($"[red]VSS Error: {Markup.Escape(ex.Message)}[/]");
      return Exit(run, (int)VssErrorCode.SnapshotCreationFailed);
    } catch (Exception ex) {
      AnsiConsole.MarkupLine($"[red]Unexpected error creating VSS snapshot: {Markup.Escape(ex.Message)}[/]");
      return Exit(run, (int)VssErrorCode.SnapshotCreationFailed);
    }
  }

  /// <summary>Writes the header panel with the given title.</summary>
  public static PhaseHandler Header(string title) => run => {
    AnsiConsole.Write(Utilities.BuildHeaderPanel(
        title,
        run.StartTime,
        run.Options.ChecksumFile.Name,
        run.Options.Algorithm,
        run.RootPath,
        run.Options.IncludeGlobs,
        run.Options.ExcludeGlobs,
        run.Options.UseVss ? "VSS Snapshot Mode" : null));
    return Task.FromResult(RunPhase.Prepare);
  };

  /// <summary>Shared tail of every report: summary table, optional diagnostics table and TSV report.</summary>
  public static async Task RenderReportAsync(RunContext run)
  {
    ResultsPresenter.RenderSummaryTable(run.Summary, run.Stopwatch.Elapsed);
    if (run.Options.ShowTable)
      ResultsPresenter.RenderDiagnosticsTable(run.Summary);
    if (run.Options.TsvReportFile != null)
      await ResultsPresenter.WriteErrorReportAsync(run.Summary, run.Options.TsvReportFile);
    run.ExitCode = ExitCodes.FromSummary(run.Summary);
  }
}
