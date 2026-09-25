namespace Glq.Cli.Configuration;

/// <summary>
/// The one folder that holds everything glq keeps: <c>~/.grdev.glq-cli/</c>. There is no user
/// override - the folder is passed in only so tests can point it at a temporary directory.
/// </summary>
internal sealed record GlqPaths(string Folder)
{
    internal const string FolderName = ".grdev.glq-cli";

    internal static GlqPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        FolderName));

    internal string ConfigFile => Path.Combine(Folder, "config.json");

    internal string CacheFolder => Path.Combine(Folder, "cache");
}
