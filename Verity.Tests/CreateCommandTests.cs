using FluentAssertions;

public class CreateCommandTests(VerityTestFixture fixture) : IClassFixture<VerityTestFixture>
{
  readonly VerityTestFixture fixture = fixture;

  [Fact]
  public async Task Create_BasicCreation()
  {
    fixture.CreateTestFile("a.txt", "hello");
    var result = await fixture.RunVerity("create");
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetFullPath("manifest.txt");
    File.Exists(manifestPath).Should().BeTrue();
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
  }

  [Fact]
  public async Task Create_EmptyDirectory()
  {
    var result = await fixture.RunVerity("create");
    result.ExitCode.Should().Be(1);
    var manifestPath = fixture.GetFullPath("manifest.txt");
    File.Exists(manifestPath).Should().BeTrue();
    File.ReadAllText(manifestPath).Should().BeEmpty();
  }

  [Fact]
  public async Task Create_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.tmp", "tmp");
    var result = await fixture.RunVerity("create --include \"*.txt;*.log\" --exclude \"*.tmp\"");
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetFullPath("manifest.txt");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.log");
    manifestContent.Should().NotContain("c.tmp");
  }
}