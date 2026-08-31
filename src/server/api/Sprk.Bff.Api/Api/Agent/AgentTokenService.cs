using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Handles SSO/OBO token exchange for M365 Copilot agent authentication.
///
/// Flow:
///   M365 Copilot → [Agent Bearer Token] → BFF AgentTokenService
///     → [OBO Exchange] → Graph API token (Files.Read.All, FileStorageContainer.Selected)
///     → [OBO Exchange] → Dataverse API token
///
/// Tokens are cached per-user with tenant-scoped keys in Redis (ADR-009, ADR-014).
/// Errors return ProblemDetails-compatible results (ADR-019).
///
/// ADR-010: Concrete type, no unnecessary interface.
/// ADR-008: Used by agent endpoint filters, not global middleware.
/// </summary>
public sealed class AgentTokenService
{
    // Resource identifiers for ITenantCache (FR-05). Tokens are user-bound OAuth tokens,
    // not authorization decisions — caching is permitted by ADR-009.
    private const string AgentGraphTokenResource = "agent-graph-token";
    private const string AgentDataverseTokenResource = "agent-dataverse-token";
    private const int CacheVersion = 1;

    /// <summary>
    /// Ordered credential provider (auth-v4 task 021), injected as the CONCRETE type per ADR-010 —
    /// only <c>DataverseAccessDataSource</c>, in the base layer, needs the interface.
    ///
    /// <para><b>Replaces this type's own static confidential-client cache</b> (task 022). That cache
    /// was one of three keyed on the same <c>(tenant|client|fingerprint)</c>, so one process could hold
    /// three confidential clients — and three OBO token caches — for the SAME identity. ADR-028 A4
    /// forbids exactly that per-call-site duplication; task 011 booked it as a time-boxed exception
    /// that <b>expires here</b>. The provider now owns the one cache, and its per-key build counter is
    /// where the sharing seam tests moved to.</para>
    ///
    /// <para>Null only under direct construction outside the BFF container, in which case every
    /// exchange fails closed with a configuration error rather than silently acquiring nothing.</para>
    /// </summary>
    private readonly OrderedCredentialClientProvider? _confidentialClients;

    private readonly ITenantCache _cache;
    private readonly ILogger<AgentTokenService> _logger;
    private readonly AgentTokenOptions _options;

    public AgentTokenService(
        ITenantCache cache,
        IOptions<AgentTokenOptions> options,
        ILogger<AgentTokenService> logger,
        OrderedCredentialClientProvider? confidentialClients = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _confidentialClients = confidentialClients;

        // No confidential client is built here any more. The provider's contract is async (selection
        // PROVES a credential before binding it) and a constructor cannot await; the client is fetched
        // at the moment of each exchange, where the provider's cache makes it a dictionary lookup.
        //
        // AgentToken:ClientSecret is deliberately NOT read. See AcquireTokenAsync's remarks and
        // IdentityConfigurationValidator rule 5 for the reconciliation this required.
        _logger.LogInformation(
            "[AGENT-TOKEN] Initialized: TenantId length={TenantLen}, ClientId length={ClientLen}, AgentAppId length={AgentLen}",
            _options.TenantId.Length, _options.ClientId.Length, _options.AgentAppId.Length);
    }

    /// <summary>
    /// Resolves the BFF's confidential client for the agent OBO exchange.
    ///
    /// <para><b>The <c>AgentToken:ClientSecret</c> reconciliation (task 022).</b> This service used to
    /// present <c>AgentToken:ClientSecret</c> specifically, while the provider resolves the transitional
    /// secret as <c>AzureAd:ClientSecret</c> → <c>API_CLIENT_SECRET</c> → <c>AZURE_CLIENT_SECRET</c>.
    /// Folding one into the other silently could have changed which secret the agent path presents, so
    /// it was decided explicitly rather than defaulted: verified on <c>spaarke-bff-dev</c> on
    /// 2026-08-21 that <c>AgentToken__ClientSecret</c>, <c>API_CLIENT_SECRET</c>,
    /// <c>AzureAd__ClientSecret</c> and <c>Dataverse__ClientSecret</c> all hold the SAME value —
    /// <c>BFF-API-ClientSecret</c> — and that <c>AgentToken__ClientId</c> is the BFF app registration.
    /// <c>Reconcile-DemoEnvironment.ps1:76</c> maps the demo environment the same way.
    ///
    /// <para>Because "verified today" is not "true forever", divergence is not left to be discovered as
    /// an opaque <c>AADSTS7000215</c> on the agent endpoint: <c>IdentityConfigurationValidator</c>
    /// rule 5 compares the two at startup by fingerprint and reports a mismatch at error level.</para>
    /// </summary>
    private Task<IConfidentialClientApplication> GetConfidentialClientAsync(CancellationToken ct)
    {
        if (_confidentialClients is null)
        {
            throw new InvalidOperationException(
                "AgentTokenService requires an OrderedCredentialClientProvider for the OBO exchange. "
                + "Inside the BFF it is registered by AuthorizationModule.AddCredentialSelection.");
        }

        return _confidentialClients.GetClientAsync(_options.TenantId, _options.ClientId, ct);
    }

    /// <summary>
    /// Exchanges the incoming M365 agent bearer token for a Graph API token via OBO.
    /// Uses .default scope to get all admin-consented Graph permissions
    /// (Files.Read.All, FileStorageContainer.Selected, etc.).
    /// </summary>
    /// <param name="httpContext">The current HTTP context containing the agent bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Graph API access token, or null if the exchange failed.</returns>
    public async Task<AgentTokenResult> AcquireGraphTokenAsync(HttpContext httpContext, CancellationToken ct = default)
    {
        var userToken = ExtractAgentToken(httpContext);
        if (userToken is null)
        {
            return AgentTokenResult.Failure("Missing or invalid Authorization header on agent request");
        }

        var tenantId = ExtractTenantId(httpContext);
        var tokenHashId = HashUserToken(userToken);

        // Check cache first (ADR-009: Redis-first caching)
        var cached = await GetCachedTokenAsync(tenantId, AgentGraphTokenResource, tokenHashId);
        if (cached is not null)
        {
            _logger.LogDebug("[AGENT-TOKEN] Graph token cache HIT");
            return AgentTokenResult.Success(cached);
        }

        // Cache miss — perform OBO exchange
        _logger.LogDebug("[AGENT-TOKEN] Graph token cache MISS, performing OBO exchange");

        try
        {
            var cca = await GetConfidentialClientAsync(ct).ConfigureAwait(false);
            var result = await cca.AcquireTokenOnBehalfOf(
                _options.GraphScopes,
                new UserAssertion(userToken)
            ).ExecuteAsync(ct);

            _logger.LogInformation(
                "[AGENT-TOKEN] Graph OBO exchange successful. Scopes: {Scopes}",
                string.Join(", ", result.Scopes));

            // Cache with configured TTL
            await SetCachedTokenAsync(tenantId, AgentGraphTokenResource, tokenHashId, result.AccessToken);

            return AgentTokenResult.Success(result.AccessToken);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex,
                "[AGENT-TOKEN] Graph OBO failed — consent required. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return AgentTokenResult.Failure(
                "Token exchange failed: user consent required for Graph API permissions. " +
                "An admin must grant consent for the required scopes.");
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex,
                "[AGENT-TOKEN] Graph OBO failed — MSAL service error. ErrorCode={ErrorCode}, StatusCode={StatusCode}",
                ex.ErrorCode, ex.StatusCode);
            return AgentTokenResult.Failure($"Token exchange failed: {ex.ErrorCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AGENT-TOKEN] Graph OBO failed — unexpected error");
            return AgentTokenResult.Failure("An unexpected error occurred during Graph token exchange");
        }
    }

    /// <summary>
    /// Exchanges the incoming M365 agent bearer token for a Dataverse API token via OBO.
    /// Scope: {DataverseEnvironmentUrl}/.default
    /// </summary>
    /// <param name="httpContext">The current HTTP context containing the agent bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Dataverse API access token, or null if the exchange failed.</returns>
    public async Task<AgentTokenResult> AcquireDataverseTokenAsync(HttpContext httpContext, CancellationToken ct = default)
    {
        var userToken = ExtractAgentToken(httpContext);
        if (userToken is null)
        {
            return AgentTokenResult.Failure("Missing or invalid Authorization header on agent request");
        }

        var tenantId = ExtractTenantId(httpContext);
        var tokenHashId = HashUserToken(userToken);

        // Check cache first (ADR-009: Redis-first caching)
        var cached = await GetCachedTokenAsync(tenantId, AgentDataverseTokenResource, tokenHashId);
        if (cached is not null)
        {
            _logger.LogDebug("[AGENT-TOKEN] Dataverse token cache HIT");
            return AgentTokenResult.Success(cached);
        }

        // Cache miss — perform OBO exchange
        _logger.LogDebug("[AGENT-TOKEN] Dataverse token cache MISS, performing OBO exchange");

        var dataverseScope = $"{_options.DataverseEnvironmentUrl}/.default";

        try
        {
            var cca = await GetConfidentialClientAsync(ct).ConfigureAwait(false);
            var result = await cca.AcquireTokenOnBehalfOf(
                new[] { dataverseScope },
                new UserAssertion(userToken)
            ).ExecuteAsync(ct);

            _logger.LogInformation(
                "[AGENT-TOKEN] Dataverse OBO exchange successful. Scopes: {Scopes}",
                string.Join(", ", result.Scopes));

            // Cache with configured TTL
            await SetCachedTokenAsync(tenantId, AgentDataverseTokenResource, tokenHashId, result.AccessToken);

            return AgentTokenResult.Success(result.AccessToken);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex,
                "[AGENT-TOKEN] Dataverse OBO failed — consent required. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return AgentTokenResult.Failure(
                "Token exchange failed: user consent required for Dataverse permissions. " +
                "An admin must grant consent for the required scopes.");
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex,
                "[AGENT-TOKEN] Dataverse OBO failed — MSAL service error. ErrorCode={ErrorCode}, StatusCode={StatusCode}",
                ex.ErrorCode, ex.StatusCode);
            return AgentTokenResult.Failure($"Token exchange failed: {ex.ErrorCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AGENT-TOKEN] Dataverse OBO failed — unexpected error");
            return AgentTokenResult.Failure("An unexpected error occurred during Dataverse token exchange");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Cache Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hashes the user token to form a stable, fixed-length cache id component (no PII).
    /// FR-05: the tenantId is supplied to the cache wrapper separately and becomes part of
    /// the on-wire key automatically.
    /// </summary>
    private static string HashUserToken(string userToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Retrieves a cached token. Returns null on cache miss or error.
    /// Cache errors are logged but do not break the flow (graceful degradation).
    /// </summary>
    private async Task<string?> GetCachedTokenAsync(string tenantId, string resource, string tokenHashId)
    {
        try
        {
            return await _cache.GetAsync<string>(tenantId, resource, tokenHashId, CacheVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AGENT-TOKEN] Cache read error for resource {Resource}, falling through to OBO",
                resource);
            return null;
        }
    }

    /// <summary>
    /// Stores a token in the cache with the configured TTL.
    /// Cache errors are logged but do not break the flow.
    /// </summary>
    private async Task SetCachedTokenAsync(string tenantId, string resource, string tokenHashId, string token)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, resource, tokenHashId, CacheVersion,
                token,
                TimeSpan.FromMinutes(_options.CacheTtlMinutes));

            _logger.LogDebug("[AGENT-TOKEN] Cached token with TTL={Ttl}min", _options.CacheTtlMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AGENT-TOKEN] Cache write error — token will not be cached");
            // Don't throw — caching is an optimization, not a requirement
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Token Extraction Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the bearer token from the Authorization header.
    /// Returns null instead of throwing to support ProblemDetails error flow.
    /// </summary>
    private string? ExtractAgentToken(HttpContext httpContext)
    {
        try
        {
            return TokenHelper.ExtractBearerToken(httpContext);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("[AGENT-TOKEN] Token extraction failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extracts the tenant ID from the authenticated user claims.
    /// Falls back to the configured tenant ID if not present in claims.
    /// </summary>
    private string ExtractTenantId(HttpContext httpContext)
    {
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogDebug("[AGENT-TOKEN] No tenant claim in token, using configured TenantId");
            return _options.TenantId;
        }

        return tenantId;
    }
}

/// <summary>
/// Result of an OBO token exchange attempt.
/// Encapsulates success/failure to support ProblemDetails error responses (ADR-019).
/// </summary>
public sealed record AgentTokenResult
{
    /// <summary>Whether the token exchange succeeded.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>The access token. Non-null when IsSuccess is true.</summary>
    public string? Token { get; private init; }

    /// <summary>Error description. Non-null when IsSuccess is false.</summary>
    public string? ErrorDetail { get; private init; }

    public static AgentTokenResult Success(string token) => new()
    {
        IsSuccess = true,
        Token = token
    };

    public static AgentTokenResult Failure(string errorDetail) => new()
    {
        IsSuccess = false,
        ErrorDetail = errorDetail
    };
}
