using Glq.Cli.Configuration;

namespace Glq.Cli.Auth;

/// <summary>One place a token can come from.</summary>
internal interface ITokenSource
{
    /// <summary>Where the token comes from, for <c>profile show</c> - never the token itself.</summary>
    string Description { get; }

    /// <summary>The token, or <c>null</c> when this source has none.</summary>
    string? TryGet();
}

/// <summary><c>GLQ_TOKEN</c> - for CI.</summary>
internal sealed class EnvironmentTokenSource(Func<string, string?> getEnvironmentVariable) : ITokenSource
{
    internal const string Variable = "GLQ_TOKEN";

    public string Description => $"environment variable {Variable}";

    public string? TryGet() => Normalize(getEnvironmentVariable(Variable));

    internal static string? Normalize(string? token) => string.IsNullOrWhiteSpace(token) ? null : token.Trim();
}

/// <summary>The profile's <c>token</c> in config.json.</summary>
internal sealed class ConfigTokenSource(Profile profile) : ITokenSource
{
    public string Description => "config.json";

    public string? TryGet() => EnvironmentTokenSource.Normalize(profile.Token);
}

/// <summary>The output of the profile's <c>token_command</c>.</summary>
internal sealed class CommandTokenSource(string command, Func<string, string> run) : ITokenSource
{
    public string Description => $"token_command ({command})";

    public string? TryGet() => EnvironmentTokenSource.Normalize(run(command));
}

/// <summary>
/// Resolves a profile's token: <c>GLQ_TOKEN</c>, then the profile's <c>token</c>, then its
/// <c>token_command</c>. A resolved token is registered with the context so it is redacted
/// from every output.
/// </summary>
internal static class TokenResolver
{
    internal static IReadOnlyList<ITokenSource> Sources(CliContext context, Profile profile)
    {
        var sources = new List<ITokenSource> { new EnvironmentTokenSource(context.GetEnvironmentVariable) };

        if (profile.EffectiveAuthMethod == Profile.AuthTokenCommand)
        {
            if (!string.IsNullOrWhiteSpace(profile.TokenCommand))
            {
                sources.Add(new CommandTokenSource(profile.TokenCommand, context.RunTokenCommand));
            }
        }
        else
        {
            sources.Add(new ConfigTokenSource(profile));
            if (!string.IsNullOrWhiteSpace(profile.TokenCommand))
            {
                sources.Add(new CommandTokenSource(profile.TokenCommand, context.RunTokenCommand));
            }
        }

        return sources;
    }

    internal static string Resolve(CliContext context, string profileName, Profile profile)
    {
        if (profile.EffectiveAuthMethod is not (Profile.AuthToken or Profile.AuthTokenCommand))
        {
            throw GlqException.Usage(
                $"Profile '{profileName}' has auth_method '{profile.AuthMethod}'; use 'token' or 'token_command'.");
        }

        foreach (var source in Sources(context, profile))
        {
            var token = source.TryGet();
            if (token is not null)
            {
                context.Secrets.Add(token);
                return token;
            }
        }

        throw GlqException.Usage(
            $"Profile '{profileName}' has no token. Set one with: glq profile set-token {profileName} (or set {EnvironmentTokenSource.Variable}).");
    }

    /// <summary>
    /// Where the token would come from, without running a <c>token_command</c> or reading the
    /// token's value into any output.
    /// </summary>
    internal static string DescribeSource(CliContext context, Profile profile)
    {
        foreach (var source in Sources(context, profile))
        {
            if (source is CommandTokenSource)
            {
                return source.Description;
            }

            if (source.TryGet() is not null)
            {
                return source.Description;
            }
        }

        return "not set";
    }
}
