using Spectre.Console;

using System.Text;

public static class Utilities
{
  /// <summary>
  /// Abbreviates a file system path for display by replacing middle components with an ellipsis.
  /// This method aims to replicate the behavior seen in Visual Studio's "Recent Files" menu.
  /// It should start by substituting the middle part of the path with an ellipsis ("...") if the path exceeds the specified maximum length.
  /// Eroding from the middle allows the path to remain recognizable while fitting within a limited space.
  /// It should eventually return a string that is no longer than the specified maximum length but is as close as possible to the maximum length.
  /// The erosion could leave nothing but the rightmost part of the path if the path is too long to even include an ellipsis.
  /// </summary>
  /// <param name="path">The path to abbreviate. The function handles null/whitespace and trims the input.</param>
  /// <param name="maxLength">The maximum character length of the resulting string. Defaults to 40.</param>
  /// <returns>
  /// The abbreviated path, or the original path if it's within the maxLength.
  /// Returns null if the input path is null, empty, or whitespace.
  /// The returned string will never exceed maxLength.
  /// </returns>
  public static string? AbbreviatePathForDisplay(string? path, int maxLength = 40)
  {
    if (string.IsNullOrWhiteSpace(path)) {
      return null;
    }

    path = path.Trim();

    if (path.Length <= maxLength) {
      return path;
    }

    const string ellipsis = "...";

    // Handle cases where maxLength is extremely small
    if (maxLength <= ellipsis.Length) {
      // Truncate from the left to preserve the end of the path
      return path.Substring(path.Length - maxLength);
    }

    string root = Path.GetPathRoot(path) ?? string.Empty;
    string filename = Path.GetFileName(path);

    // Handle a path that is just a filename (no directory part)
    if (root.Length == 0 && filename.Equals(path, StringComparison.Ordinal)) {
      // The filename is guaranteed to be > maxLength here.
      // Using string.Concat to cleanly handle the ReadOnlySpan<char> and avoid CS0019.
      return string.Concat(ellipsis, filename.AsSpan(filename.Length - (maxLength - ellipsis.Length)));
    }

    // If the root and filename with an ellipsis are already too long, we must abbreviate the filename.
    // This is a fallback for very long filenames on otherwise short paths.
    if (root.Length + ellipsis.Length + 1 + filename.Length > maxLength) {
      return string.Concat(ellipsis, filename.AsSpan(filename.Length - (maxLength - ellipsis.Length)));
    }

    string? dirPart = Path.GetDirectoryName(path);
    // If there's no directory part, we've already handled it or it should fit.
    // This check is for safety.
    if (string.IsNullOrEmpty(dirPart) || dirPart.Length <= root.Length) {
      return path;
    }

    // The core logic starts here. We work with the "middle" part of the path.
    string middle = dirPart.Substring(root.Length);

    char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
    var middleComponents = middle.Split(separators, StringSplitOptions.RemoveEmptyEntries);

    var rightBuilder = new StringBuilder(filename);

    // Add directory components from the end of the middle part to the "right" side,
    // as long as they fit within the maxLength.
    for (int i = middleComponents.Length - 1; i >= 0; i--) {
      // Check if adding the next component and a separator would exceed the limit
      if (root.Length + ellipsis.Length + 1 + middleComponents[i].Length + 1 + rightBuilder.Length > maxLength) {
        break;
      }

      // Prepend the separator and the component
      rightBuilder.Insert(0, separators[0]);
      rightBuilder.Insert(0, middleComponents[i]);
    }

    var finalPath = new StringBuilder(root);
    // For relative paths, the root is empty, so the path will correctly start with "..."
    if (root.Length > 0) {
      finalPath.Append(ellipsis);
      finalPath.Append(separators[0]);
    } else {
      finalPath.Append(ellipsis);
      finalPath.Append(separators[0]);
    }
    finalPath.Append(rightBuilder);

    return finalPath.ToString();
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
