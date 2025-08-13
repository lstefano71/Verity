using Xunit;
using FluentAssertions;
using System.IO;

public class AddCommandTests : IClassFixture<VerityTestFixture>
{
    readonly VerityTestFixture fixture;
    public AddCommandTests(VerityTestFixture fixture) => this.fixture = fixture;

    [Fact]
    public async void Add_NewFiles()
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
    public async void Add_NoNewFiles()
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
    public async void Add_GlobFiltering()
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