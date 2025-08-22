using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Verity.Utilities;

/// <summary>
/// Provides utilities for handling UAC elevation requirements for VSS operations.
/// </summary>
public static class ElevationHelper
{
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
    /// Restarts the application with elevated privileges using the original command line arguments.
    /// </summary>
    /// <param name="originalArgs">The original command line arguments.</param>
    /// <returns>True if the restart was initiated successfully, false if the user declined elevation.</returns>
    public static async Task<bool> RestartElevatedAsync(string[] originalArgs)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            throw new InvalidOperationException("Unable to determine current process path for elevation restart.");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = string.Join(" ", originalArgs.Select(EscapeArgument)),
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        try
        {
            var process = Process.Start(processInfo);
            if (process != null)
            {
                // Wait for the elevated process to start successfully
                await Task.Delay(1000);
                return true;
            }
            return false;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            // User declined the elevation prompt
            return false;
        }
        catch (Exception)
        {
            // Other errors starting the elevated process
            return false;
        }
    }

    /// <summary>
    /// Escapes a command line argument to handle spaces and special characters.
    /// </summary>
    /// <param name="argument">The argument to escape.</param>
    /// <returns>The escaped argument.</returns>
    private static string EscapeArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        if (!argument.Contains(' ') && !argument.Contains('"') && !argument.Contains('\t'))
            return argument;

        return "\"" + argument.Replace("\"", "\\\"") + "\"";
    }
}