// -----------------------------------------------------------------------------
// KvSecretsPopulationOptions.cs
//
// Bound options for the H4 handler's collaborators (KV secrets writer +
// App Service identity patcher + slot-identity role granter). Loaded from
// the "KvSecretsPopulation" configuration section by Program.cs — runtime-
// configurable so the linux-x64 App Service publish layout can be honored
// without recompiling.
//
// PATTERN PARITY:
//   Mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs and
//   Handlers/BicepInfraDeploy/BicepInfraDeployOptions.cs so operators
//   configuring the L2 App Service see a consistent shape.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Bound options for <see cref="H4KvSecretsPopulationHandler"/> collaborators.
/// Configuration key: <c>KvSecretsPopulation</c>.
/// </summary>
public sealed class KvSecretsPopulationOptions
{
    /// <summary>
    /// Path to the <c>az</c> CLI executable. Defaults to <c>az</c> (resolved
    /// via PATH). On Linux App Service the operator install path is
    /// <c>/usr/bin/az</c>. Parity with
    /// <see cref="BicepInfraDeploy.BicepInfraDeployOptions.AzCliExecutable"/>.
    /// </summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>
    /// Maximum time to wait for a single <c>az keyvault secret set/show</c>
    /// invocation. Defaults to 90 seconds — KV writes are fast but RBAC
    /// propagation + throttle back-off can extend a single call.
    /// </summary>
    public TimeSpan KvOperationTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Maximum time to wait for a single <c>az webapp update --set
    /// keyVaultReferenceIdentity=...</c> invocation (T1 PATCH). Defaults to
    /// 60 seconds per slot.
    /// </summary>
    public TimeSpan T1PatchTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum time to wait for a single <c>az role assignment create</c>
    /// invocation (T5 interim grant). Defaults to 90 seconds — RBAC creates
    /// can be slow when the target scope is a KV in a different subscription.
    /// </summary>
    public TimeSpan T5RoleGrantTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Role definition ID for the <c>Key Vault Secrets User</c> RBAC role.
    /// Well-known GUID; centralized here so tests can pin it and production
    /// can override in a hypothetical sovereign cloud where the constant
    /// differs. Default: the public-cloud role id.
    /// </summary>
    public string KeyVaultSecretsUserRoleId { get; set; } = "4633458b-17de-408a-b874-0445c86b69e6";
}
