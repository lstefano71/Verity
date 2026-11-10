using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Verity.Utilities;

/// <summary>
/// Terminal outcomes of the start-up elevation check.
/// <code>
/// Start ─ not required ───────────────────────► NotRequired
///   │
///   └ required ─ already elevated ────────────► AlreadyElevated
///        │
///        └ not elevated ─ elevated run finished ─► Relaunched  (caller exits with the child's exit code)
///             │          ─ UAC prompt declined ──► Declined    (caller exits ElevationRequired)
///             │          ─ error ────────────────► Failed      (caller exits ElevationRequired)
/// </code>
/// </summary>
public enum ElevationOutcome
{
    NotRequired,
    AlreadyElevated,
    Relaunched,
    Declined,
    Failed
}

/// <summary>Result of <see cref="ElevationHelper.EnsureElevatedAsync"/>.</summary>
/// <param name="Outcome">The terminal outcome.</param>
/// <param name="Error">The exception for <see cref="ElevationOutcome.Failed"/>, otherwise null.</param>
/// <param name="ExitCode">The elevated process's exit code for <see cref="ElevationOutcome.Relaunched"/>, otherwise null.</param>
public readonly record struct ElevationResult(ElevationOutcome Outcome, Exception? Error = null, int? ExitCode = null)
{
    /// <summary>True when the current process may continue with the requested work.</summary>
    public bool CanProceed => Outcome is ElevationOutcome.NotRequired or ElevationOutcome.AlreadyElevated;
}

/// <summary>
/// Provides utilities for handling UAC elevation requirements for VSS operations.
/// </summary>
public static class ElevationHelper
{
    /// <summary>
    /// Appended to the arguments of the elevated relaunch so the child knows it runs in its own
    /// console window (it pauses before closing) and never tries to relaunch itself again.
    /// <see cref="Program.Main"/> strips it before the arguments are parsed.
    /// </summary>
    public const string ElevatedChildMarker = "--verity-elevated-child";

    /// <summary>
    /// Runs the whole elevation decision once and reports the outcome; no side effects other
    /// than the relaunch itself. The probes are injectable so the decision table is testable.
    /// </summary>
    /// <param name="args">The original command line arguments, forwarded to the relaunched process.</param>
    /// <param name="elevationRequired">Whether the requested work needs administrator rights.</param>
    /// <param name="isElevated">Probe for the current elevation; defaults to <see cref="IsElevated"/>.</param>
    /// <param name="relaunch">
    /// Relaunch action returning the elevated process's exit code, or null if the user declined;
    /// defaults to <see cref="RestartElevatedAsync"/>.
    /// </param>
    public static async Task<ElevationResult> EnsureElevatedAsync(
        string[] args,
        bool elevationRequired,
        Func<bool>? isElevated = null,
        Func<string[], Task<int?>>? relaunch = null)
    {
        if (!elevationRequired)
            return new ElevationResult(ElevationOutcome.NotRequired);

        if ((isElevated ?? IsElevated)())
            return new ElevationResult(ElevationOutcome.AlreadyElevated);

        try
        {
            var exitCode = await (relaunch ?? RestartElevatedAsync)(args);
            return exitCode is int code
                ? new ElevationResult(ElevationOutcome.Relaunched, ExitCode: code)
                : new ElevationResult(ElevationOutcome.Declined);
        }
        catch (Exception ex)
        {
            return new ElevationResult(ElevationOutcome.Failed, ex);
        }
    }

    /// <summary>
    /// Determines if the current process is running with elevated privileges.
    /// </summary>
    /// <returns>True if the process is elevated, false otherwise.</returns>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we can't determine elevation status, assume not elevated
            return false;
        }
    }

    /// <summary>
    /// Determines if VSS mode requires elevation based on the provided options.
    /// </summary>
    /// <param name="options">The CLI options to check.</param>
    /// <returns>True if elevation is required, false otherwise.</returns>
    public static bool RequiresElevation(CliOptions options)
    {
        return options.UseVss;
    }

    /// <summary>
    /// Runs the application again with elevated privileges (in its own console window, as UAC
    /// requires) and waits for it to finish.
    /// </summary>
    /// <param name="originalArgs">The original command line arguments.</param>
    /// <returns>The elevated process's exit code, or null if the user declined the UAC prompt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="originalArgs"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The elevated process could not be started.</exception>
    /// <exception cref="Win32Exception">Starting the process failed for a reason other than the user declining.</exception>
    public static async Task<int?> RestartElevatedAsync(string[] originalArgs)
    {
        ArgumentNullException.ThrowIfNull(originalArgs);

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            throw new InvalidOperationException("Unable to determine current process path for elevation restart.");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = BuildArguments(originalArgs),
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        Process? process;
        try
        {
            process = Process.Start(processInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return null; // user declined the UAC prompt
        }

        if (process == null)
            throw new InvalidOperationException("The elevated process could not be started.");

        using (process)
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }

    /// <summary>The command line for the elevated child: the original arguments plus <see cref="ElevatedChildMarker"/>.</summary>
    internal static string BuildArguments(string[] originalArgs) =>
        string.Join(" ", originalArgs.Append(ElevatedChildMarker).Select(EscapeArgument));

    /// <summary>
    /// Quotes one argument so that the Windows command-line parser (CommandLineToArgvW / the C
    /// runtime) reads it back unchanged. Backslashes are literal except directly before a quote,
    /// so runs of backslashes preceding a quote, or the closing quote, are doubled.
    /// </summary>
    /// <param name="argument">The argument to escape.</param>
    /// <returns>The escaped argument.</returns>
    internal static string EscapeArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var sb = new System.Text.StringBuilder(argument.Length + 2).Append('"');
        int backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
                sb.Append('\\', backslashes * 2 + 1);
            else
                sb.Append('\\', backslashes);
            sb.Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        return sb.Append('"').ToString();
    }
}