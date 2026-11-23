using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// #2982 (DI-162, ontology task 204): the nightly Off/Retired close (D-170) reads EVERY not-in-force policy, by id-cursor pages, and a read
/// that faults or is cut short is reported (a policy failure: the run fails) rather than ending as a silent partial success (D-78).
/// Before: one unpaged read took Dataverse's first 5,000 rows as the list, so the open Signals of every policy past them stayed open.
/// These run the REAL closure service, writer and job over the job harness; the fake answers an unpaged <c>sprk_policy</c> read with one
/// 5,000-row page, as Dataverse does, and an id-cursor read as Dataverse does.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private static bool IsPolicyCursorRead(Microsoft.Xrm.Sdk.Query.QueryExpression q) =>
        q.EntityName == "sprk_policy" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policyid");

    /// <summary>Adds <paramref name="count"/> Off policies and returns them in the order the fake (and Dataverse) pages them: by id.</summary>
    private static List<(string Code, Guid Id)> AddOffPolicies(Harness h, int count)
    {
        var added = new List<(string, Guid)>();
        for (var i = 0; i < count; i++)
        {
            var code = $"POL-NIF-{i:D5}";
            h.Sysadmin.AddPolicy(code, SignalWriter.LaneDecide, enabled: false, PathBBody, PathBTemplate);
            added.Add((code, h.Sysadmin.PolicyIdOf(code)));
        }

        return added.OrderBy(p => p.Item2).ToList();
    }

    private static Guid SeedOpenSignalOf(Harness h, string code, Guid policyId) =>
        h.Org.SeedSignal(Key(code, "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusOpen, coreId: MatterId,
            policyId: policyId, versionId: h.Sysadmin.CurrentVersionOf(code));

    private static bool IsOpen(Harness h, Guid signalId) =>
        h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value == SignalWriter.SignalStatusOpen;

    [Fact]
    public async Task ClosePoliciesNotInForce_PastTheFirstDataversePage_ClosesTheSignalsOfEveryPolicy()
    {
        // 5,003 Off policies: the unpaged read returned the first 5,000 by id; the last three were never closed against.
        var h = new Harness();
        var policies = AddOffPolicies(h, 5003);
        var first = SeedOpenSignalOf(h, policies[0].Code, policies[0].Id);
        var onSecondPage = SeedOpenSignalOf(h, policies[5001].Code, policies[5001].Id);
        var last = SeedOpenSignalOf(h, policies[^1].Code, policies[^1].Id);

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default);

        outcome.Should().Be(new SignalClosureOutcome(3, 0, 0), "every Signal closes, including the two of policies past the first 5,000");
        outcome.IsComplete.Should().BeTrue();
        AssertSystemClosure(h.Org.Rows[first], SignalWriter.ResolutionPolicyRetired);
        AssertSystemClosure(h.Org.Rows[onSecondPage], SignalWriter.ResolutionPolicyRetired);
        AssertSystemClosure(h.Org.Rows[last], SignalWriter.ResolutionPolicyRetired);
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_ReadsEachPolicyOnce_WhenItIsBothOffAndInactive_AndAcrossPages()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5);
        // The first and the last by id are ALSO Retired (statecode Inactive), so they match both conditions and fall on different pages.
        foreach (var overlap in new[] { policies[0], policies[^1] })
            h.Sysadmin.SetPolicyState(overlap.Code, enabled: false, statecode: 1);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();
        var selections = new List<Guid>();
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal" && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_policy") is { } c
                && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal)
                selections.Add((Guid)c.Values[0]);
            return null;
        };

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default, pageSize: 2);

        outcome.Should().Be(new SignalClosureOutcome(5, 0, 0));
        selections.Should().BeEquivalentTo(policies.Select(p => p.Id), "each policy's Signals are selected exactly once, not once per condition");
        selections.Should().OnlyHaveUniqueItems();
        signals.Should().OnlyContain(s => !IsOpen(h, s));
        (await h.Closure.PoliciesNotInForceAsync(default, pageSize: 2)).Keys.Should().BeEquivalentTo(policies.Select(p => p.Id));
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_APageReadThatFails_IsReported_AndTheEarlierPagesAreStillClosed()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();
        h.Sysadmin.FaultOn = q => IsPolicyCursorRead(q) ? new TimeoutException("Dataverse timed out.") : null;

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default, pageSize: 2);

        outcome.PolicyFailures.Should().Be(1, "the cut-short read is one policy failure: not a silent partial success");
        outcome.IsComplete.Should().BeFalse();
        outcome.Closed.Should().Be(2, "the first page was read and closed");
        signals.Take(2).Should().OnlyContain(s => !IsOpen(h, s));
        signals.Skip(2).Should().OnlyContain(s => IsOpen(h, s), "the rest stay open and are reported, to be closed by a run that reads them");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Exception is TimeoutException && e.Message.Contains("on page 2") && e.Message.Contains("2 policies were read"));
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("were not all read: 2 were read"));
    }

    [Fact]
    public async Task PoliciesNotInForce_APageReadThatFails_Throws_SoTheRedactionSweepDoesNotRunOnAPartialList()
    {
        var h = new Harness();
        AddOffPolicies(h, 5);
        h.Sysadmin.FaultOn = q => IsPolicyCursorRead(q) ? new TimeoutException("Dataverse timed out.") : null;

        var act = () => h.Closure.PoliciesNotInForceAsync(default, pageSize: 2);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not all read (2 read)*");
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_MoreThanTheBoundedPages_IsReportedTruncated_NotClosedAsIfComplete()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 7);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default, pageSize: 2, maxPages: 2);

        outcome.PolicyFailures.Should().Be(1);
        outcome.IsComplete.Should().BeFalse();
        outcome.Closed.Should().Be(4, "two pages of two were read");
        signals.Take(4).Should().OnlyContain(s => !IsOpen(h, s));
        signals.Skip(4).Should().OnlyContain(s => IsOpen(h, s));
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("More than 2 pages of 2 Off policies") && e.Message.Contains("4 policies were read"));

        // The same policies, within the bound: complete.
        (await h.Closure.ClosePoliciesNotInForceAsync(default, pageSize: 2, maxPages: 4)).Should().Be(new SignalClosureOutcome(3, 0, 0));
    }

    [Fact]
    public async Task ClosePoliciesNotInForce_AStoreThatIgnoresTheCursor_EndsAsAReportedFault_NotAnEndlessLoop()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5);
        var signals = policies.Select(p => SeedOpenSignalOf(h, p.Code, p.Id)).ToList();
        h.Sysadmin.IgnorePolicyCursor = true; // every page repeats the first two policies
        var reads = 0;
        h.Sysadmin.FaultOn = q => { if (IsPolicyCursorRead(q)) reads++; return null; };

        var outcome = await h.Closure.ClosePoliciesNotInForceAsync(default, pageSize: 2);

        reads.Should().Be(1, "the second page added no policy, so the read stopped there");
        outcome.IsComplete.Should().BeFalse();
        outcome.PolicyFailures.Should().Be(1);
        signals.Take(2).Should().OnlyContain(s => !IsOpen(h, s));
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.Exception != null && e.Exception.Message.Contains("id cursor did not advance"));
    }

    [Fact]
    public async Task Run_NotInForcePoliciesPastTheFirstPage_AreClosed_AndARunWhoseReadFailsIsReportedFailed()
    {
        var h = new Harness();
        var policies = AddOffPolicies(h, 5001);
        var pastFirstPage = SeedOpenSignalOf(h, policies[^1].Code, policies[^1].Id);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[pastFirstPage], SignalWriter.ResolutionPolicyRetired);

        var again = new Harness();
        var more = AddOffPolicies(again, 5001);
        var stranded = SeedOpenSignalOf(again, more[^1].Code, more[^1].Id);
        again.Sysadmin.FaultOn = q => IsPolicyCursorRead(q) ? new TimeoutException("Dataverse timed out.") : null;

        var failed = await again.RunAsync();

        failed.Success.Should().BeFalse("a not-in-force read cut short is a failed run, not a clean one");
        failed.ErrorMessage.Should().Contain("policyFailures=2", "the close and the redaction sweep each report their cut-short read");
        failed.ResultJson.Should().Contain("\"outcome\":\"failed\"");
        IsOpen(again, stranded).Should().BeTrue("its policy was never read: reported, not closed");
        again.Log.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("were not all read"));
    }
}
