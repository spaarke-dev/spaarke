using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// H1 (ebilling-r1 + unified-access-control-r2 agreed 2026-11-13): the generic update paths' invoice-family guard covers every
/// family table — the approval, the external identifier, the correction and the submission as well as the invoice and the line.
/// On the playbook update, the output orchestrator and the field-mapping push a write of such a row is decided on its stored
/// row overlaid with the patch (one core record), a row that does not exist is refused and never created, and a write the
/// rule admits is written as before.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly Guid FamilyRow = Guid.Parse("02200000-0000-4000-8000-0000000000b2");

    private static readonly string[] WidenedTables =
        ["sprk_invoiceapproval", "sprk_externalidentifier", "sprk_invoicecorrection", "sprk_invoicesubmission"];

    /// <summary>
    /// The app-only world of one family row filed under <see cref="Matter"/> (plus any <paramref name="extra"/> stored lookups).
    /// Every other read answers nothing (no row, no query result); the derive's reads are answered by
    /// <see cref="AnswerDeriveReads"/>.
    /// </summary>
    private static Mock<IGenericEntityService> FamilyRowUnderAMatter(string table, params (string Column, EntityReference Value)[] extra)
    {
        var stored = new Entity(table, FamilyRow) { ["sprk_regardingmatter"] = MatterRef(Matter) };
        foreach (var (column, value) in extra)
            stored[column] = value;
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        app.Setup(a => a.RetrieveAsync(table, FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        return app;
    }

    /// <summary>A submission's core lookups are access columns on the two app-only handlers (refused there before the guard).</summary>
    private static bool CoreLookupIsAnAccessColumnOn(GenericPath path, string table) =>
        table == "sprk_invoicesubmission" && path != GenericPath.FieldMappingPush;

    public static TheoryData<GenericPath, string> EveryPathAndWidenedTable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in WidenedTables)
                data.Add(path, table);
        return data;
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPathAndWidenedTable))]
    public async Task AFamilyRowGivenASecondCoreRecord_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow,
            Lookups(("sprk_regardingproject", new EntityReference("sprk_project", Project))), NoPlain, FamilyRowUnderAMatter(table).Object);

        outcome.Code.Should().Be(CoreLookupIsAnAccessColumnOn(path, table)
            ? InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode
            : InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryPathAndWidenedTable))]
    public async Task AFamilyRowThatDoesNotExist_IsRefusedRecordNotFound_AndIsNeverCreated(GenericPath path, string table)
    {
        var app = FamilyRowUnderAMatter(table);
        app.Setup(a => a.RetrieveAsync(table, MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ObjectDoesNotExist());

        var outcome = await RunAsync(path, table, MissingRow, NoLookups, RenameOnly, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        NothingWritten();
    }

    public static TheoryData<GenericPath, string> EveryHandlerAndWidenedTable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in new[] { GenericPath.PlaybookUpdate, GenericPath.OutputOrchestrator })
            foreach (var table in WidenedTables)
                data.Add(path, table);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryHandlerAndWidenedTable))]
    public async Task AFamilyRowThatDoesNotExist_OnAWriteRunAsAPerson_IsRefusedReadFailed_AndIsNeverCreated(GenericPath path, string table)
    {
        var app = FamilyRowUnderAMatter(table);
        app.Setup(a => a.RetrieveAsync(table, MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ObjectDoesNotExist());

        var outcome = await RunAsync(path, table, MissingRow, NoLookups, RenameOnly, app.Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a run as a person never tells found from not-found");
        NothingWritten();
    }

    public static TheoryData<GenericPath, string, bool> EveryPathTableAndIdentityForTheDeleteRace()
    {
        var data = new TheoryData<GenericPath, string, bool>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in WidenedTables)
            {
                data.Add(path, table, false);
                if (path != GenericPath.FieldMappingPush)
                    data.Add(path, table, true);
            }

        return data;
    }

    /// <summary>
    /// The guard finds the row, it is deleted before the write, and the update-only write (<c>If-Match: *</c>) finds none: the
    /// guard's update-only branch (<see cref="InvoiceFamilyCoreRecordGuard.UpdateExistingAsync"/>) answers with its own code, not
    /// the generic branch's not-found, and nothing is created.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPathTableAndIdentityForTheDeleteRace))]
    public async Task AFamilyRowDeletedAfterTheGuard_IsRefusedByTheUpdateOnlyWrite_WithTheGuardsCode(GenericPath path, string table, bool asAPerson)
    {
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(table, FamilyRow, It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var outcome = await RunAsync(path, table, FamilyRow, NoLookups, RenameOnly, FamilyRowUnderAMatter(table).Object,
            playbookActsAs: asAPerson ? Caller : null);

        outcome.Code.Should().Be(asAPerson ? InvoiceFamilyCoreRecordGuard.ReadFailedCode : InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        _writes.Invocations.Should().NotContain(i => i.Method.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync),
            "never the upsert: a deleted row is not re-created");
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPathAndWidenedTable))]
    public async Task AFamilyRowWriteTouchingNoCoreColumn_IsWrittenOnce_UpdateOnly(GenericPath path, string table)
    {
        var app = FamilyRowUnderAMatter(table);

        var outcome = await RunAsync(path, table, FamilyRow, NoLookups, RenameOnly, app.Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
        app.Verify(a => a.RetrieveAsync(table, FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once,
            "the stored row is read once, by the guard");
    }

    /// <summary>Every path and widened table whose core lookup the path lets through (not a submission's on the two handlers,
    /// where it is an access column: that refusal is pinned by <see cref="AFamilyRowGivenASecondCoreRecord_IsRefused_AndNothingIsWritten"/>).</summary>
    public static TheoryData<GenericPath, string> EveryPathAndWidenedTableWhoseCoreLookupIsWritable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in WidenedTables.Where(t => !CoreLookupIsAnAccessColumnOn(path, t)))
                data.Add(path, table);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPathAndWidenedTableWhoseCoreLookupIsWritable))]
    public async Task AFamilyRowGivenTheCoreRecordItAlreadyNames_IsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))), NoPlain,
            FamilyRowUnderAMatter(table).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnExternalIdentifierUnderAMatter_GivenAnInvoice_IsWritten_ItsMatterStillDecides(GenericPath path)
    {
        // The guard has no rule comparing an external identifier's core record with its invoice's: with a core lookup set,
        // that lookup decides (D-68 applies only to a row with none).
        var outcome = await RunAsync(path, "sprk_externalidentifier", FamilyRow,
            Lookups((InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn, new EntityReference("sprk_invoice", Invoice))), NoPlain,
            FamilyRowUnderAMatter("sprk_externalidentifier").Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_externalidentifier", FamilyRow);
    }

    [Fact]
    public void EveryFamilyTable_IsWrittenUpdateOnly_OnTheGenericPaths()
    {
        foreach (var table in InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys)
            InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget(table).Should().BeTrue(table);
        InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget("sprk_event").Should().BeFalse();
    }
}
