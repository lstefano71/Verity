using Verity.Utilities;

namespace Verity.Tests;

public class VssPathResolverTests
{
    private const string TestSnapshotPath = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1";
    private const string TestVolumeRoot = @"C:\";

    private static VssPathResolver NewResolver() => new(TestSnapshotPath, TestVolumeRoot);

    [Fact]
    public void Constructor_ValidParameters_SetsPropertiesCorrectly()
    {
        var resolver = NewResolver();

        Assert.Equal(TestSnapshotPath, resolver.SnapshotPath);
        Assert.Equal(TestVolumeRoot, resolver.OriginalVolumeRoot);
    }

    [Fact]
    public void Constructor_VolumeRootWithoutBackslash_AddsBackslash()
    {
        var resolver = new VssPathResolver(TestSnapshotPath, "C:");

        Assert.Equal(@"C:\", resolver.OriginalVolumeRoot);
    }

    [Fact]
    public void Constructor_NullSnapshotPath_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => new VssPathResolver(null!, TestVolumeRoot));

    [Fact]
    public void Constructor_NullVolumeRoot_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => new VssPathResolver(TestSnapshotPath, null!));

    [Fact]
    public void ResolvePath_PathOnSameVolume_MapsToSnapshotPath()
    {
        var result = NewResolver().ResolvePath(@"C:\Program Files\MyApp\app.exe");

        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Program Files\MyApp\app.exe", result);
    }

    [Fact]
    public void ResolvePath_PathOnDifferentVolume_ReturnsOriginalPath()
    {
        var originalPath = @"D:\SomeFolder\file.txt";

        var result = NewResolver().ResolvePath(originalPath);

        Assert.Equal(Path.GetFullPath(originalPath), result);
    }

    [Fact]
    public void ResolvePath_RelativePath_ResolvesToSnapshotPath()
    {
        // Relative paths resolve against the current directory; only meaningful when that
        // directory is on the snapshotted volume.
        var cwdRoot = Path.GetPathRoot(Directory.GetCurrentDirectory());
        var resolver = new VssPathResolver(TestSnapshotPath, cwdRoot!);

        var result = resolver.ResolvePath("temp\\file.txt");

        Assert.StartsWith(TestSnapshotPath, result);
        Assert.EndsWith("temp\\file.txt", result);
    }

    [Fact]
    public void ResolvePath_EmptyPath_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => NewResolver().ResolvePath(string.Empty));

    [Fact]
    public void ResolvePath_NullPath_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => NewResolver().ResolvePath(null!));

    [Fact]
    public void ResolveDirectoryInfo_ValidDirectory_ReturnsSnapshotDirectoryInfo()
    {
        var result = NewResolver().ResolveDirectoryInfo(new DirectoryInfo(@"C:\Windows\System32"));

        Assert.StartsWith(TestSnapshotPath, result.FullName);
        Assert.EndsWith("Windows\\System32", result.FullName);
    }

    [Fact]
    public void ResolveDirectoryInfo_NullDirectory_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => NewResolver().ResolveDirectoryInfo(null!));

    [Fact]
    public void ResolveFileInfo_ValidFile_ReturnsSnapshotFileInfo()
    {
        var result = NewResolver().ResolveFileInfo(new FileInfo(@"C:\Windows\notepad.exe"));

        Assert.StartsWith(TestSnapshotPath, result.FullName);
        Assert.EndsWith("Windows\\notepad.exe", result.FullName);
    }

    [Fact]
    public void ResolveFileInfo_NullFile_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => NewResolver().ResolveFileInfo(null!));

    [Fact]
    public void IsPathOnSnapshotVolume_PathOnSameVolume_ReturnsTrue() =>
        Assert.True(NewResolver().IsPathOnSnapshotVolume(@"C:\Program Files\MyApp\app.exe"));

    [Fact]
    public void IsPathOnSnapshotVolume_PathOnDifferentVolume_ReturnsFalse() =>
        Assert.False(NewResolver().IsPathOnSnapshotVolume(@"D:\SomeFolder\file.txt"));

    [Fact]
    public void IsPathOnSnapshotVolume_EmptyPath_ReturnsFalse() =>
        Assert.False(NewResolver().IsPathOnSnapshotVolume(string.Empty));

    [Fact]
    public void IsPathOnSnapshotVolume_NullPath_ReturnsFalse() =>
        Assert.False(NewResolver().IsPathOnSnapshotVolume(null!));
}
