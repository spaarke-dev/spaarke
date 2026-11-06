using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// ebilling-r1 task 013 — FR-05 for the invoice family: exactly one core-record lookup (matter, project or work
/// assignment) per row, refused (never picked) when two or three are set or none is, and all five resolver fields or a
/// refusal. Plus the stored-row derivation on <see cref="CoreAncestorResolver"/> (D-75: a line's core record is its own).
/// </summary>
public class InvoiceFamilyCoreRecordGuardTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkAssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InvoiceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherInvoiceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RowId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid RecordTypeId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>The six family tables and the column each uses for each core type (live, tasks 010/012/020/021/022/027).</summary>
    public static TheoryData<string, string, string> EveryTableAndCore()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (table, columns) in new (string, (string Core, string Column)[])[]
                 {
                     ("sprk_invoice", Typed()), ("sprk_billingevent", Typed()), ("sprk_invoicesubmission", Trio()),
                     ("sprk_invoiceapproval", Trio()), ("sprk_externalidentifier", Trio()), ("sprk_invoicecorrection", Trio()),
                 })
        {
            foreach (var (core, column) in columns)
                data.Add(table, core, column);
        }

        return data;

        static (string, string)[] Typed() =>
            [("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project"), ("sprk_workassignment", "sprk_regardingworkassignment")];

        static (string, string)[] Trio() =>
        [
            ("sprk_matter", "sprk_regardingmatter"), ("sprk_project", "sprk_regardingproject"),
            ("sprk_workassignment", "sprk_regardingworkassignment"),
        ];
    }

    public static TheoryData<string> FamilyTables() => new()
    {
        "sprk_invoice", "sprk_billingevent", "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_externalidentifier",
        "sprk_invoicecorrection",
    };

    private static Guid IdOf(string core) => core switch
    {
        "sprk_matter" => MatterId,
        "sprk_project" => ProjectId,
        _ => WorkAssignmentId,
    };

    private static Entity RowWith(string table, params string[] cores)
    {
        var row = new Entity(table, RowId);
        foreach (var core in cores)
        {
            var column = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Single(l => l.CoreEntity == core).Column;
            row[column] = new EntityReference(core, IdOf(core));
        }

        return row;
    }

    // ── The lookup map ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMap_CoversExactlyTheSixFamilyTables_EachWithMatterProjectAndWorkAssignment()
    {
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys.Should().BeEquivalentTo(
            "sprk_invoice", "sprk_billingevent", "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_externalidentifier",
            "sprk_invoicecorrection");
        foreach (var (table, lookups) in InvoiceFamilyCoreRecordGuard.CoreRecordLookups)
        {
            lookups.Select(l => l.CoreEntity).Should().BeEquivalentTo(
                new[] { "sprk_matter", "sprk_project", "sprk_workassignment" }, $"{table}: FR-05 names three core types");
            lookups.Select(l => l.Column).Should().NotContain("sprk_invoice", $"{table}: D-75, an invoice is never a core record");
        }
    }

    [Theory]
    [MemberData(nameof(EveryTableAndCore))]
    public void TheMap_UsesTheLiveColumnName(string table, string core, string column)
    {
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Should().Contain((column, core));
    }

    // ── Exactly one ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryTableAndCore))]
    public void OneLookup_DecidesThatCoreRecord(string table, string core, string column)
    {
        var result = InvoiceFamilyCoreRecordGuard.Check(RowWith(table, core));

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(new EntityReference(core, IdOf(core)));
        result.RegardingColumn.Should().Be(column);
        result.IsInvoiceOnly.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(FamilyTables))]
    public void TwoLookups_AreRefused_ForEveryPair(string table)
    {
        foreach (var pair in new[]
                 {
                     new[] { "sprk_matter", "sprk_project" }, new[] { "sprk_matter", "sprk_workassignment" },
                     new[] { "sprk_project", "sprk_workassignment" },
                 })
        {
            var result = InvoiceFamilyCoreRecordGuard.Check(RowWith(table, pair));

            result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode, $"{table}: {string.Join("+", pair)}");
            result.Regarding.Should().BeNull("a refusal never picks one");
        }
    }

    [Theory]
    [MemberData(nameof(FamilyTables))]
    public void ThreeLookups_AreRefused(string table)
    {
        var result = InvoiceFamilyCoreRecordGuard.Check(RowWith(table, "sprk_matter", "sprk_project", "sprk_workassignment"));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        result.Regarding.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(FamilyTables))]
    public void NoLookup_IsRefused(string table)
    {
        var result = InvoiceFamilyCoreRecordGuard.Check(new Entity(table, RowId));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode);
        result.Regarding.Should().BeNull();
    }

    [Fact]
    public void AnEmptyIdIsUnset_SoTheOtherLookupIsTheOne()
    {
        var row = RowWith("sprk_invoice", "sprk_matter");
        row["sprk_project"] = new EntityReference("sprk_project", Guid.Empty);

        InvoiceFamilyCoreRecordGuard.Check(row).Regarding.Should().BeEquivalentTo(new EntityReference("sprk_matter", MatterId));
    }

    [Fact]
    public void UnknownEntity_IsRefused()
    {
        var result = InvoiceFamilyCoreRecordGuard.Check(new Entity("sprk_todo", RowId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.UnknownEntityCode);
        InvoiceFamilyCoreRecordGuard.ColumnsToRead("sprk_billingexport").Should().BeNull("an export batch has no core record");
    }

    [Theory]
    [InlineData("a guid, not a lookup")]
    [InlineData("a reference to the wrong table")]
    public void AValueThatIsNotALookupToItsCoreTable_IsRefused_NotReadAsUnset(string shape)
    {
        object value = shape.StartsWith("a guid", StringComparison.Ordinal)
            ? ProjectId
            : new EntityReference("sprk_matter", ProjectId);
        var row = RowWith("sprk_invoicesubmission", "sprk_matter");
        row["sprk_regardingproject"] = value;

        InvoiceFamilyCoreRecordGuard.Check(row).RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode);
    }

    [Fact]
    public void D75_ALinesInvoiceIsNeverItsCoreRecord()
    {
        var line = new Entity("sprk_billingevent", RowId) { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) };

        InvoiceFamilyCoreRecordGuard.Check(line).RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode);
        InvoiceFamilyCoreRecordGuard.ColumnsToRead("sprk_billingevent").Should().NotContain("sprk_invoice");
    }

    // ── D-68: an external identifier that regards an invoice only ────────────────────────────────────────────────

    [Fact]
    public void D68_AnInvoiceOnlyExternalIdentifier_RegardsTheInvoice()
    {
        var row = new Entity("sprk_externalidentifier", RowId)
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
        };

        var result = InvoiceFamilyCoreRecordGuard.Check(row);

        result.IsRefused.Should().BeFalse(result.Reason);
        result.IsInvoiceOnly.Should().BeTrue();
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_invoice", InvoiceId));
        result.RegardingColumn.Should().Be("sprk_regardinginvoice");
    }

    [Fact]
    public void D68_AnExternalIdentifierWithACoreRecordAndAnInvoice_RegardsTheCoreRecord()
    {
        var row = RowWith("sprk_externalidentifier", "sprk_project");
        row["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId);

        var result = InvoiceFamilyCoreRecordGuard.Check(row);

        result.IsInvoiceOnly.Should().BeFalse();
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_project", ProjectId));
    }

    [Fact]
    public void D68_TheInvoiceOnlyPathIsTheExternalIdentifiersAlone()
    {
        // A correction carries sprk_invoice, a submission sprk_invoice: neither regards an invoice instead of a core record.
        var correction = new Entity("sprk_invoicecorrection", RowId) { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) };

        InvoiceFamilyCoreRecordGuard.Check(correction).RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode);
    }

    // ── The five resolver fields ──────────────────────────────────────────────────────────────────────────────────

    private static Mock<IGenericEntityService> RecordsReturning(Entity? row)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row!);
        return mock;
    }

    private static InvoiceFamilyCoreRecordGuard.RecordTypeLookup RecordType(string numberField) =>
        (_, _) => Task.FromResult<Entity?>(new Entity("sprk_recordtype_ref", RecordTypeId)
        {
            [RegardingRecordType.RecordNumberFieldColumn] = numberField,
        });

    public static TheoryData<string, Guid, string, string> CoreTypes() => new()
    {
        { "sprk_matter", MatterId, "sprk_mattername", "sprk_matternumber" },
        { "sprk_project", ProjectId, "sprk_projectname", "sprk_projectnumber" },
        { "sprk_workassignment", WorkAssignmentId, "sprk_name", "sprk_workassignmentnumber" },
    };

    [Theory]
    [MemberData(nameof(CoreTypes))]
    public async Task OneLookup_YieldsFiveCompleteResolverFields_ForEachCoreType(
        string core, Guid id, string nameField, string numberField)
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoice", core));
        var records = RecordsReturning(new Entity(core, id) { [nameField] = "  Acme v. Widget  ", [numberField] = " M-0042 " });

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records.Object, RecordType(numberField));

        result.IsRefused.Should().BeFalse(result.Reason);
        var expectedId = id.ToString("D").ToLowerInvariant();
        result.ResolverFields.Should().Be(new InvoiceFamilyResolverFields(
            RecordTypeId, expectedId, "Acme v. Widget", $"/main.aspx?pagetype=entityrecord&etn={core}&id={expectedId}", "M-0042"));
        result.ResolverFields!.ToFields().Should().HaveCount(5).And.OnlyContain(kv => kv.Value != null && kv.Value.ToString() != string.Empty);
        result.ResolverFields.ToFields()["sprk_regardingrecordtype"].Should().BeEquivalentTo(new EntityReference("sprk_recordtype_ref", RecordTypeId));
        records.Verify(s => s.RetrieveAsync(core, id, new[] { nameField, numberField }, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task D68_AnInvoiceOnlyExternalIdentifier_GetsTheInvoicesFieldsSoTheKeyHasNoEmptyColumn()
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(new Entity("sprk_externalidentifier", RowId)
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
        });
        var records = RecordsReturning(new Entity("sprk_invoice", InvoiceId) { ["sprk_name"] = "Firm - INV-9", ["sprk_invoicenumber"] = "INV-9" });

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records.Object, RecordType("sprk_invoicenumber"));

        result.ResolverFields!.RecordId.Should().Be(InvoiceId.ToString("D").ToLowerInvariant(),
            "sprk_regardingrecordid is the D-68 key column; for an invoice-only row it holds the invoice id");
        result.ResolverFields.RecordNumber.Should().Be("INV-9");
    }

    [Fact]
    public async Task ALongName_IsCutToTheShortestColumn()
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_billingevent", "sprk_matter"));
        var records = RecordsReturning(new Entity("sprk_matter", MatterId) { ["sprk_mattername"] = new string('n', 150), ["sprk_matternumber"] = "M-1" });

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records.Object, RecordType("sprk_matternumber"));

        result.ResolverFields!.RecordName.Should().HaveLength(100);
    }

    [Theory]
    [InlineData("no record-type row")]
    [InlineData("record-type row names no number column")]
    [InlineData("blank name")]
    [InlineData("blank number")]
    public async Task AFieldThatCannotBeFilled_IsARefusal_NeverAPartialSet(string gap)
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoiceapproval", "sprk_matter"));
        var records = RecordsReturning(new Entity("sprk_matter", MatterId)
        {
            ["sprk_mattername"] = gap == "blank name" ? " " : "Acme",
            ["sprk_matternumber"] = gap == "blank number" ? null : "M-1",
        });
        InvoiceFamilyCoreRecordGuard.RecordTypeLookup recordTypes = gap switch
        {
            "no record-type row" => (_, _) => Task.FromResult<Entity?>(null),
            "record-type row names no number column" => (_, _) => Task.FromResult<Entity?>(new Entity("sprk_recordtype_ref", RecordTypeId)),
            _ => RecordType("sprk_matternumber"),
        };

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records.Object, recordTypes);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode);
        result.ResolverFields.Should().BeNull();
    }

    [Fact]
    public async Task ARecordReadFailure_IsARefusal_NotSuccessAndNotNoCoreRecord()
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoice", "sprk_project"));
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);
        records.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 503"));

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records.Object, RecordType("sprk_projectnumber"));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        result.Reason.Should().Contain("Dataverse 503");
        result.ResolverFields.Should().BeNull();
    }

    [Fact]
    public async Task ARecordTypeReadFailure_IsARefusal()
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoice", "sprk_project"));
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            decided, records.Object, (_, _) => throw new TimeoutException("429"));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    [Fact]
    public async Task AMissingCoreRecord_IsRecordNotFound()
    {
        var decided = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoice", "sprk_matter"));

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            decided, RecordsReturning(null).Object, RecordType("sprk_matternumber"));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
    }

    [Fact]
    public async Task ARefusal_IsReturnedUnchanged_WithNoRead()
    {
        var refused = InvoiceFamilyCoreRecordGuard.Check(RowWith("sprk_invoice", "sprk_matter", "sprk_project"));
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var result = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            refused, records.Object, (_, _) => throw new InvalidOperationException("must not be called"));

        result.Should().BeSameAs(refused);
    }

    // ── The stored-row derivation (CoreAncestorResolver, D-75) ───────────────────────────────────────────────────

    private static CoreAncestorResolver Resolver(Mock<IGenericEntityService> records, CoreAncestorResolver.EntityColumnProbe probe) =>
        new(records.Object, probe, NullLogger<CoreAncestorResolver>.Instance);

    private static CoreAncestorResolver.EntityColumnProbe ProbeOf(string table) =>
        (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            InvoiceFamilyCoreRecordGuard.ColumnsToRead(table)!.Append("sprk_invoice"), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task D75_AStoredLine_ResolvesItsOwnCoreRecord_WhateverInvoiceItPointsAt()
    {
        // The line's invoice points at another invoice (whose core record would differ); the line's own matter wins and the
        // invoice is neither selected nor read.
        var line = new Entity("sprk_billingevent", RowId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OtherInvoiceId),
        };
        var records = RecordsReturning(line);

        var result = await Resolver(records, ProbeOf("sprk_billingevent"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_billingevent", RowId);

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_matter", MatterId));
        records.Verify(s => s.RetrieveAsync("sprk_billingevent", RowId,
            It.Is<string[]>(c => !c.Contains("sprk_invoice") && c.Contains("sprk_regardingworkassignment")), It.IsAny<CancellationToken>()), Times.Once);
        records.Verify(s => s.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task D75_AStoredLineWithNoInvoice_ResolvesItsOwnProjectOrWorkAssignment(string core)
    {
        var line = RowWith("sprk_billingevent", core);

        var result = await Resolver(RecordsReturning(line), ProbeOf("sprk_billingevent"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_billingevent", RowId);

        result.Regarding.Should().BeEquivalentTo(new EntityReference(core, IdOf(core)));
    }

    [Fact]
    public async Task D75_AStoredLineWithNoCoreLookup_IsRefused_NotResolvedThroughItsInvoice()
    {
        var line = new Entity("sprk_billingevent", RowId) { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) };
        var records = RecordsReturning(line);

        var result = await Resolver(records, ProbeOf("sprk_billingevent"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_billingevent", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode);
        records.Verify(s => s.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AStoredRowNamingTwoCoreRecords_IsRefused()
    {
        var result = await Resolver(RecordsReturning(RowWith("sprk_invoice", "sprk_matter", "sprk_project")), ProbeOf("sprk_invoice"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_invoice", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
    }

    [Fact]
    public async Task AStoredRow_MetadataFailure_IsARefusal_NotNoCoreRecord()
    {
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var result = await Resolver(records, (_, _) => throw new InvalidOperationException("metadata unavailable"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_invoicesubmission", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    [Fact]
    public async Task AStoredRow_WhoseTableLacksALookupColumn_IsARefusal_NeverAPartialRead()
    {
        // A table without its work-assignment column in this org cannot be checked for "exactly one".
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);
        CoreAncestorResolver.EntityColumnProbe probe = (_, _) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matter", "sprk_project" });

        var result = await Resolver(records, probe).ResolveInvoiceFamilyCoreRecordAsync("sprk_invoice", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        result.Reason.Should().Contain("sprk_regardingworkassignment");
    }

    [Fact]
    public async Task AStoredRow_ReadFailure_IsARefusal()
    {
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);
        records.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 503"));

        var result = await Resolver(records, ProbeOf("sprk_invoiceapproval"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_invoiceapproval", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    [Fact]
    public async Task AStoredRow_ThatDoesNotExist_IsRecordNotFound()
    {
        var result = await Resolver(RecordsReturning(null), ProbeOf("sprk_invoiceapproval"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_invoiceapproval", RowId);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
    }

    [Theory]
    [InlineData("sprk_todo", "11111111-1111-1111-1111-111111111111", InvoiceFamilyCoreRecordGuard.UnknownEntityCode)]
    [InlineData("sprk_invoice", "00000000-0000-0000-0000-000000000000", InvoiceFamilyCoreRecordGuard.ReadFailedCode)]
    public async Task AStoredRow_OutsideTheFamilyOrWithNoId_IsRefusedWithoutARead(string table, string id, string code)
    {
        var records = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var result = await Resolver(records, (_, _) => throw new InvalidOperationException("must not be called"))
            .ResolveInvoiceFamilyCoreRecordAsync(table, Guid.Parse(id));

        result.RefusalCode.Should().Be(code);
    }

    [Fact]
    public async Task AStoredInvoiceOnlyExternalIdentifier_RegardsItsInvoice()
    {
        var row = new Entity("sprk_externalidentifier", RowId) { ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId) };

        var result = await Resolver(RecordsReturning(row), ProbeOf("sprk_externalidentifier"))
            .ResolveInvoiceFamilyCoreRecordAsync("sprk_externalidentifier", RowId);

        result.IsInvoiceOnly.Should().BeTrue();
        result.Regarding!.Id.Should().Be(InvoiceId);
    }

    // ── ISS-010 (unified-access-control-r2): the write rule — supersedes is a peer of the same core record ──────────

    private static Func<string, object?> Reads(params (string Column, object? Value)[] values) =>
        column => values.FirstOrDefault(v => v.Column == column).Value;

    private static Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> SupersededRegarding(string core, Guid id) =>
        (_, _) => Task.FromResult(InvoiceFamilyCoreRecordGuard.Check("sprk_invoice",
            Reads((InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"].Single(l => l.CoreEntity == core).Column,
                new EntityReference(core, id)))));

    private static readonly Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> NeverRead =
        (_, _) => throw new InvalidOperationException("must not be read");

    [Fact]
    public void GuardedLookups_AreTheCoreLookups_PlusTheInvoicesSupersedes_TheLinesInvoice_AndTheExternalIdentifiersInvoice()
    {
        InvoiceFamilyCoreRecordGuard.GuardedLookups("sprk_invoice").Should().BeEquivalentTo(new[]
        {
            ("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project"),
            ("sprk_regardingworkassignment", "sprk_workassignment"), ("sprk_supersedesinvoice", "sprk_invoice"),
        });
        // Task 026: a line's invoice is guarded (compared with the line's own core record); D-75 still holds, because the
        // stored-row twin (ColumnsToRead) never reads it and the line's core record is never taken from it.
        InvoiceFamilyCoreRecordGuard.GuardedLookups("sprk_billingevent").Should().BeEquivalentTo(new[]
        {
            ("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project"),
            ("sprk_regardingworkassignment", "sprk_workassignment"), ("sprk_invoice", "sprk_invoice"),
        });
        InvoiceFamilyCoreRecordGuard.ColumnsToRead("sprk_billingevent").Should().NotContain("sprk_invoice", "D-75");
        InvoiceFamilyCoreRecordGuard.GuardedLookups("sprk_externalidentifier")!.Should().Contain(("sprk_regardinginvoice", "sprk_invoice"));
        InvoiceFamilyCoreRecordGuard.GuardedLookups("sprk_todo").Should().BeNull();
    }

    [Fact]
    public async Task CheckWrite_AnInvoiceSupersedingAnInvoiceOfTheSameMatter_IsDecided()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            SupersededRegarding("sprk_matter", MatterId));

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_matter", MatterId));
    }

    [Theory]
    [InlineData("sprk_matter", "22222222-2222-2222-2222-222222222222")] // another matter
    [InlineData("sprk_project", "11111111-1111-1111-1111-111111111111")] // the same id, another core table
    public async Task CheckWrite_AnInvoiceSupersedingAnInvoiceOfAnotherCoreRecord_IsRefusedMismatch(string core, string id)
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            SupersededRegarding(core, Guid.Parse(id)));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
    }

    [Fact]
    public async Task CheckWrite_AnInvoiceSupersedingAnInvoiceThatNamesNoSingleCoreRecord_IsRefusedMismatch()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            (_, _) => Task.FromResult(InvoiceFamilyCoreRecordGuard.Check("sprk_invoice", _ => null)));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
    }

    [Theory]
    [InlineData(InvoiceFamilyCoreRecordGuard.ReadFailedCode)]
    [InlineData(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)]
    public async Task CheckWrite_WhenTheSupersededInvoiceCannotBeRead_FailsClosedWithThatCode_NeverAMatch(string code)
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            (invoice, _) => Task.FromResult(new InvoiceFamilyCoreRecord { EntityLogicalName = "sprk_invoice", RefusalCode = code, Reason = "x" }));

        result.RefusalCode.Should().Be(code);
    }

    [Fact]
    public async Task CheckWrite_WhenReadingTheSupersededInvoiceThrows_FailsClosedReadFailed()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            (_, _) => throw new HttpRequestException("boom"));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
    }

    [Fact]
    public async Task CheckWrite_ASupersedesValueThatIsNotAnInvoice_IsRefused_AndNoSupersedesMeansNoRead()
    {
        var notAnInvoice = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_matter", OtherInvoiceId))),
            NeverRead);
        var none = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead);
        var twoCores = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)), ("sprk_project", new EntityReference("sprk_project", ProjectId)),
                ("sprk_supersedesinvoice", new EntityReference("sprk_invoice", OtherInvoiceId))),
            NeverRead);

        notAnInvoice.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode);
        none.IsRefused.Should().BeFalse(none.Reason);
        twoCores.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode, "the row's own rule is asked first");
    }

    // ── Task 026 (fix-round-1 item 3b): a line belongs only to an invoice of its own core record ────────────────────

    [Fact]
    public async Task CheckWrite_ALineOfAnInvoiceOfTheSameMatter_IsDecided_OnItsOwnLookup()
    {
        EntityReference? asked = null;
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            (invoice, ct) => { asked = invoice; return SupersededRegarding("sprk_matter", MatterId)(invoice, ct); });

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_matter", MatterId));
        result.RegardingColumn.Should().Be("sprk_matter", "D-75: the line's own lookup, never its invoice");
        asked.Should().BeEquivalentTo(new EntityReference("sprk_invoice", InvoiceId));
    }

    [Theory]
    [InlineData("sprk_matter", "22222222-2222-2222-2222-222222222222")] // another matter
    [InlineData("sprk_project", "11111111-1111-1111-1111-111111111111")] // the same id, another core table
    [InlineData("sprk_workassignment", "33333333-3333-3333-3333-333333333333")]
    public async Task CheckWrite_ALineOfAnInvoiceOfAnotherCoreRecord_IsRefusedLineMismatch(string core, string id)
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            SupersededRegarding(core, Guid.Parse(id)));

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        result.Reason.Should().Contain("the invoice it belongs to");
    }

    [Fact]
    public async Task CheckWrite_ALineOfAnInvoiceThatNamesNoSingleCoreRecord_IsRefusedLineMismatch()
    {
        var none = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            (_, _) => Task.FromResult(InvoiceFamilyCoreRecordGuard.Check("sprk_invoice", _ => null)));
        var two = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            (_, _) => Task.FromResult(InvoiceFamilyCoreRecordGuard.Check("sprk_invoice",
                Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                    ("sprk_project", new EntityReference("sprk_project", ProjectId))))));

        none.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        two.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode,
            "never accepted because the invoice's own lookups include the line's core record");
    }

    [Theory]
    [InlineData(InvoiceFamilyCoreRecordGuard.ReadFailedCode)]
    [InlineData(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)]
    public async Task CheckWrite_WhenALinesInvoiceCannotBeRead_FailsClosedWithThatCode_NeverAMatch(string code)
    {
        var refused = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            (_, _) => Task.FromResult(new InvoiceFamilyCoreRecord { EntityLogicalName = "sprk_invoice", RefusalCode = code, Reason = "x" }));
        var thrown = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))),
            (_, _) => throw new HttpRequestException("boom"));

        refused.RefusalCode.Should().Be(code);
        thrown.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        thrown.Reason.Should().NotContain("boom", "the fault's own text stays out of the reason");
    }

    [Fact]
    public async Task CheckWrite_ALineWithNoInvoice_IsNotRead_ALinesOwnRuleIsAskedFirst_AndANonInvoiceValueIsRefused()
    {
        var noInvoice = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead);
        var onlyItsInvoice = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId))), NeverRead);
        var notAnInvoice = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_matter", InvoiceId))),
            NeverRead);

        noInvoice.IsRefused.Should().BeFalse(noInvoice.Reason);
        onlyItsInvoice.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode, "D-75: never derived from the invoice");
        notAnInvoice.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode);
    }

    [Theory]
    [InlineData("sprk_invoicesubmission")]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    [InlineData("sprk_externalidentifier")]
    public async Task CheckWrite_TheOtherFamilyTables_AreNeverComparedWithAnInvoice(string table)
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync(table,
            Reads(("sprk_regardingmatter", new EntityReference("sprk_matter", MatterId)),
                ("sprk_invoice", new EntityReference("sprk_invoice", InvoiceId)),
                ("sprk_regardinginvoice", new EntityReference("sprk_invoice", InvoiceId))),
            NeverRead);

        result.IsRefused.Should().BeFalse(result.Reason);
    }
}
