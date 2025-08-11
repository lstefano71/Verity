using Spectre.Console;

public static class PathUtils
{
  // Abbreviate a path to a fixed length (default 40 chars) for display
  public static string AbbreviatePathForDisplay(string path, int maxLength = 40)
  {
    if (string.IsNullOrEmpty(path) || path.Length <= maxLength) return path;
    var dir = Path.GetDirectoryName(path) ?? "";
    var file = Path.GetFileNameWithoutExtension(path);
    var ext = Path.GetExtension(path);
    static string middleTrunc(string s, int len)
    {
      if (s.Length <= len) return s;
      int keep = len - 3;
      int left = keep / 2;
      int right = keep - left;
      return string.Concat(s.AsSpan(0, left), "...", s.AsSpan(s.Length - right));
    }
    string result = dir.Length > 0 ? dir + Path.DirectorySeparatorChar + file + ext : file + ext;
    if (result.Length <= maxLength) return result;
    result = middleTrunc(result, maxLength);
    if (result.Length <= maxLength) return result;
    string dirTrunc = middleTrunc(dir, Math.Max(0, maxLength - (file.Length + ext.Length + 1)));
    result = dirTrunc + Path.DirectorySeparatorChar + file + ext;
    if (result.Length <= maxLength) return result;
    string fileTrunc = middleTrunc(file, Math.Max(0, maxLength - (dirTrunc.Length + ext.Length + 1)));
    result = dirTrunc + Path.DirectorySeparatorChar + fileTrunc + ext;
    if (result.Length <= maxLength) return result;
    string extTrunc = ext.Length > 0 ? middleTrunc(ext, Math.Max(0, maxLength - (dirTrunc.Length + fileTrunc.Length + 1))) : "";
    result = dirTrunc + Path.DirectorySeparatorChar + fileTrunc + extTrunc;
    if (result.Length <= maxLength) return result;
    return string.Concat(result.AsSpan(0, maxLength - 3), "...");
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
  public static Panel BuildHeaderPanel(string title, string version, DateTime startTime, string manifestName, string algorithm, string root)
  {
    var content =
      $"[bold]Version:[/] {version}\n[bold]Started:[/] {startTime:yyyy-MM-dd HH:mm:ss}\n" +
      $"[bold]Manifest:[/] {manifestName}\n" +
      $"[bold]Algorithm:[/] {algorithm}\n[bold]Root:[/] {root}\n";
    return new Panel(content)
      .Header($"[bold]{title}[/]", Justify.Center)
      .Expand();
  }
}
