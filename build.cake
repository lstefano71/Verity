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

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
.Does(() => {
   Information("Hello Cake!");
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

RunTarget(target);