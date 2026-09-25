using System.Text.Json.Nodes;

using Glq.Cli.Configuration;
using Glq.Cli.Masking;

namespace Glq.Cli.UnitTests.Masking;

public class MaskerTests
{
    private static Masker Create(bool enabled = true) => new("test-salt", GlqConfig.DefaultMaskFields, enabled);

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("378282246310005")]
    [InlineData("6011111111111117")]
    public void CardNumbers_AreMasked_EvenWithMaskingOff(string card)
    {
        var masked = Create(enabled: false).MaskText($"paid with {card} today");

        Assert.DoesNotContain(card, masked);
        Assert.Matches(@"^paid with card_…[0-9a-f]{8} today$", masked);
    }

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("123456789012")]
    [InlineData("12345678901234567890123")]
    public void DigitRuns_ThatAreNotCards_AreLeftAlone(string digits)
    {
        Assert.Equal($"id {digits}", Create().MaskText($"id {digits}"));
    }

    [Fact]
    public void CardNumbers_InNumericFields_AreMasked()
    {
        var row = new JsonObject { ["amount"] = 42, ["pan"] = 4111111111111111 };

        Create(enabled: false).MaskRow(row);

        Assert.Equal(42, row["amount"]!.GetValue<int>());
        Assert.StartsWith("card_…", row["pan"]!.GetValue<string>());
    }

    [Fact]
    public void Luhn_AcceptsValidAndRejectsInvalidNumbers()
    {
        Assert.True(Masker.PassesLuhn("79927398713"));
        Assert.False(Masker.PassesLuhn("79927398710"));
    }

    [Theory]
    [InlineData("contact jane.doe@example.com now", "email_…")]
    [InlineData("call +30 210 1234567 now", "phone_…")]
    [InlineData("call +306912345678 now", "phone_…")]
    [InlineData("call (210) 123-4567 now", "phone_…")]
    [InlineData("call 210 123 4567 now", "phone_…")]
    public void EmailsAndPhones_AreMaskedByPattern(string text, string expectedPrefix)
    {
        var masked = Create().MaskText(text);

        Assert.Contains(expectedPrefix, masked);
        Assert.StartsWith(text.Split(' ')[0], masked);
        Assert.EndsWith(" now", masked);
    }

    [Theory]
    [InlineData("2026-09-24T08:00:00.000Z")]
    [InlineData("from 10.0.0.1 and 192.168.100.200")]
    [InlineData("version 6.1.2 released")]
    [InlineData("status 500 in 1234 ms")]
    [InlineData("id 5f9c1a2b3c4d5e6f7a8b9c0d")]
    public void OrdinaryText_IsNotMistakenForPersonalData(string text)
    {
        Assert.Equal(text, Create().MaskText(text));
    }

    [Fact]
    public void EmailsAndPhones_AreKept_WithMaskingOff()
    {
        const string text = "jane.doe@example.com +30 210 1234567";

        Assert.Equal(text, Create(enabled: false).MaskText(text));
    }

    [Fact]
    public void MaskFields_MaskTheWholeValue_WithGlobsAndAnyCase()
    {
        var row = new JsonObject
        {
            ["customer_id"] = "C-1001",
            ["Card_Holder"] = "Jane Doe",
            ["user_id"] = 77,
            ["message"] = "login ok",
        };

        Create().MaskRow(row);

        Assert.StartsWith("cust_…", row["customer_id"]!.GetValue<string>());
        Assert.StartsWith("card_…", row["Card_Holder"]!.GetValue<string>());
        Assert.StartsWith("user_…", row["user_id"]!.GetValue<string>());
        Assert.Equal("login ok", row["message"]!.GetValue<string>());
    }

    [Fact]
    public void Pseudonyms_AreDeterministic_PerSalt()
    {
        var first = Create().Pseudonym("cust", "C-1001");
        var again = Create().Pseudonym("cust", "C-1001");
        var other = Create().Pseudonym("cust", "C-1002");
        var otherSalt = new Masker("another-salt", [], enabled: true).Pseudonym("cust", "C-1001");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.NotEqual(first, otherSalt);
    }

    [Fact]
    public void MaskFields_AreOff_WithMaskingOff()
    {
        var row = new JsonObject { ["customer_id"] = "C-1001" };

        Create(enabled: false).MaskRow(row);

        Assert.Equal("C-1001", row["customer_id"]!.GetValue<string>());
    }

    [Fact]
    public void RawViewsResponses_AreMaskedThroughTheirSchema()
    {
        var raw = JsonNode.Parse("""
            {"schema":[{"field":"customer_id"},{"field":"message"}],
             "datarows":[["C-1001","card 4111111111111111 by jane@example.com"]]}
            """);

        var masked = Create().MaskTree(raw)!.ToJsonString();

        Assert.DoesNotContain("C-1001", masked);
        Assert.DoesNotContain("4111111111111111", masked);
        Assert.DoesNotContain("jane@example.com", masked);
    }

    [Fact]
    public void RawLegacyResponses_AreMaskedByPropertyName()
    {
        var raw = JsonNode.Parse("""{"messages":[{"message":{"customer_id":"C-1001","email":"jane@example.com","source":"api"}}]}""");

        var masked = Create().MaskTree(raw)!.ToJsonString();

        Assert.DoesNotContain("C-1001", masked);
        Assert.DoesNotContain("jane@example.com", masked);
        Assert.Contains("\"source\":\"api\"", masked);
    }

    [Fact]
    public void Create_GeneratesAndSavesTheSalt_OnFirstUse()
    {
        using var host = new TestHost();
        var config = new GlqConfig();

        Masker.Create(host.Store, config, new Profile(), enabled: true);

        Assert.False(string.IsNullOrEmpty(config.MaskSalt));
        Assert.Equal(config.MaskSalt, host.ReadConfig().MaskSalt);
    }
}
