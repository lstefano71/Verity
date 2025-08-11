using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

public class ManifestFileStartedEventArgs : EventArgs
{
  public string FilePath { get; }
  public string RelativePath { get; }
  public long FileSize { get; }
  public Dictionary<string, object> Bag { get; }
  public ManifestFileStartedEventArgs(string filePath, string relativePath, long fileSize, Dictionary<string, object> bag)
  {
    FilePath = filePath;
    RelativePath = relativePath;
    FileSize = fileSize;
    Bag = bag;
  }
}

public class ManifestFileProgressEventArgs : EventArgs
{
  public string FilePath { get; }
  public string RelativePath { get; }
  public long BytesRead { get; }
  public long BytesJustRead { get; }
  public long FileSize { get; }
  public Dictionary<string, object> Bag { get; }
  public ManifestFileProgressEventArgs(string filePath, string relativePath, long bytesRead, long bytesJustRead, long fileSize, Dictionary<string, object> bag)
  {
    FilePath = filePath;
    RelativePath = relativePath;
    BytesRead = bytesRead;
    BytesJustRead = bytesJustRead;
    FileSize = fileSize;
    Bag = bag;
  }
}

public class ManifestFileCompletedEventArgs : EventArgs
{
  public string FilePath { get; }
  public string RelativePath { get; }
  public string Hash { get; }
  public Dictionary<string, object> Bag { get; }
  public ManifestFileCompletedEventArgs(string filePath, string relativePath, string hash, Dictionary<string, object> bag)
  {
    FilePath = filePath;
    RelativePath = relativePath;
    Hash = hash;
    Bag = bag;
  }
}

public class ManifestCreationService
{
  public event EventHandler<ManifestFileStartedEventArgs> FileStarted;
  public event EventHandler<ManifestFileProgressEventArgs> FileProgress;
  public event EventHandler<ManifestFileCompletedEventArgs> FileCompleted;

  public async Task<int> CreateManifestAsync(FileInfo outputManifest, DirectoryInfo root, string algorithm, int threads, CancellationToken cancellationToken)
  {
    var files = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories);
    if (files.Length == 0) return 1;
    using var manifestWriter = new ManifestWriter(outputManifest);
    long totalBytesRead = 0;
    var progressLock = new object();
    await Task.WhenAll(
        Partitioner.Create(files).GetPartitions(threads)
            .Select(partition => Task.Run(async () => {
              using (partition) {
                while (partition.MoveNext()) {
                  cancellationToken.ThrowIfCancellationRequested();
                  var file = partition.Current;
                  var relPath = Path.GetRelativePath(root.FullName, file);
                  var fileSize = new FileInfo(file).Length;
                  var bag = new Dictionary<string, object>();
                  FileStarted?.Invoke(this, new ManifestFileStartedEventArgs(file, relPath, fileSize, bag));
                  int bufferSize = FileIOUtils.GetOptimalBufferSize(fileSize);
                  using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
                  using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm.Trim().ToUpperInvariant()));
                  byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                  long bytesReadTotal = 0;
                  try {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, bufferSize, cancellationToken)) > 0) {
                      hasher.AppendData(buffer, 0, bytesRead);
                      bytesReadTotal += bytesRead;
                      FileProgress?.Invoke(this, new ManifestFileProgressEventArgs(file, relPath, bytesReadTotal, bytesRead, fileSize, bag));
                    }
                    var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    await manifestWriter.WriteEntryAsync(hash, relPath);
                    FileCompleted?.Invoke(this, new ManifestFileCompletedEventArgs(file, relPath, hash, bag));
                    lock (progressLock) {
                      totalBytesRead += fileSize;
                    }
                  } finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                  }
                }
              }
            }, cancellationToken))
    );
    return 0;
  }
}
