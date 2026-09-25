using System.Text.Json;

using Glq.Cli.Commands;

namespace Glq.Cli.UnitTests;

public class ToolCommandTests
{
    [Fact]
    public async Task BareInvocation_PrintsHelp_AndExitsWithUsageError()
    {
        using var host = new TestHost();

        var result = await host.RunAsync();

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("Usage:", result.Out);
    }

    [Fact]
    public async Task UnknownCommand_IsAUsageError()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("frobnicate");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Version_PrintsTheStandardBlock()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("version");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var lines = result.Out.Split(Environment.NewLine);
        Assert.StartsWith("version: ", lines[0]);
        Assert.StartsWith("commit-text: ", lines[1]);
        Assert.StartsWith("commit-sha: ", lines[2]);
        Assert.StartsWith("build-time: ", lines[3]);
        Assert.Equal(string.Empty, lines[4]);
        Assert.Equal("To update: dotnet tool update -g grdev.glq-cli", lines[5]);
    }

    [Fact]
    public async Task Skill_OpensWithSkillFrontmatter()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("skill");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith("---", result.Out);
        Assert.Contains("name: glq", result.Out);
        Assert.Equal(string.Empty, result.Err);
    }

    [Fact]
    public async Task SkillJson_CarriesContentAndHarnessPath()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("skill", "--json");

        using var document = JsonDocument.Parse(result.Out);
        Assert.Equal(SkillCommand.HarnessPath, document.RootElement.GetProperty("path").GetString());
        Assert.StartsWith("---", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ConfigPath_PrintsThePathAndNothingElse()
    {
        using var host = new TestHost();

        var result = await host.RunAsync("config", "path");

        Assert.Equal(host.Paths.ConfigFile + Environment.NewLine, result.Out);
    }

    [Fact]
    public async Task ConfigEdit_CreatesAnOwnerOnlyTemplate_WhenMissing()
    {
        using var host = new TestHost();
        string? opened = null;
        host.Editor = path =>
        {
            opened = path;
            return 0;
        };

        var result = await host.RunAsync("config", "edit");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(host.Paths.ConfigFile, opened);
        Assert.Contains("\"profiles\"", File.ReadAllText(host.Paths.ConfigFile));
        Assert.Null(Glq.Cli.Configuration.OwnerOnly.FindExposure(host.Paths.ConfigFile));
    }

    [Fact]
    public async Task ConfigEdit_ReportsAFailingEditor()
    {
        using var host = new TestHost();
        host.Editor = _ => 3;

        var result = await host.RunAsync("config", "edit");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("exited with code 3", result.Err);
    }

    [Fact]
    public void EditorFallback_PrefersVisual_ThenEditor_ThenPlatformDefault()
    {
        Assert.Equal("code", Editor.Resolve(name => name == "VISUAL" ? "code" : "nano"));
        Assert.Equal("nano", Editor.Resolve(name => name == "EDITOR" ? "nano" : null));
        Assert.Equal(OperatingSystem.IsWindows() ? "notepad" : "vi", Editor.Resolve(_ => null));
    }
}
