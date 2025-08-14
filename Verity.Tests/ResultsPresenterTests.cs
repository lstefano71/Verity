using Spectre.Console.Testing;

public class ResultsPresenterTests
{
  private static FinalSummary GetSampleSummary()
  {
    var results = new List<VerificationResult>
    {
      // 3 warnings: "Missing metadata"
      new(
        new ChecksumEntry("abc123", "file1.txt"),
        ResultStatus.Warning,
        ActualHash: "abc123",
        Details: "Missing metadata",
        FullPath: "C:/root/file1.txt"
      ),
      new(
        new ChecksumEntry("abc124", "file4.txt"),
        ResultStatus.Warning,
        ActualHash: "abc124",
        Details: "Missing metadata",
        FullPath: "C:/root/file4.txt"
      ),
      new(
        new ChecksumEntry("abc125", "file5.txt"),
        ResultStatus.Warning,
        ActualHash: "abc125",
        Details: "Missing metadata",
        FullPath: "C:/root/file5.txt"
      ),
      // 2 errors: "Hash mismatch"
      new(
        new ChecksumEntry("def456", "file2.txt"),
        ResultStatus.Error,
        ActualHash: "xyz789",
        Details: "Hash mismatch",
        FullPath: "C:/root/file2.txt"
      ),
      new(
        new ChecksumEntry("def457", "file6.txt"),
        ResultStatus.Error,
        ActualHash: "xyz790",
        Details: "Hash mismatch",
        FullPath: "C:/root/file6.txt"
      ),
      // 1 error: "File not found"
      new(
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
      Assert.Contains(label, output);

    // Assert the output contains the expected warning/error details
    var expectedDetails = new[] {
      "Missing metadata",
      "Hash mismatch",
      "File not found"
    };
    foreach (var detail in expectedDetails)
      Assert.Contains(detail, output);

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
      Assert.True(idx > lastIndex);
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
      Assert.True(idx > lastIndex);
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
      Assert.Contains(header, output);

    // Assert the output contains expected status values
    Assert.Contains("Warning", output);
    Assert.Contains("Error", output);

    // Assert the output contains expected details and file names
    var expectedDetails = new[] {
      "Missing metadata",
      "Hash mismatch",
      "File not found"
    };
    foreach (var detail in expectedDetails)
      Assert.Contains(detail, output);

    var expectedFiles = new[] {
      "file1.txt",
      "file2.txt",
      "file3.txt"
    };
    foreach (var file in expectedFiles)
      Assert.Contains(file, output);

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
      Assert.True(idx > lastIndex);
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
      Assert.Contains("Status", report);
      Assert.Contains("File", report);
      Assert.Contains("Details", report);
      Assert.Contains("ExpectedHash", report);
      Assert.Contains("ActualHash", report);
      Assert.Contains("WARNING", report);
      Assert.Contains("ERROR", report);
      Assert.Contains("Missing metadata", report);
      Assert.Contains("Hash mismatch", report);
      Assert.Contains("File not found", report);
    } finally {
      File.Delete(tempFile);
    }
  }
}
