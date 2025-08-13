using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

using Spectre.Console;

public static class Utilities
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

public static class GlobUtils
{
  // Normalizes a semicolon-separated glob string into an array of patterns
  public static string[] NormalizeGlobs(string? globs, bool isExclude = false)
  {
    if (string.IsNullOrWhiteSpace(globs))
      return isExclude ? [] : ["**/*"];
    return globs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }

  // Returns true if the file (relative to root) matches the include/exclude globs
  public static bool IsMatch(string relativePath, string[]? includeGlobs, string[]? excludeGlobs)
  {
    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
    if (includeGlobs is { Length: > 0 })
      matcher.AddIncludePatterns(includeGlobs);
    else
      matcher.AddInclude("*");
    if (excludeGlobs is { Length: > 0 })
      matcher.AddExcludePatterns(excludeGlobs);
    var matchResult = matcher.Match(relativePath);
    return matchResult.HasMatches;
  }

  // Filters a list of files (full paths) to those matching the globs, returning relative paths
  public static List<string> FilterFiles(IEnumerable<string> files, string root, string[]? includeGlobs, string[]? excludeGlobs)
  {
    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
    if (includeGlobs is { Length: > 0 })
      matcher.AddIncludePatterns(includeGlobs);
    else
      matcher.AddInclude("**/*");
    if (excludeGlobs is { Length: > 0 })
      matcher.AddExcludePatterns(excludeGlobs);
    var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(root));
    var matchResult = matcher.Execute(dirInfo);
    // Normalize path separators for matching
    static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    var matched = new HashSet<string>(matchResult.Files.Select(f => Normalize(f.Path)), StringComparer.OrdinalIgnoreCase);
    var relFiles = files.Select(f => Normalize(Path.GetRelativePath(root, f)));
    return [.. relFiles.Where(matched.Contains)];
  }
}
