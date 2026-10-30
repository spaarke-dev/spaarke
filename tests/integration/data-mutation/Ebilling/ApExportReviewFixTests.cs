using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// The fixes from the adversarial review of task 150 (ebilling-r1, branch task/ebill-150-fixes). Same world as <see cref="ApExportBatchTests"/>: the
/// real service over an in-memory Dataverse that now evaluates nested filters and <c>statecode</c>, enforces Dataverse's 5,000 row cap on
/// <c>TopCount</c> and a page, and enforces the batch total column's range. Each test names the finding it pins.
/// </summary>
public sealed class ApExportReviewFixTests
{
    private const int Fee = 100000000, Expense = 100000001, Adjustment = 100000002;
    private const int Active = 0, Inactive = 1;
    private const int Clean = 100000001, Reduced = 100000004, Disallowed = 100000005;
    private const string Json = "application/json";
    private const string Csv = "text/csv";

    private static JsonElement InvoicesOf(ApExportWorld world, Guid batchId) =>
        JsonDocument.Parse(world.TextOf(world.FileOf(batchId, Json))).RootElement.GetProperty("invoices").Clone();

    // =========================================================================================
    // 1. A removed (deactivated) Adjustment line is not exported as a live line
    // =========================================================================================

    [Fact(DisplayName = "150 review F1: a deactivated Adjustment line is NOT in the exported lines (the same rule every other reader of an invoice's lines uses); an active one is")]
    public async Task RemovedAdjustment_IsNotExported()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-A", lines: 0);
        world.AddLine(invoice, Fee, 1000m, sequence: 1);
        world.AddLine(invoice, Adjustment, 50m, sequence: 2, state: Active);
        world.AddLine(invoice, Adjustment, 99m, sequence: 3, state: Inactive); // removed by the reviewer (NFR-09: deactivated, never deleted)
        world.GrantEverything();

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        var lines = InvoicesOf(world, result.BatchId!.Value)[0].GetProperty("lines");
        lines.GetArrayLength().Should().Be(2, "the fee and the ACTIVE adjustment; the removed adjustment is gone");
        lines.EnumerateArray().Select(l => l.GetProperty("approved").GetDecimal()).Should().BeEquivalentTo([1000m, -50m]);
        lines.EnumerateArray().Should().NotContain(l => l.GetProperty("approved").GetDecimal() == -99m || l.GetProperty("approved").GetDecimal() == 99m);
    }

    [Fact(DisplayName = "150 review F1: a deactivated FEE line is still exported (only Adjustment lines are removed by deactivation; a Fee is never silently dropped from a money file)")]
    public async Task InactiveFeeLine_IsStillExported()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-A", lines: 0);
        world.AddLine(invoice, Fee, 1000m, sequence: 1, state: Inactive);
        world.GrantEverything();

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        InvoicesOf(world, result.BatchId!.Value)[0].GetProperty("lines").GetArrayLength().Should().Be(1);
    }

    // =========================================================================================
    // 2. Adjustment lines carry their signed contribution; the line sums reconcile
    // =========================================================================================

    [Fact(DisplayName = "150 review F2: lines[].approved add up to the invoice's ApprovedTotal and lines[].billed to its BilledTotal, for an invoice with fees, expenses, a reduced and a disallowed line and an active adjustment (the totals come from the real calculator)")]
    public async Task LineSums_ReconcileWithTheInvoiceTotals()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-A", lines: 0);
        var reduced = world.AddLine(invoice, Fee, 1000m, sequence: 1, status: Reduced, approvedAmount: 800m);
        var expense = world.AddLine(invoice, Expense, 200m, sequence: 2, status: Clean);
        var disallowed = world.AddLine(invoice, Fee, 300m, sequence: 3, status: Disallowed);
        var adjustment = world.AddLine(invoice, Adjustment, 50m, sequence: 4, state: Active);
        world.AddLine(invoice, Adjustment, 99m, sequence: 5, state: Inactive);

        // The stored totals are what InvoiceTotalsCalculator (the one owner, FR-07) gives for these lines.
        var calculation = InvoiceTotalsCalculator.Calculate(1500m,
        [
            new InvoiceLineInput(reduced.Id, InvoiceLineCostType.Fee, 1000m, 800m, InvoiceLineStatus.Reduced, null, null),
            new InvoiceLineInput(expense.Id, InvoiceLineCostType.Expense, 200m, null, InvoiceLineStatus.Clean, null, null),
            new InvoiceLineInput(disallowed.Id, InvoiceLineCostType.Fee, 300m, null, InvoiceLineStatus.Disallowed, null, null),
            new InvoiceLineInput(adjustment.Id, InvoiceLineCostType.Adjustment, 50m, null, null, InvoiceAdjustmentMethod.Fixed, 50m),
        ]);
        calculation.IsRefused.Should().BeFalse();
        calculation.ApprovedTotal.Should().Be(950m);
        world.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object>
        {
            ["sprk_totalamount"] = new Money(1500m),
            ["sprk_lineadjustmentstotal"] = new Money(calculation.LineAdjustmentsTotal),
            ["sprk_invoiceadjustmentstotal"] = new Money(calculation.InvoiceAdjustmentsTotal),
            ["sprk_approvedtotal"] = new Money(calculation.ApprovedTotal),
        });
        world.GrantEverything();

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        var exported = InvoicesOf(world, result.BatchId!.Value)[0];
        var lines = exported.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(4);
        lines.Sum(l => l.GetProperty("approved").GetDecimal()).Should().Be(exported.GetProperty("ApprovedTotal").GetDecimal()).And.Be(950m);
        lines.Sum(l => l.GetProperty("billed").GetDecimal()).Should().Be(exported.GetProperty("BilledTotal").GetDecimal()).And.Be(1500m);

        var adjustmentLine = lines.Single(l => l.GetProperty("type").GetString() == "Adjustment");
        adjustmentLine.GetProperty("approved").GetDecimal().Should().Be(-50m, "a deduction is negative");
        adjustmentLine.GetProperty("billed").GetDecimal().Should().Be(0m, "the firm billed nothing for an adjustment");
        lines.Single(l => l.GetProperty("billed").GetDecimal() == 300m).GetProperty("approved").GetDecimal().Should().Be(0m);
        lines.Single(l => l.GetProperty("billed").GetDecimal() == 1000m).GetProperty("approved").GetDecimal().Should().Be(800m);
    }

    // =========================================================================================
    // 3. Dataverse caps TopCount and a page at 5,000
    // =========================================================================================

    [Theory(DisplayName = "150 review F3: an invoice with exactly 5,000 lines exports (no query asks for more than Dataverse's 5,000 cap); with 5,001 the batch fails closed - nothing sent, nothing claimed, no file")]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public async Task LineReadStaysWithinTheRowCap(int lineCount, bool exports)
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-BIG", lines: 0);
        for (var i = 1; i <= lineCount; i++)
        {
            world.AddLine(invoice, Fee, 1m, sequence: i);
        }

        world.GrantEverything();

        var result = await world.ExportAsync(invoice);

        if (exports)
        {
            result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
            InvoicesOf(world, result.BatchId!.Value)[0].GetProperty("lines").GetArrayLength().Should().Be(5000);
        }
        else
        {
            result.Outcome.Should().Be(ApExportOutcome.Failed);
            world.Lifecycle.Sent.Should().BeEmpty();
            world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
            world.StoredFiles().Should().NotContain(f => f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Servable);
        }
    }

    [Fact(DisplayName = "150 review F3: the test world rejects a TopCount or a page above 5,000, as Dataverse does (the guard the F3 test relies on)")]
    public async Task TheWorldEnforcesTheCap()
    {
        var world = new ApExportWorld();
        var tooMany = new Microsoft.Xrm.Sdk.Query.QueryExpression("sprk_invoice") { TopCount = 5001 };
        var tooBigPage = new Microsoft.Xrm.Sdk.Query.QueryExpression("sprk_invoice") { PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { PageNumber = 1, Count = 5001 } };

        await world.Dataverse.Service.Invoking(s => s.RetrieveMultipleAsync(tooMany, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        await world.Dataverse.Service.Invoking(s => s.RetrieveMultipleAsync(tooBigPage, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
    }

    // =========================================================================================
    // 4. A batch total the column cannot hold is refused BEFORE any claim
    // =========================================================================================

    public static IEnumerable<object[]> OutOfRangeTotals() =>
    [
        [new[] { -100m }],                    // a credit memo on its own (the minimum is 0 until the widening script has run)
        [new[] { 900m, -1000m }],             // a credit memo that drags the sum below zero
        [new[] { 1_000_000_000.01m }],        // above the maximum
        [new[] { 600_000_000m, 600_000_000m }],
    ];

    [Theory(DisplayName = "150 review F4: a batch whose total is outside the batch total column's range is refused 400 batch_total_out_of_range BEFORE any claim - nothing claimed, nothing written, nothing sent")]
    [MemberData(nameof(OutOfRangeTotals))]
    public async Task TotalOutsideTheColumnRange_IsRefusedBeforeAnyClaim(decimal[] approvedTotals)
    {
        var world = new ApExportWorld();
        var invoices = approvedTotals.Select((t, i) => world.AddInvoice($"INV-{i}", approved: t)).ToArray();
        world.GrantEverything();
        var writesBefore = world.Dataverse.Writes;

        var result = await world.ExportAsync(invoices);

        result.Outcome.Should().Be(ApExportOutcome.TotalOutOfRange);
        result.Code.Should().Be(ApExportCodes.BatchTotalOutOfRange);
        result.Batch.Should().BeNull();
        world.Dataverse.Writes.Should().Be(writesBefore, "refused before the batch row, any claim or any file");
        world.Batches().Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.Lifecycle.Sent.Should().BeEmpty();
        foreach (var invoice in invoices)
        {
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
            world.BatchOfInvoice(invoice.Id).Should().BeNull();
        }
    }

    public static IEnumerable<object[]> InRangeTotals() =>
    [
        [new[] { 500m, -500m }],              // exactly the minimum
        [new[] { 1_000_000_000m }],           // exactly the maximum
        [new[] { 0m }],
    ];

    [Theory(DisplayName = "150 review F4: a total exactly at either end of the range exports")]
    [MemberData(nameof(InRangeTotals))]
    public async Task TotalAtTheEdgeOfTheRange_Exports(decimal[] approvedTotals)
    {
        var world = new ApExportWorld();
        var invoices = approvedTotals.Select((t, i) => world.AddInvoice($"INV-{i}", approved: t)).ToArray();
        world.GrantEverything();

        var result = await world.ExportAsync(invoices);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        world.Batches().Single().GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(approvedTotals.Sum());
    }

    // =========================================================================================
    // 6. Step 9 counts only a move THIS batch made
    // =========================================================================================

    [Fact(DisplayName = "150 review F6: a lifecycle answer 'already in the target status' is NOT a successful send - the batch stops, is Failed, and nothing stays claimed")]
    public async Task AlreadyInTarget_IsNotASuccessfulSend()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var first = new[] { a, b }.OrderBy(i => i.Id).First();
        world.Lifecycle.ReportsAlreadyInTarget.Add(first.Id); // the service says it was already there; this batch moved nothing

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        result.Code.Should().Be(ApExportCodes.TransitionFailed);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.Batches().Single().GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the invoice is still Approved, so its claim is released");
        world.StoredFiles().Should().OnlyContain(f => f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "150 review F6: an invoice another writer moved away from Approved (not to Sent to AP / Paid) while its move failed is NOT counted as sent - its claim is released")]
    public async Task StatusChangedByAnotherWriter_IsNotCountedAsSent()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Lifecycle.Fails.Add(a.Id);
        world.Lifecycle.BeforeTransition = id => world.Dataverse.Touch("sprk_invoice", id, new Dictionary<string, object>
        {
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Rejected),
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateInactive),
        });

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Failed, "nothing was sent: the invoice was rejected by someone else");
        result.Code.Should().Be(ApExportCodes.TransitionFailed);
        world.BatchOfInvoice(a.Id).Should().BeNull("not sent by this batch, so the claim is released");
    }

    [Theory(DisplayName = "150 review F6: an invoice that IS Sent to AP or Paid when its move failed (a lost response, a fast follower) is counted as sent - its claim is kept")]
    [InlineData(InvoiceStatusCodes.SentToAp)]
    [InlineData(InvoiceStatusCodes.Paid)]
    public async Task SentOrPaid_IsCountedAsSent_AndKeepsItsClaim(int status)
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Lifecycle.Fails.Add(a.Id);
        world.Lifecycle.BeforeTransition = id => world.Dataverse.Touch("sprk_invoice", id, new Dictionary<string, object>
        {
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateCodeOf(status == InvoiceStatusCodes.Paid ? InvoiceLifecycleStatus.Paid : InvoiceLifecycleStatus.SentToAp)),
        });

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.TransitionIncomplete);
        world.BatchOfInvoice(a.Id).Should().Be(result.BatchId, "a sent invoice keeps its claim so it can never be exported again");
    }

    [Fact(DisplayName = "150 review F6: when the invoice's status cannot be read after a failed move it counts as sent (fail safe) - its claim is kept")]
    public async Task UnreadableStatus_IsCountedAsSent()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Lifecycle.Fails.Add(a.Id);
        var armed = false;
        world.Lifecycle.BeforeTransition = _ => armed = true;
        world.Dataverse.FaultOn = (op, table, _) => armed && op == "retrieve" && table == "sprk_invoice" ? new InvalidOperationException("dataverse down") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.TransitionIncomplete);
        world.BatchOfInvoice(a.Id).Should().Be(result.BatchId);
    }

    // =========================================================================================
    // 7. ApprovedOn is the full UTC instant
    // =========================================================================================

    [Fact(DisplayName = "150 review F7: ApprovedOn is the full ISO 8601 UTC timestamp in both files (a UTC date alone can be a day off from the approver's local date)")]
    public async Task ApprovedOn_IsTheFullUtcInstant_InBothFiles()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        var approval = world.Dataverse.RowsOf("sprk_invoiceapproval").Single();
        world.Dataverse.Touch("sprk_invoiceapproval", approval.Id, new Dictionary<string, object>
        {
            ["sprk_decidedon"] = new DateTime(2026, 10, 20, 23, 30, 15, DateTimeKind.Utc),
        });

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        var csv = world.TextOf(world.FileOf(result.BatchId!.Value, Csv)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        csv[1].Should().Contain(",Pat Approver,2026-10-20T23:30:15Z,");
        InvoicesOf(world, result.BatchId.Value)[0].GetProperty("ApprovedOn").GetString().Should().Be("2026-10-20T23:30:15Z");
    }
}
