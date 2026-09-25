namespace Glq.Cli.Configuration;

/// <summary>
/// The one folder that holds everything glq keeps: <c>~/.grdev.glq-cli/</c>. There is no
/// glq-specific override; the home directory is the platform's own (<c>USERPROFILE</c> on
/// Windows, <c>HOME</c> elsewhere), and tests pass a temporary folder in directly.
/// </summary>
internal sealed record GlqPaths(string Folder)
{
    internal const string FolderName = ".grdev.glq-cli";

    internal static GlqPaths Default => new(Path.Combine(Home(), FolderName));

    internal string ConfigFile => Path.Combine(Folder, "config.json");

    internal string CacheFolder => Path.Combine(Folder, "cache");

    private static string Home()
    {
        var variable = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");
        return string.IsNullOrWhiteSpace(variable)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : variable;
    }
}
