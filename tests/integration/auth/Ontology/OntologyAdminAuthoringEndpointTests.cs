using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 101 (D-22 slice b, FR-58, #39, #46, D-13, D-14, NFR-10): the four rule-authoring routes through the REAL
/// pipeline (authentication, rate limiting, the production handlers, the real <see cref="PolicyVersionValidator"/> and compiler). Two
/// things are substituted: the caller-identity Dataverse client, by a small Dataverse (<see cref="AuthoringWorld"/>) that answers the
/// caller's reads and records every write with its If-Match; and the evaluator seam, by a recorder that returns the hits a test sets
/// (the evaluator and the job themselves are proven by SignalReevaluationJobTests, including the episode-2 re-raise).
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path). "Admin" is a caller whose writes Dataverse accepts;
/// "Console User" one whose every write Dataverse refuses with 403 (the Spaarke Console User role holds no Create or Write on the policy
/// tables, notes/security-roles.md).</remarks>
public partial class OntologyAdminAuthoringEndpointTests : IClassFixture<OntologyAdminAuthoringTestFixture>
{
    private const string Root = "/api/v1/ontology/admin/authoring";

    private const string Body = """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""";
    private const string OtherBody = """{"type":"Existence","subject":"sprk_matter","when":{"statecode":1},"all":[]}""";
    private const string Template = "A matter needs a look.";
    private const string DecidePlan = """{"actions":["send-budget-inquiry"]}""";

    private static readonly Guid PolicyId = Guid.Parse("10100000-0000-4000-8000-0000000000a1");
    private static readonly Guid V1 = Guid.Parse("10100000-0000-4000-8000-0000000000b1");
    private static readonly Guid BillingPolicyId = Guid.Parse("10100000-0000-4000-8000-0000000000a2");
    private static readonly Guid MatterType = Guid.Parse("10100000-0000-4000-8000-0000000000e1");

    private static readonly Guid MatterA = Guid.Parse("10100000-0000-4000-8000-00000000c0a1");
    private static readonly Guid MatterB = Guid.Parse("10100000-0000-4000-8000-00000000c0b1");
    private static readonly Guid MatterC = Guid.Parse("10100000-0000-4000-8000-00000000c0c1");
    private static readonly Guid HiddenMatter = Guid.Parse("10100000-0000-4000-8000-00000000c0d1");

    private readonly OntologyAdminAuthoringTestFixture _fixture;
    private readonly HttpClient _client;

    public OntologyAdminAuthoringEndpointTests(OntologyAdminAuthoringTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        // A fresh caller per test: the dry-run proofs live in the (shared) tenant cache, keyed by caller.
        _fixture.World.Caller = Guid.NewGuid();
        _fixture.World.AddPolicy(PolicyId, "POL-TEST", lane: 100000000, enabled: true, domain: null, current: V1);
        _fixture.World.AddVersion(V1, PolicyId, number: 1, inForceTo: null);
        _fixture.World.AddPolicy(BillingPolicyId, "POL-BILL", lane: 100000000, enabled: true, domain: 100000000, current: null);
        _client = _fixture.ClientFor("oid-" + _fixture.World.Caller.ToString("N"));
    }

    private static object Draft(string body = Body, string template = Template, string plan = DecidePlan) => new
    {
        ruleType = "Existence",
        ruleBody = body,
        messageTemplate = template,
        shortHeadline = "Needs a look",
        proposedAction = "Review the matter",
        decisionPlan = plan,
    };

    private static SignalHit Hit(Guid matter, string name, Guid? core = null) =>
        new("sprk_matter", matter, name, "M-" + name, new SignalCoreRecord("sprk_matter", core ?? matter, MatterType, null), null, null,
            new Dictionary<string, object?>(), Array.Empty<SignalEvidenceRef>());

    private async Task<HttpResponseMessage> DryRun(Guid? policyId, string body = Body, string? lane = null, string template = Template) =>
        await _client.PostAsJsonAsync($"{Root}/dry-run", new { policyId, lane, ruleType = "Existence", ruleBody = body, messageTemplate = template });

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> ReasonOf(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("reasonCode").GetString()!;

    // ── Catalog (#46) ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_OffersOnlyTheCompilersLists_AndTheTriageCategoriesReadAsTheCaller()
    {
        var response = await _client.GetAsync($"{Root}/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var catalog = await Json(response);
        var subjects = catalog.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("entity").GetString()!).ToList();
        subjects.Should().NotBeEmpty().And.OnlyContain(s => PredicateCompiler.EvaluatorGlobalReadableEntities.Contains(s));
        var joins = catalog.GetProperty("subjects").EnumerateArray()
            .SelectMany(s => s.GetProperty("joins").EnumerateArray()
                .Select(j => (s.GetProperty("entity").GetString()!, j.GetProperty("related").GetString()!, j.GetProperty("path").GetString()!)))
            .ToList();
        joins.Should().BeEquivalentTo(PredicateCompiler.VerifiedJoins.Select(kv => (kv.Key.Subject, kv.Key.Related, kv.Value)),
            "every verified join, and nothing else");
        catalog.GetProperty("operators").GetProperty("comparison").EnumerateArray().Select(o => o.GetString())
            .Should().BeEquivalentTo(PredicateCompiler.ComparisonOperators.Keys);
        catalog.GetProperty("triage").GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .Should().Equal("Budget change", "Fee or scope change");
        _fixture.World.Requests.Should().Contain(r => r.StartsWith("GET sprk_triagecategories?", StringComparison.Ordinal));
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public void Catalog_EveryOfferedSubjectFieldOperatorAndJoin_Compiles()
    {
        // The acceptance criterion, exhaustively: a rule body built from each catalog entry passes the SAME validation save uses
        // (ValidateForAuthoring since task 102a: ValidateForSave plus the authorable-column allow-list).
        var validator = _fixture.Services.GetRequiredService<PolicyVersionValidator>();
        var category = Guid.NewGuid();
        var catalog = OntologyAdminAuthoringService.BuildCatalog(new[] { new OntologyCatalogCategory(category, "Fee or scope change", true) });
        var bodies = new List<(string What, string Type, string Body)>();

        // Placeholders (@S, @OP ...) instead of interpolation: the JSON's own braces stay literal.
        static string B(string template, params (string Key, object Value)[] values) =>
            values.Aggregate(template, (t, v) => t.Replace(v.Key, Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture)));

        foreach (var subject in catalog.Subjects)
        {
            var s = ("@S", (object)subject.Entity);
            foreach (var op in catalog.Operators.Comparison)
                bodies.Add(($"{subject.Entity} when {op}", "Existence",
                    B("""{"type":"Existence","subject":"@S","when":{"statecode":{"@OP":0}},"all":[]}""", s, ("@OP", op))));
            foreach (var test in catalog.Operators.NullTests)
                bodies.Add(($"{subject.Entity} {test}", "Existence",
                    B("""{"type":"Existence","subject":"@S","when":{"statecode":{"@T":true}},"all":[]}""", s, ("@T", test))));
            bodies.Add(($"{subject.Entity} in", "Existence", B("""{"type":"Existence","subject":"@S","when":{"statecode":[0,1]},"all":[]}""", s)));
            foreach (var field in subject.DateOnlyFields)
                foreach (var lower in catalog.Operators.RangeLower)
                    foreach (var upper in catalog.Operators.RangeUpper)
                        bodies.Add(($"{subject.Entity}.{field} {lower}..{upper}", "Existence",
                            B("""{"type":"Existence","subject":"@S","when":{"@F":{"@LO":"now-@MAXd","@UP":"now+3d"}},"all":[]}""",
                                s, ("@F", field), ("@LO", lower), ("@UP", upper), ("@MAX", catalog.RelativeDates.MaxDays))));
            foreach (var field in subject.ThresholdFields)
                foreach (var op in catalog.Operators.Threshold)
                    bodies.Add(($"{subject.Entity}.{field.Field} {op}", "Threshold",
                        B("""{"type":"Threshold","subject":"@S","threshold":{"field":"@F","operator":"@OP","value":@V}}""",
                            s, ("@F", field.Field), ("@OP", op), ("@V", field.Max))));
            foreach (var join in subject.Joins)
            {
                foreach (var kind in new[] { "exists", "notExists" })
                    bodies.Add(($"{subject.Entity} {kind} {join.Related}", "Existence",
                        B("""{"type":"Existence","subject":"@S","all":[{"@K":"@R","path":"@P","filter":{"statecode":0}}]}""",
                            s, ("@K", kind), ("@R", join.Related), ("@P", join.Path))));
                foreach (var field in join.DateOnlyFields)
                    bodies.Add(($"{subject.Entity} exists {join.Related}.{field}", "Existence",
                        B("""{"type":"Existence","subject":"@S","all":[{"exists":"@R","path":"@P","filter":{"@F":{">=":"now-30d","<=":"now"}}}]}""",
                            s, ("@R", join.Related), ("@P", join.Path), ("@F", field))));
                if (join.Related == catalog.Triage.Entity)
                    bodies.Add(($"{subject.Entity} triage", "Existence",
                        B("""{"type":"Existence","subject":"@S","all":[{"exists":"@R","path":"@P","filter":{"@F":["@C"]}}]}""",
                            s, ("@R", join.Related), ("@P", join.Path), ("@F", catalog.Triage.Field), ("@C", category))));
            }
        }

        bodies.Should().HaveCountGreaterThan(40);
        foreach (var (what, type, body) in bodies)
        {
            var result = validator.ValidateForAuthoring(type, body, null).Validation;
            result.IsValid.Should().BeTrue($"the catalog offers '{what}', so it must compile: {string.Join("; ", result.Errors)}");
        }

        // Nothing outside the compiler's lists: the relative-date bound one past the grammar is refused.
        validator.ValidateForSave("Existence",
            """{"type":"Existence","subject":"sprk_matter","when":{"sprk_closeddate":{">=":"now-@Nd","<=":"now"}},"all":[]}"""
                .Replace("@N", (catalog.RelativeDates.MaxDays + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            null).IsValid.Should().BeFalse();
        catalog.RuleTypes.Should().Equal("Existence", "Threshold");
    }


    // ── Task 102a: the per-lane lists and the authorable fields ─────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_EachLane_CarriesTheServerCatalogs_ReadFromTheirSources()
    {
        var response = await _client.GetAsync($"{Root}/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var lanes = (await Json(response)).GetProperty("lanes").EnumerateArray().ToList();
        lanes.Select(l => l.GetProperty("lane").GetString()).Should().Equal("Decide", "Do");

        static IEnumerable<(string Code, string Label, string[] Excludes)> Actions(JsonElement list) =>
            list.EnumerateArray().Select(a => (a.GetProperty("code").GetString()!, a.GetProperty("label").GetString()!,
                a.GetProperty("excludes").EnumerateArray().Select(e => e.GetString()!).ToArray()));
        static IEnumerable<(string Code, string Label, string[] Excludes)> Expected(IEnumerable<DecisionActionDefinition> defs) =>
            // Task 161 (D-180): a substitution-only action (create-budget) is never authorable, nor named as an exclusion.
            defs.Where(d => !d.SubstitutionOnly).Select(d => (d.Code, d.Label,
                d.Excludes.Where(e => !(DecisionActionCatalog.TryGet(e, out var excluded) && excluded.SubstitutionOnly)).ToArray()));
        static IEnumerable<(int, string)> Options(JsonElement list) =>
            list.EnumerateArray().Select(o => (o.GetProperty("value").GetInt32(), o.GetProperty("label").GetString()!));

        foreach (var lane in lanes)
        {
            var which = Enum.Parse<DecisionLane>(lane.GetProperty("lane").GetString()!);
            Actions(lane.GetProperty("actions")).Should().BeEquivalentTo(
                Expected(DecisionActionCatalog.All.Where(a => a.PlanLane == which)), o => o.WithStrictOrdering(), $"{which}: the plan actions");
            Actions(lane.GetProperty("nextSteps")).Should().BeEquivalentTo(
                Expected(DecisionActionCatalog.All.Where(a => a.IsNextStep)), o => o.WithStrictOrdering(), "the Next-step creators");
            lane.GetProperty("dismissalReasons").EnumerateArray().Select(r => (r.GetProperty("value").GetString(), r.GetProperty("label").GetString()))
                .Should().Equal(DecisionActionCatalog.DismissalReasons(which).Select(r => ((string?)r.Code, (string?)r.Label)));
            Options(lane.GetProperty("severities")).Should().Equal(OntologyAdminRulesService.SeverityLabels.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
            Options(lane.GetProperty("workTypes")).Should().Equal(OntologyAdminRulesService.WorkTypeLabels.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)),
                "the server has no lane mapping for work types, so each lane carries the full list");
        }

        // Spot checks against the catalog's declared shape, so a test that only mirrors the source cannot hide a broken source.
        var decide = Actions(lanes[0].GetProperty("actions")).ToDictionary(a => a.Code);
        decide.Keys.Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance", "assign-work");
        decide["revise-budget"].Excludes.Should().Equal("approve-variance");
        decide["approve-variance"].Excludes.Should().Equal("revise-budget");
        var doActions = Actions(lanes[1].GetProperty("actions")).ToDictionary(a => a.Code);
        doActions["mark-complete"].Excludes.Should().BeEquivalentTo("reschedule", "reassign");
        Options(lanes[1].GetProperty("severities")).Select(o => o.Item2).Should().Equal("Info", "Warning", "Critical");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public void Catalog_SeveritiesAndWorkTypes_AreExactlyTheValuesTheSaveAccepts()
    {
        // The catalog reads OntologyAdminRulesService's labels; the save checks SignalWriter's severities and SignalReadService's work
        // types. Two sources: this pins them to the same sets, so the wizard never offers a value the save refuses, or hides one.
        var catalog = OntologyAdminAuthoringService.BuildCatalog(Array.Empty<OntologyCatalogCategory>());
        int[] saveSeverities = [SignalWriter.SeverityInfo, SignalWriter.SeverityWarning, SignalWriter.SeverityCritical];

        foreach (var lane in catalog.Lanes)
        {
            lane.Severities.Select(o => o.Value).Should().BeEquivalentTo(saveSeverities, $"{lane.Lane}: the severities the save accepts");
            lane.WorkTypes.Select(o => o.Value).Should().BeEquivalentTo(SignalReadService.WorkTypeKeys.Keys, $"{lane.Lane}: the work types the save accepts");
        }
    }

    [Fact]
    public void Catalog_EveryColumnItOffers_IsAllowListed_AndEveryAuthorableFieldCompiles()
    {
        var validator = _fixture.Services.GetRequiredService<PolicyVersionValidator>();
        var catalog = OntologyAdminAuthoringService.BuildCatalog(Array.Empty<OntologyCatalogCategory>());
        var allow = PolicyVersionValidator.AuthorableColumns;
        var nullTest = catalog.Operators.NullTests[0];

        foreach (var subject in catalog.Subjects)
        {
            subject.AuthorableFields.Should().NotBeEmpty($"{subject.Entity} must offer at least one field");
            foreach (var field in subject.DateOnlyFields.Concat(subject.ThresholdFields.Select(t => t.Field)).Concat(subject.AuthorableFields))
            {
                allow.Should().Contain((subject.Entity, field), $"the catalog offers {subject.Entity}.{field}");
            }

            foreach (var field in subject.AuthorableFields)
            {
                var body = """{"type":"Existence","subject":"@S","when":{"@F":{"@T":true}},"all":[]}"""
                    .Replace("@S", subject.Entity, StringComparison.Ordinal).Replace("@F", field, StringComparison.Ordinal).Replace("@T", nullTest, StringComparison.Ordinal);
                var result = validator.ValidateForAuthoring("Existence", body, null).Validation;
                result.IsValid.Should().BeTrue($"{subject.Entity}.{field} is offered, so it must compile: {string.Join("; ", result.Errors)}");
            }

            foreach (var join in subject.Joins)
            {
                allow.Should().Contain((join.Related, join.Path));
                foreach (var field in join.DateOnlyFields.Concat(join.AuthorableFields))
                {
                    allow.Should().Contain((join.Related, field), $"the catalog offers {join.Related}.{field}");
                    var body = """{"type":"Existence","subject":"@S","all":[{"exists":"@R","path":"@P","filter":{"@F":{"@T":true}}}]}"""
                        .Replace("@S", subject.Entity, StringComparison.Ordinal).Replace("@R", join.Related, StringComparison.Ordinal)
                        .Replace("@P", join.Path, StringComparison.Ordinal).Replace("@F", field, StringComparison.Ordinal).Replace("@T", nullTest, StringComparison.Ordinal);
                    var result = validator.ValidateForAuthoring("Existence", body, null).Validation;
                    result.IsValid.Should().BeTrue($"{join.Related}.{field} is offered, so it must compile: {string.Join("; ", result.Errors)}");
                }
            }
        }

        var matter = catalog.Subjects.Single(s => s.Entity == "sprk_matter");
        matter.AuthorableFields.Should().NotContain(matter.ThresholdFields.Select(t => t.Field), "a Threshold column is offered as a ThresholdField only");
        matter.Joins.Single(j => j.Related == "sprk_communication").AuthorableFields
            .Should().Contain(new[] { "sprk_triagecategory", "sprk_receiveddate", "sprk_reviewoutcome" }, "the flagship rule can be rebuilt from the catalog")
            .And.NotContain("sprk_regardingmatter", "the join path is named by path, not as a filter field");
    }

    // ── Task 102a: the validate route (compile only) ──────────────────────────────────────────────────────────

    private const string FlagshipShapedBody =
        """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_triagecategory":["8b62dd84-1fbc-f111-aaaf-3833c5e9614d"],"sprk_receiveddate":{">=":"now-30d"}}}]}""";

    private async Task<HttpResponseMessage> Validate(object? draft = null, string? lane = "Decide", Guid? policyId = null) =>
        await _client.PostAsJsonAsync($"{Root}/validate", new { policyId, lane, draft = draft ?? Draft() });

    private static Dictionary<string, string[]> ValidateErrors(JsonElement result) =>
        result.GetProperty("errors").EnumerateArray()
            .GroupBy(e => e.GetProperty("field").GetString()!)
            .ToDictionary(g => g.Key, g => g.Select(e => e.GetProperty("message").GetString()!).ToArray());

    [Fact]
    public async Task Validate_AValidDraft_Is200Valid_WithTheTemplateEligibleFields_AndNothingIsWrittenEvaluatedOrProved()
    {
        var w = _fixture.World;

        var response = await Validate(Draft(body: FlagshipShapedBody));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await Json(response);
        result.GetProperty("valid").GetBoolean().Should().BeTrue();
        result.GetProperty("errors").GetArrayLength().Should().Be(0);
        result.GetProperty("reasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        result.GetProperty("templateEligibleFields").EnumerateArray().Select(f => f.GetString())
            .Should().Equal(new[] { "sprk_triagecategory", "statecode" }, "the when field, and the exists field pinned to one value; never the ranged date");
        w.Requests.Should().Equal(new[] { "GET WhoAmI()", PrivilegeRequest(w.Caller) }, "a new rule is compiled only: no other read");
        w.Writes.Should().BeEmpty();
        _fixture.Engine.Evaluated.Should().BeEmpty();

        // Validate is not a test run: no proof was recorded, so the save still needs a dry run.
        var save = await Save(draft: Draft(body: FlagshipShapedBody));
        save.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(save)).Should().Be("ontology.admin.dry_run_required");
    }

    [Fact]
    public async Task Validate_AnInvalidDraft_Is200NotValid_WithExactlyTheFieldErrorsTheSaveReturns()
    {
        var draft = Draft(template: "The {{sprk_secretfield}} is wrong.", plan: """{"actions":["mark-complete"]}""");

        var validate = await Json(await Validate(draft));
        var save = await Save(draft: draft);

        validate.GetProperty("valid").GetBoolean().Should().BeFalse();
        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var saveErrors = (await Json(save)).GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(m => m.GetString()!).ToArray());
        ValidateErrors(validate).Should().BeEquivalentTo(saveErrors, "one validation path: validate and save cannot disagree");
        ValidateErrors(validate).Keys.Should().BeEquivalentTo("messageTemplate", "decisionPlan");
        validate.GetProperty("templateEligibleFields").EnumerateArray().Select(f => f.GetString()).Should().Equal(new[] { "statecode" },
            "the body compiled, so the Wording step can still offer its fields");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ForAnExistingPolicy_ChecksAgainstThePolicysOwnLane_NotTheRequests()
    {
        // PolicyId is a Decide-lane policy: a Do-lane plan is refused even though the request says Do, as publish would refuse it.
        var other = await Json(await Validate(Draft(plan: """{"actions":["mark-complete"]}"""), lane: "Do", policyId: PolicyId));
        var own = await Json(await Validate(Draft(), lane: "Do", policyId: PolicyId));

        other.GetProperty("valid").GetBoolean().Should().BeFalse();
        ValidateErrors(other).Keys.Should().Equal("decisionPlan");
        own.GetProperty("valid").GetBoolean().Should().BeTrue(own.ToString());
        _fixture.World.Requests.Should().Contain(r => r.Contains($"sprk_policyid eq {PolicyId:D} and (sprk_domain eq null or sprk_domain eq 100000001)",
            StringComparison.Ordinal), "the policy is read as the caller through the domain allow-list");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_AndSave_RefuseADraftThatNamesCreateBudget_ItIsOnlyEverSubstituted_D180()
    {
        // Task 161 (D-180): create-budget replaces revise-budget when the server builds the plan for a matter with no budget; a rule author
        // can never name it, so a published plan is never edited by it (D-14).
        var draft = Draft(plan: """{"actions":["send-budget-inquiry","create-budget","approve-variance"]}""");

        var validate = await Json(await Validate(draft));
        var save = await Save(draft: draft);

        validate.GetProperty("valid").GetBoolean().Should().BeFalse();
        ValidateErrors(validate)["decisionPlan"].Single().Should().Contain("action_not_allowed").And.Contain("Decide lane's catalog");
        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(save)).GetProperty("errors").GetProperty("decisionPlan")[0].GetString().Should().Be(ValidateErrors(validate)["decisionPlan"].Single());
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ACodeInBothActionsAndNextSteps_GetsTheDuplicateMessageAndCode_AsTheSaveDoes()
    {
        // The catalog offers assign-work as a Decide action AND as a next step; the plan resolver refuses a code in both lists. The
        // author must be told that, not that the code is outside the lane's catalog.
        var draft = Draft(plan: """{"actions":["assign-work"],"nextSteps":["assign-work"]}""");

        var validate = await Json(await Validate(draft));
        var save = await Save(draft: draft);

        validate.GetProperty("valid").GetBoolean().Should().BeFalse();
        validate.GetProperty("reasonCode").GetString().Should().Be("ontology.admin.duplicate_action");
        var message = ValidateErrors(validate)["decisionPlan"].Single();
        message.Should().Contain("duplicate_action").And.Contain("never in both").And.NotContain("lane's catalog");
        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonOf(save)).Should().Be("ontology.admin.duplicate_action");
        (await Json(save)).GetProperty("errors").GetProperty("decisionPlan")[0].GetString().Should().Be(message);

        // A code outside the lane keeps the catalog message and carries no code.
        var outside = await Json(await Validate(Draft(plan: """{"actions":["mark-complete"]}""")));
        ValidateErrors(outside)["decisionPlan"].Single().Should().Contain("Decide lane's catalog");
        outside.GetProperty("reasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ANewRuleAboutATableWithNoPolicySubjectType_IsNotValid_AsTheSaveWouldRefuseIt()
    {
        var result = await Json(await Validate(Draft(body: """{"type":"Existence","subject":"sprk_budgetrevision","when":{"statecode":0},"all":[]}""")));

        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        ValidateErrors(result)["ruleBody"].Single().Should().Contain("no policy subject type");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validate_ANonOntologyOrUnreadablePolicy_IsTheUniform404(bool billing)
    {
        if (!billing) _fixture.World.Unreadable.Add(("sprk_policy", PolicyId));

        var response = await Validate(policyId: billing ? BillingPolicyId : PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonOf(response)).Should().Be("ontology.admin.not_found");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validate_AsAConsoleUser_WithoutTheAuthoringPrivilege_Is403_AndNothingElseIsRead(bool existingPolicy)
    {
        var w = _fixture.World;
        w.HoldsAuthoringPrivilege = false;

        var response = await Validate(policyId: existingPolicy ? PolicyId : null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.forbidden");
        w.Requests.Should().Equal(new[] { "GET WhoAmI()", PrivilegeRequest(w.Caller) }, "the same gate as the dry run, before anything else");
        w.Writes.Should().BeEmpty();
    }

    // ── Task 102a: an unlisted column is refused by every authoring route, with one code ─────────────────────────

    public static TheoryData<string> UnlistedColumnBodies => new()
    {
        """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0,"sprk_secretnotes":"x"},"all":[]}""",
        """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_body":"x"}}]}""",
        """{"type":"Existence","subject":"sprk_matter","all":[{"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_amount":{">":0}}}]}""",
    };

    [Theory]
    [MemberData(nameof(UnlistedColumnBodies))]
    public async Task AnUnlistedColumn_IsRefusedWithTheSameCode_ByValidateDryRunSaveAndPublish(string body)
    {
        const string code = "ontology.admin.column_not_authorable";

        var validate = await Json(await Validate(Draft(body: body)));
        var dryRunExisting = await DryRun(PolicyId, body);
        var dryRunNew = await DryRun(null, body, lane: "Decide");
        var save = await Save(draft: Draft(body: body));
        var publish = await Publish(PolicyId, Draft(body: body));

        validate.GetProperty("valid").GetBoolean().Should().BeFalse();
        validate.GetProperty("reasonCode").GetString().Should().Be(code);
        ValidateErrors(validate).Keys.Should().Equal("ruleBody");
        foreach (var (route, response) in new[] { ("dry run", dryRunExisting), ("dry run (new)", dryRunNew), ("save", save), ("publish", publish) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{route}: {await response.Content.ReadAsStringAsync()}");
            (await ReasonOf(response)).Should().Be(code, route);
            (await Json(response)).GetProperty("errors").GetProperty("ruleBody")[0].GetString()
                .Should().Be(ValidateErrors(validate)["ruleBody"][0], $"{route} and validate give the same message");
        }

        _fixture.Engine.Evaluated.Should().BeEmpty("a refused body is never evaluated");
        _fixture.World.Writes.Should().BeEmpty();
    }

    // ── Dry run (#39) ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_WritesNothing_ClassifiesOpenSuppressedNew_AndDropsAMatterTheCallerCannotRead()
    {
        var w = _fixture.World;
        w.AddSignal(PolicyId, "sprk_matter", MatterA, 1, SignalWriter.SignalStatusOpen, core: MatterA, coreType: MatterType);
        w.AddSignal(PolicyId, "sprk_matter", MatterB, 1, SignalWriter.SignalStatusResolved, resolution: SignalWriter.ResolutionDismissed,
            resolvedOn: DateTime.UtcNow.AddDays(-40), suppressedUntil: DateTime.UtcNow.AddDays(10), core: MatterB, coreType: MatterType);
        // An open Signal on a matter the caller cannot read: neither listed nor counted.
        w.AddSignal(PolicyId, "sprk_matter", HiddenMatter, 1, SignalWriter.SignalStatusOpen, core: HiddenMatter, coreType: MatterType, readable: false);
        w.Unreadable.Add(("sprk_matter", HiddenMatter));
        _fixture.Engine.Hits = [Hit(MatterA, "Alpha"), Hit(MatterB, "Bravo"), Hit(MatterC, "Charlie"), Hit(HiddenMatter, "Hidden")];

        var response = await DryRun(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await Json(response);
        var rows = result.GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("subjectId").GetGuid(), r => r.GetProperty("status").GetString());
        rows.Should().BeEquivalentTo(new Dictionary<Guid, string?>
        {
            [MatterA] = "open",
            [MatterB] = "suppressed",
            [MatterC] = "new",
        }, "the hidden matter is absent");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(HiddenMatter.ToString("D")).And.NotContain("Hidden");
        result.GetProperty("wouldSupersede").GetInt32().Should().Be(1, "only the open Signal the caller can read");
        result.GetProperty("wouldReraise").GetInt32().Should().Be(1);
        result.GetProperty("open").GetInt32().Should().Be(1);
        result.GetProperty("suppressed").GetInt32().Should().Be(1);
        result.GetProperty("new").GetInt32().Should().Be(1);
        result.GetProperty("recorded").GetBoolean().Should().BeTrue();
        w.Writes.Should().BeEmpty("a dry run writes nothing");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_AMatterCoreTheCallerCannotRead_IsAbsentEvenWhenTheSubjectIsReadable()
    {
        // A To Do the caller can read, grouped under a matter they cannot open (D-33/D-123): the row is not shown.
        var todo = Guid.NewGuid();
        _fixture.World.Unreadable.Add(("sprk_matter", HiddenMatter));
        _fixture.Engine.Hits =
        [
            new SignalHit("sprk_todo", todo, "Todo", null, new SignalCoreRecord("sprk_matter", HiddenMatter, MatterType, null), null, null,
                new Dictionary<string, object?>(), Array.Empty<SignalEvidenceRef>()),
        ];

        var response = await DryRun(null, """{"type":"Existence","subject":"sprk_todo","when":{"statecode":0},"all":[]}""", lane: "Do");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await Json(response);
        result.GetProperty("rows").GetArrayLength().Should().Be(0);
        result.GetProperty("new").GetInt32().Should().Be(0);
        _fixture.World.Requests.Should().Contain(r => r.StartsWith("GET sprk_matters?$select=sprk_matterid", StringComparison.Ordinal),
            "the core record is read as the caller");
    }

    [Fact]
    public async Task DryRun_EvaluatesAPolicyBuiltInMemory_FromTheCallersReadAndTheDraft_NeverTheRequestsLane()
    {
        var response = await DryRun(PolicyId, lane: "Do");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var evaluated = _fixture.Engine.Evaluated.Should().ContainSingle().Subject;
        evaluated.PolicyId.Should().Be(PolicyId);
        evaluated.PolicyCode.Should().Be("POL-TEST");
        evaluated.Lane.Should().Be(100000000, "the lane comes from the caller's read of the policy, not the request");
        evaluated.RuleBody.Should().Be(Body);
        evaluated.RuleType.Should().Be("Existence");
        _fixture.World.Requests.Should().Contain(r => r.StartsWith("GET sprk_policies?", StringComparison.Ordinal)
            && r.Contains("(sprk_domain eq null or sprk_domain eq 100000001)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRun_AnInvalidBody_IsFieldLevel400_AndNothingIsEvaluatedOrRecorded()
    {
        var response = await DryRun(PolicyId, """{"type":"Existence","subject":"sprk_secret","when":{"statecode":0},"all":[]}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").TryGetProperty("ruleBody", out _).Should().BeTrue();
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_ANonOntologyPolicy_IsTheUniform404_AndIsNeverEvaluated()
    {
        var response = await DryRun(BillingPolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonOf(response)).Should().Be("ontology.admin.not_found");
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    // ── Save a new rule (FR-10) ───────────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Save(string code = "POL-NEW", object? draft = null, string lane = "Decide") =>
        await _client.PostAsJsonAsync($"{Root}/policies", new { policyCode = code, name = "A new rule", lane, draft = draft ?? Draft() });

    [Fact]
    public async Task Save_AValidNewRule_AfterADryRun_SavesOffInTheOntologyDomain_WithItsFirstVersionCurrent_AsTheCaller()
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Save();

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var saved = await Json(response);
        saved.GetProperty("enabled").GetBoolean().Should().BeFalse();
        var w = _fixture.World;
        w.Writes.Select(x => x.Kind).Should().Equal("POST sprk_policies", $"PATCH sprk_policies({saved.GetProperty("policyId").GetGuid():D})");
        var create = w.Writes[0].Json;
        create.GetProperty("sprk_enabled").GetBoolean().Should().BeFalse("a new rule saves Off (FR-10)");
        create.GetProperty("sprk_domain").GetInt32().Should().Be(100000001, "created explicitly in the Ontology domain");
        create.GetProperty("sprk_subjecttype").GetInt32().Should().Be(100000000);
        var version = create.GetProperty("sprk_policyversion_policy")[0];
        version.GetProperty("sprk_rulebody").GetString().Should().Be(Body);
        version.GetProperty("sprk_versionnumber").GetInt32().Should().Be(1);
        version.GetProperty("sprk_AuthoredBy@odata.bind").GetString().Should().Be($"/systemusers({w.Caller:D})");
        w.Writes[1].Json.GetProperty("sprk_CurrentVersion@odata.bind").GetString()
            .Should().Be($"/sprk_policyversions({saved.GetProperty("policyVersionId").GetGuid():D})");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty("an Off rule is not evaluated");
    }

    [Fact]
    public async Task Save_AnInvalidBody_ReturnsFieldErrors_AndWritesNothing()
    {
        var response = await Save(draft: Draft(template: "The {{sprk_secretfield}} is wrong."));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.TryGetProperty("messageTemplate", out _).Should().BeTrue("the template names a field the body does not read (section 0.3)");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_APlanFromTheOtherLane_IsAFieldError_AndWritesNothing()
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Save(draft: Draft(plan: """{"actions":["mark-complete"]}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").TryGetProperty("decisionPlan", out _).Should().BeTrue();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_WithoutADryRunOnTheExactBody_Is409_AndWritesNothing()
    {
        (await DryRun(null, OtherBody, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Save();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.dry_run_required");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_AsAConsoleUser_IsRefusedByDataverse_403()
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.RefuseWrites = true;

        var response = await Save();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonOf(response)).Should().Be("ontology.admin.refused");
        _fixture.World.Writes.Should().BeEmpty("Dataverse refused the one create; nothing landed");
    }

    [Fact]
    public async Task Save_ACodeAnotherOntologyRuleUses_Is409_AndWritesNothing()
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Save(code: "POL-TEST");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_code_taken");
        _fixture.World.Writes.Should().BeEmpty();
    }

    // ── Publish (D-13, D-14) ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Publish(Guid policyId, object? draft = null) =>
        await _client.PostAsJsonAsync($"{Root}/policies/{policyId:D}/versions", new { draft = draft ?? Draft() });

    [Fact]
    public async Task Publish_AfterADryRunOnTheSameBody_CreatesTheVersion_StampsTheOld_MovesThePointer_ThenSupersedesOnce()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Engine.Result = new PolicyReevaluationResult(true, SupersededClosed: 1, Raised: 1, FailureSummary: string.Empty);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var published = await Json(response);
        var newVersion = published.GetProperty("policyVersionId").GetGuid();
        published.GetProperty("versionNumber").GetInt32().Should().Be(2);
        published.GetProperty("previousVersionId").GetGuid().Should().Be(V1);
        published.GetProperty("reevaluation").GetString().Should().Be("completed");
        published.GetProperty("supersededClosed").GetInt32().Should().Be(1);
        published.GetProperty("raised").GetInt32().Should().Be(1);

        var w = _fixture.World;
        w.Writes.Select(x => x.Kind).Should().Equal(
            "POST sprk_policyversions", $"PATCH sprk_policyversions({V1:D})", $"PATCH sprk_policies({PolicyId:D})");
        w.Writes[1].IfMatch.Should().NotBeNull("the old version's stamp is conditional on the row as read");
        w.Writes[2].IfMatch.Should().NotBeNull("the pointer move is conditional on the policy as read");
        w.Versions[V1].InForceTo.Should().NotBeNull("the old version's sprk_inforceto is stamped (D-14)");
        w.Policies[PolicyId].Current.Should().Be(newVersion);
        w.Versions[newVersion].InForceTo.Should().BeNull("the new version is in force");
        _fixture.Engine.ReevaluateCalls.Should().ContainSingle().Which.Should().Be((PolicyId, newVersion),
            "the single-policy pass runs once, after the pointer names the new version");
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WithoutADryRun_OrAfterAnEdit_Is409_AndWritesNothing()
    {
        var none = await Publish(PolicyId);
        none.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(none)).Should().Be("ontology.admin.dry_run_required");

        (await DryRun(PolicyId, OtherBody)).StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await Publish(PolicyId); // the dry run was on OtherBody; any edit invalidates it

        edited.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.World.Writes.Should().BeEmpty();
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ADryRunOnAnotherPolicy_DoesNotCount()
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ToANonOntologyPolicy_IsTheUniform404_WithZeroWrites()
    {
        var response = await Publish(BillingPolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonOf(response)).Should().Be("ontology.admin.not_found");
        _fixture.World.Writes.Should().BeEmpty("the domain check runs before any write");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        _fixture.World.Requests.Should().Contain(r => r.Contains($"sprk_policyid eq {BillingPolicyId:D} and (sprk_domain eq null or sprk_domain eq 100000001)",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_ToAPolicyTheCallerCannotRead_IsTheUniform404_WithZeroWrites()
    {
        _fixture.World.Unreadable.Add(("sprk_policy", PolicyId));

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonOf(response)).Should().Be("ontology.admin.not_found", "the same 404 as a policy that does not exist");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_AsAConsoleUser_IsRefusedByDataverse_403_AndNothingElseIsWritten()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.RefuseWrites = true;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonOf(response)).Should().Be("ontology.admin.refused");
        _fixture.World.Writes.Should().BeEmpty();
        _fixture.World.Versions[V1].InForceTo.Should().BeNull();
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_AnInvalidBody_ReturnsFieldErrors_AndWritesNothing()
    {
        var response = await Publish(PolicyId, Draft(body: """{"type":"Switch","subject":"sprk_matter"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ToARetiredPolicy_Is409_AndWritesNothing()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.Policies[PolicyId].State = 1;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_retired");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenAConcurrentPublishStampedTheOldVersion_Is409_AndTheNewVersionIsMarkedNeverInForce()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.BumpBeforeWrite = ("PATCH", $"sprk_policyversions({V1:D})");

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        var w = _fixture.World;
        var created = w.Versions.Values.Single(v => v.Number == 2);
        created.InForceTo.Should().NotBeNull("a version that never became current is ended at once");
        w.Policies[PolicyId].Current.Should().Be(V1, "the pointer never moved");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WhenThePolicyChangedBeforeThePointerMove_UndoesTheOldStamp_AndIs409()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.BumpBeforeWrite = ("PATCH", $"sprk_policies({PolicyId:D})");

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var w = _fixture.World;
        w.Versions[V1].InForceTo.Should().BeNull("the current version is never left stamped (EventId 50309)");
        w.Policies[PolicyId].Current.Should().Be(V1);
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().NotBeNull();
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_AnOffPolicy_PublishesWithoutEvaluating()
    {
        _fixture.World.Policies[PolicyId].Enabled = false;
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Json(response)).GetProperty("reevaluation").GetString().Should().Be("notEvaluated");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty("ReevaluatePolicyAsync refuses an Off policy; it is evaluated when turned On");
    }

    [Fact]
    public async Task Publish_WhenTheSupersedePassFails_TheVersionStands_AndTheResultSaysSo()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Engine.Result = new PolicyReevaluationResult(false, 0, 0, "refused=1");

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("reevaluation").GetString().Should().Be("failed");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("refused=1", "the job's summary is logged, not returned");
    }

    private void AssertVersionPatchesTouchOnlyInForceTo()
    {
        // D-14: no route updates a published version's body, type, template or plan; sprk_inforceto is the only field ever patched.
        foreach (var patch in _fixture.World.Writes.Where(x => x.Kind.StartsWith("PATCH sprk_policyversions(", StringComparison.Ordinal)))
        {
            patch.Json.EnumerateObject().Select(p => p.Name).Should().Equal(["sprk_inforceto"], patch.Kind);
        }
    }

    [Fact]
    public void Source_TheOnlyPatchOfAVersion_IsTheInForceToStamp()
    {
        // D-14, read off the code: every PATCH the service sends either targets a policy row or is inside StampInForceToAsync, whose
        // payload is the one sprk_inforceto member. A new PATCH of a version anywhere else fails this test.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api"))) dir = dir.Parent;
        dir.Should().NotBeNull("the repository root is above the test output");
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "server", "api", "Sprk.Bff.Api", "Services", "Ontology",
            "OntologyAdminAuthoringService.cs"));

        var stamp = Regex.Match(source, @"private Task<DataverseUserResponse> StampInForceToAsync\(.*?\r?\n    }\r?\n", RegexOptions.Singleline);
        stamp.Success.Should().BeTrue();
        stamp.Value.Should().Contain("[\"sprk_inforceto\"] = inForceTo").And.Contain("$\"{VersionEntitySet}({versionId:D})\"");
        Regex.Matches(stamp.Value, @"\[""").Should().HaveCount(1, "the stamp's payload has exactly one member");

        var outside = source.Remove(stamp.Index, stamp.Length);
        foreach (System.Text.RegularExpressions.Match call in Regex.Matches(outside, @"_user\.PatchAsync\(\s*([^,]+),"))
        {
            call.Groups[1].Value.Should().StartWith("$\"{PolicyEntitySet}(", "outside the stamp, only policy rows are patched");
        }

        Regex.Matches(outside, @"_user\.PatchAsync\(").Should().HaveCount(2, "the save's pointer and the publish's pointer");
        source.Should().NotContain("DeleteAsync(");
    }

    // ── Fix round 1: the dry run's privilege gate (F1-1) ─────────────────────────────────────────────────────────

    private static string PrivilegeRequest(Guid caller) => $"GET systemusers({caller:D}){AuthoringWorld.PrivilegePathSuffix}";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DryRun_AsAConsoleUser_WithoutTheAuthoringPrivilege_Is403_AndNothingIsReadOrEvaluated(bool existingPolicy)
    {
        // A dry run writes nothing, so Dataverse checks no role on it: without this gate any signed-in user could run any body through
        // the evaluator, one bit of rows they cannot read per request.
        var w = _fixture.World;
        w.HoldsAuthoringPrivilege = false;
        _fixture.Engine.Hits = [Hit(MatterA, "Alpha")];

        var response = existingPolicy ? await DryRun(PolicyId) : await DryRun(null, lane: "Decide");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.forbidden");
        _fixture.Engine.Evaluated.Should().BeEmpty("the evaluator is never called for a caller without the privilege");
        w.Requests.Should().Equal(new[] { "GET WhoAmI()", PrivilegeRequest(w.Caller) },
            "the privilege is asked as the caller, about the id WhoAmI returned, before anything else is read");
        w.Writes.Should().BeEmpty();

        // And no proof was recorded: the save that would follow is refused.
        var save = await Save();
        save.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(save)).Should().Be("ontology.admin.dry_run_required");
        w.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DataverseUserClientErrorCodes.ServiceError, HttpStatusCode.ServiceUnavailable, "ontology.admin.unavailable")]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed, HttpStatusCode.Forbidden, "sdap.access.deny.caller_unresolved")]
    [InlineData(DataverseUserClientErrorCodes.AccessDenied, HttpStatusCode.Forbidden, "ontology.admin.forbidden")]
    public async Task DryRun_WhenThePrivilegeCannotBeRead_FailsClosed_AndNothingIsEvaluated(string error, HttpStatusCode expected, string reason)
    {
        _fixture.World.PrivilegeError = error;

        var response = await DryRun(PolicyId);

        response.StatusCode.Should().Be(expected);
        (await ReasonOf(response)).Should().Be(reason);
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("200 without RolePrivileges", HttpStatusCode.ServiceUnavailable, "ontology.admin.unavailable")]
    [InlineData("throws", HttpStatusCode.ServiceUnavailable, "ontology.admin.unavailable")]
    [InlineData("404", HttpStatusCode.ServiceUnavailable, "ontology.admin.unavailable")]
    [InlineData("400", HttpStatusCode.ServiceUnavailable, "ontology.admin.unavailable")]
    [InlineData("another privilege", HttpStatusCode.Forbidden, "ontology.admin.forbidden")]
    public async Task DryRun_EveryOtherPrivilegeAnswer_FailsClosed_AndNothingIsEvaluated(string answer, HttpStatusCode expected, string reason)
    {
        // Review pass 2 (uac-r2 F3): every branch of CheckAuthoringPrivilegeAsync's mapping. Only a row naming the privilege is "held".
        _fixture.World.PrivilegeAnswer = answer switch
        {
            "200 without RolePrivileges" => () => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { Other = 1 })),
            "throws" => () => throw new HttpRequestException("The connection was reset."),
            "404" => () => DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found."),
            "400" => () => DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.BadRequest, "Bad request."),
            _ => () => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new
            {
                RolePrivileges = new[] { new { Depth = "Global", PrivilegeName = "prvReadsprk_PolicyVersion" } },
            })),
        };
        _fixture.Engine.Hits = [Hit(MatterA, "Alpha")];

        var response = await DryRun(PolicyId);

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(reason);
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_AsksThePrivilegeFresh_OnEveryRequest()
    {
        var w = _fixture.World;

        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        w.HoldsAuthoringPrivilege = false; // the role was removed between two requests
        var third = await DryRun(PolicyId);

        third.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no earlier answer is reused");
        w.Requests.Count(r => r == PrivilegeRequest(w.Caller)).Should().Be(3);
        _fixture.Engine.Evaluated.Should().HaveCount(2);
    }

    // ── Fix round 1: the dry run refuses a subject the save would refuse (K5) ─────────────────────────────────────

    [Theory]
    [InlineData("sprk_budget")]
    [InlineData("sprk_budgetrevision")]
    [InlineData("sprk_memo")]
    public async Task DryRun_ANewRuleAboutATableWithNoPolicySubjectType_Is400_AndIsNotEvaluated(string subject)
    {
        var body = """{"type":"Existence","subject":"@S","when":{"statecode":0},"all":[]}""".Replace("@S", subject, StringComparison.Ordinal);
        _fixture.Services.GetRequiredService<PolicyVersionValidator>().ValidateForSave("Existence", body, Template).IsValid
            .Should().BeTrue("the body itself compiles; only the policy subject type is missing");

        var response = await DryRun(null, body, lane: "Decide");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await Json(response)).GetProperty("errors").GetProperty("ruleBody")[0].GetString().Should().Contain("no policy subject type");
        _fixture.Engine.Evaluated.Should().BeEmpty();
    }

    // ── Fix round 1: the proof (F3-5, K2) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_AnotherCallersDryRun_IsNotAProof_409()
    {
        var w = _fixture.World;
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK, "user A ran the exact body");

        w.Caller = Guid.NewGuid(); // user B, same tenant, same body
        using var userB = _fixture.ClientFor("oid-" + w.Caller.ToString("N"));
        var response = await userB.PostAsJsonAsync($"{Root}/policies/{PolicyId:D}/versions", new { draft = Draft() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.dry_run_required");
        w.Writes.Should().BeEmpty();
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ADryRunInTheOtherLane_IsNotAProof_409()
    {
        (await DryRun(null, lane: "Do")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Save(lane: "Decide");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.dry_run_required");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public void ProofKey_BindsTheLane()
    {
        var caller = Guid.NewGuid();
        OntologyAdminAuthoringService.ProofKey(caller, null, DecisionLane.Decide, "Existence", Body, Template)
            .Should().NotBe(OntologyAdminAuthoringService.ProofKey(caller, null, DecisionLane.Do, "Existence", Body, Template));
    }

    [Fact]
    public async Task DryRun_WhenTheProofCacheThrows_RecordedIsFalse_AndPublishIs409()
    {
        // Service level: the proof store is the only thing substituted (a cache that throws on every call).
        var w = _fixture.World;
        var cache = new Mock<ITenantCache>(MockBehavior.Strict);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis is down"));
        cache.Setup(c => c.GetAsync<DateTimeOffset?>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis is down"));
        var service = new OntologyAdminAuthoringService(w, _fixture.Services.GetRequiredService<PolicyVersionValidator>(), _fixture.Engine,
            cache.Object, TimeProvider.System, NullLogger<OntologyAdminAuthoringService>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", "oid-" + w.Caller.ToString("N")), new Claim("tid", WorkspaceTestConstants.TestTenantId)], "test"));

        var dryRun = await service.DryRunAsync(principal, new OntologyDryRunRequest(PolicyId, null, "Existence", Body, Template), CancellationToken.None);

        dryRun.Outcome.Should().Be(OntologyAdminAuthoringOutcome.Ok);
        dryRun.Value!.Recorded.Should().BeFalse("the proof could not be stored, and the dry run says so");

        var publish = await service.PublishVersionAsync(principal, PolicyId,
            new OntologyPublishRequest(new OntologyRuleDraft("Existence", Body, Template, "Needs a look", "Review the matter", DecidePlan)),
            CancellationToken.None);

        publish.Outcome.Should().Be(OntologyAdminAuthoringOutcome.Conflict);
        publish.ReasonCode.Should().Be("ontology.admin.dry_run_required", "an unreadable proof is no proof (fail closed)");
        w.Writes.Should().BeEmpty();
    }

    // ── Fix round 1: a publish whose answer is lost (F2-2, F2-3) ─────────────────────────────────────────────────

    [Fact]
    public async Task Publish_WhenThePointerMoveTimesOut_AndDidNotLand_Compensates_And503()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.FailWrite = ("PATCH", $"sprk_policies({PolicyId:D})", false, true);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var w = _fixture.World;
        w.Policies[PolicyId].Current.Should().Be(V1, "the pointer never moved");
        w.Versions[V1].InForceTo.Should().BeNull("our end stamp on the current version is undone (EventId 50309)");
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().NotBeNull("the version that never became current is ended");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WhenThePointerMoveAnswers5xxButLanded_IsASuccess_AndNothingIsCompensated()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.FailWrite = ("PATCH", $"sprk_policies({PolicyId:D})", true, false);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var newVersion = (await Json(response)).GetProperty("policyVersionId").GetGuid();
        var w = _fixture.World;
        w.Policies[PolicyId].Current.Should().Be(newVersion);
        w.Versions[newVersion].InForceTo.Should().BeNull("the version that IS current is never stamped");
        w.Versions[V1].InForceTo.Should().NotBeNull("the old version stays ended");
        _fixture.Engine.ReevaluateCalls.Should().ContainSingle().Which.Should().Be((PolicyId, newVersion), "step 4 runs");
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WhenTheOldVersionsStampAnswers5xxButLanded_Continues_AndIs200()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.FailWrite = ("PATCH", $"sprk_policyversions({V1:D})", true, false);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var newVersion = (await Json(response)).GetProperty("policyVersionId").GetGuid();
        var w = _fixture.World;
        w.Policies[PolicyId].Current.Should().Be(newVersion);
        w.Versions[newVersion].InForceTo.Should().BeNull();
        w.Versions[V1].InForceTo.Should().NotBeNull();
    }

    [Fact]
    public async Task Publish_WhenTheOldVersionsStampAnswers5xxAndDidNotLand_Compensates_And503()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.FailWrite = ("PATCH", $"sprk_policyversions({V1:D})", false, false);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var w = _fixture.World;
        w.Policies[PolicyId].Current.Should().Be(V1);
        w.Versions[V1].InForceTo.Should().BeNull();
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().NotBeNull();
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenTheRequestIsAbandonedAfterTheVersionIsCreated_StillCompletes()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Engine.Result = new PolicyReevaluationResult(true, SupersededClosed: 1, Raised: 1, FailureSummary: string.Empty);
        _fixture.World.AbortAfter = "POST sprk_policyversions";

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var w = _fixture.World;
        w.Aborted.Should().BeTrue("the request's token was dead from step 2 on");
        var created = w.Versions.Single(v => v.Value.Number == 2);
        w.Policies[PolicyId].Current.Should().Be(created.Key, "steps 2 and 3 ran on CancellationToken.None");
        w.Versions[V1].InForceTo.Should().NotBeNull();
        created.Value.InForceTo.Should().BeNull();
        _fixture.Engine.ReevaluateCalls.Should().ContainSingle("step 4 ran on CancellationToken.None");
    }

    // ── Fix round 1: a save whose pointer write fails names the orphaned policy (K6) ─────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_WhenThePointerCannotBeSet_Is503_WithTheOrphanedPolicyId(bool @throw)
    {
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.FailWrite = ("PATCH", "sprk_policies(", false, @throw);

        var response = await Save();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var created = _fixture.World.Writes.Should().ContainSingle().Subject;
        created.Kind.Should().Be("POST sprk_policies");
        var orphan = Guid.Parse(created.Json.GetProperty("sprk_policyid").GetString()!);
        (await Json(response)).GetProperty("policyId").GetGuid().Should().Be(orphan, "the client recovers by publishing a version of it");
        _fixture.World.Policies[orphan].Current.Should().BeNull();
    }

    // ── Review pass 2 ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The pointer re-read PointerOfAsync sends (distinct from the publish's first policy read, which carries the domain filter).</summary>
    private const string PointerReread = "sprk_policies?$select=sprk_policyid,_sprk_currentversion_value&";

    public static TheoryData<string> PointerWriteFailures => new()
    {
        DataverseUserClientErrorCodes.AccessDenied,
        DataverseUserClientErrorCodes.OboExchangeFailed,
    };

    [Theory]
    [MemberData(nameof(PointerWriteFailures))]
    public async Task Save_WhenThePointerWriteIsRefusedOrTheCallerUnresolved_IsTheSavedOff503_WithThePolicyId(string error)
    {
        // F1-b: the policy and its first version exist, so neither "Nothing was written" nor a policy-less 403 is true.
        (await DryRun(null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.OnWrite = kind => kind.StartsWith("PATCH sprk_policies(", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(error == DataverseUserClientErrorCodes.AccessDenied ? 403 : 401, error, error)
            : null;

        var response = await Save();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var problem = await Json(response);
        var created = _fixture.World.Writes.Should().ContainSingle().Subject;
        created.Kind.Should().Be("POST sprk_policies");
        problem.GetProperty("policyId").GetGuid().Should().Be(Guid.Parse(created.Json.GetProperty("sprk_policyid").GetString()!));
        problem.GetProperty("reasonCode").GetString().Should().Be("ontology.admin.unavailable");
        problem.GetProperty("detail").GetString().Should().Contain("saved Off").And.NotContain("Nothing was written");
    }

    [Fact]
    public async Task Publish_WhenAConcurrentPublishStampedTheCurrentVersion_ButHasNotMovedThePointer_Is409_AndTheEndStampStands()
    {
        // F1-a, the interleaving: A stamps V1 (row version 201), B reads V1 at 201 before A moves the pointer, A moves the pointer to VA.
        // Before the fix B stamped V1 If-Match 201 (it succeeded), lost the pointer race (412) and its compensation set V1's
        // sprk_inforceto back to null: a version that had really ended, left with no end.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        var aStamp = StampAgo(TimeSpan.FromSeconds(5)); // A's stamp is seconds old: inside the concurrent-publish window
        w.Versions[V1].InForceTo = aStamp;
        w.Versions[V1].RowVersion = 201;
        var va = Guid.NewGuid();
        w.AddVersion(va, PolicyId, number: 2, inForceTo: null);
        w.OnWrite = kind =>
        {
            if (kind == $"PATCH sprk_policies({PolicyId:D})" && w.Policies[PolicyId].Current == V1)
            {
                w.Policies[PolicyId].Current = va; // A's pointer move lands just before B's
                w.Policies[PolicyId].RowVersion++;
            }

            return null;
        };

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        w.Versions[V1].InForceTo.Should().NotBeNull("V1 has really ended (A published VA); its end stamp is never wiped")
            .And.Be(aStamp, "B refuses before any write, so A's stamp is untouched");
        w.Writes.Should().BeEmpty("the end stamp is seen on the read, before step 1");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        w.Requests.Should().Contain(r => r.StartsWith("GET sprk_policyversions?$select=", StringComparison.Ordinal) && r.Contains("sprk_inforceto",
            StringComparison.Ordinal), "the versions read selects sprk_inforceto");
    }

    private static string StampAgo(TimeSpan age) =>
        (DateTime.UtcNow - age).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publish_WhenTheCurrentVersionCarriesAStaleEndStamp_FromAFailedPublish_Heals_AndIs200()
    {
        // Round 3, F2: the K2 double failure (the pointer move failed AND the restore failed) leaves the pointer naming a stamped version.
        // Refusing every retry made that permanent; a stamp older than the window is a leftover, so the next publish restamps and moves.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        var staleStamp = StampAgo(OntologyAdminAuthoringService.ConcurrentPublishWindow + TimeSpan.FromMinutes(8));
        w.Versions[V1].InForceTo = staleStamp;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var newVersion = (await Json(response)).GetProperty("policyVersionId").GetGuid();
        w.Policies[PolicyId].Current.Should().Be(newVersion, "the pointer moves to the new version");
        w.Versions[V1].InForceTo.Should().NotBeNull().And.NotBe(staleStamp, "the previous version is restamped by this publish");
        w.Versions[newVersion].InForceTo.Should().BeNull("the new version is in force");
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WhenTheCurrentVersionCarriesAFreshEndStamp_Is409_AndMakesNoWrite()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        var freshStamp = StampAgo(TimeSpan.FromSeconds(60)); // inside the 2 minute window
        w.Versions[V1].InForceTo = freshStamp;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        w.Writes.Should().BeEmpty("a publish that may be in flight is refused before any write");
        w.Versions[V1].InForceTo.Should().Be(freshStamp);
        w.Policies[PolicyId].Current.Should().Be(V1);
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenTheCurrentVersionsEndStampCannotBeParsed_Is409_AndMakesNoWrite()
    {
        // Fails closed: an end stamp that cannot be dated is treated as fresh.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        w.Versions[V1].InForceTo = "not-a-date";

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        w.Writes.Should().BeEmpty();
        w.Versions[V1].InForceTo.Should().Be("not-a-date");
        w.Policies[PolicyId].Current.Should().Be(V1);
    }

    [Fact]
    public async Task Publish_WhenTheOldStampLanded_ButThePointerNoLongerNamesTheOldVersion_TheCompensationLeavesItsEndStamp()
    {
        // F1-a, the compensation's own guard: B's stamp lands, then another writer moves the pointer (412 on B's move). V1 is no longer
        // current, so it has ended: RestorePreviousAsync re-reads the pointer and leaves the stamp.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        var va = Guid.NewGuid();
        w.AddVersion(va, PolicyId, number: 2, inForceTo: null);
        w.OnWrite = kind =>
        {
            if (kind == $"PATCH sprk_policies({PolicyId:D})" && w.Policies[PolicyId].Current == V1)
            {
                w.Policies[PolicyId].Current = va;
                w.Policies[PolicyId].RowVersion++;
            }

            return null;
        };

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        w.Policies[PolicyId].Current.Should().Be(va);
        w.Versions[V1].InForceTo.Should().NotBeNull("the pointer names VA, so V1 has ended and keeps its end stamp");
        w.Versions.Values.Single(v => v.Number == 3).InForceTo.Should().NotBeNull("B's version never became current");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Publish_WhenThePointerMoveIs412_AndThePointerCannotBeReRead_TheOldStampIsLeft_NotUndoneOnAGuess()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        w.BumpBeforeWrite = ("PATCH", $"sprk_policies({PolicyId:D})");
        w.OnWrite = kind =>
        {
            if (kind == $"PATCH sprk_policies({PolicyId:D})") w.FailRead = PointerReread; // every re-read after the 412 fails
            return null;
        };

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        w.Versions[V1].InForceTo.Should().NotBeNull("an unreadable pointer undoes nothing (K2: EventId 50309 reports it)");
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().BeNull("an unreadable pointer may name it: never stamped");
        w.Requests.Should().Contain("GET " + PointerReread + $"$filter=sprk_policyid eq {PolicyId:D}");
    }

    [Fact]
    public async Task Publish_WhenThePointerMoveThrows_AndThePointerCannotBeReRead_Is503_AndNothingIsUndone()
    {
        // F3-a: SettleUnknownPointerAsync's default branch.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        w.FailWrite = ("PATCH", $"sprk_policies({PolicyId:D})", false, true);
        w.FailRead = PointerReread;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.unavailable");
        w.Policies[PolicyId].Current.Should().Be(V1);
        w.Versions[V1].InForceTo.Should().NotBeNull("whether the pointer moved is unknown: the stamp is not undone on a guess");
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().BeNull("it may be current: never stamped");
        w.Writes.Select(x => x.Kind).Should().Equal("POST sprk_policyversions", $"PATCH sprk_policyversions({V1:D})");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Publish_WhenTheOldStampIs412_RetireUnpublished_NeverStampsAVersionThePointerNamesOrMayName(bool pointerNamesNew)
    {
        // F3-a: RetireUnpublishedAsync refuses when the pointer re-read is New or Unknown.
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var w = _fixture.World;
        w.OnWrite = kind =>
        {
            if (kind == $"PATCH sprk_policyversions({V1:D})")
            {
                w.Versions[V1].RowVersion++; // a concurrent writer: the stamp answers 412
                if (pointerNamesNew) w.Policies[PolicyId].Current = w.Versions.Single(v => v.Value.Number == 2).Key;
                else w.FailRead = PointerReread;
            }

            return null;
        };

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        w.Versions.Values.Single(v => v.Number == 2).InForceTo.Should().BeNull(
            pointerNamesNew ? "the version that is current is never stamped" : "a version the pointer may name is never stamped");
        w.Versions[V1].InForceTo.Should().BeNull("the 412 stamp never landed");
        w.Writes.Select(x => x.Kind).Should().Equal("POST sprk_policyversions");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    // ── Authorization (NFR-10, D-29) ──────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/catalog" },
        { "POST", "/dry-run" },
        { "POST", "/validate" },
        { "POST", "/policies" },
        { "POST", $"/policies/{PolicyId:D}/versions" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AnUnresolvedCaller_IsTheSingle403_OnEveryRoute_AndNothingElseIsAsked(string method, string path)
    {
        _fixture.World.WhoAmIError = DataverseUserClientErrorCodes.OboExchangeFailed;

        var response = await Send(method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonOf(response)).Should().Be("sdap.access.deny.caller_unresolved");
        _fixture.World.Requests.Should().Equal("GET WhoAmI()");
        _fixture.Engine.Evaluated.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AWhoAmIDataverseRefuses_401Or403_IsTheSameSingle403_OnEveryRoute(string method, string path)
    {
        // Review pass 2 (F3-b): DataverseUserClient maps a 401 or 403 to AccessDenied; WhoAmI refused is the uniform caller_unresolved.
        _fixture.World.WhoAmIError = DataverseUserClientErrorCodes.AccessDenied;

        var response = await Send(method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonOf(response)).Should().Be("sdap.access.deny.caller_unresolved");
        _fixture.World.Requests.Should().Equal("GET WhoAmI()");
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task NoToken_Is401_OnEveryRoute(string method, string path)
    {
        var anonymous = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = method == "GET"
            ? await anonymous.GetAsync(Root + path)
            : await anonymous.PostAsJsonAsync(Root + path, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.World.Requests.Should().BeEmpty();
    }

    private async Task<HttpResponseMessage> Send(string method, string path) => method == "GET"
        ? await _client.GetAsync(Root + path)
        : await _client.PostAsJsonAsync(Root + path, new { policyCode = "POL-X", name = "x", lane = "Decide", ruleType = "Existence", ruleBody = Body, draft = Draft() });

    [Fact]
    public void Production_WiresTheEngineSeamToTheShippedEvaluatorAndJob()
    {
        using var scope = _fixture.ProductionServices().CreateScope();

        scope.ServiceProvider.GetRequiredService<IOntologyAdminAuthoringSignalEngine>().Should().BeOfType<OntologyAdminAuthoringSignalEngine>();
    }
}

/// <summary>The BFF with the caller-identity client, the inbound token and the evaluator seam substituted.</summary>
public sealed class OntologyAdminAuthoringTestFixture : WorkspaceTestFixture
{
    public AuthoringWorld World { get; } = new();

    public RecordingEngine Engine { get; } = new();

    public void Reset()
    {
        World.Reset();
        Engine.Reset();
        Engine.World = World;
    }

    public HttpClient ClientFor(string oid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oid);
        return client;
    }

    /// <summary>The service provider as the BFF registers it, before this fixture's substitutions.</summary>
    public IServiceProvider ProductionServices() => _production.Value.Services;

    private readonly Lazy<WebApplicationFactory<Program>> _production = new(() => new WorkspaceTestFixture());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => World);
            services.RemoveAll<IOntologyAdminAuthoringSignalEngine>();
            services.AddSingleton<IOntologyAdminAuthoringSignalEngine>(Engine);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, AuthoringCallerHandler>(AuthoringCallerHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AuthoringCallerHandler.SchemeName;
                o.DefaultChallengeScheme = AuthoringCallerHandler.SchemeName;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _production.IsValueCreated) _production.Value.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>The bearer value is the caller's oid; the principal carries it and the fixture tenant. No bearer is 401.</summary>
    private sealed class AuthoringCallerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OntologyAuthoringCaller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", header["Bearer ".Length..]), new Claim("tid", WorkspaceTestConstants.TestTenantId)], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

/// <summary>The evaluator seam: records what it was asked and returns what a test set.</summary>
public sealed class RecordingEngine : IOntologyAdminAuthoringSignalEngine
{
    public AuthoringWorld? World { get; set; }

    public List<SignalHit> Hits { get; set; } = new();

    public PolicyReevaluationResult Result { get; set; } = new(true, 0, 0, string.Empty);

    public ConcurrentQueue<SignalEvaluationPolicy> Evaluated { get; } = new();

    /// <summary>Task 102b: the matter scope each evaluation was asked for (null: all matters), in call order.</summary>
    public ConcurrentQueue<Guid?> Scopes { get; } = new();

    /// <summary>Task 102b: when set, the engine returns every hit whatever the scope (an evaluator that failed to confine it).</summary>
    public bool IgnoreScope { get; set; }

    /// <summary>Each call with the policy's current version AT THE TIME OF THE CALL (proves the pointer moved first).</summary>
    public ConcurrentQueue<(Guid PolicyId, Guid? CurrentAtCall)> ReevaluateCalls { get; } = new();

    public void Reset()
    {
        Hits = new();
        Result = new(true, 0, 0, string.Empty);
        Evaluated.Clear();
        Scopes.Clear();
        IgnoreScope = false;
        ReevaluateCalls.Clear();
    }

    /// <summary>As the shipped evaluator: a matter scope returns only that matter's hits (its compiled query carries the subject id).</summary>
    public Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, Guid? matterScope, CancellationToken ct)
    {
        Evaluated.Enqueue(policy);
        Scopes.Enqueue(matterScope);
        var hits = matterScope is { } m && !IgnoreScope ? Hits.Where(h => h.SubjectId == m).ToList() : Hits;
        return Task.FromResult(new SignalEvaluation(true, "sprk_matter", DateTimeOffset.UtcNow, 14, hits));
    }

    public Task<PolicyReevaluationResult> ReevaluateAsync(Guid policyId, CancellationToken ct)
    {
        if (World is { Aborted: true } && ct.CanBeCanceled)
        {
            throw new TaskCanceledException("The request was aborted.");
        }

        ReevaluateCalls.Enqueue((policyId, World?.Policies.TryGetValue(policyId, out var p) == true ? p.Current : null));
        return Task.FromResult(Result);
    }
}

/// <summary>
/// A small Dataverse as the CALLER sees it: the policy, version and Signal rows, the subject and core tables, and the triage
/// categories. It honours the domain condition when the query carries it, row versions on conditional PATCHes (412), a caller who
/// cannot read a row (it is simply not returned), and a caller whose every write Dataverse refuses (403). Every write is recorded.
/// </summary>
public sealed class AuthoringWorld : IDataverseUserClient
{
    private const string DomainFilter = "(sprk_domain eq null or sprk_domain eq 100000001)";

    public sealed class PolicyState
    {
        public required string Code { get; init; }
        public int Lane { get; init; }
        public bool Enabled { get; set; }
        public int State { get; set; }
        public int? Domain { get; init; }
        public Guid? Current { get; set; }
        public long RowVersion { get; set; } = 100;

        /// <summary>Task 102b: the policy's matter scope (<c>sprk_matter</c>); null for all matters.</summary>
        public Guid? Matter { get; set; }
    }

    public sealed class VersionState
    {
        public Guid PolicyId { get; init; }
        public int Number { get; init; }
        public string? InForceTo { get; set; }
        public long RowVersion { get; set; } = 200;
    }

    public sealed record SignalState(Guid PolicyId, string Key, int Status, int? Resolution, DateTime? ResolvedOn, DateTime? SuppressedUntil,
        Guid? Core, Guid? CoreType, bool Readable);

    public sealed record Write(string Kind, JsonElement Json, long? IfMatch);

    public Guid Caller { get; set; }
    public string? WhoAmIError { get; set; }
    public bool RefuseWrites { get; set; }

    /// <summary>Whether the caller holds prvCreatesprk_PolicyVersion (an Ontology Administrator) or not (a Console User).</summary>
    public bool HoldsAuthoringPrivilege { get; set; } = true;

    /// <summary>When set, the privilege function fails with this error code.</summary>
    public string? PrivilegeError { get; set; }

    /// <summary>A write that fails: <c>Land</c> applies it first (the answer is lost, not the write); <c>Throw</c> throws a
    /// transport timeout (<see cref="TaskCanceledException"/>, which the production client does not catch) instead of a 500.</summary>
    public (string Method, string PathPrefix, bool Land, bool Throw)? FailWrite { get; set; }

    /// <summary>The request is abandoned right after this write ("POST sprk_policyversions"): from then on every call made with a
    /// cancellable token throws, as the production client does once the request's token is cancelled.</summary>
    public string? AbortAfter { get; set; }

    public bool Aborted { get; private set; }

    /// <summary>A concurrent writer: bumps the row version of the named target just before this write lands (so it answers 412).</summary>
    public (string Method, string Path)? BumpBeforeWrite { get; set; }

    /// <summary>Called with each write's kind ("PATCH sprk_policies(id)") before it is applied: another writer can act here (it runs
    /// inside the world's lock), and a non-null answer is returned instead of applying the write.</summary>
    public Func<string, DataverseUserResponse?>? OnWrite { get; set; }

    /// <summary>A GET whose path starts with this fails with a 500 (every such read, until cleared).</summary>
    public string? FailRead { get; set; }

    /// <summary>When set, the privilege function answers this (or throws what it throws) instead of the role-based answer.</summary>
    public Func<DataverseUserResponse>? PrivilegeAnswer { get; set; }

    public Dictionary<Guid, PolicyState> Policies { get; } = new();
    public Dictionary<Guid, VersionState> Versions { get; } = new();
    public List<SignalState> Signals { get; } = new();
    public HashSet<(string Table, Guid Id)> Unreadable { get; } = new();

    /// <summary>Task 102b: rows that do not exist at all (a read of them returns nothing, exactly as for an unreadable row).</summary>
    public HashSet<(string Table, Guid Id)> Missing { get; } = new();

    /// <summary>Task 102b fix round 1 (F4): matter names, for the dry run's read of the stored matter's name as the caller.</summary>
    public Dictionary<Guid, string> MatterNames { get; } = new();

    /// <summary>Task 102b fix round 2 (uac-r2 F1): when set, the read of a matter's name throws it (an HttpClient timeout surfaces as a
    /// <see cref="TaskCanceledException"/> while the request is still live).</summary>
    public Exception? MatterNameReadThrows { get; set; }

    /// <summary>Task 102b fix round 4 (uac-r2 E): a matter's access columns as the caller's read returns them: <c>sprk_issecure</c> (null:
    /// not returned, a masked field) and <c>sprk_accesspermission</c> (null: Standard). A matter not listed is ordinary.</summary>
    public Dictionary<Guid, (bool? IsSecure, int? AccessPermission)> MatterAccess { get; } = new();

    /// <summary>Task 102b fix round 4: when set, the scope check's read of the matter throws it.</summary>
    public Exception? MatterAccessReadThrows { get; set; }
    public List<string> Requests { get; } = new();
    public List<Write> Writes { get; } = new();

    public void Reset()
    {
        Caller = Guid.Empty;
        WhoAmIError = null;
        RefuseWrites = false;
        HoldsAuthoringPrivilege = true;
        PrivilegeError = null;
        FailWrite = null;
        AbortAfter = null;
        Aborted = false;
        BumpBeforeWrite = null;
        OnWrite = null;
        FailRead = null;
        PrivilegeAnswer = null;
        Policies.Clear();
        Versions.Clear();
        Signals.Clear();
        Unreadable.Clear();
        Missing.Clear();
        MatterNames.Clear();
        MatterNameReadThrows = null;
        MatterAccess.Clear();
        MatterAccessReadThrows = null;
        Requests.Clear();
        Writes.Clear();
    }

    public void AddPolicy(Guid id, string code, int lane, bool enabled, int? domain, Guid? current, Guid? matter = null) =>
        Policies[id] = new PolicyState { Code = code, Lane = lane, Enabled = enabled, Domain = domain, Current = current, Matter = matter };

    public void AddVersion(Guid id, Guid policyId, int number, string? inForceTo) =>
        Versions[id] = new VersionState { PolicyId = policyId, Number = number, InForceTo = inForceTo };

    public void AddSignal(Guid policyId, string table, Guid subject, int episode, int status, int? resolution = null, DateTime? resolvedOn = null,
        DateTime? suppressedUntil = null, Guid? core = null, Guid? coreType = null, bool readable = true) =>
        Signals.Add(new SignalState(policyId, $"{Policies[policyId].Code}|{table}|{subject:D}|{episode}", status, resolution, resolvedOn,
            suppressedUntil, core, coreType, readable));

    private static Task<DataverseUserResponse> Ok(object body) =>
        Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body)));

    private static Task<DataverseUserResponse> NoContent() => Task.FromResult(DataverseUserResponse.Ok(204, null));

    private static Task<DataverseUserResponse> Refused() =>
        Task.FromResult(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "Principal user is missing prvCreatesprk_policy."));

    private static Task<DataverseUserResponse> Precondition() =>
        Task.FromResult(DataverseUserResponse.Fail(412, DataverseUserClientErrorCodes.BadRequest, "The version of the existing record doesn't match."));

    public const string PrivilegePathSuffix =
        "/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='prvCreatesprk_PolicyVersion')";

    private void ThrowIfAborted(CancellationToken cancellationToken)
    {
        if (Aborted && cancellationToken.CanBeCanceled)
        {
            throw new TaskCanceledException("The request was aborted.");
        }
    }

    /// <summary>The configured failure for this write, if any: null to proceed, otherwise the answer (or a throw).</summary>
    private bool TryFail(string kind, out (bool Land, bool Throw) failure)
    {
        failure = default;
        if (FailWrite is { } f && kind.StartsWith(f.Method + " " + f.PathPrefix, StringComparison.Ordinal))
        {
            FailWrite = null;
            failure = (f.Land, f.Throw);
            return true;
        }

        return false;
    }

    private static Task<DataverseUserResponse> FailedAnswer(bool @throw) =>
        @throw
            ? throw new TaskCanceledException("The request timed out.")
            : Task.FromResult(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Generic SQL error."));

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        lock (this)
        {
            ThrowIfAborted(cancellationToken);
            Requests.Add("GET " + relativePath);
            if (FailRead is { } failRead && relativePath.StartsWith(failRead, StringComparison.Ordinal))
            {
                return Task.FromResult(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Generic SQL error."));
            }

            if (relativePath == "WhoAmI()")
            {
                // DataverseUserClient.MapErrorCode: a 401 or 403 is AccessDenied; the OBO codes have no Dataverse status.
                return WhoAmIError is { } error
                    ? Task.FromResult(DataverseUserResponse.Fail(error == DataverseUserClientErrorCodes.AccessDenied ? 403 : 500, error, error))
                    : Ok(new { UserId = Caller.ToString("D") });
            }

            if (relativePath.EndsWith(PrivilegePathSuffix, StringComparison.Ordinal))
            {
                if (relativePath != $"systemusers({Caller:D}){PrivilegePathSuffix}")
                {
                    throw new InvalidOperationException($"The privilege was asked about someone other than the caller: {relativePath}");
                }

                if (PrivilegeAnswer is { } answer)
                {
                    return Task.FromResult(answer());
                }

                if (PrivilegeError is { } privilegeError)
                {
                    return Task.FromResult(DataverseUserResponse.Fail(
                        privilegeError == DataverseUserClientErrorCodes.AccessDenied ? 403 : 500, privilegeError, privilegeError));
                }

                return Ok(new
                {
                    RolePrivileges = HoldsAuthoringPrivilege
                        ? new object[] { new { Depth = "Global", PrivilegeId = Guid.NewGuid().ToString("D"), BusinessUnitId = Guid.NewGuid().ToString("D"), PrivilegeName = "prvCreatesprk_PolicyVersion" } }
                        : Array.Empty<object>(),
                });
            }

            if (relativePath.StartsWith("sprk_triagecategories?", StringComparison.Ordinal))
            {
                return Ok(new
                {
                    value = new object[]
                    {
                        new { sprk_triagecategoryid = Guid.NewGuid().ToString("D"), sprk_name = "Budget change", sprk_enabled = true },
                        new { sprk_triagecategoryid = Guid.NewGuid().ToString("D"), sprk_name = "Fee or scope change", sprk_enabled = true },
                    },
                });
            }

            if (relativePath.StartsWith("sprk_policies?", StringComparison.Ordinal))
            {
                var domainOnly = relativePath.Contains(DomainFilter, StringComparison.Ordinal);
                var byId = Regex.Match(relativePath, @"sprk_policyid eq ([0-9a-f-]{36})");
                var byCode = Regex.Match(relativePath, @"sprk_policycode eq '([^']*)'");
                var rows = Policies
                    .Where(p => !Unreadable.Contains(("sprk_policy", p.Key)))
                    .Where(p => !domainOnly || p.Value.Domain is null or 100000001)
                    .Where(p => byId.Success ? p.Key == Guid.Parse(byId.Groups[1].Value) : !byCode.Success || p.Value.Code == byCode.Groups[1].Value)
                    .Select(p => new Dictionary<string, object?>
                    {
                        ["sprk_policyid"] = p.Key.ToString("D"),
                        ["sprk_policycode"] = p.Value.Code,
                        ["sprk_lane"] = p.Value.Lane,
                        ["sprk_enabled"] = p.Value.Enabled,
                        ["statecode"] = p.Value.State,
                        ["_sprk_currentversion_value"] = p.Value.Current?.ToString("D"),
                        ["_sprk_matter_value"] = p.Value.Matter?.ToString("D"),
                        ["versionnumber"] = p.Value.RowVersion,
                        ["sprk_domain"] = p.Value.Domain,
                    })
                    .ToArray();
                return Ok(new { value = rows });
            }

            var single = Regex.Match(relativePath, @"^sprk_policyversions\(([0-9a-f-]{36})\)\?");
            if (single.Success)
            {
                var v = Versions[Guid.Parse(single.Groups[1].Value)];
                return Ok(new { sprk_inforceto = v.InForceTo, versionnumber = v.RowVersion });
            }

            if (relativePath.StartsWith("sprk_policyversions?", StringComparison.Ordinal))
            {
                var policy = Guid.Parse(Regex.Match(relativePath, @"_sprk_policy_value eq ([0-9a-f-]{36})").Groups[1].Value);
                return Ok(new
                {
                    value = Versions.Where(v => v.Value.PolicyId == policy)
                        .Select(v => new
                        {
                            sprk_policyversionid = v.Key.ToString("D"), sprk_versionnumber = v.Value.Number, sprk_inforceto = v.Value.InForceTo,
                            versionnumber = v.Value.RowVersion,
                        })
                        .ToArray(),
                });
            }

            if (relativePath.StartsWith("sprk_signals?fetchXml=", StringComparison.Ordinal))
            {
                var fetch = Uri.UnescapeDataString(relativePath["sprk_signals?fetchXml=".Length..]);
                var policy = Guid.Parse(Regex.Match(fetch, @"attribute=""sprk_policy"" operator=""eq"" value=""([0-9a-f-]{36})""").Groups[1].Value);
                return Ok(new
                {
                    value = Signals.Where(s => s.PolicyId == policy && s.Readable)
                        .Select(s => new Dictionary<string, object?>
                        {
                            ["sprk_dedupekey"] = s.Key,
                            ["sprk_signalstatus"] = s.Status,
                            ["sprk_resolutiontype"] = s.Resolution,
                            ["sprk_resolvedon"] = s.ResolvedOn?.ToString("o"),
                            ["sprk_suppresseduntil"] = s.SuppressedUntil?.ToString("o"),
                            ["sprk_corerecordid"] = s.Core?.ToString("D"),
                            ["_sprk_corerecordtype_value"] = s.CoreType?.ToString("D"),
                        })
                        .ToArray(),
                });
            }

            var meta = Regex.Match(relativePath, @"^EntityDefinitions\(LogicalName='([a-z_]+)'\)\?\$select=EntitySetName$");
            if (meta.Success)
            {
                return Ok(new { EntitySetName = meta.Groups[1].Value + "s" });
            }

            var matterName = Regex.Match(relativePath, @"^sprk_matters\?\$select=sprk_mattername&\$filter=sprk_matterid eq ([0-9a-f-]{36})&\$top=1$");
            if (matterName.Success)
            {
                if (MatterNameReadThrows is { } thrown) throw thrown;
                var id = Guid.Parse(matterName.Groups[1].Value);
                return Ok(new
                {
                    value = Unreadable.Contains(("sprk_matter", id)) || Missing.Contains(("sprk_matter", id))
                        ? Array.Empty<object>()
                        : new object[] { new Dictionary<string, object?> { ["sprk_matterid"] = id.ToString("D"), ["sprk_mattername"] = MatterNames.GetValueOrDefault(id) } },
                });
            }

            // Task 102b fix round 4 (uac-r2 E): the scope check's read of the matter with its access columns, as the caller. A row the caller
            // cannot read (or that does not exist) is absent; sprk_issecure is false and sprk_accesspermission absent (Standard) unless set.
            var matterAccess = Regex.Match(relativePath,
                @"^sprk_matters\?\$select=sprk_matterid,sprk_issecure,sprk_accesspermission,statecode&\$filter=sprk_matterid eq ([0-9a-f-]{36})&\$top=1$");
            if (matterAccess.Success)
            {
                if (MatterAccessReadThrows is { } thrown) throw thrown;
                var id = Guid.Parse(matterAccess.Groups[1].Value);
                if (Unreadable.Contains(("sprk_matter", id)) || Missing.Contains(("sprk_matter", id))) return Ok(new { value = Array.Empty<object>() });
                var row = new Dictionary<string, object?> { ["sprk_matterid"] = id.ToString("D"), ["statecode"] = 0 };
                var (isSecure, accessPermission) = MatterAccess.GetValueOrDefault(id, (false, null));
                if (isSecure is { } secure) row["sprk_issecure"] = secure;
                if (accessPermission is { } permission) row["sprk_accesspermission"] = permission;
                return Ok(new { value = new object[] { row } });
            }

            var table = Regex.Match(relativePath, @"^([a-z_]+)s\?\$select=([a-z_]+)id&\$filter=Microsoft\.Dynamics\.CRM\.In\(PropertyName='[a-z_]+id',PropertyValues=\[(.*)\]\)$");
            if (table.Success)
            {
                var logical = table.Groups[1].Value;
                var ids = Regex.Matches(table.Groups[3].Value, "[0-9a-f-]{36}").Select(m => Guid.Parse(m.Value));
                return Ok(new
                {
                    value = ids.Where(id => !Unreadable.Contains((logical, id)) && !Missing.Contains((logical, id)))
                        .Select(id => new Dictionary<string, object?> { [logical + "id"] = id.ToString("D") })
                        .ToArray(),
                });
            }

            throw new InvalidOperationException($"The authoring service asked something unexpected: {relativePath}");
        }
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
    {
        lock (this)
        {
            ThrowIfAborted(cancellationToken);
            Requests.Add("POST " + absoluteApiPath);
            if (RefuseWrites) return Refused();
            var kind = "POST " + absoluteApiPath["/api/data/v9.2/".Length..];
            if (OnWrite?.Invoke(kind) is { } hooked) return Task.FromResult(hooked);
            if (TryFail(kind, out var failure) && !failure.Land)
            {
                return FailedAnswer(failure.Throw);
            }

            var answer = Create(absoluteApiPath, jsonBody);
            if (AbortAfter == kind)
            {
                Aborted = true;
            }

            return failure.Land ? FailedAnswer(failure.Throw) : answer;
        }
    }

    private Task<DataverseUserResponse> Create(string absoluteApiPath, string jsonBody)
    {
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var json = doc.RootElement.Clone();
            switch (absoluteApiPath)
            {
                case "/api/data/v9.2/sprk_policies":
                {
                    var id = Guid.Parse(json.GetProperty("sprk_policyid").GetString()!);
                    AddPolicy(id, json.GetProperty("sprk_policycode").GetString()!, json.GetProperty("sprk_lane").GetInt32(),
                        json.GetProperty("sprk_enabled").GetBoolean(), json.GetProperty("sprk_domain").GetInt32(), current: null,
                        matter: MatterOf(json));
                    foreach (var v in json.GetProperty("sprk_policyversion_policy").EnumerateArray())
                    {
                        AddVersion(Guid.Parse(v.GetProperty("sprk_policyversionid").GetString()!), id, v.GetProperty("sprk_versionnumber").GetInt32(), null);
                    }

                    Writes.Add(new Write("POST sprk_policies", json, null));
                    return NoContent();
                }

                case "/api/data/v9.2/sprk_policyversions":
                {
                    var policy = Guid.Parse(Regex.Match(json.GetProperty("sprk_Policy@odata.bind").GetString()!, "[0-9a-f-]{36}").Value);
                    AddVersion(Guid.Parse(json.GetProperty("sprk_policyversionid").GetString()!), policy, json.GetProperty("sprk_versionnumber").GetInt32(), null);
                    Writes.Add(new Write("POST sprk_policyversions", json, null));
                    return NoContent();
                }

                default:
                    throw new InvalidOperationException($"Unexpected create: {absoluteApiPath}");
            }
        }
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
        PostAsync(absoluteApiPath, jsonBody, cancellationToken);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        Patch(relativePath, jsonBody, null, cancellationToken);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
        Patch(relativePath, jsonBody, expectedVersion, cancellationToken);

    private Task<DataverseUserResponse> Patch(string relativePath, string jsonBody, long? expected, CancellationToken cancellationToken)
    {
        lock (this)
        {
            ThrowIfAborted(cancellationToken);
            Requests.Add("PATCH " + relativePath);
            if (RefuseWrites) return Refused();
            if (OnWrite?.Invoke("PATCH " + relativePath) is { } hooked) return Task.FromResult(hooked);
            if (TryFail("PATCH " + relativePath, out var failure))
            {
                if (!failure.Land)
                {
                    return FailedAnswer(failure.Throw);
                }

                var landed = Update(relativePath, jsonBody, expected);
                return landed.Result.IsSuccess ? FailedAnswer(failure.Throw) : landed;
            }

            return Update(relativePath, jsonBody, expected);
        }
    }

    private Task<DataverseUserResponse> Update(string relativePath, string jsonBody, long? expected)
    {
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var json = doc.RootElement.Clone();
            var id = Guid.Parse(Regex.Match(relativePath, "[0-9a-f-]{36}").Value);
            if (BumpBeforeWrite is { Method: "PATCH" } bump && bump.Path == relativePath)
            {
                BumpBeforeWrite = null;
                if (relativePath.StartsWith("sprk_policies(", StringComparison.Ordinal)) Policies[id].RowVersion++;
                else Versions[id].RowVersion++;
            }

            if (relativePath.StartsWith("sprk_policies(", StringComparison.Ordinal))
            {
                var policy = Policies[id];
                if (expected is { } e && e != policy.RowVersion) return Precondition();
                policy.Current = Guid.Parse(Regex.Match(json.GetProperty("sprk_CurrentVersion@odata.bind").GetString()!, "[0-9a-f-]{36}").Value);
                if (json.TryGetProperty(MatterBind, out _))
                {
                    policy.Matter = MatterOf(json); // a null bind clears the lookup
                }

                policy.RowVersion++;
            }
            else if (relativePath.StartsWith("sprk_policyversions(", StringComparison.Ordinal))
            {
                var version = Versions[id];
                if (expected is { } e && e != version.RowVersion) return Precondition();
                version.InForceTo = json.TryGetProperty("sprk_inforceto", out var to) && to.ValueKind == JsonValueKind.String ? to.GetString() : null;
                version.RowVersion++;
            }
            else
            {
                throw new InvalidOperationException($"Unexpected update: {relativePath}");
            }

            Writes.Add(new Write("PATCH " + relativePath, json, expected));
            return NoContent();
        }
    }

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The authoring routes never delete.");

    public const string MatterBind = "sprk_Matter@odata.bind";

    /// <summary>The matter a write binds (<c>/sprk_matters(id)</c>), or null when it binds none or clears it.</summary>
    private static Guid? MatterOf(JsonElement json)
    {
        if (!json.TryGetProperty(MatterBind, out var bind) || bind.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var reference = Regex.Match(bind.GetString()!, @"^/sprk_matters\(([0-9a-f-]{36})\)$");
        return reference.Success ? Guid.Parse(reference.Groups[1].Value) : throw new InvalidOperationException($"Unexpected matter bind: {bind}");
    }
}
