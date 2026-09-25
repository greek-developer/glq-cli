using System.CommandLine;
using System.Text.RegularExpressions;

using Glq.Cli.Commands;

namespace Glq.Cli.Configuration;

/// <summary>The profile a command runs against, and the config it came from.</summary>
internal sealed partial record ActiveProfile(string Name, Profile Profile, GlqConfig Config)
{
    internal const string ProfileVariable = "GLQ_PROFILE";

    /// <summary>
    /// The name the user selected: <c>--profile</c>, then <c>GLQ_PROFILE</c>, then
    /// <c>current_profile</c>; <c>null</c> when none is set.
    /// </summary>
    internal static string? SelectedName(CliContext context, ParseResult parseResult, GlqConfig config) =>
        NonEmpty(parseResult.GetValue(GlobalOptions.Profile))
        ?? NonEmpty(context.GetEnvironmentVariable(ProfileVariable))
        ?? NonEmpty(config.CurrentProfile);

    internal static ActiveProfile Resolve(CliContext context, ParseResult parseResult, GlqConfig config)
    {
        var name = SelectedName(context, parseResult, config)
            ?? throw GlqException.Usage(
                $"No profile selected. Pass --profile <name>, set {ProfileVariable}, or run: glq profile use <name>" +
                (config.ProfilesOrEmpty.Count == 0 ? " (add one first with: glq profile add <name> --url <url>)" : string.Empty));

        return Named(config, name);
    }

    internal static ActiveProfile Named(GlqConfig config, string name)
    {
        if (!config.ProfilesOrEmpty.TryGetValue(name, out var profile))
        {
            var known = config.ProfilesOrEmpty.Keys.ToList();
            throw GlqException.Usage(known.Count == 0
                ? $"Profile '{name}' does not exist. Add it with: glq profile add {name} --url <url>"
                : $"Profile '{name}' does not exist. Known profiles: {string.Join(", ", known)}");
        }

        if (string.IsNullOrWhiteSpace(profile.Url))
        {
            throw GlqException.Usage($"Profile '{name}' has no url. Fix it with: glq config edit");
        }

        return new ActiveProfile(name, profile, config);
    }

    internal static void ValidateName(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            throw GlqException.Usage($"'{name}' is not a valid name: use letters, digits, '.', '_' and '-'.");
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex NamePattern();
}
