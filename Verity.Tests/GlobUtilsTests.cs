using Xunit;
using FluentAssertions;

public class GlobUtilsTests
{
    [Theory]
    [InlineData(null, false, new[] { "**/*" })]
    [InlineData("", false, new[] { "**/*" })]
    [InlineData("   ", false, new[] { "**/*" })]
    [InlineData(null, true, new string[] { })]
    [InlineData("", true, new string[] { })]
    [InlineData("*.cs;*.md", false, new[] { "*.cs", "*.md" })]
    [InlineData(" *.cs ; *.md ", false, new[] { "*.cs", "*.md" })]
    public void NormalizeGlobs_ReturnsExpected(string? input, bool isExclude, string[] expected)
    {
        var result = GlobUtils.NormalizeGlobs(input, isExclude);
        result.Should().Equal(expected);
    }

    [Fact]
    public void NormalizeGlobs_VeryLongString_DoesNotThrow()
    {
        var input = new string('a', 10000) + ";*.txt";
        var result = GlobUtils.NormalizeGlobs(input);
        result.Should().Contain("*.txt");
    }

    [Theory]
    [InlineData("file.txt", new[] { "*.txt" }, null, true)]
    [InlineData("file.txt", new[] { "*.md" }, null, false)]
    [InlineData("file.txt", new[] { "*.txt" }, new[] { "*.txt" }, false)]
    [InlineData("file.txt", new[] { "**/*" }, new[] { "*.txt" }, false)]
    [InlineData("folder/file.txt", new[] { "**/*.txt" }, null, true)]
    [InlineData("folder/file.txt", new[] { "folder/*.txt" }, null, true)]
    [InlineData("folder/file.txt", new[] { "folder/*.md" }, null, false)]
    public void IsMatch_BasicCases(string relativePath, string[] include, string[]? exclude, bool expected)
    {
        var result = GlobUtils.IsMatch(relativePath, include, exclude);
        result.Should().Be(expected);
    }

    [Fact]
    public void IsMatch_CaseInsensitive()
    {
        var result = GlobUtils.IsMatch("FILE.TXT", ["*.txt"], null);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsMatch_NullOrEmptyPatterns_DefaultsToIncludeAll()
    {
        GlobUtils.IsMatch("any.file", null, null).Should().BeTrue();
        GlobUtils.IsMatch("any.file", [], null).Should().BeTrue();
    }

    [Fact]
    public void IsMatch_InvalidGlobPattern_DoesNotThrow()
    {
        var result = GlobUtils.IsMatch("file.txt", ["[invalid"], null);
        result.Should().BeFalse();
    }
}