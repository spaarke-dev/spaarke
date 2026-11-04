using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095c-4 (095c-3 review; uac-r2 2026-11-03): an invoice that needs a firm is never exported. The selection refuses an invoice with
/// no firm, or with a stored <c>firm_missing</c> hold on its submission (<see cref="InvoiceFirmHold"/>) even when the firm column was set by a direct
/// write; the re-check after the claim (the guard of the Sent to AP writer) refuses one that lost its firm or gained a hold since. Over the
/// in-memory Dataverse (<see cref="ApExportWorld"/>) with the real service.
/// </summary>
/// <remarks>
/// <b>Seed proof (notes 095c section 14).</b> <c>FirmSetNatively_WhileHeld_IsRefused_NothingWritten</c> and <c>NoFirm_IsRefused_NothingWritten</c>
/// fail when the selection check is removed.
/// </remarks>
public sealed class ApExportFirmHoldTests
{
    private static Entity HoldForFirm(ApExportWorld world, Entity invoice, string? reason = SubmissionReasonCodes.FirmMissing, string? findingsJson = null)
    {
        var submission = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice.Id),
            ["sprk_outcomereasoncode"] = reason,
            ["sprk_findingsjson"] = findingsJson
                ?? """{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing","values":{}}],"lineFindings":[]}""",
        };
        world.Dataverse.Put(submission);
        return submission;
    }

    private static void AssertNothingExported(ApExportWorld world, params Entity[] invoices)
    {
        world.Lifecycle.Sent.Should().BeEmpty("no invoice is sent to AP");
        world.StoredFiles().Should().BeEmpty("no file is written");
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("every claim is released");
        foreach (var invoice in invoices)
        {
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
        }
    }

    [Fact(DisplayName = "095c-4: an Approved invoice with no firm is refused as invoice_firm_missing - the whole request, nothing claimed")]
    public async Task NoFirm_IsRefused_NothingWritten()
    {
        var world = new ApExportWorld();
        var good = world.AddInvoice("INV-GOOD");
        var bad = world.AddInvoice("INV-NOFIRM");
        world.Dataverse.Touch("sprk_invoice", bad.Id, new Dictionary<string, object> { ["sprk_vendororg"] = DBNull.Value });
        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        var result = await world.ExportAsync(good, bad);

        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        result.Code.Should().Be(ApExportCodes.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(bad.Id, ApExportCodes.ReasonFirmMissing));
        world.Dataverse.Writes.Should().Be(writes, "nothing is created, claimed or sent");
        AssertNothingExported(world, good, bad);
    }

    [Fact(DisplayName = "095c-4 uac-r2: the firm set by a direct write while the firm_missing hold is on - the export is still refused, nothing claimed")]
    public async Task FirmSetNatively_WhileHeld_IsRefused_NothingWritten()
    {
        var world = new ApExportWorld();
        var held = world.AddInvoice("INV-HELD"); // AddInvoice sets a firm: the firm column says "set", the stored hold says "needed"
        HoldForFirm(world, held);
        world.GrantEverything();
        var writes = world.Dataverse.Writes;

        var result = await world.ExportAsync(held);

        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(held.Id, ApExportCodes.ReasonFirmMissing));
        world.Dataverse.Writes.Should().Be(writes);
        AssertNothingExported(world, held);
    }

    [Theory(DisplayName = "095c-4: each stored marker holds alone (the reason, the pipeline finding, the caused line finding)")]
    [InlineData("reason")]
    [InlineData("pipeline")]
    [InlineData("line")]
    public async Task EachStoredMarker_HoldsAlone(string marker)
    {
        var world = new ApExportWorld();
        var held = world.AddInvoice("INV-HELD");
        HoldForFirm(world, held,
            reason: marker == "reason" ? SubmissionReasonCodes.FirmMissing : null,
            findingsJson: marker switch
            {
                "pipeline" => """{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing"}]}""",
                "line" => """{"lineFindings":[{"line":0,"code":"rules.not_checked","cause":"ebilling.submission.firm_missing"}]}""",
                _ => "{}",
            });
        world.GrantEverything();

        var result = await world.ExportAsync(held);

        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(held.Id, ApExportCodes.ReasonFirmMissing), marker);
        AssertNothingExported(world, held);
    }

    [Fact(DisplayName = "095c-4 must not fire: a firm invoice whose submission has a refused-rule line (no firm cause) and no hold exports")]
    public async Task FirmInvoice_WithoutAHold_Exports()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        HoldForFirm(world, a, reason: null, findingsJson: """{"pipelineFindings":[],"lineFindings":[{"line":0,"code":"rules.not_checked"}]}""");
        world.GrantEverything();

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Created);
        world.Lifecycle.Sent.Should().ContainSingle().Which.Should().Be(a.Id);
    }

    [Fact(DisplayName = "095c-4: the firm holds cannot be read - Unavailable, nothing claimed (fail closed)")]
    public async Task HoldReadFault_IsUnavailable_NothingWritten()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        var writes = world.Dataverse.Writes;
        world.Dataverse.FaultOn = (op, table, _) => op == "query" && table == "sprk_invoicesubmission" ? new TimeoutException("read timed out") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        world.Dataverse.Writes.Should().Be(writes);
        AssertNothingExported(world, a);
    }

    [Theory(DisplayName = "095c-4: the Sent to AP guard - a hold that appears, or a firm that is cleared, after the claim is 409 firm_missing; nothing is exported and every claim is released")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HoldOrNoFirm_AfterTheClaim_Is409_NothingExported(bool hold)
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
                // After the selection read and the first claim: the first invoice gains a hold, or loses its firm.
                world.Dataverse.BeforeConditionalUpdate = null;
                if (hold)
                {
                    HoldForFirm(world, first);
                }
                else
                {
                    world.Dataverse.Touch("sprk_invoice", first.Id, new Dictionary<string, object> { ["sprk_vendororg"] = DBNull.Value });
                }
            }
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Conflict);
        result.Code.Should().Be(ApExportCodes.FirmMissing);
        result.Detail.Should().Contain("A reviewer must add the firm first");
        world.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        AssertNothingExported(world, a, b);
        world.BatchOfInvoice(a.Id).Should().BeNull();
        world.BatchOfInvoice(b.Id).Should().BeNull();
    }
}
