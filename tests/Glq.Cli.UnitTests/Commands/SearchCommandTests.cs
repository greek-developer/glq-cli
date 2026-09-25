using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Glq.Cli.Client;

namespace Glq.Cli.UnitTests.Commands;

public class SearchCommandTests
{
    internal const string ViewsResponse = """
        {"schema":[
           {"column_type":"field","type":"date","field":"timestamp","name":"field: timestamp"},
           {"column_type":"field","type":"string","field":"source","name":"field: source"},
           {"column_type":"field","type":"string","field":"message","name":"field: message"}],
         "datarows":[
           ["2026-09-24T08:00:00.000Z","api-gateway","GET /orders 500 for jane.doe@example.com"],
           ["2026-09-24T08:00:01.000Z","payments","charged 4111111111111111"]],
         "metadata":{}}
        """;

    internal const string LegacyResponse = """
        {"query":"*","total_results":1,"messages":[
          {"index":"graylog_0","message":{"timestamp":"2026-09-24T08:00:00.000Z","source":"api-gateway","message":"legacy hit","customer_id":"C-1"}}]}
        """;

    internal static TestHost HostWith(FakeGraylog graylog, string profileExtras = "", string topLevel = "")
    {
        var host = new TestHost { Graylog = graylog };
        host.WriteConfig($$"""
            { {{topLevel}} "current_profile": "dev", "mask_salt": "salt",
              "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" {{profileExtras}} } } }
            """);
        return host;
    }

    private static FakeGraylog Views(string response = ViewsResponse) =>
        new FakeGraylog()
            .OnJson(HttpMethod.Post, "/api/views/search/messages", response)
            .OnJson(HttpMethod.Get, "/api/streams", StreamsCommandTests.StreamsJson);

    private static JsonNode Body(FakeGraylog graylog) =>
        JsonNode.Parse(graylog.Requests.Single(r => r.Uri.AbsolutePath == "/api/views/search/messages").Body!)!;

    [Fact]
    public async Task Search_PostsTheViewsRequest()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        var result = await host.RunAsync(
            "search", "source:api AND http_status:>=500", "--range", "15m", "--fields", "timestamp,source,message",
            "--limit", "200", "--stream", "API Gateway", "--sort", "timestamp:asc");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var body = Body(graylog);
        Assert.Equal("source:api AND http_status:>=500", body["query_string"]!["query_string"]!.GetValue<string>());
        Assert.Equal("relative", body["timerange"]!["type"]!.GetValue<string>());
        Assert.Equal(900, body["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(200, body["limit"]!.GetValue<int>());
        Assert.Equal(["timestamp", "source", "message"], body["fields_in_order"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Equal(["5f0000000000000000000002"], body["streams"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal("ASC", body["sort"]![0]!["order"]!.GetValue<string>());

        var post = graylog.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("glq", post.Headers[GraylogClient.RequestedByHeader]);
    }

    [Fact]
    public async Task Search_SendsAbsoluteRanges_InUtc()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        await host.RunAsync("search", "level:3", "--from", "2026-09-24T11:00:00", "--to", "2026-09-24T12:00:00+03:00", "--tz", "Europe/Athens");

        var range = Body(graylog)["timerange"]!;
        Assert.Equal("absolute", range["type"]!.GetValue<string>());
        Assert.Equal("2026-09-24T08:00:00.000Z", range["from"]!.GetValue<string>());
        Assert.Equal("2026-09-24T09:00:00.000Z", range["to"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_AppliesTheProfileDefaults()
    {
        var graylog = Views();
        using var host = HostWith(graylog, """, "default_range": "2h", "default_limit": 25, "default_fields": ["source"], "default_streams": ["Payments"]""");

        await host.RunAsync("search", "*");

        var body = Body(graylog);
        Assert.Equal(7200, body["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(25, body["limit"]!.GetValue<int>());
        Assert.Equal(["source"], body["fields_in_order"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Equal(["5f0000000000000000000003"], body["streams"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public async Task Search_UsesBuiltInDefaults_WithoutProfileDefaults()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        await host.RunAsync("search", "*");

        var body = Body(graylog);
        Assert.Equal(300, body["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(100, body["limit"]!.GetValue<int>());
        Assert.Null(body["streams"]);
        Assert.Equal("DESC", body["sort"]![0]!["order"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_RejectsRangeCombinedWithFromTo()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        var result = await host.RunAsync("search", "*", "--range", "1h", "--from", "2026-09-24T08:00:00Z", "--to", "2026-09-24T09:00:00Z");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Empty(graylog.Requests);
    }

    [Fact]
    public async Task Search_RejectsALimitAboveMaxLimit()
    {
        var graylog = Views();
        using var host = HostWith(graylog, topLevel: "\"max_limit\": 500,");

        var result = await host.RunAsync("search", "*", "--limit", "501");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("max_limit", result.Err);
        Assert.Empty(graylog.Requests);
    }

    [Fact]
    public async Task Search_RejectsAnUnknownStream_AndAnAmbiguousOne()
    {
        const string duplicated = """{"streams":[{"id":"5f0000000000000000000011","title":"Edge"},{"id":"5f0000000000000000000012","title":"edge"}]}""";
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/streams", duplicated));

        var unknown = await host.RunAsync("search", "*", "--stream", "Nope");
        var ambiguous = await host.RunAsync("search", "*", "--stream", "EDGE");

        Assert.Equal(ExitCodes.Usage, unknown.ExitCode);
        Assert.Contains("glq streams list", unknown.Err);
        Assert.Equal(ExitCodes.Usage, ambiguous.ExitCode);
        Assert.Contains("5f0000000000000000000011", ambiguous.Err);
        Assert.Contains("5f0000000000000000000012", ambiguous.Err);
    }

    [Fact]
    public async Task Search_PassesStreamIdsThrough_WithoutAStreamsCall()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        await host.RunAsync("search", "*", "--stream", "5f00000000000000000000aa");

        Assert.DoesNotContain(graylog.Requests, r => r.Uri.AbsolutePath == "/api/streams");
        Assert.Equal("5f00000000000000000000aa", Body(graylog)["streams"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_FallsBackToLegacy_OnViews404_AndRemembersIt()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/search/universal/relative", LegacyResponse);
        using var host = HostWith(graylog);

        var first = await host.RunAsync("search", "source:api", "--range", "1h", "-o", "jsonl", "--fields", "timestamp,source,message");
        var second = await host.RunAsync("search", "source:api", "-o", "jsonl");

        Assert.Equal(ExitCodes.Success, first.ExitCode);
        Assert.Contains("legacy hit", first.Out);
        Assert.Equal(3, graylog.Requests.Count);
        Assert.Equal("/api/views/search/messages", graylog.Requests[0].Uri.AbsolutePath);
        var legacy = graylog.Requests[1].Uri;
        Assert.Equal("/api/search/universal/relative", legacy.AbsolutePath);
        Assert.Contains("range=3600", legacy.Query);
        Assert.Contains("query=source%3Aapi", legacy.Query);
        Assert.Contains("sort=timestamp%3Adesc", legacy.Query);
        Assert.Equal("/api/search/universal/relative", graylog.Requests[2].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Search_UsesLegacyAbsolute_WithStreamFilter()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/search/universal/absolute", LegacyResponse);
        using var host = HostWith(graylog);

        var result = await host.RunAsync(
            "search", "*", "--from", "2026-09-24T08:00:00Z", "--to", "2026-09-24T09:00:00Z",
            "--stream", "5f00000000000000000000aa", "--stream", "5f00000000000000000000bb");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var query = Uri.UnescapeDataString(graylog.Requests.Last().Uri.Query);
        Assert.Contains("from=2026-09-24T08:00:00.000Z", query);
        Assert.Contains("filter=streams:5f00000000000000000000aa OR streams:5f00000000000000000000bb", query);
    }

    [Fact]
    public async Task Search_GoesStraightToLegacy_OnAnOldServer()
    {
        var graylog = new FakeGraylog()
            .OnJson(HttpMethod.Get, "/api/system", """{"version":"3.3.9"}""")
            .OnJson(HttpMethod.Get, "/api/search/universal/relative", LegacyResponse);
        using var host = HostWith(graylog);
        await host.RunAsync("profile", "test");

        await host.RunAsync("search", "*");

        Assert.DoesNotContain(graylog.Requests, r => r.Uri.AbsolutePath == "/api/views/search/messages");
    }

    [Fact]
    public async Task Search_ReportsAViewsApiError_WithoutFallingBack()
    {
        var graylog = new FakeGraylog().OnStatus(
            HttpMethod.Post, "/api/views/search/messages", HttpStatusCode.NotFound, """{"type":"ApiError","message":"Stream not found"}""");
        using var host = HostWith(graylog);

        var result = await host.RunAsync("search", "*");

        Assert.Equal(ExitCodes.Api, result.ExitCode);
        Assert.Contains("Stream not found", result.Err);
        Assert.Single(graylog.Requests);
    }

    [Fact]
    public async Task Search_ReportsAQueryError_WithTheApiCode()
    {
        var graylog = new FakeGraylog().OnStatus(
            HttpMethod.Post, "/api/views/search/messages", HttpStatusCode.BadRequest, """{"type":"ApiError","message":"Cannot parse 'source:('"}""");
        using var host = HostWith(graylog);

        var result = await host.RunAsync("search", "source:(");

        Assert.Equal(ExitCodes.Api, result.ExitCode);
        Assert.Contains("Cannot parse", result.Err);
    }

    [Fact]
    public async Task Search_ReportsForbidden_WithThePermissionHint()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(HttpMethod.Post, "/api/views/search/messages", HttpStatusCode.Forbidden));

        var result = await host.RunAsync("search", "*");

        Assert.Equal(ExitCodes.Auth, result.ExitCode);
        Assert.Contains("search and stream-read permission", result.Err);
    }

    [Fact]
    public async Task Search_MasksByDefault_InEveryFormat()
    {
        foreach (var format in new[] { "table", "json", "jsonl", "csv" })
        {
            using var host = HostWith(Views());

            var result = await host.RunAsync("search", "*", "-o", format);

            Assert.Equal(ExitCodes.Success, result.ExitCode);
            Assert.Contains("api-gateway", result.Out);
            Assert.DoesNotContain("jane.doe@example.com", result.Out);
            Assert.DoesNotContain("4111111111111111", result.Out);
            Assert.Contains("email_…", result.Out);
        }
    }

    [Fact]
    public async Task Search_NoMask_StillMasksCardNumbers()
    {
        using var host = HostWith(Views());

        var result = await host.RunAsync("search", "*", "-o", "jsonl", "--no-mask");

        Assert.Contains("jane.doe@example.com", result.Out);
        Assert.DoesNotContain("4111111111111111", result.Out);
        Assert.Contains("card_…", result.Out);
    }

    [Fact]
    public async Task Search_Raw_PrintsTheResponse_Masked()
    {
        using var host = HostWith(Views());

        var result = await host.RunAsync("search", "*", "--raw", "--no-mask");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var raw = JsonNode.Parse(result.Out)!;
        Assert.NotNull(raw["schema"]);
        Assert.Contains("jane.doe@example.com", result.Out);
        Assert.DoesNotContain("4111111111111111", result.Out);
    }

    [Fact]
    public async Task Search_ZeroResults_IsSuccess()
    {
        const string empty = """{"schema":[{"field":"timestamp"}],"datarows":[]}""";
        using var host = HostWith(Views(empty));

        var jsonl = await host.RunAsync("search", "nothing", "-o", "jsonl");
        var json = await host.RunAsync("search", "nothing", "-o", "json");
        host.StdoutIsTerminal = true;
        var table = await host.RunAsync("search", "nothing");

        Assert.Equal(ExitCodes.Success, jsonl.ExitCode);
        Assert.Equal(string.Empty, jsonl.Out);
        Assert.Equal("[]", json.Out.Trim());
        Assert.Equal(ExitCodes.Success, table.ExitCode);
        Assert.Contains("No messages matched", table.Err);
    }

    [Fact]
    public async Task Search_DefaultsToJsonl_WhenPiped_AndTable_OnATerminal()
    {
        using var host = HostWith(Views());

        var piped = await host.RunAsync("search", "*");
        host.StdoutIsTerminal = true;
        var terminal = await host.RunAsync("search", "*");

        Assert.All(piped.Out.Trim().Split(Environment.NewLine), line => JsonDocument.Parse(line).Dispose());
        Assert.StartsWith("timestamp", terminal.Out);
    }

    [Fact]
    public async Task Production_PrintsTheNotice_OnSearch()
    {
        using var host = HostWith(Views(), """, "production": true""");

        var result = await host.RunAsync("search", "*");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains(GraylogConnection.ProductionNotice, result.Err);
    }

    [Fact]
    public async Task Production_LargeRun_NeedsYes_WhenNotInteractive()
    {
        var graylog = Views();
        using var host = HostWith(graylog, """, "production": true""", topLevel: "\"confirm_limit\": 500,");

        var refused = await host.RunAsync("search", "*", "--limit", "1000");
        Assert.Equal(ExitCodes.Usage, refused.ExitCode);
        Assert.Contains("--yes", refused.Err);
        Assert.Empty(graylog.Requests);

        var confirmed = await host.RunAsync("search", "*", "--limit", "1000", "--yes");
        Assert.Equal(ExitCodes.Success, confirmed.ExitCode);
    }

    [Fact]
    public async Task Production_LargeRun_AsksWhenInteractive()
    {
        using var host = HostWith(Views(), """, "production": true""", topLevel: "\"confirm_limit\": 500,");
        host.StdinIsTerminal = true;
        host.Input = "n\n";

        var declined = await host.RunAsync("search", "*", "--limit", "1000");

        Assert.Equal(ExitCodes.Usage, declined.ExitCode);
        Assert.Contains("[y/N]", declined.Err);
    }

    [Fact]
    public async Task NonProduction_LargeRun_NeedsNoConfirmation()
    {
        using var host = HostWith(Views(), topLevel: "\"confirm_limit\": 500,");

        var result = await host.RunAsync("search", "*", "--limit", "1000");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
    }
}
