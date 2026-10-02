using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 061 — the share plane on <c>/provision-project</c>, and the reverse path on
/// <c>/unsecure-project</c>.
/// </summary>
/// <remarks>
/// <para><b>The defect these pin.</b> Task 021 assigned secure projects to a memberless Secure Record business
/// unit owner team (the BU's default team then; its NAMED owner team since task 144) — correct isolation. It issued
/// no shares. design.md
/// §5.1 says <i>"All human access is by explicit Dataverse share, including the creating attorney's"</i>,
/// so provisioning completed and left a record **no human could open**: isolated, and unreachable. The
/// assertions below are about that round trip — a secure project must end up reachable by exactly the
/// person who created it, and by nobody else.</para>
///
/// <para><b>Why the creator's share is fatal and a colleague's is not.</b> The creator's share is the
/// only thing standing between "provisioned" and "locked box", so failing to issue it must fail the
/// provision. A named colleague can be added afterwards through the Manage Access surface, so failing
/// one of those must NOT throw away a provision that otherwise succeeded.</para>
/// </remarks>
public class SecureProjectShareTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";
    private const string ProjectEntitySet = "sprk_projects";

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureProjectShareTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    private static async Task<string> DetailOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Provisioning: the creator's share is what makes the project reachable
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provisioning_SharesTheProjectBackToItsCreator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var creatorShare = _fixture.Grants.Should().ContainSingle(
            g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "the creating attorney is the one human who must be able to open the project they just made")
            .Subject;

        creatorShare.EntitySet.Should().Be(ProjectEntitySet);
        creatorShare.RecordId.Should().Be(projectId);
        creatorShare.Principal.Kind.Should().Be(DataversePrincipalKind.SystemUser);
        creatorShare.AccessRightsCsv.Should().Be(ProvisionProjectEndpoint.CreatorAccessRights);
    }

    [Fact]
    public async Task Provisioning_GivesTheCreatorShareAccess_SoTheyCanAddColleaguesWithoutAnAdministrator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.Grants
            .Single(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId)
            .AccessRightsCsv.Should().Contain("ShareAccess",
                "FR-29's '+ User' picker is the creator re-sharing their own project; without ShareAccess "
                + "every addition would need an administrator");
    }

    [Fact]
    public async Task Provisioning_SharesToNamedPrincipals_WithoutShareAccess()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var colleagueShare = _fixture.Grants.Should()
            .ContainSingle(g => g.Principal.Id == colleague).Subject;

        colleagueShare.AccessRightsCsv.Should().Be(ProvisionProjectEndpoint.CollaboratorAccessRights);
        colleagueShare.AccessRightsCsv.Should().NotContain("ShareAccess",
            "re-sharing stays with the creator so the access list cannot widen through a chain nobody reviewed");
    }

    [Fact]
    public async Task Provisioning_SharesToNobodyElse()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.Grants.Select(g => g.Principal.Id).Should().BeEquivalentTo(
            new[] { ProvisionProjectTestFixture.CallerSystemUserId },
            "a secure project that provisioning quietly shared with anyone else would defeat its own point");
    }

    /// <summary>
    /// <b>Rewritten by task 133 (C11)</b>: the identity is now resolved BEFORE any mutation, so this refusal moves
    /// nothing — it used to run after the owner move, and leave the locked box its name says it prevents.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheCallersIdentityCannotBeEstablished_RefusesBeforeChangingAnything()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);

        _fixture.Updates.Should().BeEmpty("the creator is resolved before the owner move, so nothing moved");
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// <b>Rewritten by task 133 (C11)</b>: a creator-share failure used to leave the record owned by the memberless
    /// team with nobody shared, and say "retry" to a creator who could no longer pass the Write gate. Now the record's
    /// owner and the creator's share are back where they were — read back — and the same caller's next call succeeds.
    /// </summary>
    /// <remarks>
    /// The record is owned by an ordinary business-unit team here, so the share-first grant is the one that fails and
    /// the run stops before the move: no owner PATCH is sent at all.
    /// </remarks>
    [Fact]
    public async Task Provisioning_WhenTheCreatorsShareFailsBeforeTheMove_ChangesNothing_AndTheSameCallerCanRetry()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        (await DetailOf(response)).Should().Contain("BEFORE moving it").And.Contain("as it was before the call");
        _fixture.Updates.Should().BeEmpty("share-first stops the run before the owner move");
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.SharesOn(projectId).Should().BeEmpty();
        _fixture.IsSecureOf(projectId).Should().BeTrue("provisioning never clears the secure flag");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        _fixture.FailShareForPrincipal = null;
        var retry = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
    }

    /// <summary>
    /// The creator owns the record and every share to them fails. Share-first falls back to the post-move grant
    /// (a share to a record's current owner is live gate (a)); that fails too, so the move is UNDONE: owner read back
    /// as the creator, no share left behind, no container — and the same caller's next call succeeds.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheCreatorsShareFailsAfterTheMove_MovesTheRecordBack_AndTheSameCallerCanRetry()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId); // owned by the caller, as the wizard creates it
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        (await DetailOf(response)).Should().Contain("the move was undone").And.Contain("The same caller may retry");
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId,
            "compensation moves the record back to its pre-call owner, verified by read-back");
        _fixture.OwningTeamOf(projectId).Should().BeNull();
        _fixture.SharesOn(projectId).Should().BeEmpty("the pre-call share set was empty");
        _fixture.IsSecureOf(projectId).Should().BeTrue();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        _fixture.FailShareForPrincipal = null;
        var retry = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// Compensation when the pre-call owner is a TEAM, not the creator: the share-first grant succeeded, the post-move
    /// read-back of the shares then throws (treated as a failure, never as "share present"), so the move is undone AND
    /// the share this call issued is revoked — the creator ends holding nothing they did not hold before.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenThePostMoveShareReadThrows_UndoesTheMove_AndRevokesTheShareItIssued()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0,
            "a share-first grant must never outlive a compensated run — on a team-owned record it would hand the " +
            "creator ShareAccess they did not hold before");
        _fixture.Revokes.Should().ContainSingle(r => r.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The creator held a share with OTHER rights before the call. The share-first step sets it to exactly the
    /// creator rights; when the run is compensated it is put back to exactly what it was — the pre-call share SET, not
    /// merely "a share".
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenCompensated_RestoresTheCreatorsPreCallShareRights()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedShare(projectId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess");
        var before = _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.Modifies.Should().HaveCount(2, "set to the creator rights, then restored");
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(before);
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
    }

    /// <summary>
    /// The pre-call share set cannot be read: share-first is NOT attempted (it could not be undone exactly), and the
    /// creator's share is issued after the owner move instead.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenThePreCallSharesCannotBeRead_GrantsAfterTheMoveInstead()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailNextStrictShareReads = 1; // the pre-call read
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var ownerMove = _fixture.Updates.Single(u => u.Payload.ContainsKey("ownerid@odata.bind")).Sequence;
        _fixture.Grants.Should().ContainSingle()
            .Which.Sequence.Should().BeGreaterThan(ownerMove, "no grant may precede the owner move on this path");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The share AND the compensating move both fail: the record is owned by the memberless team without a creator
    /// share. A distinct reason code, a detail that names that state, never "nothing moved" and never "retry" (the
    /// creator no longer passes the Write gate), and a CRITICAL log line for the operator.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheShareAndTheCompensationBothFail_SaysSoWithItsOwnReasonCode()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailOwnerBindTo = businessUnitTeam;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        var detail = await DetailOf(response);
        detail.Should().Contain("without a confirmed creator share").And.Contain("calls provisioning again");
        detail.Should().NotContainEquivalentOf("nothing").And.NotContainEquivalentOf("retry");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains(projectId.ToString()));
    }

    /// <summary>
    /// Live gate (a) disproved: Dataverse refuses a share to the record's CURRENT owner. Provisioning still succeeds
    /// for a creator-owned record — the share is issued once the team owns it.
    /// </summary>
    /// <remarks>Beyond the stated contract: the design hedges the unproven platform behaviour the live gate decides.</remarks>
    [Fact]
    public async Task Provisioning_WhenAShareToTheCurrentOwnerIsRefused_GrantsAfterTheMove()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.GrantToCurrentOwnerRefused = true;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// Live gate (b) disproved: the reassignment drops the share-first grant. The post-move proof sees it missing and
    /// re-issues it; the run succeeds with the creator holding exactly the creator rights.
    /// </summary>
    /// <remarks>Beyond the stated contract: the design hedges the unproven platform behaviour the live gate decides.</remarks>
    [Fact]
    public async Task Provisioning_WhenTheMoveDropsTheShare_ReissuesIt()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.AssignDropsShareOf = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Where(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId).Should().HaveCount(2);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
    }

    /// <summary>
    /// The owner PATCH is refused and the read-back shows the record NOT moved: nothing moved, and the share-first grant
    /// is taken back so the creator holds exactly what they held before.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheOwnerMoveIsRefused_TakesTheShareFirstGrantBack()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailOwnerBindTo = ProvisionProjectTestFixture.SecureOwnerTeamId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentFailed);
        (await DetailOf(response)).Should().Contain("read back and is unchanged");
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SharesOn(projectId).Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// A row read without any owner cannot be moved safely — the move could not be undone — so it is refused before
    /// any change.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheRecordsOwnerCannotBeRead_RefusesBeforeChangingAnything()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningUserId: Guid.Empty);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordOwnerUnreadable);
        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
    }

    /// <summary>Named colleagues are shared only once the creator's share is proven — after the owner move.</summary>
    [Fact]
    public async Task Provisioning_SharesColleagues_OnlyAfterTheCreatorsShareIsProvenOnTheMovedRecord()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerMove = _fixture.Updates.Single(u => u.Payload.ContainsKey("ownerid@odata.bind")).Sequence;
        _fixture.Grants.Single(g => g.Principal.Id == colleague).Sequence.Should().BeGreaterThan(ownerMove);
    }

    /// <summary>
    /// When a creator share fails, a named colleague is never shared to: colleagues follow a PROVEN creator share, so
    /// they cannot change the outcome of a refused run.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheCreatorsShareFails_SharesNoColleague()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == colleague);
    }

    [Fact]
    public async Task Provisioning_WhenAColleaguesShareFails_StillSucceeds()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = colleague;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a mistyped colleague id must not throw away a provision whose creator share succeeded");

        _fixture.Grants.Select(g => g.Principal.Id).Should()
            .BeEquivalentTo(new[] { ProvisionProjectTestFixture.CallerSystemUserId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The reverse path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsecure_ReassignsOwnershipAndRevokesEveryShare()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Revokes.Select(r => r.Principal.Id).Should().Contain(
            ProvisionProjectTestFixture.CallerSystemUserId,
            "a leftover POA row on a no-longer-secure project is an access path no secure-project UI "
            + "would show");

        _fixture.Updates.Should().Contain(
            u => u.EntitySet == ProjectEntitySet
                 && u.RecordId == projectId
                 && u.Payload.ContainsKey("sprk_issecure"),
            "the designation itself has to be cleared, not just the isolation");

        // ISS-018 regression: a readable record still reports its sweep as COMPLETE, and the count
        // still matches what was revoked. Without this the strict read could start refusing every
        // record and the happy path would go on passing.
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharesRevoked").GetInt32()
            .Should().Be(_fixture.Revokes.Count).And.BeGreaterThan(0);
    }

    /// <summary>
    /// Task 144: matters and work assignments carry <c>sprk_issecure</c> too, and the reverse path works for them
    /// exactly as for a project — owner moved and read back, every share revoked, the flag cleared, and the sweep's
    /// completeness reported. The legacy <c>projectId</c> field stays empty: a matter's id is not a project id.
    /// </summary>
    [Theory]
    [InlineData("matter", "sprk_matters")]
    [InlineData("workassignment", "sprk_workassignments")]
    public async Task Unsecure_OnAMatterOrWorkAssignment_ReassignsRevokesAndClearsTheFlag(string recordType, string entitySet)
    {
        var recordId = Guid.NewGuid();
        if (recordType == "matter")
            _fixture.SeedMatter(recordId);
        else
            _fixture.SeedWorkAssignment(recordId);
        var client = _fixture.CreateAuthenticatedClient();

        var provisioned = await client.PostAsJsonAsync(ProvisionRoute, new { recordType, recordId });
        provisioned.StatusCode.Should().Be(HttpStatusCode.OK, await provisioned.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningUserOf(recordId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId,
            "ownership moves off the secure team to the caller, verified by read-back");
        _fixture.Revokes.Should().Contain(r => r.EntitySet == entitySet && r.RecordId == recordId
                                               && r.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.IsSecureOf(recordId).Should().BeFalse();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("recordType").GetString().Should().Be(recordType);
        body.RootElement.GetProperty("recordId").GetGuid().Should().Be(recordId);
        body.RootElement.GetProperty("projectId").GetGuid().Should().Be(Guid.Empty,
            "the legacy field names a PROJECT; reporting a matter's id there would mislabel it");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ISS-018 (#995) — a failed share read must not read as a clean sweep
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsecure_OnARecordWithNoShares_ReportsACompleteSweepOfZero()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        // No provisioning call, so the record genuinely carries no shares.
        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue(
            "zero shares removed from a readable record IS a complete sweep — this is the half of the "
            + "distinction that a failed read must not be able to imitate");
    }

    [Fact]
    public async Task Unsecure_WhenTheShareReadCannotBeCompleted_ReportsAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        // The strict read refuses (more than one page, or an unreadable row). The soft read still
        // returns what it can parse, so partial progress is possible.
        _fixture.StrictShareReadSucceeds = false;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "ownership has already moved, so the flow stays non-fatal");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "a sweep driven by an incomplete enumeration must never claim to have removed everything");

        _fixture.Revokes.Should().NotBeEmpty(
            "refusing outright would leave EVERY share in place; sweeping what can be enumerated "
            + "removes some stale access");

        body.RootElement.GetProperty("sharesRevoked").GetInt32()
            .Should().Be(_fixture.Revokes.Count,
                "the reported count must match what was actually revoked — an implementation that "
                + "returned 0 alongside a non-empty sweep would otherwise pass");

        // Anchored on the endpoint's own log prefix: provisioning also writes Warning-level lines
        // interpolating this same project id, so a guid-only predicate could be satisfied by a log
        // the code under test never wrote.
        _fixture.Logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("[UNSECURE]")
                 && e.Message.Contains(projectId.ToString()),
            "the warning naming the failure was unreachable before this fix — the soft read swallowed "
            + "the failure and answered an empty list, so the catch could not fire for the case it named");
    }

    [Fact]
    public async Task Unsecure_WhenNoSharesCanBeEnumeratedAtAll_ReportsZeroRevokedAndAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        // Neither read can answer — the transport itself is failing.
        _fixture.StrictShareReadSucceeds = false;
        _fixture.SoftShareReadSucceeds = false;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "THIS is the defect: zero-revoked-because-unreadable answered 200 with sharesRevoked = 0, "
            + "indistinguishable from a record that had no shares, so an unsecure could leave every "
            + "share in place silently");

        _fixture.Revokes.Should().BeEmpty("nothing could be enumerated, so nothing was removed");
    }

    [Fact]
    public async Task Unsecure_WhenAShareRevokeFails_StaysNonFatalAndReportsAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.FailRevokeForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "best-effort per share and never fatal — a reassignment that already succeeded is not "
            + "thrown away by one stubborn POA row");

        _fixture.Updates.Should().Contain(
            u => u.RecordId == projectId && u.Payload.ContainsKey("sprk_issecure"),
            "the flag is still cleared: per ADR-003 sprk_issecure suppresses the derived-member and "
            + "org-expansion terms, NOT Dataverse's own answer, so leaving it set would buy no "
            + "protection against the surviving share while half-applying the unsecure");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "a share we failed to remove means the record is demonstrably not fully revoked");

        _fixture.Logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("[UNSECURE]")
                 && e.Message.Contains(ProvisionProjectTestFixture.CallerSystemUserId.ToString()),
            "each failure is logged with its principal so an operator can finish the job");
    }

    [Fact]
    public async Task Unsecure_OnAProjectThatIsNotSecure_MakesNoCompletenessClaim()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").ValueKind.Should().Be(JsonValueKind.Null,
            "no sweep is attempted on the idempotent path, so the response must not vouch that the "
            + "record is free of POA rows — a project that was never secure can still carry shares "
            + "issued by another surface");
    }

    [Fact]
    public async Task Unsecure_RetriedAfterAnIncompleteSweep_DoesNotThenClaimACleanSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        // First call: the sweep cannot account for every share. The flag is cleared regardless —
        // per ADR-003 it suppresses derived/org terms, not the POA row, so holding it back would
        // protect nothing.
        _fixture.StrictShareReadSucceeds = false;
        var first = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });
        using (var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            firstBody.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse();
        }

        // Retrying is the ONLY remediation a sweepComplete:false response affords — and because the
        // flag is now clear, the retry lands on the idempotent path, which sweeps nothing.
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        secondBody.RootElement.GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();
        secondBody.RootElement.GetProperty("sweepComplete").ValueKind.Should().Be(JsonValueKind.Null,
            "the retry must NOT answer 'complete sweep of zero' over a record whose shares are still "
            + "in place — that is ISS-018's exact shape reintroduced on the very path an incomplete "
            + "sweep invites the operator onto. A defaulted true here is what review caught");
    }

    [Fact]
    public async Task Unsecure_AssignsOwnershipBeforeClearingTheFlag()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        // Ordering is read off RecordedUpdate.Sequence, NOT off the bag's enumeration order —
        // Updates is a ConcurrentBag, which does not preserve insertion order.
        var writes = _fixture.Updates.Where(u => u.RecordId == projectId).ToList();

        var ownerWrite = writes
            .Where(u => u.Payload.Keys.Any(k => k.StartsWith("ownerid", StringComparison.Ordinal)))
            .Select(u => u.Sequence)
            .DefaultIfEmpty(-1)
            .Min();

        var flagWrite = writes
            .Where(u => u.Payload.ContainsKey("sprk_issecure"))
            .Select(u => u.Sequence)
            .DefaultIfEmpty(-1)
            .Min();

        ownerWrite.Should().BeGreaterThan(0, "ownership must actually be reassigned");
        flagWrite.Should().BeGreaterThan(ownerWrite,
            "clearing the flag first would advertise a normal project while it was still team-owned and "
            + "share-gated");
    }

    [Fact]
    public async Task Unsecure_OnAProjectThatIsNotSecure_IsAnIdempotentNoOp()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat is not a conflict — the caller's "
            + "intent is already satisfied");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();

        _fixture.Revokes.Should().BeEmpty();
        _fixture.Updates.Should().BeEmpty("an idempotent no-op writes nothing");
    }

    [Fact]
    public async Task Unsecure_OnAMissingProject_Is404()
    {
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonProjectNotFound);
    }

    [Fact]
    public async Task Unsecure_WhenNoOwnerCanBeResolved_RefusesRatherThanStrandingTheRecord()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false; // no request nomination, no config, no caller
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonOwnerUnresolved);
        _fixture.Revokes.Should().BeEmpty("nothing is torn down until a destination owner exists");
    }

    [Fact]
    public async Task Unsecure_HonoursAnExplicitlyNominatedOwner()
    {
        var projectId = Guid.NewGuid();
        var steward = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            UnsecureRoute, new { projectId, reassignToSystemUserId = steward });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("newOwnerSystemUserId").GetGuid().Should().Be(steward);
    }
}
