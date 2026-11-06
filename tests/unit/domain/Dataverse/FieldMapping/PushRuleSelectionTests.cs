using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (FR-23, FR-34) — which rules of a profile a push applies: Copy rules set to
/// Manual Refresh, with the Assigned-To columns set apart (added only when asked for). The rest are counted, never an error.
/// </summary>
public class PushRuleSelectionTests
{
    private const string Target = "sprk_project";

    private static FieldMappingRuleEntity Rule(string target, int mappingType = CreateTimeFieldMapping.MappingTypeCopy, int syncMode = SyncMode.ManualRefresh) => new()
    {
        Id = Guid.NewGuid(), Name = target, SourceField = "sprk_source", TargetField = target, MappingType = mappingType, SyncMode = syncMode, IsActive = true,
    };

    private static readonly Func<string, string, bool> AssignedTo =
        (table, column) => table == Target && column.StartsWith("sprk_assigned", StringComparison.Ordinal);

    [Fact]
    public void ACopyRuleAtManualRefresh_IsApplied_AndEveryOtherRuleIsCountedNotApplied()
    {
        var applied = Rule("sprk_a");
        var oneTime = Rule("sprk_b", syncMode: SyncMode.OneTime);
        var asDefault = Rule("sprk_c", CreateTimeFieldMapping.MappingTypeDefault);
        var concat = Rule("sprk_d", CreateTimeFieldMapping.MappingTypeConcat);
        var template = Rule("sprk_e", CreateTimeFieldMapping.MappingTypeTemplate);

        var selected = PushRuleSelection.Select([applied, oneTime, asDefault, concat, template], Target, includeAssignedTo: false, AssignedTo);

        selected.Rules.Should().Equal(applied);
        selected.SkippedCount.Should().Be(4);
    }

    [Fact]
    public void ARuleLeftWithoutASyncMode_ReadsAsOneTime_AndIsNotPushed()
    {
        var untouched = new FieldMappingRuleEntity { Name = "r", SourceField = "s", TargetField = "t", IsActive = true };

        var selected = PushRuleSelection.Select([untouched], Target, includeAssignedTo: false, null);

        selected.Rules.Should().BeEmpty();
        selected.SkippedCount.Should().Be(1);
    }

    [Fact]
    public void AnAssignedToRule_IsLeftOutOfAnOrdinaryPush_WhateverItsSyncMode()
    {
        var ordinary = Rule("sprk_a");
        var assigned = Rule("sprk_assignedattorney", syncMode: SyncMode.ManualRefresh);

        var selected = PushRuleSelection.Select([ordinary, assigned], Target, includeAssignedTo: false, AssignedTo);

        selected.Rules.Should().Equal(ordinary);
        selected.SkippedCount.Should().Be(1);
    }

    [Fact]
    public void WhenAskedFor_TheAssignedToCopyRulesAreAddedToTheOrdinaryOnes_WhateverTheirSyncMode()
    {
        var ordinary = Rule("sprk_a");
        var assignedOneTime = Rule("sprk_assignedattorney", syncMode: SyncMode.OneTime);
        var assignedDefault = Rule("sprk_assignedparalegal", CreateTimeFieldMapping.MappingTypeDefault);

        var selected = PushRuleSelection.Select([ordinary, assignedOneTime, assignedDefault], Target, includeAssignedTo: true, AssignedTo);

        selected.Rules.Should().Equal(ordinary, assignedOneTime);
        selected.SkippedCount.Should().Be(1, "an Assigned-To rule that is not a Copy rule is still not applied");
    }

    [Fact]
    public void TheAssignedToClassIsTheRegistrys_AColumnOnAnotherTable_IsOrdinary()
    {
        var sameName = Rule("sprk_assignedattorney");

        var selected = PushRuleSelection.Select([sameName], "sprk_event", includeAssignedTo: false, AssignedTo);

        selected.Rules.Should().ContainSingle().Which.Should().BeSameAs(sameName, "the registry says the column is Assigned-To on the project only");
    }

    // ── the columns a push may write at all (shared by the apply loop's refusals and the preview) ──

    [Theory]
    [InlineData("sprk_signal", "sprk_name")]
    [InlineData("sprk_decisionrecord", "sprk_name")]
    public void AColumnOfAWriterOnlyTable_IsNotWritable(string table, string column)
        => FieldMappingEndpoints.PushMayWrite(table, column).Should().BeFalse();

    [Theory]
    [InlineData("sprk_document", "sprk_accesspermission")]
    [InlineData("sprk_project", "ownerid")]
    [InlineData("sprk_project", "sprk_createdbyperson")]
    public void AColumnSpaarkeSets_IsNotWritable(string table, string column)
        => FieldMappingEndpoints.PushMayWrite(table, column).Should().BeFalse();

    [Fact]
    public void AnOrdinaryColumn_IsWritable_AndAMatterStillTakesItsOwnAccessPermission()
    {
        FieldMappingEndpoints.PushMayWrite("sprk_event", "sprk_clientreference").Should().BeTrue();
        FieldMappingEndpoints.PushMayWrite("sprk_matter", "sprk_accesspermission").Should().BeTrue(
            "the push runs as its caller, who may edit a matter's access permission through the form anyway");
    }
}
