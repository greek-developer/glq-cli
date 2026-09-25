using System.CommandLine;

using Glq.Cli.Configuration;
using Glq.Cli.Search;

namespace Glq.Cli.Commands;

/// <summary><c>glq search &lt;query&gt;</c> - an ad-hoc search; see specs/search.md.</summary>
internal static class SearchCommand
{
    internal static Command Create(CliContext context)
    {
        var query = new Argument<string>("query") { Description = "Graylog/Lucene query, e.g. 'source:api AND http_status:>=500'. Use '*' for everything." };
        var search = new SearchOptions();
        var run = new RunOptions();

        var command = new Command("search", "Search messages on the active profile.") { query };
        search.AddTo(command);
        run.AddTo(command);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var active = ActiveProfile.Resolve(context, parseResult, context.Store.Load());
            return SearchRunner.RunAsync(
                context, parseResult, active, parseResult.GetValue(query)!, search.Read(parseResult), run, cancellationToken);
        });

        return command;
    }
}
