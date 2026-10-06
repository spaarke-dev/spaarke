using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
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

    private static async Task<string?> DetailOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    /// <summary>The lower-case record label the copy names (<c>SecureRecordRoot.DisplayLabel</c>).</summary>
    private static string LabelOf(string recordType) => recordType == "workassignment" ? "work assignment" : recordType;

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

    /// <summary>
    /// A RESUME (owned by the team, no container) of a record whose flag is not set — by its creator, the only caller
    /// the round-10 rule admits for an unflagged record — sets it before any share.
    /// </summary>
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

    // ─────────────────────────────────────────────────────────────────────────
    // Task 150 r2 (verifier finding F1): the flag comes before the Step 4.2 shared-container UNLINK too. The first-write
    // test above seeds no container, so no unlink happens there; a reorder that moved the flag after the unlink passed
    // every test. If it regressed, a record whose shared container was unlinked and whose flag write then failed would be
    // left unflagged and containerless, under a response that says nothing changed.
    // ─────────────────────────────────────────────────────────────────────────

    private const string BusinessUnitSharedContainer = "b!business-unit-shared";

    /// <summary>Seeds an unflagged project recording a SHARED container: a business unit's, or one this BFF is configured with.</summary>
    private string SeedRecordingASharedContainer(Guid projectId, string sharedKind)
    {
        var shared = sharedKind == "businessUnit"
            ? BusinessUnitSharedContainer
            : ProvisionProjectTestFixture.ConfiguredArchiveContainerId;
        if (sharedKind == "businessUnit")
            _fixture.BusinessUnitContainers[Guid.NewGuid()] = shared;
        _fixture.SeedProject(projectId, containerId: shared, isSecure: false);
        return shared;
    }

    private IEnumerable<ProvisionProjectTestFixture.RecordedUpdate> ContainerUnlinks(Guid recordId)
        => _fixture.Updates.Where(u => u.RecordId == recordId
                                       && u.Payload.TryGetValue("sprk_containerid", out var value)
                                       && value is null);

    [Theory]
    [InlineData("businessUnit")]
    [InlineData("configured")]
    public async Task Provision_ARecordCarryingASharedContainer_SetsTheFlagBeforeUnlinkingIt(string sharedKind)
    {
        var projectId = Guid.NewGuid();
        SeedRecordingASharedContainer(projectId, sharedKind);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var flagWrite = FlagWrites(projectId).Should().ContainSingle().Subject;
        var unlink = ContainerUnlinks(projectId).Should().ContainSingle("the shared container is unlinked once").Subject;
        unlink.Sequence.Should().BeGreaterThan(flagWrite.Sequence,
            "the flag is the FIRST write: the unlink comes after it, so a failed flag write leaves the shared link in place");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    [Theory]
    [InlineData("businessUnit")]
    [InlineData("configured")]
    public async Task Provision_ARecordCarryingASharedContainer_WhenTheFlagWriteIsRefused_NeverUnlinksIt(string sharedKind)
    {
        var projectId = Guid.NewGuid();
        var shared = SeedRecordingASharedContainer(projectId, sharedKind);
        _fixture.SecureFlagWriteFails = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureFlagNotSet);
        ContainerUnlinks(projectId).Should().BeEmpty("the unlink comes after the flag, and the flag write failed");
        _fixture.Updates.Should().ContainSingle("the flag write was the only write attempted")
            .Which.Payload.Should().ContainKey("sprk_issecure");
        _fixture.ContainerIdOf(projectId).Should().Be(shared, "the record keeps the link it arrived with");
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
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
    // Provision — owner round 10 item 10: an UNFLAGGED record is secured only for its creator
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly Guid Colleague = Guid.Parse("c0000000-0000-0000-0000-0000000c0111");
    private static readonly Guid BffApplicationUser = Guid.Parse("a0000000-0000-0000-0000-0000000000b1");

    /// <summary>Nothing at all was written: no flag, no owner move, no share, no container.</summary>
    private void AssertNothingWritten(Guid recordId)
    {
        _fixture.Updates.Should().BeEmpty("the refusal comes before any write — the flag included");
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.IsSecureOf(recordId).Should().BeFalse();
        _fixture.OwningUserOf(recordId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId, "never moved");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);   // a person created it

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the caller passed the Write gate but is not the creator");
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonNotRecordCreator);
        // Owner round 13 item 10 (F6 row 9): option B, verbatim.
        (await DetailOf(response)).Should().Be(
            $"Only the person who created this {LabelOf(recordType)} can secure it this way, and you did not create it. " +
            "Nothing was changed.");
        AssertNothingWritten(recordId);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnUnflaggedRecord_ByItsCreator_IsProvisioned(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, createdBy: ProvisionProjectTestFixture.CallerSystemUserId);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    /// <summary>An app-only create (Office quick-create): createdby is the BFF, the person is <c>sprk_createdbyperson</c>.</summary>
    [Fact]
    public async Task Provision_AnUnflaggedAppCreatedRecord_ByThePersonRecordedAsItsCreator_IsProvisioned()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, createdBy: BffApplicationUser,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
    }

    [Fact]
    public async Task Provision_AnUnflaggedAppCreatedRecord_RecordedForSomeoneElse_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: BffApplicationUser, createdByPerson: Colleague);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonNotRecordCreator);
        AssertNothingWritten(recordId);
    }

    /// <summary>
    /// "createdby when human": a PERSON in createdby is the creator even when disabled — a <c>sprk_createdbyperson</c> naming
    /// the caller does not override it (only an app-only create defers to that column).
    /// </summary>
    [Fact]
    public async Task Provision_AnUnflaggedRecord_CreatedByADisabledPerson_IsNotSecuredForTheRecordedPerson()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: false, createdBy: Colleague,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SystemUsers[Colleague] = (true, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "workassignment", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonNotRecordCreator);
        AssertNothingWritten(recordId);
    }

    [Fact]
    public async Task Provision_AnUnflaggedRecord_WhoseCreatorCannotBeRead_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.SystemUserReadFailsFor = Colleague;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable,
            "an unreadable creator is never read as 'the caller created it'");
        // Owner round 13 item 10 (F6 row 10): option B, verbatim.
        (await DetailOf(response)).Should().Be(
            "Who created this project could not be looked up, so whether you may secure it could not be checked. " +
            "Nothing was changed; you may try again.");
        AssertNothingWritten(recordId);
    }

    [Fact]
    public async Task Provision_AnUnflaggedAppCreatedRecord_WhoseRecordedPersonCannotBeRead_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, createdBy: BffApplicationUser,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);
        _fixture.CreatorPersonReadFailsWith = HttpStatusCode.ServiceUnavailable;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable);
        AssertNothingWritten(recordId);
    }

    private static async Task<string?> ExtensionOf(HttpResponseMessage response, string name)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>
    /// Before the creator-person schema runs, the column records nobody, so nobody is admitted through it — and the
    /// answer is UNVERIFIABLE, not "you did not create it" (round 17 item 1, 2026-10-03: one rule for one environment
    /// fact, aligned with task 146's F3 helper). 403, deterministic (<c>creatorState: column-missing</c>: no retry), the
    /// administrator named; nothing written.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnUnflaggedAppCreatedRecord_WhenTheCreatorPersonColumnIsMissing_IsUnverifiable(
        string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, createdBy: BffApplicationUser);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);
        _fixture.CreatorPersonColumnExists = false;

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable,
            "a column this environment lacks proves nobody is or is not the creator");
        (await ExtensionOf(response, "creatorState")).Should().Be("column-missing");
        (await DetailOf(response)).Should().Be(
            $"Who created this {LabelOf(recordType)} is not recorded in this environment, so whether you may secure it " +
            "could not be checked. Nothing was changed; an administrator needs to finish setting up the environment.");
        AssertNothingWritten(recordId);
    }

    /// <summary>The same rule on an unflagged RESUME (the record already owned by the Secure Record owner team).</summary>
    [Fact]
    public async Task Provision_ResumingAnUnflaggedAppCreatedRecord_WhenTheCreatorPersonColumnIsMissing_IsUnverifiable()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: BffApplicationUser);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);
        _fixture.CreatorPersonColumnExists = false;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable);
        (await ExtensionOf(response, "creatorState")).Should().Be("column-missing");
        AssertResumeWroteNothing(recordId);
    }

    /// <summary>
    /// The missing column changes nothing when <c>createdby</c> already answers: a PERSON who is not the caller is a
    /// definite "no" (the column is never read), and the caller as <c>createdby</c> is admitted.
    /// </summary>
    [Fact]
    public async Task Provision_WhenTheCreatorPersonColumnIsMissing_ACreatedByPersonStillDecides()
    {
        var colleagues = Guid.NewGuid();
        Seed("project", colleagues, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.CreatorPersonColumnExists = false;

        var refused = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = colleagues });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(refused)).Should().Be(ProvisionProjectEndpoint.ReasonNotRecordCreator);

        var own = Guid.NewGuid();
        Seed("project", own, isSecure: false, createdBy: ProvisionProjectTestFixture.CallerSystemUserId);

        var admitted = await PostAsync(ProvisionRoute, new { recordType = "project", recordId = own });

        admitted.StatusCode.Should().Be(HttpStatusCode.OK, await admitted.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A record ALREADY flagged (an older client flagged it at create; a row from before task 150) stays on the route's
    /// Write gate: a Write holder who did not create it still provisions it (owner round 10 item 10).
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_AnAlreadyFlaggedRecord_ByAWriteHolderWhoDidNotCreateIt_StaysOnTheWriteGate(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: true, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.DelegationProbes.Should().NotBeEmpty("the route's Write gate is still what admits the caller");
    }

    /// <summary>
    /// Verifier c1 item 7 — "createdby when human": an application user in <c>createdby</c> is not a person even when it is
    /// DISABLED, so the BFF-stamped <c>sprk_createdbyperson</c> decides (here: the caller).
    /// </summary>
    [Fact]
    public async Task Provision_AnUnflaggedRecord_CreatedByADisabledApplicationUser_IsSecuredForThePersonRecordedAsItsCreator()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: BffApplicationUser,
            createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SystemUsers[BffApplicationUser] = (true, true);   // a disabled APPLICATION user

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Verifier c1 item 4: the round-10 rule names an UNFLAGGED record, and a RESUME would mark one secure too (it writes
    // the flag before its share). So an unflagged resume admits only the creator, before any write; a flagged resume —
    // every documented recovery meets one — stays on the Write gate.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Nothing at all was written by a refused resume: no flag, no share, no container, owner unchanged.</summary>
    private void AssertResumeWroteNothing(Guid recordId)
    {
        _fixture.Updates.Should().BeEmpty("the refusal comes before any write — the flag included");
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.IsSecureOf(recordId).Should().BeFalse();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId, "never moved");
        _fixture.ContainerIdOf(recordId).Should().BeNull();
    }

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_ResumingAnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite(
        string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);   // a person created it

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonNotRecordCreator);
        AssertResumeWroteNothing(recordId);
    }

    [Fact]
    public async Task Provision_ResumingAnUnflaggedRecord_WhenTheCallerCannotBeIdentified_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.CallerSystemUserIdResolves = false;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved,
            "an unidentified caller is never read as the creator");
        // Owner round 13 item 10 (F6 row 11): option B, verbatim.
        (await DetailOf(response)).Should().Be(
            "Your account could not be confirmed, so whether you created this matter could not be checked. Nothing was " +
            "changed; you may try again.");
        AssertResumeWroteNothing(recordId);
    }

    [Fact]
    public async Task Provision_ResumingAnUnflaggedAppCreatedRecord_ByThePersonRecordedAsItsCreator_Resumes()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: BffApplicationUser, createdByPerson: ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);

        var response = await PostAsync(ProvisionRoute, new { recordType = "workassignment", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// A FLAGGED resume (the shape every documented recovery meets) by a Write holder who did not create it stays on the
    /// Write gate, and the share still goes to the creator — never the caller (F8).
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_ResumingAFlaggedRecord_ByAWriteHolderWhoDidNotCreateIt_StaysOnTheWriteGate(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        FlagWrites(recordId).Should().BeEmpty("a record that already reads true is not written again");
        // F8 holds WITHOUT the transition (the wizards' and an administrator's API path): the caller is never added.
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "a resume without the Make Secure transition shares to the record's creator only (F8)");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Provision — round 33 item 1: Make Secure (transition "make-secure") is held to the Write gate
    //
    // The wizards' create-then-secure path (no transition) keeps the creator rule: its non-creator refusals are the
    // tests above (Provision_AnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite and the resume
    // twin) — the same callers and records these tests admit with the transition.
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<List<JsonElement>> SkippedOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("skippedPrincipals", out var skipped) && skipped.ValueKind == JsonValueKind.Array
            ? skipped.EnumerateArray().Select(e => e.Clone()).ToList()
            : new List<JsonElement>();
    }

    private static async Task<int> AdditionalSharedOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("additionalPrincipalsShared").GetInt32();
    }

    /// <summary>
    /// The form's Make Secure command secures an EXISTING record (task 148's surface): a Write holder who did not create it
    /// succeeds (owner R3b). Afterwards exactly what owner round 27's copy says holds: the person who created it keeps
    /// access (shared to by the call), the caller keeps access as one of the people it is shared with (shared to, as on
    /// every forward run), and the business unit's ownership is gone.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_MakeSecure_ByAWriteHolderWhoDidNotCreateIt_SecuresItAndSharesItToTheCreator(string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);   // a person created it

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
            "the creator keeps access, as the confirmation says");
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "the caller is one of the people it is shared with");
        (await AdditionalSharedOf(response)).Should().Be(1, "the creator is counted beside the caller");
        (await SkippedOf(response)).Should().BeEmpty();
    }

    /// <summary>The creator using Make Secure is shared to once — as the caller — and nobody else is added.</summary>
    [Fact]
    public async Task Provision_MakeSecure_ByTheCreator_SharesOnlyToThem()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false);   // created by the caller

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Where(g => g.RecordId == recordId).Select(g => g.Principal).Distinct()
            .Should().BeEquivalentTo(new[] { DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId) });
        (await AdditionalSharedOf(response)).Should().Be(0);
    }

    /// <summary>
    /// Only the exact token relaxes the creator rule. Any other value — another spelling, another case, padding, empty — is
    /// refused 400 before any read or write: an unknown surface never falls back to either rule.
    /// </summary>
    [Theory]
    [InlineData("make_secure")]
    [InlineData("secure")]
    [InlineData("anything-else")]
    [InlineData("Make-Secure")]
    [InlineData(" make-secure")]
    [InlineData("make-secure ")]
    [InlineData("")]
    public async Task Provision_WithAnUnrecognisedTransition_IsRefused400BeforeAnyWrite(string transition)
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unknown surface never falls back to either rule");
        AssertNothingWritten(recordId);
    }

    /// <summary>
    /// Make Secure names no colleagues: a Write holder who did not create the record must not widen its explicit access list
    /// through the application identity (Manage Access applies the eligibility and grantor checks). Refused before any write.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_NamingColleagues_IsRefused400BeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute,
            new { recordType = "project", recordId, transition = "make-secure", sharePrincipalIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertNothingWritten(recordId);
    }

    /// <summary>Who created the record could not be read: refused before any write — never secured with its creator locked out.</summary>
    [Fact]
    public async Task Provision_MakeSecure_WhenTheCreatorCannotBeRead_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.SystemUserReadFailsFor = Colleague;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable);
        AssertNothingWritten(recordId);
    }

    /// <summary>
    /// An app-created record in an environment without <c>sprk_createdbyperson</c>: who created it cannot be known, so it is
    /// not secured (it could lock its creator out) — the same 403 and <c>creatorState</c> the creator rule answers.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_OfAnAppCreatedRecord_WhenTheCreatorPersonColumnIsMissing_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: false, createdBy: BffApplicationUser);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);
        _fixture.CreatorPersonColumnExists = false;

        var response = await PostAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable);
        (await ExtensionOf(response, "creatorState")).Should().Be("column-missing");
        AssertNothingWritten(recordId);
    }

    /// <summary>An app-only create: the person the BFF recorded in <c>sprk_createdbyperson</c> is the creator shared to.</summary>
    [Fact]
    public async Task Provision_MakeSecure_OfAnAppCreatedRecord_SharesItToThePersonRecordedAsItsCreator()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: false, createdBy: BffApplicationUser, createdByPerson: Colleague);
        _fixture.SystemUsers[BffApplicationUser] = (false, true);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.Grants.Where(g => g.RecordId == recordId).Select(g => g.Principal)
            .Should().NotContain(DataversePrincipalRef.User(BffApplicationUser), "an application user is never shared to");
    }

    /// <summary>A creator who can no longer use the record (disabled) is not shared to; securing still succeeds.</summary>
    [Fact]
    public async Task Provision_MakeSecure_WithADisabledCreator_SecuresItWithoutSharingToThem()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (true, false);   // disabled

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.Grants.Where(g => g.RecordId == recordId).Select(g => g.Principal)
            .Should().NotContain(DataversePrincipalRef.User(Colleague));
        (await SkippedOf(response)).Should().BeEmpty("there is nobody to keep access through that clause");
    }

    /// <summary>
    /// The creator is on the record's No Access list: No Access wins (owner N6) — not shared to, and NAMED in the response so
    /// the caller is told (never silent). The record is still secured, for the caller.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_WhenTheCreatorIsOnTheNoAccessList_SkipsThemWithAPerPersonWarning()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, recordId);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == Colleague, "a walled creator is never shared to");
        var skipped = await SkippedOf(response);
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(Colleague);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccess);
    }

    /// <summary>
    /// The share to the creator fails: the record is secured and shared to the caller, and the creator is NAMED with
    /// <c>principal_share_failed</c> (round 33 item 5: never silent) — the caller adds them through Manage Access.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_WhenTheShareToTheCreatorFails_NamesThemInTheResponse()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.FailShareForPrincipal = Colleague;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        (await AdditionalSharedOf(response)).Should().Be(0);
        var skipped = await SkippedOf(response);
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(Colleague);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalShareFailed);
        skipped[0].GetProperty("message").GetString().Should().Contain("Manage Access");
    }

    /// <summary>
    /// Make Secure meeting an UNFLAGGED record already owned by the owner team (an earlier run stopped after the move, or a
    /// manual Assign): a Write holder who did not create it resumes it — the Write gate, as on the forward path — and, as on
    /// the forward path (round 40 item 2: the asymmetry is removed), the CALLER is shared at the creator's level and the
    /// record's creator beside them. Without the transition the same call is refused
    /// (Provision_ResumingAnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite).
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_MakeSecure_ResumingAnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_SharesTheCallerAndTheCreator(
        string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeTrue();
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
            "the creator keeps access, as the confirmation says");
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
                "a non-creator who runs Make Secure keeps access on BOTH paths (round 40 item 2)");
        (await AdditionalSharedOf(response)).Should().Be(1, "the creator is counted beside the caller, as on the forward path");
        (await SkippedOf(response)).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Round 40 item 1: a Make Secure that failed after its first write is finished by the SAME command. Every failure
    // between the flag and Step 7 leaves a FLAGGED record (the flag is never cleared) that the owner team owns with no
    // container — the shape below. The retry is a resume with the forward Make Secure rules, not the resume's own (F8)
    // rules, which would refuse the records the forward path secured anyway.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The R-150-6 retry: a FLAGGED record the owner team owns with no container (an earlier Make Secure stopped at Step 6
    /// or 7). The same non-creator Write holder finishes it; they and the record's creator each hold the creator's level.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Provision_MakeSecure_FinishingAFlaggedRecord_ByAWriteHolderWhoDidNotCreateIt_SharesTheCallerAndTheCreator(
        string recordType)
    {
        var recordId = Guid.NewGuid();
        Seed(recordType, recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType, recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        FlagWrites(recordId).Should().BeEmpty("the earlier run wrote the flag; it is not written again");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharedToCreatorSystemUserId").GetGuid()
            .Should().Be(ProvisionProjectTestFixture.CallerSystemUserId, "on Make Secure the proven share is the caller's");
    }

    /// <summary>
    /// The forward path secured a record whose creator is on its No Access list (creator skipped, named) and then stopped at
    /// Step 6. The retry finishes it with the SAME rule — never the resume's 409 <c>resume_creator_no_access</c>, which would
    /// leave the record unfinishable from the command that started it.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecordWhoseCreatorIsOnTheNoAccessList_FinishesAndNamesThem()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, recordId);

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == Colleague, "a walled creator is never shared to");
        var skipped = await SkippedOf(response);
        skipped.Should().ContainSingle();
        skipped[0].GetProperty("systemUserId").GetGuid().Should().Be(Colleague);
        skipped[0].GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonPrincipalNoAccess);
    }

    /// <summary>
    /// A disabled creator: the forward path secures without sharing to them, and so does the retry — never the resume's 409
    /// <c>resume_creator_unavailable</c>. The caller's proven share is the record's reader (S5).
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecordWithADisabledCreator_FinishesForTheCaller()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (true, false);   // disabled

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(recordId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == Colleague);
        _fixture.SomeoneCanOpen(recordId).Should().BeTrue();
    }

    /// <summary>A caller on the No Access list is refused before any write — on the resume as on the forward path.</summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecord_WhenTheCallerIsOnTheNoAccessList_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("workassignment", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.NoAccessList.DenySystemUserOnRecord(ProvisionProjectTestFixture.CallerSystemUserId, recordId);

        var response = await PostAsync(ProvisionRoute,
            new { recordType = "workassignment", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        AssertResumeWroteNothing(recordId);
    }

    /// <summary>A caller whose Dataverse identity cannot be established is refused before any write (never shared to "nobody").</summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecord_WhenTheCallerCannotBeIdentified_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.CallerSystemUserIdResolves = false;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);
        AssertResumeWroteNothing(recordId);
    }

    /// <summary>Who created the record could not be read: refused before any write, as on the forward path.</summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecord_WhenTheCreatorCannotBeRead_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.SystemUserReadFailsFor = Colleague;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonRecordCreatorUnverifiable);
        AssertResumeWroteNothing(recordId);
    }

    /// <summary>
    /// The caller's share cannot be written: 500 <c>creator_share_failed</c> (<c>resumed: true</c>), no container created —
    /// the record stays as the earlier run left it plus the flag, and the same caller may call again.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecord_WhenTheCallersShareFails_StopsBeforeTheContainer()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            problem.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("no container before the caller's share is proven");
        _fixture.ContainerIdOf(recordId).Should().BeNull();
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    /// <summary>
    /// Round 40 item 2: whoever runs Make Secure KEEPS access — never narrowed. A Full Access holder (Write + Delete, one of
    /// the people owner F3 lets remove the designation) keeps Full Access, on the forward path and on a resume; nothing is
    /// modified for them. (The wizards' creator share stays EXACTLY the creator's level.)
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_MakeSecure_ByAFullAccessHolder_KeepsTheirFullAccess(bool resuming)
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: resuming,
            owningTeamId: resuming ? ProvisionProjectTestFixture.SecureOwnerTeamId : null, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.SeedShare(recordId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            Sprk.Bff.Api.Services.Access.RecordShareLevels.FullAccessRights);
        var fullAccessMask = Sprk.Bff.Api.Services.Access.RecordShareLevels.MaskForRightsCsv(
            Sprk.Bff.Api.Services.Access.RecordShareLevels.FullAccessRights);

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(fullAccessMask,
            "a Full Access holder who secures the record keeps Full Access — never narrowed to the creator's level");
        _fixture.Modifies.Should().NotContain(m => m.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
    }

    /// <summary>
    /// The level is a floor, not a widening: a caller whose share carries a right no level grants (Assign — a secure record
    /// leaves the Secure Record business unit only through the unsecure endpoint) is set to the levels' rights only, and a
    /// View-only caller is raised to the creator's level.
    /// </summary>
    [Theory]
    [InlineData("ReadAccess", "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess")]
    [InlineData("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess,AssignAccess",
        "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess")]
    [InlineData("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess,ShareAccess,AssignAccess",
        "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess,ShareAccess")]
    public async Task Provision_MakeSecure_TheCallersShare_IsTheCreatorsLevelOrTheirOwnLevelIfHigher_NeverAssign(
        string held, string expected)
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: false, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.SeedShare(recordId, DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId), held);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(Sprk.Bff.Api.Services.Access.RecordShareLevels.MaskForRightsCsv(expected));
    }

    /// <summary>
    /// Round 40 item 5 / round 46 item 5: the Make Secure RESUME's caller No Access check that cannot be completed refuses
    /// (500 <c>creator_no_access_unverifiable</c>) before any write — never read as "not walled". The forward path's twin is
    /// <c>ProvisionNoAccessTests.Provision_WhenTheCreatorsNoAccessCheckCannotBeRead_IsRefusedBeforeAnyChange</c>.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_FinishingARecord_WhenTheCallersNoAccessCheckCannotBeRead_IsRefusedBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.NoAccessList.Faults = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        AssertResumeWroteNothing(recordId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Round 46 item 1: the caller's Make Secure share is floored on their EFFECTIVE rights before the call — what F3 decides
    // Full Access from — so Delete held by ownership or by a security role is kept exactly as Delete held by a share. Never
    // more than they held, never less than Collaborate, never Assign. Read in the same pre-write step as WhoAmI; a read
    // that fails refuses before any write with provisioning's own code (round 53 item 1).
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly int FullAccessMask = Sprk.Bff.Api.Services.Access.RecordShareLevels.MaskForRightsCsv(
        Sprk.Bff.Api.Services.Access.RecordShareLevels.FullAccessRights);

    /// <summary>
    /// A caller who held Full Access (Write + Delete) WITHOUT a share — by owning the record, or by a security role — keeps
    /// Delete through an explicit Full Access share, on the forward path and on a resume (the record is then the team's,
    /// so only a role can be the route). Without the floor they would end at Collaborate and lose F3.
    /// </summary>
    /// <remarks>
    /// Round 53 item 3: the two routes run DIFFERENT fixture paths. "ownership" answers Delete only while the caller OWNS
    /// the record (<see cref="ProvisionProjectTestFixture.CallerDeletesWhatTheyOwn"/>, a user-depth role: read at the
    /// moment of the probe, so it is gone once the Secure team owns the record — a floor read after the move would miss
    /// it); "role" answers Delete whoever owns it (<see cref="ProvisionProjectTestFixture.CallerHoldsDelete"/>, a
    /// business-unit or organization-depth role), on a record a colleague owns. The production code delegates both to
    /// <c>RetrievePrincipalAccess</c>, so a REAL ownership-held or role-held Delete is proven only live (the G-11 ui-tests
    /// "Make Secure run by the record's owner" and "Make Secure run by an administrator").
    /// </remarks>
    [Theory]
    [InlineData("ownership")]
    [InlineData("role")]
    [InlineData("role-resume")]
    public async Task Provision_MakeSecure_ACallerWhoHeldFullAccessByOwnershipOrRole_KeepsIt_ThroughAFullAccessShare(string how)
    {
        var recordId = Guid.NewGuid();
        switch (how)
        {
            case "ownership":   // the caller owns it (and did not create it); their role gives Delete on what they own
                _fixture.SeedProject(recordId, isSecure: false, createdBy: Colleague);
                _fixture.CallerDeletesWhatTheyOwn = true;
                break;
            case "role":        // a colleague owns it; the caller's role gives them Delete in its business unit
                _fixture.SeedProject(recordId, isSecure: false, owningUserId: Colleague, createdBy: Colleague);
                _fixture.CallerHoldsDelete = true;
                break;
            default:            // an earlier Make Secure stopped after the move; the caller's role still gives Delete
                _fixture.SeedProject(recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
                    createdBy: Colleague);
                _fixture.CallerHoldsDelete = true;
                break;
        }

        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(FullAccessMask,
            "Delete held by ownership or role before the call is kept, through a share (round 46 item 1)");
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
            "the creator is shared at the creator's level — the floor is the CALLER's");
        _fixture.DelegationProbes.Count(p => p.RecordId == recordId).Should().Be(2,
            "the route's Write gate, then the caller's effective rights — read once, before any write");
    }

    /// <summary>
    /// The floor never widens: a caller whose effective rights carry no Delete — whether they own the record or not — is
    /// shared at exactly the creator's level (Collaborate), on both paths.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_MakeSecure_ACallerWhoHeldCollaborateOnly_IsSharedAtCollaborate_NeverMore(bool resuming)
    {
        var recordId = Guid.NewGuid();
        if (resuming)
        {
            _fixture.SeedProject(recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
                createdBy: Colleague);
        }
        else
        {
            _fixture.SeedProject(recordId, isSecure: false, createdBy: Colleague);   // the caller owns it; no Delete held
        }

        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "never more than they held, never less than Collaborate");
    }

    /// <summary>
    /// The ownership route's twin (round 53 item 3): a role that gives Delete only on what the caller OWNS gives nothing on
    /// a record a colleague owns, nor on a resume (the Secure Record owner team already owns it) — so those callers are
    /// shared at exactly Collaborate. With the "ownership" case above, it shows that case holds Delete THROUGH owning the
    /// record: the same switch, a different owner, a different floor.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_MakeSecure_DeleteHeldOnlyOnOwnedRecords_IsNotKept_OnARecordTheCallerDoesNotOwn(bool resuming)
    {
        var recordId = Guid.NewGuid();
        if (resuming)
        {
            _fixture.SeedProject(recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
                createdBy: Colleague);
        }
        else
        {
            _fixture.SeedProject(recordId, isSecure: false, owningUserId: Colleague, createdBy: Colleague);
        }

        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.CallerDeletesWhatTheyOwn = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(
            ProvisionProjectEndpoint.CreatorAccessMask, "Delete on what they own is no Delete on a record someone else owns");
    }

    /// <summary>
    /// The read of the caller's effective rights fails — the probe throws, or answers without the Write the route admitted
    /// them on (its "could not answer") — so the floor is unknown: refused before any write, 500
    /// <c>sdap.provision.caller_rights_unverifiable</c> (round 53 item 1: provisioning's own code — F3's
    /// <c>sdap.unsecure.permission_unverifiable</c> is the unsecure endpoint's), the ratified detail verbatim, on the
    /// forward path and on a resume. Never shared at a floor of "nothing" (which would narrow a Full Access holder).
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Provision_MakeSecure_WhenTheCallersEffectiveRightsCannotBeRead_IsRefusedBeforeAnyWrite(
        bool resuming, bool answersNone)
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: false,
            owningTeamId: resuming ? ProvisionProjectTestFixture.SecureOwnerTeamId : null, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        if (answersNone)
            _fixture.FollowUpRightsProbeAnswersNone = true;
        else
            _fixture.FullAccessProbeThrows = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "matter", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be("sdap.provision.caller_rights_unverifiable",
            "the wire code, verbatim — the ribbon and the wizard's client key their copy on it");
        (await DetailOf(response)).Should().Be(
            "Which access you hold on this matter could not be read, so securing it could not make sure you keep that " +
            "access. Nothing was changed; you may try again.");
        if (resuming)
            AssertResumeWroteNothing(recordId);
        else
            AssertNothingWritten(recordId);
    }

    /// <summary>
    /// The wizards' create-then-secure path (no transition) reads no floor: its creator's share is EXACTLY the creator's
    /// level even for a caller holding Delete (they own their new record), and a rights probe that would fail after the
    /// gate is never reached — so that path never answers <c>caller_rights_unverifiable</c>.
    /// </summary>
    [Fact]
    public async Task Provision_TheWizardsPath_ReadsNoFloor_AndSharesExactlyTheCreatorsLevel()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedProject(recordId, isSecure: false);   // created and owned by the caller
        _fixture.CallerHoldsDelete = true;
        _fixture.FullAccessProbeThrows = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.DelegationProbes.Count(p => p.RecordId == recordId).Should().Be(1, "only the route's Write gate asked");
    }

    /// <summary>
    /// The owner move cannot be read back AND the share cannot be confirmed after it: the share issued without a read is the
    /// Make Secure caller's FLOOR (round 46 item 1), never the bare creator's level — the fallback does not narrow them.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_WhenTheMoveAndTheShareAreUnverified_IssuesTheFallbackShareAtTheCallersFloor()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedProject(recordId, isSecure: false, owningUserId: Colleague, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);
        _fixture.CallerHoldsDelete = true;
        _fixture.OwnerReadBackFails = true;
        _fixture.FailStrictShareReadWhileSecureOwned = true;

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        var callerGrants = _fixture.Grants
            .Where(g => g.RecordId == recordId && g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId)
            .ToList();
        callerGrants.Should().HaveCount(2, "share-first, then the unconfirmed fallback after the unverified move");
        callerGrants.Should().OnlyContain(
            g => Sprk.Bff.Api.Services.Access.RecordShareLevels.MaskForRightsCsv(g.AccessRightsCsv!) == FullAccessMask,
            "both carry the caller's floor (Full Access), never the bare creator's level");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Round 46 item 4: a FLAGGED root that records a container but is NOT owned by the Secure Record Owners team — a user
    // (a legacy record provisioned before task 133's owner move) or another team (reassigned outside Spaarke). The ribbon
    // offers Make Secure ("finish") on both; the call's forward path re-owns the root to the Secure team and KEEPS its own
    // container (no second container, the value not rewritten).
    // ─────────────────────────────────────────────────────────────────────────

    private const string ItsOwnContainer = "b!its-own-container-legacy";

    [Theory]
    [InlineData("legacy-user-owned")]
    [InlineData("other-team-owned")]
    public async Task Provision_MakeSecure_FinishingAFlaggedRecordWithAContainer_NotOwnedBySecureTeam_ReownsItAndKeepsTheContainer(
        string shape)
    {
        var recordId = Guid.NewGuid();
        var otherTeam = Guid.NewGuid();   // an ordinary owner team in another business unit
        if (shape == "legacy-user-owned")
        {
            _fixture.SeedProject(recordId, isSecure: true, containerId: ItsOwnContainer, owningUserId: Colleague,
                createdBy: Colleague);
        }
        else
        {
            _fixture.SeedProject(recordId, isSecure: true, containerId: ItsOwnContainer, owningTeamId: otherTeam,
                createdBy: Colleague);
        }

        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId, "re-owned to the Secure team");
        _fixture.ContainerIdOf(recordId).Should().Be(ItsOwnContainer, "its own container is kept");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("no second container");
        _fixture.Updates.Should().NotContain(u => u.RecordId == recordId && u.Payload.ContainsKey("sprk_containerid"),
            "the recorded value is not rewritten");
        FlagWrites(recordId).Should().BeEmpty("it was already flagged");
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "the caller keeps access");
        _fixture.ShareMaskOf(recordId, Colleague).Should().Be(ProvisionProjectEndpoint.CreatorAccessMask,
            "the creator keeps access, as the confirmation says");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("speContainerId").GetString().Should().Be(ItsOwnContainer);
    }

    /// <summary>
    /// Round 53 item 2: a flagged root with its own container, owned by ANOTHER team INSIDE the Secure Record business unit
    /// (the retired default team, before task 144's migration), is already secure and isolated — the ribbon hides Make
    /// Secure there. Called through the API anyway, the server's answer is unchanged: 409
    /// <c>owned_by_other_secure_team</c> naming the migration script, before any write and before the caller's rights are
    /// read. Moving it onto the named team is task 144's migration, never a side effect of provisioning.
    /// </summary>
    [Fact]
    public async Task Provision_MakeSecure_OnAFlaggedRecordOwnedByAnotherTeamInsideTheSecureBusinessUnit_IsStillRefused409()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedProject(recordId, isSecure: true, containerId: ItsOwnContainer,
            owningTeamId: ProvisionProjectTestFixture.SecureDefaultTeamId, createdBy: Colleague);
        _fixture.SystemUsers[Colleague] = (false, false);

        var response = await PostAsync(ProvisionRoute, new { recordType = "project", recordId, transition = "make-secure" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnedByOtherSecureTeam);
        (await DetailOf(response)).Should().Contain("scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1");
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureDefaultTeamId);
        _fixture.ContainerIdOf(recordId).Should().Be(ItsOwnContainer);
        _fixture.Updates.Should().BeEmpty("refused before any write");
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.DelegationProbes.Count(p => p.RecordId == recordId).Should().Be(1, "only the route's Write gate asked");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Unsecure — owner round 3b F3: Full Access holders and the creator only
    // ═════════════════════════════════════════════════════════════════════════

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
            // Owner round 10 item 9 (F6 row 6): option A, verbatim. The ribbon shows this message as is, so it names who
            // CAN remove the designation.
            var label = recordType == "workassignment" ? "work assignment" : recordType;
            problem.RootElement.GetProperty("detail").GetString().Should().Be(
                $"Only someone with Full Access to this {label}, or the person who created it, can remove its secure " +
                "designation. It is still secure, and nothing was changed.");
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

    /// <summary>
    /// The ONE F3 rule (task 146's <c>SecureDesignationRemoval</c>, owner round 13 item 6) asks Full Access BEFORE it reads
    /// the recorded creator person, so a Full Access holder is admitted even when that read fails.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenTheRecordedCreatorPersonCannotBeRead_AFullAccessHolderIsStillAdmitted()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: BffApplicationUser, createdByPerson: Colleague);
        _fixture.CreatorPersonReadFailsWith = HttpStatusCode.ServiceUnavailable;
        _fixture.CallerHoldsDelete = true;

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeFalse();
    }

    /// <summary>
    /// Before the creator-person schema runs, nobody can be shown to be (or not to be) the recorded creator: for a caller
    /// who is neither <c>createdby</c> nor a Full Access holder the ONE F3 rule answers "could not be checked" — 403
    /// <c>permission_unverifiable</c> — never "not permitted" and never "allowed" (owner round 13 item 6, which keeps task
    /// 146's helper behaviour over this task's earlier <c>not_permitted</c>).
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenTheCreatorPersonColumnIsMissing_ACallerWhoIsNeitherCreatorNorFullAccessIsUnverifiable()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.CreatorPersonColumnExists = false;

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonPermissionUnverifiable);
        AssertStillSecure(recordId);
    }

    /// <summary>The missing column changes nothing for a Full Access holder: Full Access is asked first.</summary>
    [Fact]
    public async Task Unsecure_WhenTheCreatorPersonColumnIsMissing_AFullAccessHolderIsStillAdmitted()
    {
        var recordId = Guid.NewGuid();
        Seed("project", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.CreatorPersonColumnExists = false;
        _fixture.CallerHoldsDelete = true;

        var response = await PostAsync(UnsecureRoute, new { recordType = "project", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(recordId).Should().BeFalse();
    }

    /// <summary>
    /// Task 150 r2 (verifier F6): the Full Access probe THROWING (rather than answering None) is refused with a reason
    /// code, before any write — not an unhandled 500.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenTheFullAccessCheckThrows_RefusesWithAReasonBeforeAnyWrite()
    {
        var recordId = Guid.NewGuid();
        Seed("matter", recordId, isSecure: true, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Colleague);
        _fixture.FullAccessProbeThrows = true;

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonPermissionUnverifiable);
        _fixture.DelegationProbes.Should().HaveCount(2, "the route's Write gate, then the Full Access check that threw");
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
