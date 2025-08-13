using Xunit;
using FluentAssertions;
using System.IO;

public class CreateCommandTests : IClassFixture<VerityTestFixture>
{
    readonly VerityTestFixture fixture;
    public CreateCommandTests(VerityTestFixture fixture) => this.fixture = fixture;

    [Fact]
    public async void Create_BasicCreation()
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
    public async void Create_EmptyDirectory()
    {
        var result = await fixture.RunVerity("create");
        result.ExitCode.Should().Be(1);
        var manifestPath = fixture.GetFullPath("manifest.txt");
        File.Exists(manifestPath).Should().BeTrue();
        File.ReadAllText(manifestPath).Should().BeEmpty();
    }

    [Fact]
    public async void Create_GlobFiltering()
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