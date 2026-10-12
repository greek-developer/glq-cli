using System.Text.Json.Nodes;

namespace Glq.Cli.UnitTests.Commands;

public class QueryCommandTests
{
    private static TestHost Host(FakeGraylog? graylog = null, string profileExtras = "")
    {
        var host = new TestHost
        {
            Graylog = graylog ?? new FakeGraylog()
                .OnJson(HttpMethod.Post, "/api/views/search/sync", SearchCommandTests.ViewsResponse)
                .OnJson(HttpMethod.Get, "/api/streams", StreamsCommandTests.StreamsJson),
        };
        host.WriteConfig($$"""
            { "current_profile": "dev",
              "profiles": {
                "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" {{profileExtras}} },
                "prod": { "url": "https://graylog.prod", "token": "{{TestHost.Token}}", "production": true } } }
            """);
        return host;
    }

    private static JsonNode LastBody(TestHost host) =>
        JsonNode.Parse(host.Graylog!.Requests.Last(r => r.Method == HttpMethod.Post).Body!)!;

    [Fact]
    public async Task Save_StoresASharedQuery_WithItsSearchOptions()
    {
        using var host = Host();

        var result = await host.RunAsync(
            "query", "save", "svc-errors", "source:{{service}} AND level:<=3", "--range", "1h", "--limit", "50",
            "--fields", "timestamp,message", "--stream", "Payments", "--description", "Errors for a service", "--tag", "ops");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var query = host.ReadConfig().Queries!["svc-errors"];
        Assert.Equal("source:{{service}} AND level:<=3", query.Query);
        Assert.Equal("1h", query.Range);
        Assert.Equal(50, query.Limit);
        Assert.Equal(["timestamp", "message"], query.Fields!);
        Assert.Equal(["Payments"], query.Streams!);
        Assert.Equal(["ops"], query.Tags!);
        Assert.Contains("source:{{service}} AND level:<=3", File.ReadAllText(host.Paths.ConfigFile));
    }

    [Fact]
    public async Task Save_ProfileOnly_StoresOnTheActiveProfile()
    {
        using var host = Host();

        await host.RunAsync("query", "save", "mine", "level:1", "--profile-only");

        var config = host.ReadConfig();
        Assert.True(config.Profiles!["dev"].Queries!.ContainsKey("mine"));
        Assert.Null(config.Queries);
    }

    [Fact]
    public async Task Save_RejectsAnInvalidQuery()
    {
        using var host = Host();

        var badRange = await host.RunAsync("query", "save", "q", "*", "--range", "soon");
        var badName = await host.RunAsync("query", "save", "no spaces", "*");

        Assert.Equal(ExitCodes.Usage, badRange.ExitCode);
        Assert.Equal(ExitCodes.Usage, badName.ExitCode);
        Assert.Null(host.ReadConfig().Queries);
    }

    [Fact]
    public async Task List_ShowsScopes_AndProfileOnlyWinsOnAClash()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "errors", "level:3");
        await host.RunAsync("query", "save", "errors", "level:2", "--profile-only");
        await host.RunAsync("query", "save", "shared-only", "level:4", "--tag", "ops");

        var onDev = await host.RunAsync("query", "list", "-o", "jsonl");
        var onProd = await host.RunAsync("query", "list", "-o", "jsonl", "--profile", "prod");
        var tagged = await host.RunAsync("query", "list", "-o", "jsonl", "--tag", "OPS");

        var devRows = onDev.Out.Trim().Split(Environment.NewLine).Select(line => JsonNode.Parse(line)!).ToList();
        Assert.Equal(2, devRows.Count);
        var errors = devRows.Single(r => r["name"]!.GetValue<string>() == "errors");
        Assert.Equal("profile:dev", errors["scope"]!.GetValue<string>());
        Assert.Equal("level:2", errors["query"]!.GetValue<string>());

        Assert.Contains("\"scope\":\"shared\"", onProd.Out);
        Assert.Contains("level:3", onProd.Out);
        Assert.DoesNotContain("level:2", onProd.Out);

        Assert.Single(tagged.Out.Trim().Split(Environment.NewLine));
    }

    [Fact]
    public async Task Show_PrintsTheQuery_WithItsScopeAndParameters()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "svc", "source:{{service}}", "--range", "1h");

        var result = await host.RunAsync("query", "show", "svc");

        var shown = JsonNode.Parse(result.Out)!;
        Assert.Equal("svc", shown["name"]!.GetValue<string>());
        Assert.Equal("shared", shown["scope"]!.GetValue<string>());
        Assert.Equal("1h", shown["range"]!.GetValue<string>());
        Assert.Equal("service", shown["params"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Rm_DeletesTheVisibleQuery()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "errors", "level:3");

        var removed = await host.RunAsync("query", "rm", "errors");
        var missing = await host.RunAsync("query", "rm", "errors");

        Assert.Equal(ExitCodes.Success, removed.ExitCode);
        Assert.Empty(host.ReadConfig().Queries!);
        Assert.Equal(ExitCodes.Usage, missing.ExitCode);
    }

    [Fact]
    public async Task Run_SubstitutesParameters_AndUsesTheStoredOptions()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "svc", "source:{{service}} AND level:<=3", "--range", "1h", "--limit", "50", "--stream", "Payments");

        var result = await host.RunAsync("query", "run", "svc", "--param", "service=payments api", "-o", "jsonl");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var body = LastBody(host);
        Assert.Equal("source:\"payments api\" AND level:<=3", body["queries"]![0]!["query"]!["query_string"]!.GetValue<string>());
        Assert.Equal(3600, body["queries"]![0]!["timerange"]!["range"]!.GetValue<long>());
        Assert.Equal(50, body["queries"]![0]!["search_types"]![0]!["limit"]!.GetValue<int>());
        Assert.Equal("5f0000000000000000000003", body["queries"]![0]!["filter"]!["filters"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_OverridesStoredOptions_ForThisRunOnly()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "q", "*", "--range", "1h", "--limit", "50");

        await host.RunAsync("query", "run", "q", "--from", "2026-09-24T08:00:00Z", "--to", "2026-09-24T09:00:00Z", "--limit", "10");

        var body = LastBody(host);
        Assert.Equal("absolute", body["queries"]![0]!["timerange"]!["type"]!.GetValue<string>());
        Assert.Equal(10, body["queries"]![0]!["search_types"]![0]!["limit"]!.GetValue<int>());
        Assert.Equal("1h", host.ReadConfig().Queries!["q"].Range);
        Assert.Equal(50, host.ReadConfig().Queries!["q"].Limit);
    }

    [Fact]
    public async Task Run_FailsClearly_OnMissingParameters()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "q", "source:{{service}} AND env:{{env}}");

        var result = await host.RunAsync("query", "run", "q");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("Missing parameters: service, env", result.Err);
        Assert.DoesNotContain(host.Graylog!.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Run_OnProduction_WithALargeLimit_NeedsYes()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "big", "*", "--limit", "5000");

        var refused = await host.RunAsync("query", "run", "big", "--profile", "prod");
        var confirmed = await host.RunAsync("query", "run", "big", "--profile", "prod", "--yes");

        Assert.Equal(ExitCodes.Usage, refused.ExitCode);
        Assert.Contains("--yes", refused.Err);
        Assert.Equal(ExitCodes.Success, confirmed.ExitCode);
        Assert.Contains("PRODUCTION", confirmed.Err);
    }

    [Fact]
    public async Task Edit_ValidatesBeforeSaving()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "q", "level:3", "--range", "1h");

        host.Editor = path =>
        {
            File.WriteAllText(path, """{ "query": "level:2", "range": "2h", "description": "edited" }""");
            return 0;
        };
        var saved = await host.RunAsync("query", "edit", "q");

        host.Editor = path =>
        {
            File.WriteAllText(path, """{ "query": "level:2", "range": "forever" }""");
            return 0;
        };
        var rejected = await host.RunAsync("query", "edit", "q");

        Assert.Equal(ExitCodes.Success, saved.ExitCode);
        Assert.Equal(ExitCodes.Usage, rejected.ExitCode);
        var query = host.ReadConfig().Queries!["q"];
        Assert.Equal("level:2", query.Query);
        Assert.Equal("2h", query.Range);
        Assert.Equal("edited", query.Description);
        Assert.Empty(Directory.GetFiles(host.Paths.Folder, "*.edit.json"));
    }

    [Fact]
    public async Task ExportThenImport_MovesQueriesBetweenPeople()
    {
        using var alice = Host();
        await alice.RunAsync("query", "save", "svc", "source:{{service}}", "--range", "1h", "--tag", "ops");
        await alice.RunAsync("query", "save", "mine", "level:1", "--profile-only");

        var export = await alice.RunAsync("query", "export");

        Assert.Equal(ExitCodes.Success, export.ExitCode);
        Assert.DoesNotContain(TestHost.Token, export.Out);
        Assert.DoesNotContain("graylog.dev", export.Out);
        Assert.DoesNotContain("token", export.Out, StringComparison.OrdinalIgnoreCase);

        using var bob = Host();
        var file = Path.Combine(bob.Folder, "queries.json");
        Directory.CreateDirectory(bob.Folder);
        File.WriteAllText(file, export.Out);

        var import = await bob.RunAsync("query", "import", file);

        Assert.Equal(ExitCodes.Success, import.ExitCode);
        var queries = bob.ReadConfig().Queries!;
        Assert.Equal("source:{{service}}", queries["svc"].Query);
        Assert.Equal(["ops"], queries["svc"].Tags!);
        Assert.Equal("level:1", queries["mine"].Query);
    }

    [Fact]
    public async Task Export_OnlyNamedQueries()
    {
        using var host = Host();
        await host.RunAsync("query", "save", "a", "level:1");
        await host.RunAsync("query", "save", "b", "level:2");

        var result = await host.RunAsync("query", "export", "b");

        var queries = JsonNode.Parse(result.Out)!["queries"]!.AsObject();
        Assert.Equal(["b"], queries.Select(pair => pair.Key));
    }

    [Theory]
    [InlineData("""{ "queries": { "q": { "query": "*", "token": "abc" } } }""")]
    [InlineData("""{ "queries": { "q": { "query": "*" } }, "profiles": { "p": { "api_key": "abc" } } }""")]
    [InlineData("""{ "queries": { "q": { "query": "*", "params": { "Password": { "default": "x" } } } } }""")]
    public async Task Import_RejectsTokenLikeKeys(string json)
    {
        using var host = Host();
        var file = Path.Combine(host.Folder, "bad.json");
        File.WriteAllText(file, json);

        var result = await host.RunAsync("query", "import", file);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("looks like a secret", result.Err);
        Assert.Null(host.ReadConfig().Queries);
    }

    [Theory]
    [InlineData("""{ "queries": { "q": { "query": "*", "url": "https://x" } } }""")]
    [InlineData("""{ "queries": { "q": { "range": "1h" } } }""")]
    [InlineData("""{ "q": { "query": "*" } }""")]
    public async Task Import_RejectsAnythingButValidQueries(string json)
    {
        using var host = Host();
        var file = Path.Combine(host.Folder, "bad.json");
        File.WriteAllText(file, json);

        var result = await host.RunAsync("query", "import", file);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Null(host.ReadConfig().Queries);
    }

    [Fact]
    public async Task Import_ProfileOnly_AddsToTheActiveProfile()
    {
        using var host = Host();
        var file = Path.Combine(host.Folder, "q.json");
        File.WriteAllText(file, """{ "queries": { "q": { "query": "level:3" } } }""");

        await host.RunAsync("query", "import", file, "--profile-only", "--profile", "prod");

        Assert.Equal("level:3", host.ReadConfig().Profiles!["prod"].Queries!["q"].Query);
    }
}
