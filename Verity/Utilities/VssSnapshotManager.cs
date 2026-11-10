using Alphaleonis.Win32.Vss;

namespace Verity.Utilities;

/// <summary>
/// Lifecycle of a <see cref="VssSnapshotManager"/>.
/// <code>
/// New ──Create──► Creating ──ok──► Active ──Dispose──► Disposed
///  ▲                  │
///  └──fail/cancel─────┘            (any state) ──Dispose──► Disposed
/// </code>
/// </summary>
public enum VssSnapshotState
{
    /// <summary>No snapshot has been requested (or a previous attempt failed and was rolled back).</summary>
    New,
    /// <summary>Snapshot creation is in progress.</summary>
    Creating,
    /// <summary>A snapshot exists and its device object is available.</summary>
    Active,
    /// <summary>The manager has been disposed; terminal.</summary>
    Disposed
}

/// <summary>
/// Position inside the VSS COM creation protocol. Steps must be performed in this order;
/// the value records the last step that completed so cleanup knows what to undo.
/// </summary>
internal enum VssCreationStep
{
    None,
    ComponentsLoaded,
    Initialized,
    BackupStateSet,
    SnapshotSetStarted,
    VolumeAdded,
    Prepared,
    SnapshotTaken,
    PropertiesResolved
}

/// <summary>
/// Manages VSS snapshot creation, lifecycle, and cleanup using the AlphaVSS library.
/// Implements IDisposable to ensure proper cleanup of snapshots.
/// </summary>
/// <remarks>
/// <see cref="Dispose"/> must not be called concurrently with <see cref="CreateSnapshotAsync"/>.
/// </remarks>
public class VssSnapshotManager : IDisposable
{
    private readonly object _gate = new();
    private IVssBackupComponents? _backupComponents;
    private Guid _snapshotSetId;
    private Guid _snapshotId;
    private VssSnapshotState _state = VssSnapshotState.New;
    private VssCreationStep _step = VssCreationStep.None;

    /// <summary>
    /// The volume path that was snapshotted (e.g., "C:\").
    /// </summary>
    public string VolumeToSnapshot { get; }

    /// <summary>
    /// The device object path of the created snapshot.
    /// </summary>
    public string? SnapshotDeviceObject { get; private set; }

    /// <summary>
    /// Current lifecycle state.
    /// </summary>
    public VssSnapshotState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>
    /// Indicates whether a snapshot is currently active.
    /// </summary>
    public bool IsSnapshotActive => State == VssSnapshotState.Active;

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
    /// Moves the lifecycle from <paramref name="from"/> to <paramref name="to"/>, or throws
    /// if the manager is not in the expected state. This is the only place the transition
    /// rules for the public lifecycle are enforced.
    /// </summary>
    private void Transition(VssSnapshotState from, VssSnapshotState to)
    {
        lock (_gate)
        {
            if (_state == VssSnapshotState.Disposed)
                throw new ObjectDisposedException(nameof(VssSnapshotManager));

            if (_state != from)
            {
                throw new InvalidOperationException(_state switch
                {
                    VssSnapshotState.Active => "A snapshot has already been created by this manager.",
                    VssSnapshotState.Creating => "Snapshot creation is already in progress.",
                    _ => $"Invalid state transition {_state} -> {to} (expected {from})."
                });
            }

            _state = to;
        }
    }

    /// <summary>
    /// Creates a VSS snapshot of the specified volume.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The snapshot device object path.</returns>
    /// <exception cref="VssSnapshotException">Thrown when snapshot creation fails or times out.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the manager has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a snapshot already exists or is being created.</exception>
    public async Task<string> CreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Transition(VssSnapshotState.New, VssSnapshotState.Creating);

        using var timeoutCts = new CancellationTokenSource(SnapshotTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await Task.Run(() => CreateSnapshotInternalAsync(combinedCts.Token), combinedCts.Token);
            Transition(VssSnapshotState.Creating, VssSnapshotState.Active);
            return SnapshotDeviceObject!;
        }
        catch (Exception ex)
        {
            // Single rollback point: whatever step was reached is undone, and the manager
            // returns to New (unless it was disposed in the meantime).
            CleanupSnapshot();
            lock (_gate)
            {
                if (_state == VssSnapshotState.Creating)
                    _state = VssSnapshotState.New;
            }

            if (ex is OperationCanceledException && timeoutCts.IsCancellationRequested)
                throw new VssSnapshotException($"Snapshot creation timed out after {SnapshotTimeout}.");

            if (ex is OperationCanceledException or VssSnapshotException or ObjectDisposedException)
                throw;

            throw new VssSnapshotException($"Failed to create VSS snapshot: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Performs the VSS creation protocol, recording each completed step in <see cref="_step"/>.
    /// Does not clean up; the caller rolls back based on <see cref="_step"/>. The two potentially
    /// long steps (writer preparation and the commit) use the VSS asynchronous calls, so the
    /// timeout and user cancellation interrupt them instead of waiting for them to finish.
    /// </summary>
    private async Task CreateSnapshotInternalAsync(CancellationToken cancellationToken)
    {
        _backupComponents = VssFactoryProvider.Default.GetVssFactory().CreateVssBackupComponents();
        Advance(VssCreationStep.ComponentsLoaded, cancellationToken);

        _backupComponents.InitializeForBackup(null);
        Advance(VssCreationStep.Initialized, cancellationToken);

        _backupComponents.SetBackupState(false, true, VssBackupType.Copy, false);
        Advance(VssCreationStep.BackupStateSet, cancellationToken);

        _snapshotSetId = _backupComponents.StartSnapshotSet();
        Advance(VssCreationStep.SnapshotSetStarted, cancellationToken);

        _snapshotId = _backupComponents.AddToSnapshotSet(VolumeToSnapshot, Guid.Empty);
        Advance(VssCreationStep.VolumeAdded, cancellationToken);

        await _backupComponents.PrepareForBackupAsync(cancellationToken);
        Advance(VssCreationStep.Prepared, cancellationToken);

        await _backupComponents.DoSnapshotSetAsync(cancellationToken);
        Advance(VssCreationStep.SnapshotTaken, cancellationToken);

        var snapshotProperties = _backupComponents.GetSnapshotProperties(_snapshotId);
        SnapshotDeviceObject = snapshotProperties.SnapshotDeviceObject;

        if (string.IsNullOrEmpty(SnapshotDeviceObject))
            throw new VssSnapshotException("Failed to retrieve snapshot device object path.");

        _step = VssCreationStep.PropertiesResolved;
    }

    /// <summary>
    /// Records that <paramref name="completed"/> finished (steps must be strictly sequential)
    /// and then honours cancellation.
    /// </summary>
    private void Advance(VssCreationStep completed, CancellationToken cancellationToken)
    {
        if (completed != _step + 1)
            throw new InvalidOperationException($"VSS creation step {completed} cannot follow {_step}.");

        _step = completed;
        cancellationToken.ThrowIfCancellationRequested();
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
    /// Undoes whatever creation steps completed and resets the creation bookkeeping.
    /// Safe to call repeatedly.
    /// </summary>
    private void CleanupSnapshot()
    {
        if (_backupComponents != null)
        {
            try
            {
                if (_step >= VssCreationStep.SnapshotTaken)
                {
                    // The snapshot set exists on the VSS service once DoSnapshotSet has completed.
                    _backupComponents.DeleteSnapshotSet(_snapshotSetId, false);
                }
                else if (_step >= VssCreationStep.SnapshotSetStarted)
                {
                    // Between StartSnapshotSet and a completed DoSnapshotSet, VSS requires
                    // AbortBackup so writers are released (ignored if nothing is under way).
                    _backupComponents.AbortBackup();
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - we're in cleanup. stderr keeps redirected output clean.
                Console.Error.WriteLine($"Warning: Failed to clean up VSS snapshot: {ex.Message}");
            }
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
        _snapshotSetId = Guid.Empty;
        _snapshotId = Guid.Empty;
        _step = VssCreationStep.None;
    }

    /// <summary>
    /// Disposes of the VSS snapshot and related resources.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == VssSnapshotState.Disposed)
                return;
            _state = VssSnapshotState.Disposed;
        }

        CleanupSnapshot();
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
