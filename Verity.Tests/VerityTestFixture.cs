using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

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
        var exePath = Path.Combine(AppContext.BaseDirectory, "Verity.exe");
        var psi = new ProcessStartInfo
        {
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
        return new ProcessResult
        {
            ExitCode = proc.ExitCode,
            StdOut = stdOut,
            StdErr = stdErr
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;
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
    }
}