using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Glq.Cli.Client;

namespace Glq.Cli.UnitTests.Commands;

public class SearchCommandTests
{
    // What /api/views/search/sync answers with, newest message first.
    internal const string ViewsResponse = """
        {"id":"job-1","results":{"q1":{"state":"COMPLETE","errors":[],"search_types":{"st1":{"type":"messages","total_results":2,"messages":[
          {"index":"graylog_0","message":{"timestamp":"2026-09-24T08:00:01.000Z","source":"payments","message":"charged 4111111111111111"}},
          {"index":"graylog_0","message":{"timestamp":"2026-09-24T08:00:00.000Z","source":"api-gateway","message":"GET /orders 500 for jane.doe@example.com","customer_id":"C-1"}}]}}}}}
        """;

    internal const string LegacyResponse = """
        {"query":"*","total_results":1,"messages":[
          {"index":"graylog_0","message":{"timestamp":"2026-09-24T08:00:00.000Z","source":"api-gateway","message":"legacy hit","customer_id":"C-1"}}]}
        """;

    internal static TestHost HostWith(FakeGraylog graylog, string profileExtras = "", string topLevel = "")
    {
        var host = new TestHost { Graylog = graylog };
        host.WriteConfig($$"""
            { {{topLevel}} "current_profile": "dev",
              "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" {{profileExtras}} } } }
            """);
        return host;
    }

    private static FakeGraylog Views(string response = ViewsResponse) =>
        new FakeGraylog()
            .OnJson(HttpMethod.Post, "/api/views/search/sync", response)
            .OnJson(HttpMethod.Get, "/api/streams", StreamsCommandTests.StreamsJson);

    private static JsonNode Body(FakeGraylog graylog) =>
        JsonNode.Parse(graylog.Requests.Single(r => r.Uri.AbsolutePath == "/api/views/search/sync").Body!)!;

    private static JsonNode Query(FakeGraylog graylog) => Body(graylog)["queries"]![0]!;

    private static JsonNode MessageList(FakeGraylog graylog) => Query(graylog)["search_types"]![0]!;

    private static string[] Strings(JsonNode? array) => [.. array!.AsArray().Select(item => item!.GetValue<string>())];

    [Fact]
    public async Task Search_PostsTheViewsRequest()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        var result = await host.RunAsync(
            "search", "source:api AND http_status:>=500", "--range", "15m", "--fields", "timestamp,source,message",
            "--limit", "200", "--stream", "API Gateway", "--sort", "timestamp:asc");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var query = Query(graylog);
        Assert.Equal("elasticsearch", query["query"]!["type"]!.GetValue<string>());
        Assert.Equal("source:api AND http_status:>=500", query["query"]!["query_string"]!.GetValue<string>());
        Assert.Equal("relative", query["timerange"]!["type"]!.GetValue<string>());
        Assert.Equal(900, query["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal("or", query["filter"]!["type"]!.GetValue<string>());
        Assert.Equal("stream", query["filter"]!["filters"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("5f0000000000000000000002", query["filter"]!["filters"]![0]!["id"]!.GetValue<string>());

        var messages = MessageList(graylog);
        Assert.Equal("messages", messages["type"]!.GetValue<string>());
        Assert.Equal(200, messages["limit"]!.GetValue<int>());
        Assert.Equal(["timestamp", "source", "message"], Strings(messages["fields"]));
        Assert.Equal("timestamp", messages["sort"]![0]!["field"]!.GetValue<string>());
        Assert.Equal("ASC", messages["sort"]![0]!["order"]!.GetValue<string>());

        var post = graylog.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("glq", post.Headers[GraylogClient.RequestedByHeader]);
    }

    [Fact]
    public async Task Search_SendsAbsoluteRanges_InUtc()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        await host.RunAsync("search", "level:3", "--from", "2026-09-24T11:00:00", "--to", "2026-09-24T12:00:00+03:00", "--tz", "Europe/Athens");

        var range = Query(graylog)["timerange"]!;
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

        Assert.Equal(7200, Query(graylog)["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(25, MessageList(graylog)["limit"]!.GetValue<int>());
        Assert.Equal(["source"], Strings(MessageList(graylog)["fields"]));
        Assert.Equal("5f0000000000000000000003", Query(graylog)["filter"]!["filters"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_UsesBuiltInDefaults_WithoutProfileDefaults()
    {
        var graylog = Views();
        using var host = HostWith(graylog);

        await host.RunAsync("search", "*");

        Assert.Equal(600, Query(graylog)["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(1000, MessageList(graylog)["limit"]!.GetValue<int>());
        Assert.Equal("DESC", MessageList(graylog)["sort"]![0]!["order"]!.GetValue<string>());
        Assert.Null(Query(graylog)["filter"]);
    }

    [Fact]
    public async Task Search_ReturnsTheServersOrder_AndOnlyTheRequestedFields()
    {
        using var host = HostWith(Views());

        var result = await host.RunAsync("search", "*", "-o", "jsonl", "--fields", "source,customer_id");

        var rows = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToList();
        Assert.Equal("payments", rows[0]["source"]!.GetValue<string>());
        Assert.Null(rows[0]["customer_id"]);
        Assert.Equal("C-1", rows[1]["customer_id"]!.GetValue<string>());
        Assert.Null(rows[1]["message"]);
    }

    [Fact]
    public async Task Search_ReportsAQueryGraylogCouldNotRun()
    {
        const string failed = """{"id":"j","results":{"q1":{"state":"ERROR","errors":[{"description":"Cannot parse 'source:('","type":"query"}],"search_types":{}}}}""";
        using var host = HostWith(Views(failed));

        var result = await host.RunAsync("search", "source:(");

        Assert.Equal(ExitCodes.Api, result.ExitCode);
        Assert.Contains("Cannot parse", result.Err);
    }

    [Fact]
    public async Task Search_JsonFlag_IsAliasForOutputJson_AndRefusesAConflictingFormat()
    {
        using var host = HostWith(Views());

        var json = await host.RunAsync("search", "*", "--json");
        var conflict = await host.RunAsync("search", "*", "--json", "-o", "csv");

        Assert.Equal(ExitCodes.Success, json.ExitCode);
        Assert.IsType<JsonArray>(JsonNode.Parse(json.Out));
        Assert.Equal(ExitCodes.Usage, conflict.ExitCode);
        Assert.Contains("--json cannot be combined with --output csv", conflict.Err);
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
        Assert.Equal("5f00000000000000000000aa", Query(graylog)["filter"]!["filters"]![0]!["id"]!.GetValue<string>());
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
        Assert.Equal("/api/views/search/sync", graylog.Requests[0].Uri.AbsolutePath);
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

        Assert.DoesNotContain(graylog.Requests, r => r.Uri.AbsolutePath == "/api/views/search/sync");
    }

    [Fact]
    public async Task Search_ReportsAViewsApiError_WithoutFallingBack()
    {
        var graylog = new FakeGraylog().OnStatus(
            HttpMethod.Post, "/api/views/search/sync", HttpStatusCode.NotFound, """{"type":"ApiError","message":"Stream not found"}""");
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
            HttpMethod.Post, "/api/views/search/sync", HttpStatusCode.BadRequest, """{"type":"ApiError","message":"Cannot parse 'source:('"}""");
        using var host = HostWith(graylog);

        var result = await host.RunAsync("search", "source:(");

        Assert.Equal(ExitCodes.Api, result.ExitCode);
        Assert.Contains("Cannot parse", result.Err);
    }

    [Fact]
    public async Task Search_ReportsForbidden_WithThePermissionHint()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(HttpMethod.Post, "/api/views/search/sync", HttpStatusCode.Forbidden));

        var result = await host.RunAsync("search", "*");

        Assert.Equal(ExitCodes.Auth, result.ExitCode);
        Assert.Contains("search and stream-read permission", result.Err);
    }

    [Fact]
    public async Task Search_PrintsValuesAsTheyAre_InEveryFormat()
    {
        foreach (var format in new[] { "table", "json", "jsonl", "csv" })
        {
            using var host = HostWith(Views());

            var result = await host.RunAsync("search", "*", "-o", format);

            Assert.Equal(ExitCodes.Success, result.ExitCode);
            Assert.Contains("api-gateway", result.Out);
            Assert.Contains("jane.doe@example.com", result.Out);
            Assert.Contains("4111111111111111", result.Out);
        }
    }

    [Fact]
    public async Task Search_Raw_PrintsTheResponse()
    {
        using var host = HostWith(Views());

        var result = await host.RunAsync("search", "*", "--raw");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var raw = JsonNode.Parse(result.Out)!;
        Assert.NotNull(raw["results"]);
        Assert.Contains("jane.doe@example.com", result.Out);
    }

    [Fact]
    public async Task Search_ZeroResults_IsSuccess()
    {
        const string empty = """{"id":"j","results":{"q1":{"state":"COMPLETE","errors":[],"search_types":{"st1":{"type":"messages","messages":[]}}}}}""";
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
