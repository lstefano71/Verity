public class UtilitiesTests
{
  [Fact]
  public void BuildHeaderPanel_ReturnsPanelWithExpectedContent()
  {
    var title = "Test Title";
    var startTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    var manifestName = "manifest.txt";
    var algorithm = "SHA256";
    var root = "C:\\root";
    var includeGlobs = new[] { "*.txt", "*.md" };
    var excludeGlobs = new[] { "*.log" };
    var panel = Utilities.BuildHeaderPanel(title, startTime, manifestName, algorithm, root, includeGlobs, excludeGlobs);
    Assert.NotNull(panel);
    Assert.NotNull(panel.Header);
    Assert.Contains(title, panel.Header.Text);
    Assert.Contains("[bold", panel.Header.Text); // Header uses bold markup

    // Use Spectre.Console.Testing's TestConsole to render and assert output
    var console = new Spectre.Console.Testing.TestConsole();
    console.Write(panel);
    var output = console.Output;
    Assert.Contains(manifestName, output);
    Assert.Contains(algorithm, output);
    Assert.Contains(root, output);

    // Assert the output contains the expected structure (plain text, not markup)
    Assert.Contains("Version:", output);
    Assert.Contains("Started:", output);
    Assert.Contains("Manifest:", output);
    Assert.Contains("Algorithm:", output);
    Assert.Contains("Root:", output);
    Assert.Contains("Include:", output);
    Assert.Contains("Exclude:", output);

    // Assert the values are present and in the correct order
    var expectedOrder = new[] {
      "Version:",
      "Started:",
      "Manifest:",
      "Algorithm:",
      "Root:",
      "Include:",
      "Exclude:"
    };
    var lastIndex = -1;
    foreach (var label in expectedOrder) {
      var idx = output.IndexOf(label);
      Assert.True(idx > lastIndex);
      lastIndex = idx;
    }

    // Assert specific values
    Assert.Contains(manifestName, output);
    Assert.Contains(algorithm, output);
    Assert.Contains(root, output);
    Assert.Contains("*.txt", output);
    Assert.Contains("*.log", output);
  }
}