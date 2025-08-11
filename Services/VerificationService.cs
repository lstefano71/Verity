using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;

public class FileStartedEventArgs(ChecksumEntry entry, string fullPath, long fileSize, Dictionary<string, object> bag) : EventArgs
{
  public ChecksumEntry Entry { get; } = entry;
  public string FullPath { get; } = fullPath;
  public long FileSize { get; } = fileSize;
  public Dictionary<string, object> Bag { get; } = bag;
}

public class FileProgressEventArgs(ChecksumEntry entry, string fullPath, long bytesRead, long fileSize, Dictionary<string, object> bag) : EventArgs
{
  public ChecksumEntry Entry { get; } = entry;
  public string FullPath { get; } = fullPath;
  public long BytesRead { get; } = bytesRead;
  public long FileSize { get; } = fileSize;
  public Dictionary<string, object> Bag { get; } = bag;
}

public class FileCompletedEventArgs(VerificationResult result, Dictionary<string, object> bag) : EventArgs
{
  public VerificationResult Result { get; } = result;
  public Dictionary<string, object> Bag { get; } = bag;
}

public class VerificationService
{
  public event EventHandler<FileStartedEventArgs> FileStarted;
  public event EventHandler<FileProgressEventArgs> FileProgress;
  public event EventHandler<FileCompletedEventArgs> FileCompleted;
  public event Action<string> FileFoundNotInChecksumList;

  public async Task<FinalSummary> VerifyChecksumsAsync(
      CliOptions options,
      CancellationToken cancellationToken,
      int parallelism = -1)
  {
    if (parallelism <= 0) parallelism = Environment.ProcessorCount;
    var rootPath = options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!;
    var checksumFileTimestamp = options.ChecksumFile.LastWriteTimeUtc;
    var lines = await File.ReadAllLinesAsync(options.ChecksumFile.FullName, cancellationToken);
    var totalFiles = lines.Length;

    var jobChannel = Channel.CreateBounded<VerificationJob>(parallelism * 2);
    var resultChannel = Channel.CreateUnbounded<VerificationResult>();

    var allListedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    long totalBytesRead = 0;

    var producer = Task.Run(async () => {
      foreach (var line in lines) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(line)) continue;
        var parts = line.Split('\t', 2);
        if (parts.Length != 2) continue;

        var entry = new ChecksumEntry(parts[0].ToLowerInvariant(), parts[1]);
        var fullPath = Path.GetFullPath(entry.RelativePath, rootPath);
        allListedFiles.TryAdd(fullPath, 0);

        long fileSize = -1;
        try {
          fileSize = new FileInfo(fullPath).Length;
        } catch (Exception) {
          // Will be handled by the consumer as a file not found error.
        }

        await jobChannel.Writer.WriteAsync(new VerificationJob(entry, fileSize), cancellationToken);
      }
      jobChannel.Writer.Complete();
    }, cancellationToken);

    var consumers = Enumerable.Range(0, parallelism)
        .Select(_ => Task.Run(async () => {
          using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(options.Algorithm));

          await foreach (var job in jobChannel.Reader.ReadAllAsync(cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(rootPath, job.Entry.RelativePath);
            VerificationResult result;
            var bag = new Dictionary<string, object>();
            FileStarted?.Invoke(this, new FileStartedEventArgs(job.Entry, fullPath, job.FileSize, bag));

            if (job.FileSize < 0) {
              result = new(job.Entry, ResultStatus.Error, Details: "File not found.", FullPath: fullPath);
            } else {
              try {
                int bufferSize = FileIOUtils.GetOptimalBufferSize(job.FileSize);
                string actualHash;
                long bytesReadTotal = 0;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                  byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                  try {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                      hasher.AppendData(buffer, 0, bytesRead);
                      bytesReadTotal += bytesRead;
                      FileProgress?.Invoke(this, new FileProgressEventArgs(job.Entry, fullPath, bytesReadTotal, job.FileSize, bag));
                    }
                    actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                  } finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                  }
                }

                Interlocked.Add(ref totalBytesRead, job.FileSize);

                if (string.Equals(actualHash, job.Entry.ExpectedHash, StringComparison.OrdinalIgnoreCase)) {
                  result = new(job.Entry, ResultStatus.Success, FullPath: fullPath);
                } else if (new FileInfo(fullPath).LastWriteTimeUtc > checksumFileTimestamp) {
                  result = new(job.Entry, ResultStatus.Warning, actualHash, "Checksum mismatch (file is newer).", fullPath);
                } else {
                  result = new(job.Entry, ResultStatus.Error, actualHash, "Checksum mismatch.", fullPath);
                }
              } catch (Exception ex) {
                result = new(job.Entry, ResultStatus.Warning, Details: $"Cannot read file: {ex.Message}", FullPath: fullPath);
              }
            }
            FileCompleted?.Invoke(this, new FileCompletedEventArgs(result, bag));
            await resultChannel.Writer.WriteAsync(result, cancellationToken);
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

    var allFilesInRoot = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
    foreach (var file in allFilesInRoot) {
      cancellationToken.ThrowIfCancellationRequested();
      if (!allListedFiles.ContainsKey(file) && !file.Equals(options.ChecksumFile.FullName, StringComparison.OrdinalIgnoreCase)) {
        warnings++;
        FileFoundNotInChecksumList?.Invoke(file);
      }
    }

    await producer;

    return new FinalSummary(totalFiles, success, warnings, errors, totalBytesRead);
  }
}
