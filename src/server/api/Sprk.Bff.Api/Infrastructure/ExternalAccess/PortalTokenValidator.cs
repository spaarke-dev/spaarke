using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Validates Power Pages portal-issued JWT tokens.
///
/// Portal tokens are issued by the Power Pages OAuth implicit grant endpoint:
///   GET {portalUrl}/_services/auth/token
///
/// Public key for validation is fetched from:
///   GET {portalUrl}/_services/auth/publickey
///
/// The public key is cached in IMemoryCache for 1 hour (it rarely changes).
/// </summary>
public class PortalTokenValidator
{
    private const string PublicKeyCacheKey = "portal:publickey";
    private static readonly TimeSpan PublicKeyCacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortalTokenValidator> _logger;

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public PortalTokenValidator(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        IConfiguration configuration,
        ILogger<PortalTokenValidator> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Validates a portal-issued JWT token.
    /// </summary>
    /// <param name="bearerToken">The raw JWT token (without "Bearer " prefix).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with claims if valid.</returns>
    public async Task<PortalTokenValidationResult> ValidateAsync(string bearerToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return PortalTokenValidationResult.Invalid("Token is empty");

        // Quick check: is this token from the portal issuer?
        if (!IsPortalToken(bearerToken))
            return PortalTokenValidationResult.NotPortalToken();

        try
        {
            var publicKey = await GetPortalPublicKeyAsync(ct);
            if (publicKey is null)
                return PortalTokenValidationResult.Invalid("Could not retrieve portal public key");

            var portalBaseUrl = _configuration["PowerPages:BaseUrl"]
                ?? throw new InvalidOperationException("PowerPages:BaseUrl configuration is required");

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = portalBaseUrl.TrimEnd('/'),
                ValidateAudience = false, // Portal tokens don't set aud for BFF use
                ValidateLifetime = true,
                IssuerSigningKey = publicKey,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = TokenHandler.ValidateToken(bearerToken, validationParameters, out var validatedToken);

            // Extract Contact identity
            var contactIdStr = principal.FindFirst("contactid")?.Value
                ?? principal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(contactIdStr))
                return PortalTokenValidationResult.Invalid("Token missing contact identity claims");

            var email = principal.FindFirst("email")?.Value
                ?? principal.FindFirst("preferred_username")?.Value
                ?? contactIdStr;

            // If sub is a GUID, use directly; otherwise treat as email for Contact lookup
            var isContactGuid = Guid.TryParse(contactIdStr, out var contactId);

            return PortalTokenValidationResult.Valid(
                contactIdClaim: contactIdStr,
                isContactGuid: isContactGuid,
                contactId: isContactGuid ? contactId : Guid.Empty,
                email: email,
                validatedToken: validatedToken as JwtSecurityToken);
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogDebug("Portal token expired: {Message}", ex.Message);
            return PortalTokenValidationResult.Invalid("Token has expired");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Portal token validation failed: {Message}", ex.Message);
            return PortalTokenValidationResult.Invalid($"Token validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating portal token");
            return PortalTokenValidationResult.Invalid("Token validation error");
        }
    }

    /// <summary>
    /// Checks if the token's issuer claim matches the portal URL pattern (without full validation).
    /// Used as a fast pre-check before attempting full validation.
    /// </summary>
    public bool IsPortalToken(string bearerToken)
    {
        try
        {
            var portalBaseUrl = _configuration["PowerPages:BaseUrl"];
            if (string.IsNullOrEmpty(portalBaseUrl))
                return false;

            var jwt = TokenHandler.ReadJwtToken(bearerToken);
            var issuer = jwt.Issuer;
            return issuer != null && issuer.StartsWith(portalBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches and caches the portal's RSA public key for token signature validation.
    /// </summary>
    private async Task<SecurityKey?> GetPortalPublicKeyAsync(CancellationToken ct)
    {
        if (_memoryCache.TryGetValue(PublicKeyCacheKey, out SecurityKey? cached))
            return cached;

        var portalBaseUrl = _configuration["PowerPages:BaseUrl"]
            ?? throw new InvalidOperationException("PowerPages:BaseUrl configuration is required");

        var publicKeyUrl = $"{portalBaseUrl.TrimEnd('/')}/_services/auth/publickey";

        try
        {
            var response = await _httpClient.GetAsync(publicKeyUrl, ct);
            response.EnsureSuccessStatusCode();

            var keyData = await response.Content.ReadFromJsonAsync<PortalPublicKeyResponse>(ct);
            if (keyData?.Key is null)
            {
                _logger.LogWarning("Portal public key endpoint returned no key: {Url}", publicKeyUrl);
                return null;
            }

            // Parse the RSA public key from the portal's response
            var securityKey = ParsePublicKey(keyData.Key);
            if (securityKey is null)
                return null;

            _memoryCache.Set(PublicKeyCacheKey, securityKey, PublicKeyCacheTtl);
            _logger.LogInformation("Cached portal public key from {Url}", publicKeyUrl);
            return securityKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch portal public key from {Url}", publicKeyUrl);
            return null;
        }
    }

    private static SecurityKey? ParsePublicKey(string publicKeyPem)
    {
        try
        {
            // Power Pages returns a PEM-encoded RSA public key or JWKS format
            if (publicKeyPem.StartsWith("-----BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                // PEM format
                var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportFromPem(publicKeyPem.AsSpan());
                return new RsaSecurityKey(rsa);
            }

            // Could also be a JWKS JSON key set — attempt to parse
            var keySet = JsonSerializer.Deserialize<JsonWebKeySet>(publicKeyPem);
            return keySet?.Keys?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private sealed class PortalPublicKeyResponse
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("keys")]
        public List<JsonWebKey>? Keys { get; set; }
    }
}

/// <summary>
/// Result of portal token validation.
/// </summary>
public sealed class PortalTokenValidationResult
{
    public bool IsValid { get; private init; }
    public bool IsPortalToken { get; private init; }
    public string? Error { get; private init; }

    /// <summary>The raw contactid claim value (GUID or email).</summary>
    public string? ContactIdClaim { get; private init; }

    /// <summary>True if ContactIdClaim is a GUID (contactid claim), false if it's an email (sub claim).</summary>
    public bool IsContactGuid { get; private init; }

    /// <summary>The Contact GUID (only valid if IsContactGuid = true).</summary>
    public Guid ContactId { get; private init; }

    /// <summary>The Contact's email address.</summary>
    public string? Email { get; private init; }

    public JwtSecurityToken? Token { get; private init; }

    public static PortalTokenValidationResult Valid(
        string contactIdClaim, bool isContactGuid, Guid contactId, string email, JwtSecurityToken? validatedToken)
        => new()
        {
            IsValid = true,
            IsPortalToken = true,
            ContactIdClaim = contactIdClaim,
            IsContactGuid = isContactGuid,
            ContactId = contactId,
            Email = email,
            Token = validatedToken
        };

    public static PortalTokenValidationResult Invalid(string error)
        => new() { IsValid = false, IsPortalToken = true, Error = error };

    public static PortalTokenValidationResult NotPortalToken()
        => new() { IsValid = false, IsPortalToken = false };
}
