using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 045e (owner, 2026-11-13, #2772 "Refuse it"): on every invoice-family table a write's
/// regarding pair (<c>sprk_regardingrecordid</c> and its record type) must name the row's core record — the core lookup as it
/// will be after the write. The ONE check (<see cref="InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync"/>), pure over
/// an in-memory catalog, and the create's pre-write rule (<see cref="RecordTableRules.InvoiceFamilyRule"/>) that asks it before
/// step 5½'s derive. The paths are pinned end to end by <c>InvoiceFamilyGenericUpdatePathTests.PairAgreement</c>,
/// <c>SecureChildOwnershipAiToolTests.InvoiceFamilyPairAgreement</c> and the create seam.
/// </summary>
public class InvoiceFamilyRegardingPairAgreementTests
{
    private static readonly Guid Core = Guid.Parse("a045e000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherCore = Guid.Parse("a045e000-0000-4000-8000-0000000000c2");
    private static readonly Guid CatalogRow = Guid.Parse("a045e000-0000-4000-8000-0000000000f1");
    private static readonly Guid OtherCatalogRow = Guid.Parse("a045e000-0000-4000-8000-0000000000f2");

    private static readonly Regex AnyGuid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private readonly List<string> _reads = [];

    /// <summary>Every family table with each of its core lookups, and the external identifier filed under an invoice only (D-68).</summary>
    public static TheoryData<string, string, string> FamilyCores()
    {
        var rows = new TheoryData<string, string, string>();
        foreach (var (table, lookups) in InvoiceFamilyCoreRecordGuard.CoreRecordLookups)
        {
            foreach (var (column, core) in lookups)
                rows.Add(table, column, core);
        }

        rows.Add(InvoiceFamilyCoreRecordGuard.ExternalIdentifierEntity, InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn, "sprk_invoice");
        return rows;
    }

    private static InvoiceFamilyCoreRecord Decided(string table, string column, string core, Guid id) =>
        InvoiceFamilyCoreRecordGuard.Check(table, c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase)
            ? new EntityReference(core, id)
            : null);

    /// <summary>The catalog: the active row of every table is <see cref="CatalogRow"/>.</summary>
    private InvoiceFamilyCoreRecordGuard.RecordTypeLookup Catalog(Func<string, Entity?>? answer = null) => (table, _) =>
    {
        _reads.Add("catalog:" + table);
        return Task.FromResult(answer is null ? new Entity("sprk_recordtype_ref", CatalogRow) : answer(table));
    };

    private static RegardingPairWrite Pair(Guid? id = null, Guid? type = null, bool clearId = false, bool clearType = false) =>
        new(id is not null || clearId, id?.ToString("D"), type is not null || clearType, type, Unreadable: false);

    /// <summary>A pair-only update of a stored row that names <see cref="Core"/> through <paramref name="column"/>.</summary>
    private Task<InvoiceFamilyCoreRecord?> PairOnlyUpdate(string table, string column, string core, RegardingPairWrite pair,
        InvoiceFamilyCoreRecordGuard.RecordTypeLookup? catalog = null, CancellationToken ct = default)
    {
        var stored = Decided(table, column, core, Core);
        return InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync(table, [], pair, stored, stored, catalog ?? Catalog(), ct);
    }

    private static void RefusedAsDisagreeing(InvoiceFamilyCoreRecord? refusal)
    {
        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        refusal.Reason.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchReason);
        AnyGuid.IsMatch(refusal.Reason!).Should().BeFalse("fixed text: it names no record");
    }

    // ── Must fire: a pair that names another record ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task APairNamingAnotherRecord_IsRefused_BeforeAnyCatalogRead(string table, string column, string core)
    {
        RefusedAsDisagreeing(await PairOnlyUpdate(table, column, core, Pair(id: OtherCore, type: CatalogRow)));
        _reads.Should().BeEmpty("the id decides it: nothing is read");
    }

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task TheCoreRecordsIdUnderAnotherType_IsRefused(string table, string column, string core)
    {
        RefusedAsDisagreeing(await PairOnlyUpdate(table, column, core, Pair(id: Core, type: OtherCatalogRow)));
        _reads.Should().Equal("catalog:" + core);
    }

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task AnotherTypeAlone_IsRefused(string table, string column, string core)
    {
        RefusedAsDisagreeing(await PairOnlyUpdate(table, column, core, Pair(type: OtherCatalogRow)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task APairClearedByAnUpdate_IsRefused_TheRowWouldNoLongerDescribeItsCoreRecord(bool clearId, bool clearType)
    {
        RefusedAsDisagreeing(await PairOnlyUpdate("sprk_invoiceapproval", "sprk_regardingmatter", "sprk_matter",
            Pair(clearId: clearId, clearType: clearType)));
    }

    [Theory]
    [InlineData("not an id")]
    [InlineData("")]
    public async Task AnIdThatNamesNoRecord_IsRefusedOnAnUpdate(string id)
    {
        RefusedAsDisagreeing(await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter",
            new RegardingPairWrite(SetsId: true, id, SetsType: false, null, Unreadable: false)));
    }

    [Fact]
    public async Task AnUnreadablePairValue_IsRefused()
    {
        RefusedAsDisagreeing(await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter",
            new RegardingPairWrite(SetsId: true, null, SetsType: false, null, Unreadable: true)));
    }

    [Fact]
    public async Task AnExternalIdentifierFiledUnderAnInvoiceOnly_GivenAnotherInvoicesId_IsRefused_ItsD68KeyDoesNotMove()
    {
        RefusedAsDisagreeing(await PairOnlyUpdate(InvoiceFamilyCoreRecordGuard.ExternalIdentifierEntity,
            InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn, "sprk_invoice", Pair(id: OtherCore)));
    }

    [Fact]
    public async Task AnUpdateThatReNamesTheRecordTheRowAlreadyNames_StillHasItsPairChecked()
    {
        // The core lookup is written but does not move, so 045d derives nothing and the pair sent would be written.
        var stored = Decided("sprk_invoicecorrection", "sprk_regardingmatter", "sprk_matter", Core);

        var refusal = await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync("sprk_invoicecorrection",
            ["sprk_regardingmatter"], Pair(id: OtherCore), stored, stored, Catalog());

        RefusedAsDisagreeing(refusal);
    }

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task ACreateWhosePairNamesAnotherRecord_IsRefused(string table, string column, string core)
    {
        var after = Decided(table, column, core, Core);

        RefusedAsDisagreeing(await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync(table, [column],
            Pair(id: OtherCore), after, before: null, Catalog()));
    }

    // ── Must not fire ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task ThePairOfTheCoreRecord_Agrees(string table, string column, string core)
    {
        (await PairOnlyUpdate(table, column, core, Pair(id: Core, type: CatalogRow))).Should().BeNull();
        _reads.Should().Equal("catalog:" + core);
    }

    [Fact]
    public async Task AnIdInAnotherCase_OrBraced_Agrees()
    {
        foreach (var text in new[] { Core.ToString("D").ToUpperInvariant(), Core.ToString("B"), $"  {Core:D} " })
        {
            (await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter",
                new RegardingPairWrite(SetsId: true, text, SetsType: false, null, Unreadable: false))).Should().BeNull(text);
        }
    }

    [Fact]
    public async Task AWriteThatSendsNoPair_IsNotChecked_AndReadsNothing()
    {
        (await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter", default)).Should().BeNull();
        _reads.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FamilyCores))]
    public async Task AWriteThatMovesTheCoreRecord_IsNotChecked_TheDeriveReplacesThePair(string table, string column, string core)
    {
        var before = Decided(table, column, core, Core);
        var after = Decided(table, column, core, OtherCore);

        (await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync(table, [column + "@odata.bind"],
            Pair(id: Guid.NewGuid(), type: OtherCatalogRow), after, before, Catalog())).Should().BeNull();
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task OnACreate_AHalfThatNamesNoRecord_IsNotChecked_TheServerWritesThePairFromTheCoreRecord()
    {
        var after = Decided("sprk_invoice", "sprk_matter", "sprk_matter", Core);

        (await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync("sprk_invoice", ["sprk_matter"],
            Pair(clearId: true, clearType: true), after, before: null, Catalog())).Should().BeNull();
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ATableOutsideTheFamily_IsNotChecked()
    {
        var notFamily = InvoiceFamilyCoreRecord.Refused("sprk_event", InvoiceFamilyCoreRecordGuard.UnknownEntityCode, "x");

        (await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync("sprk_event", [], Pair(id: OtherCore), notFamily,
            notFamily, Catalog())).Should().BeNull();
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARowThatNamesNoSingleCoreRecord_IsRefusedWithTheGuardsOwnAnswer()
    {
        var none = InvoiceFamilyCoreRecordGuard.Check("sprk_invoice", _ => null);

        var refusal = await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync("sprk_invoice", [], Pair(id: Core), none, none, Catalog());

        refusal!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode);
    }

    [Fact]
    public async Task ACatalogReadThatFaults_IsReadFailed_WithoutTheFaultsText()
    {
        var refusal = await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter", Pair(type: CatalogRow),
            (_, _) => throw new TimeoutException("socket 10.0.0.7 timed out"));

        refusal!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        refusal.Reason.Should().NotContain("10.0.0.7").And.Contain(nameof(TimeoutException));
    }

    [Fact]
    public async Task NoCatalogReader_IsReadFailed()
    {
        var stored = Decided("sprk_invoice", "sprk_matter", "sprk_matter", Core);

        var refusal = await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync("sprk_invoice", [], Pair(type: CatalogRow),
            stored, stored, recordTypes: null);

        refusal!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    [Fact]
    public async Task ACoreTableWithNoActiveCatalogRow_IsRefusedIncomplete()
    {
        var refusal = await PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter", Pair(type: CatalogRow), Catalog(_ => null));

        refusal!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode);
    }

    [Fact]
    public async Task ACancellationDuringTheCatalogRead_IsRethrown()
    {
        using var cancel = new CancellationTokenSource();
        var act = () => PairOnlyUpdate("sprk_invoice", "sprk_matter", "sprk_matter", Pair(type: CatalogRow), (_, _) =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        }, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The create's pre-write rule: asked before step 5½'s derive replaces the pair ─────────────────────────────────

    private static IDataverseUserClient NoCallerRead() => new Mock<IDataverseUserClient>(MockBehavior.Strict).Object;

    /// <summary>A create item binding <paramref name="column"/> to <see cref="Core"/>, plus the pair halves given.</summary>
    private static DataverseWriteItemMapper.MappedItem CreateItem(string table, string column, string core, Guid? pairId, Guid? pairType)
    {
        var body = new Dictionary<string, object?> { [$"{column}@odata.bind"] = $"/{core}s({Core:D})" };
        var columns = new List<string> { column };
        var lookups = new List<DataverseWriteItemMapper.MappedLookup> { new(column, core, core + "s", Core) };
        if (pairId is { } id)
        {
            body["sprk_regardingrecordid"] = id.ToString("D");
            columns.Add("sprk_regardingrecordid");
        }

        if (pairType is { } type)
        {
            body[$"{OwnedChildWrite.RegardingRecordTypeNavigationPropertyOf(table)}@odata.bind"] = $"/sprk_recordtype_refs({type:D})";
            columns.Add("sprk_regardingrecordtype");
            lookups.Add(new("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_recordtype_refs", type));
        }

        return new DataverseWriteItemMapper.MappedItem(JsonSerializer.Serialize(body), columns.ToArray()) { Lookups = lookups };
    }

    private InvoiceFamilyCoreRecordGuard.RecordLookup NamedRecord() => (table, id, columns, _) =>
    {
        _reads.Add("record:" + table);
        return Task.FromResult<Entity?>(new Entity(table, id) { [columns[0]] = "Acme v Beta", [columns[1]] = "N-1" });
    };

    private InvoiceFamilyCoreRecordGuard.RecordTypeLookup NumberedCatalog() => (table, _) =>
    {
        _reads.Add("catalog:" + table);
        return Task.FromResult<Entity?>(new Entity("sprk_recordtype_ref", CatalogRow) { [RegardingRecordType.RecordNumberFieldColumn] = table + "number" });
    };

    private Task<PreWriteResult> Create(string table, DataverseWriteItemMapper.MappedItem item) =>
        RecordTableRules.InvoiceFamilyRule.Check(new PreWriteRow(NoCallerRead(), table, item, NamedRecord(), NumberedCatalog()),
            CancellationToken.None);

    /// <summary>Every family table with its first core lookup (the rule is registered on all six).</summary>
    public static TheoryData<string, string, string> EveryFamilyTable()
    {
        var rows = new TheoryData<string, string, string>();
        foreach (var (table, lookups) in InvoiceFamilyCoreRecordGuard.CoreRecordLookups)
            rows.Add(table, lookups[0].Column, lookups[0].CoreEntity);
        return rows;
    }

    [Theory]
    [MemberData(nameof(EveryFamilyTable))]
    public async Task CreateRule_APairNamingAnotherRecord_IsRefused400_AndNothingIsDerived(string table, string column, string core)
    {
        var result = await Create(table, CreateItem(table, column, core, OtherCore, CatalogRow));

        result.AdditionalColumns.Should().BeEmpty();
        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        result.Refusal.IsUndecided.Should().BeFalse();
        result.Refusal.ParentUnavailable.Should().BeFalse();
        AnyGuid.IsMatch(result.Refusal.Detail ?? string.Empty).Should().BeFalse();
        _reads.Should().BeEmpty("refused before the derive reads anything");
    }

    [Theory]
    [MemberData(nameof(EveryFamilyTable))]
    public async Task CreateRule_TheCoreRecordsIdUnderAnotherType_IsRefused400(string table, string column, string core)
    {
        var result = await Create(table, CreateItem(table, column, core, Core, OtherCatalogRow));

        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode);
        _reads.Should().Equal("catalog:" + core);
    }

    [Theory]
    [MemberData(nameof(EveryFamilyTable))]
    public async Task CreateRule_ThePairOfTheCoreRecord_IsAdmitted_WithTheFiveDerived(string table, string column, string core)
    {
        var result = await Create(table, CreateItem(table, column, core, Core, CatalogRow));

        result.Refusal.Should().BeNull();
        result.AdditionalColumns.Should().HaveCount(5);
        result.AdditionalColumns[CoreAncestorResolver.RegardingRecordIdColumn].Should().Be(Core.ToString("D").ToLowerInvariant());
    }

    [Theory]
    [MemberData(nameof(EveryFamilyTable))]
    public async Task CreateRule_NoPair_IsAdmitted_AndReadsNothingMoreThanTheDerive(string table, string column, string core)
    {
        var result = await Create(table, CreateItem(table, column, core, null, null));

        result.Refusal.Should().BeNull();
        _reads.Should().Equal("catalog:" + core, "record:" + core);
    }

    [Fact]
    public async Task CreateRule_ACatalogFaultWhileCheckingTheType_IsUndecidedReadFailed()
    {
        var result = await RecordTableRules.InvoiceFamilyRule.Check(new PreWriteRow(NoCallerRead(), "sprk_invoice",
                CreateItem("sprk_invoice", "sprk_matter", "sprk_matter", Core, CatalogRow), NamedRecord(),
                (_, _) => throw new TimeoutException("catalog timed out")),
            CancellationToken.None);

        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        result.Refusal.IsUndecided.Should().BeTrue();
        _reads.Should().BeEmpty("nothing is derived");
    }
}
