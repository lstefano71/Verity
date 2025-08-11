using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;

public static class VerificationService
{
  public static async Task<FinalSummary> VerifyChecksumsAsync(
      CliOptions options,
      Action<long, long> onProgress,
      Action<VerificationResult> onResult,
      Action<string> onFileFoundNotInChecksumList,
      CancellationToken cancellationToken)
  {
    var rootPath = options.RootDirectory?.FullName ?? options.ChecksumFile.DirectoryName!;
    var checksumFileTimestamp = options.ChecksumFile.LastWriteTimeUtc;
    var lines = await File.ReadAllLinesAsync(options.ChecksumFile.FullName, cancellationToken);
    var totalFiles = lines.Length;

    var jobChannel = Channel.CreateBounded<VerificationJob>(Environment.ProcessorCount * 2);
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

    var consumers = Enumerable.Range(0, Environment.ProcessorCount)
        .Select(_ => Task.Run(async () => {
          using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(options.Algorithm));

          await foreach (var job in jobChannel.Reader.ReadAllAsync(cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(rootPath, job.Entry.RelativePath);
            VerificationResult result;

            if (job.FileSize < 0) {
              result = new(job.Entry, ResultStatus.Error, Details: "File not found.", FullPath: fullPath);
            } else {
              try {
                const int smallFileThreshold = 64 * 1024;
                const int defaultBufferSize = 4096;
                const int largeFileBufferSize = 1 * 1024 * 1024;
                int bufferSize = (job.FileSize > smallFileThreshold) ? largeFileBufferSize : defaultBufferSize;

                string actualHash;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous)) {
                  byte[] buffer = new byte[bufferSize];
                  int bytesRead;
                  while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
                    hasher.AppendData(buffer, 0, bytesRead);
                  }
                  actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
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

      onResult(result);
      onProgress(success + warnings + errors, totalFiles);
    }

    var allFilesInRoot = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
    foreach (var file in allFilesInRoot) {
      cancellationToken.ThrowIfCancellationRequested();
      if (!allListedFiles.ContainsKey(file) && !file.Equals(options.ChecksumFile.FullName, StringComparison.OrdinalIgnoreCase)) {
        warnings++;
        onFileFoundNotInChecksumList(file);
      }
    }

    await producer;

    return new FinalSummary(totalFiles, success, warnings, errors, totalBytesRead);
  }
}
