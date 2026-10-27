using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 063: the first Know-promotion policy definition (<c>config/know-promotion.policies.json</c>, applied to Dataverse by
/// <c>scripts/Apply-DoLanePolicies.ps1 -DefinitionPath config/know-promotion.policies.json</c>) passes the SAME gates the evaluator
/// applies at run time: the rule-body schema and compiler (<see cref="PolicyVersionValidator.ValidateForSave"/>, which also checks
/// the message template against the read set, section 0.3), the decision plan against the closed Decide-lane catalog
/// (<see cref="DecisionPlanService.Resolve"/>, D-51: Assign Work is offered), and the describer. It also pins section 0.3 for the
/// sentence itself: it may assert only what the body reads.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The row's live behaviour (a matter WITH a budget excluded, one without
/// included, against real spaarkedev1 data) is <c>tests/integration/seam/Signals/KnowPromotionPolicySeamTests.cs</c>.
/// </remarks>
[Trait("status", "new")]
public class KnowPromotionPolicyDefinitionTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static JsonObject Definition() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "config", "know-promotion.policies.json")))!.AsObject();

    private static JsonObject Policy() => Definition()["policies"]!.AsArray().Single()!.AsObject();

    private static JsonObject Version() =>
        Policy()["versions"]!.AsArray().Select(v => v!.AsObject()).Single(v => (int)v["versionNumber"]! == (int)Policy()["currentVersion"]!);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt));

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), Compiler(), NullLogger<PolicyVersionValidator>.Instance);

    private static string Compact(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void TheDefinition_HoldsExactlyTheOneKnowPromotionPolicy_InTheDecideLane_AndCarriesNoEnabledFlag()
    {
        Definition()["kind"]!.GetValue<string>().Should().Be("know-promotion-policies");
        Policy()["code"]!.GetValue<string>().Should().Be("POL-MATTER-NOBUDGET");
        Policy()["lane"]!.GetValue<string>().Should().Be("Decide", "the POML: it is a decision, not assigned work");
        Policy()["subjectType"]!.GetValue<string>().Should().Be("Matter");
        // The script creates the policy with sprk_enabled = No and never changes it afterwards: the owner reviews first.
        Policy().ContainsKey("enabled").Should().BeFalse();
    }

    [Fact]
    public void TheCurrentVersion_PassesValidateForSave_BodyAndLiteralTemplate()
    {
        var version = Version();

        var result = Validator().ValidateForSave(version["ruleType"]!.GetValue<string>(), Compact(version["ruleBody"]!), version["messageTemplate"]!.GetValue<string>());

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        version["messageTemplate"]!.GetValue<string>().Should().NotContain("{{", "section 0.3: a literal sentence cannot name a field the body does not read");
    }

    [Fact]
    public void TheBody_ReadsTheBudgetTableItself_NeverTheRevisions_AndNeverADocument()
    {
        var body = Version()["ruleBody"]!.AsObject();

        var clause = body["all"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        clause["notExists"]!.GetValue<string>().Should().Be("sprk_budget", "the message says no budget; a rule that read sprk_budgetrevision would claim what it never tested");
        clause["path"]!.GetValue<string>().Should().Be(PredicateCompiler.VerifiedJoins[("sprk_matter", "sprk_budget")]);
        Compact(body).Should().NotContain("sprk_budgetrevision");
        // Negative criterion: no document-based Absence rule (documents are reference-mode data; deferred until documents are mirrored).
        Compact(body).Should().NotContain("sprk_document");
        Definition()["policies"]!.AsArray().Should().ContainSingle("only the one budget rule is authored here");
    }

    [Fact]
    public void TheCompiledQuery_IsTheMatterCreatedFiveDaysAgo_WithAnAntiJoinOnTheActiveBudget()
    {
        var compiled = Compiler().Compile(Compact(Version()["ruleBody"]!));

        compiled.SubjectEntity.Should().Be("sprk_matter");
        var entity = XElement.Parse(compiled.FetchXml).Element("entity")!;
        var link = entity.Elements("link-entity").Should().ContainSingle().Subject;
        (link.Attribute("name")!.Value, link.Attribute("from")!.Value, link.Attribute("to")!.Value, link.Attribute("link-type")!.Value)
            .Should().Be(("sprk_budget", "sprk_matter", "sprk_matterid", "outer"));
        link.Element("filter")!.Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, (string?)c.Attribute("value")))
            .Should().Equal(("statecode", "eq", "0"));
        var root = entity.Element("filter")!.Elements("condition").ToList();
        root.Select(c => (c.Attribute("entityname")?.Value, c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, (string?)c.Attribute("value")))
            .Should().Equal(
                (null, "createdon", "le", "2026-10-11T03:00:00Z"),
                (link.Attribute("alias")!.Value, "sprk_budgetid", "null", null));
    }

    [Fact]
    public void TheMessageTemplate_AssertsOnlyWhatTheBodyReads()
    {
        // Section 0.3. The sentence has three claims; each is tied to a part of the body, and nothing else is claimed.
        var body = Version()["ruleBody"]!.AsObject();
        var template = Version()["messageTemplate"]!.GetValue<string>();
        var headline = Version()["shortHeadline"]!.GetValue<string>();
        // The policy's short name becomes the Signal title (the worklist row text), so it carries the same claims.
        var shortName = Policy()["shortName"]!.GetValue<string>();
        var name = Policy()["name"]!.GetValue<string>();

        // (1) "created more than 5 days ago" <-> when.createdon <= now-5d
        body["when"]!["createdon"]!["<="]!.GetValue<string>().Should().Be("now-5d");
        template.Should().Contain("created more than 5 days ago");
        // (2) "no ... budget record" <-> notExists sprk_budget
        template.Should().Contain("budget record");
        // (3) "active" <-> filter statecode 0 (an inactive budget row is not counted, so the sentence must say so)
        body["all"]![0]!["filter"]!["statecode"]!.GetValue<int>().Should().Be(0);
        template.Should().Contain("no active budget");
        shortName.Should().Contain("no active budget");
        name.Should().Contain("no active budget");
        // Nothing the body does not read: no revision, document, communication, amount or recency-of-news claim.
        foreach (var banned in new[] { "revision", "revised", "document", "communication", "amount", "spend", "opened", "overdue" })
        {
            template.Should().NotContainEquivalentOf(banned);
            headline.Should().NotContainEquivalentOf(banned);
            shortName.Should().NotContainEquivalentOf(banned);
            name.Should().NotContainEquivalentOf(banned);
        }

        // The body offers a template only its own createdon (the budget clause pins nothing); the literal sentence names no token at all.
        Compiler().Compile(Compact(body)).TemplateEligibleFields.Should().BeEquivalentTo(new[] { "createdon" });
        template.Should().NotContain("{{");
    }

    private static JsonObject VersionNumbered(int number) =>
        Policy()["versions"]!.AsArray().Select(v => v!.AsObject()).Single(v => (int)v["versionNumber"]! == number);

    [Fact]
    public void TheDecisionPlan_OffersAssignTask_AsTheDecideLaneAction_WithAddTodoAndAssignWorkAsNextSteps()
    {
        // D-178 (changes D-51's action): the no-budget decision creates a Task; Add To Do and Assign Work are offered as Next steps.
        var resolution = DecisionPlanService.Resolve(Compact(Version()["decisionPlan"]!), DecisionLane.Decide);

        resolution.IsRefused.Should().BeFalse(resolution.RefusalReason);
        resolution.Plan!.Actions.Select(a => a.Code).Should().Equal("assign-task");
        resolution.Plan.NextSteps.Select(a => a.Code).Should().Equal("add-todo", "assign-work");
    }

    [Fact]
    public void TheVersions_AreV1Unchanged_AndANewV2_WhichIsCurrent_FR49()
    {
        Policy()["versions"]!.AsArray().Select(v => (int)v!["versionNumber"]!).Should().Equal(1, 2);
        ((int)Policy()["currentVersion"]!).Should().Be(2);

        // Version 1 is published and immutable (FR-49 / D-14): pinned verbatim, so an edit to it fails here.
        var v1 = VersionNumbered(1);
        v1["versionId"]!.GetValue<string>().Should().Be("00000000-0000-4000-8000-000000006311");
        v1["inForceFrom"]!.GetValue<string>().Should().Be("2026-10-16T00:00:00Z");
        v1["proposedAction"]!.GetValue<string>().Should().Be("assign-work: assign someone to set up this matter's budget.");
        Compact(v1["decisionPlan"]!).Should().Be("""{"actions":["assign-work"],"nextSteps":[]}""");

        // Version 2 is its own entry with its own id; only the proposed action and the plan change.
        var v2 = VersionNumbered(2);
        v2["versionId"]!.GetValue<string>().Should().NotBe(v1["versionId"]!.GetValue<string>());
        v2["versionId"]!.GetValue<string>().Should().Be("00000000-0000-4000-8000-000000006312");
        v2["proposedAction"]!.GetValue<string>().Should().Be("assign-task: assign someone to set up this matter's budget.");
        Compact(v2["decisionPlan"]!).Should().Be("""{"actions":["assign-task"],"nextSteps":["add-todo","assign-work"]}""");
        foreach (var same in new[] { "ruleType", "ruleBody", "shortHeadline", "messageTemplate" })
            Compact(v2[same]!).Should().Be(Compact(v1[same]!), $"D-178 changes the action, not the rule ({same})");
    }

    [Fact]
    public void BothVersionPlans_ResolveForDecide_SoTheSupersededV1StillReadsBack()
    {
        foreach (var number in new[] { 1, 2 })
            DecisionPlanService.Resolve(Compact(VersionNumbered(number)["decisionPlan"]!), DecisionLane.Decide).IsRefused.Should().BeFalse($"v{number}");
    }

    [Fact]
    public void TheDecisionPlan_IsRefusedAsADoLanePlan_SoTheLaneIsWhatMakesItValid()
    {
        DecisionPlanService.Resolve(Compact(Version()["decisionPlan"]!), DecisionLane.Do).IsRefused.Should().BeTrue();
    }

    [Fact]
    public async Task TheBody_IsDescribedInWords()
    {
        var entities = new Mock<IGenericEntityService>();
        var describer = new RuleBodyDescriber(Validator(), entities.Object, NullLogger<RuleBodyDescriber>.Instance);

        var result = await describer.DescribeAsync(Compact(Version()["ruleBody"]!), CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        result.Description!.Clauses.Select(c => c.Formal).Should().Equal(
            "sprk_matter where createdon <= now-5d",
            "notExists sprk_budget via sprk_matter where statecode = 0");
    }

    [Fact]
    public void ChangingTheFiveDayKnob_ChangesTheCompiledQuery_WithNoCodeChange()
    {
        var body = Compact(Version()["ruleBody"]!);

        var fiveDays = Compiler().Compile(body);
        var tenDays = Compiler().Compile(body.Replace("now-5d", "now-10d", StringComparison.Ordinal));

        fiveDays.FetchXml.Should().Contain("2026-10-11T03:00:00Z");
        tenDays.FetchXml.Should().Contain("2026-10-06T03:00:00Z").And.NotContain("2026-10-11T03:00:00Z");
    }
}
