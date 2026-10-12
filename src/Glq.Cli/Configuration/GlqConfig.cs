using Glq.Cli.Queries;

namespace Glq.Cli.Configuration;

/// <summary>
/// The whole of <c>config.json</c>: profiles, their tokens and shared queries.
/// Optional settings are nullable so an unset value stays out of the file; the
/// <c>Effective…</c> members apply the documented defaults.
/// </summary>
internal sealed class GlqConfig
{
    internal const int DefaultMaxLimit = 10_000;

    internal const int DefaultConfirmLimit = 1_000;

    public string? CurrentProfile { get; set; }

    public int? MaxLimit { get; set; }

    public int? ConfirmLimit { get; set; }

    public SortedDictionary<string, StoredQuery>? Queries { get; set; }

    public SortedDictionary<string, Profile>? Profiles { get; set; }

    internal int EffectiveMaxLimit => MaxLimit ?? DefaultMaxLimit;

    internal int EffectiveConfirmLimit => ConfirmLimit ?? DefaultConfirmLimit;

    internal SortedDictionary<string, Profile> ProfilesOrEmpty => Profiles ??= new(StringComparer.Ordinal);

    internal SortedDictionary<string, StoredQuery> QueriesOrEmpty => Queries ??= new(StringComparer.Ordinal);
}

/// <summary>One named Graylog target - see specs/profiles.md for every field.</summary>
internal sealed class Profile
{
    internal const string AuthToken = "token";

    internal const string AuthTokenCommand = "token_command";

    internal const string DefaultRangeValue = "5m";

    internal const int DefaultLimitValue = 100;

    internal const int DefaultTimeoutSeconds = 30;

    internal static readonly IReadOnlyList<string> BuiltInFields = ["timestamp", "source", "message"];

    public string? Url { get; set; }

    public string? AuthMethod { get; set; }

    public string? Token { get; set; }

    public string? TokenCommand { get; set; }

    public bool? VerifyTls { get; set; }

    public string? CaBundle { get; set; }

    public string? Proxy { get; set; }

    public int? TimeoutSeconds { get; set; }

    public bool? Production { get; set; }

    public string? DefaultRange { get; set; }

    public List<string>? DefaultStreams { get; set; }

    public int? DefaultLimit { get; set; }

    public List<string>? DefaultFields { get; set; }

    public SortedDictionary<string, StoredQuery>? Queries { get; set; }

    internal string EffectiveAuthMethod => AuthMethod ?? AuthToken;

    internal bool EffectiveVerifyTls => VerifyTls ?? true;

    internal int EffectiveTimeoutSeconds => TimeoutSeconds ?? DefaultTimeoutSeconds;

    internal bool IsProduction => Production ?? false;

    internal string EffectiveDefaultRange => DefaultRange ?? DefaultRangeValue;

    internal IReadOnlyList<string> EffectiveDefaultStreams => DefaultStreams ?? [];

    internal int EffectiveDefaultLimit => DefaultLimit ?? DefaultLimitValue;

    internal IReadOnlyList<string> EffectiveDefaultFields => DefaultFields is { Count: > 0 } ? DefaultFields : BuiltInFields;

    internal SortedDictionary<string, StoredQuery> QueriesOrEmpty => Queries ??= new(StringComparer.Ordinal);
}
