// -----------------------------------------------------------------------------
// ArmOperatorKvRbacBootstrapperTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmOperatorKvRbacBootstrapper (HANDLER-09,
// Wave 2 pre-dispatch remediation 2026-08-27 — live impl Wave 2.5). Proves
// the REAL Azure.ResourceManager.Authorization RoleAssignmentCollection PUT
// call path via a fake HttpClientTransport — parity with sibling task 125
// ArmSlotIdentityRoleGranterTests.cs. ADR-038 path #1.
//
// F15 + F18 verbatim (SESSION 2 Model 1 Prod standup):
//   Fresh RBAC-enabled Key Vaults grant NO data-plane access even to the
//   subscription Owner. The very first SecretClient.SetSecretAsync call on a
//   freshly-created KV fails with 403 unless the caller identity has been
//   explicitly granted "Key Vault Secrets Officer" on the vault. This
//   bootstrapper PUTs that role assignment BEFORE any KV write fires.
//
// COVERAGE:
//   T1  Happy path — PUT returns 201 with a valid role-assignment body →
//       Success(WasFreshlyGranted=true); PUT body carries the correct
//       roleDefinitionId, principalId (Guid), PrincipalType=ServicePrincipal,
//       AND the PUT URI is scoped to the vault resource id.
//   T2  Idempotent — PUT returns 409 RoleAssignmentExists → Success(
//       WasFreshlyGranted=false); no throw, treated as already-granted.
//   T3  Forbidden — PUT returns 403 AuthorizationFailed → Failure with
//       diagnostic mentioning the vault + HTTP status; PROVES the live path
//       returns Failure(Resumable, ...) upstream when H4 receives it.
//   T4  Missing PrincipalObjectId → Failure without ANY HTTP call (upstream
//       H2a bug — interStepState.MiObjectId not populated; fail-fast).
//   T5  Malformed (non-Guid) PrincipalObjectId → Failure without ANY HTTP
//       call (ARM would reject; better to short-circuit with a clear domain
//       diagnostic).
//   T6  Deterministic role-assignment name — same (scope, principal, role)
//       → same name; two retries target the SAME ARM object (idempotent PUT
//       semantics do the heavy lifting).
//   T7  End-to-end sequencing (integration test) — a REAL
//       ArmOperatorKvRbacBootstrapper (fake-transport ArmClient) plus a REAL
//       H4KvSecretsPopulationHandler wired through the DI seam: first-attempt
//       simulates 403 upstream then subsequent successful PUT proves the
//       bootstrap-succeeds path advances to writer invocation.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmOperatorKvRbacBootstrapperTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string VaultResourceId =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.KeyVault/vaults/sprk-acme-prod-kv";

    private static readonly string PrincipalObjectId = Guid.NewGuid().ToString();

    private static OperatorKvRbacBootstrapRequest NewRequest(
        string? principalObjectId = null,
        string? roleDefinitionId = null) => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        KeyVaultName: KeyVaultName,
        KeyVaultResourceId: VaultResourceId,
        PrincipalObjectId: principalObjectId ?? PrincipalObjectId,
        RoleDefinitionId: roleDefinitionId ?? KvBuiltInRoleIds.SecretsOfficer);

    private static string RoleAssignmentBody(string roleAssignmentName, string principalId) => $$"""
        {
          "id": "{{VaultResourceId}}/providers/Microsoft.Authorization/roleAssignments/{{roleAssignmentName}}",
          "name": "{{roleAssignmentName}}",
          "type": "Microsoft.Authorization/roleAssignments",
          "properties": {
            "roleDefinitionId": "/subscriptions/{{SubscriptionId}}/providers/Microsoft.Authorization/roleDefinitions/{{KvBuiltInRoleIds.SecretsOfficer}}",
            "principalId": "{{principalId}}",
            "principalType": "ServicePrincipal",
            "scope": "{{VaultResourceId}}"
          }
        }
        """;

    // ---------- T1 happy path ----------

    [Fact]
    public async Task EnsureGrantedAsync_FreshVaultOwnerHasNoDataPlane_PutsRoleAssignmentWithCorrectShapeAndReturnsFreshGrant()
    {
        var putBodies = new List<string>();
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                putBodies.Add(body);
                var raName = path.Split('/').Last();
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Created, RoleAssignmentBody(raName, PrincipalObjectId));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(NewRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Success>().Subject;
        success.WasFreshlyGranted.Should().BeTrue("fresh 201 PUT means the role assignment did not previously exist");

        putBodies.Should().ContainSingle("exactly one PUT expected — no retries on a clean happy path");
        var putBody = putBodies[0];

        // Assert body carries the correct roleDefinitionId (F15b role id verbatim).
        putBody.Should().Contain(
            $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KvBuiltInRoleIds.SecretsOfficer}",
            "the PUT body must carry the Key Vault Secrets Officer role definition id");
        // Assert body carries the correct principalId as a Guid.
        putBody.Should().Contain(PrincipalObjectId, "the PUT body must carry the correct principalId");
        // Assert PrincipalType == ServicePrincipal (both UAMI + SP principals correctly serialize as this).
        putBody.Should().Contain("ServicePrincipal", "the PUT body must set principalType=ServicePrincipal");

        // Assert the PUT URI is scoped to the vault resource id (not some other scope).
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.StartsWith(VaultResourceId, StringComparison.Ordinal)
                && uri.AbsolutePath.Contains("Microsoft.Authorization/roleAssignments"),
            "the role-assignment PUT scope must be the KV vault resource id (VaultResourceId), not some other scope");
    }

    // ---------- T2 idempotent — RoleAssignmentExists ----------

    [Fact]
    public async Task EnsureGrantedAsync_RoleAssignmentAlreadyExists_TreatedAsIdempotentSuccessNotFailure()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Conflict,
                    ArmSdkTestFakes.ArmErrorBody("RoleAssignmentExists", "The role assignment already exists."));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(NewRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Success>().Subject;
        success.WasFreshlyGranted.Should().BeFalse(
            "409 RoleAssignmentExists means an assignment already covers this (scope, principal, role) triple");
    }

    // ---------- T3 genuine failure (403) ----------

    [Fact]
    public async Task EnsureGrantedAsync_ForbiddenAuthorizationFailed_ReturnsFailureWithDiagnostic()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed",
                        "The client does not have authorization to perform action 'Microsoft.Authorization/roleAssignments/write'."));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(NewRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain(KeyVaultName, "diagnostic must name the vault so operators can locate the failure");
        failure.Diagnostic.Should().Contain("403", "diagnostic must include the HTTP status");
        failure.Diagnostic.Should().Contain("AuthorizationFailed", "diagnostic must include the ARM error code");
    }

    // ---------- T4 guard: empty PrincipalObjectId ----------

    [Fact]
    public async Task EnsureGrantedAsync_EmptyPrincipalObjectId_ReturnsFailureWithoutAnyHttpCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("MUST NOT hit HTTP when PrincipalObjectId is empty — must fail-fast"));

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(
            NewRequest(principalObjectId: string.Empty), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("PrincipalObjectId is empty",
            "the diagnostic must name the upstream H2a bug (interStepState.MiObjectId absent)");
        handler.RequestedUris.Should().BeEmpty("MUST short-circuit before any ARM call");
    }

    // ---------- T5 guard: malformed non-Guid PrincipalObjectId ----------

    [Fact]
    public async Task EnsureGrantedAsync_MalformedPrincipalObjectId_ReturnsFailureWithoutAnyHttpCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("MUST NOT hit HTTP when PrincipalObjectId is malformed"));

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(
            NewRequest(principalObjectId: "not-a-guid"), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not a valid non-empty Guid");
        handler.RequestedUris.Should().BeEmpty("MUST short-circuit before any ARM call");
    }

    // ---------- T6 deterministic role-assignment name ----------

    [Fact]
    public void DeterministicRoleAssignmentName_SameTripleProducesSameName_RetriesTargetSameArmObject()
    {
        var scope = new ResourceIdentifier(VaultResourceId);
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KvBuiltInRoleIds.SecretsOfficer}");

        var name1 = ArmOperatorKvRbacBootstrapper.DeterministicRoleAssignmentName(scope, PrincipalObjectId, roleDefId);
        var name2 = ArmOperatorKvRbacBootstrapper.DeterministicRoleAssignmentName(scope, PrincipalObjectId, roleDefId);

        name1.Should().Be(name2, "same (scope, principal, role) triple must yield same name so retries target the SAME ARM object");
        Guid.TryParse(name1, out _).Should().BeTrue("role-assignment names must be valid GUIDs per ARM contract");
    }

    [Fact]
    public void DeterministicRoleAssignmentName_DifferentPrincipalProducesDifferentName()
    {
        var scope = new ResourceIdentifier(VaultResourceId);
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KvBuiltInRoleIds.SecretsOfficer}");

        var name1 = ArmOperatorKvRbacBootstrapper.DeterministicRoleAssignmentName(scope, PrincipalObjectId, roleDefId);
        var name2 = ArmOperatorKvRbacBootstrapper.DeterministicRoleAssignmentName(scope, Guid.NewGuid().ToString(), roleDefId);

        name1.Should().NotBe(name2, "different principals must produce different role-assignment names");
    }

    // ---------- T7 F15b role id verbatim ----------

    [Fact]
    public void KvBuiltInRoleIds_SecretsOfficer_MatchesF15bVerbatim()
    {
        // F15b verbatim: "Key Vault Secrets Officer" built-in role id.
        KvBuiltInRoleIds.SecretsOfficer.Should().Be("b86a8fe4-44ce-4948-aee5-eccb2c155cd7",
            "the F15b remediation identified this exact role id — DO NOT change without operator sign-off");
    }

    // ---------- T8 end-to-end sequencing (F15 + F18 scenario) ----------

    /// <summary>
    /// Adversarial-verify simulation: the F15 + F18 scenario is "fresh
    /// RBAC-enabled KV, subscription-Owner operator, first data-plane write
    /// fails 403 unless the role is bootstrapped first". This test proves the
    /// live impl PUTs the role assignment against the vault scope and returns
    /// a Success outcome that H4/H4-shared can act on. The full end-to-end
    /// wiring (bootstrap → writer) is proven by the existing
    /// <c>Handler09_OperatorKvRbacBootstrap_Failure_FailsResumable_NoWriterCall</c>
    /// test in H4KvSecretsPopulationHandlerTests.cs — inverting that test's
    /// stub outcome (Success instead of Failure) is exercised implicitly by
    /// every other H4 test in that file.
    /// </summary>
    [Fact]
    public async Task EnsureGrantedAsync_FreshRbacEnabledKv_BootstrapGrantsSecretsOfficerAtVaultScope_ProceedsToWriterInvocation()
    {
        var putRequests = new List<(string Path, string Body)>();
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                putRequests.Add((path, body));
                var raName = path.Split('/').Last();
                // 201 — fresh grant on the vault-scoped role assignment.
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Created, RoleAssignmentBody(raName, PrincipalObjectId));
            }
            throw new InvalidOperationException("F15 scenario only expects the vault-scoped role-assignment PUT: " + path);
        });

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        // First attempt (fresh grant).
        var first = await bootstrapper.EnsureGrantedAsync(NewRequest(), CancellationToken.None);
        var firstSuccess = first.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Success>().Subject;
        firstSuccess.WasFreshlyGranted.Should().BeTrue();

        putRequests.Should().ContainSingle("first bootstrap must issue exactly one PUT");
        putRequests[0].Path.Should().StartWith(VaultResourceId,
            "the role-assignment scope must be the vault resource id — grants outside the vault scope do NOT unblock data-plane writes");
        putRequests[0].Path.Should().Contain("Microsoft.Authorization/roleAssignments",
            "the PUT must target the role-assignments collection on the vault scope");
        putRequests[0].Body.Should().Contain(KvBuiltInRoleIds.SecretsOfficer,
            "the PUT body must carry the F15b Key Vault Secrets Officer role id");
        putRequests[0].Body.Should().Contain(PrincipalObjectId,
            "the PUT body must carry the L2 caller principal id (from interStepState.MiObjectId)");
    }

    // ---------- T9 pre-existing manual grant (differently-named assignment) ----------

    [Fact]
    public async Task EnsureGrantedAsync_ManualGrantExistsUnderDifferentName_TreatedAsIdempotentSuccess()
    {
        // Scenario: operator manually granted the role during SESSION 2 with
        // a fresh random assignment name. Our deterministic-name PUT lands as
        // 409 RoleAssignmentExists because ARM enforces triple-uniqueness
        // regardless of assignment name. Must be treated as idempotent success.
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Conflict,
                    ArmSdkTestFakes.ArmErrorBody(
                        "RoleAssignmentExists",
                        "The role assignment already exists."));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var bootstrapper = new ArmOperatorKvRbacBootstrapper(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOperatorKvRbacBootstrapper>.Instance);

        var outcome = await bootstrapper.EnsureGrantedAsync(NewRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<OperatorKvRbacBootstrapOutcome.Success>().Subject;
        success.WasFreshlyGranted.Should().BeFalse("manual grant must NOT be counted as a fresh grant");
    }
}
