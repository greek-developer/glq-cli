using System.Net;

namespace Glq.Cli.UnitTests.Commands;

public class StreamsCommandTests
{
    internal const string StreamsJson = """
        {"total":3,"streams":[
          {"id":"000000000000000000000001","title":"All messages","description":"Everything"},
          {"id":"5f0000000000000000000002","title":"API Gateway","description":"Edge traffic"},
          {"id":"5f0000000000000000000003","title":"Payments","description":null}]}
        """;

    internal static TestHost HostWith(FakeGraylog graylog)
    {
        var host = new TestHost { Graylog = graylog };
        host.WriteConfig($$"""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" } } }""");
        return host;
    }

    [Fact]
    public async Task List_PrintsNameIdAndDescription_SortedByName()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/streams", StreamsJson));

        var result = await host.RunAsync("streams", "list", "-o", "csv");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var lines = result.Out.Trim().Split(Environment.NewLine);
        Assert.Equal("name,id,description", lines[0]);
        Assert.Equal("All messages,000000000000000000000001,Everything", lines[1]);
        Assert.StartsWith("API Gateway,", lines[2]);
        Assert.Equal("Payments,5f0000000000000000000003,", lines[3]);
    }

    [Fact]
    public async Task List_FiltersByCaseInsensitiveSubstring()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/streams", StreamsJson));

        var result = await host.RunAsync("streams", "list", "--filter", "gate", "-o", "jsonl");

        var line = Assert.Single(result.Out.Trim().Split(Environment.NewLine));
        Assert.Contains("API Gateway", line);
    }

    [Fact]
    public async Task List_UsesTheCacheForTenMinutes()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/streams", StreamsJson);
        using var host = HostWith(graylog);

        await host.RunAsync("streams", "list");
        await host.RunAsync("streams", "list");
        await host.RunAsync("streams", "list", "--refresh");

        Assert.Equal(2, graylog.Requests.Count);
        var cache = File.ReadAllText(Path.Combine(host.Paths.CacheFolder, "dev.json"));
        Assert.Contains("API Gateway", cache);
        Assert.DoesNotContain(TestHost.Token, cache);
    }

    [Fact]
    public async Task List_ReportsForbiddenWithTheAuthCode()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(HttpMethod.Get, "/api/streams", HttpStatusCode.Forbidden));

        var result = await host.RunAsync("streams", "list");

        Assert.Equal(ExitCodes.Auth, result.ExitCode);
        Assert.Contains("stream-read permission", result.Err);
    }
}
