using FluentAssertions;

public class AbbreviatePathForDisplayTests
{
  [Theory]
  [InlineData(null, 40, null)]
  [InlineData("   ", 40, null)]
  public void AbbreviatePath_HandlesNullAndWhitespace(string? path, int maxLength, string? expected)
  {
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expected);
  }

  [Theory]
  [InlineData("short.txt", 40, "short.txt")]
  [InlineData("C:\\folder\\file.txt", 40, "C:\\folder\\file.txt")]
  [InlineData("  C:\\folder\\file.txt  ", 40, "C:\\folder\\file.txt")]
  public void AbbreviatePath_WhenPathIsShorterThanMaxLength_ReturnsTrimmedOriginal(string path, int maxLength, string expected)
  {
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expected);
  }

  [Theory]
  [InlineData("this-is-a-very-long-filename-that-will-not-fit.txt", 20, "...t-will-not-fit.txt")]
  [InlineData("C:\\folder\\another-very-long-filename-that-will-not-fit.txt", 30, "...lename-that-will-not-fit.txt")]
  public void AbbreviatePath_WhenFilenameIsTooLong_AbbreviatesFilename(string path, int maxLength, string expected)
  {
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expected);
    result?.Length.Should().BeLessThanOrEqualTo(maxLength);
  }

  [Theory]
  [InlineData("C:\\Users\\MyUsername\\Documents\\VeryImportantProject\\SourceCode\\main.cs", 40, "C:\\...\\VeryImportantProject\\SourceCode\\main.cs")]
  [InlineData("C:\\Users\\MyUsername\\Documents\\VeryImportantProject\\SourceCode\\main.cs", 30, "C:\\...\\SourceCode\\main.cs")]
  [InlineData("C:\\Users\\MyUsername\\Documents\\VeryImportantProject\\SourceCode\\main.cs", 20, "C:\\...\\main.cs")]
  public void AbbreviatePath_ForAbsolutePaths_CompactsMiddle(string path, int maxLength, string expected)
  {
    string expectedNormalized = expected.Replace('\\', Path.DirectorySeparatorChar);
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expectedNormalized);
    result?.Length.Should().BeLessThanOrEqualTo(maxLength);
  }

  [Theory]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 32, ".../subfolder/another/file.txt")]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 25, ".../another/file.txt")]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 15, ".../file.txt")]
  public void AbbreviatePath_ForRelativePaths_CompactsMiddle(string path, int maxLength, string expected)
  {
    string pathNormalized = path.Replace('/', Path.DirectorySeparatorChar);
    string expectedNormalized = expected.Replace('/', Path.DirectorySeparatorChar);
    var result = Utilities.AbbreviatePathForDisplay(pathNormalized, maxLength);
    result.Should().Be(expectedNormalized);
    result?.Length.Should().BeLessThanOrEqualTo(maxLength);
  }

  [Theory]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 30, "a/b/c/d/e/f/g/h/.../p/file.txt")]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 20, "a/b/.../p/file.txt")]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 15, ".../p/file.txt")]
  [InlineData("\\\\very-long-server-name\\very-long-share-name\\folderA\\folderB\\file.log", 50, "\\\\very-long-server-name\\...\\folderB\\file.log")]
  [InlineData("\\\\server\\share\\a\\b\\c\\d\\e\\file.txt", 25, "\\\\server\\share\\...\\e\\file.txt")]
  [InlineData("\\\\server\\share\\a\\b\\c\\d\\e\\file.txt", 18, "\\\\server\\share\\...\\file.txt")]
  public void AbbreviatePath_ForUncPaths_CompactsMiddleAndPreservesRoot(string path, int maxLength, string expected)
  {
    string expectedNormalized = expected.Replace('\\', Path.DirectorySeparatorChar);
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expectedNormalized);
    result?.Length.Should().BeLessThanOrEqualTo(maxLength);
  }

  [Theory]
  [InlineData("a-long-path-that-will-be-truncated-entirely.txt", 3, "txt")]
  [InlineData("a-long-path-that-will-be-truncated-entirely.txt", 2, "xt")]
  [InlineData("C:\\a\\b\\c\\d.txt", 3, "txt")]
  public void AbbreviatePath_WithVerySmallMaxLength_TruncatesFromLeft(string path, int maxLength, string expected)
  {
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().Be(expected);
    result?.Length.Should().Be(maxLength);
  }
}
