using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Tracking;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Security behavior of <see cref="TrackingTokenSigner"/> (FR-A1 / NFR-07). Exercises the REAL HMAC sign/verify
/// crypto through the public async API; the only Key-Vault-touching step (<c>ResolveSigningKeyAsync</c>) is
/// overridden with a deterministic key, so there is no Key Vault, no transport mock, no wiring test (ADR-038).
/// Verify-before-trust is the property under test: tampered / forged / malformed tokens MUST be rejected and
/// MUST NOT throw.
/// </summary>
public sealed class TrackingTokenSignerTests
{
    // Two distinct 32-byte keys (deterministic, not from Key Vault) for round-trip vs forgery.
    private static readonly byte[] KeyA = Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray();
    private static readonly byte[] KeyB = Enumerable.Range(0, 32).Select(i => (byte)(200 - i)).ToArray();

    private static readonly Guid RecordId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Issued = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A signer with a fixed key, bypassing Key Vault (overrides the sole KV seam).</summary>
    private sealed class FixedKeySigner : TrackingTokenSigner
    {
        private readonly byte[]? _key;
        public FixedKeySigner(byte[]? key)
            : base(
                credential: null!,
                options: Mock.Of<IOptionsMonitor<TrackingFooterOptions>>(m => m.CurrentValue == new TrackingFooterOptions()),
                configuration: new ConfigurationBuilder().Build(),
                logger: NullLogger<TrackingTokenSigner>.Instance)
        {
            _key = key;
        }

        protected override Task<byte[]?> ResolveSigningKeyAsync(string? tenantId, CancellationToken ct)
            => Task.FromResult(_key);
    }

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_RoundTripsPayload()
    {
        var signer = new FixedKeySigner(KeyA);

        var token = await signer.SignAsync("sprk_matter", RecordId, tenantId: "tenant-1", Issued);
        token.Should().NotBeNullOrEmpty();

        var result = await signer.VerifyAsync(token);

        result.IsValid.Should().BeTrue();
        result.Payload.Should().Be(new TrackingTokenPayload("sprk_matter", RecordId, "tenant-1", Issued));
    }

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_RoundTripsWithNullTenant()
    {
        var signer = new FixedKeySigner(KeyA);

        var token = await signer.SignAsync("sprk_project", RecordId, tenantId: null, Issued);
        var result = await signer.VerifyAsync(token);

        result.IsValid.Should().BeTrue();
        result.Payload!.TenantId.Should().BeNull();
        result.Payload.RecordId.Should().Be(RecordId);
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_ReturnsInvalid()
    {
        var signer = new FixedKeySigner(KeyA);
        var token = await signer.SignAsync("sprk_matter", RecordId, "t", Issued);

        var tampered = FlipCharAfterDot(token!);
        var result = await signer.VerifyAsync(tampered);

        result.IsValid.Should().BeFalse();
        result.Payload.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_TamperedPayload_ReturnsInvalid()
    {
        var signer = new FixedKeySigner(KeyA);
        var token = await signer.SignAsync("sprk_matter", RecordId, "t", Issued);

        // Alter the first payload char (still a valid base64url char) → different bytes → HMAC mismatch / bad JSON.
        var chars = token!.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        var result = await signer.VerifyAsync(new string(chars));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_TokenSignedWithDifferentKey_ReturnsInvalid()
    {
        var issuer = new FixedKeySigner(KeyA);
        var verifier = new FixedKeySigner(KeyB); // forgery: verifier holds a different key

        var token = await issuer.SignAsync("sprk_matter", RecordId, "t", Issued);
        var result = await verifier.VerifyAsync(token);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("no-dot-here")]
    [InlineData("only.")]
    [InlineData(".onlysig")]
    [InlineData("too.many.dots")]
    [InlineData("!!!.@@@")]              // non-base64url segments
    [InlineData("eyJ.notavalidsig")]     // decodable-ish payload, bad signature
    public async Task VerifyAsync_MalformedInput_ReturnsInvalidAndDoesNotThrow(string? token)
    {
        var signer = new FixedKeySigner(KeyA);

        var act = async () => await signer.VerifyAsync(token);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task SignAsync_WhenKeyUnavailable_ReturnsNull()
    {
        var signer = new FixedKeySigner(key: null); // unconfigured / KV unavailable

        var token = await signer.SignAsync("sprk_matter", RecordId, "t", Issued);

        token.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_WhenKeyUnavailable_ReturnsInvalid()
    {
        // A well-formed token from a keyed signer, verified by a signer with no key → cannot verify → invalid.
        var keyed = new FixedKeySigner(KeyA);
        var token = await keyed.SignAsync("sprk_matter", RecordId, "t", Issued);

        var keyless = new FixedKeySigner(key: null);
        var result = await keyless.VerifyAsync(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WrongLengthSignature_ReturnsInvalidWithoutThrow()
    {
        // A signature segment that decodes to the wrong length must be rejected, not throw
        // (CryptographicOperations.FixedTimeEquals returns false for unequal lengths).
        var signer = new FixedKeySigner(KeyA);
        var token = await signer.SignAsync("sprk_matter", RecordId, "t", Issued);
        var payloadSeg = token!.Split('.')[0];

        // Replace the signature with a valid-base64url but wrong-length (1-byte) value.
        var shortSig = Convert.ToBase64String(new byte[] { 0x01 }).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var result = await signer.VerifyAsync($"{payloadSeg}.{shortSig}");

        result.IsValid.Should().BeFalse();
    }

    /// <summary>Flips one character of the signature segment (after the '.') to a different base64url char.</summary>
    private static string FlipCharAfterDot(string token)
    {
        var dot = token.IndexOf('.');
        var chars = token.ToCharArray();
        var i = dot + 1;
        chars[i] = chars[i] == 'A' ? 'B' : 'A';
        return new string(chars);
    }
}
