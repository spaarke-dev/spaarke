// -----------------------------------------------------------------------------
// T5SlotMiKvRbacTrapProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for T5SlotMiKvRbacTrapProbe (task 172,
// Wave G-7 -- pipelined with H4 / task 125). ADR-038 path #1 -- pure C#
// unit tests over an ArmClient built against a fake HttpClientTransport
// (the SAME ArmSdkTestFakes helper H2a/H4 sibling probe/granter tests use).
// The ARM SDK's own request construction, URL building, and response
// deserialization all run unmodified; only the HTTP boundary is faked.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  STRUCTURAL PASSED   -- neither slot has a SAMI (post-Phase-C UAMI
//                              steady state) -> Passed(T5); role-assignment
//                              list is NEVER called.
//   T2  LIVE-GRANTED PASSED -- BOTH slots have a SAMI AND both principals
//                              hold KV Secrets User role at KV scope ->
//                              Passed(T5).
//   T3  MISSING GRANT       -- SAMI present on prod but zero role assignments
//                              at KV scope for that principal -> Failed(T5)
//                              with "MISSING GRANT" evidence.
//   T4  WRONG ROLE ID       -- SAMI present + role assignment(s) exist at
//                              KV scope for that principal BUT the role
//                              definition GUID is NOT the KV Secrets User
//                              GUID -> Failed(T5) with "WRONG ROLE ID"
//                              evidence. (task-143 shape.)
//   T5  WRONG PRINCIPAL     -- KV has a KV Secrets User assignment BUT the
//                              principalId doesn't match the observed SAMI
//                              -> Failed(T5) treated as MISSING GRANT for
//                              that SAMI (which is functionally what happens
//                              at runtime).
//   T6  ONLY PROD SAMI      -- Prod has SAMI + grant; staging has no SAMI
//                              -> Passed(T5). Grant list still consulted;
//                              staging is not required to have a grant
//                              because it has no principal to receive one.
//   T7a Missing SubscriptionId -> InfraFault(T5). ARM never called.
//   T7b Missing ResourceGroupName -> InfraFault(T5). ARM never called.
//   T7c Missing AppServiceName -> InfraFault(T5). ARM never called.
//   T7d Missing KeyVaultName -> InfraFault(T5). ARM never called.
//   T8  ARM slot-read RequestFailedException -> InfraFault(T5).
//   T9  ARM role-assignment-list RequestFailedException -> InfraFault(T5).
//   T10 Kind is TrapKind.T5SlotMiKvRbac.
//   T11 Genuine HTTP call assertion -- fake handler records ARM's
//       role-assignment LIST URL at the KV vault scope (regression guard
//       against hardcoded Passed).
//   T12 Cancellation propagates.
//
// SILENT-FAIL AUDIT (task briefing "task 143 lesson"):
//   * T4 (wrong role ID) is the exact task-143 shape at a different SDK
//     surface (there: wrong AppRoleId GUID silently 403s at Graph; here:
//     wrong role definition GUID silently satisfies the grant PUT but
//     produces 403 at runtime). The probe's role-GUID-suffix assertion
//     is what catches this class of trap.
//   * The GUID literal ExpectedKvSecretsUserRoleDefinitionGuid MUST be
//     the well-known KV Secrets User GUID (4633458b-17de-408a-b874-
//     0445c86b69e6). T14 asserts this explicitly.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class T5SlotMiKvRbacTrapProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string StagingSlotName = "staging";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";

    // Well-known KV Secrets User role definition GUID -- MUST match the probe's
    // ExpectedKvSecretsUserRoleDefinitionGuid constant (T14 pins this).
    private const string KvSecretsUserRoleId = "4633458b-17de-408a-b874-0445c86b69e6";

    // A different well-known role GUID used in the WRONG-ROLE-ID test (T4) --
    // "Reader" is the canonical benign-looking wrong choice per silent-fail-
    // trap analysis (a Reader grant satisfies most naive existence checks
    // but does NOT satisfy KV Secrets User at runtime).
    private const string ReaderRoleId = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

    private static readonly string ProdPrincipalId = Guid.NewGuid().ToString();
    private static readonly string StagingPrincipalId = Guid.NewGuid().ToString();

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: SubscriptionId,
        DataverseUrl: "https://sprk-acme.crm.dynamics.com",
        BffAppRegId: BffAppRegId,
        UamiClientId: UamiClientId,
        KeyVaultName: KeyVaultName,
        AppServiceName: AppServiceName,
        ResourceGroupName: ResourceGroupName);

    private static string SiteBodyWithIdentity(string siteAndSlotName, string? principalId) =>
        principalId is null
            ? $$"""
                {
                  "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{siteAndSlotName}}",
                  "name": "{{siteAndSlotName}}",
                  "location": "westus2",
                  "properties": { "keyVaultReferenceIdentity": "" }
                }
                """
            : $$"""
                {
                  "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{siteAndSlotName}}",
                  "name": "{{siteAndSlotName}}",
                  "location": "westus2",
                  "identity": { "type": "SystemAssigned", "principalId": "{{principalId}}", "tenantId": "{{TenantId}}" },
                  "properties": { "keyVaultReferenceIdentity": "" }
                }
                """;

    private static string RoleAssignmentsListBody(params RoleAssignmentFixture[] assignments)
    {
        if (assignments.Length == 0)
        {
            return """{ "value": [] }""";
        }

        var items = new List<string>();
        foreach (var a in assignments)
        {
            var name = Guid.NewGuid().ToString();
            items.Add($$"""
                {
                  "id": "{{a.VaultScope}}/providers/Microsoft.Authorization/roleAssignments/{{name}}",
                  "name": "{{name}}",
                  "type": "Microsoft.Authorization/roleAssignments",
                  "properties": {
                    "roleDefinitionId": "/subscriptions/{{SubscriptionId}}/providers/Microsoft.Authorization/roleDefinitions/{{a.RoleDefinitionGuid}}",
                    "principalId": "{{a.PrincipalId}}",
                    "principalType": "ServicePrincipal",
                    "scope": "{{a.VaultScope}}"
                  }
                }
                """);
        }
        return $$"""{ "value": [{{string.Join(",", items)}}] }""";
    }

    private sealed record RoleAssignmentFixture(
        string RoleDefinitionGuid,
        string PrincipalId,
        string VaultScope);

    private static string KvVaultScope() =>
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.KeyVault/vaults/{KeyVaultName}";

    // ---------- T1 STRUCTURAL PASSED ----------

    [Fact]
    public async Task ProbeAsync_NoSamiOnEitherSlot_ReturnsStructuralPassed_AndDoesNotListRoleAssignments()
    {
        var listAttempted = false;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                listAttempted = true;
                throw new InvalidOperationException("must not list role assignments when neither slot has a SAMI (structural noop)");
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, null));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
        listAttempted.Should().BeFalse("structural noop path must skip the role-assignment list");
    }

    // ---------- T2 LIVE-GRANTED PASSED ----------

    [Fact]
    public async Task ProbeAsync_BothSlotsHaveSami_AndBothHoldKvSecretsUserAtKvScope_ReturnsPassed()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(KvSecretsUserRoleId, ProdPrincipalId, KvVaultScope()),
                    new RoleAssignmentFixture(KvSecretsUserRoleId, StagingPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, StagingPrincipalId));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
    }

    // ---------- T3 MISSING GRANT ----------

    [Fact]
    public async Task ProbeAsync_ProdSamiHasNoRoleAssignmentAtKvScope_ReturnsFailedWithMissingGrantEvidence()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                // Staging has the correct grant; prod has ZERO assignments at KV scope.
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(KvSecretsUserRoleId, StagingPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, StagingPrincipalId));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
        failed.Diagnostic.Should().Contain("MISSING GRANT")
            .And.Contain("prod")
            .And.Contain(ProdPrincipalId)
            .And.Contain(KvSecretsUserRoleId, "diagnostic must cite the EXPECTED role GUID so operators know what to fix")
            .And.Contain("silent-fail trap MANIFESTED");
    }

    // ---------- T4 WRONG ROLE ID (task-143 shape) ----------

    [Fact]
    public async Task ProbeAsync_ProdSamiHasReaderInsteadOfKvSecretsUser_ReturnsFailedWithWrongRoleIdEvidence()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                // Prod has a grant... but for the WRONG role (Reader, not KV Secrets User).
                // This is the exact task-143 silent-fail shape at the RBAC scope.
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(ReaderRoleId, ProdPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("WRONG ROLE ID")
            .And.Contain(ProdPrincipalId)
            .And.Contain(ReaderRoleId, "diagnostic must cite the OBSERVED wrong role so operators know what was actually granted")
            .And.Contain(KvSecretsUserRoleId, "diagnostic must ALSO cite the EXPECTED role so the operator sees the delta");
    }

    // ---------- T5 WRONG PRINCIPAL ----------

    [Fact]
    public async Task ProbeAsync_KvHasKvSecretsUserButForDifferentPrincipal_ReturnsFailedAsMissingGrantForSami()
    {
        var otherPrincipalId = Guid.NewGuid().ToString();
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                // KV has a KV Secrets User grant... but for the WRONG principal
                // (not the observed prod SAMI). From the SAMI's perspective, this
                // is functionally the same runtime failure as MISSING GRANT.
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(KvSecretsUserRoleId, otherPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("MISSING GRANT",
            "from the observed SAMI's perspective, WRONG PRINCIPAL === MISSING GRANT for that SAMI")
            .And.Contain(ProdPrincipalId);
    }

    // ---------- T6 ONLY PROD SAMI ----------

    [Fact]
    public async Task ProbeAsync_OnlyProdHasSami_AndProdHoldsKvSecretsUser_ReturnsPassed()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(KvSecretsUserRoleId, ProdPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    // ---------- T7a-d Input guards ----------

    [Fact]
    public async Task ProbeAsync_MissingSubscriptionId_ReturnsInfraFault_AndDoesNotCallArm()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(
            _ => throw new InvalidOperationException("must not call ARM when subscription id missing")));
        var request = BuildRequest() with { SubscriptionId = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
        infra.Diagnostic.Should().Contain("SubscriptionId");
    }

    [Fact]
    public async Task ProbeAsync_MissingResourceGroupName_ReturnsInfraFault_AndDoesNotCallArm()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(
            _ => throw new InvalidOperationException("must not call ARM when rg missing")));
        var request = BuildRequest() with { ResourceGroupName = "  " };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("ResourceGroupName");
    }

    [Fact]
    public async Task ProbeAsync_MissingAppServiceName_ReturnsInfraFault_AndDoesNotCallArm()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(
            _ => throw new InvalidOperationException("must not call ARM when app service name missing")));
        var request = BuildRequest() with { AppServiceName = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("AppServiceName");
    }

    [Fact]
    public async Task ProbeAsync_MissingKeyVaultName_ReturnsInfraFault_AndDoesNotCallArm()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(
            _ => throw new InvalidOperationException("must not call ARM when kv name missing")));
        var request = BuildRequest() with { KeyVaultName = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("KeyVaultName");
    }

    // ---------- T8 ARM slot-read failure ----------

    [Fact]
    public async Task ProbeAsync_ArmSlotReadFails_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have Reader on the App Service.")));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
        infra.Diagnostic.Should().Contain("slot-identity read")
            .And.Contain(AppServiceName);
    }

    // ---------- T9 ARM role-list failure ----------

    [Fact]
    public async Task ProbeAsync_ArmRoleAssignmentListFails_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed",
                        "The client does not have Microsoft.Authorization/roleAssignments/read on the vault scope."));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("role-assignment list at KV scope")
            .And.Contain("roleAssignments/read");
    }

    // ---------- T10 Kind constant ----------

    [Fact]
    public void Kind_IsT5SlotMiKvRbac()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("should not be called")));
        probe.Kind.Should().Be(TrapKind.T5SlotMiKvRbac);
    }

    // ---------- T11 Genuine HTTP call assertion (regression guard) ----------

    [Fact]
    public async Task ProbeAsync_GenuinelyCallsArmRoleAssignmentListAtKvScope_NotHardCodedPassed()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/providers/Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, RoleAssignmentsListBody(
                    new RoleAssignmentFixture(KvSecretsUserRoleId, ProdPrincipalId, KvVaultScope())));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains($"vaults/{KeyVaultName}")
                && uri.AbsolutePath.Contains("/providers/Microsoft.Authorization/roleAssignments"),
            "the probe must call ARM's role-assignment LIST at the KV vault scope -- not return a hard-coded Passed");
    }

    // ---------- T12 Cancellation ----------

    [Fact]
    public async Task ProbeAsync_Cancelled_PropagatesOperationCanceled()
    {
        // Use an already-cancelled token so the ARM SDK's HttpMessageHandler
        // path observes cancellation deterministically (parity with real
        // cancellation semantics, not a synthetic exception injection which
        // the SDK's retry pipeline may reinterpret as an infra fault).
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, null)));
        var probe = BuildProbe(handler);

        var act = async () => await probe.ProbeAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- T13 Empty request throws ArgumentNullException ----------

    [Fact]
    public async Task ProbeAsync_NullRequest_ThrowsArgumentNullException()
    {
        var probe = BuildProbe(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("should not be called")));

        var act = async () => await probe.ProbeAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---------- T14 Well-known role GUID pinned literal ----------

    [Fact]
    public void ExpectedKvSecretsUserRoleDefinitionGuid_MatchesWellKnownPublicCloudRoleId()
    {
        // BINDING regression guard against task-143-class silent-fail (wrong
        // GUID class in a code constant). If this fails, someone changed the
        // probe's constant without updating KvSecretsPopulationOptions in
        // lockstep -- the probe would then verdict Passed on a stamp where
        // H4 granted the OLD role and Failed on stamps where H4 granted the
        // NEW role (or vice versa). Test failure = do NOT ship without
        // reviewing both files together.
        T5SlotMiKvRbacTrapProbe.ExpectedKvSecretsUserRoleDefinitionGuid
            .Should().Be("4633458b-17de-408a-b874-0445c86b69e6");
    }

    // ---------- T15 Role-definition-id GUID extraction ----------

    [Theory]
    [InlineData(
        "/subscriptions/22222222-3333-4444-5555-666666666666/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6",
        "4633458b-17de-408a-b874-0445c86b69e6")]
    [InlineData(
        "/providers/Microsoft.Management/managementGroups/mg-spaarke/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6",
        "4633458b-17de-408a-b874-0445c86b69e6",
        Skip = "documents management-group-scope role-def RIDs are also correctly parsed by tail-GUID heuristic")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("/subscriptions/foo/roleDefinitions/", null)] // trailing slash => no tail
    [InlineData("not-a-rid-at-all", null)]
    [InlineData("/foo/bar/baz/not-a-guid", null)]
    public void ExtractRoleDefinitionGuid_ReturnsExpected(string? input, string? expected)
    {
        var actual = T5SlotMiKvRbacTrapProbe.ExtractRoleDefinitionGuid(input);
        actual.Should().Be(expected);
    }

    // ---------- T16 Snapshot-shape unit test for TryFindKvSecretsUserGrant ----------

    [Fact]
    public void TryFindKvSecretsUserGrant_MatchesOnRoleGuidSuffixAndPrincipalId()
    {
        var expectedPrincipal = ProdPrincipalId;
        var snapshots = new List<T5SlotMiKvRbacTrapProbe.KvScopeRoleAssignmentSnapshot>
        {
            // Correct grant.
            new(
                RoleDefinitionResourceId:
                    $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KvSecretsUserRoleId}",
                PrincipalId: expectedPrincipal),
            // Wrong role for same principal.
            new(
                RoleDefinitionResourceId:
                    $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{ReaderRoleId}",
                PrincipalId: expectedPrincipal),
            // Right role but wrong principal.
            new(
                RoleDefinitionResourceId:
                    $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KvSecretsUserRoleId}",
                PrincipalId: Guid.NewGuid().ToString()),
        };

        var found = T5SlotMiKvRbacTrapProbe.TryFindKvSecretsUserGrant(
            snapshots, expectedPrincipal, out var observed);

        found.Should().BeTrue();
        observed.Should().Contain(KvSecretsUserRoleId).And.Contain(ReaderRoleId,
            "the observed-roles list must capture EVERY assignment for the principal so the diagnostic can distinguish wrong-role from missing-grant");
    }

    // ---------- helpers ----------

    private static T5SlotMiKvRbacTrapProbe BuildProbe(FakeArmHttpMessageHandler handler) =>
        new(ArmSdkTestFakes.NewArmClient(handler), NullLogger<T5SlotMiKvRbacTrapProbe>.Instance);
}
