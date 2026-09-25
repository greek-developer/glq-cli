using System.Text.Json;

namespace Glq.Cli.Configuration;

/// <summary>
/// Loads and saves <c>config.json</c>. The file is written atomically and owner-only, and a
/// load refuses a file that anyone else can read.
/// </summary>
internal sealed class ConfigStore(GlqPaths paths)
{
    internal GlqPaths Paths { get; } = paths;

    internal bool Exists => File.Exists(Paths.ConfigFile);

    /// <summary>Loads the config, or an empty one when the file does not exist yet.</summary>
    internal GlqConfig Load()
    {
        var path = Paths.ConfigFile;
        if (!File.Exists(path))
        {
            return new GlqConfig();
        }

        EnsureOwnerOnly(path);

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw GlqException.Usage($"{path} is not valid: {exception.Message} Fix it with: glq config edit");
        }
    }

    internal static GlqConfig Parse(string json) =>
        JsonSerializer.Deserialize<GlqConfig>(json, ConfigJson.Options) ?? new GlqConfig();

    internal void Save(GlqConfig config) => WriteOwnerOnly(Paths.ConfigFile, JsonSerializer.Serialize(config, ConfigJson.Options) + "\n");

    /// <summary>Refuses to go on when the file is readable by anyone but its owner.</summary>
    internal static void EnsureOwnerOnly(string path)
    {
        var exposure = OwnerOnly.FindExposure(path);
        if (exposure is not null)
        {
            throw GlqException.Usage(
                $"{path} holds tokens but {exposure}. Restrict it to your user with: glq config protect");
        }
    }

    /// <summary>
    /// Writes a file inside the glq folder so that it is owner-only from the moment it holds
    /// content: a protected temporary file is filled, then moved over the target.
    /// </summary>
    internal void WriteOwnerOnly(string path, string content)
    {
        OwnerOnly.CreateDirectory(Paths.Folder);

        var temporary = path + ".tmp";
        using (File.Create(temporary))
        {
        }

        try
        {
            OwnerOnly.Protect(temporary);
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
