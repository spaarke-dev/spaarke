using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice-seam KEEP category (ADR-038 §2).
/// spaarkeai-compose-r8 FR-B04 (task 062): a durable session file's lifetime follows its SESSION's
/// retention — 90-day container default for unfiled sessions, INDEFINITE for filed ones
/// (<see cref="StoredSession.Ttl"/> == <see cref="StoredSession.NeverExpireTtl"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What is real here and what is substituted.</b> The pass under test is the production
/// <see cref="SessionFileRetentionJob.RunPassAsync"/> driving the production
/// <see cref="SessionFileBlobStore"/> and the production
/// <see cref="SessionFileRetentionPolicy"/>. Two things are substituted, both at genuine external
/// boundaries: the blob service (<see cref="InMemorySessionFileBlobGateway"/>, which resolves names
/// the way Azure Blob does — opaque, ordinal, exact-match, prefix = ordinal StartsWith) and the Cosmos
/// probe (a delegate, which is exactly the shape production injects). Nothing about the decision, the
/// grouping, the sentinel handling or the delete is re-implemented by the test.
/// </para>
/// <para>
/// <b>The failure this suite exists to catch.</b> <c>-1</c> is the value a naive
/// <c>ttl &lt; elapsedSeconds</c> comparison reads as "expired 90 days ago", and the blast radius is
/// the permanent deletion of FILED matters' files. It is silent (nothing errors), delayed (day 91+)
/// and irreversible. <see cref="FiledSession_WithTheMinusOneSentinel_KeepsItsFilesThroughAnExpiryPass"/>
/// is the one test that would have caught it.
/// </para>
/// <para>
/// <b>Observed to fail before it passed.</b> Recorded in
/// <c>projects/spaarkeai-compose-r8/notes/track-b-retention-availability.md</c> §"Verification record".
/// </para>
/// </remarks>
public sealed class SessionFileRetentionSeamTests
{
    private const string TenantA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string TenantB = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string FiledSessionId = "f1111111-2222-3333-4444-555555555555";
    private const string UnfiledSessionId = "u1111111-2222-3333-4444-555555555555";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = SessionFileRetentionPolicy.DefaultRetentionWindow; // 90 days

    private readonly InMemorySessionFileBlobGateway _blobs = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Dictionary<(string Tenant, string Session), SessionRetentionProbe> _cosmos = new();
    private readonly List<(string Tenant, string Session)> _probeCalls = [];

    private SessionFileBlobStore Store => new(_blobs, NullLogger<SessionFileBlobStore>.Instance);

    private SessionFileRetentionJob BuildJob(bool dryRun = false, SessionFileBlobStore? store = null)
        => new(
            durableStore: store ?? Store,
            probeSessionRetention: (tenantId, sessionId, _) =>
            {
                _probeCalls.Add((tenantId, sessionId));
                return Task.FromResult(
                    _cosmos.TryGetValue((tenantId, sessionId), out var probe)
                        ? probe
                        : SessionRetentionProbe.Absent);
            },
            interval: TimeSpan.FromHours(24),
            dryRun: dryRun,
            retentionWindow: Window,
            timeProvider: _clock,
            logger: NullLogger<SessionFileRetentionJob>.Instance);

    /// <summary>Writes a durable copy stamped with an age, through the PRODUCTION store.</summary>
    private async Task WriteAgedAsync(string tenantId, string sessionId, string fileId, TimeSpan age)
    {
        _blobs.NextWriteCreatedOn = Now - age;
        var outcome = await Store.WriteAsync(
            tenantId, sessionId, fileId, BinaryData.FromString($"bytes for {fileId}"), "application/pdf");
        outcome.Should().Be(SessionFileStoreOutcome.Written, "the positive control must actually store something");
    }

    private bool Exists(string tenantId, string sessionId, string fileId)
        => _blobs.TryPeek(SessionFileBlobStore.BuildBlobName(tenantId, sessionId, fileId), out _);

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // THE headline case: a filed session's files are never expired.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FiledSession_WithTheMinusOneSentinel_KeepsItsFilesThroughAnExpiryPass()
    {
        // A filed matter, uploaded well over a year ago — the case where every age-based rule says
        // "delete" and the correct answer is "never".
        await WriteAgedAsync(TenantA, FiledSessionId, "filed-file-1", TimeSpan.FromDays(400));
        await WriteAgedAsync(TenantA, FiledSessionId, "filed-file-2", TimeSpan.FromDays(400));

        // Filed ⇒ ChatSessionManager persisted ttl = StoredSession.NeverExpireTtl (-1).
        _cosmos[(TenantA, FiledSessionId)] = SessionRetentionProbe.Found(StoredSession.NeverExpireTtl);

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(0);
        result.BlobsRetainedIndefinitely.Should().Be(2,
            "both files belong to a FILED session, whose Cosmos document never expires — so neither " +
            "may its durable bytes (FR-B04)");

        Exists(TenantA, FiledSessionId, "filed-file-1").Should().BeTrue();
        Exists(TenantA, FiledSessionId, "filed-file-2").Should().BeTrue();
        _blobs.DeletedBlobNames.Should().BeEmpty();
    }

    [Fact]
    public void TheSentinelIsCheckedBeforeAnyAgeArithmetic()
    {
        // Pins the ORDER, not just the outcome. A probe that says "absent" AND carries the sentinel is
        // not a state production produces — it is constructed here precisely so that if the sentinel
        // check ever moves below the state switch, this fails while every realistic test still passes.
        var contradictory = new SessionRetentionProbe(SessionRetentionState.Absent, StoredSession.NeverExpireTtl);

        SessionFileRetentionPolicy
            .Evaluate(contradictory, blobCreatedOn: Now - TimeSpan.FromDays(4000), now: Now)
            .Should().Be(SessionFileRetentionVerdict.RetainIndefinitely,
                "the -1 sentinel must short-circuit before any age comparison can run");
    }

    [Theory]
    [InlineData(-1)]   // the Cosmos sentinel itself
    [InlineData(0)]    // not valid in Cosmos; must never be read as "expires immediately"
    [InlineData(-90)]  // any other negative
    public void NonPositiveTtlIsAlwaysIndefinite(int ttl)
        => SessionFileRetentionPolicy.IsIndefiniteTtl(ttl).Should().BeTrue(
            "a non-positive ttl is never a legitimate short expiry; widening the sentinel can only KEEP " +
            "bytes, whereas narrowing it deletes filed matters' files");

    [Theory]
    [InlineData(null)]
    [InlineData(7776000)]
    [InlineData(1)]
    public void PositiveOrAbsentTtlIsNotIndefinite(int? ttl)
        => SessionFileRetentionPolicy.IsIndefiniteTtl(ttl).Should().BeFalse();

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // The unfiled path: files follow the session's own 90-day default.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnfiledSession_WhoseDocumentStillExists_KeepsItsFiles()
    {
        // Cosmos slides the 90-day TTL on every write, so an ACTIVE unfiled session can hold blobs far
        // older than the window. The document's existence is the retention signal, not the blob's age.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "live-file", TimeSpan.FromDays(200));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Found(ttl: null);

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(0);
        Exists(TenantA, UnfiledSessionId, "live-file").Should().BeTrue();
    }

    [Fact]
    public async Task UnfiledSession_WhoseDocumentHasExpired_LosesItsFiles()
    {
        // The positive control for the whole suite. Without it, every "retained" assertion above could
        // pass because the pass never deletes anything at all.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "expired-file", Window + TimeSpan.FromDays(1));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(1,
            "the session's own 90-day retention has ended (Cosmos reaped the document), so the durable " +
            "copy must follow it");
        Exists(TenantA, UnfiledSessionId, "expired-file").Should().BeFalse();
        _blobs.DeletedBlobNames.Should().ContainSingle()
            .Which.Should().Be(SessionFileBlobStore.BuildBlobName(TenantA, UnfiledSessionId, "expired-file"));
    }

    [Fact]
    public async Task ManifestExpiredButBytesPersist_IsReachedByPrefixEnumeration_NotByTheManifest()
    {
        // The case that shapes the whole design: the Cosmos manifest is GONE, so nothing in Cosmos names
        // these blobs or even names the tenant that owns them. A manifest-driven sweep is structurally
        // blind to them; the prefix enumeration is not. Three sessions, no manifest anywhere.
        await WriteAgedAsync(TenantA, "orphan01-2222-3333-4444-555555555555", "orphan-a", Window + TimeSpan.FromDays(5));
        await WriteAgedAsync(TenantA, "orphan02-2222-3333-4444-555555555555", "orphan-b", Window + TimeSpan.FromDays(5));
        await WriteAgedAsync(TenantB, "orphan03-2222-3333-4444-555555555555", "orphan-c", Window + TimeSpan.FromDays(5));
        // _cosmos is empty ⇒ every probe answers Absent, which is what an expired/never-written
        // manifest actually looks like.

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsExamined.Should().Be(3);
        result.BlobsDeleted.Should().Be(3, "orphaned bytes are unreferenced forever unless a prefix sweep reaches them");
        _blobs.Count.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Fail-closed: everything unknown, young, or unanswerable is RETAINED.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlobYoungerThanTheWindow_IsRetainedEvenWhenTheSessionIsAbsent()
    {
        // The durable write lands BEFORE the manifest write, and the manifest write is non-fatal. So a
        // just-uploaded blob can legitimately have no session document yet. Deleting it would destroy a
        // file the user uploaded seconds ago.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "just-uploaded", TimeSpan.FromMinutes(2));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(0);
        Exists(TenantA, UnfiledSessionId, "just-uploaded").Should().BeTrue();
    }

    [Fact]
    public async Task IndeterminateProbe_RetainsEverything()
    {
        // A Cosmos outage must not present as "every session expired". This is the difference between
        // ProbeSessionRetentionAsync and LoadSessionAsync, and it is why retention does not use the latter.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "file-during-outage", Window + TimeSpan.FromDays(30));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Indeterminate;

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(0);
        result.BlobsRetainedIndeterminate.Should().Be(1);
        Exists(TenantA, UnfiledSessionId, "file-during-outage").Should().BeTrue();
    }

    [Fact]
    public async Task ProbeThatThrows_IsTreatedAsIndeterminate_NotAsExpired()
    {
        await WriteAgedAsync(TenantA, UnfiledSessionId, "file-when-probe-throws", Window + TimeSpan.FromDays(30));

        var job = new SessionFileRetentionJob(
            durableStore: Store,
            probeSessionRetention: (_, _, _) => throw new InvalidOperationException("Cosmos is unreachable"),
            interval: TimeSpan.FromHours(24),
            dryRun: false,
            retentionWindow: Window,
            timeProvider: _clock,
            logger: NullLogger<SessionFileRetentionJob>.Instance);

        var result = await job.RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(0);
        Exists(TenantA, UnfiledSessionId, "file-when-probe-throws").Should().BeTrue();
    }

    [Fact]
    public async Task DryRun_EvaluatesButDeletesNothing()
    {
        await WriteAgedAsync(TenantA, UnfiledSessionId, "would-be-deleted", Window + TimeSpan.FromDays(10));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;

        var result = await BuildJob(dryRun: true).RunPassAsync(CancellationToken.None);

        result.SessionsProbed.Should().Be(1, "dry run must still do the work — it is an observation mode, not a skip");
        result.BlobsDeleted.Should().Be(0);
        Exists(TenantA, UnfiledSessionId, "would-be-deleted").Should().BeTrue();
    }

    [Fact]
    public async Task DisabledStore_MakesThePassInert()
    {
        // The shipped state: SessionFileStore:BlobEndpoint is empty until task 063 merges.
        var disabled = new SessionFileBlobStore(gateway: null, NullLogger<SessionFileBlobStore>.Instance);
        disabled.IsEnabled.Should().BeFalse();

        // Bytes exist in the gateway, but a disabled store cannot see or touch them.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "untouchable", Window + TimeSpan.FromDays(10));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;

        var result = await BuildJob(store: disabled).RunPassAsync(CancellationToken.None);

        result.Should().Be(SessionFileRetentionPassResult.Empty);
        _probeCalls.Should().BeEmpty("a disabled store must not even reach Cosmos");
        Exists(TenantA, UnfiledSessionId, "untouchable").Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Tenant isolation on the retention path (ADR-014 / ADR-015).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpiringOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone()
    {
        // Same sessionId and same fileId in two tenants — the realistic collision, since ids travel in
        // URLs, manifests and telemetry. Tenant A's session has expired; tenant B's is FILED.
        const string sharedSession = "5haredaa-2222-3333-4444-555555555555";
        const string sharedFile = "5haredff-2222-3333-4444-555555555555";

        await WriteAgedAsync(TenantA, sharedSession, sharedFile, Window + TimeSpan.FromDays(3));
        await WriteAgedAsync(TenantB, sharedSession, sharedFile, Window + TimeSpan.FromDays(3));

        _cosmos[(TenantA, sharedSession)] = SessionRetentionProbe.Absent;
        _cosmos[(TenantB, sharedSession)] = SessionRetentionProbe.Found(StoredSession.NeverExpireTtl);

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsDeleted.Should().Be(1);
        Exists(TenantA, sharedSession, sharedFile).Should().BeFalse("tenant A's session retention ended");
        Exists(TenantB, sharedSession, sharedFile).Should().BeTrue(
            "tenant B's session is FILED — the identical session and file ids must not make it collateral");

        // The probe was asked separately per tenant: a single tenant-blind probe would be the defect.
        _probeCalls.Should().Contain((TenantA, sharedSession));
        _probeCalls.Should().Contain((TenantB, sharedSession));
    }

    [Fact]
    public async Task DeleteFromAnotherTenant_UsingTheSameIds_DestroysNothing()
    {
        // Direct check on the shared primitive task 063 will reuse for erasure.
        await WriteAgedAsync(TenantA, UnfiledSessionId, "a-file", TimeSpan.FromDays(1));

        var deleted = await Store.DeleteAsync(TenantB, UnfiledSessionId, "a-file");

        deleted.Should().BeFalse("knowing another tenant's session and file ids is not a capability to delete");
        Exists(TenantA, UnfiledSessionId, "a-file").Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Cost control + the shared enumeration primitive.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionsWhoseFilesAreAllYoung_AreNotProbedAtAll()
    {
        await WriteAgedAsync(TenantA, UnfiledSessionId, "young-1", TimeSpan.FromDays(3));
        await WriteAgedAsync(TenantA, UnfiledSessionId, "young-2", TimeSpan.FromDays(3));
        await WriteAgedAsync(TenantA, FiledSessionId, "old-1", Window + TimeSpan.FromDays(1));
        _cosmos[(TenantA, FiledSessionId)] = SessionRetentionProbe.Found(StoredSession.NeverExpireTtl);

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.BlobsExamined.Should().Be(3);
        result.SessionsProbed.Should().Be(1, "a session with no blob past the window cannot produce a deletable verdict");
        _probeCalls.Should().ContainSingle().Which.Should().Be((TenantA, FiledSessionId));
    }

    [Fact]
    public async Task TenantScopedListing_SeesOnlyTheCallingTenantsSessionFiles()
    {
        await WriteAgedAsync(TenantA, UnfiledSessionId, "a-1", TimeSpan.FromDays(1));
        await WriteAgedAsync(TenantA, FiledSessionId, "a-2", TimeSpan.FromDays(1));
        await WriteAgedAsync(TenantB, UnfiledSessionId, "b-1", TimeSpan.FromDays(1));

        var forA = new List<SessionFileBlobRef>();
        await foreach (var blob in Store.ListAsync(TenantA))
        {
            forA.Add(blob);
        }

        forA.Should().HaveCount(2);
        forA.Should().OnlyContain(b => b.TenantId == TenantA);

        var forASession = new List<SessionFileBlobRef>();
        await foreach (var blob in Store.ListAsync(TenantA, UnfiledSessionId))
        {
            forASession.Add(blob);
        }

        forASession.Should().ContainSingle().Which.FileId.Should().Be("a-1");
    }

    [Fact]
    public async Task ForeignContentInTheSharedContainer_IsInvisibleToEveryEnumeration()
    {
        // ai-chunks is a SHARED container. A listing-driven delete must be incapable of acting on
        // anything whose name the write path could not have produced.
        _blobs.Seed("some-other-feature/blob.json", BinaryData.FromString("not ours"));
        _blobs.Seed($"{TenantA}/other-prefix/{UnfiledSessionId}/x", BinaryData.FromString("not ours either"));
        _blobs.Seed($"{TenantA}/session-files/{UnfiledSessionId}/too/many/segments", BinaryData.FromString("nope"));
        await WriteAgedAsync(TenantA, UnfiledSessionId, "ours", Window + TimeSpan.FromDays(1));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;

        var all = new List<SessionFileBlobRef>();
        await foreach (var blob in Store.ListAllForRetentionAsync())
        {
            all.Add(blob);
        }

        all.Should().ContainSingle().Which.FileId.Should().Be("ours");

        var result = await BuildJob().RunPassAsync(CancellationToken.None);
        result.BlobsDeleted.Should().Be(1);
        _blobs.Count.Should().Be(3, "the three foreign blobs must survive a retention pass untouched");
    }

    [Fact]
    public async Task DeleteFailure_RetainsTheBlobAndReportsIt_RatherThanFailingThePass()
    {
        await WriteAgedAsync(TenantA, UnfiledSessionId, "undeletable", Window + TimeSpan.FromDays(1));
        _cosmos[(TenantA, UnfiledSessionId)] = SessionRetentionProbe.Absent;
        _blobs.FailNextDelete = true;

        var result = await BuildJob().RunPassAsync(CancellationToken.None);

        result.DeleteFailures.Should().Be(1);
        result.BlobsDeleted.Should().Be(0);
        Exists(TenantA, UnfiledSessionId, "undeletable").Should().BeTrue("a failed delete retains, and retries next pass");
    }
}
