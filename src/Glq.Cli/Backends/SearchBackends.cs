using System.Globalization;
using System.Text.Json.Nodes;

using Glq.Cli.Client;
using Glq.Cli.Search;

namespace Glq.Cli.Backends;

/// <summary>One way of running a search against Graylog.</summary>
internal interface ISearchBackend
{
    string Name { get; }

    /// <summary>Runs the search, or returns <c>null</c> when this server lacks the API.</summary>
    Task<SearchResult?> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}

/// <summary><c>POST /api/views/search/sync</c> - the preferred search.</summary>
internal sealed class ViewsBackend(GraylogClient client) : ISearchBackend
{
    public string Name => "views";

    /// <summary>
    /// A one-query search with a single <c>messages</c> search type - the same shape the Graylog
    /// UI sends - so that Graylog applies the sort and the limit.
    /// </summary>
    internal static JsonObject BuildRequest(SearchRequest request)
    {
        var searchType = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "messages",
            ["limit"] = request.Limit,
            ["offset"] = 0,
            ["sort"] = new JsonArray(new JsonObject
            {
                ["field"] = request.Sort.Field,
                ["order"] = request.Sort.Descending ? "DESC" : "ASC",
            }),
            ["fields"] = new JsonArray([.. request.Fields.Select(f => (JsonNode?)f)]),
            ["decorators"] = new JsonArray(),
        };

        var query = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["query"] = new JsonObject { ["type"] = "elasticsearch", ["query_string"] = request.Query },
            ["timerange"] = request.Range switch
            {
                TimeRange.Absolute absolute => new JsonObject
                {
                    ["type"] = "absolute",
                    ["from"] = Iso(absolute.From),
                    ["to"] = Iso(absolute.To),
                },
                TimeRange.Relative relative => new JsonObject
                {
                    ["type"] = "relative",
                    ["range"] = (long)relative.Span.TotalSeconds,
                },
                _ => throw new InvalidOperationException("Unknown time range."),
            },
            ["search_types"] = new JsonArray(searchType),
        };

        if (request.StreamIds.Count > 0)
        {
            query["filter"] = new JsonObject
            {
                ["type"] = "or",
                ["filters"] = new JsonArray([.. request.StreamIds.Select(id => (JsonNode?)new JsonObject { ["type"] = "stream", ["id"] = id })]),
            };
        }

        return new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["queries"] = new JsonArray(query),
            ["parameters"] = new JsonArray(),
        };
    }

    /// <summary>
    /// Reads the search job: <c>results.{query}.search_types.{id}.messages[].message</c>. A query
    /// that Graylog could not run is reported with Graylog's own description.
    /// </summary>
    internal static SearchResult ParseResponse(JsonNode? response, IReadOnlyList<string> requestedFields)
    {
        var result = (response?["results"] as JsonObject)?.FirstOrDefault().Value;
        if (result is null)
        {
            throw GlqException.Api("Graylog answered the search without a result.");
        }

        var errors = (result["errors"] as JsonArray ?? [])
            .Select(error => error?["description"]?.GetValue<string>() ?? error?["message"]?.GetValue<string>())
            .OfType<string>()
            .ToList();
        if (errors.Count > 0)
        {
            throw GlqException.Api($"Graylog could not run the search: {string.Join("; ", errors)}");
        }

        var messages = ((result["search_types"] as JsonObject)?.FirstOrDefault().Value?["messages"] as JsonArray ?? [])
            .Select(item => item?["message"] as JsonObject)
            .OfType<JsonObject>()
            .Select(message =>
            {
                var row = new JsonObject();
                foreach (var field in requestedFields)
                {
                    row[field] = message[field]?.DeepClone();
                }

                return row;
            })
            .ToList();

        return new SearchResult([.. requestedFields], messages, response);
    }

    public async Task<SearchResult?> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var response = await client.SearchSyncAsync(BuildRequest(request), cancellationToken).ConfigureAwait(false);
        return response is null ? null : ParseResponse(response, request.Fields);
    }

    internal static string Iso(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary><c>GET /api/search/universal/*</c> - the legacy search, for servers without the views API.</summary>
internal sealed class LegacyBackend(GraylogClient client) : ISearchBackend
{
    public string Name => "legacy";

    internal static (UniversalRange Range, List<KeyValuePair<string, string>> Parameters) BuildRequest(SearchRequest request)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("query", string.IsNullOrWhiteSpace(request.Query) ? "*" : request.Query),
        };

        UniversalRange range;
        switch (request.Range)
        {
            case TimeRange.Absolute absolute:
                range = UniversalRange.Absolute;
                parameters.Add(new("from", ViewsBackend.Iso(absolute.From)));
                parameters.Add(new("to", ViewsBackend.Iso(absolute.To)));
                break;
            case TimeRange.Relative relative:
                range = UniversalRange.Relative;
                parameters.Add(new("range", ((long)relative.Span.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
                break;
            default:
                throw new InvalidOperationException("Unknown time range.");
        }

        parameters.Add(new("limit", request.Limit.ToString(CultureInfo.InvariantCulture)));
        parameters.Add(new("fields", string.Join(",", request.Fields)));
        parameters.Add(new("sort", request.Sort.ToString()));
        parameters.Add(new("decorate", "false"));
        if (request.StreamIds.Count > 0)
        {
            parameters.Add(new("filter", string.Join(" OR ", request.StreamIds.Select(id => $"streams:{id}"))));
        }

        return (range, parameters);
    }

    internal static SearchResult ParseResponse(JsonNode? response, IReadOnlyList<string> requestedFields)
    {
        var messages = response?["messages"] is JsonArray list
            ? list.Select(item => item?["message"] as JsonObject)
                .OfType<JsonObject>()
                .Select(message =>
                {
                    var row = new JsonObject();
                    foreach (var field in requestedFields)
                    {
                        row[field] = message[field]?.DeepClone();
                    }

                    return row;
                })
                .ToList()
            : [];

        return new SearchResult([.. requestedFields], messages, response);
    }

    public async Task<SearchResult?> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var (range, parameters) = BuildRequest(request);
        var response = await client.SearchUniversalAsync(range, parameters, cancellationToken).ConfigureAwait(false);
        return ParseResponse(response, request.Fields);
    }
}

/// <summary>
/// Picks the backend: the views API first, the legacy API when the views API answers 404 -
/// remembered per profile, as is a server version too old to have it.
/// </summary>
internal sealed class SearchService(GraylogConnection connection, TextWriter error, bool verbose)
{
    /// <summary>The first Graylog version with <c>/api/views/search/sync</c>.</summary>
    internal static readonly Version ViewsApiSince = new(4, 1);

    internal async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var views = new ViewsBackend(connection.Client);
        var legacy = new LegacyBackend(connection.Client);

        if (connection.Cache.ViewsApi != false && !IsTooOldForViews(connection.Cache.ServerVersion))
        {
            var result = await views.SearchAsync(request, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                if (connection.Cache.ViewsApi != true)
                {
                    connection.Cache.SaveViewsApi(true);
                }

                return result;
            }

            connection.Cache.SaveViewsApi(false);
            if (verbose)
            {
                error.WriteLine("views search API not found (404) - using the legacy search API");
            }
        }

        return await legacy.SearchAsync(request, cancellationToken).ConfigureAwait(false)
            ?? throw GlqException.Api("The legacy search API returned nothing.");
    }

    internal static bool IsTooOldForViews(string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion))
        {
            return false;
        }

        var core = serverVersion.Split('+', '-', ' ')[0];
        return Version.TryParse(core.Count(c => c == '.') == 0 ? core + ".0" : core, out var version)
            && version < ViewsApiSince;
    }
}
