// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 review round 1, slice 3 — the re-file pass and the Make Secure backstop of
//     SecureChildReconciliationJob: the examination budget, time slice and window cursor (B2), the watermark held until the
//     window drains, a cancelled pass keeping its place, undecidable rows failing the run and dropped after five runs (B6),
//     held and checked-out documents counted apart (B7, B4), what the backstop hands the re-file pass (B1) and the documents
//     under changed matters, projects and work assignments (A3). Each pins a contract of the job's run report or its state.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL job, relocator and resolver over task 148's and task 166's worlds.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RefilePassRoundOneTests
{
    /// <summary>A clock the test moves: <see cref="Now"/> as set; each timestamp read advances by <see cref="Step"/>.</summary>
    internal sealed class SteppingTime : TimeProvider
    {
        private long _ticks;

        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public TimeSpan Step { get; set; }

        /// <summary>Called with the 1-based number of each timestamp read.</summary>
        public Action<int>? OnTimestamp { get; set; }

        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow() => Now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            Reads++;
            OnTimestamp?.Invoke(Reads);
            var at = _ticks;
            _ticks += Step.Ticks;
            return at;
        }
    }

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:x12}");

    /// <summary>N documents filed under the plain matter, each already in its derived container (examined, never moved).</summary>
    private static (World Pointer, SecureChildShareWorld Job, Guid[] Ids) InPlace(int count)
    {
        var pointer = R.Env();
        var ids = Enumerable.Range(1, count).Select(Id).ToArray();
        foreach (var id in ids)
            pointer.Rows[("sprk_document", id)] = R.Doc(id, T.PlainMatter, T.CustomerA1Container);
        return (pointer, R.JobWorld(ids.Select(id => (id, T.CustomerA1Container, false)).ToArray()), ids);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B2 — the examination budget, the time slice and the window cursor
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B2 must fire: a window of N documents over a budget of B drains in ceil(N/B) runs, each examined once, the watermark held until it drains")]
    public async Task AWindowOverTheBudget_DrainsInCeilRuns()
    {
        var (pointer, job, _) = InPlace(5);
        var harness = new R.JobHarness(job, new T.Rig(pointer));

        var runs = new List<System.Text.Json.JsonElement>();
        for (var i = 0; i < 3; i++)
            runs.Add((await harness.RunAsync(examinationBudget: "2")).Refile);

        runs.Select(r => r.GetProperty("examined").GetInt32()).Should().Equal(2, 2, 1);
        runs.Select(r => r.GetProperty("windowDrained").GetBoolean()).Should().Equal(false, false, true);
        runs.Select(r => r.GetProperty("watermarkHeld").GetBoolean()).Should().Equal(true, true, false);
        runs.Select(r => r.GetProperty("inPlace").GetInt32()).Should().Equal(2, 2, 1);
    }

    [Fact(DisplayName = "B2 must not fire: a window within the budget drains in one run and moves the watermark")]
    public async Task AWindowWithinTheBudget_DrainsInOneRun()
    {
        var (pointer, job, _) = InPlace(3);
        var (result, refile, _) = await new R.JobHarness(job, new T.Rig(pointer)).RunAsync();

        refile.GetProperty("examined").GetInt32().Should().Be(3);
        refile.GetProperty("windowDrained").GetBoolean().Should().BeTrue();
        refile.GetProperty("watermarkHeld").GetBoolean().Should().BeFalse();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "B2: the default examination budget is 500, under SecureChild:Reconciliation:RefileExaminationBudget")]
    public void TheDefaultBudget_Is500()
    {
        SecureChildReconciliationJob.DefaultRefileExaminationBudget.Should().Be(500);
        SecureChildReconciliationJob.RefileExaminationBudgetConfigKey.Should().Be("SecureChild:Reconciliation:RefileExaminationBudget");
        SecureChildReconciliationJob.RefileTimeSlice.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact(DisplayName = "B2 must fire: the pass stops 60 s after it started; the rest waits behind the cursor and drains next run")]
    public async Task TheTimeSlice_StopsThePass_AndTheNextRunContinues()
    {
        var (pointer, job, _) = InPlace(5);
        var time = new SteppingTime { Step = TimeSpan.FromSeconds(25) };
        var harness = new R.JobHarness(job, new T.Rig(pointer), time);

        var (_, run1, _) = await harness.RunAsync();
        time.Step = TimeSpan.Zero;
        var (_, run2, _) = await harness.RunAsync();

        run1.GetProperty("examined").GetInt32().Should().Be(2, "25 s and 50 s in; at 75 s the slice is spent");
        run1.GetProperty("windowDrained").GetBoolean().Should().BeFalse();
        run2.GetProperty("examined").GetInt32().Should().Be(3, "the next run continues after the cursor");
        run2.GetProperty("windowDrained").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "B2 must fire: a document re-filed while the window is walked, BEHIND the cursor, is reached in the next window (the watermark moves to the window's start)")]
    public async Task ARowModifiedBehindTheCursor_IsReachedInTheNextWindow()
    {
        var t0 = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        var time = new SteppingTime { Now = t0 };
        var pointer = R.Env();
        var a = Id(0x20);
        var b = Id(0x30);
        var late = Id(0x10); // a lower id than the cursor will hold
        pointer.Rows[("sprk_document", a)] = R.Doc(a, T.PlainMatter, T.CustomerA1Container);
        pointer.Rows[("sprk_document", b)] = R.Doc(b, T.PlainMatter, T.CustomerA1Container);
        var job = R.JobWorld((a, T.CustomerA1Container, false), (b, T.CustomerA1Container, false));
        job.Modified("sprk_document", a, t0.UtcDateTime.AddMinutes(-5));
        job.Modified("sprk_document", b, t0.UtcDateTime.AddMinutes(-5));
        var rig = new T.Rig(pointer);
        var harness = new R.JobHarness(job, rig, time);

        (await harness.RunAsync(examinationBudget: "1")).Refile.GetProperty("windowDrained").GetBoolean().Should().BeFalse();

        // Ten minutes on, between the runs, a document with a LOWER id than the cursor is re-filed (its file misplaced).
        pointer.Rows[("sprk_document", late)] = R.Doc(late, T.PlainMatter, T.CustomerBContainer);
        job.Add("sprk_document", late,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)), ("sprk_graphdriveid", T.CustomerBContainer));
        job.Modified("sprk_document", late, t0.UtcDateTime.AddMinutes(5));
        time.Now = t0.AddMinutes(10);
        var (_, run2, _) = await harness.RunAsync(examinationBudget: "1");
        time.Now = t0.AddMinutes(20);
        var (_, run3, _) = await harness.RunAsync(examinationBudget: "1");

        run2.GetProperty("windowDrained").GetBoolean().Should().BeTrue("b was the last row ahead of the cursor");
        R.Ids(run2, "movedDocuments").Should().BeEmpty("the late row is behind the cursor in this window");
        R.Ids(run3, "movedDocuments").Should().Equal(new[] { late }, "the watermark moved to the window's start, not this run's");
        rig.DriveOf(late).Should().Be(T.CustomerA1Container);
    }

    [Fact(DisplayName = "B2 must fire: a CANCELLED pass keeps its cursor and its carry; the next run continues where it stopped")]
    public async Task ACancelledPass_KeepsItsCursorAndCarry()
    {
        var pointer = R.Env();
        var failing = Id(1);
        pointer.Rows[("sprk_document", failing)] = R.Doc(failing, T.PlainMatter, T.CustomerBContainer);
        var inPlace = Enumerable.Range(2, 4).Select(Id).ToArray();
        foreach (var id in inPlace)
            pointer.Rows[("sprk_document", id)] = R.Doc(id, T.PlainMatter, T.CustomerA1Container);
        var rig = new T.Rig(pointer);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(failing)); // its move fails: carried
        var job = R.JobWorld(new[] { (failing, T.CustomerBContainer, false) }
            .Concat(inPlace.Select(id => (id, T.CustomerA1Container, false))).ToArray());
        var time = new SteppingTime();
        var harness = new R.JobHarness(job, rig, time);
        using var cancel = new CancellationTokenSource();
        time.OnTimestamp = n =>
        {
            // Read 1 starts the run, read 2 starts the pass; reads 3.. come one per document examined. Cancel during the
            // third document (the second in place), at its check.
            if (n == 5)
                cancel.Cancel();
        };

        var run1 = async () => await harness.RunAsync(ct: cancel.Token);
        await run1.Should().ThrowAsync<OperationCanceledException>();
        time.OnTimestamp = null;
        var (_, run2, _) = await harness.RunAsync();

        R.Ids(run2, "failedDocuments").Should().Equal(new[] { failing }, "the carry survived the cancellation");
        DateTimeOffset.Parse(harness.Last.GetProperty("accessPermission").GetProperty("since").GetString()!)
            .Should().Be(time.Now - SecureChildReconciliationJob.RecentChangesOverlap,
                "the access pass ran BEFORE the cancelled re-file pass, so it completed and moved its watermark");
        run2.GetProperty("examined").GetInt32().Should().BeLessThan(1 + inPlace.Length,
            "the cursor survived: the documents the cancelled run reached are not examined again");
        run2.GetProperty("windowDrained").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "B2 must not fire: a carried row the budget did not reach this run stays carried, and is retried next run")]
    public async Task ACarriedRowTheBudgetDidNotReach_StaysCarried()
    {
        var pointer = R.Env();
        var failing = Id(1);
        pointer.Rows[("sprk_document", failing)] = R.Doc(failing, T.PlainMatter, T.CustomerBContainer);
        var inPlace = new[] { Id(2), Id(3) };
        foreach (var id in inPlace)
            pointer.Rows[("sprk_document", id)] = R.Doc(id, T.PlainMatter, T.CustomerA1Container);
        var rig = new T.Rig(pointer);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(failing)); // its move fails: carried
        var job = R.JobWorld((failing, T.CustomerBContainer, false), (inPlace[0], T.CustomerA1Container, false),
            (inPlace[1], T.CustomerA1Container, false));
        var harness = new R.JobHarness(job, rig);

        var (_, run1, _) = await harness.RunAsync();
        job.Modified("sprk_document", failing, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // only the carry brings it back
        var (_, run2, _) = await harness.RunAsync(examinationBudget: "2");
        var (_, run3, _) = await harness.RunAsync();

        R.Ids(run1, "failedDocuments").Should().Equal(failing);
        run2.GetProperty("examined").GetInt32().Should().Be(2, "the window's two documents spent the budget");
        run2.GetProperty("failed").GetInt32().Should().Be(0, "the carried row was not reached");
        run2.GetProperty("carried").GetInt32().Should().Be(1, "a carried row not reached stays carried");
        R.Ids(run3, "failedDocuments").Should().Equal(new[] { failing }, "retried in the next run");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B6 — an undecidable row fails the run; dropped after five consecutive runs; the carry's overflow holds the watermark
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B6 must fire: an undecidable document fails every run it is carried in, and is dropped (reported once) after five")]
    public async Task AnUndecidableRow_FailsTheRun_AndIsDroppedAfterFiveRuns()
    {
        var pointer = R.Env();
        var undecidable = Id(0x40);
        pointer.Rows[("sprk_document", undecidable)] = R.Doc(undecidable, Guid.NewGuid(), T.CustomerA1Container); // its matter is gone
        var job = R.JobWorld((undecidable, T.CustomerA1Container, false));
        var harness = new R.JobHarness(job, new T.Rig(pointer));

        var results = new List<(bool Success, System.Text.Json.JsonElement Refile)>();
        for (var i = 0; i < 6; i++)
        {
            var (result, refile, _) = await harness.RunAsync();
            results.Add((result.Success, refile));
            job.Modified("sprk_document", undecidable, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // only the carry brings it back
        }

        results.Take(5).Should().OnlyContain(r => !r.Success, "an undecidable row fails the run");
        results.Take(5).Select(r => r.Refile.GetProperty("undecidable").GetInt32()).Should().Equal(1, 1, 1, 1, 1);
        results.Take(4).Should().OnlyContain(r => r.Refile.GetProperty("undecidableDropped").GetInt32() == 0);
        R.Ids(results[4].Refile, "undecidableDroppedDocuments").Should().Equal(undecidable);
        results[4].Refile.GetProperty("carried").GetInt32().Should().Be(0);
        results[5].Success.Should().BeTrue("dropped: no longer carried");
        results[5].Refile.GetProperty("retriedRows").GetInt32().Should().Be(0);
    }

    [Theory(DisplayName = "B6: past the carry's limit the watermark is held (watermarkHeld), at the limit it moves")]
    [InlineData(SecureChildReconciliationJob.MaxCarriedRows + 1, true)]
    [InlineData(SecureChildReconciliationJob.MaxCarriedRows, false)]
    public async Task TheCarrysOverflow_HoldsTheWatermark(int undecidable, bool held)
    {
        var pointer = R.Env();
        var ids = Enumerable.Range(0x1000, undecidable).Select(Id).ToArray();
        foreach (var id in ids)
            pointer.Rows[("sprk_document", id)] = R.Doc(id, Guid.NewGuid(), T.CustomerA1Container);
        var job = R.JobWorld(ids.Select(id => (id, T.CustomerA1Container, false)).ToArray());

        var (_, refile, _) = await new R.JobHarness(job, new T.Rig(pointer)).RunAsync(examinationBudget: "5000");

        refile.GetProperty("windowDrained").GetBoolean().Should().BeTrue();
        refile.GetProperty("watermarkHeld").GetBoolean().Should().Be(held);
        refile.GetProperty("carried").GetInt32().Should().Be(Math.Min(undecidable, SecureChildReconciliationJob.MaxCarriedRows));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B4 / B7 — checked out and held, counted apart and not incomplete
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B4 must fire: the re-file pass lists a checked-out document apart — not failed, not carried, the run succeeds")]
    public async Task TheRefilePass_ListsACheckedOutDocument()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        pointer.Rows[("sprk_document", T.DocumentId)][DocumentContainerRelocator.CheckedOutByColumn] =
            new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);
        var rig = new T.Rig(pointer);

        var (result, refile, _) = await new R.JobHarness(R.JobWorld((T.DocumentId, T.CustomerBContainer, false)), rig).RunAsync();

        R.Ids(refile, "checkedOutDocuments").Should().Equal(T.DocumentId);
        refile.GetProperty("failed").GetInt32().Should().Be(0);
        refile.GetProperty("carried").GetInt32().Should().Be(0, "its check-in modifies the row: the window reaches it again");
        result.Success.Should().BeTrue(result.ErrorMessage);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
    }

    /// <summary>A document in S1's own container filed under S2 (an outside edit), isolated, with a ledger the backstop lists.</summary>
    internal static (World Pointer, SecureChildShareWorld Job) HeldWithALedger()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer);
        R.Own(pointer, T.DocumentId, isolated: true);
        // A readable ledger that owes nothing (what an earlier settle can leave), so the backstop lists the row.
        var ledger = DocumentContainerRelocator.RelocationLedger.EmptyLedger.Serialize();
        pointer.Rows[("sprk_document", T.DocumentId)][DocumentContainerRelocator.RelocationLedgerColumn] = ledger;
        var job = R.JobWorld((T.DocumentId, T.SecureContainer, true));
        job.Set("sprk_document", T.DocumentId, DocumentContainerRelocator.RelocationLedgerColumn, ledger);
        return (pointer, job);
    }

    [Fact(DisplayName = "B7 must fire: the backstop counts a HELD document apart (heldIsolated), not incomplete; the run succeeds")]
    public async Task TheBackstop_CountsAHeldDocumentApart()
    {
        var (pointer, job) = HeldWithALedger();

        var (result, refile, makeSecure) = await new R.JobHarness(job, new T.Rig(pointer)).RunAsync();

        makeSecure.GetProperty("heldIsolated").GetInt32().Should().Be(1);
        R.Ids(makeSecure, "heldIsolatedDocuments").Should().Equal(T.DocumentId);
        makeSecure.GetProperty("incomplete").GetInt32().Should().Be(0);
        result.Success.Should().BeTrue(result.ErrorMessage);
        refile.GetProperty("skippedForMakeSecure").GetInt32().Should().Be(1, "B1: a held document is taken");
    }

    [Fact(DisplayName = "B4 must fire: the backstop counts a CHECKED-OUT document apart, not incomplete")]
    public async Task TheBackstop_CountsACheckedOutDocumentApart()
    {
        var (pointer, job) = HeldWithALedger();
        pointer.Rows[("sprk_document", T.DocumentId)][DocumentContainerRelocator.CheckedOutByColumn] =
            new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);

        var (result, refile, makeSecure) = await new R.JobHarness(job, new T.Rig(pointer)).RunAsync();

        makeSecure.GetProperty("checkedOut").GetInt32().Should().Be(1);
        R.Ids(makeSecure, "checkedOutDocuments").Should().Equal(T.DocumentId);
        makeSecure.GetProperty("incomplete").GetInt32().Should().Be(0);
        result.Success.Should().BeTrue(result.ErrorMessage);
        refile.GetProperty("skippedForMakeSecure").GetInt32().Should().Be(1, "B1: a checked-out document is held, so taken");
    }

    [Fact(DisplayName = "B7 must not fire: a held document whose ledger cannot be read (it may owe a step) stays incomplete in the backstop")]
    public async Task TheBackstop_AHeldDocumentThatMayOwe_IsIncomplete()
    {
        var (pointer, job) = HeldWithALedger();
        pointer.Rows[("sprk_document", T.DocumentId)][DocumentContainerRelocator.RelocationLedgerColumn] = "not a ledger";

        var (result, _, makeSecure) = await new R.JobHarness(job, new T.Rig(pointer)).RunAsync();

        makeSecure.GetProperty("counts").GetProperty(nameof(RelocationState.HeldIsolated)).GetInt32().Should().Be(1);
        makeSecure.GetProperty("heldIsolated").GetInt32().Should().Be(0, "it reports a pending line: not merely waiting");
        makeSecure.GetProperty("incomplete").GetInt32().Should().Be(1);
        result.Success.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B1 — the backstop hands the re-file pass only what it settled or held
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B1 must fire: a document the backstop FAILED on is not taken — the re-file pass examines and carries it")]
    public async Task ADocumentTheBackstopFailedOn_IsExaminedByTheRefilePass()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.SecureMatter, T.CustomerAContainer);
        var rig = new T.Rig(pointer);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId)); // another relocation holds it
        var harness = new R.JobHarness(R.JobWorld((T.DocumentId, T.CustomerAContainer, true)), rig);

        var (_, refile, makeSecure) = await harness.RunAsync();

        makeSecure.GetProperty("incomplete").GetInt32().Should().Be(1);
        refile.GetProperty("skippedForMakeSecure").GetInt32().Should().Be(0);
        R.Ids(refile, "failedDocuments").Should().Equal(T.DocumentId);
        refile.GetProperty("carried").GetInt32().Should().Be(1, "carried to the next run");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A3 — the documents under a matter, project or work assignment changed in the window
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "A3 must fire: a document whose own row is unchanged, filed under a record changed in the window, is listed and moved")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task ADocumentUnderAChangedRecord_IsListed(string table)
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);
        var parent = Guid.NewGuid();
        var job = R.JobWorld((T.DocumentId, T.CustomerBContainer, false));
        job.Modified("sprk_document", T.DocumentId, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        job.Set("sprk_document", T.DocumentId, table, new EntityReference(table, parent));
        job.Add(table, parent);
        job.Modified(table, parent, DateTime.UtcNow);

        var (_, refile, _) = await new R.JobHarness(job, rig).RunAsync();

        refile.GetProperty("listed").GetInt32().Should().Be(0, "its own row is outside the window");
        refile.GetProperty("listedUnderChangedRecords").GetInt32().Should().Be(1);
        R.Ids(refile, "movedDocuments").Should().Equal(T.DocumentId);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    [Fact(DisplayName = "A3 must not fire: a document under a record changed BEFORE the window is not listed")]
    public async Task ADocumentUnderAnUnchangedRecord_IsNotListed()
    {
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);
        var parent = Guid.NewGuid();
        var job = R.JobWorld((T.DocumentId, T.CustomerBContainer, false));
        job.Modified("sprk_document", T.DocumentId, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        job.Set("sprk_document", T.DocumentId, "sprk_matter", new EntityReference("sprk_matter", parent));
        job.Add("sprk_matter", parent);
        job.Modified("sprk_matter", parent, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var (_, refile, _) = await new R.JobHarness(job, rig).RunAsync();

        refile.GetProperty("listedUnderChangedRecords").GetInt32().Should().Be(0);
        refile.GetProperty("moved").GetInt32().Should().Be(0);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
    }
}
