using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Ai.Membership;
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
        Id = Guid.NewGuid(),
        Name = target,
        SourceField = "sprk_source",
        TargetField = target,
        MappingType = mappingType,
        SyncMode = syncMode,
        IsActive = true,
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

        var selected = PushRuleSelection.Select([untouched], Target, includeAssignedTo: false, AssignedTo);

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

    // ───────────────────── the one classifier (task 040 fix round 1) ─────────────────────

    /// <summary>Every table of the registry, not only the three roots: an event, a to-do, an analysis.</summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_assignedattorney1")]
    [InlineData("sprk_project", "sprk_assignedlawfirm1")]
    [InlineData("sprk_workassignment", "sprk_assignedto")]
    [InlineData("sprk_event", "sprk_assignedattorney1")]
    [InlineData("sprk_event", "sprk_assignedto2")]
    [InlineData("sprk_todo", "sprk_assignedto")]
    [InlineData("sprk_analysis", "sprk_assignedparalegal2")]
    [InlineData("SPRK_EVENT", " sprk_AssignedAttorney1 ")]
    [InlineData("sprk_event", "sprk_assignedattorney1@odata.bind")]
    public void TheClassifier_ReadsTheRegistryForAnyTable(string table, string column)
    {
        PushRuleSelection.AssignedToColumnsOf(Bound())(table, column).Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_event", "sprk_clientreference")]
    [InlineData("sprk_reportcard", "sprk_assignedattorney1")] // the registry lists no report card column
    [InlineData("sprk_invoice", "sprk_assignedattorney1")]    // the invoice's columns are named sprk_assignedto*
    [InlineData("sprk_invoice", "sprk_assignedtoattorney1")]  // task 032b-1: a display copy, not Assigned-To (U3 (c))
    [InlineData("sprk_invoice", "sprk_assignedto1")]          // task 032b-1: sprk_invoice has no registry entry
    public void TheClassifier_AnswersNo_ForAColumnTheRegistryDoesNotList(string table, string column)
    {
        PushRuleSelection.AssignedToColumnsOf(Bound())(table, column).Should().BeFalse();
    }

    [Fact]
    public void AnOperatorRegistry_IsTheAnswer_NotTheCanonicalSeed()
    {
        var registry = new AccessConferringRegistry();
        registry.Entities["sprk_event"] = [new AccessConferringColumn { Field = "sprk_custom", IdentityType = "Contact" }];

        var classify = PushRuleSelection.AssignedToColumnsOf(registry);

        classify("sprk_event", "sprk_custom").Should().BeTrue();
        classify("sprk_event", "sprk_assignedattorney1").Should().BeFalse("an operator registry replaces the seed cleanly");
    }

    [Fact]
    public void AnUnboundRegistry_ReadsAsTheCanonicalOne_NeverAsNone()
    {
        PushRuleSelection.AssignedToColumnsOf(new AccessConferringRegistry())("sprk_event", "sprk_assignedattorney1").Should().BeTrue();
        PushRuleSelection.AssignedToColumnsOf(null)("sprk_todo", "sprk_assignedto").Should().BeTrue();
    }

    [Fact]
    public void AnEntryWithAnUnknownIdentityType_StillCounts_SoAColumnIsNeverPlainDataByAccident()
    {
        var registry = new AccessConferringRegistry();
        registry.Entities["sprk_event"] = [new AccessConferringColumn { Field = "sprk_assignedx", IdentityType = "Team" }];

        PushRuleSelection.AssignedToColumnsOf(registry)("sprk_event", "sprk_assignedx").Should().BeTrue();
    }

    // ───────────── display copies (task 032b-1; U3 (c): the invoice copies confer nothing and are never re-pushed) ─────────────

    private const string Invoice = "sprk_invoice";

    /// <summary>
    /// Must-fire: a Copy rule onto an invoice display copy is not pushed — not at Manual Refresh in an ordinary push, not when
    /// the request asks for the Assigned-To rules — and it is counted with the skipped rules, so the preview says so.
    /// </summary>
    [Theory]
    [InlineData("sprk_assignedtoattorney1", false)]
    [InlineData("sprk_assignedtoattorney2", true)]
    [InlineData("sprk_assignedtoparalegal1", false)]
    [InlineData("SPRK_AssignedToParalegal2@odata.bind", true)]
    public void ADisplayCopyRule_IsNeverPushed_WhateverItsSyncMode_AndIsCounted(string column, bool includeAssignedTo)
    {
        var plain = Rule("sprk_description");
        var manualRefresh = Rule(column, syncMode: SyncMode.ManualRefresh);
        var oneTime = Rule(column, syncMode: SyncMode.OneTime);

        var selected = PushRuleSelection.Select(
            [plain, manualRefresh, oneTime], Invoice, includeAssignedTo, PushRuleSelection.AssignedToColumnsOf(Bound()));

        selected.Rules.Should().Equal([plain], "a display copy is written only by the create; a push never re-maps it");
        selected.SkippedCount.Should().Be(2);
    }

    /// <summary>Must-not-fire: on the same invoice profile, a plain Manual Refresh column still pushes, with or without the opt-in.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APlainColumnOfTheSameInvoiceProfile_StillPushes(bool includeAssignedTo)
    {
        var description = Rule("sprk_description");
        var priority = Rule("sprk_highpriority");

        var selected = PushRuleSelection.Select(
            [description, priority], Invoice, includeAssignedTo, PushRuleSelection.AssignedToColumnsOf(Bound()));

        selected.Rules.Should().Equal(description, priority);
        selected.SkippedCount.Should().Be(0);
    }

    /// <summary>The display-copy skip is the invoice's: the same column name on another table is decided by its own class.</summary>
    [Fact]
    public void TheSameColumnNameOnAnotherTable_IsNotADisplayCopy()
    {
        var sameName = Rule("sprk_assignedtoattorney1");

        var selected = PushRuleSelection.Select([sameName], "sprk_reportcard", includeAssignedTo: false, PushRuleSelection.AssignedToColumnsOf(Bound()));

        selected.Rules.Should().Equal(sameName);
    }

    /// <summary>The registry as the BFF binds it: empty, then seeded by the defaults' post-configure.</summary>
    private static AccessConferringRegistry Bound()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(null, options);
        return options.AccessConferringRoles;
    }
}
