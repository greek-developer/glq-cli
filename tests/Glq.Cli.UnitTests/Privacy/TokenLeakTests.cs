using System.Net;
using System.Text;

namespace Glq.Cli.UnitTests.Privacy;

/// <summary>
/// Runs the whole command surface - successes, failures and debugging output - and asserts
/// the token appears in no output and in no file but config.json.
/// </summary>
public class TokenLeakTests
{
    private static readonly string Credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(TestHost.Token + ":token"));

    // A hostile or buggy server that echoes the token back in every kind of response.
    private static FakeGraylog EchoingGraylog() => new FakeGraylog()
        .OnJson(HttpMethod.Get, "/api/system", $$"""{"version":"6.1.2","note":"{{TestHost.Token}}"}""")
        .OnJson(HttpMethod.Get, "/api/streams", $$"""{"streams":[{"id":"5f0000000000000000000002","title":"API","description":"{{TestHost.Token}}"}]}""")
        .OnJson(HttpMethod.Post, "/api/views/search/sync", """
            {"id":"j","results":{"q1":{"state":"COMPLETE","errors":[],"search_types":{"st1":{"type":"messages","messages":[
              {"message":{"timestamp":"2026-09-24T08:00:00Z","message":"auth header was Basic <<CREDENTIALS>> for <<TOKEN>>"}}]}}}}}
            """.Replace("<<CREDENTIALS>>", Credentials).Replace("<<TOKEN>>", TestHost.Token));

    private static IEnumerable<string[]> CommandSurface(string queryFile) =>
    [
        ["version"],
        ["skill"],
        ["skill", "--json"],
        ["config", "path"],
        ["config", "edit"],
        ["profile", "list"],
        ["profile", "list", "-o", "json"],
        ["profile", "show"],
        ["profile", "show", "dev", "-o", "json"],
        ["profile", "test", "--verbose"],
        ["streams", "list", "--verbose", "--refresh"],
        ["search", "*", "--verbose"],
        ["search", "*", "-o", "json"],
        ["search", "*", "-o", "csv"],
        ["search", "*", "-o", "table"],
        ["search", "*", "--raw", "--verbose"],
        ["search", "*", "--raw"],
        ["search", "*", "--stream", "API"],
        ["query", "save", "q", "source:{{service}}", "--range", "1h"],
        ["query", "list", "-o", "json"],
        ["query", "show", "q"],
        ["query", "run", "q", "--param", "service=api", "--verbose"],
        ["query", "run", "q"],
        ["query", "export"],
        ["query", "import", queryFile],
        ["query", "edit", "q"],
        ["profile", "set-token", "dev"],
        ["profile", "add", "other", "--url", "https://other.example"],
        ["profile", "use", "dev"],
        ["search"],
        ["nonsense"],
        ["--help"],
        ["search", "--help"],
    ];

    [Fact]
    public async Task Token_NeverAppearsInAnyOutput()
    {
        using var host = new TestHost { Graylog = EchoingGraylog(), Secret = TestHost.Token, StdoutIsTerminal = true };
        host.WriteConfig($$"""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" } } }""");
        var queryFile = Path.Combine(host.Folder, "queries.json");
        File.WriteAllText(queryFile, """{ "queries": { "imported": { "query": "level:3" } } }""");

        foreach (var args in CommandSurface(queryFile))
        {
            await host.RunAsync(args);
        }

        AssertNoLeak(host);
    }

    [Fact]
    public async Task Token_NeverAppearsInFailureOutput()
    {
        var failures = new (string Path, Func<HttpRequestMessage, HttpResponseMessage> Respond)[]
        {
            ("/api/system", _ => FakeGraylog.Json($$"""{"type":"ApiError","message":"bad token {{TestHost.Token}}"}""", HttpStatusCode.InternalServerError)),
            ("/api/system", _ => FakeGraylog.Json($"<html>{TestHost.Token}</html>", HttpStatusCode.BadGateway)),
            ("/api/system", _ => throw new HttpRequestException($"connection reset while sending {Credentials}")),
            ("/api/system", _ => throw new InvalidOperationException($"unexpected {TestHost.Token}")),
            ("/api/system", _ => FakeGraylog.Json(string.Empty, HttpStatusCode.Unauthorized)),
            ("/api/system", _ => FakeGraylog.Json($"not json {TestHost.Token}")),
        };

        foreach (var (path, respond) in failures)
        {
            using var host = new TestHost { Graylog = new FakeGraylog().On(HttpMethod.Get, path, respond) };
            host.WriteConfig($$"""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" } } }""");

            var result = await host.RunAsync("profile", "test", "--verbose");

            Assert.NotEqual(ExitCodes.Success, result.ExitCode);
            AssertNoLeak(host);
        }
    }

    [Fact]
    public async Task EnvironmentToken_NeverAppearsInAnyOutput()
    {
        const string environmentToken = "ci-env-token-9f8e7d6c5b4a";
        using var host = new TestHost { Graylog = EchoingGraylog().OnJson(HttpMethod.Get, "/api/system", $$"""{"version":"{{environmentToken}}"}""") };
        host.WriteConfig("""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev" } } }""");
        host.SetEnvironment("GLQ_TOKEN", environmentToken);

        await host.RunAsync("profile", "test", "--verbose");
        await host.RunAsync("search", "*", "--raw");
        await host.RunAsync("profile", "show");

        Assert.All(host.AllOutput, output => Assert.DoesNotContain(environmentToken, output));
        Assert.DoesNotContain(environmentToken, File.ReadAllText(host.Paths.ConfigFile));
    }

    private static void AssertNoLeak(TestHost host)
    {
        Assert.All(host.AllOutput, output =>
        {
            Assert.DoesNotContain(TestHost.Token, output);
            Assert.DoesNotContain(Credentials, output);
        });

        // The token lives in config.json and nowhere else glq writes.
        foreach (var file in Directory.EnumerateFiles(host.Paths.Folder, "*", SearchOption.AllDirectories))
        {
            if (file != host.Paths.ConfigFile)
            {
                Assert.DoesNotContain(TestHost.Token, File.ReadAllText(file));
            }
        }
    }
}
