
public class ManifestWriter(FileInfo outputFile) : IDisposable
{
  private readonly StreamWriter _writer = new(outputFile.FullName, false, System.Text.Encoding.UTF8, 4096);
  private readonly Lock _writeLock = new();

  public async Task WriteEntryAsync(string hash, string relativePath)
  {
    lock (_writeLock) {
      // Await inside lock is not ideal, but necessary for thread safety with StreamWriter
      _writer.WriteLine($"{hash}\t{relativePath}");
    }
    await Task.CompletedTask;
  }

  public void Dispose()
  {
    _writer?.Dispose();
    GC.SuppressFinalize(this);
  }
}
