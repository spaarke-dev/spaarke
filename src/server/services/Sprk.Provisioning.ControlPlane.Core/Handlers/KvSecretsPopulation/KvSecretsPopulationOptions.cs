// -----------------------------------------------------------------------------
// KvSecretsPopulationOptions.cs
//
// Bound options for the H4 handler's collaborators (KV secrets writer +
// App Service identity patcher + slot-identity role granter). Loaded from
// the "KvSecretsPopulationOptions" configuration section by Program.cs and
// validated at Worker startup (task 245b — ValidateOnStart).
//
// PATTERN PARITY:
//   Mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs and
//   Handlers/BicepInfraDeploy/BicepInfraDeployOptions.cs so operators
//   configuring the L2 App Service see a consistent shape.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Bound options for <see cref="H4KvSecretsPopulationHandler"/> collaborators.
/// Configuration key: <c>KvSecretsPopulationOptions</c>.
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
    public string KvSecretsUserRoleId { get; set; } = "4633458b-17de-408a-b874-0445c86b69e6";

    /// <summary>
    /// Row A38a (auth-v4 §10.1 Δ1/Δ2 + §9.1 OMIT-is-the-signal; task 205a,
    /// 2026-08-25). When <c>true</c>, this environment runs on secret-free
    /// BFF identity (MI-FIC per ADR-028 A4) and the A38a credential slots
    /// (<see cref="FileKvSecretManifest.SecretFreeIdentityOmitTargets"/>: since T226
    /// only <c>BFF-API-ClientSecret</c> — the Service Bus connection string and AI Search
    /// admin key were removed from the catalog for every stamp) are (a) FILTERED from the entries
    /// <see cref="FileKvSecretManifest"/> serves (downstream of its BINDING
    /// never-delete invariant — manifest.yaml rows are NEVER touched) and
    /// (b) unioned into the task-126 FR-39 <c>OmitCanonicalNames</c> seam by
    /// H4 so the writers mark them <see cref="KvSecretWriteAction.Omitted"/>
    /// even if a non-filtering manifest implementation is ever registered.
    /// Mirrors the BFF App Service setting
    /// <c>Graph__Credentials__RequireSecretFreeIdentity=true</c> (§10.2 Δ3).
    /// Default <c>false</c> — today's live client-secret state is unchanged
    /// until an operator flips this per-environment.
    /// NEVER affects <c>Dataverse-ClientSecret</c> (Q3 Path A rollback copy,
    /// unconditional until the 2026-11-23 sunset — §6.5 record 2026-08-25).
    /// </summary>
    public bool RequireSecretFreeIdentity { get; set; }

    /// <summary>
    /// Row A38a — Q3 Path A rollback flag (§6.5 record 2026-08-25; sunset
    /// 2026-11-23). When <c>true</c> WHILE <see cref="RequireSecretFreeIdentity"/>
    /// is also <c>true</c>, the three A38a omit targets are RE-INCLUDED in
    /// served entries + NOT unioned into the omit seam (regression path back
    /// to client-secret auth). Applies ONLY to the three A38a targets — never
    /// to <c>Dataverse-ClientSecret</c>, which is already unconditional and
    /// independently governed. The positive migration marker is NOT applied
    /// while this flag is set (a rolled-back environment is not secret-free).
    /// Default <c>false</c>.
    /// </summary>
    public bool SecretFreeIdentityRollback { get; set; }

    /// <summary>
    /// Task 245b (🔒 owner-approved 2026-10-01): object id of the L2 control plane's own identity — the
    /// Worker UAMI that writes the customer vault's secrets (H4, then H3). H4's KV RBAC bootstrap grants
    /// THIS principal Key Vault Secrets Officer on the customer vault. Before T245b it granted
    /// <c>InterStepState.MiObjectId</c> — the customer stamp's BFF UAMI — write access to its own vault,
    /// while the identity that actually writes had none. The stamp UAMI keeps only Secrets User
    /// (customer.bicep). Required GUID; validated at Worker startup. Same value
    /// <c>platform-controlplane.bicep</c> passes as <c>controlPlanePrincipalId</c> to its Cosmos RBAC.
    /// </summary>
    public string ControlPlanePrincipalObjectId { get; set; } = string.Empty;

    /// <summary>
    /// Task 245b: the Spaarke platform Key Vault holding the Spaarke-shared vendor keys (owner D5:
    /// <c>BingSearch-ApiKey</c>, <c>LlamaParse-ApiKey</c>) under their canonical names. H4 copies each
    /// <c>value_source: from-platform-vault</c> manifest entry from here into the customer vault.
    /// Required; validated at Worker startup.
    /// </summary>
    public string PlatformVaultName { get; set; } = string.Empty;

    /// <summary>
    /// Startup validation (Worker/Program.cs ValidateOnStart). Throws
    /// <see cref="InvalidOperationException"/> naming the invalid setting.
    /// </summary>
    public void Validate()
    {
        if (!Guid.TryParse(ControlPlanePrincipalObjectId?.Trim(), out var principal) || principal == Guid.Empty)
        {
            throw new InvalidOperationException(
                "KvSecretsPopulationOptions:ControlPlanePrincipalObjectId must be the object id (GUID) of the L2 " +
                "control plane's own identity — the principal H4 grants Key Vault Secrets Officer on each customer " +
                $"vault (got '{ControlPlanePrincipalObjectId}'). Set by controlplane-worker-app-service.bicep " +
                "(controlPlanePrincipalId).");
        }
        if (string.IsNullOrWhiteSpace(PlatformVaultName))
        {
            throw new InvalidOperationException(
                "KvSecretsPopulationOptions:PlatformVaultName is required — the Spaarke platform Key Vault H4 copies " +
                "the Spaarke-shared vendor keys (BingSearch-ApiKey, LlamaParse-ApiKey) from.");
        }
    }
}
