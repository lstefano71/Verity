using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Testing;

public class ProcessResult
{
  public int ExitCode { get; set; }
  public string StdOut { get; set; } = "";
  public string StdErr { get; set; } = "";
}

public class CommonTestFixture : IAsyncLifetime, IDisposable
{
  public bool DebugOutputEnabled { get; set; } = false;
  public string TempDir { get; private set; } = "";
  public CommonTestFixture()
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
    // Parse args as if from CLI, but run Program methods directly.
    // All file paths are resolved as if from TempDir, but we do NOT change the process current directory.
    var stdOut = new StringWriter();
    var stdErr = new StringWriter();
    var origOut = Console.Out;
    var origErr = Console.Error;
    var testConsole = new TestConsole();
    int exitCode = -999;
    try
    {
      Console.SetOut(stdOut);
      Console.SetError(stdErr);
      AnsiConsole.Console = testConsole;
      // Split args respecting quotes
      var argList = SimpleArgSplitter.Split(args);
      if (argList.Length == 0)
        throw new ArgumentException("No command specified");
      var command = argList[0].ToLowerInvariant();
      var commandArgs = argList.Skip(1).ToArray();
      // Helper to get value for an option
      string? GetOpt(string name)
      {
        for (int i = 0; i < commandArgs.Length; i++)
        {
          if (commandArgs[i] == name && i + 1 < commandArgs.Length)
            return commandArgs[i + 1];
          if (commandArgs[i].StartsWith(name + "=", StringComparison.Ordinal))
            return commandArgs[i].Substring(name.Length + 1).Trim('"');
        }
        return null;
      }
      bool HasOpt(string name) => commandArgs.Contains(name);
      // Helper to resolve a path as absolute if not already
      string? Abs(string? path) => string.IsNullOrEmpty(path) ? path : Path.IsPathRooted(path) ? path : Path.Combine(TempDir, path);
      // Map positional and named args, resolving all paths relative to TempDir
      switch (command)
      {
        case "verify":
        {
          string checksumFile = commandArgs.Length > 0 ? Abs(commandArgs[0])! : throw new ArgumentException("Missing manifest file");
          string? root = Abs(GetOpt("--root"));
          string? algorithm = GetOpt("--algorithm");
          int? threads = int.TryParse(GetOpt("--threads"), out var t) ? t : null;
          string? tsvReport = Abs(GetOpt("--tsv-report"));
          bool showTable = HasOpt("--show-table");
          string? include = GetOpt("--include");
          string? exclude = GetOpt("--exclude");
          exitCode = await new Program().Verify(
            checksumFile,
            root,
            algorithm,
            threads,
            tsvReport,
            showTable,
            include,
            exclude,
            CancellationToken.None
          );
          break;
        }
        case "create":
        {
          string outputManifest = commandArgs.Length > 0 ? Abs(commandArgs[0])! : throw new ArgumentException("Missing output manifest");
          string? root = Abs(GetOpt("--root"));
          string? algorithm = GetOpt("--algorithm");
          int? threads = int.TryParse(GetOpt("--threads"), out var t) ? t : null;
          string? include = GetOpt("--include");
          string? exclude = GetOpt("--exclude");
          bool showTable = HasOpt("--show-table");
          string? tsvReport = Abs(GetOpt("--tsv-report"));
          exitCode = await new Program().Create(
            outputManifest,
            root,
            algorithm,
            threads,
            include,
            exclude,
            showTable,
            tsvReport,
            CancellationToken.None
          );
          break;
        }
        case "add":
        {
          string manifestPath = commandArgs.Length > 0 ? Abs(commandArgs[0])! : throw new ArgumentException("Missing manifest file");
          string? root = Abs(GetOpt("--root"));
          string? algorithm = GetOpt("--algorithm");
          int? threads = int.TryParse(GetOpt("--threads"), out var t) ? t : null;
          string? include = GetOpt("--include");
          string? exclude = GetOpt("--exclude");
          bool showTable = HasOpt("--show-table");
          string? tsvReport = Abs(GetOpt("--tsv-report"));
          exitCode = await new Program().Add(
            manifestPath,
            root,
            algorithm,
            threads,
            include,
            exclude,
            showTable,
            tsvReport,
            CancellationToken.None
          );
          break;
        }
        default:
          throw new ArgumentException($"Unknown command: {command}");
      }
    }
    catch (Exception ex)
    {
      stdErr.WriteLine(ex.ToString());
      exitCode = -999;
    }
    finally
    {
      Console.SetOut(origOut);
      Console.SetError(origErr);
      // No SystemConsole in Spectre.Console.Testing, so just set to a new TestConsole to avoid null
      AnsiConsole.Console = new TestConsole();
    }
    if (DebugOutputEnabled)
    {
      Console.WriteLine("STDOUT:\n" + stdOut.ToString());
      Console.WriteLine("STDERR:\n" + stdErr.ToString());
    }
    return new ProcessResult
    {
      ExitCode = exitCode,
      StdOut = stdOut.ToString() + testConsole.Output,
      StdErr = stdErr.ToString()
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

  public static string Md5(string input)
  {
    var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
    return string.Concat(hash.Select(b => b.ToString("x2")));
  }

  public static string Sha256(string input)
  {
    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
    return string.Concat(hash.Select(b => b.ToString("x2")));
  }
}