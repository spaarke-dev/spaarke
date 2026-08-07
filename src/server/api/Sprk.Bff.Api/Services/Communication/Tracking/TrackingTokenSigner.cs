using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Communication.Tracking;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ITrackingTokenSigner"/> (FR-A1 / NFR-07). The signing key is
/// resolved from Azure Key Vault by SECRET NAME (from <see cref="TrackingFooterOptions.SigningKeySecretName"/>,
/// supplied by task 011 — never the key material itself) using the DI-injected central
/// <see cref="TokenCredential"/> (ADR-028: managed identity in Azure; no credential is <c>new</c>-ed here).
/// The key is cached per secret name with a short TTL so the hot path (every send / every inbound rung) does
/// not round-trip Key Vault, while a rotated secret still propagates without a redeploy (ADR-018 philosophy).
/// </summary>
/// <remarks>
/// <para><b>Token format:</b> <c>base64url(payloadJson) "." base64url(HMAC-SHA256(key, payloadJson))</c>. The
/// signature covers the EXACT payload bytes the token carries, so verification is independent of JSON
/// serialization determinism. Verification recomputes the HMAC over the carried bytes and compares with
/// <see cref="CryptographicOperations.FixedTimeEquals"/> (constant-time) BEFORE the payload is trusted; any
/// parse / decode / tamper / forgery path returns <see cref="TrackingTokenVerification.Invalid"/> without
/// throwing.</para>
/// <para><b>Rotation scope (POML escalation trigger #2 boundary):</b> a single Key Vault secret per tenant.
/// Rotating the secret invalidates tokens signed with the prior key — graceful dual-key rotation is
/// deliberately out of scope (the spec names a single secret). If overlapping-key rotation is ever required,
/// that is an ADR-028 Path-A escalation, not a silent change here.</para>
/// <para><b>Testability:</b> the ONLY Key-Vault-touching step is <see cref="ResolveSigningKeyAsync"/> (protected
/// virtual); unit tests override it with a deterministic key and exercise the real sign/verify/tamper/forgery
/// crypto through the public async API — no Key Vault, no transport mocks (ADR-038).</para>
/// </remarks>
public class TrackingTokenSigner : ITrackingTokenSigner
{
    private static readonly TimeSpan KeyCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions PayloadJson = new(); // default: stable PascalCase round-trip

    private readonly TokenCredential _credential;
    private readonly IOptionsMonitor<TrackingFooterOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TrackingTokenSigner> _logger;

    private readonly ConcurrentDictionary<string, CachedKey> _keyCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private SecretClient? _secretClient;
    private bool _secretClientResolved;
    private readonly object _clientLock = new();

    public TrackingTokenSigner(
        TokenCredential credential,
        IOptionsMonitor<TrackingFooterOptions> options,
        IConfiguration configuration,
        ILogger<TrackingTokenSigner> logger)
    {
        _credential = credential;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> SignAsync(
        string recordType, Guid recordId, string? tenantId, DateTimeOffset issued, CancellationToken ct = default)
    {
        var key = await ResolveSigningKeyAsync(tenantId, ct).ConfigureAwait(false);
        if (key is null || key.Length == 0)
            return null; // unconfigured / unavailable → no footer token (non-fatal, NFR-04)

        var payload = new TrackingTokenPayload(recordType, recordId, tenantId, issued);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJson);
        var signature = HMACSHA256.HashData(key, payloadBytes);
        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    /// <inheritdoc />
    public async Task<TrackingTokenVerification> VerifyAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return TrackingTokenVerification.Invalid;

        try
        {
            var dot = token.IndexOf('.');
            // Exactly one '.' separating payload and signature.
            if (dot <= 0 || dot != token.LastIndexOf('.') || dot == token.Length - 1)
                return TrackingTokenVerification.Invalid;

            var payloadBytes = Base64UrlDecode(token.AsSpan(0, dot));
            var providedSig = Base64UrlDecode(token.AsSpan(dot + 1));

            // Read the CLAIMED payload (untrusted) only to select the tenant's key. A forged tenantId simply
            // selects that tenant's key — the forged signature then fails the constant-time compare below.
            var claimed = JsonSerializer.Deserialize<TrackingTokenPayload>(payloadBytes, PayloadJson);
            if (claimed is null || string.IsNullOrEmpty(claimed.RecordType))
                return TrackingTokenVerification.Invalid;

            var key = await ResolveSigningKeyAsync(claimed.TenantId, ct).ConfigureAwait(false);
            if (key is null || key.Length == 0)
                return TrackingTokenVerification.Invalid; // cannot verify without the key → not trusted

            var computedSig = HMACSHA256.HashData(key, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(computedSig, providedSig))
                return TrackingTokenVerification.Invalid;

            return new TrackingTokenVerification(true, claimed);
        }
        catch (Exception ex)
        {
            // Malformed base64 / JSON / any other parse failure → not a trusted token. NEVER throws (NFR-07).
            _logger.LogDebug(ex, "Tracking token verification failed (treated as untrusted).");
            return TrackingTokenVerification.Invalid;
        }
    }

    /// <summary>
    /// Resolves the raw HMAC key bytes for <paramref name="tenantId"/> from Key Vault (cached, TTL-bounded), or
    /// <c>null</c> when the feature is unconfigured (no secret name / no Key Vault URI) or the fetch fails
    /// (non-fatal). Overridden in unit tests to supply a deterministic key without Key Vault.
    /// </summary>
    protected virtual async Task<byte[]?> ResolveSigningKeyAsync(string? tenantId, CancellationToken ct)
    {
        var secretName = ResolveSecretName(tenantId);
        if (string.IsNullOrWhiteSpace(secretName))
            return null; // feature not configured → no token (NFR-04)

        if (_keyCache.TryGetValue(secretName, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return cached.Key;

        var client = GetSecretClient();
        if (client is null)
            return null;

        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the gate (another caller may have populated it).
            if (_keyCache.TryGetValue(secretName, out cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return cached.Key;

            var response = await client.GetSecretAsync(secretName, version: null, ct).ConfigureAwait(false);
            var secretValue = response.Value.Value;
            if (string.IsNullOrWhiteSpace(secretValue))
            {
                _logger.LogWarning("Tracking-footer signing secret '{SecretName}' is empty; no token will be issued.", secretName);
                return null;
            }

            var keyBytes = DecodeKey(secretValue);
            _keyCache[secretName] = new CachedKey(keyBytes, DateTimeOffset.UtcNow.Add(KeyCacheTtl));
            return keyBytes;
        }
        catch (Exception ex)
        {
            // Non-fatal: a Key Vault failure degrades to "no token" rather than failing send/capture (NFR-04).
            _logger.LogWarning(ex, "Failed to resolve tracking-footer signing key '{SecretName}' from Key Vault.", secretName);
            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// <summary>Per-tenant secret-name override (task 011 config) falling back to the global secret name.</summary>
    private string? ResolveSecretName(string? tenantId)
    {
        var opts = _options.CurrentValue;
        if (!string.IsNullOrEmpty(tenantId)
            && opts.Tenants.TryGetValue(tenantId, out var over)
            && !string.IsNullOrWhiteSpace(over.SigningKeySecretName))
        {
            return over.SigningKeySecretName;
        }
        return string.IsNullOrWhiteSpace(opts.SigningKeySecretName) ? null : opts.SigningKeySecretName;
    }

    /// <summary>
    /// Lazily builds ONE <see cref="SecretClient"/> over the central injected <see cref="TokenCredential"/>
    /// (ADR-028 — reuse, never <c>new</c> a credential). Returns null when no Key Vault URI is configured
    /// (feature off, logged once). <c>KeyVaultUri</c> mirrors the config key <c>SpeAdminModule</c> reads.
    /// </summary>
    private SecretClient? GetSecretClient()
    {
        if (_secretClientResolved)
            return _secretClient;

        lock (_clientLock)
        {
            if (_secretClientResolved)
                return _secretClient;

            var kvUri = _configuration["KeyVaultUri"] ?? _configuration["SpeAdmin:KeyVaultUri"];
            if (string.IsNullOrWhiteSpace(kvUri))
            {
                _logger.LogInformation(
                    "No KeyVaultUri configured; tracking-footer token signing is inactive (no token will be issued).");
            }
            else
            {
                _secretClient = new SecretClient(new Uri(kvUri), _credential);
            }

            _secretClientResolved = true;
            return _secretClient;
        }
    }

    /// <summary>
    /// Decodes the stored secret into key bytes. Convention is <c>openssl rand -base64 32</c> (a base64 string);
    /// falls back to the raw UTF-8 bytes when the value is not valid base64 (consistent on sign + verify, so a
    /// non-base64 secret still round-trips — only key strength differs).
    /// </summary>
    private static byte[] DecodeKey(string secretValue)
    {
        try { return Convert.FromBase64String(secretValue); }
        catch (FormatException) { return System.Text.Encoding.UTF8.GetBytes(secretValue); }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(ReadOnlySpan<char> value)
    {
        var s = new string(value).Replace('-', '+').Replace('_', '/');
        return (s.Length % 4) switch
        {
            2 => Convert.FromBase64String(s + "=="),
            3 => Convert.FromBase64String(s + "="),
            0 => Convert.FromBase64String(s),
            _ => throw new FormatException("Invalid base64url length."),
        };
    }

    private readonly record struct CachedKey(byte[] Key, DateTimeOffset ExpiresAtUtc);
}
