using Spectre.Console;
using System.Text;
using Humanizer;

public class ResultsPresenter
{
    public static void RenderSummaryTable(FinalSummary summary, TimeSpan elapsed)
    {
        var summaryTable = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Label").LeftAligned())
            .AddColumn(new TableColumn("Value").LeftAligned());

        summaryTable.AddRow("[green]Success[/]", $"{summary.SuccessCount:N0}");
        summaryTable.AddRow("[yellow]Warnings[/]", $"{summary.WarningCount:N0}");
        summaryTable.AddRow("[red]Errors[/]", $"{summary.ErrorCount:N0}");
        summaryTable.AddRow("[cyan]Total Time[/]", elapsed.Humanize(2));
        var throughput = summary.TotalBytesRead.Bytes().Per(elapsed).Humanize();
        summaryTable.AddRow("[cyan]Throughput[/]", throughput);

        AnsiConsole.Write(summaryTable);
    }

    public static void RenderDiagnosticsTable(FinalSummary summary)
    {
        if ((summary.ProblematicResults?.Count ?? 0) == 0 && (summary.UnlistedFiles?.Count ?? 0) == 0)
            return;

        AnsiConsole.WriteLine();
        var table = new Table().Expand();
        table.Border = TableBorder.Rounded;
        table.Title = new TableTitle("[bold yellow]Diagnostic Report[/]");
        table.AddColumn("Status");
        table.AddColumn("File");
        table.AddColumn("Details");
        table.AddColumn("Expected Hash");
        table.AddColumn("Actual Hash");

        foreach (var result in summary.ProblematicResults.OrderBy(r => r.Status).ThenBy(r => r.Entry.RelativePath))
        {
            var statusMarkup = result.Status switch
            {
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

        foreach (var file in summary.UnlistedFiles.OrderBy(f => f))
        {
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

    public static async Task WriteErrorReportAsync(FinalSummary summary)
    {
        var errorReport = new StringBuilder();
        errorReport.AppendLine("#Status\tFile\tDetails\tExpectedHash\tActualHash");

        foreach (var result in summary.ProblematicResults.OrderBy(r => r.Status).ThenBy(r => r.Entry.RelativePath))
        {
            errorReport.AppendLine(string.Join("\t",
                result.Status.ToString().ToUpperInvariant(),
                result.FullPath ?? result.Entry.RelativePath,
                result.Details ?? "",
                result.Entry.ExpectedHash,
                result.ActualHash ?? ""
            ));
        }

        foreach (var file in summary.UnlistedFiles.OrderBy(f => f))
        {
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
}