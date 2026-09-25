using Glq.Cli.Configuration;

namespace Glq.Cli.UnitTests.Configuration;

public class ConfigStoreTests
{
    [Fact]
    public void Load_ReturnsAnEmptyConfig_WhenTheFileDoesNotExist()
    {
        using var host = new TestHost();

        var config = host.Store.Load();

        Assert.Null(config.CurrentProfile);
        Assert.Empty(config.ProfilesOrEmpty);
    }

    [Fact]
    public void Save_WritesSnakeCaseKeys_AndOmitsUnsetValues()
    {
        using var host = new TestHost();
        var config = new GlqConfig { CurrentProfile = "dev" };
        config.ProfilesOrEmpty["dev"] = new Profile { Url = "https://graylog.dev", VerifyTls = false, DefaultRange = "1h" };

        host.Store.Save(config);

        var json = File.ReadAllText(host.Paths.ConfigFile);
        Assert.Contains("\"current_profile\": \"dev\"", json);
        Assert.Contains("\"verify_tls\": false", json);
        Assert.Contains("\"default_range\": \"1h\"", json);
        Assert.DoesNotContain("timeout_seconds", json);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        using var host = new TestHost();
        var config = new GlqConfig { MaskSalt = "salt" };
        config.QueriesOrEmpty["errors"] = new Queries.StoredQuery { Query = "level:3", Tags = ["ops"] };

        host.Store.Save(config);
        var loaded = host.Store.Load();

        Assert.Equal("salt", loaded.MaskSalt);
        Assert.Equal("level:3", loaded.QueriesOrEmpty["errors"].Query);
        Assert.Equal(["ops"], loaded.QueriesOrEmpty["errors"].Tags!);
    }

    [Fact]
    public void Save_CreatesAnOwnerOnlyFile()
    {
        using var host = new TestHost();

        host.Store.Save(new GlqConfig());

        Assert.Null(OwnerOnly.FindExposure(host.Paths.ConfigFile));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.Paths.ConfigFile));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(host.Paths.Folder));
        }
    }

    [Fact]
    public void Load_Refuses_AFileReadableByOthers()
    {
        using var host = new TestHost();
        host.Store.Save(new GlqConfig());
        MakeReadableByOthers(host.Paths.ConfigFile);

        var exception = Assert.Throws<GlqException>(() => host.Store.Load());

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("glq config protect", exception.Message);
    }

    [Fact]
    public async Task ConfigProtect_FixesAFileReadableByOthers()
    {
        using var host = new TestHost();
        host.Store.Save(new GlqConfig());
        MakeReadableByOthers(host.Paths.ConfigFile);

        var result = await host.RunAsync("config", "protect");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Null(OwnerOnly.FindExposure(host.Paths.ConfigFile));
    }

    [Fact]
    public void Load_ReportsAMisspeltKey()
    {
        using var host = new TestHost();
        host.WriteConfig("{ \"curent_profile\": \"dev\" }");

        var exception = Assert.Throws<GlqException>(() => host.Store.Load());

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("curent_profile", exception.Message);
    }

    internal static void MakeReadableByOthers(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                System.Security.AccessControl.FileSystemRights.Read,
                System.Security.AccessControl.AccessControlType.Allow));
            info.SetAccessControl(security);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
    }
}
