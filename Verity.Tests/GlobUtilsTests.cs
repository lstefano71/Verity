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

  [Fact]
  public void FilterFiles_IncludeOnly_ReturnsMatchingFiles()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var subdir = Path.Combine(tempDir, "subdir");
      Directory.CreateDirectory(subdir);
      var files = new[]
      {
        Path.Combine(tempDir, "a.txt"),
        Path.Combine(tempDir, "b.md"),
        Path.Combine(tempDir, "c.log"),
        Path.Combine(subdir, "d.txt"),
      };
      foreach (var file in files)
        File.WriteAllText(file, "test");
      var result = GlobUtils.FilterFiles(files, tempDir, new[] { "*.txt", "subdir/*.txt" }, null);
      result.Should().BeEquivalentTo(["a.txt", Path.Combine("subdir", "d.txt")]);
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FilterFiles_ExcludeOnly_RemovesExcludedFiles()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var files = new[]
      {
        Path.Combine(tempDir, "a.txt"),
        Path.Combine(tempDir, "b.md"),
        Path.Combine(tempDir, "c.log"),
      };
      // Create the files physically
      foreach (var file in files)
        File.WriteAllText(file, "test");

      var result = GlobUtils.FilterFiles(files, tempDir, null, new[] { "*.md", "*.log" });
      result.Should().BeEquivalentTo(["a.txt"]);
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FilterFiles_IncludeAndExclude_FiltersCorrectly()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var files = new[]
      {
        Path.Combine(tempDir, "a.txt"),
        Path.Combine(tempDir, "b.md"),
        Path.Combine(tempDir, "c.log"),
        Path.Combine(tempDir, "d.txt"),
      };
      foreach (var file in files)
        File.WriteAllText(file, "test");
      var result = GlobUtils.FilterFiles(files, tempDir, new[] { "*.txt", "*.md" }, new[] { "a.txt" });
      result.Should().BeEquivalentTo(["b.md", "d.txt"]);
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FilterFiles_NoGlobs_ReturnsAllFiles()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var files = new[]
      {
        Path.Combine(tempDir, "a.txt"),
        Path.Combine(tempDir, "b.md"),
      };
      foreach (var file in files)
        File.WriteAllText(file, "test");
      var result = GlobUtils.FilterFiles(files, tempDir, null, null);
      result.Should().BeEquivalentTo(["a.txt", "b.md"]);
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FilterFiles_NoFiles_ReturnsEmpty()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var files = new string[] { };
      var result = GlobUtils.FilterFiles(files, tempDir, new[] { "*.txt" }, null);
      result.Should().BeEmpty();
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FilterFiles_AllFilesExcluded_ReturnsEmpty()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    try {
      var files = new[]
      {
        Path.Combine(tempDir, "a.txt"),
        Path.Combine(tempDir, "b.md"),
      };
      foreach (var file in files)
        File.WriteAllText(file, "test");
      var result = GlobUtils.FilterFiles(files, tempDir, null, new[] { "**/*" });
      result.Should().BeEmpty();
    } finally {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }
}