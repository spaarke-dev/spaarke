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

    private readonly IConfidentialClientApplication _cca;
    private readonly ITenantCache _cache;
    private readonly ILogger<AgentTokenService> _logger;
    private readonly AgentTokenOptions _options;

    public AgentTokenService(
        ITenantCache cache,
        IOptions<AgentTokenOptions> options,
        ILogger<AgentTokenService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Build the MSAL confidential client for OBO exchanges.
        // The BFF app registration is the "middle tier" that exchanges the agent token.
        _cca = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_options.TenantId}")
            .Build();

        _logger.LogInformation(
            "[AGENT-TOKEN] Initialized: TenantId length={TenantLen}, ClientId length={ClientLen}, AgentAppId length={AgentLen}",
            _options.TenantId.Length, _options.ClientId.Length, _options.AgentAppId.Length);
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
            var result = await _cca.AcquireTokenOnBehalfOf(
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
            var result = await _cca.AcquireTokenOnBehalfOf(
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
