using System.CommandLine;

namespace Glq.Cli;

internal static class Program
{
    internal static int Main(string[] args) =>
        new RootCommand("glq: read-only Graylog queries.").Parse(args).Invoke();
}
