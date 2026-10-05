// -----------------------------------------------------------------------------
// ControlPlaneIdentityOptions.cs
//
// Task 249 (owner decision 2026-10-02): the ONE Worker setting that names the L2 control plane's own
// identity (the Worker UAMI). Two handlers grant it something on a customer stamp:
//   - H4 (KvSecretsPopulation): Key Vault Secrets Officer on the customer vault before writing it
//     (task 245b — never the stamp's BFF UAMI);
//   - H2a (BicepInfraDeploy): sent as customer.bicep's controlPlaneUamiPrincipalId → Website
//     Contributor on the stamp BFF (H4b Kudu log fetch + H9 zip-deploy), Model 1 stamps only.
// It replaces KvSecretsPopulationOptions.ControlPlanePrincipalObjectId (task 245b) and the short-lived
// BicepInfraDeployOptions.ControlPlaneUamiPrincipalId, so one value cannot drift between two settings
// and neither handler depends on the other's options. An L2-owned value, so a validated Worker option
// (ValidateOnStart), never a run parameter (.claude/constraints/provisioning.md run-context contract).
// Set by controlplane-worker-app-service.bicep: ControlPlaneIdentity__PrincipalObjectId.
//
// KNOWN MODEL 2 GAP (Model 2 is out of scope, owner 2026-09-30): H4's grant is not limited to Model 1. On a
// Model 2 stamp the vault is in the customer's tenant, where a role assignment naming this Spaarke-tenant
// principal cannot be created (whether a Lighthouse delegation can carry a data-action role such as Secrets
// Officer instead is unverified) — predates task 249 (T245b). H2a's Website Contributor grant is Model 1 only for the same reason; a Model 2
// stamp relies on Spaarke's Lighthouse delegation, whose roles nothing checks yet (H1 only checks that a
// delegation exists). Both are recorded as task 249 follow-ups.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// The L2 control plane's own identity, as the handlers that grant it access on a customer stamp need it.
/// Bound from the <c>ControlPlaneIdentity</c> configuration section; validated when the Worker starts.
/// </summary>
public sealed class ControlPlaneIdentityOptions
{
    /// <summary>Configuration section name (<c>ControlPlaneIdentity__PrincipalObjectId</c> as an app setting).</summary>
    public const string SectionName = "ControlPlaneIdentity";

    /// <summary>
    /// Object id (GUID) of the L2 control plane's own managed identity — the Worker UAMI. Required.
    /// <c>platform-controlplane.bicep</c> passes <c>uami.outputs.principalId</c> to the Worker module as
    /// <c>controlPlanePrincipalId</c>, the same value its Cosmos RBAC takes.
    /// </summary>
    public string PrincipalObjectId { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="PrincipalObjectId"/> in canonical GUID form (<c>D</c> format, trimmed). Call only after
    /// <see cref="Validate"/> has passed — the Worker guarantees that at startup.
    /// </summary>
    public string CanonicalPrincipalObjectId() => Guid.Parse(PrincipalObjectId.Trim()).ToString("D");

    /// <summary>
    /// Startup validation (Worker/Program.cs ValidateOnStart). Throws
    /// <see cref="InvalidOperationException"/> naming the setting.
    /// </summary>
    public void Validate()
    {
        if (!Guid.TryParse(PrincipalObjectId?.Trim(), out var principal) || principal == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{SectionName}:PrincipalObjectId must be the object id (GUID) of the L2 control plane's own " +
                "identity — H4 grants it Key Vault Secrets Officer on each customer vault and H2a sends it as " +
                $"customer.bicep's controlPlaneUamiPrincipalId (got '{PrincipalObjectId}'). Set by " +
                "controlplane-worker-app-service.bicep (controlPlanePrincipalId).");
        }
    }
}
