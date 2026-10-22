using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 081 (procedure model 15 answer 2, "preview = sweep"): the extraction of the open-Signal selection. The Procedure retire
/// preview, the Procedure retire close, the immediate close (<see cref="SignalClosureService.ClosePolicyRetiredNowAsync"/>) and the job's
/// sweep all select through <see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/>; these tests run the REAL selection and the
/// REAL close (writer, conditional update) over the job harness.
/// </summary>
public partial class SignalReevaluationJobTests
{
    [Fact]
    public async Task SelectOpenSignalsOfPolicy_ReturnsOpenAndAcknowledged_WithTheirInvoiceAndLine_AndClosePolicySignalsRetired_ClosesExactlyThose()
    {
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var version = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var invoiceId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var onInvoice = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: version);
        h.Org.Rows[onInvoice]["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoiceId);
        var onLine = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusAcknowledged,
            coreId: OtherMatterId, policyId: policyId, versionId: version);
        h.Org.Rows[onLine]["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", lineId);
        var acted = h.Org.SeedSignal(Key(PolicyCode, "sprk_project", ProjectId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), policyId: policyId, versionId: version);

        var selected = await h.Closure.SelectOpenSignalsOfPolicyAsync(policyId, default);

        selected.Select(s => s.Signal.Id).Should().BeEquivalentTo(new[] { onInvoice, onLine }, "Open and Acknowledged are open; Resolved is not");
        selected.Single(s => s.Signal.Id == onInvoice).RegardingInvoiceId.Should().Be(invoiceId);
        selected.Single(s => s.Signal.Id == onLine).RegardingInvoiceLineId.Should().Be(lineId);
        h.Org.WriteCalls.Should().Be(0, "the selection reads only");

        var closure = await h.Closure.ClosePolicySignalsRetiredAsync(policyId, PolicyCode, default);

        closure.Outcome.Should().Be(new SignalClosureOutcome(2, 0, 0));
        closure.Closed.Select(s => s.Signal.Id).Should().BeEquivalentTo(new[] { onInvoice, onLine });
        closure.Closed.Single(s => s.Signal.Id == onInvoice).RegardingInvoiceId.Should().Be(invoiceId, "the caller counts from what closed");
        AssertSystemClosure(h.Org.Rows[onInvoice], SignalWriter.ResolutionPolicyRetired);
        AssertSystemClosure(h.Org.Rows[onLine], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[acted].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionActed);
    }

    [Fact]
    public async Task ClosePolicySignalsRetired_ASignalAPersonResolvedSinceTheRead_IsNotReportedClosed_AndIsLeftAsThePersonLeftIt()
    {
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var version = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var closes = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: version);
        var resolvedMeanwhile = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: OtherMatterId, policyId: policyId, versionId: version);
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(resolvedMeanwhile);

        var closure = await h.Closure.ClosePolicySignalsRetiredAsync(policyId, PolicyCode, default);

        closure.Outcome.Closed.Should().Be(1);
        closure.Outcome.ChangedSinceRead.Should().Be(1);
        closure.Outcome.IsComplete.Should().BeFalse();
        closure.Closed.Select(s => s.Signal.Id).Should().Equal(closes);
        h.Org.Rows[resolvedMeanwhile].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value
            .Should().NotBe(SignalWriter.ResolutionPolicyRetired, "the person's resolution stands");
    }

    [Fact]
    public async Task ClosePolicyRetiredNow_StillReturnsTheSameOutcome_AfterTheExtraction()
    {
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));

        (await h.Closure.ClosePolicyRetiredNowAsync(policyId)).Should().Be(new SignalClosureOutcome(1, 0, 0));
        (await h.Closure.ClosePolicyRetiredNowAsync(policyId)).Should().Be(SignalClosureOutcome.None);
    }
}
