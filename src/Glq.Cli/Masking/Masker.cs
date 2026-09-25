using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Glq.Cli.Configuration;

namespace Glq.Cli.Masking;

/// <summary>
/// Masks personal data in results - see specs/privacy.md. Emails, phone numbers and named
/// fields are masked unless masking is turned off; card-like numbers (Luhn-valid, 13-19
/// digits) are masked always. A mask is a deterministic pseudonym from a salted hash, so the
/// same value masks the same way on every row.
/// </summary>
internal sealed partial class Masker
{
    private readonly byte[] _salt;

    private readonly Regex[] _fieldPatterns;

    internal Masker(string salt, IEnumerable<string> maskFields, bool enabled)
    {
        _salt = Encoding.UTF8.GetBytes(salt);
        _fieldPatterns = [.. maskFields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Glob)];
        Enabled = enabled;
    }

    /// <summary><c>false</c> under <c>--no-mask</c>: only card numbers are masked then.</summary>
    internal bool Enabled { get; }

    /// <summary>
    /// The masker for a run, from the top-level and profile <c>mask_fields</c>. Generates and
    /// saves <c>mask_salt</c> on first use.
    /// </summary>
    internal static Masker Create(ConfigStore store, GlqConfig config, Profile profile, bool enabled)
    {
        if (string.IsNullOrEmpty(config.MaskSalt))
        {
            config.MaskSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            store.Save(config);
        }

        return new Masker(config.MaskSalt, config.EffectiveMaskFields.Concat(profile.EffectiveMaskFields), enabled);
    }

    internal bool IsMaskedField(string field) => Enabled && _fieldPatterns.Any(pattern => pattern.IsMatch(field));

    /// <summary>Masks every value of a result row in place.</summary>
    internal void MaskRow(JsonObject row)
    {
        foreach (var field in row.Select(pair => pair.Key).ToList())
        {
            // Cloned first: a node still attached to the row cannot be assigned back to it.
            row[field] = MaskValue(field, row[field]?.DeepClone());
        }
    }

    /// <summary>Masks one field value: the whole value when the field is masked, its text otherwise.</summary>
    internal JsonNode? MaskValue(string? field, JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (field is not null && IsMaskedField(field) && value is JsonValue)
        {
            var text = value.GetValueKind() == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
            return JsonValue.Create(Pseudonym(PrefixFor(field), text));
        }

        return value switch
        {
            JsonObject child => MaskTree(child),
            JsonArray array => MaskTree(array),
            JsonValue scalar when scalar.GetValueKind() == System.Text.Json.JsonValueKind.String =>
                JsonValue.Create(MaskText(scalar.GetValue<string>())),
            JsonValue scalar when scalar.GetValueKind() == System.Text.Json.JsonValueKind.Number =>
                MaskNumber(scalar),
            _ => value,
        };
    }

    /// <summary>
    /// Masks a whole JSON document - for <c>--raw</c>. Object properties are treated as
    /// fields; a views-API response is masked cell by cell through its schema.
    /// </summary>
    internal JsonNode? MaskTree(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj when obj["schema"] is JsonArray schema && obj["datarows"] is JsonArray rows:
                var fields = schema.Select(column => column?["field"]?.GetValue<string>()).ToList();
                foreach (var row in rows.OfType<JsonArray>())
                {
                    for (var i = 0; i < row.Count; i++)
                    {
                        row[i] = MaskValue(i < fields.Count ? fields[i] : null, row[i]?.DeepClone());
                    }
                }

                foreach (var key in obj.Select(pair => pair.Key).Where(key => key != "datarows").ToList())
                {
                    obj[key] = MaskValue(key, obj[key]?.DeepClone());
                }

                return obj;
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    obj[key] = MaskValue(key, obj[key]?.DeepClone());
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = MaskValue(null, array[i]?.DeepClone());
                }

                return array;
            default:
                return MaskValue(null, node);
        }
    }

    /// <summary>Masks card numbers always, and emails and phone numbers when masking is on.</summary>
    internal string MaskText(string text)
    {
        text = CardPattern().Replace(text, match =>
            IsCardNumber(match.Value) ? Pseudonym("card", Digits(match.Value)) : match.Value);

        if (!Enabled)
        {
            return text;
        }

        text = EmailPattern().Replace(text, match => Pseudonym("email", match.Value.ToLowerInvariant()));
        return PhonePattern().Replace(text, match => Pseudonym("phone", Digits(match.Value)));
    }

    internal string Pseudonym(string prefix, string value)
    {
        var hash = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(value));
        return $"{prefix}_…{Convert.ToHexStringLower(hash.AsSpan(0, 4))}";
    }

    internal static bool IsCardNumber(string candidate)
    {
        var digits = Digits(candidate);
        return digits.Length is >= 13 and <= 19 && PassesLuhn(digits);
    }

    internal static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubled = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (doubled)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubled = !doubled;
        }

        return sum % 10 == 0;
    }

    private JsonNode MaskNumber(JsonValue number)
    {
        var text = number.ToJsonString();
        return text.All(char.IsAsciiDigit) && IsCardNumber(text) ? JsonValue.Create(Pseudonym("card", text)) : number;
    }

    private static string PrefixFor(string field)
    {
        var name = field.ToLowerInvariant();
        return name switch
        {
            _ when name.Contains("email", StringComparison.Ordinal) => "email",
            _ when name.Contains("phone", StringComparison.Ordinal) => "phone",
            _ when name.StartsWith("card", StringComparison.Ordinal) => "card",
            _ when name.StartsWith("customer", StringComparison.Ordinal) => "cust",
            _ when name.StartsWith("user", StringComparison.Ordinal) => "user",
            _ when name is "ip" || name.EndsWith("_ip", StringComparison.Ordinal) => "ip",
            _ => new string([.. name.Where(char.IsAsciiLetterOrDigit).Take(4)]) is { Length: > 0 } abbreviation ? abbreviation : "mask",
        };
    }

    private static string Digits(string value) => new([.. value.Where(char.IsAsciiDigit)]);

    private static Regex Glob(string pattern) => new(
        "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)")]
    private static partial Regex CardPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    // International (+30 210 1234567, +306912345678), bracketed area code ((210) 123-4567) or
    // three-plus groups of 3-4 digits (210 123 4567). Dates, IPs and versions do not match.
    [GeneratedRegex(@"(?<![\w+])(?:\+\d{1,3}[ -]?(?:\(\d{1,4}\)[ -]?)?\d{2,4}(?:[ -]?\d{2,4}){1,4}|\(\d{2,4}\)[ -]?\d{3,4}[ -]?\d{3,4}|\d{3,4}(?:[ -]\d{3,4}){2,3})(?!\w)")]
    private static partial Regex PhonePattern();
}
