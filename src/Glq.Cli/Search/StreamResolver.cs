using System.Text.RegularExpressions;

using Glq.Cli.Client;

namespace Glq.Cli.Search;

/// <summary>
/// Turns stream names into IDs through the streams API, using the per-profile cache while it
/// is fresh. An ID passes through untouched.
/// </summary>
internal sealed partial class StreamResolver(GraylogConnection connection)
{
    private IReadOnlyList<StreamInfo>? _streams;

    private bool _fetched;

    /// <summary>The readable streams: the cache while fresh, otherwise the API (and re-cached).</summary>
    internal async Task<IReadOnlyList<StreamInfo>> ListAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _streams is not null)
        {
            return _streams;
        }

        if (!refresh && connection.Cache.FreshStreams() is { } cached)
        {
            return _streams = cached;
        }

        _streams = await connection.Client.GetStreamsAsync(cancellationToken).ConfigureAwait(false);
        _fetched = true;
        connection.Cache.SaveStreams(_streams);
        return _streams;
    }

    internal async Task<IReadOnlyList<string>> ResolveAsync(IEnumerable<string> namesOrIds, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        foreach (var value in namesOrIds.Select(v => v.Trim()).Where(v => v.Length > 0))
        {
            if (IdPattern().IsMatch(value))
            {
                ids.Add(value);
                continue;
            }

            var matches = Match(await ListAsync(refresh: false, cancellationToken).ConfigureAwait(false), value);
            if (matches.Count == 0 && !_fetched)
            {
                // The cache may predate the stream: look again before giving up.
                matches = Match(await ListAsync(refresh: true, cancellationToken).ConfigureAwait(false), value);
            }

            ids.Add(matches.Count switch
            {
                1 => matches[0].Id,
                0 => throw GlqException.Usage($"No readable stream is named '{value}'. See them with: glq streams list"),
                _ => throw GlqException.Usage(
                    $"More than one stream is named '{value}' - pass an ID instead: " +
                    string.Join(", ", matches.Select(s => $"{s.Title} ({s.Id})"))),
            });
        }

        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<StreamInfo> Match(IReadOnlyList<StreamInfo> streams, string name) =>
        [.. streams.Where(s => string.Equals(s.Title, name, StringComparison.OrdinalIgnoreCase))];

    // Graylog stream IDs are MongoDB ObjectIds: 24 hex digits.
    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex IdPattern();
}
