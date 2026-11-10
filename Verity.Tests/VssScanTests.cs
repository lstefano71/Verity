using System.Security.Cryptography;
using System.Text;

using Verity.Utilities;

namespace Verity.Tests;

/// <summary>
/// In VSS mode every file enumeration must read the snapshot, not the live volume. These tests
/// fake a snapshot with an ordinary directory: a resolver whose "snapshot device" is a temp
/// folder that mirrors the live root's volume-relative path.
/// </summary>
public sealed class VssScanTests : IDisposable
{
  private readonly string _base = Path.Combine(Path.GetTempPath(), $"verity-vssscan-{Guid.NewGuid():N}");
  private readonly string _liveRoot;
  private readonly string _snapshotDevice;
  private readonly string _snapshotRoot;
  private readonly VssPathResolver _resolver;

  public VssScanTests()
  {
    _liveRoot = Path.Combine(_base, "live");
    _snapshotDevice = Path.Combine(_base, "snapshot-device");
    Directory.CreateDirectory(_liveRoot);

    var volumeRoot = Path.GetPathRoot(_liveRoot)!;
    _resolver = new VssPathResolver(_snapshotDevice, volumeRoot);
    _snapshotRoot = _resolver.ResolvePath(_liveRoot);
    Directory.CreateDirectory(_snapshotRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
  }

  private static string Sha256(string content) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

  [Fact]
  public void FakeSnapshot_IsOutsideTheLiveRoot()
  {
    // Sanity check of the fixture itself: the two views must be distinct directories.
    Assert.NotEqual(Path.GetFullPath(_liveRoot), Path.GetFullPath(_snapshotRoot));
    Assert.StartsWith(_snapshotDevice, _snapshotRoot);
  }

  [Fact]
  public async Task Verify_UnlistedFileScan_ReadsSnapshot_NotLiveVolume()
  {
    // Snapshot: a.txt (listed) + only-in-snapshot.txt (unlisted). Live: a.txt + only-live.txt.
    File.WriteAllText(Path.Combine(_snapshotRoot, "a.txt"), "a");
    File.WriteAllText(Path.Combine(_snapshotRoot, "only-in-snapshot.txt"), "s");
    File.WriteAllText(Path.Combine(_liveRoot, "a.txt"), "a");
    File.WriteAllText(Path.Combine(_liveRoot, "only-live.txt"), "l");

    var manifest = new FileInfo(Path.Combine(_liveRoot, "m.sha256"));
    File.WriteAllText(manifest.FullName, "");
    var options = new CliOptions(manifest, new DirectoryInfo(_liveRoot), "SHA256", UseVss: true);
    var entries = new List<ManifestEntry> { new() { Hash = Sha256("a"), RelativePath = "a.txt" } };

    var summary = await new VerificationService { VssResolver = _resolver }
      .VerifyChecksumsAsync(options, entries, parallelism: 1);

    Assert.Equal(1, summary.SuccessCount);
    var unlisted = Assert.Single(summary.ProblematicResults);
    Assert.Equal("only-in-snapshot.txt", unlisted.Entry.RelativePath);
    // Reported with the path the user knows, not the snapshot device path.
    Assert.Equal(Path.Combine(_liveRoot, "only-in-snapshot.txt"), unlisted.FullPath);
  }

  [Fact]
  public void ScanRoot_WithoutResolver_IsTheRootPath()
  {
    var run = new RunContext(new CliOptions(new FileInfo(Path.Combine(_liveRoot, "m.sha256")), new DirectoryInfo(_liveRoot), "SHA256"), 1, CancellationToken.None);

    Assert.Equal(_liveRoot, run.ScanRoot);
  }

  [Fact]
  public void ScanRoot_WithResolver_IsTheSnapshotRoot_WithTrailingSeparator()
  {
    var run = new RunContext(new CliOptions(new FileInfo(Path.Combine(_liveRoot, "m.sha256")), new DirectoryInfo(_liveRoot), "SHA256", UseVss: true), 1, CancellationToken.None) {
      VssResolver = _resolver
    };

    Assert.Equal(_snapshotRoot + Path.DirectorySeparatorChar, run.ScanRoot);
  }
}
