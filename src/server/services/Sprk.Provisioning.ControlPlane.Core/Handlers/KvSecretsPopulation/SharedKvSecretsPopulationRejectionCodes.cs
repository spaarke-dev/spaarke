// -----------------------------------------------------------------------------
// SharedKvSecretsPopulationRejectionCodes.cs
//
// Task 200 — machine-stable rejection codes emitted by
// H4SharedKvSecretsPopulationHandler. Parity with
// KvSecretsPopulationRejectionCodes.cs (task 047 H4) — one const per failure
// branch + lowercase kebab-case + `h4shared-*` prefix so operator UI can
// distinguish per-tenant H4 vs shared-tier H4-shared failures at a glance.
//
// STABILITY: strings are used by external tools; do NOT rename.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Machine-stable rejection codes for
/// <see cref="H4SharedKvSecretsPopulationHandler"/> failures.
/// </summary>
public static class SharedKvSecretsPopulationRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "h4shared-missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing — required to build ARM resource IDs for source services.</summary>
    public const string MissingSubscriptionId = "h4shared-missing-subscription-id";

    /// <summary>Run parameter <c>sharedKeyVaultName</c> missing — the target vault for all writes.</summary>
    public const string MissingSharedKeyVaultName = "h4shared-missing-shared-kv-name";

    /// <summary>Run parameter <c>sourceResourceGroupName</c> missing — the RG hosting the shared source services.</summary>
    public const string MissingSourceResourceGroupName = "h4shared-missing-source-resource-group";

    /// <summary>
    /// Run parameter <c>secretsVer</c> missing — feeds idempotency key
    /// kv-shared-{env}-{secretsVer}.
    /// </summary>
    public const string MissingSecretsVersion = "h4shared-missing-secrets-version";

    /// <summary>Run parameter <c>environmentName</c> missing — feeds idempotency key.</summary>
    public const string MissingEnvironmentName = "h4shared-missing-environment-name";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h4shared-run-not-found";

    /// <summary>Manifest reader threw / failed to load the canonical secret catalog. Resumable.</summary>
    public const string ManifestReadFailed = "h4shared-manifest-read-failed";

    /// <summary>
    /// BINDING pre-check violation — a from-shared-service entry has a
    /// canonical name in the never-delete set (Dataverse-ClientSecret /
    /// BFF-API-ClientSecret). QuarantineRequired.
    /// </summary>
    public const string BindingPreCheckViolation = "h4shared-binding-precheck-violation";

    /// <summary>
    /// A manifest entry's service_ref is malformed or missing (parser gate on
    /// the handler side). QuarantineRequired (write-side data-integrity).
    /// </summary>
    public const string InvalidServiceRef = "h4shared-invalid-service-ref";

    /// <summary>
    /// The source Azure service returned an error extracting the current value
    /// (403 forbidden, 404 not found, 429 throttle, etc.). Escalates per
    /// POML — root cause is typically a missing L2-UAMI RBAC assignment on
    /// the source (Bicep hardening follow-on). QuarantineRequired.
    /// </summary>
    public const string SourceServiceExtractionFailed = "h4shared-source-service-extraction-failed";

    /// <summary>
    /// The shared-KV read prerequisite failed with a non-NotFound error.
    /// QuarantineRequired.
    /// </summary>
    public const string SharedKvReadFailed = "h4shared-shared-kv-read-failed";

    /// <summary>
    /// One or more KV writes to the shared vault failed after prior writes
    /// succeeded — partial state. QuarantineRequired.
    /// </summary>
    public const string SharedKvWritePartialFailure = "h4shared-shared-kv-write-partial-failure";

    /// <summary>
    /// Post-condition probe (IArmKeyVaultRefProbe) verified that a canonical
    /// secret name written by H4-shared does NOT resolve via the target App
    /// Service's KV-ref binding (§4D I2 T1 trap). QuarantineRequired.
    /// </summary>
    public const string SharedSecretRefUnresolvable = "h4shared-shared-secret-ref-unresolvable";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h4shared-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H4-shared was in flight.</summary>
    public const string RunDeletedDuringPopulation = "h4shared-run-deleted-during-population";
}
