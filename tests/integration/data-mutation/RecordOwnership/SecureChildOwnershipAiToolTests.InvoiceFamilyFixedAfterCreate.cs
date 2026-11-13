using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045g (owner, EB-D-231, #2794) on the chat update tool, through the REAL handler and
/// core over the scripted caller's Dataverse. Rule 2: an approval, a submission, a correction and an external identifier filed
/// under a core record regard their invoice's core record. Rule 3: a line's invoice is never cleared. A read the rules need that
/// fails writes nothing. Rule 1 on this tool is in the invoice-family guard and resolver-field files.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The tables rule 2 compares with their invoice, each with the column naming that invoice.</summary>
    public static TheoryData<string, string> InvoiceChildTables() => new()
    {
        { "sprk_invoiceapproval", "sprk_invoice" },
        { "sprk_invoicesubmission", "sprk_invoice" },
        { "sprk_invoicecorrection", "sprk_invoice" },
        { "sprk_externalidentifier", InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn },
    };

    /// <summary>
    /// A stored <paramref name="table"/> row (<see cref="PairFamilyRow"/>) under the ordinary matter, filed with
    /// <paramref name="storedInvoice"/> when set; <see cref="GuardInvoice"/> under the ordinary matter and
    /// <see cref="SupersededInvoice"/> under the secure matter; the ordinary project, which the row may be moved to.
    /// </summary>
    private void InvoiceChildUnderTheMatter(string table, string invoiceColumn, Guid? storedInvoice = null)
    {
        StoredFamilyRowUnderTheMatter(table, "sprk_regardingmatter", PairFamilyRow);
        _user.RecordTypes.Remove(PairProjectType); // the catalog's own project row, as the derive reads it
        if (storedInvoice is { } invoice)
            _user.RowValues[PairFamilyRow][$"_{invoiceColumn}_value"] = invoice.ToString("D");
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
    }

    // ── Rule 2, must fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(InvoiceChildTables))]
    public async Task UpdateRecordTool_ARowGivenAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_AndNothingIsPatched(string table, string invoiceColumn)
    {
        InvoiceChildUnderTheMatter(table, invoiceColumn);

        var result = await UpdateRecord(table, PairFamilyRow, Lookup(invoiceColumn, "sprk_invoice", SupersededInvoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvoiceChildTables))]
    public async Task UpdateRecordTool_ARowMovedAwayFromItsInvoicesMatter_IsRefusedLineMismatch_ReadingItsStoredInvoiceFromTheInvoiceSet(
        string table, string invoiceColumn)
    {
        // The invoice is kept from the stored row (no bind names it), so it is read from the invoice set, not the row's own.
        InvoiceChildUnderTheMatter(table, invoiceColumn, storedInvoice: GuardInvoice);
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await UpdateRecord(table, PairFamilyRow,
            ("sprk_regardingmatter", JsonSerializer.SerializeToElement<object?>(null)),
            Lookup("sprk_regardingproject", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, result.ErrorMessage);
        reads.Should().Contain(p => p.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=", StringComparison.Ordinal));
        _user.Patches.Should().BeEmpty();
    }

    // ── Rule 2, must not fire ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(InvoiceChildTables))]
    public async Task UpdateRecordTool_ARowGivenAnInvoiceOfItsOwnMatter_IsPatched(string table, string invoiceColumn)
    {
        InvoiceChildUnderTheMatter(table, invoiceColumn);

        var result = await UpdateRecord(table, PairFamilyRow, Lookup(invoiceColumn, "sprk_invoice", GuardInvoice));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
    }

    // ── Fail closed: the invoice read faults ─────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(InvoiceChildTables))]
    public async Task UpdateRecordTool_WhenTheInvoiceCannotBeRead_IsRefused_NeverAMismatch_AndNothingIsPatched(string table, string invoiceColumn)
    {
        InvoiceChildUnderTheMatter(table, invoiceColumn);
        _user.GetOverride = path => path.StartsWith($"sprk_invoices({SupersededInvoice:D})?$select=", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "Service unavailable.")
            : null;

        var result = await UpdateRecord(table, PairFamilyRow, Lookup(invoiceColumn, "sprk_invoice", SupersededInvoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().NotBe(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty("a read fault is never \"the invoice regards the same record\"");
    }

    // ── Rule 3b: an approval's and a correction's invoice is never cleared; a submission's and an external identifier's may be

    [Theory]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    public async Task UpdateRecordTool_AnApprovalOrCorrectionsInvoiceCleared_IsRefusedInvoiceCleared_AndNothingIsPatched(string table)
    {
        InvoiceChildUnderTheMatter(table, "sprk_invoice", storedInvoice: GuardInvoice);

        var result = await UpdateRecord(table, PairFamilyRow, ("sprk_invoice", JsonSerializer.SerializeToElement<object?>(null)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_invoicesubmission", "sprk_invoice")]
    [InlineData("sprk_externalidentifier", InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn)]
    public async Task UpdateRecordTool_ASubmissionsOrExternalIdentifiersInvoiceCleared_IsPatched(string table, string invoiceColumn)
    {
        InvoiceChildUnderTheMatter(table, invoiceColumn, storedInvoice: GuardInvoice);

        var result = await UpdateRecord(table, PairFamilyRow, (invoiceColumn, JsonSerializer.SerializeToElement<object?>(null)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
    }

    // ── Rule 3 ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecordTool_ALinesInvoiceCleared_IsRefusedLineInvoiceCleared_AndNothingIsPatched()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredLine(GuardLine, OrdinaryMatter, GuardInvoice);

        var result = await UpdateRecord("sprk_billingevent", GuardLine, ("sprk_invoice", JsonSerializer.SerializeToElement<object?>(null)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }
}
