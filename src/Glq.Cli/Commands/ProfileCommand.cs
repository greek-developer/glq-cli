using System.CommandLine;
using System.Text.Json.Nodes;

using Glq.Cli.Auth;
using Glq.Cli.Configuration;
using Glq.Cli.Output;

namespace Glq.Cli.Commands;

/// <summary><c>glq profile add | list | show | use | remove | test | set-token</c> - see specs/profiles.md.</summary>
internal static class ProfileCommand
{
    internal static Command Create(CliContext context) => new("profile", "Manage Graylog profiles - one per cluster or environment.")
    {
        Add(context),
        List(context),
        Show(context),
        Use(context),
        Remove(context),
        ProfileTestCommand.Create(context),
        SetToken(context),
    };

    private static Argument<string> NameArgument() => new("name") { Description = "The profile name." };

    private static Command Add(CliContext context)
    {
        var name = NameArgument();
        var url = new Option<string>("--url") { Description = "Graylog base URL, e.g. https://graylog.example.com", Required = true };
        var production = new Option<bool>("--production") { Description = "Mark the target as production." };
        var noVerifyTls = new Option<bool>("--no-verify-tls") { Description = "Store verify_tls = false. Every run then warns." };
        var caBundle = new Option<string?>("--ca-bundle") { Description = "Path to a custom CA bundle (PEM)." };
        var proxy = new Option<string?>("--proxy") { Description = "HTTP proxy for this profile, e.g. http://10.0.0.59:8888. Without it the system/env proxy applies." };
        var timeout = new Option<int?>("--timeout-seconds") { Description = "Per-request timeout (default 30)." };
        var tokenCommand = new Option<string?>("--token-command") { Description = "Shell command that prints the token, e.g. a password-manager CLI." };
        var defaultRange = new Option<string?>("--default-range") { Description = "Relative range used when a search gives none (default 5m)." };
        var defaultStream = new Option<string[]>("--default-stream") { Description = "Stream name or ID used when a search gives none. Repeatable." };
        var defaultLimit = new Option<int?>("--default-limit") { Description = "Result count used when a search gives none (default 100)." };
        var defaultFields = new Option<string?>("--default-fields") { Description = "Comma-separated fields used when a search gives none." };
        var use = new Option<bool>("--use") { Description = "Make it the current profile." };
        var force = new Option<bool>("--force") { Description = "Replace an existing profile of the same name." };

        var command = new Command("add", "Add a profile.")
        {
            name, url, production, noVerifyTls, caBundle, proxy, timeout, tokenCommand, defaultRange, defaultStream,
            defaultLimit, defaultFields, use, force,
        };

        command.SetAction(parseResult =>
        {
            var profileName = parseResult.GetValue(name)!;
            ActiveProfile.ValidateName(profileName);

            var baseUrl = ValidateUrl(context, parseResult.GetValue(url)!);
            var range = parseResult.GetValue(defaultRange);
            if (range is not null)
            {
                _ = Search.TimeRange.ParseRelative(range);
            }

            var config = context.Store.Load();
            if (config.ProfilesOrEmpty.ContainsKey(profileName) && !parseResult.GetValue(force))
            {
                throw GlqException.Usage($"Profile '{profileName}' already exists. Pass --force to replace it.");
            }

            var command = parseResult.GetValue(tokenCommand);
            config.ProfilesOrEmpty[profileName] = new Profile
            {
                Url = baseUrl,
                AuthMethod = command is null ? null : Profile.AuthTokenCommand,
                TokenCommand = command,
                VerifyTls = parseResult.GetValue(noVerifyTls) ? false : null,
                CaBundle = parseResult.GetValue(caBundle),
                Proxy = ValidProxy(parseResult.GetValue(proxy)),
                TimeoutSeconds = Positive(parseResult.GetValue(timeout), "--timeout-seconds"),
                Production = parseResult.GetValue(production) ? true : null,
                DefaultRange = range,
                DefaultStreams = NullIfEmpty(parseResult.GetValue(defaultStream)),
                DefaultLimit = Positive(parseResult.GetValue(defaultLimit), "--default-limit"),
                DefaultFields = NullIfEmpty(SplitList(parseResult.GetValue(defaultFields))),
            };

            if (parseResult.GetValue(use) || string.IsNullOrEmpty(config.CurrentProfile))
            {
                config.CurrentProfile = profileName;
            }

            context.Store.Save(config);
            context.Err.WriteLine(command is null
                ? $"Added profile '{profileName}'. Set its token with: glq profile set-token {profileName}"
                : $"Added profile '{profileName}'. Its token comes from token_command.");
            return ExitCodes.Success;
        });

        return command;
    }

    private static Command List(CliContext context)
    {
        var output = Formatter.CreateOption();
        var json = Formatter.CreateJsonOption();
        var command = new Command("list", "List profiles; * marks the current one.") { output, json };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var selected = ActiveProfile.SelectedName(context, parseResult, config);
            var rows = config.ProfilesOrEmpty
                .Select(pair => new JsonObject
                {
                    ["current"] = pair.Key == selected ? "*" : string.Empty,
                    ["name"] = pair.Key,
                    ["url"] = pair.Value.Url,
                    ["production"] = pair.Value.IsProduction,
                })
                .ToList();

            Formatter.Write(
                context.Out,
                Formatter.Resolve(parseResult.GetValue(output), parseResult.GetValue(json), context.StdoutIsTerminal),
                ["current", "name", "url", "production"],
                rows);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Show(CliContext context)
    {
        var name = NameArgument();
        name.Arity = ArgumentArity.ZeroOrOne;
        var output = Formatter.CreateOption();
        var json = Formatter.CreateJsonOption();
        var command = new Command("show", "Show a profile's settings and where its token comes from - never the token.") { name, output, json };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var active = parseResult.GetValue(name) is { } requested
                ? ActiveProfile.Named(config, requested)
                : ActiveProfile.Resolve(context, parseResult, config);
            var profile = active.Profile;

            var settings = new JsonObject
            {
                ["name"] = active.Name,
                ["url"] = profile.Url,
                ["auth_method"] = profile.EffectiveAuthMethod,
                ["token_source"] = TokenResolver.DescribeSource(context, profile),
                ["verify_tls"] = profile.EffectiveVerifyTls,
                ["ca_bundle"] = profile.CaBundle,
                ["proxy"] = profile.Proxy,
                ["timeout_seconds"] = profile.EffectiveTimeoutSeconds,
                ["production"] = profile.IsProduction,
                ["default_range"] = profile.EffectiveDefaultRange,
                ["default_streams"] = new JsonArray([.. profile.EffectiveDefaultStreams.Select(s => (JsonNode?)s)]),
                ["default_limit"] = profile.EffectiveDefaultLimit,
                ["default_fields"] = new JsonArray([.. profile.EffectiveDefaultFields.Select(s => (JsonNode?)s)]),
                ["queries"] = profile.QueriesOrEmpty.Count,
            };

            var format = Formatter.Resolve(parseResult.GetValue(output), parseResult.GetValue(json), context.StdoutIsTerminal);
            if (format is OutputFormat.Table)
            {
                foreach (var (key, value) in settings)
                {
                    context.Out.WriteLine($"{key}: {Formatter.Text(value)}");
                }
            }
            else
            {
                Formatter.Write(context.Out, format, [.. settings.Select(pair => pair.Key)], [settings]);
            }

            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Use(CliContext context)
    {
        var name = NameArgument();
        var command = new Command("use", "Make a profile the current one.") { name };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var active = ActiveProfile.Named(config, parseResult.GetValue(name)!);
            config.CurrentProfile = active.Name;
            context.Store.Save(config);
            context.Err.WriteLine($"Current profile: {active.Name}");
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Remove(CliContext context)
    {
        var name = NameArgument();
        var yes = new Option<bool>("--yes", "-y") { Description = "Skip the confirmation." };
        var command = new Command("remove", "Remove a profile, its token and its profile-only queries.") { name, yes };
        command.SetAction(parseResult =>
        {
            var profileName = parseResult.GetValue(name)!;
            var config = context.Store.Load();
            if (!config.ProfilesOrEmpty.TryGetValue(profileName, out var profile))
            {
                throw GlqException.Usage($"Profile '{profileName}' does not exist.");
            }

            var queries = profile.QueriesOrEmpty.Count;
            if (!parseResult.GetValue(yes) &&
                !context.Confirm($"Remove profile '{profileName}', its token and {queries} profile-only quer{(queries == 1 ? "y" : "ies")}?"))
            {
                throw GlqException.Usage(context.StdinIsTerminal
                    ? "Not removed."
                    : $"Not removed: confirm with --yes to remove profile '{profileName}' without a prompt.");
            }

            config.ProfilesOrEmpty.Remove(profileName);
            if (config.CurrentProfile == profileName)
            {
                config.CurrentProfile = null;
            }

            context.Store.Save(config);
            context.Err.WriteLine($"Removed profile '{profileName}'.");
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command SetToken(CliContext context)
    {
        var name = NameArgument();
        var command = new Command("set-token", "Prompt for a profile's token, with hidden input, and store it in config.json.") { name };
        command.SetAction(parseResult =>
        {
            var profileName = parseResult.GetValue(name)!;
            var config = context.Store.Load();
            if (!config.ProfilesOrEmpty.TryGetValue(profileName, out var profile))
            {
                throw GlqException.Usage($"Profile '{profileName}' does not exist.");
            }

            if (context.StdinIsTerminal)
            {
                context.Err.Write($"Token for profile '{profileName}' (input hidden): ");
                context.Err.Flush();
            }

            var token = EnvironmentTokenSource.Normalize(context.ReadSecret())
                ?? throw GlqException.Usage("No token given; nothing was changed.");
            context.Secrets.Add(token);

            profile.Token = token;
            profile.AuthMethod = null;
            context.Store.Save(config);
            context.Err.WriteLine($"Token for profile '{profileName}' saved to {context.Paths.ConfigFile}.");
            return ExitCodes.Success;
        });
        return command;
    }

    private static string ValidateUrl(CliContext context, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw GlqException.Usage($"'{value}' is not an http(s) URL.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            context.Err.WriteLine("warning: this URL is plain http - the token will cross the network unencrypted.");
        }

        return value.TrimEnd('/');
    }

    private static int? Positive(int? value, string option) =>
        value is null or > 0 ? value : throw GlqException.Usage($"{option} must be greater than zero.");

    private static string? ValidProxy(string? value) =>
        value is null ? null : Client.GraylogConnection.ParseProxy(value).AbsoluteUri.TrimEnd('/');

    internal static string[] SplitList(string? value) =>
        value is null ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string>? NullIfEmpty(string[]? values) => values is { Length: > 0 } ? [.. values] : null;
}
