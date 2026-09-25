using System.Diagnostics;

namespace Glq.Cli.IntegrationTests;

/// <summary>
/// Runs the built glq tool as a separate process, with a temporary home directory so the
/// developer's own ~/.grdev.glq-cli is never touched.
/// </summary>
internal sealed class GlqProcess : IDisposable
{
    internal GlqProcess()
    {
        Home = Path.Combine(Path.GetTempPath(), "glq-itest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
    }

    internal string Home { get; }

    internal string GlqFolder => Path.Combine(Home, ".grdev.glq-cli");

    internal async Task<(int ExitCode, string Out, string Err)> RunAsync(params string[] args) =>
        await RunAsync(token: null, args);

    internal async Task<(int ExitCode, string Out, string Err)> RunAsync(string? token, params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "glq.dll"));
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["HOME"] = Home;
        info.Environment["USERPROFILE"] = Home;
        info.Environment.Remove("GLQ_PROFILE");
        info.Environment.Remove("GLQ_TOKEN");
        if (token is not null)
        {
            info.Environment["GLQ_TOKEN"] = token;
        }

        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>A test that needs the local Graylog from scripts/itest.ps1; skipped without it.</summary>
public sealed class GraylogFactAttribute : FactAttribute
{
    public GraylogFactAttribute()
    {
        if (string.IsNullOrEmpty(Graylog.Url) || string.IsNullOrEmpty(Graylog.Token))
        {
            Skip = "Needs the local Graylog - run scripts/itest.ps1.";
        }
    }
}

internal static class Graylog
{
    internal static string? Url => Environment.GetEnvironmentVariable("GLQ_ITEST_URL");

    internal static string? Token => Environment.GetEnvironmentVariable("GLQ_ITEST_TOKEN");
}
