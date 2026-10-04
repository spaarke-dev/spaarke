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
    public void Compile_DollarVariableReferenceInWhen_IsRefusedByTheCompiler()
    {
        // 'when' is schema-typed only as an object, so the compiler is its only gate.
        var act = () => Compiler().Compile(Body("""{"sprk_receiveddate":{">=":"now-30d"}}""", when: """{"sprk_name":"$commitment.x"}"""));

        act.Should().Throw<PredicateCompilationException>().WithMessage("*cross-clause variable reference*");
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
    [InlineData("now+1d")]
    [InlineData("now-30h")]
    [InlineData(" now-30d")]
    [InlineData("now-30d\\n")]
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
}
