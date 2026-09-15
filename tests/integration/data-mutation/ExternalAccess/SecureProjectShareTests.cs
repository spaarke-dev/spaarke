using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 061 — the share plane on <c>/provision-project</c>, and the reverse path on
/// <c>/unsecure-project</c>.
/// </summary>
/// <remarks>
/// <para><b>The defect these pin.</b> Task 021 assigned secure projects to the Secure Project business
/// unit's default owner team, which has no members — correct isolation. It issued no shares. design.md
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

    [Fact]
    public async Task Provisioning_WhenTheCallersIdentityCannotBeEstablished_FailsRatherThanLeavingALockedBox()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);

        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty(
            "the share step runs BEFORE container creation precisely so a share failure orphans nothing");
    }

    [Fact]
    public async Task Provisioning_WhenTheCreatorsShareFails_FailsAndCreatesNoContainer()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
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
