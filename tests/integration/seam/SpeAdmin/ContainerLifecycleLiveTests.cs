using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.SpeAdmin;

/// <summary>
/// Task 041 (sdap-SPE-admin-app-r2, spec FR-D02 / NFR-07) — the LiveIntegration suite covering the
/// operations WireMock cannot meaningfully fake: consent, registration, permission, and role (owner)
/// flows against Spaarke Dev, plus the destructive container lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Skip-via-return.</b> Every test starts with an <c>if (!_fixture.IsLive) return;</c> — the
/// established repo convention (see <see cref="LiveIntegrationFixture"/>'s remarks for the precedent
/// and why <c>[Trait("Category","LiveIntegration")]</c> alone would not stop this suite from trying to
/// reach Spaarke Dev in the default CI run). The one exception is
/// <see cref="Guard_RefusesDestructiveOperation_WhenIdDoesNotMatchTheFixtureContainer"/>, which is
/// pure logic over two literal strings — it makes no Graph call, needs no credential, and cannot fail
/// due to a missing tenant, so gating it behind <c>IsLive</c> would only remove regression coverage
/// for the one guard the HARD STOP conditions required to be proven before any destructive Graph call
/// was wired to it.
/// </para>
/// <para>
/// <b>What POML step 4 got right and wrong, part 1 — auth.</b> The task background says all four
/// flows (consent, registration, permission, role) need "the delegated path from task 011". That held
/// for exactly one of them. Container-type application-permission grants (registration + permission —
/// Graph's <c>containerTypeRegistrations/{id}/applicationPermissionGrants</c>, a DIFFERENT resource
/// from container types themselves) run app-only — proven live in this task, see
/// <see cref="ContainerTypePermissionGrant_ReadPath_ShowsTheOwningAppsRealGrant"/>. Only container-type
/// OWNER grants ("role" — Graph's <c>fileStorageContainerType.permissions</c>, confusingly also
/// reachable at a <c>/permissions</c> route) are delegated-and-beta-only with no app-only fallback —
/// task 027 confirmed this live (403 app-only, both API versions), and an automated test runner cannot
/// complete the interactive sign-in delegated OBO requires. See
/// <see cref="ContainerTypeOwnerGrant_RoleFlow_RequiresADelegatedToken_SkippedWithoutOne"/>.
/// </para>
/// <para>
/// <b>What POML step 4 got right and wrong, part 2 — a genuine, previously-unknown production
/// defect this task found.</b> The natural design for "registration" coverage was a self-reverting
/// register→read→remove round trip against <c>ConsumingTenantEndpoints.cs</c>'s
/// <c>RegisterConsumingTenantAsync</c> (<c>POST .../containerTypeRegistrations/{id}/applicationPermissionGrants</c>).
/// Run live against Spaarke Dev, the POST fails on BOTH API versions with <c>400 invalidRequest /
/// apiNotFound</c> — while a GET on the IDENTICAL URL succeeds and returns the real grant for the
/// owning app itself. So the collection is readable but not writable through Graph; nothing in this
/// task's scope explains why (this task builds test infrastructure, it does not chase a fix). The
/// endpoint the SPE Admin app's own "Register" button actually calls
/// (<c>POST /api/spe/containertypes/{typeId}/register</c> → <c>SpeAdminGraphService.RegisterContainerTypeAsync</c>,
/// a SEPARATE SharePoint-REST-based code path, not Graph) was not exercised here — mutating it live
/// has no confirmed, proven-reversible undo, which is exactly the reversibility bar this suite holds
/// every other destructive/mutating call to. This is recorded as a finding for a follow-up task, not
/// silently patched or silently dropped — see <c>notes/task-041-teardown-proof.md</c> §"registration
/// write-path defect".
/// </para>
/// </remarks>
[Trait("Category", "LiveIntegration")]
public sealed class ContainerLifecycleLiveTests : IClassFixture<LiveIntegrationFixture>
{
    private readonly LiveIntegrationFixture _fixture;

    public ContainerLifecycleLiveTests(LiveIntegrationFixture fixture) => _fixture = fixture;

    // ─────────────────────────────────────────────────────────────────────────
    // Guard (Step 3 HARD STOP) — always runs; see the class remarks for why.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guard_RefusesDestructiveOperation_WhenIdDoesNotMatchTheFixtureContainer()
    {
        const string fixtureProvisioned = "11111111-1111-1111-1111-111111111111";
        const string foreign = "22222222-2222-2222-2222-222222222222";

        // Negative control: a foreign id is refused.
        var refuses = () => ThrowawayContainerGuard.EnsureProvisionedByFixture(foreign, fixtureProvisioned);
        refuses.Should().Throw<InvalidOperationException>()
            .WithMessage("*not provisioned by this test run*");

        // Positive control: the fixture's own id is never refused — a guard that also flags the
        // shape it exists to protect would get worked around rather than obeyed (tests/CLAUDE.md's
        // "every rule carries a positive control" authoring rule for structural fitness functions,
        // applied here even though this guard lives outside tests/Spaarke.ArchTests/**).
        var allows = () => ThrowawayContainerGuard.EnsureProvisionedByFixture(fixtureProvisioned, fixtureProvisioned);
        allows.Should().NotThrow();
    }

    [Fact]
    public async Task DestructiveHelper_NeverCallsGraph_WhenTargetIsNotTheFixtureContainer()
    {
        if (!_fixture.IsLive) return;

        // Structural proof, not just the pure-function proof above: the guard sits INSIDE the same
        // helper a real destructive test calls, before the Graph request is issued, so a caller
        // cannot bypass it by mistake at a real call site.
        var foreignId = Guid.NewGuid().ToString();
        var act = () => SoftDeleteThroughGuard(foreignId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private Task<bool> SoftDeleteThroughGuard(string containerId)
    {
        // ContainerId! — every caller of this helper is behind an `if (!_fixture.IsLive) return;`
        // guard, and IsLive true is exactly the condition under which InitializeAsync populated it.
        ThrowawayContainerGuard.EnsureProvisionedByFixture(containerId, _fixture.ContainerId!);
        return _fixture.GraphService.SoftDeleteContainerAsync(_fixture.GraphClient, containerId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Destructive lifecycle — throwaway container ONLY (NFR-07).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThrowawayContainer_DeleteRestorePermanentDelete_SucceedsAndLeavesPreExistingContainersUntouched()
    {
        if (!_fixture.IsLive) return;

        // ContainerId! — safe: IsLive true means InitializeAsync already populated it.
        var containerId = _fixture.ContainerId!;
        ThrowawayContainerGuard.EnsureProvisionedByFixture(containerId, _fixture.ContainerId!);

        // 1. The fixture's container is active and listed.
        var active = await _fixture.GraphService.ListContainersAsync(_fixture.GraphClient, _fixture.ContainerTypeId);
        active.Select(c => c.Id).Should().Contain(containerId, "the fixture just created it");

        // 2. Soft-delete moves it to the recycle bin.
        (await _fixture.GraphService.SoftDeleteContainerAsync(_fixture.GraphClient, containerId))
            .Should().BeTrue();
        var deleted = await _fixture.GraphService.ListDeletedContainersAsync(_fixture.GraphClient, _fixture.ContainerTypeId);
        deleted.Select(c => c.Id).Should().Contain(containerId);

        // 3. Restore returns it to the active listing.
        (await _fixture.GraphService.RestoreContainerAsync(_fixture.GraphClient, containerId))
            .Should().BeTrue();
        var activeAgain = await _fixture.GraphService.ListContainersAsync(_fixture.GraphClient, _fixture.ContainerTypeId);
        activeAgain.Select(c => c.Id).Should().Contain(containerId);

        // 4. Soft-delete again, then a permanent (recycle-bin purge) delete.
        (await _fixture.GraphService.SoftDeleteContainerAsync(_fixture.GraphClient, containerId))
            .Should().BeTrue();
        (await _fixture.GraphService.PermanentDeleteContainerAsync(_fixture.GraphClient, containerId))
            .Should().BeTrue();
        var afterPurge = await _fixture.GraphService.ListDeletedContainersAsync(_fixture.GraphClient, _fixture.ContainerTypeId);
        afterPurge.Select(c => c.Id).Should().NotContain(containerId);

        // 5. NFR-07, proven rather than assumed: every pre-existing container is exactly where it
        // was before this test touched anything — the only container this suite may create or
        // destroy is its own throwaway one.
        var finalActive = await _fixture.GraphService.ListContainersAsync(_fixture.GraphClient, _fixture.ContainerTypeId);
        finalActive.Select(c => c.Id).Should().BeEquivalentTo(_fixture.PreExistingContainerIds,
            "a destructive live run must never change which pre-existing containers exist");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Registration + permission (read path) — read-only against the SHARED container type
    // (NFR-07: "read-only and additive operations may use the existing ones").
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContainerTypePermissionGrant_ReadPath_ShowsTheOwningAppsRealGrant()
    {
        if (!_fixture.IsLive) return;

        // "registration" + "permission": the owning app (170c98e1, SDAP-PCF-CLIENT) is itself a
        // registered consuming app on its own container type — Graph returns that grant unprompted,
        // so this is a real, already-true fact about the tenant, not a value this test manufactures.
        // Read-only: safe against the shared container type, no throwaway container needed.
        var permissions = await _fixture.GraphService.GetContainerTypePermissionsAsync(
            _fixture.GraphClientV1, _fixture.ContainerTypeId);

        permissions.Should().NotBeNull();
        permissions!.Should().Contain(p => p.AppId == _fixture.OwningAppClientId,
            "the owning app is always a registered consumer of its own container type");

        // ListConsumingTenantsAsync is the sibling read used by ConsumingTenantEndpoints.ListConsumersAsync
        // (the "consumers" screen) — same underlying data, different DTO shape.
        var consumers = await _fixture.GraphService.ListConsumingTenantsAsync(
            _fixture.GraphClientV1, _fixture.ContainerTypeId);
        consumers.Should().NotBeNull();
        consumers!.Should().Contain(c => c.AppId == _fixture.OwningAppClientId);
    }

    [Fact]
    public async Task ConsumingAppRegistration_WritePath_ReturnsApiNotFound_ADocumentedGraphDefect()
    {
        if (!_fixture.IsLive) return;

        // Characterization test, not a "this works" claim — see the class remarks. Confirmed live on
        // BOTH API versions: POSTing to the exact URL that GETs successfully above returns
        // 400 invalidRequest / apiNotFound. A fake, never-real app id is used so a status-code
        // surprise (if Graph's behavior ever changes) cannot register anything real — if this ever
        // starts returning 2xx, the finally block removes what it created and the assertions below
        // fail loudly, which is the correct outcome for a defect that got fixed out from under this test.
        const string testAppId = "99999999-1111-2222-3333-444444444444";
        var permissions = new[] { "readContent" };

        int? failureStatusCode = null;
        var registered = false;
        try
        {
            var result = await _fixture.GraphService.RegisterConsumingTenantAsync(
                _fixture.GraphClientV1, _fixture.ContainerTypeId, testAppId,
                tenantId: null, permissions, permissions);
            registered = result is not null;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            // RegisterConsumingTenantAsync only wraps the 404 case into SpaarkeStorageException
            // (ADR-007's "no Graph SDK types above the facade" boundary) — every other status,
            // including this one, propagates as the raw Graph SDK exception. That asymmetry is a
            // second, smaller finding recorded alongside the main one in task notes; catching the
            // SDK type here (in a test, not production code) is how this test observes the real
            // status rather than a rethrow this method never performs.
            failureStatusCode = ex.ResponseStatusCode;
        }
        finally
        {
            if (registered)
            {
                await _fixture.GraphService.RemoveConsumingTenantAsync(
                    _fixture.GraphClientV1, _fixture.ContainerTypeId, testAppId);
            }
        }

        registered.Should().BeFalse(
            "if this ever succeeds, notes/task-041-teardown-proof.md's defect record is stale — update it, don't just let this test flip green silently");
        failureStatusCode.Should().Be(400, "the write path is expected to fail with 400 invalidRequest/apiNotFound, not silently no-op");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Role (container-type OWNER grant) — delegated + beta only, no app-only fallback (task 027).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContainerTypeOwnerGrant_RoleFlow_RequiresADelegatedToken_SkippedWithoutOne()
    {
        if (!_fixture.IsLive || !_fixture.HasDelegatedToken)
        {
            // Container-type OWNER grants are delegated-and-beta-only — confirmed live 2026-08-25 by
            // task 027 (notes/task-027-findings.md §2: 403 app-only on both API versions; delegated +
            // beta is the only combination Graph will serve). Delegated means an interactive (or
            // device-code) sign-in a human completes in a browser, which this automated test runner
            // cannot do for itself. An operator can run notes/delegated-diagnostics.py's device-code
            // flow once and export the resulting access token as SPE_LIVE_DELEGATED_TOKEN before
            // `dotnet test --filter Category=LiveIntegration` to exercise this test; absent that
            // token (true for the default CI baseline and for this task's own execution — see the
            // task notes), this is a documented, deliberate no-op — not a silently-omitted flow.
            return;
        }

        var delegatedClient = _fixture.BuildDelegatedGraphClient();

        var me = await delegatedClient.Me.GetAsync(cfg => cfg.QueryParameters.Select = new[] { "userPrincipalName" });
        var upn = me?.UserPrincipalName;
        upn.Should().NotBeNullOrWhiteSpace("the delegated token must resolve to a real signed-in user");

        // Defensive cleanup: remove any leftover self-grant from a prior interrupted run.
        var existingOwners = await _fixture.GraphService.ListContainerTypeOwnersAsync(delegatedClient, _fixture.ContainerTypeId);
        var leftover = existingOwners?.FirstOrDefault(o => string.Equals(o.Email, upn, StringComparison.OrdinalIgnoreCase));
        if (leftover is not null)
        {
            await _fixture.GraphService.RemoveContainerTypeOwnerAsync(delegatedClient, _fixture.ContainerTypeId, leftover.PermissionId);
        }

        string? addedPermissionId = null;
        try
        {
            var owner = await _fixture.GraphService.AddContainerTypeOwnerAsync(delegatedClient, _fixture.ContainerTypeId, upn!);
            owner.Should().NotBeNull();
            addedPermissionId = owner!.PermissionId;

            var owners = await _fixture.GraphService.ListContainerTypeOwnersAsync(delegatedClient, _fixture.ContainerTypeId);
            owners.Should().Contain(o => o.PermissionId == addedPermissionId);
        }
        finally
        {
            if (addedPermissionId is not null)
            {
                var removed = await _fixture.GraphService.RemoveContainerTypeOwnerAsync(
                    delegatedClient, _fixture.ContainerTypeId, addedPermissionId);
                removed.Should().BeTrue(
                    "self-reverting — this suite must never leave a test-added owner grant on a shared container type");
            }
        }
    }
}
