using System.CommandLine;
using System.Reflection;
using System.Text.Json;

namespace Glq.Cli.Commands;

/// <summary><c>glq skill</c> - print the embedded agent guide, and nothing else.</summary>
internal static class SkillCommand
{
    internal const string Name = "glq";

    internal const string FileName = "SKILL.md";

    /// <summary>Where Claude Code looks for a user-level skill.</summary>
    internal const string HarnessPath = "~/.claude/skills/glq/SKILL.md";

    private static readonly Option<bool> Json = new("--json")
    {
        Description = "Print the guide as JSON, with the path a harness expects it at.",
    };

    internal static Command Create(CliContext context)
    {
        var command = new Command("skill", "Print the agent guide, ready to save as a skill file.") { Json };
        command.SetAction(parseResult =>
        {
            // Trailing newlines are trimmed because WriteLine adds one back; without this,
            // `glq skill > SKILL.md` gains a blank line on every round trip.
            var skill = Read().TrimEnd('\r', '\n');

            if (parseResult.GetValue(Json))
            {
                context.Out.WriteLine(JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["name"] = Name, ["path"] = HarnessPath, ["content"] = skill + "\n" },
                    new JsonSerializerOptions { WriteIndented = true }));
                return ExitCodes.Success;
            }

            context.Out.WriteLine(skill);
            return ExitCodes.Success;
        });
        return command;
    }

    internal static string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException(
                $"{FileName} is missing from this build. It is embedded from skills/{Name}/{FileName}.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
