using FluentAssertions;

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
    panel.Should().NotBeNull();
    panel.Header.Should().NotBeNull();
    panel.Header.Text.Should().Contain(title);
    panel.Header.Text.Should().Contain("[bold"); // Header uses bold markup

    // Use Spectre.Console.Testing's TestConsole to render and assert output
    var console = new Spectre.Console.Testing.TestConsole();
    console.Write(panel);
    var output = console.Output;
    output.Should().Contain(manifestName);
    output.Should().Contain(algorithm);
    output.Should().Contain(root);

    // Assert the output contains the expected structure (plain text, not markup)
    output.Should().Contain("Version:", "Version label not found");
    output.Should().Contain("Started:", "Started label not found");
    output.Should().Contain("Manifest:", "Manifest label not found");
    output.Should().Contain("Algorithm:", "Algorithm label not found");
    output.Should().Contain("Root:", "Root label not found");
    output.Should().Contain("Include:", "Include label not found");
    output.Should().Contain("Exclude:", "Exclude label not found");

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
      idx.Should().BeGreaterThan(lastIndex, $"Label '{label}' is out of order");
      lastIndex = idx;
    }

    // Assert specific values
    output.Should().Contain(manifestName);
    output.Should().Contain(algorithm);
    output.Should().Contain(root);
    output.Should().Contain("*.txt");
    output.Should().Contain("*.log");
  }
}