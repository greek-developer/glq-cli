using System.Globalization;
using System.Text.RegularExpressions;

namespace Glq.Cli.Search;

/// <summary>A search time range - relative to now, or absolute. See specs/search.md.</summary>
internal abstract partial record TimeRange
{
    internal sealed record Relative(TimeSpan Span) : TimeRange;

    internal sealed record Absolute(DateTimeOffset From, DateTimeOffset To) : TimeRange;

    /// <summary>Parses <c>30s</c>, <c>15m</c>, <c>2h</c>, <c>1d</c>, <c>1w</c>.</summary>
    internal static TimeSpan ParseRelative(string value)
    {
        var match = RelativePattern().Match(value.Trim());
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            throw GlqException.Usage($"'{value}' is not a relative range: use a number and a unit, like 30s, 15m, 2h, 1d or 1w.");
        }

        return match.Groups[2].Value switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            "d" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromDays(amount * 7.0),
        };
    }

    /// <summary>
    /// Parses an ISO 8601 instant. A value without an offset is read in <paramref name="zone"/>,
    /// or UTC when no zone is given.
    /// </summary>
    internal static DateTimeOffset ParseAbsolute(string value, TimeZoneInfo? zone)
    {
        var text = value.Trim();
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw GlqException.Usage($"'{value}' is not an ISO 8601 date-time, like 2026-09-24T08:00:00Z.");
        }

        if (OffsetPattern().IsMatch(text))
        {
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        var effectiveZone = zone ?? TimeZoneInfo.Utc;
        return new DateTimeOffset(local, effectiveZone.GetUtcOffset(local));
    }

    internal static TimeZoneInfo? ParseZone(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zone.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw GlqException.Usage($"'{zone}' is not a known time zone - use an IANA name like Europe/Athens.");
        }
    }

    /// <summary>
    /// The range a search runs over: <c>--range</c>, or <c>--from</c>/<c>--to</c>, or the
    /// fallback relative range when neither is given.
    /// </summary>
    internal static TimeRange Resolve(string? range, string? from, string? to, string? zone, string fallbackRange)
    {
        var hasAbsolute = !string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to);
        if (!string.IsNullOrWhiteSpace(range) && hasAbsolute)
        {
            throw GlqException.Usage("--range and --from/--to cannot be combined - use one or the other.");
        }

        if (!hasAbsolute)
        {
            return new Relative(ParseRelative(string.IsNullOrWhiteSpace(range) ? fallbackRange : range));
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            throw GlqException.Usage("An absolute range needs both --from and --to.");
        }

        var timeZone = ParseZone(zone);
        var start = ParseAbsolute(from, timeZone);
        var end = ParseAbsolute(to, timeZone);
        return start < end
            ? new Absolute(start, end)
            : throw GlqException.Usage("--from must be earlier than --to.");
    }

    [GeneratedRegex(@"^(\d+)([smhdw])$")]
    private static partial Regex RelativePattern();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
