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
    Assert.Equal(expected, result);
  }

  [Fact]
  public void NormalizeGlobs_VeryLongString_DoesNotThrow()
  {
    var input = new string('a', 10000) + ";*.txt";
    var result = GlobUtils.NormalizeGlobs(input);
    Assert.Contains("*.txt", result);
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
    Assert.Equal(expected, result);
  }

  [Fact]
  public void IsMatch_CaseInsensitive()
  {
    var result = GlobUtils.IsMatch("FILE.TXT", ["*.txt"], null);
    Assert.True(result);
  }

  [Fact]
  public void IsMatch_NullOrEmptyPatterns_DefaultsToIncludeAll()
  {
    Assert.True(GlobUtils.IsMatch("any.file", null, null));
    Assert.True(GlobUtils.IsMatch("any.file", [], null));
  }

  [Fact]
  public void IsMatch_InvalidGlobPattern_DoesNotThrow()
  {
    var result = GlobUtils.IsMatch("file.txt", ["[invalid"], null);
    Assert.False(result);
  }

  [Fact]
  public void FilterFiles_IncludeOnly_ReturnsMatchingFiles()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log", Path.Combine("subdir", "d.txt"));
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt", "subdir/*.txt"], null);
    Assert.Equal(new[] { "a.txt", Path.Combine("subdir", "d.txt") }.OrderBy(x => x), result.OrderBy(x => x));
  }

  [Fact]
  public void FilterFiles_ExcludeOnly_RemovesExcludedFiles()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log");
    var result = GlobUtils.FilterFiles(files, tempDir, null, ["*.md", "*.log"]);
    Assert.Equal(new[] { "a.txt" }.OrderBy(x => x), result.OrderBy(x => x));
  }

  [Fact]
  public void FilterFiles_IncludeAndExclude_FiltersCorrectly()
  {
    var files = CreateFiles("a.txt", "b.md", "c.log", "d.txt");
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt", "*.md"], ["a.txt"]);
    Assert.Equal(new[] { "b.md", "d.txt" }.OrderBy(x => x), result.OrderBy(x => x));
  }

  [Fact]
  public void FilterFiles_NoGlobs_ReturnsAllFiles()
  {
    var files = CreateFiles("a.txt", "b.md");
    var result = GlobUtils.FilterFiles(files, tempDir, null, null);
    Assert.Equal(new[] { "a.txt", "b.md" }.OrderBy(x => x), result.OrderBy(x => x));
  }

  [Fact]
  public void FilterFiles_NoFiles_ReturnsEmpty()
  {
    var files = Array.Empty<string>();
    var result = GlobUtils.FilterFiles(files, tempDir, ["*.txt"], null);
    Assert.Empty(result);
  }

  [Fact]
  public void FilterFiles_AllFilesExcluded_ReturnsEmpty()
  {
    var files = CreateFiles("a.txt", "b.md");
    var result = GlobUtils.FilterFiles(files, tempDir, null, ["**/*"]);
    Assert.Empty(result);
  }
}