namespace Glq.Cli;

/// <summary>
/// A failure with a plain-language message and the exit code it maps to. Anything thrown as
/// this reaches the user as one stderr line; nothing else about it is printed.
/// </summary>
internal sealed class GlqException(int exitCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    internal int ExitCode { get; } = exitCode;

    internal static GlqException Usage(string message) => new(ExitCodes.Usage, message);

    internal static GlqException Auth(string message) => new(ExitCodes.Auth, message);

    internal static GlqException Network(string message, Exception? inner = null) =>
        new(ExitCodes.Network, message, inner);

    internal static GlqException Api(string message) => new(ExitCodes.Api, message);
}
