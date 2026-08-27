// -----------------------------------------------------------------------------
// BicepDeployRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H2aBicepInfraDeployHandler (task 044).
// Every distinct failure mode gets its own code so the reconciler + operator UI
// can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-04 acceptance
//     (H2a): 15 resources per §7.2 deploy to expected names per §7.1 naming
//     convention; drift detection surfaces manual edits BEFORE apply.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-33 (T1
//     acceptance): App Service keyVaultReferenceIdentity == UAMI resource ID.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34 (upgrade
//     mode): defaults to REJECT + escalate on drift with report at
//     runNotes/drift-{customerId}-{timestamp}.md.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules:
//     Redis MUST NOT be provisioned per-customer (Q-E FR-12).
//   - projects/customer-provisioning-orchestration-r1/design.md §4B T1 —
//     ARM-verify keyVaultReferenceIdentity on both prod + staging slots.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback
//     taxonomy — mapping from rejection code to FailureClass.
//   - ADR-020 model deployment version pinning — no "latest" allowed.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Machine-stable rejection codes for <see cref="H2aBicepInfraDeployHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class BicepDeployRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing — H2a MUST know the target subscription (ADR-027 D4).</summary>
    public const string MissingSubscriptionId = "missing-subscription-id";

    /// <summary>Run parameter <c>bicepVer</c> missing — idempotency key requires the bicep-repo git SHA.</summary>
    public const string MissingBicepVersion = "missing-bicep-version";

    /// <summary>
    /// EXEC-04 (pre-dispatch audit 2026-08-27): the run's
    /// <see cref="Models.ProvisioningRun.TenancyModel"/> is blank / whitespace.
    /// H2a MUST fail fast (spec.md §4D I1 no-silent-default): a blank value
    /// silently deploying a per-customer Model 2 stack for a Model 1 shared
    /// trial would be a ~$400/mo/customer cost blow-up + tenancy invariant
    /// violation only detectable at H13. Wave 2 remediation.
    /// </summary>
    public const string MissingTenancyModel = "missing-tenancy-model";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// The active Bicep template contains a Redis resource (violates spec MUST
    /// rule + Q-E FR-12 v3.2 — Redis is per-environment via
    /// <c>scripts/Deploy-RedisCache.ps1</c>, NOT per-customer).
    /// </summary>
    public const string RedisProvisioningForbidden = "redis-provisioning-forbidden";

    /// <summary>
    /// The active Bicep template contains an Azure OpenAI model deployment
    /// version of <c>latest</c> or an unpinned/blank version literal (violates
    /// ADR-020). Model deployments MUST be pinned to specific versions
    /// (gpt-4o 2024-08-06, gpt-4o-mini 2024-07-18, text-embedding-3-large 1).
    /// </summary>
    public const string ModelVersionNotPinned = "model-version-not-pinned";

    /// <summary>
    /// Upgrade-mode drift detected — <c>az deployment group what-if</c>
    /// reported operator-modified resources. Handler REJECTED per §14A.5
    /// default; drift report written to <c>runNotes/drift-{customerId}-{timestamp}.md</c>.
    /// </summary>
    public const string UpgradeModeDrift = "upgrade-mode-drift";

    /// <summary>
    /// T1 silent-fail trap not cleared — App Service <c>keyVaultReferenceIdentity</c>
    /// on prod OR staging slot does NOT equal the deployed UAMI resource ID
    /// (spec.md FR-33). Bicep completed but the T1 post-condition failed;
    /// re-apply expected to fix.
    /// </summary>
    public const string TrapT1KeyVaultReferenceIdentityMismatch = "trap-T1-keyvault-reference-identity-mismatch";

    /// <summary>The invoked Bicep deploy runner reported a hard failure (non-zero exit / ARM error).</summary>
    public const string BicepDeployFailed = "bicep-deploy-failed";

    /// <summary>
    /// The Bicep deploy runner completed but did NOT populate the required
    /// output set (UAMI resource id, AppService prod/staging slot names,
    /// endpoint URIs). Configuration/template drift — operator must resolve.
    /// </summary>
    public const string BicepDeployOutputsIncomplete = "bicep-deploy-outputs-incomplete";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H2a was in flight.</summary>
    public const string RunDeletedDuringDeploy = "run-deleted-during-deploy";
}
