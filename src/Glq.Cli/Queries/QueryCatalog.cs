using System.Text.RegularExpressions;

using Glq.Cli.Configuration;
using Glq.Cli.Search;

namespace Glq.Cli.Queries;

/// <summary>A stored query as the active profile sees it.</summary>
internal sealed record VisibleQuery(string Name, StoredQuery Query, string? ProfileName)
{
    internal const string SharedScope = "shared";

    internal bool IsProfileOnly => ProfileName is not null;

    internal string Scope => ProfileName is null ? SharedScope : $"profile:{ProfileName}";
}

/// <summary>
/// Shared and profile-only stored queries. A profile's view is its own queries plus the
/// shared ones; a profile-only query wins over a shared one of the same name.
/// </summary>
internal static partial class QueryCatalog
{
    internal static IReadOnlyList<VisibleQuery> Visible(GlqConfig config, string? profileName)
    {
        var own = profileName is not null && config.ProfilesOrEmpty.TryGetValue(profileName, out var profile)
            ? profile.QueriesOrEmpty
            : new SortedDictionary<string, StoredQuery>(StringComparer.Ordinal);

        return
        [
            .. own.Select(pair => new VisibleQuery(pair.Key, pair.Value, profileName)),
            .. config.QueriesOrEmpty
                .Where(pair => !own.ContainsKey(pair.Key))
                .Select(pair => new VisibleQuery(pair.Key, pair.Value, null)),
        ];
    }

    internal static VisibleQuery Find(GlqConfig config, string? profileName, string name) =>
        Visible(config, profileName).FirstOrDefault(query => query.Name == name)
        ?? throw GlqException.Usage($"No stored query is named '{name}'. See them with: glq query list");

    /// <summary>The dictionary a query of this scope lives in.</summary>
    internal static SortedDictionary<string, StoredQuery> ScopeOf(GlqConfig config, string? profileName) =>
        profileName is null ? config.QueriesOrEmpty : config.ProfilesOrEmpty[profileName].QueriesOrEmpty;

    internal static void ValidateName(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            throw GlqException.Usage($"'{name}' is not a valid query name: use letters, digits, '.', '_' and '-'.");
        }
    }

    /// <summary>Checks a stored query the same way whether it came from save, edit or import.</summary>
    internal static void Validate(string name, StoredQuery query, GlqConfig config)
    {
        ValidateName(name);

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            throw GlqException.Usage($"Query '{name}' has no 'query'.");
        }

        if (query.Range is not null || query.From is not null || query.To is not null)
        {
            _ = TimeRange.Resolve(query.Range, query.From, query.To, zone: null, TimeRange.DefaultRange);
        }

        if (query.Limit is { } limit && (limit <= 0 || limit > config.EffectiveMaxLimit))
        {
            throw GlqException.Usage($"Query '{name}' has limit {limit}; it must be between 1 and max_limit ({config.EffectiveMaxLimit}).");
        }

        _ = SortSpec.Parse(query.Sort);

        foreach (var parameter in query.Params?.Keys ?? Enumerable.Empty<string>())
        {
            if (!ParamPattern().IsMatch(parameter))
            {
                throw GlqException.Usage($"Query '{name}' declares parameter '{parameter}', which is not a valid name.");
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]*$")]
    private static partial Regex ParamPattern();
}
