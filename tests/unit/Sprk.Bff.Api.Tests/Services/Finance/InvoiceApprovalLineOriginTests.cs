using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Signals;
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

    private const string BaseRefusal = "A line on this invoice has no known origin, so the invoice cannot be approved. An administrator must review the invoice's lines.";
    private const string RepeatTheAdd = " If a line was added and the add did not finish, repeat the add.";

    /// <param name="addedWithoutRecord">K6: the refused line passes the Add line marker and id rule but has no matching record, so the text adds how to recover.</param>
    private static async Task<ApprovalResult> RefusedFor(ApprovalWorld w, Guid lineId, bool addedWithoutRecord = false)
    {
        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.LineOriginUnknown);
        result.Detail.Should().Be(addedWithoutRecord ? BaseRefusal + RepeatTheAdd : BaseRefusal);
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
    [InlineData(InvoiceLineCostType.Fee, InvoiceLineStatus.Accepted)]
    [InlineData(InvoiceLineCostType.Expense, InvoiceLineStatus.Accepted)]
    // F3: a Clean stray line is refused before the Clean bulk-accept write (Writes == 0 is asserted by RefusedFor).
    [InlineData(InvoiceLineCostType.Fee, InvoiceLineStatus.Clean)]
    [InlineData(InvoiceLineCostType.Expense, InvoiceLineStatus.Clean)]
    public async Task ALineOfUnknownOrigin_IsRefused(InvoiceLineCostType type, InvoiceLineStatus status)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var stray = w.AddLine(status, 100m, type, approved: status == InvoiceLineStatus.Accepted ? 100m : null, id: Guid.NewGuid());

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

        await RefusedFor(w, added.Id, addedWithoutRecord: true);
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

        await RefusedFor(w, added.Id, addedWithoutRecord: true);
    }

    [Fact(DisplayName = "165b must fire (K4): an add-line record with createdby = the writer but createdonbehalfby set does not count")]
    public async Task ARecordCreatedOnBehalfOfTheWriter_IsRefused()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);

        // The same record, createdby the writer as Dataverse stores it, but created on the writer's behalf by another identity.
        var record = w.H.Created.Single();
        w.H.Created.Clear();
        record["createdby"] = new EntityReference("systemuser", DecisionRecordWriterTests.WriterUser);
        record["createdonbehalfby"] = new EntityReference("systemuser", BobUser);
        w.H.Seeded.Add(record);

        await RefusedFor(w, added.Id, addedWithoutRecord: true);
    }

    [Fact(DisplayName = "165b must fire: an add-line record of another decision, or whose facts name another invoice, does not count for the line")]
    public async Task ARecordOfAnotherDecisionOrInvoice_IsRefused()
    {
        var other = new ApprovalWorld();
        var added = AddedLine(other, AddDecision);
        await other.RecordAddedLineAsync(added.Id, Guid.Parse("165b0000-0000-4000-8000-0000000000b2"));

        await RefusedFor(other, added.Id, addedWithoutRecord: true);

        var named = new ApprovalWorld();
        var line = AddedLine(named, AddDecision);
        await named.RecordAddedLineAsync(line.Id, AddDecision, factsInvoiceId: Guid.NewGuid());

        await RefusedFor(named, line.Id, addedWithoutRecord: true);
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

    [Fact(DisplayName = "165b must not fire (D-96): lines detached, the invoice and its lines moved to a new core, then re-attached keep their ids and origin, and approve")]
    public async Task LinesDetachedMovedToANewCoreAndReattached_Approve()
    {
        var w = new ApprovalWorld();
        var intake = w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var added = AddedLine(w, AddDecision);
        await w.RecordAddedLineAsync(added.Id, AddDecision);
        var lines = new[] { intake, added };

        // The D-96 sequence: detach every line, move the invoice and each line to a new core record, re-attach the lines.
        foreach (var line in lines)
        {
            w.Rows[("sprk_billingevent", line.Id)].Attributes.Remove("sprk_invoice");
            w.H.Rows[("sprk_billingevent", line.Id)].Attributes.Remove("sprk_invoice");
        }

        var newCoreId = Guid.NewGuid();
        var newCore = new EntityReference(w.CoreEntity, newCoreId);
        var oldCore = w.Core;
        w.Put(new Entity(w.CoreEntity, newCoreId)
        {
            ["sprk_assignedattorney1"] = oldCore["sprk_assignedattorney1"],
            ["sprk_assignedtointernal"] = oldCore["sprk_assignedtointernal"],
            ["sprk_assignedparalegal1"] = oldCore["sprk_assignedparalegal1"],
        });
        var coreColumn = w.CoreEntity == "sprk_workassignment" ? "sprk_regardingworkassignment" : w.CoreEntity;
        w.Invoice[coreColumn] = newCore;
        w.Invoice["sprk_regardingrecordid"] = newCoreId.ToString("D");
        w.H.Rows[("sprk_invoice", w.InvoiceId)][coreColumn] = newCore;
        w.Portion[CoreAncestorResolver.StampColumnFor(w.CoreEntity)] = newCore;
        foreach (var line in lines)
        {
            w.H.Rows[("sprk_billingevent", line.Id)][coreColumn] = newCore;
        }

        foreach (var line in lines)
        {
            var reference = new EntityReference("sprk_invoice", w.InvoiceId);
            w.Rows[("sprk_billingevent", line.Id)]["sprk_invoice"] = reference;
            w.H.Rows[("sprk_billingevent", line.Id)]["sprk_invoice"] = reference;
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, $"{result.Code} {result.Detail}");
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(InvoiceLineOrigin.UnknownOriginCode));
    }
}
