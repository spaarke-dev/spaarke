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
/// Task 061: the four Do-lane policy definitions (<c>config/do-lane.policies.json</c>, applied to Dataverse by
/// <c>scripts/Apply-DoLanePolicies.ps1</c>) pass the SAME gates the evaluator applies at run time: the rule-body schema and compiler
/// (<see cref="PolicyVersionValidator.ValidateForSave"/>, which also checks the message template against the read set, section 0.3),
/// the decision plan against the closed catalog (<see cref="DecisionPlanService.Resolve"/>), and the describer. A definition that
/// fails here would be a policy row that is skipped, loudly, every night.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The rows' live behaviour (counts against real spaarkedev1 data, versus the
/// collector's own predicates) is <c>tests/integration/seam/Signals/DoLanePolicySeamTests.cs</c>.
/// </remarks>
[Trait("status", "new")]
public class DoLanePolicyDefinitionTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

    private static readonly Guid TaskEventType = Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b");

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
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "config", "do-lane.policies.json")))!.AsObject();

    private static IEnumerable<JsonObject> Policies() => Definition()["policies"]!.AsArray().Select(p => p!.AsObject());

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var p in Policies()) data.Add((string)p["code"]!);
        return data;
    }

    private static JsonObject Policy(string code) => Policies().Single(p => (string)p["code"]! == code);

    private static JsonObject CurrentVersion(JsonObject policy) =>
        policy["versions"]!.AsArray().Select(v => v!.AsObject()).Single(v => (int)v["versionNumber"]! == (int)policy["currentVersion"]!);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt));

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), Compiler(), NullLogger<PolicyVersionValidator>.Instance);

    private static string Compact(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void TheDefinition_HoldsExactlyTheFourDoLanePolicies()
    {
        Policies().Select(p => (string)p["code"]!).Should().Equal("POL-TASK-OVERDUE", "POL-TASK-DUESOON", "POL-WA-RESPONSE", "POL-TODO-OVERDUE");
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryPolicy_IsDoLane_DeclaresAWorkTypeAndASeverity_AndCarriesNoEnabledFlag(string code)
    {
        var policy = Policy(code);

        ((string)policy["lane"]!).Should().Be("Do");
        ((string?)policy["workType"]).Should().NotBeNullOrWhiteSpace("W-8: each Do rule declares its work type");
        ((string?)policy["severity"]).Should().NotBeNullOrWhiteSpace();
        // The script creates policies with sprk_enabled = No and never changes it afterwards: the definition has no say in it.
        policy.ContainsKey("enabled").Should().BeFalse();
    }

    [Theory]
    [InlineData("POL-TASK-OVERDUE", "Finish or reschedule")]
    [InlineData("POL-TASK-DUESOON", "Coming due")]
    [InlineData("POL-WA-RESPONSE", "Chase a response")]
    [InlineData("POL-TODO-OVERDUE", "Finish or reschedule")]
    public void TheWorkTypes_AreThePOMLs(string code, string workType) => ((string)Policy(code)["workType"]!).Should().Be(workType);

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryCurrentVersion_PassesValidateForSave_BodyAndLiteralTemplate(string code)
    {
        var version = CurrentVersion(Policy(code));

        var result = Validator().ValidateForSave((string)version["ruleType"]!, Compact(version["ruleBody"]!), (string)version["messageTemplate"]!);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        ((string)version["messageTemplate"]!).Should().NotContain("{{", "section 0.3: the sentence claims only what the body tests, and a knob change must not make it lie");
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryBody_IsSubjectOnly_AndNeverReadsTheFinalDueDate(string code)
    {
        var body = CurrentVersion(Policy(code))["ruleBody"]!.AsObject();

        body["all"]!.AsArray().Should().BeEmpty("D-16: a subject-only rule, zero clauses");
        body["when"]!.AsObject().Should().NotBeEmpty();
        Compact(body).Should().NotContain("sprk_finalduedate", "D-27: event rules compare sprk_duedate");
    }

    [Theory]
    [InlineData("POL-TASK-OVERDUE", new[] { "mark-complete", "reschedule", "reassign" })]
    [InlineData("POL-TASK-DUESOON", new[] { "mark-complete", "reschedule" })]
    [InlineData("POL-TODO-OVERDUE", new[] { "mark-complete", "reschedule", "reassign" })]
    [InlineData("POL-WA-RESPONSE", new[] { "send-reminder", "extend-response-date", "record-the-response" })]
    public void EveryDecisionPlan_ResolvesAgainstTheClosedDoLaneCatalog_InTheDeclaredOrder(string code, string[] actions)
    {
        var plan = CurrentVersion(Policy(code))["decisionPlan"]!;

        var resolution = DecisionPlanService.Resolve(Compact(plan), DecisionLane.Do);

        resolution.IsRefused.Should().BeFalse(resolution.RefusalReason);
        resolution.Plan!.Actions.Select(a => a.Code).Should().Equal(actions);
        resolution.Plan.NextSteps.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task EveryBody_IsDescribedInWords(string code)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var rows = new Microsoft.Xrm.Sdk.EntityCollection();
                rows.Entities.Add(new Microsoft.Xrm.Sdk.Entity("sprk_eventtype_ref", TaskEventType) { ["sprk_name"] = "Task" });
                return rows;
            });
        var describer = new RuleBodyDescriber(Validator(), entities.Object, NullLogger<RuleBodyDescriber>.Instance);
        var body = Compact(CurrentVersion(Policy(code))["ruleBody"]!);

        var result = await describer.DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        result.Description!.Clauses.Should().ContainSingle();
    }

    [Fact]
    public void TheOverdueTaskBody_ReproducesTheCollectorsPredicate_WithTheD43Knob()
    {
        // DailyBriefingCollector.QueryOverdueTasksAsync: type Task, statuscode Open, sprk_duedate on or before (today - 1) (D-43).
        var compiled = Compiler().Compile(Compact(CurrentVersion(Policy("POL-TASK-OVERDUE"))["ruleBody"]!));

        Conditions(compiled).Should().Equal(
            ("sprk_eventtype_ref", "eq", TaskEventType.ToString()),
            ("statuscode", "eq", "659490001"),
            ("sprk_duedate", "le", "2026-10-13T03:00:00Z"));
        compiled.DateOnlyWhenFields.Should().Equal("sprk_duedate");
    }

    [Fact]
    public void TheDueSoonBody_ReproducesTheCollectorsUpcomingWindow_TodayThroughThreeDaysOut()
    {
        var compiled = Compiler().Compile(Compact(CurrentVersion(Policy("POL-TASK-DUESOON"))["ruleBody"]!));

        Conditions(compiled).Should().Equal(
            ("sprk_eventtype_ref", "eq", TaskEventType.ToString()),
            ("statuscode", "eq", "659490001"),
            ("sprk_duedate", "ge", "2026-10-14T03:00:00Z"),
            ("sprk_duedate", "le", "2026-10-17T03:00:00Z"));
    }

    [Fact]
    public void TheToDoBody_CoversEveryOpenAndInProgressToDo_NotJustThoseWithAMatter()
    {
        // D-31: ALL To Dos. No regarding column is read, so a To Do with no matter and no project is in scope (it becomes an
        // owner-only Signal under "Not filed", D-35).
        var compiled = Compiler().Compile(Compact(CurrentVersion(Policy("POL-TODO-OVERDUE"))["ruleBody"]!));

        var root = XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!.Elements("condition").ToList();
        root.Select(c => c.Attribute("attribute")!.Value).Should().Equal("statuscode", "sprk_duedate");
        root[0].Attribute("operator")!.Value.Should().Be("in");
        root[0].Elements("value").Select(v => v.Value).Should().Equal("1", "659490001");
        root[1].Attribute("operator")!.Value.Should().Be("le");
    }

    [Fact]
    public void TheWorkAssignmentBody_IsActive_PastItsResponseDate_AndHasNoResponse()
    {
        var compiled = Compiler().Compile(Compact(CurrentVersion(Policy("POL-WA-RESPONSE"))["ruleBody"]!));

        Conditions(compiled).Should().Equal(
            ("statecode", "eq", "0"),
            ("sprk_responseduedate", "le", "2026-10-13T03:00:00Z"),
            ("sprk_respondedon", "null", null));
    }

    [Fact]
    public void ChangingTheOverdueKnob_ChangesTheCompiledQuery_WithNoCodeChange()
    {
        // Criterion 2: the knob lives in the body. A new version with now-5d (the collector's old constant) is a different query.
        var body = Compact(CurrentVersion(Policy("POL-TASK-OVERDUE"))["ruleBody"]!);

        var oneDay = Compiler().Compile(body);
        var fiveDays = Compiler().Compile(body.Replace("now-1d", "now-5d"));

        Conditions(oneDay).Last().Should().Be(("sprk_duedate", "le", "2026-10-13T03:00:00Z"));
        Conditions(fiveDays).Last().Should().Be(("sprk_duedate", "le", "2026-10-09T03:00:00Z"));
    }

    private static List<(string Attribute, string Operator, string? Value)> Conditions(CompiledPredicate compiled) =>
        XElement.Parse(compiled.FetchXml).Element("entity")!.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, (string?)c.Attribute("value"))).ToList();
}
