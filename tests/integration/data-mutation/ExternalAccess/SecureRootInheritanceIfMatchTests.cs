using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
/// Batch-4 integration, 158 × 140 (round 47 item 2, closing E-158-v1-2). Every ledger update an inheritance pass makes sends
/// <c>If-Match</c> with the version of the read it decided on, through task 140's client support. When another write lands
/// between that read and the pass's write (here, an operator's Declined marker), the pass's write is refused (412). The row
/// is read again, and because it no longer holds what the pass decided on, nothing is written over it. The next pass
/// decides on it as it is. Driven through the REAL inheritance, share routes' handlers, synchronizer and job over the
/// provisioning fixture. The fake ledger moves a row's version on every write, as Dataverse does.
/// </summary>
[Trait("status", "task-158-uac-r2")]
public class SecureRootInheritanceIfMatchTests : IClassFixture<ProvisionProjectTestFixture>
{
    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootInheritanceJobRunner _job;
    private readonly InternalUserShareTests.FakeSystemUsers _users = new();

    public SecureRootInheritanceIfMatchTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _job = new SecureRootInheritanceJobRunner(_fixture);
        _users.SeedPerson(Creator, "Creator");
        _users.SeedPerson(Colleague, "Colleague");
    }

    private AssignedAccessTestDoubles.FakeAssignedAccessStore Ledger => _fixture.InheritedLedger;

    private static int Mirror => RecordShareLevels.ChildMirrorMask(
        RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights));

    private static readonly AssignedAccessLedgerWrite OperatorsDeclinedMarker =
        new(AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand);

    private void SecuredWorkAssignment(Guid id, Guid matter)
    {
        _fixture.SeedWorkAssignment(id, owningTeamId: SecureTeam, containerId: $"b!wa-{id:N}", isSecure: true);
        _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        _fixture.ChildWorld.Set("sprk_workassignment", id, "sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter));
    }

    private AssignedAccessLedgerRow ColleaguesRow(Guid filed) =>
        Ledger.InheritedRowsOf(filed).Single(r => r.SystemUserId == Colleague);

    /// <summary>
    /// The operator's marker lands once, on the colleague's row, just before the pass's FIRST conditional update of it. It
    /// stands for an operator's <c>/unshare-user</c> on the filed record racing the pass.
    /// </summary>
    private void TheOperatorsMarkerLandsBeforeThePassesWrite(Guid filed)
    {
        var landed = false;
        Ledger.BeforeConditionalUpdate = rowId =>
        {
            if (landed || Ledger.InheritedRowsOf(filed).SingleOrDefault(r => r.SystemUserId == Colleague) is not { } row
                || row.Id != rowId)
            {
                return;
            }

            landed = true;
            Ledger.WriteConcurrently(rowId, OperatorsDeclinedMarker);
        };
    }

    private static DefaultHttpContext Caller() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
            new Claim("oid", "66666666-6666-6666-6666-666666666666"),
        }, "test")),
        TraceIdentifier = "trace-158x140",
    };

    private async Task<IResult> ShareAsync(Guid matter, Guid user)
    {
        using var scope = _fixture.Services.CreateScope();
        return await InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", matter, user, ExternalAccessLevel.Collaborate),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), _users.Client, _fixture.NoAccessReads,
            new Mock<ITenantCache>().Object,
            new InternalUserShareTests.StubCallerRightsProbe(
                AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
                | AccessRights.Share),
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(), SecureChildShareWorld.NobodyWalled(),
            scope.ServiceProvider.GetRequiredService<SecureRootInheritance>(),
            new AssignedAccessTestDoubles.Harness(Ledger).Materializer,
            Caller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private async Task<IResult> UnshareAsync(Guid matter, Guid user)
    {
        using var scope = _fixture.Services.CreateScope();
        return await InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("matter", matter, user),
            scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>(), _users.Client, _fixture.NoAccessReads,
            new Mock<ITenantCache>().Object,
            new AssignedAccessTestDoubles.Harness(Ledger).Materializer,
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            scope.ServiceProvider.GetRequiredService<SecureRootInheritance>(),
            Caller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    [Fact(DisplayName = "158×140: an operator's Declined marker landing before the write-ahead update refuses that update (412), and NO share is written")]
    public async Task TheWriteAheadUpdate_WhenTheOperatorsMarkerLandsFirst_IsRefused_AndNoShareIsWritten()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync(matter, Colleague);
        await UnshareAsync(matter, Colleague); // the row is ended (Revoked); a new share on the matter updates it ahead of the share
        ColleaguesRow(workAssignment).State.Should().Be(AssignedAccessState.Revoked);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
        TheOperatorsMarkerLandsBeforeThePassesWrite(workAssignment);

        await ShareAsync(matter, Colleague);

        Ledger.PreconditionFailures.Should().Contain(f => f.RowId == ColleaguesRow(workAssignment).Id,
            "the write-ahead update was sent with the version the pass read, which the marker moved on");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0, "a share whose record could not be written first is never written");
        ColleaguesRow(workAssignment).State.Should().Be(AssignedAccessState.Declined, "the operator's marker is never written over");
    }

    [Fact(DisplayName = "158×140: an operator's Declined marker landing before the confirmation refuses it, and the marker stands")]
    public async Task TheConfirmation_WhenTheOperatorsMarkerLandsFirst_IsRefused_AndTheMarkerStands()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        // The first share CREATES the row ahead of the share (no update), so the first conditional update is the confirmation.
        TheOperatorsMarkerLandsBeforeThePassesWrite(workAssignment);

        await ShareAsync(matter, Colleague);

        Ledger.PreconditionFailures.Should().ContainSingle(f => f.RowId == ColleaguesRow(workAssignment).Id);
        var row = ColleaguesRow(workAssignment);
        row.State.Should().Be(AssignedAccessState.Declined, "the confirmation is never written over the operator's marker");
        row.Reason.Should().Be(AssignedAccessReason.RemovedOutOfBand);
    }

    [Fact(DisplayName = "158×140: the reverse rule's end refuses to write over a marker that landed first; the next pass ends the row as it is")]
    public async Task TheReverseRulesEnd_WhenTheOperatorsMarkerLandsFirst_IsRefused_AndTheNextPassDecidesOnTheRowAsItIs()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareAsync(matter, Colleague);
        ColleaguesRow(workAssignment).State.Should().Be(AssignedAccessState.Shared);
        TheOperatorsMarkerLandsBeforeThePassesWrite(workAssignment);

        await UnshareAsync(matter, Colleague);

        Ledger.PreconditionFailures.Should().Contain(f => f.RowId == ColleaguesRow(workAssignment).Id);
        ColleaguesRow(workAssignment).State.Should().Be(AssignedAccessState.Declined,
            "the end decided on a Shared row is not written over the Declined row that replaced it");

        Ledger.BeforeConditionalUpdate = null;
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        var ended = ColleaguesRow(workAssignment);
        ended.State.Should().Be(AssignedAccessState.Revoked, "decided again on the row as it is: a decline ends with its parent's share");
        ended.Reason.Should().Be(AssignedAccessReason.AssignmentEnded);
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(0);
    }

    [Fact(DisplayName = "158×140: a write whose row changed in a way that does not alter the decision is sent again at the fresh version")]
    public async Task AWrite_WhenTheRowsVersionMovedButItsFactsDidNot_IsSentAgainAtTheFreshVersion()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        var touched = false;
        Ledger.BeforeConditionalUpdate = rowId =>
        {
            if (touched)
                return;
            touched = true;
            var row = Ledger.InheritedRowsOf(workAssignment).Single(r => r.Id == rowId);
            Ledger.WriteConcurrently(rowId, new AssignedAccessLedgerWrite(row.State, row.Reason)); // same facts, new version
        };

        await ShareAsync(matter, Colleague);

        Ledger.PreconditionFailures.Should().ContainSingle("the first send used the version the pass read");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(Mirror);
        var confirmed = ColleaguesRow(workAssignment);
        confirmed.State.Should().Be(AssignedAccessState.Shared);
        (confirmed.Reason ?? string.Empty).Should().NotStartWith(AssignedAccessReason.SharePending, "confirmed at the fresh version");
    }

    [Fact(DisplayName = "158×140: an unsecure ends a row that changed since it was read, deciding again on the row as it is")]
    public async Task TheUnsecure_WhenARowChangedSinceItWasRead_EndsItAtItsNewVersion()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, null, Colleague);
        SecuredWorkAssignment(workAssignment, matter);
        (await _job.RunAsync()).Success.Should().BeTrue();
        _fixture.ChildWorld.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null); // re-filed away (xiv)
        TheOperatorsMarkerLandsBeforeThePassesWrite(workAssignment);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/v1/external-access/unsecure-project", new { recordType = "workassignment", recordId = workAssignment });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Ledger.PreconditionFailures.Should().ContainSingle();
        var ended = ColleaguesRow(workAssignment);
        ended.State.Should().Be(AssignedAccessState.Revoked, "an unsecure ends every live row, whatever state it holds");
        ended.Reason.Should().Be(AssignedAccessReason.RecordUnsecured);
    }
}
