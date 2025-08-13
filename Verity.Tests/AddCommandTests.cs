using FluentAssertions;

public class AddCommandTests(VerityTestFixture fixture) : IClassFixture<VerityTestFixture>
{
  readonly VerityTestFixture fixture = fixture;

  [Fact]
  public async Task Add_NewFiles()
  {
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create");
    fixture.CreateTestFile("b.txt", "world");
    var result = await fixture.RunVerity("add");
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetFullPath("manifest.txt");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.txt");
  }

  [Fact]
  public async Task Add_NoNewFiles()
  {
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create");
    var result = await fixture.RunVerity("add");
    result.ExitCode.Should().Be(1);

    var manifestPath = fixture.GetFullPath("manifest.txt");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
  }

  [Fact]
  public async Task Add_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    await fixture.RunVerity("create");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("c.txt", "extra");
    var result = await fixture.RunVerity("add --include \"*.log\"");
    result.ExitCode.Should().Be(0);

    var manifestPath = fixture.GetFullPath("manifest.txt");
    var manifestContent = File.ReadAllText(manifestPath);
    manifestContent.Should().Contain("a.txt");
    manifestContent.Should().Contain("b.log");
    manifestContent.Should().NotContain("c.txt");
  }
}