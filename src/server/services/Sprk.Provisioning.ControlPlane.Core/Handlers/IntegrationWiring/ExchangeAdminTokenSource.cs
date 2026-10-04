// -----------------------------------------------------------------------------
// ExchangeAdminTokenSource.cs
//
// Task 251 (owner D24): what the sidecar needs to connect to Exchange Online — an access token and
// the organization to name. The Worker signs in as the 'Spaarke Exchange Admin' app registration
// through the federated identity credential whose subject is the Worker's managed identity
// (ADR-028 A4 -- the same mechanism task 248 uses for the SPE owning app). The sidecar receives
// both per request and runs Connect-ExchangeOnline -AccessToken -Organization; it holds no
// credential of its own.
//
// ORGANIZATION = the tenant's initial domain (contoso.onmicrosoft.com), read once per tenant from
// Graph GET /organization and cached. Microsoft documents only that value for app-only sign-in. A
// tenant GUID connects and reads, but every directory write then fails with "doesn't have write
// permission to target DC" (live, 2026-10-04). The lookup signs in like GraphRestAppRoleGranter
// (DefaultAzureCredential with the run's explicit tenant, §4D I5) and needs Organization.Read.All
// or Directory.Read.All for the Worker managed identity in that tenant.
//
// Justification (CLAUDE.md §11): Existing -- SpeConfidentialClientGraphFactory builds the SPE
// owning-app credential and a GRAPH client; this needs an Exchange Online token for a different app.
// Extension -- both use WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential,
// the single place the Worker mints a federated assertion; only the app and scope differ.
// Cost of doing nothing -- the sidecar has no way to authenticate to Exchange, so H14a and H13 T4
// can never run.
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Exchange Online access tokens for the <c>Spaarke Exchange Admin</c> app, obtained through the
/// Worker managed identity's federated credential, plus the tenant's initial domain to connect with.
/// Singleton; no network I/O until a token is requested.
/// </summary>
public sealed class ExchangeAdminTokenSource
{
    /// <summary>The app-only Exchange Online scope Connect-ExchangeOnline -AccessToken needs.</summary>
    public static readonly string[] ExchangeOnlineScope = { "https://outlook.office365.com/.default" };

    /// <summary>Graph scope for the initial-domain lookup.</summary>
    public static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    /// <summary>The lookup: the tenant's verified domains, of which exactly one is the initial domain.</summary>
    public const string OrganizationLookupUrl = "https://graph.microsoft.com/v1.0/organization?$select=verifiedDomains";

    private readonly Func<string, string, TokenCredential> _createCredential;
    private readonly Func<string, CancellationToken, Task<string?>> _lookupInitialDomain;
    private readonly IntegrationWiringOptions _options;
    private readonly ConcurrentDictionary<(string TenantId, string AppId), TokenCredential> _credentials = new();
    private readonly ConcurrentDictionary<string, string> _initialDomains = new();

    /// <summary>Production constructor — the token comes from the Worker UAMI (MI-FIC), the domain from Graph.</summary>
    public ExchangeAdminTokenSource(
        WorkerDataverseCredentialFactory credentials,
        IHttpClientFactory httpClientFactory,
        IOptions<IntegrationWiringOptions> options)
        : this(ResolveFrom(credentials), GraphInitialDomainLookup(httpClientFactory), options)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="createCredential"/> replaces the MI-FIC credential and
    /// <paramref name="lookupInitialDomain"/> the Graph lookup (null/empty = no initial domain found).
    /// </summary>
    internal ExchangeAdminTokenSource(
        Func<string, string, TokenCredential> createCredential,
        Func<string, CancellationToken, Task<string?>> lookupInitialDomain,
        IOptions<IntegrationWiringOptions> options)
    {
        ArgumentNullException.ThrowIfNull(createCredential);
        ArgumentNullException.ThrowIfNull(lookupInitialDomain);
        ArgumentNullException.ThrowIfNull(options);
        _createCredential = createCredential;
        _lookupInitialDomain = lookupInitialDomain;
        _options = options.Value;
    }

    /// <summary>
    /// An Exchange Online token for <c>Spaarke Exchange Admin</c> in <paramref name="tenantId"/>, and the
    /// tenant's initial domain to connect with. Never throws for configuration, sign-in or lookup
    /// failures — returns <see cref="ExchangeTokenResult.Failure"/> with a diagnostic naming what to check.
    /// </summary>
    public async Task<ExchangeTokenResult> GetTokenAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var appId = _options.ExchangeAdminAppId?.Trim() ?? string.Empty;
        if (appId.Length == 0)
        {
            return new ExchangeTokenResult.Failure(
                $"'{IntegrationWiringModule.ConfigSection}:ExchangeAdminAppId' is not configured — the Worker cannot sign in to " +
                "Exchange Online. Set it to the client id of the 'Spaarke Exchange Admin' app registration (platform-controlplane " +
                "parameter exchangeAdminAppId).");
        }

        var tenantKey = tenantId.Trim().ToLowerInvariant();
        var credential = _credentials.GetOrAdd((tenantKey, appId.ToLowerInvariant()), key => _createCredential(key.TenantId, key.AppId));
        string accessToken;
        try
        {
            accessToken = (await credential.GetTokenAsync(new TokenRequestContext(ExchangeOnlineScope), cancellationToken).ConfigureAwait(false)).Token;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationFailedException ex)
        {
            return new ExchangeTokenResult.Failure(
                $"Could not sign in to Exchange Online as app {appId} in tenant {tenantId}: {ex.Message} — check that the app's " +
                "federated identity credential names this Worker's managed identity (issuer = the managed identity's tenant, " +
                "audience api://AzureADTokenExchange) and that the app holds Exchange.ManageAsApp. Transient sign-in faults " +
                "surface the same way; a retry may succeed.");
        }
        catch (Exception ex)
        {
            // Callers promise not to throw for sign-in problems (code review S6).
            return new ExchangeTokenResult.Failure(
                $"Unexpected {ex.GetType().Name} signing in to Exchange Online as app {appId} in tenant {tenantId}: {ex.Message}");
        }

        if (!_initialDomains.TryGetValue(tenantKey, out var organization))
        {
            string? found;
            try
            {
                found = await _lookupInitialDomain(tenantKey, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ExchangeTokenResult.Failure(InitialDomainDiagnostic(tenantId, $"{ex.GetType().Name}: {ex.Message}"));
            }
            if (string.IsNullOrWhiteSpace(found))
            {
                return new ExchangeTokenResult.Failure(InitialDomainDiagnostic(tenantId, "Graph listed no initial (isInitial) verified domain"));
            }
            organization = _initialDomains.GetOrAdd(tenantKey, found.Trim());
        }

        return new ExchangeTokenResult.Success(accessToken, organization);
    }

    /// <summary>The initial domain from a Graph <c>GET /organization</c> response body, or null.</summary>
    internal static string? ParseInitialDomain(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var orgs) || orgs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var org in orgs.EnumerateArray())
        {
            if (!org.TryGetProperty("verifiedDomains", out var domains) || domains.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var d in domains.EnumerateArray())
            {
                if (d.TryGetProperty("isInitial", out var initial) && initial.ValueKind == JsonValueKind.True
                    && d.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    return name.GetString();
                }
            }
        }
        return null;
    }

    private static string InitialDomainDiagnostic(string tenantId, string cause)
        => $"Could not find the initial domain of tenant {tenantId} ({cause}). Exchange app-only writes need " +
           "-Organization <tenant>.onmicrosoft.com — with the tenant id Exchange connects but every write fails. The Worker " +
           "managed identity reads it from Graph GET /organization, which needs Organization.Read.All or Directory.Read.All in that tenant.";

    private static Func<string, string, TokenCredential> ResolveFrom(WorkerDataverseCredentialFactory credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.CreateManagedIdentityFederatedCredential;
    }

    private static Func<string, CancellationToken, Task<string?>> GraphInitialDomainLookup(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        return async (tenantId, cancellationToken) =>
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
            var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken).ConfigureAwait(false);
            using var client = httpClientFactory.CreateClient(nameof(ExchangeAdminTokenSource));
            using var request = new HttpRequestMessage(HttpMethod.Get, OrganizationLookupUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Graph GET /organization returned HTTP {(int)response.StatusCode}: {(body.Length <= 300 ? body : body[..300] + "...[truncated]")}");
            }
            return ParseInitialDomain(body);
        };
    }
}

/// <summary>Outcome of <see cref="ExchangeAdminTokenSource.GetTokenAsync"/>.</summary>
public abstract record ExchangeTokenResult
{
    private ExchangeTokenResult() { }

    /// <summary>The token (never log it) and the tenant's initial domain to pass as -Organization.</summary>
    public sealed record Success(string AccessToken, string Organization) : ExchangeTokenResult
    {
        /// <inheritdoc/>
        public override string ToString() => $"Success {{ AccessToken = ***, Organization = {Organization} }}";
    }

    /// <summary>No token; <paramref name="Diagnostic"/> says why.</summary>
    public sealed record Failure(string Diagnostic) : ExchangeTokenResult;
}
