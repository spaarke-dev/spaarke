using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Authentication.Azure;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory implementation for creating Microsoft Graph clients.
/// Authentication modes:
///   * App-only (background jobs, SpeAdminGraphService):
///       - Production / Azure-hosted: <c>DefaultAzureCredential</c> when <c>Graph:ManagedIdentity:Enabled = true</c>
///         (chains EnvironmentCredential → WorkloadIdentityCredential → ManagedIdentityCredential →
///         VisualStudioCredential → AzureCliCredential, so devs running locally via <c>az login</c>
///         authenticate without code changes). This authenticates as the managed identity's OWN principal.
///       - Otherwise: the BFF app registration's app-only token, with the credential chosen by ordered
///         selection (<see cref="ConfidentialClientTokenCredential"/>). Was an inline
///         <c>ClientSecretCredential</c> until auth-v4 task 022.
///   * On-Behalf-Of (per-request, user context): an <see cref="IConfidentialClientApplication"/> obtained
///     from <see cref="OrderedCredentialClientProvider"/> per exchange.
///
/// <para><b>Correction (auth-v4, 2026-08).</b> This comment used to assert "OBO cannot be done with
/// managed identity". That is the false premise three prior audits concluded the client secret could
/// never be removed from — and it conflates two different things. A raw managed-identity token indeed
/// cannot perform an OBO exchange. But a managed identity CAN mint a federated client assertion which
/// the app registration presents as its confidential credential, and the OBO exchange then proceeds
/// normally. That was proven on the wire against this tenant at task 002, with the user's <c>upn</c>
/// preserved so Dataverse row-level authorization still evaluates as the user. Do not re-derive the old
/// claim from any stale doc — ADR-028 Amendment A4 is canonical.</para>
///
/// Updated for Task 4.1: Uses IHttpClientFactory for centralized resilience via GraphHttpMessageHandler.
/// Updated for Phase 4: Caches OBO tokens in Redis, reducing Azure AD load by 97% (ADR-009).
/// Updated for Task 041 (Phase C): App-only Graph auth migrated to DefaultAzureCredential (managed identity)
/// when <c>Graph:ManagedIdentity:Enabled = true</c>. Eliminates a secret rotation surface for app-only Graph.
/// </summary>
public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly GraphTokenCache _tokenCache;
    private readonly bool _managedIdentityEnabled;
    private readonly string? _managedIdentityClientId;

    /// <summary>Directory (tenant) id both flows authenticate against.</summary>
    private readonly string _tenantId;

    /// <summary>
    /// The BFF app registration — resolved from <c>API_APP_ID</c> ONLY.
    ///
    /// <para><b>Task 022 removed the <c>AZURE_CLIENT_ID ?? API_APP_ID</c> fallback that used to sit
    /// here</b>, and that is a fix rather than a tidy-up. <c>AZURE_CLIENT_ID</c> is ambiguous by
    /// convention: the Azure SDK reads it as a <i>managed identity's</i> clientId, and on
    /// <c>spaarke-bff-dev</c> it is set to the UAMI's — while this field is used as the <i>app
    /// registration's</i>. Task 023 found and guarded that (it was inert only because
    /// <c>Graph:ManagedIdentity:Enabled=true</c> made the consumer dead code) and left the fix to a task
    /// that owned the app-only branch. This is that task: with the fallback gone,
    /// <c>AZURE_CLIENT_ID</c> has no consumer anywhere in <c>src/</c> and the trap is removed rather
    /// than guarded. See notes/decisions/023-identity-conflation.md §3.</para>
    /// </summary>
    private readonly string _apiAppId;

    /// <summary>
    /// Ordered credential provider (task 021), concrete per ADR-010. Supplies BOTH the OBO confidential
    /// client and — via <see cref="ConfidentialClientTokenCredential"/> — the app-only credential that
    /// used to be an inline <c>ClientSecretCredential</c>.
    /// </summary>
    private readonly OrderedCredentialClientProvider? _confidentialClients;

    private readonly Lazy<GraphServiceClient> _appOnlyClient;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<GraphClientFactory> logger,
        GraphTokenCache tokenCache,
        IConfiguration configuration,
        OrderedCredentialClientProvider? confidentialClients = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _confidentialClients = confidentialClients;

        // Task 041: Managed Identity flag for app-only Graph auth.
        // Sensible default: false (preserves the legacy local-dev path for environments that haven't
        // opted in). Production deployments set Graph__ManagedIdentity__Enabled=true and
        // Graph__ManagedIdentity__ClientId (UAMI) if not using system-assigned MI.
        _managedIdentityEnabled = bool.TryParse(
            configuration["Graph:ManagedIdentity:Enabled"], out var miEnabled) && miEnabled;
        _managedIdentityClientId = configuration["Graph:ManagedIdentity:ClientId"];

        _tenantId = configuration["TENANT_ID"] ??
            throw new InvalidOperationException("TENANT_ID not configured");
        _apiAppId = configuration["API_APP_ID"] ??
            throw new InvalidOperationException("API_APP_ID not configured");

        _logger.LogInformation("Configuring Graph auth with API_APP_ID length: {AppIdLength}", _apiAppId.Length);
        _logger.LogInformation("Using TENANT_ID length: {TenantIdLength}", _tenantId.Length);
        _logger.LogInformation(
            "Graph app-only auth mode: {Mode} (Graph:ManagedIdentity:Enabled={Enabled}, UAMI clientId set: {HasUami})",
            _managedIdentityEnabled ? "ManagedIdentity (DefaultAzureCredential)" : "OrderedCredentialProvider",
            _managedIdentityEnabled,
            !string.IsNullOrWhiteSpace(_managedIdentityClientId));

        // No confidential client is built here any more (task 022, FR-B3). The provider's contract is
        // async because selection PROVES a credential before binding it, and a constructor cannot
        // await; the client is fetched per OBO exchange, where the provider's cache makes it a lookup.
        //
        // This also removes a quiet hazard: the old constructor called Build() even when NO credential
        // was configured, producing a client that looked healthy and failed only at the first OBO
        // exchange — for every user at once.

        // PPI-014: Cache app-only GraphServiceClient as a singleton.
        // The credential and auth provider are stateless and thread-safe,
        // so they can be created once and reused across all app-only calls.
        // OBO (per-user) clients remain per-request.
        _appOnlyClient = new Lazy<GraphServiceClient>(CreateAppOnlyClient);
    }

    /// <summary>
    /// Creates Graph client for app-only operations (platform/admin tasks).
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// Task 4.1: Now uses named HttpClient with GraphHttpMessageHandler for centralized resilience.
    /// Task 041 (Phase C): When <c>Graph:ManagedIdentity:Enabled = true</c>, uses
    /// <see cref="DefaultAzureCredential"/> (App Service managed identity in Azure; chains through
    /// EnvironmentCredential / WorkloadIdentityCredential / VisualStudioCredential / AzureCliCredential
    /// for local dev so <c>az login</c> works without code changes). Falls back to
    /// <see cref="ClientSecretCredential"/> when the flag is false (legacy local-dev mode).
    /// </summary>
    private GraphServiceClient CreateAppOnlyClient()
    {
        TokenCredential credential;

        if (_managedIdentityEnabled)
        {
            // Task 041: DefaultAzureCredential — App Service managed identity in Azure.
            // Chains through dev credentials for local development (az login, VS, env vars).
            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(_managedIdentityClientId))
            {
                // User-Assigned Managed Identity: pin the credential to this UAMI client id.
                credentialOptions.ManagedIdentityClientId = _managedIdentityClientId;
                _logger.LogInformation(
                    "Creating app-only Graph client with DefaultAzureCredential (UAMI clientId: {Length} chars)",
                    _managedIdentityClientId.Length);
            }
            else
            {
                _logger.LogInformation(
                    "Creating app-only Graph client with DefaultAzureCredential (system-assigned MI or dev credential chain)");
            }

            // customer-provisioning-orchestration-r1 §4D tenant-isolation invariant I5 / FR-32
            // (task 065): pin the credential to a specific tenant so it does not silently
            // resolve to the MI-host's default tenant. Today this is the Spaarke tenant
            // (single-tenant BFF) and the resolved tenant is unchanged; the assignment is a
            // forcing-function requirement so a future multi-tenant switch is safe from
            // implicit-tenant credential-context bugs. `_tenantId` is read from
            // AZURE_TENANT_ID / TENANT_ID configuration in the ctor above (line 53).
            if (!string.IsNullOrWhiteSpace(_tenantId))
            {
                credentialOptions.TenantId = _tenantId;
            }

            credential = new DefaultAzureCredential(credentialOptions);
        }
        else
        {
            // Task 022: the app registration's own app-only credential, chosen by ordered selection
            // (MI-FIC → certificate → transitional secret) instead of an inline ClientSecretCredential.
            // The IDENTITY is unchanged — this still authenticates as the BFF app registration, which
            // is what the previous ClientSecretCredential did; only the proof of it moved.
            if (_confidentialClients is null)
            {
                throw new InvalidOperationException(
                    "App-only Graph auth requires an OrderedCredentialClientProvider when " +
                    "Graph:ManagedIdentity:Enabled is not true. Inside the BFF it is registered by " +
                    "AuthorizationModule.AddCredentialSelection. To use the managed identity's own " +
                    "principal instead, set Graph:ManagedIdentity:Enabled=true (recommended for " +
                    "Azure-hosted environments).");
            }

            credential = new ConfidentialClientTokenCredential(_confidentialClients, _tenantId, _apiAppId);
            _logger.LogDebug("Creating app-only Graph client with the ordered credential provider (ADR-028 A4)");
        }

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        // Get HttpClient with GraphHttpMessageHandler (retry, circuit breaker, timeout)
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        _logger.LogInformation(
            "Created app-only Graph client with centralized resilience handler (auth mode: {Mode})",
            _managedIdentityEnabled ? "ManagedIdentity" : "ClientSecret");

        // Use beta endpoint for SharePoint Embedded support
        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow with Redis token caching.
    /// For user context operations where SPE must enforce user permissions.
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// Task 4.1: Now uses named HttpClient with GraphHttpMessageHandler for centralized resilience.
    /// Phase 4: Caches OBO tokens (55-min TTL) to reduce Azure AD load by 97%.
    /// </summary>
    /// <param name="baseUrl">
    /// Graph base address for the resulting client. Defaults to v1.0. Threading it through here
    /// rather than duplicating the exchange means the beta variant reuses the SAME on-behalf-of call
    /// AND the same Redis token cache — the token is version-agnostic, so a cache hit acquired for a
    /// v1.0 call serves a beta call and vice versa.
    /// </param>
    private async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(
        string userAccessToken,
        string baseUrl = GraphV1BaseUrl)
    {
        // Log configuration for debugging OBO issues
        _logger.LogInformation("OBO Token Exchange - CCA configured with ClientId from API_APP_ID");
        _logger.LogDebug("Token present: {HasToken}, Token length: {TokenLength}",
            !string.IsNullOrEmpty(userAccessToken),
            userAccessToken?.Length ?? 0);

        // Decode and log token claims for debugging
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(userAccessToken);
            _logger.LogInformation("Token Claims - aud: {Aud}, iss: {Iss}, appid: {AppId}, ver: {Ver}",
                jwtToken.Audiences.FirstOrDefault(),
                jwtToken.Issuer,
                jwtToken.Claims.FirstOrDefault(c => c.Type == "appid")?.Value,
                jwtToken.Claims.FirstOrDefault(c => c.Type == "ver")?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode token for logging");
        }

        // ============================================================================
        // PHASE 4: Token Caching (ADR-009: Redis-First Caching)
        // ============================================================================
        // Check cache first to avoid expensive OBO exchange (~200ms)
        // userAccessToken is guaranteed non-null by method signature and caller validation
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken!);
        var cachedGraphToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedGraphToken != null)
        {
            // Cache HIT - use cached token (~5ms vs ~200ms for OBO)
            _logger.LogInformation("Using cached Graph token (cache hit)");
            return CreateGraphClientFromToken(cachedGraphToken, baseUrl);
        }

        // Cache MISS - perform OBO exchange
        _logger.LogDebug("Cache miss, performing OBO token exchange");

        try
        {
            // OBO Flow: Use .default scope per Microsoft OAuth 2.0 OBO documentation
            // The .default scope requests ALL permissions that have been granted to the API
            // via admin consent in Azure AD. This includes:
            // - Sites.FullControl.All
            // - Files.ReadWrite.All
            // - FileStorageContainer.Selected (SharePoint Embedded)
            // Per OAUTH-OBO-IMPLEMENTATION.md: Using individual scopes causes AADSTS70011 errors
            //
            // The confidential client is fetched per exchange (task 022). The provider owns the ONE
            // process-wide client cache, so this is a dictionary lookup on the hot path — and asking it
            // every time is what lets the credential recover to a higher-priority one after a transient
            // failure. A client held in a field would pin every OBO exchange in the process to whatever
            // credential won at startup.
            if (_confidentialClients is null)
            {
                throw new InvalidOperationException(
                    "OBO requires an OrderedCredentialClientProvider. Inside the BFF it is registered " +
                    "by AuthorizationModule.AddCredentialSelection.");
            }

            var cca = await _confidentialClients
                .GetClientAsync(_tenantId, _apiAppId)
                .ConfigureAwait(false);

            var result = await cca.AcquireTokenOnBehalfOf(
                new[] { "https://graph.microsoft.com/.default" },
                new UserAssertion(userAccessToken)
            ).ExecuteAsync();

            _logger.LogInformation("OBO token exchange successful");
            _logger.LogInformation("OBO token scopes: {Scopes}", string.Join(", ", result.Scopes));

            // Cache the token for 55 minutes (5-minute buffer before 60-minute expiration)
            await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

            return CreateGraphClientFromToken(result.AccessToken, baseUrl);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL UI required exception. ErrorCode: {ErrorCode}, Claims: {Claims}",
                ex.ErrorCode, ex.Claims);
            throw;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL service exception. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}, CorrelationId: {CorrelationId}",
                ex.ErrorCode, ex.StatusCode, ex.CorrelationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBO failed - unexpected exception: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow for user context operations.
    /// Extracts user token from Authorization header and exchanges it for Graph API token.
    /// </summary>
    /// <param name="ctx">HttpContext containing Authorization header with user's bearer token</param>
    /// <param name="ct">Cancellation token (currently unused, reserved for future async cancellation)</param>
    /// <returns>GraphServiceClient authenticated with user's delegated permissions</returns>
    /// <exception cref="UnauthorizedAccessException">Missing or invalid Authorization header</exception>
    /// <exception cref="Microsoft.Identity.Client.MsalServiceException">OBO token exchange failed</exception>
    /// <remarks>
    /// This method wraps CreateOnBehalfOfClientAsync with automatic token extraction.
    /// OBO tokens are cached in Redis for 55 minutes to reduce Azure AD load by 97%.
    /// </remarks>
    public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
    {
        // Extract bearer token from Authorization header (throws UnauthorizedAccessException if invalid)
        var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

        _logger.LogDebug("ForUserAsync called | TraceId: {TraceId}", ctx.TraceIdentifier);

        // Delegate to existing OBO implementation (handles caching, token exchange, etc.)
        return await CreateOnBehalfOfClientAsync(userAccessToken);
    }

    /// <summary>
    /// Creates Graph client using app-only authentication (Managed Identity or Client Secret).
    /// </summary>
    /// <returns>GraphServiceClient authenticated with application permissions</returns>
    /// <remarks>
    /// This method wraps CreateAppOnlyClient with a clearer name.
    /// Use for platform/admin operations (container creation, background jobs).
    /// </remarks>
    /// <remarks>
    /// PPI-014: Returns a cached singleton GraphServiceClient for app-only operations.
    /// The credential and auth provider are created once via Lazy&lt;T&gt; and reused,
    /// eliminating per-call allocation of the TokenCredential and AzureIdentityAuthenticationProvider.
    /// Task 041: The cached credential is either <see cref="DefaultAzureCredential"/> (managed identity)
    /// or <see cref="ClientSecretCredential"/> depending on <c>Graph:ManagedIdentity:Enabled</c>.
    /// OBO (per-user) clients remain per-request since they depend on user tokens.
    /// </remarks>
    public GraphServiceClient ForApp()
    {
        _logger.LogDebug("ForApp called - returning cached app-only client");

        return _appOnlyClient.Value;
    }

    /// <summary>
    /// Creates a GraphServiceClient from an access token (cached or freshly acquired).
    /// Helper method to reduce duplication between cache hit and cache miss paths.
    /// </summary>
    /// <param name="accessToken">Graph API access token (from cache or OBO exchange)</param>
    /// <returns>Configured GraphServiceClient with resilience handlers</returns>
    private GraphServiceClient CreateGraphClientFromToken(string accessToken) =>
        CreateGraphClientFromToken(accessToken, GraphV1BaseUrl);

    /// <summary>Graph v1.0 base address — the default for every delegated call.</summary>
    internal const string GraphV1BaseUrl = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Graph beta base address, for the narrow set of SPE surfaces v1.0 does not expose.
    /// </summary>
    internal const string GraphBetaBaseUrl = "https://graph.microsoft.com/beta";

    /// <summary>
    /// Creates a delegated (on-behalf-of) Graph client pointed at the <b>beta</b> endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Use this only where v1.0 genuinely cannot serve the request.</b> Today that is exactly one
    /// surface: <c>fileStorageContainerType/{id}/permissions</c> — container-type owners.
    /// </para>
    /// <para>
    /// <b>Why it has to exist</b> (task 027, measured 2026-08-24). Two facts cross:
    /// container types reject application permissions outright (<b>403</b> app-only on both versions,
    /// task 010/020), so they can only be read or written <i>delegated</i>; and the
    /// <c>permissions</c> navigation property exists on <b>beta only</b> — absent from the v1.0 CSDL,
    /// and a live v1.0 call returns
    /// <c>400 "Resource not found for the segment 'permissions'"</c> while the identical beta call
    /// returns 403 (i.e. the route exists and only auth stops it). Delegated-v1.0 and app-only-beta
    /// both existed; delegated-beta, the only combination that can serve this, did not.
    /// </para>
    /// <para>
    /// <b>This is not an auth change.</b> It reuses the SAME on-behalf-of exchange, the same cached
    /// token, and the same <c>https://graph.microsoft.com/.default</c> scope — which is
    /// version-agnostic, so one token addresses both endpoints. Only the base address differs. No new
    /// credential, no new <c>.WithClientSecret</c> site, and therefore no ADR-028 A4/E-3 surface.
    /// </para>
    /// <para>
    /// It does reintroduce a deliberate version split on container types (list/get/create/settings on
    /// v1.0, owners on beta), mirroring the documented precedent task 020 set for containers. The
    /// split is narrow and is stated at each call site rather than left for a reader to discover.
    /// </para>
    /// </remarks>
    public async Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default)
    {
        var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

        _logger.LogDebug(
            "ForUserBetaAsync called (beta endpoint — SPE container-type owners) | TraceId: {TraceId}",
            ctx.TraceIdentifier);

        return await CreateOnBehalfOfClientAsync(userAccessToken, GraphBetaBaseUrl).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a GraphServiceClient from an access token (cached or freshly acquired).
    /// Helper method to reduce duplication between cache hit and cache miss paths.
    /// </summary>
    /// <param name="accessToken">Graph API access token (from cache or OBO exchange)</param>
    /// <param name="baseUrl">Graph base address — v1.0 by default; beta only where v1.0 cannot serve.</param>
    /// <returns>Configured GraphServiceClient with resilience handlers</returns>
    private GraphServiceClient CreateGraphClientFromToken(string accessToken, string baseUrl)
    {
        // Create a simple token credential that returns the provided access token
        var tokenCredential = new SimpleTokenCredential(accessToken);

        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            // Version-agnostic: one .default token addresses both v1.0 and beta.
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        // Get HttpClient with GraphHttpMessageHandler (retry, circuit breaker, timeout)
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        _logger.LogDebug("Created Graph client with centralized resilience handler | Base: {BaseUrl}", baseUrl);

        // v1.0 by default — SharePoint Embedded containers work with the v1.0 drives endpoint, and a
        // container ID is usable directly as a Drive ID in
        // /v1.0/drives/{containerId}/root:/path:/content
        return new GraphServiceClient(httpClient, authProvider, baseUrl);
    }
}
