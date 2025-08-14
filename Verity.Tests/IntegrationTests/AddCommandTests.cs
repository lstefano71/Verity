using FluentAssertions;

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
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.txt");
  }

  [Fact]
  public async Task Add_NoNewFiles()
  {
    fixture.CreateTestFile("a.txt", "hello");
    // Place manifest in a subdirectory to avoid it being added as a new file
    var manifestDir = "manifests";
    Directory.CreateDirectory(Path.Combine(fixture.TempDir, manifestDir));
    var manifestPath = Path.Combine(manifestDir, "manifest.md5");
    var result = await fixture.RunVerity($"create {manifestPath} --root .");
    // Exclude the manifest file itself from being added
    result = await fixture.RunVerity($"add {manifestPath} --root . --exclude \"manifests/*\"");
    result.ExitCode.Should().Be(1);

    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().NotContain(manifestPath.Replace('\\', '/'));
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
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    // The manifest should contain the original file and the new file matching the glob
    manifestContent.Should().Contain("a.txt"); // original entry remains
    manifestContent.Should().Contain("b.log"); // new entry added
    manifestContent.Should().NotContain("c.txt"); // not added, not present
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
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    // The manifest should contain the original file and the new file not matching the exclude glob
    manifestContent.Should().Contain("a.txt"); // original entry remains
    manifestContent.Should().Contain("c.txt"); // new entry added
    manifestContent.Should().NotContain("b.log"); // excluded, not present
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
    result.ExitCode.Should().Be(0);

    // Add new files from subdir only
    fixture.CreateTestFile(Path.Combine(subDir, "c.txt"), "extra");
    fixture.CreateTestFile("d.txt", "other"); // Should not be added
    result = await fixture.RunVerity($"add manifest.md5 --root {subDir}");
    result.ExitCode.Should().Be(0);

    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt"); // original entry
    manifestContent.Should().Contain("c.txt"); // new entry from subdir
    manifestContent.Should().NotContain("b.txt"); // not in root
    manifestContent.Should().NotContain("d.txt"); // not in root
  }


  [Fact]
  public async Task Add_ManifestTxt_DefaultsToSha256()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = "manifest.txt";
    await fixture.RunVerity($"create {manifestPath}");
    fixture.CreateTestFile("b.txt", "world");
    var result = await fixture.RunVerity($"add {manifestPath}");
    result.ExitCode.Should().Be(0);

    var manifestContent = File.ReadAllText(Path.Combine(fixture.TempDir, manifestPath));
    var expectedHash = fixture.Sha256("world");
    manifestContent.Should().Contain(expectedHash);
    manifestContent.Should().Contain("b.txt");
  }


}