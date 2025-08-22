using Verity.Utilities;

namespace Verity.Tests;

[TestFixture]
public class VssPathResolverTests
{
    private const string TestSnapshotPath = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1";
    private const string TestVolumeRoot = @"C:\";

    [Test]
    public void Constructor_ValidParameters_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Assert
        Assert.That(resolver.SnapshotPath, Is.EqualTo(TestSnapshotPath));
        Assert.That(resolver.OriginalVolumeRoot, Is.EqualTo(TestVolumeRoot));
    }

    [Test]
    public void Constructor_VolumeRootWithoutBackslash_AddsBackslash()
    {
        // Arrange & Act
        var resolver = new VssPathResolver(TestSnapshotPath, "C:");

        // Assert
        Assert.That(resolver.OriginalVolumeRoot, Is.EqualTo(@"C:\"));
    }

    [Test]
    public void Constructor_NullSnapshotPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VssPathResolver(null!, TestVolumeRoot));
    }

    [Test]
    public void Constructor_NullVolumeRoot_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VssPathResolver(TestSnapshotPath, null!));
    }

    [Test]
    public void ResolvePath_PathOnSameVolume_MapsToSnapshotPath()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var originalPath = @"C:\Program Files\MyApp\app.exe";
        var expectedPath = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Program Files\MyApp\app.exe";

        // Act
        var result = resolver.ResolvePath(originalPath);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    public void ResolvePath_PathOnDifferentVolume_ReturnsOriginalPath()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var originalPath = @"D:\SomeFolder\file.txt";

        // Act
        var result = resolver.ResolvePath(originalPath);

        // Assert
        Assert.That(result, Is.EqualTo(Path.GetFullPath(originalPath)));
    }

    [Test]
    public void ResolvePath_RelativePath_ResolvesToSnapshotPath()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var relativePath = "temp\\file.txt";
        
        // Act
        var result = resolver.ResolvePath(relativePath);

        // Assert
        Assert.That(result, Does.StartWith(TestSnapshotPath));
        Assert.That(result, Does.EndWith("temp\\file.txt"));
    }

    [Test]
    public void ResolvePath_EmptyPath_ThrowsArgumentNullException()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => resolver.ResolvePath(string.Empty));
    }

    [Test]
    public void ResolvePath_NullPath_ThrowsArgumentNullException()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => resolver.ResolvePath(null!));
    }

    [Test]
    public void ResolveDirectoryInfo_ValidDirectory_ReturnsSnapshotDirectoryInfo()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var originalDir = new DirectoryInfo(@"C:\Windows\System32");

        // Act
        var result = resolver.ResolveDirectoryInfo(originalDir);

        // Assert
        Assert.That(result.FullName, Does.StartWith(TestSnapshotPath));
        Assert.That(result.FullName, Does.EndWith("Windows\\System32"));
    }

    [Test]
    public void ResolveDirectoryInfo_NullDirectory_ThrowsArgumentNullException()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveDirectoryInfo(null!));
    }

    [Test]
    public void ResolveFileInfo_ValidFile_ReturnsSnapshotFileInfo()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var originalFile = new FileInfo(@"C:\Windows\notepad.exe");

        // Act
        var result = resolver.ResolveFileInfo(originalFile);

        // Assert
        Assert.That(result.FullName, Does.StartWith(TestSnapshotPath));
        Assert.That(result.FullName, Does.EndWith("Windows\\notepad.exe"));
    }

    [Test]
    public void ResolveFileInfo_NullFile_ThrowsArgumentNullException()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveFileInfo(null!));
    }

    [Test]
    public void IsPathOnSnapshotVolume_PathOnSameVolume_ReturnsTrue()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var pathOnSameVolume = @"C:\Program Files\MyApp\app.exe";

        // Act
        var result = resolver.IsPathOnSnapshotVolume(pathOnSameVolume);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsPathOnSnapshotVolume_PathOnDifferentVolume_ReturnsFalse()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);
        var pathOnDifferentVolume = @"D:\SomeFolder\file.txt";

        // Act
        var result = resolver.IsPathOnSnapshotVolume(pathOnDifferentVolume);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPathOnSnapshotVolume_EmptyPath_ReturnsFalse()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act
        var result = resolver.IsPathOnSnapshotVolume(string.Empty);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPathOnSnapshotVolume_NullPath_ReturnsFalse()
    {
        // Arrange
        var resolver = new VssPathResolver(TestSnapshotPath, TestVolumeRoot);

        // Act
        var result = resolver.IsPathOnSnapshotVolume(null!);

        // Assert
        Assert.That(result, Is.False);
    }
}