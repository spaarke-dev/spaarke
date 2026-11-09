using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 049 fix round 2 (UAC-r2 ruling D2, its condition): after a project the wizard
/// creates as secure (secure from creation), the wizard's <c>provisionSecureProject</c> call does only what is left —
/// <c>already_provisioned</c> (which the wizard then treats as success) only when the creator share AND the container
/// both exist; a create that half-finished (no container recorded) is resumed by the next call, which creates the
/// container, never a 409.
/// </summary>
/// <remarks>
/// The row is seeded as the secure-from-creation create leaves it: owned by the named team, flagged, created by the
/// application for the caller (<c>sprk_createdbyperson</c>), and shared to the caller with the creator rights.
/// </remarks>
[Trait("status", "new")]
public sealed class ProvisionProjectSecureFromCreationTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string Route = "/api/v1/external-access/provision-project";

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionProjectSecureFromCreationTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.SystemUsers[SecureRootInheritanceTests.AppUser] = (false, true);
    }

    private Guid SecuredAtCreate(string? containerId)
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(
            projectId,
            owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            containerId: containerId,
            isSecure: true,
            createdBy: SecureRootInheritanceTests.AppUser,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SeedShare(projectId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            ProvisionProjectEndpoint.CreatorAccessRights);
        return projectId;
    }

    private Task<HttpResponseMessage> ProvisionAsync(HttpClient client, Guid projectId) =>
        client.PostAsJsonAsync(Route, new { projectId, projectRef = "P-2026-0049" });

    [Fact]
    public async Task ProvisionProject_AfterACompleteSecureFromCreationCreate_IsAlreadyProvisioned_AndWritesNothing()
    {
        var projectId = SecuredAtCreate(containerId: "b!secured-at-create");
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        }

        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionProject_AfterASecureFromCreationCreateThatRecordedNoContainer_CreatesTheContainer_NotA409()
    {
        var projectId = SecuredAtCreate(containerId: null);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle("the remaining work: the container");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.Grants.Should().BeEmpty("the creator already holds the creator share: no second grant");
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("ownerid@odata.bind"), "already owned by the team");
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights));
    }

    [Fact]
    public async Task ProvisionProject_AfterASecureFromCreationCreateWithNoCreatorShare_SharesTheCreator_ThenCreatesTheContainer()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, containerId: null,
            isSecure: true, createdBy: SecureRootInheritanceTests.AppUser,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().ContainSingle().Which.Principal.Id.Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }
}
