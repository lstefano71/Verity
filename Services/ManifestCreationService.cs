using Spectre.Console;

using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

public class ManifestFileStartedEventArgs(string filePath, string relativePath, long fileSize, object? bag) : EventArgs
{
  public string FilePath { get; } = filePath;
  public string RelativePath { get; } = relativePath;
  public long FileSize { get; } = fileSize;
  public object? Bag { get; } = bag;
}

public class ManifestFileProgressEventArgs(string filePath, string relativePath, long bytesRead, long bytesJustRead, long fileSize, object? bag) : EventArgs
{
  public string FilePath { get; } = filePath;
  public string RelativePath { get; } = relativePath;
  public long BytesRead { get; } = bytesRead;
  public long BytesJustRead { get; } = bytesJustRead;
  public long FileSize { get; } = fileSize;
  public object? Bag { get; } = bag;
}

public class ManifestFileCompletedEventArgs(string filePath, string relativePath, string hash, object? bag) : EventArgs
{
  public string FilePath { get; } = filePath;
  public string RelativePath { get; } = relativePath;
  public string Hash { get; } = hash;
  public object? Bag { get; } = bag;
}

public class ManifestCreationService
{
  public event EventHandler<ManifestFileStartedEventArgs> FileStarted;
  public event EventHandler<ManifestFileProgressEventArgs> FileProgress;
  public event EventHandler<ManifestFileCompletedEventArgs> FileCompleted;

  public async Task<int> CreateManifestAsync(FileInfo outputManifest, DirectoryInfo root, string algorithm, int threads, CancellationToken cancellationToken, ProgressContext? ctx = null)
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
                  ProgressTask? progressTask = null;
                  if (ctx is not null) {
                    int padLen = 50;
                    var safeRelPath = PathUtils.AbbreviateAndPadPathForDisplay(relPath, padLen);
                    progressTask = ctx.AddTask(safeRelPath, maxValue: fileSize > 0 ? fileSize : 1);
                  }
                  FileStarted?.Invoke(this, new ManifestFileStartedEventArgs(file, relPath, fileSize, (object?)progressTask));
                  int bufferSize = FileIOUtils.GetOptimalBufferSize(fileSize);
                  long bytesReadTotal = 0;
                  string hash = string.Empty;
                  using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                    using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    try {
                      int bytesRead;
                      while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                        hasher.AppendData(buffer, 0, bytesRead);
                        bytesReadTotal += bytesRead;
                        FileProgress?.Invoke(this, new ManifestFileProgressEventArgs(file, relPath, bytesReadTotal, bytesRead, fileSize, (object?)progressTask));
                      }
                      hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    } finally {
                      ArrayPool<byte>.Shared.Return(buffer);
                    }
                  }
                  await manifestWriter.WriteEntryAsync(hash, relPath);
                  FileCompleted?.Invoke(this, new ManifestFileCompletedEventArgs(file, relPath, hash, (object?)progressTask));
                }
              }
            }, cancellationToken)));
    return 0;
  }

  public async Task<int> AddToManifestAsync(FileInfo manifestFile, DirectoryInfo root, string algorithm, List<string> filesToAdd, int threads, CancellationToken cancellationToken, ProgressContext? ctx = null)
  {
    if (filesToAdd == null || filesToAdd.Count == 0) return 0;
    using var manifestWriter = new ManifestWriter(manifestFile);
    long totalBytesRead = 0;
    var progressLock = new object();
    await Task.WhenAll(
        Partitioner.Create(filesToAdd).GetPartitions(threads)
            .Select(partition => Task.Run(async () => {
              using (partition) {
                while (partition.MoveNext()) {
                  cancellationToken.ThrowIfCancellationRequested();
                  var relPath = partition.Current;
                  var file = Path.Combine(root.FullName, relPath);
                  var fileSize = new FileInfo(file).Length;
                  ProgressTask? progressTask = null;
                  if (ctx is not null) {
                    int padLen = 50;
                    var safeRelPath = PathUtils.AbbreviateAndPadPathForDisplay(relPath, padLen);
                    progressTask = ctx.AddTask(safeRelPath, maxValue: fileSize > 0 ? fileSize : 1);
                  }
                  FileStarted?.Invoke(this, new ManifestFileStartedEventArgs(file, relPath, fileSize, (object?)progressTask));
                  int bufferSize = FileIOUtils.GetOptimalBufferSize(fileSize);
                  long bytesReadTotal = 0;
                  string hash = string.Empty;
                  using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                    using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    try {
                      int bytesRead;
                      while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                        hasher.AppendData(buffer, 0, bytesRead);
                        bytesReadTotal += bytesRead;
                        FileProgress?.Invoke(this, new ManifestFileProgressEventArgs(file, relPath, bytesReadTotal, bytesRead, fileSize, (object?)progressTask));
                      }
                      hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    } finally {
                      ArrayPool<byte>.Shared.Return(buffer);
                    }
                  }
                  await manifestWriter.WriteEntryAsync(hash, relPath);
                  FileCompleted?.Invoke(this, new ManifestFileCompletedEventArgs(file, relPath, hash, (object?)progressTask));
                }
              }
            }, cancellationToken)));
    return 0;
  }
}
