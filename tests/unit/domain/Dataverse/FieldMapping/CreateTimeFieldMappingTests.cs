using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 001 (spec FR-01) — characterization of the canonical create-time field-mapping
/// engine, <see cref="CreateTimeFieldMapping"/>: every mapping type (Copy / Default / Concat / Template) against every
/// target field type, and each FR-01 semantic rule. These tests pin the engine's CURRENT behaviour; they exist so that
/// the later tasks (fill-only, source resolution, push) can change the engine on purpose and prove what moved.
/// </summary>
/// <remarks>
/// <para>The engine is pure (no I/O), so every test drives the real <c>Apply</c> over real SDK <see cref="Entity"/> values
/// and asserts the payload and the warnings. Each test name is cited by the semantics table in
/// <c>docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md</c>; renaming one means updating that table.</para>
/// <para>Target field types are the framework's seven option-set values (Text 0, Lookup 1, OptionSet 2, Number 3,
/// DateTime 4, Boolean 5, Memo 6). WholeNumber, Decimal and Money are all "Number" (3): the rule cannot tell them apart,
/// so what separates them in these tests is the CLR type of the value the engine writes.</para>
/// </remarks>
public class CreateTimeFieldMappingTests
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

    private static readonly IReadOnlySet<string> MoneyTargetColumn =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TargetColumn };

    // ───────────────────────────── Copy × target field type ─────────────────────────────

    [Fact]
    public void Copy_TextTarget_FromTextSource_CopiesTheString()
    {
        var (target, warnings) = Run(CopyRule(Text), SourceWith(SourceColumn, "Acme v. Globex"));

        target[TargetColumn].Should().Be("Acme v. Globex");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_MemoTarget_FromTextSource_CopiesTheString()
    {
        var (target, warnings) = Run(CopyRule(Memo), SourceWith(SourceColumn, "line one\nline two"));

        target[TargetColumn].Should().Be("line one\nline two");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_WholeNumberTarget_FromIntSource_WritesTheInt()
    {
        var (target, _) = Run(CopyRule(Number), SourceWith(SourceColumn, 42));

        target[TargetColumn].Should().BeOfType<int>().Which.Should().Be(42);
    }

    [Fact]
    public void Copy_DecimalTarget_FromDecimalSource_WritesTheDecimal()
    {
        var (target, _) = Run(CopyRule(Number), SourceWith(SourceColumn, 12.5m));

        target[TargetColumn].Should().BeOfType<decimal>().Which.Should().Be(12.5m);
    }

    [Fact]
    public void Copy_MoneyTarget_FromMoneySource_WritesAMoneyNotADecimal()
    {
        var (target, _) = Run(CopyRule(Number), SourceWith(SourceColumn, new Money(1500.25m)));

        target[TargetColumn].Should().BeOfType<Money>().Which.Value.Should().Be(1500.25m);
    }

    [Fact]
    public void Copy_BooleanTarget_FromBoolSource_WritesTheBool()
    {
        var (target, _) = Run(CopyRule(Boolean), SourceWith(SourceColumn, true));

        target[TargetColumn].Should().BeOfType<bool>().Which.Should().BeTrue();
    }

    [Fact]
    public void Copy_OptionSetTarget_FromOptionSetSource_WritesTheOptionValueNotTheLabel()
    {
        var source = SourceWith(SourceColumn, new OptionSetValue(100000002));
        source.FormattedValues[SourceColumn] = "High";

        var (target, _) = Run(CopyRule(OptionSet), source);

        target[TargetColumn].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000002);
    }

    [Fact]
    public void Copy_DateTimeTarget_FromDateTimeSource_WritesTheDateTimeNotAString()
    {
        var when = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

        var (target, _) = Run(CopyRule(DateTimeType), SourceWith(SourceColumn, when));

        target[TargetColumn].Should().BeOfType<DateTime>().Which.Should().Be(when);
    }

    [Fact]
    public void Copy_LookupTarget_FromLookupSource_BindsAFreshReferenceWithoutTheName()
    {
        var id = Guid.NewGuid();
        var sourceReference = new EntityReference("systemuser", id) { Name = "Pat Attorney" };

        var (target, warnings) = Run(CopyRule(Lookup), SourceWith(SourceColumn, sourceReference));

        var written = target[TargetColumn].Should().BeOfType<EntityReference>().Subject;
        written.LogicalName.Should().Be("systemuser");
        written.Id.Should().Be(id);
        written.Should().NotBeSameAs(sourceReference);
        written.Name.Should().BeNull();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_LookupTarget_FromANonLookupValue_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(CopyRule(Lookup), SourceWith(SourceColumn, "not a lookup"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("not a lookup");
    }

    [Fact]
    public void Copy_LookupTarget_FromAnEmptyGuidReference_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(CopyRule(Lookup), SourceWith(SourceColumn, new EntityReference("systemuser", Guid.Empty)));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle();
    }

    // ───────────────── Copy into Text/Memo: option-set label / ISO-8601 date / Yes-No rendering ─────────────────

    [Theory]
    [InlineData(Text)]
    [InlineData(Memo)]
    public void Copy_TextOrMemoTarget_FromOptionSetWithLabel_RendersTheLabel(int targetFieldType)
    {
        var source = SourceWith(SourceColumn, new OptionSetValue(100000001));
        source.FormattedValues[SourceColumn] = "Active";

        var (target, _) = Run(CopyRule(targetFieldType), source);

        target[TargetColumn].Should().Be("Active");
    }

    [Fact]
    public void Copy_TextTarget_FromOptionSetWithoutLabel_RendersTheInteger()
    {
        var (target, _) = Run(CopyRule(Text), SourceWith(SourceColumn, new OptionSetValue(100000001)));

        target[TargetColumn].Should().Be("100000001");
    }

    [Theory]
    [InlineData(Text)]
    [InlineData(Memo)]
    public void Copy_TextOrMemoTarget_FromDateTime_RendersIso8601(int targetFieldType)
    {
        var when = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

        var (target, _) = Run(CopyRule(targetFieldType), SourceWith(SourceColumn, when));

        target[TargetColumn].Should().Be("2026-03-15T10:30:00.0000000Z");
    }

    [Theory]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    public void Copy_TextTarget_FromBoolean_RendersYesOrNo(bool value, string expected)
    {
        var (target, _) = Run(CopyRule(Text), SourceWith(SourceColumn, value));

        target[TargetColumn].Should().Be(expected);
    }

    [Fact]
    public void Copy_TextTarget_FromMoney_RendersTheInvariantAmount()
    {
        var (target, _) = Run(CopyRule(Text), SourceWith(SourceColumn, new Money(1500.25m)));

        target[TargetColumn].Should().Be("1500.25");
    }

    [Fact]
    public void Copy_TextTarget_FromWholeNumberAndDecimal_RendersInvariantCulture()
    {
        var (whole, _) = Run(CopyRule(Text), SourceWith(SourceColumn, 1234567));
        var (fractional, _) = Run(CopyRule(Text), SourceWith(SourceColumn, 1234.5m));

        whole[TargetColumn].Should().Be("1234567");
        fractional[TargetColumn].Should().Be("1234.5");
    }

    [Fact]
    public void Copy_TextTarget_FromLookup_RendersTheReferenceNameElseTheGuid()
    {
        var named = new EntityReference("systemuser", Guid.NewGuid()) { Name = "Pat Attorney" };
        var unnamedId = Guid.NewGuid();
        var unnamed = new EntityReference("systemuser", unnamedId);

        var (withName, _) = Run(CopyRule(Text), SourceWith(SourceColumn, named));
        var (withoutName, _) = Run(CopyRule(Text), SourceWith(SourceColumn, unnamed));

        withName[TargetColumn].Should().Be("Pat Attorney");
        withoutName[TargetColumn].Should().Be(unnamedId.ToString("D"));
    }

    // ─────────────────────────────────── null source is skipped ───────────────────────────────────

    [Theory]
    [InlineData(Text)]
    [InlineData(Memo)]
    [InlineData(Number)]
    [InlineData(OptionSet)]
    [InlineData(DateTimeType)]
    [InlineData(Boolean)]
    [InlineData(Lookup)]
    public void Copy_NullSourceValue_ProducesNoColumnAndNoWarning(int targetFieldType)
    {
        var source = new Entity("sprk_matter") { [SourceColumn] = null };

        var (target, warnings) = Run(CopyRule(targetFieldType), source);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_SourceAttributeAbsent_ProducesNoColumnAndNoWarning()
    {
        var (target, warnings) = Run(CopyRule(Text), new Entity("sprk_matter"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_SourceRecordNotRead_ProducesNoColumnAndNoWarning()
    {
        var (target, warnings) = Run(CopyRule(Text), source: null);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Copy_RuleWithNoSourceField_IsSilentlySkipped()
    {
        var rule = CopyRule(Text);
        rule.SourceField = "  ";

        var (target, warnings) = Run(rule, SourceWith(SourceColumn, "value"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().BeEmpty();
    }

    // ───────────────────────────── Default × target field type (typed conversion) ─────────────────────────────

    [Theory]
    [InlineData(Text)]
    [InlineData(Memo)]
    public void Default_TextOrMemoTarget_WritesTheLiteralAsWritten(int targetFieldType)
    {
        var (target, warnings) = Run(DefaultRule(targetFieldType, "  Pending review  "));

        target[TargetColumn].Should().Be("  Pending review  ");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Default_WholeNumberTarget_ConvertsToAnInt()
    {
        var (target, _) = Run(DefaultRule(Number, "42"));

        target[TargetColumn].Should().BeOfType<int>().Which.Should().Be(42);
    }

    [Fact]
    public void Default_DecimalTarget_ConvertsToADecimalWhenTheLiteralHasAFraction()
    {
        var (target, _) = Run(DefaultRule(Number, "12.5"));

        target[TargetColumn].Should().BeOfType<decimal>().Which.Should().Be(12.5m);
    }

    // DI-001 / #2124 (fixed on task 002): the rule's "Number" cannot say "Money", so the caller names the currency
    // columns it knows; a Default into one is written as a Money (the SDK rejects a decimal in a money column).
    [Theory]
    [InlineData("1500.25", 1500.25)]
    [InlineData("1500", 1500)]
    public void Default_MoneyTarget_WritesAMoney(string literal, double expected)
    {
        var (target, warnings) = Run(
            new[] { DefaultRule(Number, literal) }, source: null, moneyAttributes: MoneyTargetColumn);

        target[TargetColumn].Should().BeOfType<Money>().Which.Value.Should().Be((decimal)expected);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Default_MoneyTarget_FromANonNumber_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(
            new[] { DefaultRule(Number, "lots") }, source: null, moneyAttributes: MoneyTargetColumn);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("cannot be written");
    }

    [Fact]
    public void Default_NumberTarget_NotNamedAsMoney_StillWritesAPlainDecimal()
    {
        var (target, _) = Run(DefaultRule(Number, "1500.25"));

        target[TargetColumn].Should().BeOfType<decimal>().Which.Should().Be(1500.25m);
    }

    [Fact]
    public void Default_NumberTarget_FromANonNumber_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(Number, "forty-two"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("cannot be written");
    }

    [Fact]
    public void Default_OptionSetTarget_ConvertsToAnOptionSetValue()
    {
        var (target, _) = Run(DefaultRule(OptionSet, "100000003"));

        target[TargetColumn].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000003);
    }

    [Fact]
    public void Default_OptionSetTarget_FromALabel_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(OptionSet, "High"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("No", false)]
    public void Default_BooleanTarget_ConvertsTrueFalseOneZeroYesNo(string literal, bool expected)
    {
        var (target, _) = Run(DefaultRule(Boolean, literal));

        target[TargetColumn].Should().BeOfType<bool>().Which.Should().Be(expected);
    }

    [Fact]
    public void Default_BooleanTarget_FromAnUnrecognisedWord_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(Boolean, "maybe"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle();
    }

    [Theory]
    [InlineData("2026-03-15T10:30:00Z", 2026, 3, 15, 10, 30)]
    [InlineData("2026-03-15", 2026, 3, 15, 0, 0)]
    [InlineData("2026-03-15T10:30:00+02:00", 2026, 3, 15, 8, 30)]
    public void Default_DateTimeTarget_ConvertsToAUtcDateTime(
        string literal, int year, int month, int day, int hour, int minute)
    {
        var (target, _) = Run(DefaultRule(DateTimeType, literal));

        var written = target[TargetColumn].Should().BeOfType<DateTime>().Subject;
        written.Should().Be(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc));
        written.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Default_DateTimeTarget_FromANonDate_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(DateTimeType, "next Tuesday-ish"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void Default_LookupTarget_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(Lookup, Guid.NewGuid().ToString()));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void Default_NoDefaultValue_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(DefaultRule(Text, defaultValue: null));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("no default value");
    }

    [Fact]
    public void Default_DoesNotNeedASourceRecord()
    {
        var (target, warnings) = Run(DefaultRule(Text, "constant"), source: null);

        target[TargetColumn].Should().Be("constant");
        warnings.Should().BeEmpty();
    }

    // ─────────────────────────── Concat / Template × target field type (one {field} resolver) ───────────────────────────

    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_TextTarget_ResolvesPlaceholdersInOrder(int mappingType)
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme", ["sprk_second"] = "Globex" };

        var (target, warnings) = Run(ExpressionRule(mappingType, Text, "{sprk_first} v. {sprk_second}"), source);

        target[TargetColumn].Should().Be("Acme v. Globex");
        warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_MemoTarget_ResolvesPlaceholders(int mappingType)
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme" };

        var (target, _) = Run(ExpressionRule(mappingType, Memo, "Client:\n{sprk_first}"), source);

        target[TargetColumn].Should().Be("Client:\nAcme");
    }

    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_LookupTarget_IsSkippedWithOneWarning(int mappingType)
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme" };

        var (target, warnings) = Run(ExpressionRule(mappingType, Lookup, "{sprk_first}"), source);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("cannot set a lookup");
    }

    // DI-001 / #2124 (fixed on task 002): a Concat/Template into a non-text, non-lookup target converts the resolved
    // text to the SDK's typed value with the same conventions as a Default literal — never a string.
    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_NumberTarget_ConvertsTheResolvedTextToAnIntOrDecimal(int mappingType)
    {
        var whole = new Entity("sprk_matter") { ["sprk_count"] = 7 };
        var fractional = new Entity("sprk_matter") { ["sprk_count"] = 12.5m };

        Run(ExpressionRule(mappingType, Number, "{sprk_count}"), whole).Target[TargetColumn]
            .Should().BeOfType<int>().Which.Should().Be(7);
        Run(ExpressionRule(mappingType, Number, "{sprk_count}"), fractional).Target[TargetColumn]
            .Should().BeOfType<decimal>().Which.Should().Be(12.5m);
    }

    [Fact]
    public void Expression_MoneyTarget_ConvertsTheResolvedTextToAMoney()
    {
        var source = new Entity("sprk_matter") { ["sprk_budget"] = new Money(1500.25m) };

        var (target, warnings) = Run(
            new[] { ExpressionRule(Template, Number, "{sprk_budget}") }, source, moneyAttributes: MoneyTargetColumn);

        target[TargetColumn].Should().BeOfType<Money>().Which.Value.Should().Be(1500.25m);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Expression_OptionSetTarget_ConvertsTheResolvedTextToAnOptionSetValue()
    {
        var source = new Entity("sprk_matter") { ["sprk_code"] = "100000002" };

        var (target, _) = Run(ExpressionRule(Template, OptionSet, "{sprk_code}"), source);

        target[TargetColumn].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000002);
    }

    [Fact]
    public void Expression_DateTimeTarget_ConvertsTheResolvedTextToAUtcDateTime()
    {
        var source = new Entity("sprk_matter") { ["sprk_due"] = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc) };

        var (target, _) = Run(ExpressionRule(Template, DateTimeType, "{sprk_due}"), source);

        target[TargetColumn].Should().BeOfType<DateTime>().Which.Should().Be(new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Expression_BooleanTarget_ConvertsTheResolvedTextToABool()
    {
        var source = new Entity("sprk_matter") { ["sprk_active"] = true };

        var (target, _) = Run(ExpressionRule(Template, Boolean, "{sprk_active}"), source);

        target[TargetColumn].Should().BeOfType<bool>().Which.Should().BeTrue();
    }

    [Theory]
    [InlineData(Number)]
    [InlineData(OptionSet)]
    [InlineData(DateTimeType)]
    [InlineData(Boolean)]
    public void Expression_NonTextNonLookupTarget_WhoseResultDoesNotConvert_IsSkippedWithOneWarningNeverAString(
        int targetFieldType)
    {
        var source = new Entity("sprk_matter") { ["sprk_name"] = "Acme" };

        var (target, warnings) = Run(ExpressionRule(Template, targetFieldType, "{sprk_name} Ltd"), source);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("cannot be written");
    }

    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_NoExpression_IsSkippedWithOneWarning(int mappingType)
    {
        var (target, warnings) = Run(ExpressionRule(mappingType, Text, expression: null), new Entity("sprk_matter"));

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("no expression");
    }

    // ───────────────────────── the {field} resolver: shared text rendering and placeholder rules ─────────────────────────

    [Theory]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Expression_RendersEachValueKindWithTheCopyIntoTextConventions(int mappingType)
    {
        var source = new Entity("sprk_matter")
        {
            ["sprk_status"] = new OptionSetValue(100000001),
            ["sprk_due"] = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc),
            ["sprk_active"] = true,
            ["sprk_budget"] = new Money(1500.25m),
            ["sprk_lead"] = new EntityReference("systemuser", Guid.NewGuid()) { Name = "Pat Attorney" },
        };
        source.FormattedValues["sprk_status"] = "Active";

        var (target, warnings) = Run(
            ExpressionRule(mappingType, Text, "{sprk_status}|{sprk_due}|{sprk_active}|{sprk_budget}|{sprk_lead}"), source);

        target[TargetColumn].Should().Be("Active|2026-03-15T10:30:00.0000000Z|Yes|1500.25|Pat Attorney");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Expression_ConcatAndTemplate_ProduceTheSameTextForTheSameExpression()
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme", ["sprk_active"] = false };
        const string expression = "{sprk_first} / {sprk_active}";

        var (concat, _) = Run(ExpressionRule(Concat, Text, expression), source);
        var (template, _) = Run(ExpressionRule(Template, Text, expression), source);

        concat[TargetColumn].Should().Be("Acme / No");
        template[TargetColumn].Should().Be(concat[TargetColumn]);
    }

    [Fact]
    public void Expression_PlaceholderNameIsTrimmed()
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme" };

        var (target, warnings) = Run(ExpressionRule(Template, Text, "[{ sprk_first }]"), source);

        target[TargetColumn].Should().Be("[Acme]");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Expression_UnresolvedPlaceholder_IsBlankedWithOneWarningNeverTheLiteralToken()
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = "Acme" };

        var (target, warnings) = Run(ExpressionRule(Concat, Text, "{sprk_first} - {sprk_missing}"), source);

        target[TargetColumn].Should().Be("Acme - ");
        warnings.Should().ContainSingle().Which.Should().Contain("sprk_missing").And.Contain("left blank");
    }

    [Fact]
    public void Expression_NullPlaceholderValue_IsTreatedAsUnresolved()
    {
        var source = new Entity("sprk_matter") { ["sprk_first"] = null };

        var (target, warnings) = Run(ExpressionRule(Template, Text, "x{sprk_first}y"), source);

        target[TargetColumn].Should().Be("xy");
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void Expression_PlaceholdersButNoSourceRecord_ProducesNoColumnAndNoWarning()
    {
        var (target, warnings) = Run(ExpressionRule(Concat, Text, "{sprk_first}"), source: null);

        target.Contains(TargetColumn).Should().BeFalse();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Expression_WithoutPlaceholders_WritesTheLiteralEvenWithNoSourceRecord()
    {
        var (target, warnings) = Run(ExpressionRule(Template, Text, "Fixed text"), source: null);

        target[TargetColumn].Should().Be("Fixed text");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void CollectSourceColumns_ReturnsCopySourcesAndEveryPlaceholder_DistinctAndCaseInsensitive()
    {
        var rules = new[]
        {
            CopyRule(Text, sourceField: " sprk_alpha "),
            CopyRule(Text, sourceField: "SPRK_ALPHA"),
            ExpressionRule(Concat, Text, "{sprk_beta} {sprk_gamma}"),
            ExpressionRule(Template, Text, "{ sprk_beta }"),
            DefaultRule(Text, "ignored"),
            CopyRule(Text, sourceField: " "),
        };

        var columns = CreateTimeFieldMapping.CollectSourceColumns(rules);

        columns.Should().BeEquivalentTo(new[] { "sprk_alpha", "sprk_beta", "sprk_gamma" });
    }

    // ─────────────────────────────── protected columns are skipped with a warning ───────────────────────────────

    [Theory]
    [InlineData(Copy)]
    [InlineData(Default)]
    [InlineData(Concat)]
    [InlineData(Template)]
    public void Apply_ProtectedTargetColumn_ProducesNoColumnAndExactlyOneWarning(int mappingType)
    {
        var protectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matternumber" };
        var rule = RuleFor(mappingType, Text);
        rule.TargetField = "sprk_matternumber";
        var source = new Entity("sprk_matter") { [SourceColumn] = "M-1", ["sprk_first"] = "M-1" };

        var (target, warnings) = Run(new[] { rule }, source, protectedColumns);

        target.Contains("sprk_matternumber").Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("sprk_matternumber").And.Contain("managed by the server");
    }

    [Fact]
    public void Apply_ProtectedTargetColumn_DoesNotStopTheRemainingRules()
    {
        var protectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matternumber" };
        var blocked = DefaultRule(Text, "X");
        blocked.TargetField = "sprk_matternumber";
        var allowed = DefaultRule(Text, "kept");
        allowed.TargetField = "sprk_other";

        var (target, warnings) = Run(new[] { blocked, allowed }, source: null, protectedColumns);

        target.Contains("sprk_matternumber").Should().BeFalse();
        target["sprk_other"].Should().Be("kept");
        warnings.Should().ContainSingle();
    }

    [Fact]
    public void Apply_ProtectedTargetColumn_MatchIsTrimmedAndFollowsTheCallersComparer()
    {
        var protectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ownerid" };
        var rule = DefaultRule(Text, "X");
        rule.TargetField = "  OwnerId ";

        var (target, warnings) = Run(rule, source: null, protectedColumns);

        target.Attributes.Should().BeEmpty();
        warnings.Should().ContainSingle();
    }

    // ─────────────────────────────── rules apply in sprk_executionorder order ───────────────────────────────

    [Fact]
    public void Apply_RulesOutOfOrder_AreAppliedInExecutionOrderSoTheHighestOrderWritesLast()
    {
        var late = DefaultRule(Text, "from order 20");
        late.ExecutionOrder = 20;
        var early = DefaultRule(Text, "from order 10");
        early.ExecutionOrder = 10;
        var middle = DefaultRule(Text, "from order 15");
        middle.ExecutionOrder = 15;

        var (target, _) = Run(new[] { late, early, middle }, source: null);

        target[TargetColumn].Should().Be("from order 20");
    }

    [Fact]
    public void Apply_TwoRulesOnTheSameTarget_TheHigherExecutionOrderWinsWhateverTheMappingType()
    {
        var first = CopyRule(Text);
        first.ExecutionOrder = 1;
        var second = DefaultRule(Text, "default wins");
        second.ExecutionOrder = 2;
        var source = SourceWith(SourceColumn, "copied");

        var (target, _) = Run(new[] { second, first }, source);

        target[TargetColumn].Should().Be("default wins");
    }

    // ─────────────────────────────── never throws; one bad rule never aborts the rest ───────────────────────────────

    [Fact]
    public void Apply_ARuleThatThrows_IsSkippedWithOneGenericWarningAndTheNextRuleStillApplies()
    {
        var boom = DefaultRule(Text, "never written");
        boom.TargetField = "sprk_boom";
        boom.ExecutionOrder = 1;
        var fine = DefaultRule(Text, "still applied");
        fine.TargetField = "sprk_fine";
        fine.ExecutionOrder = 2;
        var throwingProtectedSet = new ThrowingSet("sprk_boom");

        var (target, warnings) = Run(new[] { boom, fine }, source: null, throwingProtectedSet);

        target.Contains("sprk_boom").Should().BeFalse();
        target["sprk_fine"].Should().Be("still applied");
        var warning = warnings.Should().ContainSingle().Subject;
        warning.Should().Contain("failed and was skipped");
        warning.Should().NotContain("simulated engine failure");
    }

    [Fact]
    public void Apply_RuleWithNoTargetField_IsSkippedWithOneWarning()
    {
        var rule = DefaultRule(Text, "X");
        rule.TargetField = "   ";

        var (target, warnings) = Run(rule, source: null);

        target.Attributes.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("no target field");
    }

    [Fact]
    public void Apply_UnknownMappingType_IsSkippedWithOneWarning()
    {
        var (target, warnings) = Run(RuleFor(mappingType: 9, Text), SourceWith(SourceColumn, "v"));

        target.Attributes.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("unknown mapping type");
    }

    // ─────────────────────────────────────────────── helpers ───────────────────────────────────────────────

    private static (Entity Target, List<string> Warnings) Run(
        FieldMappingRuleEntity rule, Entity? source, IReadOnlySet<string>? protectedColumns = null) =>
        Run(new[] { rule }, source, protectedColumns);

    private static (Entity Target, List<string> Warnings) Run(FieldMappingRuleEntity rule) => Run(new[] { rule }, source: null);

    private static (Entity Target, List<string> Warnings) Run(
        IEnumerable<FieldMappingRuleEntity> rules,
        Entity? source,
        IReadOnlySet<string>? protectedColumns = null,
        IReadOnlySet<string>? moneyAttributes = null)
    {
        // Field mapping task 018c: the overwrite entry point (Apply) is deleted with the Office creation core; the engine's
        // one entry point is fill-only, and over an EMPTY target (nothing supplied) it writes exactly what Apply wrote.
        var target = new Entity("sprk_matter");
        var warnings = new List<string>();

        CreateTimeFieldMapping.ApplyFillOnly(
            rules, target, source, protectedColumns ?? NoProtectedColumns, warnings, NullLogger.Instance, moneyAttributes);

        return (target, warnings);
    }

    private static Entity SourceWith(string column, object value) => new("sprk_matter") { [column] = value };

    private static FieldMappingRuleEntity RuleFor(int mappingType, int targetFieldType) => new()
    {
        Name = "rule",
        SourceField = SourceColumn,
        TargetField = TargetColumn,
        MappingType = mappingType,
        TargetFieldType = targetFieldType,
        IsActive = true,
    };

    private static FieldMappingRuleEntity CopyRule(int targetFieldType, string sourceField = SourceColumn)
    {
        var rule = RuleFor(Copy, targetFieldType);
        rule.SourceField = sourceField;
        return rule;
    }

    private static FieldMappingRuleEntity DefaultRule(int targetFieldType, string? defaultValue)
    {
        var rule = RuleFor(Default, targetFieldType);
        rule.DefaultValue = defaultValue;
        return rule;
    }

    private static FieldMappingRuleEntity ExpressionRule(int mappingType, int targetFieldType, string? expression)
    {
        var rule = RuleFor(mappingType, targetFieldType);
        rule.Expression = expression;
        return rule;
    }

    /// <summary>
    /// A protected-column set whose lookup throws for one column — the only seam to make a single rule fail. The FIRST
    /// lookup of that column answers <c>false</c>: under fill-only (the engine's one entry point since task 018c) that is the
    /// supplied-column snapshot, which reads the set before any rule runs; the rule's own lookup then throws, inside the
    /// per-rule guard this test pins.
    /// </summary>
    private sealed class ThrowingSet(string throwOn) : IReadOnlySet<string>
    {
        private int _lookups;

        public bool Contains(string item) =>
            item == throwOn && ++_lookups > 1 ? throw new InvalidOperationException("simulated engine failure") : false;

        public int Count => 0;
        public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool IsProperSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool Overlaps(IEnumerable<string> other) => throw new NotSupportedException();
        public bool SetEquals(IEnumerable<string> other) => throw new NotSupportedException();
    }
}
