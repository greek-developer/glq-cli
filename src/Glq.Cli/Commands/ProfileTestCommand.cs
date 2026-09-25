using System.CommandLine;

using Glq.Cli.Auth;
using Glq.Cli.Client;
using Glq.Cli.Configuration;

namespace Glq.Cli.Commands;

/// <summary><c>glq profile test [name]</c> - connectivity, authentication and server version.</summary>
internal static class ProfileTestCommand
{
    internal static Command Create(CliContext context)
    {
        var name = new Argument<string?>("name") { Description = "The profile to test (default: the active profile).", Arity = ArgumentArity.ZeroOrOne };
        var command = new Command("test", "Check connectivity and authentication, and print the Graylog server version.") { name };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = context.Store.Load();
            var active = parseResult.GetValue(name) is { } requested
                ? ActiveProfile.Named(config, requested)
                : ActiveProfile.Resolve(context, parseResult, config);

            using var connection = GraylogConnection.Open(
                context, active, parseResult.GetValue(GlobalOptions.Insecure), parseResult.GetValue(GlobalOptions.Verbose));

            context.Out.WriteLine($"profile: {active.Name}");
            context.Out.WriteLine($"url: {active.Profile.Url}");
            context.Out.WriteLine($"token: from {TokenResolver.DescribeSource(context, active.Profile)}");

            try
            {
                var system = await connection.Client.GetSystemAsync(cancellationToken).ConfigureAwait(false);
                connection.Cache.SaveServerVersion(system.Version);
                context.Out.WriteLine("auth: ok");
                context.Out.WriteLine($"server version: {system.Version}");
            }
            catch (GlqException exception) when (exception.ExitCode == ExitCodes.Auth && exception.Message.Contains("(403)", StringComparison.Ordinal))
            {
                // A 403 means the token was accepted; its user may just lack system-read permission.
                context.Out.WriteLine("auth: ok");
                context.Out.WriteLine("server version: unknown - the token's user may not read system information");
            }
            catch (GlqException exception) when (exception.ExitCode == ExitCodes.Auth)
            {
                context.Out.WriteLine("auth: failed");
                throw;
            }

            return ExitCodes.Success;
        });
        return command;
    }
}
