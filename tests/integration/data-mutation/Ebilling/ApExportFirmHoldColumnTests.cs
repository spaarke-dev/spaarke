using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095c-5 (uac-r2 2026-11-04) and the 095c-4 review fixes, AP export side: the invoice's own field-secured hold
/// (<c>sprk_invoice.sprk_firmhold</c>) keeps an invoice out of an export after its submission is unlinked, repointed or deleted, and after the reason
/// is rewritten and the firm set with the findings unchanged; a read fault or a missing column refuses; a firm invoice with no hold exports; the
/// candidates list offers no invoice the export would refuse (K2); and the refusal sentences fit the reason (F1, K1). Over the in-memory Dataverse
/// (<see cref="ApExportWorld"/>, the app-only store) with the real service.
/// </summary>
/// <remarks>
/// <b>Seed proof (notes 095c section 15).</b> With the invoice-side read removed from <see cref="InvoiceFirmHold.HeldInvoicesAsync"/>,
/// <c>SubmissionUnlinkedRepointedOrDeleted_IsRefused_NothingWritten</c> (all three cases) and <c>Candidates_LeaveOutEveryInvoiceTheExportRefuses</c> fail.
/// </remarks>
public sealed class ApExportFirmHoldColumnTests
{
    private const string FindingsWithTheHold = """{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing","values":{}}],"lineFindings":[]}""";

    private static Entity Submission(ApExportWorld world, Entity invoice, string? reason = SubmissionReasonCodes.FirmMissing, string? findingsJson = FindingsWithTheHold)
    {
        var submission = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice.Id),
            ["sprk_outcomereasoncode"] = reason,
            ["sprk_findingsjson"] = findingsJson,
        };
        world.Dataverse.Put(submission);
        return submission;
    }

    private static void HoldInvoice(ApExportWorld world, Entity invoice) =>
        world.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object> { [InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceHoldValue });

    private static void AssertNothingExported(ApExportWorld world, int writesBefore, params Entity[] invoices)
    {
        world.Dataverse.Writes.Should().Be(writesBefore, "nothing is created, claimed or sent");
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        foreach (var invoice in invoices)
        {
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
        }
    }

    [Theory(DisplayName = "095c-5 uac-r2 (1)(2): the submission unlinked, repointed or deleted - the invoice's own hold still refuses the export, nothing claimed")]
    [InlineData("unlinked")]
    [InlineData("repointed")]
    [InlineData("deleted")]
    public async Task SubmissionUnlinkedRepointedOrDeleted_IsRefused_NothingWritten(string write)
    {
        var world = new ApExportWorld();
        var held = world.AddInvoice("INV-HELD"); // the firm column says "set" (a direct write); the hold says "needed"
        var submission = Submission(world, held);
        HoldInvoice(world, held);
        switch (write)
        {
            case "unlinked":
                world.Dataverse.Touch("sprk_invoicesubmission", submission.Id, new Dictionary<string, object> { ["sprk_invoice"] = DBNull.Value });
                break;
            case "repointed":
                world.Dataverse.Touch("sprk_invoicesubmission", submission.Id, new Dictionary<string, object> { ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()) });
                break;
            default:
                await world.Dataverse.Service.DeleteAsync("sprk_invoicesubmission", submission.Id, CancellationToken.None);
                break;
        }

        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        var result = await world.ExportAsync(held);

        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused, write);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(held.Id, ApExportCodes.ReasonFirmMissing));
        result.Detail.Should().Be(
            "An invoice in the selection has no firm, or is waiting for one: a reviewer must add the firm first. Nothing was exported.",
            "095c-4 review F1: the sentence fits the reason; it does not say 'only Approved invoices'");
        AssertNothingExported(world, writes, held);
    }

    [Theory(DisplayName = "095c-5 uac-r2 (3) / 095c-4 review pin: the reason rewritten and the firm set, the findings JSON unchanged - the export stays refused")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReasonRewritten_FirmSet_FindingsUnchanged_IsRefused(bool invoiceHold)
    {
        var world = new ApExportWorld();
        var held = world.AddInvoice("INV-HELD");
        var submission = Submission(world, held, reason: "ebilling.submission.accepted"); // the reason rewritten; the findings as the persister wrote them
        if (invoiceHold)
        {
            HoldInvoice(world, held);
        }

        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        var result = await world.ExportAsync(held);

        world.Dataverse.Get("sprk_invoicesubmission", submission.Id).GetAttributeValue<string>("sprk_findingsjson").Should().Be(FindingsWithTheHold);
        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Reason.Should().Be(ApExportCodes.ReasonFirmMissing);
        AssertNothingExported(world, writes, held);
    }

    [Theory(DisplayName = "095c-5 fail closed: the invoice hold cannot be read (a fault, or the column missing in an environment that has not run the script) - Unavailable, nothing claimed")]
    [InlineData("fault")]
    [InlineData("missing column")]
    public async Task InvoiceHoldReadFault_IsUnavailable_NothingWritten(string how)
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        var writes = world.Dataverse.Writes;
        var selectionRead = true;
        world.Dataverse.FaultOn = (op, table, _) =>
        {
            // The selection reads each invoice with RetrieveAsync; the hold is the first QUERY of sprk_invoice.
            if (op != "query" || table != "sprk_invoice" || !selectionRead)
            {
                return null;
            }

            selectionRead = false;
            return how == "fault"
                ? new TimeoutException("read timed out")
                : new InvalidOperationException("'sprk_invoice' entity doesn't contain attribute with Name = 'sprk_firmhold'");
        };

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        AssertNothingExported(world, writes, a);
    }

    [Fact(DisplayName = "095c-5 must not fire: a firm invoice whose hold column is empty, with a submission and no hold, exports")]
    public async Task FirmInvoice_HoldColumnEmpty_Exports()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        Submission(world, a, reason: null, findingsJson: """{"pipelineFindings":[],"lineFindings":[]}""");
        world.GrantEverything();

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Created);
        world.Lifecycle.Sent.Should().ContainSingle().Which.Should().Be(a.Id);
    }

    [Fact(DisplayName = "095c-4 review K2: the candidates leave out every invoice the export refuses (no firm, held by its own hold, held by a submission); a firm invoice stays")]
    public async Task Candidates_LeaveOutEveryInvoiceTheExportRefuses()
    {
        var world = new ApExportWorld();
        var ok = world.AddInvoice("INV-OK");
        var noFirm = world.AddInvoice("INV-NOFIRM");
        world.Dataverse.Touch("sprk_invoice", noFirm.Id, new Dictionary<string, object> { ["sprk_vendororg"] = DBNull.Value });
        var invoiceHeld = world.AddInvoice("INV-INVOICE-HOLD");
        HoldInvoice(world, invoiceHeld);
        var submissionHeld = world.AddInvoice("INV-SUBMISSION-HOLD");
        Submission(world, submissionHeld);
        world.GrantEverything();

        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        candidates!.Candidates.Select(c => c.InvoiceId).Should().BeEquivalentTo([ok.Id]);
    }

    [Fact(DisplayName = "095c-4 review K2: a hold read fault while listing candidates throws (the route answers unavailable), never the unfiltered list")]
    public async Task Candidates_OnAHoldReadFault_Throw()
    {
        var world = new ApExportWorld();
        world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Dataverse.FaultOn = (op, table, _) => op == "query" && table == "sprk_invoicesubmission" ? new TimeoutException("read timed out") : null;

        var list = () => world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        await list.Should().ThrowAsync<TimeoutException>();
    }

    [Fact(DisplayName = "095c-4 review F1: a selection refused for several reasons says so; one refused only for other reasons keeps its sentence")]
    public void TheSelectionSentence_FitsTheReasons()
    {
        var firm = new ApExportInvoiceRefusal(Guid.NewGuid(), ApExportCodes.ReasonFirmMissing);
        var notApproved = new ApExportInvoiceRefusal(Guid.NewGuid(), ApExportCodes.ReasonNotApproved);

        ApExportService.SelectionRefusedDetail([notApproved]).Should().Be("Only Approved invoices that are not yet in an export batch can be exported. Nothing was exported.");
        ApExportService.SelectionRefusedDetail([firm, notApproved]).Should().Contain("have a firm");
        ApExportService.SelectionRefusedDetail([firm]).Should().NotContain("Only Approved").And.Contain("a reviewer must add the firm first");
    }

    [Fact(DisplayName = "095c-4 review K1: a hold that appears after the claim (the firm column set) is 409 with the 'waiting for its firm' sentence")]
    public async Task HoldAfterTheClaim_SaysWaitingForItsFirm()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var last = a.Id.CompareTo(b.Id) < 0 ? b : a; // claims run in id order
        var first = ReferenceEquals(last, a) ? b : a;
        world.Dataverse.BeforeConditionalUpdate = (table, id) =>
        {
            if (table == "sprk_invoice" && id == last.Id)
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                HoldInvoice(world, first);
            }
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Conflict);
        result.Code.Should().Be(ApExportCodes.FirmMissing);
        result.Detail.Should().Contain("waiting for its firm to be assigned").And.NotContain("has no firm");
        world.Lifecycle.Sent.Should().BeEmpty();
        world.BatchOfInvoice(a.Id).Should().BeNull();
        world.BatchOfInvoice(b.Id).Should().BeNull();
    }
}
