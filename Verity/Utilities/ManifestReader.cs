public class ManifestEntry
{
  public string? Hash { get; set; }
  public string? RelativePath { get; set; }
}

public class ManifestReader(FileInfo manifestFile, DirectoryInfo? rootDirectory)
{
  public FileInfo ManifestFile { get; } = manifestFile;
  public DirectoryInfo? RootDirectory { get; } = rootDirectory;

  public async Task<IReadOnlyList<ManifestEntry?>> ReadEntriesAsync(CancellationToken cancellationToken)
  {
    var entries = new List<ManifestEntry?>();
    await foreach (var line in File.ReadLinesAsync(ManifestFile.FullName, cancellationToken)) {
      if (string.IsNullOrWhiteSpace(line) || !line.Contains('\t')) continue;
      var parts = line.Split('\t');
      if (parts.Length < 2) continue;
      entries.Add(new ManifestEntry { Hash = parts[0], RelativePath = parts[1] });
    }
    return entries;
  }

  public async Task<int> GetFileCountAsync(CancellationToken cancellationToken)
  {
    var entries = await ReadEntriesAsync(cancellationToken);
    return entries.Count;
  }

  public async Task<long> GetTotalBytesAsync(CancellationToken cancellationToken)
  {
    var entries = await ReadEntriesAsync(cancellationToken);
    long totalBytes = entries.Select(e => {
      var fullPath = RootDirectory != null ? Path.Combine(RootDirectory.FullName, e.RelativePath) : Path.Combine(ManifestFile.DirectoryName, e.RelativePath);
      return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
    }).Sum();
    return totalBytes;
  }
}
