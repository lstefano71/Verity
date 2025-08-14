using FluentAssertions;

public class VerifyCommandTests : CommandTestBase, IClassFixture<CommonTestFixture>
{
  public VerifyCommandTests(CommonTestFixture fixture) : base(fixture) { }

  [Fact]
  public async Task Verify_Success()
  {
    var manifestPath = fixture.GetManifestPath("md5");
    File.Exists(manifestPath).Should().BeFalse();
    _ = fixture.CreateTestFile("a.txt", "hello");
    var hash = CommonTestFixture.Md5("hello");
    fixture.CreateManifest("md5", (hash, "a.txt"));
    var reportPath = fixture.GetFullPath("report.tsv");
    File.Exists(reportPath).Should().BeFalse();
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    result.ExitCode.Should().Be(0);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    // Verify the report is empty since everything is valid
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Should().BeEmpty();
  }

  [Fact]
  public async Task Verify_FileNotFound()
  {
    var hash = CommonTestFixture.Md5("hello");
    fixture.CreateManifest("md5", (hash, "notfound.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
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
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    result.ExitCode.Should().Be(-1);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "ERROR").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_HashMismatch_Warning()
  {
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"));
    await Task.Delay(1100); // ensure file is newer
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    result.ExitCode.Should().Be(1);
    result.StdErr.Should().BeEmpty();
    File.Exists(reportPath).Should().BeTrue();
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.Status == "WARNING").Should().BeTrue();
  }

  [Fact]
  public async Task Verify_UnlistedFile_Warning()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateManifest("md5", (CommonTestFixture.Md5("hello"), "a.txt"));
    fixture.CreateTestFile("extra.txt", "extra");
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
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
    var result = await fixture.RunVerity("verify manifest.md5 --include \"*.txt\" --tsv-report report.tsv");
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    rows.Any(row => row.File.Contains("b.log")).Should().BeFalse();
    rows.Any(row => row.File.Contains("a.txt")).Should().BeTrue();
  }

  [Fact]
  public async Task Verify_ManifestTxt_DefaultsToSha256()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = "manifest.txt";
    await fixture.RunVerity($"create {manifestPath}");
    var result = await fixture.RunVerity($"verify {manifestPath}");
    result.ExitCode.Should().Be(0);
    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    var expectedHash = CommonTestFixture.Sha256("hello");
    manifestContent.Should().Contain(expectedHash);
  }
}

