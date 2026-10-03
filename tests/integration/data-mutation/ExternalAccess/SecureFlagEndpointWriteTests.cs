using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 150 — <c>sprk_issecure</c> is field-secured, so the secure/unsecure endpoints are its
/// ONLY writers: provisioning sets it (first write, read back), unsecure clears it (last), and only the people owner
/// decision F3 names may clear it.
/// </summary>
/// <remarks>
/// <para><b>The contract, closed.</b> (1) Provisioning accepts an UNFLAGGED record (the new client never writes the flag)
/// and one already FLAGGED but not provisioned (the old client, rows from before task 150) and treats both identically;
/// the flag is its first write, read back; every refusal before it leaves the record unflagged; a failed write or a
/// read-back that is not <c>true</c> stops it with nothing else written; compensation never clears it. (2) Unsecure is
/// open only to a Full Access holder (Write + Delete) or the record's creator (<c>createdby</c>, or the stamped
/// <c>sprk_createdbyperson</c>); any other Write holder is refused 403 with nothing written; an EMPTY flag is refused,
/// never answered "already not secure". (3) A caller without Write is refused by both, on all three root types.</para>
///
/// <para><b>What an empty flag models.</b> Dataverse returns a field-secured column the reading identity has no Read on
/// as EMPTY rather than refusing the read (<see cref="ProvisionProjectTestFixture.SecureFlagReadsEmpty"/>). Under task
/// 150's invariant every row holds true or false, so an empty value is the BFF having lost its field-level Read.</para>
/// </remarks>
[Trait("Category", "Security")]
public class SecureFlagEndpointWriteTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureFlagEndpointWriteTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    private Task<HttpResponseMessage> PostAsync(string route, object body)
        => _fixture.CreateEntitledClient().PostAsJsonAsync(route, body);

    private void Seed(string recordType, Guid id, bool isSecure, Guid? owningTeamId = null, string? containerId = null,
        Guid? createdBy = null, Guid? createdByPerson = null)
    {
        switch (recordType)
        {
            case "project":
                _fixture.SeedProject(id, owningTeamId: owningTeamId, containerId: containerId, isSecure: isSecure,
                    createdBy: createdBy, createdByPerson: createdByPerson);
                break;
            case "matter":
                _fixture.SeedMatter(id, owningTeamId, containerId, isSecure, createdBy, createdByPerson);
                break;
            default:
                _fixture.SeedWorkAssignment(id, owningTeamId, containerId, isSecure, createdBy, createdByPerson);
                break;
        }
    }

    private IEnumerable<ProvisionProjectTestFixture.RecordedUpdate> FlagWrites(Guid recordId)
        => _fixture.Updates.Where(u => u.RecordId == recordId && u.Payload.ContainsKey("sprk_issecure"));

    // ═════════════════════════════════════════════════════════════════════════
    // Provisioning sets the flag — first, read back, and both arrival shapes alike
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnUnflaggedRecord_SetsTheFlagAsItsFirstWrite(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();

        var flagWrite = FlagWrites(recordId).Should().ContainSingle().Subject;
        flagWrite.Payload["sprk_issecure"].Should().Be(bool.TrueString);

        // Every other write — the owner move, the container link, every share — comes after it.
        var otherSequences = _fixture.Updates.Where(u => u != flagWrite).Select(u => u.Sequence)
            .Concat(_fixture.Grants.Select(g => g.Sequence))
            .Concat(_fixture.Modifies.Select(m => m.Sequence))
            .ToArray();
        otherSequences.Should().NotBeEmpty();
        otherSequences.Should().OnlyContain(s => s > flagWrite.Sequence,
            "the flag is the FIRST write: from it on, every failure leaves a flagged record whose uploads are refused");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnAlreadyFlaggedRecord_IsProvisionedTheSameWay_WithoutWritingTheFlagAgain(string recordType)
    {
        var flagged = Guid.NewGuid();
        var unflagged = Guid.NewGuid();
        Seed(recordType, flagged, isSecure: true);
        Seed(recordType, unflagged, isSecure: false);

        var flaggedResponse = await PostAsync(ProvisionRoute, new { recordType, recordId = flagged });
        var unflaggedResponse = await PostAsync(ProvisionRoute, new { recordType, recordId = unflagged });

        flaggedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unflaggedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        FlagWrites(flagged).Should().BeEmpty("a record that already reads true is not written again");

        foreach (var id in new[] { flagged, unflagged })
        {
            _fixture.IsSecureOf(id).Should().BeTrue();
            _fixture.OwningTeamOf(id).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
            _fixture.ContainerIdOf(id).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
            _fixture.ShareMaskOf(id, ProvisionProjectTestFixture.CallerSystemUserId)
                .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        }
    }

    [Fact]
    public async Task Provision_AnAlreadyProvisionedRecord_StillAnswers409_AndWritesNothing()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            containerId: "b!its-own-container");

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_WhenTheFlagWriteIsRefused_StopsWithNothingElseWritten()
    {
        var matterId = Guid.NewGuid();
        Seed("matter", matterId, isSecure: false);
        _fixture.SecureFlagWriteFails = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId = matterId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureFlagNotSet);
        _fixture.Updates.Should().ContainSingle("the flag write was the only write attempted")
            .Which.Payload.Should().ContainKey("sprk_issecure");
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningUserOf(matterId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
    }

    [Fact]
    public async Task Provision_WhenTheFlagWriteIsAcceptedButDoesNotReadBackTrue_StopsWithNothingElseWritten()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: false);
        _fixture.SecureFlagWriteNotApplied = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureFlagNotSet);
        _fixture.IsSecureOf(projectId).Should().BeFalse();
        _fixture.Updates.Should().ContainSingle();
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The BFF lost its field-level Read: Step 1 reads the flag EMPTY, the write may land, and the read-back is empty
    /// too. An empty read-back is not success — the run stops before the owner move.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheFlagReadsBackEmpty_StopsBeforeTheOwnerMove()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: false);
        _fixture.SecureFlagReadsEmpty = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureFlagNotSet);
        _fixture.OwningTeamOf(projectId).Should().BeNull("the record was never moved");
        _fixture.Grants.Should().BeEmpty();
    }

    /// <summary>A refusal BEFORE the flag write leaves the record unflagged — "nothing changed" stays true.</summary>
    [Fact]
    public async Task Provision_RefusedBeforeAnyWrite_LeavesTheRecordUnflagged()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: false);
        _fixture.SecureBuMatchCount = 0;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureBuNotFound);
        _fixture.Updates.Should().BeEmpty();
        _fixture.IsSecureOf(projectId).Should().BeFalse();
    }

    /// <summary>Compensation restores ownership and shares — never the flag.</summary>
    [Fact]
    public async Task Provision_WhenTheMoveIsUndone_TheFlagStaysSet()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: false);
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;
        _fixture.AssignDropsShareOf = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId, "the move was undone");
        _fixture.IsSecureOf(projectId).Should().BeTrue(
            "un-flagging a record the user asked to secure would route its next upload to shared storage");
        FlagWrites(projectId).Should().OnlyContain(u => u.Payload["sprk_issecure"] == bool.TrueString);
    }

    /// <summary>A RESUME (owned by the team, no container) of a record whose flag is not set sets it before any share.</summary>
    [Fact]
    public async Task Provision_ResumingAnUnflaggedRecord_SetsTheFlagBeforeTheShare()
    {
        var projectId = Guid.NewGuid();
        Seed("project", projectId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(projectId).Should().BeTrue();
        var flagWrite = FlagWrites(projectId).Should().ContainSingle().Subject;
        _fixture.Grants.Concat(_fixture.Modifies).Should().OnlyContain(s => s.Sequence > flagWrite.Sequence);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("workassignment")]
    public async Task Provision_WhenTheCallerLacksWrite_Is403AndTheFlagIsNotWritten(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false);
        _fixture.CallerHoldsWrite = false;

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        _fixture.Updates.Should().BeEmpty();
        _fixture.IsSecureOf(recordId).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Unsecure — owner round 3b F3: Full Access holders and the creator only
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly Guid Colleague = Guid.Parse("c0000000-0000-0000-0000-0000000c0111");
    private static readonly Guid BffApplicationUser = Guid.Parse("a0000000-0000-0000-0000-0000000000b1");

    private void AssertStillSecure(Guid recordId)
    {
        _fixture.Updates.Should().BeEmpty("the refusal comes before any write");
        _fixture.Revokes.Should().BeEmpty();
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Unsecure_ByAWriteHolderWhoIsNeitherFullAccessNorTheCreator_Is403AndTheRecordStaysSecure(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);

        var response = await PostAsync(UnsecureRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonNotPermitted);
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("detail").GetString().Should()
                .Contain("Full Access").And.Contain("the person who created it",
                    "the ribbon shows this message as is, so it must name who CAN remove the designation");
        }

        AssertStillSecure(recordId);
    }

    [Fact]
    public async Task Unsecure_ByAFullAccessHolderWhoDidNotCreateIt_Succeeds()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.CallerHoldsDelete = true;

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeFalse();
    }

    [Fact]
    public async Task Unsecure_ByTheCreator_Succeeds_WithoutAskingForTheirRights()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeFalse();
        _fixture.DelegationProbes.Should().ContainSingle("only the route's Write gate asked; the creator needs no rights probe");
    }

    /// <summary>An app-created record: createdby is the BFF application user, the person is <c>sprk_createdbyperson</c>.</summary>
    [Fact]
    public async Task Unsecure_ByThePersonRecordedAsCreator_OnAnAppCreatedRecord_Succeeds()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: BffApplicationUser, createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);

        var response = await PostAsync(UnsecureRoute, new { recordType = "workassignment", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeFalse();
    }

    [Fact]
    public async Task Unsecure_WhenTheRecordedCreatorPersonCannotBeRead_RefusesBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: BffApplicationUser, createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.CreatorPersonReadFailsWith = HttpStatusCode.ServiceUnavailable;

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonPermissionUnverifiable);
        AssertStillSecure(recordId);
    }

    /// <summary>Before the creator-person schema runs, the column records nobody: no one is admitted by it.</summary>
    [Fact]
    public async Task Unsecure_WhenTheCreatorPersonColumnIsMissing_AdmitsNobodyThroughIt()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.CreatorPersonColumnExists = false;

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonNotPermitted);
        AssertStillSecure(recordId);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("workassignment")]
    public async Task Unsecure_WhenTheCallerLacksWrite_Is403AndNothingChanges(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.CallerHoldsWrite = false;

        var response = await PostAsync(UnsecureRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        AssertStillSecure(recordId);
    }

    /// <summary>
    /// The masked flag on the reverse path: before task 150 an EMPTY flag answered 200 "already not secure" and skipped
    /// everything — on a record that may well be secure.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenTheFlagReadsEmpty_RefusesRatherThanAnsweringAlreadyNotSecure()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.SecureFlagReadsEmpty = true;

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonSecureFlagUnreadable);
        AssertStillSecure(recordId);
    }
}
