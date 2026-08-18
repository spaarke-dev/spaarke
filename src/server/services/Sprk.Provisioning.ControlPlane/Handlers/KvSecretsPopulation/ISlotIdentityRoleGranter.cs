// -----------------------------------------------------------------------------
// ISlotIdentityRoleGranter.cs
//
// T5 interim mitigation abstraction — grants the KV Secrets User role to BOTH
// slot System-Assigned MI principals per design.md §4B T5 + spec.md FR-33 T5.
//
// T5 STRUCTURAL NOTE:
//   T5 is STRUCTURALLY IMPOSSIBLE post-Phase-C UAMI (uami.bicep task 028 +
//   app-service.bicep task 029). Once the App Service uses UAMI-only (no
//   System-Assigned MI on either slot), there is nothing to grant against on
//   the per-slot side — the UAMI is the single identity on both slots.
//   Task 029 shipped the UAMI-only refactor at commit fce5fb70f. Task 030
//   shipped the RBAC migration from System-Assigned MI to UAMI at
//   b52f991d9.
//
//   Given this project's Bicep is already UAMI-only, T5 in a fresh customer
//   stamp is a DEFENSIVE NO-OP — the granter's <see cref="SlotIdentityRoleGrantResult"/>
//   returns <see cref="SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity"/>
//   when it cannot find any System-Assigned MI on either slot. The H4 handler
//   treats this as SUCCESS (structurally impossible), NOT a failure.
//
//   Kept as a seam so pre-Phase-C rolling deploys (a hypothetical rollback
//   scenario) OR non-canonical customer stamps that somehow have a
//   System-Assigned MI can be mitigated without emergency code changes.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="AzCliSlotIdentityRoleGranter"/> shells out to
//       `az webapp identity show` + `az role assignment create`.
//     - Test: per-unit-test stubs returning canned outcomes.
//   Interface earns its keep — no NIH; the T5 fix must be executable + tested.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// T5 interim mitigation — grants KV Secrets User to BOTH slot System-Assigned
/// MI principals. Returns <see cref="SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity"/>
/// (SUCCESS-equivalent) when the slots have no System-Assigned MI to grant to
/// (post-Phase-C UAMI structural fix).
/// </summary>
public interface ISlotIdentityRoleGranter
{
    /// <summary>
    /// Attempts to read both slots' System-Assigned MI principal IDs and grant
    /// the KV Secrets User role. Domain outcomes never throw; only genuine
    /// infra faults (network, auth) throw so the handler can classify per §4C.
    /// </summary>
    /// <param name="input">The subscription + resource group + app service + slot names + KV resource id + role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SlotIdentityRoleGrantResult> GrantAsync(
        SlotIdentityRoleGrantInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to <see cref="ISlotIdentityRoleGranter.GrantAsync"/>.
/// </summary>
/// <param name="SubscriptionId">Customer subscription id (App Service + KV).</param>
/// <param name="ResourceGroupName">Resource group containing the App Service + KV.</param>
/// <param name="AppServiceName">App Service name (for slot MI resolution).</param>
/// <param name="StagingSlotName">Staging slot name (typically <c>staging</c>).</param>
/// <param name="VaultResourceId">The full KV resource id (grant scope) — e.g. <c>/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{name}</c>.</param>
/// <param name="RoleDefinitionId">The KV Secrets User role definition id (well-known GUID from <see cref="KvSecretsPopulationOptions.KvSecretsUserRoleId"/>).</param>
public sealed record SlotIdentityRoleGrantInput(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    string StagingSlotName,
    string VaultResourceId,
    string RoleDefinitionId);

/// <summary>
/// Discriminated result of the T5 grant attempt.
/// </summary>
public abstract record SlotIdentityRoleGrantResult
{
    private SlotIdentityRoleGrantResult() { }

    /// <summary>Both slots had System-Assigned MIs and both were granted the KV Secrets User role.</summary>
    public sealed record Granted(string ProdPrincipalId, string StagingPrincipalId) : SlotIdentityRoleGrantResult;

    /// <summary>
    /// STRUCTURALLY IMPOSSIBLE post-Phase-C UAMI — neither slot has a
    /// System-Assigned MI to grant to. Treated as SUCCESS by the H4 handler
    /// (this is the desired post-Phase-C steady state).
    /// </summary>
    public sealed record NoSlotSystemAssignedIdentity(string Diagnostic) : SlotIdentityRoleGrantResult;

    /// <summary>
    /// One or both grants failed. Not fatal to the pipeline (T5 is INTERIM per
    /// spec.md FR-33 T5); the handler classifies as Resumable so the operator
    /// can re-run or wait for UAMI migration.
    /// </summary>
    public sealed record Failure(string Diagnostic) : SlotIdentityRoleGrantResult;
}
