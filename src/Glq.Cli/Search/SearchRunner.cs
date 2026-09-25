using System.CommandLine;
using System.Text.Json;

using Glq.Cli.Backends;
using Glq.Cli.Client;
using Glq.Cli.Commands;
using Glq.Cli.Configuration;
using Glq.Cli.Masking;
using Glq.Cli.Output;

namespace Glq.Cli.Search;

/// <summary>
/// Runs one search end to end for <c>search</c> and <c>query run</c>: applies profile
/// defaults and limits, asks for confirmation where production needs it, searches, masks and
/// prints.
/// </summary>
internal static class SearchRunner
{
    private static readonly JsonSerializerOptions RawJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static async Task<int> RunAsync(
        CliContext context,
        ParseResult parseResult,
        ActiveProfile active,
        string query,
        SearchSpec spec,
        RunOptions run,
        CancellationToken cancellationToken)
    {
        var profile = active.Profile;
        var config = active.Config;

        var range = TimeRange.Resolve(spec.Range, spec.From, spec.To, spec.TimeZone, profile.EffectiveDefaultRange);
        var limit = spec.Limit ?? profile.EffectiveDefaultLimit;
        if (limit <= 0)
        {
            throw GlqException.Usage("--limit must be greater than zero.");
        }

        if (limit > config.EffectiveMaxLimit)
        {
            throw GlqException.Usage($"--limit {limit} is above max_limit ({config.EffectiveMaxLimit}) in config.json.");
        }

        var fields = spec.Fields is { Count: > 0 } requested ? requested : profile.EffectiveDefaultFields;
        var sort = SortSpec.Parse(spec.Sort);

        using var connection = GraylogConnection.Open(
            context, active, parseResult.GetValue(GlobalOptions.Insecure), parseResult.GetValue(GlobalOptions.Verbose));

        if (profile.IsProduction && limit > config.EffectiveConfirmLimit && !parseResult.GetValue(run.Yes))
        {
            if (!context.Confirm($"Fetch up to {limit} messages from PRODUCTION profile '{active.Name}'?"))
            {
                throw GlqException.Usage(context.StdinIsTerminal
                    ? "Cancelled."
                    : $"--limit {limit} is above the production confirmation threshold ({config.EffectiveConfirmLimit}); pass --yes to run it.");
            }
        }

        var streamNames = spec.Streams is { Count: > 0 } streams ? streams : profile.EffectiveDefaultStreams;
        var streamIds = streamNames.Count == 0
            ? []
            : await new StreamResolver(connection).ResolveAsync(streamNames, cancellationToken).ConfigureAwait(false);

        var request = new SearchRequest(query, range, streamIds, fields, limit, sort);
        var verbose = parseResult.GetValue(GlobalOptions.Verbose);
        var result = await new SearchService(connection, context.Err, verbose).SearchAsync(request, cancellationToken).ConfigureAwait(false);

        var masker = Masker.Create(context.Store, config, profile, enabled: !parseResult.GetValue(run.NoMask));

        if (parseResult.GetValue(run.Raw))
        {
            var raw = masker.MaskTree(result.Raw?.DeepClone());
            context.Out.WriteLine(raw is null ? "null" : raw.ToJsonString(RawJson));
            return ExitCodes.Success;
        }

        foreach (var message in result.Messages)
        {
            masker.MaskRow(message);
        }

        var format = Formatter.Resolve(parseResult.GetValue(run.Output), context.StdoutIsTerminal);
        if (format is OutputFormat.Table && result.Messages.Count == 0)
        {
            context.Err.WriteLine("No messages matched.");
            return ExitCodes.Success;
        }

        Formatter.Write(context.Out, format, result.Fields, result.Messages);
        return ExitCodes.Success;
    }
}
