using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// #3014 (DI-164, ontology task 209): the nightly Off/Retired close (D-170) reads Signals only for the not-in-force policies that still
/// have an open Signal. Before: after the paged policy read (task 204) it sent one <c>sprk_signal</c> query per Off or Retired policy, and
/// ebilling switches record rules Off on every core-record delete, so the close's Signal reads grew without bound while most of those
/// policies had nothing open. Now one paged read of the open Signals joined to their not-in-force policy names the policies to close, and
/// the per-policy selection and close run for those only. Which Signals close, and how, is unchanged (the must-not-fire controls below),
/// and that read, like the policy read, is reported when it faults or is cut short (task 204's rule).
/// These run the REAL closure service and writer over the job harness; the fake answers the joined read as Dataverse does (the status on
/// the Signal, D-170's conditions and the domain allowlist on its policy row).
/// </summary>
public partial class SignalReevaluationJobTests
{
    private static bool IsSignalRead(QueryExpression q) => q.EntityName == "sprk_signal";

    /// <summary>The per-policy selection (<c>SignalClosureService.OpenSignalsOfPolicyQuery</c>): the policy lookup equals one id.</summary>
    private static Guid? SelectedPolicyOf(QueryExpression q) =>
        q.EntityName == "sprk_signal"
        && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_policy" && c.Operator == ConditionOperator.Equal) is { } c
            ? (Guid)c.Values[0]
            : null;

    private static bool IsOpenSignalJoinRead(QueryExpression q) =>
        q.EntityName == "sprk_signal" && q.LinkEntities.Any(l => l.LinkToEntityName == "sprk_policy");

    /// <summary>Counts every Signal read the close sends, and records which policies it selected one by one.</summary>
    private static (Func<int> SignalReads, List<Guid> Selected) ObserveSignalReads(Harness h)
    {
        var reads = 0;
        var selected = new List<Guid>();
        h.Sysadmin.FaultOn = q =>
        {
            if (IsSignalRead(q)) reads++;
            if (SelectedPolicyOf(q) is { } policy) selected.Add(policy);
            return null;
        };
        return (() => reads, selected);
    }

    private static void AssertClosedAsBefore(Harness h, Guid signalId)
    {
        var row = h.Org.Rows[signalId];
        AssertSystemClosure(row, SignalWriter.ResolutionPolicyRetired);
        row.GetAttributeValue<DateTime?>("sprk_lastevaluated").Should().Be(row.GetAttributeValue<DateTime?>("sprk_resolvedon"),
            "D-120: resolvedon and lastevaluated are written in the same update");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task ClosePoliciesNotInForce_ManyOffPolicies_FewWithOpenSignals_SignalReadsDoNotGrowWithTheOffPolicies(int offPolicies)
    {
        // 3 of the Off policies have open Signals (one has two, one of them Acknowledged); every other Off policy has none.
        var h = new Harness();
        var policies = AddOffPolicies(h, offPolicies);
        var withSignals = new[] { policies[7], policies[offPolicies / 2], policies[^1] };
        var signals = withSignals.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();
        signals.Add(h.Org.SeedSignal(Key(withSignals[0].Code, "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusAcknowledged,
            coreId: MatterId, policyId: withSignals[0].Id, versionId: h.Sysadmin.CurrentVersionOf(withSignals[0].Code)));
        var (signalReads, selected) = ObserveSignalReads(h);

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default);

        // Before: one Signal read per Off policy (500, then 1,000). Now: one page of the joined read + one selection per policy with an
        // open Signal (3), whatever the number of Off policies.
        signalReads().Should().Be(1 + 3, "the Signal reads are the joined read's pages plus one selection per policy that has an open Signal");
        selected.Should().BeEquivalentTo(withSignals.Select(p => p.Id), "only the policies with an open Signal are selected, each once");
        outcome.Should().Be(new SignalClosureOutcome(4, 0, 0));
        outcome.IsComplete.Should().BeTrue();
        foreach (var signal in signals) AssertClosedAsBefore(h, signal);
        h.Org.ConditionalUpdates.Should().HaveCount(4, "row-version conditional, one per open Signal");
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_TheJoinedReadReturnsOnlyTheOpenSignalsOfNotInForceClosablePolicies_InOnePage()
    {
        // Fix round 1 (F3): the join's link filter IS the performance fix (the intersection with the policy read keeps the closes right
        // without it). Qualifying: Off Ontology, Retired Ontology (Open and Acknowledged), Off Billing. Noise the filter must keep out of the
        // read: open Signals of an in-force Ontology policy (state filter) and of an Off Docketing policy (domain allowlist). The page holds
        // exactly the qualifying Signals, so one read means nothing else came back.
        var h = new Harness();
        var off = AddOffPolicies(h, 2);
        var qualifying = off.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();
        h.Sysadmin.AddPolicy("POL-RETIRED", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyState("POL-RETIRED", enabled: true, statecode: 1);
        var retired = h.Sysadmin.PolicyIdOf("POL-RETIRED");
        qualifying.Add(SeedOpenSignalOf(h, "POL-RETIRED", retired));
        qualifying.Add(h.Org.SeedSignal(Key("POL-RETIRED", "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusAcknowledged,
            coreId: MatterId, policyId: retired, versionId: h.Sysadmin.CurrentVersionOf("POL-RETIRED")));
        h.Sysadmin.AddPolicy("POL-BILL-OFF", SignalWriter.LaneDecide, enabled: false, PathBBody, PathBTemplate);
        SetDomain(h, "POL-BILL-OFF", RuleDomains.PolicyDomainBilling);
        qualifying.Add(SeedOpenSignalOf(h, "POL-BILL-OFF", h.Sysadmin.PolicyIdOf("POL-BILL-OFF")));

        h.Sysadmin.AddPolicy("POL-LIVE", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        var noise = Enumerable.Range(0, 10).Select(_ => SeedOpenSignalOf(h, "POL-LIVE", h.Sysadmin.PolicyIdOf("POL-LIVE"))).ToList();
        h.Sysadmin.AddPolicy("POL-DOCKET-OFF", SignalWriter.LaneDecide, enabled: false, PathBBody, PathBTemplate);
        SetDomain(h, "POL-DOCKET-OFF", 100000002); // reserved Docketing: not closable on retirement
        noise.AddRange(Enumerable.Range(0, 3).Select(_ => SeedOpenSignalOf(h, "POL-DOCKET-OFF", h.Sysadmin.PolicyIdOf("POL-DOCKET-OFF"))));
        h.Sysadmin.PageSizeCap = qualifying.Count;

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default);

        h.Sysadmin.OpenSignalPolicyJoinReads.Should().Be(1,
            "the read returns only the open Signals of Off or Retired policies of a closable domain, so they fit one page of their own count");
        outcome.Should().Be(new SignalClosureOutcome(qualifying.Count, 0, 0));
        outcome.IsComplete.Should().BeTrue();
        foreach (var signal in qualifying) AssertClosedAsBefore(h, signal);
        noise.Should().OnlyContain(s => IsOpen(h, s), "an in-force policy's and a Docketing policy's Signals are never closed");
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_InForceSignalsAreUntouched_ResolvedAreNotStamped_AndAPolicyWithNothingOpenIsNotSelected()
    {
        var h = new Harness();
        var off = AddOffPolicies(h, 3); // [0] has an open Signal, [1] only a Resolved one, [2] none
        h.Sysadmin.AddPolicy("POL-LIVE", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        var live = h.Sysadmin.PolicyIdOf("POL-LIVE");
        var liveOpen = SeedOpenSignalOf(h, "POL-LIVE", live);
        var liveAcknowledged = h.Org.SeedSignal(Key("POL-LIVE", "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusAcknowledged,
            coreId: MatterId, policyId: live, versionId: h.Sysadmin.CurrentVersionOf("POL-LIVE"));
        var closes = SeedOpenSignalOf(h, off[0].Code, off[0].Id);
        var resolved = h.Org.SeedSignal(Key(off[1].Code, "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: MatterId, policyId: off[1].Id,
            versionId: h.Sysadmin.CurrentVersionOf(off[1].Code));
        var untouched = new[] { liveOpen, liveAcknowledged, resolved }
            .ToDictionary(id => id, id => (Version: h.Org.VersionOf(id), Fields: h.Org.Rows[id].Attributes.ToDictionary(a => a.Key, a => a.Value)));
        var (_, selected) = ObserveSignalReads(h);

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default);

        outcome.Should().Be(new SignalClosureOutcome(1, 0, 0));
        AssertClosedAsBefore(h, closes);
        selected.Should().Equal(new[] { off[0].Id }, "an in-force policy, and an Off policy with nothing open, are never selected");
        foreach (var (id, before) in untouched)
        {
            h.Org.VersionOf(id).Should().Be(before.Version, "D-61: a Signal the close does not change is not written");
            h.Org.Rows[id].Attributes.ToDictionary(a => a.Key, a => a.Value).Should().BeEquivalentTo(before.Fields);
        }

        h.Org.ConditionalUpdates.Select(u => u.Id).Should().Equal(closes);
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_TheOpenSignalReadPastItsBound_IsReportedTruncated_AndThePoliciesItNamedAreClosed()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).OrderBy(id => id).ToList(); // the read's order: by Signal id
        h.Sysadmin.PageSizeCap = 2;

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default, maxPages: 2);

        outcome.PolicyFailures.Should().Be(1, "a cut-short open-Signal read is one policy failure: the run fails (task 204's rule)");
        outcome.IsComplete.Should().BeFalse();
        outcome.Closed.Should().Be(4, "two pages of two Signals were read, naming four policies, and those are closed");
        signals.Take(4).Should().OnlyContain(s => !IsOpen(h, s));
        IsOpen(h, signals[^1]).Should().BeTrue("its Signal was past the bound: reported, and closed by a run that reads it");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("More than 2 pages of 5000 open Signals of the Off and Retired policies") && e.Message.Contains("4 were read in all"));
        h.Log.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains("open Signals of the Off and Retired policies were not all read: 4 were read, naming 4 of the policies read"));

        // The next run reads the rest: the closed ones dropped out.
        (await h.Closure.ClosePoliciesNotInForceAsync(default, maxPages: 2)).Should().Be(new SignalClosureOutcome(1, 0, 0));
        signals.Should().OnlyContain(s => !IsOpen(h, s));
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_AnOpenSignalPageThatFails_IsReported_AndThePoliciesReadBeforeItAreClosed()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).OrderBy(id => id).ToList();
        h.Sysadmin.PageSizeCap = 2;
        h.Sysadmin.FaultOn = q => IsOpenSignalJoinRead(q) && q.PageInfo.PageNumber == 2 ? new TimeoutException("Dataverse timed out.") : null;

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default);

        outcome.PolicyFailures.Should().Be(1);
        outcome.IsComplete.Should().BeFalse();
        outcome.Closed.Should().Be(2, "the first page named two policies; their Signals close");
        signals.Take(2).Should().OnlyContain(s => !IsOpen(h, s));
        signals.Skip(2).Should().OnlyContain(s => IsOpen(h, s));
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Exception is TimeoutException && e.Message.Contains("could not be read on page 2") && e.Message.Contains("2 were read in all"));
    }

    [Fact]
    public async Task Run_AnOpenSignalReadCutShort_FailsTheRun_LikeACutShortPolicyRead()
    {
        // One Off policy with 201 open Signals and one-Signal pages: the joined read reaches its 200-page bound with more to come.
        var h = new Harness();
        var policy = AddOffPolicies(h, 1)[0];
        var signals = Enumerable.Range(0, SignalEvaluator.MaxPages + 1).Select(_ => SeedOpenSignalOf(h, policy.Code, policy.Id)).ToList();
        h.Sysadmin.PageSizeCap = 1;

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("the open-Signal read was cut short: a failed run, not a clean one");
        result.ErrorMessage.Should().Contain("policyFailures=1");
        result.ResultJson.Should().Contain("\"outcome\":\"failed\"");
        signals.Should().OnlyContain(s => !IsOpen(h, s), "the policy the read named is closed through its own selection, in full");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains($"More than {SignalEvaluator.MaxPages} pages"));

        // A faulted read: reported failed, and nothing it did not reach is closed.
        var again = new Harness();
        var other = AddOffPolicies(again, 1)[0];
        var stranded = SeedOpenSignalOf(again, other.Code, other.Id);
        again.Sysadmin.FaultOn = q => IsOpenSignalJoinRead(q) ? new TimeoutException("Dataverse timed out.") : null;

        var failed = await again.RunAsync();

        failed.Success.Should().BeFalse();
        failed.ErrorMessage.Should().Contain("policyFailures=1");
        IsOpen(again, stranded).Should().BeTrue();
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_NoPolicyNotInForce_ReadsNoSignal()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-LIVE", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        var open = SeedOpenSignalOf(h, "POL-LIVE", h.Sysadmin.PolicyIdOf("POL-LIVE"));
        var (signalReads, _) = ObserveSignalReads(h);

        (await h.Closure.ClosePoliciesNotInForceAsync(default)).Should().Be(SignalClosureOutcome.None);

        signalReads().Should().Be(0, "with no Off or Retired policy there is nothing to look for");
        IsOpen(h, open).Should().BeTrue();
    }
}
