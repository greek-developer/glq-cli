using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glq.Cli.Configuration;

/// <summary>The JSON conventions of every file glq reads or writes: snake_case keys.</summary>
internal static class ConfigJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A misspelt key is an error, not a silently ignored setting.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}
