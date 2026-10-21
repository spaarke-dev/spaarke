// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 review round 1, slice 2 — the F3 release of an S1-to-S2 move: the ONE way an isolated
//     document's file leaves secure record S1's own container for S2's. A BFF re-file with F3 records it
//     (SecureChildReconciler.AfterRefileAsync → DocumentContainerRelocator.RecordReleaseAsync, under the document's lock);
//     the widening guard honours it only on an exact match (this item, into this container, unexpired) and the re-point
//     consumes it. A missing, unreadable, stale or mismatched release holds the move. Each pins a write contract.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver and relocator over task 166's
// document-pointer world, and the REAL SecureChildReconciler over task 148's world.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Ledger = Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationLedger;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using Release = Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationRelease;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocationReleaseTests
{
    private const string Item = R.Item;
    private const string LedgerColumn = DocumentContainerRelocator.RelocationLedgerColumn;

    /// <summary>Who re-filed the document with F3.</summary>
    private static readonly Guid Caller = Guid.Parse("f3f3f3f3-0000-4000-8000-000000002159");

    /// <summary>S1 — the secure matter whose own container holds the file.</summary>
    private static readonly RecordOwnershipParent S1 = new("sprk_matter", T.SecureMatter);

    /// <summary>An isolated document filed under S2, its file (BFF-bound and BFF-uploaded) still in S1's own container.</summary>
    private static (World World, T.Rig Rig) RefiledToS2()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer);
        R.Own(world, T.DocumentId, isolated: true);
        return (world, new T.Rig(world));
    }

    private static SecureExitPermit Permit(params RecordOwnershipParent[] left)
        => new(left, new SecureRemovalDecision(SecureRemovalOutcome.Permitted, SecureRemovalBasis.FullAccess, Caller));

    /// <summary>A written re-file's answer that carries <paramref name="permit"/> (null = it left no secure root).</summary>
    private static RecordOwnerResolution Reparent(SecureExitPermit? permit)
        => new(RecordOwnerOutcome.Owned, SecureChildShareWorld.SecureTeam, null, null) { SecureExitPermitted = permit };

    private static Release AReleaseFor(
        string item = Item, string target = R.SecondSecureContainer, DateTimeOffset? at = null) => new()
        {
            SourceDrive = T.SecureContainer,
            SourceItem = item,
            TargetContainer = target,
            SourceRecordTable = S1.EntityLogicalName,
            SourceRecordId = S1.RecordId,
            AuthorizedBy = Caller,
            Basis = nameof(SecureRemovalBasis.FullAccess),
            At = at ?? DateTimeOffset.UtcNow,
        };

    private static void Store(World world, Release release)
        => world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = (Ledger.EmptyLedger with { Release = release }).Serialize();

    private static Ledger? LedgerOf(World world)
        => Ledger.Parse(world.Rows[("sprk_document", T.DocumentId)].GetAttributeValue<string>(LedgerColumn));

    private static Task<DocumentRelocationOutcome> RelocateAsync(T.Rig rig, RelocationPurpose purpose, bool apply = true)
        => rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply, purpose);

    private static void NoFileStep(T.Rig rig)
    {
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted");
        rig.Copies.Should().BeEmpty();
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "S1's bytes stay in S1's container");
    }

    /// <summary>The REAL reconciler over task 148's world, holding the rig's relocator (the release writer).</summary>
    private static SecureChildReconciler Reconciler(T.Rig rig)
    {
        var shareWorld = SecureChildShareWorld.Standard();
        shareWorld.Add("sprk_document", T.DocumentId, ("owningteam", new EntityReference("team", shareWorld.OwnerTeam)));
        return SecureChildShareWorld.ReconcilerOver(() => shareWorld, new FakeRecordShareTable(), null!, relocator: rig.Relocator);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The move — held without a release; moved with an exact one
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "slice 2 must not fire: an S1-to-S2 edit made OUTSIDE the BFF (no release) is held, whatever the purpose")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task OutsideTheBff_S1ToS2_IsHeld(RelocationPurpose purpose)
    {
        var (world, rig) = RefiledToS2();

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        outcome.Detail.Should().Contain("no F3 re-file released this move");
        world.Updates.Should().BeEmpty();
        NoFileStep(rig);
    }

    [Theory(DisplayName = "slice 2 must fire: a BFF re-file with F3 records the release, and the next relocation moves S1's file into S2 and consumes it")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.MakeSecure)]
    public async Task ABffRefileWithF3_Moves(RelocationPurpose purpose)
    {
        var (world, rig) = RefiledToS2();

        await Reconciler(rig).AfterRefileAsync(
            "sprk_document", T.DocumentId, () => Task.FromResult(true), Reparent(Permit(S1)), CancellationToken.None);

        var recorded = LedgerOf(world)!.Release;
        recorded.Should().NotBeNull("the re-file recorded the release of this move");
        recorded!.SourceDrive.Should().Be(T.SecureContainer);
        recorded.SourceItem.Should().Be(Item);
        recorded.TargetContainer.Should().Be(R.SecondSecureContainer);
        recorded.SourceRecordId.Should().Be(T.SecureMatter);
        recorded.AuthorizedBy.Should().Be(Caller);
        rig.Locks.Held.Should().BeEmpty("it was written under the document's lock, then released");

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
        LedgerOf(world)!.Release.Should().BeNull("the re-point consumed it");
    }

    [Fact(DisplayName = "slice 2: the provisioning lane with S2 as its expected target moves a released file too")]
    public async Task TheProvisioningLane_MovesAReleasedFile()
    {
        var (_, rig) = RefiledToS2();
        (await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1))).State.Should().Be(RelocationReleaseState.Recorded);

        var batch = await rig.Relocator.RelocateDocumentsAsync(
            [T.DocumentId], R.SecondSecureContainer, RelocationPurpose.MakeSecure, apply: true);

        batch.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Relocated);
    }

    [Fact(DisplayName = "slice 2: report-only plans the released move and writes nothing")]
    public async Task ReportOnly_PlansTheReleasedMove()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor());

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile, apply: false);

        outcome.State.Should().Be(RelocationState.WouldRelocate, outcome.Detail);
        world.Updates.Should().BeEmpty();
        LedgerOf(world)!.Release.Should().NotBeNull("report-only consumes nothing");
    }

    [Fact(DisplayName = "slice 2 must not fire: a release for a DIFFERENT target holds the move (and is kept: it is still current)")]
    public async Task AReleaseForADifferentTarget_IsHeld()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(target: T.CustomerAContainer));

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        outcome.Detail.Should().Contain("for another move, or has expired");
        world.Updates.Should().BeEmpty();
        NoFileStep(rig);
    }

    [Fact(DisplayName = "slice 2 must not fire: a release for ANOTHER item holds the move, and the locked pass drops it (spent)")]
    public async Task AReleaseForAnotherItem_IsHeld_AndDropped()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(item: "01ANOTHERITEM"));

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        world.Updates.Should().ContainSingle().Which.Fields.Should().ContainKey(LedgerColumn)
            .WhoseValue.Should().Be(DBNull.Value, "a spent release, and nothing else owed: the column is cleared");
        NoFileStep(rig);
    }

    [Fact(DisplayName = "slice 2 must not fire: an EXPIRED release holds the move, and the locked pass drops it")]
    public async Task AnExpiredRelease_IsHeld_AndDropped()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(at: DateTimeOffset.UtcNow - DocumentContainerRelocator.ReleaseLifetime - TimeSpan.FromMinutes(1)));

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        LedgerOf(world)!.Release.Should().BeNull("dropped under the lock");
        NoFileStep(rig);
    }

    [Fact(DisplayName = "slice 2: a release inside its lifetime still moves (the lifetime boundary)")]
    public async Task AReleaseInsideItsLifetime_Moves()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(at: DateTimeOffset.UtcNow - DocumentContainerRelocator.ReleaseLifetime + TimeSpan.FromMinutes(5)));

        (await RelocateAsync(rig, RelocationPurpose.Refile)).State.Should().Be(RelocationState.Relocated);
    }

    [Fact(DisplayName = "slice 2 must not fire: a release dated in the FUTURE (beyond clock skew) holds the move")]
    public async Task AFutureRelease_IsHeld()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(at: DateTimeOffset.UtcNow + TimeSpan.FromHours(1)));

        (await RelocateAsync(rig, RelocationPurpose.Refile)).State.Should().Be(RelocationState.HeldIsolated);
        NoFileStep(rig);
    }

    [Theory(DisplayName = "slice 2 must not fire: an UNREADABLE release (a malformed entry, or a ledger that is not one) holds the move and writes nothing")]
    [InlineData("{\"v\":1,\"entries\":[],\"release\":{\"sourceDrive\":\"b!secure-matter-own-container\",\"sourceItem\":\"x\"}}")]
    [InlineData("not a ledger")]
    [InlineData("{\"v\":1,\"entries\":[],\"release\":{\"sourceDrive\":\"b!secure-matter-own-container\",\"sourceItem\":\"01ITEM\",\"targetContainer\":\" \",\"sourceRecordTable\":\"sprk_matter\"}}")]
    public async Task AnUnreadableRelease_IsHeld(string stored)
    {
        var (world, rig) = RefiledToS2();
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = stored;

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        outcome.Detail.Should().Contain("unreadable");
        world.Updates.Should().BeEmpty();
        NoFileStep(rig);
    }

    [Fact(DisplayName = "slice 2 must not fire: report-only (no drop) still holds a release for ANOTHER item — the match is on the item, not the drive")]
    public async Task ReportOnly_AReleaseForAnotherItem_IsHeld()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor(item: "01ANOTHERITEM"));

        (await RelocateAsync(rig, RelocationPurpose.Refile, apply: false)).State.Should().Be(RelocationState.HeldIsolated);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "slice 2 must not fire: a release that cannot be READ (Dataverse unavailable) moves nothing")]
    public async Task AReleaseReadFault_MovesNothing()
    {
        var (world, rig) = RefiledToS2();
        Store(world, AReleaseFor());
        world.RetrieveFault = (table, columns) => table == "sprk_document" && columns is [LedgerColumn]
            ? new TimeoutException("Dataverse unavailable")
            : null;

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("released its move");
        world.Updates.Should().BeEmpty();
        NoFileStep(rig);
    }

    [Fact(DisplayName = "slice 2: adding a ledger entry keeps the release (only the re-point consumes it)")]
    public void AddingAnEntry_KeepsTheRelease()
    {
        var release = AReleaseFor();
        var ledger = new Ledger([]) { Release = release }
            .With(new DocumentContainerRelocator.RelocationLedgerEntry { SourceDrive = "b!d", SourceItem = "01X" });

        ledger.Release.Should().Be(release);
        Ledger.Parse(ledger.Serialize())!.Release.Should().Be(release, "it survives the round trip");
    }

    [Fact(DisplayName = "slice 2: the ledger settle keeps the release while it writes back what an earlier move owed")]
    public async Task TheSettle_KeepsTheRelease()
    {
        var (world, rig) = RefiledToS2();
        var owed = new DocumentContainerRelocator.RelocationLedgerEntry
        {
            SourceDrive = "b!an-earlier-container",
            SourceItem = "01EARLIER",
            Source = DocumentContainerRelocator.LedgerSourceState.Removed,
            Indexed = DocumentContainerRelocator.LedgerIndexScope.Own, // owes the full re-key of a removed source: settled now
        };
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] =
            (new Ledger([owed]) { Release = AReleaseFor() }).Serialize();

        var outcome = await RelocateAsync(rig, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        world.Updates.First().Fields[LedgerColumn].Should().BeOfType<string>("the settle's write-back keeps the release");
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
        LedgerOf(world)!.Release.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Recording the release — the one ledger writer, under the lock, from what it reads itself
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "slice 2 must not fire: a permit for a DIFFERENT secure record than the one holding the file records nothing")]
    public async Task APermitThatDidNotLeaveTheFilesRecord_RecordsNothing()
    {
        var (world, rig) = RefiledToS2();

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(new RecordOwnershipParent("sprk_matter", Guid.NewGuid())));

        outcome.State.Should().Be(RelocationReleaseState.NotCovered, outcome.Detail);
        world.Updates.Should().BeEmpty();
        (await RelocateAsync(rig, RelocationPurpose.Refile)).State.Should().Be(RelocationState.HeldIsolated);
    }

    [Theory(DisplayName = "slice 2: no release is needed (and none written) when the file does not leave one secure record's container for another's")]
    [InlineData("bu")]       // the file is in a business unit's container: into S2 narrows
    [InlineData("plain")]    // the document derives to no secure record's container
    [InlineData("inplace")]  // the file is already in S2's container
    public async Task NoReleaseNeeded_WritesNothing(string shape)
    {
        var world = R.Env();
        var (matter, drive) = shape switch
        {
            "bu" => (R.SecondSecureMatter, T.CustomerAContainer),
            "plain" => (T.PlainMatter, T.SecureContainer),
            _ => (R.SecondSecureMatter, R.SecondSecureContainer),
        };
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, matter, drive);
        R.Own(world, T.DocumentId, isolated: true);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.NotNeeded, outcome.Detail);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "slice 2: a lock another relocation HOLDS past the wait records nothing (the re-file stands; the file stays held)")]
    public async Task AHeldLock_RecordsNothing()
    {
        var (world, rig) = RefiledToS2();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));
        var started = DateTimeOffset.UtcNow;

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.LockHeld, outcome.Detail);
        (DateTimeOffset.UtcNow - started).Should().BeGreaterThanOrEqualTo(DocumentContainerRelocator.ReleaseLockWait - TimeSpan.FromMilliseconds(50));
        world.Updates.Should().BeEmpty("never written outside the lock");
    }

    [Fact(DisplayName = "slice 2: a lock freed within the wait is taken, and the release is recorded")]
    public async Task ALockFreedWithinTheWait_Records()
    {
        var (world, rig) = RefiledToS2();
        var key = DocumentContainerRelocator.RelocationLockKey(T.DocumentId);
        rig.Locks.Held.Add(key);
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            rig.Locks.Held.Remove(key);
        });

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.Recorded, outcome.Detail);
        LedgerOf(world)!.Release.Should().NotBeNull();
    }

    [Fact(DisplayName = "slice 2: an UNREADABLE ledger is never overwritten by a release")]
    public async Task AnUnreadableLedger_IsNotOverwritten()
    {
        var (world, rig) = RefiledToS2();
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = "not a ledger";

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.Failed, outcome.Detail);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "slice 2: recording a release keeps every entry an earlier move still owes")]
    public async Task Recording_KeepsWhatTheLedgerOwes()
    {
        var (world, rig) = RefiledToS2();
        var owed = new DocumentContainerRelocator.RelocationLedgerEntry { SourceDrive = "b!earlier", SourceItem = "01EARLIER", RekeyPending = true };
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = new Ledger([owed]).Serialize();

        (await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1))).State.Should().Be(RelocationReleaseState.Recorded);

        var ledger = LedgerOf(world)!;
        ledger.Entries.Should().ContainSingle().Which.SourceItem.Should().Be("01EARLIER");
        ledger.Release.Should().NotBeNull();
    }

    [Fact(DisplayName = "slice 2: a Dataverse fault while recording is Failed, never thrown, and writes nothing")]
    public async Task AFaultWhileRecording_IsFailed()
    {
        var (world, rig) = RefiledToS2();
        world.UpdateFault = new TimeoutException("Dataverse unavailable");

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.Failed, outcome.Detail);
        rig.Locks.Held.Should().BeEmpty("the lock is released whatever happened");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The write site — SecureChildReconciler.AfterRefileAsync
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "slice 2 must not fire: a re-file that left no secure root with F3 (no permit) records no release")]
    public async Task AfterRefile_WithoutAPermit_RecordsNothing()
    {
        var (world, rig) = RefiledToS2();

        await Reconciler(rig).AfterRefileAsync(
            "sprk_document", T.DocumentId, () => Task.FromResult(true), Reparent(permit: null), CancellationToken.None);
        await Reconciler(rig).AfterRefileAsync("sprk_document", T.DocumentId, () => Task.FromResult(true), CancellationToken.None);

        world.Updates.Should().BeEmpty();
        (await RelocateAsync(rig, RelocationPurpose.Refile)).State.Should().Be(RelocationState.HeldIsolated);
    }

    [Fact(DisplayName = "slice 2 must not fire: a permit on a re-filed row that is NOT a document records no release")]
    public async Task AfterRefile_OfAnotherTable_RecordsNothing()
    {
        var (world, rig) = RefiledToS2();

        await Reconciler(rig).AfterRefileAsync(
            "sprk_event", T.DocumentId, () => Task.FromResult(true), Reparent(Permit(S1)), CancellationToken.None);

        world.Updates.Should().BeEmpty();
    }
}
