// -----------------------------------------------------------------------------
// EntraAppRegOptions.cs
//
// Bound options for the H3 handler's collaborators (script provisioner +
// admin-consent verifier). Loaded from the "EntraAppReg" configuration section
// by Program.cs — runtime-configurable so the linux-x64 App Service publish
// layout can be honored without recompiling.
//
// PATTERN PARITY:
//   Mirrors Handlers/BicepInfraDeploy/BicepInfraDeployOptions.cs so operators
//   configuring the L2 App Service see a consistent shape.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Bound options for <see cref="H3EntraAppRegHandler"/> collaborators.
/// Configuration key: <c>EntraAppReg</c>.
/// </summary>
public sealed class EntraAppRegOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy.BicepInfraDeployOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to <c>scripts/Register-EntraAppRegistrations.ps1</c>
    /// (hardened by r1 task 010 per commit fea66c023). Defaults to
    /// <c>scripts/Register-EntraAppRegistrations.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string RegisterEntraAppRegistrationsScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Register-EntraAppRegistrations.ps1");

    /// <summary>
    /// Maximum time to wait for a single Register-EntraAppRegistrations.ps1
    /// invocation. Defaults to 10 minutes — the script performs 5 Graph API
    /// steps + a KV write; a healthy invocation completes in ~30–60 seconds,
    /// but Graph transient faults + KV RBAC propagation can extend it.
    /// </summary>
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Expected number of Graph application-role assignments after admin
    /// consent (feeds <see cref="IAdminConsentVerifier"/>). Defaults to 14
    /// (r1 task 005 populated all 14 GUIDs in <c>GraphAppRoles.cs</c>). If
    /// GraphAppRoles.cs adds/removes a role, this default advances in lockstep
    /// via <see cref="H3EntraAppRegHandler"/>'s runtime read from the constant.
    /// </summary>
    public int ExpectedAppRoleCount { get; set; } = 14;
}
