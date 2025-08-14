using Spectre.Console;

public static class Utilities
{
  /// <summary>
  /// Abbreviates a file system path for display by inserting ellipses in the middle of the path if it exceeds a specified maximum length.

  /// </summary>
  /// <param name="path">The path to abbreviate. The function handles null/whitespace and trims the input.</param>
  /// <param name="maxLength">The maximum character length of the resulting string. Defaults to 40.</param>
  /// <returns>
  /// The abbreviated path, or the original path if it's within the maxLength.
  /// Returns null if the input path is null, empty, or whitespace.
  /// The abbreviated path will be exactly maxLength characters long, with ellipses inserted as needed.
  /// </returns>
  public static string? AbbreviatePathForDisplay(string? path, int maxLength = 40)
  {
    path = path?.Trim();
    if (string.IsNullOrWhiteSpace(path)) return path;
    if (path.Length <= maxLength) return path;
    var ellipsis = "...";
    if (maxLength <= ellipsis.Length) return path[^maxLength..];

    var leftSegment = (maxLength - ellipsis.Length) / 2;
    var rightSegment = maxLength - leftSegment - ellipsis.Length;
    return path[..leftSegment] + ellipsis + path[^rightSegment..];
  }


  // Pads the abbreviated path with '▪' to the left if needed, then escapes for Spectre.Console
  public static string AbbreviateAndPadPathForDisplay(string path, int padLen = 50)
  {
    var abbreviated = AbbreviatePathForDisplay(path, padLen);
    int padCount = padLen - abbreviated.Length;
    if (padCount > 0) abbreviated = new string('▪', padCount) + abbreviated;
    return Markup.Escape(abbreviated);
  }

  // Helper to build a Spectre.Console Panel for header info
  public static Panel BuildHeaderPanel(string title, DateTime startTime, string manifestName, string algorithm, string root, string[]? includeGlobs = null, string[]? excludeGlobs = null)
  {
    var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
    var versionAttr = entryAssembly?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
      .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
      .FirstOrDefault();
    var version = versionAttr?.InformationalVersion ?? "0.0.0";
    var content =
      $"[bold]Version:[/] {version}\n[bold]Started:[/] {startTime.ToUniversalTime():yyyy-MM-dd HH:mm:ssZ}\n" +
      $"[bold]Manifest:[/] {manifestName}\n" +
      $"[bold]Algorithm:[/] {algorithm}\n[bold]Root:[/] {root}\n";
    if (includeGlobs is { Length: > 0 } && (includeGlobs.Length != 1 || includeGlobs[0] != "*"))
      content += $"[bold]Include:[/] {string.Join(", ", includeGlobs)}\n";
    if (excludeGlobs is { Length: > 0 })
      content += $"[bold]Exclude:[/] {string.Join(", ", excludeGlobs)}\n";
    return new Panel(content)
      .Header($"[bold]{title}[/]", Justify.Center)
      .Expand();
  }
}
