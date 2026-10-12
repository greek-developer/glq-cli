using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glq.Cli.Client;

/// <summary>
/// The typed Graylog client, and the only way glq talks to Graylog. It is read-only by
/// construction: it exposes search, stream listing and system info, and no method takes an
/// arbitrary path.
/// </summary>
internal sealed class GraylogClient(HttpClient http)
{
    internal const string RequestedByHeader = "X-Requested-By";

    internal Uri? BaseAddress => http.BaseAddress;

    /// <summary><c>GET /api/system</c> - the server version.</summary>
    internal async Task<SystemInfo> GetSystemAsync(CancellationToken cancellationToken)
    {
        var body = await SendAsync(HttpMethod.Get, "api/system", content: null, cancellationToken).ConfigureAwait(false);
        return new SystemInfo(body?["version"]?.GetValue<string>() ?? "unknown");
    }

    /// <summary><c>GET /api/streams</c> - the streams the token's user can read.</summary>
    internal async Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(CancellationToken cancellationToken)
    {
        var body = await SendAsync(HttpMethod.Get, "api/streams", content: null, cancellationToken).ConfigureAwait(false);
        return body?["streams"] is JsonArray streams
            ? [.. streams.OfType<JsonObject>().Select(StreamInfo.FromJson)]
            : [];
    }

    /// <summary>
    /// <c>POST /api/views/search/sync</c> - the preferred search: runs a search without saving
    /// it and waits for the result. <c>null</c> when the endpoint does not exist (a 404 that
    /// is not a Graylog API error).
    /// </summary>
    internal async Task<JsonNode?> SearchSyncAsync(JsonObject request, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(HttpMethod.Post, "api/views/search/sync", JsonContent.Create(request), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotFoundException notFound) when (notFound.ApiMessage is null)
        {
            return null;
        }
        catch (NotFoundException notFound)
        {
            throw GlqException.Api($"Graylog returned 404: {notFound.ApiMessage}");
        }
    }

    /// <summary><c>GET /api/search/universal/{relative|absolute}</c> - the legacy search.</summary>
    internal async Task<JsonNode?> SearchUniversalAsync(
        UniversalRange range,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        var query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var path = $"api/search/universal/{(range == UniversalRange.Relative ? "relative" : "absolute")}?{query}";
        try
        {
            return await SendAsync(HttpMethod.Get, path, content: null, cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            throw GlqException.Api("This Graylog has neither the views search API nor the legacy universal search API.");
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (method != HttpMethod.Get)
        {
            // Graylog's CSRF protection rejects non-GET requests without it.
            request.Headers.Add(RequestedByHeader, "glq");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw GlqException.Network(
                $"Graylog at {Host} did not answer within {http.Timeout.TotalSeconds:0} seconds (timeout_seconds).", exception);
        }
        catch (HttpRequestException exception) when (FindTlsFailure(exception) is { } tls)
        {
            throw GlqException.Network(
                $"TLS error talking to {Host}: {tls.Message.TrimEnd('.')}. Check the profile's ca_bundle; --insecure skips verification but is not safe.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw GlqException.Network($"Could not reach Graylog at {Host}: {exception.Message}", exception);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    throw GlqException.Api($"Graylog at {Host} answered {path.Split('?')[0]} with something that is not JSON.");
                }
            }

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => GlqException.Auth(
                    $"Graylog at {Host} rejected the token (401). Check it with: glq profile test - or replace it with: glq profile set-token <name>"),
                HttpStatusCode.Forbidden => GlqException.Auth(
                    $"Graylog at {Host} refused the request (403): the token's user needs search and stream-read permission on the requested streams."),
                HttpStatusCode.NotFound => new NotFoundException(ApiErrorMessage(text)),
                _ => GlqException.Api($"Graylog returned {(int)response.StatusCode} {response.ReasonPhrase}: {ErrorMessage(text)}"),
            };
        }
    }

    private string Host => http.BaseAddress?.Authority ?? "(no url)";

    private static AuthenticationException? FindTlsFailure(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException tls)
            {
                return tls;
            }
        }

        return null;
    }

    // Graylog reports failures as {"type":"ApiError","message":"..."}; anything else is shown trimmed.
    private static string ErrorMessage(string body)
    {
        if (ApiErrorMessage(body) is { } message)
        {
            return message;
        }

        var trimmed = body.ReplaceLineEndings(" ").Trim();
        return trimmed.Length == 0 ? "(no details)" : trimmed.Length > 300 ? trimmed[..300] + "…" : trimmed;
    }

    private static string? ApiErrorMessage(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject error && error["message"] is JsonValue message &&
                message.TryGetValue<string>(out var text)
                ? text
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class NotFoundException(string? apiMessage) : Exception
    {
        internal string? ApiMessage { get; } = apiMessage;
    }
}

internal enum UniversalRange
{
    Relative,
    Absolute,
}

internal sealed record SystemInfo(string Version);

internal sealed record StreamInfo(string Id, string Title, string? Description)
{
    internal static StreamInfo FromJson(JsonObject stream) => new(
        stream["id"]?.GetValue<string>() ?? string.Empty,
        stream["title"]?.GetValue<string>() ?? string.Empty,
        stream["description"]?.GetValue<string>());
}
