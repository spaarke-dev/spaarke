using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 165b-2 T2 (EB-D-200 item 3): AP export decides only from server-written sources, so an Approved invoice whose stored totals differ from
/// what <see cref="InvoiceTotalsCalculator"/> derives from its lines is left out, the way 165b-3 leaves out an invoice without its approve record: the
/// candidate list does not offer it (the other invoices are offered and export), an explicit request naming it is refused per invoice
/// (<c>totals_mismatch</c>, nothing claimed), and the re-check after the claim refuses (409 <c>ebilling.apexport.totals_mismatch</c>) before any file or
/// Sent to AP write. Over the in-memory Dataverse (<see cref="ApExportWorld"/>, whose invoices carry the totals the calculator derives from their lines)
/// with the real service.
/// </summary>
/// <remarks>Seed (notes/165b-integrity-checks.md section 7): with the totals check dropped from AP export, every must-fire case fails and the controls pass.</remarks>
public sealed class ApExportTotalsTests
{
    private const int Fee = 100000000;

    private static async Task<IReadOnlyList<Guid>> CandidateIds(ApExportWorld world) =>
        (await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None))!.Candidates.Select(c => c.InvoiceId).ToList();

    /// <summary>Two Approved invoices whose stored totals derive from their lines; <paramref name="tamper"/> then makes the second one's differ.</summary>
    private static (ApExportWorld World, Entity Good, Entity Bad) WorldWith(Action<ApExportWorld, Entity> tamper)
    {
        var world = new ApExportWorld();
        var good = world.AddInvoice("INV-GOOD");
        var bad = world.AddInvoice("INV-BAD");
        tamper(world, bad);
        world.GrantEverything();
        return (world, good, bad);
    }

    private static void Tamper(ApExportWorld world, Entity invoice, string kind)
    {
        var line = world.Dataverse.RowsOf("sprk_billingevent").First(l => l.GetAttributeValue<EntityReference>("sprk_invoice")?.Id == invoice.Id);
        switch (kind)
        {
            case "line-edited":
                world.Dataverse.Touch("sprk_billingevent", line.Id, new Dictionary<string, object> { ["sprk_amount"] = new Money(451m) });
                break;
            case "calculator-refuses":
                world.Dataverse.Touch("sprk_billingevent", line.Id, new Dictionary<string, object> { ["sprk_costtype"] = new OptionSetValue(999) });
                break;
            case "total-missing":
                world.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object> { ["sprk_feestotal"] = null! });
                break;
            default:
                var stored = world.Invoice(invoice.Id).GetAttributeValue<Money>(kind).Value;
                world.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object> { [kind] = new Money(stored + 0.01m) });
                break;
        }
    }

    [Theory(DisplayName = "165b-2 T2 must fire: an invoice whose stored totals differ from its lines is left out - not a candidate, refused per invoice (totals_mismatch, nothing claimed) - and the other invoice exports")]
    [InlineData("sprk_feestotal")]
    [InlineData("sprk_expensestotal")]
    [InlineData("sprk_lineadjustmentstotal")]
    [InlineData("sprk_invoiceadjustmentstotal")]
    [InlineData("sprk_approvedtotal")]
    [InlineData("line-edited")]
    [InlineData("calculator-refuses")]
    [InlineData("total-missing")]
    public async Task T2_StoredTotalsThatDiffer_AreLeftOut_AndTheOtherInvoiceExports(string kind)
    {
        var (world, good, bad) = WorldWith((w, invoice) => Tamper(w, invoice, kind));

        var candidates = await CandidateIds(world);
        candidates.Should().Contain(good.Id).And.NotContain(bad.Id);
        world.Log.Entries.Should().Contain((LogLevel.Warning, $"AP export refusal ebilling.apexport.totals_mismatch on sprk_invoice {bad.Id}"));

        // An explicit request naming it is refused per invoice; nothing is created, claimed or sent.
        var writes = world.Dataverse.Writes;
        var refused = await world.ExportAsync(good, bad);
        refused.Outcome.Should().Be(ApExportOutcome.SelectionRefused, refused.Detail);
        refused.Code.Should().Be(ApExportCodes.SelectionRefused);
        refused.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(bad.Id, ApExportCodes.ReasonTotalsMismatch));
        refused.Detail.Should().Be("An invoice in the selection has stored totals that do not match its lines: an administrator must review it. Nothing was exported.");
        world.Dataverse.Writes.Should().Be(writes, "nothing is created, claimed or sent");
        world.Batches().Should().BeEmpty();
        world.Lifecycle.Sent.Should().BeEmpty();

        // The other candidate exports without it.
        var exported = await world.ExportAsync(good);
        exported.Outcome.Should().Be(ApExportOutcome.Created, exported.Detail);
        world.StatusOfInvoice(good.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.StatusOfInvoice(bad.Id).Should().Be(InvoiceStatusCodes.Approved);
        world.BatchOfInvoice(bad.Id).Should().BeNull();
    }

    [Fact(DisplayName = "165b-2 T2 must fire: the re-check after the claim refuses an invoice whose line changed after the selection (409 totals_mismatch) before any file or Sent to AP write; claims released")]
    public async Task T2_ReCheck_ALineChangedAfterTheSelection_IsRefusedBeforeAnyFile()
    {
        var world = new ApExportWorld();
        var invoice = world.AddInvoice("INV-RECHECK");
        var line = world.Dataverse.RowsOf("sprk_billingevent").First(l => l.GetAttributeValue<EntityReference>("sprk_invoice")?.Id == invoice.Id);
        world.GrantEverything();
        world.Dataverse.BeforeConditionalUpdate = (table, _) =>
        {
            if (table == "sprk_invoice")
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                world.Dataverse.Touch("sprk_billingevent", line.Id, new Dictionary<string, object> { ["sprk_amount"] = new Money(1m) });
            }
        };

        var result = await world.ExportAsync(invoice);

        result.Outcome.Should().Be(ApExportOutcome.Conflict, result.Detail);
        result.Code.Should().Be(ApExportCodes.TotalsMismatch);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the claim is released");
        world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "165b-2 T2 control: invoices whose stored totals match their lines (with a Reduced line and an adjustment) are candidates and export; one batched line read per check")]
    public async Task T2_Control_MatchingTotals_Export()
    {
        var world = new ApExportWorld();
        var plain = world.AddInvoice("INV-PLAIN");
        var adjusted = world.AddInvoice("INV-ADJUSTED", lines: 0);
        world.AddLine(adjusted, Fee, 1000m, sequence: 1, status: 100000004, approvedAmount: 800m); // Reduced
        world.AddLine(adjusted, 100000002, 50m, sequence: 2, state: 0); // an active Fixed adjustment
        world.GrantEverything();

        world.Dataverse.Log.Clear();
        (await CandidateIds(world)).Should().Contain([plain.Id, adjusted.Id]);
        world.Dataverse.Log.Count(l => l == "query sprk_billingevent").Should().Be(1, "one batched line read for the whole candidate list");

        var result = await world.ExportAsync(plain, adjusted);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        world.StatusOfInvoice(plain.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.StatusOfInvoice(adjusted.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.Log.Entries.Should().NotContain(e => e.Message.Contains("totals_mismatch", StringComparison.Ordinal));
    }
}
