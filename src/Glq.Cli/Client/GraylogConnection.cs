using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Glq.Cli.Auth;
using Glq.Cli.Configuration;

using Microsoft.Extensions.DependencyInjection;

namespace Glq.Cli.Client;

/// <summary>
/// An open connection to one profile's Graylog: the typed client, built through
/// <see cref="IHttpClientFactory"/> with the profile's URL, token, TLS settings and timeout.
/// </summary>
internal sealed class GraylogConnection : IDisposable
{
    internal const string ProductionNotice = "querying PRODUCTION Graylog — results may contain personal data";

    private readonly ServiceProvider _services;

    private GraylogConnection(ServiceProvider services, GraylogClient client, ProfileCache cache)
    {
        _services = services;
        Client = client;
        Cache = cache;
    }

    internal GraylogClient Client { get; }

    internal ProfileCache Cache { get; }

    /// <summary>
    /// Opens the connection: prints the production notice and any TLS warning, resolves the
    /// token and builds the client.
    /// </summary>
    internal static GraylogConnection Open(CliContext context, ActiveProfile active, bool insecure, bool verbose)
    {
        var profile = active.Profile;
        if (profile.IsProduction)
        {
            context.WarnOnce(ProductionNotice);
        }

        var verifyTls = profile.EffectiveVerifyTls && !insecure;
        if (!verifyTls)
        {
            context.WarnOnce($"warning: TLS certificate verification is OFF for profile '{active.Name}' - the connection can be intercepted.");
        }

        var token = TokenResolver.Resolve(context, active.Name, profile);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:token"));

        var services = new ServiceCollection();
        var builder = services
            .AddHttpClient<GraylogClient>(http =>
            {
                http.BaseAddress = new Uri(profile.Url!.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(profile.EffectiveTimeoutSeconds);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("glq", "1"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => context.PrimaryHandler?.Invoke() ?? CreateHandler(profile, verifyTls));

        if (verbose)
        {
            builder.AddHttpMessageHandler(() => new TraceHandler(context.Err));
        }

        var provider = services.BuildServiceProvider();
        return new GraylogConnection(
            provider,
            provider.GetRequiredService<GraylogClient>(),
            new ProfileCache(context.Paths, active.Name, profile.Url!, context.Time));
    }

    public void Dispose() => _services.Dispose();

    private static SocketsHttpHandler CreateHandler(Profile profile, bool verifyTls)
    {
        var handler = new SocketsHttpHandler();
        if (!verifyTls)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }
        else if (!string.IsNullOrWhiteSpace(profile.CaBundle))
        {
            var authorities = LoadCaBundle(profile.CaBundle);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                ValidateAgainst(authorities, certificate, errors);
        }

        return handler;
    }

    private static X509Certificate2Collection LoadCaBundle(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.StartsWith('~')
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..]
            : path));
        try
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(full);
            return authorities.Count > 0
                ? authorities
                : throw GlqException.Usage($"ca_bundle '{full}' holds no certificates.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            throw GlqException.Usage($"ca_bundle '{full}' could not be read: {exception.Message}");
        }
    }

    // Trusts the system store as usual, and otherwise a chain that ends in the bundle.
    // A host-name mismatch is never forgiven.
    private static bool ValidateAgainst(X509Certificate2Collection authorities, X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var leaf = new X509Certificate2(certificate);
        return chain.Build(leaf);
    }

    /// <summary>
    /// Writes each request and response line to stderr for <c>--verbose</c>. Header values
    /// are printed except <c>Authorization</c>, which is always redacted.
    /// </summary>
    internal sealed class TraceHandler(TextWriter error) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            error.WriteLine($"> {request.Method} {request.RequestUri}");
            foreach (var header in request.Headers)
            {
                var value = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    ? "[redacted]"
                    : string.Join(", ", header.Value);
                error.WriteLine($"> {header.Key}: {value}");
            }

            var stopwatch = Stopwatch.StartNew();
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase} ({stopwatch.ElapsedMilliseconds} ms)");
            return response;
        }
    }
}
