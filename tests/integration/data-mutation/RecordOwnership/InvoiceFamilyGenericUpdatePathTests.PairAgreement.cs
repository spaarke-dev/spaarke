using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045e (owner, 2026-11-13, #2772 "Refuse it"; FR-23 amended for the invoice family): on
/// the playbook update and the output orchestrator, a write of an invoice-family row whose regarding pair names a record other
/// than the row's core record (the core lookup written in the same request, else the stored one) is refused by the path's
/// invoice-family guard, through the path's existing form (the guard's code-only refusal), and nothing is written. The
/// field-mapping push asks the same guard, but never maps a pair (the mapping engine skips a rule aimed at it), so its pair moves
/// only with the core lookup. A pair that names the core record, a write with no pair, and a write that moves the core lookup
/// (045d derives the pair) are written as before. Driven through the real paths (<see cref="RunAsync"/>).
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    /// <summary>The core lookup of <paramref name="table"/> that names a matter.</summary>
    private static string MatterColumnOf(string table) =>
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Single(l => l.CoreEntity == "sprk_matter").Column;

    /// <summary>The app-only world of one family row of <paramref name="table"/> stored under <see cref="Matter"/>.</summary>
    private static Mock<IGenericEntityService> StoredUnderTheMatter(string table, params (string Column, EntityReference Value)[] extra)
    {
        var stored = new Entity(table, FamilyRow) { [MatterColumnOf(table)] = MatterRef(Matter) };
        foreach (var (column, value) in extra)
            stored[column] = value;
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        app.Setup(a => a.RetrieveAsync(table, FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        return app;
    }

    private static IReadOnlyDictionary<string, object?> PairId(Guid id) =>
        new Dictionary<string, object?> { ["sprk_regardingrecordid"] = id.ToString("D") };

    private static (string Column, EntityReference? Record) PairType(Guid catalogRow) =>
        ("sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", catalogRow));

    /// <summary>The paths that write a pair they are sent (the push never maps one: the mapping engine skips a rule aimed at the
    /// pair, FR-05(e), so its pair moves only with the core lookup — <see cref="APushRuleAimedAtThePair_IsSkipped_AndNothingIsWritten"/>).</summary>
    public static TheoryData<GenericPath, string> EveryPairWritingPathAndFamilyTable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in new[] { GenericPath.PlaybookUpdate, GenericPath.OutputOrchestrator })
            foreach (var table in InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys)
                data.Add(path, table);
        return data;
    }

    public static TheoryData<string> InvoiceFamilyTables() => new(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys);

    public static TheoryData<GenericPath, string> EveryPathAndFamilyTable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys)
                data.Add(path, table);
        return data;
    }

    private void RefusedAsDisagreeing(PathOutcome outcome)
    {
        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode, outcome.Message);
        outcome.Message.Should().Contain(InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode));
        NoGuid(outcome);
        NothingWritten();
    }

    // ── Must fire ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPairWritingPathAndFamilyTable))]
    public async Task APairNamingAnotherRecordThanTheStoredCoreLookup_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow, NoLookups, PairId(OtherMatter), StoredUnderTheMatter(table).Object);

        RefusedAsDisagreeing(outcome);
        _deriveReads.Should().BeEmpty("the id decides it: the catalog is not read");
    }

    [Theory]
    [MemberData(nameof(EveryPairWritingPathAndFamilyTable))]
    public async Task TheCoreRecordsIdUnderAnotherRecordType_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups(PairType(CatalogRowOf("sprk_project"))), PairId(Matter),
            StoredUnderTheMatter(table).Object);

        RefusedAsDisagreeing(outcome);
        _deriveReads.Should().Equal(["catalog:sprk_matter"], "the type is checked against the core table's catalog row, app-only");
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task APairNamingAnotherRecord_WithTheCoreLookupReNamedInTheSameWrite_IsRefused(GenericPath path)
    {
        // The core lookup is written but names the record the row already names: nothing moves, 045d derives nothing, so the pair
        // sent would be written and is checked.
        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))),
            PairId(Project), StoredUnderTheMatter("sprk_invoiceapproval").Object);

        RefusedAsDisagreeing(outcome);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnExternalIdentifierFiledUnderAnInvoiceOnly_GivenAnotherInvoicesId_IsRefused_ItsKeyDoesNotMove(GenericPath path)
    {
        // D-68: the pair is a column of the external identifier's alternate key; it moves only with the row's own lookup.
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        app.Setup(a => a.RetrieveAsync("sprk_externalidentifier", FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_externalidentifier", FamilyRow)
            {
                [InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn] = new EntityReference("sprk_invoice", Invoice),
            });

        var outcome = await RunAsync(path, "sprk_externalidentifier", FamilyRow, NoLookups, PairId(OtherInvoice), app.Object);

        RefusedAsDisagreeing(outcome);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnExternalIdentifierUnderAMatter_GivenItsInvoicesId_IsRefused_ItsMatterDecides(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_externalidentifier", FamilyRow, NoLookups, PairId(Invoice),
            StoredUnderTheMatter("sprk_externalidentifier",
                (InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn, new EntityReference("sprk_invoice", Invoice))).Object);

        RefusedAsDisagreeing(outcome);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task APairThePathsPlannerClears_IsDecidedAsWritten_AndRefused_NothingIsWritten(GenericPath path)
    {
        // The check decides the payload as it will be written, after the path's pair planner. A type-only write of the matter type
        // to an approval: the planner (045b rule 2) finds no agreeing lookup of that table on this row and clears the pair — a
        // payload that would leave the row without the pair of its core record, which the request itself never sent.
        var app = StoredUnderTheMatter("sprk_invoiceapproval");
        app.Setup(a => a.RetrieveAsync("sprk_recordtype_ref", CatalogRowOf("sprk_matter"), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", CatalogRowOf("sprk_matter")) { ["sprk_recordlogicalname"] = "sprk_matter" });

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))), NoPlain,
            app.Object);

        RefusedAsDisagreeing(outcome);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, true)]
    [InlineData(GenericPath.PlaybookUpdate, false)]
    [InlineData(GenericPath.OutputOrchestrator, true)]
    [InlineData(GenericPath.OutputOrchestrator, false)]
    public async Task AnInvoiceMovedWithAPairSent_IsRefusedCoreRecordFixed_NotForThePair(GenericPath path, bool agrees)
    {
        // Task 045g rule 1 is decided first: the pair a core-moving write sends — agreeing with the record the row would name,
        // or not — never answers in its place.
        var outcome = await RunAsync(path, "sprk_invoice", Invoice,
            Lookups(("sprk_matter", MatterRef(OtherMatter)), PairType(CatalogRowOf("sprk_matter"))),
            PairId(agrees ? OtherMatter : Project), WorldWithLines(OtherMatter, Matter).Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode, outcome.Message);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnAgreeingPairWithARecordUrlNamingAnotherRecord_IsRefused_AndNothingIsWritten(GenericPath path)
    {
        var sent = new Dictionary<string, object?>
        {
            ["sprk_regardingrecordid"] = Matter.ToString("D"),
            ["sprk_regardingrecordurl"] = $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={OtherMatter:D}",
        };

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))), sent,
            StoredUnderTheMatter("sprk_invoiceapproval").Object);

        RefusedAsDisagreeing(outcome);
    }

    /// <summary>A record URL of the matter core: the server's form, a client's absolute form on another host, another scheme.</summary>
    public enum MatterUrlForm { Canonical, AbsoluteOnAnotherHost, AnotherScheme }

    private static string MatterUrl(MatterUrlForm form) => form switch
    {
        MatterUrlForm.Canonical => $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={Matter.ToString("D").ToLowerInvariant()}",
        MatterUrlForm.AbsoluteOnAnotherHost =>
            $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={Matter.ToString("B").ToUpperInvariant()}",
        _ => $"ftp://other.example/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={Matter:D}",
    };

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, MatterUrlForm.Canonical)]
    [InlineData(GenericPath.PlaybookUpdate, MatterUrlForm.AbsoluteOnAnotherHost)]
    [InlineData(GenericPath.PlaybookUpdate, MatterUrlForm.AnotherScheme)]
    [InlineData(GenericPath.OutputOrchestrator, MatterUrlForm.Canonical)]
    [InlineData(GenericPath.OutputOrchestrator, MatterUrlForm.AbsoluteOnAnotherHost)]
    [InlineData(GenericPath.OutputOrchestrator, MatterUrlForm.AnotherScheme)]
    public async Task AnAgreeingPairWithARecordUrlOfTheCoreRecord_IsWrittenWithTheCanonicalUrl(GenericPath path, MatterUrlForm form)
    {
        var sent = new Dictionary<string, object?> { ["sprk_regardingrecordid"] = Matter.ToString("D"), ["sprk_regardingrecordurl"] = MatterUrl(form) };

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))), sent,
            StoredUnderTheMatter("sprk_invoiceapproval").Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_invoiceapproval", FamilyRow);
        SentPayload("sprk_invoiceapproval", FamilyRow)["sprk_regardingrecordurl"].Should().Be(MatterUrl(MatterUrlForm.Canonical),
            "an admitted URL is stored as the server writes it");
    }

    [Theory]
    [MemberData(nameof(InvoiceFamilyTables))]
    public async Task APushRuleAimedAtThePair_IsSkipped_AndNothingIsWritten(string table)
    {
        // The push never writes a pair it is mapped: the mapping engine skips a rule aimed at the pair (FR-05(e)), so a family
        // child's pair moves only with its core lookup (045d), and the agreement check has nothing to decide.
        var outcome = await RunAsync(GenericPath.FieldMappingPush, table, FamilyRow, Lookups(PairType(CatalogRowOf("sprk_project"))),
            PairId(OtherMatter), StoredUnderTheMatter(table).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        NothingWritten();
        _deriveReads.Should().BeEmpty();
    }

    // ── Must not fire ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPairWritingPathAndFamilyTable))]
    public async Task ThePairOfTheCoreRecord_IsWritten_WithItsIdInCanonicalForm(GenericPath path, string table)
    {
        var sent = new Dictionary<string, object?> { ["sprk_regardingrecordid"] = Matter.ToString("B").ToUpperInvariant() };

        var outcome = await RunAsync(path, table, FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))), sent,
            StoredUnderTheMatter(table).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
        SentPayload(table, FamilyRow)["sprk_regardingrecordid"].Should().Be(Matter.ToString("D").ToLowerInvariant(),
            "the D-68 key never takes another spelling of the same id");
    }

    /// <summary>An external identifier filed under its invoice alone (D-68: no core lookup), as the app reads it.</summary>
    private static Mock<IGenericEntityService> StoredUnderTheInvoiceOnly()
    {
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        app.Setup(a => a.RetrieveAsync("sprk_externalidentifier", FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_externalidentifier", FamilyRow)
            {
                [InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn] = new EntityReference("sprk_invoice", Invoice),
            });
        return app;
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnExternalIdentifierFiledUnderAnInvoiceOnly_GivenItsInvoicesPair_IsWritten_WithTheIdCanonical(GenericPath path)
    {
        // D-68: the row regards its invoice, so the invoice's pair agrees; it is never refused as naming no core record.
        var sent = new Dictionary<string, object?> { ["sprk_regardingrecordid"] = Invoice.ToString("N").ToUpperInvariant() };

        var outcome = await RunAsync(path, "sprk_externalidentifier", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_invoice"))), sent,
            StoredUnderTheInvoiceOnly().Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_externalidentifier", FamilyRow);
        SentPayload("sprk_externalidentifier", FamilyRow)["sprk_regardingrecordid"].Should().Be(Invoice.ToString("D").ToLowerInvariant());
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnExternalIdentifierFiledUnderAnInvoiceOnly_GivenAMattersPair_IsRefused(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_externalidentifier", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))),
            PairId(Matter), StoredUnderTheInvoiceOnly().Object);

        RefusedAsDisagreeing(outcome);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AWriteThatMovesTheCoreLookup_WithADisagreeingPair_IsWrittenWithTheDerivedPair(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow,
            Lookups(("sprk_regardingmatter", MatterRef(OtherMatter)), PairType(CatalogRowOf("sprk_project"))), PairId(Project),
            StoredUnderTheMatter("sprk_invoiceapproval").Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoiceapproval", FamilyRow), "sprk_RegardingRecordType", "sprk_matter",
            OtherMatter, ShownOn(path, RecordName(OtherMatter)), ShownOn(path, RecordNumber(OtherMatter)));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AWriteThatMovesTheCoreLookup_WithoutAPair_IsWrittenWithTheDerivedPair(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoicecorrection", FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(OtherMatter))),
            NoPlain, StoredUnderTheMatter("sprk_invoicecorrection").Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoicecorrection", FamilyRow), "sprk_RegardingRecordType", "sprk_matter",
            OtherMatter, ShownOn(path, RecordName(OtherMatter)), ShownOn(path, RecordNumber(OtherMatter)));
    }

    [Theory]
    [MemberData(nameof(EveryPathAndFamilyTable))]
    public async Task AWriteWithNoPair_TouchingNoCoreLookup_IsWritten_AndReadsNoCatalog(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow, NoLookups, RenameOnly, StoredUnderTheMatter(table).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
        _deriveReads.Should().BeEmpty();
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task ACatalogReadThatFaults_IsRefusedReadFailed_AndNothingIsWritten(GenericPath path)
    {
        _catalogAnswer = _ => throw new TimeoutException("socket 10.0.0.9 timed out");

        var outcome = await RunAsync(path, "sprk_invoice", FamilyRow, Lookups(PairType(CatalogRowOf("sprk_matter"))), PairId(Matter),
            StoredUnderTheMatter("sprk_invoice").Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Message.Should().NotContain("10.0.0.9");
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AStoredRowReadThatFaults_WithAPairSent_IsRefusedReadFailed_AndNothingIsWritten(GenericPath path)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync("sprk_invoiceapproval", FamilyRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.9 timed out"));

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow, NoLookups, PairId(Matter), app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Message.Should().NotContain("10.0.0.9");
        NothingWritten();
    }

    [Fact]
    public async Task ACancellationDuringTheCatalogRead_IsRethrown_AndNothingIsWritten()
    {
        using var cancel = new CancellationTokenSource();
        _catalogAnswer = _ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        var act = () => Handler(StoredUnderTheMatter("sprk_invoice").Object).UpdateAsync(
            "sprk_invoice", FamilyRow, new Dictionary<string, object?>
            {
                ["sprk_regardingrecordid"] = Matter.ToString("D"),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", CatalogRowOf("sprk_matter")),
            },
            Sprk.Bff.Api.Services.Ai.ConcurrencyMode.None, maxRetries: 1, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingWritten();
    }
}
