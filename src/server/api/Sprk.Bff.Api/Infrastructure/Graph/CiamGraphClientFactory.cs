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
/// the former <c>SpeAdminTokenProvider</c>'s per-authority MSAL confidential-client construction
/// (<c>GetOrCreateMsalApp</c>: <c>WithAuthority</c> + Key-Vault-sourced credential, cached — that type was
/// removed 2026-10-04, when SPE Admin stopped authenticating as owning apps), per the ADR-007 constraint
/// to reuse the established mechanism rather than fork a new one.</para>
///
/// ADR-010: registered as a concrete singleton (no interface); do not over-abstract.
/// </summary>
public sealed class CiamGraphClientFactory
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    private readonly SecretClient _secretClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CiamGraphClientFactory> _logger;

    // Task 185 slice F (ADR-010 path C): the Ciam:* values are read at construction but VALIDATED at first use
    // (EnsureConfigured), so a host without CIAM can construct this factory — and the services that depend on it — and only
    // a CIAM call there throws InvalidOperationException. Configured hosts behave as before.
    private readonly string? _instance;        // https://{sub}.ciamlogin.com
    private readonly string? _tenantId;
    private readonly string? _clientId;         // CIAM provisioner app registration (task 002)
    private readonly string? _certificateName;  // Key Vault certificate name (task 002)

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
        _instance = ciam["Instance"]?.TrimEnd('/');
        _tenantId = ciam["TenantId"];

        var provisioner = ciam.GetSection("GraphProvisioner");
        _clientId = provisioner["ClientId"];
        _certificateName = provisioner["CertificateName"];
    }

    /// <summary>
    /// Task 185 slice F: whether every <c>Ciam:*</c> value this factory needs is configured. Pure; never throws.
    /// </summary>
    // T240d (provisioning) makes Ciam:GraphProvisioner:CertificateName optional when MI-FIC is configured; do not tighten this.
    internal bool IsConfigured =>
        _instance is not null && _tenantId is not null && _clientId is not null && _certificateName is not null;

    /// <summary>
    /// Task 185 slice F: throws <see cref="InvalidOperationException"/> naming the first <c>Ciam:*</c> value that is not
    /// configured — the validation the constructor used to run, moved to first use with the same messages.
    /// </summary>
    internal void EnsureConfigured()
    {
        _ = _instance ?? throw new InvalidOperationException("Ciam:Instance is not configured.");
        _ = _tenantId ?? throw new InvalidOperationException("Ciam:TenantId is not configured.");
        _ = _clientId ?? throw new InvalidOperationException("Ciam:GraphProvisioner:ClientId is not configured.");
        _ = _certificateName ?? throw new InvalidOperationException("Ciam:GraphProvisioner:CertificateName is not configured.");
    }

    /// <summary>{Instance}/{TenantId} — https://{sub}.ciamlogin.com/{tid}. Only after <see cref="EnsureConfigured"/>.</summary>
    private string Authority => $"{_instance}/{_tenantId}";

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
        EnsureConfigured();

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
                .Create(_clientId!)
                .WithCertificate(certificate)
                .WithAuthority(Authority)
                .Build();

            _logger.LogInformation(
                "Built CIAM app-only MSAL confidential client. Authority: {Authority}; certificate: {CertName} (thumbprint {Thumbprint}).",
                Authority, _certificateName, certificate.Thumbprint);

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
    ///
    /// <para><b>Body extracted to <see cref="Auth.KeyVaultCertificateLoader"/> at auth-v4 task 021</b>,
    /// so the ordered credential selector's certificate branch reuses this exact load rather than
    /// forking a second one. Behaviour here is unchanged — the shared helper is this method's body,
    /// moved verbatim, including <c>EphemeralKeySet</c> and the <see cref="FormatException"/>
    /// diagnostic.</para>
    /// </summary>
    private Task<X509Certificate2> LoadCertificateAsync(CancellationToken ct)
        => Auth.KeyVaultCertificateLoader.LoadAsync(_secretClient, _certificateName!, ct);
}
