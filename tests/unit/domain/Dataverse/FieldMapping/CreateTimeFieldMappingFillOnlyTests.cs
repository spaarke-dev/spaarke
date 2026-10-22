using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 002 (spec FR-02, FR-05(e)) — the fill-only entry point
/// <see cref="CreateTimeFieldMapping.ApplyFillOnly"/> and the refusal of regarding-pair / resolver / protected targets.
/// Task 001's <c>CreateTimeFieldMappingTests</c> pins the overwrite-free typing and stays the home of those cases.
/// </summary>
/// <remarks>
/// "Not supplied" is the FR-02 definition: scalars absent, null or empty/whitespace; lookups no bind; booleans and
/// option sets absent only (so <c>false</c> and option value <c>0</c> are supplied). The target is the caller's payload.
/// </remarks>
public class CreateTimeFieldMappingFillOnlyTests
{
    private const int Copy = CreateTimeFieldMapping.MappingTypeCopy;
    private const int Default = CreateTimeFieldMapping.MappingTypeDefault;
    private const int Concat = CreateTimeFieldMapping.MappingTypeConcat;
    private const int Template = CreateTimeFieldMapping.MappingTypeTemplate;

    private const int Text = CreateTimeFieldMapping.FieldTypeText;
    private const int Lookup = CreateTimeFieldMapping.FieldTypeLookup;
    private const int OptionSet = CreateTimeFieldMapping.FieldTypeOptionSet;
    private const int Number = CreateTimeFieldMapping.FieldTypeNumber;
    private const int DateTimeType = CreateTimeFieldMapping.FieldTypeDateTime;
    private const int Boolean = CreateTimeFieldMapping.FieldTypeBoolean;
    private const int Memo = CreateTimeFieldMapping.FieldTypeMemo;

    private const string TargetColumn = "sprk_target";
    private const string SourceColumn = "sprk_source";

    private static readonly IReadOnlySet<string> NoProtectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // ───────────────────────────── scalars ─────────────────────────────

    [Theory]
    [InlineData(Text)]
    [InlineData(Memo)]
    public void FillOnly_SuppliedTextScalar_Survives_AndRaisesNoWarning(int targetFieldType)
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = "typed by the user" };

        var warnings = FillOnly(new[] { CopyRule(targetFieldType) }, target, SourceWith(SourceColumn, "from the source"));

        target[TargetColumn].Should().Be("typed by the user");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_SuppliedNumberAndDateTime_Survive()
    {
        var chosen = new DateTime(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var target = new Entity("sprk_matter") { ["sprk_count"] = 5, ["sprk_due"] = chosen };
        var source = new Entity("sprk_matter") { ["sprk_srccount"] = 99, ["sprk_srcdue"] = DateTime.UtcNow };
        var rules = new[]
        {
            CopyRule(Number, "sprk_srccount", "sprk_count"),
            CopyRule(DateTimeType, "sprk_srcdue", "sprk_due"),
        };

        var warnings = FillOnly(rules, target, source);

        target["sprk_count"].Should().Be(5);
        target["sprk_due"].Should().Be(chosen);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_AbsentScalar_ReceivesTheMappedValue()
    {
        var target = new Entity("sprk_matter");

        FillOnly(new[] { CopyRule(Text) }, target, SourceWith(SourceColumn, "from the source"));

        target[TargetColumn].Should().Be("from the source");
    }

    [Fact]
    public void FillOnly_NullScalar_ReceivesTheMappedValue()
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = null };

        FillOnly(new[] { CopyRule(Text) }, target, SourceWith(SourceColumn, "from the source"));

        target[TargetColumn].Should().Be("from the source");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \r\n")]
    public void FillOnly_EmptyOrWhitespaceString_IsUnsuppliedAndReceivesTheMappedValue(string callerValue)
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = callerValue };

        var warnings = FillOnly(new[] { CopyRule(Text) }, target, SourceWith(SourceColumn, "from the source"));

        target[TargetColumn].Should().Be("from the source");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_WhitespaceString_WhenNothingIsMapped_IsNormalisedToNull()
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = "   " };

        FillOnly(new[] { CopyRule(Text) }, target, new Entity("sprk_matter")); // the source has no value to copy

        target.Contains(TargetColumn).Should().BeFalse();
    }

    // ───────────────────────────── lookups ─────────────────────────────

    [Fact]
    public void FillOnly_LookupWithAnExistingBind_Survives()
    {
        var picked = new EntityReference("sprk_organization", Guid.NewGuid());
        var target = new Entity("sprk_reportcard") { [TargetColumn] = picked };
        var source = SourceWith(SourceColumn, new EntityReference("sprk_organization", Guid.NewGuid()));

        var warnings = FillOnly(new[] { CopyRule(Lookup) }, target, source);

        target[TargetColumn].Should().BeSameAs(picked);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_LookupWithNoBind_IsFilled()
    {
        var mappedId = Guid.NewGuid();
        var target = new Entity("sprk_reportcard");

        FillOnly(new[] { CopyRule(Lookup) }, target, SourceWith(SourceColumn, new EntityReference("sprk_organization", mappedId)));

        target[TargetColumn].Should().BeOfType<EntityReference>().Which.Id.Should().Be(mappedId);
    }

    [Theory]
    [InlineData(false)] // an explicit null
    [InlineData(true)]  // an empty-id reference
    public void FillOnly_LookupHoldingNullOrAnEmptyReference_CountsAsNoBind(bool emptyReference)
    {
        var mappedId = Guid.NewGuid();
        var target = new Entity("sprk_reportcard")
        {
            [TargetColumn] = emptyReference ? new EntityReference("sprk_organization", Guid.Empty) : null,
        };

        FillOnly(new[] { CopyRule(Lookup) }, target, SourceWith(SourceColumn, new EntityReference("sprk_organization", mappedId)));

        target[TargetColumn].Should().BeOfType<EntityReference>().Which.Id.Should().Be(mappedId);
    }

    // ───────────────────────────── booleans and option sets: absent only ─────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillOnly_SuppliedBoolean_IncludingFalse_Survives(bool callerValue)
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = callerValue };

        var warnings = FillOnly(new[] { DefaultRule(Boolean, callerValue ? "false" : "true") }, target, source: null);

        target[TargetColumn].Should().Be(callerValue);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_AbsentBoolean_IsFilled()
    {
        var target = new Entity("sprk_matter");

        FillOnly(new[] { DefaultRule(Boolean, "true") }, target, source: null);

        target[TargetColumn].Should().Be(true);
    }

    [Fact]
    public void FillOnly_SuppliedOptionSetValueZero_Survives()
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = new OptionSetValue(0) };

        var warnings = FillOnly(new[] { DefaultRule(OptionSet, "100000003") }, target, source: null);

        target[TargetColumn].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(0);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_AbsentOptionSet_IsFilled()
    {
        var target = new Entity("sprk_matter");

        FillOnly(new[] { DefaultRule(OptionSet, "100000003") }, target, source: null);

        target[TargetColumn].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000003);
    }

    [Theory]
    [InlineData(Boolean, "true")]
    [InlineData(OptionSet, "100000003")]
    public void FillOnly_BooleanOrOptionSetKeyPresentWithNull_IsStillSupplied_BecauseOnlyAbsentCounts(
        int targetFieldType, string literal)
    {
        var target = new Entity("sprk_matter") { [TargetColumn] = null };

        FillOnly(new[] { DefaultRule(targetFieldType, literal) }, target, source: null);

        target[TargetColumn].Should().BeNull();
    }

    // ───────────────────────────── Report Card regression (design-review §2) ─────────────────────────────

    // The Matter→Report Card profile maps Law Firm 1 / Assigned External / Assigned Internal (+ the attorney matrix)
    // as lookup→lookup Copy rules. The wizard's pickers are the caller's payload; mapping used to replace them.
    [Fact]
    public void ReportCard_LawFirm1_AssignedExternal_AssignedInternal_SuppliedByTheCaller_AreNotOverwritten_AndAnUnsuppliedSiblingIsFilled()
    {
        var pickedLawFirm = new EntityReference("sprk_organization", Guid.NewGuid());
        var pickedExternal = new EntityReference("contact", Guid.NewGuid());
        var pickedInternal = new EntityReference("contact", Guid.NewGuid());
        var matterAttorney = new EntityReference("contact", Guid.NewGuid());

        var reportCard = new Entity("sprk_reportcard")
        {
            ["sprk_name"] = "Q3 report card",
            ["sprk_assignedtolawfirm1"] = pickedLawFirm,
            ["sprk_assignedtoexternal"] = pickedExternal,
            ["sprk_assignedtointernal"] = pickedInternal,
        };
        var matter = new Entity("sprk_matter")
        {
            ["sprk_lawfirm1"] = new EntityReference("sprk_organization", Guid.NewGuid()),
            ["sprk_externalcontact"] = new EntityReference("contact", Guid.NewGuid()),
            ["sprk_internalcontact"] = new EntityReference("contact", Guid.NewGuid()),
            ["sprk_attorney1"] = matterAttorney,
        };
        var rules = new[]
        {
            CopyRule(Lookup, "sprk_lawfirm1", "sprk_assignedtolawfirm1"),
            CopyRule(Lookup, "sprk_externalcontact", "sprk_assignedtoexternal"),
            CopyRule(Lookup, "sprk_internalcontact", "sprk_assignedtointernal"),
            CopyRule(Lookup, "sprk_attorney1", "sprk_assignedattorney1"),
        };

        var warnings = FillOnly(rules, reportCard, matter);

        reportCard["sprk_assignedtolawfirm1"].Should().BeSameAs(pickedLawFirm);
        reportCard["sprk_assignedtoexternal"].Should().BeSameAs(pickedExternal);
        reportCard["sprk_assignedtointernal"].Should().BeSameAs(pickedInternal);
        reportCard["sprk_assignedattorney1"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(matterAttorney.Id);
        warnings.Should().BeEmpty();
    }

    // ───────────────────────────── negative / contrast ─────────────────────────────

    [Fact]
    public void FillOnly_WhenTheCallerSuppliedEveryMappedColumn_NothingIsWritten_AndNoWarningIsRaised()
    {
        var chosenRef = new EntityReference("contact", Guid.NewGuid());
        var target = new Entity("sprk_matter")
        {
            ["sprk_text"] = "t",
            ["sprk_lookup"] = chosenRef,
            ["sprk_flag"] = false,
            ["sprk_choice"] = new OptionSetValue(0),
        };
        var before = target.Attributes.ToDictionary(a => a.Key, a => a.Value);
        var source = new Entity("sprk_matter")
        {
            ["sprk_s1"] = "other",
            ["sprk_s2"] = new EntityReference("contact", Guid.NewGuid()),
        };
        var rules = new[]
        {
            CopyRule(Text, "sprk_s1", "sprk_text"),
            CopyRule(Lookup, "sprk_s2", "sprk_lookup"),
            DefaultRule(Boolean, "true", "sprk_flag"),
            DefaultRule(OptionSet, "100000001", "sprk_choice"),
            ExpressionRule(Template, Text, "{sprk_s1}!", "sprk_text"),
        };

        var warnings = FillOnly(rules, target, source);

        target.Attributes.Should().BeEquivalentTo(before);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FillOnly_TwoRulesOnTheSameUnsuppliedColumn_KeepLastRuleWins_TheFirstDoesNotBlockTheSecond()
    {
        var first = DefaultRule(Text, "first");
        first.ExecutionOrder = 1;
        var second = DefaultRule(Text, "second");
        second.ExecutionOrder = 2;
        var target = new Entity("sprk_matter");

        FillOnly(new[] { second, first }, target, source: null);

        target[TargetColumn].Should().Be("second");
    }

    // Apply_TheOverwriteEntryPoint_StillReplacesAnExistingValue is gone (field mapping task 018c): the overwrite entry point
    // was the Office creation core's, deleted with it. Fill-only keeping the caller's value is pinned above.

    // ───────────────────────────── target guard (FR-05(e), ADR-024) ─────────────────────────────

    public static TheoryData<string> GuardedPairAndResolverColumns => new()
    {
        "sprk_regardingrecordid",
        "sprk_regardingrecordtype",
        "sprk_regardingrecordname",
        "sprk_regardingrecordurl",
        "sprk_regardingrecordnumber",
    };

    [Theory]
    [MemberData(nameof(GuardedPairAndResolverColumns))]
    public void PairOrResolverTarget_ProducesNoColumn_AndExactlyOneWarningNamingTheRuleAndTarget(string guardedColumn)
    {
        var rule = DefaultRule(Text, "should never be written", guardedColumn);
        rule.Name = "Matter to Event: regarding";
        var target = new Entity("sprk_event");
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(new[] { rule }, target, null, NoProtectedColumns, warnings, NullLogger.Instance);

        target.Contains(guardedColumn).Should().BeFalse();
        var warning = warnings.Should().ContainSingle().Subject;
        warning.Should().Contain("Matter to Event: regarding").And.Contain(guardedColumn);
    }

    // Apply_TheLegacyOverwriteEntryPoint_StillWritesAMappedPair_BecauseOfficeProjectFilingReliesOnIt is gone (field mapping
    // task 018c), changed on purpose: the Office quick-create derives the pair before the create (task 018b, G4) and no
    // mapping rule writes it any more — the refusal is pinned by the pair-target tests here.

    [Theory]
    [InlineData(Copy)]
    [InlineData(Default)]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void PairTarget_IsRefusedForEveryMappingType_AndTheCallersOwnPairValueIsLeftAlone(int mappingType)
    {
        var rule = new FieldMappingRuleEntity
        {
            Name = "pair rule",
            SourceField = SourceColumn,
            TargetField = "sprk_regardingrecordid",
            MappingType = mappingType,
            TargetFieldType = Text,
            DefaultValue = "x",
            Expression = "{sprk_source}",
            IsActive = true,
        };
        var target = new Entity("sprk_event") { ["sprk_regardingrecordid"] = "caller-set-guid" };
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(
            new[] { rule }, target, SourceWith(SourceColumn, "mapped"), NoProtectedColumns, warnings, NullLogger.Instance);

        target["sprk_regardingrecordid"].Should().Be("caller-set-guid");
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void PairOrResolverTarget_MatchIsCaseInsensitiveAndTrimmed()
    {
        var target = new Entity("sprk_event");
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(
            new[] { DefaultRule(Text, "x", "  SPRK_RegardingRecordType ") },
            target, null, NoProtectedColumns, warnings, NullLogger.Instance);

        target.Attributes.Should().BeEmpty();
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void ProtectedColumnTarget_ProducesNoColumn_AndExactlyOneWarning_UnderFillOnlyToo()
    {
        var protectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matternumber" };
        var target = new Entity("sprk_matter");
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(
            new[] { DefaultRule(Text, "MAT-1", "sprk_matternumber") },
            target, null, protectedColumns, warnings, NullLogger.Instance);

        target.Contains("sprk_matternumber").Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("sprk_matternumber");
    }

    [Fact]
    public void RefusedTargets_DoNotStopTheRemainingRules()
    {
        var refused = DefaultRule(Text, "x", "sprk_regardingrecordid");
        refused.ExecutionOrder = 1;
        var fine = DefaultRule(Text, "kept", "sprk_other");
        fine.ExecutionOrder = 2;
        var target = new Entity("sprk_event");
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(new[] { refused, fine }, target, null, NoProtectedColumns, warnings, NullLogger.Instance);

        target["sprk_other"].Should().Be("kept");
        warnings.Should().ContainSingle();
    }

    // ───────────────────── the origin set (task 014, FR-08) ─────────────────────

    [Fact]
    public void MappedColumns_NamesExactlyTheColumnsARuleWrote_NeverASuppliedGuardedOrEmptyOne()
    {
        // Every rule kind that writes (Copy text, Copy lookup, Default, Concat), and every way a rule does not: the caller
        // supplied the column, the target is the pair or protected, the source has no value, the default does not convert.
        var assignee = new EntityReference("contact", Guid.Parse("a0140000-0000-4000-8000-0000000000c1"));
        var source = new Entity("sprk_matter") { ["sprk_src_text"] = "copied", ["sprk_src_lookup"] = assignee, ["sprk_src_name"] = "Acme" };
        var rules = new[]
        {
            CopyRule(Text, "sprk_src_text", "sprk_copied"),
            CopyRule(Lookup, "sprk_src_lookup", "sprk_assignee"),
            DefaultRule(Text, "fixed", "sprk_defaulted"),
            ExpressionRule(Concat, Text, "{sprk_src_name} matter", "sprk_concatenated"),
            CopyRule(Text, "sprk_src_text", "sprk_supplied"),
            CopyRule(Text, "sprk_src_text", "sprk_regardingrecordid"),
            CopyRule(Text, "sprk_src_text", "sprk_matternumber"),
            CopyRule(Text, "sprk_src_absent", "sprk_nothingtocopy"),
            DefaultRule(Number, "not a number", "sprk_unconvertible"),
        };
        var target = new Entity("sprk_event") { ["sprk_supplied"] = "the caller's" };
        var protectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matternumber" };
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CreateTimeFieldMapping.ApplyFillOnly(
            rules, target, source, protectedColumns, new List<string>(), NullLogger.Instance, mappedColumns: mapped);

        mapped.Should().BeEquivalentTo("sprk_copied", "sprk_assignee", "sprk_defaulted", "sprk_concatenated");
        target["sprk_supplied"].Should().Be("the caller's");
    }

    [Fact]
    public void MappedColumns_IsOptional_AndApplyWritesTheSameValuesWithoutIt()
    {
        var withSet = new Entity("sprk_event");
        var without = new Entity("sprk_event");
        var source = SourceWith(SourceColumn, "from the source");

        CreateTimeFieldMapping.ApplyFillOnly(
            new[] { CopyRule(Text) }, withSet, source, NoProtectedColumns, new List<string>(), NullLogger.Instance,
            mappedColumns: new HashSet<string>());
        CreateTimeFieldMapping.ApplyFillOnly(
            new[] { CopyRule(Text) }, without, source, NoProtectedColumns, new List<string>(), NullLogger.Instance);

        withSet.Attributes.Should().BeEquivalentTo(without.Attributes);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static List<string> FillOnly(IEnumerable<FieldMappingRuleEntity> rules, Entity target, Entity? source)
    {
        var warnings = new List<string>();
        CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, NoProtectedColumns, warnings, NullLogger.Instance);
        return warnings;
    }

    private static Entity SourceWith(string column, object value) => new("sprk_matter") { [column] = value };

    private static FieldMappingRuleEntity RuleFor(int mappingType, int targetFieldType, string targetColumn = TargetColumn) => new()
    {
        Name = "rule",
        SourceField = SourceColumn,
        TargetField = targetColumn,
        MappingType = mappingType,
        TargetFieldType = targetFieldType,
        IsActive = true,
    };

    private static FieldMappingRuleEntity CopyRule(
        int targetFieldType, string sourceField = SourceColumn, string targetColumn = TargetColumn)
    {
        var rule = RuleFor(Copy, targetFieldType, targetColumn);
        rule.SourceField = sourceField;
        return rule;
    }

    private static FieldMappingRuleEntity DefaultRule(int targetFieldType, string? defaultValue, string targetColumn = TargetColumn)
    {
        var rule = RuleFor(Default, targetFieldType, targetColumn);
        rule.DefaultValue = defaultValue;
        return rule;
    }

    private static FieldMappingRuleEntity ExpressionRule(
        int mappingType, int targetFieldType, string? expression, string targetColumn = TargetColumn)
    {
        var rule = RuleFor(mappingType, targetFieldType, targetColumn);
        rule.Expression = expression;
        return rule;
    }
}
