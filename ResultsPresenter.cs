using Humanizer;

using Spectre.Console;

using System.Text;

public class ResultsPresenter
{
  private static IEnumerable<(string Label, int Count)> GetTopDetailsGroups(IEnumerable<VerificationResult> results, string color, int topN = 3)
  {
    var groups = results
        .GroupBy(r => r.Details ?? "(no details)")
        .Select(g => new { Label = g.Key, Count = g.Count() })
        .OrderByDescending(g => g.Count)
        .ThenBy(g => g.Label);


    // take the top N groups and sum the rest into "Other"

    var topGroups = groups.Take(topN).ToList();
      var otherCount = groups.Skip(topN).Sum(g => g.Count);
      if (otherCount > 0)
      {
          topGroups.Add(new { Label = "Other", Count = otherCount });
      }
      // Format with color
      return topGroups.Select(g => ($"  [{color}]- {g.Label}[/]", g.Count));
  }

  public static void RenderSummaryTable(FinalSummary summary, TimeSpan elapsed)
  {
    var summaryTable = new Table()
        .NoBorder()
        .HideHeaders()
        .AddColumn(new TableColumn("Label").LeftAligned())
        .AddColumn(new TableColumn("Value").LeftAligned());

    // Success total
    summaryTable.AddRow("[green]Success[/]", $"{summary.SuccessCount:N0}");
    // No breakdown for Success, as individual results are not stored

    // Warnings total
    summaryTable.AddRow("[yellow]Warnings[/]", $"{summary.WarningCount:N0}");
    // Top 3 Details for Warnings, rest as Other
    foreach (var (label, count) in GetTopDetailsGroups(
        summary.ProblematicResults.Where(r => r.Status == ResultStatus.Warning), "yellow"))
    {
        summaryTable.AddRow(label, $"{count:N0}");
    }

    // Errors total
    summaryTable.AddRow("[red]Errors[/]", $"{summary.ErrorCount:N0}");
    // Top 3 Details for Errors, rest as Other
    foreach (var (label, count) in GetTopDetailsGroups(
        summary.ProblematicResults.Where(r => r.Status == ResultStatus.Error), "red"))
    {
        summaryTable.AddRow(label, $"{count:N0}");
    }

    summaryTable.AddEmptyRow();
    summaryTable.AddRow("[blue]Total Files[/]", $"{summary.TotalFiles:N0}");
    summaryTable.AddRow("[blue]Total Bytes Hashed[/]", summary.TotalBytesRead.Bytes().Humanize());
    summaryTable.AddRow("[cyan]Total Time[/]", elapsed.Humanize(2));
    var throughput = summary.TotalBytesRead.Bytes().Per(elapsed).Humanize();
    summaryTable.AddRow("[cyan]Throughput[/]", throughput);

    AnsiConsole.Write(summaryTable);
  }

  public static void RenderDiagnosticsTable(FinalSummary summary)
  {
    if ((summary.ProblematicResults?.Count ?? 0) == 0 && (summary.UnlistedFiles?.Count ?? 0) == 0)
      return;

    const int hashDisplayLen = 12;

    AnsiConsole.WriteLine();
    var table = new Table().Expand();
    table.Border = TableBorder.Rounded;
    table.Title = new TableTitle("[bold yellow]Diagnostic Report[/]");
    table.AddColumn("Status");
    table.AddColumn("File");
    table.AddColumn("Details");
    table.AddColumn("Expected Hash");
    table.AddColumn("Actual Hash");

    static string TruncateHash(string? hash) =>
      string.IsNullOrEmpty(hash) ? "N/A" :
      hash.Length > hashDisplayLen ? hash[..hashDisplayLen] + "…" : hash;

    foreach (var result in summary.ProblematicResults.OrderBy(r => r.Status).ThenBy(r => r.Entry.RelativePath)) {
      var statusMarkup = result.Status switch {
        ResultStatus.Warning => "[yellow]Warning[/]",
        ResultStatus.Error => "[red]Error[/]",
        _ => "[grey]Info[/]"
      };
      table.AddRow(
          statusMarkup,
          result.FullPath ?? result.Entry.RelativePath,
          result.Details ?? string.Empty,
          TruncateHash(result.Entry.ExpectedHash),
          TruncateHash(result.ActualHash)
      );
    }

    foreach (var file in summary.UnlistedFiles.OrderBy(f => f)) {
      table.AddRow(
          "Warning",
          Markup.Escape(file),
          "File exists but not in checksum list.",
          Markup.Escape("N/A"),
          Markup.Escape("N/A")
      );
    }

    AnsiConsole.Write(table);
  }

  public static async Task WriteErrorReportAsync(FinalSummary summary, FileInfo? outputFile = null)
  {
    var errorReport = new StringBuilder();
    errorReport.AppendLine("#Status\tFile\tDetails\tExpectedHash\tActualHash");

    foreach (var result in summary.ProblematicResults.OrderBy(r => r.Status).ThenBy(r => r.Entry.RelativePath)) {
      errorReport.AppendLine(string.Join("\t",
          result.Status.ToString().ToUpperInvariant(),
          result.FullPath ?? result.Entry.RelativePath,
          result.Details ?? "",
          result.Entry.ExpectedHash,
          result.ActualHash ?? ""
      ));
    }

    foreach (var file in summary.UnlistedFiles.OrderBy(f => f)) {
      errorReport.AppendLine(string.Join("\t",
          "WARNING",
          file,
          "File exists but not in checksum list.",
          "",
          ""
      ));
    }

    if (outputFile != null) {
      await File.WriteAllTextAsync(outputFile.FullName, errorReport.ToString());
    }
    // Only write to stderr if no output file specified
    else {
      await global::System.Console.Error.WriteAsync(errorReport.ToString());
    }
  }
}