using System.Collections.Concurrent;
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
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
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
public class OntologyAdminAuthoringEndpointTests : IClassFixture<OntologyAdminAuthoringTestFixture>
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
        // The acceptance criterion, exhaustively: a rule body built from each catalog entry passes the SAME ValidateForSave save uses.
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
            var result = validator.ValidateForSave(type, body, null);
            result.IsValid.Should().BeTrue($"the catalog offers '{what}', so it must compile: {string.Join("; ", result.Errors)}");
        }

        // Nothing outside the compiler's lists: the relative-date bound one past the grammar is refused.
        validator.ValidateForSave("Existence",
            """{"type":"Existence","subject":"sprk_matter","when":{"sprk_closeddate":{">=":"now-@Nd","<=":"now"}},"all":[]}"""
                .Replace("@N", (catalog.RelativeDates.MaxDays + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            null).IsValid.Should().BeFalse();
        catalog.RuleTypes.Should().Equal("Existence", "Threshold");
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
        foreach (Match call in Regex.Matches(outside, @"_user\.PatchAsync\(\s*([^,]+),"))
        {
            call.Groups[1].Value.Should().StartWith("$\"{PolicyEntitySet}(", "outside the stamp, only policy rows are patched");
        }

        Regex.Matches(outside, @"_user\.PatchAsync\(").Should().HaveCount(2, "the save's pointer and the publish's pointer");
        source.Should().NotContain("DeleteAsync(");
    }

    // ── Authorization (NFR-10, D-29) ──────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/catalog" },
        { "POST", "/dry-run" },
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

    /// <summary>Each call with the policy's current version AT THE TIME OF THE CALL (proves the pointer moved first).</summary>
    public ConcurrentQueue<(Guid PolicyId, Guid? CurrentAtCall)> ReevaluateCalls { get; } = new();

    public void Reset()
    {
        Hits = new();
        Result = new(true, 0, 0, string.Empty);
        Evaluated.Clear();
        ReevaluateCalls.Clear();
    }

    public Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, CancellationToken ct)
    {
        Evaluated.Enqueue(policy);
        return Task.FromResult(new SignalEvaluation(true, "sprk_matter", DateTimeOffset.UtcNow, 14, Hits));
    }

    public Task<PolicyReevaluationResult> ReevaluateAsync(Guid policyId, CancellationToken ct)
    {
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

    /// <summary>A concurrent writer: bumps the row version of the named target just before this write lands (so it answers 412).</summary>
    public (string Method, string Path)? BumpBeforeWrite { get; set; }

    public Dictionary<Guid, PolicyState> Policies { get; } = new();
    public Dictionary<Guid, VersionState> Versions { get; } = new();
    public List<SignalState> Signals { get; } = new();
    public HashSet<(string Table, Guid Id)> Unreadable { get; } = new();
    public List<string> Requests { get; } = new();
    public List<Write> Writes { get; } = new();

    public void Reset()
    {
        Caller = Guid.Empty;
        WhoAmIError = null;
        RefuseWrites = false;
        BumpBeforeWrite = null;
        Policies.Clear();
        Versions.Clear();
        Signals.Clear();
        Unreadable.Clear();
        Requests.Clear();
        Writes.Clear();
    }

    public void AddPolicy(Guid id, string code, int lane, bool enabled, int? domain, Guid? current) =>
        Policies[id] = new PolicyState { Code = code, Lane = lane, Enabled = enabled, Domain = domain, Current = current };

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

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        lock (this)
        {
            Requests.Add("GET " + relativePath);
            if (relativePath == "WhoAmI()")
            {
                return WhoAmIError is { } error
                    ? Task.FromResult(DataverseUserResponse.Fail(500, error, error))
                    : Ok(new { UserId = Caller.ToString("D") });
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
                        .Select(v => new { sprk_policyversionid = v.Key.ToString("D"), sprk_versionnumber = v.Value.Number, versionnumber = v.Value.RowVersion })
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

            var table = Regex.Match(relativePath, @"^([a-z_]+)s\?\$select=([a-z_]+)id&\$filter=Microsoft\.Dynamics\.CRM\.In\(PropertyName='[a-z_]+id',PropertyValues=\[(.*)\]\)$");
            if (table.Success)
            {
                var logical = table.Groups[1].Value;
                var ids = Regex.Matches(table.Groups[3].Value, "[0-9a-f-]{36}").Select(m => Guid.Parse(m.Value));
                return Ok(new
                {
                    value = ids.Where(id => !Unreadable.Contains((logical, id)))
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
            Requests.Add("POST " + absoluteApiPath);
            if (RefuseWrites) return Refused();
            using var doc = JsonDocument.Parse(jsonBody);
            var json = doc.RootElement.Clone();
            switch (absoluteApiPath)
            {
                case "/api/data/v9.2/sprk_policies":
                {
                    var id = Guid.Parse(json.GetProperty("sprk_policyid").GetString()!);
                    AddPolicy(id, json.GetProperty("sprk_policycode").GetString()!, json.GetProperty("sprk_lane").GetInt32(),
                        json.GetProperty("sprk_enabled").GetBoolean(), json.GetProperty("sprk_domain").GetInt32(), current: null);
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
        Patch(relativePath, jsonBody, null);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
        Patch(relativePath, jsonBody, expectedVersion);

    private Task<DataverseUserResponse> Patch(string relativePath, string jsonBody, long? expected)
    {
        lock (this)
        {
            Requests.Add("PATCH " + relativePath);
            if (RefuseWrites) return Refused();
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
}
