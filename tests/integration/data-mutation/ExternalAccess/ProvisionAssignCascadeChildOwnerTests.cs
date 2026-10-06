using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 133 (C11, #1054), owner round 10 item 4 — compensation's reverse Assign cascade.
/// </summary>
/// <remarks>
/// <para><b>The defect these pin.</b> An owner move of a project or a matter cascades <c>Assign</c> to its SharePoint
/// document locations and documents (live metadata). The owner accepted that for the move INTO the secure owner team
/// (round 4 item 3). Compensation's move BACK cascades the same way — to the RECORD's pre-call owner, so a child whose
/// own owner differed from the record's silently changed hands. Now each child's own owner is snapshotted before any
/// write (<c>AssignCascadeChildOwners</c>) and put back after a verified move back; a child that cannot be is named with
/// the call that puts it back.</para>
/// <para>The fixture applies the cascade the way Dataverse does: every child of a moved project or matter takes the
/// root's new owner.</para>
/// </remarks>
public class ProvisionAssignCascadeChildOwnerTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string Route = "/api/v1/external-access/provision-project";
    private const string Location = "sharepointdocumentlocation";
    private const string Document = "sharepointdocument";

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionAssignCascadeChildOwnerTests(ProvisionProjectTestFixture fixture)
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

    private void SeedRoot(string recordType, Guid recordId, Guid owningTeamId)
    {
        if (recordType == "matter")
            _fixture.SeedMatter(recordId, owningTeamId: owningTeamId);
        else
            _fixture.SeedProject(recordId, owningTeamId: owningTeamId);
    }

    private IEnumerable<ProvisionProjectTestFixture.RecordedUpdate> ChildOwnerWrites() =>
        _fixture.Updates.Where(u => u.EntitySet is "sharepointdocumentlocations" or "sharepointdocuments");

    /// <summary>
    /// THE owner-mandated test (round 10 item 4). The record is owned by a business-unit team; one document location
    /// shares that owner, another location and a document have owners of their own. The post-move share proof fails, so
    /// the move is undone — and the undo's cascade would give all three the record's owner. After the call each is back
    /// on ITS OWN pre-call owner, read back; the child that already had the record's owner was never written.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    public async Task Compensation_PutsEveryCascadedChildBackOnItsOwnPreCallOwner(string recordType)
    {
        var recordId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        var sameOwnerLocation = Guid.NewGuid();
        var ownOwnerLocation = Guid.NewGuid();
        var ownOwnerDocument = Guid.NewGuid();
        var locationOwner = DataversePrincipalRef.User(Guid.NewGuid());
        var documentOwner = DataversePrincipalRef.Team(Guid.NewGuid());
        SeedRoot(recordType, recordId, businessUnitTeam);
        _fixture.SeedCascadeChild(recordId, Location, sameOwnerLocation, DataversePrincipalRef.Team(businessUnitTeam));
        _fixture.SeedCascadeChild(recordId, Location, ownOwnerLocation, locationOwner);
        _fixture.SeedCascadeChild(recordId, Document, ownOwnerDocument, documentOwner);
        _fixture.SharePointDocumentReadRefused = false; // an org with SharePoint integration on
        _fixture.FailStrictShareReadWhileSecureOwned = true; // the post-move proof fails → compensate

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        problem.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.GetProperty("childOwnersRestored").GetBoolean().Should().BeTrue();
        problem.GetProperty("detail").GetString().Should().Contain("The same caller may retry");
        _fixture.OwningTeamOf(recordId).Should().Be(businessUnitTeam);

        _fixture.OwnerOfCascadeChild(ownOwnerLocation).Should().Be(locationOwner,
            "its own owner differed from the record's: the move back's cascade gave it the record's, and it is put back");
        _fixture.OwnerOfCascadeChild(ownOwnerDocument).Should().Be(documentOwner);
        _fixture.OwnerOfCascadeChild(sameOwnerLocation).Should().Be(DataversePrincipalRef.Team(businessUnitTeam));

        ChildOwnerWrites().Select(u => (u.RecordId, u.Payload["ownerid@odata.bind"])).Should().BeEquivalentTo(new[]
        {
            (ownOwnerLocation, (string?)$"/systemusers({locationOwner.Id})"),
            (ownOwnerDocument, (string?)$"/teams({documentOwner.Id})")
        }, "only the children the cascade moved off their own owner are written — each with its own bind");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// A child that cannot be put back (its owner bind is refused): fail closed — its own code, the child named in the
    /// response with its own owner and the exact call that puts it back, the same in a CRITICAL log line, and no "retry"
    /// (another run would snapshot the owner it has now). The others are still put back. Once an administrator has made
    /// the named call, the creator's next call provisions the record.
    /// </summary>
    [Fact]
    public async Task Compensation_WhenAChildCannotBePutBack_NamesItWithTheCallThatPutsItBack()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        var stuckLocation = Guid.NewGuid();
        var otherLocation = Guid.NewGuid();
        var stuckOwner = DataversePrincipalRef.User(Guid.NewGuid());
        var otherOwner = DataversePrincipalRef.User(Guid.NewGuid());
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedCascadeChild(projectId, Location, stuckLocation, stuckOwner);
        _fixture.SeedCascadeChild(projectId, Location, otherLocation, otherOwner);
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailChildOwnerBindFor = stuckLocation;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCascadeChildrenNotRestored);
        problem.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.GetProperty("childOwnersRestored").GetBoolean().Should().BeFalse();

        var expectedCall =
            $"PATCH /api/data/v9.2/sharepointdocumentlocations({stuckLocation}) {{\"ownerid@odata.bind\":\"/systemusers({stuckOwner.Id})\"}}";
        var named = problem.GetProperty("childOwnersNotRestored").EnumerateArray().Should().ContainSingle().Subject;
        named.GetProperty("table").GetString().Should().Be(Location);
        named.GetProperty("id").GetGuid().Should().Be(stuckLocation);
        named.GetProperty("ownerType").GetString().Should().Be("systemuser");
        named.GetProperty("ownerId").GetGuid().Should().Be(stuckOwner.Id);
        named.GetProperty("outcome").GetString().Should().Be("Refused");
        named.GetProperty("nextCall").GetString().Should().Be(expectedCall);

        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain(stuckLocation.ToString()).And.Contain("before provisioning is called again");
        detail.Should().NotContainEquivalentOf("retry");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical && e.Message.Contains(stuckLocation.ToString()) && e.Message.Contains(expectedCall));

        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.OwnerOfCascadeChild(otherLocation).Should().Be(otherOwner, "the others are still put back");
        _fixture.OwnerOfCascadeChild(stuckLocation).Should().Be(DataversePrincipalRef.Team(businessUnitTeam),
            "what the move back's cascade left — the state the response describes");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();

        // The named call, made by an administrator; then the creator's own call provisions the record.
        _fixture.FailChildOwnerBindFor = null;
        _fixture.FailStrictShareReadWhileSecureOwned = false;
        _fixture.SeedCascadeChild(projectId, Location, stuckLocation, stuckOwner);

        var next = await ProvisionAsync(new { projectId });

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The restore outcomes besides a plain <c>Refused</c> (owner round 10 item 4: "a child whose restore fails is named").
    /// <c>Unreadable</c>: the child's owner cannot be read before the restore, so nothing is written to it.
    /// <c>Unverified</c>: its PATCH is sent but it cannot be read back. <c>NotApplied</c> (task 133 c1-r2): its PATCH is
    /// accepted but not applied — the read-back finds it still on the owner the undo's cascade left. A row that is GONE
    /// after its PATCH (task 133 c1-r4) — read back as no row — is <c>NotApplied</c> when the PATCH was accepted and
    /// <c>Refused</c> when it was refused: only the read BEFORE the restore decides <c>Gone</c>, so a row that vanishes
    /// after it is never counted back. Each is a failure — never "already owned", "restored" or "gone" — named with its own
    /// owner, its outcome and the call that puts it back, in the response and in a CRITICAL line, with no "retry" (another
    /// run would snapshot the owner it has now). The other child is still put back.
    /// </summary>
    [Theory]
    [InlineData("read-fails-before-restore", "Unreadable")]
    [InlineData("read-back-fails", "Unverified")]
    [InlineData("bind-accepted-not-applied", "NotApplied")]
    [InlineData("row-gone-after-accepted-bind", "NotApplied")]
    [InlineData("row-gone-after-refused-bind", "Refused")]
    public async Task Compensation_WhenAChildsRestoreCannotBeConfirmed_NamesItAsNotRestored(string shape, string outcome)
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        var unknownLocation = Guid.NewGuid();
        var otherLocation = Guid.NewGuid();
        var unknownOwner = DataversePrincipalRef.User(Guid.NewGuid());
        var otherOwner = DataversePrincipalRef.Team(Guid.NewGuid());
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedCascadeChild(projectId, Location, unknownLocation, unknownOwner);
        _fixture.SeedCascadeChild(projectId, Location, otherLocation, otherOwner);
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.FailStrictShareReadWhileSecureOwned = true; // the post-move proof fails → compensate
        switch (shape)
        {
            case "read-fails-before-restore": // its read BEFORE the restore throws
                _fixture.FailChildOwnerReadFor = unknownLocation;
                break;
            case "read-back-fails": // its read-back AFTER the PATCH throws
                _fixture.FailChildOwnerReadBackAfterBindFor = unknownLocation;
                break;
            case "bind-accepted-not-applied": // its PATCH is accepted, not applied
                _fixture.IgnoreChildOwnerBindFor = unknownLocation;
                break;
            case "row-gone-after-accepted-bind": // its PATCH is accepted; then no row reads back
                _fixture.RemoveChildOnBindFor = unknownLocation;
                break;
            case "row-gone-after-refused-bind": // its PATCH is refused; then no row reads back
                _fixture.RemoveChildOnBindFor = unknownLocation;
                _fixture.FailChildOwnerBindFor = unknownLocation;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Not a shape this theory describes.");
        }

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCascadeChildrenNotRestored);
        problem.GetProperty("ownershipRestored").GetBoolean().Should().BeTrue();
        problem.GetProperty("childOwnersRestored").GetBoolean().Should().BeFalse();

        var expectedCall =
            $"PATCH /api/data/v9.2/sharepointdocumentlocations({unknownLocation}) {{\"ownerid@odata.bind\":\"/systemusers({unknownOwner.Id})\"}}";
        var named = problem.GetProperty("childOwnersNotRestored").EnumerateArray().Should().ContainSingle().Subject;
        named.GetProperty("table").GetString().Should().Be(Location);
        named.GetProperty("id").GetGuid().Should().Be(unknownLocation);
        named.GetProperty("ownerType").GetString().Should().Be("systemuser");
        named.GetProperty("ownerId").GetGuid().Should().Be(unknownOwner.Id);
        named.GetProperty("outcome").GetString().Should().Be(outcome);
        named.GetProperty("nextCall").GetString().Should().Be(expectedCall);

        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain(unknownLocation.ToString()).And.Contain("before provisioning is called again");
        detail.Should().NotContainEquivalentOf("retry");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical
            && e.Message.Contains($"{Location} {unknownLocation} ({outcome})")
            && e.Message.Contains(expectedCall));

        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);
        _fixture.OwnerOfCascadeChild(otherLocation).Should().Be(otherOwner, "the other child is still put back");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();

        var writesToUnknown = ChildOwnerWrites().Where(u => u.RecordId == unknownLocation).ToList();
        if (shape == "read-fails-before-restore")
        {
            writesToUnknown.Should().BeEmpty("its owner could not be read, so nothing was written to it");
            _fixture.OwnerOfCascadeChild(unknownLocation).Should().Be(DataversePrincipalRef.Team(businessUnitTeam),
                "what the move back's cascade left");
        }
        else
        {
            writesToUnknown.Should().ContainSingle(
                    "the PATCH was sent once; only its read-back failed, it was not applied, or the row was gone after it")
                .Which.Payload["ownerid@odata.bind"].Should().Be($"/systemusers({unknownOwner.Id})");
        }

        switch (shape)
        {
            case "bind-accepted-not-applied":
                _fixture.OwnerOfCascadeChild(unknownLocation).Should().Be(DataversePrincipalRef.Team(businessUnitTeam),
                    "the PATCH was accepted and not applied: the child is still where the undo's cascade left it");
                break;
            case "row-gone-after-accepted-bind":
            case "row-gone-after-refused-bind":
                _fixture.OwnerOfCascadeChild(unknownLocation).Should().BeNull(
                    "the row no longer reads after its PATCH: nothing confirms it is on its own owner, so it is not back");
                break;
        }
    }

    /// <summary>
    /// The snapshot cannot be read completely: refused BEFORE any write (a move whose cascade could not be undone child by
    /// child is not attempted). A transient failure is the same caller's retry; a 400 is Dataverse refusing the read, and a
    /// 401 / 403 is Dataverse refusing the service's sign-in or its Read privilege on the table (task 133 c1-r2 — e.g. a BFF
    /// application user without Read on <c>sharepointdocumentlocation</c>) — each deterministic, so the detail says calling
    /// again repeats it and names the privilege, and no retry is offered for a read that fails every time.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unreadable")]
    [InlineData(HttpStatusCode.TooManyRequests, "unreadable")]
    [InlineData(HttpStatusCode.BadRequest, "refused")]
    [InlineData(HttpStatusCode.Unauthorized, "refused")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    public async Task Provisioning_WhenTheCascadedRowsCannotBeRead_RefusesBeforeAnyWrite(HttpStatusCode status, string state)
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.CascadeChildSnapshotReadFailsWith = status;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCascadeChildrenUnreadable);
        problem.GetProperty("childTable").GetString().Should().Be(Location);
        problem.GetProperty("cascadeChildState").GetString().Should().Be(state);
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("BEFORE changing anything");
        if (state == "refused")
            detail.Should().Contain("Calling again repeats this refusal").And.Contain("Read privilege").And.NotContain("retry");
        else
            detail.Should().Contain("The same caller may retry");
        _fixture.Updates.Should().BeEmpty("refused before any mutation");
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningTeamOf(projectId).Should().Be(businessUnitTeam);

        if (state == "unreadable")
        {
            _fixture.CascadeChildSnapshotReadFailsWith = null;
            var retry = await ProvisionAsync(new { projectId });
            retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// The snapshot reads ONE page: a full page may not be every row, and a row read without an owner — or without its id
    /// (task 133 c1-r2) — cannot be put back. Each is incomplete — refused before any write, deterministically (calling
    /// again reads the same). An id-less row is never recorded under an empty id: the restore's by-id read of that would
    /// find no row and report <c>Gone</c>, which counts as back.
    /// </summary>
    [Theory]
    [InlineData("full-page")]
    [InlineData("ownerless-row")]
    [InlineData("idless-row")]
    public async Task Provisioning_WhenTheCascadedRowsAreReadIncompletely_RefusesBeforeAnyWrite(string shape)
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        switch (shape)
        {
            case "full-page":
                for (var i = 0; i < Sprk.Bff.Api.Infrastructure.Dataverse.AssignCascadeChildOwners.PageLimit; i++)
                    _fixture.SeedCascadeChild(projectId, Location, Guid.NewGuid(), DataversePrincipalRef.User(Guid.NewGuid()));
                break;
            case "ownerless-row":
                _fixture.SeedCascadeChild(projectId, Location, Guid.NewGuid(), DataversePrincipalRef.User(Guid.Empty));
                break;
            default:
                var idless = Guid.NewGuid();
                _fixture.SeedCascadeChild(projectId, Location, idless, DataversePrincipalRef.User(Guid.NewGuid()));
                _fixture.ChildRowReadWithoutIdFor = idless;
                break;
        }
        _fixture.SharePointDocumentReadRefused = false;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCascadeChildrenUnreadable);
        problem.GetProperty("childTable").GetString().Should().Be(Location);
        problem.GetProperty("cascadeChildState").GetString().Should().Be("refused");
        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// Dev's live shape (2026-10-03): a <c>sharepointdocuments</c> read is refused 400 ("SharePoint S2S and MSTeams
    /// integration is not enabled for this org"). Under a document location the documents must be read, so that refusal
    /// stops the run before any write — never read as "no documents".
    /// </summary>
    [Fact]
    public async Task Provisioning_UnderADocumentLocation_WhenSharePointDocumentsAreRefused_RefusesBeforeAnyWrite()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SeedCascadeChild(projectId, Location, Guid.NewGuid(), DataversePrincipalRef.User(Guid.NewGuid()));

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCascadeChildrenUnreadable);
        problem.GetProperty("childTable").GetString().Should().Be(Document);
        problem.GetProperty("cascadeChildState").GetString().Should().Be("refused");
        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The same dev shape with NO document location — every record in dev today: documents live in SharePoint Embedded, so
    /// there is no SharePoint folder for a <c>sharepointdocument</c> to come from. The documents are never read, so the
    /// refusal above cannot block provisioning.
    /// </summary>
    /// <remarks>Beyond the closed set: pins the live finding that a snapshot reading <c>sharepointdocuments</c> every time
    /// would refuse every project and matter provisioning in dev.</remarks>
    [Fact]
    public async Task Provisioning_WithNoDocumentLocation_NeverReadsSharePointDocuments()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Queries.Should().Contain(q => q.EntitySet == "sharepointdocumentlocations"
                                               && q.Filter == $"_regardingobjectid_value eq {projectId}");
        _fixture.Queries.Should().NotContain(q => q.EntitySet == "sharepointdocuments");
    }

    /// <summary>
    /// Where no owner move cascades, nothing is snapshotted (task 133 c1-r2, verifier item 3; note §16.3). A work
    /// assignment IS moved, but its Assign cascades to no table (live metadata 2026-10-03), so no cascaded row is read. A
    /// resume (the record is already owned by the team, with no container) makes no owner move at all.
    /// </summary>
    /// <remarks>Beyond the closed set: pins §16.3's "Resume; work assignment — no snapshot" row, which a seed that described
    /// a work assignment as cascading (or snapshotted on resume) survived — refusing more, but adding reads the live
    /// metadata says are needless.</remarks>
    [Theory]
    [InlineData("workassignment", false)]
    [InlineData("project", true)]
    [InlineData("matter", true)]
    public async Task Provisioning_WhenNoOwnerMoveCascades_ReadsNoCascadedRows(string recordType, bool resume)
    {
        var recordId = Guid.NewGuid();
        Guid? owningTeam = resume ? ProvisionProjectTestFixture.SecureOwnerTeamId : null;
        switch (recordType)
        {
            case "workassignment": _fixture.SeedWorkAssignment(recordId, owningTeamId: owningTeam); break;
            case "matter": _fixture.SeedMatter(recordId, owningTeamId: owningTeam); break;
            default: _fixture.SeedProject(recordId, owningTeamId: owningTeam); break;
        }

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().Be(resume);
        _fixture.OwningTeamOf(recordId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.Queries.Should().NotContain(q => q.EntitySet == "sharepointdocumentlocations" || q.EntitySet == "sharepointdocuments",
            resume ? "a resume makes no owner move, so nothing cascades" : "a work assignment's Assign cascades to no table");
    }

    /// <summary>
    /// On success the cascaded rows stay with the secure owner team — the forward cascade the owner accepted (round 4 item
    /// 3). The snapshot is only for an undo: nothing is put back.
    /// </summary>
    /// <remarks>Beyond the closed set: a restore that also ran on success would undo the owner-accepted forward cascade.</remarks>
    [Fact]
    public async Task Provisioning_WhenItSucceeds_LeavesTheCascadedRowsWithTheTeam()
    {
        var projectId = Guid.NewGuid();
        var location = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: Guid.NewGuid());
        _fixture.SeedCascadeChild(projectId, Location, location, DataversePrincipalRef.User(Guid.NewGuid()));
        _fixture.SharePointDocumentReadRefused = false;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwnerOfCascadeChild(location).Should().Be(DataversePrincipalRef.Team(ProvisionProjectTestFixture.SecureOwnerTeamId));
        ChildOwnerWrites().Should().BeEmpty();
    }

    /// <summary>
    /// The move back is sent but cannot be read back (unverified). Nothing is put back — whether the record moved is
    /// unknown — but the children a landed move back left on the wrong owner (those whose own owner was not the record's)
    /// are named, with the call for each, in the response and a CRITICAL log line. The child that shared the record's
    /// owner is not named.
    /// </summary>
    [Fact]
    public async Task Compensation_WhenTheMoveBackCannotBeVerified_NamesTheRowsItWouldLeaveOnTheWrongOwner()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        var sameOwnerLocation = Guid.NewGuid();
        var ownOwnerLocation = Guid.NewGuid();
        var locationOwner = DataversePrincipalRef.User(Guid.NewGuid());
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedCascadeChild(projectId, Location, sameOwnerLocation, DataversePrincipalRef.Team(businessUnitTeam));
        _fixture.SeedCascadeChild(projectId, Location, ownOwnerLocation, locationOwner);
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.FailStrictShareReadWhileSecureOwned = true;       // compensate
        _fixture.FailOwnerReadBackAfterBindTo = businessUnitTeam;  // the undo lands; its read-back throws

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailedResumable);
        problem.GetProperty("ownershipVerified").GetBoolean().Should().BeFalse();
        var expectedCall =
            $"PATCH /api/data/v9.2/sharepointdocumentlocations({ownOwnerLocation}) {{\"ownerid@odata.bind\":\"/systemusers({locationOwner.Id})\"}}";
        var atRisk = problem.GetProperty("childOwnersAtRisk").EnumerateArray().Should().ContainSingle().Subject;
        atRisk.GetProperty("id").GetGuid().Should().Be(ownOwnerLocation);
        atRisk.GetProperty("nextCall").GetString().Should().Be(expectedCall);
        problem.GetProperty("detail").GetString().Should().Contain("If the move back did take effect")
            .And.Contain(ownOwnerLocation.ToString()).And.NotContain(sameOwnerLocation.ToString());
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains(expectedCall));

        ChildOwnerWrites().Should().BeEmpty("nothing is put back while whether the record moved is unknown");
        _fixture.OwnerOfCascadeChild(ownOwnerLocation).Should().Be(DataversePrincipalRef.Team(businessUnitTeam),
            "the undo landed in the fixture, so the child IS on the record's owner — exactly what the response warns of");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Batch 4 integration — task 132 (C12) x task 133: every owner change the provisioning run makes is evicted, once
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Records every owner-change eviction (the hook at its module boundary).</summary>
    private sealed class RecordingInvalidator : IMembershipCacheInvalidator
    {
        public ConcurrentQueue<(string Entity, string EntitySet, Guid RecordId)> OwnerChanges { get; } = new();

        public Task PublishInvalidationAsync(Guid personId, string entityLogicalName, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRecordOwnerChangeAsync(
            string entityLogicalName, string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
        {
            OwnerChanges.Enqueue((entityLogicalName, entitySetName, recordId));
            return Task.CompletedTask;
        }

        // Share-change evictions are triggered by DataverseWebApiService's share writes (through IRecordShareWriteObserver);
        // this host replaces the share seam with the fixture's share table, so no share write reaches the client and none
        // reaches here; not recorded.
        public Task InvalidateRecordShareChangeAsync(string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private async Task<(HttpResponseMessage Response, RecordingInvalidator Evictions)> ProvisionRecordingEvictionsAsync(object body)
    {
        var recorder = new RecordingInvalidator();
        using var host = _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(recorder);
        }));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");
        var response = await client.PostAsJsonAsync(Route, body);
        return (response, recorder);
    }

    /// <summary>A successful run evicts the record exactly once (the move, the creator's and the colleagues' shares).</summary>
    [Fact]
    public async Task Evictions_ASuccessfulRun_EvictsTheRecordOnce()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: Guid.NewGuid());

        var (response, evictions) = await ProvisionRecordingEvictionsAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        evictions.OwnerChanges.Should().Equal(("sprk_project", "sprk_projects", projectId));
    }

    /// <summary>
    /// A verified compensation: the forward move and the move back are two owner changes (one eviction each), and each
    /// child put back on its own owner is evicted once; the child that already had the record's owner was never written,
    /// so it is not evicted.
    /// </summary>
    [Fact]
    public async Task Evictions_ACompensation_EvictsBothMovesAndEveryChildPutBack()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        var sameOwnerLocation = Guid.NewGuid();
        var ownOwnerLocation = Guid.NewGuid();
        var ownOwnerDocument = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedCascadeChild(projectId, Location, sameOwnerLocation, DataversePrincipalRef.Team(businessUnitTeam));
        _fixture.SeedCascadeChild(projectId, Location, ownOwnerLocation, DataversePrincipalRef.User(Guid.NewGuid()));
        _fixture.SeedCascadeChild(projectId, Document, ownOwnerDocument, DataversePrincipalRef.Team(Guid.NewGuid()));
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.FailStrictShareReadWhileSecureOwned = true; // the post-move proof fails → compensate

        var (response, evictions) = await ProvisionRecordingEvictionsAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("childOwnersRestored").GetBoolean().Should().BeTrue();
        evictions.OwnerChanges.Should().BeEquivalentTo(new[]
        {
            ("sprk_project", "sprk_projects", projectId),                                  // the move to the team
            ("sprk_project", "sprk_projects", projectId),                                  // the move back
            ("sharepointdocumentlocation", "sharepointdocumentlocations", ownOwnerLocation), // put back
            ("sharepointdocument", "sharepointdocuments", ownOwnerDocument),                 // put back
        });
    }

    /// <summary>
    /// The move back cannot be read back: it may have landed, so it is evicted as well (the forward move already was);
    /// nothing is put back, so no child is evicted.
    /// </summary>
    [Fact]
    public async Task Evictions_AnUnverifiedMoveBack_EvictsTheRecordForBothMoves_AndNoChild()
    {
        var projectId = Guid.NewGuid();
        var businessUnitTeam = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: businessUnitTeam);
        _fixture.SeedCascadeChild(projectId, Location, Guid.NewGuid(), DataversePrincipalRef.User(Guid.NewGuid()));
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.FailStrictShareReadWhileSecureOwned = true;
        _fixture.FailOwnerReadBackAfterBindTo = businessUnitTeam;

        var (response, evictions) = await ProvisionRecordingEvictionsAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        evictions.OwnerChanges.Should().Equal(
            ("sprk_project", "sprk_projects", projectId), ("sprk_project", "sprk_projects", projectId));
    }

    /// <summary>A forward move that cannot be read back may have landed: evicted once, before the failure is reported.</summary>
    [Fact]
    public async Task Evictions_AnUnverifiedForwardMove_EvictsTheRecordOnce()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: Guid.NewGuid());
        _fixture.OwnerReadBackFails = true;

        var (response, evictions) = await ProvisionRecordingEvictionsAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        evictions.OwnerChanges.Should().Equal(("sprk_project", "sprk_projects", projectId));
    }

    /// <summary>A refusal before any owner move (the cascaded rows cannot be read) changes nothing and evicts nothing.</summary>
    [Fact]
    public async Task Evictions_ARefusalBeforeAnyMove_EvictsNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: Guid.NewGuid());
        _fixture.CascadeChildSnapshotReadFailsWith = HttpStatusCode.ServiceUnavailable;

        var (response, evictions) = await ProvisionRecordingEvictionsAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("ownerid@odata.bind"), "precondition: nothing moved");
        evictions.OwnerChanges.Should().BeEmpty();
    }
}
