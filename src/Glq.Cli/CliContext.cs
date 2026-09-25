using System.ComponentModel;
using System.Diagnostics;

using Glq.Cli.Configuration;
using Glq.Cli.Output;

namespace Glq.Cli;

/// <summary>
/// Everything a command touches outside itself - output streams, environment, the glq folder,
/// the terminal, HTTP. Program builds the real one; tests build one over temporary folders,
/// captured writers and a fake HTTP handler.
/// </summary>
internal sealed class CliContext
{
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    internal CliContext(TextWriter output, TextWriter error, GlqPaths paths)
    {
        Out = new RedactingTextWriter(output, Secrets);
        Err = new RedactingTextWriter(error, Secrets);
        Paths = paths;
        Store = new ConfigStore(paths);
    }

    internal SecretRegistry Secrets { get; } = new();

    internal TextWriter Out { get; }

    internal TextWriter Err { get; }

    internal TextReader In { get; init; } = Console.In;

    internal GlqPaths Paths { get; }

    internal ConfigStore Store { get; }

    internal Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    internal bool StdoutIsTerminal { get; init; }

    internal bool StdinIsTerminal { get; init; }

    internal TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Replaces the network for tests; <c>null</c> means real sockets.</summary>
    internal Func<HttpMessageHandler>? PrimaryHandler { get; init; }

    /// <summary>Reads a secret without echoing it.</summary>
    internal Func<string?> ReadSecret { get; init; } = ConsoleSecret.Read;

    /// <summary>Opens a file in the user's editor and returns the editor's exit code.</summary>
    internal Func<string, int> RunEditor { get; init; } = Editor.Run;

    /// <summary>Runs a profile's <c>token_command</c> and returns its standard output.</summary>
    internal Func<string, string> RunTokenCommand { get; init; } = ShellCommand.Run;

    internal static CliContext CreateDefault() => new(Console.Out, Console.Error, GlqPaths.Default)
    {
        StdoutIsTerminal = !Console.IsOutputRedirected,
        StdinIsTerminal = !Console.IsInputRedirected,
    };

    /// <summary>Writes a warning to stderr once per run, however often it is raised.</summary>
    internal void WarnOnce(string message)
    {
        if (_warned.Add(message))
        {
            Err.WriteLine(message);
        }
    }

    /// <summary>
    /// Asks a yes/no question on stderr when stdin is a terminal. Returns <c>false</c> - never
    /// blocks - when there is no one to ask.
    /// </summary>
    internal bool Confirm(string question)
    {
        if (!StdinIsTerminal)
        {
            return false;
        }

        Err.Write($"{question} [y/N] ");
        Err.Flush();
        var answer = In.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The editor fallback shared by <c>config edit</c> and <c>query edit</c>.</summary>
internal static class Editor
{
    internal static string Resolve(Func<string, string?> getEnvironmentVariable) =>
        NonEmpty(getEnvironmentVariable("VISUAL"))
        ?? NonEmpty(getEnvironmentVariable("EDITOR"))
        ?? (OperatingSystem.IsWindows() ? "notepad" : "vi");

    internal static int Run(string path)
    {
        var editor = Resolve(Environment.GetEnvironmentVariable);
        try
        {
            // ArgumentList quotes the path itself, so spaces and quotes survive intact.
            var info = new ProcessStartInfo(editor) { UseShellExecute = false };
            info.ArgumentList.Add(path);

            using var process = Process.Start(info)
                ?? throw GlqException.Usage($"Could not launch editor '{editor}'.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw GlqException.Usage($"Could not launch editor '{editor}': {exception.Message}. Set $VISUAL or $EDITOR.");
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Runs a shell command and captures its standard output.</summary>
internal static class ShellCommand
{
    internal static string Run(string command)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        // The command's stderr goes straight to the user's terminal, not through glq.
        info.RedirectStandardError = false;

        try
        {
            using var process = Process.Start(info)
                ?? throw GlqException.Usage("token_command could not be started.");
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Never echo the output: on failure it may still hold part of a secret.
            return process.ExitCode == 0
                ? output
                : throw GlqException.Usage($"token_command exited with code {process.ExitCode}.");
        }
        catch (Win32Exception exception)
        {
            throw GlqException.Usage($"token_command could not be started: {exception.Message}");
        }
    }
}

/// <summary>Reads a line from the console without echoing it.</summary>
internal static class ConsoleSecret
{
    internal static string? Read()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
