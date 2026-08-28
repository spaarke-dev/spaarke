// -----------------------------------------------------------------------------
// IModelDeploymentReferenceWriter.cs
//
// Seam abstraction H12cRuntimeReferencesHandler uses to upsert
// sprk_aimodeldeployment runtime-reference rows against the customer's
// Dataverse environment. Interface-abstracted so unit tests substitute a
// fake (parity with every other Wave-C4/Cp handler's collaborator seams —
// H10's IDataverseAppUserCreator, H7's IEnvVarValuesWriter, etc.).
//
// SCOPE:
//   ONE writer call per HandleAsync invocation, upserting ALL 3
//   PinnedModelCatalog.Models rows in one batch. The write is check-then-
//   patch-or-create per model (find by sprk_name, PATCH if found, POST if
//   not) — safe to retry in full on ANY partial failure (parity with H7's
//   "every write is a natural upsert" reasoning — see H12cRuntimeReferences
//   Handler.cs file header ROLLBACK CLASSIFICATION table).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Writes/updates <c>sprk_aimodeldeployment</c> rows in a customer's target
/// Dataverse environment so each pinned model deployment points at the
/// correct Azure OpenAI endpoint for the customer's tenancy tier.
/// </summary>
public interface IModelDeploymentReferenceWriter
{
    /// <summary>
    /// Upserts every entry in <paramref name="request"/>.<see cref="ModelDeploymentReferenceWriteRequest.Deployments"/>
    /// against <paramref name="request"/>.<see cref="ModelDeploymentReferenceWriteRequest.DataverseEnvironmentUrl"/>.
    /// Find-by-<c>sprk_name</c>, PATCH if found, POST if not — idempotent.
    /// </summary>
    Task<ModelDeploymentReferenceWriteOutcome> UpsertAsync(
        ModelDeploymentReferenceWriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One <c>sprk_aimodeldeployment</c> row to upsert.
/// </summary>
/// <param name="ModelId">Written to BOTH <c>sprk_name</c> (lookup key) and <c>sprk_modelid</c>.</param>
/// <param name="Capability">Written to <c>sprk_capability</c>.</param>
/// <param name="EndpointUri">Written to <c>sprk_endpoint</c> — the Azure OpenAI resource endpoint (customer-dedicated for Model 2, shared-platform for Model 1).</param>
/// <param name="Description">Optional human-readable note written to <c>sprk_description</c> (e.g. Model 1's metering-attribution note — see H12cRuntimeReferencesHandler.cs "Model 1 metering attribution" remark).</param>
public sealed record ModelDeploymentReference(
    string ModelId,
    ModelCapability Capability,
    string EndpointUri,
    string? Description);

/// <summary>
/// Request to upsert a batch of <see cref="ModelDeploymentReference"/> rows
/// against one customer's Dataverse environment.
/// </summary>
/// <param name="DataverseEnvironmentUrl">Absolute base URL of the target Dataverse environment (e.g. <c>https://spaarke-acme.crm.dynamics.com</c>).</param>
/// <param name="TenantId">Explicit Entra tenant id for token acquisition (§4D I5 — never an ambient default-tenant credential).</param>
/// <param name="Provider">Written to every row's <c>sprk_provider</c> — <see cref="ModelProvider.AzureOpenAI"/> for r1's entire scope.</param>
/// <param name="Deployments">The rows to upsert (typically <see cref="PinnedModelCatalog.Models"/>, projected with an endpoint + description).</param>
public sealed record ModelDeploymentReferenceWriteRequest(
    string DataverseEnvironmentUrl,
    string TenantId,
    ModelProvider Provider,
    IReadOnlyList<ModelDeploymentReference> Deployments);

/// <summary>
/// Discriminated outcome of <see cref="IModelDeploymentReferenceWriter.UpsertAsync"/>.
/// </summary>
public abstract record ModelDeploymentReferenceWriteOutcome
{
    private ModelDeploymentReferenceWriteOutcome() { }

    /// <summary>Every row in the batch upserted successfully.</summary>
    /// <param name="UpsertedModelIds">The <c>sprk_name</c> values written, in request order — used for GateEntry evidence.</param>
    public sealed record Success(IReadOnlyList<string> UpsertedModelIds) : ModelDeploymentReferenceWriteOutcome;

    /// <summary>
    /// The batch failed (any row). The write is safe to retry in full — see
    /// the ROLLBACK CLASSIFICATION table in H12cRuntimeReferencesHandler.cs.
    /// </summary>
    public sealed record Failure(string Diagnostic) : ModelDeploymentReferenceWriteOutcome;
}
