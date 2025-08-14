public class AbbreviatePathForDisplayTests
{
  // These test cases were already correct.
  [Theory]
  [InlineData(null, 40, null)]
  [InlineData("   ", 40, "")]
  public void AbbreviatePath_HandlesNullAndWhitespace(string? path, int maxLength, string? expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(expected, abbreviated);
  }

  // These test cases were already correct.
  [Theory]
  [InlineData("short.txt", 40, "short.txt")]
  [InlineData("C:\\folder\\file.txt", 40, "C:\\folder\\file.txt")]
  [InlineData("test   ", 40, "test")]
  [InlineData("  C:\\folder\\file.txt  ", 40, "C:\\folder\\file.txt")]
  public void AbbreviatePath_WhenPathIsShorterThanMaxLength_ReturnsTrimmedOriginal(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(expected, abbreviated);
  }

  [Theory]
  [InlineData("this-is-a-very-long-filename-that-will-not-fit.txt",
    20, "this-is-...t-fit.txt")]
  [InlineData(@"C:\folder\another-very-long-filename-that-will-not-fit.txt",
    30, @"C:\folder\ano...ll-not-fit.txt")]
  public void AbbreviatePath_WhenFilenameIsTooLong_AbbreviatesFilename(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }

  [Theory]
  [InlineData(@"C:\Users\MyUsername\Documents\VeryImportantProject\SourceCode\main.cs",
    46, @"C:\Users\MyUsername\D...ect\SourceCode\main.cs")]
  [InlineData(@"C:\Users\MyUsername\Documents\VeryImportantProject\SourceCode\main.cs",
    30, @"C:\Users\MyUs...ceCode\main.cs")]
  [InlineData(@"C:\Users\MyUsername\Documents\VeryImportantProject\SourceCode\main.cs",
    20, @"C:\Users...e\main.cs")]
  public void AbbreviatePath_ForAbsolutePaths_CompactsMiddle(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }

  [Theory]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 32, "verylongfolder...nother/file.txt")]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 25, "verylongfol...er/file.txt")]
  [InlineData("verylongfoldername/subfolder/another/file.txt", 15, "verylo...le.txt")]
  public void AbbreviatePath_ForRelativePaths_CompactsMiddle(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }

  [Theory]
  [InlineData(@"\\very-long-server-name\very-long-share-name\folderA\folderB\file.log",
    50, @"\\very-long-server-name...folderA\folderB\file.log")]
  [InlineData(@"\\server\share\a\b\c\d\e\file.txt",
    29, @"\\server\shar...\d\e\file.txt")]
  [InlineData(@"\\server\share\a\b\c\d\e\file.txt",
    27, @"\\server\sha...d\e\file.txt")]
  public void AbbreviatePath_ForUncPaths_CompactsMiddleAndPreservesRoot(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }

  [Theory]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 30, "a/b/c/d/e/f/g...n/o/p/file.txt")]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 20, "a/b/c/d/.../file.txt")]
  [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/file.txt", 15, "a/b/c/...le.txt")]
  [InlineData(@"a\b\c\d\e\f\g\h\i\j\k\l\m\n\o\p\file.txt", 15, @"a\b\c\...le.txt")]
  public void AbbreviatePath_CompactsMiddle(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }

  // These test cases were already correct.
  [Theory]
  [InlineData("a-long-path-that-will-be-truncated-entirely.txt", 3, "txt")]
  [InlineData("a-long-path-that-will-be-truncated-entirely.txt", 2, "xt")]
  [InlineData(@"C:\a\b\c\d.txt", 3, "txt")]
  public void AbbreviatePath_WithVerySmallMaxLength_TruncatesFromLeft(string path, int maxLength, string expected)
  {
    var abbreviated = Utilities.AbbreviatePathForDisplay(path, maxLength);
    Assert.Equal(maxLength, expected?.Length);
    Assert.Equal(expected, abbreviated);
  }
}