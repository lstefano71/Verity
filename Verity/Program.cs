using ConsoleAppFramework;

using Humanizer;

using Spectre.Console;

using System.Diagnostics;

using Verity.Utilities;

public class Program
{

  // The application entry point is simplified to just run our class.
  public static async Task Main(string[] args)
  {
    // Check if VSS mode is requested by looking for --vss flag in arguments
    bool vssRequested = args.Any(arg => string.Equals(arg, "--vss", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(arg, "-vss", StringComparison.OrdinalIgnoreCase));

    // If VSS mode is requested and we're not elevated, try to restart with elevation
    if (vssRequested && !ElevationHelper.IsElevated())
      AnsiConsole.MarkupLine("[yellow]VSS mode requires administrator privileges. Requesting elevation...[/]");

    var elevation = await ElevationHelper.EnsureElevatedAsync(args, vssRequested);
    switch (elevation.Outcome)
    {
      case ElevationOutcome.Relaunched:
        AnsiConsole.MarkupLine("[green]Restarting with elevated privileges...[/]");
        Environment.Exit(0); // Exit the original process
        return;
      case ElevationOutcome.Declined:
        AnsiConsole.MarkupLine("[red]Elevation was declined. VSS mode cannot be used without administrator privileges.[/]");
        Environment.Exit((int)VssErrorCode.ElevationRequired);
        return;
      case ElevationOutcome.Failed:
        AnsiConsole.MarkupLine($"[red]Failed to restart with elevation: {Markup.Escape(elevation.Error!.Message)}[/]");
        Environment.Exit((int)VssErrorCode.ElevationRequired);
        return;
    }

    var app = ConsoleApp.Create();
    app.Add<Program>();
    await app.RunAsync(args);
  }

  /// <summary>
  /// Verifies file integrity against a checksum manifest.
  /// </summary>
  /// <param name="checksumFile">Path to the manifest file.</param>
  /// <param name="root">The root directory for resolving file paths.</param>
  /// <param name="algorithm">Hashing algorithm to use (e.g., SHA256, MD5).</param>
  /// <param name="threads">Number of parallel threads to use.</param>
  /// <param name="tsvReport">Path to write a machine-readable TSV error report.</param>
  /// <param name="showTable">Force the diagnostic table to be shown even if there are no issues.</param>
  /// <param name="include">Semicolon-separated glob patterns for files to include.</param>
  /// <param name="exclude">Semicolon-separated glob patterns for files to exclude.</param>
  /// <param name="vss">Create VSS snapshot for accessing locked files (requires elevation).</param>
  [Command("verify")]
  public async Task<int> Verify(
      [Argument] string checksumFile,
      string? root = null,
      string? algorithm = null,
      int? threads = null,
      string? tsvReport = null,
      bool showTable = false,
      string? include = null,
      string? exclude = null,
      bool vss = false,
      CancellationToken cancellationToken = default
  )
  {
    try {
      var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(checksumFile) : algorithm;
      var options = new CliOptions(new FileInfo(checksumFile),
        !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm,
        !string.IsNullOrWhiteSpace(tsvReport) ? new FileInfo(tsvReport) : null,
        showTable,
        GlobUtils.NormalizeGlobs(include, false),
        GlobUtils.NormalizeGlobs(exclude, true),
        vss);
      return await RunVerification(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) { AnsiConsole.MarkupLine("[red]Interrupted by user[/]"); return ExitCodes.Interrupted; }
  }

  /// <summary>
  /// Creates a new checksum manifest from a directory.
  /// </summary>
  /// <param name="outputManifest">Path for the output manifest file.</param>
  /// <param name="root">-r, The root directory to scan for files. Defaults to the manifest's directory.</param>
  /// <param name="algorithm">-a, Hashing algorithm to use (e.g., SHA256, MD5). Inferred from extension if omitted.</param>
  /// <param name="threads">-t, Number of parallel threads to use. Defaults to processor count.</param>
  /// <param name="include">Semicolon-separated glob patterns for files to include in the manifest.</param>
  /// <param name="exclude">Semicolon-separated glob patterns for files to exclude from the manifest.</param>
  /// <param name="showTable">Force the diagnostic table to be shown even if there are no issues.</param>
  /// <param name="tsvReport">Path to write a machine-readable TSV report.</param>
  /// <param name="vss">Create VSS snapshot for accessing locked files (requires elevation).</param>
  [Command("create")]
  public async Task<int> Create(
      [Argument] string outputManifest,
      string? root = null,
      string algorithm = null,
      int? threads = null,
      string? include = null,
      string? exclude = null,
      bool showTable = false,
      string? tsvReport = null,
      bool vss = false,
      CancellationToken cancellationToken = default)
  {
    try {
      var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(outputManifest) : algorithm;
      var options = new CliOptions(new FileInfo(outputManifest),
        !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm,
        !string.IsNullOrWhiteSpace(tsvReport) ? new FileInfo(tsvReport) : null,
        showTable,
        GlobUtils.NormalizeGlobs(include, false),
        GlobUtils.NormalizeGlobs(exclude, true),
        vss);

      return await RunCreateManifest(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) {
      AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
      return ExitCodes.Interrupted;
    }
  }

  /// <summary>
  /// Scans a directory and adds new, unlisted files to an existing manifest.
  /// </summary>
  /// <param name="manifestPath">Path to the existing manifest file to update.</param>
  /// <param name="root">-r, The root directory to scan for new files. Defaults to the manifest's directory.</param>
  /// <param name="algorithm">-a, Hashing algorithm to use (e.g., SHA256, MD5). Inferred from extension if omitted.</param>
  /// <param name="threads">-t, Number of parallel threads to use. Defaults to processor count.</param>
  /// <param name="include">Semicolon-separated glob patterns for new files to include.</param>
  /// <param name="exclude">Semicolon-separated glob patterns for files to exclude from being added.</param>
  /// <param name="showTable">Force the diagnostic table to be shown even if there are no issues.</param>
  /// <param name="tsvReport">Path to write a machine-readable TSV report.</param>
  /// <param name="vss">Create VSS snapshot for accessing locked files (requires elevation).</param>
  [Command("add")]
  public async Task<int> Add(
      [Argument] string manifestPath,
      string? root = null,
      string? algorithm = null,
      int? threads = null,
      string? include = null,
      string? exclude = null,
      bool showTable = false,
      string? tsvReport = null,
      bool vss = false,
      CancellationToken cancellationToken = default)
  {
    try {
      var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(manifestPath) : algorithm;
      var options = new CliOptions(new FileInfo(manifestPath),
        !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm,
        !string.IsNullOrWhiteSpace(tsvReport) ? new FileInfo(tsvReport) : null,
        showTable,
        GlobUtils.NormalizeGlobs(include, false),
        GlobUtils.NormalizeGlobs(exclude, true),
        vss);

      return await RunAddToManifest(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) {
      AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
      return ExitCodes.Interrupted;
    }
  }

  public static async Task<int> RunVerification(CliOptions options, int threads, CancellationToken cancellationToken)
  {
    var run = new RunContext(options, threads, cancellationToken);
    IReadOnlyList<ManifestEntry> manifestEntries = [];
    int totalFiles = 0;
    long totalBytes = 0;

    async Task<RunPhase> Prepare(RunContext r)
    {
      await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("green"))
          .StartAsync($"Reading manifest: {r.Options.ChecksumFile.FullName}...", async ctx => {
            var reader = new ManifestReader(r.Options.ChecksumFile, r.Options.RootDirectory);
            var entries = await reader.ReadEntriesAsync(r.CancellationToken);
            manifestEntries = [.. entries.Where(e => e != null && GlobUtils.IsMatch(e.RelativePath!, r.Options.IncludeGlobs, r.Options.ExcludeGlobs))];
            totalFiles = manifestEntries.Count;

            // Efficiently enumerate files and tally sizes
            var rootDir = r.Options.RootDirectory ?? new DirectoryInfo(r.Options.ChecksumFile.DirectoryName!);
            var manifestRelPaths = new HashSet<string>(manifestEntries.Select(e => e.RelativePath!), StringComparer.OrdinalIgnoreCase);
            foreach (var fsi in rootDir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) {
              if (fsi is FileInfo fi) {
                var relPath = Path.GetRelativePath(rootDir.FullName, fi.FullName);
                ctx.Status = $"[green]Reading manifest: {r.Options.ChecksumFile.FullName}[/] [grey]({relPath})[/]...";
                if (manifestRelPaths.Contains(relPath)) {
                  totalBytes += fi.Length;
                }
              }
            }
            await Task.Delay(100, r.CancellationToken); // Ensure spinner is visible
          });
      return RunPhase.Process;
    }

    async Task<RunPhase> Process(RunContext r)
    {
      r.Stopwatch.Restart(); // time only the verification itself
      long filesCompleted = 0;

      await AnsiConsole.Progress()
          .AutoClear(true)
          .HideCompleted(true)
          .Columns([
              new TaskDescriptionColumn(),
              new ProgressBarColumn(),
              new PercentageColumn(),
              new RemainingTimeColumn(),
              new SpinnerColumn(),
          ])
          .StartAsync(async ctx => {
            var mainTask = ctx.AddTask($"[green]Verifying files ({totalBytes.Bytes().Humanize()})[/]", maxValue: totalFiles);
            var verificationService = new VerificationService();

            // Set VSS resolver if available
            if (r.VssResolver != null) {
              verificationService.VssResolver = r.VssResolver;
            }

            verificationService.FileStarted += (sender, e) => {
              int padLen = 50;
              var safeRelPath = Utilities.AbbreviateAndPadPathForDisplay(e.Entry.RelativePath, padLen);
              e.Bag = ctx.AddTask(safeRelPath, maxValue: e.FileSize > 0 ? e.FileSize : 1);
            };
            verificationService.FileProgress += (sender, e) => {
              if (e.Bag is ProgressTask fileTask) {
                fileTask.Value = e.BytesRead;
              }
            };
            verificationService.FileCompleted += (sender, e) => {
              mainTask.Value = Interlocked.Increment(ref filesCompleted);
              if (e.Bag is ProgressTask fileTask) {
                fileTask.Value = fileTask.MaxValue;
              }
            };
            verificationService.FileFoundNotInChecksumList += (path) => { };

            r.Summary = await verificationService.VerifyChecksumsAsync(r.Options, manifestEntries, r.Threads, r.CancellationToken);
            mainTask.StopTask();
          });

      r.Stopwatch.Stop();
      return RunPhase.Report;
    }

    async Task<RunPhase> Report(RunContext r)
    {
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine("[bold underline]Verification Complete[/]");
      await RunPipeline.RenderReportAsync(r);
      return RunPhase.Finished;
    }

    return await RunPipeline.RunAsync(run, new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = RunPipeline.VssSetupAsync,
      [RunPhase.Header] = RunPipeline.Header("Verification Info"),
      [RunPhase.Prepare] = Prepare,
      [RunPhase.Process] = Process,
      [RunPhase.Report] = Report,
    });
  }

  public static async Task<int> RunManifestOperation(
    ManifestOperationMode mode,
    CliOptions options,
    int threads,
    CancellationToken cancellationToken)
  {
    var run = new RunContext(options, threads, cancellationToken);
    var creating = mode == ManifestOperationMode.Create;
    IReadOnlyCollection<string> files = [];
    IReadOnlyCollection<string> newFiles = [];
    long totalBytes = 0;

    async Task<RunPhase> Prepare(RunContext r)
    {
      if (string.IsNullOrEmpty(r.RootPath)) {
        AnsiConsole.MarkupLine("[red]Error: Root directory must be specified and exist.[/]");
        return RunPipeline.Exit(r, ExitCodes.Errors);
      }

      if (creating) {
        (files, totalBytes) = await FilesToAddAsync(r.Options, r.RootPath, r.CancellationToken);
        if (files.Count == 0) {
          // Write an empty manifest using ManifestWriter
          using var manifestWriter = new ManifestWriter(r.Options.ChecksumFile);
          await manifestWriter.WriteAllEntriesAsync([]);
          AnsiConsole.MarkupLine("[yellow]No files found in the specified root directory.[/]");
          return RunPipeline.Exit(r, ExitCodes.Warnings);
        }
      } else {
        (newFiles, totalBytes) = await NewFilesAsync(r.Options, r.RootPath, r.CancellationToken);
        if (newFiles.Count == 0) {
          AnsiConsole.MarkupLine("[yellow]No new files to add to manifest.[/]");
          return RunPipeline.Exit(r, ExitCodes.Warnings);
        }
      }
      await Task.Delay(100, r.CancellationToken);
      return RunPhase.Process;
    }

    async Task<RunPhase> Process(RunContext r)
    {
      long totalBytesRead = 0;
      long filesProcessed = 0;

      await AnsiConsole.Progress()
          .AutoClear(true)
          .HideCompleted(true)
          .Columns(
              new TaskDescriptionColumn(),
              new ProgressBarColumn(),
              new PercentageColumn(),
              new RemainingTimeColumn(),
              new SpinnerColumn()
          )
          .StartAsync(async ctx => {
            var mainTask = ctx.AddTask($"[green]{(creating ? "Creating manifest" : "Adding to manifest")} ({totalBytes.Bytes().Humanize()})[/]", maxValue: totalBytes);
            var manifestService = new ManifestCreationService();

            // Set VSS resolver if available
            if (r.VssResolver != null) {
              manifestService.VssResolver = r.VssResolver;
            }
            manifestService.FileStarted += (sender, e) => {
              int padLen = 50;
              var safeRelPath = Utilities.AbbreviateAndPadPathForDisplay(e.RelativePath, padLen);
              var fileTask = ctx.AddTask(safeRelPath, maxValue: e.FileSize > 0 ? e.FileSize : 1);
              e.Bag = fileTask;
              fileTask.Value = 0;
            };
            manifestService.FileProgress += (sender, e) => {
              if (e.Bag is ProgressTask fileTask) {
                fileTask.Value = e.BytesRead;
              }
              mainTask.Value = Interlocked.Add(ref totalBytesRead, e.BytesJustRead);
            };
            manifestService.FileCompleted += (sender, e) => {
              Interlocked.Increment(ref filesProcessed);
              if (e.Bag is ProgressTask fileTask) {
                fileTask.Value = fileTask.MaxValue;
              }
            };
            if (creating)
              r.Summary = await manifestService.CreateManifestAsync(r.Options.ChecksumFile,
                new DirectoryInfo(r.RootPath), r.Options.Algorithm,
                files, r.Threads, r.CancellationToken);
            else
              r.Summary = await manifestService.AddToManifestAsync(r.Options.ChecksumFile,
                new DirectoryInfo(r.RootPath), r.Options.Algorithm,
                newFiles, r.Threads, r.CancellationToken);
            mainTask.StopTask();
          });

      r.Stopwatch.Stop();
      return RunPhase.Report;
    }

    async Task<RunPhase> Report(RunContext r)
    {
      AnsiConsole.MarkupLine($"[green]Manifest {(creating ? "created" : "updated")}: [/] {r.Options.ChecksumFile.FullName}");
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine($"[bold underline]{(creating ? "Creation" : "Add")} Complete[/]");
      await RunPipeline.RenderReportAsync(r);
      return RunPhase.Finished;
    }

    return await RunPipeline.RunAsync(run, new Dictionary<RunPhase, PhaseHandler> {
      [RunPhase.VssSetup] = RunPipeline.VssSetupAsync,
      [RunPhase.Header] = RunPipeline.Header(creating ? "Manifest Creation Info" : "Manifest Add Info"),
      [RunPhase.Prepare] = Prepare,
      [RunPhase.Process] = Process,
      [RunPhase.Report] = Report,
    });
  }

  private static async Task<(IReadOnlyCollection<string> newFiles, long totalBytes)> NewFilesAsync(
    CliOptions options, string rootPath,
    CancellationToken cancellationToken = default)
  {
    List<string> newFiles = [];
    var totalBytes = 0L;
    IReadOnlyList<ManifestEntry> manifestEntries = [];
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Reading manifest: {options.ChecksumFile.FullName}...", async ctx => {
          var reader = new ManifestReader(options.ChecksumFile, options.RootDirectory);
          manifestEntries = await reader.ReadEntriesAsync(cancellationToken);
          await Task.Delay(100, cancellationToken);
        });
    var listedFiles = new HashSet<string>(manifestEntries.Select(e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Scanning directory: {rootPath}...", async ctx => {
          var dirInfo = new DirectoryInfo(rootPath);
          foreach (var fsi in dirInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) {
            if (fsi is FileInfo fi) {
              var relPath = Path.GetRelativePath(rootPath, fi.FullName);
              ctx.Status = $"[green]Scanning directory: {rootPath}[/] [grey]({relPath})[/]...";
              if (GlobUtils.IsMatch(relPath, options.IncludeGlobs, options.ExcludeGlobs) && !listedFiles.Contains(relPath)) {
                newFiles.Add(relPath);
                totalBytes += fi.Length;
              }
            }
          }
        });
    return (newFiles, totalBytes);
  }

  private static async Task<(IReadOnlyCollection<string> files, long totalBytes)> FilesToAddAsync(CliOptions options, string rootPath, CancellationToken cancellationToken)
  {
    List<string> files = [];
    var totalBytes = 0L;

    await AnsiConsole.Status()
              .Spinner(Spinner.Known.Dots)
              .SpinnerStyle(Style.Parse("green"))
              .StartAsync($"Calculating total size: {rootPath}...", async ctx => {
                var dirInfo = new DirectoryInfo(rootPath);
                foreach (var fsi in dirInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) {
                  if (fsi is FileInfo fi) {
                    var relPath = Path.GetRelativePath(rootPath, fi.FullName);
                    ctx.Status = $"[green]Calculating total size: {rootPath}[/] [grey]({relPath})[/]...";

                    if (GlobUtils.IsMatch(relPath, options.IncludeGlobs, options.ExcludeGlobs)) {
                      files.Add(relPath);
                      totalBytes += fi.Length;
                    }
                  }
                }
              });
    return (files, totalBytes);
  }

  public static async Task<int> RunCreateManifest(CliOptions options, int threads, CancellationToken cancellationToken)
  {
    return await RunManifestOperation(ManifestOperationMode.Create, options, threads, cancellationToken);
  }

  public static async Task<int> RunAddToManifest(CliOptions options, int threads, CancellationToken cancellationToken)
  {
    return await RunManifestOperation(ManifestOperationMode.Add, options, threads, cancellationToken);
  }

  public static string InferAlgorithmFromExtension(string manifestPath)
  {
    var ext = Path.GetExtension(manifestPath).ToLowerInvariant();
    return ext switch {
      ".sha256" => "SHA256",
      ".md5" => "MD5",
      ".sha1" => "SHA1",
      _ => "SHA256" // Ensure a default value is always returned
    };
  }
}

public enum ManifestOperationMode
{
  Create,
  Add
}
