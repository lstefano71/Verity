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
}
