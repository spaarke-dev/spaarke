// -----------------------------------------------------------------------------
// IOperatorKvRbacBootstrapper.cs
//
// HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27) — F15 + F18 verbatim.
// Fresh RBAC-enabled Key Vaults grant NO data-plane access even to the
// subscription Owner — the very first SecretClient.SetSecretAsync call on
// a freshly-created KV fails with 403 unless the caller identity has been
// explicitly granted "Key Vault Secrets Officer" on the vault. Both H4
// (per-tenant KV) and H4-shared (shared KV) hit this failure mode on
// SESSION 2's Model 1 Prod standup; the operator manually granted this for
// BOTH KVs. Automation eliminates the manual step + prevents dead-loop
// halts.
//
// This seam gates H4 + H4-shared on a bootstrap RBAC grant of "Key Vault
// Secrets Officer" (built-in role id b86a8fe4-44ce-4948-aee5-eccb2c155cd7)
// to the L2 caller identity on the target vault BEFORE any KV write fires.
// Idempotent — no-op when the assignment already exists.
//
// PRODUCTION IMPL:
//   <see cref="ArmOperatorKvRbacBootstrapper"/> uses Azure.ResourceManager.
//   Authorization to PUT a role assignment. Wave 2 ships as a scaffold
//   returning Success unconditionally with an informational log line; the
//   real ARM PUT lands as an incremental change without touching H4 or
//   H4-shared.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Grants Key Vault Secrets Officer (or the configured role) to the L2
/// caller identity on the target vault BEFORE the writer fires. F15 + F18
/// verbatim: fresh RBAC-enabled KVs deny data-plane access even to
/// subscription Owner. Idempotent — no-op when the assignment already
/// exists.
/// </summary>
public interface IOperatorKvRbacBootstrapper
{
    /// <summary>
    /// Ensures the configured operator role is granted on the target vault.
    /// Domain outcomes never throw; infrastructure faults propagate.
    /// </summary>
    Task<OperatorKvRbacBootstrapOutcome> EnsureGrantedAsync(
        OperatorKvRbacBootstrapRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Input for <see cref="IOperatorKvRbacBootstrapper.EnsureGrantedAsync"/>.</summary>
/// <param name="SubscriptionId">Target Azure subscription id.</param>
/// <param name="ResourceGroupName">RG that hosts the target vault.</param>
/// <param name="KeyVaultName">Target vault name.</param>
/// <param name="KeyVaultResourceId">Full resource-id of the target vault (used as scope).</param>
/// <param name="PrincipalObjectId">Principal (operator / L2 UAMI) to grant the role to.</param>
/// <param name="RoleDefinitionId">Role id (default: Key Vault Secrets Officer built-in role).</param>
public sealed record OperatorKvRbacBootstrapRequest(
    string SubscriptionId,
    string ResourceGroupName,
    string KeyVaultName,
    string KeyVaultResourceId,
    string PrincipalObjectId,
    string RoleDefinitionId);

/// <summary>Result of <see cref="IOperatorKvRbacBootstrapper.EnsureGrantedAsync"/>.</summary>
public abstract record OperatorKvRbacBootstrapOutcome
{
    /// <summary>Role assignment exists (either freshly granted or already-present).</summary>
    public sealed record Success(bool WasFreshlyGranted) : OperatorKvRbacBootstrapOutcome;

    /// <summary>Grant could not be applied — vault RBAC still blocks the caller.</summary>
    public sealed record Failure(string Diagnostic) : OperatorKvRbacBootstrapOutcome;
}

/// <summary>Well-known Azure built-in role ids (subset).</summary>
public static class KvBuiltInRoleIds
{
    /// <summary>Key Vault Secrets Officer — F15b role id verbatim in punchlist.</summary>
    public const string SecretsOfficer = "b86a8fe4-44ce-4948-aee5-eccb2c155cd7";
}
