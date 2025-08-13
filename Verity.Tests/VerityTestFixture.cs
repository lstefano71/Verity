using System.Diagnostics;

public class ProcessResult
{
  public int ExitCode { get; set; }
  public string StdOut { get; set; } = "";
  public string StdErr { get; set; } = "";
}

public class VerityTestFixture : IAsyncLifetime, IDisposable
{
  public string TempDir { get; private set; } = "";
  public VerityTestFixture()
  {
    TempDir = Path.Combine(Path.GetTempPath(), "VerityTest_" + Guid.NewGuid());
    Directory.CreateDirectory(TempDir);
  }

  public string CreateTestFile(string relativePath, string content)
  {
    var fullPath = Path.Combine(TempDir, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, content);
    return fullPath;
  }

  public string GetManifestPath(string extension = "md5")
  {
    return Path.Combine(TempDir, $"manifest.{extension}");
  }

  public void CreateManifest(string extension, params (string hash, string file)[] entries)
  {
    var manifestPath = GetManifestPath(extension);
    var lines = entries.Select(e => $"{e.hash}\t{e.file}");
    File.WriteAllLines(manifestPath, lines);
  }

  public void ModifyTestFile(string relativePath, string newContent)
  {
    var fullPath = Path.Combine(TempDir, relativePath);
    File.WriteAllText(fullPath, newContent);
  }

  public void DeleteTestFile(string relativePath)
  {
    var fullPath = Path.Combine(TempDir, relativePath);
    if (File.Exists(fullPath)) File.Delete(fullPath);
  }

  public string GetFullPath(string relativePath)
  {
    return Path.Combine(TempDir, relativePath);
  }

  public async Task<ProcessResult> RunVerity(string args)
  {
    // Use the main Verity.exe from Verity\bin\Debug\net9.0
    // Use the correct absolute path for Verity.exe
    // Use the workspace root to construct the path to Verity.exe
    // Dynamically search for Verity.exe in candidate locations
    // Go up to workspace root, then down into Verity/bin/... for Verity.exe
    var testBaseDir = AppContext.BaseDirectory;
    // Go up four levels: net9.0 -> Debug -> bin -> Verity.Tests -> workspace root
    var workspaceRoot = Path.GetFullPath(Path.Combine(testBaseDir, "..", "..", "..", ".."));
    var verityProjectDir = Path.Combine(workspaceRoot, "Verity");
    var candidatePaths = new[] {
    Path.Combine(verityProjectDir, "bin", "Release", "net9.0", "publish", "Verity.exe"),
    Path.Combine(verityProjectDir, "bin", "Release", "net9.0", "Verity.exe"),
    Path.Combine(verityProjectDir, "bin", "Debug", "net9.0", "Verity.exe")
  };
    string? exePath = candidatePaths
      .Where(File.Exists)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();
    if (exePath == null)
      throw new FileNotFoundException($"Verity.exe not found in any candidate location: {string.Join("; ", candidatePaths)}");
    var psi = new ProcessStartInfo {
      FileName = exePath,
      Arguments = args,
      WorkingDirectory = TempDir,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var proc = Process.Start(psi)!;
    var stdOut = await proc.StandardOutput.ReadToEndAsync();
    var stdErr = await proc.StandardError.ReadToEndAsync();
    proc.WaitForExit();
    return new ProcessResult {
      ExitCode = proc.ExitCode,
      StdOut = stdOut,
      StdErr = stdErr
    };
  }

  public async Task InitializeAsync()
  {
    // Remove cleanup logic from fixture
    await Task.CompletedTask;
  }
  public Task DisposeAsync()
  {
    if (Directory.Exists(TempDir))
      Directory.Delete(TempDir, true);
    return Task.CompletedTask;
  }
  public void Dispose()
  {
    if (Directory.Exists(TempDir))
      Directory.Delete(TempDir, true);
    GC.SuppressFinalize(this);
  }
}