using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045e (owner decision 2026-11-13): on the six invoice-family tables the regarding
/// pair (<c>sprk_regardingrecordid</c> and its record type) names the row's own core record — the core lookup the same request
/// writes, else the stored one. On the CALLER update paths (<c>PATCH /api/v1/child-records/sprk_invoice/{id}</c> and the chat
/// tool <c>dataverse.update_record</c>), a pair that names another record is refused through the path's own refusal form and
/// nothing is written; a pair that agrees is written; a write that moves the core lookup keeps 045d's derive. The type is
/// compared through the record-type catalog, read as the app. Driven through the REAL handlers over the scripted Dataverse.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid PairOtherMatter = Guid.Parse("a2470000-0000-4000-8000-0000000045e1");
    private static readonly Guid PairFamilyRow = Guid.Parse("a2470000-0000-4000-8000-0000000045e2");
    private static readonly Guid PairProjectType = Guid.Parse("a2470000-0000-4000-8000-0000000045e3");

    /// <summary>The six invoice-family tables and the core lookup each names a matter by.</summary>
    public static TheoryData<string, string> PairFamilyTables() => new()
    {
        { "sprk_invoice", "sprk_matter" },
        { "sprk_billingevent", "sprk_matter" },
        { "sprk_invoicesubmission", "sprk_regardingmatter" },
        { "sprk_invoiceapproval", "sprk_regardingmatter" },
        { "sprk_externalidentifier", "sprk_regardingmatter" },
        { "sprk_invoicecorrection", "sprk_regardingmatter" },
    };

    /// <summary>A stored family row filed under the ordinary matter, as the caller and the world read it.</summary>
    private void StoredFamilyRowUnderTheMatter(string table, string column, Guid id)
    {
        _user.RowValues[id] = new Dictionary<string, object?> { [$"_{column}_value"] = OrdinaryMatter.ToString("D") };
        _world.WithRecord(table, id, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { [column] = new EntityReference("sprk_matter", OrdinaryMatter) });
        CatalogKnowsTheMatterType();
        _user.RecordTypes[PairProjectType] = "sprk_project";
    }

    private static (string Column, JsonElement Value) PairIdItem(Guid id) =>
        ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(id.ToString("D")));

    private static (string Column, JsonElement Value) PairTypeItem(Guid typeId) =>
        Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", typeId);

    private static Dictionary<string, object?> InvoicePair(Guid id, Guid typeId) => new()
    {
        ["sprk_regardingrecordid"] = id.ToString("D"),
        ["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({typeId:D})",
    };

    // ── Must fire: the PATCH ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "045e (must fire): PATCH of an invoice whose pair names another matter is refused, nothing written")]
    public async Task ChildRefile_AnInvoicePairNamingAnotherMatter_IsRefusedMismatch_AndNothingIsWritten()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);

        var result = await RefileChild("sprk_invoice", PairFamilyRow, InvoicePair(PairOtherMatter, RecordTypeMatter));

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        Detail(result).Should().NotContain(PairOtherMatter.ToString("D")).And.NotContain(OrdinaryMatter.ToString("D"),
            "the refusal names no record");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact(DisplayName = "045e (must fire): PATCH of an invoice whose pair names its own matter's id under the project type is refused")]
    public async Task ChildRefile_AnInvoicePairOfItsMattersIdUnderAnotherType_IsRefusedMismatch()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);

        var result = await RefileChild("sprk_invoice", PairFamilyRow, InvoicePair(OrdinaryMatter, PairProjectType));

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        _appCatalogQueries.Should().Equal(new[] { "sprk_matter" }, "the type is compared through the catalog, read as the app");
        _user.Patches.Should().BeEmpty();
    }

    // ── Must fire: the chat tool, all six tables ────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "045e (must fire): the chat update tool refuses a family row's pair naming another matter")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowPairNamingAnotherMatter_IsRefusedMismatch(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow, PairIdItem(PairOtherMatter), PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        result.ErrorMessage.Should().NotContain(PairOtherMatter.ToString("D"));
        _user.Patches.Should().BeEmpty();
    }

    [Theory(DisplayName = "045e (must fire): the chat update tool refuses a family row's own matter id under the project type")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowPairOfItsMattersIdUnderAnotherType_IsRefusedMismatch(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow, PairIdItem(OrdinaryMatter), PairTypeItem(PairProjectType));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    [Theory(DisplayName = "045e (must fire): the chat update tool refuses an id-only pair naming another record")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowIdOnlyNamingAnotherMatter_IsRefusedMismatch(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow, PairIdItem(PairOtherMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    // ── Must not fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "045e (must not fire): PATCH of an invoice whose pair names its own matter is written")]
    public async Task ChildRefile_AnInvoicePairNamingItsOwnMatter_IsWritten()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);

        var result = await RefileChild("sprk_invoice", PairFamilyRow, InvoicePair(OrdinaryMatter, RecordTypeMatter));

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, "sprk_regardingrecordid").GetString().Should().Be(OrdinaryMatter.ToString("D"));
    }

    [Theory(DisplayName = "045e (must not fire): the chat update tool writes a family row's pair naming its own matter")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowPairNamingItsOwnMatter_IsWritten(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow,
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(OrdinaryMatter.ToString("B").ToUpperInvariant())),
            PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
    }

    [Theory(DisplayName = "045e (must not fire): a rename of a family row reads no catalog and is written")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowRename_IsWritten_WithNoCatalogRead(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow, ("sprk_name", JsonSerializer.SerializeToElement("Renamed")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
        _appCatalogQueries.Should().BeEmpty("no pair is sent, so nothing is compared");
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "045e (fail closed): PATCH whose catalog read faults is read_failed, naming no fault text, nothing written")]
    public async Task ChildRefile_AnInvoicePairWhoseCatalogReadFaults_IsReadFailed_AndNothingIsWritten()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);
        _appCatalogFault = new InvalidOperationException("catalog read timed out at 10.0.0.12");

        var result = await RefileChild("sprk_invoice", PairFamilyRow, InvoicePair(OrdinaryMatter, RecordTypeMatter));

        Status(result).Should().Be(StatusCodes.Status500InternalServerError, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        Detail(result).Should().NotContain("10.0.0.12");
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "045e (fail closed): the chat update tool whose catalog read faults is read_failed, nothing written")]
    public async Task UpdateRecordTool_AFamilyRowPairWhoseCatalogReadFaults_IsReadFailed_AndNothingIsWritten()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoicesubmission", "sprk_regardingmatter", PairFamilyRow);
        _appCatalogFault = new InvalidOperationException("catalog read timed out at 10.0.0.12");

        var result = await UpdateRecord("sprk_invoicesubmission", PairFamilyRow, PairIdItem(OrdinaryMatter), PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, result.ErrorMessage);
        result.ErrorMessage.Should().NotContain("10.0.0.12");
        _user.Patches.Should().BeEmpty();
    }
}
