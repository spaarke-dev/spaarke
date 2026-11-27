using System.Collections.Concurrent;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;
using Xunit;
using Commit = Sprk.Bff.Api.Tests.Integration.DataMutation.Signals.DecisionCommitServiceTests;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ISS-010 (#3184) part C, run 3: the access window's fault and scope matrix on the four-action decision, through the REAL commit
/// path (<see cref="BuildFourActionWorld"/>). The design's §6 must-fire tests on both wires, the 403 that is never kept past its
/// step, the faults that are never kept, the window's scope (closed before the Decision Record and the follow-ups on success,
/// failure and replay) and uac-r2's A3 wire-order assertion. Nothing here adds a hook to production code: the tests observe the
/// counting wires, the follow-up and writer seams, and the request's <c>Items</c>.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private const string ChildCreateBudget = "sprk_budget";

    /// <summary>A second request of the same caller on the same world (a retry from the client): same services, same claims.</summary>
    private static HttpContext NextRequest(FourActionWorld w)
    {
        var http = Commit.Http();
        ((ClaimsIdentity)http.User.Identity!).AddClaim(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Commit.CallerOid.ToString()));
        http.RequestServices = w.Http.RequestServices;
        return http;
    }

    private static Task<DecisionCommitResult> CommitOnAsync(FourActionWorld w, HttpContext http, bool retry = false)
    {
        new HttpContextAccessor().HttpContext = http;
        return w.Rig.CommitAsync(FourActionReview(PfReview) with { Retry = retry }, http: http);
    }

    // ── 1. The restamp must-fire, on both wires (design §6; uac-r2: "The restamp must-fire runs on BOTH wires") ───────────

    /// <summary>
    /// Must-fire (probe wire): step 1's write takes the caller's AppendTo on the matter away (the restamp to Secure or Restricted
    /// that follows its app-only create), so step 2 must ask the probe again and be refused, with step 1's one write landed. Fails
    /// if <see cref="DecisionAccessWindow.EndStep"/> stops forgetting the record-access answers: step 2 is then served window 0's
    /// "AppendTo held" and creates its event.
    /// </summary>
    [Fact]
    public async Task Iss010C_MustFire_AccessTakenAwayByStep1_IsReAskedOnTheProbeWire_AndStep2IsRefused()
    {
        var w = BuildFourActionWorld();
        _onAppCreate = (table, _, _) =>
        {
            if (table == ChildCreateBudget)
                w.Probe.NoRights.Add(OrdinaryMatter);
        };

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Failed, result.Detail);
        result.ActionCode.Should().Be("assign-task");
        result.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        _appCreates.Select(c => c.Table).Should().Equal(new[] { ChildCreateBudget }, "only step 1's write landed");
        w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess sprk_matters")
            .Should().Be(2, "window 0 asked once; step 2 asked the probe again after step 1 wrote");
    }

    /// <summary>
    /// Must-fire (route-core wire): the same, where the probe still allows and only the route core's own
    /// <c>RetrievePrincipalAccess</c> (the impersonated caller client) now says no. Step 2 must ask the route core's wire again and be
    /// refused. Fails if <see cref="DecisionAccessWindow.EndStep"/> stops forgetting the record-access answers: the route core is then
    /// served step 1's kept mask through <see cref="DecisionAccessWindowCallerClient"/> and creates the event.
    /// </summary>
    [Fact]
    public async Task Iss010C_MustFire_AccessTakenAwayByStep1_IsReAskedOnTheRouteCoreWire_AndStep2IsRefused()
    {
        var w = BuildFourActionWorld();
        _onAppCreate = (table, _, _) =>
        {
            if (table == ChildCreateBudget)
                w.User.NoAppendTo.Add(OrdinaryMatter);
        };

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Failed, result.Detail);
        result.ActionCode.Should().Be("assign-task");
        _appCreates.Select(c => c.Table).Should().Equal(new[] { ChildCreateBudget }, "only step 1's write landed");
        w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess sprk_matters")
            .Should().Be(2, "the probe still allows: it is not what refused step 2");
        w.Profile.Calls.Count(c => c.Seam == RouteSeam && c.Target.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                && c.Key.Contains("sprk_matters", StringComparison.Ordinal))
            .Should().Be(2, "step 1's route core asked once; step 2's asked the wire again");
    }

    // ── 2. A 403 is kept only within its step; a later grant is honoured ─────────────────────────────────────────────────

    /// <summary>
    /// Must-fire + must-not-fire: the assignee contact is granted in window 0 (FR-51 and every preflight pass), then step 1's write
    /// takes it away: step 2 re-asks, gets Dataverse's 403 (no rights) and is refused cleanly (nothing of its own written; the review
    /// can resume). The grant is restored and the client retries the same review on a NEW request: the 403 is not kept past its step
    /// or its commit, so the resumed review asks again, is allowed, and records the decision without repeating step 1.
    /// The first half fails if <see cref="DecisionAccessWindow.EndStep"/> stops forgetting the record-access answers (step 2 is
    /// served window 0's grant).
    /// </summary>
    /// <remarks>On the real commit path every caller-access check is fail-closed, so a denial always ends the commit at the step it is
    /// asked in; "denied, then granted later in the SAME commit" has no path. The resumed review is where a later grant meets the
    /// same decision. The window-level pair (a 403 kept within its step, asked again after it) is
    /// <c>DecisionAccessWindowTests.A403_IsKeptOnlyWithinItsStep</c>.</remarks>
    [Fact]
    public async Task Iss010C_A403AfterAStep_RefusesThatStep_AndALaterGrant_IsHonouredWhenTheReviewResumes()
    {
        var w = BuildFourActionWorld();
        _onAppCreate = (table, _, _) =>
        {
            if (table == ChildCreateBudget)
                w.Probe.NoRights.Add(PfAssignee);
        };

        var refused = await w.CommitAsync();

        refused.Status.Should().Be(DecisionCommitStatus.Failed, refused.Detail);
        refused.ActionCode.Should().Be("assign-task");
        refused.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        _appCreates.Select(c => c.Table).Should().Equal(new[] { ChildCreateBudget });
        w.Http.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty("the refused commit closed its window");

        w.Probe.NoRights.Remove(PfAssignee); // granted
        _onAppCreate = null;
        var contactAsksBefore = w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess contacts");
        var retryRequest = NextRequest(w);

        var resumed = await CommitOnAsync(w, retryRequest, retry: true);

        resumed.Status.Should().Be(DecisionCommitStatus.Recorded, resumed.Detail);
        _appCreates.Select(c => c.Table).Should().Equal(ChildCreateBudget, "sprk_event", "sprk_todo", "sprk_workassignment");
        w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess contacts")
            .Should().BeGreaterThan(contactAsksBefore, "the resumed review asked the contact's rights again");
        retryRequest.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty();
    }

    /// <summary>
    /// Must-not-fire: a 403 in window 0 is Dataverse's answer and is honoured: the assignee contact is denied from the start, the
    /// commit is Forbidden at the first preflight that asks, and nothing is written.
    /// </summary>
    [Fact]
    public async Task Iss010C_MustNotFire_A403InWindow0_RefusesTheCommit_BeforeAnyWrite()
    {
        var w = BuildFourActionWorld();
        w.Probe.NoRights.Add(PfAssignee);

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Forbidden, result.Detail);
        result.ActionCode.Should().Be("assign-task");
        _appCreates.Should().BeEmpty();
        w.Rig.Records.Should().BeEmpty();
        w.Http.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty();
    }

    // ── 3. Faults are never kept ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Must-fire: a non-success route-core answer is never kept. The route core's <c>RetrievePrincipalAccess</c> on an
    /// organisation-owned row (<c>sprk_recordtype_ref</c>, <c>transactioncurrency</c>) answers 400 (live: the message refuses the table),
    /// which the route core tolerates and falls back from; with the window, every one of those asks still reaches the wire, exactly as
    /// many as without the window, and the decision is recorded with the same writes. Fails if <see cref="DecisionAccessWindow.RouteGetAsync"/>
    /// keeps a response that is not a success (the second ask is served the kept 400 as if it were an answer).
    /// </summary>
    [Fact]
    public async Task Iss010C_MustFire_ARouteCoreFault_IsNeverKept_EveryOrganisationOwnedAskReachesTheWire()
    {
        static int OrgOwnedAsks(DecisionCommitCallProfile profile) =>
            profile.Calls.Count(c => c.Seam == RouteSeam && c.Target.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                && (c.Key.Contains("sprk_recordtype_refs", StringComparison.Ordinal) || c.Key.Contains("transactioncurrencies", StringComparison.Ordinal)));

        var without = BuildFourActionWorld(withoutAccessWindow: true);
        (await without.CommitAsync()).Status.Should().Be(DecisionCommitStatus.Recorded);
        var expected = OrgOwnedAsks(without.Profile);
        var expectedWrites = without.Profile.WriteTranscript();
        _appCreates.Clear();

        var w = BuildFourActionWorld();
        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        expected.Should().BeGreaterThan(1, "the decision asks the organisation-owned tables more than once");
        OrgOwnedAsks(w.Profile).Should().Be(expected, "a 400 is a fault, never an answer the window keeps");
        w.Profile.WriteTranscript().Should().Be(expectedWrites);
    }

    /// <summary>
    /// Must-fire: a probe fault is never kept and never an allow. The probe's privilege wire faults (503) once for the To Do's Create
    /// privilege in window 0: the commit is refused before any write. Retried on a new request with the fault gone, the privilege is
    /// asked on the wire again and the decision is recorded.
    /// </summary>
    [Fact]
    public async Task Iss010C_MustFire_AProbeFault_RefusesBeforeAnyWrite_AndIsAskedAgainOnTheRetry()
    {
        var w = BuildFourActionWorld();
        w.Probe.Faults.Add("prvCreatesprk_Todo");

        var faulted = await w.CommitAsync();

        faulted.Status.Should().NotBe(DecisionCommitStatus.Recorded, faulted.Detail);
        _appCreates.Should().BeEmpty("a fault is never an allow");
        var todoAsks = w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrieveUserSetOfPrivilegesByNames prvCreatesprk_Todo");

        w.Probe.Faults.Clear();
        var retried = await CommitOnAsync(w, NextRequest(w));

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrieveUserSetOfPrivilegesByNames prvCreatesprk_Todo")
            .Should().BeGreaterThan(todoAsks, "the faulted privilege was asked on the wire again");
    }

    // ── 4. Scope: the window is closed before the Decision Record and the follow-ups ───────────────────────────────────

    /// <summary>Whether the request carries an open window: served by <see cref="DecisionAccessWindow.Of"/>, or still in its slot.</summary>
    private static bool WindowOpen(HttpContext http) =>
        DecisionAccessWindow.Of(http) is not null || http.Items.Values.OfType<DecisionAccessWindow>().Any();

    /// <summary>A seam that notes, for each call, whether the request's window was open when it was made.</summary>
    public class WindowWatch : System.Reflection.DispatchProxy
    {
        private object _inner = null!;
        private Func<bool> _open = null!;
        private ConcurrentQueue<(string Method, string? Request, bool WindowOpen)> _log = null!;

        internal static T Wrap<T>(T inner, HttpContext http, ConcurrentQueue<(string, string?, bool)> log) where T : class
        {
            var proxy = Create<T, WindowWatch>();
            var self = (WindowWatch)(object)proxy;
            self._inner = inner;
            self._open = () => WindowOpen(http);
            self._log = log;
            return proxy;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            _log.Enqueue((targetMethod!.Name, args?.FirstOrDefault() is OrganizationRequest r ? r.RequestName : null, _open()));
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    /// <summary>
    /// Scope (success): the window is closed for step (5): the Decision Record write, the Signal closes and every follow-up
    /// (assignee notices, after-recorded) run with no window in the request. Fails if the close before step (5) is removed.
    /// </summary>
    [Fact]
    public async Task Iss010C_Scope_OnSuccess_TheDecisionRecordAndTheFollowUps_RunWithNoWindow()
    {
        var w = BuildFourActionWorld();
        var writer = new ConcurrentQueue<(string, string?, bool)>();
        w.Rig.OrgSeam = WindowWatch.Wrap(w.Rig.OrgSeam!, w.Http, writer);
        var followUps = new ConcurrentQueue<(string Name, bool Open)>();
        w.Rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .Callback(() => followUps.Enqueue(("notify", WindowOpen(w.Http))))
            .ReturnsAsync(Array.Empty<string>());
        w.Rig.FollowUps.Setup(f => f.AfterRecordedAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(),
                It.IsAny<IReadOnlyList<DecisionCommitService.SignalRow>>(), It.IsAny<CancellationToken>()))
            .Callback(() => followUps.Enqueue(("after-recorded", WindowOpen(w.Http))))
            .ReturnsAsync(Array.Empty<string>());

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var writes = writer.Where(c => c.Item1 == nameof(IOrganizationServiceAsync2.CreateAsync) || c.Item2 is "Update" or "Create").ToList();
        writes.Should().NotBeEmpty("the Decision Record and the Signal close are written through the writer");
        writes.Should().OnlyContain(c => !c.Item3, "the Decision Record and the Signal closes run with the window closed");
        followUps.Select(f => f.Name).Should().Contain(new[] { "notify", "after-recorded" });
        followUps.Should().OnlyContain(f => !f.Open, "every follow-up runs with the window closed");
    }

    /// <summary>
    /// Scope (failure): when a step fails, the failed-commit follow-up runs with no window in the request. Fails if the close on
    /// the failure branch is removed.
    /// </summary>
    [Fact]
    public async Task Iss010C_Scope_OnFailure_TheFollowUpRunsWithNoWindow()
    {
        var w = BuildFourActionWorld();
        w.User.Held.Remove("prvCreatesprk_workassignment"); // the route core refuses step 4 after three writes
        var seen = new ConcurrentQueue<bool>();
        w.Rig.FollowUps.Setup(f => f.AfterFailedCommitAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(), It.IsAny<CancellationToken>()))
            .Callback(() => seen.Enqueue(WindowOpen(w.Http)))
            .Returns(Task.CompletedTask);

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Failed, result.Detail);
        _appCreates.Should().HaveCount(3);
        seen.Should().ContainSingle().Which.Should().BeFalse("the failed-commit follow-up runs with the window closed");
        w.Http.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty();
    }

    /// <summary>
    /// Scope (replay): the ledger forgot the outcome and the record is found by its review id: the replay branch closes the
    /// still-open Signal with no window in the request. Fails if the close at the top of the replay branch is removed.
    /// </summary>
    [Fact]
    public async Task Iss010C_Scope_OnReplay_TheSignalCloseRunsWithNoWindow()
    {
        var w = BuildFourActionWorld();
        w.Rig.BeforeSignalUpdate = _ =>
        {
            w.Rig.BeforeSignalUpdate = null;
            throw new TimeoutException("no answer"); // the first run's close fails once, so the replay has a close to finish
        };
        (await w.CommitAsync()).Status.Should().NotBe(DecisionCommitStatus.Forbidden);
        var written = w.Rig.Records.Single();
        written.Id = Guid.Parse("c0430000-0000-4000-8000-000000000060");
        written["createdby"] = new EntityReference("systemuser", Guid.Parse("3121bf1b-9fbf-f111-aaaf-0022482913fc"));
        w.Rig.ExistingRecords.Add(written);
        await w.Rig.Ledger.RemoveAsync(Commit.Tenant, DecisionCommitService.LedgerResource, $"{Commit.CallerOid:D}:{PfReview:D}", DecisionCommitService.LedgerVersion);

        var replay = NextRequest(w);
        var watched = new ConcurrentQueue<(string, string?, bool)>();
        w.Rig.OrgSeam = WindowWatch.Wrap(w.Rig.OrgSeam!, replay, watched);

        var result = await CommitOnAsync(w, replay);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeTrue();
        var closes = watched.Where(c => c.Item2 == "Update").ToList();
        closes.Should().NotBeEmpty("the replay closes the Signal the first run left open");
        closes.Should().OnlyContain(c => !c.Item3, "the replay's writes run with the window closed");
        replay.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty();
    }

    // ── 5. A3: in wire order, no record-access answer is given after a step's create ──────────────────────────────────

    /// <summary>
    /// uac-r2 A3, at runtime, on the four-action decision: for each child-create step, in wire order, no record-access ((c))
    /// answer is given after that step's create. Observed from outside production code:
    /// <list type="bullet">
    /// <item>At each step's app-only create the test makes the window forget its record-access answers, so any later (c) ask of that
    /// step must reach a wire (the probe's or the route core's counting wire), where it is recorded in order.</item>
    /// <item>The step boundary is the window's own per-step reset: at the create the test plants a sentinel record-access answer
    /// in the window. While the sentinel is still kept, the step that created has not ended; the commit's
    /// <see cref="DecisionAccessWindow.EndStep"/> is what removes it.</item>
    /// </list>
    /// So a (c) ask reaching a wire after a create while that create's sentinel is still kept is an answer the window would have given
    /// after the create. Fails if a caller-access question is added after the create (in the executor's write, the route core, or
    /// the post-create services).
    /// </summary>
    [Fact]
    public async Task Iss010C_A3_InWireOrder_NoRecordAccessAnswerIsGiven_AfterAStepsCreate()
    {
        var w = BuildFourActionWorld();
        var sentinelProbe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };
        sentinelProbe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyList<(string EntitySet, Guid RecordId)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessRights?[] { AccessRights.AppendTo });
        int SentinelAsks() => sentinelProbe.Invocations.Count;

        var order = new ConcurrentQueue<(int Seq, string Event, bool SameStepAsCreate)>();
        var seq = 0;
        var gate = new object();
        (DecisionAccessWindow Window, Guid Sentinel, string Table)? landed = null;

        // Whether the step that made the last create is still running: its sentinel is still kept by the window.
        bool StillInTheCreatingStep()
        {
            if (landed is not { } l)
                return false;
            var before = SentinelAsks();
            l.Window.CallerRightsAsync(sentinelProbe.Object, w.Http, "sprk_a3sentinels", l.Sentinel, default).GetAwaiter().GetResult();
            if (SentinelAsks() == before)
                return true;
            landed = null; // the window forgot it: the commit ended that step
            return false;
        }

        void Ask(string what)
        {
            lock (gate)
                order.Enqueue((Interlocked.Increment(ref seq), what, StillInTheCreatingStep()));
        }

        w.Probe.OnPrincipalAccess = (set, _) => Ask($"probe {set}");
        w.User.GetOverride = path =>
        {
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
                Ask("route " + path[(path.LastIndexOf("odata.id", StringComparison.Ordinal) + 11)..path.LastIndexOf('(')]);
            return null;
        };
        _onAppCreate = (table, _, _) =>
        {
            lock (gate)
            {
                order.Enqueue((Interlocked.Increment(ref seq), "create " + table, true));
                var window = DecisionAccessWindow.Of(w.Http);
                window.Should().NotBeNull("a child create keeps the window serving through its create");
                window!.ForgetRecordAccess(); // from here, any ask of this step must reach a wire
                var sentinel = Guid.NewGuid();
                window.CallerRightsAsync(sentinelProbe.Object, w.Http, "sprk_a3sentinels", sentinel, default).GetAwaiter().GetResult();
                landed = (window, sentinel, table);
            }
        };

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var events = order.OrderBy(e => e.Seq).ToList();
        events.Where(e => e.Event.StartsWith("create ", StringComparison.Ordinal)).Select(e => e.Event)
            .Should().Equal("create sprk_budget", "create sprk_event", "create sprk_todo", "create sprk_workassignment");
        for (var k = 1; k < 4; k++)
        {
            var create = events.Where(e => e.Event.StartsWith("create ", StringComparison.Ordinal)).ElementAt(k);
            events.Should().Contain(e => e.Seq < create.Seq && e.Event.StartsWith("route ", StringComparison.Ordinal)
                    && events.Count(c => c.Seq < e.Seq && c.Event.StartsWith("create ", StringComparison.Ordinal)) == k,
                $"the control: step {k + 1}'s route core asks its (c) questions on the wire before its create");
        }

        events.Where(e => !e.Event.StartsWith("create ", StringComparison.Ordinal) && e.SameStepAsCreate)
            .Select(e => $"#{e.Seq} {e.Event}")
            .Should().BeEmpty("no record-access answer is given after a step's create, in wire order");
        events.Count(e => e.Event.StartsWith("probe ", StringComparison.Ordinal))
            .Should().Be(w.Profile.Calls.Count(c => c.Seam == ProbeSeam && c.Target.StartsWith("RetrievePrincipalAccess", StringComparison.Ordinal)),
                "every probe (c) ask at the wire was observed");
    }
}
