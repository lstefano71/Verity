using FluentAssertions;

using System.Security.Cryptography;

public class CreateCommandTests : CommandTestBase, IClassFixture<VerityTestFixture>
{
  public CreateCommandTests(VerityTestFixture fixture) : base(fixture) { }

  private string Md5(string input)
  {
    using var md5 = MD5.Create();
    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return string.Concat(hash.Select(b => b.ToString("x2")));
  }

  [Fact]
  public async Task Create_BasicCreation()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    var result = await fixture.RunVerity("create manifest.md5");
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
    result.ExitCode.Should().Be(0);
    File.Exists(manifestPath).Should().BeTrue();
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain(Md5("hello"));
  }

  [Fact]
  public async Task Create_EmptyDirectory_WarningReport()
  {
    var manifestPath = fixture.GetManifestPath("md5");
    if (File.Exists(manifestPath)) File.Delete(manifestPath);
    File.Exists(manifestPath).Should().BeFalse();
    var reportPath = fixture.GetFullPath("report.tsv");
    if (File.Exists(reportPath)) File.Delete(manifestPath);
    File.Exists(manifestPath).Should().BeFalse();
    var manifestDir = Path.GetDirectoryName(manifestPath);
    Directory.Exists(manifestDir).Should().BeTrue();
    Directory.GetFiles(manifestDir).Should().BeEmpty();
    var result = await fixture.RunVerity("create manifest.md5 --tsv-report report.tsv");
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
    result.ExitCode.Should().Be(1);
    File.Exists(manifestPath).Should().BeTrue();
    File.ReadAllText(manifestPath).Should().BeEmpty();
    File.Exists(reportPath).Should().BeFalse();
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
    Console.WriteLine("STDOUT:\n" + result.StdOut);
    Console.WriteLine("STDERR:\n" + result.StdErr);
    result.ExitCode.Should().Be(0);
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.log");
    manifestContent.Should().NotContain("c.tmp");
  }
}