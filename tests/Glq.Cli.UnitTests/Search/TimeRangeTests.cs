using Glq.Cli.Search;

namespace Glq.Cli.UnitTests.Search;

public class TimeRangeTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("15m", 900)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("1w", 604800)]
    public void ParseRelative_AcceptsEachUnit(string value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TimeRange.ParseRelative(value));
    }

    [Theory]
    [InlineData("15")]
    [InlineData("m")]
    [InlineData("0m")]
    [InlineData("-5m")]
    [InlineData("1y")]
    public void ParseRelative_RejectsOtherShapes(string value)
    {
        var exception = Assert.Throws<GlqException>(() => TimeRange.ParseRelative(value));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
    }

    [Fact]
    public void ParseAbsolute_ReadsValuesWithoutOffset_AsUtc()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero),
            TimeRange.ParseAbsolute("2026-09-24T08:00:00", zone: null));
    }

    [Fact]
    public void ParseAbsolute_ReadsValuesWithoutOffset_InTheGivenZone()
    {
        var athens = TimeRange.ParseZone("Europe/Athens");

        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero),
            TimeRange.ParseAbsolute("2026-09-24T11:00:00", athens).ToUniversalTime());
    }

    [Fact]
    public void ParseAbsolute_KeepsAnExplicitOffset_EvenWithAZone()
    {
        var athens = TimeRange.ParseZone("Europe/Athens");

        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero),
            TimeRange.ParseAbsolute("2026-09-24T08:00:00Z", athens).ToUniversalTime());
    }

    [Fact]
    public void Resolve_FallsBackToTheDefaultRange()
    {
        Assert.Equal(new TimeRange.Relative(TimeSpan.FromMinutes(5)), TimeRange.Resolve(null, null, null, null, "5m"));
    }

    [Fact]
    public void Resolve_RequiresBothEndsOfAnAbsoluteRange_InOrder()
    {
        Assert.Throws<GlqException>(() => TimeRange.Resolve(null, "2026-09-24T08:00:00Z", null, null, "5m"));
        Assert.Throws<GlqException>(() => TimeRange.Resolve(null, "2026-09-24T09:00:00Z", "2026-09-24T08:00:00Z", null, "5m"));
    }

    [Fact]
    public void ParseZone_RejectsAnUnknownZone()
    {
        Assert.Throws<GlqException>(() => TimeRange.ParseZone("Mars/Olympus"));
    }
}
