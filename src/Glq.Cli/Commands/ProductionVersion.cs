using System.Text.Json;

namespace Glq.Cli.Commands;

// Generated at build time (see GenerateProductionVersion in Glq.Cli.csproj) - never hand-edit.
internal sealed record ProductionVersion(
    string Version,
    string CommitMessage,
    string CommitSha,
    string BuildTime)
{
    internal const string FileName = "ProductionVersion.json";

    internal static ProductionVersion Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetString();
            var commitMessage = root.GetProperty("commit").GetProperty("message").GetString();
            var commitSha = root.GetProperty("commit").GetProperty("sha").GetString();
            var buildTime = root.GetProperty("build").GetProperty("time").GetString();

            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(commitMessage) ||
                string.IsNullOrWhiteSpace(commitSha) || string.IsNullOrWhiteSpace(buildTime))
            {
                throw GlqException.Usage($"{FileName} is missing required fields.");
            }

            return new ProductionVersion(version, commitMessage, commitSha, buildTime);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw GlqException.Usage($"{FileName} is not valid: {exception.Message}");
        }
    }

    internal string ToDisplay() => string.Join(
        Environment.NewLine,
        $"version: {Version}",
        $"commit-text: {CommitMessage}",
        $"commit-sha: {CommitSha}",
        $"build-time: {BuildTime}",
        string.Empty,
        "To update: dotnet tool update -g grdev.glq-cli");
}
