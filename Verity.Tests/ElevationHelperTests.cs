using System.Runtime.InteropServices;

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

    private static Func<string[], Task<int?>> NeverRelaunch() =>
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
            relaunch: a => { forwarded = a; return Task.FromResult<int?>(0); });

        Assert.Equal(ElevationOutcome.Relaunched, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Same(args, forwarded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(102)]
    public async Task EnsureElevated_Relaunched_CarriesChildExitCode(int childExitCode)
    {
        var result = await ElevationHelper.EnsureElevatedAsync(
            [], true, isElevated: () => false, relaunch: _ => Task.FromResult<int?>(childExitCode));

        Assert.Equal(ElevationOutcome.Relaunched, result.Outcome);
        Assert.Equal(childExitCode, result.ExitCode);
    }

    [Fact]
    public async Task EnsureElevated_UserDeclines_ReturnsDeclined()
    {
        var result = await ElevationHelper.EnsureElevatedAsync(
            [], true, isElevated: () => false, relaunch: _ => Task.FromResult<int?>(null));

        Assert.Equal(ElevationOutcome.Declined, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Null(result.Error);
        Assert.Null(result.ExitCode);
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

    // --- Command line of the elevated child: must survive the Windows argument parser intact.

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Parses <paramref name="arguments"/> exactly as the child process will.</summary>
    private static string[] ParseLikeWindows(string arguments)
    {
        // CommandLineToArgvW treats the first token as the program name, with different rules.
        var argv = CommandLineToArgvW("prog.exe " + arguments, out var count);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            return Enumerable.Range(1, count - 1)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with space")]
    [InlineData("")]
    [InlineData("with\ttab")]
    [InlineData("with\"quote")]
    [InlineData("\"already quoted\"")]
    [InlineData(@"C:\Program Files\")]          // trailing backslash must not swallow the closing quote
    [InlineData(@"C:\dir\\")]
    [InlineData(@"back\\""slash quote")]
    [InlineData(@"\\server\share\path")]
    public void EscapeArgument_RoundTripsThroughWindowsParser(string argument)
    {
        Assert.Equal([argument], ParseLikeWindows(ElevationHelper.EscapeArgument(argument)));
    }

    [Fact]
    public void BuildArguments_RoundTrips_AndAppendsChildMarker()
    {
        var args = new[] { "verify", @"C:\Program Files\m.sha256", "--root", @"C:\Program Files\", "--vss" };

        Assert.Equal([.. args, ElevationHelper.ElevatedChildMarker], ParseLikeWindows(ElevationHelper.BuildArguments(args)));
    }
}
