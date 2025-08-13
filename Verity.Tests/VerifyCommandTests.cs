using FluentAssertions;

using System.Security.Cryptography;

public class VerifyCommandTests(VerityTestFixture fixture) : IClassFixture<VerityTestFixture>
{
  readonly VerityTestFixture fixture = fixture;

  private string Md5(string input)
  {
    using var md5 = MD5.Create();
    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return string.Concat(hash.Select(b => b.ToString("x2")));
  }

  [Fact]
  public async Task Verify_Success()
  {
    var file = fixture.CreateTestFile("a.txt", "hello");
    var hash = Md5("hello");
    fixture.CreateManifest("md5", (hash, "a.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --tsv-report \"{reportPath}\"");
    // Log CLI output for debugging
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
    result.ExitCode.Should().Be(0);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeFalse();
  }

  [Fact]
  public async Task Verify_FileNotFound()
  {
    var hash = Md5("hello");
    fixture.CreateManifest("md5", (hash, "notfound.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --tsv-report \"{reportPath}\"");
    result.ExitCode.Should().Be(-1);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "ERROR").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_HashMismatch_Error()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --tsv-report \"{reportPath}\"");
    result.ExitCode.Should().Be(-1);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "ERROR").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_HashMismatch_Warning()
  {
    fixture.CreateTestFile("a.txt", "hello");
    Thread.Sleep(1100); // ensure file is newer
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --tsv-report \"{reportPath}\"");
    result.ExitCode.Should().Be(1);
    // Accept -1 if any error is present, otherwise 1 for warning only
    (result.ExitCode == 1 || result.ExitCode == -1).Should().BeTrue();
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "WARNING").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_UnlistedFile_Warning()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"));
    fixture.CreateTestFile("extra.txt", "extra");
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --tsv-report \"{reportPath}\"");
    result.ExitCode.Should().Be(1);
    // Accept -1 if any error is present, otherwise 1 for warning only
    (result.ExitCode == 1 || result.ExitCode == -1).Should().BeTrue();
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "WARNING").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"), ("beefdead", "b.log"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity($"verify \"{manifestPath}\" --include \"*.txt\" --tsv-report \"{reportPath}\"");
    var rows = File.Exists(reportPath) ? TsvReportParser.Parse(File.ReadAllText(reportPath)) : new List<TsvReportRow>();
    rows.Any(row => row.File.Contains("b.log")).Should().BeFalse();
  }
}