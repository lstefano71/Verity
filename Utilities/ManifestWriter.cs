public class ManifestWriter : IDisposable
{
  private StreamWriter _writer;
  public ManifestWriter(FileInfo outputFile)
  {
    _writer = new StreamWriter(outputFile.FullName, false, System.Text.Encoding.UTF8);
  }

  public async Task WriteEntryAsync(string hash, string relativePath)
  {
    await _writer.WriteLineAsync($"{hash}\t{relativePath}");
  }

  public void Dispose()
  {
    _writer?.Dispose();
  }
}
