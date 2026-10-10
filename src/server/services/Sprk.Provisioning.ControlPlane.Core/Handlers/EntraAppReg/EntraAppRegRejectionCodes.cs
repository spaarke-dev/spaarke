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
    /// <see cref="EntraAppRegOptions.ExpectedDelegatedScopeCount"/> is &lt; 1 —
    /// configuration drift. RENAMED SCOPE (task 130, Wave G-3): this guard
    /// previously validated the app-role catalog count; H10 now owns the
    /// 14-app-role catalog exclusively (see EntraAppRegPermissionCatalog.cs
    /// file header), so this guard validates the 5-delegated-scope count H3's
    /// own <see cref="IAdminConsentVerifier"/> checks. The STRING VALUE is
    /// kept unchanged for rejection-code stability (external tools / operator
    /// UI filters key on this constant).
    /// </summary>
    public const string NullAppRoleIdInCatalog = "appreg-null-approleid-in-catalog";

    /// <summary>
    /// Run parameter <c>tenancyModel</c> missing or unrecognized — I6 invariant
    /// (design.md §4D, spec.md FR-40, task 130). The Model 1 vs Model 2 branch
    /// selection MUST take an explicit tenancy-model parameter with NO default
    /// value; a missing/blank/unrecognized value refuses loudly rather than
    /// silently defaulting to either branch.
    /// </summary>
    public const string MissingOrInvalidTenancyModel = "appreg-missing-or-invalid-tenancy-model";

    /// <summary>H3 requires <c>InterStepState.MiObjectId</c> (UAMI principalId — the FIC <c>subject</c>, per auth-v4 §3.1) populated by H2a before H3 dispatches, in BOTH tenancy models post-task-222 per D-13.</summary>
    public const string MissingUamiObjectId = "appreg-missing-uami-object-id";

    /// <summary>
    /// Task 230b: assigning the keyless-proof app role on the customer's BFF service principal to the L2 Worker
    /// identity failed (after the propagation retries). Without it H13 cannot run the stamp's keyless proof.
    /// </summary>
    public const string KeylessProofRoleAssignmentFailed = "appreg-keyless-proof-role-assignment-failed";

    /// <remarks>
    /// RETIRED 2026-09-29 (task 222 per D-13): the H3 shared-app-reg branch was deleted from
    /// <see cref="H3EntraAppRegHandler"/>. This code is no longer emitted by any live code path.
    /// VALUE PRESERVED per this file's STABILITY guidance ("add new codes as needed and mark old ones
    /// @[Obsolete] on removal") — external operator UI filters / alerting rules / log queries keyed on
    /// this constant since task 130 (Wave G-3) must not silently break.
    /// </remarks>
    [System.Obsolete("Retired 2026-09-29 per D-13 (task 222) — H3 shared-app-reg branch deleted. Value kept for external-tool log/alerting historical stability. Do not reference from new code.", error: false)]
    public const string MissingSharedAppRegConfig = "appreg-missing-shared-appreg-config";

    /// <remarks>
    /// RETIRED 2026-09-29 (task 222 per D-13): the H3 shared-app-reg branch was deleted from
    /// <see cref="H3EntraAppRegHandler"/>. This code is no longer emitted by any live code path.
    /// VALUE PRESERVED per this file's STABILITY guidance ("add new codes as needed and mark old ones
    /// @[Obsolete] on removal") — external operator UI filters / alerting rules / log queries keyed on
    /// this constant since task 130 (Wave G-3) must not silently break.
    /// </remarks>
    [System.Obsolete("Retired 2026-09-29 per D-13 (task 222) — H3 shared-app-reg branch deleted. Value kept for external-tool log/alerting historical stability. Do not reference from new code.", error: false)]
    public const string SharedAppRegConfigurationDrift = "appreg-shared-appreg-configuration-drift";

    /// <summary>Model 2's federated identity credential creation (auth-v4 §3.1 recipe) failed.</summary>
    public const string FicCreationFailed = "appreg-fic-creation-failed";

    /// <summary>
    /// Model 2's FIC structural verification (independent re-GET of the
    /// just-written FIC confirming its (issuer, subject, audience) triple —
    /// see GraphAppRegistrationProvisioner GOTCHA 2: L2 cannot perform a live
    /// exchange) did not succeed after exhausting the read-after-write retry
    /// budget. NOTE (A42): the EXCHANGE-side propagation code is the measured
    /// AADSTS70025 (~8 intermittent failures over ~130s; 70021 documented but
    /// never observed live) — that policy lives in FicExchangeOutcomeClassifier
    /// for the exchange-capable hosts, not in this re-GET path.
    /// </summary>
    public const string FicVerificationFailed = "appreg-fic-verification-failed";

    /// <summary>
    /// Model 2's FIC would pair an app registration with a UAMI from a
    /// DIFFERENT tenant — refused loudly at provisioning time by
    /// <see cref="GraphAppRegistrationProvisioner.AssertFicTenancy"/>
    /// (task 205b row A42, SF-5 closure; port of the master script's
    /// <c>Assert-SpaarkeFicTenancy</c>). Entra's same-tenant FIC rule makes
    /// the cross-tenant pair structurally impossible — it would CREATE
    /// successfully and fail only at the customer's first OBO exchange,
    /// weeks later, silently. Resumable: operator corrects the run's tenant /
    /// profile configuration and resumes.
    /// </summary>
    public const string CrossTenantFicRefused = "appreg-cross-tenant-fic-refused";

    /// <summary>
    /// ISS-015: H3 cannot plan the federated credential that lets the L2 Worker sign in as the customer's BFF registration
    /// (H6 / H7 / H7b, D-13) — <c>ControlPlaneIdentity:PrincipalObjectId</c> is blank or not a GUID, it equals the stamp's
    /// BFF UAMI, or <c>EntraAppRegOptions:WorkerFicName</c> is blank or equal to <c>FicName</c>. Platform configuration
    /// drift, refused before any Graph write. Resumable after the Worker setting is fixed.
    /// </summary>
    public const string WorkerFicIdentityMissing = "appreg-worker-fic-identity-missing";

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

    /// <summary>
    /// T240a: the intake <c>dataverseEnvUrl</c> fails <c>DataverseEnvironmentUrlRule</c> (the rule POST /api/runs and H5
    /// apply), so H3 cannot derive the code pages' SPA redirect. A run that passed intake cannot reach this; it guards a
    /// run document that predates the rule. Resumable after the operator corrects the run.
    /// </summary>
    public const string DataverseEnvUrlInvalid = "appreg-dataverse-env-url-invalid";

    /// <summary>
    /// T240a: <c>EntraAppRegOptions:PreAuthorizedClientAppIds</c> holds a value that is not a GUID — platform
    /// configuration drift. Nothing is written to Entra. Resumable after the Worker setting is fixed.
    /// </summary>
    public const string PreAuthorizedClientAppIdInvalid = "appreg-preauthorized-client-invalid";

    /// <summary>
    /// T240a: the app registration has no enabled <c>user_impersonation</c> scope to pre-authorize clients on, after H3's
    /// own reconcile added it — Entra state H3 cannot repair. Resumable.
    /// </summary>
    public const string ClientAccessFailed = "appreg-client-access-failed";

    /// <summary>
    /// T240a review: an EXISTING registration named <c>spaarke-bff-api-{customerId}</c> is not safe to adopt — more than
    /// one has that name, it holds a client secret or certificate (stamps are secret-free), it carries a federated
    /// credential H3 did not create, or someone other than the control plane owns it. H3 would otherwise give it the
    /// stamp's federated credential and H10 would make it the customer's Dataverse administrator, so a registration
    /// pre-created under that predictable name by anyone in the tenant must never be taken over. Nothing is written;
    /// the operator investigates and removes the impostor (or the stale registration), then resumes.
    /// </summary>
    public const string AdoptionRefused = "appreg-adoption-refused";

    /// <summary>
    /// T255 (INCOMING-141 §5): H3 could not put the <c>acct</c> optional claim on the registration's access tokens, or
    /// did not read it back afterwards. Without it the stamp BFF denies every first sign-in of a customer employee
    /// (<c>workforce_acct_claim_missing</c>). Resumable — the step is idempotent.
    /// </summary>
    public const string AcctClaimFailed = "appreg-acct-claim-failed";
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
    /// delegated Graph permissions (EntraAppRegPermissionCatalog). Flipped from <c>Pending</c> to
    /// <c>Verified</c> when <see cref="IAdminConsentVerifier"/> confirms all
    /// grants are present. Design.md §6.2 gateStates naming (kebab-case).
    /// </summary>
    public const string AdminConsent = "admin-consent";
}
