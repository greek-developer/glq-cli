using Glq.Cli.Queries;

namespace Glq.Cli.UnitTests.Queries;

public class QueryTemplateTests
{
    private static readonly Dictionary<string, string> NoValues = [];

    [Fact]
    public void Placeholders_AreListedOnce_InOrder()
    {
        Assert.Equal(["service", "level"], QueryTemplate.Placeholders("source:{{service}} AND level:{{ level }} OR app:{{service}}"));
    }

    [Fact]
    public void Render_FillsPlainValuesBare()
    {
        var query = QueryTemplate.Render("source:{{service}} AND level:<={{level}}", new Dictionary<string, string> { ["service"] = "payments-api", ["level"] = "3" }, null);

        Assert.Equal("source:payments-api AND level:<=3", query);
    }

    [Fact]
    public void Render_UsesDeclaredDefaults()
    {
        var declared = new Dictionary<string, QueryParam> { ["level"] = new() { Default = "4" } };

        Assert.Equal("level:<=4", QueryTemplate.Render("level:<={{level}}", NoValues, declared));
    }

    [Fact]
    public void Render_ListsEveryMissingParameter()
    {
        var exception = Assert.Throws<GlqException>(() => QueryTemplate.Render("a:{{one}} AND b:{{two}} AND c:{{three}}", new Dictionary<string, string> { ["two"] = "x" }, null));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("one, three", exception.Message);
        Assert.Contains("--param one=<value>", exception.Message);
    }

    [Fact]
    public void Render_RejectsAnUnknownParameter()
    {
        var exception = Assert.Throws<GlqException>(() => QueryTemplate.Render("a:{{one}}", new Dictionary<string, string> { ["one"] = "x", ["tow"] = "y" }, null));

        Assert.Contains("tow", exception.Message);
    }

    [Theory]
    [InlineData("payments", "payments")]
    [InlineData("api-gateway", "api-gateway")]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("OR", "\"OR\"")]
    [InlineData("-negated", "\"-negated\"")]
    [InlineData("wild*", "\"wild*\"")]
    public void Escape_QuotesAnythingButAPlainTerm(string value, string expected)
    {
        Assert.Equal(expected, QueryTemplate.Escape(value));
    }

    [Theory]
    [InlineData("x OR source:*")]
    [InlineData("x) OR (source:*")]
    [InlineData("x\" OR source:* OR \"")]
    [InlineData("x\\\" OR source:*")]
    [InlineData("x AND NOT level:1")]
    [InlineData("[* TO *]")]
    public void Render_KeepsInjectionAttempts_InsideOnePhrase(string attack)
    {
        var query = QueryTemplate.Render("service:{{service}} AND level:3", new Dictionary<string, string> { ["service"] = attack }, null);

        // The value becomes one phrase: a quote, then the value with only \ and " escaped, then a quote.
        var phrase = query["service:".Length..query.LastIndexOf(" AND level:3", StringComparison.Ordinal)];
        Assert.StartsWith("\"", phrase);
        Assert.EndsWith("\"", phrase);
        Assert.Equal(attack, Unescape(phrase[1..^1]));
        Assert.EndsWith(" AND level:3", query);
    }

    [Fact]
    public void Render_InsideAuthorsQuotes_EscapesWithoutAddingQuotes()
    {
        var query = QueryTemplate.Render("message:\"{{text}}\"", new Dictionary<string, string> { ["text"] = "say \"hi\" \\ bye" }, null);

        Assert.Equal("message:\"say \\\"hi\\\" \\\\ bye\"", query);
    }

    // Reads a phrase the way Lucene does: a backslash escapes the next character, and an
    // unescaped quote would end the phrase early.
    private static string Unescape(string phrase)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < phrase.Length; i++)
        {
            if (phrase[i] == '\\')
            {
                result.Append(phrase[++i]);
                continue;
            }

            Assert.NotEqual('"', phrase[i]);
            result.Append(phrase[i]);
        }

        return result.ToString();
    }
}
