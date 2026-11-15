using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
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
    }

    /// <summary>Review K2: a mapped value with no source read is checked as not told before the table is looked at — even a
    /// table without the column — and an unknown source table (never the target's) is the same.</summary>
    [Fact]
    public void NoRead_IsUnreadable_BeforeTheTableIsLookedAt_AndAnUnknownSourceTableToo()
    {
        RecordCreationService.MappingSourceAccessOf("sprk_invoice", null).Should().Be(RecordCreationService.MappingSourceAccess.Unreadable);
        RecordCreationService.MappingSourceAccessOf(null, null).Should().Be(RecordCreationService.MappingSourceAccess.Unreadable);
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

    /// <summary>Review F2: a found value that is no known level is Unknown, never "no Restricted ancestor".</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(100000099)]
    public void AFoundAnswerWithAnUnknownLevel_IsUnknown_NeverNoRestrictedAncestor(int value)
    {
        var answer = new InheritedAccessPermission.Answer(ParentTopsStatus.Found, value, [], null);

        answer.Unknown.Should().BeTrue();
        answer.NoRestrictedAncestor.Should().BeFalse();
    }

    // ── Review F2: a To Do's, Event's or Communication's answer, from the three states ───────────────────────────────

    [Fact]
    public void AChild_UnderAnUnknownAnswer_WritesNothing_AndIsNeverRestricted()
    {
        foreach (var answer in new[]
                 {
                     InheritedAccessPermission.Answer.Undetermined("a parent could not be read"),
                     new InheritedAccessPermission.Answer(ParentTopsStatus.Found, null, [], null),
                     new InheritedAccessPermission.Answer(ParentTopsStatus.Found, 100000099, [], null),
                 })
        {
            var access = RecordCreationService.ChildAccessPermission(answer, ownReadable: true, ownRestricted: true);

            access.Write.Should().BeNull("a value that is no known level is never written");
            access.EndsRestricted.Should().BeFalse();
            access.Undetermined.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(InheritedAccessPermission.Standard, false)]
    [InlineData(InheritedAccessPermission.Limited, false)]
    [InlineData(InheritedAccessPermission.Restricted, true)]
    public void AChild_UnderAKnownLevel_WritesIt(int level, bool endsRestricted)
    {
        var access = RecordCreationService.ChildAccessPermission(
            new InheritedAccessPermission.Answer(ParentTopsStatus.Found, level, [], null), true, false);

        access.Write.Should().Be(level);
        access.EndsRestricted.Should().Be(endsRestricted);
        access.Undetermined.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AParentlessChild_KeepsItsOwnValue(bool ownRestricted, bool endsRestricted)
    {
        var access = RecordCreationService.ChildAccessPermission(InheritedAccessPermission.Answer.NoParent, true, ownRestricted);

        access.Write.Should().BeNull();
        access.EndsRestricted.Should().Be(endsRestricted);
        access.Undetermined.Should().BeFalse();
    }

    // ── UAC-r2 Q2: a project's or work assignment's answer, by the secure-root plan's floor ───────────────────────────

    private static SecureFilingParent AMatter => new(Matter, Id, "A matter");

    [Fact]
    public void ThePlansFloor_IsTheStrictestValueItRead_WhenEveryParentWasRead()
    {
        var restricted = new SecureParentsAnswer([], null)
        {
            StrictestPermission = new FilingPermission(InheritedAccessPermission.Restricted, AMatter),
        };
        var standard = new SecureParentsAnswer([], null);

        AccessPermissionFloor.Of(restricted).Should().Be(new AccessPermissionFloor(InheritedAccessPermission.Restricted, true));
        AccessPermissionFloor.Of(restricted).IsRestricted.Should().BeTrue();
        AccessPermissionFloor.Of(standard).Should().Be(new AccessPermissionFloor(null, true));
        AccessPermissionFloor.Of(standard).IsRestricted.Should().BeFalse();
    }

    [Fact]
    public void ThePlansFloor_IsUndecided_WhenAParentCouldNotBeRead_EvenWithARestrictedValueBeside_It()
    {
        var unread = new SecureParentsAnswer([], "whether the sprk_matter it is filed under is secure could not be read")
        {
            StrictestPermission = new FilingPermission(InheritedAccessPermission.Restricted, AMatter),
        };

        AccessPermissionFloor.Of(unread).Should().Be(AccessPermissionFloor.Undecided);
        AccessPermissionFloor.Of(unread).IsRestricted.Should().BeFalse();
    }

    [Fact]
    public void AnOrdinaryPlan_FilesUnderNothing_AndARefusedPlan_IsUndecided()
    {
        SecureRootCreatePlan.Ordinary.AccessPermissionFloor.Should().Be(AccessPermissionFloor.NoParent);
        SecureRootCreatePlan.Refused(RecordOwnerResolution.Refused("code", "words"))
            .AccessPermissionFloor.Should().Be(AccessPermissionFloor.Undecided);
    }

    [Fact]
    public void ARoot_UnderARestrictedFloor_EndsRestricted_AndWritesNothing()
    {
        var access = RecordCreationService.RootAccessPermission(
            ownReadable: true, ownRestricted: false, new AccessPermissionFloor(InheritedAccessPermission.Restricted, true));

        access.EndsRestricted.Should().BeTrue();
        access.Undetermined.Should().BeFalse();
        access.Write.Should().BeNull("the cascade owns a project's stored value");
        access.Record.Should().BeNull("the access record is the cascade's alone");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(InheritedAccessPermission.Standard)]
    [InlineData(InheritedAccessPermission.Limited)]
    public void ARoot_UnderADecidedFloorThatIsNotRestricted_IsNotRestricted(int? floor)
    {
        var access = RecordCreationService.RootAccessPermission(true, false, new AccessPermissionFloor(floor, true));

        access.EndsRestricted.Should().BeFalse();
        access.Undetermined.Should().BeFalse();
    }

    [Fact]
    public void ARoot_UnderAnUndecidedFloor_OrNoPlan_IsNeverRestricted_AndIsUndetermined()
    {
        foreach (var floor in new[] { AccessPermissionFloor.Undecided, null })
        {
            var access = RecordCreationService.RootAccessPermission(true, false, floor);

            access.EndsRestricted.Should().BeFalse();
            access.Undetermined.Should().BeTrue();
        }
    }

    [Fact]
    public void ARoot_WhoseOwnValueIsRestricted_EndsRestricted_WhateverTheFloor()
    {
        RecordCreationService.RootAccessPermission(true, true, AccessPermissionFloor.Undecided).EndsRestricted.Should().BeTrue();
        RecordCreationService.RootAccessPermission(true, true, AccessPermissionFloor.NoParent).EndsRestricted.Should().BeTrue();
    }

    /// <summary>What the create relies on: once the cascade follows a new project's parents (after the create, outside the
    /// create request), a Restricted parent raises its stored value to Restricted.</summary>
    [Fact]
    public void TheCascade_RaisesANewProjectUnderARestrictedParent_ToRestricted()
    {
        var ancestry = new SecureParentsAnswer([], null)
        {
            DirectParents = [new FilingParentState(AMatter, EffectiveSecure: false, EffectiveRank: 2)],
        };

        var decided = SecureRootInheritance.Decide(storedSecure: false, storedPermission: null, markerText: null, ancestry);

        decided.Stop.Should().BeNull();
        decided.TargetPermission.Should().Be(InheritedAccessPermission.Restricted);
        decided.Tighten.Should().BeTrue();
    }
}
