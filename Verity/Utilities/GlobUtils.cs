using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

using Spectre.Console;

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
