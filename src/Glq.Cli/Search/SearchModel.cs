using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Glq.Cli.Search;

/// <summary>One search, fully resolved: stream IDs, not names; an explicit range, fields and limit.</summary>
internal sealed record SearchRequest(
    string Query,
    TimeRange Range,
    IReadOnlyList<string> StreamIds,
    IReadOnlyList<string> Fields,
    int Limit,
    SortSpec Sort);

/// <summary>A sort order such as <c>timestamp:desc</c>.</summary>
internal sealed partial record SortSpec(string Field, bool Descending)
{
    internal static readonly SortSpec Default = new("timestamp", Descending: true);

    internal static SortSpec Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        var match = Pattern().Match(value.Trim());
        return match.Success
            ? new SortSpec(match.Groups[1].Value, !match.Groups[2].Success || match.Groups[2].Value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            : throw GlqException.Usage($"'{value}' is not a sort: use <field>:asc or <field>:desc.");
    }

    public override string ToString() => $"{Field}:{(Descending ? "desc" : "asc")}";

    [GeneratedRegex("^([A-Za-z0-9_.@-]+)(?::(asc|desc))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}

/// <summary>Matching messages as rows of fields, plus the untransformed response for <c>--raw</c>.</summary>
internal sealed record SearchResult(IReadOnlyList<string> Fields, IReadOnlyList<JsonObject> Messages, JsonNode? Raw);
