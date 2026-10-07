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
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonContainerOwnershipUnreadable);
        problem.GetProperty("containerOwnershipState").GetString().Should().Be("unreadable",
            "a fault that is not Dataverse refusing the read may pass on the next call");
        AssertNothingWritten(projectId, OwnContainer);

        _fixture.ContainerOwnershipReadFails = false;
        var retry = await ProvisionAsync(new { projectId });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "the stated recovery — the same caller calls again — works");
        _fixture.ContainerIdOf(projectId).Should().Be(OwnContainer);
    }

    /// <summary>
    /// Owner round 14 item 3 (task 133 c1-r4): the container check classifies its read failure by the cascade reads' rule.
    /// A 401/403 (the service's sign-in or Read privilege refused) or a 400 (Dataverse refusing the query) repeats on every
    /// call: <c>containerOwnershipState: refused</c>, and the detail says calling again repeats it and names the Read
    /// privilege — never "the same caller may". A 503 or 429 stays <c>unreadable</c>, the same caller's retry, which then
    /// works. Each refused before any write.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.BadRequest, "refused")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unreadable")]
    [InlineData(HttpStatusCode.TooManyRequests, "unreadable")]
    public async Task Provision_WhenTheContainerCheckIsRefusedOrFails_ClassifiesItByTheReadsStatus(
        HttpStatusCode status, string state)
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        _fixture.ContainerOwnershipReadFailsWith = status;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonContainerOwnershipUnreadable);
        problem.GetProperty("containerOwnershipState").GetString().Should().Be(state);
        var detail = problem.GetProperty("detail").GetString();
        if (state == "refused")
        {
            detail.Should().Contain("Calling again repeats this refusal").And.Contain("Read privilege")
                .And.NotContain("the same caller may");
        }
        else
        {
            detail.Should().Contain("(the same caller may)").And.NotContain("repeats this refusal");
        }
        AssertNothingWritten(projectId, OwnContainer);

        if (state == "unreadable")
        {
            _fixture.ContainerOwnershipReadFailsWith = null;
            var retry = await ProvisionAsync(new { projectId });
            retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 133 r1 (verifier round 4, finding 1): a record that KEEPS its own container cannot be resumed — once the
    // team owns it, "owned by the team AND a container recorded" is the 409 marker, whatever happened after the move.
    // So such a record is moved only after its creator's share is confirmed, and every failure after the move names a
    // recovery that works against that marker. Each test then makes the NEXT call and asserts what the response said.
    // ─────────────────────────────────────────────────────────────────────────

    private const string ManageAccessRecovery = "Manage Access";

    private static async Task<string> DetailOf(HttpResponseMessage response)
        => (await ProblemOf(response)).GetProperty("detail").GetString() ?? string.Empty;

    /// <summary>
    /// The verifier's probe 2 (the S10 setup plus a container of its own): the record's shares cannot be read before the
    /// move, so no share-first grant could be made — and for a record that keeps its container, moving it anyway could
    /// strand it beyond any provisioning call. Refused BEFORE ANY WRITE; someone can still open it; the same caller's next
    /// call, once the read works, finishes it with the container kept.
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenThePreCallSharesCannotBeRead_RefusesBeforeAnyWrite()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        _fixture.FailNextStrictShareReads = 1;                                     // the pre-call read
        _fixture.OwnerReadBackFails = true;                                        // the S10 faults after it
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        problem.GetProperty("detail").GetString().Should().Contain("own SPE container").And.Contain("BEFORE");
        AssertNothingWritten(projectId, OwnContainer);
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        _fixture.OwnerReadBackFails = false;
        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailShareWhileSecureOwned = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(OwnContainer);
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The verifier's probe 1: the share proof after the move fails AND the compensating move fails. The record keeps its
    /// own container, so the response must not promise a resume (the next call is the 409) — it names the administrator's
    /// Manage Access share. The creator's share was confirmed before the move and is still there, so someone can open it,
    /// and the next call's 409 is TRUE: owned by the team, its own container, the creator holding exactly the creator rights.
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenTheShareProofAndTheUndoFail_NamesARecoveryThatWorks()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam, containerId: OwnContainer);
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailOwnerBindTo = businessUnitTeam;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("already_provisioned").And.Contain(ManageAccessRecovery)
            .And.Contain("confirmed before the move");
        detail.Should().NotContain("it resumes", "a team-owned record that keeps its container is never resumed");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Critical && e.Message.Contains(projectId.ToString())
            && e.Message.Contains(ManageAccessRecovery));
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue("the creator's share confirmed before the move still stands");

        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailOwnerBindTo = null;
        var updatesBefore = _fixture.Updates.Count;
        var grantsBefore = _fixture.Grants.Count;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.Conflict, "exactly what the detail said the next call answers");
        (await ProblemOf(next)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.Updates.Should().HaveCount(updatesBefore);
        _fixture.Grants.Should().HaveCount(grantsBefore);
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ContainerIdOf(projectId).Should().Be(OwnContainer);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "so the 409 describes a record that IS provisioned");
    }

    /// <summary>
    /// The owner move cannot be read back and the creator's share cannot be read back after it either (the share is NOT
    /// confirmed). For a record that keeps its own container the response must not promise a resume; it names the
    /// already_provisioned answer and the Manage Access recovery. The share confirmed before the move stands, so the next
    /// call's 409 describes a provisioned record.
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenTheMoveIsUnverifiedAndTheShareUnconfirmed_NamesARecoveryThatWorks()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        _fixture.OwnerReadBackFails = true;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        problem.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeFalse();
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("NOT confirmed").And.Contain("already_provisioned").And.Contain(ManageAccessRecovery);
        detail.Should().NotContain("resumed").And.NotContain("it resumes");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        _fixture.OwnerReadBackFails = false;
        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailShareWhileSecureOwned = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemOf(next)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
    }

    /// <summary>
    /// The owner move cannot be read back but the creator's share is (confirmed). For a record that keeps its own
    /// container the next call is the 409 — provisioning is complete — and the detail says so instead of "resumed".
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenTheMoveIsUnverifiedButTheShareIsConfirmed_SaysTheNextCallIsTheConflict()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);
        _fixture.OwnerReadBackFails = true;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        problem.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeTrue();
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        problem.GetProperty("detail").GetString().Should().Contain("already_provisioned").And.NotContain("resumed");

        _fixture.OwnerReadBackFails = false;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The residual the owner question in the task note (§14) is about: Dataverse refuses a share to the record's CURRENT
    /// owner (live gate (a)), so share-first falls back to the share after the move — and after the move the owner read-back,
    /// the share read and the grant all fail. If the move landed, nobody can open the record, and because it keeps its own
    /// container the next call is the 409, not a resume. The response must say exactly that and name the recovery that
    /// works (an administrator's Manage Access share), logged CRITICAL — never "it resumes".
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenNoShareExistsAfterAnUnverifiedMove_NamesManageAccess_NotAResume()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer); // owned by the caller
        _fixture.GrantToCurrentOwnerRefused = true;
        _fixture.OwnerReadBackFails = true;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("already_provisioned").And.Contain(ManageAccessRecovery).And.NotContain("resumes");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Critical && e.Message.Contains(ManageAccessRecovery));

        _fixture.OwnerReadBackFails = false;
        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailShareWhileSecureOwned = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.Conflict, "the detail said the next call is the 409, and it is");
    }

    /// <summary>
    /// The negative direction of the double failure's creator sentence (task 133 r2, verifier round 5 seed P5). Dataverse
    /// refuses the share to the record's CURRENT owner (live gate (a)), so share-first falls back to the share after the
    /// move with NOTHING proven; the move lands (read back), the share proof after it fails, and the undo does not take
    /// effect. Nobody can open the record. The detail must not claim a share "confirmed before the move" — true only when
    /// share-first proved one (probe 1, above) — and must name the Manage Access recovery; the next call is the 409 it says.
    /// </summary>
    [Fact]
    public async Task Provision_KeepingItsOwnContainer_WhenTheFallbackShareAndTheUndoFailAfterTheMove_NeverClaimsAConfirmedShare()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, containerId: OwnContainer);                // owned by the caller, its creator
        _fixture.GrantToCurrentOwnerRefused = true;                                // live gate (a) disproved
        _fixture.FailStrictShareReadWhileSecureOwned = true;                       // the proof after the move fails
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;
        _fixture.FailOwnerBindTo = ProvisionProjectTestFixture.CallerSystemUserId; // the undo is refused

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("containerKept").GetBoolean().Should().BeTrue();
        problem.GetProperty("ownershipRestored").GetBoolean().Should().BeFalse();
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().NotContain("confirmed before the move", "share-first proved nothing: the fallback ran");
        detail.Should().Contain("may not be able to open it")
            .And.Contain("already_provisioned").And.Contain(ManageAccessRecovery).And.NotContain("it resumes");
        _fixture.Grants.Should().BeEmpty("no share to the creator was ever accepted");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.SomeoneCanOpen(projectId).Should().BeFalse("the residual the detail and the CRITICAL log describe");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Critical && e.Message.Contains(projectId.ToString())
            && e.Message.Contains(ManageAccessRecovery));

        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailShareWhileSecureOwned = null;
        _fixture.FailOwnerBindTo = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.Conflict, "the detail said the next call is the 409, and it is");
        (await ProblemOf(next)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 133 r1: a SHARED container (a business unit's, or a configured one) is unlinked BEFORE the owner move, so a
    // failure after the move leaves "owned by the team, no container" — which the next call resumes — never "owned by
    // the team with a shared container recorded", which the 409 marker would refuse forever while the secure record's
    // uploads went to shared storage.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The container could not be created after the move. The business unit's container was unlinked before the move, so
    /// the record is "owned by the team, no container", and the same caller's next call RESUMES it — as the detail says.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingABusinessUnitsContainer_WhenContainerCreationFails_TheNextCallResumes()
    {
        const string buContainer = "b!business-unit-shared";
        var projectId = Guid.NewGuid();
        _fixture.BusinessUnitContainers[Guid.NewGuid()] = buContainer;
        _fixture.SeedProject(projectId, containerId: buContainer);
        _fixture.SpeContainerCreationSucceeds = false;

        var response = await ProvisionAsync(new { projectId });

        (await ProblemOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonContainerCreationFailed);
        _fixture.ContainerIdOf(projectId).Should().BeNull("the shared container was unlinked before the move");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        var clear = _fixture.Updates.Single(u => u.Payload.ContainsKey("sprk_containerid"));
        clear.Payload["sprk_containerid"].Should().BeNull();
        clear.Sequence.Should().BeLessThan(
            _fixture.Updates.Single(u => u.Payload.ContainsKey("ownerid@odata.bind")).Sequence,
            "unlinked BEFORE the owner move");

        _fixture.SpeContainerCreationSucceeds = true;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// A configured shared container, and the share proof and the undo both fail after the move: the administrator-only
    /// state — but the shared container was unlinked first, so the record is "owned by the team, no container" and an
    /// administrator's call RESUMES it, sharing to the record's creator, exactly as the detail says.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingAConfiguredContainer_WhenTheShareAndTheUndoFail_AnAdministratorsCallResumes()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam,
            containerId: ProvisionProjectTestFixture.ConfiguredArchiveContainerId);
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailOwnerBindTo = businessUnitTeam;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("containerKept").GetBoolean().Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Contain("it resumes").And.Contain("unlinked");
        _fixture.ContainerIdOf(projectId).Should().BeNull();

        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailOwnerBindTo = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The shared container could not be unlinked before the move: refused, with the owner never moved and no share
    /// issued; the same caller's next call finishes.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenItCannotBeUnlinked_StopsBeforeTheMove()
    {
        const string buContainer = "b!business-unit-shared";
        var projectId = Guid.NewGuid();
        _fixture.BusinessUnitContainers[Guid.NewGuid()] = buContainer;
        _fixture.SeedProject(projectId, containerId: buContainer);
        _fixture.ContainerClearSucceeds = false;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonSharedContainerNotCleared);
        problem.GetProperty("speContainerId").GetString().Should().Be(buContainer);
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("ownerid@odata.bind"));
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningUserOf(projectId).Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);

        _fixture.ContainerClearSucceeds = true;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// A share-first failure AFTER the shared container was unlinked: the response says the link was removed (the record
    /// is not "as it was" in that respect), and the next call, which no longer sees a container, finishes.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenShareFirstFails_SaysTheLinkWasRemoved()
    {
        const string buContainer = "b!business-unit-shared";
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.BusinessUnitContainers[Guid.NewGuid()] = buContainer;
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam, containerId: buContainer);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        problem.GetProperty("detail").GetString().Should().Contain("unlinked");
        _fixture.ContainerIdOf(projectId).Should().BeNull();
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);

        _fixture.FailShareForPrincipal = null;
        var next = await ProvisionAsync(new { projectId });
        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 133 r2 (verifier round 5, seeds P7 / P8): EVERY failure after the shared container was unlinked says so. The
    // share-first refusal and the double failure were pinned in r1; these pin the other four sites — the refused move,
    // the unverified move with and without a share, and the verified undo — so no site can drop the sentence unseen.
    // Each then makes the next call the detail names.
    // ─────────────────────────────────────────────────────────────────────────

    private const string BusinessUnitContainer = "b!business-unit-shared";

    /// <summary>Seeds a project owned by a business-unit team that records that business unit's shared container.</summary>
    private Guid SeedRecordingABusinessUnitsContainer(Guid? owningTeamId)
    {
        var projectId = Guid.NewGuid();
        _fixture.BusinessUnitContainers[Guid.NewGuid()] = BusinessUnitContainer;
        _fixture.SeedProject(projectId, owningTeamId: owningTeamId, containerId: BusinessUnitContainer);
        return projectId;
    }

    /// <summary>The move to the team is refused and read back unchanged: the detail says the link was removed.</summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenTheMoveIsRefused_SaysTheLinkWasRemoved()
    {
        var businessUnitTeam = Guid.NewGuid();
        var projectId = SeedRecordingABusinessUnitsContainer(businessUnitTeam);
        _fixture.FailOwnerBindTo = ProvisionProjectTestFixture.SecureOwnerTeamId;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentFailed);
        problem.GetProperty("detail").GetString().Should().Contain("unlinked");
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.ContainerIdOf(projectId).Should().BeNull("the link was removed before the move, and stays removed");

        _fixture.FailOwnerBindTo = null;
        var next = await ProvisionAsync(new { projectId });
        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// The owner move cannot be read back; the creator's share is (confirmed). Not a kept container, so the next call
    /// resumes — and the detail says the link was removed (seed P8).
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenTheMoveIsUnverified_SaysTheLinkWasRemoved()
    {
        var projectId = SeedRecordingABusinessUnitsContainer(Guid.NewGuid());
        _fixture.OwnerReadBackFails = true;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        problem.GetProperty("containerKept").GetBoolean().Should().BeFalse();
        problem.GetProperty("creatorShareConfirmed").GetBoolean().Should().BeTrue();
        problem.GetProperty("detail").GetString().Should().Contain("unlinked").And.Contain("resumed");
        _fixture.ContainerIdOf(projectId).Should().BeNull();

        _fixture.OwnerReadBackFails = false;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue("the team owns it with no container");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// The owner move cannot be read back and no share to the creator could be issued — share-first fell back (live gate
    /// (a)) and the grant after the move failed. The detail says the link was removed and that an administrator's call
    /// resumes; it does, sharing to the record's creator.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenNoShareExistsAfterAnUnverifiedMove_SaysTheLinkWasRemoved()
    {
        var projectId = SeedRecordingABusinessUnitsContainer(owningTeamId: null); // owned by the caller, its creator
        _fixture.GrantToCurrentOwnerRefused = true;
        _fixture.OwnerReadBackFails = true;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailShareWhileSecureOwned = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("containerKept").GetBoolean().Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Contain("unlinked").And.Contain("resumes");
        _fixture.ContainerIdOf(projectId).Should().BeNull();
        _fixture.SomeoneCanOpen(projectId).Should().BeFalse("the state only an administrator's call can finish");

        _fixture.OwnerReadBackFails = false;
        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.FailShareWhileSecureOwned = null;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "the resume shares to the record's creator");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// The share proof fails after the move and the move is undone (read back): the record is back with its pre-call
    /// owner but WITHOUT the shared link, so the detail says it was unlinked (seed P7). The next call provisions it from
    /// the start.
    /// </summary>
    [Fact]
    public async Task Provision_ReplacingASharedContainer_WhenTheMoveIsUndone_SaysTheLinkWasRemoved()
    {
        var businessUnitTeam = Guid.NewGuid();
        var projectId = SeedRecordingABusinessUnitsContainer(businessUnitTeam);
        _fixture.FailStrictShareReadWhileSecureOwned = true;

        var response = await ProvisionAsync(new { projectId });

        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        problem.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.GetProperty("detail").GetString().Should().Contain("unlinked");
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.ContainerIdOf(projectId).Should().BeNull();

        _fixture.FailStrictShareReadWhileSecureOwned = false;
        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }
}
