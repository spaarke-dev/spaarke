using System.Security.Claims;
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
/// unified-access-control-r2 task 114 (owner round 67) — the secure-root inheritance and a RESTRICTED filed record. A secure
/// matter's sharees reach the secure work assignment filed under it (task 158), through the ONE share-eligibility rule
/// (<c>InternalShareEndpoints.ClassifyEligibility</c>): a sharee flagged <c>sprk_isexternal = true</c> is never copied onto a
/// Restricted filed record; one the Restricted remover took away is recorded <c>Skipped(restricted)</c> — a known cause,
/// never Declined — and is passed on again once the record is not Restricted. Driven through the REAL share handler,
/// inheritance and synchronizer over the provisioning fixture's Dataverse. KEEP path: data-mutation.
/// </summary>
[Trait("status", "task-114-uac-r2")]
public class SecureRootInheritanceRestrictedTests : IClassFixture<ProvisionProjectTestFixture>
{
    private static readonly Guid External = Guid.Parse("b0000000-0000-0000-0000-000000114e01");
    private static readonly Guid Blank = Guid.Parse("b0000000-0000-0000-0000-000000114b01");

    private readonly ProvisionProjectTestFixture _fixture;
    private readonly InternalUserShareTests.FakeSystemUsers _users = new();

    public SecureRootInheritanceRestrictedTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _users.SeedPerson(Creator, "Creator");
        _users.SeedPerson(External, "External Erin", isExternal: true);
        _users.SeedPerson(Blank, "Blank Bo", isExternal: null);
        Person(External, isExternal: true);
        Person(Blank, isExternal: null);
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    private static int Mirror => RecordShareLevels.ChildMirrorMask(
        RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights));

    /// <summary>The systemuser row the inheritance's rule reads (SDK): an enabled person, the flag as given (null = blank).</summary>
    private void Person(Guid id, bool? isExternal)
    {
        if (!World.Has("systemuser", id))
            World.Add("systemuser", id);
        World.Set("systemuser", id, "isdisabled", false);
        World.Set("systemuser", id, "accessmode", new OptionSetValue(0));
        World.Set("systemuser", id, "sprk_isexternal", isExternal);
    }

    private void Restricted(Guid workAssignment, bool restricted) =>
        World.Set("sprk_workassignment", workAssignment, "sprk_accesspermission",
            new OptionSetValue(restricted ? ExternalParticipationService.AccessPermissionRestricted : 100000000));

    private void SecuredWorkAssignment(Guid id, Guid matter)
    {
        _fixture.SeedWorkAssignment(id, owningTeamId: SecureTeam, containerId: $"b!wa-{id:N}", isSecure: true);
        _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        World.Set("sprk_workassignment", id, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
    }

    private AssignedAccessLedgerRow? RowOf(Guid filed, Guid user) =>
        _fixture.InheritedLedger.InheritedRowsOf(filed).SingleOrDefault(r => r.SystemUserId == user);

    private static DefaultHttpContext Caller() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
            new Claim("oid", "66666666-6666-6666-6666-666666666666"),
        }, "test")),
        TraceIdentifier = "trace-114",
    };

    private async Task<IResult> ShareOnMatterAsync(Guid matter, Guid user)
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
            new AssignedAccessTestDoubles.Harness(_fixture.InheritedLedger).Materializer,
            Caller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private async Task<SecureRootInheritResult> PassAsync(Guid workAssignment)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .PassShareesToFiledRecordAsync("sprk_workassignment", workAssignment, "trace-114", CancellationToken.None);
    }

    /// <summary>
    /// The rule: a matter shared with a user flagged external (shared there — the matter is not Restricted) does not pass
    /// that share on to a RESTRICTED work assignment filed under it; a blank-flagged sharee is passed on.
    /// </summary>
    [Fact]
    public async Task ARestrictedFiledRecord_IsNeverGivenAParentSharee_FlaggedExternal_ButABlankFlaggedOneIsGiven()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        Restricted(workAssignment, true);

        (await ShareOnMatterAsync(matter, External)).Should().BeOfType<Ok<ShareRecordWithUserResponse>>(
            "the matter is not Restricted, so an external-flagged licensed user is shared with there (2026-09-18 ruling)");
        await ShareOnMatterAsync(matter, Blank);

        _fixture.ShareMaskOf(matter, External).Should().NotBe(0);
        _fixture.ShareMaskOf(workAssignment, External).Should().Be(0, "a Restricted record admits no user flagged external");
        RowOf(workAssignment, External).Should().BeNull("nothing was passed on, so nothing is on record as passed on");
        _fixture.ShareMaskOf(workAssignment, Blank).Should().Be(Mirror, "a blank sprk_isexternal is not external (round 67 item 3)");
    }

    /// <summary>
    /// The positive twin: the same external sharee IS passed on to a filed record that is not Restricted.
    /// </summary>
    [Fact]
    public async Task AFiledRecordThatIsNotRestricted_IsGivenTheParentsExternalFlaggedSharee()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        Restricted(workAssignment, false);

        await ShareOnMatterAsync(matter, External);

        _fixture.ShareMaskOf(workAssignment, External).Should().Be(Mirror);
        RowOf(workAssignment, External)!.State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>
    /// Becoming Restricted: the inherited share the Restricted remover takes away is recorded Skipped(restricted) — a known
    /// cause, never Declined — and once the record is no longer Restricted the next pass passes it on again.
    /// </summary>
    [Fact]
    public async Task AnInheritedShareRemovedBecauseTheRecordBecameRestricted_IsSkippedRestricted_AndGivenBackAfter()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        await ShareOnMatterAsync(matter, External);
        _fixture.ShareMaskOf(workAssignment, External).Should().Be(Mirror);

        // The record becomes Restricted; the Restricted remover takes the external user's share (its revoke, here).
        Restricted(workAssignment, true);
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDataverseRecordShareService>().RevokeAccessAsync(
                "sprk_workassignments", workAssignment, DataversePrincipalRef.User(External), CancellationToken.None);
        }

        await PassAsync(workAssignment);

        var row = RowOf(workAssignment, External)!;
        row.State.Should().Be(AssignedAccessState.Skipped, "a known cause — never an operator's removal (Declined)");
        row.Reason.Should().Be(AssignedAccessReason.Restricted);
        _fixture.ShareMaskOf(workAssignment, External).Should().Be(0, "and nothing is given back while it is Restricted");

        Restricted(workAssignment, false);
        await PassAsync(workAssignment);

        _fixture.ShareMaskOf(workAssignment, External).Should().Be(Mirror, "passed on again once the record is not Restricted");
        RowOf(workAssignment, External)!.State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>
    /// Fail closed (ADR-003): on a Restricted filed record, while who among the parent's sharees is flagged external cannot
    /// be read, nobody is passed on — not even a blank-flagged sharee; once it can be read, the next pass gives them.
    /// </summary>
    [Fact]
    public async Task WhileTheShareesFlagsCannotBeRead_NothingIsPassedOnToARestrictedFiledRecord_AndTheNextPassGivesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecuredWorkAssignment(workAssignment, matter);
        Restricted(workAssignment, true);

        World.FailingQueriesOf("systemuser");
        try
        {
            await ShareOnMatterAsync(matter, Blank);
            _fixture.ShareMaskOf(workAssignment, Blank).Should().Be(0, "the rule could not be applied, so nothing was given");
        }
        finally
        {
            World.ClearQueryFaults();
        }

        await PassAsync(workAssignment);

        _fixture.ShareMaskOf(workAssignment, Blank).Should().Be(Mirror);
    }
}
