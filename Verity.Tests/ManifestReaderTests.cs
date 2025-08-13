using FluentAssertions;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

public class ManifestReaderTests : IDisposable
{
  private readonly string tempDir;

  public ManifestReaderTests()
  {
    tempDir = Path.Combine(Path.GetTempPath(), "ManifestReaderTest_" + Guid.NewGuid());
    Directory.CreateDirectory(tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(tempDir))
      Directory.Delete(tempDir, true);
    GC.SuppressFinalize(this);
  }

  private string CreateManifest(params string[] lines)
  {
    var manifestPath = Path.Combine(tempDir, "manifest.txt");
    File.WriteAllLines(manifestPath, lines);
    return manifestPath;
  }

  private string CreateFile(string relativePath, string content)
  {
    var fullPath = Path.Combine(tempDir, relativePath);
    var dir = Path.GetDirectoryName(fullPath);
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
    File.WriteAllText(fullPath, content);
    return fullPath;
  }

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

  [Fact]
  public async Task ReadEntriesAsync_WithValidManifest_ReturnsEntries()
  {
    var manifestPath = CreateManifest(
      "hash1\tfile1.txt",
      "hash2\tfile2.txt"
    );
    var reader = new ManifestReader(new FileInfo(manifestPath), new DirectoryInfo(tempDir));
    var entries = await reader.ReadEntriesAsync(CancellationToken.None);
    entries.Should().HaveCount(2);
    entries[0]!.Hash.Should().Be("hash1");
    entries[0]!.RelativePath.Should().Be("file1.txt");
    entries[1]!.Hash.Should().Be("hash2");
    entries[1]!.RelativePath.Should().Be("file2.txt");
  }

  [Fact]
  public async Task GetFileCountAsync_ReturnsCorrectCount()
  {
    var manifestPath = CreateManifest(
      "hash1\tfile1.txt",
      "hash2\tfile2.txt"
    );
    var reader = new ManifestReader(new FileInfo(manifestPath), new DirectoryInfo(tempDir));
    var count = await reader.GetFileCountAsync(CancellationToken.None);
    count.Should().Be(2);
  }

  [Fact]
  public async Task GetTotalBytesAsync_ReturnsSumOfFileSizes()
  {
    var manifestPath = CreateManifest(
      "hash1\tfile1.txt",
      "hash2\tfile2.txt"
    );
    CreateFile("file1.txt", "abc"); // 3 bytes
    CreateFile("file2.txt", "defgh"); // 5 bytes
    var reader = new ManifestReader(new FileInfo(manifestPath), new DirectoryInfo(tempDir));
    var totalBytes = await reader.GetTotalBytesAsync(CancellationToken.None);
    totalBytes.Should().Be(8);
  }
}