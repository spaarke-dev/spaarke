using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 144 (C10 part 1, GitHub #967) — secure records are owned by a NAMED, NON-DEFAULT,
/// MEMBERLESS owner team in a Secure Record business unit that holds NO users, for projects AND matters AND work
/// assignments; provisioning refuses otherwise.
/// </summary>
/// <remarks>
/// <para><b>The two holes.</b> (1) Provisioning owned secure records by the BU's DEFAULT team, whose membership
/// Dataverse maintains from each user's business unit and which cannot be curated — moving any user into the BU made
/// them a member of the team owning every secure record, and nothing checked. (2) A user in the BU reads every record
/// owned there by business-unit DEPTH, whoever the owner team is — a named team alone does not close that. Both
/// invariants are checked before ANY mutation, and an unreadable answer to either refuses (ADR-003): "could not count"
/// is never "zero".</para>
///
/// <para><b>The third hole.</b> All three roots carry <c>sprk_issecure</c>, but only projects could be provisioned: a
/// secure matter or work assignment stayed owned by its creator in an ordinary business unit — not isolated at all —
/// and every upload to it 409'd for want of its own container. The delegation filter now resolves the SAME root the
/// handler re-owns.</para>
///
/// <para>Every negative asserts the WRITE LOG, not just the status: a refusal that still assigned the record or created
/// a container would be the defect with a better status code.</para>
/// </remarks>
public class SecureNamedOwnerTeamProvisioningTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureNamedOwnerTeamProvisioningTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    private Task<HttpResponseMessage> ProvisionAsync(object body)
    {
        var client = _fixture.CreateEntitledClient();
        return client.PostAsJsonAsync(ProvisionRoute, body);
    }

    /// <summary>"Nothing was written": no update, no share, no container, and the record still where it was.</summary>
    private void AssertNothingWritten(Guid recordId)
    {
        _fixture.Updates.Should().BeEmpty("every refusal here happens BEFORE any mutation");
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningTeamOf(recordId).Should().BeNull("the record's ownership must be unchanged");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Positive: all three roots end owned by the named team, shared to the creator, with their own container
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("project", "sprk_projects", "Secure Project — Seeded Secure Project")]
    [InlineData("matter", "sprk_matters", "Secure Matter — Seeded Secure Matter")]
    [InlineData("workassignment", "sprk_workassignments", "Secure Work Assignment — Seeded Secure Work Assignment")]
    public async Task Provision_EachRootType_EndsOwnedByTheNamedTeam_SharedToTheCreator_WithItsOwnContainer(
        string recordType, string entitySet, string expectedContainerName)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId);

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId,
            "ownership is read back from the row, not inferred from the PATCH");
        _fixture.Updates.Should().Contain(u => u.EntitySet == entitySet && u.RecordId == recordId
                                               && u.Payload.ContainsKey("ownerid@odata.bind"));

        var creatorShare = _fixture.Grants.Should().ContainSingle().Subject;
        creatorShare.EntitySet.Should().Be(entitySet);
        creatorShare.RecordId.Should().Be(recordId);
        creatorShare.Principal.Id.Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        creatorShare.AccessRightsCsv.Should().Be(ProvisionProjectEndpoint.CreatorAccessRights);

        _fixture.CreatedContainerDisplayNames.Should().ContainSingle().Which.Should().Be(expectedContainerName);
        _fixture.CreatedContainerBusinessUnits.Should().ContainSingle().Which.Should().Be(
            ProvisionProjectTestFixture.SecureBuId,
            "the secure record's own container is bound to the unit that owns the record — the Secure Record business " +
            "unit — so no customer's SPE administrator reaches it through the admin plane (task 165, owner round 20)");
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId,
            "a secure root without its own container fails every upload closed (RecordContainerResolver)");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("recordType").GetString().Should().Be(recordType);
        body.RootElement.GetProperty("recordId").GetGuid().Should().Be(recordId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Negative: the named team must exist exactly once — and the default team is NEVER the answer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provision_WhenTwoNamedOwnerTeamsMatch_RefusesAndWritesNothing()
    {
        var matterId = Guid.NewGuid();
        _fixture.SeedMatter(matterId);
        _fixture.OwnerTeamMatchCount = 2;

        var response = await ProvisionAsync(new { recordType = "matter", recordId = matterId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamAmbiguous);
        AssertNothingWritten(matterId);
    }

    /// <summary>
    /// The misconfiguration in which ONLY <c>isdefault eq false</c> stands between provisioning and the default team:
    /// the default team carries the configured owner-team name and no named team exists. Provisioning must still refuse.
    /// </summary>
    [Fact]
    public async Task Provision_WhenOnlyTheDefaultTeamCarriesTheOwnerTeamName_RefusesRatherThanSelectingTheDefaultTeam()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerTeamMatchCount = 0;
        _fixture.DefaultTeamCarriesOwnerTeamName = true;

        var response = await ProvisionAsync(new { recordType = "project", recordId = projectId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamNotFound);
        _fixture.OwningTeamOf(projectId).Should().NotBe(ProvisionProjectTestFixture.SecureDefaultTeamId);
        AssertNothingWritten(projectId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Negative: the memberless invariant (any member, of any kind) — and "could not count" is not zero
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One member refuses. A membership row does not say what kind of principal it holds, and the query does not filter
    /// on it, so a human and an application user are refused alike — both are seeded to show neither is waved through.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheNamedTeamHasAnyMember_HumanOrApplicationUser_RefusesBeforeAnyMutation()
    {
        var workAssignmentId = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignmentId);
        var human = Guid.NewGuid();
        var applicationUser = Guid.NewGuid();
        _fixture.OwnerTeamMembers.AddRange(new[] { human, applicationUser });

        var response = await ProvisionAsync(new { recordType = "workassignment", recordId = workAssignmentId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamHasMembers);
        AssertNothingWritten(workAssignmentId);

        // The members are named to the OPERATOR (a CRITICAL log line), not to the caller: any Write holder on a secure
        // record can reach this response, and removing team members is an administrator's job.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(human.ToString()).And.NotContain(applicationUser.ToString());
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical
                                                    && e.Message.Contains("[PROVISION]")
                                                    && e.Message.Contains(human.ToString())
                                                    && e.Message.Contains(applicationUser.ToString()),
            "a populated owner team is a live exposure of every secure record already owned by it, not only a refusal");
    }

    [Fact]
    public async Task Provision_WhenTheMembershipReadFails_RefusesInsteadOfReadingItAsZero()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.MembershipReadSucceeds = false;

        var response = await ProvisionAsync(new { projectId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamMembershipUnreadable);
        AssertNothingWritten(projectId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Negative: the no-user business unit — enabled or disabled, human or application
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<bool, bool> BusinessUnitUserKinds => new()
    {
        { false, false }, // an enabled human
        { true, false },  // a DISABLED user — re-enabling is one click
        { false, true },  // an APPLICATION user — reads by depth like anyone else
    };

    [Theory]
    [MemberData(nameof(BusinessUnitUserKinds))]
    public async Task Provision_WhenAnySystemUserSitsInTheSecureBusinessUnit_RefusesBeforeAnyMutation(
        bool isDisabled, bool isApplicationUser)
    {
        var matterId = Guid.NewGuid();
        _fixture.SeedMatter(matterId);
        var userInBu = Guid.NewGuid();
        _fixture.SecureBuUsers.Add(new ProvisionProjectTestFixture.SecureBuUser(userInBu, isDisabled, isApplicationUser));

        var response = await ProvisionAsync(new { recordType = "matter", recordId = matterId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureBuHasUsers);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(userInBu.ToString(),
            "who sits in the business unit is for the operator log, not the caller");
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains(userInBu.ToString()));
        AssertNothingWritten(matterId);
    }

    [Fact]
    public async Task Provision_WhenTheBusinessUnitUserReadFails_RefusesInsteadOfReadingItAsZero()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.BusinessUnitUserReadSucceeds = false;

        var response = await ProvisionAsync(new { projectId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureBuUsersUnreadable);
        AssertNothingWritten(projectId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Negative: a record already owned inside the Secure Record BU by another team (the retired default team)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A record provisioned before task 144 is owned by the default team. Moving it onto the named team is the migration
    /// script's job, not a side effect of provisioning, so it is refused before any write and pointed at the script.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheRecordIsStillOwnedByTheRetiredDefaultTeam_RefusesAndWritesNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureDefaultTeamId,
            containerId: "b!its-own-container-from-before");

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnedByOtherSecureTeam);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Migrate-SecureRecordsToNamedOwnerTeam.ps1");
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureDefaultTeamId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Authorization: the record authorized is the record re-owned
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("project", "sprk_projects")]
    [InlineData("matter", "sprk_matters")]
    [InlineData("workassignment", "sprk_workassignments")]
    public async Task Provision_TheDelegationFilterAuthorizesTheSameRootTheHandlerReowns(string recordType, string entitySet)
    {
        var recordId = Guid.NewGuid();
        var decoyProjectId = Guid.NewGuid();
        Seed(recordType, recordId);
        _fixture.SeedProject(decoyProjectId);

        // The legacy projectId names a DIFFERENT record. recordType + recordId must win in BOTH the filter and the
        // handler — a request must not authorize against one record and re-own another.
        var response = await ProvisionAsync(new { projectId = decoyProjectId, recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.DelegationProbes.Should().ContainSingle().Which.Should().Be((entitySet, recordId));
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.OwningTeamOf(decoyProjectId).Should().BeNull("the record that was not authorized must not be touched");
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_WhenTheCallerLacksWriteOnTheRecord_Is403AndNothingChanges(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId);
        _fixture.CallerHoldsWrite = false;

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        AssertNothingWritten(recordId);
    }

    [Fact]
    public async Task Provision_WithAnUnknownRecordType_IsRefusedAndNothingIsProbedOrWritten()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedProject(recordId);

        var response = await ProvisionAsync(new { recordType = "invoice", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the delegation filter cannot resolve a target, and an unresolved target denies (403, not 400, so ids " +
            "cannot be enumerated before authorization)");
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyTargetUnresolved);
        _fixture.DelegationProbes.Should().BeEmpty();
        AssertNothingWritten(recordId);
    }

    [Theory]
    [InlineData("matter", "sprk_matters")]
    [InlineData("workassignment", "sprk_workassignments")]
    public async Task Unsecure_TheDelegationFilterAuthorizesTheSameRootTheHandlerReowns(string recordType, string entitySet)
    {
        var recordId = Guid.NewGuid();
        var decoyProjectId = Guid.NewGuid();
        Seed(recordType, recordId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.SeedProject(decoyProjectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        var client = _fixture.CreateEntitledClient();

        // As for provisioning: the legacy projectId names a DIFFERENT record, and recordType + recordId must win in
        // both the filter and the handler.
        var response = await client.PostAsJsonAsync(
            UnsecureRoute, new { projectId = decoyProjectId, recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.DelegationProbes.Should().ContainSingle().Which.Should().Be((entitySet, recordId));
        _fixture.OwningUserOf(recordId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.OwningTeamOf(decoyProjectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId,
            "the record that was not authorized must not be un-secured");
        _fixture.IsSecureOf(decoyProjectId).Should().BeTrue();
    }

    [Fact]
    public async Task Unsecure_WhenTheCallerLacksWriteOnTheMatter_Is403AndNothingChanges()
    {
        var matterId = Guid.NewGuid();
        _fixture.SeedMatter(matterId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.CallerHoldsWrite = false;
        var client = _fixture.CreateEntitledClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { recordType = "matter", recordId = matterId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        _fixture.Updates.Should().BeEmpty();
        _fixture.Revokes.Should().BeEmpty();
        _fixture.OwningTeamOf(matterId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The projection guard, for the two roots task 144 added
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProvisioningSelect_ForMatterAndWorkAssignment_NamesOnlyColumnsThatExistOnTheirTables()
    {
        Columns(SecureRecordRoot.Matter.ProvisioningSelect)
            .Should().OnlyContain(c => ProvisionProjectTestFixture.LiveMatterColumns.Contains(c));
        Columns(SecureRecordRoot.WorkAssignment.ProvisioningSelect)
            .Should().OnlyContain(c => ProvisionProjectTestFixture.LiveWorkAssignmentColumns.Contains(c),
                "the work assignment's name column is sprk_name — the one root that breaks the {entity}name pattern");
    }

    private static string[] Columns(string select) =>
        select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void Seed(string recordType, Guid id, Guid? owningTeamId = null)
    {
        switch (recordType)
        {
            case "project":
                _fixture.SeedProject(id, owningTeamId: owningTeamId);
                break;
            case "matter":
                _fixture.SeedMatter(id, owningTeamId: owningTeamId);
                break;
            default:
                _fixture.SeedWorkAssignment(id, owningTeamId: owningTeamId);
                break;
        }
    }
}
