// -----------------------------------------------------------------------------
// EntraAppRegRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H3EntraAppRegHandler (task 046).
// Every distinct failure mode gets its own code so the reconciler + operator UI
// can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-06 acceptance
//     (H3): 1 BFF app-reg with 14 permission grants per GraphAppRoles.cs;
//     Register-EntraAppRegistrations.ps1 idempotent; -TenantId mandatory;
//     app-reg's 14 permissions returned by Graph oauth2PermissionGrants
//     query. Sign-in audience AzureADMultipleOrgs (Model 2 consent).
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules:
//     Dataverse S2S app-reg MUST NOT be re-introduced (r3 task 060 dropped it).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//     -TenantId mandatory; no default fallback.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H3 row +
//     §4C rollback taxonomy — H3 failures are Resumable (operator resolves
//     consent / permissions / connectivity + POST /api/runs/{id}/resume);
//     admin-consent PENDING is WaitingOnGate (NOT a failure).
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
//
// PATTERN PARITY:
//   Mirrors Handlers/BicepInfraDeploy/BicepDeployRejectionCodes.cs and
//   Handlers/SubscriptionReadiness/SubscriptionReadinessRejectionCodes.cs —
//   one const per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Machine-stable rejection codes for <see cref="H3EntraAppRegHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class EntraAppRegRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant); PS script requires <c>-TenantId</c>.</summary>
    public const string MissingTenantId = "appreg-missing-tenant-id";

    /// <summary>Run parameter <c>keyVaultName</c> missing — the PS script requires <c>-KeyVaultName</c> for <c>BFF-API-ClientSecret</c> storage.</summary>
    public const string MissingKeyVaultName = "appreg-missing-kv-name";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "appreg-run-not-found";

    /// <summary>
    /// <see cref="Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles"/> constant
    /// contains one or more roles with a null <c>AppRoleId</c>. Handler MUST
    /// refuse to proceed — a null AppRoleId propagates a Graph 400 downstream
    /// (r1 task 005 Phase A completed all 14 GUIDs; this guard catches
    /// regression). NEVER emitted after Phase A completion; kept as a
    /// forcing-function.
    /// </summary>
    public const string NullAppRoleIdInCatalog = "appreg-null-approleid-in-catalog";

    /// <summary>
    /// The provisioner reported a hard failure (PS script non-zero exit / Graph
    /// error). Handler classifies as Resumable (operator resolves the missing
    /// precondition — Entra permission, KV RBAC, or connectivity — then
    /// POSTs /api/runs/{id}/resume).
    /// </summary>
    public const string ProvisioningFailed = "appreg-provisioning-failed";

    /// <summary>
    /// The provisioner completed but did NOT populate the required output
    /// set (BFF appId + KV secret URI ref). Configuration drift on the script
    /// side — operator must resolve.
    /// </summary>
    public const string ProvisioningOutputsIncomplete = "appreg-provisioning-outputs-incomplete";

    /// <summary>
    /// Client secret was returned as cleartext (or matched a known secret
    /// pattern) rather than a KV URI reference. ADR-028 MUST rule — secrets
    /// NEVER traverse Cosmos parameters/interStepState in cleartext.
    /// Quarantine-required (write-side data leak).
    /// </summary>
    public const string CleartextSecretLeak = "appreg-cleartext-secret-leak";

    /// <summary>
    /// The provisioner returned an output claiming a Dataverse S2S app-reg
    /// was provisioned. spec.md MUST rule — S2S app-reg dropped by r3 task 060
    /// (zero code consumers; BFF app-reg IS the Dataverse Application User).
    /// Quarantine-required — orphaned S2S state left in Entra.
    /// </summary>
    public const string S2SAppRegForbidden = "appreg-s2s-forbidden";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "appreg-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H3 was in flight.</summary>
    public const string RunDeletedDuringProvisioning = "appreg-run-deleted-during-provisioning";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c>
/// by H3 Entra app-reg. Kept as string constants so grep across the codebase
/// finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class EntraAppRegGates
{
    /// <summary>
    /// The gate H3 owns for tenant-admin consent of the BFF app-registration's
    /// 14 Graph application-role assignments. Flipped from <c>Pending</c> to
    /// <c>Verified</c> when <see cref="IAdminConsentVerifier"/> confirms all
    /// grants are present. Design.md §6.2 gateStates naming (kebab-case).
    /// </summary>
    public const string AdminConsent = "admin-consent";
}
