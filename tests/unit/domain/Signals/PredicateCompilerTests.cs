using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Unit tests for <see cref="PredicateCompiler"/> (spec FR-06, FR-07; design.md §8.0.1; task 021).
/// </summary>
/// <remarks>
/// <para>The load-bearing assertion is the GOLDEN one: the compiler's output for the live Path B rule body must
/// equal, byte for byte, the hand-written FetchXML that was run against real <c>spaarkedev1</c> data and returned
/// the positive matter while excluding both negative controls
/// (<c>tests/fixtures/signals/pathb-existence.reference.fetchxml</c>; results recorded in
/// <c>projects/spaarke-ontology-platform-r1/notes/pathb-fetchxml-reference.md</c>). The clock is pinned to the
/// instant that reference is anchored at, so equality here means "the compiler emits the query that was proven".
/// <b>If the compiler's output shape changes, the fixture must be re-run on real data, not just re-baselined.</b></para>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): a pure compiler whose silent failure mode — an
/// anti-join that returns every row or no row — reads as a working predicate. Every refusal below guards one input
/// that would otherwise compile into such a query.</para>
/// </remarks>
[Trait("status", "repaired")]
public class PredicateCompilerTests
{
    /// <summary>The instant the hand-written reference is anchored at (window = 2026-09-04T02:15:00Z).</summary>
    private static readonly DateTimeOffset ReferenceRunAt = new(2026, 10, 4, 2, 15, 0, TimeSpan.Zero);

    private static PredicateCompiler Compiler(DateTimeOffset? now = null) =>
        new(new RuleBodySchemaValidator(), new FakeTimeProvider(now ?? ReferenceRunAt));

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tests", "fixtures", "signals", name));

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

    private static string Normalize(string xml) => xml.Replace("\r\n", "\n").Trim();

    /// <summary>An exists clause over sprk_communication (a verified join) with the given filter.</summary>
    private static string Body(string filterJson, string when = "{}") =>
        "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"when\":" + when + ",\"all\":[" +
        "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":" + filterJson + "}]}";

    private static string Clause(string kind, string related, string path, string filterJson = "{\"createdon\":{\">=\":\"now-30d\"}}") =>
        "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"all\":[{\"" + kind + "\":\"" + related +
        "\",\"path\":\"" + path + "\",\"filter\":" + filterJson + "}]}";

    private static XElement Condition(CompiledPredicate compiled, string attribute) =>
        XElement.Parse(compiled.FetchXml).Descendants("condition").Single(c => c.Attribute("attribute")!.Value == attribute);

    // ── Golden: the compiled query IS the one proven on real data ───────────────────────────────────────

    [Fact]
    public void Compile_LivePathBBody_EqualsHandVerifiedReferenceByteForByte()
    {
        var compiled = Compiler().Compile(Fixture("pathb-existence.rulebody.json"));

        Normalize(compiled.FetchXml).Should().Be(Normalize(Fixture("pathb-existence.reference.fetchxml")));
        compiled.WindowAnchorUtc.Should().Be(ReferenceRunAt);
        compiled.SubjectEntity.Should().Be("sprk_matter");
        compiled.SubjectIdAttribute.Should().Be("sprk_matterid");
    }

    // ── Shape: one query, both conjuncts, the anti-join where it must be ─────────────────────────────────

    [Fact]
    public void Compile_PathB_IsOneOrderedFetchHoldingBothConjuncts()
    {
        var fetch = XElement.Parse(Compiler().Compile(Fixture("pathb-existence.rulebody.json")).FetchXml);

        fetch.Attribute("distinct")!.Value.Should().Be("true");
        var entity = fetch.Elements("entity").Should().ContainSingle().Subject;
        entity.Element("order")!.Attribute("attribute")!.Value.Should().Be("sprk_matterid");

        var links = entity.Elements("link-entity").ToList();
        links.Select(l => (l.Attribute("name")!.Value, l.Attribute("from")!.Value, l.Attribute("link-type")!.Value))
            .Should().Equal(("sprk_communication", "sprk_regardingmatter", "inner"), ("sprk_budgetrevision", "sprk_matter", "outer"));

        var nullTest = entity.Element("filter")!.Elements("condition").Should().ContainSingle().Subject;
        nullTest.Attribute("entityname")!.Value.Should().Be(links[1].Attribute("alias")!.Value);
        nullTest.Attribute("attribute")!.Value.Should().Be("sprk_budgetrevisionid");
        nullTest.Attribute("operator")!.Value.Should().Be("null");
    }

    [Fact]
    public void Compile_NotExistsWindow_LivesInsideTheOuterLinkNotTheRootFilter()
    {
        // Placed in the root (WHERE) filter the window tests the outer join's null row and is never true, so the
        // query returns NOTHING. Proven on spaarkedev1 (reference notes §3, probe V4). This test is the guard.
        var entity = XElement.Parse(Compiler().Compile(Fixture("pathb-existence.rulebody.json")).FetchXml)
            .Element("entity")!;

        entity.Element("filter")!.Descendants("condition")
            .Should().NotContain(c => c.Attribute("attribute")!.Value == "sprk_revisedon");

        var outer = entity.Elements("link-entity").Single(l => l.Attribute("link-type")!.Value == "outer");
        outer.Element("filter")!.Elements("condition")
            .Should().ContainSingle(c => c.Attribute("attribute")!.Value == "sprk_revisedon"
                                         && c.Attribute("operator")!.Value == "ge");
    }

    [Fact]
    public void Compile_TwoNotExistsClauses_EachGetsItsOwnIndependentNullTest()
    {
        const string body = """
            {"type":"Existence","subject":"sprk_matter","all":[
              {"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}},
              {"notExists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_receiveddate":{">=":"now-1d"}}}]}
            """;

        var entity = XElement.Parse(Compiler().Compile(body).FetchXml).Element("entity")!;

        entity.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("entityname")!.Value, c.Attribute("attribute")!.Value))
            .Should().Equal(("c0", "sprk_budgetrevisionid"), ("c1", "sprk_communicationid"));
    }

    // ── Joins fail closed (review F1) ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("notExists", "sprk_budgetrevision", "sprk_budget")]          // a real lookup, but to sprk_budget
    [InlineData("notExists", "sprk_communication", "sprk_regardingbudget")] // a real lookup, but to sprk_budget
    [InlineData("exists", "sprk_communication", "ownerid")]
    [InlineData("exists", "sprk_communication", "sprk_communicationid")]
    public void Compile_PathThatIsNotTheVerifiedLookupToTheSubject_IsRefused(string kind, string related, string path)
    {
        var act = () => Compiler().Compile(Clause(kind, related, path));

        act.Should().Throw<PredicateCompilationException>()
            .WithMessage($"*'{related}.{path}' is not a verified lookup from {related} to sprk_matter*");
    }

    [Fact]
    public void Compile_ClauseEntityWithNoVerifiedJoin_IsRefused()
    {
        // sprk_invoice is Global-readable, but no join from it to sprk_matter has been verified against the schema.
        var act = () => Compiler().Compile(Clause("notExists", "sprk_invoice", "sprk_matter"));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*'sprk_invoice.sprk_matter' is not a verified lookup*");
    }

    // ── Read depth fails closed (task 006 finding, notes/security-roles.md §9) ──────────────────────────

    [Theory]
    [InlineData("notExists", "sprk_spendsnapshot")]   // Basic depth for the writer: would be TRUE for every matter
    [InlineData("exists", "sprk_document")]           // Basic depth: would be FALSE for every matter
    [InlineData("notExists", "sprk_signal")]          // not in the verified Global set
    public void Compile_ClauseOverTableNotInGlobalReadAllowList_IsRefused(string kind, string entity)
    {
        var act = () => Compiler().Compile(Clause(kind, entity, "sprk_matter"));

        act.Should().Throw<PredicateCompilationException>().WithMessage($"*'{entity}' is not in the verified Global-read allow-list*");
    }

    [Fact]
    public void Compile_SubjectNotInGlobalReadAllowList_IsRefused()
    {
        var body = Body("""{"sprk_receiveddate":{">=":"now-30d"}}""").Replace("\"subject\":\"sprk_matter\"", "\"subject\":\"sprk_document\"");

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*'sprk_document' is not in the verified Global-read allow-list*");
    }

    // ── FR-07: sprk_budget.modifiedon is never evidence of a revision ─────────────────────────────────────

    [Fact]
    public void Compile_PathB_ReadsNoModifiedOnColumnAtAll()
    {
        Compiler().Compile(Fixture("pathb-existence.rulebody.json")).FetchXml.Should().NotContain("modifiedon");
    }

    [Fact]
    public void Compile_ClauseReadingBudgetModifiedOn_IsRefusedAsFr07()
    {
        var act = () => Compiler().Compile(Clause("notExists", "sprk_budget", "sprk_matter", """{"modifiedon":{">=":"now-30d"}}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*sprk_budget.modifiedon*FR-07*");
    }

    [Fact]
    public void Compile_WhenScopeReadingBudgetModifiedOn_IsRefusedAsFr07()
    {
        var body = "{\"type\":\"Existence\",\"subject\":\"sprk_budget\",\"when\":{\"modifiedon\":{\">=\":\"now-30d\"}}," +
                   "\"all\":[{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingbudget\",\"filter\":{\"sprk_receiveddate\":{\">=\":\"now-30d\"}}}]}";

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*when.modifiedon*FR-07*");
    }

    [Fact]
    public void Compile_FieldNameWithTrailingNewline_IsRefusedNotTreatedAsTheBareName()
    {
        // .NET '$' matches before a trailing \n; a $-anchored check would let "modifiedon\n" slip past FR-07 (review F2).
        var act = () => Compiler().Compile(Clause("notExists", "sprk_budget", "sprk_matter", """{"modifiedon\n":{">=":"now-30d"}}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*is not a Dataverse logical name*");
    }

    // ── No cross-clause variable passing (design.md §8.0.1(a)) ───────────────────────────────────────────

    [Fact]
    public void Compile_DollarVariableReferenceInClause_IsRefusedBySchema()
    {
        var act = () => Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"$commitment.sprk_receiveddate"}}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*failed Existence schema validation*");
    }

    [Fact]
    public void Compile_DollarVariableReferenceInWhen_IsRefusedBySchema()
    {
        // Task 022 rework round 2 (F7): 'when' now shares the clause filters' per-value grammar, so the schema
        // refuses this first; CompileSchemaValidated_DollarVariableReferenceInWhen_* pins the compiler's own gate.
        var act = () => Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"now-30d"}}""", when: """{"sprk_name":"$commitment.x"}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*failed Existence schema validation*");
    }

    [Fact]
    public void Compile_BindProperty_IsRefusedBySchema()
    {
        const string body = """
            {"type":"Existence","subject":"sprk_matter","all":[
              {"exists":"sprk_communication","path":"sprk_regardingmatter","bind":"commitment",
               "filter":{"sprk_receiveddate":{">=":"now-30d"}}}]}
            """;

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*failed Existence schema validation*");
    }

    // ── Values and operators ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(">=", "ge")]
    [InlineData("<=", "le")]
    [InlineData(">", "gt")]
    [InlineData("<", "lt")]
    [InlineData("<>", "ne")]
    [InlineData("=", "eq")]
    public void Compile_ComparisonOperator_MapsToFetchXmlOperator(string op, string expected)
    {
        var condition = Condition(Compiler().Compile(Body("{\"sprk_reviewoutcome\":{\"" + op + "\":100000003}}")), "sprk_reviewoutcome");

        condition.Attribute("operator")!.Value.Should().Be(expected);
        condition.Attribute("value")!.Value.Should().Be("100000003");
    }

    [Fact]
    public void Compile_RelativeDate_ResolvesAgainstTheClockAsUtc()
    {
        var compiled = Compiler(new DateTimeOffset(2026, 3, 10, 9, 30, 45, 500, TimeSpan.Zero))
            .Compile(Body("""{"sprk_receiveddate":{">=":"now-7d"},"sprk_sentat":{"<":"now"}}"""));

        Condition(compiled, "sprk_receiveddate").Attribute("value")!.Value.Should().Be("2026-03-03T09:30:45Z");
        Condition(compiled, "sprk_sentat").Attribute("value")!.Value.Should().Be("2026-03-10T09:30:45Z");
    }

    [Theory]
    [InlineData("now-30")]
    [InlineData("NOW-30d")]
    [InlineData("now-30h")]
    [InlineData(" now-30d")]
    [InlineData("now-30d\\n")]
    // Task 024: now+Nd is accepted (Compile_FutureRelativeDate_*); these near-misses of it are still refused.
    [InlineData("NOW+3d")]
    [InlineData("now+3")]
    [InlineData("now+1h")]
    [InlineData("now+-3d")]
    [InlineData("now+12345d")]
    public void Compile_MalformedRelativeDate_IsRefusedNotSentAsALiteral(string token)
    {
        var act = () => Compiler().Compile(Body("{\"sprk_receiveddate\":{\">=\":\"" + token + "\"}}"));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*relative date*");
    }

    [Fact]
    public void Compile_LiteralThatMerelyStartsWithNow_PassesThroughUnchanged()
    {
        Condition(Compiler().Compile(Body("""{"sprk_name":"Nowak"}""")), "sprk_name")
            .Attribute("value")!.Value.Should().Be("Nowak");
    }

    [Theory]
    [InlineData("true", "1")]
    [InlineData("false", "0")]
    public void Compile_Boolean_EmitsFetchXmlBitValue(string json, string expected)
    {
        // Emitted form only — NOT yet exercised on real data (no Path B clause filters a boolean).
        Condition(Compiler().Compile(Body("{\"sprk_isprivate\":" + json + "}")), "sprk_isprivate")
            .Attribute("value")!.Value.Should().Be(expected);
    }

    [Fact]
    public void Compile_WhenFilter_BecomesARootConditionOnTheSubject()
    {
        var compiled = Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"now-30d"}}""", when: """{"statecode":0}"""));

        XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!.Elements("condition")
            .Should().ContainSingle(c => c.Attribute("attribute")!.Value == "statecode"
                                         && c.Attribute("operator")!.Value == "eq" && c.Attribute("value")!.Value == "0");
    }

    [Fact]
    public void Compile_EmptyIdList_IsRefused()
    {
        var act = () => Compiler().Compile(Body("""{"sprk_triagecategory":[]}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*empty id list*");
    }

    [Theory]
    [InlineData("sprk_matter\\\" or \\\"1\\\"=\\\"1")]
    [InlineData("Sprk_Matter")]
    [InlineData("sprk matter")]
    [InlineData("sprk_matter\\n")]
    public void Compile_NonLogicalNameSubject_IsRefused(string subject)
    {
        var body = Body("""{"sprk_receiveddate":{">=":"now-30d"}}""").Replace("\"subject\":\"sprk_matter\"", "\"subject\":\"" + subject + "\"");

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*is not a Dataverse logical name*");
    }

    [Fact]
    public void Compile_XmlInvalidCharacterInValue_IsRefusedAsACompilationError()
    {
        var act = () => Compiler().Compile(Body("""{"sprk_name":"a\u0001b"}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*cannot appear in XML*");
    }

    [Fact]
    public void Compile_DuplicateKeys_AreRefused()
    {
        // Otherwise the schema validator and the compiler could each read a different one of the two values.
        var act = () => Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"now-30d"},"sprk_receiveddate":{">=":"now-1d"}}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*duplicate keys are refused*");
    }

    [Fact]
    public void Compile_MoreClausesThanDataverseLinkLimit_IsRefused()
    {
        var clause = "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":{\"sprk_receiveddate\":{\">=\":\"now-30d\"}}}";
        var body = "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"all\":[" +
                   string.Join(",", Enumerable.Repeat(clause, PredicateCompiler.MaxClauses + 1)) + "]}";

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*exceed Dataverse's limit of 15*");
    }

    // ── Scope narrowing for the per-matter event triggers (spec FR-13) ───────────────────────────────────

    [Fact]
    public void Compile_WithSubjectId_AddsOneRootConditionOnTheSubjectKey()
    {
        var matterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");

        var compiled = Compiler().Compile(Fixture("pathb-existence.rulebody.json"), matterId);

        XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!.Elements("condition")
            .Should().ContainSingle(c => c.Attribute("attribute")!.Value == "sprk_matterid"
                                         && c.Attribute("operator")!.Value == "eq"
                                         && c.Attribute("value")!.Value == matterId.ToString("D"));
    }

    [Fact]
    public void Compile_WithEmptySubjectId_IsRefused()
    {
        var act = () => Compiler().Compile(Fixture("pathb-existence.rulebody.json"), Guid.Empty);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*subjectId must not be Guid.Empty*");
    }

    // =====================================================================================
    // Task 022 template vocabulary (review finding #5 + rework round 2 finding F3, coordinator decision):
    // TemplateEligibleFields = subject 'when' fields (any operator) + exists-clause fields PINNED to exactly one
    // value. Never notExists fields; never ambiguous ones.
    // =====================================================================================

    [Fact]
    public void Compile_LivePathB_HasNoTemplateEligibleField_EveryExistsFieldIsUnpinned_NotExistsFieldIsAbsent()
    {
        // The live body pins nothing: a two-value 'in', a range and a '<>'. Its shipped message template has zero
        // placeholders (notes/004-seed-policy-rows.md), which is exactly why it remains valid under F3.
        var compiled = Compiler().Compile(Fixture("pathb-existence.rulebody.json"));

        compiled.TemplateEligibleFields.Should().BeEmpty();
        compiled.UnpinnedTemplateFields.Should().BeEquivalentTo("sprk_triagecategory", "sprk_receiveddate", "sprk_reviewoutcome");
        compiled.AmbiguousTemplateFields.Should().BeEmpty();
        compiled.UnpinnedTemplateFields.Should().NotContain("sprk_revisedon", "it is read only inside the notExists clause");
    }

    [Fact]
    public void Compile_WhenFieldWithRangeFilter_IsTemplateEligible()
    {
        // The subject is ONE row, so its own value of a 'when' field is defined whatever the operator.
        var compiled = Compiler().Compile(Body("""{"sprk_direction":1}""", when: """{"sprk_mattertype":{">=":100000001}}"""));

        compiled.TemplateEligibleFields.Should().Contain("sprk_mattertype");
    }

    [Theory]
    [InlineData("""{"sprk_direction":1}""")]              // bare scalar = eq
    [InlineData("""{"sprk_direction":{"=":1}}""")]        // explicit eq
    [InlineData("""{"sprk_direction":[1]}""")]            // one-element 'in'
    public void Compile_ExistsFieldPinnedToOneValue_IsTemplateEligible(string filter)
    {
        var compiled = Compiler().Compile(Body(filter));

        compiled.TemplateEligibleFields.Should().Contain("sprk_direction");
    }

    [Theory]
    [InlineData("""{"sprk_receiveddate":{">=":"now-30d"}}""")] // range
    [InlineData("""{"sprk_receiveddate":{"<>":"now"}}""")]     // not-equal
    [InlineData("""{"sprk_receiveddate":["now","now-1d"]}""")] // two-value 'in'
    public void Compile_ExistsFieldNotPinnedToOneValue_IsNotTemplateEligible_IsUnpinned(string filter)
    {
        // N matching related rows can carry N different values, so a {{sprk_receiveddate}} token is undefined.
        var compiled = Compiler().Compile(Body(filter));

        compiled.TemplateEligibleFields.Should().NotContain("sprk_receiveddate");
        compiled.UnpinnedTemplateFields.Should().Contain("sprk_receiveddate");
    }

    [Fact]
    public void Compile_TwoExistsClausesPinningTheSameFieldToDifferentValues_IsAmbiguous()
    {
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": 1 } },
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": 2 } }
              ]
            }
            """;

        var compiled = Compiler().Compile(body);

        compiled.TemplateEligibleFields.Should().NotContain("sprk_direction");
        compiled.AmbiguousTemplateFields.Should().Contain("sprk_direction");
    }

    [Fact]
    public void Compile_TwoExistsClausesOneRangeOnePinned_FieldIsNotTemplateEligible()
    {
        // Every exists occurrence must pin the field: the second clause's range makes the token undefined.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": 1 } },
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": { ">": 0 } } }
              ]
            }
            """;

        var compiled = Compiler().Compile(body);

        compiled.TemplateEligibleFields.Should().NotContain("sprk_direction");
        compiled.UnpinnedTemplateFields.Should().Contain("sprk_direction");
    }

    [Fact]
    public void Compile_FieldReadOnTwoPositiveEntities_IsNotTemplateEligible_IsAmbiguous()
    {
        // "sprk_name" appears in the subject's own "when" filter (entity sprk_matter) AND in an "exists"
        // clause's filter (entity sprk_communication) -- two DISTINCT positive entities, so the value is not
        // well defined and the field must not be treated as safely readable.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "when": { "sprk_name": "whatever" },
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_name": "x" } }
              ]
            }
            """;

        var compiled = Compiler().Compile(body);

        compiled.TemplateEligibleFields.Should().NotContain("sprk_name");
        compiled.AmbiguousTemplateFields.Should().Contain("sprk_name");
    }

    // =====================================================================================
    // Rework round 2, finding F7: the compiler's own numeric bound (defence in depth under the schema). Exercised
    // through CompileSchemaValidated, which skips the schema, so these prove the COMPILER refuses on its own.
    // =====================================================================================

    [Theory]
    [InlineData("""{"sprk_amount":1e999999}""")]
    [InlineData("""{"sprk_amount":-1e300}""")]
    public void CompileSchemaValidated_OutOfRangeNumberInWhen_IsRefusedByTheCompiler(string when)
    {
        var act = () => Compiler().CompileSchemaValidated(Body("""{"sprk_direction":1}""", when: when), subjectId: null);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*when.sprk_amount*outside the supported range*");
    }

    [Fact]
    public void CompileSchemaValidated_DollarVariableReferenceInWhen_IsStillRefusedByTheCompiler()
    {
        // Defence in depth: refused here even when the schema evaluation is skipped.
        var act = () => Compiler().CompileSchemaValidated(Body("""{"sprk_direction":1}""", when: """{"sprk_name":"$commitment.x"}"""), subjectId: null);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*cross-clause variable reference*");
    }

    // Round 3, finding L3: numbers reach FetchXML as a canonical invariant decimal, never the raw JSON spelling.
    [Theory]
    [InlineData("12345.67", "12345.67")]
    [InlineData("1e2", "100")]
    [InlineData("1E-3", "0.001")]
    [InlineData("-5", "-5")]
    public void Compile_Number_IsEmittedAsACanonicalInvariantDecimal(string json, string expected)
    {
        Condition(Compiler().Compile(Body("{\"sprk_amount\":" + json + "}")), "sprk_amount")
            .Attribute("value")!.Value.Should().Be(expected);
    }

    // =====================================================================================
    // Round 3, finding M1: an unpaired UTF-16 surrogate escape in a property name is a PredicateCompilationException
    // from BOTH entry points -- never the InvalidOperationException JsonProperty.Name throws.
    // =====================================================================================

    [Theory]
    [InlineData("""{"sprk_\ud800":1}""")]
    [InlineData("""{"sprk_name":"a\udc00"}""")]
    public void Compile_UnpairedSurrogateEscape_IsRefusedAsACompilationError(string filter)
    {
        var act = () => Compiler().Compile(Body(filter));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*not valid JSON*");
    }

    [Theory]
    [InlineData("""{"sprk_\ud800":1}""")]     // name: thrown by the strict parse's duplicate-name check
    [InlineData("""{"sprk_name":"a\udc00"}""")] // value: thrown by GetString while compiling
    public void CompileSchemaValidated_UnpairedSurrogateEscape_IsRefusedAsACompilationError(string filter)
    {
        // The schema is skipped here, so the compiler's own reads are what must be contained.
        var act = () => Compiler().CompileSchemaValidated(Body(filter), subjectId: null);

        act.Should().Throw<PredicateCompilationException>().WithMessage("*not valid JSON*unpaired UTF-16 surrogate*");
    }

    // =====================================================================================
    // Round 3, findings L3 + L5: two exists clauses pinning the SAME value -> eligible; pins compare by value.
    // =====================================================================================

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1", "1.0")]
    [InlineData("100", "1e2")]
    [InlineData("\"Fee\"", "[\"Fee\"]")]
    public void Compile_TwoExistsClausesPinningTheSameFieldToTheSameValue_IsTemplateEligible(string first, string second)
    {
        var body = "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"all\":[" +
                   "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":{\"sprk_direction\":" + first + "}}," +
                   "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":{\"sprk_direction\":" + second + "}}]}";

        var compiled = Compiler().Compile(body);

        compiled.TemplateEligibleFields.Should().Contain("sprk_direction");
        compiled.AmbiguousTemplateFields.Should().NotContain("sprk_direction");
    }

    [Fact]
    public void Compile_TwoExistsClausesPinningStringsDifferingOnlyInCase_IsAmbiguous()
    {
        // Documented conservative choice (L3): strings compare ordinally although Dataverse 'eq' is case-insensitive.
        var body = "{\"type\":\"Existence\",\"subject\":\"sprk_matter\",\"all\":[" +
                   "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":{\"sprk_name\":\"Fee\"}}," +
                   "{\"exists\":\"sprk_communication\",\"path\":\"sprk_regardingmatter\",\"filter\":{\"sprk_name\":\"fee\"}}]}";

        Compiler().Compile(body).AmbiguousTemplateFields.Should().Contain("sprk_name");
    }

    // =====================================================================================
    // Task 022 rework (review finding #9): bounded refusals on rule-body length and 'in'-list length.
    // =====================================================================================

    [Fact]
    public void Compile_RuleBodyLongerThanMaxLength_IsRefused()
    {
        var hugeValue = new string('a', PredicateCompiler.MaxRuleBodyLength + 1);
        var body = Body($$"""{"sprk_name":"{{hugeValue}}"}""");

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>().WithMessage($"*exceeding the {PredicateCompiler.MaxRuleBodyLength}-character limit*");
    }

    [Fact]
    public void Compile_InListLongerThanMaxLength_IsRefused()
    {
        var hugeList = string.Join(",", Enumerable.Range(0, PredicateCompiler.MaxInListLength + 1).Select(i => $"\"id{i}\""));
        var body = Body($$"""{"sprk_triagecategory":[{{hugeList}}]}""");

        var act = () => Compiler().Compile(body);

        act.Should().Throw<PredicateCompilationException>()
            .WithMessage($"*an 'in' list of {PredicateCompiler.MaxInListLength + 1} values exceeds the {PredicateCompiler.MaxInListLength}-value limit*");
    }

    // =====================================================================================
    // Task 024 (D-16, D-40, D-13): the Do-lane grammar -- subject-only bodies, now+Nd, the three Do subjects, the
    // two-bound date range and the quietWindowDays knob. Everything else stays refused.
    // =====================================================================================

    /// <summary>The instant the three Do-rule references are anchored at (notes/024-progress.md).</summary>
    private static readonly DateTimeOffset DoReferenceRunAt = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private static string SubjectOnly(string subject, string whenJson, string extra = "") =>
        "{\"type\":\"Existence\",\"subject\":\"" + subject + "\",\"when\":" + whenJson + ",\"all\":[]" + extra + "}";

    private static XElement RootFilter(CompiledPredicate compiled) =>
        XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!;

    [Theory]
    [InlineData("do-overdue-task")]
    [InlineData("do-task-due-within-3-days")]
    [InlineData("do-workassignment-past-due")]
    public void Compile_DoRuleFixture_EqualsHandVerifiedReferenceByteForByte(string fixture)
    {
        // The hand-written references were run on spaarkedev1 first (notes/024-progress.md); equality here means
        // the compiler emits the query that was proven. SignalPredicateTests runs both and compares row sets.
        var compiled = Compiler(DoReferenceRunAt).Compile(Fixture(fixture + ".rulebody.json"));

        Normalize(compiled.FetchXml).Should().Be(Normalize(Fixture(fixture + ".reference.fetchxml")));
        compiled.WindowAnchorUtc.Should().Be(DoReferenceRunAt);
    }

    [Fact]
    public void Compile_SubjectOnlyEventBody_IsOneFetchWithNoLinkEntity_AndTheWhenFilterAtTheRoot()
    {
        var compiled = Compiler().Compile(SubjectOnly("sprk_event", """{"statuscode":659490001,"sprk_duedate":{"<":"now-5d"}}"""));

        var entity = XElement.Parse(compiled.FetchXml).Elements("entity").Should().ContainSingle().Subject;
        entity.Elements("link-entity").Should().BeEmpty();
        compiled.SubjectEntity.Should().Be("sprk_event");
        compiled.SubjectIdAttribute.Should().Be("sprk_eventid");
        RootFilter(compiled).Elements("condition").Select(c => c.Attribute("attribute")!.Value)
            .Should().Equal("statuscode", "sprk_duedate");
    }

    [Theory]
    [InlineData("""{"type":"Existence","subject":"sprk_event","when":{},"all":[]}""")]
    [InlineData("""{"type":"Existence","subject":"sprk_event","all":[]}""")]
    public void Compile_ZeroClausesAndNoWhenCondition_IsRefused_ItWouldMatchEveryRow(string body)
    {
        Compiler().Invoking(c => c.Compile(body)).Should().Throw<PredicateCompilationException>()
            .WithMessage("*failed Existence schema validation*");

        // The compiler refuses it on its own too (CompileSchemaValidated skips the schema).
        Compiler().Invoking(c => c.CompileSchemaValidated(body, subjectId: null)).Should().Throw<PredicateCompilationException>()
            .WithMessage("*at least one 'when' condition*every row*");
    }

    [Theory]
    [InlineData("sprk_event", "sprk_duedate")]
    [InlineData("sprk_todo", "sprk_duedate")]
    [InlineData("sprk_workassignment", "sprk_responseduedate")]
    public void Compile_DoLaneSubject_IsAccepted(string subject, string dueField)
    {
        var compiled = Compiler().Compile(SubjectOnly(subject, "{\"" + dueField + "\":{\"<\":\"now\"}}"));

        compiled.SubjectEntity.Should().Be(subject);
        compiled.SubjectIdAttribute.Should().Be(subject + "id");
    }

    [Fact]
    public void Compile_ServiceRequestSubject_IsStillRefused()
    {
        var act = () => Compiler().Compile(SubjectOnly("sprk_servicerequest", """{"statecode":0}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*'sprk_servicerequest' is not in the verified Global-read allow-list*");
    }

    [Fact]
    public void Compile_DoLaneSubjectWithAClause_IsStillRefused_NoVerifiedJoin()
    {
        // D-16 adds no VerifiedJoins entry: a Do subject may be scoped by 'when' only.
        var body = """
            {"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[
              {"exists":"sprk_communication","path":"sprk_regardingevent","filter":{"sprk_direction":1}}]}
            """;

        Compiler().Invoking(c => c.Compile(body)).Should().Throw<PredicateCompilationException>()
            .WithMessage("*'sprk_communication.sprk_regardingevent' is not a verified lookup from sprk_communication to sprk_event*");
    }

    [Fact]
    public void Compile_FutureRelativeDate_ResolvesAgainstTheSameAnchorAsPastOnes()
    {
        var compiled = Compiler(new DateTimeOffset(2026, 3, 10, 9, 30, 45, TimeSpan.Zero))
            .Compile(SubjectOnly("sprk_event", """{"sprk_duedate":{"<=":"now+3d"},"sprk_basedate":{">=":"now-3d"}}"""));

        compiled.WindowAnchorUtc.Should().Be(new DateTimeOffset(2026, 3, 10, 9, 30, 45, TimeSpan.Zero));
        Condition(compiled, "sprk_duedate").Attribute("value")!.Value.Should().Be("2026-03-13T09:30:45Z");
        Condition(compiled, "sprk_basedate").Attribute("value")!.Value.Should().Be("2026-03-07T09:30:45Z");
    }

    // ── D-40: one lower plus one upper relative-date bound ───────────────────────────────────────────────

    [Theory]
    [InlineData(">=", "<=", "ge", "le")]
    [InlineData(">", "<", "gt", "lt")]
    [InlineData("<=", ">=", "ge", "le")] // order in the object does not matter; lower is emitted first
    public void Compile_TwoBoundDateRange_BecomesTwoConditionsInTheSameAndFilter(string first, string second, string lowerOp, string upperOp)
    {
        var range = first.StartsWith('>') ? $"\"{first}\":\"now\",\"{second}\":\"now+3d\"" : $"\"{first}\":\"now+3d\",\"{second}\":\"now\"";
        var compiled = Compiler(DoReferenceRunAt).Compile(SubjectOnly("sprk_event", "{\"sprk_duedate\":{" + range + "}}"));

        var filter = RootFilter(compiled);
        filter.Attribute("type")!.Value.Should().Be("and");
        filter.Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, c.Attribute("value")!.Value))
            .Should().Equal(("sprk_duedate", lowerOp, "2026-10-07T00:00:00Z"), ("sprk_duedate", upperOp, "2026-10-10T00:00:00Z"));
        XElement.Parse(compiled.FetchXml).Descendants("filter").Should().ContainSingle("a range is never an OR");
    }

    [Theory]
    [InlineData("""{">=":"now",">":"now-1d"}""", "*exactly ONE lower bound*")]                          // two lower bounds
    [InlineData("""{"<=":"now+3d","<":"now+1d"}""", "*exactly ONE lower bound*")]                       // two upper bounds
    [InlineData("""{">=":"now","<=":"now+3d","<":"now+5d"}""", "*exactly one operator, or one lower*")] // a third key
    [InlineData("""{"=":"now","<=":"now+3d"}""", "*'=' cannot bound a date range*")]                    // '=' is not a bound
    [InlineData("""{"<>":"now","<=":"now+3d"}""", "*'<>' cannot bound a date range*")]                 // '<>' is not a bound
    [InlineData("""{">=":1,"<=":5}""", "*relative-date bounds only*")]                                  // not a date: numeric bounds
    [InlineData("""{">=":"2026-01-01","<=":"2026-12-31"}""", "*relative-date bounds only*")]            // absolute dates
    [InlineData("""{">=":"now","<=":"now+3"}""", "*relative-date bounds only*")]                        // a malformed relative date
    [InlineData("""{">=":"now","<=":"now+3d\n"}""", "*relative-date bounds only*")]                     // trailing newline (review F3)
    public void Compile_RangeOutsideD40_IsRefused_ByTheSchemaAndByTheCompiler(string range, string compilerMessage)
    {
        var body = SubjectOnly("sprk_event", "{\"sprk_duedate\":" + range + "}");

        new RuleBodySchemaValidator().Validate(RuleType.Existence, body).IsValid.Should().BeFalse();
        // Defence in depth: refused by the compiler alone too (the schema is skipped here), for the stated reason.
        Compiler().Invoking(c => c.CompileSchemaValidated(body, subjectId: null)).Should().Throw<PredicateCompilationException>()
            .WithMessage(compilerMessage);
    }

    [Fact]
    public void Compile_RangeInANotExistsClause_StaysInsideTheOuterLink()
    {
        // D-40 says "a date field"; the grammar is shared, so a range is also legal in a clause filter. In a notExists
        // it MUST land in the join's ON clause like any window (a root-filter window returns nothing; probe V4).
        const string body = """
            {"type":"Existence","subject":"sprk_matter","all":[
              {"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d","<":"now"}}}]}
            """;

        var entity = XElement.Parse(Compiler().Compile(body).FetchXml).Element("entity")!;

        entity.Element("filter")!.Descendants("condition").Should().NotContain(c => c.Attribute("attribute")!.Value == "sprk_revisedon");
        entity.Element("link-entity")!.Element("filter")!.Elements("condition").Select(c => c.Attribute("operator")!.Value)
            .Should().Equal("ge", "lt");
    }

    [Theory]
    [InlineData("""{">=":"now+3d","<=":"now"}""")]                // inverted: matches nothing
    [InlineData("""{">":"now","<":"now"}""")]                     // empty: matches nothing
    public void Compile_RangeThatMatchesNothing_IsRefusedByTheCompiler(string range)
    {
        // Schema-valid shape (the schema cannot compare two relative dates); the compiler resolves and refuses it.
        var act = () => Compiler().Compile(SubjectOnly("sprk_event", "{\"sprk_duedate\":" + range + "}"));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*lower bound is not before its upper bound*");
    }

    [Fact]
    public void Compile_RangeInAnExistsClause_NeverPinsItsField()
    {
        // §0.3: a range can match N related rows with N values, so it can never feed a {{token}}.
        var compiled = Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"now-30d","<":"now"}}"""));

        compiled.UnpinnedTemplateFields.Should().Contain("sprk_receiveddate");
        compiled.TemplateEligibleFields.Should().NotContain("sprk_receiveddate");
    }

    // ── D-13 / FR-17a: the quietWindowDays knob ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("7", 7)]
    [InlineData("0", 0)]
    [InlineData("3650", 3650)]
    [InlineData("7.0", 7)]   // JSON Schema 'integer' accepts an integral number in any spelling (review F2)
    [InlineData("1e2", 100)]
    public void Compile_QuietWindowDays_IsExposedOnThePredicate(string json, int expected)
    {
        Compiler().Compile(SubjectOnly("sprk_event", """{"statuscode":659490001}""", ",\"quietWindowDays\":" + json))
            .QuietWindowDays.Should().Be(expected);
    }

    [Fact]
    public void Compile_NoQuietWindowDays_ReportsTheFourteenDayDefault()
    {
        PredicateCompiler.DefaultQuietWindowDays.Should().Be(14);
        Compiler().Compile(Fixture("pathb-existence.rulebody.json")).QuietWindowDays.Should().Be(14);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"7\"")]
    [InlineData("7.5")]
    [InlineData("3651")]
    [InlineData("null")]
    public void Compile_InvalidQuietWindowDays_IsRefused_AtTheSchemaAndByTheCompiler(string value)
    {
        var body = SubjectOnly("sprk_event", """{"statuscode":659490001}""", ",\"quietWindowDays\":" + value);

        Compiler().Invoking(c => c.Compile(body)).Should().Throw<PredicateCompilationException>()
            .WithMessage("*failed Existence schema validation*");
        Compiler().Invoking(c => c.CompileSchemaValidated(body, subjectId: null)).Should().Throw<PredicateCompilationException>()
            .WithMessage("*quietWindowDays*");
    }

    // ── Date Only metadata for the evaluator's per-item "today" (D-25, task 031) ────────────────────────

    [Theory]
    [InlineData("do-overdue-task", "sprk_duedate")]
    [InlineData("do-task-due-within-3-days", "sprk_duedate")]
    [InlineData("do-workassignment-past-due", "sprk_responseduedate")]
    public void Compile_DoRuleFixture_ReportsItsDateOnlyWhenField(string fixture, string dateOnlyField)
    {
        // statuscode / statecode / sprk_eventtype_ref are when-fields too, and are not Date Only.
        Compiler().Compile(Fixture(fixture + ".rulebody.json")).DateOnlyWhenFields.Should().Equal(dateOnlyField);
    }

    [Fact]
    public void Compile_DateAndTimeWhenField_IsNotReportedAsDateOnly()
    {
        // sprk_plannedstart is DateAndTime/UserLocal on sprk_event (live metadata 2026-10-07).
        Compiler().Compile(SubjectOnly("sprk_event", """{"sprk_plannedstart":{"<":"now"}}"""))
            .DateOnlyWhenFields.Should().BeEmpty();
    }
}
