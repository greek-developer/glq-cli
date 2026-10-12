using System.Text.Json.Nodes;

namespace Glq.Cli.IntegrationTests;

/// <summary>
/// glq against a real Graylog seeded by scripts/itest.ps1 with synthetic messages from the
/// sources itest-api and itest-payments.
/// </summary>
public class GraylogTests
{
    private static async Task<GlqProcess> ConfiguredAsync()
    {
        var glq = new GlqProcess();
        var added = await glq.RunAsync("profile", "add", "local", "--url", Graylog.Url!, "--default-range", "1d");
        Assert.Equal(0, added.ExitCode);
        return glq;
    }

    private static List<JsonNode> Lines(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => JsonNode.Parse(line)!)];

    [GraylogFact]
    public async Task ProfileTest_ReportsTheServerVersion()
    {
        using var glq = await ConfiguredAsync();

        var result = await glq.RunAsync(Graylog.Token, "profile", "test");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("auth: ok", result.Out);
        Assert.Matches(@"server version: \d+\.\d+", result.Out);
    }

    [GraylogFact]
    public async Task ProfileTest_RejectsABadToken()
    {
        using var glq = await ConfiguredAsync();

        var result = await glq.RunAsync("not-a-real-token-0000", "profile", "test");

        Assert.Equal(2, result.ExitCode);
    }

    [GraylogFact]
    public async Task Search_ViaTheViewsApi_FindsMessages()
    {
        using var glq = await ConfiguredAsync();

        var result = await glq.RunAsync(
            Graylog.Token, "search", "source:itest-api", "--fields", "timestamp,source,message,customer_id", "-o", "jsonl");

        Assert.Equal(0, result.ExitCode);
        var rows = Lines(result.Out);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("itest-api", row["source"]!.GetValue<string>()));
        Assert.Contains("C-1001", result.Out);
    }

    [GraylogFact]
    public async Task Search_ViaTheLegacyApi_FindsTheSameMessages()
    {
        using var glq = await ConfiguredAsync();

        // Mark the views API as missing for this profile, as a 404 would, to force the legacy path.
        Directory.CreateDirectory(Path.Combine(glq.GlqFolder, "cache"));
        File.WriteAllText(
            Path.Combine(glq.GlqFolder, "cache", "local.json"),
            new JsonObject { ["url"] = Graylog.Url!.TrimEnd('/'), ["views_api"] = false }.ToJsonString());

        var result = await glq.RunAsync(Graylog.Token, "search", "source:itest-api", "-o", "jsonl", "--verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("/api/search/universal/relative", result.Err);
        Assert.Equal(2, Lines(result.Out).Count);
    }

    [GraylogFact]
    public async Task StreamsList_ShowsTheDefaultStream()
    {
        using var glq = await ConfiguredAsync();

        var result = await glq.RunAsync(Graylog.Token, "streams", "list", "-o", "jsonl");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(Lines(result.Out));
    }

    [GraylogFact]
    public async Task StoredQuery_RunsWithAParameter()
    {
        using var glq = await ConfiguredAsync();
        await glq.RunAsync("query", "save", "by-source", "source:{{service}} AND level:3");

        var result = await glq.RunAsync(Graylog.Token, "query", "run", "by-source", "--param", "service=itest-payments", "-o", "jsonl");

        Assert.Equal(0, result.ExitCode);
        var row = Assert.Single(Lines(result.Out));
        Assert.Equal("payment declined", row["message"]!.GetValue<string>());
    }

    [GraylogFact]
    public async Task Token_NeverReachesOutput()
    {
        using var glq = await ConfiguredAsync();

        var results = new[]
        {
            await glq.RunAsync(Graylog.Token, "profile", "test", "--verbose"),
            await glq.RunAsync(Graylog.Token, "search", "*", "--raw", "--verbose"),
            await glq.RunAsync(Graylog.Token, "profile", "show", "local"),
        };

        Assert.All(results, result => Assert.DoesNotContain(Graylog.Token!, result.Out + result.Err));
    }
}
