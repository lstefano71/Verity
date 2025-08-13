using ConsoleAppFramework;

using Humanizer;

using Spectre.Console;

using System.Diagnostics;
using System.Reflection;

public class Program
{

  // The application entry point is simplified to just run our class.
  public static async Task Main(string[] args)
  {
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
  [Command("verify")]
  public async Task<int> Verify(
      [Argument] string checksumFile,
      string? root = null,
      string algorithm = "SHA256",
      int? threads = null,
      string? tsvReport = null,
      bool showTable = false,
      string? include = null,
      string? exclude = null,
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
        GlobUtils.NormalizeGlobs(exclude, true));
      return await RunVerification(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) { AnsiConsole.MarkupLine("[red]Interrupted by user[/]"); return -2; }
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
  [Command("create")]
  public async Task<int> Create(
      [Argument] string outputManifest,
      string? root = null,
      string algorithm = "SHA256",
      int? threads = null,
      string? include = null,
      string? exclude = null,
      CancellationToken cancellationToken = default)
  {
    try {
      var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(outputManifest) : algorithm;
      var options = new CliOptions(new FileInfo(outputManifest),
        !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm,
        null, // tsvReport is not applicable for create
        false, // showTable is not applicable for create
        GlobUtils.NormalizeGlobs(include, false),
        GlobUtils.NormalizeGlobs(exclude, true));

      return await RunCreateManifest(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) {
      AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
      return -2;
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
  [Command("add")]
  public async Task<int> Add(
      [Argument] string manifestPath,
      string? root = null,
      string algorithm = "SHA256",
      int? threads = null,
      string? include = null,
      string? exclude = null,
      CancellationToken cancellationToken = default)
  {
    try {
      var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(manifestPath) : algorithm;
      var options = new CliOptions(new FileInfo(manifestPath),
        !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm,
        null, // tsvReport is not applicable for add
        false, // showTable is not applicable for add
        GlobUtils.NormalizeGlobs(include, false),
        GlobUtils.NormalizeGlobs(exclude, true));

      return await RunAddToManifest(options, threads ?? Environment.ProcessorCount, cancellationToken);
    } catch (OperationCanceledException) {
      AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
      return -2;
    }
  }


  public static async Task<int> RunVerification(CliOptions options, int threads, CancellationToken cancellationToken)
  {
    var startTime = DateTime.Now;
    var headerPanel = Utilities.BuildHeaderPanel(
        "Verification Info",
        startTime,
        options.ChecksumFile.Name,
        options.Algorithm,
        options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!,
        options.IncludeGlobs,
        options.ExcludeGlobs
    );
    AnsiConsole.Write(headerPanel);

    IReadOnlyList<ManifestEntry> manifestEntries = [];
    int totalFiles = 0;
    long totalBytes = 0;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Reading manifest: {options.ChecksumFile.FullName}...", async ctx => {
          var reader = new ManifestReader(options.ChecksumFile, options.RootDirectory);
          var entries = await reader.ReadEntriesAsync(cancellationToken);
          manifestEntries = [.. entries.Where(e => e != null && GlobUtils.IsMatch(e.RelativePath!, options.IncludeGlobs, options.ExcludeGlobs))];
          totalFiles = manifestEntries.Count;
          totalBytes = manifestEntries.Select(e => {
            var fullPath = options.RootDirectory != null ? Path.Combine(options.RootDirectory.FullName, e.RelativePath!) : Path.Combine(options.ChecksumFile.DirectoryName!, e.RelativePath!);
            return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
          }).Sum();
          await Task.Delay(100, cancellationToken); // Ensure spinner is visible
        });

    var stopwatch = Stopwatch.StartNew();
    FinalSummary summary = new(0, 0, 0, 0, 0, [], []);

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
            mainTask.Value++;
            if (e.Bag is ProgressTask fileTask) {
              fileTask.Value = fileTask.MaxValue;
            }
          };
          verificationService.FileFoundNotInChecksumList += (path) => { };

          summary = await verificationService.VerifyChecksumsAsync(options, manifestEntries, threads, cancellationToken);
          mainTask.StopTask();
        });

    stopwatch.Stop();
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Verification Complete[/]");

    var presenter = new ResultsPresenter();
    ResultsPresenter.RenderSummaryTable(summary, stopwatch.Elapsed);
    if (options.ShowTable)
      ResultsPresenter.RenderDiagnosticsTable(summary);
    if (options.TsvReportFile != null)
      await ResultsPresenter.WriteErrorReportAsync(summary, options.TsvReportFile);

    if (summary.ErrorCount > 0) return -1;
    if (summary.WarningCount > 0) return 1;
    return 0;
  }

  public static async Task<int> RunManifestOperation(
    ManifestOperationMode mode,
    CliOptions options,
    int threads,
    CancellationToken cancellationToken)
  {
    var startTime = DateTime.Now;
    var stopwatch = Stopwatch.StartNew();
    var rootPath = options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!;

    var headerPanel = Utilities.BuildHeaderPanel(
        mode == ManifestOperationMode.Create ? "Manifest Creation Info" : "Manifest Add Info",
        startTime,
        options.ChecksumFile.Name,
        options.Algorithm,
        rootPath,
        options.IncludeGlobs,
        options.ExcludeGlobs
    );
    AnsiConsole.Write(headerPanel);

    if (string.IsNullOrEmpty(rootPath)) {
      AnsiConsole.MarkupLine("[red]Error: Root directory must be specified and exist.[/]");
      return -1;
    }

    string[] files = [];
    List<string> newFiles = [];
    long totalBytes = 0;
    if (mode == ManifestOperationMode.Create) {
      await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("green"))
          .StartAsync($"Calculating total size: {rootPath}...", async ctx => {
            var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
            var relFiles = GlobUtils.FilterFiles(allFiles, rootPath, options.IncludeGlobs, options.ExcludeGlobs);
            files = [.. relFiles];
            totalBytes = files.Select(f => new FileInfo(Path.Combine(rootPath, f)).Length).Sum();
            await Task.Delay(100, cancellationToken);
          });
      if (files is null or []) {
        AnsiConsole.MarkupLine("[yellow]No files found in the specified root directory.[/]");
        return 1;
      }
    } else {
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
            var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
            var filteredFiles = GlobUtils.FilterFiles(allFiles, rootPath, options.IncludeGlobs, options.ExcludeGlobs);
            newFiles = [.. filteredFiles
              .Where(rel => !listedFiles.Contains(rel))];
            if (newFiles.Count == 0) {
              AnsiConsole.MarkupLine("[yellow]No new files to add to manifest.[/]");
            }
            totalBytes = newFiles.Select(f => new FileInfo(Path.Combine(rootPath, f)).Length).Sum();
            await Task.Delay(100, cancellationToken);
          });
    }

    long totalBytesRead = 0;
    int filesProcessed = 0;
    int exitCode = 0;
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
          var mainTask = ctx.AddTask($"[green]{(mode == ManifestOperationMode.Create ? "Creating manifest" : "Adding to manifest")} ({totalBytes.Bytes().Humanize()})[/]", maxValue: totalBytes);
          var manifestService = new ManifestCreationService();
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
            Interlocked.Add(ref totalBytesRead, e.BytesJustRead);
            mainTask.Value = totalBytesRead;
          };
          manifestService.FileCompleted += (sender, e) => {
            filesProcessed++;
            if (e.Bag is ProgressTask fileTask) {
              fileTask.Value = fileTask.MaxValue;
            }
          };
          if (mode == ManifestOperationMode.Create)
            exitCode = await manifestService.CreateManifestAsync(options.ChecksumFile,
              new DirectoryInfo(rootPath), options.Algorithm,
              files, threads, cancellationToken);
          else
            exitCode = await manifestService.AddToManifestAsync(options.ChecksumFile,
              new DirectoryInfo(rootPath), options.Algorithm,
              newFiles, threads, cancellationToken);
          mainTask.StopTask();
        });
    stopwatch.Stop();
    AnsiConsole.MarkupLine($"[green]Manifest {(mode == ManifestOperationMode.Create ? "created" : "updated")}:[/] {options.ChecksumFile.FullName}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[bold underline]{(mode == ManifestOperationMode.Create ? "Creation" : "Add")} Complete[/]");
    var summaryTable = new Table()
      .NoBorder()
      .HideHeaders()
      .AddColumn(new TableColumn("Label").LeftAligned())
      .AddColumn(new TableColumn("Value").LeftAligned());
    if (mode == ManifestOperationMode.Create) {
      summaryTable.AddRow("[green]Files[/]", $"{files.Length:N0}");
    } else {
      summaryTable.AddRow("[green]Files Added[/]", $"{newFiles.Count:N0}");
    }
    summaryTable.AddRow("[cyan]Total Bytes[/]", $"{totalBytes.Bytes().Humanize()}");
    summaryTable.AddRow("[cyan]Total Time[/]", stopwatch.Elapsed.Humanize(2));
    var throughput = totalBytes.Bytes().Per(stopwatch.Elapsed).Humanize();
    summaryTable.AddRow("[cyan]Throughput[/]", throughput);
    AnsiConsole.Write(summaryTable);
    return exitCode;
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
