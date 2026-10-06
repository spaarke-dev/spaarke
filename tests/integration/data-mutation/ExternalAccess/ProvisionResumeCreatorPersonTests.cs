using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 133 b2 — owner round 7 item 2, option (a): the RESUME shares to <c>createdby</c> when
/// that is a usable person, else to the BFF-stamped <c>sprk_createdbyperson</c>, and still refuses when neither is.
/// </summary>
/// <remarks>
/// <para><b>Why the column exists.</b> An Office quick-create (and <c>POST /api/v1/work-assignments</c>) creates the
/// record APP-ONLY, so <c>createdby</c> is the BFF application user and <c>createdonbehalfby</c> is empty (live
/// 2026-10-01). Before b2 a stranded secure record of that kind could only be refused at resume; the BFF now records the
/// person on every create path (<see cref="RecordCreatorPerson"/>) and the resume shares to them.</para>
/// <para><b>F8 still holds:</b> whoever CALLS the resume never receives the share in their place, and a share another
/// person holds never turns a refusal into a completion.</para>
/// </remarks>
public class ProvisionResumeCreatorPersonTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string Route = "/api/v1/external-access/provision-project";

    private static readonly Guid AppUser = Guid.Parse("a0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Maker = Guid.Parse("b0000000-0000-0000-0000-0000000000b1");

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionResumeCreatorPersonTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.SystemUsers[AppUser] = (false, true);
        _fixture.SystemUsers[Maker] = (false, false);
    }

    private Task<HttpResponseMessage> ProvisionAsync(object body) =>
        _fixture.CreateEntitledClient().PostAsJsonAsync(Route, body);

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private void AssertNothingWritten()
    {
        _fixture.Grants.Should().BeEmpty("it is never shared to the caller or anyone else instead (F8)");
        _fixture.Modifies.Should().BeEmpty();
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The Office shape: a matter (or project, or work assignment) the BFF created app-only, stranded after the owner
    /// move. The resume shares to the person recorded in <c>sprk_createdbyperson</c> — exactly the creator rights — and
    /// finishes; the caller (an administrator here) receives nothing.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task Resume_OfAnAppCreatedRecord_SharesToTheRecordedPerson(string recordType)
    {
        var recordId = Guid.NewGuid();
        var team = ProvisionProjectTestFixture.SecureOwnerTeamId;
        switch (recordType)
        {
            case "project": _fixture.SeedProject(recordId, owningTeamId: team, createdBy: AppUser, createdByPerson: Maker); break;
            case "matter": _fixture.SeedMatter(recordId, owningTeamId: team, createdBy: AppUser, createdByPerson: Maker); break;
            default: _fixture.SeedWorkAssignment(recordId, owningTeamId: team, createdBy: AppUser, createdByPerson: Maker); break;
        }

        var response = await ProvisionAsync(new { recordType, recordId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().ContainSingle().Which.Should().Match<ProvisionProjectTestFixture.RecordedShare>(g =>
            g.Principal.Id == Maker && g.AccessRightsCsv == ProvisionProjectEndpoint.CreatorAccessRights);
        _fixture.ShareMaskOf(recordId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0,
            "the resume caller is not the creator and receives nothing (F8)");
        _fixture.ShareMaskOf(recordId, AppUser).Should().Be(0, "nothing is ever shared to the application user");
        _fixture.SomeoneCanOpen(recordId).Should().BeTrue();
        var body = await BodyOf(response);
        body.GetProperty("resumed").GetBoolean().Should().BeTrue();
        body.GetProperty("sharedToCreatorSystemUserId").GetGuid().Should().Be(Maker);
    }

    /// <summary>
    /// A usable <c>createdby</c> wins — the column is not even consulted, so a different person recorded there does not
    /// receive the share.
    /// </summary>
    [Fact]
    public async Task Resume_WhenCreatedByIsAUsablePerson_SharesToCreatedBy_NotTheColumn()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: creator, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(creator);
    }

    /// <summary>
    /// The owner's rule is "createdby when it is a usable human, else the column": a DISABLED createdby with a usable
    /// person recorded shares to that person.
    /// </summary>
    [Fact]
    public async Task Resume_WhenCreatedByIsDisabled_AndAPersonIsRecorded_SharesToThatPerson()
    {
        var projectId = Guid.NewGuid();
        var disabledCreator = Guid.NewGuid();
        _fixture.SystemUsers[disabledCreator] = (true, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: disabledCreator, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(Maker);
        _fixture.ShareMaskOf(projectId, disabledCreator).Should().Be(0);
    }

    /// <summary>
    /// Neither is a usable person: refused with <c>resume_creator_unavailable</c>, zero writes, and the extensions name
    /// the deciding state and column — the recorded person's when one is recorded, createdby's when none is.
    /// </summary>
    [Theory]
    [InlineData("disabled", "disabled", "sprk_createdbyperson")]
    [InlineData("application-user", "application-user", "sprk_createdbyperson")]
    [InlineData("absent", "absent", "sprk_createdbyperson")]
    [InlineData("none", "application-user", "createdby")]
    public async Task Resume_WhenNeitherIsAUsablePerson_RefusesAndWritesNothing(
        string recordedPerson, string expectedState, string expectedColumn)
    {
        var projectId = Guid.NewGuid();
        Guid? person = null;
        if (recordedPerson != "none")
        {
            person = Guid.NewGuid();
            switch (recordedPerson)
            {
                case "disabled": _fixture.SystemUsers[person.Value] = (true, false); break;
                case "application-user": _fixture.SystemUsers[person.Value] = (false, true); break;
                    // "absent": no systemuser row answers for it.
            }
        }
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: AppUser, createdByPerson: person);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await BodyOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        problem.GetProperty("creatorState").GetString().Should().Be(expectedState);
        problem.GetProperty("creatorColumn").GetString().Should().Be(expectedColumn);
        problem.GetProperty("createdByState").GetString().Should().Be("application-user");
        AssertNothingWritten();
    }

    /// <summary>
    /// The column does not exist — an environment where <c>Set-RecordCreatorPersonSchema.ps1</c> has not run answers its
    /// projection with a 400. That is deterministic, so it is NOT the transient <c>unreadable</c> (which the client offers
    /// the same caller as a retry that would fail every time): <c>creatorState: column-missing</c>, naming the column and
    /// the schema script, zero writes — never a substitute share.
    /// </summary>
    /// <remarks>
    /// <b>Rewritten by task 133 r1</b> (verifier round 4, finding 10). This test pinned <c>unreadable</c> for the absent
    /// column, which the wizard classified as retryable: its "Try securing again" would fail until the schema was applied.
    /// </remarks>
    [Fact]
    public async Task Resume_WhenTheColumnDoesNotExist_RefusesAsColumnMissing_NotAsARetry()
    {
        var projectId = Guid.NewGuid();
        _fixture.CreatorPersonColumnExists = false;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: AppUser);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        problem.GetProperty("creatorState").GetString().Should().Be("column-missing");
        problem.GetProperty("creatorColumn").GetString().Should().Be(RecordCreatorPerson.Column);
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("Set-RecordCreatorPersonSchema.ps1").And.Contain("-Verify")
            .And.Contain("repeats this refusal");
        detail.Should().NotContain("the same caller may)", "calling again cannot succeed until the schema is applied");
        AssertNothingWritten();
    }

    /// <summary>
    /// The column exists but its read fails TRANSIENTLY: 500 <c>unreadable</c> naming the column (the same caller may call
    /// again), zero writes; once the read works the same caller's call shares to the recorded person.
    /// </summary>
    /// <remarks>
    /// <b>Task 133 r2</b> (verifier round 5, seed P14): the failure is raised as the real <c>DataverseWebApiClient</c>
    /// raises it — <see cref="HttpRequestException"/> carrying the status — for the statuses a transient fault answers.
    /// Only a 400 is <c>column-missing</c>; reading "any HTTP failure" as one would turn a retryable 503 or 429 into an
    /// administrator's job.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Resume_WhenTheColumnReadFailsTransiently_Refuses500Unreadable_AndTheSameCallerCanRetry(
        HttpStatusCode transientStatus)
    {
        var projectId = Guid.NewGuid();
        _fixture.CreatorPersonReadFailsWith = transientStatus;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: AppUser, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("creatorState").GetString().Should().Be("unreadable");
        problem.GetProperty("creatorColumn").GetString().Should().Be(RecordCreatorPerson.Column);
        AssertNothingWritten();

        _fixture.CreatorPersonReadFailsWith = null;
        var retry = await ProvisionAsync(new { projectId });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(Maker);
    }

    /// <summary>
    /// <c>createdby</c> cannot be READ: the decision stops there (500 <c>unreadable</c>, naming createdby) rather than
    /// falling through to the column — createdby may well be a usable person, and the rule names it first. The usable
    /// person recorded in the column receives nothing on this call.
    /// </summary>
    [Fact]
    public async Task Resume_WhenCreatedByCannotBeRead_DoesNotFallThroughToTheColumn()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SystemUserReadFailsFor = creator;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: creator, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("creatorState").GetString().Should().Be("unreadable");
        problem.GetProperty("creatorColumn").GetString().Should().Be("createdby");
        AssertNothingWritten();

        _fixture.SystemUserReadFailsFor = null;
        var retry = await ProvisionAsync(new { projectId });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "the same caller's call succeeds once the read works");
        _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(creator);
    }

    /// <summary>
    /// The recorded person cannot be read (the systemuser read fails): 500 <c>unreadable</c>, zero writes.
    /// </summary>
    [Fact]
    public async Task Resume_WhenTheRecordedPersonCannotBeRead_Refuses500()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Guid.Empty, createdByPerson: Maker); // createdby absent: the column decides
        _fixture.SystemUserByIdReadSucceeds = false;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("creatorState").GetString().Should().Be("unreadable");
        problem.GetProperty("creatorColumn").GetString().Should().Be(RecordCreatorPerson.Column);
        AssertNothingWritten();
    }

    // ── Owner round 14 item 3 (task 133 c1-r4): a creator read Dataverse REFUSES is an administrator's job ──────────
    //
    // A 401/403 (the service's sign-in, or its Read privilege on the table, refused) repeats on every call, as the cascade
    // reads already treat it: creatorState "refused", HTTP 500, a detail that says calling again repeats it and names the
    // Read privilege — never "the same caller may" (the wizard offered a retry that failed every time). A 503 or 429 stays
    // the transient "unreadable". Every case writes nothing and shares to nobody instead (F8).

    /// <summary>The detail's recovery for each read state: an administrator for <c>refused</c>, the caller for <c>unreadable</c>.</summary>
    private static void AssertRecoveryFor(string state, JsonElement problem)
    {
        var detail = problem.GetProperty("detail").GetString();
        if (state == "refused")
        {
            detail.Should().Contain("Calling again repeats this refusal").And.Contain("Read privilege")
                .And.NotContain("(the same caller may)");
        }
        else
        {
            detail.Should().Contain("(the same caller may)").And.NotContain("repeats this refusal");
        }
    }

    /// <summary>
    /// The read of <c>sprk_createdbyperson</c> itself is refused 401/403: <c>refused</c>, naming the column — not
    /// <c>column-missing</c> (only a 400 to that read means the column is absent) and not the transient <c>unreadable</c>.
    /// Calling again with nothing changed is refused the same way.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Resume_WhenTheColumnReadIsRefused_Refuses500Refused_NotARetry(HttpStatusCode status)
    {
        var projectId = Guid.NewGuid();
        _fixture.CreatorPersonReadFailsWith = status;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: AppUser, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        problem.GetProperty("creatorState").GetString().Should().Be("refused");
        problem.GetProperty("creatorColumn").GetString().Should().Be(RecordCreatorPerson.Column);
        problem.GetProperty("creatorPersonState").GetString().Should().Be("refused");
        AssertRecoveryFor("refused", problem);
        AssertNothingWritten();

        var again = await ProvisionAsync(new { projectId });
        (await BodyOf(again)).GetProperty("creatorState").GetString().Should().Be("refused",
            "deterministic: the same call is refused the same way until an administrator acts");
        AssertNothingWritten();
    }

    /// <summary>
    /// <c>createdby</c>'s systemuser read fails: refused 401/403 → <c>refused</c>; 503/429 → <c>unreadable</c>. Either way
    /// the decision stops there (createdby may be a usable person) — the person recorded in the column receives nothing.
    /// After a transient failure the same caller's call succeeds.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unreadable")]
    [InlineData(HttpStatusCode.TooManyRequests, "unreadable")]
    public async Task Resume_WhenCreatedBysReadFails_ClassifiesItByTheReadsStatus(HttpStatusCode status, string state)
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SystemUserReadFailsFor = creator;
        _fixture.SystemUserReadFailsWith = status;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: creator, createdByPerson: Maker);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        problem.GetProperty("creatorState").GetString().Should().Be(state);
        problem.GetProperty("creatorColumn").GetString().Should().Be("createdby");
        problem.GetProperty("createdByState").GetString().Should().Be(state);
        AssertRecoveryFor(state, problem);
        AssertNothingWritten();

        if (state == "unreadable")
        {
            _fixture.SystemUserReadFailsFor = null;
            var retry = await ProvisionAsync(new { projectId });
            retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
            _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(creator);
        }
    }

    /// <summary>
    /// The recorded person's systemuser read fails (createdby absent, so the column decides): refused 401/403 →
    /// <c>refused</c>; 503/429 → <c>unreadable</c>. Zero writes either way.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unreadable")]
    [InlineData(HttpStatusCode.TooManyRequests, "unreadable")]
    public async Task Resume_WhenTheRecordedPersonsReadFails_ClassifiesItByTheReadsStatus(HttpStatusCode status, string state)
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: Guid.Empty, createdByPerson: Maker); // createdby absent: the column decides
        _fixture.SystemUserByIdReadSucceeds = false;
        _fixture.SystemUserReadFailsWith = status;

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await BodyOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        problem.GetProperty("creatorState").GetString().Should().Be(state);
        problem.GetProperty("creatorColumn").GetString().Should().Be(RecordCreatorPerson.Column);
        problem.GetProperty("creatorPersonState").GetString().Should().Be(state);
        AssertRecoveryFor(state, problem);
        AssertNothingWritten();
    }

    /// <summary>
    /// The FORWARD path never reads the column: an environment without it still provisions a new secure record
    /// (deploy-order tolerance — only a resume that needs the column reports it unreadable).
    /// </summary>
    [Fact]
    public async Task ForwardProvisioning_DoesNotNeedTheColumn()
    {
        var projectId = Guid.NewGuid();
        _fixture.CreatorPersonColumnExists = false;
        _fixture.SeedProject(projectId);

        var response = await ProvisionAsync(new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ProvisionProjectEndpoint.ProjectProvisioningSelect.Should().NotContain(RecordCreatorPerson.ValueColumn,
            "a Step-1 select naming the column would 400 every provisioning in an environment the schema has not reached");
    }

    /// <summary>
    /// A resume request naming colleagues whose caller's identity cannot be established is refused as
    /// <c>creator_unresolved</c> — it is NOT told it "is not the record's creator", which nothing showed (task 133
    /// verifier minor finding). Nothing is written; the same call without the list completes.
    /// </summary>
    [Fact]
    public async Task Resume_NamingColleagues_WhenTheCallerCannotBeIdentified_SaysSoRatherThanNotTheCreator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            createdBy: AppUser, createdByPerson: Maker);
        _fixture.CallerSystemUserIdResolves = false;

        var refused = await ProvisionAsync(new { projectId, sharePrincipalIds = new[] { Guid.NewGuid() } });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await BodyOf(refused);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);
        problem.GetProperty("detail").GetString().Should().Contain("could not be established")
            .And.NotContain("is not the record's creator").And.NotContain("only the person who created the record may do that while");
        AssertNothingWritten();

        var completed = await ProvisionAsync(new { projectId });
        completed.StatusCode.Should().Be(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => g.Principal.Id).Should().Equal(Maker);
    }
}
