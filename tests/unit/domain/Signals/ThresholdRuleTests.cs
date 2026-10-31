using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// The Threshold rule type (task 025; D-16; spec FR-05): its JSON Schema, its compiler, the save and evaluation gates and the
/// describer. The job-level proof (a Threshold policy through the unchanged <see cref="SignalReevaluationJob"/>) is in
/// <c>SignalReevaluationJobTests</c>; the real-schema pairing is <c>SignalPredicateTests</c> (seam).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). Every refusal below guards one input that would otherwise save
/// as a rule that asserts something it never tested (section 0.3) or compile into a query that quietly returns the wrong rows.
/// The matter column names asserted here are paired with <c>SignalPredicateTests.ThresholdColumns_AreNumericOnTheLiveSchema</c>.
/// </remarks>
[Trait("status", "new")]
public class ThresholdRuleTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 10, 11, 3, 0, 0, TimeSpan.Zero);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(Anchor));

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), Compiler(), NullLogger<PolicyVersionValidator>.Instance);

    private static string Body(string field = "sprk_budgetutilizationpercent", string op = ">=", string value = "90", string extra = "") =>
        "{\"type\":\"Threshold\",\"subject\":\"sprk_matter\",\"threshold\":{\"field\":\"" + field + "\",\"operator\":\"" + op +
        "\",\"value\":" + value + "}" + extra + "}";

    private static XElement Fetch(CompiledPredicate compiled) => XElement.Parse(compiled.FetchXml);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "tests", "fixtures", "signals", name));

    private static string Normalize(string xml) => xml.Replace("\r\n", "\n").Trim();

    // ── Accepted: one numeric comparison on the subject, one FetchXML condition ───────────────────────────────

    [Fact]
    public void UtilizationAtLeast90_ValidatesAndCompilesToExactlyOneCondition()
    {
        Validator().ValidateForSave("Threshold", Body(), messageTemplate: null).IsValid.Should().BeTrue();

        var compiled = Compiler().Compile(Body(), null, RuleType.Threshold);

        compiled.SubjectEntity.Should().Be("sprk_matter");
        compiled.SubjectIdAttribute.Should().Be("sprk_matterid");
        var fetch = Fetch(compiled);
        var condition = fetch.Descendants("condition").Should().ContainSingle().Subject;
        (condition.Attribute("attribute")!.Value, condition.Attribute("operator")!.Value, condition.Attribute("value")!.Value)
            .Should().Be(("sprk_budgetutilizationpercent", "ge", "90"));
        fetch.Descendants("link-entity").Should().BeEmpty("a threshold reads no other entity");
        compiled.QuietWindowDays.Should().Be(PredicateCompiler.DefaultQuietWindowDays);
        compiled.DateOnlyWhenFields.Should().BeEmpty();
    }

    [Theory]
    [InlineData(">", "gt")]
    [InlineData(">=", "ge")]
    [InlineData("<", "lt")]
    [InlineData("<=", "le")]
    public void EveryThresholdOperator_MapsToItsFetchXmlOperator(string op, string fetchOperator)
    {
        var compiled = Compiler().Compile(Body(op: op), null, RuleType.Threshold);

        Fetch(compiled).Descendants("condition").Single().Attribute("operator")!.Value.Should().Be(fetchOperator);
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_budgetutilizationpercent")]
    [InlineData("sprk_matter", "sprk_totalbudget")]
    [InlineData("sprk_matter", "sprk_totalspendtodate")]
    [InlineData("sprk_project", "sprk_budgetutilizationpercent")]
    [InlineData("sprk_project", "sprk_totalbudget")]
    [InlineData("sprk_project", "sprk_totalspendtodate")]
    public void EveryVerifiedColumn_OnMatterAndProject_Compiles_AndIsMarkedForEvaluationFromTheBaseRows(string subject, string field)
    {
        var body = Body(field: field, value: "50000").Replace("\"sprk_matter\"", $"\"{subject}\"", StringComparison.Ordinal);

        Validator().ValidateForSave("Threshold", body, null).IsValid.Should().BeTrue();
        var compiled = Compiler().Compile(body, null, RuleType.Threshold);

        compiled.SubjectEntity.Should().Be(subject);
        compiled.SubjectIdAttribute.Should().Be(subject + "id");
        Fetch(compiled).Descendants("condition").Single().Attribute("attribute")!.Value.Should().Be(field);
        compiled.Threshold.Should().Be(new ThresholdComparison(field, "ge", "50000", 50000m, BaseDerived: true));
    }

    [Fact]
    public void TheThresholdColumns_AreExactlyTheSixDerivedOnes_AndTheTableIsListedForEveryOne() =>
        PredicateCompiler.ThresholdColumns.Should().BeEquivalentTo(new[]
        {
            ("sprk_matter", "sprk_budgetutilizationpercent"), ("sprk_matter", "sprk_totalbudget"), ("sprk_matter", "sprk_totalspendtodate"),
            ("sprk_project", "sprk_budgetutilizationpercent"), ("sprk_project", "sprk_totalbudget"), ("sprk_project", "sprk_totalspendtodate"),
        });

    [Fact]
    public void EveryThresholdColumn_IsOneTheEvaluatorDerivesFromTheBaseRows() =>
        PredicateCompiler.ThresholdColumns.Should().OnlyContain(c => Sprk.Bff.Api.Services.Finance.FinanceBaseValues.IsDerived(c.Entity, c.Column),
            "a Threshold column the evaluator reads from the stored row would be judged on a value nothing keeps current (D-122)");

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void AWhenOnADerivedColumn_InAThresholdBody_IsRefused_BecauseItWouldReadTheStaleStoredCopy(string subject)
    {
        var body = Body(extra: ",\"when\":{\"sprk_totalspendtodate\":{\">=\":5000}}").Replace("\"sprk_matter\"", $"\"{subject}\"", StringComparison.Ordinal);

        var result = Validator().ValidateForSave("Threshold", body, null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CompileRefused);
        result.Errors.Should().ContainSingle().Which.Should().Contain("when.sprk_totalspendtodate").And.Contain("not kept current");
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void AWhenOnADerivedColumn_InAnExistenceBody_IsRefusedToo_BecauseItWouldReadTheSameStaleStoredCopy(string subject)
    {
        var body = ("""{"type":"Existence","subject":"sprk_matter","when":{"sprk_totalspendtodate":{">=":5000}},"all":[]}""")
            .Replace("\"sprk_matter\"", $"\"{subject}\"", StringComparison.Ordinal);

        Compiler().Invoking(c => c.Compile(body)).Should().Throw<PredicateCompilationException>()
            .Which.Message.Should().Contain("when.sprk_totalspendtodate").And.Contain("not kept current");
    }

    [Fact]
    public void AnExistenceBody_MayStillScopeOnAColumnThatIsNotDerived() =>
        Compiler().Invoking(c => c.Compile(
            """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""")).Should().NotThrow();

    [Theory]
    [InlineData("12.50", "12.50")]
    [InlineData("1e2", "100")]      // canonical decimal, never exponent notation in FetchXML
    [InlineData("-5", "-5")]
    [InlineData("0", "0")]
    public void TheDeclaredNumber_IsEmittedCanonically(string written, string emitted)
    {
        var compiled = Compiler().Compile(Body(value: written), null, RuleType.Threshold);

        Fetch(compiled).Descendants("condition").Single().Attribute("value")!.Value.Should().Be(emitted);
    }

    [Fact]
    public void ASubjectScopeAndASubjectNarrowing_AddTheirOwnConditions_ToTheSameFilter()
    {
        var subject = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
        var body = Body(extra: ",\"when\":{\"statecode\":0}");

        var compiled = Compiler().Compile(body, subject, RuleType.Threshold);

        var filter = Fetch(compiled).Element("entity")!.Element("filter")!;
        filter.Attribute("type")!.Value.Should().Be("and");
        filter.Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value))
            .Should().BeEquivalentTo(new[]
            {
                ("sprk_matterid", "eq"), ("sprk_budgetutilizationpercent", "ge"), ("statecode", "eq"),
            });
    }

    [Fact]
    public void AnEmptyWhen_IsAllowed_BecauseTheComparisonItselfScopesTheSubject() =>
        Validator().ValidateForSave("Threshold", Body(extra: ",\"when\":{}"), null).IsValid.Should().BeTrue();

    [Fact]
    public void TheQuietWindowKnob_IsCarried()
    {
        var compiled = Compiler().Compile(Body(extra: ",\"quietWindowDays\":30"), null, RuleType.Threshold);

        compiled.QuietWindowDays.Should().Be(30);
    }

    [Fact]
    public void TheFixtureBody_CompilesToTheHandWrittenReferenceQuery_ByteForByte()
    {
        var compiled = Compiler().Compile(Fixture("threshold-matter-spend.rulebody.json"), null, RuleType.Threshold);

        Normalize(compiled.FetchXml).Should().Be(Normalize(Fixture("threshold-matter-spend.reference.fetchxml")));
    }

    // ── Refused at save: comparing columns, arithmetic, another entity, extra comparisons ─────────────────────

    [Theory]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate","operator":">=","value":"sprk_totalbudget"}}""", "/threshold/value")]   // two columns
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate","operator":">=","value":"$budget"}}""", "/threshold/value")]         // a reference
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate / sprk_totalbudget","operator":">=","value":0.9}}""", "/threshold/field")] // a ratio
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate - sprk_totalbudget","operator":">","value":0}}""", "/threshold/field")] // arithmetic
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_budget.sprk_totalbudget","operator":">","value":0}}""", "/threshold/field")]            // another entity
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":{"+":[1,2]}}}""", "/threshold/value")]              // an expression
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":"=","value":5}}""", "/threshold/operator")]                      // not a threshold
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":"<>","value":5}}""", "/threshold/operator")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5,"and":{"field":"sprk_totalspendtodate","operator":"<","value":1}}}""", "/threshold/and")] // a second comparison
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">="}}""", "/threshold")]                                      // no value
    [InlineData("""{"type":"Threshold","subject":"sprk_matter"}""", "")]                                                                                                          // no comparison
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"all":[]}""", "/all")]                         // no clauses
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":1}}]}""", "/all")] // another entity
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"exists":"sprk_communication"}""", "/exists")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"when":{"sprk_name":"$x"}}""", "/when/sprk_name")]  // the shared per-value rules
    public void ABodyThatComparesColumns_UsesArithmetic_OrReadsAnotherEntity_IsRefusedWithAFieldLevelError(string body, string pointer)
    {
        var result = Validator().ValidateForSave("Threshold", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().Contain(e => e.StartsWith(pointer + ":", StringComparison.Ordinal),
            "the error must name the offending part of the body: " + string.Join(" | ", result.Errors));
    }

    [Theory]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"then":{}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"messageTemplate":"x"}""")]
    [InlineData("""{"type":"Existence","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5}}""")]
    public void ReservedMembersAndAWrongTypeDiscriminator_AreRefused(string body)
    {
        var result = Validator().ValidateForSave("Threshold", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.SchemaInvalid);
    }

    [Fact]
    public void AnExistenceBodyUnderTheThresholdType_AndAThresholdBodyUnderTheExistenceType_AreBothRefused()
    {
        const string existence = """{"type":"Existence","subject":"sprk_event","when":{"statecode":0},"all":[]}""";

        Validator().ValidateForSave("Threshold", existence, null).IsValid.Should().BeFalse();
        Validator().ValidateForSave("Existence", Body(), null).IsValid.Should().BeFalse();
    }

    // ── Refused by the compiler: a column or subject that is not a verified numeric one ───────────────────────

    [Theory]
    [InlineData("sprk_mattername")]       // text
    [InlineData("sprk_openeddate")]       // a date
    [InlineData("sprk_regardingmatter")]  // not a matter column at all
    [InlineData("statecode")]
    public void AColumnOutsideTheVerifiedNumericList_IsRefusedByTheCompiler_NamingTheAllowedOnes(string field)
    {
        var result = Validator().ValidateForSave("Threshold", Body(field: field), null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CompileRefused);
        result.Errors.Should().ContainSingle().Which.Should().Contain("sprk_budgetutilizationpercent").And.Contain("verified numeric");
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_servicerequest")]
    public void ASubjectOtherThanTheMatter_IsRefused(string subject)
    {
        var body = Body().Replace("\"sprk_matter\"", $"\"{subject}\"", StringComparison.Ordinal);

        var result = Validator().ValidateForSave("Threshold", body, null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CompileRefused);
    }

    [Theory]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":"sprk_totalspendtodate"}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":"=","value":5}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":5},"all":[]}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":1e999999}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":"90"}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter"}""")]
    public void TheCompilerRefusesTheseOnItsOwn_WhenTheSchemaIsSkipped(string body)
    {
        // CompileSchemaValidated skips the schema (PolicyVersionValidator evaluates it once), so the compiler must hold the line itself.
        Compiler().Invoking(c => c.CompileSchemaValidated(body, subjectId: null, RuleType.Threshold))
            .Should().Throw<PredicateCompilationException>();
    }

    // ── Switch stays refused; the closed set is unchanged (FR-05) ─────────────────────────────────────────────

    [Theory]
    [InlineData("Switch")]
    [InlineData("100000001")]
    public void Switch_IsRefusedAsRuleTypeUnsupported_AtSaveAndAtEvaluation(string ruleType)
    {
        var save = Validator().ValidateForSave(ruleType, Body(), null);
        save.IsValid.Should().BeFalse();
        save.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.RuleTypeUnsupported);

        var snapshot = new PolicyVersionSnapshot(Guid.NewGuid(), "POL-X", ruleType, Body(), null);
        Validator().TryPrepareForEvaluation(snapshot, subjectId: null, out var compiled).Should().BeFalse();
        compiled.Should().BeNull();
    }

    [Fact]
    public void TheCompilerItself_RefusesSwitch_WithItsOwnExceptionType() =>
        Compiler().Invoking(c => c.Compile(Body(), null, RuleType.Switch)).Should().Throw<PredicateCompilationException>();

    [Fact]
    public void TheClosedRuleTypeSet_IsUnchanged() =>
        // ebilling-r1 task 070 (FR-24): the Ontology domain is unchanged; the eleven Billing members are pinned in
        // Billing/BillingRuleCatalogueTests.
        Enum.GetValues<RuleType>().Where(t => RuleDomains.DomainOf(t) == RuleDomain.Ontology)
            .Should().BeEquivalentTo(new[] { RuleType.Threshold, RuleType.Switch, RuleType.Existence });

    // ── The evaluation gate hands the evaluator the same CompiledPredicate contract ───────────────────────────

    [Fact]
    public void TryPrepareForEvaluation_ReturnsTheCompiledPredicate_ForAThresholdVersion_ByNameOrOptionValue()
    {
        foreach (var ruleType in new[] { "Threshold", "100000000" })
        {
            var snapshot = new PolicyVersionSnapshot(Guid.NewGuid(), "POL-SPEND", ruleType, Body(), null);

            Validator().TryPrepareForEvaluation(snapshot, subjectId: null, out var compiled).Should().BeTrue();

            compiled!.SubjectEntity.Should().Be("sprk_matter");
            compiled.FetchXml.Should().Contain("sprk_budgetutilizationpercent");
        }
    }

    [Fact]
    public void AnInvalidThresholdBody_IsRefusedAtEvaluationToo_AndCompilesToNothing()
    {
        var snapshot = new PolicyVersionSnapshot(Guid.NewGuid(), "POL-SPEND", "Threshold", Body(field: "sprk_mattername"), null);

        Validator().TryPrepareForEvaluation(snapshot, subjectId: null, out var compiled).Should().BeFalse();
        compiled.Should().BeNull();
    }

    // ── Section 0.3: a template names only what the rule read ─────────────────────────────────────────────────

    [Fact]
    public void ATemplate_MayNameTheComparedColumn_AndAWhenField()
    {
        var body = Body(extra: ",\"when\":{\"statecode\":0}");

        var result = Validator().ValidateForSave(
            "Threshold", body, "Utilization is {{sprk_budgetutilizationpercent}} on a matter with status {{statecode}}.");

        result.IsValid.Should().BeTrue();
        Compiler().Compile(body, null, RuleType.Threshold).TemplateEligibleFields
            .Should().BeEquivalentTo("sprk_budgetutilizationpercent", "statecode");
    }

    [Theory]
    [InlineData("Spend is {{sprk_totalspendtodate}}.")]   // a matter column the rule did not read
    [InlineData("Over {{threshold}} percent.")]           // the declared number is not a token
    [InlineData("Over {{value}} percent.")]
    public void ATemplate_NamingAnythingElse_IsRefused_AsOutsideTheReadSet(string template)
    {
        var result = Validator().ValidateForSave("Threshold", Body(), template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
    }

    // ── Invariants that keep the allow-list honest without a live call ────────────────────────────────────────

    [Fact]
    public void EveryThresholdColumn_IsOnATableTheEvaluatorReadsAtGlobalDepth() =>
        PredicateCompiler.ThresholdColumns.Select(c => c.Entity).Distinct()
            .Should().OnlyContain(e => PredicateCompiler.EvaluatorGlobalReadableEntities.Contains(e));

    [Fact]
    public void TheThresholdSchemaFilterGrammar_IsAVerbatimCopyOfTheExistenceSchemas()
    {
        // The Threshold 'when' must not carry what an Existence clause value may not: both files hold the same per-value
        // grammar, and this fails the moment one is edited without the other.
        JsonObject Defs(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Schemas", file)))!["$defs"]!.AsObject();

        var existence = Defs("existence-rule.schema.json");
        var threshold = Defs("threshold-rule.schema.json");

        threshold.Select(kv => kv.Key).Should().BeEquivalentTo("filterConditions", "filterValue", "nullTest", "dateRange", "relativeDate", "scalar");
        foreach (var (name, node) in threshold)
        {
            JsonNode.DeepEquals(node, existence[name]).Should().BeTrue($"$defs/{name} must match existence-rule.schema.json");
        }
    }

    // ── K1: a value outside the column's Dataverse range is refused at save ──────────────────────────────────

    [Theory]
    [InlineData("sprk_totalbudget", "922337203685478")]
    [InlineData("sprk_totalspendtodate", "-922337203685478")]
    [InlineData("sprk_budgetutilizationpercent", "100000000001")]
    [InlineData("sprk_budgetutilizationpercent", "-100000000001")]
    public void AValueOutsideTheColumnsRange_IsRefusedAtSave_ByTheValidatorAndTheCompiler(string field, string value)
    {
        Compiler().Invoking(c => c.Compile(Body(field: field, value: value), null, RuleType.Threshold))
            .Should().Throw<PredicateCompilationException>().WithMessage("*outside the range Dataverse holds*");
        Compiler().Invoking(c => c.CompileSchemaValidated(Body(field: field, value: value), null, RuleType.Threshold))
            .Should().Throw<PredicateCompilationException>().WithMessage("*outside the range Dataverse holds*");

        Validator().ValidateForSave("Threshold", Body(field: field, value: value), messageTemplate: null).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("sprk_totalbudget", "922337203685477")]
    [InlineData("sprk_totalspendtodate", "-922337203685477")]
    [InlineData("sprk_budgetutilizationpercent", "100000000000")]
    [InlineData("sprk_budgetutilizationpercent", "-100000000000")]
    public void AValueAtTheColumnsLimit_IsAccepted(string field, string value)
    {
        Compiler().Invoking(c => c.Compile(Body(field: field, value: value), null, RuleType.Threshold)).Should().NotThrow();
    }

    // ── The describer (task 026 hand-off) ─────────────────────────────────────────────────────────────────────

    private static RuleBodyDescriber Describer()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return new RuleBodyDescriber(Validator(), entities.Object, NullLogger<RuleBodyDescriber>.Instance);
    }

    [Fact]
    public async Task TheDescriber_StatesTheThresholdInWords_AndInFormalForm()
    {
        var result = await Describer().DescribeAsync(Body(), CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        var line = result.Description!.Clauses.Should().ContainSingle().Subject;
        line.ClauseId.Should().Be("threshold");
        line.Kind.Should().Be("Threshold");
        line.Text.Should().Be("The matter's budget utilization (percent of budget spent) is at least 90.");
        line.Formal.Should().Be("sprk_matter.sprk_budgetutilizationpercent >= 90");
    }

    [Theory]
    [InlineData(">", "is more than")]
    [InlineData(">=", "is at least")]
    [InlineData("<", "is less than")]
    [InlineData("<=", "is at most")]
    public async Task TheDescriber_UsesNumericWording_NotDateWording(string op, string words)
    {
        var result = await Describer().DescribeAsync(Body(field: "sprk_totalbudget", op: op, value: "250000"), CancellationToken.None);

        result.Description!.Clauses.Single().Text.Should().Be($"The matter's total budget {words} 250000.");
    }

    [Fact]
    public async Task TheDescriber_PrintsTheCompilersCanonicalNumber_NotTheRawJson()
    {
        // "1e2" reads 100; 32 significant digits read the rounded decimal the compiled query actually compares.
        var exponent = await Describer().DescribeAsync(Body(value: "1e2"), CancellationToken.None);
        exponent.Description!.Clauses.Single().Text.Should().Be("The matter's budget utilization (percent of budget spent) is at least 100.");
        exponent.Description.Clauses.Single().Formal.Should().Be("sprk_matter.sprk_budgetutilizationpercent >= 100");

        const string longValue = "90.123456789012345678901234567891";
        var compiled = Compiler().Compile(Body(value: longValue), null, RuleType.Threshold);
        var compared = Fetch(compiled).Descendants("condition").Single().Attribute("value")!.Value;
        compared.Should().NotBe(longValue, "the compiler rounds to System.Decimal");

        var described = await Describer().DescribeAsync(Body(value: longValue), CancellationToken.None);
        described.Description!.Clauses.Single().Text.Should().Be($"The matter's budget utilization (percent of budget spent) is at least {compared}.");
        described.Description.Clauses.Single().Formal.Should().EndWith(">= " + compared);
    }

    [Theory]
    [InlineData(">=", "is at least")]
    [InlineData(">", "is more than")]
    [InlineData("<=", "is at most")]
    [InlineData("<", "is less than")]
    public async Task TheDescriber_UsesNumericWording_ForANumericWhenComparison_InThresholdAndExistenceBodies(string op, string words)
    {
        var existence = "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"when\":{\"statuscode\":{\"" + op + "\":5000}},\"all\":[]}";

        var result = await Describer().DescribeAsync(existence, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        result.Description!.Clauses.Single().Text.Should().Contain($"status {words} 5000").And.NotContain("after").And.NotContain("before");

    }

    [Fact]
    public async Task TheDescriber_LabelsUtilizationAsAPercent_AndNamesTheProject()
    {
        var body = Body().Replace("\"sprk_matter\"", "\"sprk_project\"", StringComparison.Ordinal);

        var result = await Describer().DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        var line = result.Description!.Clauses.Single();
        line.Text.Should().Be("The project's budget utilization (percent of budget spent) is at least 90.");
        line.Formal.Should().Be("sprk_project.sprk_budgetutilizationpercent >= 90");
    }

    [Fact]
    public void AnOutOfRangeUtilization_IsRefusedWithTheUnitStated()
    {
        var result = Validator().ValidateForSave("Threshold", Body(value: "100000000001"), null);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("PERCENT").And.Contain("100 means fully spent");
    }

    [Fact]
    public async Task TheDescriber_KeepsDateWording_ForADateOperand()
    {
        var existence = """{"type":"Existence","subject":"sprk_matter","when":{"sprk_openeddate":{">=":"now-30d"}},"all":[]}""";

        var result = await Describer().DescribeAsync(existence, CancellationToken.None);

        result.Description!.Clauses.Single().Text.Should().Contain("is on or after 30 days ago");
    }

    [Fact]
    public async Task TheDescriber_AddsTheScopeLine_WhenThereIsAWhen()
    {
        var result = await Describer().DescribeAsync(Body(extra: ",\"when\":{\"statecode\":0}"), CancellationToken.None);

        result.Description!.Clauses.Select(c => c.ClauseId).Should().Equal("when", "threshold");
        result.Description.Clauses[0].Formal.Should().Be("sprk_matter where statecode = 0");
    }

    [Theory]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":">=","value":"sprk_totalspendtodate"}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_mattername","operator":">=","value":1}}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalbudget","operator":"=","value":1}}""")]
    public async Task TheDescriber_RefusesWhatTheCompilerRefuses(string body)
    {
        var result = await Describer().DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeTrue();
        result.Refusal.Should().Contain("compiler");
    }

    // The operator semantics of the computed value against the declared one. Pins every boundary: a swap of gt/ge or lt/le
    // flips exactly the "equal" row. Offsets are in cents so the values stay decimal (the live columns are money/decimal).
    [Theory]
    [InlineData("ge", -1, false)]
    [InlineData("ge", 0, true)]
    [InlineData("ge", 1, true)]
    [InlineData("gt", -1, false)]
    [InlineData("gt", 0, false)]
    [InlineData("gt", 1, true)]
    [InlineData("le", -1, true)]
    [InlineData("le", 0, true)]
    [InlineData("le", 1, false)]
    [InlineData("lt", -1, true)]
    [InlineData("lt", 0, false)]
    [InlineData("lt", 1, false)]
    public void ThresholdHolds_AppliesEachOperatorAtTheBoundary(string fetchOperator, int centsFromDeclared, bool expected)
    {
        const decimal declared = 25000.00m;

        SignalEvaluator.ThresholdHolds(declared + (centsFromDeclared * 0.01m), fetchOperator, declared).Should().Be(expected);
    }
}
