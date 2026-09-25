using System.Net;
using System.Security.Authentication;
using System.Text;

using Glq.Cli.Client;

namespace Glq.Cli.UnitTests.Client;

public class ProfileTestTests
{
    private static TestHost HostWith(FakeGraylog graylog, bool production = false)
    {
        var host = new TestHost { Graylog = graylog };
        host.WriteConfig($$"""
            { "current_profile": "dev", "profiles": {
              "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}", "production": {{(production ? "true" : "false")}} },
              "prod": { "url": "https://graylog.prod", "token": "{{TestHost.Token}}" } } }
            """);
        return host;
    }

    [Fact]
    public async Task Test_ReportsAuthAndServerVersion()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2+abc"}"""));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("auth: ok", result.Out);
        Assert.Contains("server version: 6.1.2+abc", result.Out);
    }

    [Fact]
    public async Task Test_AuthenticatesWithTheTokenAsBasicUsername()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}""");
        using var host = HostWith(graylog);

        await host.RunAsync("profile", "test");

        var request = Assert.Single(graylog.Requests);
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(TestHost.Token + ":token"));
        Assert.Equal(expected, request.Headers["Authorization"]);
        Assert.Contains("application/json", request.Headers["Accept"]);
    }

    [Fact]
    public async Task ProfileFlag_ChangesTheTargetCluster()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}""");
        using var host = HostWith(graylog);

        await host.RunAsync("profile", "test");
        await host.RunAsync("profile", "test", "--profile", "prod");

        Assert.Equal("graylog.dev", graylog.Requests[0].Uri.Host);
        Assert.Equal("graylog.prod", graylog.Requests[1].Uri.Host);
        Assert.Equal(graylog.Requests[0].Uri.AbsolutePath, graylog.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Unauthorized_ExitsWithTheAuthCode()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(HttpMethod.Get, "/api/system", HttpStatusCode.Unauthorized));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Auth, result.ExitCode);
        Assert.Contains("auth: failed", result.Out);
        Assert.Contains("401", result.Err);
    }

    [Fact]
    public async Task Forbidden_OnSystemInfo_StillMeansAuthenticated()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(HttpMethod.Get, "/api/system", HttpStatusCode.Forbidden));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("auth: ok", result.Out);
        Assert.Contains("server version: unknown", result.Out);
    }

    [Fact]
    public async Task Timeout_ExitsWithTheNetworkCode()
    {
        using var host = HostWith(new FakeGraylog().OnThrow(HttpMethod.Get, "/api/system", new TaskCanceledException("timeout")));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Network, result.ExitCode);
        Assert.Contains("did not answer within 30 seconds", result.Err);
    }

    [Fact]
    public async Task TlsFailure_ExitsWithTheNetworkCode_AndNamesTls()
    {
        using var host = HostWith(new FakeGraylog().OnThrow(
            HttpMethod.Get,
            "/api/system",
            new HttpRequestException("SSL failed", new AuthenticationException("The remote certificate is invalid."))));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Network, result.ExitCode);
        Assert.Contains("TLS error", result.Err);
        Assert.Contains("ca_bundle", result.Err);
    }

    [Fact]
    public async Task ConnectionFailure_ExitsWithTheNetworkCode()
    {
        using var host = HostWith(new FakeGraylog().OnThrow(HttpMethod.Get, "/api/system", new HttpRequestException("No such host is known.")));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Network, result.ExitCode);
        Assert.Contains("No such host", result.Err);
    }

    [Fact]
    public async Task ServerError_ExitsWithTheApiCode_AndGraylogsMessage()
    {
        using var host = HostWith(new FakeGraylog().OnStatus(
            HttpMethod.Get, "/api/system", HttpStatusCode.InternalServerError, """{"type":"ApiError","message":"index unavailable"}"""));

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Api, result.ExitCode);
        Assert.Contains("index unavailable", result.Err);
    }

    [Fact]
    public async Task MissingToken_IsAConfigurationError()
    {
        using var host = new TestHost { Graylog = new FakeGraylog() };
        host.WriteConfig("""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev" } } }""");

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("glq profile set-token dev", result.Err);
    }

    [Fact]
    public async Task Verbose_TracesRequests_WithAuthorizationRedacted()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}"""));

        var result = await host.RunAsync("profile", "test", "--verbose");

        Assert.Contains("> GET https://graylog.dev/api/system", result.Err);
        Assert.Contains("> Authorization: [redacted]", result.Err);
        Assert.Contains("< 200", result.Err);
        Assert.DoesNotContain(TestHost.Token, result.Err);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(TestHost.Token + ":token")), result.Err);
    }

    [Fact]
    public async Task Production_PrintsTheNotice()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}"""), production: true);

        var result = await host.RunAsync("profile", "test");

        Assert.Contains(GraylogConnection.ProductionNotice, result.Err);
    }

    [Fact]
    public async Task Insecure_WarnsOnEveryRun()
    {
        using var host = HostWith(new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}"""));

        var first = await host.RunAsync("profile", "test", "--insecure");
        var second = await host.RunAsync("profile", "test", "--insecure");

        Assert.Contains("TLS certificate verification is OFF", first.Err);
        Assert.Contains("TLS certificate verification is OFF", second.Err);
    }

    [Fact]
    public async Task EnvironmentToken_WinsOverTheConfigToken()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}""");
        using var host = HostWith(graylog);
        host.SetEnvironment("GLQ_TOKEN", "ci-token-0123456789");

        await host.RunAsync("profile", "test");

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ci-token-0123456789:token"));
        Assert.Equal(expected, graylog.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task TokenCommand_SuppliesTheToken()
    {
        var graylog = new FakeGraylog().OnJson(HttpMethod.Get, "/api/system", """{"version":"6.1.2"}""");
        using var host = new TestHost { Graylog = graylog, TokenCommand = _ => "cmd-token-0123456789\n" };
        host.WriteConfig("""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev", "auth_method": "token_command", "token_command": "pass show graylog" } } }""");

        var result = await host.RunAsync("profile", "test");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("cmd-token-0123456789:token"));
        Assert.Equal(expected, graylog.Requests[0].Headers["Authorization"]);
    }
}
