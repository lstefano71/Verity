using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;

var verifyCommand = new Command("verify", "Verify files against a checksum manifest.");
var verifyChecksumFileArgument = new Argument<FileInfo>(
    name: "checksumFile",
    description: "Path to the checksum file (e.g., hashes.sha256).")
    .ExistingOnly();
var verifyRootOption = new Option<DirectoryInfo>(
    name: "--root",
    description: "The root directory for the files. If omitted, the checksum file's directory is used.")
    .ExistingOnly();
var verifyAlgorithmOption = new Option<string>(
    "--algorithm",
    () => "SHA256",
    "The hash algorithm to use.");
verifyCommand.AddArgument(verifyChecksumFileArgument);
verifyCommand.AddOption(verifyRootOption);
verifyCommand.AddOption(verifyAlgorithmOption);
verifyCommand.SetHandler(
    async (FileInfo file, DirectoryInfo root, string algo) =>
    {
        var usedAlgorithm = string.IsNullOrWhiteSpace(algo) ? InferAlgorithmFromExtension(file.FullName) : algo;
        var options = new CliOptions(file, root, usedAlgorithm);
        await RunVerification(options);
    },
    verifyChecksumFileArgument,
    verifyRootOption,
    verifyAlgorithmOption
);

var createCommand = new Command("create", "Create a checksum manifest from files in a directory.");
var outputManifestArgument = new Argument<FileInfo>(
    name: "outputManifest",
    description: "Path to write the checksum manifest file.");
var createRootOption = new Option<DirectoryInfo>(
    name: "--root",
    description: "The root directory to scan for files.")
    .ExistingOnly();
var createAlgorithmOption = new Option<string>(
    "--algorithm",
    () => "SHA256",
    "The hash algorithm to use.");
createCommand.AddArgument(outputManifestArgument);
createCommand.AddOption(createRootOption);
createCommand.AddOption(createAlgorithmOption);
createCommand.SetHandler(
    async (FileInfo outputManifest, DirectoryInfo root, string algo) =>
    {
        var usedAlgorithm = string.IsNullOrWhiteSpace(algo) ? InferAlgorithmFromExtension(outputManifest.FullName) : algo;
        await RunCreateManifest(outputManifest, root, usedAlgorithm);
    },
    outputManifestArgument,
    createRootOption,
    createAlgorithmOption
);

var rootCommand = new RootCommand("Verity: A high-performance file checksum verifier.");
rootCommand.AddCommand(verifyCommand);
rootCommand.AddCommand(createCommand);
rootCommand.TreatUnmatchedTokensAsErrors = true;

async static Task<int> RunVerification(CliOptions options)
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

async static Task RunCreateManifest(FileInfo outputManifest, DirectoryInfo root, string algorithm)
{
    var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    AnsiConsole.MarkupLine($"[bold cyan]Verity v{version}[/] - Checksum Manifest Creator");
    if (root == null || !root.Exists)
    {
        AnsiConsole.MarkupLine("[red]Error: Root directory must be specified and exist.[/]");
        return;
    }
    var files = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories);
    if (files.Length == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No files found in the specified root directory.[/]");
        return;
    }
    using var manifestWriter = new StreamWriter(outputManifest.FullName, false, Encoding.UTF8);
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
            foreach (var file in files)
            {
                var relPath = Path.GetRelativePath(root.FullName, file);
                string hash = await ComputeFileHashAsync(file, algorithm);
                manifestWriter.WriteLine($"{hash}\t{relPath}");
                progressTask.Increment(1);
            }
        });
    AnsiConsole.MarkupLine($"[green]Manifest created:[/] {outputManifest.FullName}");
}

static async Task<string> ComputeFileHashAsync(string filePath, string algorithm)
{
    using var stream = File.OpenRead(filePath);
    using var hasher = HashAlgorithm.Create(algorithm) ?? throw new InvalidOperationException($"Unknown hash algorithm: {algorithm}");
    var hashBytes = await hasher.ComputeHashAsync(stream);
    return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
}

static string InferAlgorithmFromExtension(string manifestPath)
{
    var ext = Path.GetExtension(manifestPath).ToLowerInvariant();
    return ext switch
    {
        ".sha256" => "SHA256",
        ".md5" => "MD5",
        ".sha1" => "SHA1",
        _ => "SHA256"
    };
}

return await rootCommand.InvokeAsync(args);
