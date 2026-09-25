using System.Net;
using System.Text;

namespace Glq.Cli.UnitTests;

/// <summary>A stand-in for a Graylog server: routes requests to canned responses and records them.</summary>
internal sealed class FakeGraylog : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    internal List<RecordedRequest> Requests { get; } = [];

    internal FakeGraylog On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((method, path, respond));
        return this;
    }

    internal FakeGraylog OnJson(HttpMethod method, string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => Json(json, status));

    internal FakeGraylog OnStatus(HttpMethod method, string path, HttpStatusCode status, string body = "") =>
        On(method, path, _ => Json(body, status));

    internal FakeGraylog OnThrow(HttpMethod method, string path, Exception exception) =>
        On(method, path, _ => throw exception);

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            body));

        var route = _routes.LastOrDefault(r => r.Method == request.Method && r.Path == request.RequestUri!.AbsolutePath);
        return route.Respond is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) }
            : route.Respond(request);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);
