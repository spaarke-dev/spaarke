using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Authentication.Azure;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Cross-tenant, <b>app-only</b> Microsoft Graph client factory targeting the Entra External ID
/// (CIAM) tenant. Consumed by the admin-initiated external-user provisioner (task 025) to create
/// and manage CIAM local accounts (Graph <c>POST /users</c>) cross-tenant.
///
/// <para><b>Why this exists (ADR-010 justification).</b> The single-tenant
/// <see cref="GraphClientFactory"/> binds to the workforce authority and cannot acquire a token for
/// the separate CIAM tenant. Graph application permissions (<c>User.ReadWrite.All</c>) are consented
/// to an app registration that lives IN the CIAM tenant (task 002), so a per-authority confidential
/// client is required. <see cref="GraphClientFactory"/> cannot be extended to hold a second authority.</para>
///
/// <para><b>Auth model (ADR-028 Amendment A1 — broker-only).</b> This is app-only client-credentials
/// against the CIAM authority. The external user's token is NEVER exchanged here — there is no OBO on
/// the external path. The credential is a certificate whose private key lives in Key Vault (task 002:
/// <c>ciam-graph-provisioner-cert</c>); it is materialized in-process at runtime and never embedded in
/// source or config (NFR-06).</para>
///
/// <para><b>Pattern.</b> Modeled on
/// <see cref="Services.SpeAdmin.SpeAdminTokenProvider"/>'s per-authority MSAL confidential-client
/// construction (<c>GetOrCreateMsalApp</c>: <c>WithAuthority</c> + Key-Vault-sourced credential, cached),
/// per the ADR-007 constraint to reuse the established mechanism rather than fork a new one.</para>
///
/// ADR-010: registered as a concrete singleton (no interface) — matches
/// <see cref="Services.SpeAdmin.SpeAdminTokenProvider"/>; do not over-abstract.
/// </summary>
public sealed class CiamGraphClientFactory
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    private readonly SecretClient _secretClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CiamGraphClientFactory> _logger;

    private readonly string _authority;        // {Instance}/{TenantId} — https://{sub}.ciamlogin.com/{tid}
    private readonly string _clientId;         // CIAM provisioner app registration (task 002)
    private readonly string _certificateName;  // Key Vault certificate name (task 002)

    // Lazily-built, cached per-authority confidential client. MSAL caches app-only tokens internally,
    // so a single instance is reused across provisioning calls. The lock guards the one-time build
    // (a single Key Vault cert fetch) against concurrent first-callers.
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConfidentialClientApplication? _app;

    public CiamGraphClientFactory(
        SecretClient secretClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CiamGraphClientFactory> logger)
    {
        _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        // The CIAM authority (Instance + TenantId) is shared with the token-validation "Ciam" scheme
        // (task 020). The provisioner app + certificate are a distinct credential nested under
        // Ciam:GraphProvisioner (task 002 — different app registration than the token-validation audience).
        var ciam = configuration.GetSection("Ciam");
        var instance = ciam["Instance"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Ciam:Instance is not configured.");
        var tenantId = ciam["TenantId"]
            ?? throw new InvalidOperationException("Ciam:TenantId is not configured.");

        var provisioner = ciam.GetSection("GraphProvisioner");
        _clientId = provisioner["ClientId"]
            ?? throw new InvalidOperationException("Ciam:GraphProvisioner:ClientId is not configured.");
        _certificateName = provisioner["CertificateName"]
            ?? throw new InvalidOperationException("Ciam:GraphProvisioner:CertificateName is not configured.");

        _authority = $"{instance}/{tenantId}";
    }

    /// <summary>
    /// Acquires an app-only Graph access token for the CIAM tenant via client-credentials against the
    /// CIAM authority. No user token is involved (broker-only invariant). MSAL caches the token
    /// internally (~1 hour) and refreshes it near expiry.
    /// </summary>
    public async Task<string> AcquireAppOnlyTokenAsync(CancellationToken ct = default)
    {
        var app = await GetOrCreateAppAsync(ct).ConfigureAwait(false);
        var result = await app.AcquireTokenForClient(GraphDefaultScope).ExecuteAsync(ct).ConfigureAwait(false);
        return result.AccessToken;
    }

    /// <summary>
    /// Returns a <see cref="GraphServiceClient"/> authenticated app-only against the CIAM tenant,
    /// suitable for user-management operations (<c>POST /users</c>). Uses the shared resilient
    /// "GraphApiClient" HttpClient (retry / circuit-breaker) exactly like <see cref="GraphClientFactory"/>.
    /// </summary>
    public async Task<GraphServiceClient> CreateClientForAppAsync(CancellationToken ct = default)
    {
        var token = await AcquireAppOnlyTokenAsync(ct).ConfigureAwait(false);

        var authProvider = new AzureIdentityAuthenticationProvider(
            new SimpleTokenCredential(token),
            scopes: GraphDefaultScope);

        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        // v1.0 — CIAM local-account create (POST /users, Example 3) is a v1.0 Graph operation.
        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/v1.0");
    }

    private async Task<IConfidentialClientApplication> GetOrCreateAppAsync(CancellationToken ct)
    {
        if (_app is not null)
        {
            return _app;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_app is not null)
            {
                return _app;
            }

            var certificate = await LoadCertificateAsync(ct).ConfigureAwait(false);

            _app = ConfidentialClientApplicationBuilder
                .Create(_clientId)
                .WithCertificate(certificate)
                .WithAuthority(_authority)
                .Build();

            _logger.LogInformation(
                "Built CIAM app-only MSAL confidential client. Authority: {Authority}; certificate: {CertName} (thumbprint {Thumbprint}).",
                _authority, _certificateName, certificate.Thumbprint);

            return _app;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Loads the provisioner certificate (with private key) from Key Vault. A Key Vault certificate's
    /// full PKCS#12 (cert + exportable private key) is exposed as a <i>secret</i> of the same name;
    /// retrieving it via <see cref="SecretClient"/> yields base64-encoded PFX bytes. The private key is
    /// materialized only in-process (<see cref="X509KeyStorageFlags.EphemeralKeySet"/> — never written to
    /// disk) and is never exported to source or config.
    /// </summary>
    private async Task<X509Certificate2> LoadCertificateAsync(CancellationToken ct)
    {
        try
        {
            KeyVaultSecret secret = await _secretClient
                .GetSecretAsync(_certificateName, version: null, ct)
                .ConfigureAwait(false);

            var pfxBytes = Convert.FromBase64String(secret.Value);

            // EphemeralKeySet: keep the private key in memory only (no on-disk key persistence).
            // Target runtime is Linux App Service; ephemeral RSA signing is supported there and on
            // modern Windows for local dev.
            return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Key Vault secret '{_certificateName}' is not a base64-encoded PFX. " +
                "Verify it is a Key Vault certificate (not a plain secret) with an exportable private key.", ex);
        }
    }
}
