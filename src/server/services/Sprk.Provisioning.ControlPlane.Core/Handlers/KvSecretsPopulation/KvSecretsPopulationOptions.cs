// -----------------------------------------------------------------------------
// KvSecretsPopulationOptions.cs
//
// Bound options for the H4 handler's collaborators (KV secrets writer +
// App Service identity patcher + slot-identity role granter). Loaded from
// the "KvSecretsPopulationOptions" configuration section by Program.cs. (Its one
// required value, the L2 principal validated at Worker startup since task 245b,
// moved to ControlPlaneIdentityOptions — task 249.)
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
    // Task 253 (G38): AzCliExecutable DELETED — SecretClientKvWriter uses the Key Vault SDK; the Worker host has no az.

    /// <summary>
    /// Maximum time to wait for a single Key Vault secret set/get call
    /// (SecretClientKvWriter). Defaults to 90 seconds — KV writes are fast but RBAC
    /// propagation + throttle back-off can extend a single call.
    /// </summary>
    public TimeSpan KvOperationTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Maximum time to wait for a single <c>keyVaultReferenceIdentity</c> ARM
    /// PATCH (T1 — ArmAppServiceIdentityPatcher). Defaults to 60 seconds per slot.
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
    /// (<see cref="FileKvSecretManifest.SecretFreeIdentityOmitTargets"/>: <c>BFF-API-ClientSecret</c> and,
    /// since task 225b, <c>Dataverse-ClientSecret</c> — the Service Bus connection string and AI Search
    /// admin key were removed from the catalog for every stamp by T226) are (a) FILTERED from the entries
    /// <see cref="FileKvSecretManifest"/> serves (downstream of its BINDING
    /// never-delete invariant — manifest.yaml rows are NEVER touched) and
    /// (b) unioned into the task-126 FR-39 <c>OmitCanonicalNames</c> seam by
    /// H4 so the writers mark them <see cref="KvSecretWriteAction.Omitted"/>
    /// even if a non-filtering manifest implementation is ever registered.
    /// Mirrors the BFF App Service setting
    /// <c>Graph__Credentials__RequireSecretFreeIdentity=true</c> (§10.2 Δ3).
    /// Default <c>true</c> (task 225b, plan G21): every new stamp runs MI-FIC, and the BINDING
    /// credential-lifecycle rule forbids creating <c>BFF-API-ClientSecret</c> or
    /// <c>Dataverse-ClientSecret</c> in a secret-free environment (H4 omits — no sentinel). The
    /// Q3 Path A hold on <c>Dataverse-ClientSecret</c> (sunset 2026-11-23) protects the EXISTING
    /// live copy from deletion; it never required H4 to write a new one.
    /// </summary>
    public bool RequireSecretFreeIdentity { get; set; } = true;

    /// <summary>
    /// Row A38a — Q3 Path A rollback flag (§6.5 record 2026-08-25; sunset
    /// 2026-11-23). When <c>true</c> WHILE <see cref="RequireSecretFreeIdentity"/>
    /// is also <c>true</c>, the A38a omit targets are RE-INCLUDED in
    /// served entries + NOT unioned into the omit seam (regression path back
    /// to client-secret auth). Applies to exactly the
    /// <see cref="FileKvSecretManifest.SecretFreeIdentityOmitTargets"/>. The positive migration marker is NOT applied
    /// while this flag is set (a rolled-back environment is not secret-free).
    /// Default <c>false</c>.
    /// <para>
    /// Scope (task 225b): rollback exists only for an environment still on client-secret auth (credential-lifecycle
    /// rule prong 3) — never for new provisioning. Both re-included secrets are <c>from-existing-kv</c> entries that
    /// nothing produces, so on a new stamp H4 fails with the flag set instead of writing a client secret. The flag
    /// is Worker-wide and is not set by Bicep: a redeploy clears a value set by hand.
    /// </para>
    /// </summary>
    public bool SecretFreeIdentityRollback { get; set; }

    // Task 249 (owner decision 2026-10-02): the L2 principal H4 grants Key Vault Secrets Officer on each
    // customer vault (task 245b) moved to ControlPlaneIdentityOptions.PrincipalObjectId, shared with H2a.

    // Task 225b (owner D18, 2026-10-02): the platform-vault option (the Spaarke platform vault H4 copied
    // the Spaarke-shared vendor keys from) was removed together with those keys and their manifest value
    // source — Bing Search v7 was retired by Microsoft 2025-08-11 and LlamaParse has no production
    // caller, so no customer stamp carries a vendor key.
}
