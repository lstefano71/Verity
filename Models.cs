Models.cs
code
C#
download
content_copy
expand_less

using System.IO;

public record struct CliOptions(
    FileInfo ChecksumFile,
    DirectoryInfo? RootDirectory,
    string Algorithm
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
    string? FullPath = null
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
    long TotalBytesRead
);
