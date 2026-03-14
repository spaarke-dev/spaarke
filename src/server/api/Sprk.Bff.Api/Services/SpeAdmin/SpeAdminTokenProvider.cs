using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Identity.Client;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Provides access tokens for SPE Admin Graph API operations, supporting both:
///
///   1. Single-app mode (Phase 1): App-only tokens via ClientSecretCredential.
///      Used when the BU config does not specify a separate owning app registration.
///
///   2. Multi-app mode (Phase 3): OBO (On-Behalf-Of) token exchange — the admin user's
///      incoming token is exchanged for a token scoped to the owning app registration.
///      Each owning app gets its own token cache entry keyed by (configId, sha256(userToken)).
///
/// ADR-010: Concrete type (no interface), registered as Singleton.
/// Auth constraint: OBO MUST use .default scope; tokens MUST be cached with 55-minute TTL;
///                  user tokens MUST be hashed (SHA256) before use as cache keys.
/// </summary>
public sealed class SpeAdminTokenProvider
{
    // -------------------------------------------------------------------------
    // Token cache entry — MSAL token + expiry
    // -------------------------------------------------------------------------

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly SecretClient _secretClient;
    private readonly ILogger<SpeAdminTokenProvider> _logger;

    /// <summary>
    /// Thread-safe per-app OBO token cache.
    /// Key: "{configId}:{sha256(userToken)}" — prevents cross-app token contamination.
    /// Per auth constraint: 55-minute TTL (5-minute buffer before token expiry).
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedToken> _oboTokenCache = new();

    /// <summary>
    /// Thread-safe per-app MSAL confidential client cache.
    /// Key: configId — one MSAL application instance per owning app registration.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, IConfidentialClientApplication> _msalClientCache = new();

    private static readonly TimeSpan OboTokenTtl = TimeSpan.FromMinutes(55);

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public SpeAdminTokenProvider(
        SecretClient secretClient,
        ILogger<SpeAdminTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(secretClient);
        ArgumentNullException.ThrowIfNull(logger);

        _secretClient = secretClient;
        _logger = logger;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Acquires an access token for the owning app registration in the given BU config
    /// using the OBO (On-Behalf-Of) flow.
    ///
    /// Flow:
    ///   1. Check OBO token cache — return cached token if not expired.
    ///   2. Fetch the owning app client secret from Key Vault.
    ///   3. Build/reuse MSAL ConfidentialClientApplication for the owning app.
    ///   4. Exchange the user's incoming token for an owning-app-scoped token via OBO.
    ///   5. Cache the resulting token with 55-minute TTL.
    ///
    /// Auth constraint: MUST use ".default" scope (not individual scopes) for OBO exchange.
    /// Auth constraint: MUST hash user token (SHA256) before using as cache key.
    /// Auth constraint: Cache TTL is 55 minutes (5-minute buffer before 1-hour token expiry).
    /// </summary>
    /// <param name="config">Resolved container type config with owning app credentials.</param>
    /// <param name="userAccessToken">
    /// The admin user's incoming Bearer token (from the BFF request Authorization header).
    /// Will be exchanged for an owning-app-scoped token via OBO.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An access token scoped to the owning app's Graph API permissions.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the config lacks owning app fields; callers should check <see cref="SpeAdminGraphService.ContainerTypeConfig.HasOwningApp"/> first.
    /// </exception>
    public async Task<string> AcquireOwningAppTokenAsync(
        SpeAdminGraphService.ContainerTypeConfig config,
        string userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAccessToken);

        if (!config.HasOwningApp)
        {
            throw new InvalidOperationException(
                $"Config '{config.ConfigId}' does not have owning app credentials. " +
                "Check HasOwningApp before calling AcquireOwningAppTokenAsync.");
        }

        // Build cache key: configId + SHA256(userToken)
        // Auth constraint: MUST hash user tokens; MUST NOT store plaintext tokens.
        var tokenHash = HashToken(userAccessToken);
        var cacheKey = $"{config.ConfigId}:{tokenHash}";

        // 1. Check OBO token cache
        if (_oboTokenCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug(
                "OBO token cache HIT for configId {ConfigId}. Expires: {ExpiresAt}",
                config.ConfigId, cached.ExpiresAt);
            return cached.AccessToken;
        }

        // 2. Fetch owning app client secret from Key Vault (at request time, not startup)
        _logger.LogInformation(
            "OBO token cache MISS for configId {ConfigId}. Fetching owning app secret '{SecretName}'.",
            config.ConfigId, config.OwningAppSecretName);

        var clientSecret = await FetchKeyVaultSecretAsync(config.OwningAppSecretName!, ct);

        // 3. Build/reuse MSAL ConfidentialClientApplication for the owning app
        var msalApp = GetOrCreateMsalApp(config, clientSecret);

        // 4. Exchange user token for owning-app token via OBO
        // Auth constraint: MUST use .default scope for OBO exchange (not individual scopes)
        var scopes = new[] { $"api://{config.OwningAppId}/.default" };

        AuthenticationResult result;
        try
        {
            result = await msalApp
                .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userAccessToken))
                .ExecuteAsync(ct);
        }
        catch (MsalException ex)
        {
            _logger.LogError(
                ex,
                "OBO token exchange failed for configId {ConfigId}, owningAppId {OwningAppId}. " +
                "Error: {MsalError}",
                config.ConfigId, config.OwningAppId, ex.ErrorCode);

            // Remove stale MSAL app from cache so next request rebuilds with fresh credentials
            _msalClientCache.TryRemove(config.ConfigId, out _);
            throw new InvalidOperationException(
                $"OBO token exchange failed for config '{config.ConfigId}'. " +
                $"MSAL error: {ex.ErrorCode}. Verify the owning app is configured for OBO flow.", ex);
        }

        // 5. Cache with 55-minute TTL (auth constraint: 5-minute buffer before expiry)
        var token = result.AccessToken;
        var expiry = DateTimeOffset.UtcNow.Add(OboTokenTtl);
        _oboTokenCache[cacheKey] = new CachedToken(token, expiry);

        _logger.LogInformation(
            "OBO token acquired and cached for configId {ConfigId}. TTL: {Ttl}",
            config.ConfigId, OboTokenTtl);

        return token;
    }

    /// <summary>
    /// Fetches the client secret for the owning app from Key Vault.
    /// Called at request time (not startup) to support secret rotation.
    /// </summary>
    public async Task<string> FetchOwningAppSecretAsync(
        SpeAdminGraphService.ContainerTypeConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.OwningAppSecretName))
        {
            throw new InvalidOperationException(
                $"Config '{config.ConfigId}' does not have an owning app secret name.");
        }

        return await FetchKeyVaultSecretAsync(config.OwningAppSecretName, ct);
    }

    /// <summary>
    /// Validates that Key Vault secrets are accessible for all provided configs with owning apps.
    /// Called during startup validation — warns on inaccessible secrets but does not throw.
    /// Returns a list of configs that failed validation.
    /// </summary>
    public async Task<IReadOnlyList<(Guid ConfigId, string SecretName, string Error)>> ValidateOwningAppSecretsAsync(
        IEnumerable<SpeAdminGraphService.ContainerTypeConfig> configs,
        CancellationToken ct = default)
    {
        var failures = new List<(Guid ConfigId, string SecretName, string Error)>();

        foreach (var config in configs.Where(c => c.HasOwningApp))
        {
            try
            {
                var secret = await FetchKeyVaultSecretAsync(config.OwningAppSecretName!, ct);

                if (string.IsNullOrWhiteSpace(secret))
                {
                    failures.Add((config.ConfigId, config.OwningAppSecretName!, "Secret exists but is empty."));
                    _logger.LogWarning(
                        "Startup validation: Key Vault secret '{SecretName}' for configId {ConfigId} is empty.",
                        config.OwningAppSecretName, config.ConfigId);
                }
                else
                {
                    _logger.LogDebug(
                        "Startup validation: Key Vault secret '{SecretName}' for configId {ConfigId} is accessible.",
                        config.OwningAppSecretName, config.ConfigId);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message;
                failures.Add((config.ConfigId, config.OwningAppSecretName!, errorMsg));
                _logger.LogWarning(
                    ex,
                    "Startup validation: Key Vault secret '{SecretName}' for configId {ConfigId} is inaccessible. " +
                    "OBO token acquisition will fail at request time for this config.",
                    config.OwningAppSecretName, config.ConfigId);
            }
        }

        if (failures.Count == 0)
        {
            _logger.LogInformation(
                "Startup validation: All owning app Key Vault secrets are accessible.");
        }
        else
        {
            _logger.LogWarning(
                "Startup validation: {FailureCount} owning app Key Vault secret(s) are inaccessible. " +
                "Affected configs: {ConfigIds}",
                failures.Count,
                string.Join(", ", failures.Select(f => f.ConfigId)));
        }

        return failures;
    }

    /// <summary>
    /// Evicts all expired OBO token cache entries.
    /// Should be called periodically (e.g., alongside SpeAdminGraphService.EvictExpiredClients).
    /// </summary>
    public void EvictExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _oboTokenCache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            _oboTokenCache.TryRemove(key, out _);
        }

        if (expired.Count > 0)
        {
            _logger.LogDebug("Evicted {Count} expired OBO token cache entries.", expired.Count);
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Returns or creates a MSAL ConfidentialClientApplication for the owning app.
    /// The MSAL app instance is per configId (one per owning app registration).
    ///
    /// Note: When the client secret changes (Key Vault rotation), the old MSAL app will
    /// use the stale secret until the MSAL app is evicted from the cache. OBO token
    /// acquisition failures will trigger eviction automatically.
    /// </summary>
    private IConfidentialClientApplication GetOrCreateMsalApp(
        SpeAdminGraphService.ContainerTypeConfig config,
        string clientSecret)
    {
        return _msalClientCache.GetOrAdd(config.ConfigId, _ =>
        {
            var authority = !string.IsNullOrWhiteSpace(config.OwningAppTenantId)
                ? $"https://login.microsoftonline.com/{config.OwningAppTenantId}"
                : "https://login.microsoftonline.com/organizations";

            _logger.LogDebug(
                "Creating MSAL ConfidentialClientApplication for owning app {OwningAppId}, configId {ConfigId}.",
                config.OwningAppId, config.ConfigId);

            return ConfidentialClientApplicationBuilder
                .Create(config.OwningAppId)
                .WithClientSecret(clientSecret)
                .WithAuthority(authority)
                .Build();
        });
    }

    /// <summary>
    /// Fetches a secret from Azure Key Vault by name.
    /// Throws <see cref="InvalidOperationException"/> on 404 or 403.
    /// </summary>
    private async Task<string> FetchKeyVaultSecretAsync(string secretName, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Retrieving owning app secret '{SecretName}' from Key Vault.", secretName);

            var response = await _secretClient.GetSecretAsync(secretName, version: null, ct);
            var secret = response.Value.Value;

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"Key Vault secret '{secretName}' exists but contains an empty value.");
            }

            return secret;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            _logger.LogError(
                "Key Vault secret '{SecretName}' not found. " +
                "Verify the secret name in sprk_specontainertypeconfig (sprk_owningappsecretname).",
                secretName);
            throw new InvalidOperationException(
                $"Key Vault secret '{secretName}' not found. Verify the secret is provisioned.", ex);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "Access denied to Key Vault secret '{SecretName}'. " +
                "Verify the managed identity has Get permission on this secret.",
                secretName);
            throw new InvalidOperationException(
                $"Access denied to Key Vault secret '{secretName}'. Check managed identity permissions.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error fetching Key Vault secret '{SecretName}'.", secretName);
            throw;
        }
    }

    /// <summary>
    /// Computes a SHA256 hash of the access token for use as a cache key.
    /// Auth constraint: MUST NOT store user tokens in plaintext.
    /// Returns a lowercase hex string (64 characters).
    /// </summary>
    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
