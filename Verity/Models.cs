public record struct CliOptions(
    FileInfo ChecksumFile,
    DirectoryInfo? RootDirectory,
    string Algorithm,
    FileInfo? TsvReportFile = null,
    bool ShowTable = false,
    string[]? IncludeGlobs = null,
    string[]? ExcludeGlobs = null,
    bool UseVss = false
);

public record ChecksumEntry(
    string ExpectedHash,
    string RelativePath
);

public record VerificationJob(
    ChecksumEntry Entry,
    long FileSize
);

public record VerificationResult(
    ChecksumEntry Entry,
    ResultStatus Status,
    string? ActualHash = null,
    string? Details = null,
    string? FullPath = null,
  Exception Exception = null
);

public enum ResultStatus
{
  Success,
  Warning,
  Error
}

public record FinalSummary(
    int TotalFiles,
    int SuccessCount,
    int WarningCount,
    int ErrorCount,
    long TotalBytesRead,
    IReadOnlyList<VerificationResult> ProblematicResults
);
