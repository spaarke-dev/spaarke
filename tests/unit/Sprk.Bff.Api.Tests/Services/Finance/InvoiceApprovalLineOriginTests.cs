using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 165b item 1 (task 164 criterion): approval refuses a line of unknown origin. A Fee or Expense line is known when it is the
/// invoice's own submission's intake line, or a line Add line created on this invoice with the <c>add-line</c> Decision Record the writer
/// identity wrote. Otherwise 409 <c>ebilling.approve.line_origin_unknown</c>, nothing written, one log line per line with the table and row id.
/// Over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the real services.
/// </summary>
public sealed class InvoiceApprovalLineOriginTests
{
    private static readonly Guid AddDecision = Guid.Parse("165b0000-0000-4000-8000-0000000000a1");

    private static async Task<ApprovalResult> RefusedFor(ApprovalWorld w, Guid lineId)
    {
        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.LineOriginUnknown);
        result.Detail.Should().Be("A line on this invoice has no known origin, so the invoice cannot be approved. An administrator must review the invoice's lines.");
        w.Writes.Should().Be(0, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(InvoiceLineOrigin.UnknownOriginCode))
            .Which.Message.Should().Be($"Approval refusal {InvoiceLineOrigin.UnknownOriginCode} on sprk_billingevent {lineId}");
        return result;
    }

    /// <summary>A line Add line created on this invoice: the derived id and the reviewer-added marker.</summary>
    private static Entity AddedLine(ApprovalWorld w, Guid decisionId, InvoiceLineStatus status = InvoiceLineStatus.Accepted) =>
        w.AddLine(status, 200m, extraction: ExtractionProvenance.ForReviewerAddedLine(decisionId), approved: 200m,
            id: ExtractionProvenance.AddedLineIdFor(w.InvoiceId, decisionId));

    // ── Must fire ──

    [Theory(DisplayName = "165b must fire: a Fee or Expense line that is neither an intake line nor an added line is refused (409 line_origin_unknown), nothing written")]
    [InlineData(InvoiceLineCostType.Fee)]
    [InlineData(InvoiceLineCostType.Expense)]
    public async Task ALineOfUnknownOrigin_IsRefused(InvoiceLineCostType type)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var stray = w.AddLine(InvoiceLineStatus.Accepted, 100m, type, approved: 100m, id: Guid.NewGuid());

        await RefusedFor(w, stray.Id);
    }

    [Fact(DisplayName = "165b must fire: another submission's intake line on this invoice is refused (an intake line belongs to its own submission's invoice)")]
    public async Task AnotherSubmissionsIntakeLine_IsRefused()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var other = w.AddLine(InvoiceLineStatus.Accepted, 100m, approved: 100m, id: InvoiceSubmissionPersister.LineIdFor(Guid.NewGuid(), 1));

        await RefusedFor(w, other.Id);
    }

    [Fact(DisplayName = "165b must fire: the reviewer-added marker and the derived id without the add-line record are refused (the marker alone is not an exemption)")]
    public async Task AMarkedLineWithoutItsRecord_IsRefused()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var added = AddedLine(w, AddDecision);
        ExtractionProvenance.IsReviewerAdded(added).Should().BeTrue("the setup: the marker and id rule hold");

        await RefusedFor(w, added.Id);
    }

    [Fact(DisplayName = "165b must fire: an add-line record the writer identity did not create does not count")]
    public async Task ARecordNotCreatedByTheWriter_IsRefused()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);

        // The same record as another identity would store it: every column equal, createdby not the writer's.
        var record = w.H.Created.Single();
        w.H.Created.Clear();
        record["createdby"] = new EntityReference("systemuser", BobUser);
        w.H.Seeded.Add(record);

        await RefusedFor(w, added.Id);
    }

    [Fact(DisplayName = "165b must fire: an add-line record of another decision, or whose facts name another invoice, does not count for the line")]
    public async Task ARecordOfAnotherDecisionOrInvoice_IsRefused()
    {
        var other = new ApprovalWorld();
        var added = AddedLine(other, AddDecision);
        await other.RecordAddedLineAsync(added.Id, Guid.Parse("165b0000-0000-4000-8000-0000000000b2"));

        await RefusedFor(other, added.Id);

        var named = new ApprovalWorld();
        var line = AddedLine(named, AddDecision);
        await named.RecordAddedLineAsync(line.Id, AddDecision, factsInvoiceId: Guid.NewGuid());

        await RefusedFor(named, line.Id);
    }

    [Fact(DisplayName = "165b must fire: a line added on another invoice and moved here is refused, even with that invoice's record")]
    public async Task ALineAddedOnAnotherInvoice_IsRefused()
    {
        var w = new ApprovalWorld();
        var elsewhere = Guid.NewGuid();
        var moved = w.AddLine(InvoiceLineStatus.Accepted, 200m, extraction: ExtractionProvenance.ForReviewerAddedLine(AddDecision), approved: 200m,
            id: ExtractionProvenance.AddedLineIdFor(elsewhere, AddDecision));
        await w.RecordAddedLineAsync(moved.Id, AddDecision, factsInvoiceId: elsewhere);

        await RefusedFor(w, moved.Id);
    }

    [Fact(DisplayName = "165b fail closed: a fault reading the add-line records is Unavailable, nothing written")]
    public async Task AFaultReadingTheRecords_IsUnavailable()
    {
        var w = new ApprovalWorld();
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);
        w.Faults["read:sprk_decisionrecord"] = new InvalidOperationException("simulated read fault");

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Unavailable, result.Detail);
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);
    }

    // ── Must not fire ──

    [Fact(DisplayName = "165b must not fire: intake lines (Fee and Expense) and an Add line line with its record approve; an Adjustment line is not checked")]
    public async Task IntakeLinesAndARecordedAddedLine_Approve()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        w.AddLine(InvoiceLineStatus.Accepted, 100m, InvoiceLineCostType.Expense, approved: 100m);
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);
        w.AddLine(InvoiceLineStatus.Accepted, -50m, InvoiceLineCostType.Adjustment, approved: -50m, id: Guid.NewGuid());

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, $"{result.Code} {result.Detail}");
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(InvoiceLineOrigin.UnknownOriginCode));
    }

    [Fact(DisplayName = "165b must not fire (D-96): a line detached and re-attached to the same invoice keeps its id and its origin, and approves")]
    public async Task ALineReattachedToTheSameInvoice_Approves()
    {
        var w = new ApprovalWorld();
        var intake = w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);
        foreach (var line in new[] { intake, added })
        {
            var row = w.Rows[("sprk_billingevent", line.Id)];
            row["sprk_invoice"] = null;
            row["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId);
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, $"{result.Code} {result.Detail}");
    }
}
