using Glq.Cli.Configuration;

namespace Glq.Cli.UnitTests.Commands;

public class ProfileCommandTests
{
    [Fact]
    public async Task Add_CreatesTheProfile_AndMakesTheFirstOneCurrent()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev/", "--default-range", "1h");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var config = host.ReadConfig();
        Assert.Equal("dev", config.CurrentProfile);
        Assert.Equal("https://graylog.dev", config.Profiles!["dev"].Url);
        Assert.Equal("1h", config.Profiles["dev"].DefaultRange);
        Assert.Null(OwnerOnly.FindExposure(host.Paths.ConfigFile));
    }

    [Fact]
    public async Task Add_KeepsTheCurrentProfile_UnlessUseIsGiven()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");

        await host.RunAsync("profile", "add", "staging", "--url", "https://graylog.staging");
        Assert.Equal("dev", host.ReadConfig().CurrentProfile);

        await host.RunAsync("profile", "add", "prod", "--url", "https://graylog.prod", "--production", "--use");
        Assert.Equal("prod", host.ReadConfig().CurrentProfile);
        Assert.True(host.ReadConfig().Profiles!["prod"].Production);
    }

    [Fact]
    public async Task Add_RefusesADuplicate_UnlessForced()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");

        var duplicate = await host.RunAsync("profile", "add", "dev", "--url", "https://other.dev");
        var forced = await host.RunAsync("profile", "add", "dev", "--url", "https://other.dev", "--force");

        Assert.Equal(ExitCodes.Usage, duplicate.ExitCode);
        Assert.Equal(ExitCodes.Success, forced.ExitCode);
        Assert.Equal("https://other.dev", host.ReadConfig().Profiles!["dev"].Url);
    }

    [Theory]
    [InlineData("graylog.dev")]
    [InlineData("ftp://graylog.dev")]
    public async Task Add_RejectsANonHttpUrl(string url)
    {
        using var host = new TestHost();

        var result = await host.RunAsync("profile", "add", "dev", "--url", url);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.False(File.Exists(host.Paths.ConfigFile));
    }

    [Fact]
    public async Task Add_WarnsAboutPlainHttp()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("profile", "add", "dev", "--url", "http://graylog.dev");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("unencrypted", result.Err);
    }

    [Fact]
    public async Task List_MarksTheCurrentProfile()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");
        await host.RunAsync("profile", "add", "prod", "--url", "https://graylog.prod");

        var result = await host.RunAsync("profile", "list", "-o", "jsonl");

        var lines = result.Out.Trim().Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"current\":\"*\",\"name\":\"dev\"", lines[0]);
        Assert.Contains("\"current\":\"\",\"name\":\"prod\"", lines[1]);
    }

    [Fact]
    public async Task Use_SetsTheCurrentProfile_AndRejectsAnUnknownOne()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");
        await host.RunAsync("profile", "add", "prod", "--url", "https://graylog.prod");

        var used = await host.RunAsync("profile", "use", "prod");
        var unknown = await host.RunAsync("profile", "use", "qa");

        Assert.Equal(ExitCodes.Success, used.ExitCode);
        Assert.Equal("prod", host.ReadConfig().CurrentProfile);
        Assert.Equal(ExitCodes.Usage, unknown.ExitCode);
        Assert.Contains("dev, prod", unknown.Err);
    }

    [Fact]
    public async Task SetToken_StoresTheToken_WithoutPrintingIt()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");
        host.Secret = TestHost.Token;

        var result = await host.RunAsync("profile", "set-token", "dev");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(TestHost.Token, host.ReadConfig().Profiles!["dev"].Token);
        Assert.DoesNotContain(TestHost.Token, result.Out + result.Err);
    }

    [Fact]
    public async Task SetToken_RejectsAnEmptyToken()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");
        host.Secret = "  ";

        var result = await host.RunAsync("profile", "set-token", "dev");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Null(host.ReadConfig().Profiles!["dev"].Token);
    }

    [Fact]
    public async Task Show_ReportsWhereTheTokenComesFrom_NeverTheToken()
    {
        using var host = new TestHost { StdoutIsTerminal = true };
        host.WriteConfig($$"""{ "current_profile": "dev", "profiles": { "dev": { "url": "https://graylog.dev", "token": "{{TestHost.Token}}" } } }""");

        var fromConfig = await host.RunAsync("profile", "show", "dev");
        host.SetEnvironment("GLQ_TOKEN", "env-token-0123456789");
        var fromEnvironment = await host.RunAsync("profile", "show");

        Assert.Contains("token_source: config.json", fromConfig.Out);
        Assert.Contains("token_source: environment variable GLQ_TOKEN", fromEnvironment.Out);
        Assert.DoesNotContain(TestHost.Token, fromConfig.Out + fromConfig.Err + fromEnvironment.Out + fromEnvironment.Err);
        Assert.DoesNotContain("env-token-0123456789", fromEnvironment.Out + fromEnvironment.Err);
    }

    [Fact]
    public async Task Show_NamesTheTokenCommand_WithoutRunningIt()
    {
        using var host = new TestHost { StdoutIsTerminal = true };
        host.WriteConfig("""{ "profiles": { "dev": { "url": "https://graylog.dev", "auth_method": "token_command", "token_command": "pass show graylog" } } }""");
        host.TokenCommand = _ => throw new InvalidOperationException("must not run");

        var result = await host.RunAsync("profile", "show", "dev");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("token_source: token_command (pass show graylog)", result.Out);
    }

    [Fact]
    public async Task Remove_NeedsConfirmation_WhenNotInteractive()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");

        var refused = await host.RunAsync("profile", "remove", "dev");
        Assert.Equal(ExitCodes.Usage, refused.ExitCode);
        Assert.Contains("--yes", refused.Err);
        Assert.True(host.ReadConfig().Profiles!.ContainsKey("dev"));

        var removed = await host.RunAsync("profile", "remove", "dev", "--yes");
        Assert.Equal(ExitCodes.Success, removed.ExitCode);
        Assert.Empty(host.ReadConfig().Profiles!);
        Assert.Null(host.ReadConfig().CurrentProfile);
    }

    [Fact]
    public async Task Remove_AsksInteractively()
    {
        using var host = new TestHost();
        await host.RunAsync("profile", "add", "dev", "--url", "https://graylog.dev");
        host.StdinIsTerminal = true;
        host.Input = "y\n";

        var result = await host.RunAsync("profile", "remove", "dev");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("[y/N]", result.Err);
        Assert.Empty(host.ReadConfig().Profiles!);
    }

    [Fact]
    public async Task Resolution_PrefersTheFlag_ThenTheEnvironment_ThenTheConfig()
    {
        using var host = new TestHost { StdoutIsTerminal = true };
        host.WriteConfig("""
            { "current_profile": "a", "profiles": {
              "a": { "url": "https://a.example" }, "b": { "url": "https://b.example" }, "c": { "url": "https://c.example" } } }
            """);

        Assert.Contains("name: a", (await host.RunAsync("profile", "show")).Out);
        host.SetEnvironment("GLQ_PROFILE", "b");
        Assert.Contains("name: b", (await host.RunAsync("profile", "show")).Out);
        Assert.Contains("name: c", (await host.RunAsync("profile", "show", "--profile", "c")).Out);
    }

    [Fact]
    public async Task Resolution_FailsWithGuidance_WhenNoProfileIsSelected()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("profile", "show");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("glq profile add", result.Err);
    }
}
