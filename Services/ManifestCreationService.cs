using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

public class ManifestFileStartedEventArgs(string filePath, string relativePath, long fileSize, object? bag) : EventArgs
{
  public string FilePath { get; } = filePath;
  public string RelativePath { get; } = relativePath;
  public long FileSize { get; } = fileSize;
  public object? Bag { get; set; } = bag;
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

  private async Task<FinalSummary> ProcessManifestFilesAsync(ManifestOperationMode mode, 
    FileInfo manifestFile, DirectoryInfo root, 
    string algorithm, IEnumerable<string> files, int threads, CancellationToken cancellationToken)
  {
    if (files == null || !files.Any())
      return new FinalSummary(0, 0, 0, 0, 0, [], []);
    var newEntries = new ConcurrentBag<(string hash, string relativePath)>();
    var problematicResults = new ConcurrentBag<VerificationResult>();
    int success = 0, warnings = 0, errors = 0;
    long totalBytesRead = 0;
    int totalFiles = files.Count();

    await Task.WhenAll(
        Partitioner.Create(files).GetPartitions(threads)
            .Select(partition => Task.Run(async () => {
              using (partition) {
                while (partition.MoveNext()) {
                  cancellationToken.ThrowIfCancellationRequested();
                  var relPath = mode == ManifestOperationMode.Create ? Path.GetRelativePath(root.FullName, partition.Current) : partition.Current;
                  var file = Path.Combine(root.FullName, relPath);
                  long fileSize = 0;
                  try {
                    fileSize = new FileInfo(file).Length;
                  } catch {
                    fileSize = 0;
                  }
                  object? bag = null;
                  var fileStartedArgs = new ManifestFileStartedEventArgs(file, relPath, fileSize, bag);
                  FileStarted?.Invoke(this, fileStartedArgs);
                  bag = fileStartedArgs.Bag;

                  int bufferSize = FileIOUtils.GetOptimalBufferSize(fileSize);
                  long bytesReadTotal = 0;
                  string hash = string.Empty;
                  try {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                      using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
                      byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                      try {
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                          hasher.AppendData(buffer, 0, bytesRead);
                          bytesReadTotal += bytesRead;
                          FileProgress?.Invoke(this, new ManifestFileProgressEventArgs(file, relPath, bytesReadTotal, bytesRead, fileSize, bag));
                        }
                        hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                        Interlocked.Add(ref totalBytesRead, fileSize);
                        success++;
                      } finally {
                        ArrayPool<byte>.Shared.Return(buffer);
                      }
                    }
                    newEntries.Add((hash, relPath));
                    FileCompleted?.Invoke(this, new ManifestFileCompletedEventArgs(file, relPath, hash, bag));
                  } catch (Exception ex) {
                    // File read error
                    errors++;
                    problematicResults.Add(new VerificationResult(
                      new ChecksumEntry("", relPath),
                      ResultStatus.Error,
                      null,
                      $"Cannot read file: {ex.Message}",
                      file
                    ));
                  }
                }
              }
            }, cancellationToken)));

    List<(string hash, string relativePath)> allEntries;
    if (mode == ManifestOperationMode.Add) {
      // Read existing entries and merge
      var reader = new ManifestReader(manifestFile, root);
      var manifestEntries = await reader.ReadEntriesAsync(cancellationToken);
      var existingEntries = manifestEntries
        .Where(entry => entry != null && entry.Hash != null && entry.RelativePath != null)
        .Select(entry => (entry.Hash!, entry.RelativePath!))
        .ToList();
      // Avoid duplicates: only add new entries for files not already present
      var existingPaths = new HashSet<string>(existingEntries.Select(e => e.Item2), StringComparer.OrdinalIgnoreCase);
      var filteredNewEntries = newEntries.Where(e => !existingPaths.Contains(e.relativePath)).OrderBy(e => e.relativePath);
      allEntries = [.. existingEntries, .. filteredNewEntries];
    } else {
      allEntries = [.. newEntries.OrderBy(e => e.relativePath)];
    }
    using var manifestWriter = new ManifestWriter(manifestFile);
    await manifestWriter.WriteAllEntriesAsync(allEntries);
    return new FinalSummary(
      totalFiles,
      success,
      warnings,
      errors,
      totalBytesRead,
      [.. problematicResults],
      []
    );
  }

  public async Task<FinalSummary> CreateManifestAsync(FileInfo outputManifest, DirectoryInfo root, string algorithm, IEnumerable<string> files, int threads, CancellationToken cancellationToken)
  {
    var fullPaths = files.Select(f => Path.Combine(root.FullName, f));
    return await ProcessManifestFilesAsync(ManifestOperationMode.Create, outputManifest, root, algorithm, fullPaths, threads, cancellationToken);
  }

  public async Task<FinalSummary> AddToManifestAsync(FileInfo manifestFile, DirectoryInfo root, 
    string algorithm, IReadOnlyCollection<string> filesToAdd, int threads, CancellationToken cancellationToken)
  {
    return await ProcessManifestFilesAsync(ManifestOperationMode.Add, manifestFile, 
      root, algorithm, filesToAdd, threads, cancellationToken);
  }
}
