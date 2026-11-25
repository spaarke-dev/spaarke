using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildReconciliationJob;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2750: four passes of <see cref="Job"/> — recent changes, the inherited Access Permission, the re-file pass and ordinary
/// parent moves — hold their watermark while a run cannot complete (the emergency stop, a listing fault), and a held window
/// keeps growing. Each window listing read at most the page bound (20 pages) per table and THREW past it, and the watermark
/// moved only past a completed listing, so once the held window grew past the bound the pass failed EVERY run, even after
/// the cause cleared, until the instance restarted. The fix caps a listing at the bound, oldest change first, and moves the
/// watermark only to the last listed instant (inclusive, so rows sharing it are listed again): the window drains.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>, answering one row a page so the 20-page bound is
/// 20 rows) with the REAL job, reconcilers, synchronizer, relocator and resolver (the #2735 harness), on a clock the test
/// moves. Each pass gets 24 rows changed in the held window — four past the bound — and six of them share the one instant the
/// cap falls on, three before the cap and three after. Each row needs its own correction (its own secure record, its own
/// access value, its own file move, its own reassigned record), so a skipped row is seen. An inherent multi-record arrange at
/// a KEEP integration path.</para>
/// </summary>
public class Issue2750_HeldWindowPastThePageBoundTests
{
    private const int Rows = 24;
    private const int Restricted = InheritedAccessPermission.Restricted;
    private const int Standard = InheritedAccessPermission.Standard;

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Business unit B, where the ordinary records were reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000275001");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000275002");

    private readonly FakeTimeProvider _time = new(T0);
    private readonly SecureChildShareWorld _world = R.JobWorld();
    private bool _ordinaryListingFaults;

    /// <summary>The children whose owner writes fail (their root is left incomplete) until the test clears them.</summary>
    private readonly HashSet<Guid> _failingWrites = new();

    public enum Pass
    {
        RecentChanges,
        AccessPermission,
        Refile,
        OrdinaryMoves,
    }

    /// <summary>
    /// The held window grows past the page bound; once the cause clears, the pass drains it within a few runs and every row
    /// changed in it — the three that share the cap's instant past the cap included — gets its correction.
    /// </summary>
    [Theory]
    [InlineData(Pass.RecentChanges)]
    [InlineData(Pass.AccessPermission)]
    [InlineData(Pass.Refile)]
    [InlineData(Pass.OrdinaryMoves)]
    public async Task ExecuteAsync_AfterAHeldWindowGrewPastThePageBound_RecoversWithinAFewRuns_AndSkipsNoRow(Pass pass)
    {
        var harness = new Harness(this);
        var ordinary = pass == Pass.OrdinaryMoves;
        await harness.RunAsync(recentWrites: true, ordinaryWrites: ordinary); // stores the watermark; the sweeps complete

        var done = Seed(harness, pass);

        // Hold the window while it grows past the bound: the emergency stop (the ordinary pass: a listing fault — the stop
        // switches its mode, and a switch restarts its catch-up sweep, which would walk the records whatever the listing did).
        _ordinaryListingFaults = ordinary;
        _time.Advance(TimeSpan.FromMinutes(5));
        var held = await harness.RunAsync(recentWrites: ordinary, ordinaryWrites: ordinary);
        Report(held, pass).GetProperty("mode").GetString().Should().Be(ordinary ? Job.ModeWrite : Job.ModeReportOnly);
        done().Should().BeFalse("nothing is corrected while the window is held");

        _ordinaryListingFaults = false;
        var runs = new List<JsonElement>();
        while (!done() && runs.Count < 4)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            runs.Add(await harness.RunAsync(recentWrites: true, ordinaryWrites: ordinary));
        }

        using var scope = new FluentAssertions.Execution.AssertionScope();
        runs.Select(r => Report(r, pass).GetProperty("failure").ValueKind).Should().OnlyContain(k => k == JsonValueKind.Null,
            "a listing past the page bound is capped there, not a failure");
        runs.Should().Contain(r => Report(r, pass).GetProperty("cappedAt").ValueKind == JsonValueKind.String,
            "the held window's listing reached the page bound");
        done().Should().BeTrue($"the {pass} pass drains its held window within a few runs, the rows sharing the cap's instant included");
    }

    /// <summary>
    /// The ordinary pass's carry holds fewer roots than one capped listing returns: past it, the watermark moves to the oldest
    /// change of the roots left out (they are listed again), so the window still drains rather than being held for ever.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryMovesPastTheCarryCap_DrainsTheHeldWindow_AndSkipsNoRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 2);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "5");
        var done = Seed(harness, Pass.OrdinaryMoves);

        var runs = 0;
        while (!done() && runs < 12)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var run = await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "5");
            Report(run, Pass.OrdinaryMoves).GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
            runs++;
        }

        done().Should().BeTrue("five roots a run, the roots past the carry listed again: 24 roots drain in a few runs");
    }

    /// <summary>
    /// Review F2: the re-file window is capped on its FIRST run and walked over several (the examination budget), and a later
    /// run of the same window is not capped. The documents past the first cap whose ids sort before the cursor are not walked in
    /// this window; the window must still move only to the first cap when it drains, so the next window lists them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RefileWindowCappedOnItsFirstRunOnly_MovesOnlyToThatCap_AndMovesEveryDocument()
    {
        var harness = new Harness(this);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: false);

        // Ids order the walk: the tie group's rows (17–22) and row 23 sort first (01–07), the rest after them (20–30).
        var ids = Enumerable.Range(0, Rows)
            .Select(i => OrderedId(i is >= 17 and <= 22 ? i - 16 : i == 23 ? 7 : 0x20 + i))
            .ToArray();
        _world.MaxRowsPerPage = 1; // the first run's listing is capped at 20 rows: the tie group's first three, not 4–7
        for (var i = 0; i < Rows; i++)
        {
            harness.Pointer.Rows[("sprk_document", ids[i])] = R.Doc(ids[i], T.PlainMatter, T.CustomerBContainer);
            _world.Add("sprk_document", ids[i],
                ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                ("sprk_graphdriveid", T.CustomerBContainer));
            _world.Modified("sprk_document", ids[i], At(i));
        }

        // The window starts well after the rows changed: when it drains, its start (less the overlap) is past every row, so
        // only the first run's cap keeps the rows past it in the next window.
        _time.Advance(TimeSpan.FromMinutes(5));
        var first = await harness.RunAsync(recentWrites: true, ordinaryWrites: false, examinationBudget: "8");
        Report(first, Pass.Refile).GetProperty("cappedAt").ValueKind.Should().Be(JsonValueKind.String);
        Report(first, Pass.Refile).GetProperty("windowDrained").GetBoolean().Should().BeFalse("eight of twenty are examined");

        _world.MaxRowsPerPage = null; // the window's later runs list every document: not capped
        for (var run = 0; run < 6 && !ids.All(id => harness.Rig.DriveOf(id) == T.CustomerA1Container); run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await harness.RunAsync(recentWrites: true, ordinaryWrites: false, examinationBudget: "8");
        }

        ids.Where(id => harness.Rig.DriveOf(id) != T.CustomerA1Container).Should().BeEmpty(
            "the documents past the first run's cap, behind the cursor, are listed by the next window and moved");
    }

    /// <summary>
    /// Review F1-b: production shape (the carry holds more roots than a run walks), and more roots than the carry holds share
    /// ONE change instant. The window must advance through the tie (a key set, not the instant alone) and reach every root.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryMovesTiePastTheCarryBoundary_AdvancesThroughTheTie_AndReachesEveryRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 6);
        await SweepCompleteAsync(harness, maxRoots: "2");
        var events = ReassignedMatters(12, _ => (T0 + TimeSpan.FromMinutes(1)).UtcDateTime, firstId: 0x301);

        var runs = 0;
        while (!Moved(events) && runs < 15)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var run = await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "2");
            Report(run, Pass.OrdinaryMoves).GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
            runs++;
        }

        Moved(events).Should().BeTrue("two roots a run through a tie of twelve past a carry of six reaches every root");
    }

    /// <summary>
    /// Review F1-a: production shape (a carry of four, two roots a run, one owner write a run), the write cap reached while the
    /// catch-up sweep runs. Swept roots S1–S6 sort before the listed L1–L3, so the sweep reaches the L roots last. When swept
    /// roots the write cap stopped were CARRIED, ahead of the roots past the root cap: each run filled the carry exactly and the
    /// cursor moved on (to S2, S4, then S6); in run five the sweep reached L1 and L2 (already moved) with the write cap spent,
    /// carried them ahead of the overflow, and S6 fell out of the full carry with the cursor already at S6 — no listing and no
    /// sweep reached it again. Every reassigned record's children must follow it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryMovesWriteCapDuringTheSweep_WithAFullCarry_DropsNoRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 4);
        // Reassigned long ago (in no window): only the catch-up sweep reaches them. The ids order the sweep: these first.
        var swept = ReassignedMatters(6, _ => SecureChildShareWorld.RecordsLastModified, firstId: 0x101);
        // Reassigned just now: listed by the first run, after the swept ones in the carry's order.
        var listed = ReassignedMatters(3, i => (T0 + TimeSpan.FromSeconds(i)).UtcDateTime, firstId: 0x201);

        for (var run = 0; run < 30 && !(Moved(swept) && Moved(listed)); run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var report = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "2", maxOrdinaryMoves: "1"),
                Pass.OrdinaryMoves);
            report.GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        }

        using var scope = new FluentAssertions.Execution.AssertionScope();
        swept.Where(id => _world.OwnerOf("sprk_event", id) != DataversePrincipalRef.Team(TeamB)).Should().BeEmpty(
            "every record the sweep reached has its children moved, the ones the write cap stopped included");
        listed.Where(id => _world.OwnerOf("sprk_event", id) != DataversePrincipalRef.Team(TeamB)).Should().BeEmpty(
            "a carried root the window has moved past is never dropped from the carry");
    }

    /// <summary>
    /// Review 2 F2 (a): two ordinary tables, and the matter listing capped at the page bound inside a tie — one matter at the
    /// cap's instant read, one not. The first root left out of the carry is a PROJECT at that instant. The window must move to
    /// the cap's instant plainly (inclusive), not resume at the project's key: that key would skip every matter of the instant,
    /// the unread one included.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryListingCappedInATie_FirstLeftOutAtTheCapInstant_ReachesTheUnreadRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 18);
        await SweepCompleteAsync(harness, maxRoots: "50");

        // 21 matters (one a page: the listing reads 20 — M0–M18 and M19, not M20, which shares M19's instant) and a project at
        // that instant. Two roots a run: M0, M1 are walked, M2–M19 fill the carry of 18, and the project is the first left out.
        var start = _time.GetUtcNow().UtcDateTime;
        DateTime At(int i) => start + TimeSpan.FromSeconds(Math.Min(i, 19));
        var events = ReassignedMatters(21, At, firstId: 0x501);
        events.AddRange(ReassignedMatters(1, _ => At(19), firstId: 0x601, table: "sprk_project", lookup: "sprk_regardingproject"));
        _world.MaxRowsPerPage = 1;

        _time.Advance(TimeSpan.FromMinutes(2));
        var first = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "2"), Pass.OrdinaryMoves);
        first.GetProperty("cappedAt").GetDateTime().Should().Be(At(19), "the matter listing is capped inside the tie");
        _world.MaxRowsPerPage = null;

        for (var run = 0; run < 30 && !Moved(events); run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "2");
        }

        Moved(events).Should().BeTrue("the matter at the cap's instant that the capped listing did not read is listed next run");
    }

    /// <summary>
    /// Review 2 F2 (b): more roots that no listing reaches again than the carry holds — swept roots left incomplete, carried
    /// while the cursor moved past them. The one left out of the full carry is behind the cursor: the sweep must start over
    /// (reported as <c>sweep.reopened</c>) so it is reached once the writes succeed.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryUnlistedRootsPastTheCarry_ReopensTheSweep_AndReachesEveryRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 3);
        // Reassigned long ago (in no window): only the sweep reaches them, one a run; their writes fail at first.
        var swept = ReassignedMatters(4, _ => SecureChildShareWorld.RecordsLastModified, firstId: 0x701);
        _failingWrites.UnionWith(swept);

        // Runs 1–3 carry S1, S2, S3 (the carry is not past its cap, so the cursor moves on to S3); run 4 reaches S4: four roots
        // no listing reaches again for a carry of three, and S3 — behind the cursor — is left out.
        var reopened = false;
        for (var run = 0; run < 4; run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var report = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "1"), Pass.OrdinaryMoves);
            reopened |= report.GetProperty("sweep").GetProperty("reopened").GetBoolean();
        }

        reopened.Should().BeTrue("four roots no listing reaches again were carried past a carry of three");
        _failingWrites.Clear();
        for (var run = 0; run < 150 && !Moved(swept); run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "1");
        }

        Moved(swept).Should().BeTrue("the root left out of the carry, behind the cursor, is reached by the sweep started over");
    }

    /// <summary>
    /// Review 2 F1 / F2 (c): a run that is only THROTTLED with a carry full of roots no listing reaches again — the first
    /// carried root walked spends the write cap, so the rest are carried unread — holds its window (<c>windowHeld</c>) but
    /// does not fail: the carried roots are walked first next run. Every root's children still move.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryThrottledRunWithAFullUnlistedCarry_HoldsTheWindow_AndDoesNotFail()
    {
        var harness = new Harness(this, ordinaryCarryCap: 2);
        await SweepCompleteAsync(harness, maxRoots: "50");
        // Four reassigned records, three children each, at distinct instants; two roots and one owner write a run.
        var start = _time.GetUtcNow().UtcDateTime;
        var events = ReassignedMatters(4, i => start + TimeSpan.FromSeconds(i), firstId: 0x801, eventsEach: 3);

        var held = false;
        for (var run = 0; run < 40 && !Moved(events); run++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var report = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "2", maxOrdinaryMoves: "1"),
                Pass.OrdinaryMoves);
            report.GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null, "a run that is only throttled does not fail");
            held |= report.TryGetProperty("windowHeld", out var windowHeld) && windowHeld.GetBoolean();
        }

        held.Should().BeTrue("a run's carry was full of roots no listing reaches again, so its window did not advance");
        Moved(events).Should().BeTrue();
    }

    // ── #1821, second round: the capped window's helpers, and what a capped listing must still report and order ──

    [Fact]
    public void Earliest_OfTwoCappedInstants_IsTheEarlierOne_AndAnUncappedSideDefersToTheOther()
    {
        var early = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var late = early.AddMinutes(5);

        Job.Earliest(early, late).Should().Be(early, "two tables capped at different instants: the window moves to the earlier");
        Job.Earliest(late, early).Should().Be(early);
        Job.Earliest(early, early).Should().Be(early);
        Job.Earliest(early, null).Should().Be(early);
        Job.Earliest(null, late).Should().Be(late);
        Job.Earliest(null, null).Should().BeNull("no table was capped");
    }

    [Fact]
    public void WatermarkAfter_MovesToTheCap_OnlyWhenTheCapIsEarlierThanTheRunsOwnWatermark()
    {
        var uncapped = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var earlier = uncapped.UtcDateTime.AddMinutes(-3);

        Job.WatermarkAfter(uncapped, null).Should().Be(uncapped);
        Job.WatermarkAfter(uncapped, earlier).Should().Be(new DateTimeOffset(earlier, TimeSpan.Zero));
        Job.WatermarkAfter(uncapped, uncapped.UtcDateTime).Should().Be(uncapped, "a cap at the run's own watermark moves it nowhere");
        Job.WatermarkAfter(uncapped, uncapped.UtcDateTime.AddSeconds(1)).Should().Be(uncapped, "a later cap never moves the watermark past the run's own");
    }

    [Fact]
    public void WatermarkAfter_ReadsALocalCapAsItsUtcInstant_AndAnUnspecifiedOneAsUtc()
    {
        // Dataverse answers modifiedon in UTC; a Local value is converted, an Unspecified one is read as UTC. The two differ only on a
        // machine whose zone is not UTC (a UTC runner reads both the same).
        var uncapped = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var cap = uncapped.UtcDateTime.AddMinutes(-3);
        var local = TimeZoneInfo.ConvertTimeFromUtc(cap, TimeZoneInfo.Local);
        var unspecified = DateTime.SpecifyKind(cap, DateTimeKind.Unspecified);
        local.Kind.Should().Be(DateTimeKind.Local);

        Job.WatermarkAfter(uncapped, local).UtcDateTime.Should().Be(cap);
        Job.WatermarkAfter(uncapped, unspecified).UtcDateTime.Should().Be(cap);
    }

    /// <summary>
    /// A capped recent-changes listing whose rows are all Secure-team-owned already has nothing to reconcile, but it still read
    /// only up to its cap: the early return must carry that cap, or the window moves past rows it never listed.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ACappedRecentChangesListing_OfRowsAlreadySecure_ReportsTheCap_AndHoldsTheWindowThere()
    {
        var harness = new Harness(this);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: false);
        _world.MaxRowsPerPage = 1; // the 20-page bound is 20 rows
        for (var i = 0; i < Rows; i++)
        {
            var id = Guid.NewGuid();
            _world.Add("sprk_todo", id, ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)));
            _world.Modified("sprk_todo", id, At(i));
        }

        _time.Advance(TimeSpan.FromMinutes(5));
        var capped = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: false), Pass.RecentChanges);
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: false), Pass.RecentChanges);

        capped.GetProperty("rowsChanged").GetInt32().Should().Be(0, "every listed row is already owned by the Secure team");
        capped.GetProperty("cappedAt").GetDateTime().Should().Be(At(19), "the 20th row read is the cap");
        next.GetProperty("since").GetDateTime().Should().Be(At(19), "the window moves only to the cap, so the rows past it are listed next");
    }

    /// <summary>
    /// The same capped listing when the Secure Record owner team is gone by the time the synchronizer looks for the roots above the
    /// changed rows (the job's own check saw it; it was removed in between): the walk answers "not applicable" and nothing is
    /// reconciled, but the listing still read only up to its cap, so the report keeps it and the window moves only there. Losing
    /// it on this path would move the window past rows the listing never read, and they would never be listed.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ACappedRecentChangesListing_WhenTheSecureTeamVanishesBeforeTheWalk_StillReportsTheCap()
    {
        var harness = new Harness(this);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: false);
        _world.MaxRowsPerPage = 1; // the 20-page bound is 20 rows
        for (var i = 0; i < Rows; i++)
        {
            var id = Guid.NewGuid();
            _world.Add("sprk_todo", id, ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)));
            _world.Modified("sprk_todo", id, At(i));
        }

        // Rows owned by the General team survive the listing's filter, so the synchronizer is asked about them — and by then
        // the Secure team is gone (the job's own check already saw it, before the first to-do listing).
        _world.AfterQueriesOf("sprk_todo", _world.QueriedTables.Count(t => t == "sprk_todo") + 1,
            w => w.Remove("team", SecureChildShareWorld.SecureTeam));

        _time.Advance(TimeSpan.FromMinutes(5));
        var capped = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: false), Pass.RecentChanges);
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = Report(await harness.RunAsync(recentWrites: true, ordinaryWrites: false), Pass.RecentChanges);

        using var scope = new FluentAssertions.Execution.AssertionScope();
        capped.GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null, "a team that is gone leaves nothing to decide, it is not a failure");
        capped.GetProperty("rowsChanged").GetInt32().Should().Be(20, "the listing read to the page bound, none of its rows owned by the Secure team");
        capped.GetProperty("rootsFound").GetInt32().Should().Be(0);
        capped.GetProperty("cappedAt").ValueKind.Should().Be(JsonValueKind.String, "the listing reached the page bound, and the report says so");
        next.GetProperty("since").GetDateTime().Should().Be(At(19), "the window moves only to the cap, so the rows past it are listed next");
    }

    /// <summary>
    /// Two roots at one instant in different tables, the project's id below the matter's: past a carry of one, the key order (instant,
    /// then TABLE, then id) leaves the project out and keeps the matter, so the matter is walked first next run and the window
    /// resumes at the project's key. Ordering by id alone would swap them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryRootsPastTheCarryAtOneInstant_AreKeptInTableOrder_NotIdOrder()
    {
        var harness = new Harness(this, ordinaryCarryCap: 1);
        await SweepCompleteAsync(harness, maxRoots: "50");
        var start = _time.GetUtcNow().UtcDateTime;
        var walkedFirst = ReassignedMatters(1, _ => start, firstId: 0x10);
        var matter = ReassignedMatters(1, _ => start, firstId: 0x30);
        var project = ReassignedMatters(1, _ => start, firstId: 0x20, table: "sprk_project", lookup: "sprk_regardingproject");

        _time.Advance(TimeSpan.FromMinutes(2));
        await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "1"); // walks the first matter; one root is carried
        _time.Advance(TimeSpan.FromMinutes(2));
        await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "1");

        Moved(walkedFirst).Should().BeTrue();
        Moved(matter).Should().BeTrue("the matter sorts before the project at one instant, so it is the carried root");
        Moved(project).Should().BeFalse("the project, left out of the carry, is listed again from its key: after the matter");
    }

    private static Guid OrderedId(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:x12}");

    private async Task SweepCompleteAsync(Harness harness, string maxRoots)
    {
        for (var i = 0; i < 20; i++)
        {
            var run = await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: maxRoots);
            if (Report(run, Pass.OrdinaryMoves).GetProperty("sweep").GetProperty("complete").GetBoolean())
                return;
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        throw new InvalidOperationException("Test: the ordinary catch-up sweep did not complete.");
    }

    /// <summary>
    /// <paramref name="count"/> ordinary records (matters unless <paramref name="table"/> says otherwise) reassigned to B, each
    /// with <paramref name="eventsEach"/> events still owned by A; the event ids (each record's own ids when
    /// <paramref name="firstId"/> is given, so the sweep and key order are fixed).
    /// </summary>
    private List<Guid> ReassignedMatters(
        int count, Func<int, DateTime> modifiedAt, int? firstId = null, string table = "sprk_matter",
        string lookup = "sprk_regardingmatter", int eventsEach = 1)
    {
        if (!_world.Has("team", TeamB))
            _world.Add("businessunit", BuB, ("name", "Litigation")).Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);
        var events = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var root = firstId is { } first ? OrderedId(first + i) : Guid.NewGuid();
            _world.Add(table, root, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));
            _world.Modified(table, root, modifiedAt(i));
            for (var e = 0; e < eventsEach; e++)
            {
                var evt = Guid.NewGuid();
                _world.Add("sprk_event", evt,
                    ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                    (lookup, new EntityReference(table, root)),
                    ("versionnumber", 1L));
                events.Add(evt);
            }
        }

        return events;
    }

    private bool Moved(IEnumerable<Guid> events) =>
        events.All(id => _world.OwnerOf("sprk_event", id) == DataversePrincipalRef.Team(TeamB));

    // ── The world ──

    /// <summary>When the <paramref name="i"/>-th row changed: one second apart, except rows 17–22, which share one instant.
    /// One row a page and 20 pages read rows 0–19, so the cap falls inside that instant.</summary>
    private static DateTime At(int i) => (T0 + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(i is >= 17 and <= 22 ? 17 : i)).UtcDateTime;

    /// <summary>The 24 rows the pass must correct, changed in the held window; returns whether all are corrected.</summary>
    private Func<bool> Seed(Harness harness, Pass pass)
    {
        _world.MaxRowsPerPage = 1; // the 20-page bound is 20 rows: a listing of 24 rows reaches it
        var ids = Enumerable.Range(0, Rows).Select(_ => Guid.NewGuid()).ToArray();
        switch (pass)
        {
            case Pass.RecentChanges:
                {
                    // A to-do written outside the product under each of 24 secure records (8 per root table, within the bound).
                    var roots = new[] { ("sprk_matter", "sprk_regardingmatter"), ("sprk_project", "sprk_regardingproject"),
                    ("sprk_workassignment", "sprk_regardingworkassignment") };
                    for (var i = 0; i < Rows; i++)
                    {
                        var (table, lookup) = roots[i % roots.Length];
                        var root = Guid.NewGuid();
                        _world.SecureRoot(table, root)
                            .UserOwnedChild("sprk_todo", ids[i], (lookup, table, root))
                            .Modified("sprk_todo", ids[i], At(i));
                    }

                    return () => ids.All(id =>
                        _world.OwnerOf("sprk_todo", id) == DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
                }

            case Pass.AccessPermission:
                {
                    // 24 to-dos under one Restricted matter that did not change: each to-do is reached only by its own listing.
                    var matter = Guid.NewGuid();
                    _world.OrdinaryRoot("sprk_matter", matter);
                    _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(Restricted));
                    for (var i = 0; i < Rows; i++)
                    {
                        _world.OrdinaryChild("sprk_todo", ids[i], ("sprk_regardingmatter", "sprk_matter", matter));
                        _world.Set("sprk_todo", ids[i], InheritedAccessPermission.Column, new OptionSetValue(Standard));
                        _world.Modified("sprk_todo", ids[i], At(i));
                    }

                    return () => ids.All(id =>
                        _world.ValueOf<OptionSetValue>("sprk_todo", id, InheritedAccessPermission.Column)?.Value == Restricted);
                }

            case Pass.Refile:
                {
                    // 24 documents re-filed under the plain matter whose files are still in another business unit's container.
                    for (var i = 0; i < Rows; i++)
                    {
                        harness.Pointer.Rows[("sprk_document", ids[i])] = R.Doc(ids[i], T.PlainMatter, T.CustomerBContainer);
                        _world.Add("sprk_document", ids[i],
                            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                            ("sprk_graphdriveid", T.CustomerBContainer));
                        _world.Modified("sprk_document", ids[i], At(i));
                    }

                    return () => ids.All(id => harness.Rig.DriveOf(id) == T.CustomerA1Container);
                }

            default:
                {
                    // 24 ordinary matters reassigned to business unit B, each with an event its old default team still owns.
                    _world.Add("businessunit", BuB, ("name", "Litigation"))
                        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);
                    var events = Enumerable.Range(0, Rows).Select(_ => Guid.NewGuid()).ToArray();
                    for (var i = 0; i < Rows; i++)
                    {
                        _world.Add("sprk_matter", ids[i], ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));
                        _world.Modified("sprk_matter", ids[i], At(i));
                        _world.Add("sprk_event", events[i],
                            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                            ("sprk_regardingmatter", new EntityReference("sprk_matter", ids[i])),
                            ("versionnumber", 1L));
                    }

                    return () => events.All(id => _world.OwnerOf("sprk_event", id) == DataversePrincipalRef.Team(TeamB));
                }
        }
    }

    private static JsonElement Report(JsonElement run, Pass pass) => run.GetProperty(pass switch
    {
        Pass.RecentChanges => "recentChanges",
        Pass.AccessPermission => "accessPermission",
        Pass.Refile => "refileRelocations",
        _ => "ordinaryMoves",
    });

    private static bool IsOrdinaryListing(QueryExpression query) =>
        SecureChildLineage.IsRoot(query.EntityName)
        && query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_issecure")
        && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    // ── The job, as the #2735 harness composes it (with the ordinary pass's conditional Web API), on the test's clock ──

    private sealed class Harness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly Job _job;

        public Harness(Issue2750_HeldWindowPastThePageBoundTests test, int ordinaryCarryCap = Job.MaxCarriedRows)
        {
            var world = test._world;
            Rig = new T.Rig(Pointer);
            var entities = SecureChildShareWorld.EntitiesOver(() => world);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => test._ordinaryListingFaults && IsOrdinaryListing(query)
                    ? throw new InvalidOperationException("Test: the reassigned ordinary records could not be listed.")
                    : Task.FromResult(world.Answer(query)));

            var webApi = new FaultingOwnerWebApi(() => world, test._failingWrites);
            var services = new ServiceCollection();
            services.AddSingleton(entities.Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), webApi));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            services.AddScoped(_ => Rig.Relocator);
            services.AddScoped(_ => Rig.Resolver);
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _configuration[Job.MaxRelocationsPerRunConfigKey] = "100";
            _job = new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), test._time,
                _configuration, NullLogger<Job>.Instance)
            {
                OrdinaryCarryCap = ordinaryCarryCap,
            };
        }

        /// <summary>The relocator's view of the documents (task 166's pointer world).</summary>
        public TestRecordContainerResolver.DocumentPointerWorld Pointer { get; } = R.Env();

        public T.Rig Rig { get; }

        /// <summary>A manual run: the secure catch-up stands aside, so only the recent-changes listing reaches the to-dos.</summary>
        public async Task<JsonElement> RunAsync(
            bool recentWrites, bool ordinaryWrites, string? maxRoots = null, string? examinationBudget = null,
            string? maxOrdinaryMoves = null)
        {
            _configuration[Job.RecentChangesWritesEnabledConfigKey] = recentWrites ? null : "false";
            _configuration[Job.OrdinaryMovesWritesEnabledConfigKey] = ordinaryWrites ? "true" : null;
            _configuration[Job.MaxRootsPerRunConfigKey] = maxRoots;
            _configuration[Job.RefileExaminationBudgetConfigKey] = examinationBudget;
            _configuration[Job.MaxOrdinaryMovesPerRunConfigKey] = maxOrdinaryMoves;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "corr-2750", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            return doc.RootElement.Clone();
        }
    }

    /// <summary>The ordinary pass's conditional owner write over the world; a write to a child in the failing set faults.</summary>
    private sealed class FaultingOwnerWebApi : DataverseWebApiClient
    {
        private readonly SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi _inner;
        private readonly HashSet<Guid> _failing;

        public FaultingOwnerWebApi(Func<SecureChildShareWorld> world, HashSet<Guid> failing)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
        {
            _inner = new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(world);
            _failing = failing;
        }

        public override Task UpdateIfMatchAsync(
            string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default) =>
            _failing.Contains(id)
                ? throw new InvalidOperationException($"Test: the owner write to {entitySetName}({id}) failed.")
                : _inner.UpdateIfMatchAsync(entitySetName, id, entity, etag, cancellationToken);
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}
