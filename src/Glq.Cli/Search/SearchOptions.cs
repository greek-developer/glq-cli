using System.CommandLine;

using Glq.Cli.Commands;
using Glq.Cli.Output;

namespace Glq.Cli.Search;

/// <summary>What a search asks for; any value left <c>null</c> falls back to a default.</summary>
internal sealed record SearchSpec(
    string? Range = null,
    string? From = null,
    string? To = null,
    string? TimeZone = null,
    IReadOnlyList<string>? Streams = null,
    IReadOnlyList<string>? Fields = null,
    int? Limit = null,
    string? Sort = null)
{
    /// <summary>This spec with every value set in <paramref name="overrides"/> taking precedence.</summary>
    internal SearchSpec OverriddenBy(SearchSpec overrides)
    {
        // A range given on the command line replaces a stored range of either kind.
        var overridesRange = overrides.Range is not null || overrides.From is not null || overrides.To is not null;
        return new SearchSpec(
            overridesRange ? overrides.Range : Range,
            overridesRange ? overrides.From : From,
            overridesRange ? overrides.To : To,
            overrides.TimeZone ?? TimeZone,
            overrides.Streams ?? Streams,
            overrides.Fields ?? Fields,
            overrides.Limit ?? Limit,
            overrides.Sort ?? Sort);
    }
}

/// <summary>The search options shared by <c>search</c>, <c>query save</c> and <c>query run</c>.</summary>
internal sealed class SearchOptions
{
    internal Option<string?> Range { get; } = new("--range") { Description = "Relative range ending now: 30s, 15m, 2h, 1d, 1w (default: the profile's default_range)." };

    internal Option<string?> From { get; } = new("--from") { Description = "Absolute range start, ISO 8601. UTC unless it carries an offset or --tz is given." };

    internal Option<string?> To { get; } = new("--to") { Description = "Absolute range end, ISO 8601." };

    internal Option<string?> TimeZone { get; } = new("--tz") { Description = "Time zone for --from/--to values without an offset, e.g. Europe/Athens." };

    internal Option<string[]> Streams { get; } = new("--stream", "-s") { Description = "Stream name or ID. Repeatable (default: the profile's default_streams)." };

    internal Option<string?> Fields { get; } = new("--fields", "-f") { Description = "Comma-separated fields to return (default: the profile's default_fields)." };

    internal Option<int?> Limit { get; } = new("--limit", "-n") { Description = "Maximum messages to return (default: the profile's default_limit)." };

    internal Option<string?> Sort { get; } = new("--sort") { Description = "Sort as <field>:asc|desc (default: timestamp:desc)." };

    internal IEnumerable<Option> All => [Range, From, To, TimeZone, Streams, Fields, Limit, Sort];

    internal void AddTo(Command command)
    {
        foreach (var option in All)
        {
            command.Options.Add(option);
        }
    }

    internal SearchSpec Read(ParseResult parseResult) => new(
        parseResult.GetValue(Range),
        parseResult.GetValue(From),
        parseResult.GetValue(To),
        parseResult.GetValue(TimeZone),
        parseResult.GetValue(Streams) is { Length: > 0 } streams ? streams : null,
        parseResult.GetValue(Fields) is { } fields ? ProfileCommand.SplitList(fields) : null,
        parseResult.GetValue(Limit),
        parseResult.GetValue(Sort));
}

/// <summary>The options that shape a run's output, shared by <c>search</c> and <c>query run</c>.</summary>
internal sealed class RunOptions
{
    internal Option<OutputFormat?> Output { get; } = Formatter.CreateOption();

    internal Option<bool> Raw { get; } = new("--raw") { Description = "Print the untransformed API response, for debugging. Still masked." };

    internal Option<bool> NoMask { get; } = new("--no-mask") { Description = "Turn off masking of emails, phones and mask_fields. Card numbers stay masked." };

    internal Option<bool> Yes { get; } = new("--yes", "-y") { Description = "Confirm a large run against a production profile without a prompt." };

    internal void AddTo(Command command)
    {
        command.Options.Add(Output);
        command.Options.Add(Raw);
        command.Options.Add(NoMask);
        command.Options.Add(Yes);
    }
}
