using FluentAssertions;

using System.Security.Cryptography;

public class AddCommandTests(VerityTestFixture fixture) : IClassFixture<VerityTestFixture>
{
  readonly VerityTestFixture fixture = fixture;

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
    await fixture.RunVerity("create manifest.md5");
    var result = await fixture.RunVerity("add manifest.md5");
    result.ExitCode.Should().Be(1);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
  }

  [Fact]
  public async Task Add_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create manifest.md5");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.txt", "extra");
    var result = await fixture.RunVerity("add manifest.md5 --include \"*.log\"");
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetManifestPath("md5");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.log");
    manifestContent.Should().NotContain("c.txt");
  }
}