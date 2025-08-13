using FluentAssertions;

public class UtilitiesTests
{
  [Theory]
  [InlineData("short.txt", 40, "short.txt")]
  [InlineData("C:\\folder\\subfolder\\file.txt", 40, "C:\\folder\\subfolder\\file.txt")]
  [InlineData("C:\\verylongfoldername\\subfolder\\file.txt", 20, "C:\\...\\file.txt")]
  [InlineData("C:\\folder\\subfolder\\verylongfilename.txt", 20, "C:\\...\\verylongfilename.txt")]
  [InlineData("C:\\folder\\subfolder\\file.txt", 10, "C:\\...")]
  public void AbbreviatePathForDisplay_TruncatesCorrectly(string path, int maxLength, string expectedStart)
  {
    var result = Utilities.AbbreviatePathForDisplay(path, maxLength);
    result.Should().StartWith(expectedStart[..Math.Min(result.Length, expectedStart.Length)]);
    result.Length.Should().BeLessThanOrEqualTo(maxLength);
  }

  [Fact]
  public void AbbreviatePathForDisplay_EmptyOrNull_ReturnsInput()
  {
    Utilities.AbbreviatePathForDisplay("", 40).Should().Be("");
    Utilities.AbbreviatePathForDisplay(null, 40).Should().Be(null);
  }

  [Fact]
  public void AbbreviatePathForDisplay_MaxLengthEdge()
  {
    var path = new string('a', 100);
    var result = Utilities.AbbreviatePathForDisplay(path, 10);
    result?.Length.Should().BeLessThanOrEqualTo(10);
  }

  [Fact]
  public void AbbreviatePathForDisplay_UnicodePath()
  {
    var path = "C:\\файл\\длинноеимяфайла.txt";
    var result = Utilities.AbbreviatePathForDisplay(path, 15);
    result?.Length.Should().BeLessThanOrEqualTo(15);
  }

  [Fact]
  public void AbbreviatePathForDisplay_WhitespacePath()
  {
    var path = "   ";
    var result = Utilities.AbbreviatePathForDisplay(path, 2);
    result.Should().Be("");
  }

  [Fact]
  public void AbbreviateAndPadPathForDisplay_PadsAndEscapes()
  {
    var path = "C:\\folder\\file.txt";
    var result = Utilities.AbbreviateAndPadPathForDisplay(path, 50);
    result.Should().Contain("▪");
    result.Should().Contain("C:\\folder\\file.txt");
    result.Should().NotContain("["); // Should be escaped
  }

  [Fact]
  public void AbbreviateAndPadPathForDisplay_ExactLength()
  {
    var path = new string('a', 50);
    var result = Utilities.AbbreviateAndPadPathForDisplay(path, 50);
    result.Should().NotContain("▪");
    result.Length.Should().Be(50);
  }

  [Fact]
  public void BuildHeaderPanel_ReturnsPanelWithExpectedContent()
  {
    var title = "Test Title";
    var startTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    var manifestName = "manifest.txt";
    var algorithm = "SHA256";
    var root = "C:\\root";
    var includeGlobs = new[] { "*.txt", "*.md" };
    var excludeGlobs = new[] { "*.log" };
    var panel = Utilities.BuildHeaderPanel(title, startTime, manifestName, algorithm, root, includeGlobs, excludeGlobs);
    panel.Should().NotBeNull();
    panel.Header.Should().NotBeNull();
    panel.Header.Text.Should().Contain(title);
    panel.ToString().Should().Contain("manifest.txt");
    panel.ToString().Should().Contain("SHA256");
    panel.ToString().Should().Contain("C:\\root");
    panel.ToString().Should().Contain("*.txt");
    panel.ToString().Should().Contain("*.log");
  }
}