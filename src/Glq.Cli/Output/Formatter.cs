using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glq.Cli.Output;

/// <summary>The output formats of specs/output.md.</summary>
internal enum OutputFormat
{
    Table,
    Json,
    Jsonl,
    Csv,
}

/// <summary>Writes rows of named fields as table, JSON, JSON Lines or CSV.</summary>
internal static class Formatter
{
    private const int MaxTableCell = 80;

    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions Indented = new(Compact) { WriteIndented = true };

    internal static Option<OutputFormat?> CreateOption() => new("--output", "-o")
    {
        Description = "Output format: table, json, jsonl or csv (default: table on a terminal, jsonl otherwise).",
    };

    /// <summary>The requested format, or the default for where stdout goes.</summary>
    internal static OutputFormat Resolve(OutputFormat? requested, bool stdoutIsTerminal) =>
        requested ?? (stdoutIsTerminal ? OutputFormat.Table : OutputFormat.Jsonl);

    internal static void Write(TextWriter writer, OutputFormat format, IReadOnlyList<string> columns, IReadOnlyList<JsonObject> rows)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(new JsonArray([.. rows.Select(row => Project(row, columns))]).ToJsonString(Indented));
                break;
            case OutputFormat.Jsonl:
                foreach (var row in rows)
                {
                    writer.WriteLine(Project(row, columns).ToJsonString(Compact));
                }

                break;
            case OutputFormat.Csv:
                WriteCsv(writer, columns, rows);
                break;
            default:
                WriteTable(writer, columns, rows);
                break;
        }
    }

    /// <summary>A value as display text: strings bare, everything else as JSON, null as empty.</summary>
    internal static string Text(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(Compact),
    };

    private static JsonObject Project(JsonObject row, IReadOnlyList<string> columns)
    {
        var projected = new JsonObject();
        foreach (var column in columns)
        {
            projected[column] = row.TryGetPropertyValue(column, out var value) ? value?.DeepClone() : null;
        }

        return projected;
    }

    private static void WriteTable(TextWriter writer, IReadOnlyList<string> columns, IReadOnlyList<JsonObject> rows)
    {
        var cells = rows
            .Select(row => columns.Select(column => Cell(row.TryGetPropertyValue(column, out var value) ? value : null)).ToArray())
            .ToList();

        var widths = columns
            .Select((column, index) => Math.Max(column.Length, cells.Count == 0 ? 0 : cells.Max(row => row[index].Length)))
            .ToArray();

        writer.WriteLine(Line(columns.ToArray(), widths));
        writer.WriteLine(Line(widths.Select(width => new string('-', width)).ToArray(), widths));
        foreach (var row in cells)
        {
            writer.WriteLine(Line(row, widths));
        }
    }

    private static string Line(string[] values, int[] widths)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            // The last column is not padded, so lines carry no trailing spaces.
            builder.Append(i == values.Length - 1 ? values[i] : values[i].PadRight(widths[i] + 2));
        }

        return builder.ToString();
    }

    private static string Cell(JsonNode? node)
    {
        var text = Text(node).ReplaceLineEndings(" ");
        return text.Length <= MaxTableCell ? text : string.Concat(text.AsSpan(0, MaxTableCell - 1), "…");
    }

    private static void WriteCsv(TextWriter writer, IReadOnlyList<string> columns, IReadOnlyList<JsonObject> rows)
    {
        writer.WriteLine(string.Join(",", columns.Select(Quote)));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", columns.Select(column =>
                Quote(Text(row.TryGetPropertyValue(column, out var value) ? value : null)))));
        }
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value != value.Trim()
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
