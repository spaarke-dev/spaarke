using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
/// unified-access-control-r2 task 158 r1 — owner rounds 30 and 31 and the first verification's findings, driven through the
/// REAL inheritance, job, provisioning, share routes' handlers and synchronizer over the provisioning fixture's Dataverse:
/// <list type="bullet">
/// <item>Round 31 item 1: the person a record is secured for honours its OWN No Access list and EVERY secure parent's —
/// checked before provisioning's first write, never dependent on the record's flag (it arrives unflagged on the inherited
/// path).</item>
/// <item>Round 30: where each share passed on to a filed secure root came from is recorded on task 142's
/// <c>sprk_assignedaccess</c> ledger; the parent's unshare removes only the inherited share that is still unmodified; a
/// direct share stays; an operator's removal on the filed record is Declined and never re-added while the parent share
/// persists; the job reconciles the provenance (changes made outside the BFF).</item>
/// <item>The job: any deferral fails the run and the next run continues from a cursor; records filed under a parent whose
/// flag is EMPTY are reported; the pair listing finds every spelling the decision side accepts; an unconfirmed pair candidate
/// is reported (A2); a half-provisioned filed record is completed (A6).</item>
/// </list>
/// </summary>
[Trait("status", "task-158-uac-r2")]
public class SecureRootInheritanceRound31Tests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";

    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootInheritanceJobRunner _job;
    private readonly InternalUserShareTests.FakeSystemUsers _users = new();

    public SecureRootInheritanceRound31Tests(ProvisionProjectTestFixture fixture)
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

    private void ShouldBeSecure(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeTrue($"{because}: sprk_issecure");
        _fixture.OwningTeamOf(id).Should().Be(SecureTeam, $"{because}: the named owner team");
        _fixture.ContainerIdOf(id).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, $"{because}: its own container");
        _fixture.ShareMaskOf(id, Creator).Should().Be(Mask(ProvisionProjectEndpoint.CreatorAccessRights), $"{because}: its creator is shared");
    }

    private void ShouldBeUntouched(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeFalse(because);
        _fixture.OwningTeamOf(id).Should().BeNull(because);
        _fixture.Updates.Should().NotContain(u => u.RecordId == id, because);
        _fixture.SharesOn(id).Should().BeEmpty(because);
    }

    /// <summary>A work assignment already secured under <paramref name="matter"/> — as the rule leaves it.</summary>
    private void SecuredWorkAssignment(Guid id, Guid matter)
    {
        _fixture.SeedWorkAssignment(id, owningTeamId: SecureTeam, containerId: $"b!wa-{id:N}", isSecure: true);
        _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        World.Set("sprk_workassignment", id, "sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter));
    }

    private IReadOnlyList<AssignedAccessLedgerRow> Provenance(Guid filed) => _fixture.InheritedLedger.InheritedRowsOf(filed);

    // ── The share routes' handlers, with the host's REAL inheritance and synchronizer and the fixture's ledger ─────────

    private static DefaultHttpContext Caller() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
            new Claim("oid", "66666666-6666-6666-6666-666666666666"),
        }, "test")),
        TraceIdentifier = "trace-158-r1",
    };

    private async Task<IResult> ShareAsync(string recordType, Guid recordId, Guid user,
        ExternalAccessLevel level = ExternalAccessLevel.Collaborate)
    {
        using var scope = _fixture.Services.CreateScope();
        return await InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest(recordType, recordId, user, level),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), _users.Client, _fixture.NoAccessReads,
            new Mock<ITenantCache>().Object,
            new InternalUserShareTests.StubCallerRightsProbe(
                AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
                | AccessRights.Share),
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(), SecureChildShareWorld.NobodyWalled(),
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

    private static (int Status, string? Code, JsonElement Body) Problem(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var body = JsonSerializer.SerializeToElement(problem.ProblemDetails.Extensions);
        return (problem.StatusCode, problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var c) ? c?.ToString() : null, body);
    }

    // ══ Owner round 31 item 1 — the creator honours BOTH No Access lists, before any write ══════════════════════════════

    /// <summary>
    /// The verifier's CRITICAL probe: a work assignment filed OUT OF BAND under a secure matter arrives UNFLAGGED (what the
    /// guard reads before Step 4.1), and its creator is on the work assignment's OWN No Access list. The inherited
    /// provisioning must refuse BEFORE any write — never flag it, move it or share it to the walled creator — and the job
    /// reports it.
    /// </summary>
    [Fact]
    public async Task TheJob_WhenARecordsCreatorIsOnItsOwnNoAccessList_NeitherSecuresNorSharesIt_ThoughItReadsUnflagged()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.NoAccessReads.Flags[workAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, workAssignment);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _fixture.ShareMaskOf(workAssignment, Creator).Should().Be(0, "a walled creator is never shared on it");
        ShouldBeUntouched(workAssignment, "refused before provisioning's first write");
    }

    /// <summary>
    /// The verifier's MEDIUM: the creator is on the SECURE MATTER's No Access list (not the record's) — the record filed under
    /// it is not secured for them, nothing is written, and the job reports it — the same rule the sharee mirror applies.
    /// </summary>
    [Fact]
    public async Task TheJob_WhenTheCreatorIsOnTheSecureParentsNoAccessList_DoesNotSecureTheRecordFiledUnderIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        ShouldBeUntouched(workAssignment, "the creator is walled off the secure matter it is filed under");
    }

    /// <summary>
    /// The RESUME branch of the inherited path has the same order (the verifier: "the resume creator check runs before
    /// resumeFlag"): a half-provisioned record (owned by the named team, no container) reading unflagged, whose creator is
    /// on its own No Access list — refused, nothing shared.
    /// </summary>
    [Fact]
    public async Task TheJob_ResumingARecordWhoseCreatorIsWalled_RefusesBeforeTheFlagOrTheShare()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, isSecure: false);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter));
        _fixture.NoAccessReads.Flags[workAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, workAssignment);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(ProvisionProjectEndpoint.ReasonResumeCreatorNoAccess);
        _fixture.ShareMaskOf(workAssignment, Creator).Should().Be(0);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("the flag is written only after the creator's walls are cleared");
    }

    /// <summary>
    /// The TRANSITION: provisioning a matter whose filed work assignment was created by someone walled off the matter — the
    /// matter's own steps stand, the work assignment is reported (<c>children_incomplete</c> naming it with
    /// <c>creator_no_access</c>) and left exactly as it was.
    /// </summary>
    [Fact]
    public async Task ProvisioningAMatter_WhoseFiledRecordsCreatorIsWalledOffTheMatter_LeavesThatRecordAndReportsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter, createdBy: Outsider);
        _fixture.NoAccessList.DenySystemUserOnRecord(Outsider, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        var outstanding = doc.RootElement.GetProperty("filedRecords").EnumerateArray().Single();
        outstanding.GetProperty("recordId").GetGuid().Should().Be(workAssignment);
        outstanding.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Outsider).Should().Be(0);
    }

    // ══ Owner round 30 — the provenance of inherited shares ═══════════════════════════════════════════════════════════

    /// <summary>
    /// A sharee given on the secure matter (<c>/share-user</c>) reaches its secure filed work assignment in the same call,
    /// and the ledger records where it came from: one row on the work assignment, source
    /// <c>inherited:sprk_matter:{matter}</c>, the user as the subject, <see cref="AssignedAccessState.Shared"/>, the mask
    /// written.
    /// </summary>
    [Fact]
    public async Task SharingOnTheMatter_PassesTheShareOn_AndRecordsWhereItCameFrom()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);

        (await ShareAsync("matter", matter, Colleague)).Should().BeOfType<Ok<ShareRecordWithUserResponse>>();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        var row = Provenance(workAssignment).Should().ContainSingle(r => r.SystemUserId == Colleague).Subject;
        row.SourceField.Should().Be(AssignedAccessStore.InheritedSourceField("sprk_matter", matter));
        row.State.Should().Be(AssignedAccessState.Shared);
        row.GrantedLevel.Should().Be(Mirror, "the mask the rule wrote — what 'unmodified' is decided against");
    }

    /// <summary>
    /// The parent's unshare (<c>/unshare-user</c> on the matter) removes the share the matter's sharing gave the user on the
    /// filed work assignment — in the same call — because it is still the unmodified inherited one; its row is Revoked.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_RemovesTheUnmodifiedInheritedShare_FromTheFiledRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);

        var result = await UnshareAsync("matter", matter, Colleague);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "its access there came from the matter");
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Revoked);
        row.Reason.Should().Be(AssignedAccessReason.AccessRemoved);
        _fixture.ShareMaskOf(workAssignment, Creator).Should().Be(Mask(ProvisionProjectEndpoint.CreatorAccessRights), "the creator is untouched");
    }

    /// <summary>A4: a share the user already held directly on the filed record (at least the mirror) is never removed by the parent's unshare.</summary>
    [Fact]
    public async Task UnsharingFromTheMatter_KeepsADirectShareThatAlreadyCarriedTheMirror()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        await ShareAsync("matter", matter, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.CoveredByExisting);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(ProvisionProjectEndpoint.CollaboratorAccessRights), "direct access stays");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptDirectShare);
    }

    /// <summary>A4: an inherited share someone RAISED on the filed record since is no longer the rule's — kept on the parent's unshare.</summary>
    [Fact]
    public async Task UnsharingFromTheMatter_KeepsAnInheritedShareThatWasRaisedSince()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CreatorAccessRights);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(ProvisionProjectEndpoint.CreatorAccessRights), "modified: kept");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptModified);
    }

    /// <summary>
    /// A4 "never lower existing access": a user who held View Only on the filed record before the matter's sharing raised
    /// it is put BACK to View Only on the matter's unshare — never removed below what they held.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_PutsARaisedShareBackToWhatItWasBefore()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        await ShareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(RecordShareLevels.ViewOnlyRights) | Mirror);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mask(RecordShareLevels.ViewOnlyRights));
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    /// <summary>
    /// Owner round 30: an operator's removal on the FILED record is recorded Declined and is never re-added while the
    /// matter still shares the user — neither by a later pass-on nor by the job; once the matter's share ends the row ends,
    /// and a later share on the matter passes it on again.
    /// </summary>
    [Fact]
    public async Task AnOperatorsRemovalOnTheFiledRecord_IsNeverUndoneWhileTheParentStillSharesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);

        (await UnshareAsync("workassignment", workAssignment, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Declined);

        (await _job.RunAsync()).Success.Should().BeTrue();
        await ShareAsync("matter", matter, Colleague); // a repeat share on the matter re-runs the pass-on
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "declined: never re-added while the matter's share persists");

        await UnshareAsync("matter", matter, Colleague);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Revoked);

        await ShareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "a new share on the matter is passed on again");
    }

    /// <summary>
    /// The L4 job reconciles provenance: the matter's share was removed OUTSIDE the BFF (a model-driven-app Unshare) — the
    /// next run removes the unmodified inherited share from the filed record.
    /// </summary>
    [Fact]
    public async Task TheJob_EndsAnInheritedShare_WhenTheParentWasUnsharedOutsideTheBff()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        _fixture.RemoveShare(matter, DataversePrincipalRef.User(Colleague));
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Revoked);
    }

    /// <summary>
    /// The L4 job: an inherited share removed on the FILED record outside the BFF is recorded Declined and never re-added
    /// while the matter's share persists.
    /// </summary>
    [Fact]
    public async Task TheJob_RecordsAnOutOfBandRemovalOnTheFiledRecordAsDeclined_AndNeverReAddsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Colleague));
        (await _job.RunAsync()).Success.Should().BeTrue();
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Declined);
        row.Reason.Should().Be(AssignedAccessReason.RemovedOutOfBand);
    }

    /// <summary>
    /// A TEAM sharee of the matter is passed on and recorded with its team (<c>sprk_subjectteam</c>); the matter's unshare of
    /// the team (outside the BFF — the share routes are user-only) ends it on the filed record at the next run.
    /// </summary>
    [Fact]
    public async Task ATeamSharee_IsPassedOn_RecordedWithItsTeam_AndEndedWhenTheMatterNoLongerSharesIt()
    {
        var (matter, workAssignment, team) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedShare(matter, DataversePrincipalRef.Team(team), ProvisionProjectEndpoint.CollaboratorAccessRights);
        SecuredWorkAssignment(workAssignment, matter);

        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, DataversePrincipalRef.Team(team)).Should().Be(Mirror);
        Provenance(workAssignment).Should().ContainSingle(r => r.SubjectTeamId == team && r.State == AssignedAccessState.Shared);

        _fixture.RemoveShare(matter, DataversePrincipalRef.Team(team));
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.ShareMaskOf(workAssignment, DataversePrincipalRef.Team(team)).Should().Be(0);
    }

    /// <summary>
    /// A4 "a share that is also direct": the user also holds an independent (Assigned-To, task 142) ledger row on the filed
    /// record naming them — the matter's unshare ends the inherited row but never removes the share.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_KeepsAShareAnIndependentLedgerRowAlsoJustifies()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.InheritedLedger.SeedRow(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType.WorkAssignment, workAssignment,
            "sprk_assignedtointernal", new AssignedSubject(AssignedSubjectKind.Contact, Guid.NewGuid()), AssignedAccessState.Shared,
            systemUserId: Colleague);

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "the Assigned-To rule also gives it");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptOtherField);
    }

    /// <summary>
    /// A share another secure parent still passes on is kept: the work assignment was re-filed away from the matter (its
    /// inherited row from the matter remains) and is filed under a secure project that shares the same user — the matter's
    /// unshare ends its row without removing the share.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_KeepsAShareAnotherSecureParentStillPassesOn()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureProject(_fixture, project, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject",
            new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "the secure project it is filed under still passes it on");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague
                && r.SourceField == AssignedAccessStore.InheritedSourceField("sprk_matter", matter))
            .Reason.Should().Be(AssignedAccessReason.KeptOtherSource);
    }

    /// <summary>S5 holds on the reverse rule: an inherited share that is the record's LAST reader is kept (ended without removal).</summary>
    [Fact]
    public async Task UnsharingFromTheMatter_NeverRemovesTheFiledRecordsLastReader()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Creator)); // nobody else opens it any more

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "S5: never the last person who can open it");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.KeptLastReader);
    }

    /// <summary>Fault: the provenance cannot be read — the share pass-on gives nobody anything and the share route reports it (children_incomplete).</summary>
    [Fact]
    public async Task SharingOnTheMatter_WhenTheProvenanceCannotBeRead_GivesNothing_AndReportsChildrenIncomplete()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.InheritedLedger.FailLedgerRead = true;

        var result = await ShareAsync("matter", matter, Colleague);

        var (status, code, body) = Problem(result);
        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().BeGreaterThan(0);
        _fixture.ShareMaskOf(matter, Colleague).Should().NotBe(0, "the matter's own share stands");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "nothing is given whose origin could not be recorded");
    }

    /// <summary>Fault: the reverse fan-out cannot read the provenance — the unshare stands and is reported (children_incomplete).</summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenTheProvenanceCannotBeRead_StandsAndReportsChildrenIncomplete()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.InheritedLedger.FailLedgerRead = true;

        var result = await UnshareAsync("matter", matter, Colleague);

        var (status, code, _) = Problem(result);
        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        _fixture.ShareMaskOf(matter, Colleague).Should().Be(0, "the matter's unshare stands");
    }

    /// <summary>Fault: the provenance cannot be written — the job's run is not a success (until it is recorded, an unshare could not remove it).</summary>
    [Fact]
    public async Task TheJob_WhenTheProvenanceCannotBeWritten_FailsTheRun()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.InheritedLedger.FailLedgerWrites = true;

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
    }

    // ══ Round 30 — the provenance's lifecycle beyond one share (task 158 r1 completion) ════════════════════════════════

    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    private Task<HttpResponseMessage> UnsecureRouteAsync(string recordType, Guid recordId) =>
        _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { recordType, recordId });

    /// <summary>
    /// Unsecuring a filed record revokes every share on it, so what its parent passed on ENDS with it
    /// (<see cref="AssignedAccessReason.RecordUnsecured"/>) — and when it is secured again under its secure parent, the
    /// parent's sharee is passed on again. A row left Shared would read, after the revoke, as an operator's removal
    /// (Declined) and withhold the sharee for good. (Task 158 r1c-v2: the record is first RE-FILED AWAY from the matter — it
    /// keeps what the matter passed on, interpretation xiv — so its unsecure is not refused; unsecuring the matter would
    /// itself end those rows now, round 39 item 1.)
    /// </summary>
    [Fact]
    public async Task UnsecuringAFiledRecord_EndsWhatItsParentPassedOn_SoSecuringItAgainPassesTheShareeOnAgain()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null); // re-filed away: it keeps the share (xiv)
        var unsecured = await UnsecureRouteAsync("workassignment", workAssignment);

        unsecured.StatusCode.Should().Be(HttpStatusCode.OK, await unsecured.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "an unsecure revokes every share");
        var ended = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        ended.State.Should().Be(AssignedAccessState.Revoked);
        ended.Reason.Should().Be(AssignedAccessReason.RecordUnsecured);

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter",
            new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter)); // filed under the secure matter again
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("filed under a secure matter again");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "never mistaken for an operator's removal");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague && r.State != AssignedAccessState.Revoked)
            .State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>Fault: the provenance cannot be ended — the unsecure stops before revoking anything (children_incomplete).</summary>
    [Fact]
    public async Task UnsecuringAFiledRecord_WhenItsProvenanceCannotBeEnded_StopsBeforeRevokingItsShares()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null); // re-filed away: it keeps the share (xiv)
        _fixture.InheritedLedger.FailLedgerWrites = true;

        var response = await UnsecureRouteAsync("workassignment", workAssignment);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("the flag stays until the same call completes it");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "its shares were not revoked");
    }

    /// <summary>
    /// Owner round 30, "never re-added WHILE the parent share persists": an operator removed the inherited share on the filed
    /// record (Declined); the matter's share then ended OUTSIDE the BFF — the job ends the decline with it — and a later
    /// share on the matter is passed on again.
    /// </summary>
    [Fact]
    public async Task TheJob_EndsADeclineWhenTheParentsShareEndsOutsideTheBff_SoALaterShareIsPassedOnAgain()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        (await UnshareAsync("workassignment", workAssignment, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Declined);

        _fixture.RemoveShare(matter, DataversePrincipalRef.User(Colleague));
        (await _job.RunAsync()).Success.Should().BeTrue();
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Revoked,
            "the decline held only while the matter's share persisted");

        _fixture.SeedShare(matter, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "a new share on the matter is passed on again");
    }

    /// <summary>
    /// ADR-003 in the provenance reconcile: whether a parent that passed a share on still shares the person cannot be read —
    /// the record's pass is incomplete and the run is not a success (never read as "not secure, ends nothing").
    /// </summary>
    [Fact]
    public async Task TheJob_WhenAParentThatPassedAShareOnCannotBeRead_FailsTheRun_NamingTheRecord()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureProject(_fixture, project, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.RightsCsvForMask(Mirror));
        await _fixture.InheritedLedger.CreateInheritedLedgerAsync(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType.WorkAssignment,
            workAssignment, "sprk_project", project, DataversePrincipalRef.User(Colleague),
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: Mirror), CancellationToken.None);
        World.FailingRowReadsOf("sprk_project", project);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "nothing is ended on an unread answer");
    }

    /// <summary>
    /// Task 158 r1c-v2 — main-session round 39 item 1 REVERSES interpretation xiii (this test asserted the opposite before):
    /// unsecuring a parent ends what it passed on. The matter's unsecure ends the unmodified share it passed on to the secure
    /// work assignment filed under it, and its row is ended. Task 175 (owner round 84, replacing "never auto-unsecure"): the
    /// work assignment then follows the matter out of secure in the same call (owned by its business unit's team, its own
    /// shares revoked).
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_EndsWhatItPassedOn_AndTheFiledRecordFollowsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);

        (await UnsecureRouteAsync("matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.IsSecureOf(workAssignment).Should().BeFalse("round 84: it follows the matter out of secure");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "round 39: the access came only from the matter's share");
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Revoked);
        row.Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// The L4 job also reconciles the provenance of a record re-filed AWAY from its secure parent (re-filing is not an
    /// unshare, so it keeps what the parent passed on): when the parent's share ends outside the BFF, the next run ends the
    /// unmodified inherited share — though the record is no longer listed under that parent.
    /// </summary>
    [Fact]
    public async Task TheJob_EndsAnInheritedShareOnARecordReFiledAwayFromTheParent_WhenTheParentUnsharesOutsideTheBff()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null); // re-filed under nothing
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "re-filing is not an unshare");

        _fixture.RemoveShare(matter, DataversePrincipalRef.User(Colleague));
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "its access there came from the matter");
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("a record left with no parent keeps its values (round 84)");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// ADR-003 on the unshare fan-out: whether the matter is still secure cannot be read when its sharee is removed — the
    /// inherited share stays and the unshare reports it (children_incomplete, <c>filedRecordsNotUpdated</c>), never "done".
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenTheMatterCannotBeReadForTheFanOut_ReportsTheFiledRecordAsNotUpdated()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        World.FailingRowReadsOf("sprk_matter", matter);

        var (status, code, body) = Problem(await UnshareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1, "the work assignment's inherited share was not ended");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "nothing is ended on an unread answer");
    }

    /// <summary>
    /// The intersection rule on the way back (owner round 11 item 4, task 149's "a principal its known roots do not share is
    /// revoked"): the work assignment is ALSO filed under a project flagged secure but not isolated (its mirror cannot be
    /// trusted) — the matter, which was read, no longer shares the person, so the intersection does not carry them: the
    /// inherited share is removed.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_RemovesTheShare_ThoughAnotherParentCannotBeTrusted_BecauseTheMatterNoLongerCarriesIt()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.SeedProject(project, isSecure: true); // flagged, owned by a user: not isolated
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// The intersection rule on the way back, undecided: the work assignment was re-filed under a secure project that DOES
    /// share the person and under one flagged secure but not isolated — every parent that was read still carries them, one
    /// cannot be trusted: the share stays and the matter's unshare reports it (children_incomplete), for the job to finish.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_HoldsTheShare_WhenEveryReadParentCarriesItButAnotherCannotBeTrusted()
    {
        var (matter, project, untrusted, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureProject(_fixture, project, Colleague);
        _fixture.SeedProject(untrusted, isSecure: true); // flagged, owned by a user: not isolated
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));
        FilePair(_fixture, "sprk_workassignment", workAssignment, untrusted, RecordTypeRef(_fixture, "sprk_project"));

        var (status, code, body) = Problem(await UnshareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "held: never removed on an undecided intersection");
    }

    /// <summary>
    /// ADR-003 in the job's reconcile of records no longer filed under their source: whether the parent still shares what
    /// it passed on cannot be read — the run is not a success, naming it, and nothing is ended.
    /// </summary>
    [Fact]
    public async Task TheJob_WhenAParentOfAReFiledRecordCannotBeRead_FailsTheRun_AndEndsNothing()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.FailingRowReadsOf("sprk_matter", matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain("no longer filed under their source");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
    }

    /// <summary>
    /// The mirror's No Access guard on a filed ROOT (task 149's rule): the matter's sharee is on the work assignment's own No
    /// Access list — never given it, and nothing is recorded as passed on.
    /// </summary>
    [Fact]
    public async Task TheJob_NeverPassesOnASharee_WhoIsOnTheFiledRecordsNoAccessList()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, workAssignment);

        await _job.RunAsync();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "walled off the filed record");
        Provenance(workAssignment).Should().NotContain(r => r.SystemUserId == Colleague && r.State == AssignedAccessState.Shared);
    }

    /// <summary>
    /// A share on an ORDINARY matter passes nothing on, and reads nothing of what is filed under it (the pass stops at the
    /// matter's flag) — the share route's cost is the share.
    /// </summary>
    [Fact]
    public async Task SharingOnAnOrdinaryMatter_ReadsNothingFiledUnderIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.QueriedTables.Clear();

        await ShareAsync("matter", matter, Colleague);

        World.QueriedTables.Should().NotContain("sprk_workassignment", "nothing filed under an ordinary matter is read");
        _fixture.SharesOn(workAssignment).Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 17 item 3 on the share route: a share on a matter whose flag is EMPTY cannot decide what is owed to the
    /// records filed under it — reported (children_incomplete), never read as "not secure, nothing to pass on".
    /// </summary>
    [Fact]
    public async Task SharingOnAMatterWhoseFlagIsEmpty_ReportsTheFiledRecordsAsNotUpdated()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: false);
        World.Set("sprk_matter", matter, "sprk_issecure", null);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);

        var (status, code, body) = Problem(await ShareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().BeGreaterThan(0);
        _fixture.SharesOn(workAssignment).Should().BeEmpty("nothing is written on an undecided answer");
    }

    // ══ The job — resumable, and blind to nothing ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The verifier's LOW: the per-run bound never starves the records behind it. The first
    /// <see cref="SecureRootInheritanceJob.MaxProvisioningsPerRun"/> records (in the job's order) are refused on every run;
    /// the one behind them is deferred — the run is not a success — and the NEXT run continues from the cursor and secures it.
    /// </summary>
    [Fact]
    public async Task TheJob_ContinuesFromItsCursor_SoRecordsBehindOnesThatKeepFailingAreReached()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var refused = Enumerable.Range(1, SecureRootInheritanceJob.MaxProvisioningsPerRun)
            .Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToList();
        foreach (var id in refused)
            FiledWorkAssignment(_fixture, id, "sprk_regardingmatter", "sprk_matter", matter, createdBy: AppUser); // nobody to secure it for
        _fixture.CreatorPersonColumnExists = true;
        var behind = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        FiledWorkAssignment(_fixture, behind, "sprk_regardingmatter", "sprk_matter", matter);

        var first = await _job.RunAsync();

        first.Success.Should().BeFalse();
        _fixture.IsSecureOf(behind).Should().BeFalse("deferred past the bound");
        using (var doc = JsonDocument.Parse(first.ResultJson!))
            doc.RootElement.GetProperty("deferred").GetInt32().Should().Be(1);

        var second = await _job.RunAsync();

        ShouldBeSecure(behind, "the second run continues after the last record the first reached");
        second.Success.Should().BeFalse("the refused records are still reported");
    }

    /// <summary>
    /// Owner round 17 item 3: a record filed only under a matter whose flag is EMPTY is never outside the rule — the job
    /// lists that parent too, finds the record, writes nothing to it, and reports it (the run is not a success).
    /// </summary>
    [Fact]
    public async Task TheJob_ReportsARecordFiledUnderAParentWhoseFlagIsEmpty()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: false);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.Set("sprk_matter", matter, "sprk_issecure", null);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(SecureRootInheritance.ReasonParentUnverifiable);
        using (var doc = JsonDocument.Parse(run.ResultJson!))
            doc.RootElement.GetProperty("emptyFlagParents").GetInt32().Should().Be(1);
        ShouldBeUntouched(workAssignment, "an empty flag is never a reason to write");
    }

    /// <summary>
    /// The verifier's LOW: the pair listing finds EVERY spelling the decision side accepts (<c>Guid.TryParse</c> of the
    /// trimmed text) — no hyphens, parentheses, the hex-tuple format, padding, upper case — so a project filed under a
    /// secure matter by such a pair is secured by the job.
    /// </summary>
    [Theory]
    [InlineData("N")]
    [InlineData("P")]
    [InlineData("X")]
    [InlineData("padded")]
    [InlineData("upper")]
    public async Task TheJob_FindsAPairInEverySpellingTheDecisionAccepts(string spelling)
    {
        var (matter, project) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        World.Set("sprk_project", project, "sprk_regardingrecordid", spelling switch
        {
            "padded" => $"  {matter:D}  ",
            "upper" => matter.ToString("D").ToUpperInvariant(),
            _ => matter.ToString(spelling),
        });

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(project, $"its pair names the matter in the {spelling} spelling");
    }

    /// <summary>
    /// Verifier seed A2: a pair naming a secure matter whose TYPE cannot be read is an UNCONFIRMED candidate the job must
    /// still decide — reported unverifiable (the run is not a success), nothing written — and the matter's provisioning
    /// names it as not done.
    /// </summary>
    [Fact]
    public async Task AFiledRecordWhosePairTypeCannotBeRead_IsReportedByTheJobAndTheTransition_AndNothingIsWritten()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, isSecure: true);
        _fixture.SeedWorkAssignment(workAssignment, isSecure: false);
        var unreadableType = RecordTypeRef(_fixture, "sprk_matter");
        FilePair(_fixture, "sprk_workassignment", workAssignment, matter, unreadableType);
        World.FailingRowReadsOf("sprk_recordtype_ref", unreadableType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var outstanding = doc.RootElement.GetProperty("filedRecords").EnumerateArray().Single();
            outstanding.GetProperty("recordId").GetGuid().Should().Be(workAssignment);
            outstanding.GetProperty("outcome").GetString().Should().Be("unverifiable");
        }

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(SecureRootInheritance.ReasonParentUnverifiable);
        ShouldBeUntouched(workAssignment, "an unreadable pair type is never 'not filed under', and never a reason to write");
    }

    /// <summary>
    /// Verifier seed A6: a filed record that is flagged and owned by the named team but has NO container (a provisioning
    /// that stopped, or a create into isolation whose container step failed) is NOT "already secure" — the job completes
    /// it through the re-entry branch (its own container, its creator shared).
    /// </summary>
    [Fact]
    public async Task TheJob_CompletesAFiledRecordThatIsTeamOwnedAndFlaggedButHasNoContainer()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter));

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        ShouldBeSecure(workAssignment, "completed through the re-entry branch");
    }

    // ══ Task 158 r1c-v1 — write-ahead provenance (verifier item 1) and the reverse rule's remaining gaps (items 3, 4) ═══


    /// <summary>
    /// Verifier item 1 (CONFIRMED by its probe; this is the probe, kept): the provenance cannot be written. Before r1c-v1 the
    /// share was written first, so the retry found it with no row and recorded it as DIRECT (covered-by-existing) — the run
    /// said success, and the matter's unshare never removed it (round 30 broken). The row is now written BEFORE the share: a
    /// row that cannot be written writes no share (the run fails); the next run records and writes it; the unshare removes it.
    /// </summary>
    [Fact]
    public async Task AProvenanceWriteFault_NeverLeavesAShareThatLooksDirect_SoTheParentsUnshareStillRemovesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.InheritedLedger.FailLedgerWrites = true;

        (await _job.RunAsync()).Success.Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "no share is written whose origin could not be recorded");

        _fixture.InheritedLedger.FailLedgerWrites = false;
        var retry = await _job.RunAsync();

        retry.Success.Should().BeTrue(retry.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared, "passed on by the matter — never recorded as direct access");
        row.Reason.Should().BeNull("confirmed: the share read back");

        (await UnshareAsync("matter", matter, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "round 30: removed from the filed record with the parent");
    }

    /// <summary>
    /// Write-ahead, the other fault: the row was recorded and the share written, but CONFIRMING the row failed. The run is not
    /// a success; the share stays on record as passed on (unconfirmed), so the next run confirms it from the share in place,
    /// and the matter's unshare removes it.
    /// </summary>
    [Fact]
    public async Task AConfirmationFault_LeavesTheShareOnRecordAsPassedOn_SoTheNextRunConfirmsIt_AndTheUnshareRemovesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.InheritedLedger.FailLedgerUpdates = true;

        (await _job.RunAsync()).Success.Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "its row was recorded first, so the share was written");
        var unconfirmed = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        unconfirmed.State.Should().Be(AssignedAccessState.Shared);
        unconfirmed.Reason.Should().Be(AssignedAccessReason.SharePending);
        unconfirmed.GrantedLevel.Should().Be(Mirror);

        _fixture.InheritedLedger.FailLedgerUpdates = false;
        (await _job.RunAsync()).Success.Should().BeTrue();
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().BeNull("confirmed by the share in place");

        await UnshareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// Write-ahead: an unconfirmed row whose share IS in place is still the matter's share — the matter's unshare removes it
    /// even before any pass confirmed it.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_RemovesAShareStillUnconfirmedOnRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.InheritedLedger.FailLedgerUpdates = true;
        await _job.RunAsync();
        _fixture.InheritedLedger.FailLedgerUpdates = false;

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// Write-ahead: the row was recorded but its share never landed (the share write failed). The next run writes it again —
    /// it is never read as an operator's removal (Declined), which would withhold the matter's sharee for good.
    /// </summary>
    [Fact]
    public async Task ARecordedShareThatNeverLanded_IsWrittenAgainByTheNextRun_NeverReadAsARemoval()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.FailShareForPrincipal = Colleague;

        (await _job.RunAsync()).Success.Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.SharePending);

        _fixture.FailShareForPrincipal = null;
        var retry = await _job.RunAsync();

        retry.Success.Should().BeTrue(retry.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared);
        row.Reason.Should().BeNull();
    }

    /// <summary>
    /// Write-ahead: the matter unshares the colleague while their recorded share never landed — the row ends with nothing
    /// removed (assignment-ended: nothing of the rule's is on the record), never "kept as modified".
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_EndsARecordedShareThatNeverLanded_RemovingNothing()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.FailShareForPrincipal = Colleague;
        await _job.RunAsync();
        _fixture.FailShareForPrincipal = null;

        (await UnshareAsync("matter", matter, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Revoked);
        row.Reason.Should().Be(AssignedAccessReason.AssignmentEnded);
    }

    /// <summary>
    /// Verifier item 1, the race: a concurrent pass (the <c>/share-user</c> pass-on against the job) records the same
    /// (record, matter, colleague) between this pass's read and its write. The alternate key settles it: this pass writes
    /// NOTHING over that row and writes no share on its stale decision; the next pass decides on the row as it stands.
    /// </summary>
    [Fact]
    public async Task ARowAConcurrentPassRecordsFirst_IsNeverOverwritten_AndNoShareIsWrittenOnTheStaleDecision()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        AssignedAccessLedgerRow? concurrent = null;
        _fixture.InheritedLedger.BeforeInheritedCreate = key =>
        {
            if (concurrent is null && key.Contains(Colleague.ToString("D"), StringComparison.Ordinal))
            {
                concurrent = _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_matter", matter,
                    DataversePrincipalRef.User(Colleague),
                    new AssignedAccessLedgerWrite(AssignedAccessState.Shared, AssignedAccessReason.SharePending, GrantedLevel: Mirror));
            }
        };

        var first = await _job.RunAsync();

        first.Success.Should().BeFalse("its record lost the race: decided again on the next pass");
        _fixture.InheritedLedger.InheritedCreateConflicts.Should().ContainSingle();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "no share on a decision made without seeing that row");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.SharePending,
            "the concurrent pass's row is never overwritten");

        _fixture.InheritedLedger.BeforeInheritedCreate = null;
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        await UnshareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// Verifier item 1, the race as the verifier traced it: this pass found the colleague's share in place with no row and
    /// would record it as DIRECT — but a concurrent pass's row for that share lands just before. The covered-by-existing record
    /// loses to the key and writes nothing: the share stays recorded as passed on, so the matter's unshare removes it.
    /// </summary>
    [Fact]
    public async Task ADirectAccessRecordThatLosesTheRace_NeverOverwritesTheShareAConcurrentPassRecordedAsPassedOn()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Colleague), RecordShareLevels.RightsCsvForMask(Mirror));
        _fixture.InheritedLedger.BeforeInheritedCreate = key =>
        {
            if (key.Contains(Colleague.ToString("D"), StringComparison.Ordinal) && !Provenance(workAssignment).Any(r => r.SystemUserId == Colleague))
            {
                _fixture.InheritedLedger.SeedInheritedRow(ExternalGrantRootType.WorkAssignment, workAssignment, "sprk_matter", matter,
                    DataversePrincipalRef.User(Colleague), new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: Mirror));
            }
        };

        (await _job.RunAsync()).Success.Should().BeFalse("its record lost the race: reported, decided on the next pass");

        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared, "never downgraded to covered-by-existing (direct)");
        _fixture.InheritedLedger.BeforeInheritedCreate = null;
        await UnshareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "round 30: still removed with the matter's share");
    }

    /// <summary>
    /// S5 on the reverse rule, made complete: the colleague's inherited share is kept as the record's LAST reader — but it is
    /// still the share the matter passed on, so its row stays live. Once someone else can open the record, the next run removes
    /// it (an ended row would have left it for good).
    /// </summary>
    [Fact]
    public async Task AShareKeptAsTheLastReader_IsRemovedOnceSomeoneElseCanOpenTheRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Creator));
        await UnshareAsync("matter", matter, Colleague);
        var kept = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        kept.State.Should().Be(AssignedAccessState.Shared, "still the share the matter passed on");
        kept.Reason.Should().Be(AssignedAccessReason.KeptLastReader);

        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "someone else can open it now");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    /// <summary>
    /// The misclassification on the S5 path (the same defect as verifier item 1): a share kept as the last reader, then shared
    /// on the matter again, is recorded as passed on — never as direct access — so the matter's next unshare removes it.
    /// </summary>
    [Fact]
    public async Task AShareKeptAsTheLastReader_ThatTheMatterSharesAgain_IsStillPassedOn_NeverRecordedAsDirect()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync("matter", matter, Colleague);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Creator));
        await UnshareAsync("matter", matter, Colleague);

        await ShareAsync("matter", matter, Colleague);
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared);
        row.Reason.Should().BeNull("an ordinary inherited share again");

        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        await UnshareAsync("matter", matter, Colleague);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// Write-ahead over an operator's own share: the colleague was shared on the filed record by an operator (Adopted) at View
    /// Only, and the matter later passes a HIGHER level on. The raise is the rule's, so it is recorded - the operator's level
    /// as the level it raised from - and the matter's unshare takes back only the raise, leaving the operator's share as the
    /// operator set it (before r1c-v1 the raise went unrecorded and was kept for good).
    /// </summary>
    [Fact]
    public async Task ARaiseOverAnOperatorsOwnShare_IsRecorded_SoTheParentsUnshareTakesBackOnlyTheRaise()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        _fixture.SeedShare(matter, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        (await ShareAsync("workassignment", workAssignment, Colleague, ExternalAccessLevel.ViewOnly))
            .Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).State.Should().Be(AssignedAccessState.Adopted);
        var operators = _fixture.ShareMaskOf(workAssignment, Colleague);

        _fixture.SeedShare(matter, DataversePrincipalRef.User(Colleague), ProvisionProjectEndpoint.CollaboratorAccessRights);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(operators | Mirror, "raised to what the matter passes on");

        await UnshareAsync("matter", matter, Colleague);

        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(operators, "only the rule's raise is taken back");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    /// <summary>
    /// Verifier item 3 (the mask comparison in the reverse rule): the work assignment was re-filed from the matter to a secure
    /// project that shares the colleague only at View Only. The matter's unshare must not count the project as justifying the
    /// whole share — it carries LESS than the matter passed on: the matter's share is taken back, and the project's View Only
    /// is given back in the same call, so the colleague holds exactly what the project passes on.
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_LeavesExactlyWhatAnotherParentPassesOnAtALowerLevel()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project);
        _fixture.SeedShare(project, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "re-filing is not an unshare");

        var result = await UnshareAsync("matter", matter, Colleague);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        var viewOnly = RecordShareLevels.ChildMirrorMask(Mask(RecordShareLevels.ViewOnlyRights));
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(viewOnly,
            "never kept at the matter's level by a parent that carries less; the project's own level, at once");
        Provenance(workAssignment).Single(r => r.SystemUserId == Colleague
                && r.SourceField == AssignedAccessStore.InheritedSourceField("sprk_matter", matter))
            .Reason.Should().Be(AssignedAccessReason.AccessRemoved);
        var fromProject = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague
            && r.SourceField == AssignedAccessStore.InheritedSourceField("sprk_project", project));
        fromProject.State.Should().Be(AssignedAccessState.Shared);
        fromProject.GrantedLevel.Should().Be(viewOnly);
    }

    /// <summary>The work assignment of the item-3 tests: passed the colleague by the matter, then re-filed under a project that shares them at View Only.</summary>
    private async Task<(Guid Matter, Guid Project, Guid WorkAssignment)> ReFiledUnderAProjectSharingLessAsync()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project);
        _fixture.SeedShare(project, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));
        return (matter, project, workAssignment);
    }

    /// <summary>
    /// Verifier item 3, the give-back that fails: the matter's share is taken back, but what the project still passes on
    /// cannot be given back (the write fails) - reported through children_incomplete (filedRecordsNotUpdated), never "done".
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt()
    {
        var (matter, _, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        _fixture.FailShareForPrincipal = Colleague;

        var result = await UnshareAsync("matter", matter, Colleague);
        var (status, code, body) = Problem(result);

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        // Task 158 r1c-v2 (the verifier's seed X15): the copy says what happened — access removed, the rest given back later.
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should()
            .StartWith("This user's access to the record was removed.")
            .And.Contain("could not be given back yet; it is given back automatically within a few minutes.");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "the matter's share was taken back; the project's could not be given");

        _fixture.FailShareForPrincipal = null;
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(RecordShareLevels.ChildMirrorMask(Mask(RecordShareLevels.ViewOnlyRights)),
            "the job gives it back");
    }

    /// <summary>
    /// Verifier item 3, the give-back that cannot be decided: the work assignment is ALSO filed under a project flagged secure
    /// but not isolated (its mirror cannot be trusted), so what the remaining parents pass on cannot be given back yet -
    /// reported, never "done".
    /// </summary>
    [Fact]
    public async Task UnsharingFromTheMatter_WhenWhatTheRemainingParentsPassOnCannotBeDecided_ReportsIt()
    {
        var (matter, _, workAssignment) = await ReFiledUnderAProjectSharingLessAsync();
        var untrusted = Guid.NewGuid();
        _fixture.SeedProject(untrusted, isSecure: true); // flagged, owned by a user: not isolated
        FilePair(_fixture, "sprk_workassignment", workAssignment, untrusted, RecordTypeRef(_fixture, "sprk_project"));

        var (status, code, body) = Problem(await UnshareAsync("matter", matter, Colleague));

        status.Should().Be(500);
        code.Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        body.GetProperty("filedRecordsNotUpdated").GetInt32().Should().Be(1);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    /// <summary>
    /// Verifier item 4 (the decline set in the job's step (b)): an operator removed the colleague on the work assignment while
    /// it was under the matter (Declined); it was then re-filed under a secure project that also shares the colleague — still
    /// not given, the matter's share persisting. The matter's share then ends outside the BFF: ONE run ends the decline and,
    /// in the same pass, gives the colleague the project's share.
    /// </summary>
    [Fact]
    public async Task TheJob_EndsADeclineFromAParentTheRecordWasReFiledAwayFrom_AndGivesTheNewParentsShareInTheSameRun()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecureProject(_fixture, project, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        (await UnshareAsync("workassignment", workAssignment, Colleague)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", project));
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "declined while the matter's share persists");

        _fixture.RemoveShare(matter, DataversePrincipalRef.User(Colleague));
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror,
            "the decline ended with the matter's share, and the project's share is given in the same run");
    }

    /// <summary>
    /// New in r1c-v1 (task 142's criterion 9 applied to round 30): task 143's enforcer removes a WALLED colleague's inherited
    /// share from the filed record — a known cause, not an operator's removal. The row waits (Skipped,
    /// removed-by-no-access) instead of Declined, and once the wall is lifted the matter's share is passed on again.
    /// </summary>
    [Fact]
    public async Task AShareTheNoAccessEnforcerRemoved_IsPassedOnAgainOnceTheWallIsLifted_NeverReadAsAnOperatorsRemoval()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        var wall = _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, workAssignment);
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Colleague)); // task 143's enforcer

        (await _job.RunAsync()).Success.Should().BeTrue();
        var waiting = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        waiting.State.Should().Be(AssignedAccessState.Skipped);
        waiting.Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "walled");

        _fixture.NoAccessList.Lift(wall);
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror, "passed on again once the wall is lifted");
    }

    /// <summary>
    /// ADR-003 on that decision: the inherited share was removed while the colleague's No Access state cannot be checked —
    /// neither re-added nor recorded either way, and the run is not a success.
    /// </summary>
    [Fact]
    public async Task AnInheritedShareRemovedWhileTheNoAccessListCannotBeChecked_IsNeitherReAddedNorRecorded_AndTheRunFails()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.RemoveShare(workAssignment, DataversePrincipalRef.User(Colleague));
        _fixture.NoAccessList.FaultsWhenSubjectNames = Colleague;

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "nothing re-added on an undecided answer");
        var row = Provenance(workAssignment).Single(r => r.SystemUserId == Colleague);
        row.State.Should().Be(AssignedAccessState.Shared, "nothing recorded either way");
        row.Reason.Should().BeNull();
    }
}
