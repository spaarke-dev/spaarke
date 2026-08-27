// -----------------------------------------------------------------------------
// ArmOperatorKvRbacBootstrapper.cs
//
// HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27) — F15 + F18 verbatim.
// LIVE production <see cref="IOperatorKvRbacBootstrapper"/> impl (Wave 2.5:
// replaces the Wave-2 log-and-return-Success scaffold that shipped in commit
// 5a532e548 alongside the seam + rejection codes + handler wiring). Uses
// Azure.ResourceManager.Authorization's
// <see cref="Azure.ResourceManager.Authorization.RoleAssignmentCollection.CreateOrUpdateAsync(Azure.WaitUntil, string, Azure.ResourceManager.Authorization.Models.RoleAssignmentCreateOrUpdateContent, System.Threading.CancellationToken)"/>
// to PUT a role assignment scoped to the target Key Vault so the L2-caller
// principal (typically the L2 UAMI, whose object id H2a writes to
// <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.MiObjectId"/>)
// gains data-plane write access BEFORE the first
// <see cref="Azure.Security.KeyVault.Secrets.SecretClient.SetSecretAsync(Azure.Security.KeyVault.Secrets.KeyVaultSecret, System.Threading.CancellationToken)"/>
// fires.
//
// GROUND-TRUTHED SDK SHAPES (reused verbatim from sibling task 125
// <see cref="ArmSlotIdentityRoleGranter"/> — SAME package pin
// Azure.ResourceManager.Authorization 1.1.4 already resolved in this project's
// csproj alongside Azure.ResourceManager 1.14.0):
//   - ArmClient.GetRoleAssignments(ResourceIdentifier scope) -> RoleAssignmentCollection
//     (Azure.ResourceManager.Authorization.AuthorizationExtensions — an
//     ArmClient extension method, a LOCAL reference — no network call).
//   - RoleAssignmentCollection.CreateOrUpdateAsync(WaitUntil.Completed, name,
//     content, ct) issues PUT
//     {scope}/providers/Microsoft.Authorization/roleAssignments/{name} — a
//     single 201/200 response with a terminal body completes WaitUntil.Completed
//     without extra polling.
//   - RoleAssignmentCreateOrUpdateContent(ResourceIdentifier roleDefinitionId,
//     Guid principalId) — PrincipalId is a Guid (NOT a string).
//   - RoleManagementPrincipalType.ServicePrincipal — extensible-enum static
//     property; correct value for both UAMI principals and app-registration SP
//     principals (the two shapes the L2 caller can present).
//   - A 409 with error code "RoleAssignmentExists" surfaces as
//     RequestFailedException Status=409 ErrorCode="RoleAssignmentExists" —
//     treated as idempotent success (WasFreshlyGranted=false).
//
// IDEMPOTENCY (parity with ArmSlotIdentityRoleGranter): the role-assignment
// NAME is a deterministic hash of (scope, principalId, roleDefinitionId) so a
// retry targets the SAME ARM object. ARM's idempotent PUT semantics do the
// heavy lifting; the "RoleAssignmentExists" catch is retained as defense-in-
// depth for the case where a differently-named assignment already covers the
// SAME triple (e.g. a manual operator grant per SESSION 2's procedure).
//
// DOMAIN OUTCOMES (never throw for expected shapes):
//   - Success(WasFreshlyGranted=true)  — PUT returned 200/201 (fresh grant).
//   - Success(WasFreshlyGranted=false) — 409 RoleAssignmentExists (idempotent).
//   - Failure(diagnostic)              — any other RequestFailedException
//                                        (403/AuthorizationFailed most common,
//                                        also 400/InvalidPrincipalId etc.) OR
//                                        empty/malformed PrincipalObjectId.
//   Only OperationCanceledException propagates (cooperative cancellation).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Authorization.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// LIVE production <see cref="IOperatorKvRbacBootstrapper"/>. Grants the
/// configured KV data-plane role (default: Key Vault Secrets Officer,
/// <see cref="KvBuiltInRoleIds.SecretsOfficer"/>) to the target principal at
/// vault scope via Azure.ResourceManager.Authorization. Idempotent —
/// re-invocation with the same scope+principal+role triple returns
/// <see cref="OperatorKvRbacBootstrapOutcome.Success"/> with
/// <c>WasFreshlyGranted=false</c>. Constructed with an <see cref="ArmClient"/>
/// so tests inject one built against a fake HTTP transport — parity with the
/// sibling H4 ARM collaborators (task 121/123/125 pattern).
/// </summary>
public sealed class ArmOperatorKvRbacBootstrapper : IOperatorKvRbacBootstrapper
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmOperatorKvRbacBootstrapper> _logger;

    /// <summary>Constructs the bootstrapper. Production DI reuses the shared UAMI-pinned ArmClient factory pattern (Program.cs).</summary>
    public ArmOperatorKvRbacBootstrapper(
        ArmClient armClient,
        ILogger<ArmOperatorKvRbacBootstrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OperatorKvRbacBootstrapOutcome> EnsureGrantedAsync(
        OperatorKvRbacBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Guard: PrincipalObjectId must be a well-formed non-empty Guid.
        //     H4/H4-shared pass string.Empty when interStepState.MiObjectId is
        //     absent (upstream H2a bug) — surface as domain Failure with a
        //     specific diagnostic (Resumable per H4 classification), NEVER a
        //     silent success + downstream 403 loop.
        if (string.IsNullOrWhiteSpace(request.PrincipalObjectId))
        {
            return new OperatorKvRbacBootstrapOutcome.Failure(
                $"KV RBAC bootstrap on vault '{request.KeyVaultName}' aborted: PrincipalObjectId is empty. " +
                "Upstream H2a MUST populate interStepState.MiObjectId (UAMI principal object id) " +
                "before H4 dispatches. Resume after fixing the upstream population.");
        }
        if (!Guid.TryParse(request.PrincipalObjectId, out var principalGuid) || principalGuid == Guid.Empty)
        {
            return new OperatorKvRbacBootstrapOutcome.Failure(
                $"KV RBAC bootstrap on vault '{request.KeyVaultName}' aborted: PrincipalObjectId " +
                $"'{request.PrincipalObjectId}' is not a valid non-empty Guid. RoleAssignment PUT requires a Guid principalId.");
        }
        if (string.IsNullOrWhiteSpace(request.KeyVaultResourceId))
        {
            return new OperatorKvRbacBootstrapOutcome.Failure(
                $"KV RBAC bootstrap on vault '{request.KeyVaultName}' aborted: KeyVaultResourceId is empty. " +
                "H4/H4-shared MUST derive this from subscription + rg + vault name.");
        }
        if (string.IsNullOrWhiteSpace(request.RoleDefinitionId))
        {
            return new OperatorKvRbacBootstrapOutcome.Failure(
                $"KV RBAC bootstrap on vault '{request.KeyVaultName}' aborted: RoleDefinitionId is empty. " +
                $"Default should be {nameof(KvBuiltInRoleIds)}.{nameof(KvBuiltInRoleIds.SecretsOfficer)} " +
                $"({KvBuiltInRoleIds.SecretsOfficer}).");
        }
        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return new OperatorKvRbacBootstrapOutcome.Failure(
                $"KV RBAC bootstrap on vault '{request.KeyVaultName}' aborted: SubscriptionId is empty. " +
                "Required to build the roleDefinitions/{id} scope.");
        }

        var scope = new ResourceIdentifier(request.KeyVaultResourceId);
        var roleDefinitionId = new ResourceIdentifier(
            $"/subscriptions/{request.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{request.RoleDefinitionId}");

        var content = new RoleAssignmentCreateOrUpdateContent(roleDefinitionId, principalGuid)
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };
        var roleAssignmentName = DeterministicRoleAssignmentName(scope, request.PrincipalObjectId, roleDefinitionId);
        var collection = _armClient.GetRoleAssignments(scope);

        try
        {
            _logger.LogInformation(
                "HANDLER-09 bootstrap: PUT role assignment '{RoleAssignmentName}' scope={Scope} " +
                "principalId={PrincipalId} roleDefinitionId={RoleDefinitionId} vault={Vault}",
                roleAssignmentName, scope, request.PrincipalObjectId, roleDefinitionId, request.KeyVaultName);

            await collection.CreateOrUpdateAsync(WaitUntil.Completed, roleAssignmentName, content, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "HANDLER-09 bootstrap: fresh grant succeeded on vault '{Vault}' for principal '{Principal}' role '{Role}'",
                request.KeyVaultName, request.PrincipalObjectId, request.RoleDefinitionId);
            return new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: true);
        }
        catch (RequestFailedException ex) when (
            ex.Status == 409
            || string.Equals(ex.ErrorCode, "RoleAssignmentExists", StringComparison.OrdinalIgnoreCase))
        {
            // Defense-in-depth: either the deterministic-name PUT lost a race
            // to another attempt, OR a differently-named assignment already
            // covers this (scope, principal, role) triple (e.g. a manual grant
            // from SESSION 2's procedure). ARM enforces triple-uniqueness
            // regardless of assignment name.
            _logger.LogInformation(
                "HANDLER-09 bootstrap: role assignment already exists on vault '{Vault}' for principal '{Principal}' " +
                "(status={Status} code={ErrorCode}) — treated as idempotent success",
                request.KeyVaultName, request.PrincipalObjectId, ex.Status, ex.ErrorCode);
            return new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: false);
        }
        catch (RequestFailedException ex)
        {
            var diagnostic =
                $"KV RBAC bootstrap FAILED on vault '{request.KeyVaultName}' for principal " +
                $"'{request.PrincipalObjectId}' (role '{request.RoleDefinitionId}'): HTTP {ex.Status} " +
                $"{ex.ErrorCode}: {ex.Message}. Manual grant of 'Key Vault Secrets Officer' on the vault to " +
                "the L2 caller principal (SESSION 2 procedure) unblocks resume.";
            _logger.LogError(ex, "HANDLER-09 bootstrap failed: {Diagnostic}", diagnostic);
            return new OperatorKvRbacBootstrapOutcome.Failure(diagnostic);
        }
    }

    /// <summary>
    /// Derives a stable role-assignment name (a GUID) from
    /// (scope, principalId, roleDefinitionId) so a retry targets the SAME
    /// ARM object. Exposed <c>internal</c> so tests assert stability without
    /// depending on ARM call order. NOT cryptographically an RFC 4122 v5 UUID
    /// — sufficient for a deterministic, collision-resistant name. Parity with
    /// <see cref="ArmSlotIdentityRoleGranter.DeterministicRoleAssignmentName"/>.
    /// </summary>
    internal static string DeterministicRoleAssignmentName(
        ResourceIdentifier scope, string principalId, ResourceIdentifier roleDefinitionId)
    {
        var seed = $"{scope}|{principalId}|{roleDefinitionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes).ToString();
    }
}
