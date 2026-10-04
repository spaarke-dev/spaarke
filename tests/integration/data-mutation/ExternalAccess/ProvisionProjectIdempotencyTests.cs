using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// <c>POST /api/v1/external-access/provision-project</c> — the re-scoped provisioning contract
/// (task 021, 2026-08-25).
///
/// <para><b>What provisioning now does:</b> resolves the ONE canonical Secure Record business unit by
/// name from configuration, assigns the project to that business unit's NAMED owner team (task 144 — never its
/// default team; the named-team invariants are pinned in <see cref="SecureNamedOwnerTeamProvisioningTests"/>), creates
/// the project's own SPE container, and records it on <c>sprk_containerid</c>. It creates no business unit,
/// no account, and never writes <c>sprk_externalaccount</c>.</para>
///
/// <para><b>The four defects these tests pin.</b></para>
/// <list type="number">
///   <item><b>BU-per-project.</b> The old code created a child business unit per project and parented
///   it to the ROOT business unit — contradicting design.md §5.1 ("no BU-per-project proliferation")
///   and, worse, placing those business units OUTSIDE the one NFR-05's standing assertion guards.</item>
///   <item><b>Client-data destruction, latent.</b> <c>sprk_externalaccount</c> is the project's CLIENT
///   lookup. Provisioning created a synthetic "External Access — {project}" account and aimed it at
///   that column; the write failed for five months on a wrong column name, and that failure is the
///   only reason no client was ever overwritten. Repairing the name — the original scope of this
///   task — would have activated the corruption.</item>
///   <item><b>The live 409 regression.</b> The idempotency guard added 2026-08-23 keyed on
///   <c>sprk_containerid</c>, which the Create Project wizard writes at CREATE time from the creating
///   user's business unit. Every secure project therefore answered "already provisioned" and none was
///   ever provisioned.</item>
///   <item><b>The silent stamp.</b> The recording PATCH failed and was swallowed into a 200, which is
///   why all of the above stayed invisible.</item>
/// </list>
/// </summary>
public class ProvisionProjectIdempotencyTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string Route = "/api/v1/external-access/provision-project";

    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionProjectIdempotencyTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;

        // The fixture is shared across the class; the write log and the environment switches are not.
        // Reset before every test so a "wrote nothing" assertion cannot fail on another test's
        // residue — or, worse, pass on it.
        _fixture.Reset();
    }

    private Task<HttpResponseMessage> ProvisionAsync(HttpClient client, Guid projectId) =>
        client.PostAsJsonAsync(Route, new { projectId, projectRef = "P-2026-0001" });

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The live regression — the most urgent thing this task fixes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A project carrying a wizard-cascaded <c>sprk_containerid</c> must still provision.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the live regression, and it is mine.</b> The 2026-08-23 guard treated a
    /// non-empty <c>sprk_containerid</c> as proof of provisioning. But
    /// <c>EntityCreationService.applyUserBuDefaults</c> writes that field at CREATE time, cascaded from
    /// the creating user's business unit, for every project including secure ones — and real
    /// <c>sprk_project</c> rows carry such values. So the wizard created the project with the field
    /// already populated, provisioning answered 409 "already provisioned", and the secure project was
    /// left pointing at the shared business-unit container that other users can reach.</para>
    ///
    /// <para>A guard against double-provisioning had become a guard against provisioning. The root
    /// error was choosing a marker without checking who else writes the field.</para>
    ///
    /// <para><b>Setup made explicit by task 133 b2.</b> The cascaded value IS the creating user's business unit's
    /// container, so the fixture now records it on that business unit. Since b2 provisioning classifies a recorded
    /// container before replacing it — a business unit's shared container is replaced, the record's OWN is kept (live
    /// 2026-10-02, 65a3fab2) — so without the business unit holding it, this value would read as the record's own. The
    /// contract pinned here is unchanged: the shared cascade value is replaced by the record's own container.</para>
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectCarriesAWizardCascadedContainerId_StillProvisions()
    {
        var projectId = Guid.NewGuid();
        _fixture.BusinessUnitContainers[Guid.NewGuid()] = "b!cascaded-from-users-bu";
        _fixture.SeedProject(projectId, owningTeamId: null, containerId: "b!cascaded-from-users-bu");
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a container id cascaded by the wizard from the creating user's business unit is not " +
            "evidence that this project was provisioned — it is shared state, and treating it as a " +
            "marker 409'd every secure project");

        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId,
            "the cascaded shared container must be replaced by the project's own");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Never create a BU, never create an account, never write the client column
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Provisioning creates no Dataverse rows at all — no business unit, no account.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_OnTheHappyPath_CreatesNoBusinessUnitAndNoAccount()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.CreatedEntitySets.Should().BeEmpty(
            "design.md §5.1 says 'no BU-per-project proliferation', and the per-project account was " +
            "consumed by nothing — a business unit created here would also sit OUTSIDE the business " +
            "unit NFR-05's assertion guards, which is worse than redundant");
    }

    /// <summary>
    /// <c>sprk_externalaccount</c> never appears in any write payload.
    /// </summary>
    /// <remarks>
    /// The single most important assertion in this file. That column is the project's CLIENT
    /// (<c>ProjectLiveFactResolver.cs:33</c> maps the predicate <c>client</c> to it). The retired code
    /// aimed a synthetic account at it; had the column name been repaired rather than the mechanism
    /// removed, provisioning a secure project would have overwritten its client with a junk record.
    /// Asserted on the payload rather than the entity set, because the offending write targeted
    /// <c>sprk_projects</c> — an entity set provisioning legitimately writes.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_OnTheHappyPath_NeverWritesTheClientLookup()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        await ProvisionAsync(client, projectId);

        var writtenColumns = _fixture.Updates.SelectMany(u => u.Payload.Keys).ToArray();

        writtenColumns.Should().NotContain(
            c => c.Contains("externalaccount", StringComparison.OrdinalIgnoreCase),
            "sprk_externalaccount is the project's CLIENT lookup; writing it would replace the client " +
            "with a synthetic 'External Access — {project}' account");

        writtenColumns.Should().NotContain(
            c => c.Contains("securitybu", StringComparison.OrdinalIgnoreCase),
            "there is no per-project security business unit to record any more");
    }

    /// <summary>
    /// The container is recorded as a plain string — no <c>@odata.bind</c>, no navigation property.
    /// </summary>
    /// <remarks>
    /// This is what dissolved the blocker that stopped the first attempt at this task.
    /// <c>sprk_containerid</c> is <c>NVARCHAR(100)</c> (live metadata), so it needs no case-sensitive
    /// PascalCase navigation property — the thing that could not be recovered offline and that, if
    /// guessed wrong, is silently accepted and ignored.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_RecordsTheContainer_AsAPlainStringNotAnODataBind()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        await ProvisionAsync(client, projectId);

        var stamp = _fixture.Updates
            .SingleOrDefault(u => u.Payload.ContainsKey("sprk_containerid"));

        stamp.Should().NotBeNull("the container must be recorded on the project");
        stamp!.Payload["sprk_containerid"].Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        stamp.Payload.Keys.Should().NotContain(k => k.Contains("sprk_containerid@odata.bind"),
            "sprk_containerid is NVARCHAR(100), not a lookup");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Business unit resolved BY NAME, and failing closed when it cannot be
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The configured business-unit name is what gets resolved, and the response reports it.</summary>
    [Fact]
    public async Task ProvisionProject_ResolvesTheSecureBusinessUnit_ByTheConfiguredName()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("businessUnitName").GetString()
            .Should().Be(ProvisionProjectTestFixture.SecureBuName);
        body.RootElement.GetProperty("businessUnitId").GetString()
            .Should().Contain(ProvisionProjectTestFixture.SecureBuId.ToString());
    }

    /// <summary>
    /// An absent Secure Record business unit fails closed — and provisions nothing.
    /// </summary>
    /// <remarks>
    /// The alternative behaviours are both disclosures: falling back to the ROOT business unit (what
    /// the retired code did) or to the caller's own puts a secure record where ordinary users reach it
    /// at Deep depth (design.md §5.2). A loud stop is recoverable; a silent substitution is not
    /// detectable from outside at all.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheSecureBusinessUnitIsAbsent_FailsClosedAndProvisionsNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SecureBuMatchCount = 0;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureBuNotFound);

        _fixture.Updates.Should().BeEmpty("nothing may be written when the target BU cannot be resolved");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("no container may be created either");
        _fixture.OwningTeamOf(projectId).Should().BeNull("ownership must not have moved");
    }

    /// <summary>
    /// An ambiguous business-unit name fails closed rather than taking an arbitrary row.
    /// </summary>
    /// <remarks>
    /// This is why the lookup selects <c>$top=2</c>. With <c>$top=1</c> ambiguity is invisible: the
    /// endpoint silently accepts whichever row Dataverse returns first, and an arbitrary owner is
    /// precisely the class of silent wrongness this task removes.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheBusinessUnitNameIsAmbiguous_FailsClosed()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SecureBuMatchCount = 2;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonSecureBuAmbiguous);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ownership: the owner team, verified by read-back
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The project ends up owned by the Secure Record business unit's NAMED owner team — never its default team.
    /// </summary>
    /// <remarks>
    /// <b>Rewritten by task 144 (#967).</b> This test used to pin the business unit's DEFAULT owner team. That was the
    /// defect: Dataverse maintains a default team's membership from each user's business unit and it cannot be
    /// curated, so a user moved into the Secure Record BU silently became a member of the team owning every secure
    /// record. Ownership is asserted by reading the row back rather than by observing the PATCH, because observing the
    /// PATCH is exactly what the old code did wrong.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_AssignsTheProject_ToTheBusinessUnitsNamedOwnerTeam_NeverItsDefaultTeam()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId)
            .And.NotBe(ProvisionProjectTestFixture.SecureDefaultTeamId,
                "the default team's membership follows every user placed in the business unit");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("ownerTeamId").GetString()
            .Should().Contain(ProvisionProjectTestFixture.SecureOwnerTeamId.ToString());
        body.RootElement.GetProperty("ownerTeamName").GetString()
            .Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamName);
    }

    /// <summary>
    /// Ownership is assigned BEFORE the container is created.
    /// </summary>
    /// <remarks>
    /// Ownership is the SECURITY step; the container is the storage step. If the container step fails
    /// after ownership, the project is at least correctly owned inside the Secure Record business
    /// unit. Reversed, the same failure leaves a secure project owned by its creating user in an
    /// Operations business unit — strictly the worse posture. So the order is load-bearing, not
    /// incidental, and is pinned here.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenContainerCreationFails_TheProjectIsStillOwnedByTheSecureTeam()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SpeContainerCreationSucceeds = false;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse("a container that was not created is not a success");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId,
            "ownership is assigned first precisely so a storage failure does not leave the record " +
            "outside the Secure Record business unit");
    }

    /// <summary>
    /// An ownership PATCH that Dataverse accepts but does not apply is detected, not trusted.
    /// </summary>
    /// <remarks>
    /// <para>The failure mode: an unrecognised <c>@odata.bind</c> property is accepted and IGNORED — no
    /// error, no write. That is what hid the old stamping bug for five months, and it is not defended
    /// against by getting the name right, because "did I get the name right?" is unanswerable offline.
    /// Reading the owner back turns an unverifiable assumption into an observed fact.</para>
    ///
    /// <para>Nothing may be provisioned when the claim did not land: a container created for a project
    /// that is not actually owned by the secure team is an orphan attached to an unsecured record.</para>
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheOwnershipPatchIsSilentlyIgnored_FailsAndCreatesNoContainer()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnershipPatchIsApplied = false;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should()
            .Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentNotApplied);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty(
            "a container provisioned for a project that is not actually owned by the secure team is " +
            "an orphan attached to an unsecured record");
    }

    /// <summary>
    /// A business unit with no NAMED owner team fails closed — and does not fall back to its default team, which the
    /// roster always contains (task 144; rewritten from the default-team version of this test).
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheNamedOwnerTeamIsMissing_FailsClosedAndNeverUsesTheDefaultTeam()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerTeamMatchCount = 0;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamNotFound);
        _fixture.Updates.Should().BeEmpty("the default team is present in the roster and must not be chosen instead");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.OwningTeamOf(projectId).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fail loud when the container cannot be recorded (ADR-003)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A container that was created but could not be recorded returns non-2xx AND names the container.
    /// </summary>
    /// <remarks>
    /// This replaces a <c>catch</c> + <c>LogWarning</c> + <c>return 200</c>. That swallow is the single
    /// reason the broken column names survived five months: provisioning created real infrastructure,
    /// failed to record any of it, and reported success. The id is what makes the orphan reconcilable —
    /// a container nobody recorded is otherwise invisible.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheContainerCannotBeRecorded_FailsLoudlyAndNamesTheContainer()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.ContainerStampSucceeds = false;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse(
            "a run that cannot record what it created must not report success (ADR-003)");
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonContainerNotRecorded);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("speContainerId").GetString()
            .Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId,
                "an operator cannot reconcile an orphaned container whose id was never reported");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency, on a marker only provisioning writes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A project already owned by the secure owner team is refused, and nothing is written.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the write count, not the status code: a 409 that still created a
    /// second SPE container would be the original defect wearing a better status code.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenAlreadyOwnedBySecureTeam_IsRefusedAndWritesNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(
            projectId,
            owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            containerId: "b!already-provisioned");
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "re-provisioning would orphan the container the project's documents already live in");
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// Claimed-but-incomplete is RESUMED: the share goes to the record's <c>createdby</c> user (exactly the creator
    /// rights), a container is created and recorded, 200 — and the caller, an administrator here, gets no share.
    /// </summary>
    /// <remarks>
    /// <b>Rewritten by task 133 (C11).</b> This test pinned a 409 for this state. That refusal WAS the lock-out: a
    /// record owned by the memberless team with no creator share could not be finished by its creator (no Write) nor
    /// by anyone else (409). Task 076 made Step 7 the only writer of <c>sprk_containerid</c>, so "owned, no container"
    /// now reliably means "an earlier run stopped after the move", which is safe to finish.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenOwnershipWasClaimedButNoContainerRecorded_ResumesForTheRecordsCreator()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(
            projectId,
            owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            containerId: null,
            createdBy: creator);
        using var client = _fixture.CreateEntitledClient(); // the caller is NOT the creator

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().ContainSingle().Which.Should().Match<ProvisionProjectTestFixture.RecordedShare>(g =>
            g.Principal.Id == creator && g.AccessRightsCsv == ProvisionProjectEndpoint.CreatorAccessRights);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0,
            "a resume never widens the access list to whoever called it (owner decision F8)");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("ownerid@odata.bind"),
            "the record is already owned by the team");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharedToCreatorSystemUserId").GetGuid().Should().Be(creator);
    }

    /// <summary>A resume whose creator already holds exactly the creator rights writes no second share.</summary>
    [Fact]
    public async Task ProvisionProject_WhenResumingAndTheCreatorShareExists_IssuesNoSecondGrant()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.SeedShare(projectId, Spaarke.Dataverse.DataversePrincipalRef.User(ProvisionProjectTestFixture.CallerSystemUserId),
            ProvisionProjectEndpoint.CreatorAccessRights);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// A resume whose <c>createdby</c> is not a usable person refuses with its own reason code — and shares to nobody,
    /// least of all the caller — creating nothing. That holds even when ANOTHER enabled person already holds a Read
    /// share (task 133 verifier round 2): the closed acceptance criterion and owner decision F8's interim default are a
    /// refusal, so a share someone else holds does not turn it into a completion. An unreadable <c>createdby</c> is a
    /// 500 (a read failed — transient), the other states a 409.
    /// </summary>
    [Theory]
    [InlineData("disabled", HttpStatusCode.Conflict)]
    [InlineData("application-user", HttpStatusCode.Conflict)]
    [InlineData("absent", HttpStatusCode.Conflict)]
    [InlineData("unreadable", HttpStatusCode.InternalServerError)]
    public async Task ProvisionProject_WhenResumingAndTheCreatorIsNotAUsablePerson_RefusesAndSharesToNobody(
        string state, HttpStatusCode expectedStatus)
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var otherPerson = Guid.NewGuid();
        switch (state)
        {
            case "disabled": _fixture.SystemUsers[creator] = (true, false); break;
            case "application-user": _fixture.SystemUsers[creator] = (false, true); break;
            case "unreadable": _fixture.SystemUsers[creator] = (false, false); _fixture.SystemUserByIdReadSucceeds = false; break;
        }
        _fixture.SystemUsers[otherPerson] = (false, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        _fixture.SeedShare(projectId, Spaarke.Dataverse.DataversePrincipalRef.User(otherPerson),
            ProvisionProjectEndpoint.CollaboratorAccessRights);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(expectedStatus);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("creatorState").GetString().Should().Be(state);
        _fixture.Grants.Should().BeEmpty("it is never shared to the caller as a substitute");
        _fixture.Modifies.Should().BeEmpty();
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty(
            "another person's share does not complete the resume (F8 interim default: refusal)");
    }

    /// <summary>
    /// A <c>createdby</c> whose <c>isdisabled</c> reads as null is not proven enabled, so it is treated as disabled —
    /// only a user read back as enabled is someone a secure record is kept open for (task 133 verifier round 2).
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenResumingAndTheCreatorsDisabledFlagIsNull_TreatsThemAsDisabled()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (null, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("creatorState").GetString().Should().Be("disabled");
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// A recovery <c>resume_creator_unavailable</c> states is real (task 133 verifier round 2), driven against the
    /// endpoint: a DISABLED creator, re-enabled by an administrator, lets the next call resume and share to them.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheCreatorIsDisabled_ReEnablingThemLetsTheResumeComplete()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (true, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        using var client = _fixture.CreateEntitledClient(); // an administrator: Write through their role

        (await ReasonCodeOf(await ProvisionAsync(client, projectId)))
            .Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        (await ProvisionAsync(client, projectId)).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "calling again without the stated recovery repeats the refusal");

        _fixture.SystemUsers[creator] = (false, false); // the administrator re-enables the creator
        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().ContainSingle().Which.Principal.Id.Should().Be(creator);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0);
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// An app-created record (Office quick-create: <c>createdby</c> is the BFF application user) cannot be resumed —
    /// and the stated recovery works: an administrator assigns it to the person who should hold it, which takes it out
    /// of the owner team, and THAT person's call provisions it from the start, sharing it to them.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheCreatorIsAnApplication_AssigningTheRecordToAPersonLetsThemProvisionIt()
    {
        var projectId = Guid.NewGuid();
        var appCreator = Guid.NewGuid();
        _fixture.SystemUsers[appCreator] = (false, true);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: appCreator);
        using var client = _fixture.CreateEntitledClient();

        (await ReasonCodeOf(await ProvisionAsync(client, projectId)))
            .Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        _fixture.Grants.Should().BeEmpty();

        // The administrator's Assign: the record is owned by the intended person (here, the next caller).
        _fixture.SeedProject(projectId, owningUserId: ProvisionProjectTestFixture.CallerSystemUserId, createdBy: appCreator);
        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeFalse("it ran from the start");
        body.RootElement.GetProperty("sharedToCreatorSystemUserId").GetGuid()
            .Should().Be(ProvisionProjectTestFixture.CallerSystemUserId);
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask);
        _fixture.ShareMaskOf(projectId, appCreator).Should().Be(0, "nothing is ever shared to the application user");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// The resume's share to <c>createdby</c> cannot be written: the run stops with <c>creator_share_failed</c>
    /// (<c>resumed: true</c>) BEFORE any container is created or recorded (task 133 verifier round 2). Continuing would
    /// create and record a container on a team-owned record nobody holds a share on — the C11 locked box, made
    /// permanent, because the next call would then answer 409 <c>already_provisioned</c>.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheResumesCreatorShareFails_StopsBeforeTheContainer()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        _fixture.FailShareWhileSecureOwned = creator;
        var openableBefore = _fixture.SomeoneCanOpen(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
        }
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty("no container on a record nobody can open");
        _fixture.Updates.Should().NotContain(u => u.Payload.ContainsKey("sprk_containerid"));
        _fixture.ContainerIdOf(projectId).Should().BeNull("an unrecorded container keeps the record resumable");
        _fixture.SomeoneCanOpen(projectId).Should().Be(openableBefore);

        _fixture.FailShareWhileSecureOwned = null;
        var retry = await ProvisionAsync(client, projectId);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "the record stayed resumable, so the next call finishes it");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();
    }

    /// <summary>
    /// <c>createdby</c> unreadable and the caller IS <c>createdby</c>, naming a colleague, while another person holds a
    /// share: refused before any write — no colleague is shared while the creator's share is unproven (task 133
    /// verifier round 2).
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenResumingWithAnUnreadableCreator_SharesNoColleague()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        var otherPerson = Guid.NewGuid();
        _fixture.SystemUsers[otherPerson] = (false, false);
        _fixture.SystemUserByIdReadSucceeds = false;
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId); // createdby = caller
        _fixture.SeedShare(projectId, Spaarke.Dataverse.DataversePrincipalRef.User(otherPerson),
            ProvisionProjectEndpoint.CollaboratorAccessRights);
        using var client = _fixture.CreateEntitledClient();

        var response = await client.PostAsJsonAsync(Route, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The record's own creator, named in <c>sharePrincipalIds</c> by a caller who is NOT the creator, is not a
    /// colleague: the resume is not refused, and the creator receives only the creator share (task 133 verifier
    /// round 2).
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenANonCreatorResumeNamesOnlyTheCreator_CompletesWithTheCreatorShareOnly()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        using var client = _fixture.CreateEntitledClient(); // the caller is NOT the creator

        var response = await client.PostAsJsonAsync(Route, new { projectId, sharePrincipalIds = new[] { creator } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().ContainSingle().Which.Should().Match<ProvisionProjectTestFixture.RecordedShare>(g =>
            g.Principal.Id == creator && g.AccessRightsCsv == ProvisionProjectEndpoint.CreatorAccessRights);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("additionalPrincipalsShared").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// A colleague who already holds a share (an earlier run got that far) is not shared to again — a second
    /// GrantAccess would union Collaborate into whatever they hold — and is counted as shared.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenAColleagueAlreadyHoldsAShare_IsNotSharedAgain()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId); // createdby = caller
        _fixture.SeedShare(projectId, Spaarke.Dataverse.DataversePrincipalRef.User(colleague),
            Sprk.Bff.Api.Services.Access.RecordShareLevels.ViewOnlyRights);
        using var client = _fixture.CreateEntitledClient();

        var response = await client.PostAsJsonAsync(Route, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Should().NotContain(g => g.Principal.Id == colleague);
        _fixture.ShareMaskOf(projectId, colleague).Should().Be(
            Sprk.Bff.Api.Services.Access.RecordShareLevels.MaskForRightsCsv(
                Sprk.Bff.Api.Services.Access.RecordShareLevels.ViewOnlyRights));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("additionalPrincipalsShared").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// A resume caller who is NOT the record's creator cannot add people — themselves included — through
    /// <c>sharePrincipalIds</c> (task 133 verifier round 1). Refused before any write; the same call without the list
    /// completes the resume, sharing only to the creator.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenANonCreatorResumesWithSharePrincipalIds_RefusesBeforeAnyWrite()
    {
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        _fixture.SystemUsers[creator] = (false, false);
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId, createdBy: creator);
        using var client = _fixture.CreateEntitledClient(); // the caller is NOT the creator

        var refused = await client.PostAsJsonAsync(Route, new
        {
            projectId,
            sharePrincipalIds = new[] { ProvisionProjectTestFixture.CallerSystemUserId }
        });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(refused)).Should().Be(ProvisionProjectEndpoint.ReasonResumeColleaguesNotPermitted);
        _fixture.Grants.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId).Should().Be(0,
            "the resume caller receives no share unless they are the creator");

        var completed = await ProvisionAsync(client, projectId);

        completed.StatusCode.Should().Be(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => g.Principal.Id).Should().BeEquivalentTo(new[] { creator });
    }

    /// <summary>
    /// A resume runs every ensure step the forward path runs: the creator share, the named colleagues, the container
    /// and its record — everything but the owner move it no longer needs.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_Resume_RunsEveryStepTheForwardPathRunsExceptTheMove()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        using var client = _fixture.CreateEntitledClient();

        var response = await client.PostAsJsonAsync(Route, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Grants.Select(g => (g.Principal.Id, g.AccessRightsCsv)).Should().BeEquivalentTo(new[]
        {
            (ProvisionProjectTestFixture.CallerSystemUserId, ProvisionProjectEndpoint.CreatorAccessRights),
            (colleague, ProvisionProjectEndpoint.CollaboratorAccessRights)
        });
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle();
        _fixture.Updates.Should().ContainSingle().Which.Payload.Should().ContainKey("sprk_containerid");
    }

    /// <summary>
    /// Self-service resume after a container failure: the record is left secured and shared with no container; the
    /// CREATOR's own next call (their Write comes from that share) completes it, without a second creator grant.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_AfterAContainerFailure_TheCreatorsOwnRetryResumesAndCompletes()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SpeContainerCreationSucceeds = false;
        using var client = _fixture.CreateEntitledClient();

        var first = await ProvisionAsync(client, projectId);

        (await ReasonCodeOf(first)).Should().Be(ProvisionProjectEndpoint.ReasonContainerCreationFailed);
        (await first.Content.ReadAsStringAsync()).Should().Contain("resumes from here");
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue("the creator's share is in place");

        _fixture.SpeContainerCreationSucceeds = true;
        var retry = await ProvisionAsync(client, projectId);

        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        _fixture.Grants.Where(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().ContainSingle("the resumed run finds the share and does not issue it again");
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// After a container that could not be RECORDED, the next call resumes too, and records a new container.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_AfterTheContainerCouldNotBeRecorded_TheNextCallResumesAndRecordsOne()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.ContainerStampSucceeds = false;
        using var client = _fixture.CreateEntitledClient();

        var first = await ProvisionAsync(client, projectId);
        (await ReasonCodeOf(first)).Should().Be(ProvisionProjectEndpoint.ReasonContainerNotRecorded);

        _fixture.ContainerStampSucceeds = true;
        var retry = await ProvisionAsync(client, projectId);

        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    /// <summary>
    /// The owner PATCH committed but its read-back threw: the response no longer says nothing was provisioned — it says
    /// the outcome is unverified and the creator's share is in place — and the next call behaves by the OBSERVED state
    /// (here: owned by the team, no container → resume).
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheOwnerReadBackThrows_SaysUnverified_AndTheNextCallResumes()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerReadBackFails = true;
        using var client = _fixture.CreateEntitledClient();

        var first = await ProvisionAsync(client, projectId);

        (await ReasonCodeOf(first)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);
        var detail = await first.Content.ReadAsStringAsync();
        detail.Should().NotContain("Nothing has been provisioned").And.Contain("is not known");
        _fixture.ShareMaskOf(projectId, ProvisionProjectTestFixture.CallerSystemUserId)
            .Should().Be(ProvisionProjectEndpoint.CreatorAccessMask, "the share-first grant is kept: S5");
        _fixture.SomeoneCanOpen(projectId).Should().BeTrue();

        _fixture.OwnerReadBackFails = false;
        var next = await ProvisionAsync(client, projectId);

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// An unconfigured container type is certain to stop Step 6, so it is refused BEFORE any change (task 133) — never
    /// after the record is owned by a memberless team.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheContainerTypeIsNotConfigured_RefusesBeforeChangingAnything()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.SetContainerTypeId("not-a-guid");
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonContainerTypeNotConfigured);
        _fixture.Updates.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    /// <summary>
    /// The same unverified read-back, when the PATCH had in fact NOT landed: the next call sees an unprovisioned record
    /// and provisions it from the start.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheOwnerReadBackThrowsAndThePatchDidNotLand_TheNextCallStartsOver()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerReadBackFails = true;
        _fixture.OwnershipPatchIsApplied = false;
        using var client = _fixture.CreateEntitledClient();

        (await ReasonCodeOf(await ProvisionAsync(client, projectId)))
            .Should().Be(ProvisionProjectEndpoint.ReasonOwnerAssignmentUnverified);

        _fixture.OwnerReadBackFails = false;
        _fixture.OwnershipPatchIsApplied = true;
        var next = await ProvisionAsync(client, projectId);

        next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("resumed").GetBoolean().Should().BeFalse();
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
    }

    /// <summary>
    /// A project still referencing a retired per-project security business unit is refused.
    /// </summary>
    /// <remarks>
    /// Nothing writes <c>sprk_securitybu</c> any more, but projects provisioned by the retired
    /// mechanism carry one. Silently re-provisioning such a project onto the canonical business unit
    /// would strand the old business unit and its container with nothing referencing them, so the
    /// migration is made a deliberate act rather than a side effect of a retry.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectCarriesALegacyPerProjectBusinessUnit_IsRefused()
    {
        var projectId = Guid.NewGuid();
        var legacyBu = Guid.NewGuid();
        _fixture.SeedProject(projectId, legacySecurityBuId: legacyBu, containerId: "b!legacy-container");
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonLegacyPerProjectBu);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("legacyBusinessUnitId").GetString()
            .Should().Contain(legacyBu.ToString(), "the operator needs to know which BU to migrate off");
        _fixture.Updates.Should().BeEmpty();
    }

    /// <summary>
    /// CONVERTED by task 150 (was "a non-secure project is rejected 400"). <c>sprk_issecure</c> is field-secured and
    /// provisioning is now its only writer, so a project from the client arrives UNFLAGGED and provisioning marks it
    /// secure as its first write. The full contract — first write, read-back, the flagged-already path, every refusal —
    /// is pinned in <see cref="SecureFlagEndpointWriteTests"/>.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectIsNotYetFlagged_MarksItSecureAndProvisions()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(projectId).Should().BeTrue();
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        _fixture.ContainerIdOf(projectId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The projection guard — added 2026-08-24 after this project INTRODUCED
    // the fifth instance of the stale-column class here
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every column the provisioning read projects must exist on <c>sprk_project</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this test exists.</b> Commit <c>95d3f0f68</c> — the commit that added the
    /// idempotency guard — put <c>_sprk_securitybuid_value,sprk_specontainerid</c> into the Step 1
    /// projection. Neither column exists on the table. Dataverse answers a bad projection with 400, the
    /// endpoint's <c>catch</c> turned that into a 500 with the cause hidden, and provisioning created
    /// nothing — while the guard built on those same two columns read null forever and could never
    /// fire.</para>
    ///
    /// <para>All five tests then in this file stayed green throughout, because the fixture returned
    /// canned rows regardless of the projection. Task 016 had already built the fix for that failure
    /// mode — a fake that rejects unknown columns the way Dataverse does — and it had not been carried
    /// across to this fixture.</para>
    /// </remarks>
    [Fact]
    public void ProjectProvisioningSelect_NamesOnlyColumnsThatExistOnTheTable()
    {
        var columns = ProvisionProjectEndpoint.ProjectProvisioningSelect
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        columns.Should().OnlyContain(c => ProvisionProjectTestFixture.LiveProjectColumns.Contains(c),
            "a $select naming a nonexistent column returns 400, which surfaces as a failed provision " +
            "rather than as a schema mistake");
    }

    /// <summary>
    /// The specific names that broke it, pinned so a revert cannot happen quietly — plus the marker
    /// column the re-scope depends on.
    /// </summary>
    [Fact]
    public void ProjectProvisioningSelect_ReadsTheOwningTeamAndRejectsTheRetiredNames()
    {
        var select = ProvisionProjectEndpoint.ProjectProvisioningSelect;

        select.Should().Contain("_owningteam_value",
            "ownership by the secure owner team IS the idempotency marker — without this column the " +
            "guard cannot fire at all");
        select.Should().Contain("sprk_containerid");
        select.Should().Contain("_owninguser_value",
            "task 133: with _owningteam_value, the pre-call owner a failed provisioning moves the record back to");
        select.Should().Contain("_createdby_value", "task 133: the person a resumed provisioning shares to");
        select.Should().Contain("_sprk_securitybu_value",
            "still read, to refuse projects provisioned by the retired BU-per-project mechanism");

        select.Should().NotContain("_sprk_securitybuid_value",
            "sprk_securitybuid does not exist on sprk_project");
        select.Should().NotContain("sprk_specontainerid",
            "sprk_specontainerid exists on sprk_container, not sprk_project — that is where the name " +
            "was borrowed from");
    }

    /// <summary>
    /// The default Secure Record business-unit name matches the deployed environment.
    /// </summary>
    /// <remarks>
    /// <b>RENAMED 2026-09-29 (task 121, D-12 §2):</b> the deployed BU is now <c>Secure Record</c>. It
    /// was renamed from <c>Secure Project</c> because the BU holds secure rows of THREE entity types
    /// (<c>sprk_project</c>, <c>sprk_matter</c>, <c>sprk_workassignment</c> all carry
    /// <c>sprk_issecure</c>), so naming it after one of them described the topology wrongly.
    ///
    /// <para><b>The lesson that put this test here, restated against the new name.</b> design.md §5.1
    /// and the authoring POML both said "Secure Projects" (plural); live Dataverse metadata
    /// (2026-08-25) said <c>Secure Project</c> — singular. Shipping the plural would have failed closed
    /// on every call: the correct DIRECTION, for a fabricated reason, looking like a missing
    /// environment rather than a wrong string. A default that is wrong is worse than no default —
    /// it fails only at runtime, in an environment nobody is watching.</para>
    ///
    /// <para>So this test pins the EXACT deployed string, and the guard assertions below encode the two
    /// ways this constant has been observed to go wrong: a pluralised variant, and a stale name left
    /// behind by a half-finished rename. Both are spelled out rather than covered by a single equality
    /// check, because the equality check alone reports "expected X, found Y" without saying which
    /// mistake was made.</para>
    /// </remarks>
    [Fact]
    public void DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed()
    {
        ProvisionProjectEndpoint.DefaultSecureBusinessUnitName.Should().Be("Secure Record");

        ProvisionProjectEndpoint.DefaultSecureBusinessUnitName.Should().NotEndWith("s",
            "the deployed business unit is SINGULAR; the plural came from a design doc, not from metadata");

        ProvisionProjectEndpoint.DefaultSecureBusinessUnitName.Should().NotContain("Project",
            "the BU was renamed Secure Project -> Secure Record (task 121). A 'Project' here means the "
            + "rename was only half applied — and because the name is a fail-closed lookup key, the "
            + "symptom is 'business unit not found', which reads like a missing environment");
    }

    /// <summary>
    /// The config key that overrides the default lives in the same section as its sibling.
    /// </summary>
    /// <remarks>
    /// Task 121 renamed the config section <c>SecureProject:</c> → <c>SecureRecord:</c>. There are TWO
    /// keys in it — this one and <c>UnsecureOwnerUserId</c> on
    /// <see cref="UnsecureProjectEndpoint"/> — and renaming one without the other would split a single
    /// section in two, leaving an operator to set half their configuration under each name. Nothing in
    /// the repo set either key, so the rename orphaned nothing; this test is what stops them drifting
    /// apart later.
    /// </remarks>
    [Fact]
    public void SecureRecordConfigKeys_ShareOneSection()
    {
        const string section = "SecureRecord:";

        ProvisionProjectEndpoint.SecureBusinessUnitNameConfigKey.Should().StartWith(section);
        ProvisionProjectEndpoint.SecureOwnerTeamNameConfigKey.Should().StartWith(section);
        UnsecureProjectEndpoint.UnsecureOwnerUserIdConfigKey.Should().StartWith(section);
    }

    /// <summary>
    /// The default owner-team name is the one the owner chose (decision F9, 2026-09-30) and is NOT the business unit's
    /// own name — the name its DEFAULT team carries.
    /// </summary>
    /// <remarks>
    /// A fail-closed lookup key, like the BU name above: the live team, this default, guide §4 and this test move
    /// together. The inequality is the load-bearing half. Were the two names equal, a configuration that dropped
    /// <c>isdefault eq false</c> from the lookup would select the default team silently — exactly the team task 144
    /// retires.
    /// </remarks>
    [Fact]
    public void DefaultSecureOwnerTeamName_IsTheOwnersChoice_AndNotTheBusinessUnitsOwnName()
    {
        ProvisionProjectEndpoint.DefaultSecureOwnerTeamName.Should().Be("Secure Record Owners");
        ProvisionProjectEndpoint.DefaultSecureOwnerTeamName.Should()
            .NotBe(ProvisionProjectEndpoint.DefaultSecureBusinessUnitName,
                "the BU's default team carries the BU's name; the owner team must be distinguishable from it");
    }
}
