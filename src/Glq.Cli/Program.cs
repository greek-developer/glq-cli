using System.CommandLine;
using System.Text;

using Glq.Cli.Commands;

namespace Glq.Cli;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        UseUtf8Output();
        return await RunAsync(args, CliContext.CreateDefault()).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var root = BuildRootCommand(context);

        // System.CommandLine's own handler would print a stack trace; turning it off lets every
        // failure reach the catch below and leave as one plain stderr line.
        var invocation = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = context.Out,
            Error = context.Err,
        };

        try
        {
            // A bare invocation shows help and reports a usage error - it never performs an action.
            if (args.Length == 0)
            {
                await root.Parse("--help").InvokeAsync(invocation).ConfigureAwait(false);
                return ExitCodes.Usage;
            }

            return await root.Parse(args).InvokeAsync(invocation).ConfigureAwait(false);
        }
        catch (GlqException exception)
        {
            context.Err.WriteLine(exception.Message);
            return exception.ExitCode;
        }
        catch (Exception exception)
        {
            // Nothing unhandled should reach an agent as a stack trace.
            context.Err.WriteLine($"Unexpected error: {exception.Message}");
            return ExitCodes.Usage;
        }
        finally
        {
            context.Out.Flush();
            context.Err.Flush();
        }
    }

    internal static RootCommand BuildRootCommand(CliContext context) => new(
        "glq: read-only Graylog queries with named profiles and stored queries.")
    {
        GlobalOptions.Profile,
        GlobalOptions.Insecure,
        GlobalOptions.Verbose,
        SearchCommand.Create(context),
        ProfileCommand.Create(context),
        QueryCommand.Create(context),
        StreamsCommand.Create(context),
        ConfigCommand.Create(context),
        VersionCommand.Create(context),
        SkillCommand.Create(context),
    };

    // Windows consoles default to a legacy code page, which silently transliterates anything
    // outside it - fine on screen, corrupted the moment output is redirected or piped.
    private static void UseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // Some redirected handles refuse the change; plain ASCII output still works.
        }
    }
}
