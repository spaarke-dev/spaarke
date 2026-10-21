// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 review round 1, slice 4 — the relocator's terminal outcomes (B5: a file over 250 MB, a ledger
//     owing MaxLedgerEntries moves), the shared lock-wait budget of a re-file's descendant releases, and the #2151 home
//     rule's two boundaries (A4: only the document's OWN pointer, and only a file IN the declared home). Each pins a
//     write or derivation contract.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver and relocator over task 166's
// document-pointer world.

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocationTerminalAndHomeRuleTests
{
    private const string LedgerColumn = DocumentContainerRelocator.RelocationLedgerColumn;

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B5 — terminal outcomes
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A re-filed document (plain matter) whose BFF-uploaded file sits in another business unit's container.</summary>
    private static (World World, T.Rig Rig) Misplaced(long? size = null)
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        if (size is { } bytes)
            world.Items[(T.CustomerBContainer, R.Item)] = new SpeItemCreator("video.mp4", null, T.BffApplication, bytes, "hash-big");
        return (world, new T.Rig(world));
    }

    /// <summary>A ledger owing <paramref name="count"/> earlier moves whose index step cannot settle (the indexing answers pending).</summary>
    private static string LedgerOwing(int count)
    {
        var entries = string.Join(",", Enumerable.Range(1, count).Select(i =>
            $$"""{"sourceDrive":"b!earlier-{{i}}","sourceItem":"01EARLIER{{i}}","source":"removed","rekeyPending":false,"indexed":"none","at":"2026-10-05T00:00:00+00:00"}"""));
        return $$"""{"v":1,"entries":[{{entries}}]}""";
    }

    [Fact(DisplayName = "B5 must fire: a file over 250 MB is a TERMINAL failure (too-large) — nothing copied")]
    public async Task AFileOver250MB_IsTerminal()
    {
        var (world, rig) = Misplaced(DocumentContainerRelocator.MaxRelocatableBytes + 1);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.TerminalCause.Should().Be(DocumentContainerRelocator.TerminalTooLarge);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "B5 must not fire: a file of exactly 250 MB is not terminal (and is moved)")]
    public async Task AFileOfExactly250MB_IsNotTerminal()
    {
        var (_, rig) = Misplaced(DocumentContainerRelocator.MaxRelocatableBytes);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.TerminalCause.Should().BeNull(outcome.Detail);
    }

    [Fact(DisplayName = "B5 must not fire: a RETRYABLE failure (another relocation holds the lock) is not terminal")]
    public async Task ARetryableFailure_IsNotTerminal()
    {
        var (_, rig) = Misplaced();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.TerminalCause.Should().BeNull();
    }

    [Fact(DisplayName = "B5 must fire: a row whose ledger already owes 8 moves is a TERMINAL failure (ledger-full)")]
    public async Task ALedgerOwingEightMoves_IsTerminal()
    {
        var (world, rig) = Misplaced();
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = LedgerOwing(DocumentContainerRelocator.MaxLedgerEntries);
        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Pending("the index is unavailable");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.TerminalCause.Should().Be(DocumentContainerRelocator.TerminalLedgerFull);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
    }

    [Fact(DisplayName = "B5 must not fire: a ledger owing 7 moves still lets the file move")]
    public async Task ALedgerOwingSevenMoves_StillMoves()
    {
        var (world, rig) = Misplaced();
        world.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = LedgerOwing(DocumentContainerRelocator.MaxLedgerEntries - 1);
        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Pending("the index is unavailable");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        outcome.TerminalCause.Should().BeNull(outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    // ── B5 in the job: reported once, never carried, never failing a run ────────────────────────────────────────────

    private static (string Id, string Cause)[] TerminalDocuments(System.Text.Json.JsonElement section)
        => section.GetProperty("terminalDocuments").EnumerateArray()
            .Select(e => (e.GetProperty("id").GetString()!, e.GetProperty("cause").GetString()!)).ToArray();

    [Fact(DisplayName = "B5 must fire: the re-file pass reports a too-large document ONCE — not failed, not carried, the runs succeed")]
    public async Task TheRefilePass_ReportsATerminalDocumentOnce()
    {
        var (_, rig) = Misplaced(DocumentContainerRelocator.MaxRelocatableBytes + 1);
        var job = R.JobWorld((T.DocumentId, T.CustomerBContainer, false));
        var harness = new R.JobHarness(job, rig);

        var (first, run1, _) = await harness.RunAsync();
        job.Modified("sprk_document", T.DocumentId, DateTime.UtcNow.AddMinutes(1)); // an edit brings it back into the window
        var (second, run2, _) = await harness.RunAsync();

        first.Success.Should().BeTrue(first.ErrorMessage);
        run1.GetProperty("terminal").GetInt32().Should().Be(1);
        TerminalDocuments(run1).Should().Equal((T.DocumentId.ToString(), DocumentContainerRelocator.TerminalTooLarge));
        run1.GetProperty("failed").GetInt32().Should().Be(0);
        run1.GetProperty("carried").GetInt32().Should().Be(0);
        second.Success.Should().BeTrue(second.ErrorMessage);
        run2.GetProperty("examined").GetInt32().Should().Be(1, "examined again");
        run2.GetProperty("terminal").GetInt32().Should().Be(1);
        TerminalDocuments(run2).Should().BeEmpty("reported once per instance");
    }

    [Fact(DisplayName = "B5: a document seen NOT terminal in between is reported again when it is terminal again")]
    public async Task TheRefilePass_ReportsAgain_AfterARunItWasNotTerminal()
    {
        var (_, rig) = Misplaced(DocumentContainerRelocator.MaxRelocatableBytes + 1);
        var job = R.JobWorld((T.DocumentId, T.CustomerBContainer, false));
        var harness = new R.JobHarness(job, rig);
        var lockKey = DocumentContainerRelocator.RelocationLockKey(T.DocumentId);

        var (_, run1, _) = await harness.RunAsync();
        rig.Locks.Held.Add(lockKey); // run 2: another relocation holds it — a retryable failure, carried
        var (_, run2, _) = await harness.RunAsync();
        rig.Locks.Held.Remove(lockKey);
        var (_, run3, _) = await harness.RunAsync();

        TerminalDocuments(run1).Should().ContainSingle();
        R.Ids(run2, "failedDocuments").Should().Equal(T.DocumentId);
        TerminalDocuments(run3).Should().Equal((T.DocumentId.ToString(), DocumentContainerRelocator.TerminalTooLarge));
    }

    [Fact(DisplayName = "B5 must not fire: a retryable failure in the re-file pass is failed and carried, never terminal")]
    public async Task TheRefilePass_ARetryableFailure_IsNotTerminal()
    {
        var (_, rig) = Misplaced();
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));

        var (result, refile, _) = await new R.JobHarness(R.JobWorld((T.DocumentId, T.CustomerBContainer, false)), rig).RunAsync();

        refile.GetProperty("terminal").GetInt32().Should().Be(0);
        TerminalDocuments(refile).Should().BeEmpty();
        R.Ids(refile, "failedDocuments").Should().Equal(T.DocumentId);
        refile.GetProperty("carried").GetInt32().Should().Be(1);
        result.Success.Should().BeFalse();
    }

    [Fact(DisplayName = "B5 must fire: the backstop counts a too-large document terminal at once — not incomplete, taken, the run succeeds")]
    public async Task TheBackstop_ATooLargeDocument_IsTerminal()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.SecureMatter, T.CustomerAContainer);
        pointer.Items[(T.CustomerAContainer, R.Item)] =
            new SpeItemCreator("video.mp4", null, T.BffApplication, DocumentContainerRelocator.MaxRelocatableBytes + 1, "hash-big");
        var rig = new T.Rig(pointer);

        var (result, refile, makeSecure) =
            await new R.JobHarness(R.JobWorld((T.DocumentId, T.CustomerAContainer, true)), rig).RunAsync();

        makeSecure.GetProperty("terminal").GetInt32().Should().Be(1);
        TerminalDocuments(makeSecure).Should().Equal((T.DocumentId.ToString(), DocumentContainerRelocator.TerminalTooLarge));
        makeSecure.GetProperty("incomplete").GetInt32().Should().Be(0);
        refile.GetProperty("skippedForMakeSecure").GetInt32().Should().Be(1, "taken: the re-file pass does not report it again");
        refile.GetProperty("terminal").GetInt32().Should().Be(0);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "B5 must fire: an unreadable ledger is incomplete for four backstop runs, terminal (reported once) from the fifth")]
    public async Task TheBackstop_AnUnreadableLedger_IsTerminalFromTheFifthRun()
    {
        var (pointer, job) = RefilePassRoundOneTests.HeldWithALedger();
        pointer.Rows[("sprk_document", T.DocumentId)][LedgerColumn] = "not a ledger";
        var harness = new R.JobHarness(job, new T.Rig(pointer));

        var runs = new List<(bool Success, System.Text.Json.JsonElement MakeSecure)>();
        for (var i = 0; i < SecureChildReconciliationJob.MaxUnreadableLedgerRuns + 1; i++)
        {
            var (result, _, makeSecure) = await harness.RunAsync();
            runs.Add((result.Success, makeSecure));
        }

        runs.Take(4).Should().OnlyContain(r => !r.Success && r.MakeSecure.GetProperty("incomplete").GetInt32() == 1);
        runs.Take(4).Should().OnlyContain(r => r.MakeSecure.GetProperty("terminal").GetInt32() == 0);
        runs[4].Success.Should().BeTrue();
        runs[4].MakeSecure.GetProperty("incomplete").GetInt32().Should().Be(0);
        TerminalDocuments(runs[4].MakeSecure).Should().Equal((T.DocumentId.ToString(), DocumentContainerRelocator.TerminalLedgerUnreadable));
        runs[5].Success.Should().BeTrue();
        runs[5].MakeSecure.GetProperty("terminal").GetInt32().Should().Be(1);
        TerminalDocuments(runs[5].MakeSecure).Should().BeEmpty("reported once");
        SecureChildReconciliationJob.MaxUnreadableLedgerRuns.Should().Be(5);
    }

    [Fact(DisplayName = "B5 must not fire: a ledger read once between unreadable runs restarts the count — four more runs are not terminal")]
    public async Task TheBackstop_AReadableRun_RestartsTheUnreadableCount()
    {
        var (pointer, job) = RefilePassRoundOneTests.HeldWithALedger();
        var row = pointer.Rows[("sprk_document", T.DocumentId)];
        var readable = row[LedgerColumn];
        var harness = new R.JobHarness(job, new T.Rig(pointer));

        var results = new List<(bool Success, System.Text.Json.JsonElement MakeSecure)>();
        for (var i = 0; i < 9; i++)
        {
            row[LedgerColumn] = i == 4 ? readable : "not a ledger";
            var (result, _, makeSecure) = await harness.RunAsync();
            results.Add((result.Success, makeSecure));
        }

        results[4].Success.Should().BeTrue("the readable run: held, owing nothing");
        results.Skip(5).Should().OnlyContain(r => !r.Success && r.MakeSecure.GetProperty("terminal").GetInt32() == 0,
            "four unreadable runs after a readable one are not five consecutive");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Slice 4 — the descendants' releases share one lock-wait budget
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid Caller = Guid.Parse("f3f3f3f3-0000-4000-8000-000000002159");

    private static SecureExitPermit Permit()
        => new([new RecordOwnershipParent("sprk_matter", T.SecureMatter)],
            new SecureRemovalDecision(SecureRemovalOutcome.Permitted, SecureRemovalBasis.FullAccess, Caller),
            [new RecordOwnershipParent("sprk_matter", R.SecondSecureMatter)]);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-4000-8000-0000000c{n:x4}");

    /// <summary>Isolated documents filed under S2, each file still in S1's own container.</summary>
    private static (World World, T.Rig Rig, Guid[] Ids) RefiledToS2(int count)
    {
        var world = R.Env();
        var ids = Enumerable.Range(1, count).Select(Id).ToArray();
        foreach (var id in ids)
        {
            world.Rows[("sprk_document", id)] = R.Doc(id, R.SecondSecureMatter, T.SecureContainer);
            R.Own(world, id, isolated: true);
        }

        return (world, new T.Rig(world), ids);
    }

    [Fact(DisplayName = "slice 4 must fire: busy locks share ONE wait budget — past it a busy document gets one attempt and no wait")]
    public async Task BusyDescendants_ShareOneBudget()
    {
        var (_, rig, ids) = RefiledToS2(3);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(ids[0]));
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(ids[1]));
        var clock = Stopwatch.StartNew();

        var outcomes = await rig.Relocator.RecordReleasesAsync(ids, Permit(), TimeSpan.FromMilliseconds(600), CancellationToken.None);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1800), "the second busy document waited for nothing");
        outcomes.Select(o => o.DocumentId).Should().Equal(ids);
        outcomes.Select(o => o.Outcome.State).Should().Equal(
            RelocationReleaseState.LockHeld, RelocationReleaseState.LockHeld, RelocationReleaseState.Recorded);
    }

    [Fact(DisplayName = "slice 4 must not fire: within the budget each busy document still waits at most ReleaseLockWait, and the rest are recorded")]
    public async Task WithinTheBudget_EachWaitsAtMostTheSingleWait()
    {
        var (_, rig, ids) = RefiledToS2(2);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(ids[0]));
        var clock = Stopwatch.StartNew();

        var outcomes = await rig.Relocator.RecordReleasesAsync(ids, Permit());

        clock.Elapsed.Should().BeGreaterThanOrEqualTo(DocumentContainerRelocator.ReleaseLockWait - TimeSpan.FromMilliseconds(50));
        clock.Elapsed.Should().BeLessThan(DocumentContainerRelocator.DescendantReleaseLockBudget - TimeSpan.FromMilliseconds(500),
            "one busy document waits ReleaseLockWait, not the whole budget");
        outcomes.Select(o => o.Outcome.State).Should().Equal(RelocationReleaseState.LockHeld, RelocationReleaseState.Recorded);
    }

    [Fact(DisplayName = "slice 4: the descendants' budget is 5 s and a single wait 2 s")]
    public void TheBudgets()
    {
        DocumentContainerRelocator.DescendantReleaseLockBudget.Should().Be(TimeSpan.FromSeconds(5));
        DocumentContainerRelocator.ReleaseLockWait.Should().Be(TimeSpan.FromSeconds(2));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A4 — the #2151 home rule's boundaries
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";

    [Fact(DisplayName = "A4 must not fire: with a home configured, an unfiled bound document in its business unit's container still derives to the business unit (in place)")]
    public async Task AHome_DoesNotClaimAnUnfiledDocumentInItsBusinessUnitsContainer()
    {
        var world = R.Env(strict: true, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, null, T.CustomerAContainer);
        var rig = new T.Rig(world);

        var derivation = await rig.Resolver.DeriveDocumentContainersAsync(T.DocumentId);
        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);

        derivation.DeclaredHome.Should().BeFalse(derivation.Reason);
        derivation.PrimaryContainer.Should().Be(T.CustomerAContainer);
        outcome.State.Should().Be(RelocationState.InPlace, outcome.Detail);
    }

    [Fact(DisplayName = "A4 must fire: a document unfiled IN the declared home derives to the home (the rule's own case)")]
    public async Task AnUnfiledDocumentInTheHome_DerivesToTheHome()
    {
        var world = R.Env(strict: true, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, null, R.ProcedureLibrary);
        var rig = new T.Rig(world);

        var derivation = await rig.Resolver.DeriveDocumentContainersAsync(T.DocumentId);

        derivation.DeclaredHome.Should().BeTrue(derivation.Reason);
        derivation.PrimaryContainer.Should().Be(R.ProcedureLibrary);
    }

    [Fact(DisplayName = "A4 must not fire: a CHILD of a home document derives through its parent's business unit, never the parent's home")]
    public async Task AChildOfAHomeDocument_DerivesThroughItsParentsBusinessUnit()
    {
        var parent = Guid.Parse("00000000-0000-4000-8000-0000000a4001");
        var world = R.Env(strict: true, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", parent)] = R.Doc(parent, null, R.ProcedureLibrary);
        var child = R.Doc(T.DocumentId, null, R.ProcedureLibrary);
        child["sprk_parentdocument"] = new EntityReference("sprk_document", parent);
        world.Rows[("sprk_document", T.DocumentId)] = child;
        var rig = new T.Rig(world);

        var derivation = await rig.Resolver.DeriveDocumentContainersAsync(T.DocumentId);

        derivation.Decided.Should().BeTrue(derivation.Reason);
        derivation.DeclaredHome.Should().BeFalse(derivation.Reason);
        derivation.PrimaryContainer.Should().Be(T.CustomerAContainer, "its parent's owning business unit's container");
    }
}
