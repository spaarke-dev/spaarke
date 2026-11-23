using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Tracking;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// The REAL <see cref="TrackingTokenSigner"/> (HMAC-SHA256 sign and constant-time verify) with a fixed key instead of Key Vault: the one
/// override its type offers for tests. A null key is the "no signing key configured" case (sign returns null, verify is invalid).
/// </summary>
internal sealed class FixedKeyTrackingTokenSigner(byte[]? key) : TrackingTokenSigner(
    credential: null!,
    options: Mock.Of<IOptionsMonitor<TrackingFooterOptions>>(m => m.CurrentValue == new TrackingFooterOptions()),
    configuration: new ConfigurationBuilder().Build(),
    logger: NullLogger<TrackingTokenSigner>.Instance)
{
    public static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    public static readonly byte[] KeyB = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    protected override Task<byte[]?> ResolveSigningKeyAsync(string? tenantId, CancellationToken ct) => Task.FromResult(key);
}
