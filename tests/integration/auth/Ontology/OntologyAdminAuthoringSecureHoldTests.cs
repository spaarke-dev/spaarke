using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// unified-access-control-r2 #2084 (owner round 102 item 4; option A3, agreed with spaarke-ontology-platform-r1): a publish asks
/// <see cref="IRecordOwnershipResolver"/> who would own the new version BEFORE any write. The Secure Record Owners team is a 409
/// <c>ontology.admin.rule_held_secure</c> with no POST and no PATCH; an ordinary answer binds no owner; a refusal or a fault is a 503 with
/// nothing written.
/// </summary>
public partial class OntologyAdminAuthoringEndpointTests
{
    private const string RuleHeldSecure = "ontology.admin.rule_held_secure";
    private const string OwnerUnavailable = "ontology.admin.owner_unavailable";
    private static readonly Guid SecureTeam = Guid.Parse("20840000-0000-4000-8000-0000000000f1");

    private void ResolverAnswersTheSecureTeam()
    {
        _fixture.Ownership.TeamId = SecureTeam;
        _fixture.Ownership.IsSecureOwner = true;
    }

    /// <summary>Round 2: the rule row itself is owned by the Secure team (the reconciler has re-owned it with its Secure matter).</summary>
    private void TheRuleIsHeldByTheSecureTeam() => _fixture.World.Policies[PolicyId].Owner = SecureTeam;

    private void AssertNothingWasWritten()
    {
        _fixture.World.Writes.Should().BeEmpty("the owner is decided before the version POST, the end stamp and the pointer/scope PATCH");
        _fixture.World.Requests.Should().NotContain(r => r.StartsWith("POST", StringComparison.Ordinal) || r.StartsWith("PATCH", StringComparison.Ordinal));
        _fixture.Engine.ReevaluateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_OfARuleHeldByASecureMattersTeam_Is409RuleHeldSecure_AndWritesNothing()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterA)).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam();
        TheRuleIsHeldByTheSecureTeam();

        var response = await ScopedPublish(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(RuleHeldSecure);
        var detail = (await Json(response)).GetProperty("detail").GetString();
        detail.Should().Be("This rule is held by a Secure matter's team; publish a new rule instead.");
        detail.Should().NotContain(MatterA.ToString("D"), "the message names no matter");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_AsksTheResolverWithTheRule_ItsStoredMatter_AndTheRequestedMatter()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var asked = _fixture.Ownership.Requests.Should().ContainSingle().Subject;
        asked.Parents.Should().BeEquivalentTo(new[]
        {
            new RecordOwnershipParent("sprk_policy", PolicyId),
            new RecordOwnershipParent("sprk_matter", MatterA),
            new RecordOwnershipParent("sprk_matter", MatterB),
        });
        asked.WhenUnfiled.Should().Be(UnfiledOwnership.KeepCreator);
    }

    [Fact]
    public async Task Publish_AsksTheResolverWithNoChildTable_SoTheAuthorWins_RootIncluded()
    {
        // D-197 / UAC round 137 ("author wins, including the root business unit"): the ownership question names NO child table.
        // 194b's restricted-content step applies only when ChildEntityLogicalName is a restricted-content table; naming
        // sprk_policyversion here (ForChild / ForRow) would make the resolver refuse every root-homed author with 503.
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var asked = _fixture.Ownership.Requests.Should().ContainSingle().Subject;
        asked.ChildEntityLogicalName.Should().BeNull("a child table here would make 194b refuse root-homed authors (D-197)");
        asked.WhenUnfiled.Should().Be(UnfiledOwnership.KeepCreator);
    }

    [Fact]
    public async Task Publish_WhenTheRequestedMatterTurnsSecureAfterTheScopeCheckPassed_IsScopeMatterSecured_AndWritesNothing()
    {
        // Case (i): the caller's read of MatterB says ordinary (CheckScopeAsync passes), but by the time the owner is asked the
        // resolver answers the Secure team. The rule itself is not held (its owner is not that team), so the Secure answer came from
        // the requested matter: round 2 gives the scope check's own 400, not "held by a Secure matter's team".
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam();

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(ScopeMatterSecured);
        (await Json(response)).GetProperty("errors").GetProperty("matterId")[0].GetString()
            .Should().Be("A rule cannot be limited to a Secure or Restricted matter, or to one whose access level could not be read.");
        MatterWasRead(MatterB).Should().BeTrue("the scope check ran and passed first");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_OfARuleNotYetReownedFromASecureStoredMatter_ToAnOrdinaryMatter_Is409RuleHeldSecure_NotTheScopeError()
    {
        // Round 3 (ontology F3): the rule still stores MatterA, which is Secure but the rule is not yet re-owned (inside the reconcile
        // window). Limiting it to ordinary MatterB passes the scope check; the resolver answers Secure because of A. The 400 would
        // blame B, which the caller cannot fix, so the answer is "held", and nothing is written.
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam();

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(RuleHeldSecure);
        MatterWasRead(MatterB).Should().BeTrue("the scope check ran and passed first");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_OfAHeldRule_LimitedToAnotherMatter_IsStill409RuleHeldSecure_AndWritesNothing()
    {
        // Round 2, the other branch: the rule row IS owned by the Secure team, so the answer is "held", whichever matter is named.
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, MatterB)).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam();
        TheRuleIsHeldByTheSecureTeam();

        var response = await ScopedPublish(PolicyId, MatterB);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(RuleHeldSecure);
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_ToASecureMatter_IsRefusedByTheScopeCheck_BeforeTheResolverIsAsked()
    {
        // Round 2 (refusal order): the requested matter already reads as Secure, so CheckScopeAsync refuses with its 400 and the
        // ownership resolver is never asked.
        _fixture.World.MatterAccess[MatterA] = (true, null);
        ResolverAnswersTheSecureTeam();

        var response = await ScopedPublish(PolicyId, MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ReasonOf(response)).Should().Be(ScopeMatterSecured);
        _fixture.Ownership.Requests.Should().BeEmpty("the scope check refuses before the owner is asked");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_UnscopingARuleHeldSecure_IsRefused_AndWritesNothing()
    {
        _fixture.World.Policies[PolicyId].Matter = MatterA;
        (await ScopedDryRun(PolicyId, null, allMatters: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam();

        var response = await ScopedPublish(PolicyId, null, allMatters: true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonOf(response)).Should().Be(RuleHeldSecure);
        AssertNothingWasWritten();
        _fixture.World.Policies[PolicyId].Matter.Should().Be(MatterA, "the scope PATCH never ran");
    }

    [Fact]
    public async Task Publish_WithAnOrdinaryTeamAnswer_BindsNoOwner_AndTheVersionStaysTheCallers()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        AssertTheVersionCarriesNoOwner();
    }

    [Fact]
    public async Task Publish_WithAnUnchangedAnswer_IsOrdinary_AndBindsNoOwner()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var published = await ServiceWith(new UnchangedResolver()).PublishVersionAsync(Principal(), PolicyId,
            new OntologyPublishRequest(new OntologyRuleDraft("Existence", Body, Template, "Needs a look", "Review the matter", DecidePlan)),
            CancellationToken.None);

        published.Outcome.Should().Be(OntologyAdminAuthoringOutcome.Ok, published.ReasonCode);
        AssertTheVersionCarriesNoOwner();
    }

    private void AssertTheVersionCarriesNoOwner()
    {
        var create = _fixture.World.Writes.Should().ContainSingle(x => x.Kind == "POST sprk_policyversions").Subject;
        create.Json.EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.StartsWith("ownerid", StringComparison.OrdinalIgnoreCase),
            "an ordinary answer binds no owner: the caller cannot assign (no prvAssign), and the row stays theirs as before #2084");
        _fixture.World.Writes.Select(x => x.Kind).Should().Equal(
            "POST sprk_policyversions", $"PATCH sprk_policyversions({V1:D})", $"PATCH sprk_policies({PolicyId:D})");
    }

    [Theory]
    [InlineData(RecordOwnerRefusal.ParentUnresolved)]
    [InlineData(RecordOwnerRefusal.SecureParentNotIsolated)]
    [InlineData(RecordOwnerRefusal.ParentUndetermined)]
    public async Task Publish_WhenTheOwnerIsUndetermined_Is503_AndWritesNothing(string refusal)
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Ownership.TeamId = null;
        _fixture.Ownership.RefusalCode = refusal;

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReasonOf(response)).Should().Be(OwnerUnavailable, "round 2: the wizard can say nothing was changed");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Publish_WhenTheResolverFaults_Is503_AndWritesNothing()
    {
        (await DryRun(PolicyId)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Ownership.Fault = new TimeoutException("Dataverse throttled the owner read");

        var response = await Publish(PolicyId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReasonOf(response)).Should().Be(OwnerUnavailable, "round 2: the wizard can say nothing was changed");
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task Save_IsUnchanged_ItNeverAsksTheResolver_AndBindsNoOwner()
    {
        (await ScopedDryRun(null, MatterA, lane: "Decide")).StatusCode.Should().Be(HttpStatusCode.OK);
        ResolverAnswersTheSecureTeam(); // would refuse a publish; the save (:969/:989) is left as agreed with ontology

        var response = await ScopedSave(MatterA);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Ownership.Requests.Should().BeEmpty();
        _fixture.World.Writes[0].Json.EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.StartsWith("ownerid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheOwnershipResolver_IsARequiredDependency()
    {
        var construct = () => new OntologyAdminAuthoringService(_fixture.World, _fixture.Services.GetRequiredService<PolicyVersionValidator>(),
            _fixture.Engine, ownership: null!, _fixture.Services.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            TimeProvider.System, NullLogger<OntologyAdminAuthoringService>.Instance);

        construct.Should().Throw<ArgumentNullException>().WithParameterName("ownership");
    }

    [Fact]
    public void Production_BuildsTheAuthoringServiceWithTheRealOwnershipResolver()
    {
        using var scope = _fixture.ProductionServices().CreateScope();

        scope.ServiceProvider.GetRequiredService<IRecordOwnershipResolver>().Should().BeOfType<RecordOwnershipResolver>();
    }

    private System.Security.Claims.ClaimsPrincipal Principal() => new(new System.Security.Claims.ClaimsIdentity(
        [new System.Security.Claims.Claim("oid", "oid-" + _fixture.World.Caller.ToString("N")),
         new System.Security.Claims.Claim("tid", Sprk.Bff.Api.Tests.Integration.Workspace.WorkspaceTestConstants.TestTenantId)], "test"));

    private OntologyAdminAuthoringService ServiceWith(IRecordOwnershipResolver ownership) =>
        new(_fixture.World, _fixture.Services.GetRequiredService<PolicyVersionValidator>(), _fixture.Engine, ownership,
            _fixture.Services.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(), TimeProvider.System,
            NullLogger<OntologyAdminAuthoringService>.Instance);

    /// <summary>Answers <see cref="RecordOwnerOutcome.Unchanged"/>: the resolver names no team (the row keeps its creator).</summary>
    private sealed class UnchangedResolver : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) => Task.FromResult<Guid?>(null);

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct) =>
            Task.FromResult(RecordOwnerResolution.Unchanged("test: keeps its creator"));

        public Task<RecordOwnerResolution> ReparentAsync(RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
