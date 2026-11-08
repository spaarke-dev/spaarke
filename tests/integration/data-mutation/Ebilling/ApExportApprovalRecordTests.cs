using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 165b-3 part B (EB-D-208): AP export decides only from server-written sources. An invoice counts as Approved only when an
/// approve Decision Record the writer identity created exists for it (<c>createdby</c> the writer, <c>createdonbehalfby</c> empty). Without it the
/// candidate list leaves the invoice out, an explicit request is refused per invoice (<c>approval_not_recorded</c>, nothing claimed) and the
/// re-check after the claim refuses (409 <c>invoice_conflict</c>) before any file or Sent to AP write. Over the in-memory Dataverse
/// (<see cref="ApExportWorld"/>) with the real service.
/// </summary>
/// <remarks>Seed (notes/165b-integrity-checks.md section 6.2): with the record requirement dropped from the selection, R1 and R3 fail.</remarks>
public sealed class ApExportApprovalRecordTests
{
    private static async Task<IReadOnlyList<Guid>> CandidateIds(ApExportWorld world) =>
        (await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None))!.Candidates.Select(c => c.InvoiceId).ToList();

    private static void AssertRefusedNothingClaimed(ApExportWorld world, ApExportResult result, Entity invoice, int writesBefore)
    {
        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused, result.Detail);
        result.Code.Should().Be(ApExportCodes.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(invoice.Id, ApExportCodes.ReasonApprovalNotRecorded));
        world.Dataverse.Writes.Should().Be(writesBefore, "nothing is created, claimed or sent");
        world.Batches().Should().BeEmpty();
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
        world.BatchOfInvoice(invoice.Id).Should().BeNull();
        world.Log.Entries.Should().Contain((LogLevel.Warning, $"AP export refusal ebilling.apexport.approval_not_recorded on sprk_invoice {invoice.Id}"));
    }

    [Fact(DisplayName = "165b-3 R1 must fire: an Approved invoice with no writer-created approve record is left out of the candidates and refused by the export (approval_not_recorded), nothing claimed")]
    public async Task R1_ApprovedWithoutTheRecord_IsLeftOut()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-NOREC", approveRecord: false);
        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        (await CandidateIds(world)).Should().NotContain(invoice.Id);
        AssertRefusedNothingClaimed(world, await world.ExportAsync(invoice), invoice, writes);
    }

    [Fact(DisplayName = "165b-3 R1 control: the same invoice with the writer's approve record is a candidate and exports to Sent to AP; one batched record read per check")]
    public async Task R1_Control_TheWritersRecord_Exports()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-REC");
        var other = world.AddInvoice("INV-REC-2");
        world.GrantEverything();

        (await CandidateIds(world)).Should().Contain([invoice.Id, other.Id]);
        world.Dataverse.Log.Count(l => l == "query sprk_decisionrecord").Should().Be(1, "one batched read for the whole candidate list");
        world.Dataverse.Log.Count(l => l == "query sprk_invoiceapproval").Should().Be(1, "the approval keys: one batched portion read, never one per invoice");

        world.Dataverse.Log.Clear();
        var result = await world.ExportAsync(invoice, other);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.StatusOfInvoice(other.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.Dataverse.Log.Count(l => l == "query sprk_decisionrecord").Should().Be(2, "one batched read at the selection and one at the re-check, never one per invoice");
        world.Log.Entries.Should().NotContain(e => e.Message.Contains("approval_not_recorded", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "165b-3 R3 must fire: an approve record created by another identity, or on the writer's behalf by one, does not count - the invoice is left out")]
    [InlineData("another-identity")]
    [InlineData("on-behalf-of-the-writer")]
    public async Task R3_ARecordNotCreatedByTheWriter_DoesNotCount(string kind)
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-FORGED", approveRecord: false);
        var someone = Guid.NewGuid();
        _ = kind == "another-identity"
            ? world.AddApproveRecord(invoice, createdBy: someone)
            : world.AddApproveRecord(invoice, onBehalfBy: someone);
        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        (await CandidateIds(world)).Should().NotContain(invoice.Id);
        AssertRefusedNothingClaimed(world, await world.ExportAsync(invoice), invoice, writes);
    }

    [Fact(DisplayName = "165b-3 R3 control: the writer's own approve record counts (createdby the writer, createdonbehalfby empty)")]
    public async Task R3_Control_TheWritersOwnRecord_Counts()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-WRITER", approveRecord: false);
        world.AddApproveRecord(invoice, createdBy: DecisionRecordWriterTests.WriterUser);
        world.GrantEverything();

        (await CandidateIds(world)).Should().Contain(invoice.Id);
        (await world.ExportAsync(invoice)).Outcome.Should().Be(ApExportOutcome.Created);
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "165b-3 part B: the re-check after the claim refuses an invoice whose approve record no longer counts (409 invoice_conflict) before any file or Sent to AP write; claims released")]
    public async Task ReCheck_ARecordThatNoLongerCounts_IsRefusedBeforeAnyFile()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-RECHECK", approveRecord: false);
        var record = world.AddApproveRecord(invoice);
        world.GrantEverything();
        world.Dataverse.BeforeConditionalUpdate = (table, _) =>
        {
            if (table == "sprk_invoice")
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                world.Dataverse.Touch("sprk_decisionrecord", record.Id,
                    new Dictionary<string, object> { ["createdonbehalfby"] = new EntityReference("systemuser", Guid.NewGuid()) });
            }
        };

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Conflict, result.Detail);
        result.Code.Should().Be(ApExportCodes.InvoiceConflict);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the claim is released");
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    // ── Fix round (uac-r2 F1): the record counts only under the approval's own key, as completion matches it ──

    [Theory(DisplayName = "165b-3 fix round (uac-r2 F1) must fire: a writer-created approve record regarding the invoice under another key does not count - left out of the candidates, refused by the export (approval_not_recorded), nothing claimed")]
    [InlineData("another-invoice's-key")]
    [InlineData("an-unrelated-key")]
    [InlineData("no-key")]
    public async Task KeyMismatch_ARecordUnderAnotherKey_DoesNotCount(string kind)
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-OTHER-KEY", approveRecord: false);
        var other = world.AddInvoice("INV-KEY-OWNER", approveRecord: false);
        var record = world.AddApproveRecord(invoice, reviewId: kind == "another-invoice's-key" ? world.ApprovalKeyOf(other) : Guid.NewGuid());
        if (kind == "no-key")
        {
            world.Dataverse.Touch("sprk_decisionrecord", record.Id, new Dictionary<string, object> { ["sprk_steps"] = "{}" });
        }

        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        (await CandidateIds(world)).Should().NotContain(invoice.Id);
        AssertRefusedNothingClaimed(world, await world.ExportAsync(invoice), invoice, writes);
    }

    [Fact(DisplayName = "165b-3 fix round (uac-r2 F1) control: the writer-created record under the approval's own key counts - a candidate, and it exports")]
    public async Task KeyMismatch_Control_TheApprovalsOwnKey_Counts()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-OWN-KEY", approveRecord: false);
        world.AddApproveRecord(invoice, reviewId: world.ApprovalKeyOf(invoice));
        world.GrantEverything();

        (await CandidateIds(world)).Should().Contain(invoice.Id);
        (await world.ExportAsync(invoice)).Outcome.Should().Be(ApExportOutcome.Created);
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "165b-3 fix round (uac-r2 F1) must fire: an Approved invoice with the writer's record but no approval portion does not count (fail closed) - left out and refused")]
    public async Task KeyMismatch_NoPortion_DoesNotCount()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-NO-PORTION");
        var portion = world.Dataverse.RowsOf("sprk_invoiceapproval").Single(r => r.GetAttributeValue<EntityReference>("sprk_invoice")?.Id == invoice.Id);
        await world.Dataverse.Service.DeleteAsync("sprk_invoiceapproval", portion.Id, CancellationToken.None);
        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        (await CandidateIds(world)).Should().NotContain(invoice.Id);
        AssertRefusedNothingClaimed(world, await world.ExportAsync(invoice), invoice, writes);
    }

    [Fact(DisplayName = "165b-3 fix round (uac-r2 F1) must fire: the re-check after the claim refuses a record whose key no longer matches (409 invoice_conflict) before any file or Sent to AP write")]
    public async Task KeyMismatch_ReCheck_IsRefusedBeforeAnyFile()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-RECHECK-KEY", approveRecord: false);
        var record = world.AddApproveRecord(invoice);
        world.GrantEverything();
        world.Dataverse.BeforeConditionalUpdate = (table, _) =>
        {
            if (table == "sprk_invoice")
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                world.Dataverse.Touch("sprk_decisionrecord", record.Id,
                    new Dictionary<string, object> { ["sprk_steps"] = $"{{\"reviewId\":\"{Guid.NewGuid():D}\"}}" });
            }
        };

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Conflict, result.Detail);
        result.Code.Should().Be(ApExportCodes.InvoiceConflict);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the claim is released");
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "165b-3 part B writer-id fault: the writer identity cannot be read - the export is refused (503 unavailable, nothing created or claimed) and the candidate list fails (nothing shown)")]
    public async Task WriterIdFault_FailsClosed()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-FAULT");
        world.GrantEverything();
        world.Writer.WhoAmIFault = new TimeoutException("whoami");
        var writes = world.Dataverse.Writes;

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        result.Code.Should().Be(ApExportCodes.Unavailable);
        world.Dataverse.Writes.Should().Be(writes);
        world.Batches().Should().BeEmpty();
        world.BatchOfInvoice(invoice.Id).Should().BeNull();

        var list = () => world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);
        await list.Should().ThrowAsync<TimeoutException>("a fault never reads as 'no records'");
    }
}
