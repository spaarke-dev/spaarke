using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 147 (C10 part 2, client and non-product writers): the L4 net for every child written
/// where no BFF code runs. That covers out-of-the-box forms, quick create, editable grids, imports, flows, and every client
/// writer still on <c>Xrm.WebApi</c>. Each such child is created owned by its creator, in the creator's business unit. The
/// secure-child reconciliation job's RECENT-CHANGES pass lists the child rows changed since its watermark. It finds the
/// secure records they sit under (the resolver's own upward walk) and reconciles those records first, in the same run,
/// whether or not the capped sweep window reaches them.
/// </summary>
/// <remarks>
/// Driven through the REAL job, the REAL reconciler, the REAL ownership resolver and the REAL share synchronizer, over the
/// in-memory Dataverse (<see cref="SecureChildShareWorld"/>), which evaluates every query, the <c>modifiedon</c> filter
/// included. Every test that expects a write also seeds a decoy and asserts that it is never written.
/// </remarks>
public class ClientChildFixUpTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid Sharee = Guid.Parse("b0000000-0000-0000-0000-0000000000b2");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    /// <summary>Three secure work assignments, ordered by id, so the sweep window (cap 1) is predictable.</summary>
    private static Guid[] OrderedRoots(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).OrderBy(g => g).ToArray();

    /// <summary>
    /// The SHIPPING STATE (owner round 28 item 2: "the L4 recent-changes pass runs with writes ON every 2 minutes") - not an
    /// ADR-038 B3 wiring test: perturb the cron or the enabled flag and it reddens (the NoAccessShareReconciliation
    /// precedent).
    /// </summary>
    [Fact]
    public void TheJobShipsEnabled_EveryTwoMinutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == SecureChildReconciliationJob.JobIdConstant);

        registration.CronSchedule.Should().Be("*/2 * * * *");
        registration.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// AC (job watermark pass): a to-do created outside the product (user-owned, in the creator's business unit) under a
    /// secure record is re-owned to the Secure team in the NEXT run, with the record's sharee mirrored onto it. This holds
    /// even though the capped sweep window (one record) covers a different record in that run. The run's report names the
    /// record under <c>recentChanges</c> and lists the change with the row's previous owner. An ordinary record's
    /// user-owned to-do and an unfiled user-owned document, both changed in the same window, are never written.
    /// </summary>
    [Fact]
    public async Task ANonProductChildOfASecureRecord_IsReownedAndMirrored_InTheNextRun_EvenOutsideTheSweepWindow()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (first, target) = (roots[0], roots[1]);
        var (todo, ordinaryProject, ordinaryTodo, unfiled) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", first).SecureRoot("sprk_workassignment", target)
            .OrdinaryRoot("sprk_project", ordinaryProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(3))
            .UserOwnedChild("sprk_todo", ordinaryTodo, ("sprk_regardingproject", "sprk_project", ordinaryProject))
            .Modified("sprk_todo", ordinaryTodo, MinutesAgo(3))
            .UserOwnedChild("sprk_document", unfiled)
            .Modified("sprk_document", unfiled, MinutesAgo(3));
        job.Shares.Seed("sprk_workassignment", target, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        report.GetProperty("rootsInRun").GetInt32().Should().Be(1, "the sweep window is the first record only");
        report.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{first:D}");
        var recent = report.GetProperty("recentChanges");
        recent.GetProperty("rowsChanged").GetInt32().Should().Be(3, "the three changed rows not owned by the Secure team");
        recent.GetProperty("roots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_workassignment:{target:D}");

        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "a child created outside the product under a secure record is isolated within one run");
        job.Shares.MaskOf("sprk_todo", todo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the record's sharee keeps sight of it (task 149's mirror, run by the same pass)");
        var listed = report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo);
        listed.GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.Changed));
        listed.GetProperty("previousOwner").GetString().Should().Be($"systemusers({SecureChildShareWorld.SomeUser:D})");

        job.World.OwnerOf("sprk_todo", ordinaryTodo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "a child of an ORDINARY record keeps today's ownership");
        job.World.OwnerOf("sprk_document", unfiled).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "an unfiled document is unchanged");
        job.World.OwnerWrites.Should().NotContain(w => w.Id == ordinaryTodo || w.Id == unfiled);
        job.LastResult!.Success.Should().BeTrue();
    }

    /// <summary>
    /// AC (memo through an event): a memo created in Notepad or the event side pane, as the user, regarding an EVENT of a
    /// secure project is re-owned to the Secure team. It is found through the event, a hop the memo's own lookups do not
    /// name.
    /// </summary>
    [Fact]
    public async Task AMemoRegardingAnEventOfASecureProject_IsReownedThroughTheEvent()
    {
        var job = new JobHarness();
        var (project, @event, memo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_project", project)
            .SecureChild("sprk_event", @event, ("sprk_regardingproject", "sprk_project", project))
            .UserOwnedChild("sprk_memo", memo, ("sprk_regardingevent", "sprk_event", @event))
            .Modified("sprk_memo", memo, MinutesAgo(1));

        var report = await job.RunAsync(writesEnabled: "true", webApiNeeded: true);

        job.World.OwnerOf("sprk_memo", memo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        report.GetProperty("recentChanges").GetProperty("roots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_project:{project:D}");
    }

    /// <summary>
    /// Round 28 item 2 (task 147 r1): with NO configuration at all — the deployed default — the recent-changes pass WRITES
    /// (the L4 net is on in every environment where 148 is deployed) while the sweep stays report-only. The correction is
    /// the standing report: listed in <c>changes</c> with <c>pass: "recent"</c> and counted in
    /// <c>recentChanges.corrected</c>.
    /// </summary>
    [Fact]
    public async Task WithNoConfiguration_TheRecentPassWrites_AndReportsTheCorrection_WhileTheSweepStaysReportOnly()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (swept, target, sweptTodo, todo) = (roots[0], roots[1], Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", swept).SecureRoot("sprk_workassignment", target)
            // In the sweep window, never modified: only the (report-only) sweep reaches it.
            .UserOwnedChild("sprk_todo", sweptTodo, ("sprk_regardingworkassignment", "sprk_workassignment", swept))
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(2));

        var report = await job.RunAsync(writesEnabled: null, maxRootsPerRun: "1");

        report.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly, "the sweep's own mode");
        var recent = report.GetProperty("recentChanges");
        recent.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        recent.GetProperty("corrected").GetInt32().Should().Be(1);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo)
            .GetProperty("pass").GetString().Should().Be("recent");
        job.World.OwnerOf("sprk_todo", sweptTodo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "the sweep window is report-only until the owner turns the backfill's writes on");
        report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == sweptTodo)
            .GetProperty("pass").GetString().Should().Be("sweep");
    }

    /// <summary>
    /// The emergency stop (<c>RecentChangesWritesEnabled=false</c>): the recent-changes pass plans the change
    /// (<c>wouldChange</c>, the row's current owner listed) and writes nothing, owners and shares alike. A pass that wrote
    /// nothing does not move the watermark, so the first run after the stop is lifted still sees the change and corrects it.
    /// </summary>
    [Fact]
    public async Task TheEmergencyStop_PlansTheRecentChange_AndWritesNothing_AndKeepsTheWindow()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2));
        job.Shares.Seed("sprk_workassignment", root, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await job.RunAsync(writesEnabled: null, recentWritesEnabled: "false");

        report.GetProperty("recentChanges").GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        report.GetProperty("wouldChange").GetInt32().Should().Be(1);
        job.World.OwnerWrites.Should().BeEmpty();
        job.Shares.WriteLog.Should().BeEmpty();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));

        var again = await job.RunAsync(writesEnabled: null, recentWritesEnabled: "false");
        again.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(1);

        await job.RunAsync(writesEnabled: null);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// A SCHEDULED tick with the sweep report-only is the L4 net alone: it runs the recent-changes pass, takes no sweep
    /// window and leaves the backfill's cursor where it was, so the next manual (backfill) run begins where the previous
    /// manual run stopped.
    /// </summary>
    [Fact]
    public async Task AScheduledTick_WithTheSweepReportOnly_RunsOnlyTheRecentPass_AndLeavesTheSweepCursor()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(3);
        foreach (var root in roots)
            job.World.SecureRoot("sprk_workassignment", root);
        var todo = Guid.NewGuid();
        job.World.UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", roots[2]));

        var manual = await job.RunAsync(writesEnabled: null, maxRootsPerRun: "1");
        manual.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{roots[0]:D}");

        job.World.Modified("sprk_todo", todo, MinutesAgo(1));
        var scheduled = await job.RunAsync(writesEnabled: null, maxRootsPerRun: "1", trigger: JobRunTrigger.Scheduled);

        scheduled.GetProperty("sweepRan").GetBoolean().Should().BeFalse();
        scheduled.GetProperty("rootsInRun").GetInt32().Should().Be(0);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the scheduled tick is the L4 net: the recent-changes pass writes");

        var next = await job.RunAsync(writesEnabled: null, maxRootsPerRun: "1");
        next.GetProperty("startPosition").GetInt32().Should().Be(2, "the scheduled tick did not move the backfill's cursor");
    }

    /// <summary>
    /// The watermark moves: a run that completed sets it to its own start (minus the overlap), so the next run does not list
    /// a change the previous run already handled. A row already owned by the Secure team is never listed: it needs no move,
    /// and its shares belong to task 149's job, so active editing of isolated rows triggers no extra pass.
    /// </summary>
    [Fact]
    public async Task TheWatermarkAdvancesPastACompletedRun_AndIsolatedRowsAreNeverListed()
    {
        var job = new JobHarness();
        var (root, todo, isolated, ordinaryProject, ordinaryTodo, untouched) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root).OrdinaryRoot("sprk_project", ordinaryProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(10))
            .SecureChild("sprk_event", isolated, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_event", isolated, MinutesAgo(10))
            // Stays user-owned (an ordinary record's child), so it is still listable on the second run if the
            // watermark did not move.
            .UserOwnedChild("sprk_todo", ordinaryTodo, ("sprk_regardingproject", "sprk_project", ordinaryProject))
            .Modified("sprk_todo", ordinaryTodo, MinutesAgo(10))
            // Never modified (no modifiedon): never listed.
            .UserOwnedChild("sprk_todo", untouched, ("sprk_regardingproject", "sprk_project", ordinaryProject));

        var first = await job.RunAsync(writesEnabled: "true");
        var second = await job.RunAsync(writesEnabled: "true");

        first.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(2,
            "the two user-owned to-dos changed in the window; the Secure-team-owned event needs no move, and the to-do " +
            "that did not change is not listed");
        second.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(0,
            "a change before the previous run's watermark is not listed again");
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// Fail closed (ADR-003): when the changed rows cannot be listed, the run reports the failure and is not a success, and
    /// its watermark stays where it was. The next run, with the fault cleared, looks at the SAME window and corrects the
    /// child. A record outside the sweep window is used, so only the recent-changes pass can reach it.
    /// </summary>
    [Fact]
    public async Task AListingThatFails_IsReported_AndTheWindowIsLookedAtAgainNextRun()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(3);
        var target = roots[2];
        var todo = Guid.NewGuid();
        foreach (var root in roots)
            job.World.SecureRoot("sprk_workassignment", root);
        job.World.UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(10))
            .FailingQueriesOf("sprk_agreement");

        var failed = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        failed.GetProperty("recentChanges").GetProperty("failure").GetString().Should().NotBeNullOrEmpty();
        job.LastResult!.Success.Should().BeFalse();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));

        job.World.ClearQueryFaults();
        var retried = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        retried.GetProperty("rootsInRun").GetInt32().Should().Be(1);
        retried.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{roots[1]:D}",
            "the sweep window is the second record, so the target is reached by the recent-changes pass alone");
        retried.GetProperty("recentChanges").GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the failed run's window was looked at again, not skipped");
    }

    /// <summary>
    /// ADR-003, never a silent skip: a changed child under a record flagged secure whose provisioning did not complete
    /// (still owned by an ordinary team) cannot be placed. It is listed under <c>recentChanges.undetermined</c>, the run is
    /// not a success, and the child is not written. The flagged record lies OUTSIDE this run's sweep window, so only the
    /// recent-changes report can make the run fail.
    /// </summary>
    [Fact]
    public async Task AChangedChildUnderAFlaggedButNotIsolatedRecord_IsReportedUndetermined_AndNotWritten()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (secure, flagged, todo) = (roots[0], roots[1], Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", secure).FlaggedNotIsolatedRoot("sprk_workassignment", flagged)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", flagged))
            .Modified("sprk_todo", todo, MinutesAgo(2));

        var report = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(0, "the sweep window held only the isolated record");
        var undetermined = report.GetProperty("recentChanges").GetProperty("undetermined").EnumerateArray()
            .Select(u => u.GetString()).ToList();
        undetermined.Should().ContainSingle().Which.Should().Contain(todo.ToString("D"));
        job.LastResult!.Success.Should().BeFalse();
        job.LastResult.ErrorMessage.Should().Contain(todo.ToString("D"));
        job.World.OwnerWrites.Should().NotContain(w => w.Id == todo);
    }

    /// <summary>
    /// A changed child whose re-own Dataverse refuses is reported: its record is incomplete, the row is listed Failed with
    /// its previous owner, and the run is not a success. It is never left user-owned without appearing in the report.
    /// </summary>
    [Fact]
    public async Task AChangedChildWhoseReownIsRefused_IsReportedFailed_AndTheRunIsNotASuccess()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .RefusingOwnerWritesOf(todo);

        var report = await job.RunAsync(writesEnabled: "true");

        report.GetProperty("failed").GetInt32().Should().Be(1);
        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(1);
        report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo)
            .GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.Failed));
        job.LastResult!.Success.Should().BeFalse();
    }

    /// <summary>
    /// Task 147 r1 (verifier item 4): a recently changed record whose pass came back incomplete is NOT reported once and
    /// then left to the capped sweep. It is carried: every following run reconciles it again first (and reports it again
    /// while it still fails), and the first run after the fault clears corrects the child — even though the child has not
    /// changed again (it is behind the watermark) and the record lies outside every sweep window those runs take.
    /// </summary>
    [Fact]
    public async Task ARefusedReown_IsRetriedEveryRun_UntilItSucceeds_OutsideTheSweepWindow()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(4);
        var target = roots[3];
        var todo = Guid.NewGuid();
        foreach (var root in roots)
            job.World.SecureRoot("sprk_workassignment", root);
        job.World.UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .RefusingOwnerWritesOf(todo);

        var first = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");
        first.GetProperty("recentChanges").GetProperty("carriedRoots").GetInt32().Should().Be(1);

        var second = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");
        second.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(0,
            "the child has not changed again: it is behind the watermark");
        second.GetProperty("recentChanges").GetProperty("retriedRoots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_workassignment:{target:D}");
        second.GetProperty("failed").GetInt32().Should().Be(1, "still refused, so it is reported again");
        job.LastResult!.Success.Should().BeFalse();

        job.World.ClearOwnerWriteFaults();
        var third = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        third.GetProperty("resumeAfter").GetString().Should().NotBe($"sprk_workassignment:{target:D}",
            "the sweep window has not reached the record");
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        third.GetProperty("recentChanges").GetProperty("carriedRoots").GetInt32().Should().Be(0);
        job.LastResult!.Success.Should().BeTrue();
    }

    /// <summary>
    /// Task 147 r1c-v1 (verifier item 4): a carried record that has been UNSECURED since (its flag cleared, its owner back
    /// to an ordinary team — what <c>/unsecure-project</c> leaves) is dropped: the L4 net, whose Sweep trigger only moves
    /// rows into isolation, never reconciles it again, never carries it on, and names it in <c>droppedRoots</c> rather than
    /// letting it leave the carried set silently.
    /// </summary>
    [Fact]
    public async Task ACarriedRecordNoLongerFlaggedSecure_IsDropped_NotReconciledAgain_AndReported()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .RefusingOwnerWritesOf(todo);

        var refused = await job.RunAsync(writesEnabled: null);
        refused.GetProperty("recentChanges").GetProperty("carriedRoots").GetInt32().Should().Be(1);

        // The record is unsecured before the next run (and the fault that refused the re-own clears).
        job.World.Set("sprk_workassignment", root, "sprk_issecure", false);
        job.World.MoveOwner("sprk_workassignment", root, DataversePrincipalRef.Team(GeneralTeam));
        job.World.ClearOwnerWriteFaults();
        var writesBefore = job.World.OwnerWrites.Count;

        var next = await job.RunAsync(writesEnabled: null);

        job.LastResult!.ProcessedItems.Should().Be(0, "a record no longer flagged secure is not reconciled by the L4 net");
        next.GetProperty("examined").GetInt32().Should().Be(0);
        job.World.OwnerWrites.Should().HaveCount(writesBefore, "nothing under the unsecured record is written");
        var recent = next.GetProperty("recentChanges");
        recent.GetProperty("carriedRoots").GetInt32().Should().Be(0, "it is not carried on");
        recent.GetProperty("retriedRoots").GetArrayLength().Should().Be(0, "it was not looked at again");
        recent.GetProperty("droppedRoots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_workassignment:{root:D}");
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        job.LastResult.Success.Should().BeTrue();
    }

    /// <summary>
    /// Task 147 r1c-v1 (verifier item 3): the job carries at most <see cref="SecureChildReconciliationJob.MaxCarriedRows"/>
    /// unplaced rows between runs. Past that, the WATERMARK HOLDS, so the next run lists the whole window again and a row
    /// past the cap is never dropped from the L4 net. 1,000 events under one record flagged secure but not yet isolated
    /// fill the carried set (they list first: <c>sprk_event</c> sorts before <c>sprk_todo</c>); the 1,001st unplaced row, a
    /// to-do under a SECOND such record, is not carried. Once both records' provisioning completes, every row is corrected
    /// — the to-do included, which only the re-listed window can still reach (the sweep is report-only, no catch-up runs on
    /// a manual trigger, and no carried row is filed under its record).
    /// </summary>
    [Fact]
    public async Task MoreUnplacedRowsThanTheJobCarries_HoldTheWatermark_SoTheRowPastTheCapIsNeverDropped()
    {
        var job = new JobHarness();
        var (crowded, other, pastTheCap) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.FlaggedNotIsolatedRoot("sprk_workassignment", crowded).FlaggedNotIsolatedRoot("sprk_workassignment", other);
        var events = Enumerable.Range(0, SecureChildReconciliationJob.MaxCarriedRows).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var @event in events)
        {
            job.World.UserOwnedChild("sprk_event", @event, ("sprk_regardingworkassignment", "sprk_workassignment", crowded))
                .Modified("sprk_event", @event, MinutesAgo(10));
        }
        job.World.UserOwnedChild("sprk_todo", pastTheCap, ("sprk_regardingworkassignment", "sprk_workassignment", other))
            .Modified("sprk_todo", pastTheCap, MinutesAgo(10));

        var first = await job.RunAsync(writesEnabled: null);

        var firstRecent = first.GetProperty("recentChanges");
        firstRecent.GetProperty("rowsChanged").GetInt32().Should().Be(SecureChildReconciliationJob.MaxCarriedRows + 1);
        firstRecent.GetProperty("pendingOverflow").GetBoolean().Should().BeTrue();
        firstRecent.GetProperty("carriedRows").GetInt32().Should().Be(SecureChildReconciliationJob.MaxCarriedRows);
        job.LastResult!.Success.Should().BeFalse("unplaced rows are reported");

        var second = await job.RunAsync(writesEnabled: null);
        second.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(
            SecureChildReconciliationJob.MaxCarriedRows + 1,
            "the watermark held while the carried rows overflowed, so the whole window is listed again — the row past " +
            "the cap included");

        // Both records' provisioning completes: they are isolated now.
        job.World.MoveOwner("sprk_workassignment", crowded, DataversePrincipalRef.Team(SecureTeam));
        job.World.MoveOwner("sprk_workassignment", other, DataversePrincipalRef.Team(SecureTeam));
        var third = await job.RunAsync(writesEnabled: null);

        job.World.OwnerOf("sprk_todo", pastTheCap).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the row past the carried cap is still in the L4 net");
        events.Should().OnlyContain(e => job.World.OwnerOf("sprk_event", e) == DataversePrincipalRef.Team(SecureTeam));
        third.GetProperty("recentChanges").GetProperty("pendingOverflow").GetBoolean().Should().BeFalse();
        third.GetProperty("recentChanges").GetProperty("carriedRows").GetInt32().Should().Be(0);
    }

    // ── Task 147 r1c: the CATCH-UP — what an instance carried is lost on a restart, so a new instance walks every secure
    //    record once before it trusts its watermark alone ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A refused re-own is carried in the job's memory (r1). A restart (deploy, scale-out) starts an instance that does not
    /// have it, and the child has not changed again — it is behind any lookback. The new instance's catch-up walks every
    /// secure record once, so the child is still corrected; the catch-up's corrections are reported with pass "catch-up".
    /// </summary>
    [Fact]
    public async Task ARefusedReownCarriedBeforeARestart_IsCorrectedByTheNewInstancesCatchUp()
    {
        var before = new JobHarness();
        var roots = OrderedRoots(3);
        var (target, todo, ordinaryProject, ordinaryTodo) = (roots[2], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var root in roots)
            before.World.SecureRoot("sprk_workassignment", root);
        before.World.UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .RefusingOwnerWritesOf(todo)
            .OrdinaryRoot("sprk_project", ordinaryProject)
            .UserOwnedChild("sprk_todo", ordinaryTodo, ("sprk_regardingproject", "sprk_project", ordinaryProject))
            .Modified("sprk_todo", ordinaryTodo, MinutesAgo(600));

        var refused = await before.RunAsync(writesEnabled: null);
        refused.GetProperty("recentChanges").GetProperty("carriedRoots").GetInt32().Should().Be(1);

        // Time passes, the fault clears, the instance restarts: the child is now far behind the initial lookback.
        before.World.ClearOwnerWriteFaults().Modified("sprk_todo", todo, MinutesAgo(600));
        var after = new JobHarness(before.World, before.Shares);

        var first = await after.RunAsync(writesEnabled: null, maxRootsPerRun: "2", trigger: JobRunTrigger.Scheduled);
        first.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(0,
            "nothing changed since the new instance's lookback: only the catch-up can reach the child");
        first.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("ran").GetBoolean().Should().BeTrue();
        first.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("complete").GetBoolean().Should().BeFalse();
        after.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "two records a run, from the first: the third is the next run's");

        var second = await after.RunAsync(writesEnabled: null, maxRootsPerRun: "2", trigger: JobRunTrigger.Scheduled);

        after.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        var catchUp = second.GetProperty("recentChanges").GetProperty("catchUp");
        catchUp.GetProperty("complete").GetBoolean().Should().BeTrue();
        second.GetProperty("recentChanges").GetProperty("corrected").GetInt32().Should().Be(1);
        second.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo)
            .GetProperty("pass").GetString().Should().Be("catch-up");
        second.GetProperty("sweepRan").GetBoolean().Should().BeFalse("the sweep stays report-only and off the schedule");
        after.World.OwnerWrites.Should().NotContain(w => w.Id == ordinaryTodo, "an ordinary record's child is never written");
        after.LastResult!.Success.Should().BeTrue();

        var third = await after.RunAsync(writesEnabled: null, maxRootsPerRun: "2", trigger: JobRunTrigger.Scheduled);
        third.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("ran").GetBoolean().Should().BeFalse(
            "an instance walks every record ONCE; then its watermark alone decides");
    }

    /// <summary>
    /// The catch-up is part of the SCHEDULED net. A manual run is the backfill script's review run, whose report-only plan
    /// must not be applied under the operator's eyes; and while the sweep itself writes, its window covers every record in
    /// order, so the catch-up stands aside.
    /// </summary>
    [Fact]
    public async Task TheCatchUp_DoesNotRunOnAManualTrigger_NorWhileTheSweepWrites()
    {
        var job = new JobHarness();
        var root = Guid.NewGuid();
        var todo = Guid.NewGuid();
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(600));

        var manual = await job.RunAsync(writesEnabled: null);
        manual.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("ran").GetBoolean().Should().BeFalse();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "the review run plans; it never writes");

        var sweeping = await job.RunAsync(writesEnabled: "true", trigger: JobRunTrigger.Scheduled);
        sweeping.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("ran").GetBoolean().Should().BeFalse();
        sweeping.GetProperty("sweepRan").GetBoolean().Should().BeTrue();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam), "the writing sweep did it");
    }

    /// <summary>
    /// Under the emergency stop (RecentChangesWritesEnabled=false) the catch-up plans and writes nothing, and does NOT
    /// advance: a report-only walk corrected nothing, so the first run with writes on starts the walk from the first record.
    /// </summary>
    [Fact]
    public async Task TheCatchUp_UnderTheEmergencyStop_WritesNothing_AndDoesNotAdvance()
    {
        var job = new JobHarness();
        var root = Guid.NewGuid();
        var todo = Guid.NewGuid();
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(600));

        var stopped = await job.RunAsync(writesEnabled: null, recentWritesEnabled: "false", trigger: JobRunTrigger.Scheduled);

        stopped.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("ran").GetBoolean().Should().BeTrue();
        stopped.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("complete").GetBoolean().Should().BeFalse();
        stopped.GetProperty("wouldChange").GetInt32().Should().Be(1);
        job.World.OwnerWrites.Should().BeEmpty();

        var resumed = await job.RunAsync(writesEnabled: null, trigger: JobRunTrigger.Scheduled);
        resumed.GetProperty("recentChanges").GetProperty("catchUp").GetProperty("complete").GetBoolean().Should().BeTrue();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// Task 147 r1 (verifier item 4): a changed row the walk cannot place (its record is flagged secure but not isolated)
    /// is carried and looked at again in EVERY run, reported each time, not only in the run that listed it. Once the record
    /// is isolated (its provisioning completed) the next run places the row and moves it, with no further edit to the row.
    /// </summary>
    [Fact]
    public async Task AnUndeterminedRow_IsReportedEveryRun_AndPlacedOnceItsRecordIsIsolated()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (secure, flagged, todo) = (roots[0], roots[1], Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", secure).FlaggedNotIsolatedRoot("sprk_workassignment", flagged)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", flagged))
            .Modified("sprk_todo", todo, MinutesAgo(2));

        await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");
        job.LastResult!.Success.Should().BeFalse();

        var second = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");
        second.GetProperty("recentChanges").GetProperty("retriedRows").GetInt32().Should().Be(1);
        second.GetProperty("recentChanges").GetProperty("undetermined").EnumerateArray().Select(u => u.GetString())
            .Should().ContainSingle().Which.Should().Contain(todo.ToString("D"));
        job.LastResult!.Success.Should().BeFalse("an unplaced row is reported in every run until it is placed");

        // The record's provisioning completes: it is now isolated.
        job.World.MoveOwner("sprk_workassignment", flagged, DataversePrincipalRef.Team(SecureTeam));
        var third = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        third.GetProperty("recentChanges").GetProperty("carriedRows").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Task 147 r1 (verifier item 4): a changed row under a MISSING ancestor is reported in every run it stays that way —
    /// never once and then forgotten — and drops out once the row itself is gone.
    /// </summary>
    [Fact]
    public async Task ARowUnderAMissingAncestor_IsReportedEveryRun_AndDropsOutWhenDeleted()
    {
        var job = new JobHarness();
        var (missing, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", Guid.NewGuid())
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", missing))
            .Modified("sprk_todo", todo, MinutesAgo(2));

        await job.RunAsync(writesEnabled: "true");
        var second = await job.RunAsync(writesEnabled: "true");

        second.GetProperty("recentChanges").GetProperty("undetermined").EnumerateArray().Select(u => u.GetString())
            .Should().ContainSingle().Which.Should().Contain("does not exist");
        job.LastResult!.Success.Should().BeFalse();

        job.World.Remove("sprk_todo", todo);
        var third = await job.RunAsync(writesEnabled: "true");
        third.GetProperty("recentChanges").GetProperty("undetermined").GetArrayLength().Should().Be(0);
        third.GetProperty("recentChanges").GetProperty("carriedRows").GetInt32().Should().Be(0);
        job.LastResult!.Success.Should().BeTrue();
    }

    /// <summary>
    /// Task 147 r1 (verifier item 5): the synchronizer answering FAILED (a second "Secure Record Owners" team appears between
    /// the job's own team check and the synchronizer's) is a failure of the pass — reported, the run unsuccessful, nothing
    /// written, and the window kept — never mapped to "no failure".
    /// </summary>
    [Fact]
    public async Task TheSynchronizerAnsweringFailed_IsReported_NothingIsWritten_AndTheWindowIsKept()
    {
        var job = new JobHarness();
        var (root, todo, duplicate) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .AfterQueriesOf("team", 1, w => w.Team(duplicate, SecureChildShareWorld.SecureBu,
                SecureChildShareWorld.SecureOwnerTeamName, isDefault: false, teamType: 0));

        var report = await job.RunAsync(writesEnabled: null);

        report.GetProperty("recentChanges").GetProperty("failure").GetString().Should().NotBeNullOrEmpty();
        job.LastResult!.Success.Should().BeFalse();
        job.World.OwnerWrites.Should().BeEmpty();

        job.World.Remove("team", duplicate);
        await job.RunAsync(writesEnabled: null);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the failed run kept its window, so the next run still lists and corrects the child");
    }

    /// <summary>
    /// Task 147 r1 (verifier item 5): the job's OWN team check refusing (two "Secure Record Owners" teams from the start) is
    /// reported as the pass's failure; nothing is written and the run is not a success.
    /// </summary>
    [Fact]
    public async Task TheJobsOwnTeamCheckRefusing_IsReported_AndNothingIsWritten()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .Team(Guid.NewGuid(), SecureChildShareWorld.SecureBu, SecureChildShareWorld.SecureOwnerTeamName,
                isDefault: false, teamType: 0);

        var report = await job.RunAsync(writesEnabled: null);

        report.GetProperty("recentChanges").GetProperty("failure").GetString().Should().NotBeNullOrEmpty();
        report.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(0, "nothing was listed");
        job.LastResult!.Success.Should().BeFalse();
        job.World.OwnerWrites.Should().BeEmpty();
    }

    /// <summary>
    /// The job over a world, built as the scheduler builds it: ONE job instance (it keeps its cursor and its watermark),
    /// with the real reconciler and the real synchronizer resolved per run from a scope. Work-assignment roots cascade
    /// nothing on Assign, so the platform-cascade Web API client is never called. A project or matter root needs a Web
    /// API client that answers "no cascade rows" (<c>webApiNeeded</c>).
    /// </summary>
    private sealed class JobHarness
    {
        public JobHarness()
            : this(SecureChildShareWorld.Standard(), new FakeRecordShareTable())
        {
        }

        /// <summary>Task 147 r1c: a NEW job instance (a restart, a deploy, a scale-out) over the SAME Dataverse.</summary>
        public JobHarness(SecureChildShareWorld world, FakeRecordShareTable shares) => (World, Shares) = (world, shares);

        public SecureChildShareWorld World { get; }
        public FakeRecordShareTable Shares { get; }
        public JobRunResult? LastResult { get; private set; }
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private SecureChildReconciliationJob? _job;
        private ServiceProvider? _provider;

        public async Task<JsonElement> RunAsync(
            string? writesEnabled, string? maxRootsPerRun = null, bool webApiNeeded = false,
            string? recentWritesEnabled = null, JobRunTrigger trigger = JobRunTrigger.ManualAdmin)
        {
            if (_job is null)
            {
                var services = new ServiceCollection();
                services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => World).Object);
                var webApi = webApiNeeded ? NoCascadeRowsWebApi.Create() : null;
                services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => World, Shares, webApi!));
                services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => World, Shares));
                services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => World)); // task 173: as the host registers it
                _provider = services.BuildServiceProvider();
                _job = new SecureChildReconciliationJob(
                    _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _configuration,
                    NullLogger<SecureChildReconciliationJob>.Instance);
            }

            _configuration[SecureChildReconciliationJob.WritesEnabledConfigKey] = writesEnabled;
            _configuration[SecureChildReconciliationJob.MaxRootsPerRunConfigKey] = maxRootsPerRun;
            _configuration[SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey] = recentWritesEnabled;
            LastResult = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", trigger, new Dictionary<string, object>()),
                CancellationToken.None);

            using var doc = JsonDocument.Parse(LastResult.ResultJson!);
            return doc.RootElement.Clone();
        }
    }

    /// <summary>
    /// A Web API client double for a project or matter root: the platform's Assign-cascade tables (SharePoint document
    /// locations and documents) answer with no rows. Any write through it throws, because this test expects none.
    /// </summary>
    private static class NoCascadeRowsWebApi
    {
        public static DataverseWebApiClient Create()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Graph:ManagedIdentity:Enabled"] = "true",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb",
            }).Build();
            var client = new Moq.Mock<DataverseWebApiClient>(
                Moq.MockBehavior.Strict, config, NullLogger<DataverseWebApiClient>.Instance, null!, null!);
            client
                .Setup(c => c.QueryAsync<Moq.It.IsAnyType>(
                    Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(),
                    Moq.It.IsAny<int?>(), Moq.It.IsAny<int?>(), Moq.It.IsAny<CancellationToken>()))
                .Returns(new Moq.InvocationFunc(invocation =>
                {
                    var listType = typeof(List<>).MakeGenericType(invocation.Method.GetGenericArguments()[0]);
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(listType)
                        .Invoke(null, new[] { Activator.CreateInstance(listType) })!;
                }));
            return client.Object;
        }
    }
}
