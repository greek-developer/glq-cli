using System.Text.Json;

using Glq.Cli.Configuration;

namespace Glq.Cli.Client;

/// <summary>
/// The per-profile cache under <c>~/.grdev.glq-cli/cache/</c>. It holds stream metadata (for
/// ten minutes) and the server version - never message content. An entry written for a
/// different URL is ignored, so re-pointing a profile never serves stale streams.
/// </summary>
internal sealed class ProfileCache(GlqPaths paths, string profileName, string url, TimeProvider time)
{
    internal static readonly TimeSpan StreamsTtl = TimeSpan.FromMinutes(10);

    private string FilePath => Path.Combine(paths.CacheFolder, profileName + ".json");

    internal string? ServerVersion => Load().ServerVersion;

    /// <summary><c>false</c> once the views search API answered 404 on this server.</summary>
    internal bool? ViewsApi => Load().ViewsApi;

    internal IReadOnlyList<StreamInfo>? FreshStreams()
    {
        var entry = Load();
        return entry.StreamsFetchedAt is { } fetched && time.GetUtcNow() - fetched < StreamsTtl && entry.Streams is not null
            ? [.. entry.Streams.Select(s => new StreamInfo(s.Id, s.Title, s.Description))]
            : null;
    }

    internal void SaveServerVersion(string version) => Update(entry => entry.ServerVersion = version);

    internal void SaveViewsApi(bool available) => Update(entry => entry.ViewsApi = available);

    internal void SaveStreams(IReadOnlyList<StreamInfo> streams) => Update(entry =>
    {
        entry.StreamsFetchedAt = time.GetUtcNow();
        entry.Streams = [.. streams.Select(s => new CachedStream { Id = s.Id, Title = s.Title, Description = s.Description })];
    });

    private CacheEntry Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(FilePath), Options);
                if (entry is not null && entry.Url == url)
                {
                    return entry;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged cache is only a cache: start again.
        }

        return new CacheEntry { Url = url };
    }

    private void Update(Action<CacheEntry> change)
    {
        var entry = Load();
        change(entry);
        try
        {
            OwnerOnly.CreateDirectory(paths.Folder);
            OwnerOnly.CreateDirectory(paths.CacheFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entry, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Failing to cache never fails the command.
        }
    }

    private static readonly JsonSerializerOptions Options = new(ConfigJson.Options)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    private sealed class CacheEntry
    {
        public string? Url { get; set; }

        public string? ServerVersion { get; set; }

        public bool? ViewsApi { get; set; }

        public DateTimeOffset? StreamsFetchedAt { get; set; }

        public List<CachedStream>? Streams { get; set; }
    }

    private sealed class CachedStream
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}
