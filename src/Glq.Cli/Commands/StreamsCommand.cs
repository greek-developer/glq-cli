using System.CommandLine;
using System.Text.Json.Nodes;

using Glq.Cli.Client;
using Glq.Cli.Configuration;
using Glq.Cli.Output;
using Glq.Cli.Search;

namespace Glq.Cli.Commands;

/// <summary><c>glq streams list</c> - see specs/streams.md.</summary>
internal static class StreamsCommand
{
    internal static Command Create(CliContext context)
    {
        var filter = new Option<string?>("--filter") { Description = "Show only streams whose name contains this text (any case)." };
        var refresh = new Option<bool>("--refresh") { Description = "Ignore the 10-minute stream cache." };
        var output = Formatter.CreateOption();
        var json = Formatter.CreateJsonOption();

        var list = new Command("list", "List the streams the token's user can read.") { filter, refresh, output, json };
        list.SetAction(async (parseResult, cancellationToken) =>
        {
            var active = ActiveProfile.Resolve(context, parseResult, context.Store.Load());
            using var connection = GraylogConnection.Open(
                context, active, parseResult.GetValue(GlobalOptions.Insecure), parseResult.GetValue(GlobalOptions.Verbose));

            var streams = await new StreamResolver(connection)
                .ListAsync(parseResult.GetValue(refresh), cancellationToken)
                .ConfigureAwait(false);

            var text = parseResult.GetValue(filter);
            var rows = streams
                .Where(s => string.IsNullOrEmpty(text) || s.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
                .Select(s => new JsonObject { ["name"] = s.Title, ["id"] = s.Id, ["description"] = s.Description })
                .ToList();

            Formatter.Write(
                context.Out,
                Formatter.Resolve(parseResult.GetValue(output), parseResult.GetValue(json), context.StdoutIsTerminal),
                ["name", "id", "description"],
                rows);
            return ExitCodes.Success;
        });

        return new Command("streams", "Inspect Graylog streams.") { list };
    }
}
