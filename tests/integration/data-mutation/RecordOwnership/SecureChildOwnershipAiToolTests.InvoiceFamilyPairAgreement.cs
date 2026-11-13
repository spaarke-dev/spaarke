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
        var body = BodyOf(_user.Patches.Should().ContainSingle().Subject.Body);
        body["sprk_regardingrecordid"].GetString().Should().Be(OrdinaryMatter.ToString("D").ToLowerInvariant(),
            "the pair id is written in its canonical form, whatever spelling was sent");
    }

    [Fact(DisplayName = "045e (canonical id): PATCH of an invoice whose agreeing pair id is braced and upper-case writes it canonical")]
    public async Task ChildRefile_AnAgreeingInvoicePairIdInAnotherSpelling_IsWrittenCanonical()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);
        var sent = InvoicePair(OrdinaryMatter, RecordTypeMatter);
        sent["sprk_regardingrecordid"] = OrdinaryMatter.ToString("B").ToUpperInvariant();

        var result = await RefileChild("sprk_invoice", PairFamilyRow, sent);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, "sprk_regardingrecordid").GetString().Should().Be(OrdinaryMatter.ToString("D").ToLowerInvariant());
    }

    // ── A write that moves the core lookup: the derived pair is written; naming the same record is no move ──────────

    [Fact(DisplayName = "045e (core move): PATCH moving an invoice to a project with a pair naming another record writes the derived pair")]
    public async Task ChildRefile_AnInvoiceMovedToAProject_WithADisagreeingPair_IsWrittenWithTheDerivedPair()
    {
        InvoiceOnTheMatter_MovingToTheProject();
        var sent = MoveGuardInvoiceToTheProject();
        foreach (var (key, value) in InvoicePair(PairOtherMatter, RecordTypeMatter))
            sent[key] = value;

        var result = await RefileChild("sprk_invoice", GuardInvoice, sent);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShouldCarryTheFiveOf(BodyOf(_user.Patches.Should().ContainSingle().Subject.Body), "sprk_project", OrdinaryProject,
            "sprk_regardingrecordtype");
    }

    [Fact(DisplayName = "045e (core move): PATCH binding an invoice's matter to the matter it names, with a pair naming another, is refused")]
    public async Task ChildRefile_AnInvoiceMatterBoundToTheSameMatter_WithADisagreeingPair_IsRefusedMismatch()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);
        var sent = InvoicePair(PairOtherMatter, RecordTypeMatter);
        sent["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})";

        var result = await RefileChild("sprk_invoice", PairFamilyRow, sent);

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "045e (core move): the chat tool moving an approval to another matter with a pair naming the old one writes the derived pair")]
    public async Task UpdateRecordTool_AnApprovalMovedToAnotherMatter_WithADisagreeingPair_IsWrittenWithTheDerivedPair()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);
        _world.WithRecord("sprk_matter", RederiveMatter, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await UpdateRecord("sprk_invoiceapproval", PairFamilyRow,
            Lookup("sprk_regardingmatter", "sprk_matter", RederiveMatter), PairIdItem(OrdinaryMatter), PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var body = BodyOf(_user.Patches.Should().ContainSingle().Subject.Body);
        body["sprk_regardingrecordid"].GetString().Should().Be(RederiveMatter.ToString("D").ToLowerInvariant());
        body["sprk_RegardingRecordType@odata.bind"].GetString().Should().Be($"/sprk_recordtype_refs({RecordTypeMatter:D})");
    }

    [Fact(DisplayName = "045e (core move): the chat tool setting an approval's matter to the one it names, with a pair naming another, is refused")]
    public async Task UpdateRecordTool_AnApprovalMatterSetToTheSameMatter_WithADisagreeingPair_IsRefusedMismatch()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);

        var result = await UpdateRecord("sprk_invoiceapproval", PairFamilyRow,
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter), PairIdItem(PairOtherMatter), PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "045e (core move, concurrent): the chat tool refuses a sent pair the derive left in place when the check sees a move")]
    public async Task UpdateRecordTool_AnApprovalMove_WhoseDeriveFoundNothingToMove_WithADisagreeingPair_IsRefusedMismatch()
    {
        // The caller path reads the row's core lookups twice: 045d's derive, then the guard's decision. The derive reads the row
        // already on the new matter (a concurrent write), so it derives nothing and the sent pair stays in the write; the decision
        // reads it on the old matter, so the write is a move. The pair as it will be written is checked all the same
        // (aCoreMoveIsChecked) — it names the old matter, not the one the row will name — so the write is refused, nothing patched.
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);
        _world.WithRecord("sprk_matter", RederiveMatter, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var coreReads = 0;
        _user.GetOverride = path =>
        {
            if (path.StartsWith($"sprk_invoiceapprovals({PairFamilyRow:D})?$select=", StringComparison.Ordinal)
                && path.Contains("_sprk_regardingmatter_value", StringComparison.Ordinal))
            {
                var seen = ++coreReads == 1 ? RederiveMatter : OrdinaryMatter;
                _user.RowValues[PairFamilyRow] = new Dictionary<string, object?> { ["_sprk_regardingmatter_value"] = seen.ToString("D") };
            }

            return null;
        };

        var result = await UpdateRecord("sprk_invoiceapproval", PairFamilyRow,
            Lookup("sprk_regardingmatter", "sprk_matter", RederiveMatter), PairIdItem(OrdinaryMatter), PairTypeItem(RecordTypeMatter));

        coreReads.Should().Be(2, "the derive's read, then the decision's");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    // ── D-96 is decided before the pair: an invoice moved while a line stays behind ──────────────────────────────────

    /// <summary>
    /// The guard's invoice moved from the ordinary matter to the project while a line stays on the matter (D-96). With
    /// <paramref name="deriveSeesTheMoveDone"/>, the derive's read of the core lookups (the one that asks no superseded invoice)
    /// answers the project — it then derives nothing, so the pair the request sends reaches the guard's decision, whose read
    /// still answers the matter (a move).
    /// </summary>
    private void InvoiceMovingToTheProject_WithALineLeftBehind(bool deriveSeesTheMoveDone)
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);
        AppLine(GuardInvoice, "sprk_matter", "sprk_matter", OrdinaryMatter);
        CatalogKnowsTheMatterType();
        _user.RecordTypes[ScriptedUserClient.CatalogTypeIdOf("sprk_project")] = "sprk_project";
        if (!deriveSeesTheMoveDone)
            return;
        _user.GetOverride = path =>
        {
            if (path.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=_sprk_matter_value", StringComparison.Ordinal))
            {
                _user.RowValues[GuardInvoice] = path.Contains("_sprk_supersedesinvoice_value", StringComparison.Ordinal)
                    ? new Dictionary<string, object?> { ["_sprk_matter_value"] = OrdinaryMatter.ToString("D") }
                    : new Dictionary<string, object?> { ["_sprk_project_value"] = OrdinaryProject.ToString("D") };
            }

            return null;
        };
    }

    [Theory(DisplayName = "045e (D-96 first): PATCH moving an invoice while a line stays behind, with a pair sent, is the line refusal")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildRefile_AnInvoiceMovedWhileALineStaysBehind_WithAPairSent_IsRefusedLineMismatch(bool agrees)
    {
        InvoiceMovingToTheProject_WithALineLeftBehind(deriveSeesTheMoveDone: !agrees);
        var sent = MoveGuardInvoiceToTheProject();
        foreach (var (key, value) in agrees
                     ? InvoicePair(OrdinaryProject, ScriptedUserClient.CatalogTypeIdOf("sprk_project"))
                     : InvoicePair(PairOtherMatter, RecordTypeMatter))
        {
            sent[key] = value;
        }

        var result = await RefileChild("sprk_invoice", GuardInvoice, sent);

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Theory(DisplayName = "045e (D-96 first): the chat tool moving an invoice while a line stays behind, with a pair sent, is the line refusal")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateRecordTool_AnInvoiceMovedWhileALineStaysBehind_WithAPairSent_IsRefusedLineMismatch(bool agrees)
    {
        InvoiceMovingToTheProject_WithALineLeftBehind(deriveSeesTheMoveDone: !agrees);

        var result = await UpdateRecord("sprk_invoice", GuardInvoice,
            ("sprk_matter", JsonSerializer.SerializeToElement<object?>(null)), Lookup("sprk_project", "sprk_project", OrdinaryProject),
            agrees ? PairIdItem(OrdinaryProject) : PairIdItem(PairOtherMatter),
            agrees ? PairTypeItem(ScriptedUserClient.CatalogTypeIdOf("sprk_project")) : PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    // ── A record URL sent with an agreeing pair names the same record (fix round 3) ─────────────────────────────────

    [Fact(DisplayName = "045e (URL): PATCH of an invoice with an agreeing pair and a URL naming another record is refused")]
    public async Task ChildRefile_AnAgreeingInvoicePairWithAUrlNamingAnotherRecord_IsRefusedMismatch()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);
        var sent = InvoicePair(OrdinaryMatter, RecordTypeMatter);
        sent["sprk_regardingrecordurl"] = $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={PairOtherMatter:D}";

        var result = await RefileChild("sprk_invoice", PairFamilyRow, sent);

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Theory(DisplayName = "045e (URL): the chat tool refuses a family row's URL alone naming another record")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowUrlAloneNamingAnotherRecord_IsRefusedMismatch(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);

        var result = await UpdateRecord(table, PairFamilyRow, ("sprk_regardingrecordurl",
            JsonSerializer.SerializeToElement($"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={PairOtherMatter:D}")));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    private static string CanonicalUrlOfTheMatter =>
        $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter.ToString("D").ToLowerInvariant()}";

    [Theory(DisplayName = "045e (URL): the chat tool writes a family row's absolute URL of its own matter on another host in the canonical form")]
    [MemberData(nameof(PairFamilyTables))]
    public async Task UpdateRecordTool_AFamilyRowAbsoluteUrlOfItsMatter_IsWrittenCanonical(string table, string column)
    {
        StoredFamilyRowUnderTheMatter(table, column, PairFamilyRow);
        var url = $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter.ToString("B").ToUpperInvariant()}";

        var result = await UpdateRecord(table, PairFamilyRow, ("sprk_regardingrecordurl", JsonSerializer.SerializeToElement(url)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body)["sprk_regardingrecordurl"].GetString().Should().Be(CanonicalUrlOfTheMatter);
    }

    [Theory(DisplayName = "045e (URL): the chat tool writes a URL of the row's matter in the canonical form, whatever its scheme")]
    [InlineData("ftp://other.example/main.aspx?pagetype=entityrecord&etn=SPRK_MATTER&id={0}")]
    [InlineData("/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={0}")]
    public async Task UpdateRecordTool_AnApprovalUrlOfItsMatter_IsWrittenCanonical(string form)
    {
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);

        var result = await UpdateRecord("sprk_invoiceapproval", PairFamilyRow, ("sprk_regardingrecordurl",
            JsonSerializer.SerializeToElement(string.Format(System.Globalization.CultureInfo.InvariantCulture, form, OrdinaryMatter.ToString("D").ToLowerInvariant()))));

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body)["sprk_regardingrecordurl"].GetString().Should().Be(CanonicalUrlOfTheMatter);
    }

    [Fact(DisplayName = "045e (URL): PATCH of an invoice with an agreeing pair and an absolute URL of its matter writes the canonical URL")]
    public async Task ChildRefile_AnAgreeingInvoicePairWithAnAbsoluteUrlOfItsMatter_IsWrittenCanonical()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoice", "sprk_matter", PairFamilyRow);
        var sent = InvoicePair(OrdinaryMatter, RecordTypeMatter);
        sent["sprk_regardingrecordurl"] = $"https://other.example/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter:B}";

        var result = await RefileChild("sprk_invoice", PairFamilyRow, sent);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body)["sprk_regardingrecordurl"].GetString().Should().Be(CanonicalUrlOfTheMatter);
    }

    [Fact(DisplayName = "045e (core move): the chat tool moving an approval with a URL naming another record writes the derived URL")]
    public async Task UpdateRecordTool_AnApprovalMovedToAnotherMatter_WithAForeignUrl_IsWrittenWithTheDerivedUrl()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);
        _world.WithRecord("sprk_matter", RederiveMatter, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await UpdateRecord("sprk_invoiceapproval", PairFamilyRow,
            Lookup("sprk_regardingmatter", "sprk_matter", RederiveMatter),
            ("sprk_regardingrecordurl", JsonSerializer.SerializeToElement($"https://other.example/main.aspx?pagetype=entityrecord&etn=sprk_project&id={PairOtherMatter:D}")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body)["sprk_regardingrecordurl"].GetString()
            .Should().Be($"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={RederiveMatter.ToString("D").ToLowerInvariant()}");
    }

    // ── D-68: an external identifier filed under its invoice alone (no core lookup) ─────────────────────────────────

    private void StoredInvoiceOnlyExternalIdentifier()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _user.RowValues[PairFamilyRow] = new Dictionary<string, object?> { ["_sprk_regardinginvoice_value"] = GuardInvoice.ToString("D") };
        _world.WithRecord("sprk_externalidentifier", PairFamilyRow, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", GuardInvoice) });
        CatalogKnowsTheMatterType();
        _user.RecordTypes[ScriptedUserClient.CatalogTypeIdOf("sprk_invoice")] = "sprk_invoice";
    }

    [Fact(DisplayName = "045e D-68 (must not fire): the chat tool writes an invoice-only identifier's pair naming its invoice, canonical")]
    public async Task UpdateRecordTool_AnInvoiceOnlyIdentifierPairNamingItsInvoice_IsWrittenCanonical()
    {
        StoredInvoiceOnlyExternalIdentifier();

        var result = await UpdateRecord("sprk_externalidentifier", PairFamilyRow,
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(GuardInvoice.ToString("B").ToUpperInvariant())),
            PairTypeItem(ScriptedUserClient.CatalogTypeIdOf("sprk_invoice")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body)["sprk_regardingrecordid"].GetString().Should()
            .Be(GuardInvoice.ToString("D").ToLowerInvariant());
    }

    [Fact(DisplayName = "045e D-68 (must fire): the chat tool refuses an invoice-only identifier's pair naming a matter")]
    public async Task UpdateRecordTool_AnInvoiceOnlyIdentifierPairNamingAMatter_IsRefusedMismatch()
    {
        StoredInvoiceOnlyExternalIdentifier();

        var result = await UpdateRecord("sprk_externalidentifier", PairFamilyRow, PairIdItem(OrdinaryMatter), PairTypeItem(RecordTypeMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
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
