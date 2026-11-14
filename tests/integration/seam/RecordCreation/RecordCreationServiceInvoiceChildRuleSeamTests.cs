using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 045g rule 2 (owner, EB-D-231, #2794) on the CREATE: an approval, a submission, a
/// correction and an external identifier filed under a core record regard their invoice's core record. Driven through the REAL
/// service's step 5½ (the registered <see cref="RecordTableRules.InvoiceFamilyRule"/>) and <see cref="RecordCreationProblemMap"/>,
/// with the invoice read as the caller. A PARTIAL of the pipeline seam fixture.
/// </summary>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    private static readonly Guid ChildRuleInvoice = Guid.Parse("a045a000-0000-4000-8000-000000000001");

    /// <summary>Each table rule 2 compares with its invoice, with the binding that names the invoice.</summary>
    public static TheoryData<string, string> InvoiceChildCreates() => new()
    {
        { "sprk_invoiceapproval", "sprk_Invoice_bind" },
        { "sprk_invoicesubmission", "sprk_Invoice_bind" },
        { "sprk_invoicecorrection", "sprk_Invoice_bind" },
        { "sprk_externalidentifier", "sprk_RegardingInvoice_bind" },
    };

    private RecordCreationRequest InvoiceChildRequest(string table, string invoiceBind)
    {
        // A caller who may create the row and file it with an invoice.
        _scripted.Held.Add($"prvCreate{table}");
        _scripted.Held.Add($"prvAppend{table}");
        _scripted.Held.Add("prvAppendTosprk_invoice");
        return WebApiRequest(table, new Dictionary<string, object?>
        {
            ["sprk_name"] = "Filed with an invoice",
            ["sprk_RegardingMatter_bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            [invoiceBind] = $"/sprk_invoices({ChildRuleInvoice:D})",
        });
    }

    /// <summary>The invoice as the caller reads it: under <paramref name="column"/> = <paramref name="core"/>.</summary>
    private void ChildRuleInvoiceRegards(string column, Guid core) =>
        _scripted.RowValues[ChildRuleInvoice] = new Dictionary<string, object?> { [$"_{column}_value"] = core.ToString("D") };

    [Theory]
    [MemberData(nameof(InvoiceChildCreates))]
    public async Task CreateAsync_ARowFiledWithAnInvoiceOfAnotherCoreRecord_Is400LineMismatch_AndWritesNothing(string table, string invoiceBind)
    {
        ChildRuleInvoiceRegards("sprk_project", FamilyProject);

        var outcome = await Service().CreateAsync(InvoiceChildRequest(table, invoiceBind), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, outcome.Refusal.Detail);
        outcome.Refusal.Detail.Should().EndWith(InvoiceFamilyCoreRecordGuard.ReturnAndResubmitHint);
        Problem(outcome, table).Status.Should().Be(StatusCodes.Status400BadRequest);
        AssertNothingWritten();
    }

    [Theory]
    [MemberData(nameof(InvoiceChildCreates))]
    public async Task CreateAsync_ARowFiledWithAnInvoiceOfItsOwnMatter_PassesTheRule(string table, string invoiceBind)
    {
        ChildRuleInvoiceRegards("sprk_matter", OrdinaryMatter);

        var outcome = await Service().CreateAsync(InvoiceChildRequest(table, invoiceBind), AsCaller(), CancellationToken.None);

        // The registered rule ran and admitted the row (this fixture's world has no invoice row for the later ownership step).
        outcome.Steps.Should().ContainSingle(s => s.Step == RecordCreationStep.PreWriteRules)
            .Which.State.Should().Be(RecordCreationStepState.Ran, "the invoice-family rule admitted the row");
        outcome.Refusal?.PreWrite.Should().BeNull();
        (outcome.Refusal?.Code ?? string.Empty).Should().NotStartWith("invoice_family_");
    }

    [Theory]
    [MemberData(nameof(InvoiceChildCreates))]
    public async Task CreateAsync_ARowFiledWithAnInvoiceTheCallerCannotRead_WritesNothing_AndIsNeverAMismatch(string table, string invoiceBind)
    {
        _scripted.UnreadableRows.Add(ChildRuleInvoice);

        var outcome = await Service().CreateAsync(InvoiceChildRequest(table, invoiceBind), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Code.Should().NotBe(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        Problem(outcome, table).Status.Should().Be(StatusCodes.Status404NotFound, "a row the caller cannot read is the uniform not-found");
        AssertNothingWritten();
    }

    // ── Rule 3b at create (owner, 2026-11-13): an approval or a correction is created with its invoice ─────────────────

    /// <summary>A create of <paramref name="table"/> under the ordinary matter, its invoice <paramref name="invoice"/>: absent
    /// (the binding is not sent), null, or a binding to an empty id.</summary>
    private RecordCreationRequest NoInvoiceRequest(string table, string invoiceBind, string invoice)
    {
        _scripted.Held.Add($"prvCreate{table}");
        _scripted.Held.Add($"prvAppend{table}");
        _scripted.Held.Add("prvAppendTosprk_invoice");
        var body = new Dictionary<string, object?>
        {
            ["sprk_name"] = "Filed under a matter",
            ["sprk_RegardingMatter_bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        };
        if (invoice == "null")
            body[invoiceBind] = null;
        else if (invoice == "empty id")
            body[invoiceBind] = $"/sprk_invoices({Guid.Empty:D})";
        return WebApiRequest(table, body);
    }

    public static TheoryData<string, string> ApprovalOrCorrectionWithNoInvoice()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in new[] { "sprk_invoiceapproval", "sprk_invoicecorrection" })
            foreach (var invoice in new[] { "absent", "null" })
                data.Add(table, invoice);
        return data;
    }

    [Theory]
    [MemberData(nameof(ApprovalOrCorrectionWithNoInvoice))]
    public async Task CreateAsync_AnApprovalOrCorrectionNamingNoInvoice_Is400InvoiceRequired_AndWritesNothing(string table, string invoice)
    {
        var outcome = await Service().CreateAsync(NoInvoiceRequest(table, "sprk_Invoice_bind", invoice), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceRequiredCode, outcome.Refusal.Detail);
        Problem(outcome, table).Status.Should().Be(StatusCodes.Status400BadRequest);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    public async Task CreateAsync_AnApprovalOrCorrectionBindingAnEmptyInvoiceId_Is400_AndWritesNothing(string table)
    {
        // The payload step refuses a binding to an empty id before any rule runs; the guard's own answer to an empty id (the
        // invoice-required code) is pinned in InvoiceFamilyCoreRecordGuardTests.
        var outcome = await Service().CreateAsync(NoInvoiceRequest(table, "sprk_Invoice_bind", "empty id"), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.InvalidPayloadCode, outcome.Refusal.Detail);
        Problem(outcome, table).Status.Should().Be(StatusCodes.Status400BadRequest);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("sprk_invoicesubmission", "sprk_Invoice_bind")]
    [InlineData("sprk_externalidentifier", "sprk_RegardingInvoice_bind")]
    public async Task CreateAsync_ASubmissionOrExternalIdentifierNamingNoInvoice_PassesTheRule(string table, string invoiceBind)
    {
        var outcome = await Service().CreateAsync(NoInvoiceRequest(table, invoiceBind, "absent"), AsCaller(), CancellationToken.None);

        outcome.Steps.Should().ContainSingle(s => s.Step == RecordCreationStep.PreWriteRules)
            .Which.State.Should().Be(RecordCreationStepState.Ran, "the invoice-family rule admitted the row");
        outcome.Refusal?.PreWrite.Should().BeNull();
        (outcome.Refusal?.Code ?? string.Empty).Should().NotStartWith("invoice_family_");
    }
}
