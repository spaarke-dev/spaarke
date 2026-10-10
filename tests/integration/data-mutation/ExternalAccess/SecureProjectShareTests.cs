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

    /// <summary>
    /// Task 149: Step 5.5 is a root-share writer, so provisioning fans its shares out to the record's secure children —
    /// here a document the Secure team already owns (a secure child filed before the root finished provisioning, task
    /// 158's shape). The child receives the creator, through the SAME synchronizer the share endpoints use, and never
    /// with Share.
    /// </summary>
    [Fact]
    public async Task Provisioning_FansTheNewSharesOutToTheRecordsSecureChildren()
    {
        var projectId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_project", projectId)
            .SecureChild("sprk_document", documentId, ("sprk_project", "sprk_project", projectId));
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var childShare = _fixture.Grants.Should().ContainSingle(g => g.RecordId == documentId).Subject;
        childShare.EntitySet.Should().Be("sprk_documents");
        childShare.Principal.Should().Be(DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        childShare.AccessRightsCsv.Should().Contain("ShareAccess",
            "the creator's Collaborate share carries Share, and a child mirrors it since owner round 91");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 149 Step 8 on EVERY successful path (integration residual found merging 132, 2026-10-04)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A record ALREADY recording its own container (task 133 b2): kept, so Steps 6 + 7 create nothing.</summary>
    private const string KeptOwnContainer = "b!its-own-container";

    private void SeedRoot(string recordType, Guid recordId, string? containerId)
    {
        switch (recordType)
        {
            case "project": _fixture.SeedProject(recordId, containerId: containerId); break;
            case "matter": _fixture.SeedMatter(recordId, containerId: containerId); break;
            default: _fixture.SeedWorkAssignment(recordId, containerId: containerId); break;
        }
    }

    /// <summary>
    /// The integration residual found merging task 132 (2026-10-04): Step 8 ran only after Steps 6 + 7 created and
    /// recorded a NEW container, so a secure record that KEPT its own container (task 133 b2) returned 200 with its
    /// secure children still missing the shares Step 5.5 had just written. Both successful paths — container kept and
    /// container created — now fan out exactly once, through the same synchronizer, for every root type, and never with
    /// Share.
    /// </summary>
    [Theory]
    [InlineData("project", true)]
    [InlineData("matter", true)]
    [InlineData("workassignment", true)]
    [InlineData("project", false)]
    [InlineData("matter", false)]
    [InlineData("workassignment", false)]
    public async Task Provisioning_FansTheNewSharesOutToTheSecureChildren_WhetherTheContainerIsKeptOrCreated(
        string recordType, bool keepsItsOwnContainer)
    {
        var recordId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var rootTable = "sprk_" + recordType;
        SeedRoot(recordType, recordId, keepsItsOwnContainer ? KeptOwnContainer : null);
        _fixture.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot(rootTable, recordId)
            .SecureChild("sprk_document", documentId, (rootTable, rootTable, recordId));
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("speContainerId").GetString().Should().Be(
                keepsItsOwnContainer ? KeptOwnContainer : ProvisionProjectTestFixture.ProvisionedContainerId,
                "the theory must exercise the path it names");
        }
        _fixture.CreatedContainerDisplayNames.Should().HaveCount(keepsItsOwnContainer ? 0 : 1);

        var childShare = _fixture.Grants.Should().ContainSingle(g => g.RecordId == documentId,
            "the record's secure child follows the creator share Step 5.5 wrote, on the kept-container path as on the " +
            "new-container path").Subject;
        childShare.EntitySet.Should().Be("sprk_documents");
        childShare.Principal.Should().Be(DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        childShare.AccessRightsCsv.Should().Contain("ShareAccess",
            "the creator's Collaborate share carries Share, and a child mirrors it since owner round 91");
    }

    /// <summary>
    /// On either path, a child pass that cannot complete is reported, never a success (task 148, ADR-003; it supersedes
    /// 149-f1's "log and succeed"): the record IS provisioned with its kept or created container, the answer is 500
    /// children_incomplete, and the child table was queried — proof the pass runs on that path. A repeat call completes it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provisioning_WhenTheChildPassCannotComplete_IsChildrenIncomplete_OnBothContainerPaths(
        bool keepsItsOwnContainer)
    {
        var projectId = Guid.NewGuid();
        SeedRoot("project", projectId, keepsItsOwnContainer ? KeptOwnContainer : null);
        _fixture.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_project", projectId)
            .SecureChild("sprk_document", Guid.NewGuid(), ("sprk_project", "sprk_project", projectId))
            .FailingQueriesOf("sprk_document");
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            problem.RootElement.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.ContainerIdOf(projectId).Should().Be(
            keepsItsOwnContainer ? KeptOwnContainer : ProvisionProjectTestFixture.ProvisionedContainerId,
            "the record's own steps stand; only its related records are left to complete");
        _fixture.ChildWorld.QueriedTables.Should().Contain("sprk_document",
            "the child pass runs on this path and asks for the record's children");
        _fixture.Grants.Should().OnlyContain(g => g.RecordId == projectId, "no child share is written from an unreadable read");
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

    /// <summary>
    /// Owner 2026-09-30 (C4, task 139): colleagues named at provisioning receive EXACTLY the creator's rights — the
    /// Collaborate level, which carries ShareAccess so they can use the model-driven Share command as well as Manage
    /// Access. Asserted against the LITERAL and against the creator's own share on the same project.
    /// </summary>
    [Fact]
    public async Task Provisioning_SharesToNamedPrincipals_WithExactlyTheCreatorsRights()
    {
        const string collaborateLiteral = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess";
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var colleagueShare = _fixture.Grants.Should()
            .ContainSingle(g => g.Principal.Id == colleague).Subject;
        var creatorShare = _fixture.Grants
            .Single(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);

        colleagueShare.AccessRightsCsv.Should().Be(collaborateLiteral);
        creatorShare.AccessRightsCsv.Should().Be(collaborateLiteral);
        colleagueShare.AccessRightsCsv.Should().Be(creatorShare.AccessRightsCsv,
            "owner 2026-09-30: a colleague named at provisioning holds exactly what the creator holds");
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
        _fixture.RevokesAsOwner.Should().BeEmpty(
            "a TEAM owns the record, so the creator's share is an ordinary sharee's - revoked app-only, never as a user");
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
    /// The pre-call share set could not be read, the post-move share write fails, and compensation's only safe target is
    /// "no share" — which also removes the share the creator held BEFORE the call. The response must say exactly that
    /// (task 133 verifier round 1): <c>sharesRestored: false</c>, <c>creatorShareRemoved: true</c>, and never "the share
    /// this call issued was removed", which would hide that a pre-existing share is gone.
    /// </summary>
    /// <remarks>
    /// Setup corrected by task 133 b2 (merge of task 139): the pre-existing share is View Only. It was Collaborate, which
    /// task 139 made EXACTLY the creator rights (Share joined Collaborate) — so the post-move proof now found the share
    /// already exact, wrote nothing, and the run succeeded: the scenario under test ("a share with other rights the call
    /// must overwrite") no longer arose. The contract is unchanged.
    /// </remarks>
    [Fact]
    public async Task Provisioning_WhenCompensatedWithoutAPreCallRead_SaysThePreExistingShareWasRemovedToo()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedShare(projectId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            Sprk.Bff.Api.Services.Access.RecordShareLevels.ViewOnlyRights);
        _fixture.FailNextStrictShareReads = 1; // the pre-call read
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId; // the post-move write
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("sharesRestored").GetBoolean().Should().BeFalse(
            "the creator's pre-call share is gone, so the shares are NOT as they were");
        problem.RootElement.GetProperty("creatorShareRemoved").GetBoolean().Should().BeTrue();
        var detail = problem.RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("including any share the creator held before this call")
            .And.NotContain("The share this call issued to the creator was removed");
    }

    /// <summary>
    /// The pre-call read AND the post-move read fail, so this call wrote no share at all before compensating. The
    /// response says the creator's shares are as they were — and they are: the pre-existing share is untouched.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenCompensatedHavingWrittenNoShare_SaysTheSharesAreAsTheyWere()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedShare(projectId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            ProvisionProjectEndpoint.CollaboratorAccessRights);
        var before = _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.FailNextStrictShareReads = 1;               // the pre-call read
        _fixture.FailStrictShareReadWhileSecureOwned = true; // the post-move proof
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(before);
        _fixture.Grants.Should().BeEmpty();
        _fixture.Revokes.Should().BeEmpty();
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("sharesRestored").GetBoolean().Should().BeTrue();
        problem.RootElement.GetProperty("creatorShareRemoved").GetBoolean().Should().BeFalse();
        problem.RootElement.GetProperty("detail").GetString().Should().Contain("wrote no share for the creator");
    }

    /// <summary>
    /// The owner read-back throws AND the shares cannot be read on the moved record: the creator's share is issued
    /// without a read to confirm it (S5), and the response says it is NOT confirmed rather than "in place" (task 133
    /// verifier round 1) — <c>creatorShareConfirmed: false</c>.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheOwnerMoveIsUnverifiedAndTheShareCannotBeReadBack_SaysTheShareIsNotConfirmed()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailNextStrictShareReads = 1;               // no share-first: the pre-call read fails
        _fixture.OwnerReadBackFails = true;                  // the move lands but cannot be read back
        _fixture.FailStrictShareReadWhileSecureOwned = true; // nor can the shares on the moved record
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeFalse();
        var detail = problem.RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("NOT confirmed").And.NotContain("is in place");
        _fixture.Grants.Should().ContainSingle(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The owner read-back throws but the shares CAN be read: the creator's share is proven on the record by a read
    /// (not assumed from the share-first step), and the response says so — <c>creatorShareConfirmed: true</c>.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheOwnerMoveIsUnverified_ProvesTheCreatorsShareByARead()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerReadBackFails = true;
        _fixture.AssignDropsShareOf = ProvisionProjectTestFixture.CallerSystemUserId; // the move drops share-first's grant
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeTrue();
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
                "a share proven before the move is re-proven after it, and re-issued when the move dropped it");
        _fixture.Grants.Where(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId).Should().HaveCount(2);
    }

    /// <summary>
    /// Share-first PROVED the creator's share, the owner move is unverified, and after it neither the read nor a fresh
    /// grant works: the share proven before the move still counts as issued, so the response is
    /// <c>owner_assignment_unverified</c> with <c>creatorShareConfirmed: false</c> (the caller may retry) — not the
    /// administrator-only <c>creator_share_failed_resumable</c> with a CRITICAL log (task 133 verifier round 2).
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheMoveIsUnverifiedAndNoShareCanBeIssuedAfterIt_TheShareFirstGrantStillCounts()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerReadBackFails = true;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeFalse();
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "share-first's grant was proven before the move");
        _fixture.Logs.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
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
    /// The compensating move is SENT but its outcome cannot be read back (task 133 b2, verifier seed S19). ADR-003: an
    /// unverifiable compensation is a failure, never "reverted" — so this is the administrator-only state, with
    /// <c>ownershipRestored: false</c> and <c>ownershipVerified: false</c>, a CRITICAL log line, and a detail that never
    /// says "nothing moved" or offers the creator a retry.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheCompensatingMoveCannotBeReadBack_IsTheAdministratorOnlyState()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.FailStrictShareReadWhileSecureOwned = true;    // the post-move proof fails → compensate
        _fixture.FailOwnerReadBackAfterBindTo = businessUnitTeam; // the undo is sent, then its read-back throws
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.RootElement.GetProperty("ownershipRestored").GetBoolean().Should().BeFalse();
        problem.RootElement.GetProperty("ownershipVerified").GetBoolean().Should().BeFalse(
            "the undo's outcome is unknown, which is not the same claim as 'it did not take effect'");
        var detail = problem.RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("could not be verified").And.Contain("calls provisioning again");
        detail.Should().NotContainEquivalentOf("nothing").And.NotContainEquivalentOf("retry");
        // Task 133 r1: the undo's outcome is unknown, so the resume is promised only "while the team owns it" — if the
        // undo DID land, an administrator's call would run from the start instead, and the detail says what applies then.
        detail.Should().Contain("While the team owns it").And.Contain("If the move back did take effect");
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains(projectId.ToString()));
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The FORWARD owner move is unverified AND no creator share can be issued or was proven before it (task 133 b2,
    /// verifier seed S10). If the move landed, nobody can open the record (S5) — so it is the administrator-only state
    /// with a CRITICAL log line, never <c>owner_assignment_unverified</c>'s "the same caller may retry".
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheMoveIsUnverifiedAndNoShareExistsOrCanBeIssued_IsTheAdministratorOnlyState()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailNextStrictShareReads = 1;                                     // no share-first: pre-call read fails
        _fixture.OwnerReadBackFails = true;                                        // the move lands, unverifiably
        _fixture.FailStrictShareReadWhileSecureOwned = true;                       // no read after it
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId; // no grant after it
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.RootElement.GetProperty("ownerTeamId").GetGuid().Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        var detail = problem.RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("no share to its creator could be issued").And.Contain("administrator");
        detail.Should().NotContainEquivalentOf("retry").And.NotContain("Nothing has been provisioned");
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains(projectId.ToString()));
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.SomeoneCanOpen(projectId).Should().BeFalse(
            "this IS the state the code calls CRITICAL — the move landed in the fixture and no share exists");
    }

    /// <summary>
    /// Compensation's share restore WRITES but reads back wrong (task 133 b2, verifier seed S22b): the revoke is accepted
    /// and not applied. The response must say the share is not confirmed removed — <c>sharesRestored: false</c> — never
    /// that the creator's shares are "as they were".
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheShareRestoreReadsBackWrong_SaysTheSharesAreNotRestored()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.FailStrictShareReadWhileSecureOwned = true;                 // the post-move proof fails → compensate
        _fixture.RevokeNotAppliedFor = ProvisionProjectTestFixture.CallerSystemUserId; // the restore does not take
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.RootElement.GetProperty("sharesRestored").GetBoolean().Should().BeFalse(
            "the read-back after the restore still shows the share this call issued");
        problem.RootElement.GetProperty("creatorShareRemoved").GetBoolean().Should().BeFalse();
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Contain("could not be confirmed removed").And.NotContain("as it was before the call (read back)");
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "the fixture kept it — the claim must match");
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

    /// <summary>
    /// A colleague share failure still leaves a 200 WITH THE COUNT REPORTED (task 061 behaviour; task 133 acceptance):
    /// two colleagues named, one fails, <c>additionalPrincipalsShared</c> is 1.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenAColleaguesShareFails_StillSucceeds()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        var reachableColleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = colleague;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague, reachableColleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a mistyped colleague id must not throw away a provision whose creator share succeeded");

        _fixture.Grants.Select(g => g.Principal.Id).Should()
            .BeEquivalentTo(new[] { ProvisionProjectTestFixture.CallerSystemUserId, reachableColleague });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("additionalPrincipalsShared").GetInt32().Should().Be(1,
            "the count reports the colleagues actually shared, not the colleagues named");

        // Task 150, round 33 item 5: never silent — the colleague who was not shared to is NAMED, with the reason.
        var skipped = body.RootElement.GetProperty("skippedPrincipals").EnumerateArray().ToList();
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(colleague);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalShareFailed);
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

    /// <remarks>
    /// Task 150 moved this refusal EARLIER: the caller's identity is now established first, by the F3 check (only a
    /// Full Access holder or the creator may remove the designation), and an unestablished caller is refused there —
    /// with nothing torn down, as before. The owner fallback then reuses that identity, so <c>owner_unresolved</c> is
    /// a defensive branch only.
    /// </remarks>
    [Fact]
    public async Task Unsecure_WhenTheCallerCannotBeIdentified_RefusesRatherThanStrandingTheRecord()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false; // no request nomination, no config, no caller
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonPermissionUnverifiable);
        _fixture.Revokes.Should().BeEmpty("nothing is torn down until a destination owner exists");
        _fixture.Updates.Should().BeEmpty();
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

    // ─────────────────────────────────────────────────────────────────────────
    // The owner's own share (live on dev 2026-10-06): Dataverse refuses an app-only RevokeAccess of the share held by the
    // record's CURRENT owning user — 400 0x80040223 "Only owner can revoke access to the owner" — and accepts it sent as
    // that user (MSCRMCallerID). The fixture's share double applies that rule always, as Dataverse does.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A creator-driven unsecure hands the record back to the creator, who still holds provisioning's share. That share is
    /// revoked AS the creator (now the owner) — before the fix it was sent app-only, refused, and every such unsecure ended
    /// <c>sweepComplete: false</c> with the explicit share (Share included) surviving. A colleague's share is still revoked
    /// app-only: only the owner's own share is ever sent as a user.
    /// </summary>
    [Fact]
    public async Task Unsecure_ByTheCreator_RevokesTheirOwnShareAsTheOwner_AndTheSweepIsComplete()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        var provisioned = await client.PostAsJsonAsync(ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });
        provisioned.StatusCode.Should().Be(HttpStatusCode.OK, await provisioned.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().NotBe(0,
            "precondition: provisioning shared the record to its creator");

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId,
            "precondition: the creator is the new owner, so their share is the owner's own");
        _fixture.OwnerShareRevokesRefused.Should().BeEmpty("the owner's own share is never sent app-only");
        _fixture.RevokesAsOwner.Should().ContainSingle()
            .Which.Principal.Should().Be(DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        _fixture.Revokes.Should().Contain(r => r.Principal == DataversePrincipalRef.User(colleague));
        _fixture.RevokesAsOwner.Should().NotContain(r => r.Principal.Id == colleague,
            "a principal that does not own the record is never impersonated");
        _fixture.SharesOn(projectId).Should().BeEmpty("no explicit share outlives the unsecure — the owner's included");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Unsecured to a NOMINATED owner who holds a share: the nominee's share is the owner's own (revoked as the nominee);
    /// the creator's share is now an ordinary sharee's (revoked app-only). The owner the sweep acts as is the one Step 3
    /// read back, not the caller.
    /// </summary>
    [Fact]
    public async Task Unsecure_ToANominatedOwnerWhoHoldsAShare_RevokesOnlyTheNomineesShareAsTheOwner()
    {
        var projectId = Guid.NewGuid();
        var steward = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        var provisioned = await client.PostAsJsonAsync(ProvisionRoute, new { projectId, sharePrincipalIds = new[] { steward } });
        provisioned.StatusCode.Should().Be(HttpStatusCode.OK, await provisioned.Content.ReadAsStringAsync());

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId, reassignToSystemUserId = steward });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningUserOf(projectId).Should().Be(steward);
        _fixture.OwnerShareRevokesRefused.Should().BeEmpty();
        _fixture.RevokesAsOwner.Should().ContainSingle()
            .Which.Principal.Should().Be(DataversePrincipalRef.User(steward));
        _fixture.Revokes.Should().Contain(r => r.Principal == DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        _fixture.SharesOn(projectId).Should().BeEmpty();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Provisioning's NotMoved undo on a record the CREATOR owns: the share-first grant is revoked as the creator (still the
    /// owner — nothing moved), read back as gone, and the response says <c>sharesRestored: true</c>. Before the fix the
    /// app-only revoke was refused and the self-share stayed.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenTheMoveIsNotApplied_OnACreatorOwnedRecord_RevokesTheShareFirstGrantAsTheCreator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId); // owned by the caller, as the wizard creates it
        _fixture.OwnershipPatchIsApplied = false;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentNotApplied);
        _fixture.Grants.Should().ContainSingle(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "precondition: the share-first grant was written before the move");
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.OwnerShareRevokesRefused.Should().BeEmpty();
        _fixture.RevokesAsOwner.Should().ContainSingle()
            .Which.Principal.Should().Be(DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("sharesRestored").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Compensation after the move BACK to a creator who owned the record: the creator's share is put back to "no share" as
    /// the creator (the owner again, read back) — <c>sharesRestored: true</c>, mask 0.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenCompensatedBackToTheCreator_RevokesTheCreatorsShareAsTheCreator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId); // owned by the caller
        _fixture.FailStrictShareReadWhileSecureOwned = true; // the post-move proof fails -> compensate
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId,
            "precondition: the move was undone, back to the creator");
        _fixture.OwnerShareRevokesRefused.Should().BeEmpty();
        _fixture.RevokesAsOwner.Should().ContainSingle()
            .Which.Principal.Should().Be(DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId));
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.RootElement.GetProperty("sharesRestored").GetBoolean().Should().BeTrue();
        problem.RootElement.GetProperty("creatorShareRemoved").GetBoolean().Should().BeFalse();
    }
}
