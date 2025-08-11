public static class FileIOUtils
{
    public static int GetOptimalBufferSize(long fileSize)
    {
        const int smallFileThreshold = 64 * 1024; // 64KB
        const int defaultBufferSize = 4096; // 4KB
        const int largeFileBufferSize = 1 * 1024 * 1024; // 1MB
        return (fileSize > smallFileThreshold) ? largeFileBufferSize : defaultBufferSize;
    }
}
