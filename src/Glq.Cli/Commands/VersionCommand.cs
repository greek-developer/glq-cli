using System.CommandLine;

namespace Glq.Cli.Commands;

/// <summary><c>glq version</c> - the build identity from ProductionVersion.json.</summary>
internal static class VersionCommand
{
    internal static Command Create(CliContext context)
    {
        var command = new Command("version", "Print the version, commit and build time of this build.");
        command.SetAction(_ =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, ProductionVersion.FileName);
            if (!File.Exists(path))
            {
                throw GlqException.Usage(
                    $"{ProductionVersion.FileName} is missing from '{AppContext.BaseDirectory}'. It is written at build time.");
            }

            context.Out.WriteLine(ProductionVersion.Parse(File.ReadAllText(path)).ToDisplay());
            return ExitCodes.Success;
        });
        return command;
    }
}
