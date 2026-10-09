using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 158 r1c-v2 — main-session rounds 39 and 47, driven through the REAL routes and handlers,
/// the REAL inheritance, job, synchronizer, No Access guard and Assigned-To materializer over the provisioning fixture:
/// <list type="bullet">
/// <item>Round 39 item 1 (interpretation xiii reversed): unsecuring a parent ENDS what it passed on — only the unmodified
/// inherited share; a direct share, a raised mask (put back), a share another secure parent still justifies and the record's
/// last reader are kept; failures report through children_incomplete and the same call completes them. A parent that reads
/// not secure, or no longer exists, passes nothing on wherever the reverse rule looks.</item>
/// <item>Round 39 item 2: a DIRECT share on a filed secure record honours every secure parent's No Access list —
/// <c>/share-user</c>, colleagues named in <c>/provision-project</c>, and the Assigned-To rule's suggestion — through the
/// guard's one entry point.</item>
/// <item>Round 47 item 1 (E-158-v1-1): an Assigned-To CoveredByExisting row never justifies keeping a parent's share; the
/// covering share's end is a known cause (Skipped, covering-share-ended, written ahead) and the materializer suggests the
/// assignee at once; an assignment that ends gives the record its parents' sharees at once.</item>
/// <item>Round 47 item 2 (now): the operator's Declined marker is written BEFORE the revoke.</item>
/// <item>Round 47 item 3: the verifier's surviving seeds X03, X07, X08, X09 and X17 each have a test that bites.</item>
/// <item>The post-write check: a share decided on a read that changed before it landed is decided again.</item>
/// <item>Round 58 (task 158's final round): a share the enforcer removed for a secure PARENT's No Access list is recorded as
/// the wall's (both ledgers) and given again once the wall is lifted; an operator's unshare that does not land puts its
/// Declined marker back; the already-ordinary unsecure's refusal says what the operator must do.</item>
/// </list>
/// </summary>
[Trait("status", "task-158-uac-r2")]
public class SecureRootInheritanceRound39Tests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootInheritanceJobRunner _job;
    private readonly InternalUserShareTests.FakeSystemUsers _users = new();

    public SecureRootInheritanceRound39Tests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[Outsider] = (false, false);
        _fixture.SystemUsers[AppUser] = (false, true);
        _job = new SecureRootInheritanceJobRunner(_fixture);
        _users.SeedPerson(Creator, "Creator");
        _users.SeedPerson(Colleague, "Colleague");
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    private static int Mask(string rightsCsv) => RecordShareLevels.MaskForRightsCsv(rightsCsv);

    private static int Mirror => RecordShareLevels.ChildMirrorMask(Mask(ProvisionProjectEndpoint.CollaboratorAccessRights));

    private static int ViewMirror => RecordShareLevels.ChildMirrorMask(Mask(RecordShareLevels.ViewOnlyRights));

    private static readonly RootRecordFlags SecureFlags = new(IsSecure: true, IsRestricted: false);

    /// <summary>A work assignment already secured under <paramref name="matter"/> — as the rule leaves it.</summary>
    private void SecuredWorkAssignment(Guid id, Guid matter)
    {
        _fixture.SeedWorkAssignment(id, owningTeamId: SecureTeam, containerId: $"b!wa-{id:N}", isSecure: true);
        _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        World.Set("sprk_workassignment", id, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
    }

    private void AlsoFiledUnderProject(Guid workAssignment, Guid project) =>
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));

    /// <summary>
    /// Task 175 (owner round 84, replacing "never auto-unsecure"): after the matter's unsecure has decided what it passed on
    /// (the round-39 reasons below), the work assignment filed only under it FOLLOWS it out of secure in the same call —
    /// owned by its business unit's team, every explicit share revoked, as <c>/unsecure-project</c> does.
    /// </summary>
    private void ShouldHaveFollowedTheMatter(Guid workAssignment, Guid user)
    {
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("round 84: the work assignment follows the matter");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
        _fixture.ShareMaskOf(workAssignment, user).Should().Be(0, "its explicit shares go with its secure designation");
    }


    private IReadOnlyList<AssignedAccessLedgerRow> Provenance(Guid filed) => _fixture.InheritedLedger.InheritedRowsOf(filed);

    private AssignedAccessLedgerRow ProvenanceFrom(Guid filed, string parentTable, Guid parent, Guid user) =>
        Provenance(filed).Single(r => r.SystemUserId == user && r.SourceField == AssignedAccessStore.InheritedSourceField(parentTable, parent));

    /// <summary>The matter's flag cleared OUTSIDE the BFF (an administrator past the field security) — re-owned to a user.</summary>
    private void MatterUnsecuredOutOfBand(Guid matter) => _fixture.SeedMatter(matter, owningTeamId: null, isSecure: false);

    /// <summary>Task 142's materializer harness over the host's shares, No Access guard and scopes (round 47 item 1).</summary>
    private AssignedAccessTestDoubles.Harness HostAssignedAccess()
    {
        var harness = _fixture.AssignedAccess;
        harness.SharesOverride = _fixture.Services.GetRequiredService<IDataverseRecordShareService>();
        using var scope = _fixture.Services.CreateScope();
        harness.GuardOverride = scope.ServiceProvider.GetRequiredService<SecureShareNoAccessGuard>();
        harness.Scopes = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
        return harness;
    }

    // ── The routes' handlers ──────────────────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext Caller() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
            new Claim("oid", "66666666-6666-6666-6666-666666666666"),
        }, "test")),
        TraceIdentifier = "trace-158-r1c-v2",
    };

    /// <param name="hostGuard">The host's REAL No Access guard (its world, its deny list) — round 39 item 2's cases; otherwise
    /// one that walls nobody.</param>
    private async Task<IResult> ShareAsync(string recordType, Guid recordId, Guid user,
        ExternalAccessLevel level = ExternalAccessLevel.Collaborate, bool hostGuard = false)
    {
        using var scope = _fixture.Services.CreateScope();
        return await InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest(recordType, recordId, user, level),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), _users.Client, _fixture.NoAccessReads,
            new Mock<ITenantCache>().Object,
            new InternalUserShareTests.StubCallerRightsProbe(
                AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
                | AccessRights.Share),
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            hostGuard ? scope.ServiceProvider.GetRequiredService<SecureShareNoAccessGuard>() : SecureChildShareWorld.NobodyWalled(),
            scope.ServiceProvider.GetRequiredService<SecureRootInheritance>(),
            new AssignedAccessTestDoubles.Harness(_fixture.InheritedLedger).Materializer,
            Caller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private async Task<IResult> UnshareAsync(string recordType, Guid recordId, Guid user)
    {
        using var scope = _fixture.Services.CreateScope();
        return await InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest(recordType, recordId, user),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), _users.Client, _fixture.NoAccessReads,
            new Mock<ITenantCache>().Object,
            new AssignedAccessTestDoubles.Harness(_fixture.InheritedLedger).Materializer,
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            scope.ServiceProvider.GetRequiredService<SecureRootInheritance>(),
            new Spaarke.Scheduling.ProcessLocalScheduledJobLease(), Caller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private static (int Status, string? Code, JsonElement Body, string? Detail) Problem(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var body = JsonSerializer.SerializeToElement(problem.ProblemDetails.Extensions);
        return (problem.StatusCode, problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var c) ? c?.ToString() : null,
            body, problem.ProblemDetails.Detail);
    }

    private Task<HttpResponseMessage> UnsecureRouteAsync(string recordType, Guid recordId) =>
        _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { recordType, recordId });

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    // ══ Round 39 item 1 — unsecuring a parent ends what it passed on ═══════════════════════════════════════════════════

    /// <summary>KEPT: a direct share — the colleague already held the matter's mirror on the work assignment (recorded direct).</summary>
    [Fact]
    public async Task UnsecuringTheMatter_KeepsADirectShare()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        await ShareAsync("matter", matter, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.CoveredByExisting);

        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldHaveFollowedTheMatter(workAssignment, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptDirectShare);
    }

    /// <summary>KEPT, a raised mask: the colleague held View Only before the matter raised it — put back to View Only, never removed.</summary>
    [Fact]
    public async Task UnsecuringTheMatter_PutsARaisedShareBackToWhatItWasBefore()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        await ShareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(RecordShareLevels.ViewOnlyRights) | Mirror);

        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldHaveFollowedTheMatter(workAssignment, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    /// <summary>KEPT: an inherited share someone changed on the work assignment since — no longer the rule's.</summary>
    [Fact]
    public async Task UnsecuringTheMatter_KeepsAShareSomeoneChangedSince()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CreatorAccessRights);

        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldHaveFollowedTheMatter(workAssignment, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptModified);
    }

    /// <summary>
    /// KEPT: a share another secure parent still justifies — the work assignment is filed under the matter AND a secure
    /// project that shares the colleague. The matter's flag still reads secure while its unsecure runs; the matter justifies
    /// nothing any more, and the project carries the share.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_KeepsAShareAnotherSecureParentStillPassesOn()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        AlsoFiledUnderProject(workAssignment, project);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "the secure project it is filed under still passes it on");
        ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague).Reason.Should().Be(AssignedAccessReason.KeptOtherSource);
        ProvenanceFrom(workAssignment, "sprk_project", project, Colleague).State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>KEPT, S5: the colleague's inherited share is the work assignment's last reader — never removed (its row stays live).</summary>
    [Fact]
    public async Task UnsecuringTheMatter_NeverRemovesTheFiledRecordsLastReader()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Creator)); // nobody else opens it any more

        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);

        // S5 held through the matter's step (the row below); the follow then makes the record its business unit's, so it is
        // never left with nobody who can open it.
        ShouldHaveFollowedTheMatter(workAssignment, Colleague);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Revoked,
            "kept by the matter's step (S5), then ended by the work assignment's own un-secure with the share itself");
    }

    /// <summary>
    /// The work assignment was re-filed from the matter to a secure project that shares the colleague only at View Only. The
    /// matter's unsecure takes its share back and — once the matter's flag is cleared — gives back the project's View Only in
    /// the same call: the colleague holds exactly what the project passes on.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_GivesBackWhatAnotherParentPassesOnAtALowerLevel()
    {
        var (matter, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(ViewMirror, "the project's own level, at once");
    }

    /// <summary>
    /// Fault: what the matter passed on cannot be removed (the revoke fails). The unsecure stops BEFORE clearing the flag —
    /// 500 children_incomplete naming the record count — and the same call, repeated, completes it.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_WhenWhatItPassedOnCannotBeRemoved_StopsBeforeTheFlag_AndTheSameCallCompletesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.FailRevokeForPrincipal = Colleague;

        var first = await UnsecureRouteAsync("matter", matter);

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.IsSecureOf(matter).Should().BeTrue("the flag is cleared only once what it passed on has ended");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        _fixture.FailRevokeForPrincipal = null;
        var again = await UnsecureRouteAsync("matter", matter);

        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(matter).Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the repeat ended what was left");
    }

    /// <summary>Fault: what the matter passed on cannot be read — nothing decided, the flag kept, children_incomplete.</summary>
    [Fact]
    public async Task UnsecuringTheMatter_WhenWhatItPassedOnCannotBeRead_StopsBeforeTheFlag()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.InheritedLedger.FailLedgerRead = true;

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.IsSecureOf(matter).Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "nothing is ended on an unread provenance");
    }

    /// <summary>
    /// Fault after the flag: what the project still passes on cannot be given back yet — reported (children_incomplete,
    /// filedRecordsNotUpdated) though the matter IS unsecured; the job gives it back.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt_AndTheJobGivesIt()
    {
        var (matter, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        _fixture.FailShareForPrincipal = Colleague;

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.IsSecureOf(matter).Should().BeFalse("the matter itself was unsecured");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);

        _fixture.FailShareForPrincipal = null;
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(ViewMirror, "the job gives it back");
    }

    /// <summary>The work assignment passed the colleague by the matter (Collaborate), then re-filed under a project sharing View Only.</summary>
    private async Task<(Guid Matter, Guid WorkAssignment)> ReFiledUnderAProjectSharingLessAsync()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project);
        _fixture.SeedShare(project, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        AlsoFiledUnderProject(workAssignment, project);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "re-filing is not an unshare");
        return (matter, workAssignment);
    }

    /// <summary>
    /// A parent that reads NOT secure passes nothing on, wherever the reverse rule looks: the matter's flag was cleared out
    /// of band, and the job — visiting the work assignment through the secure project it is also filed under — ends what the
    /// matter passed on.
    /// </summary>
    [Fact]
    public async Task TheJob_EndsWhatAnUnsecuredParentStillPassedOn()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        SecureProject(_fixture, project);
        AlsoFiledUnderProject(workAssignment, project);
        MatterUnsecuredOutOfBand(matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "an unsecured matter passes nothing on");
        ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>The same rule on <c>/unshare-user</c>: an unshare on an unsecured matter ends what it is still on record as passing on.</summary>
    [Fact]
    public async Task UnsharingFromAnUnsecuredMatter_EndsWhatItStillPassedOn()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        MatterUnsecuredOutOfBand(matter);

        (await UnshareAsync("matter", matter, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();

        _fixture.IsSecureOf(workAssignment).Should().BeTrue("an unshare is not an unsecure (the job's round-84 cascade follows later)");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// The unsecure's repeat on a matter that is already ordinary: what it is still on record as passing on (its flag cleared
    /// out of band) is ended by the same call.
    /// </summary>
    [Fact]
    public async Task UnsecuringAMatterThatIsAlreadyOrdinary_EndsWhatItStillPassedOn()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        MatterUnsecuredOutOfBand(matter);

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await JsonOf(response)).GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>The same call, failing: what an already-ordinary matter still passed on cannot be removed — reported, the same call completes it.</summary>
    [Fact]
    public async Task UnsecuringAMatterThatIsAlreadyOrdinary_WhenWhatItStillPassedOnCannotBeRemoved_ReportsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        MatterUnsecuredOutOfBand(matter);
        _fixture.FailRevokeForPrincipal = Colleague;

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
    }

    /// <summary>The same call gives back what the filed record's other secure parent still passes on (at its lower level).</summary>
    [Fact]
    public async Task UnsecuringAMatterThatIsAlreadyOrdinary_GivesBackWhatAnotherParentPassesOn()
    {
        var (matter, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        MatterUnsecuredOutOfBand(matter);

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(ViewMirror);
    }

    /// <summary>More rows passed on than one read returns: the unsecure stops before the flag (never decided on part of them).</summary>
    [Fact]
    public async Task UnsecuringTheMatter_WhenWhatItPassedOnIsMoreThanOneReadReturns_StopsBeforeTheFlag()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.InheritedLedger.InheritedByParentTruncated = true;

        var response = await UnsecureRouteAsync("matter", matter);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.IsSecureOf(matter).Should().BeTrue();
    }

    /// <summary>The <c>/unshare-user</c> fan-out, likewise: more rows than one read returns is reported, never "done".</summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenWhatItPassedOnIsMoreThanOneReadReturns_ReportsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.InheritedLedger.InheritedByParentTruncated = true;

        var (status, code, _, _) = Problem(await UnshareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "what one read returned was still ended");
    }

    /// <summary>
    /// ADR-003 / round 17 item 3: a parent whose flag reads EMPTY is never "unsecured" — what it passed on is held and the
    /// record reported. (The work assignment was re-filed away from the matter, so its own filing reads cleanly and only the
    /// provenance reaches the matter.)
    /// </summary>
    [Fact]
    public async Task TheJob_NeverReadsAnEmptyParentFlagAsUnsecured_ItReportsTheRecord()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        SecureProject(_fixture, project);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        AlsoFiledUnderProject(workAssignment, project);
        MatterUnsecuredOutOfBand(matter);
        World.Set("sprk_matter", matter, "sprk_issecure", null);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "an empty flag is never read as not secure");
    }

    /// <summary>A parent that no longer exists passes nothing on: the job ends what it passed on.</summary>
    [Fact]
    public async Task TheJob_EndsWhatADeletedParentPassedOn()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        SecureProject(_fixture, project);
        AlsoFiledUnderProject(workAssignment, project);
        World.Delete("sprk_matter", matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// A parent flagged secure but NOT isolated (its unsecure in progress, or re-owned outside Spaarke) ends nothing: what it
    /// passed on is held — the share stays, and the record is reported.
    /// </summary>
    [Fact]
    public async Task TheJob_HoldsWhatAParentFlaggedSecureButNotIsolatedPassedOn()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.SeedMatter(matter, owningTeamId: null, isSecure: true); // re-owned to a user, still flagged

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "held, never ended on an untrusted parent");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Shared);
    }

    // ══ Round 39 item 2 — a direct share on a filed secure record honours every secure parent's No Access list ════════

    /// <summary>
    /// <c>/share-user</c> on a work assignment filed under a secure matter, for a person on the MATTER's No Access list (not
    /// the work assignment's): refused with the existing code, the message naming the matter's list, nothing written.
    /// </summary>
    [Fact]
    public async Task SharingAFiledRecord_WithAPersonOnTheSecureMattersNoAccessList_IsRefused()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, matter);

        var (status, code, _, detail) = Problem(await ShareAsync("workassignment", workAssignment, Colleague, hostGuard: true));

        status.Should().Be(403);
        code.Should().Be(InternalShareEndpoints.SubjectNoAccessReasonCode);
        detail.Should().Contain("secure matter this record is filed under");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>The record's OWN list still refuses with its own message.</summary>
    [Fact]
    public async Task SharingAFiledRecord_WithAPersonOnItsOwnNoAccessList_IsRefusedForItsOwnList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, workAssignment);

        var (status, code, _, detail) = Problem(await ShareAsync("workassignment", workAssignment, Colleague, hostGuard: true));

        status.Should().Be(403);
        code.Should().Be(InternalShareEndpoints.SubjectNoAccessReasonCode);
        detail.Should().Be("This person is on the No Access list for this record, so it was not shared with them.");
    }

    /// <summary>ADR-003: the matter the work assignment is filed under cannot be read — refused (500), nothing written.</summary>
    [Fact]
    public async Task SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        World.FailingRowReadsOf("sprk_matter", matter);

        var (status, code, _, detail) = Problem(await ShareAsync("workassignment", workAssignment, Colleague, hostGuard: true));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.NoAccessUnverifiableReasonCode);
        detail.Should().Contain("the No Access list of a secure record this record is filed under", "never 'this record's list'");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>A wall anywhere is final: the record's own list cannot be checked, but the matter's walls the person — refused as walled (403).</summary>
    [Fact]
    public async Task SharingAFiledRecord_WhenItsOwnListCannotBeCheckedButTheMattersWallsThePerson_IsRefusedAsWalled()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, matter);
        _fixture.NoAccessReads.Absent[workAssignment] = true; // the work assignment's own flags do not come back

        var (status, code, _, _) = Problem(await ShareAsync("workassignment", workAssignment, Colleague, hostGuard: true));

        status.Should().Be(403);
        code.Should().Be(InternalShareEndpoints.SubjectNoAccessReasonCode);
    }

    /// <summary>
    /// <c>/provision-project</c> on a work assignment filed under a secure matter: a colleague on the MATTER's No Access list
    /// is skipped with the existing per-person code and a message naming the matter's list; the other colleague is shared.
    /// </summary>
    [Fact]
    public async Task ProvisioningAFiledRecord_SkipsANamedColleagueOnTheSecureMattersNoAccessList()
    {
        var (matter, workAssignment, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: true); // flagged by Make Secure, owned by its creator
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId = workAssignment, sharePrincipalIds = new[] { Colleague, other } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "walled off the matter it is filed under");
        _fixture.ShareMaskOf(workAssignment, other).Should().NotBe(0);
        var skipped = (await JsonOf(response)).GetProperty("skippedPrincipals").EnumerateArray().Single();
        skipped.GetProperty("systemUserId").GetGuid().Should().Be(Colleague);
        skipped.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccess);
        skipped.GetProperty("message").GetString().Should().Contain("secure matter this record is filed under");
    }

    /// <summary>
    /// Round 31 item 1 through the same entry point: the caller provisioning a work assignment filed under a secure matter is
    /// on the MATTER's list — refused before any write, the message naming the matter's list (never an entry).
    /// </summary>
    [Fact]
    public async Task ProvisioningAFiledRecord_WhenTheCallerIsOnTheSecureMattersNoAccessList_IsRefusedNamingThatList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId = workAssignment });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        problem.GetProperty("detail").GetString().Should().Contain("the No Access list of the secure matter it is filed under");
        _fixture.OwningTeamOf(workAssignment).Should().BeNull("nothing was changed");
    }

    /// <summary>
    /// The matter a work assignment is filed under cannot be read when its creator provisions it: refused before any write
    /// (unverifiable), the message saying it is a secure record it is filed under whose list could not be checked.
    /// </summary>
    [Fact]
    public async Task ProvisioningAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        World.FailingRowReadsOf("sprk_matter", matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId = workAssignment });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        problem.GetProperty("detail").GetString().Should().Contain("the No Access list of a secure record it is filed under");
    }

    /// <summary>The same on the RESUME path (the work assignment already team-owned, its container missing): refused, naming that list.</summary>
    [Fact]
    public async Task ResumingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        World.FailingRowReadsOf("sprk_matter", matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId = workAssignment });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorNoAccess);
        problem.GetProperty("detail").GetString().Should().Contain("the No Access list of a secure record it is filed under");
    }

    /// <summary>
    /// The matter becomes unreadable after the creator's check (between the creator's share and the colleagues'): the named
    /// colleague is skipped as unverifiable, the warning saying it is a secure record it is filed under whose list could not
    /// be checked.
    /// </summary>
    [Fact]
    public async Task ProvisioningAFiledRecord_WhenTheMatterBecomesUnreadableBeforeTheColleagues_SkipsThemNamingThatList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        _fixture.OnGranted = (record, principal) =>
        {
            if (record == workAssignment && principal == DataversePrincipalRef.User(Creator))
                World.FailingRowReadsOf("sprk_matter", matter);
        };

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId = workAssignment, sharePrincipalIds = new[] { Colleague } });

        var body = await JsonOf(response);
        var skipped = body.GetProperty("skippedPrincipals").EnumerateArray().Single();
        skipped.GetProperty("systemUserId").GetGuid().Should().Be(Colleague);
        skipped.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccessUnverifiable);
        skipped.GetProperty("message").GetString().Should().Contain("the No Access list of a secure record this record is filed under");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// Task 142's suggestion on a filed secure work assignment honours the secure matter's list too: an assignee on it is
    /// never suggested (Skipped, no-access).
    /// </summary>
    [Fact]
    public async Task TheAssignedToRule_NeverSuggestsAPersonOnTheSecureMattersNoAccessList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        var assigned = HostAssignedAccess();
        var (contact, user) = assigned.LinkedContact();
        _fixture.InheritedLedger.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", contact);
        assigned.Participations.Flags[workAssignment] = SecureFlags;
        _fixture.NoAccessList.DenySystemUserOnRecord(user, matter);

        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);

        var row = _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single();
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    // ══ Round 47 item 1 — E-158-v1-1 ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The assignee held Read + Share on the work assignment; the matter's mirror raised it to Collaborate, which task 142's
    /// rule then found COVERING its target (CoveredByExisting — it wrote nothing). The matter's unshare: (1) that observation
    /// never justifies keeping the matter's level — the share is put back to what it was; (2) the 142 row's coverage ended, a
    /// known cause, and the materializer runs at once — on the secure record the assignee is SUGGESTED, never dropped.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_TakesBackAShareTheAssignedToRuleOnlyFoundCovering_AndSuggestsTheAssigneeAtOnce()
    {
        var (matter, workAssignment, contact, user) = await AssigneeCoveredByTheMattersShareAsync();

        (await UnshareAsync("matter", matter, user)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();

        _fixture.ShareMaskOf(workAssignment, user).Should().Be(ReadAndShare, "(1) the matter's raise is taken back");
        Provenance(workAssignment).Single(r => r.SystemUserId == user).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
        var assignee = _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single();
        assignee.State.Should().Be(AssignedAccessState.PendingConfirmation, "(2) suggested at once on a secure record");
    }

    /// <summary>Round 47 item 1 (2), write-ahead: the 142 row is Skipped (covering-share-ended) BEFORE the share is narrowed.</summary>
    [Fact]
    public async Task TheCoveringShareEndedMarker_IsWrittenBeforeTheShareIsNarrowed()
    {
        var (matter, workAssignment, contact, user) = await AssigneeCoveredByTheMattersShareAsync();
        (AssignedAccessState State, string? Reason)? atNarrowing = null;
        _fixture.OnModify = (record, principal) =>
        {
            if (record == workAssignment && principal == DataversePrincipalRef.User(user))
            {
                var row = _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single();
                atNarrowing = (row.State, row.Reason);
            }
        };

        await UnshareAsync("matter", matter, user);

        atNarrowing.Should().Be((AssignedAccessState.Skipped, AssignedAccessReason.CoveringShareEnded));
    }

    private static int ReadAndShare => Mask(RecordShareLevels.ViewOnlyRights) | 262144;

    /// <summary>The assignee (a linked contact's user) holds Read + Share; the matter raises it to Collaborate; 142 records it covered.</summary>
    private async Task<(Guid Matter, Guid WorkAssignment, Guid Contact, Guid User)> AssigneeCoveredByTheMattersShareAsync()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        var assigned = HostAssignedAccess();
        var (contact, user) = assigned.LinkedContact();
        _users.SeedPerson(user, "Assignee");
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(user), RecordShareLevels.RightsCsvForMask(ReadAndShare));
        (await ShareAsync("matter", matter, user)).Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        _fixture.ShareMaskOf(workAssignment, user).Should().Be(ReadAndShare | Mirror);

        _fixture.InheritedLedger.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", contact);
        assigned.Participations.Flags[workAssignment] = SecureFlags;
        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);
        _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single().State.Should().Be(AssignedAccessState.CoveredByExisting);
        return (matter, workAssignment, contact, user);
    }

    /// <summary>
    /// Round 47 item 1 (3): an assignment that ends on a filed secure work assignment runs the inheritance's sharee-only pass
    /// at once — the matter's sharee, whom no pass had reached yet, is given the work assignment in the same sync.
    /// </summary>
    [Fact]
    public async Task AnAssignmentThatEndsOnAFiledSecureRecord_GivesItsParentsShareesAtOnce()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        var assigned = HostAssignedAccess();
        var contact = assigned.Contact();
        _fixture.InheritedLedger.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", contact);
        assigned.Participations.Flags[workAssignment] = SecureFlags;
        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "no pass has reached it yet");

        _fixture.InheritedLedger.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", null);
        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "given at once when the assignment ended");
    }

    /// <summary>
    /// Round 47 item 1 (3) without a host (a materializer built outside one): an assignment that ends on a work assignment
    /// leaves its parents' sharees to the secure-root inheritance job — the materialization itself completes.
    /// </summary>
    [Fact]
    public async Task AnAssignmentThatEndsWhereNoHostIsReachable_LeavesTheParentsShareesToTheJob()
    {
        var harness = new AssignedAccessTestDoubles.Harness();
        var workAssignment = Guid.NewGuid();
        var contact = harness.Contact();
        harness.Store.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", contact);
        harness.Participations.Flags[workAssignment] = SecureFlags;
        await harness.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);
        harness.Store.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", null);

        var ended = await harness.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);

        ended.Complete.Should().BeTrue();
        harness.Store.RowsOf(workAssignment, contact).Single().State.Should().Be(AssignedAccessState.Revoked);
    }

    // ══ Round 47 item 2 — the operator's Declined marker BEFORE the revoke ═══════════════════════════════════════════════

    [Fact]
    public async Task AnOperatorsUnshareOnTheFiledRecord_IsRecordedDeclinedBeforeTheShareIsRemoved()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        AssignedAccessState? atRevoke = null;
        _fixture.OnRevoke = (record, principal) =>
        {
            if (record == workAssignment && principal == DataversePrincipalRef.User(Colleague))
                atRevoke = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State;
        };

        (await UnshareAsync("workassignment", workAssignment, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();

        atRevoke.Should().Be(AssignedAccessState.Declined, "the operator's decision is on record before the share goes");
    }

    // ══ Round 47 item 3 — the verifier's surviving seeds ════════════════════════════════════════════════════════════════

    /// <summary>
    /// X09 (CONFIRMED over-retention when seeded): the matter shared the colleague at View Only, then raised it to Collaborate.
    /// The re-raise keeps the level the FIRST share raised from (nothing), never the View Only it passed on itself — so the
    /// matter's unshare removes everything it gave.
    /// </summary>
    [Fact]
    public async Task ReRaisingAnInheritedShare_KeepsWhatItFirstRaisedFrom_SoTheParentsUnshareRemovesItAll()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague, ExternalAccessLevel.ViewOnly);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(ViewMirror);
        await ShareAsync("matter", matter, Colleague, ExternalAccessLevel.Collaborate);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "View Only was the matter's too: nothing is left");
    }

    /// <summary>
    /// X17: an operator adopted the colleague's share on the work assignment (its row from the matter is Adopted); the row from
    /// the project a marker failed to reach is still Shared. A pass that finds the share covered NEVER rewrites the operator's
    /// Adopted row as passed on — so the matter's unshare keeps it as the operator's.
    /// </summary>
    [Fact]
    public async Task AnOperatorsAdoptedRow_IsNeverRewrittenAsPassedOn_ByAPassThatFindsTheShareCovered()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        AlsoFiledUnderProject(workAssignment, project);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.RightsCsvForMask(Mirror));
        _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_matter", matter,
            DataversePrincipalRef.User(Colleague),
            new AssignedAccessLedgerWrite(AssignedAccessState.Adopted, AssignedAccessReason.ManualGrant, GrantedLevel: Mirror));
        _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_project", project,
            DataversePrincipalRef.User(Colleague),
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: Mirror));

        (await _job.RunAsync()).Success.Should().BeTrue();

        ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague).State.Should().Be(AssignedAccessState.Adopted,
            "the operator's decision is never overwritten");
        await UnshareAsync("matter", matter, Colleague);
        ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague).Reason.Should().Be(AssignedAccessReason.KeptAdopted);
    }

    /// <summary>
    /// X03: the colleague holds a DIRECT View Only share; the matter's raise to its mirror was recorded but never landed (its
    /// row is not in place). Filed under a project too, the pass finds the colleague covered — and records the project's
    /// provenance as DIRECT (CoveredByExisting), never as inherited from the matter's row whose level is not on the record.
    /// </summary>
    [Fact]
    public async Task ADirectShareBesideAnotherParentsShareThatNeverLanded_IsRecordedAsDirect_NeverAsPassedOn()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project);
        _fixture.SeedShare(project, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        SecuredWorkAssignment(workAssignment, matter);
        AlsoFiledUnderProject(workAssignment, project);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_matter", matter,
            DataversePrincipalRef.User(Colleague),
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared,
                AssignedAccessReason.SharePending + ";" + AssignedAccessReason.RaisedFromMaskPrefix + ViewMirror, GrantedLevel: Mirror));

        await _job.RunAsync();

        var fromProject = ProvenanceFrom(workAssignment, "sprk_project", project, Colleague);
        fromProject.State.Should().Be(AssignedAccessState.CoveredByExisting, "the matter's level is not on the record");
        fromProject.GrantedLevel.Should().Be(ViewMirror);
    }

    /// <summary>
    /// X07: what the project still passes on cannot be given back because the No Access list cannot be checked (the give-back
    /// is HELD) — reported (children_incomplete), never counted as given back.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenTheGiveBackCannotCheckTheNoAccessList_ReportsIt()
    {
        var (matter, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        _fixture.NoAccessList.FaultsWhenSubjectNames = Colleague;

        var (status, code, body, _) = Problem(await UnshareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// X08: the matter's share was recorded but never landed, and someone else gave the colleague View Only meanwhile. The
    /// matter's unshare removes nothing and records that share as someone else's (kept-modified), never "assignment ended".
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_KeepsAShareSomeoneElseGaveWhileTheRecordedOneNeverLanded()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.FailShareForPrincipal = Colleague;
        await _job.RunAsync();
        _fixture.FailShareForPrincipal = null;
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(RecordShareLevels.ViewOnlyRights));
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptModified);
    }

    /// <summary>
    /// X16: in one pass, the colleague's share from the matter is KEPT as the work assignment's last reader (S5 — its row
    /// stays live), and the project the work assignment was re-filed under raises the colleague. The raise starts from what
    /// the colleague held before ANY rule's share — nothing — because the kept share is still the matter's (its row is live,
    /// never read as ended in that pass): so when both sources end, nothing of the rules' is left beyond what S5 keeps.
    /// </summary>
    [Fact]
    public async Task AShareKeptAsTheLastReader_IsNeverTheLevelAnotherParentsRaiseStartsFrom()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Creator)); // the colleague is its last reader
        SecureProject(_fixture, project);
        var readAndDelete = Mask(RecordShareLevels.ViewOnlyRights) | 65536;
        _fixture.SeedShare(project, DataversePrincipalRef.User(Colleague), RecordShareLevels.RightsCsvForMask(readAndDelete));
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        AlsoFiledUnderProject(workAssignment, project);
        _fixture.RemoveShare(matter, DataversePrincipalRef.User(Colleague)); // the matter's unshare, outside the BFF

        await _job.RunAsync();

        ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague).Reason.Should().Be(AssignedAccessReason.KeptLastReader);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror | readAndDelete, "raised by the project");
        ProvenanceFrom(workAssignment, "sprk_project", project, Colleague).Reason.Should().BeNull(
            "raised from nothing: the kept share is still the matter's, never a level someone gave directly");
    }

    // ══ The post-write check: a share decided on a read that changed before it landed ═══════════════════════════════════

    /// <summary>
    /// The matter is unsecured WHILE the pass writes its sharee's share (between the pass's read of the matter and its write —
    /// the unsecure's reverse pass ran before this share's row existed). Once the share has landed, the pass finds the matter
    /// passes nothing on any more and ends it — the share never outlives its source.
    /// </summary>
    [Fact]
    public async Task AShareWrittenWhileItsParentIsUnsecured_IsEndedOnceItLands()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        var unsecured = false;
        _fixture.InheritedLedger.BeforeInheritedCreate = key =>
        {
            if (!unsecured && key.Contains(Colleague.ToString("D"), StringComparison.Ordinal))
            {
                unsecured = true;
                MatterUnsecuredOutOfBand(matter);
            }
        };

        await _job.RunAsync();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "its source ended before it landed");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Revoked);
    }

    /// <summary>
    /// A concurrent unshare of the colleague on the matter ended the pass's recorded row before its share landed (it found
    /// nothing to remove). Once the share has landed, the pass puts the row back on record and ends it — the share is removed.
    /// </summary>
    [Fact]
    public async Task AShareWhoseRowAConcurrentUnshareEnded_IsPutBackOnRecordAndEnded()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            _fixture.RemoveShare(matter, principal);
            EndRow(workAssignment, Colleague, AssignedAccessReason.AssignmentEnded);
        };

        await _job.RunAsync();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the matter no longer shares the colleague");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// A concurrent pass ended the row while the matter STILL shares the colleague: the share stands and is put back on record
    /// as passed on — so the matter's later unshare still removes it (never recorded as direct).
    /// </summary>
    [Fact]
    public async Task AShareWhoseRowWasEndedWhileTheParentStillSharesIt_IsPutBackOnRecordAsPassedOn()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            EndRow(workAssignment, Colleague, AssignedAccessReason.AssignmentEnded);
        };

        (await _job.RunAsync()).Success.Should().BeTrue();
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared, "put back on record");
        (await _job.RunAsync()).Success.Should().BeTrue();

        await UnshareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "round 30: removed with the matter's share");
    }

    /// <summary>A concurrent reverse pass ended the row AND removed the share: nothing of the pass's is left, and nothing is put back.</summary>
    [Fact]
    public async Task AShareAConcurrentPassAlsoRemoved_IsNotPutBackOnRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            var count = 0;
            _fixture.InheritedLedger.BeforeInheritedRead = () =>
            {
                // The pass's confirmation read is the first after its share landed; its check of what it wrote, the second.
                if (++count != 2)
                    return;
                _fixture.InheritedLedger.BeforeInheritedRead = null;
                _fixture.RemoveShare(matter, principal);
                _fixture.RemoveShare(workAssignment, principal);
                EndRow(workAssignment, Colleague, AssignedAccessReason.AccessRemoved);
            };
        };

        await _job.RunAsync();

        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Revoked, "nothing of the pass's is on the record");
        row.Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>The share's row was DELETED outside the BFF as it landed: it is put back on record (as passed on), never left with no row.</summary>
    [Fact]
    public async Task AShareWhoseRowWasDeletedAsItLanded_IsPutBackOnRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            _fixture.InheritedLedger.Ledger.RemoveAll(r => r.WorkAssignmentId == workAssignment && r.SystemUserId == Colleague);
        };

        await _job.RunAsync();

        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared);
        row.GrantedLevel.Should().Be(Mirror);
    }

    /// <summary>
    /// Putting the row back loses the race to a concurrent pass that recorded it first: nothing is written over that row, and
    /// the pass is reported (the next pass decides on the row as it stands).
    /// </summary>
    [Fact]
    public async Task PuttingAShareBackOnRecord_ThatLosesTheRace_IsReported()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            _fixture.InheritedLedger.Ledger.RemoveAll(r => r.WorkAssignmentId == workAssignment && r.SystemUserId == Colleague);
            _fixture.InheritedLedger.BeforeInheritedCreate = key =>
            {
                if (!key.Contains(Colleague.ToString("D"), StringComparison.Ordinal))
                    return;
                _fixture.InheritedLedger.BeforeInheritedCreate = null;
                _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_matter", matter,
                    DataversePrincipalRef.User(Colleague), new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: Mirror));
            };
        };

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse("the record lost the race: decided again on the next pass");
        _fixture.InheritedLedger.InheritedCreateConflicts.Should().ContainSingle();
    }

    /// <summary>Whether the matter still passes on the share just written cannot be read once it landed — reported, never "done".</summary>
    [Fact]
    public async Task AShareWhoseParentCannotBeReadOnceItLands_IsReported()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.OnGranted = (record, principal) =>
        {
            if (record != workAssignment || principal != DataversePrincipalRef.User(Colleague))
                return;
            _fixture.OnGranted = null;
            World.FailingRowReadsOf("sprk_matter", matter);
        };

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
    }

    // ══ Task 158 final round — main-session round 58 ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Round 58 item 1, the inheritance's side: task 143's enforcer removed the colleague's inherited share on the work
    /// assignment because they are on the MATTER's No Access list (the matter itself still shares them). The next pass records
    /// it as the wall's removal (Skipped, removed-by-no-access) — never an operator's Declined — and once the wall is lifted
    /// the matter's share is passed on again.
    /// </summary>
    [Fact]
    public async Task AShareTheWallRemovedForTheMattersList_IsRecordedAsTheWalls_AndPassedOnAgainOnceItIsLifted()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        var wall = _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, matter);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Colleague)); // the enforcer, through the matter's list

        await _job.RunAsync();

        var row = ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess, "the wall's removal, never an operator's");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "nothing is given while the wall stands");

        _fixture.NoAccessList.Lift(wall);
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "passed on again once the wall is lifted");
    }

    /// <summary>
    /// Round 58 item 1, task 142's side: an Assigned-To share on the work assignment (made while it was ordinary) is removed
    /// by the enforcer for the MATTER's list. The rule records it as the wall's removal — never an operator's Declined — and
    /// restores it once the wall is lifted (criterion 9).
    /// </summary>
    [Fact]
    public async Task AnAssignedToShareTheWallRemovedForTheMattersList_IsRestoredOnceItIsLifted()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        var assigned = HostAssignedAccess();
        var (contact, user) = assigned.LinkedContact();
        _fixture.InheritedLedger.Assign(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_assignedtointernal", contact);
        assigned.Participations.Flags[workAssignment] = RootRecordFlags.None; // shared while the record was ordinary
        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);
        _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single().State.Should().Be(AssignedAccessState.Shared);
        assigned.Participations.Flags[workAssignment] = SecureFlags; // then secured under the matter
        var wall = _fixture.NoAccessList.DenySystemUserOnRecord(user, matter);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(user)); // the enforcer, through the matter's list

        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);

        var row = _fixture.InheritedLedger.RowsOf(workAssignment, contact).Single();
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess, "the wall's removal, never an operator's");

        _fixture.NoAccessList.Lift(wall);
        await assigned.SyncAsync(ExternalGrantRootType.WorkAssignment, workAssignment);

        _fixture.ShareMaskOf(workAssignment, user).Should().NotBe(0, "restored once the wall is lifted");
    }

    /// <summary>
    /// Round 58 item 2 — the verifier's probe as a regression test: the operator's <c>/unshare-user</c> on the filed record
    /// writes its Declined marker, then the revoke fails (or Dataverse accepts it and keeps the share). The marker is PUT BACK
    /// in the same request, so a share the operator did not remove is never on record as declined — and the matter's own
    /// unshare, plus a job run, still ends the share it passed on (the probe kept mask 23).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOperatorsUnshareThatDoesNotLand_PutsTheMarkerBack_SoTheMattersUnshareStillEndsTheShare(bool revokeThrows)
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        if (revokeThrows)
            _fixture.FailRevokeForPrincipal = Colleague;
        else
            _fixture.RevokeNotAppliedFor = Colleague;

        var (status, _, _, _) = Problem(await UnshareAsync("workassignment", workAssignment, Colleague));

        status.Should().Be(500, "the removal did not land");
        var row = ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague);
        row.State.Should().Be(AssignedAccessState.Shared, "a share the operator did not remove is never on record as declined");
        row.Reason.Should().NotBe(AssignedAccessReason.RemovedByOperator);

        _fixture.FailRevokeForPrincipal = null;
        _fixture.RevokeNotAppliedFor = null;
        await UnshareAsync("matter", matter, Colleague);
        await _job.RunAsync();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the matter's unshare ends what it passed on");
    }

    /// <summary>A CONFIRMED operator's unshare keeps its Declined marker (only a removal that did not land is put back).</summary>
    [Fact]
    public async Task AConfirmedOperatorsUnshare_KeepsItsDeclinedMarker()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();

        (await UnshareAsync("workassignment", workAssignment, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();

        var row = ProvenanceFrom(workAssignment, "sprk_matter", matter, Colleague);
        row.State.Should().Be(AssignedAccessState.Declined);
        row.Reason.Should().Be(AssignedAccessReason.RemovedByOperator);
    }

    /// <summary>
    /// Round 58 item 2, the already-ordinary path's copy: what could not be removed is still on record, so the answer tells the
    /// operator to run Unsecure again — and that repeat does remove it.
    /// </summary>
    [Fact]
    public async Task UnsecuringAMatterThatIsAlreadyOrdinary_WhenSomethingCannotBeRemoved_SaysToRunItAgain_AndTheRepeatRemovesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        MatterUnsecuredOutOfBand(matter);
        _fixture.FailRevokeForPrincipal = Colleague;

        var first = await UnsecureRouteAsync("matter", matter);

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(first)).GetProperty("detail").GetString().Should().Be(
            "The matter is not secure, but the access it had passed on to the secure work assignments and projects filed under " +
            "it could not all be removed yet (1 not removed). Run Unsecure on this matter again to remove the rest.");

        _fixture.FailRevokeForPrincipal = null;
        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the repeat removed it, as the answer said");
    }

    /// <summary>
    /// Round 58 item 2, the already-ordinary path's copy: what another secure parent still gives could not be given back. A
    /// repeat call CANNOT give it back (what it passed on is ended), so the answer never says "calling again completes it" — it
    /// says no action is needed, and the secure-root inheritance job gives it back.
    /// </summary>
    [Fact]
    public async Task UnsecuringAMatterThatIsAlreadyOrdinary_WhenAGiveBackFails_SaysNoActionIsNeeded_AndTheJobGivesIt()
    {
        var (matter, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        MatterUnsecuredOutOfBand(matter);
        _fixture.FailShareForPrincipal = Colleague;

        var first = await UnsecureRouteAsync("matter", matter);

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("detail").GetString().Should().Be(
            "The matter is not secure, and the access it had passed on to the secure work assignments and projects filed under " +
            "it was removed. On 1 of them, the access the other secure records they are filed under still give could not be " +
            "given back yet. No action is needed for that: it is given back automatically within a few minutes (running " +
            "Unsecure again does not give it back); open those records' Manage Access to check.");

        _fixture.FailShareForPrincipal = null;
        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "a repeat call finds nothing left to give back");
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(ViewMirror, "the job gives it back");
    }

    /// <summary>Ends the inherited row of <paramref name="user"/> on <paramref name="filed"/> as a concurrent reverse pass would.</summary>
    private void EndRow(Guid filed, Guid user, string reason)
    {
        var row = Provenance(filed).Single(r => r.SystemUserId == user);
        _fixture.InheritedLedger.UpdateLedgerAsync(row.Id, new AssignedAccessLedgerWrite(AssignedAccessState.Revoked, reason), CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}
