using FluentAssertions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// The per-table create rules registry (field-mapping-server-write-path-r1 task 012, spec FR-11). The five column lists
/// it replaces are now views over it; each view must hold exactly what its list held before the registry, because the
/// create paths that read them (the child-records route, the chat create tool, the Office quick-create) must not change.
/// The expected values are the pre-change list contents, copied from the source at 83bf5cfa8 (task 012 notes,
/// "Inventory"). Pure data and lookups, no I/O — hence <c>tests/unit/domain/**</c> (ADR-038 §2 path 6).
/// </summary>
public class RecordTableRulesTests
{
    // ── The views equal the pre-change lists ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServerOwnedColumns_View_EqualsThePreRegistryList()
    {
        OwnedChildWrite.ServerOwnedColumns.Should().BeEquivalentTo(
            "ownerid", "owninguser", "owningteam", "owningbusinessunit",
            "createdby", "createdon", "createdonbehalfby",
            "modifiedby", "modifiedon", "modifiedonbehalfby",
            "overriddencreatedon", "importsequencenumber");
        OwnedChildWrite.ServerOwnedColumns.Contains("OwnerId").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void RootServerOwnedColumns_View_EqualsThePreRegistryList()
    {
        ChildRecordEndpoints.RootServerOwnedColumns.Should().BeEquivalentTo(
            "sprk_containerid", "sprk_securitybu", "sprk_accesspermission");
        ChildRecordEndpoints.RootServerOwnedColumns.Contains("SPRK_ContainerId").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void MatterProtectedAttributes_View_EqualsThePreRegistryList()
    {
        OfficeRecordCreationService.MatterProtectedAttributes.Should().BeEquivalentTo(
            "sprk_matternumber", "ownerid", "sprk_containerid", "sprk_createdbyperson");
        OfficeRecordCreationService.MatterProtectedAttributes.Contains("SPRK_MatterNumber").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void ProjectProtectedAttributes_View_EqualsThePreRegistryList()
    {
        OfficeRecordCreationService.ProjectProtectedAttributes.Should().BeEquivalentTo(
            "sprk_projectnumber", "ownerid", "sprk_containerid", "sprk_createdbyperson", "sprk_issecure");
        OfficeRecordCreationService.ProjectProtectedAttributes.Contains("Sprk_IsSecure").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void MatterAndProjectProtectedAttributes_StayPerTable_NotMerged()
    {
        // Task 031: a merged set would newly skip a mapping rule targeting sprk_projectnumber on a MATTER create.
        OfficeRecordCreationService.MatterProtectedAttributes.Should().NotContain("sprk_projectnumber").And.NotContain("sprk_issecure");
        OfficeRecordCreationService.ProjectProtectedAttributes.Should().NotContain("sprk_matternumber");
    }

    [Fact]
    public void ForPersonColumns_View_EqualsThePreRegistryMap()
    {
        OwnedChildWrite.ForPersonColumns.Should().BeEquivalentTo(new Dictionary<string, OwnedChildWrite.ForPersonColumn>
        {
            ["sprk_todo"] = new("sprk_assignedto", "contact"),
            ["sprk_event"] = new("sprk_assignedto", "contact"),
            ["sprk_matter"] = new("sprk_assignedtointernal", "contact"),
            ["sprk_project"] = new("sprk_assignedtointernal", "contact"),
            ["sprk_workassignment"] = new("sprk_assignedtointernal", "contact"),
            ["sprk_communication"] = new("sprk_sentby", "systemuser"),
        });
        OwnedChildWrite.ForPersonColumns.ContainsKey("SPRK_Todo").Should().BeTrue("the map was case-insensitive");
        OwnedChildWrite.ForPersonColumns.ContainsKey("sprk_memo").Should().BeFalse("a table not listed has no 'for' column");
    }

    // ── Lookups ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_notaregisteredtable")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_TableWithNoEntry_ReturnsNull(string? table)
    {
        RecordTableRules.Find(table).Should().BeNull();
    }

    [Fact]
    public void Get_TableWithNoEntry_ThrowsNamingTheTable()
    {
        var act = () => RecordTableRules.Get("sprk_notaregisteredtable");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*sprk_notaregisteredtable*");
    }

    [Fact]
    public void Find_IgnoresCaseAndPadding()
    {
        RecordTableRules.Find("  SPRK_Matter ")!.Table.Should().Be("sprk_matter");
    }

    // ── Per-table facts the old lists did not carry ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_matter", "sprk_matternumber")]
    [InlineData("sprk_project", "sprk_projectnumber")]
    public void AutonumberColumn_IsTheNumberingSchemaColumn_AndIsProtectedFromMapping(string table, string column)
    {
        var rule = RecordTableRules.Get(table);

        rule.AutonumberColumn.Should().Be(column);
        rule.MappingProtectedColumns.Should().Contain(column, "a mapped number would pre-empt the platform sequence (task 076)");
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_billingevent")]
    public void MappingProtectedColumns_TheInvoiceFamilysCoreLookups_AreNeverMapped(string table)
    {
        // Task 014 (ebilling-r1's review of 013, item 3): InvoiceFamilyCoreRecordGuard.CoreRecordLookups on work/ebilling-r1
        // (TypedMatterProjectPlusRegardingWorkAssignment) — a row names exactly ONE core record, the caller's.
        RecordTableRules.Get(table).MappingProtectedColumns
            .Should().BeEquivalentTo("sprk_matter", "sprk_project", "sprk_regardingworkassignment");
        RecordTableRules.Get(table).MappingProtectedColumns.Contains("SPRK_Matter").Should().BeTrue("case-insensitive, as every list");
    }

    [Fact]
    public void MappingProtectedColumns_ADocumentsAccessInheritance_IsNeverMapped_ItsAccessPermissionStaysFillOnly()
    {
        // Task 014 (unified-access-control-r2 D7 / #2169): the BFF writes sprk_accessinheritance; sprk_accesspermission is a
        // value a mapping may fill when the caller left it empty.
        var document = RecordTableRules.Get("sprk_document");

        document.MappingProtectedColumns.Should().BeEquivalentTo(Sprk.Bff.Api.Services.Access.AccessInheritance.Column);
        document.MappingProtectedColumns.Should().NotContain("sprk_accesspermission");
        Sprk.Bff.Api.Services.Access.AccessInheritance.Column.Should().Be("sprk_accessinheritance");
    }

    [Fact]
    public void MappingProtectedColumns_AreDeclaredOnlyOnTheTablesThatNeedThem()
    {
        RecordTableRules.All.Where(r => r.MappingProtectedColumns.Count > 0).Select(r => r.Table)
            .Should().BeEquivalentTo("sprk_matter", "sprk_project", "sprk_invoice", "sprk_billingevent", "sprk_document");
    }

    [Fact]
    public void Kind_TheThreeSecureRootsAreRoots_EveryOtherTableIsAChild()
    {
        RecordTableRules.All.Where(r => r.Kind == RecordTableKind.Root).Select(r => r.Table)
            .Should().BeEquivalentTo(RecordCreatorPerson.StampedTables);
    }

    [Theory]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_budgetrevision")]
    public void MappingApplies_OntologyWriterTables_AreExcludedByANamedRule(string table)
    {
        var rule = RecordTableRules.Get(table);

        rule.MappingApplies.Should().BeFalse("§0.3 binds their content (ontology-r1)");
        rule.MappingExclusion!.Name.Should().Be("OntologyContentIsNotMapped");
    }

    [Fact]
    public void MappingApplies_EveryOtherTable_IsTrue()
    {
        RecordTableRules.All.Where(r => !r.MappingApplies).Select(r => r.Table)
            .Should().BeEquivalentTo("sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision");
    }

    [Fact]
    public void SearchIndexed_IsMatterProjectAndDocumentOnly()
    {
        RecordTableRules.All.Where(r => r.SearchIndexed).Select(r => r.Table)
            .Should().BeEquivalentTo("sprk_matter", "sprk_project", "sprk_document");
    }

    // ── Named rules carry a rationale ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryNamedRule_HasANameAndARationale()
    {
        var named = RecordTableRules.All
            .SelectMany(r => new[] { r.OwnershipException, r.MappingExclusion }.Concat(r.PreWriteRules.Select(p => p.Rule)))
            .Where(n => n is not null)
            .ToList();

        named.Should().NotBeEmpty();
        named.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n!.Name) && !string.IsNullOrWhiteSpace(n.Rationale));
    }

    [Fact]
    public void OwnershipException_IsDeclaredExactlyWhereThePathKeepsTheCreatorWhenUnfiled()
    {
        // The registry's exception and OwnedChildWrite.KeepsItsCreatorWhenUnfiled (the rule PathFor enforces today) must
        // name the same registered tables; sprk_communicationthread (E2) is not a registry table yet (task 012 notes).
        var declared = RecordTableRules.All
            .Where(r => r.OwnershipException?.Name == "UnfiledCommunicationKeepsCreator").Select(r => r.Table);
        var enforced = RecordTableRules.All
            .Where(r => OwnedChildWrite.KeepsItsCreatorWhenUnfiled(r.Table)).Select(r => r.Table);

        declared.Should().BeEquivalentTo(enforced).And.Equal("sprk_communication");
        RecordTableRules.All.Where(r => r.OwnershipException is not null).Should().HaveCount(1);
    }

    [Theory]
    [InlineData("", "detail")]
    [InlineData("invoice_family_no_core_record", " ")]
    public void PreWriteRefusal_RefusedWithoutACodeOrADetail_IsRefused(string code, string detail)
    {
        var act = () => PreWriteRefusal.Refused(code, detail);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NamedRule_BlankRationale_IsRefused(string rationale)
    {
        var act = () => new NamedRule("SomeException", rationale);

        act.Should().Throw<ArgumentException>();
    }
}
