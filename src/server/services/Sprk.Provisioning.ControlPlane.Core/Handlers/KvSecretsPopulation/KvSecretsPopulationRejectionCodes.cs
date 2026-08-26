// -----------------------------------------------------------------------------
// KvSecretsPopulationRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H4KvSecretsPopulationHandler
// (task 047). Every distinct failure mode gets its own code so the reconciler +
// operator UI can branch on the exact reason WITHOUT string-matching the
// human-readable Diagnostic (which may be reworded for clarity).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-07 acceptance
//     (H4): KV secrets populated per canonical §7.9 names; App Service
//     keyVaultReferenceIdentity PATCHed to UAMI on BOTH slots (T1);
//     interim T5 grants KV RBAC to BOTH slot System-Assigned MI principals.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-33 (T1 + T5):
//       T1 = keyVaultReferenceIdentity == UAMI on both slots (silent-fail
//            trap #1 owner) — H4 PATCHes + verifies.
//       T5 = interim slot-MI RBAC grant — structurally superseded post-Phase-C
//            UAMI (uami.bicep task 028; app-service.bicep task 029).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34 (upgrade
//       mode H4): rotation-safe by default; explicit <c>rotate</c> run param
//       required to overwrite live secrets.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules:
//       BINDING pre-check — NEVER delete Dataverse-ClientSecret or
//       BFF-API-ClientSecret (r3 handoff; OBO + shared-lib Dataverse depend).
//   - projects/customer-provisioning-orchestration-r1/design.md §4B T1/T5.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback
//       taxonomy — mapping from rejection code to FailureClass.
//   - projects/customer-provisioning-orchestration-r1/design.md §7.9 canonical
//       KV secret + resource naming.
//   - .claude/adr/ADR-028 auth ceremony: keyVaultReferenceIdentity PATCH to
//       UAMI is one of the 21 MUSTs.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
//
// PATTERN PARITY:
//   Mirrors Handlers/BicepInfraDeploy/BicepDeployRejectionCodes.cs and
//   Handlers/EntraAppReg/EntraAppRegRejectionCodes.cs — one const per failure
//   branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Machine-stable rejection codes for <see cref="H4KvSecretsPopulationHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class KvSecretsPopulationRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "kvsecrets-missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing — H4 needs it for both `az keyvault` + `az role assignment` scoping (ADR-027 D4).</summary>
    public const string MissingSubscriptionId = "kvsecrets-missing-subscription-id";

    /// <summary>Run parameter <c>keyVaultName</c> missing — the target vault for all writes.</summary>
    public const string MissingKeyVaultName = "kvsecrets-missing-kv-name";

    /// <summary>Run parameter <c>secretsVer</c> missing — feeds idempotency key kv-{customerId}-{secretsVer}.</summary>
    public const string MissingSecretsVersion = "kvsecrets-missing-secrets-version";

    /// <summary>
    /// Run parameter <c>resourceGroupName</c> missing — H4 needs it to PATCH
    /// App Service keyVaultReferenceIdentity (T1) + query slot System-Assigned
    /// MI principals (T5 interim).
    /// </summary>
    public const string MissingResourceGroupName = "kvsecrets-missing-resource-group";

    /// <summary>Run parameter <c>appServiceName</c> missing — H4 needs it for the T1 PATCH.</summary>
    public const string MissingAppServiceName = "kvsecrets-missing-app-service-name";

    /// <summary>Run parameter <c>userAssignedIdentityResourceId</c> missing — the UAMI resource ID that H4 patches into keyVaultReferenceIdentity (T1).</summary>
    public const string MissingUamiResourceId = "kvsecrets-missing-uami-resource-id";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "kvsecrets-run-not-found";

    /// <summary>
    /// Manifest reader threw / failed to load the canonical secret catalog
    /// (Phase H task 084 artifact). Resumable — the reconciler retries after
    /// operator resolves the manifest source.
    /// </summary>
    public const string ManifestReadFailed = "kvsecrets-manifest-read-failed";

    /// <summary>
    /// BINDING pre-check violation — the loaded manifest contains a Delete op
    /// targeting <c>Dataverse-ClientSecret</c> or <c>BFF-API-ClientSecret</c>.
    /// Quarantine-required (fleet-wide OBO + shared-lib Dataverse depend on
    /// these secrets; deleting them silently breaks the whole fleet).
    /// See r3 handoff + spec.md MUST rule.
    /// </summary>
    public const string BindingPreCheckViolation = "kvsecrets-binding-precheck-violation";

    /// <summary>
    /// One or more KV writes failed after prior writes succeeded — partial
    /// state on the target vault. Quarantine-required (§4C partial-fail
    /// classification; operator must inspect + reconcile).
    /// </summary>
    public const string KvWritePartialFailure = "kvsecrets-kv-write-partial-failure";

    /// <summary>
    /// The KV writer failed on the very first entry with no prior writes.
    /// Resumable — no partial state; operator resolves KV connectivity /
    /// RBAC / throttle and resumes.
    /// </summary>
    public const string KvWriteFailedNoPartialState = "kvsecrets-kv-write-failed-no-partial-state";

    /// <summary>
    /// ADR-028 cleartext-secret-leak guard — the manifest or writer surface
    /// returned a value that looks secret-shaped where a KV URI reference or
    /// canonical name was expected. Quarantine-required (write-side data leak).
    /// </summary>
    public const string CleartextSecretLeak = "kvsecrets-cleartext-secret-leak";

    /// <summary>
    /// T1 silent-fail trap — App Service <c>keyVaultReferenceIdentity</c>
    /// PATCH shell-out (`az webapp update`) failed on the prod or staging
    /// slot. Quarantine-required (partial App Service state; downstream BFF
    /// boot would fail resolving KV refs).
    /// </summary>
    public const string TrapT1PatchFailed = "kvsecrets-trap-T1-patch-failed";

    /// <summary>
    /// T1 silent-fail trap post-condition — after PATCH, ARM read confirmed
    /// <c>keyVaultReferenceIdentity != UAMI</c> on prod or staging slot.
    /// Quarantine-required (patch failed silently or Bicep drift).
    /// </summary>
    public const string TrapT1VerificationMismatch = "kvsecrets-trap-T1-verification-mismatch";

    /// <summary>
    /// T5 interim mitigation failed — the KV Secrets User role grant against
    /// one or both slot System-Assigned MI principals failed. Resumable — the
    /// KV writes + T1 PATCH already succeeded; operator retries T5 or
    /// waits for post-Phase-C UAMI (structural fix). NON-fatal to the
    /// pipeline once the T5 trap is structurally impossible (uami.bicep
    /// task 028 + app-service.bicep task 029 land).
    /// </summary>
    public const string TrapT5GrantFailed = "kvsecrets-trap-T5-grant-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "kvsecrets-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H4 was in flight.</summary>
    public const string RunDeletedDuringPopulation = "kvsecrets-run-deleted-during-population";

    /// <summary>
    /// Row A38a — the positive secret-free migration marker (KV tag
    /// <c>spaarke-secret-free-identity</c> + registry
    /// <c>sprk_credentialmode</c>) could not be applied on a
    /// RequireSecretFreeIdentity environment. Resumable — writes/omits
    /// succeeded and the marker applier is idempotent, so the operator fixes
    /// the cause (tag RBAC / registry row / sprk_credentialmode column) and
    /// resumes. FAIL-LOUD by design: an unmarked secret-free vault is the
    /// remediation-plan §5.3 fleet-consistency gap.
    /// </summary>
    public const string SecretFreeMarkerApplyFailed = "kvsecrets-secret-free-marker-apply-failed";
}
