using System.CommandLine;

namespace Glq.Cli.Commands;

/// <summary>Options every command accepts.</summary>
internal static class GlobalOptions
{
    internal static readonly Option<string?> Profile = new("--profile", "-p")
    {
        Description = "Profile to use (default: $GLQ_PROFILE, then current_profile in config.json).",
        Recursive = true,
    };

    internal static readonly Option<bool> Insecure = new("--insecure")
    {
        Description = "Skip TLS certificate verification for this run. Prints a warning.",
        Recursive = true,
    };

    internal static readonly Option<bool> Verbose = new("--verbose", "-v")
    {
        Description = "Trace HTTP requests to stderr, with the Authorization header redacted.",
        Recursive = true,
    };
}
