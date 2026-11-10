using Verity.Utilities;

namespace Verity.Tests;

public class ElevationHelperTests
{
    [Fact]
    public void IsElevated_DoesNotThrow()
    {
        var ex = Record.Exception(() => ElevationHelper.IsElevated());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiresElevation_FollowsUseVss(bool useVss)
    {
        var options = new CliOptions(new FileInfo("test.sha256"), null, "SHA256", UseVss: useVss);

        Assert.Equal(useVss, ElevationHelper.RequiresElevation(options));
    }

    [Fact]
    public async Task RestartElevatedAsync_NullArgs_ThrowsArgumentNullException()
    {
        // Throws while building the start info, before any process is started.
        await Assert.ThrowsAsync<ArgumentNullException>(() => ElevationHelper.RestartElevatedAsync(null!));
    }

    // --- EnsureElevatedAsync: the full decision table, with probes injected so that
    // --- no process is ever launched and no UAC prompt is shown.

    private static Func<string[], Task<bool>> NeverRelaunch() =>
        _ => throw new Xunit.Sdk.XunitException("relaunch must not be attempted");

    [Fact]
    public async Task EnsureElevated_NotRequired_ReturnsNotRequired_WithoutProbing()
    {
        var result = await ElevationHelper.EnsureElevatedAsync(
            ["verify", "x"], elevationRequired: false,
            isElevated: () => throw new Xunit.Sdk.XunitException("must not probe"),
            relaunch: NeverRelaunch());

        Assert.Equal(ElevationOutcome.NotRequired, result.Outcome);
        Assert.True(result.CanProceed);
    }

    [Fact]
    public async Task EnsureElevated_RequiredAndAlreadyElevated_ReturnsAlreadyElevated()
    {
        var result = await ElevationHelper.EnsureElevatedAsync(
            [], elevationRequired: true, isElevated: () => true, relaunch: NeverRelaunch());

        Assert.Equal(ElevationOutcome.AlreadyElevated, result.Outcome);
        Assert.True(result.CanProceed);
    }

    [Fact]
    public async Task EnsureElevated_RelaunchStarts_ReturnsRelaunched_AndForwardsArgs()
    {
        string[]? forwarded = null;
        var args = new[] { "verify", "m.sha256", "--vss" };

        var result = await ElevationHelper.EnsureElevatedAsync(
            args, true, isElevated: () => false,
            relaunch: a => { forwarded = a; return Task.FromResult(true); });

        Assert.Equal(ElevationOutcome.Relaunched, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Same(args, forwarded);
    }

    [Fact]
    public async Task EnsureElevated_UserDeclines_ReturnsDeclined()
    {
        var result = await ElevationHelper.EnsureElevatedAsync(
            [], true, isElevated: () => false, relaunch: _ => Task.FromResult(false));

        Assert.Equal(ElevationOutcome.Declined, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task EnsureElevated_RelaunchThrows_ReturnsFailed_WithError()
    {
        var boom = new InvalidOperationException("boom");

        var result = await ElevationHelper.EnsureElevatedAsync(
            [], true, isElevated: () => false, relaunch: _ => throw boom);

        Assert.Equal(ElevationOutcome.Failed, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Same(boom, result.Error);
    }
}
