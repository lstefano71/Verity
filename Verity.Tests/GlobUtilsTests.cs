using FluentAssertions;

public class GlobUtilsTests : IDisposable
{
  private readonly string tempDir;

  public GlobUtilsTests()
  {
    tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(tempDir))
      Directory.Delete(tempDir, true);
    GC.SuppressFinalize(this);
  }

  private string[] CreateFiles(params string[] relativePaths)
  {
    var fullPaths = relativePaths.Select(p => Path.Combine(tempDir, p)).ToArray();
    foreach (var path in fullPaths) {
      var dir = Path.GetDirectoryName(path);
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
      File.WriteAllText(path, "test");
    }
    return fullPaths;
  }

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

  [Fact]
  public void FilterFiles_IncludeOnly_ReturnsMatchingFiles()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log", Path.Combine("subdir", "d.txt"));
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt", "subdir/*.txt"], null);
    result.Should().BeEquivalentTo(["a.txt", Path.Combine("subdir", "d.txt")]);
  }

  [Fact]
  public void FilterFiles_ExcludeOnly_RemovesExcludedFiles()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log");
    var result = GlobUtils.FilterFiles(files, tempDir, null, ["*.md", "*.log"]);
    result.Should().BeEquivalentTo(["a.txt"]);
  }

  [Fact]
  public void FilterFiles_IncludeAndExclude_FiltersCorrectly()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log", "d.txt");
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt", "*.md"], ["a.txt"]);
    result.Should().BeEquivalentTo(["b.md", "d.txt"]);
  }

  [Fact]
  public void FilterFiles_NoGlobs_ReturnsAllFiles()
  {
    var files = CreateFiles("a.txt", "b.md");
    var result = GlobUtils.FilterFiles(files, tempDir, null, null);
    result.Should().BeEquivalentTo(["a.txt", "b.md"]);
  }

  [Fact]
  public void FilterFiles_NoFiles_ReturnsEmpty()
  {
    var files = Array.Empty<string>();
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt"], null);
    result.Should().BeEmpty();
  }

  [Fact]
  public void FilterFiles_AllFilesExcluded_ReturnsEmpty()
  {
    var files = CreateFiles("a.txt", "b.md");
    var result = GlobUtils.FilterFiles(files, tempDir, null, ["**/*"]);
    result.Should().BeEmpty();
  }
}