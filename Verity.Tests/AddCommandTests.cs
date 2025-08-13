using FluentAssertions;

using System.Security.Cryptography;

public class AddCommandTests : CommandTestBase, IClassFixture<VerityTestFixture>
{
  public AddCommandTests(VerityTestFixture fixture) : base(fixture) { }

  private string Md5(string input)
  {
    using var md5 = MD5.Create();
    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return string.Concat(hash.Select(b => b.ToString("x2")));
  }

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
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
    // Exclude the manifest file itself from being added
    result = await fixture.RunVerity($"add {manifestPath} --root . --exclude \"manifests/*\"");
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
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
}