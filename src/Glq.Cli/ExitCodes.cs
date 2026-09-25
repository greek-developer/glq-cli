namespace Glq.Cli;

/// <summary>The process exit-code contract - see specs/tool.md.</summary>
internal static class ExitCodes
{
    /// <summary>Success, including zero results.</summary>
    internal const int Success = 0;

    /// <summary>Usage or configuration error.</summary>
    internal const int Usage = 1;

    /// <summary>Authentication or authorization error (401/403).</summary>
    internal const int Auth = 2;

    /// <summary>Network or TLS error.</summary>
    internal const int Network = 3;

    /// <summary>Graylog API or query error.</summary>
    internal const int Api = 4;
}
