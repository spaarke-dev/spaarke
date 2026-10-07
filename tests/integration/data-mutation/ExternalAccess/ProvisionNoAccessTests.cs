using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 143, criterion 9 (owner N6, answered "as recommended"): secure provisioning and the
/// No Access list. A walled CREATOR is refused before the ownership move with nothing moved; a walled named colleague is
/// skipped with a per-person warning while the others are still shared; a resume never shares to a walled creator.
/// </summary>
/// <remarks>
/// The production <c>SecureShareNoAccessGuard</c> runs inside the real endpoint pipeline (<see cref="ProvisionProjectTestFixture"/>),
/// over the fixture's deny-list reader (wire seam only). Placement: <c>data-mutation</c> — provisioning moves ownership
/// and writes shares.
/// </remarks>
public class ProvisionNoAccessTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionNoAccessTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Provision_WhenTheCreatorIsOnTheRecordsNoAccessList_IsRefusedBeforeTheMove_WithNothingChanged()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.NoAccessList.DenySystemUserOnRecord(ProvisionProjectTestFixture.CallerSystemUserId, projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        body.GetProperty("detail").GetString().Should().Contain("No Access list");
        _fixture.Updates.Should().BeEmpty("nothing is moved, unlinked or stamped (owner N6: refuse BEFORE the move)");
        _fixture.Grants.Should().BeEmpty("and nothing is shared");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("and no container is created");
        _fixture.OwningTeamOf(projectId).Should().NotBe(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    [Fact]
    public async Task Provision_WhenTheCreatorIsWalledThroughAnOrganizationTheRecordReferences_IsRefusedToo()
    {
        var projectId = Guid.NewGuid();
        var firm = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.NoAccessReads.RecordOrganizations[projectId] = new[] { firm };
        _fixture.NoAccessList.DenySystemUserOnOrganization(ProvisionProjectTestFixture.CallerSystemUserId, firm);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _fixture.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_WhenTheCreatorsNoAccessCheckCannotBeRead_IsRefusedBeforeAnyChange()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.NoAccessList.Faults = true;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await BodyOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_AWalledNamedColleague_IsSkippedWithAWarning_AndTheOthersAreStillShared()
    {
        var projectId = Guid.NewGuid();
        var walled = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.NoAccessList.DenySystemUserOnRecord(walled, projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { walled, colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == walled, "a walled colleague is never shared to");
        _fixture.Grants.Should().Contain(g => g.Principal.Id == colleague, "the other named colleague still is");
        _fixture.Grants.Should().Contain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);

        var body = await BodyOf(response);
        body.GetProperty("additionalPrincipalsShared").GetInt32().Should().Be(1);
        var skipped = body.GetProperty("skippedPrincipals").EnumerateArray().ToList();
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(walled);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccess);
        skipped[0].GetProperty("message").GetString().Should().Contain("No Access list");
    }

    /// <summary>
    /// unified-access-control-r2 task 114 (owner round 67): the ONE share-eligibility rule — on a RESTRICTED record a named
    /// colleague flagged external is skipped with its own reason code and a per-person message; a blank-flagged colleague is
    /// shared; on a record that is NOT Restricted the same external colleague is shared.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provision_ANamedColleagueFlaggedExternal_IsSkippedOnlyWhenTheRecordIsRestricted(bool restricted)
    {
        var projectId = Guid.NewGuid();
        var external = Guid.NewGuid();
        var blank = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SystemUsers[external] = (false, false);
        _fixture.SystemUsers[blank] = (false, false);
        _fixture.ExternalUsers.Add(external);
        if (restricted)
            _fixture.RestrictedRecords.Add(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { external, blank } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().Contain(g => g.Principal.Id == blank, "a blank sprk_isexternal is not external");
        var skipped = (await BodyOf(response)).GetProperty("skippedPrincipals").EnumerateArray().ToList();
        if (restricted)
        {
            _fixture.Grants.Should().NotContain(g => g.Principal.Id == external, "a Restricted record admits no user flagged external");
            skipped.Should().ContainSingle();
            skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(external);
            skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalExternalOnRestricted);
            skipped[0].GetProperty("message").GetString().Should().Contain("Restricted");
        }
        else
        {
            _fixture.Grants.Should().Contain(g => g.Principal.Id == external, "on an ordinary record an external-flagged user is shared with");
            skipped.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Owner round 67 item 3 (decided 2026-10-06: Restricted wins over the last-reader rule): the CALLER — the person the
    /// record is secured for — is flagged external on a Restricted record. They are NOT shared to, they are named in
    /// skippedPrincipals with principal_external_on_restricted, and the record is still provisioned (secured, its own
    /// container) with the response saying plainly that nobody internal can open it.
    /// </summary>
    [Fact]
    public async Task Provision_ByACallerFlaggedExternal_OnARestrictedRecord_SkipsThem_AndStillProvisions_SayingNobodyInternalCanOpenIt()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.ExternalUsers.Add(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.RestrictedRecords.Add(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId, "the record is still secured");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "a Restricted record admits no user flagged external — not even the person it is secured for");

        var body = await BodyOf(response);
        var skipped = body.GetProperty("skippedPrincipals").EnumerateArray().ToList();
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalExternalOnRestricted);
        body.GetProperty("noInternalReader").GetBoolean().Should().BeTrue();
        body.GetProperty("noInternalReaderMessage").GetString().Should().Contain("An administrator must share it with an internal user");
    }

    /// <summary>The twin: an INTERNAL colleague named on the same Restricted call is shared, so someone internal remains.</summary>
    [Fact]
    public async Task Provision_ByACallerFlaggedExternal_OnARestrictedRecord_WithAnInternalColleague_ReportsAnInternalReader()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SystemUsers[colleague] = (false, false);
        _fixture.ExternalUsers.Add(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.RestrictedRecords.Add(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().Contain(g => g.Principal.Id == colleague);
        var body = await BodyOf(response);
        body.GetProperty("noInternalReader").GetBoolean().Should().BeFalse();
        body.TryGetProperty("noInternalReaderMessage", out var message).Should().BeTrue();
        message.ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Task 114 verifier V6: every creator-share path on a Restricted record, with an external-flagged person ──

    /// <summary>
    /// The RESUME (EnsureResumeCreatorShareAsync): a record the owner team owns with no container, whose creator is flagged
    /// external and which is Restricted, is finished — but the creator is NOT shared to, is not reported as shared to
    /// (sharedToCreatorSystemUserId is the empty GUID), is named in skippedPrincipals, and nobody internal can open it. The
    /// twin (an internal creator) is shared to as before.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resume_ACreatorFlaggedExternal_OnARestrictedRecord_IsNotShared_AndTheResumeFinishes(bool creatorIsExternal)
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        if (creatorIsExternal)
            _fixture.ExternalUsers.Add(creator);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, containerId: null,
            createdBy: creator);
        _fixture.RestrictedRecords.Add(projectId);
        using var client = _fixture.CreateEntitledClient(); // the caller is NOT the creator

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        var body = await BodyOf(response);
        body.GetProperty("resumed").GetBoolean().Should().BeTrue();
        var skipped = body.GetProperty("skippedPrincipals").EnumerateArray().ToList();
        if (creatorIsExternal)
        {
            _fixture.Grants.Should().NotContain(g => g.Principal.Id == creator, "a Restricted record admits no user flagged external");
            body.GetProperty("sharedToCreatorSystemUserId").GetGuid().Should().Be(Guid.Empty, "nobody was shared to");
            skipped.Should().ContainSingle();
            skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(creator);
            skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalExternalOnRestricted);
            body.GetProperty("noInternalReader").GetBoolean().Should().BeTrue();
        }
        else
        {
            _fixture.ShareMaskOf(projectId, creator).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
            body.GetProperty("sharedToCreatorSystemUserId").GetGuid().Should().Be(creator);
            skipped.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The MAKE SECURE resume (ResumeMakeSecureAsync): a record the owner team owns with no container, finished by a Write
    /// holder who is flagged external, on a Restricted record. The caller is NOT shared to and is named; the record's
    /// internal creator IS shared to, so someone internal can open it.
    /// </summary>
    [Fact]
    public async Task MakeSecureResume_ByACallerFlaggedExternal_OnARestrictedRecord_SkipsTheCaller_AndSharesTheInternalCreator()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(projectId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: creator);
        _fixture.ExternalUsers.Add(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.RestrictedRecords.Add(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { recordType = "project", recordId = projectId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "a Restricted record admits no user flagged external — not even the Make Secure caller");
        _fixture.ShareMaskOf(projectId, creator).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        var body = await BodyOf(response);
        body.GetProperty("sharedToCreatorSystemUserId").GetGuid().Should().Be(Guid.Empty);
        var skipped = body.GetProperty("skippedPrincipals").EnumerateArray().ToList();
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalExternalOnRestricted);
        body.GetProperty("noInternalReader").GetBoolean().Should().BeFalse("the internal creator was shared to");
    }

    /// <summary>
    /// Verifier V2 — the UNVERIFIED owner move: the creator is flagged external on a Restricted record, so nothing was
    /// shared to them. The answer never says the share is in place: creatorShareConfirmed is false, the reason the share
    /// was skipped is principal_external_on_restricted, and the text says plainly it was NOT shared.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheOwnerMoveIsUnverified_AndTheCreatorIsFlaggedExternalOnARestrictedRecord_SaysItWasNotShared()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.ExternalUsers.Add(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.RestrictedRecords.Add(projectId);
        _fixture.OwnerReadBackFails = true;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        body.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeFalse("nobody was shared to");
        body.GetProperty("creatorShareSkippedReason").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonPrincipalExternalOnRestricted);
        body.GetProperty("detail").GetString().Should().Contain("NOT shared").And.NotContain("is in place");
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "no share, and no last-resort share either");
    }

    /// <summary>
    /// Verifier V1 — the last-resort share after an UNVERIFIED move is written only when a FRESH read says the person is not
    /// barred. Here whether the creator is flagged external on the Restricted record cannot be read at all: nothing is
    /// written (no unconfirmed share to someone who may be external), and the answer is that no share could be issued.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheOwnerMoveIsUnverified_AndTheCreatorsExternalFlagCannotBeRead_IssuesNoLastResortShare()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.RestrictedRecords.Add(projectId);
        _fixture.OwnerReadBackFails = true;
        _fixture.SystemUserReadFailsFor = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "an unreadable flag on a Restricted record is no licence for an unconfirmed share");
    }

    [Fact]
    public async Task Provision_ANamedColleagueWhoseNoAccessCheckCannotBeRead_IsSkippedToo_AndTheOthersAreStillShared()
    {
        // Criterion 9, the fail-closed half (task 143 r1, seed S20): "could not tell" is never "not walled". The
        // colleague's link read fails, so the guard answers Unverifiable — that colleague is skipped with its own reason
        // code, while the creator and the other colleague are still shared.
        var projectId = Guid.NewGuid();
        var unverifiable = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.UnreadableLinkUsers.Add(unverifiable);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { unverifiable, colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == unverifiable, "an unverifiable colleague is never shared to");
        _fixture.Grants.Should().Contain(g => g.Principal.Id == colleague);
        _fixture.Grants.Should().Contain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);

        var skipped = (await BodyOf(response)).GetProperty("skippedPrincipals").EnumerateArray().ToList();
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(unverifiable);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccessUnverifiable);
    }

    [Fact]
    public async Task Resume_WhenTheRecordsCreatorIsOnItsNoAccessList_SharesNothing_AndWritesNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.NoAccessList.DenySystemUserOnRecord(ProvisionProjectTestFixture.CallerSystemUserId, projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorNoAccess);
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }
}
