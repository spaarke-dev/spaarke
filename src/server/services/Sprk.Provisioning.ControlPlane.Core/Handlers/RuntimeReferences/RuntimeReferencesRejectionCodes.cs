// -----------------------------------------------------------------------------
// RuntimeReferencesRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H12cRuntimeReferencesHandler on
// HandlerResult.Failure. Lowercase kebab-case (parity with
// BicepDeployRejectionCodes / AppConfigSeedRejectionCodes) for greppability +
// runbook lookup without parsing English diagnostics.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-17 (H12c
//     acceptance).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a (Model 1
//     vs Model 2 endpoint-source branch) + §4C rollback (see
//     H12cRuntimeReferencesHandler.cs file header ROLLBACK CLASSIFICATION
//     table — every H12c failure mode is Resumable).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Stable rejection codes returned by <c>H12cRuntimeReferencesHandler</c> on
/// <c>HandlerResult.Failure</c>.
/// </summary>
public static class RuntimeReferencesRejectionCodes
{
    /// <summary>Cosmos partition read returned no matching ProvisioningRun for (customerId, runId).</summary>
    public const string RunNotFound = "runtimerefs-run-not-found";

    /// <summary><c>run.Parameters.NonSecret["tenantId"]</c> was null / whitespace (§4D I1 — no default-tenant fallback).</summary>
    public const string MissingTenantId = "runtimerefs-missing-tenant-id";

    /// <summary>
    /// <c>run.InterStepState.DataverseEnvUrl</c> was null/blank — H5/H6 (Dataverse
    /// env creation + solution import) MUST complete before H12c dispatches.
    /// </summary>
    public const string MissingDataverseUrl = "runtimerefs-missing-dataverse-url";

    /// <summary>
    /// <c>run.CompletedPhases</c> is missing an H12a and/or H12b entry. Per
    /// design.md §4.1 DAG: "H12c (runtime refs — needs both H12a + H12b +
    /// H2a OpenAI)". Diagnostic names exactly which upstream handler(s) are
    /// missing.
    /// </summary>
    public const string MissingUpstreamHandlers = "runtimerefs-missing-upstream-handlers";

    /// <summary><c>run.TenancyModel</c> is neither <c>Model1Shared</c> nor <c>Model2Dedicated</c>. Handler does NOT upsert on this branch.</summary>
    public const string UnknownTenancyModel = "runtimerefs-unknown-tenancy-model";

    /// <summary>
    /// Model2Dedicated branch: <c>run.InterStepState.OpenAiEndpoint</c> was
    /// null/blank — H2a (task 044) MUST complete + populate interStepState
    /// before H12c dispatches on a dedicated-tier customer.
    /// </summary>
    public const string MissingOpenAiEndpoint = "runtimerefs-missing-openai-endpoint";

    /// <summary>
    /// Model1Shared branch: <c>RuntimeReferencesOptions.SharedPlatformOpenAiEndpoint</c>
    /// is not configured. Operator/infra issue, not a per-customer condition —
    /// blocks EVERY Model1Shared run in this environment until set.
    /// </summary>
    public const string MissingSharedPlatformEndpointConfiguration = "runtimerefs-missing-shared-platform-endpoint-configuration";

    /// <summary>
    /// <see cref="IModelDeploymentReferenceWriter.UpsertAsync"/> returned
    /// <see cref="ModelDeploymentReferenceWriteOutcome.Failure"/> or threw.
    /// Safe to retry in full (see ROLLBACK CLASSIFICATION table).
    /// </summary>
    public const string ModelDeploymentWriteFailed = "runtimerefs-model-deployment-write-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "runtimerefs-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H12c was in flight.</summary>
    public const string RunDeletedDuringWrite = "runtimerefs-run-deleted-during-write";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H12c. Kept as string constants so grep across the codebase finds every
/// read/write of the same gate name (design.md §6.2).
/// </summary>
public static class RuntimeReferencesGates
{
    /// <summary>
    /// The gate H12c owns for the overall runtime-reference-write outcome —
    /// flipped from <c>Pending</c> to <c>Verified</c> once all 3
    /// <see cref="PinnedModelCatalog.Models"/> rows upsert successfully.
    /// </summary>
    public const string RuntimeReferencesWritten = "runtime-references-written";
}
