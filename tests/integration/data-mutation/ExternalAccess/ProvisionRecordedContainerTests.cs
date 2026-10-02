using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 133 b2 — provisioning a record that ALREADY records a container must never orphan it.
/// </summary>
/// <remarks>
/// <para><b>The live defect (2026-10-02, task 144 note §13.4).</b> Project 65a3fab2 was secure and already recorded its
/// OWN container; provisioning created a new one and overwrote <c>sprk_containerid</c>, leaving the first referenced by
/// no record — an orphan, and with it anything stored there. The forward path now classifies the recorded value before
/// any write: a business unit's or a configured shared container is replaced (its owner keeps pointing at it), one
/// another root also records is refused, and anything else is the record's own and is KEPT.</para>
/// <para>Every refusal here is asserted to have written NOTHING — no owner move, no share, no container — because each
/// fires before the first mutation.</para>
/// </remarks>
public class ProvisionRecordedContainerTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string Route = "/api/v1/external-access/provision-project";
    private const string OwnContainer = "b!its-own-container";

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionRecordedContainerTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private Task<HttpResponseMessage> ProvisionAsync(object body) =>
        _fixture.CreateEntitledClient().PostAsJsonAsync(Route, body);

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.Clone();
    }

    private void AssertNothingWritten(Guid recordId, string expectedContainer)
    {
        _fixture.Updates.Should().BeEmpty("refused before any mutation");
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.ContainerIdOf(recordId).Should().Be(expectedContainer);
        _fixture.OwningTeamOf(recordId).Should().NotBe(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    /// <summary>
    /// The 65a3fab2 shape: a secure record, not yet owned by the team, recording a container no business unit,
    /// configuration or other record holds. It is secured and shared as usual, and its container is KEPT — none created,
    /// <c>sprk_containerid</c> never rewritten — so nothing is orphaned. A second call is then the ordinary 409.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_WhenTheRecordAlreadyRecordsItsOwnContainer_KeepsIt_AndOrphansNothing(string recordType)
    {
        var recordId = Guid.NewGuid();
        switch (recordType)
        {
            case "project": _fixture.SeedProject(recordId, containerId: OwnContainer); break;
            case "matter": _fixture.SeedMatter(recordId, containerId: OwnContainer); break;
            default: _fixture.SeedWorkAssignment(recordId, containerId: OwnContainer); break;
        }

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("speContainerId").GetString().Should().Be(OwnContainer);
        }
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("the record's own container is kept, not replaced");
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("sprk_containerid"),
            "rewriting the value is what orphaned b!HBRbo… live");
        _fixture.ContainerIdOf(recordId).Should().Be(OwnContainer);
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.SomeoneCanOpen(recordId).Should().BeTrue();

        var again = await ProvisionAsync(new { recordType, recordId });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "owned by the team WITH a container recorded = provisioned");
        (await ProblemOf(again)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
    }

    /// <summary>
    /// The same container recorded on ANOTHER project, matter or work assignment — not shared storage, so it belongs to
    /// one of them and provisioning cannot tell which: refused (409) before any write, naming the other record.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_WhenAnotherRootRecordsTheSameContainer_RefusesBeforeAnyWrite(string otherType)
    {
        var projectId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        switch (otherType)
        {
            case "project": _fixture.SeedProject(otherId, containerId: OwnContainer, isSecure: false); break;
            case "matter": _fixture.SeedMatter(otherId, containerId: OwnContainer, isSecure: false); break;
            default: _fixture.SeedWorkAssignment(otherId, containerId: OwnContainer, isSecure: false); break;
        }

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonContainerSharedWithAnotherRecord);
        problem.GetProperty("otherRecordType").GetString().Should().Be(otherType);
        problem.TryGetProperty("otherRecordId", out _).Should().BeFalse(
            "which record holds it goes to the operator log, not to a caller who may not hold Write on it");
        _fixture.Logs.Entries.Should().Contain(e => e.Message.Contains(otherId.ToString()),
            "the administrator finds the other record in the log");
        AssertNothingWritten(projectId, OwnContainer);
    }

    /// <summary>
    /// A business unit's shared container (the pre-task-076 create-time cascade) is replaced by the record's own; the
    /// business unit keeps it, so nothing is orphaned. Recognised BEFORE the other-roots check: many records carry the
    /// same business-unit value, and that must not read as "another record holds it".
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheRecordedContainerIsABusinessUnits_ReplacesIt_EvenWhenOtherRecordsCarryItToo()
    {
        const string buContainer = "b!business-unit-shared";
        var businessUnit = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        _fixture.BusinessUnitContainers[businessUnit] = buContainer;
        _fixture.SeedProject(projectId, containerId: buContainer);
        _fixture.SeedProject(Guid.NewGuid(), containerId: buContainer, isSecure: false); // an ordinary record sharing it

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle();
        _fixture.BusinessUnitContainers[businessUnit].Should().Be(buContainer, "the business unit keeps its container");
    }

    /// <summary>A container this BFF is configured to use for many records is replaced the same way.</summary>
    [Fact]
    public async Task Provision_WhenTheRecordedContainerIsAConfiguredSharedOne_ReplacesIt()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: ProvisionProjectTestFixture.ConfiguredArchiveContainerId);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle();
    }

    /// <summary>
    /// Whether the recorded container is shared cannot be read: refused (500) before any write — never guessed as "its
    /// own" (which could keep another record's storage) nor as "shared" (which would orphan the record's own).
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheRecordedContainerCannotBeChecked_RefusesBeforeAnyWrite()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        _fixture.ContainerOwnershipReadFails = true;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonContainerOwnershipUnreadable);
        AssertNothingWritten(projectId, OwnContainer);

        _fixture.ContainerOwnershipReadFails = false;
        var retry = await ProvisionAsync(new { projectId });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "the stated recovery — the same caller calls again — works");
        _fixture.ContainerIdOf(projectId).Should().Be(OwnContainer);
    }
}
