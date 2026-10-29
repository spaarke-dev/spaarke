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

    private async Task<HttpResponseMessage> ScopedDryRun(
        Guid? policyId, Guid? matterId, string body = Body, string? lane = null, string? tenant = null, bool? allMatters = null) =>
        await _client.PostAsJsonAsync($"{Root}/dry-run",
            new { policyId, lane, ruleType = "Existence", ruleBody = body, messageTemplate = Template, matterId, tenant, allMatters });

    private async Task<HttpResponseMessage> ScopedValidate(
        Guid? matterId, object? draft = null, Guid? policyId = null, string? tenant = null, bool? allMatters = null) =>
        await _client.PostAsJsonAsync($"{Root}/validate", new { policyId, lane = "Decide", draft = draft ?? Draft(), matterId, tenant, allMatters });

    private async Task<HttpResponseMessage> ScopedSave(Guid? matterId, object? draft = null, string? tenant = null) =>
        await _client.PostAsJsonAsync($"{Root}/policies",
            new { policyCode = "POL-NEW", name = "A new rule", lane = "Decide", draft = draft ?? Draft(), matterId, tenant });

    private async Task<HttpResponseMessage> ScopedPublish(
        Guid policyId, Guid? matterId, object? draft = null, string? tenant = null, bool? allMatters = null) =>
        await _client.PostAsJsonAsync($"{Root}/policies/{policyId:D}/versions", new { draft = draft ?? Draft(), matterId, tenant, allMatters });

    private AuthoringWorld.Write PointerPatch() =>
        _fixture.World.Writes.Should().ContainSingle(x => x.Kind == $"PATCH sprk_policies({PolicyId:D})").Subject;

    private bool MatterWasRead(Guid matter) =>
        // Fix round 4: the scope check reads the matter's access columns with its id ("sprk_matterid,sprk_issecure,...").
        _fixture.World.Requests.Any(r => r.StartsWith("GET sprk_matters?$select=sprk_matterid", StringComparison.Ordinal)
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
    public async Task Scope_PublishFromOneMatterToAllMatters_WithAllMatters_ClearsTheScopeWithANullBind()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, null, allMatters: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, null, allMatters: true);

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

    // ── Fix round 1 (F4): the scope intent is explicit; no intent never clears a stored scope ──────────────────────

    private const string ScopeIntentRequired = "ontology.admin.scope_intent_required";

    [Fact]
    public async Task Scope_AStaleClientWithNoIntent_OnAScopedPolicy_IsRefused_AndTheScopeIsKept()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;

        var dryRun = await ScopedDryRun(PolicyId, null);
        var publish = await ScopedPublish(PolicyId, null);

        foreach (var (route, response) in new[] { ("dry run", dryRun), ("publish", publish) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, route);
            (await ReasonOf(response)).Should().Be(ScopeIntentRequired, route);
            (await Json(response)).GetProperty("errors").GetProperty("matterId")[0].GetString()
                .Should().Be("This rule is limited to a matter. Send that matter to keep the limit, or all matters to remove it.", route);
        }

        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty("a request with no scope intent never clears the scope");
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA);
    }

    [Fact]
    public async Task Scope_Validate_OnAScopedPolicy_RefusesNoIntent_LikeTheDryRunAndPublish()
    {
        // Fix round 2 (K2): validate reads the stored scope and applies the one shared check, with the same code, field and message.
        // Validate answers a refusal as 200 not valid (never an error status), as for every other validate refusal.
        _fixture.World.Policies[PolicyId].Matter = MatterA;

        var validate = await ScopedValidate(null, policyId: PolicyId);
        var dryRun = await ScopedDryRun(PolicyId, null);

        validate.StatusCode.Should().Be(HttpStatusCode.OK, await validate.Content.ReadAsStringAsync());
        var result = await Json(validate);
        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        result.GetProperty("reasonCode").GetString().Should().Be(ScopeIntentRequired);
        ValidateErrors(result).Should().ContainKey("matterId").WhoseValue.Should().Equal(
            (await Json(dryRun)).GetProperty("errors").GetProperty("matterId")[0].GetString());
        _fixture.World.Writes.Should().BeEmpty();

        // Must-not-fire: the same policy with a matter, or with allMatters, validates; an unscoped policy with neither does too.
        (await Json(await ScopedValidate(MatterA, policyId: PolicyId))).GetProperty("valid").GetBoolean().Should().BeTrue();
        (await Json(await ScopedValidate(null, policyId: PolicyId, allMatters: true))).GetProperty("valid").GetBoolean().Should().BeTrue();
        _fixture.World.Policies[PolicyId].Matter = null;
        (await Json(await ScopedValidate(null, policyId: PolicyId))).GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Scope_Validate_AMatterAndAllMattersTogether_IsRefused_WithTheIntentCode(bool storedScope)
    {
        _fixture.World.Policies[PolicyId].Matter = storedScope ? MatterA : null;

        var response = await ScopedValidate(MatterB, policyId: PolicyId, allMatters: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await Json(response);
        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        result.GetProperty("reasonCode").GetString().Should().Be(ScopeIntentRequired);
        ValidateErrors(result).Should().ContainKey("matterId").WhoseValue.Should().Equal("Send a matter or all matters, not both.");
        MatterWasRead(MatterB).Should().BeFalse("a contradictory request is not read on");
    }

    [Fact]
    public async Task Scope_AllMattersFalse_IsNoIntent_OnAScopedPolicy()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;

        var publish = await ScopedPublish(PolicyId, null, allMatters: false);

        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonOf(publish)).Should().Be(ScopeIntentRequired);
        _fixture.World.Writes.Should().BeEmpty();
    }

    public static TheoryData<string> IntentRoutes => new() { "dry run", "dry run (new)", "publish" };

    [Theory]
    [MemberData(nameof(IntentRoutes))]
    public async Task Scope_AMatterAndAllMattersTogether_IsRefused_WithTheIntentCode(string route)
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;

        var response = route switch
        {
            "dry run" => await ScopedDryRun(PolicyId, MatterB, allMatters: true),
            "dry run (new)" => await ScopedDryRun(null, MatterB, lane: "Decide", allMatters: true),
            _ => await ScopedPublish(PolicyId, MatterB, allMatters: true),
        };

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, route);
        (await ReasonOf(response)).Should().Be(ScopeIntentRequired, route);
        (await Json(response)).GetProperty("errors").GetProperty("matterId")[0].GetString()
            .Should().Be("Send a matter or all matters, not both.", route);
        MatterWasRead(MatterB).Should().BeFalse("a contradictory request is not read on");
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA);
    }

    [Fact]
    public async Task Scope_TwoAdmins_AStoredScopeChangedSinceTheDryRun_IsPolicyChanged_AndNothingIsWritten()
    {
        // Admin 1 tests keeping M; admin 2 then publishes N; admin 1's publish (still "keep M") would silently move N back to M.
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.Policies[PolicyId].Matter = MatterB;

        var response = await ScopedPublish(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        _fixture.World.Writes.Should().BeEmpty("refused before any write");
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterB);
    }

    [Fact]
    public async Task Scope_TwoAdmins_AScopeAddedSinceAnUnscopedDryRun_IsPolicyChanged()
    {
        (await ScopedDryRun(PolicyId, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.Policies[PolicyId].Matter = MatterB;

        var response = await ScopedPublish(PolicyId, null, allMatters: true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be("ontology.admin.policy_changed");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Scope_DryRun_ReturnsTheStoredScope_WithItsNameWhenTheCallerCanReadIt()
    {
        var w = _fixture.World;
        w.Policies[PolicyId].Matter = MatterA;
        w.MatterNames[MatterA] = "Alpha matter";

        var keep = await Json(await ScopedDryRun(PolicyId, MatterA));
        keep.GetProperty("storedMatterId").GetGuid().Should().Be(MatterA);
        keep.GetProperty("storedMatterName").GetString().Should().Be("Alpha matter");

        // A stored matter the caller cannot read: its id, never its name (uac K3); the admin can still clear the limit (K4).
        w.Policies[PolicyId].Matter = HiddenMatter;
        w.Unreadable.Add(("sprk_matter", HiddenMatter));
        w.MatterNames[HiddenMatter] = "Hidden matter";
        var clear = await ScopedDryRun(PolicyId, null, allMatters: true);
        clear.StatusCode.Should().Be(HttpStatusCode.OK, await clear.Content.ReadAsStringAsync());
        var cleared = await Json(clear);
        cleared.GetProperty("storedMatterId").GetGuid().Should().Be(HiddenMatter);
        cleared.GetProperty("storedMatterName").ValueKind.Should().Be(JsonValueKind.Null);
        (await clear.Content.ReadAsStringAsync()).Should().NotContain("Hidden matter");

        w.Policies[PolicyId].Matter = null;
        var unscoped = await Json(await ScopedDryRun(PolicyId, null));
        unscoped.GetProperty("storedMatterId").ValueKind.Should().Be(JsonValueKind.Null, "an unscoped policy with no intent: as before");
    }

    [Fact]
    public async Task Scope_DryRun_AFaultReadingTheStoredMattersName_NeverFailsTheDryRun()
    {
        // Fix round 2 (uac-r2 F1): the name is display only. An HttpClient timeout is a TaskCanceledException while the request's own
        // token is not cancelled; the dry run still answers 200 with its counts, the stored id and a null name.
        var w = _fixture.World;
        w.Policies[PolicyId].Matter = MatterA;
        w.MatterNames[MatterA] = "Alpha matter";
        w.MatterNameReadThrows = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        var response = await ScopedDryRun(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body.GetProperty("storedMatterId").GetGuid().Should().Be(MatterA);
        body.GetProperty("storedMatterName").ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("wouldReraise", out _).Should().BeTrue("the dry run's counts are still returned");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Alpha matter");
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

    // ── Fix round 4 (uac-r2 E, D-123): a rule cannot be limited to a Secure or Restricted matter ────────────────────────────────

    private const string ScopeMatterSecured = "ontology.admin.scope_matter_secured";
    private const string ScopeMatterSecuredMessage =
        "A rule cannot be limited to a Secure or Restricted matter, or to one whose access level could not be read.";

    /// <summary>Restricted, as the root tables' <c>sprk_accesspermission</c> option set holds it (ExternalParticipationService).</summary>
    private const int AccessPermissionRestricted = 100000002;
    private const int AccessPermissionLimited = 100000001;

    public static TheoryData<string, string> SecuredMatterCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var route in new[] { "validate", "dry run", "dry run (new)", "save", "publish" })
        {
            foreach (var kind in new[] { "secure", "restricted", "secure and restricted", "flag not returned" }) data.Add(route, kind);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SecuredMatterCases))]
    public async Task Scope_R4_ASecureOrRestrictedMatter_IsRefused_ScopeMatterSecured_OnEveryRoute(string route, string kind)
    {
        _fixture.World.MatterAccess[MatterA] = kind switch
        {
            "secure" => (true, null),
            "restricted" => (false, AccessPermissionRestricted),
            "secure and restricted" => (true, AccessPermissionRestricted),
            _ => (null, null), // sprk_issecure masked: fails closed as Unreadable
        };

        var refusal = await Refusal(route, MatterA);

        refusal.Should().Be((HttpStatusCode.BadRequest, ScopeMatterSecured, "matterId=" + ScopeMatterSecuredMessage), $"{route}, {kind}");
        MatterWasRead(MatterA).Should().BeTrue("the matter is read as the caller");
        _fixture.Engine.Evaluated.Should().BeEmpty("a refused scope is never evaluated");
        _fixture.World.Writes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScopedRoutes))]
    public async Task Scope_R4_AnOrdinaryOrLimitedMatter_IsAllowed(string route)
    {
        // Must-not-fire: Standard (no value) and Limited are not refused; the refusal is Secure or Restricted only.
        _fixture.World.MatterAccess[MatterB] = (false, AccessPermissionLimited);
        if (route == "publish")
        {
            (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ScopedPublish(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);
            _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA);
            // Fix round 5 (uac-r2 K3 nit): and the Limited matter is allowed to publish too, not only the ordinary one.
            (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ScopedPublish(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);
            _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterB);
            return;
        }

        if (route == "save")
        {
            (await ScopedDryRun(null, MatterB, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ScopedSave(MatterB)).StatusCode.Should().Be(HttpStatusCode.Created);
            return;
        }

        var ordinary = await Refusal(route, MatterA);
        var limited = await Refusal(route, MatterB);

        ordinary.Should().Be((HttpStatusCode.OK, (string?)null, string.Empty), route);
        limited.Should().Be(ordinary, route);
    }

    [Theory]
    [InlineData("validate", "500")]
    [InlineData("dry run", "500")]
    [InlineData("save", "500")]
    [InlineData("publish", "500")]
    [InlineData("dry run", "throws")]
    [InlineData("publish", "throws")]
    public async Task Scope_R4_WhenTheMattersAccessCannotBeRead_TheScopeIsNeverAllowed(string route, string fault)
    {
        // Fail closed: a read fault is "unavailable", never an ordinary matter.
        if (fault == "throws") _fixture.World.MatterAccessReadThrows = new HttpRequestException("Dataverse is unreachable.");
        else _fixture.World.FailRead = "sprk_matters?$select=sprk_matterid,sprk_issecure";

        var response = route switch
        {
            "validate" => await ScopedValidate(MatterA),
            "dry run" => await ScopedDryRun(PolicyId, MatterA),
            "save" => await ScopedSave(MatterA),
            _ => await ScopedPublish(PolicyId, MatterA),
        };

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{route}: {await response.Content.ReadAsStringAsync()}");
        (await Json(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.admin.unavailable");
        _fixture.Engine.Evaluated.Should().BeEmpty();
        _fixture.World.Writes.Should().BeEmpty();
    }
}
