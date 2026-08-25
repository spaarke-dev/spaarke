using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// <c>tests/integration/tenant/**</c> — tenant-isolation KEEP category (ADR-038 §2 path #4).
/// Pins the ADR-014 / ADR-015 partitioning invariant of <see cref="SessionFileBlobStore"/>
/// (spaarkeai-compose-r8 FR-B01).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this suite exists.</b> A partitioning mistake in a byte store is a cross-tenant data
/// exposure that no functional test surfaces — upload works, download works, recall works, and the
/// feature is wrong the entire time. Nothing else in the pipeline would notice: the Cosmos manifest,
/// the AI-Search filter and the Redis key are all independently tenant-scoped, so a shared blob path
/// would leak silently underneath three correct-looking layers.
/// </para>
/// <para>
/// <b>Why these are reachability tests, not string tests.</b> The tests below never assert on the
/// shape of a path. They write bytes through the production store as one tenant and then attempt to
/// read them through the production store as another, against
/// <see cref="InMemorySessionFileBlobGateway"/> — which resolves blob names the way Azure Blob does:
/// opaque, ordinal, exact-match, no path semantics. Whether the read hits or misses is decided
/// entirely by the name <see cref="SessionFileBlobStore.BuildBlobName"/> produced.
/// </para>
/// <para>
/// <b>Observed to fail before it passed.</b> With the tenant segment removed from
/// <c>BuildBlobName</c> (i.e. <c>session-files/{sessionId}/{fileId}</c>),
/// <see cref="Read_FromAnotherTenant_WithTheSameSessionAndFileIds_MustNotReturnTheBytes"/> fails with
/// tenant B receiving tenant A's bytes, and
/// <see cref="Write_PlacesTheBlobUnderTheCallingTenantsPrefix"/> fails on the prefix invariant. Both
/// pass once the tenant segment is restored. See
/// <c>projects/spaarkeai-compose-r8/notes/track-b-placement-justification.md</c> §6 for the recorded
/// failure output.
/// </para>
/// </remarks>
[Trait("category", "tenant-isolation")]
public sealed class SessionFileBlobStoreTenantIsolationTests
{
    private const string TenantA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string TenantB = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string FileId = "99999999999999999999999999999999";

    private static readonly BinaryData TenantASecret =
        BinaryData.FromString("PRIVILEGED — tenant A settlement figures. Must never reach tenant B.");

    private static (SessionFileBlobStore Store, InMemorySessionFileBlobGateway Blobs) BuildStore()
    {
        var blobs = new InMemorySessionFileBlobGateway();
        var store = new SessionFileBlobStore(blobs, NullLogger<SessionFileBlobStore>.Instance);
        return (store, blobs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The core invariant: identifiers are not authority. Knowing (sessionId, fileId)
    // must not be enough to read another tenant's bytes.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Read_FromAnotherTenant_WithTheSameSessionAndFileIds_MustNotReturnTheBytes()
    {
        var (store, blobs) = BuildStore();

        // Tenant A uploads a file.
        var write = await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");
        write.Should().Be(SessionFileStoreOutcome.Written);

        // Tenant B knows the identifiers — the realistic attack, since session and file ids travel in
        // URLs, manifests, telemetry and client state. They are NOT a capability.
        var crossTenantRead = await store.ReadAsync(TenantB, SessionId, FileId);

        crossTenantRead.Should().BeNull(
            "a tenant must not be able to read another tenant's session-file bytes by knowing the " +
            "session id and file id (ADR-014 / ADR-015 — this is the failure this suite exists to catch)");

        // Prove the miss was caused by PARTITIONING, not by the write silently not happening.
        blobs.Count.Should().Be(1, "tenant A's write must still be present in the store");
        blobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, SessionId, FileId), out var stillThere)
            .Should().BeTrue("tenant A's bytes are physically present — tenant B simply cannot address them");
        stillThere!.ToString().Should().Be(TenantASecret.ToString());
    }

    [Fact]
    public async Task Read_FromTheOwningTenant_ReturnsTheBytes()
    {
        // Positive control. Without this, the cross-tenant test above could pass vacuously
        // (e.g. if the store silently wrote nothing, or every read returned null).
        var (store, _) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var ownRead = await store.ReadAsync(TenantA, SessionId, FileId);

        ownRead.Should().NotBeNull("the owning tenant must be able to read its own durable copy back");
        ownRead!.Content.ToString().Should().Be(TenantASecret.ToString());
        ownRead.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task TwoTenants_UsingIdenticalSessionAndFileIds_GetIndependentBytes()
    {
        // Same identifiers in two tenants must not collide — neither by overwriting each other nor by
        // reading through to each other. A collision here is both a leak and a data-loss bug.
        var (store, blobs) = BuildStore();

        var aBytes = BinaryData.FromString("tenant A content");
        var bBytes = BinaryData.FromString("tenant B content");

        await store.WriteAsync(TenantA, SessionId, FileId, aBytes, "text/plain");
        await store.WriteAsync(TenantB, SessionId, FileId, bBytes, "text/plain");

        blobs.Count.Should().Be(2, "identical identifiers in different tenants must occupy different blobs");
        (await store.ReadAsync(TenantA, SessionId, FileId))!.Content.ToString().Should().Be("tenant A content");
        (await store.ReadAsync(TenantB, SessionId, FileId))!.Content.ToString().Should().Be("tenant B content");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The tenant must be the FIRST segment, so the boundary is a prefix.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Write_PlacesTheBlobUnderTheCallingTenantsPrefix()
    {
        var (store, blobs) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        blobs.BlobNames.Should().ContainSingle();
        blobs.BlobNames[0].Should().StartWith(TenantA + "/",
            "the tenant id must be the FIRST path segment so the tenant boundary is a prefix — any " +
            "prefix-scoped operation added later (listing, scoped SAS, lifecycle rule, GDPR prefix " +
            "delete) is then tenant-scoped by construction rather than by remembering a filter");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Identifier injection: a caller must not be able to compose their way out of
    // their own prefix, whichever segment they control.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    // Classic traversal shapes.
    [InlineData("../" + TenantA + "/session-files/" + SessionId)]
    [InlineData("..")]
    // Direct separator injection (Blob names are flat, so a '/' is just more path).
    [InlineData(TenantA + "/session-files/" + SessionId)]
    [InlineData("sess/../../" + TenantA)]
    // Backslash + encoded forms, in case a future gateway normalises them.
    [InlineData("sess\\..\\" + TenantA)]
    [InlineData("sess%2F..%2F")]
    // Whitespace / control characters that could confuse a downstream consumer.
    [InlineData("sess id")]
    [InlineData("")]
    [InlineData("   ")]
    // Trailing newline: .NET's `$` anchor matches BEFORE a trailing \n, so a `^…$` pattern would let
    // this through and put a newline into the blob name (and into anything derived from it). The
    // pattern is anchored `\A…\z` precisely so this is rejected — this case is the forcing function.
    [InlineData("11111111-2222-3333-4444-555555555555\n")]
    [InlineData("11111111-2222-3333-4444-555555555555\r\n")]
    public async Task Write_RejectsSessionIdsThatCouldEscapeTheTenantPrefix(string craftedSessionId)
    {
        var (store, blobs) = BuildStore();

        var attempt = async () => await store.WriteAsync(
            TenantB, craftedSessionId, FileId, BinaryData.FromString("tenant B payload"), "text/plain");

        await attempt.Should().ThrowAsync<ArgumentException>(
            "an identifier that is not a safe blob-name segment is refused outright — bytes we cannot " +
            "provably partition are not stored at all");

        blobs.Count.Should().Be(0, "nothing may be written when the name cannot be safely composed");
    }

    [Theory]
    [InlineData("../" + TenantA + "/session-files/" + SessionId + "/" + FileId)]
    [InlineData(TenantA + "/x")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Read_RejectsFileIdsThatCouldEscapeTheTenantPrefix(string craftedFileId)
    {
        var (store, blobs) = BuildStore();

        // Plant a real tenant-A blob so a successful escape would actually return something.
        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var attempt = async () => await store.ReadAsync(TenantB, SessionId, craftedFileId);

        await attempt.Should().ThrowAsync<ArgumentException>(
            "a crafted file id must be refused before it is concatenated into a blob name");

        blobs.Count.Should().Be(1, "the crafted read must not have disturbed tenant A's blob");
    }

    [Fact]
    public async Task Read_WithAnEmptyTenantId_IsRefused()
    {
        // Belt-and-braces on the segment that IS the boundary. A blank tenant would otherwise place
        // content at a shared root reachable by any other blank-tenant caller.
        var (store, _) = BuildStore();

        var read = async () => await store.ReadAsync("", SessionId, FileId);
        await read.Should().ThrowAsync<ArgumentException>();

        var write = async () => await store.WriteAsync("  ", SessionId, FileId, TenantASecret, "text/plain");
        await write.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Read_IsCaseSensitiveOnTheTenantSegment()
    {
        // Azure Blob names are case-sensitive. A tenant id differing only by case is a DIFFERENT
        // tenant as far as the store is concerned, and must not resolve to the same bytes.
        var (store, _) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var upperCased = await store.ReadAsync(TenantA.ToUpperInvariant(), SessionId, FileId);

        upperCased.Should().BeNull(
            "blob names are ordinal/case-sensitive; the store must not fold case and must not " +
            "accidentally serve one tenant's bytes to a differently-cased identifier");
    }
}
