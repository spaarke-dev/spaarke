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
/// <para><b>What provisioning now does:</b> resolves the ONE canonical Secure Project business unit by
/// name from configuration, assigns the project to that business unit's default owner team, creates the
/// project's own SPE container, and records it on <c>sprk_containerid</c>. It creates no business unit,
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
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectCarriesAWizardCascadedContainerId_StillProvisions()
    {
        var projectId = Guid.NewGuid();
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
    /// An absent Secure Project business unit fails closed — and provisions nothing.
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
    /// The project ends up owned by the Secure Project business unit's default owner team.
    /// </summary>
    /// <remarks>
    /// Design.md §5.1a: the default owner team, NOT a service account. Every business unit is created
    /// with one, so it needs no provisioning — no licence, no credential to rotate, no extra identity
    /// to audit. Ownership is asserted by reading the row back rather than by observing the PATCH,
    /// because observing the PATCH is exactly what the old code did wrong.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_AssignsTheProject_ToTheBusinessUnitsDefaultOwnerTeam()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.OwningTeamOf(projectId).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("ownerTeamId").GetString()
            .Should().Contain(ProvisionProjectTestFixture.SecureOwnerTeamId.ToString());
    }

    /// <summary>
    /// Ownership is assigned BEFORE the container is created.
    /// </summary>
    /// <remarks>
    /// Ownership is the SECURITY step; the container is the storage step. If the container step fails
    /// after ownership, the project is at least correctly owned inside the Secure Project business
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
            "outside the Secure Project business unit");
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

    /// <summary>A business unit with no default owner team fails closed.</summary>
    [Fact]
    public async Task ProvisionProject_WhenTheBusinessUnitHasNoDefaultOwnerTeam_FailsClosed()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.OwnerTeamMatchCount = 0;
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonOwnerTeamNotFound);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
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
    /// Claimed-but-incomplete is still refused, and the refusal says which state it found.
    /// </summary>
    /// <remarks>
    /// Ownership without a container means an earlier run claimed the project and then failed. A blind
    /// re-run is where the most damage happens, so it is refused — but the operator has to be able to
    /// tell "this already worked" from "this half-worked", or the natural response is to try again.
    /// </remarks>
    [Fact]
    public async Task ProvisionProject_WhenOwnershipWasClaimedButNoContainerRecorded_IsRefusedAndSaysSo()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(
            projectId,
            owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId,
            containerId: null);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no SPE container recorded",
            "the operator needs to distinguish a completed provision from a claimed-then-failed one");
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
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

    /// <summary>A non-secure project is rejected — provisioning is only for secure projects.</summary>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectIsNotSecure_IsRejectedAndWritesNothing()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        using var client = _fixture.CreateEntitledClient();

        var response = await ProvisionAsync(client, projectId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Updates.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
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
        select.Should().Contain("_sprk_securitybu_value",
            "still read, to refuse projects provisioned by the retired BU-per-project mechanism");

        select.Should().NotContain("_sprk_securitybuid_value",
            "sprk_securitybuid does not exist on sprk_project");
        select.Should().NotContain("sprk_specontainerid",
            "sprk_specontainerid exists on sprk_container, not sprk_project — that is where the name " +
            "was borrowed from");
    }

    /// <summary>
    /// The default Secure Project business-unit name matches the deployed environment.
    /// </summary>
    /// <remarks>
    /// <b>SINGULAR.</b> design.md §5.1 and this task's own POML both specified "Secure Projects"
    /// (plural); live Dataverse metadata (2026-08-25) says the business unit is named
    /// <c>Secure Project</c>. Shipping the plural would have failed closed on every call — the correct
    /// direction, but for a fabricated reason, and it would have looked like a missing environment
    /// rather than a wrong string. Pinned here because a default that is wrong is worse than no
    /// default: it fails only at runtime, in an environment nobody is watching.
    /// </remarks>
    [Fact]
    public void DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed()
    {
        ProvisionProjectEndpoint.DefaultSecureBusinessUnitName.Should().Be("Secure Project");
        ProvisionProjectEndpoint.DefaultSecureBusinessUnitName.Should().NotEndWith("Projects",
            "the deployed business unit is singular; the plural came from a design doc, not from metadata");
    }
}
