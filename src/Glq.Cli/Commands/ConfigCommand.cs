using System.CommandLine;

using Glq.Cli.Configuration;

namespace Glq.Cli.Commands;

/// <summary><c>glq config path | edit | protect</c>.</summary>
internal static class ConfigCommand
{
    // A fill-in-the-blanks template, not a working config: the shape of every section, with
    // the values left for the user to complete.
    internal const string Template =
        """
        {
          "current_profile": "",
          "profiles": {
            "": {
              "url": "",
              "token": ""
            }
          },
          "queries": {}
        }

        """;

    internal static Command Create(CliContext context)
    {
        var path = new Command("path", "Print the path of config.json.");
        path.SetAction(_ =>
        {
            context.Out.WriteLine(context.Paths.ConfigFile);
            return ExitCodes.Success;
        });

        var edit = new Command("edit", "Open config.json in $VISUAL / $EDITOR, creating a template if it is missing.");
        edit.SetAction(_ =>
        {
            var file = context.Paths.ConfigFile;
            if (!File.Exists(file))
            {
                context.Store.WriteOwnerOnly(file, Template);
            }

            var exitCode = context.RunEditor(file);
            if (exitCode != 0)
            {
                throw GlqException.Usage($"The editor exited with code {exitCode}.");
            }

            return ExitCodes.Success;
        });

        var protect = new Command("protect", "Restrict config.json to your user - the fix when glq refuses a readable file.");
        protect.SetAction(_ =>
        {
            var file = context.Paths.ConfigFile;
            if (!File.Exists(file))
            {
                throw GlqException.Usage($"{file} does not exist yet.");
            }

            OwnerOnly.Protect(file);
            context.Err.WriteLine($"{file} is now readable by your user only.");
            return ExitCodes.Success;
        });

        return new Command("config", "Locate, edit and protect config.json.") { path, edit, protect };
    }
}
