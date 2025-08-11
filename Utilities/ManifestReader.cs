public class ManifestEntry
{
  public string Hash { get; set; }
  public string RelativePath { get; set; }
}

public class ManifestReader
{
  public FileInfo ManifestFile { get; }
  public DirectoryInfo? RootDirectory { get; }
  public ManifestReader(FileInfo manifestFile, DirectoryInfo? rootDirectory)
  {
    ManifestFile = manifestFile;
    RootDirectory = rootDirectory;
  }

  public async Task<IReadOnlyList<ManifestEntry>> ReadEntriesAsync(CancellationToken cancellationToken)
  {
    var lines = await File.ReadAllLinesAsync(ManifestFile.FullName, cancellationToken);
    var entries = lines
        .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('\t'))
        .Select(line => {
          var parts = line.Split('\t');
          if (parts.Length < 2) return null;
          return new ManifestEntry { Hash = parts[0], RelativePath = parts[1] };
        })
        .Where(e => e != null)
        .ToList();
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
