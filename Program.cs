using ConsoleAppFramework;

using Humanizer;

using Spectre.Console;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

public class Program
{
  public static async Task Main(string[] args)
  {
    var app = ConsoleApp.Create();
    app.Add("verify", async Task<int> ([Argument] string checksumFile,
      string? root = null, string algorithm = "SHA256",
      int? threads = null,
      CancellationToken cancellationToken = default) => {
        try {
          var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(checksumFile) : algorithm;
          var options = new CliOptions(new FileInfo(checksumFile),
            !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm);
          var exitCode = await RunVerification(options, threads ?? Environment.ProcessorCount, cancellationToken);
          return exitCode;
        } catch (OperationCanceledException) {
          AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
          return -2;
        }
      });
    app.Add("create", async Task<int> ([Argument] string outputManifest,
      string? root = null, string algorithm = "SHA256",
      int? threads = null,
      CancellationToken cancellationToken = default) => {
        try {
          var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(outputManifest) : algorithm;
          var exitCode = await RunCreateManifest(new FileInfo(outputManifest),
            !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null,
            usedAlgorithm, threads ?? Environment.ProcessorCount, cancellationToken);
          return exitCode;
        } catch (OperationCanceledException) {
          AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
          return -2;
        }
      });
    app.Add("add", async Task<int> ([Argument] string manifestPath,
      string? root = null, string algorithm = "SHA256",
      int? threads = null,
      CancellationToken cancellationToken = default) => {
        try {
          var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(manifestPath) : algorithm;
          var exitCode = await RunAddToManifest(new FileInfo(manifestPath),
            !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null,
            usedAlgorithm, threads ?? Environment.ProcessorCount, cancellationToken);
          return exitCode;
        } catch (OperationCanceledException) {
          AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
          return -2;
        }
      });
    await app.RunAsync(args);
  }

  public static async Task<int> RunVerification(CliOptions options, int threads, CancellationToken cancellationToken)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    var startTime = DateTime.Now;
    var headerPanel = PathUtils.BuildHeaderPanel(
        "Verification Info",
        version,
        startTime,
        options.ChecksumFile.Name,
        options.Algorithm,
        options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!
    );
    AnsiConsole.Write(headerPanel);

    // Pre-scan spinner for manifest parsing
    IReadOnlyList<ManifestEntry> manifestEntries;
    int totalFiles = 0;
    long totalBytes = 0;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Reading manifest: {options.ChecksumFile.FullName}...", async ctx => {
          var reader = new ManifestReader(options.ChecksumFile, options.RootDirectory);
          manifestEntries = await reader.ReadEntriesAsync(cancellationToken);
          totalFiles = manifestEntries.Count;
          totalBytes = await reader.GetTotalBytesAsync(cancellationToken);
          await Task.Delay(100, cancellationToken); // Ensure spinner is visible
        });
    // If you need lines for diagnostics, you can get them from manifestEntries
    var problematicResults = new ConcurrentBag<VerificationResult>();
    var unlistedFiles = new ConcurrentBag<string>();
    var stopwatch = Stopwatch.StartNew();
    FinalSummary summary = new(0, 0, 0, 0, 0);
    int filesProcessed = 0;

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
            //            fileTasks[e.Entry.RelativePath] = e.Bag as ProgressTask;
          };

          verificationService.FileProgress += (sender, e) => {
            if (e.Bag is ProgressTask fileTask) {
              fileTask.Value = e.BytesRead;
            }
          };

          verificationService.FileCompleted += (sender, e) => {
            filesProcessed++;
            mainTask.Value = filesProcessed;
            if (e.Result.Status != ResultStatus.Success) {
              problematicResults.Add(e.Result);
            }
            // Mark file task as complete
            if (e.Bag is ProgressTask fileTask) {
              fileTask.Value = fileTask.MaxValue;
            }
          };

          verificationService.FileFoundNotInChecksumList += (path) => {
            unlistedFiles.Add(path);
          };

          summary = await verificationService.VerifyChecksumsAsync(options, cancellationToken, threads, ctx);
          mainTask.StopTask();
        });

    stopwatch.Stop();
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Verification Complete[/]");
    var summaryTable = new Table()
      .NoBorder()
      .HideHeaders()
      .AddColumn(new TableColumn("Label").LeftAligned())
      .AddColumn(new TableColumn("Value").LeftAligned());

    summaryTable.AddRow("[green]Success[/]", $"{summary.SuccessCount:N0}");
    summaryTable.AddRow("[yellow]Warnings[/]", $"{summary.WarningCount:N0}");
    summaryTable.AddRow("[red]Errors[/]", $"{summary.ErrorCount:N0}");
    summaryTable.AddRow("[cyan]Total Time[/]", stopwatch.Elapsed.Humanize(2));
    var throughput = summary.TotalBytesRead.Bytes().Per(stopwatch.Elapsed).Humanize();
    summaryTable.AddRow("[cyan]Throughput[/]", throughput);
    AnsiConsole.Write(summaryTable);

    if (!problematicResults.IsEmpty || !unlistedFiles.IsEmpty) {
      AnsiConsole.WriteLine();
      var table = new Table().Expand();
      table.Border = TableBorder.Rounded;
      table.Title = new TableTitle("[bold yellow]Diagnostic Report[/]");
      table.AddColumn("Status");
      table.AddColumn("File");
      table.AddColumn("Details");
      table.AddColumn("Expected Hash");
      table.AddColumn("Actual Hash");

      var allProblems = problematicResults.OrderBy(r => r.Status).ThenBy(r => r.Entry.RelativePath);
      var orderedUnlistedFiles = unlistedFiles.OrderBy(f => f);

      foreach (var result in allProblems) {
        var statusMarkup = result.Status switch {
          ResultStatus.Warning => "[yellow]Warning[/]",
          ResultStatus.Error => "[red]Error[/]",
          _ => "[grey]Info[/]"
        };
        table.AddRow(
            statusMarkup,
            result.FullPath ?? result.Entry.RelativePath,
            result.Details ?? string.Empty,
            result.Entry.ExpectedHash,
            result.ActualHash ?? "N/A"
        );
      }

      foreach (var file in orderedUnlistedFiles) {
        table.AddRow(
            "Warning",
            file,
            "File exists but not in checksum list.",
            "N/A",
            "N/A"
        );
      }

      AnsiConsole.Write(table);

      var errorReport = new StringBuilder();
      errorReport.AppendLine("#Status\tFile\tDetails\tExpectedHash\tActualHash");

      foreach (var result in allProblems) {
        errorReport.AppendLine(string.Join("\t",
            result.Status.ToString().ToUpperInvariant(),
            result.FullPath ?? result.Entry.RelativePath,
            result.Details ?? "",
            result.Entry.ExpectedHash,
            result.ActualHash ?? ""
        ));
      }

      foreach (var file in orderedUnlistedFiles) {
        errorReport.AppendLine(string.Join("\t",
            "WARNING",
            file,
            "File exists but not in checksum list.",
            "",
            ""
        ));
      }

      await global::System.Console.Error.WriteAsync(errorReport.ToString());
    }

    if (summary.ErrorCount > 0) return -1;
    if (summary.WarningCount > 0) return 1;
    return 0;
  }

  public static async Task<int> RunManifestOperation(
    ManifestOperationMode mode,
    FileInfo manifestFile,
    DirectoryInfo? root,
    string algorithm,
    int threads,
    CancellationToken cancellationToken)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    var startTime = DateTime.Now;
    var stopwatch = Stopwatch.StartNew();
    var rootPath = root?.FullName ?? manifestFile.DirectoryName!;

    var headerPanel = PathUtils.BuildHeaderPanel(
        mode == ManifestOperationMode.Create ? "Manifest Creation Info" : "Manifest Add Info",
        version,
        startTime,
        manifestFile.Name,
        algorithm,
        rootPath
    );
    AnsiConsole.Write(headerPanel);

    if (string.IsNullOrEmpty(rootPath)) {
      AnsiConsole.MarkupLine("[red]Error: Root directory must be specified and exist.[/]");
      return -1;
    }

    string[] files = [];
    List<string> filesToAdd = [];
    long totalBytes = 0;
    if (mode == ManifestOperationMode.Create) {
      await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("green"))
          .StartAsync($"Calculating total size: {rootPath}...", async ctx => {
            files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
            totalBytes = files.Select(f => new FileInfo(f).Length).Sum();
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
          .StartAsync($"Reading manifest: {manifestFile.FullName}...", async ctx => {
            var reader = new ManifestReader(manifestFile, root);
            manifestEntries = await reader.ReadEntriesAsync(cancellationToken);
            await Task.Delay(100, cancellationToken);
          });
      var listedFiles = new HashSet<string>(manifestEntries.Select(e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
      await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("green"))
          .StartAsync($"Scanning directory: {rootPath}...", async ctx => {
            files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
            await Task.Delay(100, cancellationToken);
          });
      filesToAdd = files
        .Select(f => Path.GetRelativePath(rootPath, f))
        .Where(rel => !listedFiles.Contains(rel))
        .ToList();
      if (filesToAdd.Count == 0) {
        AnsiConsole.MarkupLine("[yellow]No new files to add to manifest.[/]");
        return 0;
      }
      totalBytes = filesToAdd.Select(f => new FileInfo(Path.Combine(rootPath, f)).Length).Sum();
    }

    long totalBytesRead = 0;
    int filesProcessed = 0;
    int exitCode = 0;
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
          var mainTask = ctx.AddTask($"[green]{(mode == ManifestOperationMode.Create ? "Creating manifest" : "Adding to manifest")} ({totalBytes.Bytes().Humanize()})[/]", maxValue: totalBytes);
          var manifestService = new ManifestCreationService();
          manifestService.FileStarted += (sender, e) => { };
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
            exitCode = await manifestService.CreateManifestAsync(manifestFile,
              new DirectoryInfo(rootPath), algorithm,
              threads, cancellationToken, ctx);
          else
            exitCode = await manifestService.AddToManifestAsync(manifestFile,
              new DirectoryInfo(rootPath), algorithm,
              filesToAdd, threads, cancellationToken, ctx);
          mainTask.StopTask();
        });
    stopwatch.Stop();
    AnsiConsole.MarkupLine($"[green]Manifest {(mode == ManifestOperationMode.Create ? "created" : "updated")}:[/] {manifestFile.FullName}");
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
      summaryTable.AddRow("[green]Files Added[/]", $"{filesToAdd.Count:N0}");
    }
    summaryTable.AddRow("[cyan]Total Bytes[/]", $"{totalBytes.Bytes().Humanize()}");
    summaryTable.AddRow("[cyan]Total Time[/]", stopwatch.Elapsed.Humanize(2));
    var throughput = totalBytes.Bytes().Per(stopwatch.Elapsed).Humanize();
    summaryTable.AddRow("[cyan]Throughput[/]", throughput);
    AnsiConsole.Write(summaryTable);
    return exitCode;
  }

  public static async Task<int> RunCreateManifest(FileInfo outputManifest,
    DirectoryInfo? root, string algorithm, int threads, CancellationToken cancellationToken)
  {
    return await RunManifestOperation(ManifestOperationMode.Create, outputManifest, root, algorithm, threads, cancellationToken);
  }

  public static async Task<int> RunAddToManifest(FileInfo manifestFile,
    DirectoryInfo? root, string algorithm, int threads, CancellationToken cancellationToken)
  {
    return await RunManifestOperation(ManifestOperationMode.Add, manifestFile, root, algorithm, threads, cancellationToken);
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
