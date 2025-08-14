public class AddCommandTests : CommandTestBase, IClassFixture<CommonTestFixture>
{
  public AddCommandTests(CommonTestFixture fixture) : base(fixture) { }

  [Fact]
  public async Task Add_NewFiles()
  {
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create manifest.md5");
    fixture.CreateTestFile("b.txt", "world");
    var result = await fixture.RunVerity("add manifest.md5");
    Assert.Equal(0, result.ExitCode);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    Assert.Contains("a.txt", manifestContent);
    Assert.Contains("b.txt", manifestContent);
  }

  [Fact]
  public async Task Add_NoNewFiles()
  {
    fixture.CreateTestFile("a.txt", "hello");
    // Place manifest in a subdirectory to avoid it being added as a new file
    var manifestDir = "manifests";
    Directory.CreateDirectory(Path.Combine(fixture.TempDir, manifestDir));
    var manifestPath = Path.Combine(manifestDir, "manifest.md5");
    _ = await fixture.RunVerity($"create {manifestPath} --root .");
    // Exclude the manifest file itself from being added
    ProcessResult? result = await fixture.RunVerity($"add {manifestPath} --root . --exclude \"manifests/*\"");
    Assert.Equal(1, result.ExitCode);

    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    Assert.Contains("a.txt", manifestContent);
    Assert.DoesNotContain(manifestPath.Replace('\\', '/'), manifestContent);
  }

  [Fact]
  public async Task Add_GlobFilteringInclude()
  {
    // Create initial file and manifest
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create manifest.md5");
    // Add two new files, only one matches the glob
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.txt", "extra");
    var result = await fixture.RunVerity("add manifest.md5 --include \"*.log\"");
    Assert.Equal(0, result.ExitCode);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    // The manifest should contain the original file and the new file matching the glob
    Assert.Contains("a.txt", manifestContent); // original entry remains
    Assert.Contains("b.log", manifestContent); // new entry added
    Assert.DoesNotContain("c.txt", manifestContent); // not added, not present
  }

  [Fact]
  public async Task Add_GlobFilteringExclude()
  {
    // Create initial file and manifest
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create manifest.md5");
    // Add two new files, one will be excluded by glob
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.txt", "extra");
    var result = await fixture.RunVerity("add manifest.md5 --exclude \"*.log\"");
    Assert.Equal(0, result.ExitCode);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    // The manifest should contain the original file and the new file not matching the exclude glob
    Assert.Contains("a.txt", manifestContent); // original entry remains
    Assert.Contains("c.txt", manifestContent); // new entry added
    Assert.DoesNotContain("b.log", manifestContent); // excluded, not present
  }

  [Fact]
  public async Task Add_WithCustomRoot_AddsOnlyRootFiles()
  {
    // Create files in a subdirectory
    var subDir = "subdir";
    Directory.CreateDirectory(Path.Combine(fixture.TempDir, subDir));
    fixture.CreateTestFile(Path.Combine(subDir, "a.txt"), "hello");
    fixture.CreateTestFile("b.txt", "world"); // Should not be added

    // Create manifest in root, only for a.txt
    var manifestPath = fixture.GetManifestPath("md5");
    var result = await fixture.RunVerity($"create manifest.md5 --include \"{subDir}/*\"");
    Assert.Equal(0, result.ExitCode);

    // Add new files from subdir only
    fixture.CreateTestFile(Path.Combine(subDir, "c.txt"), "extra");
    fixture.CreateTestFile("d.txt", "other"); // Should not be added
    result = await fixture.RunVerity($"add manifest.md5 --root {subDir}");
    Assert.Equal(0, result.ExitCode);

    var manifestContent = File.ReadAllText(manifestPath);
    Assert.Contains("a.txt", manifestContent); // original entry
    Assert.Contains("c.txt", manifestContent); // new entry from subdir
    Assert.DoesNotContain("b.txt", manifestContent); // not in root
    Assert.DoesNotContain("d.txt", manifestContent); // not in root
  }


  [Fact]
  public async Task Add_ManifestTxt_DefaultsToSha256()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = "manifest.txt";
    await fixture.RunVerity($"create {manifestPath}");
    fixture.CreateTestFile("b.txt", "world");
    var result = await fixture.RunVerity($"add {manifestPath}");
    Assert.Equal(0, result.ExitCode);

    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    var expectedHash = CommonTestFixture.Sha256("world");
    Assert.Contains(expectedHash, manifestContent);
    Assert.Contains("b.txt", manifestContent);
  }


}