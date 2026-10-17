using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 102b (D-165; DI-62 #1647, DI-112 #2069): the rule's matter scope on the four authoring routes. One shared
/// check: a matter scope needs a rule about matters, the matter is read AS THE CALLER (absent and unreadable get one answer), the tenant
/// is never taken from a request, the dry run evaluates within the scope, the proof binds it, save binds it, and publish moves it in
/// the same conditional write as the pointer.
/// </summary>
public partial class OntologyAdminAuthoringEndpointTests
{
    private const string ScopeMismatch = "ontology.admin.scope_subject_mismatch";
    private const string MatterNotFound = "The matter was not found.";
    private const string TodoBody = """{"type":"Existence","subject":"sprk_todo","when":{"statecode":0},"all":[]}""";

    private static readonly Guid GhostMatter = Guid.Parse("10100000-0000-4000-8000-00000000c0e1");

    private async Task<HttpResponseMessage> ScopedDryRun(Guid? policyId, Guid? matterId, string body = Body, string? lane = null, string? tenant = null) =>
        await _client.PostAsJsonAsync($"{Root}/dry-run",
            new { policyId, lane, ruleType = "Existence", ruleBody = body, messageTemplate = Template, matterId, tenant });

    private async Task<HttpResponseMessage> ScopedValidate(Guid? matterId, object? draft = null, Guid? policyId = null, string? tenant = null) =>
        await _client.PostAsJsonAsync($"{Root}/validate", new { policyId, lane = "Decide", draft = draft ?? Draft(), matterId, tenant });

    private async Task<HttpResponseMessage> ScopedSave(Guid? matterId, object? draft = null, string? tenant = null) =>
        await _client.PostAsJsonAsync($"{Root}/policies",
            new { policyCode = "POL-NEW", name = "A new rule", lane = "Decide", draft = draft ?? Draft(), matterId, tenant });

    private async Task<HttpResponseMessage> ScopedPublish(Guid policyId, Guid? matterId, object? draft = null, string? tenant = null) =>
        await _client.PostAsJsonAsync($"{Root}/policies/{policyId:D}/versions", new { draft = draft ?? Draft(), matterId, tenant });

    private AuthoringWorld.Write PointerPatch() =>
        _fixture.World.Writes.Should().ContainSingle(x => x.Kind == $"PATCH sprk_policies({PolicyId:D})").Subject;

    private bool MatterWasRead(Guid matter) =>
        _fixture.World.Requests.Any(r => r.StartsWith("GET sprk_matters?$select=sprk_matterid&$filter=", StringComparison.Ordinal)
            && r.Contains(matter.ToString("D"), StringComparison.Ordinal));

    // ── Save and publish persist the scope (AC1) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scope_SaveWithAMatter_BindsTheMatterAsTheCaller_AndNeverWritesATenant()
    {
        (await ScopedDryRun(null, MatterA, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedSave(MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var w = _fixture.World;
        var create = w.Writes[0];
        create.Kind.Should().Be("POST sprk_policies");
        create.Json.GetProperty(AuthoringWorld.MatterBind).GetString().Should().Be($"/sprk_matters({MatterA:D})");
        create.Json.TryGetProperty("sprk_tenant", out _).Should().BeFalse("the tenant scope is never written (D-165)");
        w.Policies[(await Json(response)).GetProperty("policyId").GetGuid()].Matter.Should().Be(MatterA);
        MatterWasRead(MatterA).Should().BeTrue("the matter is read as the caller before anything is written");
    }

    [Fact]
    public async Task Scope_PublishWithAMatter_MovesThePointerAndTheScopeInOneConditionalWrite_ThenReevaluatesOnce()
    {
        (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var newVersion = (await Json(response)).GetProperty("policyVersionId").GetGuid();
        var pointer = PointerPatch();
        pointer.IfMatch.Should().NotBeNull("the scope rides in the conditional pointer write");
        pointer.Json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sprk_CurrentVersion@odata.bind", AuthoringWorld.MatterBind);
        pointer.Json.GetProperty(AuthoringWorld.MatterBind).GetString().Should().Be($"/sprk_matters({MatterA:D})");
        pointer.Json.TryGetProperty("sprk_tenant", out _).Should().BeFalse();
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA);
        _fixture.Engine.ReevaluateCalls.Should().ContainSingle().Which.Should().Be((PolicyId, newVersion));
        AssertVersionPatchesTouchOnlyInForceTo();
    }

    [Fact]
    public async Task Scope_PublishFromOneMatterToAllMatters_ClearsTheScopeWithANullBind()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PointerPatch().Json.GetProperty(AuthoringWorld.MatterBind).ValueKind.Should().Be(JsonValueKind.Null);
        _fixture.World.Policies[PolicyId].Matter.Should().BeNull();
    }

    [Fact]
    public async Task Scope_PublishFromOneMatterToAnother_RebindsTheScope()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PointerPatch().Json.GetProperty(AuthoringWorld.MatterBind).GetString().Should().Be($"/sprk_matters({MatterB:D})");
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterB);
    }

    [Fact]
    public async Task Scope_PublishKeepingTheScope_SendsExactlyThePointerMove()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PointerPatch().Json.EnumerateObject().Select(p => p.Name).Should().Equal("sprk_CurrentVersion@odata.bind");
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA);
    }

    // ── Unscoped behaviour is unchanged (AC4) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scope_None_DryRunSaveAndPublishBehaveAsBefore_NoMatterIsReadOrBound()
    {
        (await ScopedDryRun(null, null, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScopedSave(null)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ScopedDryRun(PolicyId, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScopedPublish(PolicyId, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Engine.Scopes.Should().Equal(new Guid?[] { null, null }, "an unscoped rule is evaluated over all matters");
        _fixture.World.Requests.Should().NotContain(r => r.StartsWith("GET sprk_matters?", StringComparison.Ordinal));
        _fixture.World.Writes[0].Json.TryGetProperty(AuthoringWorld.MatterBind, out _).Should().BeFalse();
        PointerPatch().Json.EnumerateObject().Select(p => p.Name).Should().Equal("sprk_CurrentVersion@odata.bind");
    }

    // ── The dry run evaluates within the scope (AC3, DI-112) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Scope_DryRun_EvaluatesWithinTheMatter_ListsOnlyIt_AndCountsWhatThePublishSupersedes()
    {
        var w = _fixture.World;
        w.AddSignal(PolicyId, "sprk_matter", MatterA, 1, SignalWriter.SignalStatusOpen, core: MatterA, coreType: MatterType);
        w.AddSignal(PolicyId, "sprk_matter", MatterB, 1, SignalWriter.SignalStatusOpen, core: MatterB, coreType: MatterType);
        _fixture.Engine.Hits = [Hit(MatterA, "Alpha"), Hit(MatterB, "Bravo"), Hit(MatterC, "Charlie")];

        var response = await ScopedDryRun(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Engine.Scopes.Should().Equal(new Guid?[] { MatterA }, "the same subject scope the publish re-evaluation uses");
        var result = await Json(response);
        result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("subjectId").GetGuid()).Should().Equal(MatterA);
        result.GetProperty("wouldReraise").GetInt32().Should().Be(1, "only the in-scope open Signal is re-raised");
        result.GetProperty("wouldSupersede").GetInt32().Should().Be(2,
            "a publish supersedes every open Signal of the policy (the out-of-scope one closes and is not re-raised)");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Bravo").And.NotContain("Charlie");
    }

    [Fact]
    public async Task Scope_DryRun_AnEvaluationThatEscapesTheScope_IsRefused_NeverShownOrProved()
    {
        _fixture.Engine.Hits = [Hit(MatterA, "Alpha"), Hit(MatterB, "Bravo")];
        _fixture.Engine.IgnoreScope = true;

        var response = await ScopedDryRun(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Bravo").And.NotContain(MatterB.ToString("D"));
        _fixture.Engine.IgnoreScope = false;
        var publish = await ScopedPublish(PolicyId, MatterA);
        publish.StatusCode.Should().Be(HttpStatusCode.Conflict, "the refused dry run recorded no proof");
        (await ReasonOf(publish)).Should().Be("ontology.admin.dry_run_required");
    }

    // ── The proof binds the scope ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scope_ADryRunWithinOneMatter_IsNoProofForAnotherScope()
    {
        (await ScopedDryRun(null, MatterA, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var (route, response) in new[]
                 {
                     ("save, another matter", await ScopedSave(MatterB)),
                     ("save, all matters", await ScopedSave(null)),
                     ("publish, another matter", await ScopedPublish(PolicyId, MatterB)),
                     ("publish, all matters", await ScopedPublish(PolicyId, null)),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, route);
            (await ReasonOf(response)).Should().Be("ontology.admin.dry_run_required", route);
        }

        _fixture.World.Writes.Should().BeEmpty();
    }

    // ── Negative cases (AC4) ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Each route's answer to a scope it refuses, as (status, reason code, the matterId/tenant field errors).</summary>
    private async Task<(HttpStatusCode Status, string? Reason, string Errors)> Refusal(string route, Guid? matterId, string body = Body, string? tenant = null)
    {
        var response = route switch
        {
            "validate" => await ScopedValidate(matterId, Draft(body: body), tenant: tenant),
            "dry run" => await ScopedDryRun(PolicyId, matterId, body, tenant: tenant),
            "dry run (new)" => await ScopedDryRun(null, matterId, body, lane: "Decide", tenant: tenant),
            "save" => await ScopedSave(matterId, Draft(body: body), tenant),
            "publish" => await ScopedPublish(PolicyId, matterId, Draft(body: body), tenant),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var json = await Json(response);
        if (route == "validate")
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, "validate answers a refusal as not valid, never as an error");
            var errors = ValidateErrors(json).Where(e => e.Key is "matterId" or "tenant").OrderBy(e => e.Key)
                .Select(e => e.Key + "=" + string.Join("|", e.Value));
            // A not-valid answer is reported as the 400 the other routes give, so one expectation covers all of them.
            var status = json.GetProperty("valid").GetBoolean() ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            return (status, json.GetProperty("reasonCode").GetString(), string.Join(";", errors));
        }

        var fields = json.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().Where(p => p.Name is "matterId" or "tenant").OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => p.Name + "=" + string.Join("|", p.Value.EnumerateArray().Select(m => m.GetString())))
            : [];
        return (response.StatusCode, json.TryGetProperty("reasonCode", out var r) ? r.GetString() : null, string.Join(";", fields));
    }

    public static TheoryData<string> ScopedRoutes => new() { "validate", "dry run", "dry run (new)", "save", "publish" };

    [Theory]
    [MemberData(nameof(ScopedRoutes))]
    public async Task Scope_AMatterThatDoesNotExist_AndOneTheCallerCannotRead_GetTheSameAnswer(string route)
    {
        _fixture.World.Missing.Add(("sprk_matter", GhostMatter));
        _fixture.World.Unreadable.Add(("sprk_matter", HiddenMatter));

        var missing = await Refusal(route, GhostMatter);
        var unreadable = await Refusal(route, HiddenMatter);
        var empty = await Refusal(route, Guid.Empty);

        missing.Should().Be((HttpStatusCode.BadRequest, (string?)null, "matterId=" + MatterNotFound), route);
        unreadable.Should().Be(missing, "no oracle: a matter the caller cannot read is answered as one that does not exist");
        empty.Should().Be(missing);
        MatterWasRead(GhostMatter).Should().BeTrue("the matter is read as the caller");
        MatterWasRead(HiddenMatter).Should().BeTrue();
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScopedRoutes))]
    public async Task Scope_AMatterScopeOnARuleThatIsNotAboutMatters_IsScopeSubjectMismatch(string route)
    {
        var refusal = await Refusal(route, MatterA, TodoBody);

        refusal.Should().Be((HttpStatusCode.BadRequest, ScopeMismatch, "matterId=A matter limit is allowed only on a rule about matters."), route);
        MatterWasRead(MatterA).Should().BeFalse("a refused scope is not read on the request's behalf");
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScopedRoutes))]
    public async Task Scope_ATenantValue_IsRefused_WithOrWithoutAMatter(string route)
    {
        const string tenantRefused = "tenant=A rule has no tenant scope: every environment is single-tenant.";

        var alone = await Refusal(route, null, tenant: "contoso");
        var withMatter = await Refusal(route, MatterA, tenant: "contoso");

        alone.Should().Be((HttpStatusCode.BadRequest, (string?)null, tenantRefused), route);
        withMatter.Should().Be(alone, route);
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScopedRoutes))]
    public async Task Scope_ABlankTenant_IsNoTenant_AndIsNotRefused(string route)
    {
        var refusal = await Refusal(route, null, tenant: "  ");

        refusal.Errors.Should().BeEmpty(route);
    }
}
