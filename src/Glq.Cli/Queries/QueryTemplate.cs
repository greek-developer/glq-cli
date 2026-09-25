using System.Text;
using System.Text.RegularExpressions;

namespace Glq.Cli.Queries;

/// <summary>
/// <c>{{param}}</c> placeholders in a stored query. Values are escaped so they cannot break
/// out of their Lucene clause: anything beyond a plain term is quoted as a phrase.
/// </summary>
internal static partial class QueryTemplate
{
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal) { "AND", "OR", "NOT", "TO" };

    /// <summary>The placeholder names in order of first appearance.</summary>
    internal static IReadOnlyList<string> Placeholders(string query) =>
        [.. PlaceholderPattern().Matches(query).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Fills every placeholder from <paramref name="values"/>, then the declared defaults.
    /// Fails, listing them all, when any placeholder has neither.
    /// </summary>
    internal static string Render(string query, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, QueryParam>? declared)
    {
        var placeholders = Placeholders(query);

        var unknown = values.Keys
            .Where(key => !placeholders.Contains(key) && declared?.ContainsKey(key) != true)
            .ToList();
        if (unknown.Count > 0)
        {
            throw GlqException.Usage(
                $"Unknown parameter{(unknown.Count == 1 ? string.Empty : "s")}: {string.Join(", ", unknown)}. " +
                $"This query takes: {(placeholders.Count == 0 ? "no parameters" : string.Join(", ", placeholders))}.");
        }

        var missing = placeholders
            .Where(name => !values.ContainsKey(name) && declared?.GetValueOrDefault(name)?.Default is null)
            .ToList();
        if (missing.Count > 0)
        {
            throw GlqException.Usage(
                $"Missing parameter{(missing.Count == 1 ? string.Empty : "s")}: {string.Join(", ", missing)}. " +
                $"Pass {string.Join(" ", missing.Select(name => $"--param {name}=<value>"))}");
        }

        return PlaceholderPattern().Replace(query, match =>
        {
            var name = match.Groups[1].Value;
            var value = values.TryGetValue(name, out var given) ? given : declared![name].Default!;

            // Already inside a phrase ("{{name}}"): escape only, the quotes are the author's.
            var quoted = match.Index > 0 && query[match.Index - 1] == '"' &&
                match.Index + match.Length < query.Length && query[match.Index + match.Length] == '"';
            return quoted ? EscapeInPhrase(value) : Escape(value);
        });
    }

    /// <summary>A value as one Lucene term: bare when it is a plain word, a quoted phrase otherwise.</summary>
    internal static string Escape(string value) =>
        PlainTerm().IsMatch(value) && !Operators.Contains(value)
            ? value
            : "\"" + EscapeInPhrase(value) + "\"";

    /// <summary>Escapes the two characters that end or break a quoted phrase.</summary>
    internal static string EscapeInPhrase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var character in value)
        {
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    // A term Lucene reads literally: no operators, wildcards, ranges, grouping or whitespace,
    // and no leading '-' (which would negate it).
    [GeneratedRegex(@"^[A-Za-z0-9_.@][A-Za-z0-9_.@-]*$")]
    private static partial Regex PlainTerm();
}
