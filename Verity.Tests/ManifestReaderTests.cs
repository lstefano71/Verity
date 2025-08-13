using FluentAssertions;

public class ManifestReaderTests
{
  [Fact]
  public void ParseLine_WithValidLine_ReturnsCorrectEntry()
  {
    var line = "abc123\tfile.txt";
    var entry = ManifestReader.ParseLine(line);
    entry.Should().NotBeNull();
    entry!.Hash.Should().Be("abc123");
    entry.RelativePath.Should().Be("file.txt");
  }

  [Theory]
  [InlineData("abc123 file.txt")]
  [InlineData("abc123")]
  [InlineData("abc123\t")]
  [InlineData("\tfile.txt")]
  public void ParseLine_WithMalformedLine_ReturnsNull(string line)
  {
    ManifestReader.ParseLine(line).Should().BeNull();
  }

  [Theory]
  [InlineData("")]
  [InlineData(" ")]
  [InlineData("\t")]
  public void ParseLine_WithEmptyOrWhitespaceLine_ReturnsNull(string line)
  {
    ManifestReader.ParseLine(line).Should().BeNull();
  }

  [Fact]
  public void ParseLine_WithExtraTabs_ParsesFirstTwoParts()
  {
    var line = "abc123\tfile.txt\tignored\tmore";
    var entry = ManifestReader.ParseLine(line);
    entry.Should().NotBeNull();
    entry!.Hash.Should().Be("abc123");
    entry.RelativePath.Should().Be("file.txt");
  }

  [Fact]
  public void ParseLine_UnicodeAndTabs()
  {
    var line = "юникод\tфайл.txt";
    var entry = ManifestReader.ParseLine(line);
    entry.Should().NotBeNull();
    entry!.Hash.Should().Be("юникод");
    entry.RelativePath.Should().Be("файл.txt");
  }
}