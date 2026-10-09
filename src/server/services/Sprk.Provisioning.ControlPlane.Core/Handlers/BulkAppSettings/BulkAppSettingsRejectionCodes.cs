// -----------------------------------------------------------------------------
// BulkAppSettingsRejectionCodes.cs
//
// Task 201 — machine-stable rejection codes emitted by
// H4bBulkAppSettingsHandler. `h4b-*` prefix so operator UI can distinguish
// H4 vs H4b failures at a glance. STABILITY: strings are used
// by external tools; do NOT rename.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>Machine-stable rejection codes for <see cref="H4bBulkAppSettingsHandler"/> failures.</summary>
public static class BulkAppSettingsRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1).</summary>
    public const string MissingTenantId = "h4b-missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing.</summary>
    public const string MissingSubscriptionId = "h4b-missing-subscription-id";

    /// <summary><c>InterStepState.KeyVaultName</c> (H2a output) missing — the vault H4b's Key Vault references name.</summary>
    public const string MissingKeyVaultName = "h4b-missing-kv-name";

    /// <summary><c>InterStepState.ResourceGroupName</c> (H2a output) missing — the BFF App Service's resource group.</summary>
    public const string MissingResourceGroupName = "h4b-missing-resource-group";

    /// <summary><c>InterStepState.AppServiceName</c> (H2a output) missing — the App Service H4b writes + its /healthz probe URL.</summary>
    public const string MissingAppServiceName = "h4b-missing-app-service-name";


    // (h4b-missing-environment-name retired by task 245a: the stamp environment resolves through
    //  IntakeParameterCatalog.ResolveEnvironmentName — CreateRun stores it, default 'prod'.)

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h4b-run-not-found";

    /// <summary>Manifest reader threw / failed to load the per_env_settings list. Resumable.</summary>
    public const string ManifestReadFailed = "h4b-manifest-read-failed";

    /// <summary>
    /// A required per_env_settings entry's source (PerEnvSourceCatalog — a typed
    /// InterStepState output or an intake value) is absent. Resumable — the
    /// producing handler must complete (or the intake value be supplied) before
    /// H4b re-dispatches. Diagnostic names the source, where it lives, and the
    /// iOptionsModule for actionable operator triage.
    /// </summary>
    public const string PerEnvInputMissing = "h4b-per-env-input-missing";

    /// <summary>
    /// ARM refused (or could not be reached for) the app-settings merge on the
    /// production site or the staging slot (task 253 — formerly a non-zero exit
    /// of the generated Configure script). Resumable — the merge is idempotent,
    /// so a re-run converges once the cause is fixed. Diagnostic names the slot
    /// and the ARM error, never a setting value.
    /// </summary>
    public const string AppSettingsWriteFailed = "h4b-appsettings-write-failed";

    /// <summary>
    /// /healthz probe never returned 200 within the 8-min backoff budget. Diagnostic
    /// enriched with the parsed IOptions module name from container docker logs
    /// when parseable — otherwise a generic diagnostic pointing operator at the
    /// Kudu docker-log endpoint. QuarantineRequired (App Service is in
    /// half-configured state; new dispatch would compound).
    /// </summary>
    public const string HealthzTimeout = "h4b-healthz-timeout";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h4b-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H4b was in flight.</summary>
    public const string RunDeletedDuringPopulation = "h4b-run-deleted-during-population";
}
