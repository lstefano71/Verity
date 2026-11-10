using Verity.Utilities;

namespace Verity.Tests.IntegrationTests;

/// <summary>
/// Runs only when elevated on Windows AND the environment variable VERITY_VSS_TESTS=1 is set
/// (xUnit equivalent of NUnit's [Explicit]); these tests create real shadow copies.
/// </summary>
public sealed class ElevatedVssFactAttribute : FactAttribute
{
    public ElevatedVssFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only.";
        else if (!ElevationHelper.IsElevated())
            Skip = "Requires elevation.";
        else if (Environment.GetEnvironmentVariable("VERITY_VSS_TESTS") != "1")
            Skip = "Set VERITY_VSS_TESTS=1 to run tests that create real VSS snapshots.";
    }
}

/// <summary>Runs only when NOT elevated (checks the failure path of snapshot creation).</summary>
public sealed class NonElevatedWindowsFactAttribute : FactAttribute
{
    public NonElevatedWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only.";
        else if (ElevationHelper.IsElevated())
            Skip = "Only meaningful without elevation.";
    }
}

public class VssSnapshotIntegrationTests
{
    private static string VolumeRoot => Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    // ---- Real VSS (opt-in, elevated) -------------------------------------------------

    [ElevatedVssFact]
    public async Task CreateSnapshot_Succeeds_AndBecomesActive()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);

        var snapshotPath = await vssManager.CreateSnapshotAsync();

        Assert.False(string.IsNullOrEmpty(snapshotPath));
        Assert.True(vssManager.IsSnapshotActive);
        Assert.Equal(VssSnapshotState.Active, vssManager.State);
    }

    [ElevatedVssFact]
    public async Task CreatePathResolver_WorksCorrectly()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);
        await vssManager.CreateSnapshotAsync();

        var pathResolver = vssManager.CreatePathResolver();

        Assert.False(string.IsNullOrEmpty(pathResolver.SnapshotPath));
        Assert.Equal(VolumeRoot, pathResolver.OriginalVolumeRoot);
    }

    [ElevatedVssFact]
    public async Task CanAccessFilesThroughSnapshot()
    {
        var testFile = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(testFile))
            return; // nothing to compare against on this machine

        using var vssManager = new VssSnapshotManager(VolumeRoot);
        await vssManager.CreateSnapshotAsync();
        var snapshotPath = vssManager.CreatePathResolver().ResolvePath(testFile);

        Assert.True(File.Exists(snapshotPath), "File should be accessible through VSS snapshot");
        Assert.Equal(new FileInfo(testFile).Length, new FileInfo(snapshotPath).Length);
    }

    [ElevatedVssFact]
    public async Task Dispose_AfterCreate_MovesToDisposed()
    {
        VssSnapshotManager vssManager;
        using (vssManager = new VssSnapshotManager(VolumeRoot))
        {
            await vssManager.CreateSnapshotAsync();
            Assert.True(vssManager.IsSnapshotActive);
        }

        Assert.Equal(VssSnapshotState.Disposed, vssManager.State);
        Assert.False(vssManager.IsSnapshotActive);
    }

    [ElevatedVssFact]
    public async Task MultipleCreateCalls_ThrowsInvalidOperation()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);
        await vssManager.CreateSnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => vssManager.CreateSnapshotAsync());
        Assert.Equal(VssSnapshotState.Active, vssManager.State); // a rejected call must not disturb the snapshot
    }

    // ---- State machine behaviour (no real snapshot required) -------------------------

    [NonElevatedWindowsFact]
    public async Task CreateWithoutElevation_ThrowsVssSnapshotException_AndReturnsToNew()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);

        await Assert.ThrowsAsync<VssSnapshotException>(() => vssManager.CreateSnapshotAsync());

        Assert.Equal(VssSnapshotState.New, vssManager.State);
    }

    [Fact]
    public void NewManager_IsInNewState_AndNotActive()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);

        Assert.Equal(VssSnapshotState.New, vssManager.State);
        Assert.False(vssManager.IsSnapshotActive);
        Assert.Null(vssManager.SnapshotDeviceObject);
    }

    [Fact]
    public async Task CreateSnapshotAsync_ZeroTimeout_ThrowsTimedOut_AndReturnsToNew()
    {
        // A zero timeout cancels the token before work starts, so this is deterministic
        // and never reaches the real VSS service.
        using var vssManager = new VssSnapshotManager(VolumeRoot, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<VssSnapshotException>(() => vssManager.CreateSnapshotAsync());

        Assert.Contains("timed out", ex.Message);
        Assert.Equal(VssSnapshotState.New, vssManager.State);
    }

    [Fact]
    public async Task CreateSnapshotAsync_CallerCancels_ThrowsOperationCanceled_AndReturnsToNew()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vssManager.CreateSnapshotAsync(cts.Token));

        Assert.Equal(VssSnapshotState.New, vssManager.State);
    }

    [Fact]
    public async Task CreateSnapshotAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var vssManager = new VssSnapshotManager(VolumeRoot);
        vssManager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => vssManager.CreateSnapshotAsync());
        Assert.Equal(VssSnapshotState.Disposed, vssManager.State);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var vssManager = new VssSnapshotManager(VolumeRoot);

        vssManager.Dispose();
        var ex = Record.Exception(() => vssManager.Dispose());

        Assert.Null(ex);
        Assert.Equal(VssSnapshotState.Disposed, vssManager.State);
    }

    [Fact]
    public void CreatePathResolver_WithoutSnapshot_ThrowsInvalidOperation()
    {
        using var vssManager = new VssSnapshotManager(VolumeRoot);

        Assert.Throws<InvalidOperationException>(() => vssManager.CreatePathResolver());
    }

    [Fact]
    public void Constructor_AppendsTrailingBackslash()
    {
        using var vssManager = new VssSnapshotManager("C:");

        Assert.Equal(@"C:\", vssManager.VolumeToSnapshot);
    }
}
