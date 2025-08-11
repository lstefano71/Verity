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
      CancellationToken cancellationToken = default) => {
        try {
          var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(checksumFile) : algorithm;
          var options = new CliOptions(new FileInfo(checksumFile), !string.IsNullOrWhiteSpace(root) ? new DirectoryInfo(root) : null, usedAlgorithm);
          var exitCode = await RunVerification(options, cancellationToken);
          return exitCode;
        } catch (OperationCanceledException) {
          AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
          return -2;
        }
      });
    app.Add("create", async Task<int> ([Argument] string outputManifest,
      string? root = null, string algorithm = "SHA256",
      CancellationToken cancellationToken = default) => {
        try {
          var usedAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? InferAlgorithmFromExtension(outputManifest) : algorithm;
          var exitCode = await RunCreateManifest(new FileInfo(outputManifest), new DirectoryInfo(root), usedAlgorithm, cancellationToken);
          return exitCode;
        } catch (OperationCanceledException) {
          AnsiConsole.MarkupLine("[red]Interrupted by user[/]");
          return -2;
        }
      });
    await app.RunAsync(args);
  }

  public static async Task<int> RunVerification(CliOptions options, CancellationToken cancellationToken)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    AnsiConsole.MarkupLine($"[bold cyan]Verity v{version}[/] - Checksum Verifier");

    var problematicResults = new ConcurrentBag<VerificationResult>();
    var unlistedFiles = new ConcurrentBag<string>();
    var stopwatch = Stopwatch.StartNew();

    FinalSummary summary = new(0, 0, 0, 0, 0);

    await AnsiConsole.Progress()
        .Columns([
            new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
        ])
        .StartAsync(async ctx => {
          var progressTask = ctx.AddTask("[green]Verifying files[/]");

          Action<long, long> onProgress = (processed, total) => {
            progressTask.MaxValue = total;
            progressTask.Value = processed;
          };

          Action<VerificationResult> onResult = (result) => {
            if (result.Status != ResultStatus.Success) {
              problematicResults.Add(result);
            }
          };

          Action<string> onFileFound = (path) => {
            unlistedFiles.Add(path);
          };

          summary = await VerificationService.VerifyChecksumsAsync(options, onProgress, onResult, onFileFound, cancellationToken);
          progressTask.StopTask();
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

  public static async Task<int> RunCreateManifest(FileInfo outputManifest, DirectoryInfo root, string algorithm, CancellationToken cancellationToken)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    var startTime = DateTime.Now;
    var stopwatch = Stopwatch.StartNew();
    var headerPanel = new Panel(
      $"[bold]Version:[/] {version}\n[bold]Started:[/] {startTime:yyyy-MM-dd HH:mm:ss}\n" +
      $"[bold]Manifest:[/] {outputManifest.Name}\n" +
      $"[bold]Algorithm:[/] {algorithm}\n[bold]Root:[/] {root.FullName}\n")
        .Header("[bold]Manifest Creation Info[/]", Justify.Center)
        .Expand();
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
            [.. Partitioner.Create(files).GetPartitions(Environment.ProcessorCount)
              .Select(partition => Task.Run(async () => {
                using (partition) {
                  while (partition.MoveNext()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = partition.Current;
                    var relPath = Path.GetRelativePath(root.FullName, file);
                    var fileSize = new FileInfo(file).Length;
                    int padLen = 50;
                    var abbreviated = PathUtils.AbbreviatePathForDisplay(relPath, padLen);
                    int padCount = padLen - abbreviated.Length;
                    if (padCount > 0) abbreviated = new string('▪', padCount) + abbreviated;
                    var safeRelPath = Markup.Escape(abbreviated);
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
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
    using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
    byte[] buffer = new byte[bufferSize];
    long totalRead = 0;
    int bytesRead;
    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
    {
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
