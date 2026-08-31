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
/// <b>Why these are reachability tests, not string tests.</b> Every isolation claim here is settled by
/// an actual read: bytes are written through the production store as one tenant, then read back through
/// the production store as another, against <see cref="InMemorySessionFileBlobGateway"/> — which
/// resolves blob names the way Azure Blob does: opaque, ordinal, exact-match, no path semantics.
/// Whether the read hits or misses is decided entirely by the name
/// <see cref="SessionFileBlobStore.BuildBlobName"/> produced. Two tests
/// (<see cref="Write_PlacesTheBlobUnderTheCallingTenantsPrefix"/> and
/// <see cref="BuildBlobName_PutsTheTenantFirst_SoTheAssertTripwireCannotBeSilentlyRemoved"/>) DO assert
/// on the name — deliberately, because "the tenant is the first segment" is a structural property that
/// a reachability test cannot express, and it is what makes every later prefix-scoped operation
/// tenant-safe by construction.
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

    [Fact]
    public void BuildBlobName_PutsTheTenantFirst_SoTheAssertTripwireCannotBeSilentlyRemoved()
    {
        // `SessionFileBlobStore.AssertTenantPartitioned` is unreachable in normal operation — it exists
        // as a tripwire for a FUTURE edit to BuildBlobName that drops or reorders the tenant segment.
        // An untested tripwire can itself be deleted silently, so this pins the property the tripwire
        // guards, directly and independently of any read.
        var name = SessionFileBlobStore.BuildBlobName(TenantA, SessionId, FileId);

        name.Should().Be($"{TenantA}/session-files/{SessionId}/{FileId}");
        name.Should().StartWith(TenantA + "/");
        name.Should().NotContain("..");
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
    // Azure advises against blob names whose segments end in '.'; some clients and intermediaries
    // normalise them away, which would silently change which blob a name addresses.
    [InlineData("11111111-2222-3333-4444-555555555555.")]
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

    // ─────────────────────────────────────────────────────────────────────────
    // spaarkeai-compose-r8 task 062 (FR-B04) — the ENUMERATION + DELETE surface retention adds and
    // task 063's erasure will reuse. It carries a strictly larger blast radius than read/write: a
    // listing that crosses the boundary leaks the EXISTENCE and SIZE of another tenant's files even
    // when the bytes stay unreadable, and a delete that crosses it destroys them.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyTheCallingTenantsBlobs()
    {
        var (store, blobs) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");
        await store.WriteAsync(TenantB, SessionId, FileId, BinaryData.FromString("tenant B content"), "text/plain");

        var listedForB = new List<SessionFileBlobRef>();
        await foreach (var blob in store.ListAsync(TenantB))
        {
            listedForB.Add(blob);
        }

        listedForB.Should().ContainSingle("tenant B has exactly one durable copy");
        listedForB[0].TenantId.Should().Be(TenantB);
        listedForB.Should().NotContain(b => b.TenantId == TenantA,
            "a listing must not disclose even the EXISTENCE of another tenant's session files");

        // Positive control: both blobs are physically present, so the single result above is a
        // partitioning outcome and not an empty store.
        blobs.Count.Should().Be(2);
    }

    [Fact]
    public async Task List_ScopedToASession_DoesNotReachAnotherTenantsIdenticallyNamedSession()
    {
        var (store, _) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var listedForB = new List<SessionFileBlobRef>();
        await foreach (var blob in store.ListAsync(TenantB, SessionId))
        {
            listedForB.Add(blob);
        }

        listedForB.Should().BeEmpty(
            "knowing another tenant's session id must not enumerate that session's files — it is an " +
            "identifier, not a capability");
    }

    [Fact]
    public async Task Delete_FromAnotherTenant_WithTheSameIds_DestroysNothing()
    {
        // The highest-consequence version of the identifiers-are-not-authority rule: a cross-tenant
        // read leaks, a cross-tenant DELETE is unrecoverable data loss.
        var (store, blobs) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var deleted = await store.DeleteAsync(TenantB, SessionId, FileId);

        deleted.Should().BeFalse();
        blobs.Count.Should().Be(1);
        blobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, SessionId, FileId), out var stillThere)
            .Should().BeTrue("tenant A's bytes must survive a cross-tenant delete attempt");
        stillThere!.ToString().Should().Be(TenantASecret.ToString());

        // Positive control: the owning tenant CAN delete it, so the BeFalse above is partitioning and
        // not a store that simply never deletes anything.
        (await store.DeleteAsync(TenantA, SessionId, FileId)).Should().BeTrue();
        blobs.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../other-tenant")]
    [InlineData("tenant/with-slash")]
    public async Task List_WithAnUnsafeTenantSegment_IsRefusedBeforeAnyEnumeration(string craftedTenantId)
    {
        var (store, _) = BuildStore();
        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        var attempt = async () =>
        {
            await foreach (var _ in store.ListAsync(craftedTenantId))
            {
                // The throw must happen while composing the prefix, not part-way through results.
            }
        };

        await attempt.Should().ThrowAsync<ArgumentException>(
            "a crafted tenant segment must be refused before it becomes a listing prefix");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // spaarkeai-compose-r8 task 063 (FR-B06) — the ERASURE composition. The two primitives above are
    // individually tenant-safe; this pins that COMPOSING them stays tenant-safe, because that is what
    // GDPR erasure actually calls and it is the one operation whose blast radius is permanent.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Erasure_RequestedByAnotherTenant_EnumeratesNothingAndDestroysNothing()
    {
        var (store, blobs) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");

        // Tenant B knows tenant A's session id exactly and asks for that session to be erased.
        var crossTenant = await SessionFileEraser.EraseSessionFilesAsync(
            store, TenantB, SessionId, NullLogger<SessionFileBlobStore>.Instance);

        crossTenant.BlobsDeleted.Should().Be(0);
        crossTenant.State.Should().Be(SessionFileErasureState.Erased,
            "from tenant B the prefix really is empty — another tenant's session is not merely " +
            "un-erasable, it is invisible, which is the same answer a non-existent session gives");
        blobs.Count.Should().Be(1, "tenant A's bytes must survive a cross-tenant erasure request");
        blobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, SessionId, FileId), out var survived)
            .Should().BeTrue();
        survived!.ToString().Should().Be(TenantASecret.ToString());

        // POSITIVE CONTROL: the owning tenant CAN erase it. Without this the assertions above would
        // pass just as well against an eraser that never deletes anything at all.
        var owning = await SessionFileEraser.EraseSessionFilesAsync(
            store, TenantA, SessionId, NullLogger<SessionFileBlobStore>.Instance);

        owning.BlobsDeleted.Should().Be(1);
        owning.State.Should().Be(SessionFileErasureState.Erased);
        blobs.Count.Should().Be(0);
    }

    [Fact]
    public async Task Erasure_DeletesOnlyWithinTheCallingTenantsPrefix_EvenWhenTwoTenantsShareEveryIdentifier()
    {
        var (store, blobs) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");
        await store.WriteAsync(TenantB, SessionId, FileId, BinaryData.FromString("tenant B content"), "text/plain");

        var erasure = await SessionFileEraser.EraseSessionFilesAsync(
            store, TenantA, SessionId, NullLogger<SessionFileBlobStore>.Instance);

        erasure.BlobsDeleted.Should().Be(1);
        blobs.DeletedBlobNames.Should().ContainSingle()
            .Which.Should().StartWith(TenantA + "/",
                "every delete an erasure issues is composed from the CALLING tenant, so even a listing " +
                "that widened could not redirect one across the boundary");
        blobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantB, SessionId, FileId), out _)
            .Should().BeTrue("tenant B's identically-identified file is a different blob entirely");
    }

    [Fact]
    public async Task ParsedListings_AlwaysAttributeABlobToTheTenantItIsPhysicallyStoredUnder()
    {
        // The property the SYSTEM-scope retention enumeration depends on: it has no caller tenant to
        // scope by, so correctness rests entirely on the tenant being recovered from the name itself.
        var (store, _) = BuildStore();

        await store.WriteAsync(TenantA, SessionId, FileId, TenantASecret, "application/pdf");
        await store.WriteAsync(TenantB, SessionId, FileId, BinaryData.FromString("tenant B content"), "text/plain");

        var all = new List<SessionFileBlobRef>();
        await foreach (var blob in store.ListAllForRetentionAsync())
        {
            all.Add(blob);
        }

        all.Should().HaveCount(2);
        all.Should().OnlyContain(b => b.BlobName.StartsWith(b.TenantId + "/", StringComparison.Ordinal),
            "every row's TenantId must be the first segment of its own blob name — that is what makes " +
            "the downstream Cosmos probe and delete tenant-correct without a caller tenant to trust");
    }
}
