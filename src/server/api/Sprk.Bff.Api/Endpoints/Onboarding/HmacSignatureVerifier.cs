using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// HMAC-SHA256 signature verifier for the consent-callback endpoint (task
/// 042 — customer-provisioning-orchestration-r1). Verifies the signature
/// header value against a fresh HMAC computed over the raw request body
/// using the configured shared signing key from
/// <see cref="OnboardingOptions.HmacSigningKey"/>.
///
/// <para>
/// Distinct from <see cref="Sprk.Bff.Api.Api.Filters.WebhookSignatureFilter"/>
/// (Communication + Email Service Endpoint webhooks) — kept as a plain
/// POCO verifier here so the endpoint handler can call it inline (with a
/// distinct 400 Bad Request response for missing header vs 401 Unauthorized
/// for invalid signature) and so the verifier is trivially unit-testable
/// in isolation (POML step 8 acceptance).
/// </para>
///
/// <para>
/// Wire format: signature MAY be prefixed with <c>sha256=</c>
/// (case-insensitive). The remainder MUST be either lowercase / uppercase
/// hex (64 chars) OR Base64 (standard or URL-safe). Comparison is
/// constant-time via
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// </para>
///
/// <para>
/// FAIL-CLOSED: When the configured signing key is null/empty AND
/// <see cref="OnboardingOptions.EnableDevBypass"/> is false, verification
/// returns <see cref="HmacSignatureVerifyResult.KeyNotConfigured"/> so the
/// endpoint returns 401 Unauthorized. The BFF never accepts unsigned
/// consent-callback traffic in any environment.
/// </para>
/// </summary>
public sealed class HmacSignatureVerifier
{
    private readonly IOptionsMonitor<OnboardingOptions> _options;

    /// <summary>Constructs the verifier bound to the <c>Onboarding</c> options section.</summary>
    public HmacSignatureVerifier(IOptionsMonitor<OnboardingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Verifies the signature <paramref name="signatureHeaderValue"/>
    /// against a fresh HMAC-SHA256 computed over
    /// <paramref name="rawBody"/> using the configured signing key.
    /// </summary>
    /// <param name="rawBody">Raw request body bytes (exact bytes over which the sender computed the signature).</param>
    /// <param name="signatureHeaderValue">Signature header value from the request (nullable — <c>null</c> or whitespace returns <see cref="HmacSignatureVerifyResult.MissingSignature"/>).</param>
    /// <returns>
    /// <see cref="HmacSignatureVerifyResult.Valid"/> on match;
    /// <see cref="HmacSignatureVerifyResult.MissingSignature"/> when the
    /// header value is null/whitespace;
    /// <see cref="HmacSignatureVerifyResult.MalformedSignature"/> when the
    /// header value is not a valid hex or base64 SHA-256 digest;
    /// <see cref="HmacSignatureVerifyResult.KeyNotConfigured"/> when the
    /// server has no signing key AND dev-bypass is not enabled;
    /// <see cref="HmacSignatureVerifyResult.SignatureMismatch"/> on a
    /// constant-time compare failure.
    /// </returns>
    public HmacSignatureVerifyResult Verify(ReadOnlySpan<byte> rawBody, string? signatureHeaderValue)
    {
        if (string.IsNullOrWhiteSpace(signatureHeaderValue))
        {
            return HmacSignatureVerifyResult.MissingSignature;
        }

        var opts = _options.CurrentValue;
        if (string.IsNullOrEmpty(opts.HmacSigningKey))
        {
            // Deliberately does NOT distinguish dev-bypass here — the endpoint
            // layer decides whether dev-bypass is permitted (based on env)
            // and can short-circuit before calling Verify().
            return HmacSignatureVerifyResult.KeyNotConfigured;
        }

        // Strip optional "sha256=" prefix.
        var raw = signatureHeaderValue!.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeaderValue[7..]
            : signatureHeaderValue;

        if (!TryDecodeSignature(raw, out var providedHash))
        {
            return HmacSignatureVerifyResult.MalformedSignature;
        }

        byte[] computedHash;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(opts.HmacSigningKey)))
        {
            computedHash = hmac.ComputeHash(rawBody.ToArray());
        }

        // FixedTimeEquals requires equal lengths; length-mismatch is handled
        // as constant-time-relative-to-the-data by short-circuiting up front.
        if (providedHash.Length != computedHash.Length
            || !CryptographicOperations.FixedTimeEquals(providedHash, computedHash))
        {
            return HmacSignatureVerifyResult.SignatureMismatch;
        }

        return HmacSignatureVerifyResult.Valid;
    }

    /// <summary>
    /// Attempts to decode the signature value as either hex (64 chars for
    /// SHA-256) or Base64 (standard or URL-safe). Returns false for any
    /// other format.
    /// </summary>
    private static bool TryDecodeSignature(string value, out byte[] decoded)
    {
        if (value.Length == 64 && IsHex(value))
        {
            try
            {
                decoded = Convert.FromHexString(value);
                return true;
            }
            catch (FormatException)
            {
                // Fall through to base64.
            }
        }

        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            try
            {
                var padded = value.Replace('-', '+').Replace('_', '/');
                var pad = padded.Length % 4;
                if (pad == 2) padded += "==";
                else if (pad == 3) padded += "=";
                decoded = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                decoded = Array.Empty<byte>();
                return false;
            }
        }
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9')
                  || (c >= 'a' && c <= 'f')
                  || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Outcome of a <see cref="HmacSignatureVerifier.Verify"/> call. Endpoint
/// layer maps each value to a distinct HTTP status per POML acceptance
/// criteria #2 (missing → 400) and #3 (invalid → 401).
/// </summary>
public enum HmacSignatureVerifyResult
{
    /// <summary>Signature matched — proceed with request handling.</summary>
    Valid = 0,

    /// <summary>Signature header was null / whitespace. Maps to HTTP 400 Bad Request.</summary>
    MissingSignature = 1,

    /// <summary>Signature header was present but could not be decoded as hex or base64. Maps to HTTP 401 Unauthorized.</summary>
    MalformedSignature = 2,

    /// <summary>Server has no signing key configured AND dev-bypass is not enabled. Maps to HTTP 401 Unauthorized.</summary>
    KeyNotConfigured = 3,

    /// <summary>Signature was decoded but did not match the computed HMAC. Maps to HTTP 401 Unauthorized.</summary>
    SignatureMismatch = 4,
}
