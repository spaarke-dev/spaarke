using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Construction-time contract of <see cref="SessionFileBlobStore"/> (spaarkeai-compose-r8 FR-B01,
/// task 060): managed identity only, no secret in configuration, no container creation, and a
/// deployment without a configured endpoint degrades VISIBLY rather than pretending to store bytes.
/// </summary>
public sealed class SessionFileBlobStoreConfigurationTests
{
    private static TokenCredential Credential => Mock.Of<TokenCredential>();

    // ─────────────────────────────────────────────────────────────────────────
    // Root CLAUDE.md §9 / ADR-028 — managed identity, never a key.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=sprk;AccountKey=abc123==;EndpointSuffix=core.windows.net")]
    [InlineData("https://sprk.blob.core.windows.net/?sv=2021-08-06&sig=REDACTED")]
    [InlineData("BlobEndpoint=https://sprk.blob.core.windows.net/;SharedAccessSignature=sv=2021")]
    [InlineData("https://sprk.blob.core.windows.net;AccountKey=abc123==")]
    public void Constructor_RefusesAnEndpointCarryingASecret(string secretBearingEndpoint)
    {
        // A connection string or SAS in configuration is a secret in configuration. Refusing at
        // construction turns a silent credential downgrade into a startup failure someone must fix.
        var construct = () => new SessionFileBlobStore(
            secretBearingEndpoint, containerName: null, Credential, NullLogger<SessionFileBlobStore>.Instance);

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity*");
    }

    [Fact]
    public void Constructor_RefusesAnEndpointThatIsNotAnAbsoluteUri()
    {
        var construct = () => new SessionFileBlobStore(
            "sprkstorage", containerName: null, Credential, NullLogger<SessionFileBlobStore>.Instance);

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*absolute URI*");
    }

    [Theory]
    [InlineData("http://sprk.blob.core.windows.net")]
    [InlineData("HTTP://sprk.blob.core.windows.net")]
    [InlineData("ftp://sprk.blob.core.windows.net")]
    public void Constructor_RefusesANonHttpsEndpoint(string insecureEndpoint)
    {
        // A managed-identity bearer token is sent on this connection. An http endpoint would put it on
        // the wire in cleartext, so the scheme is a security decision rather than a formatting one.
        var construct = () => new SessionFileBlobStore(
            insecureEndpoint, containerName: null, Credential, NullLogger<SessionFileBlobStore>.Instance);

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*https*");
    }

    [Theory]
    [InlineData("has/slash")]      // would move the tenant out of first position in the blob path
    [InlineData("UPPERCASE")]
    [InlineData("ab")]             // < 3 chars
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    public void Constructor_RefusesAnInvalidContainerName(string containerName)
    {
        // The container name is the ONE name component that does not go through the per-segment
        // validator, so it gets its own. A value containing '/' would silently reshape the blob path.
        var construct = () => new SessionFileBlobStore(
            "https://sprk.blob.core.windows.net", containerName, Credential,
            NullLogger<SessionFileBlobStore>.Instance);

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*container name*");
    }

    // NOTE (ADR-038 §7 ban B4 — constructor null-check tests): a
    // `Constructor_RequiresACredential` test was written here and then DELETED. The production code
    // keeps `ArgumentNullException.ThrowIfNull(credential)`; B4's ruling is "delete the test; trust the
    // throw helper". The behaviour that actually matters — that there is no non-managed-identity path
    // to fall back to — is covered by the secret-rejection theory above, which tests a decision rather
    // than a language feature.

    [Fact]
    public void Constructor_WithAValidEndpoint_EnablesTheStore()
    {
        var store = new SessionFileBlobStore(
            "https://sprkspaarkedevsa.blob.core.windows.net", null, Credential,
            NullLogger<SessionFileBlobStore>.Instance);

        store.IsEnabled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Unconfigured deployment: degrade visibly, never silently.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNoEndpoint_LeavesTheStoreDisabled(string? endpoint)
    {
        var store = new SessionFileBlobStore(
            endpoint, null, Credential, NullLogger<SessionFileBlobStore>.Instance);

        store.IsEnabled.Should().BeFalse(
            "local dev and test hosts have no storage account; they must behave exactly as they did " +
            "before this component existed rather than failing at startup");
    }

    [Fact]
    public async Task WriteAsync_WhenDisabled_ReportsStoreDisabled_AndDoesNotThrow()
    {
        var store = new SessionFileBlobStore(
            blobEndpoint: null, containerName: null, Credential, NullLogger<SessionFileBlobStore>.Instance);

        var outcome = await store.WriteAsync(
            "aaaaaaaa-1111-2222-3333-444444444444",
            "11111111-2222-3333-4444-555555555555",
            "99999999999999999999999999999999",
            BinaryData.FromString("payload"),
            "text/plain");

        outcome.Should().Be(SessionFileStoreOutcome.StoreDisabled,
            "the caller must be able to tell 'no durable store configured' from 'the bytes are durable' " +
            "— a bool or a silent no-op is how the original defect stayed invisible");
    }

    [Fact]
    public async Task WriteAsync_WhenEnabled_ReportsWritten()
    {
        var store = new SessionFileBlobStore(
            new InMemorySessionFileBlobGateway(), NullLogger<SessionFileBlobStore>.Instance);

        var outcome = await store.WriteAsync(
            "aaaaaaaa-1111-2222-3333-444444444444",
            "11111111-2222-3333-4444-555555555555",
            "99999999999999999999999999999999",
            BinaryData.FromString("payload"),
            "text/plain");

        outcome.Should().Be(SessionFileStoreOutcome.Written);
    }

    [Fact]
    public async Task WriteAsync_WhenTheStoreFails_Throws_SoTheCallerCannotReportSuccess()
    {
        // The upload endpoint converts this into a 500. Returning a "soft failure" here would let the
        // endpoint answer 202 for a file that will not survive — the defect FR-B01 exists to remove.
        var store = new SessionFileBlobStore(
            new ThrowingGateway(), NullLogger<SessionFileBlobStore>.Instance);

        var write = async () => await store.WriteAsync(
            "aaaaaaaa-1111-2222-3333-444444444444",
            "11111111-2222-3333-4444-555555555555",
            "99999999999999999999999999999999",
            BinaryData.FromString("payload"),
            "text/plain");

        await write.Should().ThrowAsync<InvalidOperationException>();
    }

    // NOTE (ADR-038 §7 ban B6 — mirror tests): a `DefaultContainer_IsOneProvisionedByBicep` test
    // asserting `DefaultContainerName == "ai-chunks"` was written here and then DELETED. It was
    // implementation == implementation: it could not verify that the container is actually provisioned,
    // and anyone changing the default would simply change the literal in both places. The constraint it
    // was trying to express lives where it can be acted on — the XML doc on
    // `SessionFileBlobStore.DefaultContainerName` and
    // `projects/spaarkeai-compose-r8/notes/track-b-placement-justification.md` §4.

    private sealed class ThrowingGateway : SessionFileBlobGateway
    {
        public override Task UploadAsync(string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated blob failure");

        public override Task<SessionFileBytes?> DownloadAsync(string blobName, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated blob failure");
    }
}
