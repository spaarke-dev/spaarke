// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 final adversarial pass, fix round — five write contracts of the relocator:
//     F1 a document checked out (or re-pointed) while its file is copied is never re-pointed, and its source never
//        deleted — on the move AND on the re-copy of an edited source;
//     F2 a re-copy WITHIN the source container keeps (re-binds) the row's F3 release, so the released S1-to-S2 move runs;
//     F3 a release a re-file could not record inline is retried durably (ADR-004 job), bound to the move the re-file
//        permitted, and never records what the inline path would refuse;
//     F4 a Make Secure move OUT of another secure record's own container needs the source proven the row's own;
//     F5 a move cancelled after its copy exists removes the copy (never one the row may already name); a re-point that
//        was SENT and did not answer (cancelled, timed out, a transport fault) keeps the copy whatever a re-read says — it
//        may still land (PR #2161 re-check); only a DEFINITE refusal (a Dataverse fault, a concurrency rejection) removes it.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver and relocator over task 166's
// document-pointer world; Service Bus is the boundary of the job queue.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Jobs;
using Spaarke.Dataverse;
using Xunit;
using Ledger = Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationLedger;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using Release = Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationRelease;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocationFixRoundTests
{
    private const string Item = R.Item;
    private const string LedgerColumn = DocumentContainerRelocator.RelocationLedgerColumn;

    /// <summary>Who re-filed the document with F3.</summary>
    private static readonly Guid Caller = Guid.Parse("f3f3f3f3-0000-4000-8000-00000000f1f1");

    /// <summary>S1 — the secure matter whose own container holds the file.</summary>
    private static readonly RecordOwnershipParent S1 = new("sprk_matter", T.SecureMatter);

    /// <summary>S2 — the secure matter the re-file filed the document under (the permit's post-change root).</summary>
    private static readonly RecordOwnershipParent S2 = new("sprk_matter", R.SecondSecureMatter);

    private static SecureExitPermit Permit(params RecordOwnershipParent[] left)
        => new(left, new SecureRemovalDecision(SecureRemovalOutcome.Permitted, SecureRemovalBasis.FullAccess, Caller), [S2]);

    /// <summary>The document filed under <paramref name="matter"/>, its file at <paramref name="drive"/>, owned as told.</summary>
    private static (World World, T.Rig Rig) Placed(Guid matter, string drive, bool isolated, string? bound = Item)
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, matter, drive, bound);
        R.Own(world, T.DocumentId, isolated);
        return (world, new T.Rig(world));
    }

    /// <summary>An isolated document filed under S2, its file (BFF-bound and BFF-uploaded) still in S1's own container.</summary>
    private static (World World, T.Rig Rig) RefiledToS2(string? bound = Item) => Placed(R.SecondSecureMatter, T.SecureContainer, true, bound);

    private static Release AReleaseFor(string item = Item, DateTimeOffset? at = null) => new()
    {
        SourceDrive = T.SecureContainer,
        SourceItem = item,
        TargetContainer = R.SecondSecureContainer,
        SourceRecordTable = S1.EntityLogicalName,
        SourceRecordId = S1.RecordId,
        AuthorizedBy = Caller,
        Basis = nameof(SecureRemovalBasis.FullAccess),
        At = at ?? DateTimeOffset.UtcNow,
    };

    private static Ledger? LedgerOf(World world)
        => Ledger.Parse(world.Rows[("sprk_document", T.DocumentId)].GetAttributeValue<string>(LedgerColumn));

    private static void CheckOut(World world, Guid id)
        => world.Rows[("sprk_document", id)][DocumentContainerRelocator.CheckedOutByColumn] =
            new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F1 — a checkout (or a re-point) that starts during the copy stops the move before its re-point
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "F1 must fire: a document checked out while its file is copied is held CheckedOut — no re-point, the copy removed, the source never deleted")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task F1_ACheckoutDuringTheCopy_IsHeld(RelocationPurpose purpose)
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        rig.BeforeDownload = (_, _) =>
        {
            CheckOut(world, T.DocumentId); // the user opens it in Office while the versions replay
            return Task.CompletedTask;
        };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.CheckedOut, outcome.Detail);
        world.Updates.Should().BeEmpty("the row is never re-pointed");
        rig.Steps.Should().Contain($"delete {T.CustomerA1Container}/{T.Rig.CopyItem}", "the copy is removed");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerAContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "the item the holder's Office session edits is never deleted");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerAContainer);
    }

    [Fact(DisplayName = "F1 must fire: a row re-pointed while its file is copied is not re-pointed again — the copy is removed and nothing is written")]
    public async Task F1_ARowRePointedDuringTheCopy_WritesNothing()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        rig.BeforeDownload = (_, _) =>
        {
            var row = world.Rows[("sprk_document", T.DocumentId)];
            row["sprk_graphitemid"] = row[R.Bound] = "01ANEWERFILE";
            return Task.CompletedTask;
        };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("its file changed while the file was copied");
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().Contain($"delete {T.CustomerA1Container}/{T.Rig.CopyItem}");
    }

    [Fact(DisplayName = "F1 must fire: a document checked out while an edited source is RE-COPIED is held CheckedOut — not re-pointed, the edited source kept")]
    public async Task F1_ACheckoutDuringTheReCopy_IsHeld()
    {
        // A Make Secure move whose source delete failed; then the OLD file is edited (round 45 item 4: the next call
        // re-copies). The user checks the document out while that re-copy replays.
        var world = T.Environment();
        world.Rows[("sprk_document", T.DocumentId)] = T.Doc(matter: T.SecureMatter, drive: T.CustomerAContainer, item: Item);
        var rig = new T.Rig(world) { DeleteFails = (drive, item) => drive == T.CustomerAContainer && item == Item };
        (await rig.Relocator.RelocateDocumentsAsync([T.DocumentId], T.SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeFalse();
        world.Items[(T.CustomerAContainer, Item)] = world.ItemFacts(T.CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(T.CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T.Rig.Written, 1234, T.Rig.CreatorName) { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("2.0", null, new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero), 2000, "Late Editor")
            {
                LastModifiedByUserId = T.Creator,
            },
        ];
        rig.DeleteFails = (_, _) => false;
        var writes = world.Updates.Count;
        rig.BeforeDownload = (_, _) =>
        {
            CheckOut(world, T.DocumentId);
            return Task.CompletedTask;
        };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.CheckedOut, outcome.Detail);
        world.Updates.Should().HaveCount(writes, "the re-copy never re-points a checked-out document");
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem);
        world.ItemFacts(T.CustomerAContainer, Item).Should().NotBeNull("the edited source is kept");
        rig.Copies.Should().HaveCount(2);
        world.ItemFacts(rig.Copies[1].Drive, rig.Copies[1].Item).Should().BeNull("the re-copy's own copy is removed");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F2 — a re-copy inside S1 keeps the release (re-bound), so the released move into S2 still runs
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "F2 must fire: a pending re-copy inside S1, then an F3 release, then a pass: the re-copy keeps the release and the file moves into S2")]
    public async Task F2_AReCopyInsideS1_KeepsTheRelease_AndTheFileMovesIntoS2()
    {
        const string current = "01CURRENTFILEINS1";
        var (world, rig) = RefiledToS2(bound: current);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row["sprk_graphitemid"] = current;
        world.Items[(T.SecureContainer, current)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-current");
        // An earlier move into S1 still owes its source in customer A's container, which was edited after that move.
        world.Items[(T.CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 2000, "source-edited");
        rig.Versions[(T.CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T.Rig.Written, 1234, T.Rig.CreatorName) { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("2.0", null, new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero), 2000, "Late Editor")
            {
                LastModifiedByUserId = T.Creator,
            },
        ];
        var owed = new DocumentContainerRelocator.RelocationLedgerEntry
        {
            SourceDrive = T.CustomerAContainer,
            SourceItem = Item,
            RekeyPending = true,
            At = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            Witness = new DocumentContainerRelocator.RelocationWitness(1234, "hash-1234", "1.0"),
        };
        row[LedgerColumn] = (new Ledger([owed]) { Release = AReleaseFor(item: current) }).Serialize();

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.State.Should().NotBe(RelocationState.HeldIsolated, outcome.Detail);
        outcome.State.Should().BeOneOf(RelocationState.Relocated, RelocationState.RelocationPending);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer, "the release, re-bound to the re-copy, let the file follow");
        LedgerOf(world)!.Release.Should().BeNull("the move into S2 consumed it");
    }

    [Fact(DisplayName = "F2 must not fire: the release a re-copy keeps is still exact — re-bound to the copy, same source container, target, record and age")]
    public async Task F2_TheKeptRelease_IsExact()
    {
        const string current = "01CURRENTFILEINS1";
        var (world, rig) = RefiledToS2(bound: current);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row["sprk_graphitemid"] = current;
        world.Items[(T.SecureContainer, current)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-current");
        world.Items[(T.CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 2000, "source-edited");
        rig.Versions[(T.CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T.Rig.Written, 1234, T.Rig.CreatorName) { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("2.0", null, new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero), 2000, "Late Editor")
            {
                LastModifiedByUserId = T.Creator,
            },
        ];
        var recorded = AReleaseFor(item: current, at: DateTimeOffset.UtcNow.AddDays(-3));
        row[LedgerColumn] = (new Ledger([new DocumentContainerRelocator.RelocationLedgerEntry
        {
            SourceDrive = T.CustomerAContainer,
            SourceItem = Item,
            RekeyPending = true,
            Witness = new DocumentContainerRelocator.RelocationWitness(1234, "hash-1234", "1.0"),
        }])
        { Release = recorded }).Serialize();
        var kept = new List<Release>();
        rig.BeforeDownload = (drive, item) =>
        {
            // The first download after the re-copy re-pointed the row is the move into S2's: what is the release now?
            if (drive == T.SecureContainer && item == T.Rig.CopyItem && LedgerOf(world)?.Release is { } now)
                kept.Add(now);
            return Task.CompletedTask;
        };

        await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        var rebound = kept.Should().ContainSingle().Subject;
        rebound.Should().Be(recorded with { SourceItem = T.Rig.CopyItem }, "only the item is re-bound");
        rebound.Releases(T.SecureContainer, T.Rig.CopyItem, R.SecondSecureContainer, DateTimeOffset.UtcNow).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F3 — a release not recorded inline is retried durably, bound to the move the re-file permitted
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static RelocationReleaseRetryPayload PayloadOf(JobContract job)
        => job.Payload!.Deserialize<RelocationReleaseRetryPayload>(RelocationReleaseRetryJobHandler.PayloadJson)!;

    private static RelocationReleaseRetryJobHandler Handler(T.Rig rig, TimeSpan? lockWait = null)
        => new(rig.Relocator, NullLogger<RelocationReleaseRetryJobHandler>.Instance) { LockWait = lockWait ?? TimeSpan.Zero };

    internal static RelocationReleaseRetryPayload APayload(
        string drive = T.SecureContainer, string target = R.SecondSecureContainer, DateTimeOffset? at = null, RecordOwnershipParent? left = null)
        => new(T.DocumentId, drive, target, [left ?? S1], Caller, SecureRemovalBasis.FullAccess, at ?? DateTimeOffset.UtcNow, [S2]);

    [Fact(DisplayName = "F3 must fire: a lock held past the wait queues a durable retry, bound to the file's container, the target and the time of the re-file")]
    public async Task F3_AHeldLock_QueuesARetry()
    {
        var (world, rig) = RefiledToS2();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));
        var before = DateTimeOffset.UtcNow;

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.LockHeld, outcome.Detail);
        outcome.RetryQueued.Should().BeTrue();
        world.Updates.Should().BeEmpty("never written outside the lock");
        var job = rig.Submitted.Should().ContainSingle().Subject;
        job.JobType.Should().Be(RelocationReleaseRetryJobHandler.JobTypeName);
        var payload = PayloadOf(job);
        payload.Should().BeEquivalentTo(new
        {
            DocumentId = T.DocumentId,
            SourceDrive = T.SecureContainer,
            TargetContainer = R.SecondSecureContainer,
            AuthorizedBy = Caller,
            Basis = SecureRemovalBasis.FullAccess,
        });
        payload.RootsLeft.Should().Equal(S1);
        payload.RootsAfter.Should().Equal(S2);
        payload.PermittedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(before + DocumentContainerRelocator.ReleaseLockWait + TimeSpan.FromSeconds(1),
            "the time the re-file asked, before the lock wait");
    }

    [Fact(DisplayName = "F3 must fire: a write that fails inline queues a retry too")]
    public async Task F3_AFailedWrite_QueuesARetry()
    {
        var (world, rig) = RefiledToS2();
        world.UpdateFault = new TimeoutException("Dataverse unavailable");

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.Failed, outcome.Detail);
        outcome.RetryQueued.Should().BeTrue();
        rig.Submitted.Should().ContainSingle();
    }

    [Fact(DisplayName = "F3 must not fire: a release recorded inline queues nothing")]
    public async Task F3_ARecordedRelease_QueuesNothing()
    {
        var (_, rig) = RefiledToS2();

        (await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1))).State.Should().Be(RelocationReleaseState.Recorded);

        rig.Submitted.Should().BeEmpty();
    }

    [Fact(DisplayName = "F3: a retry that cannot be queued is reported (not queued) with the recovery that works — never 're-file again'")]
    public async Task F3_ARetryThatCannotBeQueued_IsReportedWithTheRealRecovery()
    {
        var (_, rig) = RefiledToS2();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));
        rig.SubmitFault = new InvalidOperationException("Service Bus unavailable");

        var outcome = await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));

        outcome.State.Should().Be(RelocationReleaseState.LockHeld);
        outcome.RetryQueued.Should().BeFalse();
        outcome.Detail.Should().Contain("its retry could not be queued").And.Contain(DocumentContainerRelocator.UnreleasedRecovery);
    }

    [Fact(DisplayName = "F3 must fire: the queued retry records the release under the lock, with the re-file's own time, and the file then moves into S2")]
    public async Task F3_TheQueuedRetry_RecordsTheRelease_AndTheFileMoves()
    {
        var (world, rig) = RefiledToS2();
        var key = DocumentContainerRelocator.RelocationLockKey(T.DocumentId);
        rig.Locks.Held.Add(key);
        await rig.Relocator.RecordReleaseAsync(T.DocumentId, Permit(S1));
        rig.Locks.Held.Remove(key); // the other relocation finished
        var job = rig.Submitted.Single();

        var processed = await Handler(rig).ProcessAsync(job, CancellationToken.None);

        processed.Status.Should().Be(JobStatus.Completed);
        var release = LedgerOf(world)!.Release!;
        release.At.Should().Be(PayloadOf(job).PermittedAt, "the release never outlives the re-file's permission");
        release.SourceItem.Should().Be(Item);
        release.TargetContainer.Should().Be(R.SecondSecureContainer);
        release.AuthorizedBy.Should().Be(Caller);
        rig.Locks.Held.Should().BeEmpty();

        (await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile))
            .State.Should().Be(RelocationState.Relocated);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
    }

    [Fact(DisplayName = "F3: a redelivered retry keeps the release already recorded (idempotent)")]
    public async Task F3_ARedeliveredRetry_IsIdempotent()
    {
        var (world, rig) = RefiledToS2();
        var job = RelocationReleaseRetryJobHandler.NewJob(APayload());
        (await Handler(rig).ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        var writes = world.Updates.Count;

        (await Handler(rig).ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        world.Updates.Should().HaveCount(writes, "the release is recorded already");
    }

    public static TheoryData<string, Func<RelocationReleaseRetryPayload>> RefusedRetries => new()
    {
        { "the row's file is no longer in the source container", () => APayload(drive: T.CustomerAContainer) },
        { "the row no longer derives to the target", () => APayload(target: T.CustomerAContainer) },
        { "the re-file's permission is older than 30 days", () => APayload(at: DateTimeOffset.UtcNow.AddDays(-31)) },
        { "the re-file did not leave the record holding the file", () => APayload(left: new RecordOwnershipParent("sprk_matter", T.PlainMatter)) },
        // PR #2161 round 4 (F1-b): the target is not the own container of a secure record the re-file filed the row under.
        { "the target is no root the re-file filed it under", () => APayload() with { RootsAfter = [new RecordOwnershipParent("sprk_matter", T.SecureMatter)] } },
    };

    [Theory(DisplayName = "F3 must not fire: the queued retry never records a release the inline path would refuse")]
    [MemberData(nameof(RefusedRetries))]
    public async Task F3_TheQueuedRetry_RefusesWhatTheInlinePathWould(string why, Func<RelocationReleaseRetryPayload> payload)
    {
        var (world, rig) = RefiledToS2();

        var processed = await Handler(rig).ProcessAsync(RelocationReleaseRetryJobHandler.NewJob(payload()), CancellationToken.None);

        processed.Status.Should().Be(JobStatus.Completed, "{0}: a retry would find the same", why);
        world.Updates.Should().BeEmpty(why);
        LedgerOf(world)!.Release.Should().BeNull(why);
    }

    [Fact(DisplayName = "F3 must not fire: a retry whose release the re-point already consumed records nothing (the file is in place)")]
    public async Task F3_ARetryAfterTheMove_RecordsNothing()
    {
        var (world, rig) = Placed(R.SecondSecureMatter, R.SecondSecureContainer, isolated: true);

        var processed = await Handler(rig).ProcessAsync(RelocationReleaseRetryJobHandler.NewJob(APayload()), CancellationToken.None);

        processed.Status.Should().Be(JobStatus.Completed);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "F3: a retry whose lock stays held is retried, then dead-lettered on its last delivery")]
    public async Task F3_AHeldLock_IsRetried_ThenDeadLettered()
    {
        var (world, rig) = RefiledToS2();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));
        var job = RelocationReleaseRetryJobHandler.NewJob(APayload());

        (await Handler(rig).ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        job.DeliveryCount = JobContract.MaxDeliveryCount;
        (await Handler(rig).ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);
        world.Updates.Should().BeEmpty();
    }

    public static TheoryData<string, Func<RelocationReleaseRetryPayload>> IncompletePayloads => new()
    {
        { "no root left", () => APayload() with { RootsLeft = [] } },
        { "an unspecified root", () => APayload() with { RootsLeft = [S1, new RecordOwnershipParent("sprk_matter", Guid.Empty)] } },
        { "a basis F3 refuses on", () => APayload() with { Basis = SecureRemovalBasis.NotFullAccessOrCreator } },
        { "an unknown caller's basis", () => APayload() with { Basis = SecureRemovalBasis.CallerUnknown } },
        { "no caller", () => APayload() with { AuthorizedBy = Guid.Empty } },
        { "no time of permission", () => APayload() with { PermittedAt = default } },
        { "no source container", () => APayload(drive: " ") },
        { "no target", () => APayload(target: " ") },
        { "no document", () => APayload() with { DocumentId = Guid.Empty } },
        { "no post-change roots (queued before PR #2161 round 4)", () => APayload() with { RootsAfter = null } },
        { "an unspecified post-change root", () => APayload() with { RootsAfter = [S2, new RecordOwnershipParent("sprk_matter", Guid.Empty)] } },
    };

    [Theory(DisplayName = "F3: a payload that names no complete release is dead-lettered without a read")]
    [MemberData(nameof(IncompletePayloads))]
    public async Task F3_AnIncompletePayload_IsPoisoned(string why, Func<RelocationReleaseRetryPayload> payload)
    {
        var (world, rig) = RefiledToS2();
        var job = RelocationReleaseRetryJobHandler.NewJob(payload());

        (await Handler(rig).ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned, why);
        world.Updates.Should().BeEmpty(why);
        LedgerOf(world)?.Release.Should().BeNull(why);
    }

    [Fact(DisplayName = "F3: an incomplete payload's dead-letter log names the document and the one recovery, never a resubmit")]
    public async Task F3_AnIncompletePayload_IsLoggedWithItsDocumentAndTheRecovery()
    {
        // PR #2161 round 5 (K3): a payload queued before round 4 (no RootsAfter) is dead-lettered as incomplete; resubmitting
        // it changes nothing, so the log names the document and the one recovery (file it back under the secure record
        // holding its file, then re-file it with F3) — ids only.
        var (_, rig) = RefiledToS2();
        var job = RelocationReleaseRetryJobHandler.NewJob(APayload() with { RootsAfter = null });
        var log = new HandlerLog();

        (await new RelocationReleaseRetryJobHandler(rig.Relocator, log) { LockWait = TimeSpan.Zero }
            .ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);

        var entry = log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error).Subject;
        entry.Message.Should().Contain(T.DocumentId.ToString()).And.Contain(job.JobId.ToString())
            .And.Contain(DocumentContainerRelocator.UnreleasedRecovery);
        entry.Message.Should().NotContainEquivalentOf("resubmit");
    }

    private sealed class HandlerLog : Microsoft.Extensions.Logging.ILogger<RelocationReleaseRetryJobHandler>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact(DisplayName = "F3: the retry waits up to two minutes per delivery, every five seconds")]
    public void F3_TheRetryWait()
    {
        DocumentContainerRelocator.ReleaseRetryLockWait.Should().Be(TimeSpan.FromMinutes(2));
        DocumentContainerRelocator.ReleaseRetryLockRetryInterval.Should().Be(TimeSpan.FromSeconds(5));
        new RelocationReleaseRetryJobHandler(new T.Rig(R.Env()).Relocator, NullLogger<RelocationReleaseRetryJobHandler>.Instance)
            .LockWait.Should().Be(DocumentContainerRelocator.ReleaseRetryLockWait);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F4 — Make Secure out of ANOTHER secure record's own container needs the source proven the row's own
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "F4 must fire: a released S1-to-S2 move of an UNBOUND pointer to another person's item is SourceUnverified under Make Secure")]
    public async Task F4_AnUnboundPointerToAnotherItem_IsSourceUnverified()
    {
        var (world, rig) = RefiledToS2(bound: null);
        world.Items[(T.SecureContainer, Item)] = new SpeItemCreator("memo.docx", T.OtherPersonObjectId.ToString("D"), null, 1234, "hash-1234");
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = (Ledger.EmptyLedger with { Release = AReleaseFor() }).Serialize();

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.SourceUnverified, outcome.Detail);
        rig.Copies.Should().BeEmpty("nothing is copied into S2");
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
    }

    [Fact(DisplayName = "F4 must not fire: a released S1-to-S2 move of a BOUND row is relocated under Make Secure, whoever uploaded the item")]
    public async Task F4_ABoundRow_IsRelocated()
    {
        var (world, rig) = RefiledToS2();
        world.Items[(T.SecureContainer, Item)] = new SpeItemCreator("memo.docx", T.OtherPersonObjectId.ToString("D"), null, 1234, "hash-1234");
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = (Ledger.EmptyLedger with { Release = AReleaseFor() }).Serialize();

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
    }

    // Known limit of the prescribed proof: the legacy check requires the document to hang off the secure record whose
    // container holds the file, which a row re-filed S1 to S2 no longer does (a row linked to both is Undecidable). So an
    // UNBOUND row's S1-to-S2 move waits for the item-binding backfill to bind it; a bound row moves, and every move out
    // of a business unit's container (it narrows) is unchanged.
    [Fact(DisplayName = "F4 must fire: a legacy UNBOUND row that no longer hangs off S1 is SourceUnverified, even for its creator's own file — until the binding backfill binds it")]
    public async Task F4_AnUnboundRowThatLeftS1_IsSourceUnverified()
    {
        var (world, rig) = RefiledToS2(bound: null);
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = (Ledger.EmptyLedger with { Release = AReleaseFor() }).Serialize();

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.SourceUnverified, outcome.Detail);
        rig.Copies.Should().BeEmpty();
    }

    [Fact(DisplayName = "F4 must not fire: Make Secure from a business unit's container (it narrows) still moves an unverified file")]
    public async Task F4_FromABusinessUnitsContainer_StillMoves()
    {
        var (world, rig) = Placed(T.SecureMatter, T.CustomerAContainer, isolated: false, bound: null);
        world.Items[(T.CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", T.OtherPersonObjectId.ToString("D"), null, 1234, "hash-1234");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F5 — a move cancelled after its copy exists removes the copy (never one the row may already name)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A document in customer B's container whose file has three versions (it moves into customer A1's).</summary>
    private static (World World, T.Rig Rig) WithThreeVersions()
    {
        var world = T.Environment();
        world.Rows[("sprk_document", T.DocumentId)] = T.Doc(drive: T.CustomerBContainer, item: Item);
        var rig = new T.Rig(world);
        rig.Versions[(T.CustomerBContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T.Rig.Written, 100, "Alice First") { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("2.0", null, T.Rig.Written.AddDays(1), 200, "Bob Second") { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("3.0", null, T.Rig.Written.AddDays(2), 1234, "Carol Current") { LastModifiedByUserId = T.Creator },
        ];
        return (world, rig);
    }

    [Fact(DisplayName = "F5 must fire: a move cancelled mid-replay (shutdown) removes its partial copy, uncancelled, and writes nothing")]
    public async Task F5_ACancelledReplay_RemovesThePartialCopy()
    {
        var (world, rig) = WithThreeVersions();
        using var cts = new CancellationTokenSource();
        rig.BeforeVersionDownload = (_, _, version) =>
        {
            if (version == "2.0")
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        };

        var move = () => rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration, cts.Token);

        await move.Should().ThrowAsync<OperationCanceledException>();
        rig.Copies.Should().ContainSingle("version 1.0 created the copy before the cancellation");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, CancellationToken.None), Times.Once,
            "the partial copy is removed with a token that is not the cancelled one");
        world.Updates.Should().BeEmpty();
        rig.Locks.Held.Should().BeEmpty("the lock is released");
    }

    /// <summary>
    /// The pointer write (the update that names the copy) is cancelled: <paramref name="landed"/> = it reached Dataverse
    /// first (the row names the copy), else it did not.
    /// </summary>
    private static CancellationTokenSource CancelThePointerWrite(World world, bool landed)
    {
        var cts = new CancellationTokenSource();
        var service = Mock.Get(world.EntityService!);
        service.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                var pointerWrite = fields.TryGetValue("sprk_graphitemid", out var item) && Equals(item, T.Rig.CopyItem);
                if (!pointerWrite || landed)
                {
                    world.Updates.Add((entity, id, fields));
                    foreach (var (column, value) in fields)
                    {
                        if (value is DBNull)
                            world.Rows[(entity, id)].Attributes.Remove(column);
                        else
                            world.Rows[(entity, id)][column] = value;
                    }
                }

                if (pointerWrite)
                {
                    cts.Cancel();
                    return Task.FromCanceled(cts.Token);
                }

                return Task.CompletedTask;
            });
        return cts;
    }

    [Fact(DisplayName = "F5 must fire: a re-point cancelled before the row names the copy KEEPS the copy — the write was sent and may still land")]
    public async Task F5_ACancelledRePointThatDidNotLand_KeepsTheCopy()
    {
        var (world, rig) = WithThreeVersions();
        using var cts = CancelThePointerWrite(world, landed: false);

        var move = () => rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration, cts.Token);

        await move.Should().ThrowAsync<OperationCanceledException>();
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "a re-read that does not name the copy proves nothing for a write that did not answer");
        world.ItemFacts(T.CustomerA1Container, T.Rig.CopyItem).Should().NotBeNull();
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull("the source is never touched");
    }

    [Fact(DisplayName = "F5 must not fire: a re-point cancelled AFTER it landed keeps the copy the row now names (its ledger owes the rest)")]
    public async Task F5_ACancelledRePointThatLanded_KeepsTheCopy()
    {
        var (world, rig) = WithThreeVersions();
        using var cts = CancelThePointerWrite(world, landed: true);

        var move = () => rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration, cts.Token);

        await move.Should().ThrowAsync<OperationCanceledException>();
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the row names the copy: deleting it would lose the document");
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem);
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull("the source is kept; the ledger owes its settle");
    }

    // F5 (any fault) — a re-point that FAULTS (a timeout, a 5xx after commit, a transport fault after send) may have
    // landed: the row is read again, and the copy is deleted only when the row provably does not name it.

    /// <summary>
    /// The pointer write (the update that names the copy) throws a NON-cancellation fault: <paramref name="landed"/> = it
    /// reached Dataverse first. <paramref name="rereadFails"/> = every read of the document row after it faults.
    /// </summary>
    private static void FailThePointerWrite(World world, bool landed, bool rereadFails)
    {
        var sent = false;
        var service = Mock.Get(world.EntityService!);
        service.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                var pointerWrite = fields.TryGetValue("sprk_graphitemid", out var item) && Equals(item, T.Rig.CopyItem);
                if (!pointerWrite || landed)
                {
                    world.Updates.Add((entity, id, fields));
                    foreach (var (column, value) in fields)
                    {
                        if (value is DBNull)
                            world.Rows[(entity, id)].Attributes.Remove(column);
                        else
                            world.Rows[(entity, id)][column] = value;
                    }
                }

                if (pointerWrite)
                {
                    sent = true;
                    return Task.FromException(new TimeoutException("the update did not answer"));
                }

                return Task.CompletedTask;
            });
        world.RetrieveFault = (entity, _) => sent && rereadFails && entity == "sprk_document"
            ? new HttpRequestException("Dataverse is unreachable")
            : null;
    }

    [Fact(DisplayName = "F5 must fire: a re-point that faults AFTER it landed keeps the copy the row names, and the move is committed")]
    public async Task F5_AFaultedRePointThatLanded_KeepsTheCopy_AndCommits()
    {
        var (world, rig) = WithThreeVersions();
        FailThePointerWrite(world, landed: true, rereadFails: false);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the row names the copy: deleting it would lose the document");
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem);
        outcome.State.Should().NotBe(RelocationState.Failed, outcome.Detail);
        outcome.TargetItem.Should().Be(T.Rig.CopyItem, "the landed re-point is the move's commit");
    }

    [Theory(DisplayName = "F5 must fire: a re-point that faults and whose row cannot be read again keeps the copy (a stray copy is recoverable, a dangling pointer is not)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F5_AFaultedRePointWithAnUnreadableRow_KeepsTheCopy(bool landed)
    {
        var (world, rig) = WithThreeVersions();
        FailThePointerWrite(world, landed, rereadFails: true);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("could not be read again");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the row may name the copy");
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull("the source is never touched");
    }

    [Fact(DisplayName = "F5 must fire: a re-point that TIMES OUT and whose row does not name the copy yet keeps the copy — the outcome is unknown")]
    public async Task F5_ATimedOutRePointThatDidNotLand_KeepsTheCopy()
    {
        var (world, rig) = WithThreeVersions();
        FailThePointerWrite(world, landed: false, rereadFails: false);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("may still land").And.Contain("the copy is kept");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull();
    }

    [Fact(DisplayName = "F5 must fire: a released S1-to-S2 re-point that faults after it landed consumes the release once and is reported Relocated, never Failed")]
    public async Task F5_AFaultedReleasedRePointThatLanded_ConsumesTheReleaseOnce()
    {
        var (world, rig) = RefiledToS2();
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = (Ledger.EmptyLedger with { Release = AReleaseFor() }).Serialize();
        FailThePointerWrite(world, landed: true, rereadFails: false);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
        LedgerOf(world)?.Release.Should().BeNull("the landed re-point consumed it");
        world.Updates.Count(u => u.Fields.TryGetValue("sprk_graphitemid", out var i) && Equals(i, T.Rig.CopyItem))
            .Should().Be(1, "the re-point is written once, never repeated");
        rig.Spe.Verify(s => s.DeleteFileAsync(R.SecondSecureContainer, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never);
    }
}
