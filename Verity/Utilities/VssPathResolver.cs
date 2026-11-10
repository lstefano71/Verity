namespace Verity.Utilities;

/// <summary>
/// Provides path resolution services for translating original file paths to VSS snapshot paths.
/// </summary>
public class VssPathResolver
{
    /// <summary>
    /// The snapshot device path provided by VSS.
    /// </summary>
    public string SnapshotPath { get; }

    /// <summary>
    /// The original volume root that was snapshotted (e.g., "C:\").
    /// </summary>
    public string OriginalVolumeRoot { get; }

    /// <summary>
    /// Initializes a new instance of the VssPathResolver class.
    /// </summary>
    /// <param name="snapshotPath">The snapshot device path from VSS.</param>
    /// <param name="originalVolumeRoot">The original volume root that was snapshotted.</param>
    public VssPathResolver(string snapshotPath, string originalVolumeRoot)
    {
        SnapshotPath = snapshotPath ?? throw new ArgumentNullException(nameof(snapshotPath));
        OriginalVolumeRoot = originalVolumeRoot ?? throw new ArgumentNullException(nameof(originalVolumeRoot));

        // Ensure the original volume root ends with a backslash
        if (!OriginalVolumeRoot.EndsWith('\\'))
        {
            OriginalVolumeRoot += '\\';
        }
    }

    /// <summary>
    /// Resolves an original file path to its corresponding snapshot path.
    /// </summary>
    /// <param name="originalPath">The original file path to resolve.</param>
    /// <returns>The corresponding path in the VSS snapshot.</returns>
    public string ResolvePath(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath))
            throw new ArgumentNullException(nameof(originalPath));

        // Get the full path to handle relative paths
        var fullOriginalPath = Path.GetFullPath(originalPath);

        // Check if the path is on the same volume as the snapshot
        if (!fullOriginalPath.StartsWith(OriginalVolumeRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Path is not on the snapshotted volume, return original path
            return fullOriginalPath;
        }

        // Remove the volume root and map to snapshot path
        var relativePath = fullOriginalPath.Substring(OriginalVolumeRoot.Length);
        
        // Construct the snapshot path
        var snapshotFilePath = Path.Combine(SnapshotPath, relativePath);
        
        return snapshotFilePath;
    }

    /// <summary>
    /// Creates a DirectoryInfo object that points to the snapshot version of the directory.
    /// </summary>
    /// <param name="original">The original DirectoryInfo.</param>
    /// <returns>A DirectoryInfo pointing to the snapshot path.</returns>
    public DirectoryInfo ResolveDirectoryInfo(DirectoryInfo original)
    {
        if (original == null)
            throw new ArgumentNullException(nameof(original));

        var resolvedPath = ResolvePath(original.FullName);
        return new DirectoryInfo(resolvedPath);
    }

    /// <summary>
    /// Creates a FileInfo object that points to the snapshot version of the file.
    /// </summary>
    /// <param name="original">The original FileInfo.</param>
    /// <returns>A FileInfo pointing to the snapshot path.</returns>
    public FileInfo ResolveFileInfo(FileInfo original)
    {
        if (original == null)
            throw new ArgumentNullException(nameof(original));

        var resolvedPath = ResolvePath(original.FullName);
        return new FileInfo(resolvedPath);
    }

    /// <summary>
    /// Checks if the given path is on the same volume as the snapshot.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is on the snapshotted volume, false otherwise.</returns>
    public bool IsPathOnSnapshotVolume(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(OriginalVolumeRoot, StringComparison.OrdinalIgnoreCase);
    }
}