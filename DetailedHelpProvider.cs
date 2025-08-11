using Spectre.Console;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

public class DetailedHelpProvider : IHelpProvider
{
  public IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
  {
    var output = new List<IRenderable> {
      new Markup("[bold underline]USAGE:[/]"),
      new Text($"    {model.ApplicationName} [OPTIONS] <COMMAND>\n"),
      new Text(""),
      new Markup("[bold underline]OPTIONS:[/]"),
      new Text("    -h, --help    Prints help information\n"),
      new Text(""),
      new Markup("[bold underline]COMMANDS:[/]"),
      new Text("")
    };
    foreach (var cmd in model.Commands) {
      if (cmd.Name == "verify") {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new Markup("[bold]verify <checksumFile>[/]"), new Text("Verifies files against a checksum manifest."));
        grid.AddRow(new Text("    --root [Directory]"), new Text("Root directory for files (optional)"));
        grid.AddRow(new Text("    --algorithm [String]"), new Text("Hashing algorithm (optional, default: SHA256)"));
        output.Add(grid);
      } else if (cmd.Name == "create") {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new Markup("[bold]create <outputManifest>[/]"), new Text("Creates a checksum manifest from a directory."));
        grid.AddRow(new Text("    --root [Directory]"), new Text("Root directory to scan"));
        grid.AddRow(new Text("    --algorithm [String]"), new Text("Hashing algorithm (optional, default: SHA256)"));
        output.Add(grid);
      } else {
        output.Add(new Markup($"[bold]{cmd.Name}[/]"));
      }
      output.Add(new Text(""));
    }
    return output;
  }
}
