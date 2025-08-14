public class CreateCommandTests : CommandTestBase, IClassFixture<CommonTestFixture>
{
  public CreateCommandTests(CommonTestFixture fixture) : base(fixture) { }

  [Fact]
  public async Task Create_ManifestTxt_DefaultsToSha256()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = "manifest.txt";
    var result = await fixture.RunVerity($"create {manifestPath}");
    Assert.Equal(0, result.ExitCode);

    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    var expectedHash = CommonTestFixture.Sha256("hello");
    Assert.Contains(expectedHash, manifestContent);
    Assert.Contains("a.txt", manifestContent);
  }

  [Fact]
  public async Task Create_BasicCreation()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    var result = await fixture.RunVerity("create manifest.md5");
    Assert.Equal(0, result.ExitCode);
    Assert.True(File.Exists(manifestPath));
    var manifestContent = File.ReadAllText(manifestPath);
    Assert.Contains("a.txt", manifestContent);
    Assert.Contains(CommonTestFixture.Md5("hello"), manifestContent);
  }

  [Fact]
  public async Task Create_EmptyDirectory_WarningReport()
  {
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    Assert.False(File.Exists(manifestPath));
    var reportPath = fixture.GetFullPath("report.tsv");
    if (File.Exists(reportPath)) File.Delete(manifestPath);
    Assert.False(File.Exists(manifestPath));
    var manifestDir = Path.GetDirectoryName(manifestPath)!;
    Assert.True(Directory.Exists(manifestDir));
    Assert.Empty(Directory.GetFiles(manifestDir));
    var result = await fixture.RunVerity("create manifest.md5 --tsv-report report.tsv");
    Assert.Equal(1, result.ExitCode);
    Assert.True(File.Exists(manifestPath));
    Assert.Empty(File.ReadAllText(manifestPath));
    Assert.False(File.Exists(reportPath));
  }

  [Fact]
  public async Task Create_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.tmp", "tmp");
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    var result = await fixture.RunVerity("create manifest.md5 --include \"*.txt;*.log\" --exclude \"*.tmp\"");
    Assert.Equal(0, result.ExitCode);
    var manifestContent = File.ReadAllText(manifestPath);
    Assert.Contains("a.txt", manifestContent);
    Assert.Contains("b.log", manifestContent);
    Assert.DoesNotContain("c.tmp", manifestContent);
  }

  [Fact]
  public async Task Create_WithCustomRoot_IncludesOnlyRootFiles()
  {
    // Create files in a subdirectory
    var subDir = "subdir";
    Directory.CreateDirectory(Path.Combine(fixture.TempDir, subDir));
    fixture.CreateTestFile(Path.Combine(subDir, "a.txt"), "hello");
    fixture.CreateTestFile("b.txt", "world"); // Should not be included

    // Place manifest in root, but scan subdir
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    var result = await fixture.RunVerity($"create manifest.md5 --root {subDir}");
    Assert.Equal(0, result.ExitCode);
    Assert.True(File.Exists(manifestPath));
    var manifestContent = File.ReadAllText(manifestPath);
    Assert.Contains("a.txt", manifestContent);
    Assert.DoesNotContain("b.txt", manifestContent);
  }
}