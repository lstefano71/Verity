#load "./CakeHelpers.cake"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

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

Task("Build")
.Does(() => {
   DotNetBuild("./Verity.csproj", new DotNetBuildSettings {
      Configuration = "Release"
   });
   Information("Project compiled in Release mode.");
});

Task("AOT-Compile")
.Does(() => {
   DotNetPublish("./Verity.csproj", new DotNetPublishSettings {
      Configuration = "Release",
      ArgumentCustomization = args => args
         .Append("/p:PublishAot=true")
         .Append("/p:PublishTrimmed=true")
         .Append("-r win-x64") // Change runtime identifier as needed
   });
   Information("AOT compilation completed in Release mode.");
});

Task("Run-Tests")
.Does(() => {
   var verityExe = System.IO.Path.Combine(Directory("./bin/Debug/net9.0"), "Verity.exe");
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