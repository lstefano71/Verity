using ConsoleAppFramework;

using Humanizer;

using Spectre.Console;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
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
          var options = new CliOptions(new FileInfo(checksumFile), !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm);
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
          var exitCode = await RunCreateManifest(new FileInfo(outputManifest), new DirectoryInfo(root), usedAlgorithm, threads ?? Environment.ProcessorCount, cancellationToken);
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
        options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName
    );
    AnsiConsole.Write(headerPanel);

    // Pre-scan spinner for manifest parsing
    string[] manifestLines = null;
    int totalFiles = 0;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Reading manifest: {options.ChecksumFile.FullName}...", async ctx => {
          manifestLines = await File.ReadAllLinesAsync(options.ChecksumFile.FullName, cancellationToken);
          totalFiles = manifestLines.Count(line => !string.IsNullOrWhiteSpace(line) && line.Contains('\t'));
          await Task.Delay(100, cancellationToken); // Ensure spinner is visible
        });

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
          var mainTask = ctx.AddTask("[green]Verifying files[/]", maxValue: totalFiles);
          var fileTasks = new ConcurrentDictionary<string, ProgressTask>();
          var verificationService = new VerificationService();

          verificationService.FileStarted += (sender, e) => {
            int padLen = 50;
            var safeRelPath = PathUtils.AbbreviateAndPadPathForDisplay(e.Entry.RelativePath, padLen);
            var fileTask = ctx.AddTask(safeRelPath, maxValue: e.FileSize > 0 ? e.FileSize : 1);
            e.Bag["progressTask"] = fileTask;
            fileTasks[e.Entry.RelativePath] = fileTask;
          };

          verificationService.FileProgress += (sender, e) => {
            if (e.Bag.TryGetValue("progressTask", out var taskObj) && taskObj is ProgressTask fileTask) {
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
            if (e.Bag.TryGetValue("progressTask", out var taskObj) && taskObj is ProgressTask fileTask) {
              fileTask.Value = fileTask.MaxValue;
            }
          };

          verificationService.FileFoundNotInChecksumList += (path) => {
            unlistedFiles.Add(path);
          };

          summary = await verificationService.VerifyChecksumsAsync(options, cancellationToken, threads);
          mainTask.StopTask();
        });

    stopwatch.Stop();
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Verification Complete[/]");
    AnsiConsole.MarkupLine($"[green]  Success:[/] {summary.SuccessCount:N0}");
    AnsiConsole.MarkupLine($"[yellow]Warnings:[/] {summary.WarningCount:N0}");
    AnsiConsole.MarkupLine($"[red]    Errors:[/] {summary.ErrorCount:N0}");
    AnsiConsole.MarkupLine($"[cyan]Total Time:[/] {stopwatch.Elapsed.Humanize(2)}");
    var throughput = summary.TotalBytesRead.Bytes().Per(stopwatch.Elapsed).Humanize();
    AnsiConsole.MarkupLine($"[cyan] Throughput:[/] {throughput}");

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

      await Console.Error.WriteAsync(errorReport.ToString());
    }

    if (summary.ErrorCount > 0) return -1;
    if (summary.WarningCount > 0) return 1;
    return 0;
  }

  public static async Task<int> RunCreateManifest(FileInfo outputManifest, DirectoryInfo root, string algorithm, int parallelism, CancellationToken cancellationToken)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    var startTime = DateTime.Now;
    var stopwatch = Stopwatch.StartNew();
    var headerPanel = PathUtils.BuildHeaderPanel(
        "Manifest Creation Info",
        version,
        startTime,
        outputManifest.Name,
        algorithm,
        root.FullName
    );
    AnsiConsole.Write(headerPanel);

    if (root == null || !root.Exists) {
      AnsiConsole.MarkupLine("[red]Error: Root directory must be specified and exist.[/]");
      return -1;
    }

    // Show spinner while calculating total size
    string[] files = null;
    long totalBytes = 0;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync($"Calculating total size: {root.FullName}...", async ctx => {
          files = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories);
          totalBytes = files.Select(f => new FileInfo(f).Length).Sum();
          await Task.Delay(100, cancellationToken); // Just to ensure spinner is visible for a moment
        });

    if (files.Length == 0) {
      AnsiConsole.MarkupLine("[yellow]No files found in the specified root directory.[/]");
      return 1;
    }
    using var manifestWriter = new StreamWriter(outputManifest.FullName, false, Encoding.UTF8);

    long totalBytesRead = 0;
    var progressLock = new object();

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
          // Create a progress task for each file
          var mainTask = ctx.AddTask("[green]Creating manifest[/]", maxValue: totalBytes);
          await Task.WhenAll(
            [.. Partitioner.Create(files).GetPartitions(parallelism)
              .Select(partition => Task.Run(async () => {
                using (partition) {
                  while (partition.MoveNext()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = partition.Current;
                    var relPath = Path.GetRelativePath(root.FullName, file);
                    var fileSize = new FileInfo(file).Length;
                    int padLen = 50;
                    var safeRelPath = PathUtils.AbbreviateAndPadPathForDisplay(relPath, padLen);
                    var task = ctx.AddTask(safeRelPath, maxValue: fileSize);

                    var fileProgress = new Progress<long>(v => task.Value = v);
                    string hash = await ComputeFileHashAsync(file, algorithm, cancellationToken, fileProgress);
                    lock (manifestWriter) {
                      manifestWriter.WriteLine($"{hash}\t{relPath}");
                    }
                    task.Value = fileSize;
                    lock (progressLock) {
                      totalBytesRead += fileSize;
                      mainTask.Value = totalBytesRead;
                    }
                  }
                }
              }, cancellationToken)
            )]
          );
        });
    stopwatch.Stop();
    AnsiConsole.MarkupLine($"[green]Manifest created:[/] {outputManifest.FullName}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Creation Complete[/]");
    AnsiConsole.MarkupLine($"[green]  Files:[/] {files.Length:N0}");
    AnsiConsole.MarkupLine($"[cyan]Total Bytes:[/] {totalBytesRead.Bytes().Humanize()}");
    AnsiConsole.MarkupLine($"[cyan]Total Time:[/] {stopwatch.Elapsed.Humanize(2)}");
    var throughput = totalBytesRead.Bytes().Per(stopwatch.Elapsed).Humanize();
    AnsiConsole.MarkupLine($"[cyan] Throughput:[/] {throughput}");
    return 0;
  }

  public static async Task<string> ComputeFileHashAsync(string filePath, string algorithm, CancellationToken cancellationToken, IProgress<long>? progress = null)
  {
    var fileSize = new FileInfo(filePath).Length;
    int bufferSize = FileIOUtils.GetOptimalBufferSize(fileSize);
    // Normalize algorithm name to uppercase for compatibility
    var normalizedAlgorithm = algorithm?.Trim().ToUpperInvariant();
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
    using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(normalizedAlgorithm));
    byte[] buffer = new byte[bufferSize];
    long totalRead = 0;
    int bytesRead;
    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0) {
      hasher.AppendData(buffer, 0, bytesRead);
      totalRead += bytesRead;
      progress?.Report(totalRead);
    }
    return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
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
