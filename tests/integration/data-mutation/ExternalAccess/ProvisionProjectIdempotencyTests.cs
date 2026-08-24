using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// <c>POST /api/v1/external-access/provision-project</c> must not provision a project twice.
///
/// <para><b>What was wrong</b> (found in task 008 review, fixed 2026-08-23). Nothing on this path was
/// idempotent: not the client (<c>CreateProjectWizard/provisioningService.ts</c> posts once and does not
/// dedupe), not the route (unlike <c>/office/save</c>, this group carries no <c>IdempotencyFilter</c>),
/// and not Dataverse. A second call created a SECOND business unit, a SECOND SPE container and a SECOND
/// account, then overwrote <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c> and
/// <c>sprk_externalaccountid</c> on the project — repointing it at empty infrastructure while its
/// existing documents stayed in the original container.</para>
///
/// <para><b>Why it was reachable in practice.</b> Not double-clicking. Provisioning makes three slow
/// remote calls (BU create, Graph SPE container create, account create), so a client or proxy timeout
/// mid-flight is ordinary — and the reference-stamping step is deliberately non-fatal, so a run that
/// failed to record its own output still returns 200 having created real infrastructure. The natural
/// operator response to either is to run it again.</para>
///
/// <para><b>User-visible consequence of the bug:</b> an outside-counsel user silently stops seeing a
/// secure project's documents. No error is raised anywhere — the project simply points at an empty
/// container.</para>
/// </summary>
public class ProvisionProjectIdempotencyTests : IClassFixture<ProvisionProjectTestFixture>
{
    private readonly ProvisionProjectTestFixture _fixture;

    public ProvisionProjectIdempotencyTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;

        // The fixture is shared across the class; the write log is not. Reset before every test so a
        // "created nothing" assertion cannot fail on another test's residue — or, worse, pass on it.
        _fixture.Reset();
    }

    /// <summary>
    /// A project already carrying a Business Unit reference is refused with 409 — and, critically,
    /// NOTHING is created. The assertion on the create-count is the one that matters: a 409 that still
    /// left a stray business unit behind would be the same defect with a better status code.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenTheProjectIsAlreadyProvisioned_IsRefusedAndCreatesNothing()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _fixture.SeedSecureProject(projectId, businessUnitId: Guid.NewGuid(), speContainerId: "b!existing-container");
        using var client = _fixture.CreateEntitledClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/external-access/provision-project", new
        {
            projectId,
            projectRef = "P-2026-0001"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "re-provisioning would orphan the container the project's documents already live in");
        _fixture.CreatedEntitySets.Should().BeEmpty(
            "a refusal that still created a Business Unit, container or Account would leave exactly the " +
            "orphaned infrastructure this guard exists to prevent");
        _fixture.UpdatedEntitySets.Should().BeEmpty(
            "the project's existing infrastructure references must not be repointed");
    }

    /// <summary>
    /// Half-provisioned counts as provisioned. The reference-stamping step is non-fatal, so a project can
    /// legitimately end up with a container recorded but no BU (or the reverse) — and that partial state
    /// is where a blind re-run does the MOST damage, because the unrecorded half becomes invisible
    /// garbage. Requiring both references before refusing would let exactly that case through.
    /// </summary>
    [Theory]
    [InlineData(true, false)]   // BU recorded, container not
    [InlineData(false, true)]   // container recorded, BU not
    public async Task ProvisionProject_WhenOnlyPartiallyProvisioned_IsStillRefused(bool hasBu, bool hasContainer)
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _fixture.SeedSecureProject(
            projectId,
            businessUnitId: hasBu ? Guid.NewGuid() : null,
            speContainerId: hasContainer ? "b!partial-container" : null);
        using var client = _fixture.CreateEntitledClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/external-access/provision-project", new
        {
            projectId,
            projectRef = "P-2026-0002"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.CreatedEntitySets.Should().BeEmpty();
    }

    /// <summary>
    /// The negative twin, and the guard against over-blocking: a genuinely unprovisioned secure project
    /// must still provision. Without this the "fix" could be a blanket 409 and the tests above would not
    /// notice.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_ForAnUnprovisionedSecureProject_ProceedsPastTheIdempotencyGuard()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _fixture.SeedSecureProject(projectId, businessUnitId: null, speContainerId: null);
        using var client = _fixture.CreateEntitledClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/external-access/provision-project", new
        {
            projectId,
            projectRef = "P-2026-0003"
        });

        // Assert — the guard let it through; provisioning then fails downstream on the unavailable
        // test-host Graph/SPE services, which is not what this test is about.
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            "an unprovisioned secure project must still be provisionable");
        _fixture.CreatedEntitySets.Should().Contain("businessunits",
            "reaching Business Unit creation proves the request passed both the delegation gate and the guard");
    }

    /// <summary>
    /// The 409 tells the operator what already exists. A bare conflict would leave them unable to decide
    /// between "this already worked" and "something is wrong", which is how a second run gets attempted.
    /// </summary>
    [Fact]
    public async Task ProvisionProject_WhenRefused_NamesTheExistingInfrastructure()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var existingBu = Guid.NewGuid();
        _fixture.SeedSecureProject(projectId, businessUnitId: existingBu, speContainerId: "b!existing-container");
        using var client = _fixture.CreateEntitledClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/external-access/provision-project", new
        {
            projectId,
            projectRef = "P-2026-0004"
        });

        // Assert
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("businessUnitId").GetString()
            .Should().Contain(existingBu.ToString(), "the operator needs to know which BU is already in use");
        problem.RootElement.GetProperty("speContainerId").GetString()
            .Should().Be("b!existing-container");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The guard that was missing — added 2026-08-24 after the review found that this
    // project INTRODUCED the fifth instance of the stale-column class here.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every column the provisioning read projects must exist on <c>sprk_project</c>.
    ///
    /// <para><b>Why this test exists.</b> Commit <c>95d3f0f68</c> — the commit that added the idempotency
    /// guard these very tests cover — put <c>_sprk_securitybuid_value,sprk_specontainerid</c> into the
    /// Step 1 projection. Neither column exists on the table (live metadata: the lookup is
    /// <c>sprk_securitybu</c>, the text column is <c>sprk_containerid</c>). Dataverse answers a bad
    /// projection with 400, the endpoint's <c>catch</c> turned that into a 500 with the cause hidden, and
    /// provisioning created nothing — while the guard built on those same two columns read null forever
    /// and could never fire.</para>
    ///
    /// <para>All five tests above stayed green throughout, because the fixture returned canned rows
    /// regardless of the projection. Task 016 had already built the fix for that failure mode —
    /// a fake that rejects unknown columns the way Dataverse does — and it was not carried across to this
    /// fixture. It is now, and this test pins the projection directly so the constant cannot drift either.</para>
    /// </summary>
    [Fact]
    public void ProjectProvisioningSelect_NamesOnlyColumnsThatExistOnTheTable()
    {
        var columns = ProvisionProjectEndpoint.ProjectProvisioningSelect
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        columns.Should().OnlyContain(c => ProvisionProjectTestFixture.LiveProjectColumns.Contains(c),
            "a $select naming a nonexistent column returns 400, which surfaces as a failed provision " +
            "rather than as a schema mistake — this is the fifth instance of that class in this codebase");
    }

    /// <summary>
    /// The two specific names that broke it, pinned by name so a revert is impossible to do quietly.
    /// </summary>
    [Fact]
    public void ProjectProvisioningSelect_UsesTheLiveSecurityBuAndContainerColumns()
    {
        var select = ProvisionProjectEndpoint.ProjectProvisioningSelect;

        select.Should().Contain("_sprk_securitybu_value").And.Contain("sprk_containerid");
        select.Should().NotContain("_sprk_securitybuid_value",
            "sprk_securitybuid does not exist on sprk_project");
        select.Should().NotContain("sprk_specontainerid",
            "sprk_specontainerid exists on sprk_container, not sprk_project — that is where the name was borrowed from");
    }
}
