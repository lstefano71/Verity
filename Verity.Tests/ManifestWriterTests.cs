using FluentAssertions;

public class ManifestWriterTests : IDisposable
{
  private readonly string tempDir;
  private readonly string manifestPath;

  public ManifestWriterTests()
  {
    tempDir = Path.Combine(Path.GetTempPath(), "ManifestWriterTest_" + Guid.NewGuid());
    Directory.CreateDirectory(tempDir);
    manifestPath = Path.Combine(tempDir, "manifest.txt");
  }

  public void Dispose()
  {
    if (Directory.Exists(tempDir))
      Directory.Delete(tempDir, true);
    GC.SuppressFinalize(this);
  }

  private string[] ReadManifestLines()
  {
    return File.Exists(manifestPath) ? File.ReadAllLines(manifestPath) : [];
  }

  [Fact]
  public async Task WriteEntryAsync_WritesSingleEntry()
  {
    using var writer = new ManifestWriter(new FileInfo(manifestPath));
    await writer.WriteEntryAsync("hash1", "file1.txt");
    writer.Dispose();
    var lines = ReadManifestLines();
    lines.Should().ContainSingle().Which.Should().Be("hash1\tfile1.txt");
  }

  [Fact]
  public async Task WriteAllEntriesAsync_WritesMultipleEntries()
  {
    var entries = new[] {
            ("hash1", "file1.txt"),
            ("hash2", "file2.txt")
        };
    using var writer = new ManifestWriter(new FileInfo(manifestPath));
    await writer.WriteAllEntriesAsync(entries);
    writer.Dispose();
    var lines = ReadManifestLines();
    lines.Should().HaveCount(2);
    lines[0].Should().Be("hash1\tfile1.txt");
    lines[1].Should().Be("hash2\tfile2.txt");
  }

  [Fact]
  public async Task WriteEntryAsync_MultipleCalls_AppendsEntries()
  {
    using var writer = new ManifestWriter(new FileInfo(manifestPath));
    await writer.WriteEntryAsync("hash1", "file1.txt");
    await writer.WriteEntryAsync("hash2", "file2.txt");
    writer.Dispose();
    var lines = ReadManifestLines();
    lines.Should().HaveCount(2);
    lines[0].Should().Be("hash1\tfile1.txt");
    lines[1].Should().Be("hash2\tfile2.txt");
  }
}
