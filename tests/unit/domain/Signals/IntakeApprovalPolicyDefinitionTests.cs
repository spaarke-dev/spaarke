using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarke-intake-pipeline-r1 task 017: the intake approval policy definition (<c>config/intake-approval.policies.json</c>, applied by
/// <c>scripts/intake/Apply-IntakeApprovalPolicy.ps1</c>) passes the gates the evaluator applies at run time: the rule-body schema and
/// compiler with the literal template (<see cref="PolicyVersionValidator.ValidateForSave"/>), and the decision plan against the closed
/// Decide-lane catalog (<see cref="DecisionPlanService.Resolve"/>). It pins what the run relies on: one Decide-lane policy on the
/// service request, matching on the Awaiting approval stage only, offering exactly the two intake actions, never counting toward
/// suppression.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).</remarks>
[Trait("status", "new")]
public class IntakeApprovalPolicyDefinitionTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 11, 28, 3, 0, 0, TimeSpan.Zero);

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
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "config", "intake-approval.policies.json")))!.AsObject();

    private static JsonObject Policy() => Definition()["policies"]!.AsArray().Single()!.AsObject();

    private static JsonObject Version() =>
        Policy()["versions"]!.AsArray().Select(v => v!.AsObject()).Single(v => (int)v["versionNumber"]! == (int)Policy()["currentVersion"]!);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt));

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), Compiler(), NullLogger<PolicyVersionValidator>.Instance);

    private static string Compact(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void TheDefinition_HoldsOneDecideLanePolicy_OnTheServiceRequest_ThatNeverCountsTowardSuppression()
    {
        Definition()["kind"]!.GetValue<string>().Should().Be("intake-approval-policies");
        Definition()["policies"]!.AsArray().Should().ContainSingle();
        Policy()["code"]!.GetValue<string>().Should().Be("POL-INTAKE-APPROVAL");
        Policy()["lane"]!.GetValue<string>().Should().Be("Decide");
        Policy()["subjectType"]!.GetValue<string>().Should().Be("Service Request");
        Policy()["countsTowardSuppression"]!.GetValue<bool>().Should().BeFalse("D-11: an approval is never suppressed by other Signals");
        // Deviation recorded for task 019: no approval work type yet, so the Console shows the item under Other.
        Policy()["workType"].Should().BeNull();
        // The script creates the policy disabled; the owner turns it on.
        Policy().ContainsKey("enabled").Should().BeFalse();
    }

    [Fact]
    public void TheCurrentVersion_PassesValidateForSave_WithALiteralTemplate()
    {
        var version = Version();

        var result = Validator().ValidateForSave(version["ruleType"]!.GetValue<string>(), Compact(version["ruleBody"]!), version["messageTemplate"]!.GetValue<string>());

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        version["messageTemplate"]!.GetValue<string>().Should().NotContain("{{");
        version["shortHeadline"]!.GetValue<string>().Should().NotContain("{{");
    }

    [Fact]
    public void TheCompiledQuery_MatchesTheServiceRequest_OnTheAwaitingApprovalStage_Only()
    {
        var compiled = Compiler().Compile(Compact(Version()["ruleBody"]!));

        compiled.SubjectEntity.Should().Be(IntakeRunStore.RequestEntity);
        var entity = XElement.Parse(compiled.FetchXml).Element("entity")!;
        entity.Elements("link-entity").Should().BeEmpty();
        entity.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, (string?)c.Attribute("value")))
            .Should().Equal(("sprk_intakestage", "eq", IntakeRequestStages.AwaitingApproval.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        IntakeRequestStages.AwaitingApproval.Should().Be(100000003);
    }

    [Fact]
    public void TheDecisionPlan_OffersExactlyApproveAndReject_AsMutuallyExclusiveActions_WithNoNextSteps()
    {
        var resolution = DecisionPlanService.Resolve(Compact(Version()["decisionPlan"]!), DecisionLane.Decide);

        resolution.IsRefused.Should().BeFalse(resolution.RefusalReason);
        resolution.Plan!.Actions.Select(a => a.Code).Should().Equal(DecisionActionCatalog.ApproveIntakeCode, DecisionActionCatalog.RejectIntakeCode);
        resolution.Plan.NextSteps.Should().BeEmpty();
        DecisionActionCatalog.FirstConflict([DecisionActionCatalog.ApproveIntakeCode, DecisionActionCatalog.RejectIntakeCode])
            .Should().NotBeNull("exactly one of the two is taken");
        DecisionActionCatalog.IsIntakeApprovalPlan(resolution.Plan.Actions.Select(a => a.Code)).Should().BeTrue();
    }

    [Fact]
    public void TheDecisionPlan_OffersNoDismissalReason_BecauseTheCommitRouteNeverDismissesAnIntakeApproval()
    {
        var resolution = DecisionPlanService.Resolve(Compact(Version()["decisionPlan"]!), DecisionLane.Decide);

        resolution.Plan!.DismissalReasons.Should().BeEmpty("the commit route refuses every dismissal of an intake approval, so none is offered");
    }

    [Fact]
    public void AnOrdinaryDecidePlan_StillOffersItsDismissalReasons()
    {
        var resolution = DecisionPlanService.Resolve("""{"actions":["send-budget-inquiry","approve-variance"],"nextSteps":[]}""", DecisionLane.Decide);

        resolution.Plan!.DismissalReasons.Should().Equal(DecisionActionCatalog.DismissalReasons(DecisionLane.Decide)).And.NotBeEmpty();
    }

    [Fact]
    public void TheDecisionPlan_IsRefusedAsADoLanePlan()
    {
        DecisionPlanService.Resolve(Compact(Version()["decisionPlan"]!), DecisionLane.Do).IsRefused.Should().BeTrue();
    }

    [Fact]
    public void TheProposedAction_NamesOnlyPlanCodes()
    {
        var proposed = Version()["proposedAction"]!.GetValue<string>();
        var named = DecisionActionCatalog.All.Where(a => proposed.Contains(a.Code, StringComparison.Ordinal)).Select(a => a.Code);

        named.Should().BeEquivalentTo(new[] { DecisionActionCatalog.ApproveIntakeCode, DecisionActionCatalog.RejectIntakeCode });
    }
}
