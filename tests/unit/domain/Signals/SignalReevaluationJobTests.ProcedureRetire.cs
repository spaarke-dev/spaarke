using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

        var selected = await h.Closure.SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject: true, default);

        selected.Select(s => s.Signal.Id).Should().BeEquivalentTo(new[] { onInvoice, onLine }, "Open and Acknowledged are open; Resolved is not");
        selected.Single(s => s.Signal.Id == onInvoice).RegardingInvoiceId.Should().Be(invoiceId);
        selected.Single(s => s.Signal.Id == onLine).RegardingInvoiceLineId.Should().Be(lineId);
        h.Org.WriteCalls.Should().Be(0, "the selection reads only");

        var closure = await h.Closure.ClosePolicySignalsRetiredAsync(policyId, PolicyCode, withBillingSubject: true, default);

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

        var closure = await h.Closure.ClosePolicySignalsRetiredAsync(policyId, PolicyCode, withBillingSubject: true, default);

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

    // ── Ontology review F1 (2026-10-22): the invoice subject columns are opt-in ─────────────────────────────

    /// <summary>A by-policy open-Signal read (the shared selection), as Dataverse received it.</summary>
    private static bool IsOpenSignalsOfPolicyRead(QueryExpression q) =>
        q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == ConditionOperator.Equal);

    /// <summary>Which rows a query selects (table, filter, order, page size), without its columns or page cursor.</summary>
    private static string CriteriaOf(QueryExpression q) =>
        $"{q.EntityName}|{q.Criteria.FilterOperator}|"
        + string.Join(";", q.Criteria.Conditions.Select(c => $"{c.AttributeName} {c.Operator} {string.Join(",", c.Values)}"))
        + $"|{q.Criteria.Filters.Count}|{string.Join(";", q.Orders.Select(o => $"{o.AttributeName} {o.OrderType}"))}|{q.PageInfo?.Count}"
        + $"|{q.LinkEntities.Count}";

    [Fact]
    public async Task SweepAndAdminRetireClose_NeverSelectTheInvoiceSubjectColumns_SoTheyCloseWhereEbillingsSchemaIsNotDeployed()
    {
        // Must-fire for F1: an environment without ebilling's Signal schema answers 400 to a ColumnSet naming either invoice column.
        // Before the fix every per-policy read of the sweep failed there (swallowed: no Off/Retired policy ever closed its Signals) and
        // the admin retire close threw.
        var h = new Harness().WithPathBPolicy(enabled: false);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var version = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var columnSets = new List<string[]>();
        h.Sysadmin.FaultOn = q =>
        {
            if (!IsOpenSignalsOfPolicyRead(q)) return null;
            columnSets.Add(q.ColumnSet.Columns.ToArray());
            return q.ColumnSet.Columns.Any(SignalClosureService.BillingSubjectColumns.Contains)
                ? new InvalidOperationException("400: 'sprk_signal' entity doesn't contain attribute with Name = 'sprk_regardinginvoice'")
                : null;
        };
        var swept = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: version);

        var sweep = await h.Closure.ClosePoliciesNotInForceAsync(default);

        sweep.Should().Be(new SignalClosureOutcome(1, 0, 0), "the sweep closes the Off policy's Signal: no PolicyFailures");
        AssertSystemClosure(h.Org.Rows[swept], SignalWriter.ResolutionPolicyRetired);

        var retiredNow = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: OtherMatterId, policyId: policyId, versionId: version);

        (await h.Closure.ClosePolicyRetiredNowAsync(policyId)).Should().Be(new SignalClosureOutcome(1, 0, 0), "the admin retire close does not throw");
        AssertSystemClosure(h.Org.Rows[retiredNow], SignalWriter.ResolutionPolicyRetired);

        columnSets.Should().HaveCount(2, "one selection by the sweep, one by the admin retire close (non-vacuity)");
        columnSets.Should().OnlyContain(c => !c.Contains(SignalClosureService.RegardingInvoiceColumn) && !c.Contains(SignalClosureService.RegardingInvoiceLineColumn));
    }

    [Fact]
    public async Task EveryPolicyRetiredSelection_SelectsTheSameRows_AndOnlyTheProcedureRetireOptsInToTheInvoiceColumns()
    {
        // "Preview = sweep" still holds for which rows: the Procedure retire preview and close, the admin retire close and the sweep send the
        // same criteria, built by SignalClosureService.OpenSignalsOfPolicyQuery; their ColumnSets differ only by the opt-in.
        var h = new Harness().WithPathBPolicy(enabled: false);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var reads = new List<(string Criteria, string[] Columns)>();
        h.Sysadmin.FaultOn = q =>
        {
            if (IsOpenSignalsOfPolicyRead(q)) reads.Add((CriteriaOf(q), q.ColumnSet.Columns.ToArray()));
            return null;
        };

        await h.Closure.SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject: true, default);              // Procedure retire preview
        await h.Closure.ClosePolicySignalsRetiredAsync(policyId, PolicyCode, withBillingSubject: true, default);   // Procedure retire close
        await h.Closure.ClosePolicyRetiredNowAsync(policyId);                                                     // admin retire close
        await h.Closure.ClosePoliciesNotInForceAsync(default);                                                    // nightly sweep

        reads.Should().HaveCount(4);
        var builder = CriteriaOf(SignalClosureService.OpenSignalsOfPolicyQuery(policyId, withBillingSubject: false));
        CriteriaOf(SignalClosureService.OpenSignalsOfPolicyQuery(policyId, withBillingSubject: true)).Should().Be(builder,
            "the opt-in changes the columns only");
        reads.Should().OnlyContain(r => r.Criteria == builder, "every caller selects the rows the one builder describes");

        var plain = SignalClosureService.OpenSignalColumns;
        reads[0].Columns.Should().BeEquivalentTo(plain.Concat(SignalClosureService.BillingSubjectColumns));
        reads[1].Columns.Should().BeEquivalentTo(plain.Concat(SignalClosureService.BillingSubjectColumns));
        reads[2].Columns.Should().BeEquivalentTo(plain);
        reads[3].Columns.Should().BeEquivalentTo(plain);
        plain.Should().NotContain(SignalClosureService.BillingSubjectColumns);
    }
}
