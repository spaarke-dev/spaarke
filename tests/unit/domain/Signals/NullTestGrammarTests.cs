using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 061 (D-54): the rule-body grammar's NULL TEST, <c>{"null": true}</c> / <c>{"notNull": true}</c> on the row's own column.
/// It exists because "active work assignment past its response due date AND no response recorded" has no other spelling: the
/// grammar compared values, never absence. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): a null test that compiled to the
/// wrong FetchXML operator, or fell through a consumer as "true", would raise a chase Signal for an assignment that was already
/// answered (or hide one that was not) while reading as a working predicate.
/// </summary>
/// <remarks>
/// The live-metadata half of these assertions (sprk_respondedon is Date Only, sprk_responseduedate is Date Only) is paired with the
/// real schema by <c>SignalPredicateTests.DateOnlyColumns_MatchTheLiveSchema_InBothDirections</c> and by
/// <c>DoLanePolicySeamTests</c>.
/// </remarks>
[Trait("status", "new")]
public class NullTestGrammarTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt));

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), Compiler(), NullLogger<PolicyVersionValidator>.Instance);

    private static string SubjectOnly(string subject, string whenJson) =>
        "{\"type\":\"Existence\",\"subject\":\"" + subject + "\",\"when\":" + whenJson + ",\"all\":[]}";

    /// <summary>The D-54 body: an active work assignment past its response due date with no response recorded.</summary>
    internal const string WorkAssignmentNoResponseBody =
        """{"type":"Existence","subject":"sprk_workassignment","when":{"statecode":0,"sprk_responseduedate":{"<":"now"},"sprk_respondedon":{"null":true}},"all":[]}""";

    private static List<XElement> RootConditions(CompiledPredicate compiled) =>
        XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!.Elements("condition").ToList();

    // ── The compiler ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("null", "null")]
    [InlineData("notNull", "not-null")]
    public void Compile_NullTest_EmitsTheFetchOperatorOnTheSubjectsOwnColumn_WithNoValueAndNoEntityName(string member, string fetchOperator)
    {
        var compiled = Compiler().Compile(SubjectOnly("sprk_workassignment", "{\"statecode\":0,\"sprk_respondedon\":{\"" + member + "\":true}}"));

        var condition = RootConditions(compiled).Single(c => c.Attribute("attribute")!.Value == "sprk_respondedon");
        condition.Attribute("operator")!.Value.Should().Be(fetchOperator);
        condition.Attribute("value").Should().BeNull("a null test names no operand");
        condition.Attribute("entityname").Should().BeNull("it tests the SUBJECT's own column");
        condition.Elements("value").Should().BeEmpty();
    }

    [Fact]
    public void Compile_NullTest_AndTheNotExistsAntiJoin_StayTwoDistinctConditions()
    {
        // The anti-join also compiles to operator="null", but on the link alias's key. A subject column test must not be
        // mistaken for it (and must not be swallowed by it): one of each, told apart by entityname.
        const string body = """
            {"type":"Existence","subject":"sprk_matter","when":{"sprk_closeddate":{"null":true}},"all":[
              {"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}}]}
            """;

        var conditions = RootConditions(Compiler().Compile(body)).Where(c => c.Attribute("operator")!.Value == "null").ToList();

        conditions.Should().HaveCount(2);
        var subjectTest = conditions.Single(c => c.Attribute("entityname") is null);
        subjectTest.Attribute("attribute")!.Value.Should().Be("sprk_closeddate");
        var antiJoin = conditions.Single(c => c.Attribute("entityname") is not null);
        antiJoin.Attribute("entityname")!.Value.Should().Be("c0");
        antiJoin.Attribute("attribute")!.Value.Should().Be("sprk_budgetrevisionid");
    }

    [Fact]
    public void Compile_NullTestInAClauseFilter_LandsInTheLinkEntityFilter_NotTheRootFilter()
    {
        const string body = """
            {"type":"Existence","subject":"sprk_matter","all":[
              {"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_reviewoutcome":{"null":true}}}]}
            """;

        var entity = XElement.Parse(Compiler().Compile(body).FetchXml).Element("entity")!;

        entity.Element("link-entity")!.Element("filter")!.Elements("condition")
            .Should().ContainSingle(c => c.Attribute("attribute")!.Value == "sprk_reviewoutcome" && c.Attribute("operator")!.Value == "null");
        entity.Element("filter")?.Elements("condition").Should().NotContain(c => c.Attribute("attribute")!.Value == "sprk_reviewoutcome");
    }

    [Fact]
    public void Compile_NullTest_NeverPinsItsField_SoNoTemplateCanNameIt()
    {
        // Section 0.3: the rule reads "no value", so there is no value for a sentence to state.
        var compiled = Compiler().Compile(WorkAssignmentNoResponseBody);

        compiled.TemplateEligibleFields.Should().Contain("statecode").And.NotContain("sprk_respondedon");
        compiled.UnpinnedTemplateFields.Should().Contain("sprk_respondedon");
    }

    [Theory]
    [InlineData("""{"null":false}""")]
    [InlineData("""{"null":"x"}""")]
    [InlineData("""{"null":5}""")]
    [InlineData("""{"null":null}""")]
    [InlineData("""{"notNull":false}""")]
    [InlineData("""{"notNull":"2026-10-01"}""")]
    [InlineData("""{"null":true,"notNull":true}""")]
    [InlineData("""{"null":true,"<":"now"}""")]
    [InlineData("""{"isNull":true}""")]
    [InlineData("""{"Null":true}""")]
    public void Compile_NullTestWithAnOperandOrAnythingElse_IsRefused(string value)
    {
        var act = () => Compiler().Compile(SubjectOnly("sprk_workassignment", "{\"sprk_respondedon\":" + value + "}"));

        act.Should().Throw<PredicateCompilationException>();
    }

    [Theory]
    [InlineData("""{"null":false}""")]
    [InlineData("""{"null":"x"}""")]
    [InlineData("""{"notNull":0}""")]
    public void CompileSchemaValidated_NullTestWithAnOperand_IsRefusedByTheCompilerOnItsOwn(string value)
    {
        // CompileSchemaValidated skips the schema, so this proves the COMPILER refuses an operand (defence in depth).
        var act = () => Compiler().CompileSchemaValidated(SubjectOnly("sprk_workassignment", "{\"sprk_respondedon\":" + value + "}"), subjectId: null);

        act.Should().Throw<PredicateCompilationException>();
    }

    [Fact]
    public void Compile_DoLaneWorkAssignmentBody_IsExactlyTheFourConditionsTheRuleStates()
    {
        var conditions = RootConditions(Compiler().Compile(WorkAssignmentNoResponseBody))
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, (string?)c.Attribute("value"))).ToList();

        conditions.Should().Equal(
            ("statecode", "eq", "0"),
            ("sprk_responseduedate", "lt", "2026-10-14T03:00:00Z"),
            ("sprk_respondedon", "null", null));
    }

    // ── Save-time validation ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"null":true}""", true)]
    [InlineData("""{"notNull":true}""", true)]
    [InlineData("""{"null":false}""", false)]
    [InlineData("""{"null":"x"}""", false)]
    [InlineData("""{"null":true,"notNull":true}""", false)]
    public void SchemaValidator_AcceptsOnlyTheTwoValueFreeTests(string value, bool valid)
    {
        var result = new RuleBodySchemaValidator().Validate(RuleType.Existence, SubjectOnly("sprk_workassignment", "{\"sprk_respondedon\":" + value + "}"));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void ValidateForSave_TheWorkAssignmentBody_IsValid_WithALiteralTemplate()
    {
        var result = Validator().ValidateForSave("Existence", WorkAssignmentNoResponseBody, "This work assignment is past its response date and no response is recorded.");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForSave_ATemplateNamingTheNullTestedField_IsRefused()
    {
        // A sentence cannot state a value the rule never read.
        var result = Validator().ValidateForSave("Existence", WorkAssignmentNoResponseBody, "Responded on {{sprk_respondedon}}.");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        // The reason must name the null test, not an 'exists' clause this body does not have (review F3).
        result.Errors.Should().ContainSingle(e => e.Contains("'when' null test", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"null":true}""", true)]
    [InlineData("""{"null":"x"}""", false)]
    public void ValidateForSave_AThresholdWhenSharesTheSameNullTestGrammar(string value, bool valid)
    {
        // Threshold keeps a verbatim copy of the Existence filter grammar (ThresholdRuleTests pins the copy), so its 'when' takes
        // the null test too and refuses the same malformed ones.
        var body = "{\"type\":\"Threshold\",\"subject\":\"sprk_matter\",\"threshold\":{\"field\":\"sprk_budgetutilizationpercent\",\"operator\":\">=\",\"value\":90},\"when\":{\"sprk_closeddate\":" + value + "}}";

        Validator().ValidateForSave("Threshold", body, null).IsValid.Should().Be(valid);
    }

    // ── The evaluator: bounds, widening, facts ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluator_ADateOnlyColumnUnderANullTest_IsNotARelativeBound_SoItIsNeverWidenedOrReJudged()
    {
        // sprk_respondedon IS a Date Only column, so the compiler reports it; but a null test is not a date bound. The per-item
        // judge (SignalEvaluator.Holds) must not treat it as one, and the widening must leave its condition exactly as compiled.
        var compiled = Compiler().Compile(WorkAssignmentNoResponseBody);
        compiled.DateOnlyWhenFields.Should().BeEquivalentTo("sprk_responseduedate", "sprk_respondedon");

        using var body = System.Text.Json.JsonDocument.Parse(WorkAssignmentNoResponseBody);
        var bounds = SignalEvaluator.ReadDateOnlyBounds(body.RootElement.GetProperty("when"), compiled.DateOnlyWhenFields);

        bounds.Should().ContainSingle().Which.Field.Should().Be("sprk_responseduedate");

        var fetch = XDocument.Parse(compiled.FetchXml);
        var entity = fetch.Root!.Element("entity")!;
        SignalEvaluator.WidenDateOnlyBounds(entity, bounds, compiled.WindowAnchorUtc);
        entity.Element("filter")!.Elements("condition")
            .Single(c => c.Attribute("attribute")!.Value == "sprk_respondedon")
            .Attribute("operator")!.Value.Should().Be("null");
        entity.Element("filter")!.Elements("condition").Count(c => c.Attribute("attribute")!.Value == "sprk_responseduedate")
            .Should().Be(1, "the response due date bound is replaced by ONE widened upper bound (a '<' has no lower)");
    }

    [Theory]
    [InlineData("""{">=":"now-2d"}""", "ge", true)]
    [InlineData("""{">":"now-2d"}""", "gt", true)]
    [InlineData("""{"<=":"now-2d"}""", "le", true)]
    [InlineData("""{"<":"now-2d"}""", "lt", true)]
    [InlineData("""{"<>":"now-2d"}""", "ne", true)]
    [InlineData("""{"=":"now-2d"}""", "eq", true)]
    [InlineData("""{"null":true}""", "null", false)]
    [InlineData("""{"notNull":true}""", "not-null", false)]
    public void JudgeAndCompiler_AgreeOnEveryOperatorOfADateOnlyWhenField(string value, string compiledOperator, bool judgedPerItem)
    {
        // The compiler decides the query; SignalEvaluator.ReadDateOnlyBounds decides which conditions are re-judged per item (D-25).
        // They must agree on every operator the grammar admits: a comparison is compiled to X AND judged as X; a null test is compiled
        // to null / not-null and NEVER judged as a date (it names no date, and re-judging it would drop every row, since 'Holds'
        // answers false for an absent value under any operator but 'ne').
        var body = SubjectOnly("sprk_workassignment", "{\"sprk_responseduedate\":" + value + "}");
        var compiled = Compiler().Compile(body);
        using var doc = System.Text.Json.JsonDocument.Parse(body);

        var bounds = SignalEvaluator.ReadDateOnlyBounds(doc.RootElement.GetProperty("when"), compiled.DateOnlyWhenFields);

        RootConditions(compiled).Single(c => c.Attribute("attribute")!.Value == "sprk_responseduedate")
            .Attribute("operator")!.Value.Should().Be(compiledOperator);
        if (judgedPerItem) bounds.Should().ContainSingle().Which.Operator.Should().Be(compiledOperator);
        else bounds.Should().BeEmpty();
    }

    // ── The describer ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Describer_StatesTheNullTestInWords_AndFormally()
    {
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, new FakeTimeProvider(RunAt)), NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);

        var result = await describer.DescribeAsync(WorkAssignmentNoResponseBody, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        var when = result.Description!.Clauses.Single();
        when.Text.Should().Contain("it has no response recorded");
        when.Formal.Should().Contain("sprk_respondedon is null");
    }

    [Fact]
    public async Task Describer_NotNull_ReadsAsHavingOne()
    {
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, new FakeTimeProvider(RunAt)), NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);

        var result = await describer.DescribeAsync(
            SubjectOnly("sprk_workassignment", """{"statecode":0,"sprk_respondedon":{"notNull":true}}"""), CancellationToken.None);

        var when = result.Description!.Clauses.Single();
        when.Text.Should().Contain("it has a response recorded");
        when.Formal.Should().Contain("sprk_respondedon is not null");
    }

    [Fact]
    public async Task Describer_ABodyWithANullTestOperand_IsRefused_NotDescribed()
    {
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, new FakeTimeProvider(RunAt)), NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);

        var result = await describer.DescribeAsync(SubjectOnly("sprk_workassignment", """{"sprk_respondedon":{"null":"x"}}"""), CancellationToken.None);

        result.IsRefused.Should().BeTrue();
    }
}
