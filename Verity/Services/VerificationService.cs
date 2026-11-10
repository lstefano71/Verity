using Spectre.Console;

using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;

using Verity.Utilities;

public class FileStartedEventArgs(ChecksumEntry entry, string fullPath, long fileSize, object? bag) : EventArgs
{
  public ChecksumEntry Entry { get; } = entry;
  public string FullPath { get; } = fullPath;
  public long FileSize { get; } = fileSize;
  public object? Bag { get; set; } = bag;
}

public class FileProgressEventArgs(ChecksumEntry entry, string fullPath, long bytesRead, long fileSize, object? bag) : EventArgs
{
  public ChecksumEntry Entry { get; } = entry;
  public string FullPath { get; } = fullPath;
  public long BytesRead { get; } = bytesRead;
  public long FileSize { get; } = fileSize;
  public object? Bag { get; } = bag;
}

public class FileCompletedEventArgs(VerificationResult result, object? bag) : EventArgs
{
  public VerificationResult Result { get; } = result;
  public object? Bag { get; } = bag;
}

public class VerificationService
{
  /// <summary>
  /// Optional VSS path resolver for accessing files through VSS snapshots.
  /// </summary>
  public VssPathResolver? VssResolver { get; set; }

  public event EventHandler<FileStartedEventArgs>? FileStarted;
  public event EventHandler<FileProgressEventArgs>? FileProgress;
  public event EventHandler<FileCompletedEventArgs>? FileCompleted;
  public event Action<string>? FileFoundNotInChecksumList;

  public async Task<FinalSummary> VerifyChecksumsAsync(
      CliOptions options,
      IReadOnlyList<ManifestEntry> manifestEntries,
      int parallelism = -1,
      CancellationToken cancellationToken = default)
  {
    if (parallelism <= 0) parallelism = Environment.ProcessorCount;
    var rootPath = options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!;
    var checksumFileTimestamp = options.ChecksumFile.LastWriteTimeUtc;
    int totalFiles = manifestEntries.Count;

    var jobChannel = Channel.CreateBounded<VerificationJob>(parallelism * 2);
    var resultChannel = Channel.CreateUnbounded<VerificationResult>();

    var allListedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    long totalBytesRead = 0;

    var problematicResults = new List<VerificationResult>();
    // var unlistedFiles = new List<string>(); // REMOVE

    var producer = Task.Run(async () => {
      foreach (var entry in manifestEntries) {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(entry.RelativePath!, rootPath);
        allListedFiles.TryAdd(fullPath, 0);

        long fileSize = -1;
        try {
          // Use VSS path if resolver is available
          var effectivePath = VssResolver?.ResolvePath(fullPath) ?? fullPath;
          fileSize = new FileInfo(effectivePath).Length;
        } catch (Exception) {
          // Will be handled by the consumer as a file not found error.
        }

        await jobChannel.Writer.WriteAsync(new VerificationJob(new ChecksumEntry(entry.Hash!, entry.RelativePath!), fileSize), cancellationToken);
      }
      jobChannel.Writer.Complete();
    }, cancellationToken);

    var consumers = Enumerable.Range(0, parallelism)
        .Select(_ => Task.Run(async () => {
          using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(options.Algorithm));

          await foreach (var job in jobChannel.Reader.ReadAllAsync(cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(rootPath, job.Entry.RelativePath);
                    
            // Use VSS path if resolver is available and path is on the snapshot volume
            var effectivePath = VssResolver?.ResolvePath(fullPath) ?? fullPath;
                    
            VerificationResult result;
            var evt = new FileStartedEventArgs(job.Entry, fullPath, job.FileSize, null);
            FileStarted?.Invoke(this, evt);
            var taskInfo = evt.Bag;

            if (job.FileSize < 0) {
              result = new(job.Entry, ResultStatus.Error, Details: "File not found.", FullPath: fullPath);
            } else {
              try {
                int bufferSize = FileIOUtils.GetOptimalBufferSize(job.FileSize);
                string actualHash;
                long bytesReadTotal = 0;
                using (var stream = new FileStream(effectivePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                  byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                  try {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                      hasher.AppendData(buffer, 0, bytesRead);
                      bytesReadTotal += bytesRead;
                      FileProgress?.Invoke(this, new FileProgressEventArgs(job.Entry, fullPath, bytesReadTotal, job.FileSize, taskInfo));
                    }
                    actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                  } finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                  }
                }

                Interlocked.Add(ref totalBytesRead, job.FileSize);
                if (string.Equals(actualHash, job.Entry.ExpectedHash, StringComparison.OrdinalIgnoreCase)) {
                  result = new(job.Entry, ResultStatus.Success, FullPath: fullPath);
                } else if (new FileInfo(effectivePath).LastWriteTimeUtc > checksumFileTimestamp) {
                  result = new(job.Entry, ResultStatus.Warning, actualHash, "Checksum mismatch (file is newer).", fullPath);
                } else {
                  result = new(job.Entry, ResultStatus.Error, actualHash, "Checksum mismatch.", fullPath);
                }
              } catch (Exception ex) {
                result = new(job.Entry, ResultStatus.Warning, Details: "Cannot read file",
                  FullPath: fullPath, Exception: ex);
              }
            }
            FileCompleted?.Invoke(this, new FileCompletedEventArgs(result, taskInfo));
            await resultChannel.Writer.WriteAsync(result, cancellationToken);

            if (result.Status != ResultStatus.Success) {
              problematicResults.Add(result);
            }
          }
        }, cancellationToken)).ToArray();

    await Task.WhenAll(consumers);
    resultChannel.Writer.Complete();

    int success = 0, warnings = 0, errors = 0;

    await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken)) {
      cancellationToken.ThrowIfCancellationRequested();
      switch (result.Status) {
        case ResultStatus.Success: success++; break;
        case ResultStatus.Warning: warnings++; break;
        case ResultStatus.Error: errors++; break;
      }
    }

    var manifestRelPaths = new HashSet<string>(manifestEntries.Select(e => e.RelativePath!), StringComparer.OrdinalIgnoreCase);
    var allFilesInRoot = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
    var filteredFiles = GlobUtils.FilterFiles(allFilesInRoot, rootPath, options.IncludeGlobs, options.ExcludeGlobs);
    foreach (var relFile in filteredFiles) {
      var absFile = Path.Combine(rootPath, relFile);
      cancellationToken.ThrowIfCancellationRequested();
      if (!manifestRelPaths.Contains(relFile) && !absFile.Equals(options.ChecksumFile.FullName, StringComparison.OrdinalIgnoreCase)) {
        warnings++;
        FileFoundNotInChecksumList?.Invoke(absFile);
        // Instead of unlistedFiles.Add(absFile), add to problematicResults:
        var entry = new ChecksumEntry("", relFile);
        problematicResults.Add(new VerificationResult(
            entry,
            ResultStatus.Warning,
            ActualHash: null,
            Details: "File exists but not in checksum list.",
            FullPath: absFile
        ));
      }
    }

    await producer;

    return new FinalSummary(
        totalFiles,
        success,
        warnings,
        errors,
        totalBytesRead,
        problematicResults
    // REMOVE unlistedFiles
    );
  }
}
public static class StatusClassifier
{
  public static ResultStatus Classify(string? expectedHash, string? actualHash, DateTime fileWriteTime, DateTime manifestWriteTime)
  {
    if (string.IsNullOrWhiteSpace(expectedHash) || string.IsNullOrWhiteSpace(actualHash))
      return ResultStatus.Error;
    if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
      return ResultStatus.Success;
    if (fileWriteTime > manifestWriteTime)
      return ResultStatus.Warning;
    return ResultStatus.Error;
  }
}
