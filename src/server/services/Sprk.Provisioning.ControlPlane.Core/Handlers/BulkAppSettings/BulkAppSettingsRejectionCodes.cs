// -----------------------------------------------------------------------------
// BulkAppSettingsRejectionCodes.cs
//
// Task 201 — machine-stable rejection codes emitted by
// H4bBulkAppSettingsHandler. `h4b-*` prefix so operator UI can distinguish
// H4 vs H4-shared vs H4b failures at a glance. STABILITY: strings are used
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

    /// <summary>Run parameter <c>keyVaultName</c> missing — needed for the generated script's -VaultName arg.</summary>
    public const string MissingKeyVaultName = "h4b-missing-kv-name";

    /// <summary>Run parameter <c>resourceGroupName</c> missing — needed for the generated script's -ResourceGroupName arg.</summary>
    public const string MissingResourceGroupName = "h4b-missing-resource-group";

    /// <summary>Run parameter <c>appServiceName</c> missing — needed for the generated script's -AppServiceName arg + /healthz probe URL.</summary>
    public const string MissingAppServiceName = "h4b-missing-app-service-name";

    /// <summary>Run parameter <c>secretsVer</c> missing — feeds idempotency key appsettings-{env}-{secretsVer}.</summary>
    public const string MissingSecretsVersion = "h4b-missing-secrets-version";

    /// <summary>Run parameter <c>environmentName</c> missing — feeds idempotency key.</summary>
    public const string MissingEnvironmentName = "h4b-missing-environment-name";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h4b-run-not-found";

    /// <summary>Manifest reader threw / failed to load the per_env_settings list. Resumable.</summary>
    public const string ManifestReadFailed = "h4b-manifest-read-failed";

    /// <summary>
    /// A required per_env_settings entry's source key is absent from
    /// envelope.Parameters.NonSecret. Resumable — upstream handler MUST populate
    /// the source before H4b re-dispatches. Diagnostic names the missing
    /// per_env_source + iOptionsModule for actionable operator triage.
    /// </summary>
    public const string PerEnvInputMissing = "h4b-per-env-input-missing";

    /// <summary>
    /// The generated Configure-AppServiceSettings.generated.ps1 shelled call
    /// returned a non-zero exit code. Resumable — writes are transactional per
    /// batched call; operator resolves the underlying cause + resumes. Diagnostic
    /// carries redacted stdout/stderr tail.
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
