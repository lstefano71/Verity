using Spectre.Console;
using Spectre.Console.Cli;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

public class Program
{
  public static int Main(string[] args)
  {
    var app = new CommandApp();
    app.Configure(config => {
      config.SetApplicationName("Verity");
      config.SetHelpProvider(new DetailedHelpProvider());
      config.AddCommand<VerifyCommand>("verify");
      config.AddCommand<CreateCommand>("create");
    });
    return app.Run(args);
  }

  public static async Task<int> RunVerification(CliOptions options)
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

          summary = await VerificationService.VerifyChecksumsAsync(options, onProgress, onResult, onFileFound);
          progressTask.StopTask();
        });

    stopwatch.Stop();

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Verification Complete[/]");
    AnsiConsole.MarkupLine($"[green]  Success:[/] {summary.SuccessCount}");
    AnsiConsole.MarkupLine($"[yellow]Warnings:[/] {summary.WarningCount}");
    AnsiConsole.MarkupLine($"[red]    Errors:[/] {summary.ErrorCount}");
    AnsiConsole.MarkupLine($"[cyan]Total Time:[/] {stopwatch.Elapsed.TotalSeconds:F2}s");

    var megabytesPerSecond = summary.TotalBytesRead / 1024.0 / 1024.0 / stopwatch.Elapsed.TotalSeconds;
    if (double.IsNormal(megabytesPerSecond)) {
      AnsiConsole.MarkupLine($"[cyan] Throughput:[/] {megabytesPerSecond:F2} MB/s");
    }

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

  public static async Task<int> RunCreateManifest(FileInfo outputManifest, DirectoryInfo root, string algorithm)
  {
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    var startTime = DateTime.Now;
    var stopwatch = Stopwatch.StartNew();
    // Print static header once
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
    var files = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories);
    if (files.Length == 0) {
      AnsiConsole.MarkupLine("[yellow]No files found in the specified root directory.[/]");
      return 1;
    }
    using var manifestWriter = new StreamWriter(outputManifest.FullName, false, Encoding.UTF8);

    var currentlyHashing = new ConcurrentDictionary<string, byte>();
    var progressLock = new object();
    int processed = 0;
    long totalBytesRead = 0;

    await AnsiConsole.Progress()
        .Columns([
            new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
        ])
        .StartAsync(async ctx => {
          var progressTask = ctx.AddTask("[green]Hashing files[/]", maxValue: files.Length);
          await Task.WhenAll(
                  Partitioner.Create(files).GetPartitions(Environment.ProcessorCount)
                      .Select(partition => Task.Run(async () => {
                        using (partition) {
                          while (partition.MoveNext()) {
                            var file = partition.Current;
                            var relPath = Path.GetRelativePath(root.FullName, file);
                            currentlyHashing[relPath] = 0;
                            string hash = await ComputeFileHashAsync(file, algorithm);
                            lock (manifestWriter) {
                              manifestWriter.WriteLine($"{hash}\t{relPath}");
                            }
                            currentlyHashing.TryRemove(relPath, out _);
                            lock (progressLock) {
                              processed++;
                              totalBytesRead += new FileInfo(file).Length;
                              progressTask.Value = processed;
                            }
                          }
                        }
                      })
                  )
              );
        });
    stopwatch.Stop();
    AnsiConsole.MarkupLine($"[green]Manifest created:[/] {outputManifest.FullName}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Creation Complete[/]");
    AnsiConsole.MarkupLine($"[green]  Files:[/] {files.Length}");
    AnsiConsole.MarkupLine($"[cyan]Total Bytes:[/] {totalBytesRead}");
    AnsiConsole.MarkupLine($"[cyan]Total Time:[/] {stopwatch.Elapsed.TotalSeconds:F2}s");
    var mbps = totalBytesRead / 1024.0 / 1024.0 / stopwatch.Elapsed.TotalSeconds;
    if (double.IsNormal(mbps)) {
      AnsiConsole.MarkupLine($"[cyan] Throughput:[/] {mbps:F2} MB/s");
    }
    return 0;
  }

  public static async Task<string> ComputeFileHashAsync(string filePath, string algorithm)
  {
    using var stream = File.OpenRead(filePath);
    using HashAlgorithm hasher = algorithm switch {
      "SHA256" => SHA256.Create(),
      "SHA1" => SHA1.Create(),
      "MD5" => MD5.Create(),
      _ => throw new InvalidOperationException($"Unknown hash algorithm: {algorithm}")
    };
    var hashBytes = await hasher.ComputeHashAsync(stream);
    return Convert.ToHexStringLower(hashBytes);
  }

  public static string InferAlgorithmFromExtension(string manifestPath)
  {
    var ext = Path.GetExtension(manifestPath).ToLowerInvariant();
    return ext switch {
      ".sha256" => "SHA256",
      ".md5" => "MD5",
      ".sha1" => "SHA1",
      _ => "SHA256"
    };
  }
}

public class VerifyCommand : AsyncCommand<VerifySettings>
{
  public override async Task<int> ExecuteAsync(CommandContext context, VerifySettings settings)
  {
    var usedAlgorithm = string.IsNullOrWhiteSpace(settings.Algorithm) ? Program.InferAlgorithmFromExtension(settings.ChecksumFile.FullName) : settings.Algorithm;
    var options = new CliOptions(settings.ChecksumFile, settings.Root, usedAlgorithm);
    return await Program.RunVerification(options);
  }
}

public class CreateCommand : AsyncCommand<CreateSettings>
{
  public override async Task<int> ExecuteAsync(CommandContext context, CreateSettings settings)
  {
    var usedAlgorithm = string.IsNullOrWhiteSpace(settings.Algorithm) ? Program.InferAlgorithmFromExtension(settings.OutputManifest.FullName) : settings.Algorithm;
    return await Program.RunCreateManifest(settings.OutputManifest, settings.Root, usedAlgorithm);
  }
}

public class VerifySettings : CommandSettings
{
  [CommandArgument(0, "<checksumFile>")]
  public FileInfo ChecksumFile { get; set; }

  [CommandOption("--root")]
  public DirectoryInfo? Root { get; set; }

  [CommandOption("--algorithm")]
  public string? Algorithm { get; set; }
}

public class CreateSettings : CommandSettings
{
  [CommandArgument(0, "<outputManifest>")]
  public FileInfo OutputManifest { get; set; }

  [CommandOption("--root")]
  public DirectoryInfo Root { get; set; }

  [CommandOption("--algorithm")]
  public string? Algorithm { get; set; }
}
