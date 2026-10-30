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
        // Task 017: the route's view was deleted when the route moved onto the service; the registry entry it viewed is read.
        var workAssignment = RecordTableRules.Get("sprk_workassignment").TableServerOwnedColumns;
        workAssignment.Should().BeEquivalentTo(
            "sprk_containerid", "sprk_securitybu", "sprk_accesspermission");
        workAssignment.Contains("SPRK_ContainerId").Should().BeTrue("the list was case-insensitive");
    }

    // The pre-registry lists plus #2220: the secure flag and the access columns are the BFF's own on every secure root.
    private static readonly string[] SecureRootAccessColumns =
        ["sprk_issecure", "sprk_accesspermission", "sprk_accessinheritance", "sprk_securitybu"];

    [Fact]
    public void MatterMappingProtectedColumns_EqualThePreRegistryList_PlusTheSecureRootAccessColumns()
    {
        // Task 018c: the Office core's view over this set is deleted; the pin stays on the registry, its one source.
        RecordTableRules.Get("sprk_matter").MappingProtectedColumns.Should().BeEquivalentTo(
            ["sprk_matternumber", "ownerid", "sprk_containerid", "sprk_createdbyperson", .. SecureRootAccessColumns]);
        RecordTableRules.Get("sprk_matter").MappingProtectedColumns.Contains("SPRK_MatterNumber").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void ProjectMappingProtectedColumns_EqualThePreRegistryList_PlusTheSecureRootAccessColumns()
    {
        RecordTableRules.Get("sprk_project").MappingProtectedColumns.Should().BeEquivalentTo(
            ["sprk_projectnumber", "ownerid", "sprk_containerid", "sprk_createdbyperson", .. SecureRootAccessColumns]);
        RecordTableRules.Get("sprk_project").MappingProtectedColumns.Contains("Sprk_IsSecure").Should().BeTrue("the list was case-insensitive");
    }

    [Fact]
    public void MatterAndProjectMappingProtectedColumns_StayPerTable_NotMerged()
    {
        // Task 031: a merged set would newly skip a mapping rule targeting sprk_projectnumber on a MATTER create.
        RecordTableRules.Get("sprk_matter").MappingProtectedColumns.Should().NotContain("sprk_projectnumber");
        RecordTableRules.Get("sprk_project").MappingProtectedColumns.Should().NotContain("sprk_matternumber");
    }

    // ── #2220: the secure flag and access columns are never mapped, identically on every secure root ────────────────────

    /// <summary>The secure roots, from the registry that names them (<see cref="RecordCreatorPerson.StampedTables"/>), so a new one is included.</summary>
    public static TheoryData<string> SecureRoots()
    {
        var data = new TheoryData<string>();
        foreach (var root in RecordCreatorPerson.StampedTables.Order())
            data.Add(root);
        return data;
    }

    [Theory]
    [MemberData(nameof(SecureRoots))]
    public void UnifiedCreate_EverySecureRoot_ProtectsExactlyTheSameSecureFlagAndAccessColumnsFromMapping(string table)
    {
        // The set the unified service applies to a mapping rule (registry ∪ table-owned ∪ the secure-root additions).
        var protectedFromMapping = RecordCreationService.ProtectedFromMapping(table, RecordTableRules.Get(table), null);

        protectedFromMapping.Intersect(SecureRootAccessColumns, StringComparer.OrdinalIgnoreCase)
            .Should().BeEquivalentTo(SecureRootAccessColumns, $"{table}: the four columns are the BFF's own (#2220)");
    }

    [Fact]
    public void SecureRootAccessColumns_AreNotProtectedOnAnOrdinaryChild()
    {
        // Must not fire: a child's access columns (a document's sprk_accesspermission) stay fill-only, task 014.
        RecordCreationService.ProtectedFromMapping("sprk_document", RecordTableRules.Get("sprk_document"), null)
            .Should().NotContain("sprk_accesspermission").And.NotContain("sprk_issecure").And.NotContain("sprk_securitybu");
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
    [InlineData("sprk_budget", "sprk_budgetnumber")] // task 167 (D-193): BUD-{SEQNUM:6}
    public void AutonumberColumn_IsTheNumberingSchemaColumn_AndIsProtectedFromMapping(string table, string column)
    {
        var rule = RecordTableRules.Get(table);

        rule.AutonumberColumn.Should().Be(column);
        rule.MappingProtectedColumns.Should().Contain(column, "a mapped number would pre-empt the platform sequence (task 076)");
    }

    // Task 167 (D-193): the rule says the platform fills the number, so the solution source must declare the format on that very column.
    [Theory]
    [InlineData("sprk_Matter", "sprk_matternumber", "MAT-{SEQNUM:6}")]
    [InlineData("sprk_Project", "sprk_projectnumber", "PRJ-{SEQNUM:6}")]
    [InlineData("sprk_Budget", "sprk_budgetnumber", "BUD-{SEQNUM:6}")]
    public void AutonumberColumn_IsDeclaredWithItsSeqnumFormat_InTheSolutionSource(string folder, string column, string format)
    {
        var entityXml = System.Xml.Linq.XDocument.Load(Path.Combine(
            RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", folder, "Entity.xml"));

        var attribute = entityXml.Descendants("attribute")
            .Single(a => string.Equals((string?)a.Element("LogicalName"), column, StringComparison.Ordinal));

        ((string?)attribute.Element("AutoNumberFormat")).Should().Be(format);
        RecordTableRules.Get(folder.ToLowerInvariant()).AutonumberColumn.Should().Be(column);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
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
            .Should().BeEquivalentTo("sprk_matter", "sprk_project", "sprk_invoice", "sprk_billingevent", "sprk_document", "sprk_budget",
                // Task 017 (coordinator item 8): the other four invoice-family tables protect their core lookups too.
                "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_externalidentifier", "sprk_invoicecorrection");
    }

    // ── Task 017 (coordinator item 8): every invoice-family table's core lookups are mapping-protected ───────────────

    public static TheoryData<string> InvoiceFamilyTables() => new(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys);

    /// <summary>The guard's core lookups of <paramref name="table"/> that <paramref name="protectedColumns"/> leaves mappable.</summary>
    private static IReadOnlyList<string> UnprotectedCoreLookups(string table, IReadOnlySet<string> protectedColumns) =>
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Select(l => l.Column).Where(c => !protectedColumns.Contains(c)).ToArray();

    [Theory]
    [MemberData(nameof(InvoiceFamilyTables))]
    public void MappingProtectedColumns_EveryInvoiceFamilyTable_CoversEveryCoreLookupItsGuardReads(string table)
    {
        // A mapping that filled a SECOND core lookup would fail a valid create with invoice_family_multiple_core_records; the
        // protected set is derived from the guard, so it covers every core lookup the guard reads, on every family table.
        var rule = RecordTableRules.Find(table);
        rule.Should().NotBeNull($"{table} is an invoice-family table, so the registry declares it");
        UnprotectedCoreLookups(table, rule!.MappingProtectedColumns).Should().BeEmpty();
        rule.PreWriteRules.Should().Contain(RecordTableRules.InvoiceFamilyRule);
    }

    [Fact]
    public void MappingProtectedColumns_TheCoreLookupCheck_FiresOnASetMissingOne()
    {
        // Must fire: the check above finds a core lookup a protected set leaves mappable.
        var missingProject = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_regardingmatter", "sprk_regardingworkassignment" };
        UnprotectedCoreLookups("sprk_invoicesubmission", missingProject).Should().Equal("sprk_regardingproject");
        UnprotectedCoreLookups("sprk_invoice", new HashSet<string>()).Should()
            .BeEquivalentTo("sprk_matter", "sprk_project", "sprk_regardingworkassignment");
    }

    // ── Task 017 (#1608, decision 2b): one server-owned set on every secure root; a document's access record ─────────

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public void TableServerOwnedColumns_EverySecureRoot_RefusesTheSameThreeColumns(string table) =>
        RecordTableRules.Get(table).TableServerOwnedColumns.Should().BeEquivalentTo(
            "sprk_containerid", "sprk_securitybu", "sprk_accesspermission");

    [Fact]
    public void TableServerOwnedColumns_ADocument_RefusesItsAccessInheritance_NotItsAccessPermission()
    {
        var document = RecordTableRules.Get("sprk_document").TableServerOwnedColumns;
        document.Should().BeEquivalentTo(Sprk.Bff.Api.Services.Access.AccessInheritance.Column);
        document.Should().NotContain(InheritedAccessPermission.Column, "UAC-r2 D7: a caller may fill it at create");
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

    // ── Task 018a fix round (F4-1): the optional references ──────────────────────────────────────────────────────────

    [Fact]
    public void OptionalReferenceLookups_AreExactlyWhatTheOfficeCreateDropsToday()
    {
        // The deleted Office core's ApplyReferenceAsync (MatterTypeAttribute, MatterPracticeAreaAttribute,
        // ProjectTypeAttribute at 2a5f076857): literals, so the pin outlives task 018c's deletion of that service.
        RecordTableRules.Get("sprk_matter").OptionalReferenceLookups.Should().BeEquivalentTo("sprk_mattertype", "sprk_practicearea");
        RecordTableRules.Get("sprk_project").OptionalReferenceLookups.Should().BeEquivalentTo("sprk_projecttype_ref");
        RecordTableRules.All.Where(r => r.Table is not ("sprk_matter" or "sprk_project"))
            .Should().OnlyContain(r => r.OptionalReferenceLookups.Count == 0, "no other table declares a reference");
    }

    [Fact]
    public void OptionalReferenceLookups_NeverHoldAColumnThatIsNeverAReference()
    {
        var offending = RecordTableRules.All
            .SelectMany(r => r.OptionalReferenceLookups.Select(c => (r.Table, Column: c, Rule: r)))
            .Where(x => RecordCreationService.IsNeverAReference(x.Table, x.Rule, x.Column))
            .Select(x => $"{x.Table}.{x.Column}");

        offending.Should().BeEmpty(
            "a reference is dropped with a warning, so it may never be the for person, a column mapping never writes, the " +
            "regarding pair, a filing lookup, or an access-conferring or Assigned* column");
    }

    [Theory]
    [InlineData("sprk_event", "sprk_assignedto")] // the for person (and Assigned*)
    [InlineData("sprk_todo", "sprk_assignedto")]
    [InlineData("sprk_matter", "sprk_assignedtointernal")]
    [InlineData("sprk_project", "sprk_assignedtointernal")]
    [InlineData("sprk_workassignment", "sprk_assignedattorney1")] // Assigned*, not the for person
    [InlineData("sprk_project", "sprk_regardingrecordtype")] // the regarding pair
    [InlineData("sprk_project", "sprk_regardingrecordid")]
    [InlineData("sprk_matter", "ownerid")] // ProtectedFromMapping
    [InlineData("sprk_matter", "sprk_matternumber")]
    [InlineData("sprk_event", "sprk_regardingmatter")] // filing lookups
    [InlineData("sprk_workassignment", "sprk_regardingmatter")]
    [InlineData("sprk_document", "sprk_matter")]
    [InlineData("sprk_document", "sprk_accesspermission")] // access-conferring
    [InlineData("sprk_matter", "sprk_issecure")]
    [InlineData("sprk_event", "sprk_containerid")]
    public void IsNeverAReference_Fires(string table, string column)
    {
        RecordCreationService.IsNeverAReference(table, RecordTableRules.Get(table), column).Should().BeTrue();
        RecordCreationService.IsNeverAReference(table, RecordTableRules.Get(table), column.ToUpperInvariant()).Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_mattertype")]
    [InlineData("sprk_matter", "sprk_practicearea")]
    [InlineData("sprk_project", "sprk_projecttype_ref")]
    public void IsNeverAReference_DoesNotFire_OnTheDeclaredReferences(string table, string column) =>
        RecordCreationService.IsNeverAReference(table, RecordTableRules.Get(table), column).Should().BeFalse();

    // ── Task 037 (FR-36): the work assignment's Assigned To, copied from its own Assigned To Internal ────────────────

    private static readonly Guid Internal = Guid.Parse("a0370000-0000-4000-8000-0000000000c1");
    private static readonly Guid Chosen = Guid.Parse("a0370000-0000-4000-8000-0000000000c2");

    private static DataverseWriteItemMapper.MappedLookup ContactIn(string column, Guid id) => new(column, "contact", "contacts", id);

    private static DataverseWriteItemMapper.MappedItem Row(
        IReadOnlyList<DataverseWriteItemMapper.MappedLookup> lookups, params string[] otherColumns) =>
        new("{}", [.. otherColumns, .. lookups.Select(l => l.Column)]) { Lookups = lookups, ClearedColumns = [] };

    [Fact]
    public void CopiedDefault_IsDeclaredExactlyOnce_TheWorkAssignmentsAssignedTo_FromItsAssignedToInternal()
    {
        var copied = RecordTableRules.All
            .SelectMany(r => r.ServerDefaults.Where(d => d.CopiedFrom is not null).Select(d => (r.Table, d.Column, d.CopiedFrom)))
            .ToList();

        copied.Should().Equal([("sprk_workassignment", "sprk_assignedto", "sprk_assignedtointernal")],
            "FR-36 is declared once, on the work assignment's rule, and no other table copies a column");
    }

    /// <summary>
    /// Task 037 fix round 2 (UAC-r2 K3): the declaration rule a copied default must keep — its TARGET is never a filing lookup.
    /// A copy of a server-set source is server-set (no AppendTo), but RecordCreationService's "a server-set lookup is never a
    /// filing lookup" refusal runs BEFORE step 3½ extends the server-set set, and OwnedChildWrite has no such refusal: a copy
    /// onto a filing lookup would file the row under a record nobody asked about. Filing = the create paths' own predicate
    /// (<see cref="RecordCreationService.IsFilingLookup(string, string)"/>: lineage, ownership-parent and secure-root filing
    /// columns), or a column whose referenced table is an ownership parent
    /// (<see cref="RecordReparent.FilingParentOf"/> → <see cref="RecordOwnershipResolver.IsOwnershipParent"/>).
    /// </summary>
    private static string? CopiedOntoAFilingLookup(string table, ServerDefault copied)
    {
        if (copied.CopiedFrom is null)
            return null;
        var target = copied.Column;
        return RecordCreationService.IsFilingLookup(table, target)
               || RecordOwnershipResolver.IsOwnershipParent(RecordReparent.FilingParentOf(table, target))
            ? target
            : null;
    }

    [Fact]
    public void CopiedDefault_NoDeclaredCopyTargetsAFilingLookup()
    {
        RecordTableRules.All
            .SelectMany(r => r.ServerDefaults.Select(d => (r.Table, Target: CopiedOntoAFilingLookup(r.Table, d))))
            .Where(c => c.Target is not null)
            .Should().BeEmpty("a copied default (server-set when its source is) must never be a lookup that files the row");
    }

    [Fact]
    public void CopiedDefault_FilingTargetGuard_MustNotFire_OnTheRealAssignedToRule()
    {
        var real = RecordTableRules.Get("sprk_workassignment").ServerDefaults.Single(d => d.CopiedFrom is not null);

        CopiedOntoAFilingLookup("sprk_workassignment", real).Should().BeNull("sprk_assignedto is a person, not a filing lookup");
    }

    [Theory]
    [InlineData("sprk_workassignment", "sprk_regardingmatter")] // a secure root's filing column
    [InlineData("sprk_todo", "sprk_regardingmatter")]           // a child's lineage lookup to an ownership parent
    public void CopiedDefault_FilingTargetGuard_MustFire_OnASyntheticCopyOntoAFilingLookup(string table, string target)
    {
        var synthetic = new ServerDefault(target, "a synthetic control", "copied onto a filing lookup")
        {
            CopiedFrom = "sprk_assignedtointernal",
        };

        CopiedOntoAFilingLookup(table, synthetic).Should().Be(target);
    }

    [Fact]
    public void CopiedDefaultsFor_AssignedToUnset_FillsItWithTheBoundAssignedToInternal_AsIs()
    {
        var fill = RecordTableRules.CopiedDefaultsFor(
                RecordTableRules.Get("sprk_workassignment"), Row([ContactIn("sprk_assignedtointernal", Internal)], "sprk_name"))
            .Should().ContainSingle().Subject;

        fill.Default.Column.Should().Be("sprk_assignedto");
        fill.Source.Should().Be(ContactIn("sprk_assignedtointernal", Internal), "the reference is copied unchanged (contact → contact)");
    }

    [Fact]
    public void CopiedDefaultsFor_ASuppliedAssignedTo_FillsNothing()
    {
        RecordTableRules.CopiedDefaultsFor(RecordTableRules.Get("sprk_workassignment"),
                Row([ContactIn("sprk_assignedtointernal", Internal), ContactIn("sprk_assignedto", Chosen)]))
            .Should().BeEmpty("the caller's value wins");
    }

    [Fact]
    public void CopiedDefaultsFor_AnExplicitNullAssignedTo_FillsNothing_UnsetIsAbsentOnly()
    {
        var cleared = new DataverseWriteItemMapper.MappedItem("{}", ["sprk_assignedtointernal", "sprk_assignedto"])
        {
            Lookups = [ContactIn("sprk_assignedtointernal", Internal)],
            ClearedColumns = ["sprk_assignedto"],
        };

        RecordTableRules.CopiedDefaultsFor(RecordTableRules.Get("sprk_workassignment"), cleared)
            .Should().BeEmpty("an explicit null is the caller's choice, not an unset column");
    }

    [Fact]
    public void CopiedDefaultsFor_AssignedToInternalUnsetOrCleared_NamesTheFill_WithNoSource()
    {
        var unset = Row([], "sprk_name");
        var cleared = new DataverseWriteItemMapper.MappedItem("{}", ["sprk_assignedtointernal"]) { ClearedColumns = ["sprk_assignedtointernal"] };

        foreach (var row in new[] { unset, cleared })
        {
            var fill = RecordTableRules.CopiedDefaultsFor(RecordTableRules.Get("sprk_workassignment"), row).Should().ContainSingle().Subject;
            fill.Default.Column.Should().Be("sprk_assignedto");
            fill.Source.Should().BeNull("no source: the column stays blank (the create paths log a warning)");
        }
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_notaregisteredtable")]
    public void CopiedDefaultsFor_AnyOtherTable_FillsNothing(string table)
    {
        RecordTableRules.CopiedDefaultsFor(RecordTableRules.Find(table), Row([ContactIn("sprk_assignedtointernal", Internal)]))
            .Should().BeEmpty();
    }
}
