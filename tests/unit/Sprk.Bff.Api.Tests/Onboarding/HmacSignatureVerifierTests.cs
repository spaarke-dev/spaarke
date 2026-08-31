// -----------------------------------------------------------------------------
// HmacSignatureVerifierTests.cs
//
// Unit tests for the H0.5 consent-callback HMAC-SHA256 verifier (task 042 —
// customer-provisioning-orchestration-r1). Covers happy path + all four
// negative branches enumerated by HmacSignatureVerifyResult.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Endpoints.Onboarding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Onboarding;

public sealed class HmacSignatureVerifierTests
{
    private const string ValidKey = "test-signing-key-42-chars-of-shared-secret";
    private static readonly byte[] BodyBytes = Encoding.UTF8.GetBytes(
        "{\"customerId\":\"acme-corp\",\"tid\":\"11111111-1111-1111-1111-111111111111\"}");

    [Fact]
    public void Verify_ValidHexSignature_ReturnsValid()
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var sig = ComputeHexSignature(ValidKey, BodyBytes);

        var result = verifier.Verify(BodyBytes, sig);

        result.Should().Be(HmacSignatureVerifyResult.Valid);
    }

    [Fact]
    public void Verify_ValidHexSignature_WithSha256Prefix_ReturnsValid()
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var sig = "sha256=" + ComputeHexSignature(ValidKey, BodyBytes);

        var result = verifier.Verify(BodyBytes, sig);

        result.Should().Be(HmacSignatureVerifyResult.Valid);
    }

    [Fact]
    public void Verify_ValidBase64Signature_ReturnsValid()
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var sig = ComputeBase64Signature(ValidKey, BodyBytes);

        var result = verifier.Verify(BodyBytes, sig);

        result.Should().Be(HmacSignatureVerifyResult.Valid);
    }

    [Fact]
    public void Verify_ValidUppercaseHexSignature_ReturnsValid()
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var sig = ComputeHexSignature(ValidKey, BodyBytes).ToUpperInvariant();

        var result = verifier.Verify(BodyBytes, sig);

        result.Should().Be(HmacSignatureVerifyResult.Valid);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_MissingSignature_ReturnsMissingSignature(string? headerValue)
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });

        var result = verifier.Verify(BodyBytes, headerValue);

        result.Should().Be(HmacSignatureVerifyResult.MissingSignature);
    }

    [Theory]
    [InlineData("not-hex-not-base64-%%%%%%%%")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")] // wrong-length "hex"
    public void Verify_MalformedSignature_ReturnsMalformedSignature(string headerValue)
    {
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });

        var result = verifier.Verify(BodyBytes, headerValue);

        result.Should().Be(HmacSignatureVerifyResult.MalformedSignature);
    }

    [Fact]
    public void Verify_InvalidSignature_SameLength_ReturnsSignatureMismatch()
    {
        // A well-formed hex signature but computed with a DIFFERENT key.
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var wrongKeySig = ComputeHexSignature("a-completely-different-key", BodyBytes);

        var result = verifier.Verify(BodyBytes, wrongKeySig);

        result.Should().Be(HmacSignatureVerifyResult.SignatureMismatch);
    }

    [Fact]
    public void Verify_KeyNotConfigured_ReturnsKeyNotConfigured()
    {
        // Empty key AND a caller-supplied signature — the verifier does NOT
        // silently accept unsigned traffic. Fail-closed per §Q spec.md NFR-05.
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = string.Empty });
        var sig = ComputeHexSignature("any-key", BodyBytes);

        var result = verifier.Verify(BodyBytes, sig);

        result.Should().Be(HmacSignatureVerifyResult.KeyNotConfigured);
    }

    [Fact]
    public void Verify_BodyMutation_DetectedAsMismatch()
    {
        // The signature is computed over the ORIGINAL body; if the body is
        // mutated by a MITM, the verifier MUST reject.
        var verifier = NewVerifier(new OnboardingOptions { HmacSigningKey = ValidKey });
        var sig = ComputeHexSignature(ValidKey, BodyBytes);
        var mutated = BodyBytes.ToArray();
        mutated[0] ^= 0x01; // flip one bit

        var result = verifier.Verify(mutated, sig);

        result.Should().Be(HmacSignatureVerifyResult.SignatureMismatch);
    }

    private static HmacSignatureVerifier NewVerifier(OnboardingOptions options)
    {
        var monitor = new StaticOptionsMonitor<OnboardingOptions>(options);
        return new HmacSignatureVerifier(monitor);
    }

    private static string ComputeHexSignature(string key, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string ComputeBase64Signature(string key, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    /// <summary>Test-only <see cref="IOptionsMonitor{TOptions}"/> that returns a fixed value.</summary>
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
