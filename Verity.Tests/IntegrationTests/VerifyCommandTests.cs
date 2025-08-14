public class VerifyCommandTests : CommandTestBase, IClassFixture<CommonTestFixture>
{
  public VerifyCommandTests(CommonTestFixture fixture) : base(fixture) { }

  [Fact]
  public async Task Verify_Success()
  {
    var manifestPath = fixture.GetManifestPath("md5");
    Assert.False(File.Exists(manifestPath));
    _ = fixture.CreateTestFile("a.txt", "hello");
    var hash = CommonTestFixture.Md5("hello");
    fixture.CreateManifest("md5", (hash, "a.txt"));
    var reportPath = fixture.GetFullPath("report.tsv");
    Assert.False(File.Exists(reportPath));
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    Assert.Equal(0, result.ExitCode);
    Assert.Empty(result.StdErr);
    Assert.True(File.Exists(reportPath));
    // Verify the report is empty since everything is valid
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    Assert.Empty(rows);
  }

  [Fact]
  public async Task Verify_FileNotFound()
  {
    var hash = CommonTestFixture.Md5("hello");
    fixture.CreateManifest("md5", (hash, "notfound.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    Assert.Equal(-1, result.ExitCode);
    Assert.Empty(result.StdErr);
    Assert.True(File.Exists(reportPath));
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    Assert.True(rows.Any(row => row.Status == "ERROR"));
  }

  [Fact]
  public async Task Verify_HashMismatch_Error()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateManifest("md5", ("deadbeef", "a.txt"));
    var manifestPath = fixture.GetManifestPath("md5");
    var reportPath = fixture.GetFullPath("report.tsv");
    var result = await fixture.RunVerity("verify manifest.md5 --tsv-report report.tsv");
    Assert.Equal(-1, result.ExitCode);
    Assert.Empty(result.StdErr);
    Assert.True(File.Exists(reportPath));
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    Assert.True(rows.Any(row => row.Status == "ERROR"));
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
    Assert.Equal(1, result.ExitCode);
    Assert.Empty(result.StdErr);
    Assert.True(File.Exists(reportPath));
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    Assert.True(rows.Any(row => row.Status == "WARNING"));
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
    Assert.Equal(1, result.ExitCode);
    // Accept -1 if any error is present, otherwise 1 for warning only
    Assert.True(result.ExitCode == 1 || result.ExitCode == -1);
    Assert.Empty(result.StdErr);
    Assert.True(File.Exists(reportPath));
    var rows = TsvReportParser.Parse(File.ReadAllText(reportPath));
    Assert.True(rows.Any(row => row.Status == "WARNING"));
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
    Assert.False(rows.Any(row => row.File.Contains("b.log")));
    Assert.True(rows.Any(row => row.File.Contains("a.txt")));
  }

  [Fact]
  public async Task Verify_ManifestTxt_DefaultsToSha256()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = "manifest.txt";
    await fixture.RunVerity($"create {manifestPath}");
    var result = await fixture.RunVerity($"verify {manifestPath}");
    Assert.Equal(0, result.ExitCode);
    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    var expectedHash = CommonTestFixture.Sha256("hello");
    Assert.Contains(expectedHash, manifestContent);
  }
}

