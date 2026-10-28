using System;
using System.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using EdgeKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.EdgeKind;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 070 (FR-29) — the row shape and the exclusions of <see cref="RecordFamilyEdges"/>.
/// Parity with the existing registries, the UAC-r2 content pin and the Filing-only isolation views are
/// <c>tests/Spaarke.ArchTests/RecordFamilyEdgesGuardTests</c>.
/// KEEP path: tests/unit/domain/** (ADR-038 §2 — pure static data; no I/O, no mocks).
/// </summary>
public class RecordFamilyEdgesTests
{
    [Fact]
    public void All_EveryRow_IsFamilyKindFiling()
    {
        RecordFamilyEdges.All.Should().NotBeEmpty();
        RecordFamilyEdges.All.Should().OnlyContain(e => e.FamilyKind == FamilyKind.Filing);
    }

    [Fact]
    public void All_EachEdge_IsListedOnceAndALookupNamesOneParent()
    {
        RecordFamilyEdges.All
            .Select(e => $"{e.ChildTable}.{e.LookupColumn}->{e.ParentTable}".ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
        RecordFamilyEdges.All
            .Where(e => e.EdgeKind != EdgeKind.Pair)
            .Select(e => $"{e.ChildTable}.{e.LookupColumn}".ToLowerInvariant())
            .Should().OnlyHaveUniqueItems("a typed lookup targets one table; only the pair names several");
    }

    [Fact]
    public void EdgeKind_FollowsTheTableAndTheColumn_AgainstCoreAncestorResolver()
    {
        // The core tables and the four stamp columns are CoreAncestorResolver's, read here rather than restated.
        var stampCore = CoreAncestorResolver.CoreAncestorLookups
            .ToDictionary(l => l.LookupAttribute, l => l.EntityType, StringComparer.OrdinalIgnoreCase);

        foreach (var e in RecordFamilyEdges.All)
        {
            var expected = CoreAncestorResolver.IsCoreRecordEntity(e.ChildTable)
                ? e.LookupColumn == RecordFamilyEdges.PairIdColumn ? EdgeKind.Pair : EdgeKind.FilingCore
                : stampCore.ContainsKey(e.LookupColumn) ? EdgeKind.Stamp : EdgeKind.LineageHop;

            e.EdgeKind.Should().Be(expected, $"{e.ChildTable}.{e.LookupColumn}");
            if (e.EdgeKind == EdgeKind.Stamp)
            {
                e.ParentTable.Should().Be(stampCore[e.LookupColumn], $"a stamp names its own core ({e.ChildTable}.{e.LookupColumn})");
            }
        }
    }

    [Fact]
    public void InSecureFamily_IsFalseOnlyForAServiceRequestParent()
    {
        RecordFamilyEdges.All.Should().OnlyContain(e =>
            e.InSecureFamily != string.Equals(e.ParentTable, "sprk_servicerequest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatterToProject_IsTheRegardingPair_NeverTheAssociation()
    {
        RecordFamilyEdges.All.Should().ContainSingle(e =>
            e.ChildTable == "sprk_project" && e.ParentTable == "sprk_matter"
            && e.LookupColumn == RecordFamilyEdges.PairIdColumn && e.EdgeKind == EdgeKind.Pair);
        RecordFamilyEdges.PairTypeColumn.Should().Be(CoreAncestorResolver.RegardingRecordTypeColumn);
    }

    [Theory]
    [InlineData("sprk_signal", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_signal", "sprk_decisionrecord", "sprk_decisionrecord")]
    [InlineData("sprk_signal", "sprk_regardingcommunication", "sprk_communication")]
    [InlineData("sprk_signal", "sprk_regardingdocument", "sprk_document")]
    [InlineData("sprk_signal", "sprk_regardingevent", "sprk_event")]
    [InlineData("sprk_signal", "sprk_regardinginvoice", "sprk_invoice")]
    [InlineData("sprk_signal", "sprk_regardingmatter", "sprk_matter")]
    [InlineData("sprk_signal", "sprk_regardingproject", "sprk_project")]
    [InlineData("sprk_signal", "sprk_regardingtodo", "sprk_todo")]
    [InlineData("sprk_signal", "sprk_regardingworkassignment", "sprk_workassignment")]
    [InlineData("sprk_decisionrecord", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_decisionrecord", "sprk_project", "sprk_project")]
    [InlineData("sprk_decisionrecord", "sprk_workassignment", "sprk_workassignment")]
    [InlineData("sprk_decisionrecord", "sprk_regardingdocument", "sprk_document")]
    [InlineData("sprk_budgetrevision", "sprk_budget", "sprk_budget")]
    [InlineData("sprk_budgetrevision", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_budgetbucket", "sprk_budget", "sprk_budget")]
    [InlineData("sprk_policy", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_policyversion", "sprk_policy", "sprk_policy")]
    public void All_OntologyAndUac194Edges_ArePresent(string child, string column, string parent)
    {
        RecordFamilyEdges.All.Should().ContainSingle(e =>
            e.ChildTable == child && e.LookupColumn == column && e.ParentTable == parent && e.InSecureFamily);
    }

    [Theory]
    [InlineData("sprk_policy", "sprk_currentversion")] // a loop: the rule's own version
    [InlineData("sprk_signal", "sprk_policy")] // a citation, not a filing
    [InlineData("sprk_decisionrecord", "sprk_policyversion")] // a citation, not a filing
    public void All_ExcludedLookups_AreAbsent(string child, string column)
    {
        RecordFamilyEdges.All.Should().NotContain(e => e.ChildTable == child && e.LookupColumn == column);
    }

    [Theory]
    [InlineData("sprk_procedureassignment")] // configuration about the record (owner, 2026-10-27)
    [InlineData("sprk_invoicelineitem")] // until ebilling deploys it (#2348)
    [InlineData("sprk_externalrecordaccess")] // a grant row
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("sprk_organization")]
    [InlineData("systemuser")]
    [InlineData("team")]
    [InlineData("sprk_timekeeper")]
    public void All_ExcludedTables_AreNeitherChildNorParent(string table)
    {
        RecordFamilyEdges.All.Should().NotContain(e => e.ChildTable == table || e.ParentTable == table);
    }

    [Fact]
    public void All_NoAssociationOrPeopleLookup_IsAnEdge()
    {
        // N:N associations (sprk_Project_Matter_nn and the like) are relationships, not lookups; people lookups
        // (assigned*, attorneys, the law firm) name people and organizations.
        RecordFamilyEdges.All.Should().NotContain(e =>
            e.LookupColumn.EndsWith("_nn", StringComparison.OrdinalIgnoreCase)
            || e.LookupColumn.Contains("assigned", StringComparison.OrdinalIgnoreCase)
            || e.LookupColumn.Contains("attorney", StringComparison.OrdinalIgnoreCase)
            || e.LookupColumn.Contains("lawfirm", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_matternumber")]
    [InlineData("sprk_project", "sprk_projectnumber")]
    [InlineData("sprk_workassignment", "sprk_workassignmentnumber")]
    [InlineData("sprk_todo", null)]
    public void RecordNumberColumnOf_KnownTables_ReturnsTheirNumberColumnOrNull(string table, string? expected)
    {
        RecordFamilyEdges.RecordNumberColumnOf(table).Should().Be(expected);
    }

    [Fact]
    public void All_EachRow_CarriesItsChildTablesRecordNumberColumn()
    {
        RecordFamilyEdges.All.Should().OnlyContain(e =>
            e.RecordNumberColumn == RecordFamilyEdges.RecordNumberColumnOf(e.ChildTable));
    }
}
