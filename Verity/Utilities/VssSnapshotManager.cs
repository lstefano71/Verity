using AlphaVSS;
using Spectre.Console;

namespace Verity.Utilities;

/// <summary>
/// Manages VSS snapshot creation, lifecycle, and cleanup using AlphaVSS library.
/// Implements IDisposable to ensure proper cleanup of snapshots.
/// </summary>
public class VssSnapshotManager : IDisposable
{
    private IVssBackupComponents? _backupComponents;
    private Guid _snapshotSetId;
    private Guid _snapshotId;
    private bool _disposed;
    private bool _snapshotCreated;

    /// <summary>
    /// The volume path that was snapshotted (e.g., "C:\").
    /// </summary>
    public string VolumeToSnapshot { get; }

    /// <summary>
    /// The device object path of the created snapshot.
    /// </summary>
    public string? SnapshotDeviceObject { get; private set; }

    /// <summary>
    /// Indicates whether a snapshot is currently active.
    /// </summary>
    public bool IsSnapshotActive => SnapshotDeviceObject != null && _snapshotCreated;

    /// <summary>
    /// The timeout for snapshot operations.
    /// </summary>
    public TimeSpan SnapshotTimeout { get; }

    /// <summary>
    /// Initializes a new instance of the VssSnapshotManager class.
    /// </summary>
    /// <param name="volumeToSnapshot">The volume to create a snapshot of (e.g., "C:\").</param>
    /// <param name="snapshotTimeout">The timeout for snapshot operations. Defaults to 5 minutes.</param>
    public VssSnapshotManager(string volumeToSnapshot, TimeSpan? snapshotTimeout = null)
    {
        VolumeToSnapshot = volumeToSnapshot ?? throw new ArgumentNullException(nameof(volumeToSnapshot));
        SnapshotTimeout = snapshotTimeout ?? TimeSpan.FromMinutes(5);
        
        // Ensure the volume path is in the correct format
        if (!VolumeToSnapshot.EndsWith('\\'))
        {
            VolumeToSnapshot += '\\';
        }
    }

    /// <summary>
    /// Creates a VSS snapshot of the specified volume.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The snapshot device object path.</returns>
    /// <exception cref="VssSnapshotException">Thrown when snapshot creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the manager has been disposed.</exception>
    public async Task<string> CreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VssSnapshotManager));

        if (_snapshotCreated)
            throw new InvalidOperationException("A snapshot has already been created by this manager.");

        try
        {
            using var timeoutCts = new CancellationTokenSource(SnapshotTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await Task.Run(() => CreateSnapshotInternal(combinedCts.Token), combinedCts.Token);
            _snapshotCreated = true;
            return SnapshotDeviceObject!;
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new VssSnapshotException($"Snapshot creation timed out after {SnapshotTimeout}.");
        }
        catch (Exception ex) when (!(ex is VssSnapshotException))
        {
            throw new VssSnapshotException($"Failed to create VSS snapshot: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates the VSS snapshot synchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private void CreateSnapshotInternal(CancellationToken cancellationToken)
    {
        try
        {
            // Initialize VSS
            _backupComponents = VssUtils.LoadImplementation().CreateVssBackupComponents();

            cancellationToken.ThrowIfCancellationRequested();

            // Initialize for backup
            _backupComponents.InitializeForBackup(null);

            cancellationToken.ThrowIfCancellationRequested();

            // Set backup state
            _backupComponents.SetBackupState(false, true, VssBackupType.Copy, false);

            cancellationToken.ThrowIfCancellationRequested();

            // Create snapshot set
            _snapshotSetId = _backupComponents.StartSnapshotSet();

            cancellationToken.ThrowIfCancellationRequested();

            // Add volume to snapshot set
            _snapshotId = _backupComponents.AddToSnapshotSet(VolumeToSnapshot, Guid.Empty);

            cancellationToken.ThrowIfCancellationRequested();

            // Prepare for backup
            _backupComponents.PrepareForBackup();

            cancellationToken.ThrowIfCancellationRequested();

            // Create the snapshots
            _backupComponents.DoSnapshotSet();

            cancellationToken.ThrowIfCancellationRequested();

            // Get snapshot properties
            var snapshotProperties = _backupComponents.GetSnapshotProperties(_snapshotId);
            SnapshotDeviceObject = snapshotProperties.SnapshotDeviceObject;

            if (string.IsNullOrEmpty(SnapshotDeviceObject))
            {
                throw new VssSnapshotException("Failed to retrieve snapshot device object path.");
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            // Clean up on failure
            CleanupSnapshot();
            throw new VssSnapshotException($"VSS snapshot creation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a VssPathResolver for the created snapshot.
    /// </summary>
    /// <returns>A VssPathResolver instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no snapshot is active.</exception>
    public VssPathResolver CreatePathResolver()
    {
        if (!IsSnapshotActive)
            throw new InvalidOperationException("No active snapshot to create path resolver for.");

        return new VssPathResolver(SnapshotDeviceObject!, VolumeToSnapshot);
    }

    /// <summary>
    /// Cleans up the VSS snapshot and related resources.
    /// </summary>
    private void CleanupSnapshot()
    {
        try
        {
            if (_backupComponents != null && _snapshotSetId != Guid.Empty)
            {
                try
                {
                    _backupComponents.DeleteSnapshots(_snapshotSetId, VssObjectType.SnapshotSet, false);
                }
                catch (Exception ex)
                {
                    // Log but don't throw - we're in cleanup
                    Console.WriteLine($"Warning: Failed to delete VSS snapshot: {ex.Message}");
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        try
        {
            _backupComponents?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }

        _backupComponents = null;
        SnapshotDeviceObject = null;
        _snapshotCreated = false;
        _snapshotSetId = Guid.Empty;
        _snapshotId = Guid.Empty;
    }

    /// <summary>
    /// Disposes of the VSS snapshot and related resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupSnapshot();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer to ensure cleanup if Dispose is not called.
    /// </summary>
    ~VssSnapshotManager()
    {
        CleanupSnapshot();
    }
}

/// <summary>
/// Exception thrown when VSS snapshot operations fail.
/// </summary>
public class VssSnapshotException : Exception
{
    public VssSnapshotException(string message) : base(message)
    {
    }

    public VssSnapshotException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Error codes for VSS-related failures.
/// </summary>
public enum VssErrorCode
{
    /// <summary>
    /// Elevation is required for VSS operations.
    /// </summary>
    ElevationRequired = 100,

    /// <summary>
    /// VSS is not available on this system.
    /// </summary>
    VssNotAvailable = 101,

    /// <summary>
    /// Failed to create the VSS snapshot.
    /// </summary>
    SnapshotCreationFailed = 102,

    /// <summary>
    /// The volume does not support VSS snapshots.
    /// </summary>
    VolumeNotSupported = 103
}