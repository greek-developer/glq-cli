using System.Text;

namespace Glq.Cli.Output;

/// <summary>
/// Wraps stdout or stderr and replaces every registered secret with <c>***</c> before it is
/// written. The last line of defence behind "a token never reaches any output": whatever
/// path a token takes to a writer, it arrives redacted.
/// </summary>
internal sealed class RedactingTextWriter(TextWriter inner, SecretRegistry secrets) : TextWriter
{
    internal const string Mask = "***";

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value) => inner.Write(secrets.Redact(value));

    public override void Write(char[] buffer, int index, int count) =>
        inner.Write(secrets.Redact(new string(buffer, index, count)));

    public override void WriteLine(string? value) => inner.WriteLine(secrets.Redact(value));

    public override void WriteLine() => inner.WriteLine();

    public override void Flush() => inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Flush();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The secrets known to this process - every token resolved during the run.</summary>
internal sealed class SecretRegistry
{
    private readonly List<string> _secrets = [];

    internal void Add(string? secret)
    {
        // Very short values would redact ordinary text; no real token is this short.
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 8 && !_secrets.Contains(secret))
        {
            _secrets.Add(secret);
        }
    }

    internal string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        foreach (var secret in _secrets)
        {
            value = value.Replace(secret, RedactingTextWriter.Mask, StringComparison.Ordinal);
        }

        return value;
    }
}
