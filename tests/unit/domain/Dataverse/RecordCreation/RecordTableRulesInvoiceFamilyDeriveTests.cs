using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 045d (owner ruling Q8 (a)): the invoice-family pre-write rule
/// (<see cref="RecordTableRules.InvoiceFamilyRule"/>) adds the five resolver fields of the core record the guard decided, all
/// five or none, and a derive that cannot complete them refuses through the rule's existing mapping
/// (<see cref="RecordTableRules.InvoiceFamilyPreWrite"/>): the row is never written without them. Also
/// <see cref="OwnedChildWrite.WithColumnsSet"/>, which sets them over the caller's own. Pure over in-memory readers —
/// <c>tests/unit/domain/**</c> (ADR-038 §2 path 6). The service's step 5½ is pinned by
/// <c>RecordCreationServiceInvoiceFamilyRuleSeamTests</c>.
/// </summary>
public class RecordTableRulesInvoiceFamilyDeriveTests
{
    private static readonly Guid Core = Guid.Parse("a045d000-0000-4000-8000-0000000000C1");
    private static readonly Guid OtherCore = Guid.Parse("a045d000-0000-4000-8000-0000000000C2");
    private static readonly Guid CatalogRow = Guid.Parse("a045d000-0000-4000-8000-0000000000F1");

    private readonly List<string> _reads = [];

    /// <summary>The caller: the rule asks it nothing for a create that names one core record and no other invoice.</summary>
    private static IDataverseUserClient Caller() => new Mock<IDataverseUserClient>(MockBehavior.Strict).Object;

    private static DataverseWriteItemMapper.MappedItem Binding(params (string Column, string Table)[] lookups)
    {
        var body = lookups.ToDictionary(l => $"{l.Column}@odata.bind", l => (object?)$"/{l.Table}s({Core:D})");
        return new DataverseWriteItemMapper.MappedItem(JsonSerializer.Serialize(body), lookups.Select(l => l.Column).ToArray())
        {
            Lookups = lookups.Select(l => new DataverseWriteItemMapper.MappedLookup(l.Column, l.Table, l.Table + "s",
                l == lookups[0] ? Core : OtherCore)).ToArray(),
        };
    }

    private InvoiceFamilyCoreRecordGuard.RecordTypeLookup Catalog(Func<string, Entity?> answer) => (table, _) =>
    {
        _reads.Add("catalog:" + table);
        return Task.FromResult(answer(table));
    };

    private InvoiceFamilyCoreRecordGuard.RecordTypeLookup CatalogNamingNumber() =>
        Catalog(table => new Entity("sprk_recordtype_ref", CatalogRow) { [RegardingRecordType.RecordNumberFieldColumn] = table + "number" });

    private InvoiceFamilyCoreRecordGuard.RecordLookup Record(Func<string, Guid, string[], Entity?> answer) => (table, id, columns, _) =>
    {
        _reads.Add($"record:{table}:{string.Join(",", columns)}");
        return Task.FromResult(answer(table, id, columns));
    };

    private InvoiceFamilyCoreRecordGuard.RecordLookup NamedRecord(string? name = "Acme v Beta", string? number = "N-000045") =>
        Record((table, id, columns) => new Entity(table, id) { [columns[0]] = name, [columns[1]] = number });

    private static Task<PreWriteResult> Run(string table, DataverseWriteItemMapper.MappedItem item,
        InvoiceFamilyCoreRecordGuard.RecordLookup? records, InvoiceFamilyCoreRecordGuard.RecordTypeLookup? recordTypes) =>
        RecordTableRules.InvoiceFamilyRule.Check(new PreWriteRow(Caller(), table, item, records, recordTypes), CancellationToken.None);

    // ── Must fire: every family table, every core lookup, gets all five ─────────────────────────────────────────────

    public static TheoryData<string, string, string> FamilyRows()
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

    [Theory]
    [MemberData(nameof(FamilyRows))]
    public async Task AnAdmittedRow_GetsTheFiveResolverFieldsOfTheRecordItNames(string table, string column, string core)
    {
        var result = await Run(table, Binding((column, core)), NamedRecord(), CatalogNamingNumber());

        result.Refusal.Should().BeNull();
        result.AdditionalColumns.Should().HaveCount(5);
        result.AdditionalColumns[CoreAncestorResolver.RegardingRecordTypeColumn].Should()
            .BeEquivalentTo(new EntityReference("sprk_recordtype_ref", CatalogRow));
        result.AdditionalColumns[CoreAncestorResolver.RegardingRecordIdColumn].Should().Be(Core.ToString("D").ToLowerInvariant());
        result.AdditionalColumns["sprk_regardingrecordname"].Should().Be("Acme v Beta");
        result.AdditionalColumns["sprk_regardingrecordurl"].Should()
            .Be($"/main.aspx?pagetype=entityrecord&etn={core}&id={Core.ToString("D").ToLowerInvariant()}");
        result.AdditionalColumns["sprk_regardingrecordnumber"].Should().Be("N-000045");
        _reads.Should().Equal(
            ["catalog:" + core, $"record:{core}:{RegardingRecordType.GetPrimaryNameField(core)},{core}number"],
            "the catalog row names the number column; the record is read for its name and that column only");
    }

    [Fact]
    public async Task ALongName_IsCutToTheColumnsLength()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), NamedRecord(name: new string('n', 150)), CatalogNamingNumber());

        ((string)result.AdditionalColumns["sprk_regardingrecordname"]).Should().HaveLength(100);
    }

    // ── Fail closed: a derive that cannot complete all five refuses, through the existing mapping ──────────────────

    [Fact]
    public async Task ACatalogReadThatFaults_IsUndecidedReadFailed_AndTheRecordIsNotRead()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), NamedRecord(),
            (_, _) => throw new TimeoutException("catalog timed out"));

        result.AdditionalColumns.Should().BeEmpty();
        result.Refusal!.IsUndecided.Should().BeTrue("a read the derive needed failed: never written without the five");
        result.Refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        _reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no row")]
    [InlineData("a row naming no number column")]
    public async Task ACatalogWithNoUsableRow_IsRefusedIncomplete(string shape)
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), NamedRecord(),
            Catalog(_ => shape == "no row" ? null : new Entity("sprk_recordtype_ref", CatalogRow)));

        result.AdditionalColumns.Should().BeEmpty();
        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode);
        result.Refusal.IsUndecided.Should().BeFalse();
        result.Refusal.ParentUnavailable.Should().BeFalse();
        _reads.Should().NotContain(r => r.StartsWith("record:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecordThatIsNotThere_IsTheUniformNotFound()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), Record((_, _, _) => null), CatalogNamingNumber());

        result.Refusal!.ParentUnavailable.Should().BeTrue("record_not_found maps to the uniform not-found, as before");
        result.Refusal.Code.Should().BeNull();
        result.AdditionalColumns.Should().BeEmpty();
    }

    [Fact]
    public async Task ARecordReadThatFaults_IsUndecidedReadFailed()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")),
            (_, _, _, _) => throw new InvalidOperationException("the record read answered 503"), CatalogNamingNumber());

        result.Refusal!.IsUndecided.Should().BeTrue();
        result.Refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        result.AdditionalColumns.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "N-1")]
    [InlineData("  ", "N-1")]
    [InlineData("Acme", null)]
    [InlineData("Acme", "")]
    public async Task ARecordWithABlankNameOrNumber_IsRefusedIncomplete_NeverAPartialSet(string? name, string? number)
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), NamedRecord(name, number), CatalogNamingNumber());

        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode);
        result.AdditionalColumns.Should().BeEmpty();
    }

    [Fact]
    public async Task ARowWithNoReaders_IsUndecidedReadFailed()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter")), records: null, recordTypes: null);

        result.Refusal!.IsUndecided.Should().BeTrue("a runner that passes no reader cannot admit a row without its five fields");
        result.Refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARowTheGuardRefuses_IsRefusedWithTheGuardsCode_AndNothingIsDerived()
    {
        var result = await Run("sprk_invoice", Binding(("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project")),
            NamedRecord(), CatalogNamingNumber());

        result.Refusal!.Code.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode, "the guard's own refusal comes first, unchanged");
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ATableOutsideTheFamily_IsAdmittedAsItIs_AndNothingIsRead()
    {
        var result = await Run("sprk_event", Binding(("sprk_regardingmatter", "sprk_matter")), NamedRecord(), CatalogNamingNumber());

        result.Should().BeSameAs(PreWriteResult.Admit);
        _reads.Should().BeEmpty();
    }

    // ── WithColumnsSet: the five are set over the caller's own ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_invoice", "sprk_regardingrecordtype")]
    [InlineData("sprk_billingevent", "sprk_RegardingRecordType")]
    public void TheFiveAreSetOverTheCallersOwn_ThePairTypeOnTheTablesNavigationProperty(string table, string navigation)
    {
        var forgedType = Guid.Parse("a045d000-0000-4000-8000-0000000000FF");
        var item = new DataverseWriteItemMapper.MappedItem(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sprk_name"] = "kept",
            ["sprk_regardingrecordname"] = "forged",
            ["sprk_regardingrecordid"] = "forged",
            [$"{navigation}@odata.bind"] = $"/sprk_recordtype_refs({forgedType:D})",
        }), ["sprk_name", "sprk_regardingrecordname", "sprk_regardingrecordid", "sprk_regardingrecordtype"])
        {
            Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_recordtype_refs", forgedType)],
        };
        var fields = new InvoiceFamilyResolverFields(CatalogRow, Core.ToString("D"), "Acme v Beta", "/main.aspx?x", "N-1").ToFields();

        var set = OwnedChildWrite.WithColumnsSet(table, item, fields);

        var body = JsonSerializer.Deserialize<Dictionary<string, string>>(set.JsonBody)!;
        body.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["sprk_name"] = "kept",
            ["sprk_regardingrecordname"] = "Acme v Beta",
            ["sprk_regardingrecordid"] = Core.ToString("D"),
            ["sprk_regardingrecordurl"] = "/main.aspx?x",
            ["sprk_regardingrecordnumber"] = "N-1",
            [$"{navigation}@odata.bind"] = $"/sprk_recordtype_refs({CatalogRow:D})",
        });
        set.Lookups.Should().ContainSingle().Which.RecordId.Should().Be(CatalogRow);
        set.Columns.Should().BeEquivalentTo(["sprk_name", "sprk_regardingrecordtype", "sprk_regardingrecordid",
            "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber"]);
    }

    [Fact]
    public void NoColumns_LeavesTheItemAsItIs()
    {
        var item = Binding(("sprk_matter", "sprk_matter"));

        OwnedChildWrite.WithColumnsSet("sprk_invoice", item, new Dictionary<string, object>()).Should().BeSameAs(item);
    }
}
