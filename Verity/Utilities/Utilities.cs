using Spectre.Console;

public static class Utilities
{
  public static string AbbreviatePathForDisplay(string path, int maxLength = 40)
  {
    path = path?.Trim();
    if (string.IsNullOrWhiteSpace(path)) return path;

    if (path.Length <= maxLength) return path;
    if (maxLength < 4) return path[0] + ".." + path[1];

    // Detect drive letter and colon (Windows style)
    string drive = "";
    string rest = path;
    if (path.Length > 2 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
    {
        drive = path[..3]; // e.g. "C:\"
        rest = path[3..];
    }

    var dir = Path.GetDirectoryName(rest) ?? "";
    var file = Path.GetFileName(rest); // Use full filename for display
    if (string.IsNullOrWhiteSpace(file)) return "";

    // Always produce C:\...\file.txt style for long paths
    if (dir.Length > 0 && (drive + dir + Path.DirectorySeparatorChar + file).Length > maxLength)
    {
        // Show drive, ellipsis, then filename
        string abbreviated = $"{drive}...{Path.DirectorySeparatorChar}{file}";
        if (abbreviated.Length > maxLength)
            abbreviated = abbreviated[..maxLength];
        return abbreviated;
    }

    string result = drive + (dir.Length > 0 ? dir + Path.DirectorySeparatorChar + file : file);
    if (result.Length > maxLength)
      result = result[..maxLength];
    return result;
  }

  // Pads the abbreviated path with '▪' to the left if needed, then escapes for Spectre.Console
  public static string AbbreviateAndPadPathForDisplay(string path, int padLen = 50)
  {
    var abbreviated = AbbreviatePathForDisplay(path, padLen);
    int padCount = padLen - abbreviated.Length;
    if (padCount > 0) abbreviated = new string('▪', padCount) + abbreviated;
    return Spectre.Console.Markup.Escape(abbreviated);
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
