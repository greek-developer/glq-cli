using Glq.Cli.Configuration;

namespace Glq.Cli.UnitTests;

/// <summary>
/// Runs glq in-process against a temporary glq folder, captured output, a fake environment
/// and, when given, a fake Graylog.
/// </summary>
internal sealed class TestHost : IDisposable
{
    internal const string Token = "tok3n-v3ry-s3cr3t-abcdef0123456789";

    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    internal TestHost()
    {
        Folder = Path.Combine(Path.GetTempPath(), "glq-tests", Guid.NewGuid().ToString("N"));
        Paths = new GlqPaths(Path.Combine(Folder, GlqPaths.FolderName));
    }

    internal string Folder { get; }

    internal GlqPaths Paths { get; }

    internal FakeGraylog? Graylog { get; set; }

    internal bool StdoutIsTerminal { get; set; }

    internal bool StdinIsTerminal { get; set; }

    internal string Input { get; set; } = string.Empty;

    internal string? Secret { get; set; }

    internal Func<string, int>? Editor { get; set; }

    internal Func<string, string>? TokenCommand { get; set; }

    internal List<string> AllOutput { get; } = [];

    internal ConfigStore Store => new(Paths);

    internal void SetEnvironment(string name, string? value) => _environment[name] = value;

    /// <summary>Writes config.json exactly as glq would - owner-only.</summary>
    internal void WriteConfig(string json) => Store.WriteOwnerOnly(Paths.ConfigFile, json);

    internal GlqConfig ReadConfig() => ConfigStore.Parse(File.ReadAllText(Paths.ConfigFile));

    internal async Task<RunResult> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext(output, error, Paths)
        {
            In = new StringReader(Input),
            GetEnvironmentVariable = name => _environment.GetValueOrDefault(name),
            StdoutIsTerminal = StdoutIsTerminal,
            StdinIsTerminal = StdinIsTerminal,
            PrimaryHandler = Graylog is null ? null : () => Graylog,
            ReadSecret = () => Secret,
            RunEditor = path => Editor?.Invoke(path) ?? 0,
            RunTokenCommand = command => TokenCommand?.Invoke(command) ?? throw new InvalidOperationException("no token command"),
        };

        var exitCode = await Program.RunAsync(args, context);
        var result = new RunResult(exitCode, output.ToString(), error.ToString());
        AllOutput.Add(result.Out);
        AllOutput.Add(result.Err);
        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

internal sealed record RunResult(int ExitCode, string Out, string Err);
