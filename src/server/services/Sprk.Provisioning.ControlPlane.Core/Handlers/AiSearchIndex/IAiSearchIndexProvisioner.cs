// -----------------------------------------------------------------------------
// IAiSearchIndexProvisioner.cs
//
// L2 abstraction over the Model-2 AI Search index provisioning invocation. The
// production implementation (<see cref="DeployAllIndexesScriptProvisioner"/>)
// shells out to <c>scripts/ai-search/Deploy-AllIndexes.ps1</c> — the FR-05 /
// FR-07 catalog authority per task 002 audit. Unit tests inject stubs to
// avoid pwsh + real AI Search round-trips.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DeployAllIndexesScriptProvisioner"/> shells to
//       the PS script with DefaultAzureCredential-driven auth.
//     - Test: stubs injected per unit test that construct outcomes directly
//       (see H2bAiSearchIndexHandlerTests).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs SDK):
//   Same trade-off as H2a's IBicepDeployRunner: the PS script IS the FR-05 /
//   FR-07 catalog source-of-truth (audit § 1). Re-implementing in C# via
//   Azure.Search.Documents SDK would fork the catalog + require duplicate
//   invariant-verifier maintenance. H2b therefore WRAPS the script (adding
//   Model 1 branching + retired-name rejection + Cosmos state advancement the
//   script does not perform) rather than replacing it.
//
// MODEL 1 vs MODEL 2 SCOPE:
//   This provisioner runs ONLY for Model 2 (dedicated). The Model 1 branch of
//   H2b does NOT invoke a provisioner — it only VERIFIES presence (via
//   <see cref="IAiSearchIndexVerifier"/>) and provisions a per-tenant filter
//   template (via <see cref="IAiSearchTenantFilterTemplateProvisioner"/>) per
//   design.md §4.1a.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Executes the 7-index deploy for Model 2 (dedicated) AI Search services.
/// Production impl shells out to <c>scripts/ai-search/Deploy-AllIndexes.ps1</c>;
/// test impls return canned <see cref="AiSearchIndexProvisionOutcome"/>s.
/// </summary>
public interface IAiSearchIndexProvisioner
{
    /// <summary>
    /// Provisions the requested index set (defaults to the canonical 7 when
    /// unfiltered). Returns a typed outcome — success carries the
    /// provisioned-name list; failure carries a diagnostic. Domain failures
    /// do NOT throw (parity with <see cref="BicepInfraDeploy.IBicepDeployRunner"/>);
    /// infra faults MAY throw so the handler classifies per §4C.
    /// </summary>
    Task<AiSearchIndexProvisionOutcome> ProvisionAsync(
        AiSearchIndexProvisionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Model-2 index-provisioning invocation. Immutable record;
/// the caller (<see cref="H2bAiSearchIndexHandler"/>) constructs one per run
/// from <see cref="Models.ProvisioningRun.Parameters"/> +
/// <see cref="Models.InterStepState.AiSearchEndpoint"/> populated by H2a.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit, never default).</param>
/// <param name="EnvironmentName">Target environment (<c>dev</c> / <c>staging</c> / <c>prod</c> / <c>demo</c>) — the script's <c>-Environment</c> parameter.</param>
/// <param name="SearchEndpoint">Customer's dedicated AI Search endpoint URI from H2a's <see cref="Models.InterStepState.AiSearchEndpoint"/>. Empty for Model 1 (not used).</param>
/// <param name="RequestedIndexNames">Subset of canonical 7 to provision — empty means "all". Never contains retired names (H2b guard rejects earlier).</param>
/// <param name="IndexVersion">Manifest hash of the schema JSONs in <c>scripts/ai-search/</c> — feeds the idempotency key <c>aisearch-{customerId}-{indexVer}</c> per POML constraint.</param>
public sealed record AiSearchIndexProvisionRequest(
    string CustomerId,
    string TenantId,
    string EnvironmentName,
    string SearchEndpoint,
    ImmutableArray<string> RequestedIndexNames,
    string IndexVersion);

/// <summary>
/// Discriminated result of <see cref="IAiSearchIndexProvisioner.ProvisionAsync"/>.
/// Success carries the provisioned-name list (for verifier scoping); Failure
/// carries a diagnostic (later mapped to
/// <see cref="AiSearchIndexRejectionCodes.IndexProvisioningFailed"/> by the
/// handler).
/// </summary>
public abstract record AiSearchIndexProvisionOutcome
{
    private AiSearchIndexProvisionOutcome() { }

    /// <summary>Provisioning succeeded — <paramref name="ProvisionedIndexNames"/> is the exact set the verifier should assert against.</summary>
    public sealed record Success(ImmutableArray<string> ProvisionedIndexNames) : AiSearchIndexProvisionOutcome;

    /// <summary>
    /// Provisioning failed. <paramref name="Diagnostic"/> is the operator-facing
    /// message (e.g. "Deploy-AllIndexes.ps1 exit 7: PUT spaarke-files-index HTTP
    /// 400: unknown field 'foo'"). Handler wraps this in a
    /// <see cref="FailureClass.QuarantineRequired"/> §4C classification —
    /// partial deploys can leave a subset of indexes created.
    /// </summary>
    public sealed record Failure(string Diagnostic) : AiSearchIndexProvisionOutcome;
}
