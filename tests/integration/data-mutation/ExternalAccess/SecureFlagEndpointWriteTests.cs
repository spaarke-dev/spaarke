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
