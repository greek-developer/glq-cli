using System.Text.Json;
using System.Text.Json.Nodes;

using Glq.Cli.Output;

namespace Glq.Cli.UnitTests.Output;

public class FormatterTests
{
    private static readonly string[] Columns = ["timestamp", "source", "message"];

    private static List<JsonObject> Rows() =>
    [
        new() { ["timestamp"] = "2026-09-24T08:00:00Z", ["source"] = "api", ["message"] = "hello, \"world\"", ["extra"] = "hidden" },
        new() { ["timestamp"] = "2026-09-24T08:00:01Z", ["source"] = "worker", ["message"] = "line one\nline two" },
    ];

    private static string Write(OutputFormat format, List<JsonObject>? rows = null)
    {
        var writer = new StringWriter();
        Formatter.Write(writer, format, Columns, rows ?? Rows());
        return writer.ToString();
    }

    [Fact]
    public void Resolve_DefaultsByWhereStdoutGoes()
    {
        Assert.Equal(OutputFormat.Table, Formatter.Resolve(null, stdoutIsTerminal: true));
        Assert.Equal(OutputFormat.Jsonl, Formatter.Resolve(null, stdoutIsTerminal: false));
        Assert.Equal(OutputFormat.Csv, Formatter.Resolve(OutputFormat.Csv, stdoutIsTerminal: true));
        Assert.Equal(OutputFormat.Json, Formatter.Resolve(null, json: true, stdoutIsTerminal: true));
        Assert.Equal(OutputFormat.Json, Formatter.Resolve(OutputFormat.Json, json: true, stdoutIsTerminal: false));
        Assert.Throws<GlqException>(() => Formatter.Resolve(OutputFormat.Table, json: true, stdoutIsTerminal: false));
    }

    [Fact]
    public void Json_IsOneArray_OfTheSelectedFieldsOnly()
    {
        using var document = JsonDocument.Parse(Write(OutputFormat.Json));

        Assert.Equal(2, document.RootElement.GetArrayLength());
        var first = document.RootElement[0];
        Assert.Equal("api", first.GetProperty("source").GetString());
        Assert.False(first.TryGetProperty("extra", out _));
    }

    [Fact]
    public void Jsonl_IsOneObjectPerLine()
    {
        var lines = Write(OutputFormat.Jsonl).TrimEnd().Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
        Assert.Equal("line one\nline two", JsonNode.Parse(lines[1])!["message"]!.GetValue<string>());
    }

    [Fact]
    public void Csv_HasAHeader_AndQuotesWhereNeeded()
    {
        var csv = Write(OutputFormat.Csv);

        Assert.StartsWith("timestamp,source,message" + Environment.NewLine, csv);
        Assert.Contains("2026-09-24T08:00:00Z,api,\"hello, \"\"world\"\"\"", csv);
        Assert.Contains("\"line one\nline two\"", csv);
    }

    [Fact]
    public void Table_AlignsColumns_AndKeepsEachRowOnOneLine()
    {
        var lines = Write(OutputFormat.Table).TrimEnd().Split(Environment.NewLine);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("timestamp", lines[0]);
        Assert.Equal(lines[0].IndexOf("source", StringComparison.Ordinal), lines[2].IndexOf("api", StringComparison.Ordinal));
        Assert.Contains("line one line two", lines[3]);
    }

    [Fact]
    public void EmptyResults_AreValidInEveryFormat()
    {
        Assert.Equal("[]", Write(OutputFormat.Json, []).Trim());
        Assert.Equal(string.Empty, Write(OutputFormat.Jsonl, []));
        Assert.Equal("timestamp,source,message", Write(OutputFormat.Csv, []).Trim());
    }

    [Fact]
    public void NonAsciiText_IsWrittenAsIs()
    {
        var rows = new List<JsonObject> { new() { ["message"] = "Καλημέρα — ok" } };
        var writer = new StringWriter();

        Formatter.Write(writer, OutputFormat.Jsonl, ["message"], rows);

        Assert.Contains("Καλημέρα — ok", writer.ToString());
    }
}
