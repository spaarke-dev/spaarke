using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 022b — the Restricted-source rule's pure parts: which tables carry
/// <c>sprk_accesspermission</c> (from the code's own metadata), what a source's value says, what a row's own payload value is,
/// and the three states of what a new row's parents give it. The rule through the real service is the seam suite's
/// (<c>RecordCreationServiceMappingSeamTests.RestrictedSource.cs</c>).
/// </summary>
public class RestrictedMappingSourceTests
{
    private const string Matter = "sprk_matter";
    private static readonly Guid Id = Guid.Parse("022b0000-0000-4000-8000-000000000001");

    // ── The source predicate: which tables carry the column ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_communication")]
    [InlineData("sprk_document")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public void TheInheritingTablesAndTheSecureRoots_CarryTheColumn(string table)
    {
        InheritedAccessPermission.CarriesColumn(table).Should().BeTrue();
        InheritedAccessPermission.TablesCarryingColumn().Should().Contain(table);
        RecordCreationService.MappingSourceAccessColumns(table).Should().Equal(InheritedAccessPermission.Column);
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_budget")]
    [InlineData("contact")]
    [InlineData(null)]
    public void AnyOtherTable_DoesNotCarryIt_AndIsNeverReadForIt(string? table)
    {
        InheritedAccessPermission.CarriesColumn(table).Should().BeFalse();
        InheritedAccessPermission.SourceCarriesColumn(table).Should().BeFalse();
        if (table is not null)
            RecordCreationService.MappingSourceAccessColumns(table).Should().BeEmpty();
    }

    /// <summary>UAC-r2 (2026-11-12): the agreement is read as a mapping SOURCE only — no inheritance, no enforcement, and a
    /// new agreement is never Restricted.</summary>
    [Fact]
    public void TheAgreement_IsASourceOnlyTable()
    {
        const string agreement = "sprk_agreement";

        InheritedAccessPermission.SourceCarriesColumn(agreement).Should().BeTrue();
        InheritedAccessPermission.SourceTablesCarryingColumn().Should().Contain(agreement);
        RecordCreationService.MappingSourceAccessColumns(agreement).Should().Equal(InheritedAccessPermission.Column);
        RecordCreationService.MappingSourceAccessOf(agreement,
                new Entity(agreement, Id) { [InheritedAccessPermission.Column] = new OptionSetValue(InheritedAccessPermission.Restricted) })
            .Should().Be(RecordCreationService.MappingSourceAccess.Restricted);
        RecordCreationService.MappingSourceAccessOf(agreement, null).Should().Be(RecordCreationService.MappingSourceAccess.Unreadable);

        InheritedAccessPermission.CarriesColumn(agreement).Should().BeFalse("a new agreement is never Restricted");
        InheritedAccessPermission.TablesCarryingColumn().Should().NotContain(agreement);
        InheritedAccessPermission.AppliesTo(agreement).Should().BeFalse("no inheritance");
        InheritedAccessPermission.SecureRootColumns.Should().NotContainKey(agreement, "no enforcement");
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_matter")]
    public void EveryTargetTable_IsAlsoASourceTable(string table) =>
        InheritedAccessPermission.SourceCarriesColumn(table).Should().BeTrue();

    // ── What a source's value says ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InheritedAccessPermission.Restricted, "Restricted")]
    [InlineData(InheritedAccessPermission.Standard, "NotRestricted")]
    [InlineData(InheritedAccessPermission.Limited, "NotRestricted")]
    [InlineData(5, "Unreadable")] // must fire: no known level is never ranked by guess
    public void ASourcesOptionValue_IsReadAsItsLevel(int value, string expected)
    {
        var read = new Entity(Matter, Id) { [InheritedAccessPermission.Column] = new OptionSetValue(value) };

        RecordCreationService.MappingSourceAccessOf(Matter, read).ToString().Should().Be(expected);
    }

    [Fact]
    public void ASourceRowWithoutTheValue_CountsAsStandard_TheWalksOwnRule()
    {
        RecordCreationService.MappingSourceAccessOf(Matter, new Entity(Matter, Id)).Should()
            .Be(RecordCreationService.MappingSourceAccess.NotRestricted);
        RecordCreationService.MappingSourceAccessOf(Matter, new Entity(Matter, Id) { [InheritedAccessPermission.Column] = null })
            .Should().Be(RecordCreationService.MappingSourceAccess.NotRestricted);
    }

    [Fact]
    public void NoRead_OrAValueOfAnotherType_IsUnreadable_FailClosed()
    {
        RecordCreationService.MappingSourceAccessOf(Matter, null).Should().Be(RecordCreationService.MappingSourceAccess.Unreadable);
        RecordCreationService.MappingSourceAccessOf(Matter, new Entity(Matter, Id) { [InheritedAccessPermission.Column] = "Restricted" })
            .Should().Be(RecordCreationService.MappingSourceAccess.Unreadable);
    }

    [Fact]
    public void ASourceTableWithoutTheColumn_IsNoColumn_WhateverItsRowHolds()
    {
        var read = new Entity("sprk_invoice", Id) { [InheritedAccessPermission.Column] = new OptionSetValue(InheritedAccessPermission.Restricted) };

        RecordCreationService.MappingSourceAccessOf("sprk_invoice", read).Should().Be(RecordCreationService.MappingSourceAccess.NoColumn);
        RecordCreationService.MappingSourceAccessOf("sprk_invoice", null).Should().Be(RecordCreationService.MappingSourceAccess.NoColumn);
    }

    // ── The row's own payload value ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{}", true, null)]
    [InlineData("{\"sprk_name\":\"x\"}", true, null)]
    [InlineData("{\"sprk_accesspermission\":null}", true, null)]
    [InlineData("{\"sprk_accesspermission\":100000002}", true, 100000002)]
    [InlineData("{\"SPRK_AccessPermission\":100000001}", true, 100000001)]
    [InlineData("{\"sprk_accesspermission\":\"100000002\"}", false, null)] // a string is not a level: not readable
    [InlineData("{\"sprk_accesspermission\":1.5}", false, null)]
    public void TheRowsOwnValue_IsReadFromItsPayload(string body, bool readable, int? value)
    {
        RecordCreationService.OwnAccessPermissionOf(new DataverseWriteItemMapper.MappedItem(body, []))
            .Should().Be((readable, value));
    }

    // ── The three states of what a new row's parents give it ──────────────────────────────────────────────────────────

    [Fact]
    public void NoParent_IsNoRestrictedAncestor()
    {
        var answer = InheritedAccessPermission.Answer.NoParent;

        answer.Status.Should().Be(ParentTopsStatus.NoParent);
        answer.NoRestrictedAncestor.Should().BeTrue();
        answer.Unknown.Should().BeFalse();
    }

    [Theory]
    [InlineData(InheritedAccessPermission.Standard, true)]
    [InlineData(InheritedAccessPermission.Limited, true)]
    [InlineData(InheritedAccessPermission.Restricted, false)]
    public void AFoundValue_IsTheValue(int value, bool noRestrictedAncestor)
    {
        var answer = new InheritedAccessPermission.Answer(ParentTopsStatus.Found, value, [], null);

        answer.NoRestrictedAncestor.Should().Be(noRestrictedAncestor);
        answer.Unknown.Should().BeFalse();
    }

    [Fact]
    public void AnUndeterminedAnswer_OrAFoundAnswerWithNoValue_IsUnknown_NeverNoRestrictedAncestor()
    {
        var undetermined = InheritedAccessPermission.Answer.Undetermined("a parent could not be read");
        var empty = new InheritedAccessPermission.Answer(ParentTopsStatus.Found, null, [], null);

        undetermined.Status.Should().Be(ParentTopsStatus.Undetermined);
        undetermined.Reason.Should().Be("a parent could not be read");
        undetermined.Unknown.Should().BeTrue();
        undetermined.NoRestrictedAncestor.Should().BeFalse();
        empty.Unknown.Should().BeTrue();
        empty.NoRestrictedAncestor.Should().BeFalse();
    }
}
