using Spectre.Console;

using System.Collections.Concurrent;
using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text;

var checksumFileArgument = new Argument<FileInfo>(
    name: "checksumFile",
    description: "Path to the checksum file (e.g., hashes.sha256).")
    .ExistingOnly();

var rootOption = new Option<DirectoryInfo>(
  name: "--root",
  description: "The root directory for the files. If omitted, the checksum file's directory is used.")
  .ExistingOnly();

var algorithmOption = new Option<string>(
    name: "--algorithm",
    description: "The hash algorithm to use.",
    getDefaultValue: () => "SHA256");

var rootCommand = new RootCommand("Verity: A high-performance file checksum verifier.")
{
    checksumFileArgument,
    rootOption,
    algorithmOption
};


rootCommand.SetHandler(async (file, root, algo) => {
  var options = new CliOptions(file, root, algo);
  await RunVerification(options);
}, checksumFileArgument, rootOption, algorithmOption);

return await rootCommand.InvokeAsync(args);

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
