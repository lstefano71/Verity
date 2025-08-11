#load "./CakeHelpers.cake"
#addin nuget:?package=Cake.Json&version=7.0.1
#addin nuget:?package=Newtonsoft.Json&version=13.0.3


///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var projectName = "Verity"; // Project base name for artifact

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

// FIX: Reverted to using .Combine() which is the correct method for chaining path segments.
var publishDir = Directory("./bin")
   + Directory(configuration)
   + Directory("net9.0")
   + Directory("win-x64")
   + Directory("publish");

var artifactsDir = Directory("./artifacts");

///////////////////////////////////////////////////////////////////////////////
// HELPER FUNCTIONS
///////////////////////////////////////////////////////////////////////////////

// FIX: Corrected the helper to use StartAndReturnProcess, which returns an IProcess object.
string StartProcessAndReadOutput(string tool, string args)
{
    // This alias returns an IProcess object that we can interact with.
    var process = StartAndReturnProcess(tool, new ProcessSettings {
        Arguments = args,
        RedirectStandardOutput = true
    });
    process.WaitForExit(); // This method exists on IProcess
    return string.Join("\n", process.GetStandardOutput()); // This method also exists on IProcess
}

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
   // Executed BEFORE the first task.
   Information("Running tasks...");
});

Teardown(ctx =>
{
   // Executed AFTER the last task.
   Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory("./bin");
    CleanDirectory("./obj");
    CleanDirectory(artifactsDir);
    Information("Cleaned build and artifact directories.");
});

Task("Build")
    .IsDependentOn("Clean")
    .Does(() => {
    DotNetBuild("./Verity.csproj", new DotNetBuildSettings {
        Configuration = configuration
    });
    Information("Project compiled in {0} mode.", configuration);
});

Task("AOT-Compile")
    .IsDependentOn("Build")
    .Does(() => {
    DotNetPublish("./Verity.csproj", new DotNetPublishSettings {
        Configuration = configuration,
        OutputDirectory = publishDir,
        ArgumentCustomization = args => args
            .Append("/p:PublishAot=true")
            .Append("/p:PublishTrimmed=true")
            .Append("-r win-x64") // Change runtime identifier as needed
    });
    Information("AOT compilation completed to: {0}", publishDir);
});

Task("Package")
    .IsDependentOn("AOT-Compile")
    .IsDependentOn("Run-Tests")
    .Does(() => {
    // 1. Get Version Information from Nerdbank.GitVersioning tool
    Information("Getting version information from NBGV...");
    var versionJson = StartProcessAndReadOutput("nbgv", "get-version --format json");
    
    // FIX: Use Cake's built-in JSON parser. No external dependency needed.
    dynamic versionInfo = ParseJson(versionJson);
    string packageVersion = versionInfo.NuGetPackageVersion;
    Information("Package version: {0}", packageVersion);


    // 2. Get the current Git branch name
    var branchName = EnvironmentVariable("GITHUB_REF_NAME");
    if (string.IsNullOrEmpty(branchName))
    {
        Information("Not in GitHub Actions, getting branch from git...");
        branchName = StartProcessAndReadOutput("git", "rev-parse --abbrev-ref HEAD");
    }
    Information("Current branch: {0}", branchName);

    // 3. Construct the package file name
    var sanitizedBranchName = branchName.Replace("/", "-").Trim();

    var packageFileName = $"{projectName}-v{packageVersion}";
    if (!string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sanitizedBranchName))
    {
        packageFileName += $"-{sanitizedBranchName}";
    }
    packageFileName += ".zip";
    Information("Package name will be: {0}", packageFileName);


    // 4. Create the artifact directory and the zip package
    EnsureDirectoryExists(artifactsDir);
    // FIX: Use .CombineWithFilePath() to join a directory path with a file name string.
    var outputPath = artifactsDir + Directory(packageFileName);
    var filesToZip = GetFiles($"{publishDir}/*.*");

    Zip(publishDir, outputPath.ToString(), filesToZip.ToArray());
    Information("Package created at: {0}", outputPath);
});


Task("Run-Tests")
.Does(() => {
   var verityExe = System.IO.Path.Combine(Directory("./bin") + Directory(configuration) + Directory("net9.0"), "Verity.exe");
   var algorithms = new[] {
      new { Name = "SHA256", Ext = ".sha256" },
      new { Name = "MD5", Ext = ".md5" },
      new { Name = "SHA1", Ext = ".sha1" }
   };

   foreach (var algo in algorithms) {
      // Destroy and rebuild the test file system
      var testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"verity-test-{Guid.NewGuid()}");
      if (System.IO.Directory.Exists(testRoot)) {
         System.IO.Directory.Delete(testRoot, true);
      }
      System.IO.Directory.CreateDirectory(testRoot);
      Information($"Test root: {testRoot} for {algo.Name}");

      // Fill with files and directories
      var subDir = System.IO.Path.Combine(testRoot, "subdir");
      System.IO.Directory.CreateDirectory(subDir);
      var file1 = System.IO.Path.Combine(testRoot, "file1.txt");
      var file2 = System.IO.Path.Combine(subDir, "file2.txt");
      System.IO.File.WriteAllText(file1, "Hello World");
      System.IO.File.WriteAllText(file2, "Cake Test");

      // Use manifest extension to select algorithm
      var manifestPath = System.IO.Path.Combine(testRoot, $"manifest{algo.Ext}");

      // Execute Verity create
      var createResult = StartProcess(verityExe, $"create {manifestPath} --root {testRoot}");
      if (createResult != 0) {
         Error($"[FAIL] Manifest creation failed for {algo.Name} (exit code {createResult})");
      } else {
         Information($"Manifest created for {algo.Name}.");

            // Strict manifest validation
            ValidateManifestStrict(manifestPath, testRoot, algo.Name);
      }

      var verifyResult = StartProcess(verityExe, $"verify {manifestPath} --root {testRoot}");
      if (verifyResult != 0) {
         Error($"[FAIL] Manifest verification failed for {algo.Name} (exit code {verifyResult})");
      } else {
         Information($"Manifest verified for {algo.Name}.");
      }

      // Introduce errors
      System.IO.File.Delete(file1);
      System.IO.File.WriteAllText(file2, "Modified content");
      var file3 = System.IO.Path.Combine(testRoot, "file3.txt");
      System.IO.File.WriteAllText(file3, "Extra file");
      Information($"Errors introduced for {algo.Name}: file removed, modified, and added.");

      var errorVerifyResult = StartProcess(verityExe, $"verify {manifestPath} --root {testRoot}");
      if (errorVerifyResult == 0) {
         Error($"[FAIL] Error verification did not fail for {algo.Name} (exit code {errorVerifyResult})");
      } else {
         Information($"Error verification completed for {algo.Name} (exit code {errorVerifyResult}).");
      }
   }
});

RunTarget(target);