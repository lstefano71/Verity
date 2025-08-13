using FluentAssertions;

using Spectre.Console.Testing;

public class ResultsPresenterTests
{
  private FinalSummary GetSampleSummary()
  {
    var results = new List<VerificationResult>
    {
      // 3 warnings: "Missing metadata"
      new VerificationResult(
        new ChecksumEntry("abc123", "file1.txt"),
        ResultStatus.Warning,
        ActualHash: "abc123",
        Details: "Missing metadata",
        FullPath: "C:/root/file1.txt"
      ),
      new VerificationResult(
        new ChecksumEntry("abc124", "file4.txt"),
        ResultStatus.Warning,
        ActualHash: "abc124",
        Details: "Missing metadata",
        FullPath: "C:/root/file4.txt"
      ),
      new VerificationResult(
        new ChecksumEntry("abc125", "file5.txt"),
        ResultStatus.Warning,
        ActualHash: "abc125",
        Details: "Missing metadata",
        FullPath: "C:/root/file5.txt"
      ),
      // 2 errors: "Hash mismatch"
      new VerificationResult(
        new ChecksumEntry("def456", "file2.txt"),
        ResultStatus.Error,
        ActualHash: "xyz789",
        Details: "Hash mismatch",
        FullPath: "C:/root/file2.txt"
      ),
      new VerificationResult(
        new ChecksumEntry("def457", "file6.txt"),
        ResultStatus.Error,
        ActualHash: "xyz790",
        Details: "Hash mismatch",
        FullPath: "C:/root/file6.txt"
      ),
      // 1 error: "File not found"
      new VerificationResult(
        new ChecksumEntry("ghi789", "file3.txt"),
        ResultStatus.Error,
        ActualHash: null,
        Details: "File not found",
        FullPath: "C:/root/file3.txt"
      ),
    };
    return new FinalSummary(
      TotalFiles: 16,
      SuccessCount: 10,
      WarningCount: 3,
      ErrorCount: 3,
      TotalBytesRead: 1234567,
      ProblematicResults: results
    );
  }

  [Fact]
  public void RenderSummaryTable_ShouldRenderExpectedLabelsAndValues()
  {
    var summary = GetSampleSummary();
    var elapsed = TimeSpan.FromSeconds(42);
    var console = new TestConsole();
    Spectre.Console.AnsiConsole.Console = console;
    ResultsPresenter.RenderSummaryTable(summary, elapsed);
    var output = console.Output;

    // Assert the output contains the expected labels
    var expectedLabels = new[] {
      "Success",
      "Warnings",
      "Errors",
      "Total Files",
      "Total Bytes Hashed",
      "Total Time",
      "Throughput"
    };
    foreach (var label in expectedLabels)
      output.Should().Contain(label, $"Label '{label}' not found");

    // Assert the output contains the expected warning/error details
    var expectedDetails = new[] {
      "Missing metadata",
      "Hash mismatch",
      "File not found"
    };
    foreach (var detail in expectedDetails)
      output.Should().Contain(detail, $"Detail '{detail}' not found");

    // Assert the order of summary labels
    var orderLabels = new[] {
      "Success",
      "Warnings",
      "Errors",
      "Total Files",
      "Total Bytes Hashed",
      "Total Time",
      "Throughput"
    };
    var lastIndex = -1;
    foreach (var label in orderLabels) {
      var idx = output.IndexOf(label);
      idx.Should().BeGreaterThan(lastIndex, $"Label '{label}' is out of order");
      lastIndex = idx;
    }

    // Assert the order of details under Warnings and Errors
    var detailsOrder = new[] {
      "Missing metadata",
      "Hash mismatch",
      "File not found"
    };
    lastIndex = -1;
    foreach (var detail in detailsOrder) {
      var idx = output.IndexOf(detail);
      idx.Should().BeGreaterThan(lastIndex, $"Detail '{detail}' is out of order");
      lastIndex = idx;
    }
  }

  [Fact]
  public void RenderDiagnosticsTable_ShouldRenderDiagnosticReportWithProblematicResults()
  {
    var summary = GetSampleSummary();
    var console = new TestConsole();
    Spectre.Console.AnsiConsole.Console = console;
    ResultsPresenter.RenderDiagnosticsTable(summary);
    var output = console.Output;

    // Assert the output contains the expected table headers
    var expectedHeaders = new[] {
      "Diagnostic Report",
      "Status",
      "File",
      "Details",
      "Expected Hash",
      "Actual Hash"
    };
    foreach (var header in expectedHeaders)
      output.Should().Contain(header, $"Header '{header}' not found");

    // Assert the output contains expected status values
    output.Should().Contain("Warning", "Warning status not found");
    output.Should().Contain("Error", "Error status not found");

    // Assert the output contains expected details and file names
    var expectedDetails = new[] {
      "Missing metadata",
      "Hash mismatch",
      "File not found"
    };
    foreach (var detail in expectedDetails)
      output.Should().Contain(detail, $"Detail '{detail}' not found");

    var expectedFiles = new[] {
      "file1.txt",
      "file2.txt",
      "file3.txt"
    };
    foreach (var file in expectedFiles)
      output.Should().Contain(file, $"File '{file}' not found");

    // Assert the order of table headers
    var orderHeaders = new[] {
      "Status",
      "File",
      "Details",
      "Expected Hash",
      "Actual Hash"
    };
    var lastIndex = output.IndexOf("Diagnostic Report");
    foreach (var header in orderHeaders) {
      var idx = output.IndexOf(header);
      idx.Should().BeGreaterThan(lastIndex, $"Header '{header}' is out of order");
      lastIndex = idx;
    }
  }

  [Fact]
  public async Task WriteErrorReportAsync_ShouldWriteErrorReportToFile()
  {
    var summary = GetSampleSummary();
    var tempFile = Path.GetTempFileName();
    try {
      await ResultsPresenter.WriteErrorReportAsync(summary, new FileInfo(tempFile));
      var report = await File.ReadAllTextAsync(tempFile);
      report.Should().Contain("Status");
      report.Should().Contain("File");
      report.Should().Contain("Details");
      report.Should().Contain("ExpectedHash");
      report.Should().Contain("ActualHash");
      report.Should().Contain("WARNING");
      report.Should().Contain("ERROR");
      report.Should().Contain("Missing metadata");
      report.Should().Contain("Hash mismatch");
      report.Should().Contain("File not found");
    } finally {
      File.Delete(tempFile);
    }
  }
}
