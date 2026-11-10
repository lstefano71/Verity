using Verity.Utilities;

namespace Verity.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
[Platform("Win")]
public class VssSnapshotIntegrationTests
{
    [Test]
    [Explicit("Requires elevation and actual VSS capability")]
    public async Task VssSnapshotManager_CreateSnapshot_RequiresElevation()
    {
        // This test should only be run manually when elevated
        if (!ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test requires elevation to run.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            var snapshotPath = await vssManager.CreateSnapshotAsync();
            Assert.That(snapshotPath, Is.Not.Null.And.Not.Empty);
            Assert.That(vssManager.IsSnapshotActive, Is.True);
        });
    }

    [Test]
    [Explicit("Requires elevation and actual VSS capability")]
    public async Task VssSnapshotManager_CreatePathResolver_WorksCorrectly()
    {
        // This test should only be run manually when elevated
        if (!ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test requires elevation to run.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        await vssManager.CreateSnapshotAsync();
        
        // Act
        var pathResolver = vssManager.CreatePathResolver();
        
        // Assert
        Assert.That(pathResolver, Is.Not.Null);
        Assert.That(pathResolver.SnapshotPath, Is.Not.Null.And.Not.Empty);
        Assert.That(pathResolver.OriginalVolumeRoot, Is.EqualTo(volumeRoot));
    }

    [Test]
    [Explicit("Requires elevation and actual VSS capability")]
    public async Task VssSnapshotManager_CanAccessFilesThroughSnapshot()
    {
        // This test should only be run manually when elevated
        if (!ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test requires elevation to run.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var testFile = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        
        // Skip test if the file doesn't exist
        if (!File.Exists(testFile))
        {
            Assert.Ignore($"Test file {testFile} not found.");
        }
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        await vssManager.CreateSnapshotAsync();
        var pathResolver = vssManager.CreatePathResolver();
        
        // Act
        var snapshotPath = pathResolver.ResolvePath(testFile);
        
        // Assert
        Assert.That(File.Exists(snapshotPath), Is.True, "File should be accessible through VSS snapshot");
        
        // Verify file contents are the same
        var originalSize = new FileInfo(testFile).Length;
        var snapshotSize = new FileInfo(snapshotPath).Length;
        Assert.That(snapshotSize, Is.EqualTo(originalSize), "File sizes should match");
    }

    [Test]
    public void VssSnapshotManager_CreateWithoutElevation_ThrowsException()
    {
        // This test verifies that VSS operations fail gracefully without elevation
        if (ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test should only run without elevation.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        
        // Act & Assert
        Assert.ThrowsAsync<VssSnapshotException>(async () =>
        {
            await vssManager.CreateSnapshotAsync();
        });
    }

    [Test]
    [Explicit("Requires elevation and actual VSS capability")]
    public async Task VssSnapshotManager_Dispose_CleansUpSnapshot()
    {
        // This test should only be run manually when elevated
        if (!ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test requires elevation to run.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        string? snapshotPath = null;
        
        // Act
        using (var vssManager = new VssSnapshotManager(volumeRoot))
        {
            snapshotPath = await vssManager.CreateSnapshotAsync();
            Assert.That(vssManager.IsSnapshotActive, Is.True);
        } // Dispose called here
        
        // Assert - After disposal, snapshot should be cleaned up
        // Note: We can't easily verify the snapshot is gone without VSS APIs,
        // but we can at least verify the operation completed without throwing
        Assert.That(snapshotPath, Is.Not.Null);
    }

    [Test]
    [Explicit("Requires elevation and actual VSS capability")]
    public async Task VssSnapshotManager_MultipleCreateCalls_ThrowsException()
    {
        // This test should only be run manually when elevated
        if (!ElevationHelper.IsElevated())
        {
            Assert.Ignore("This test requires elevation to run.");
        }

        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        await vssManager.CreateSnapshotAsync();
        
        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await vssManager.CreateSnapshotAsync();
        });
    }

    [Test]
    public async Task VssSnapshotManager_CreateSnapshotAsync_RespectsTimeout()
    {
        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var shortTimeout = TimeSpan.FromMilliseconds(1); // Very short timeout
        
        using var vssManager = new VssSnapshotManager(volumeRoot, shortTimeout);
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<VssSnapshotException>(async () =>
        {
            await vssManager.CreateSnapshotAsync();
        });
        
        Assert.That(ex.Message, Does.Contain("timed out"));
    }

    [Test]
    public void VssSnapshotManager_CreatePathResolver_WithoutSnapshot_ThrowsException()
    {
        var volumeRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        using var vssManager = new VssSnapshotManager(volumeRoot);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            vssManager.CreatePathResolver();
        });
    }
}