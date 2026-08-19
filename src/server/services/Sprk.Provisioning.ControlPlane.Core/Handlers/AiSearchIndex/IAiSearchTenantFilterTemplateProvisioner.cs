// -----------------------------------------------------------------------------
// IAiSearchTenantFilterTemplateProvisioner.cs
//
// L2 abstraction over the Model-1 per-tenant filter-template provisioner. This
// is the ONLY handler in the pipeline whose responsibility is to enforce the
// §4D I2 (FR-29) tenant-isolation invariant at PROVISIONING time — every
// Model 1 tenant onboarded MUST have a per-tenant template that binds
// `tenantId eq '{ctx.TenantId}'` as a mandatory query filter across all 7
// shared indexes. If this handler doesn't create the template, downstream BFF
// AI Search calls have no template to enforce the filter through and
// cross-tenant document bleed becomes possible (design.md §4D I2 severity:
// CATASTROPHIC — legal privilege leak).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-29 (I2):
//     unconditional `tenantId eq` filter on every AI Search query.
//   - projects/customer-provisioning-orchestration-r1/design.md §4D I2:
//     enforcement mechanism includes the per-tenant template provisioned at
//     Model 1 tenant onboarding.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a:
//     Model 1 H2b "provisions per-tenant `tenantId`-filter query template
//     (does NOT re-create indexes)".
//   - ADR-014: session-isolation invariant (spaarke-session-files carries
//     the canonical tenantId + sessionId dual-filter — H2b's template MUST
//     strengthen this, never weaken).
//
// PRODUCTION IMPL (task 124, Wave G-2):
//   <see cref="AiSearchTenantFilterTemplateProvisioner"/> REPLACES the
//   wave-C4 <c>StubAiSearchTenantFilterTemplateProvisioner</c> (which only
//   logged the intended template + returned Success — DS-4 §2 "I2
//   provisioning half not real"). The template-store target is Cosmos (a
//   NEW per-tenant document in a NEW, TTL-less `tenantFilterTemplates`
//   container on the SAME shared platform Cosmos account L2 already owns —
//   see AiSearchTenantFilterTemplateProvisioner.cs's header for the full
//   candidate-comparison rationale). The seam contract is unchanged: given
//   (SearchEndpoint, TenantId, CustomerId, IndexNames), provision a
//   per-tenant artifact whose contents include the literal filter predicate
//   `tenantId eq '{TenantId}'` for each index in the list. Second call with
//   same inputs = idempotent no-op (verified by content-comparison, not
//   merely PUT-safe overwrite). The handler + its tests are UNCHANGED by
//   this swap — proof the seam was designed correctly in wave C4.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   ≥2 impls: production (<see cref="AiSearchTenantFilterTemplateProvisioner"/>,
//   Cosmos-backed) + test stubs (per-test canned outcomes, see
//   H2bAiSearchIndexHandlerTests). Interface earns its keep.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Provisions a per-tenant AI Search filter query template binding
/// <c>tenantId eq '{ctx.TenantId}'</c> as a mandatory filter across the 7
/// canonical shared indexes (Model 1 branch only). Enforces §4D I2 / FR-29
/// at provisioning time.
/// </summary>
public interface IAiSearchTenantFilterTemplateProvisioner
{
    /// <summary>
    /// Provisions the per-tenant filter template. Idempotent — second call
    /// with the same inputs is a no-op (§4C Retryable-with-cleanup class).
    /// Domain failures do NOT throw; infra faults MAY throw so the handler
    /// classifies as Resumable per §4C.
    /// </summary>
    Task<AiSearchTenantFilterTemplateOutcome> ProvisionAsync(
        AiSearchTenantFilterTemplateRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a per-tenant filter-template provisioning invocation.
/// </summary>
/// <param name="SearchEndpoint">Shared platform AI Search endpoint (Model 1 only).</param>
/// <param name="TenantId">Entra tenant id whose value is embedded verbatim in the filter predicate — §4D I1 forbids default fallback.</param>
/// <param name="CustomerId">Customer partition key — template store key includes this to keep templates unique per-customer even if two tenants collide.</param>
/// <param name="IndexNames">Canonical 7 by default — template applies the filter across all of them.</param>
public sealed record AiSearchTenantFilterTemplateRequest(
    string SearchEndpoint,
    string TenantId,
    string CustomerId,
    ImmutableArray<string> IndexNames);

/// <summary>
/// Discriminated result of <see cref="IAiSearchTenantFilterTemplateProvisioner.ProvisionAsync"/>.
/// </summary>
public abstract record AiSearchTenantFilterTemplateOutcome
{
    private AiSearchTenantFilterTemplateOutcome() { }

    /// <summary>Template provisioned (or already present at same version — PUT semantics).</summary>
    public sealed record Success : AiSearchTenantFilterTemplateOutcome;

    /// <summary>
    /// Provisioning failed. <paramref name="Diagnostic"/> feeds the operator
    /// message. Handler wraps in a <see cref="FailureClass.Resumable"/> §4C
    /// classification because the PUT is idempotent and retry-safe.
    /// </summary>
    public sealed record Failure(string Diagnostic) : AiSearchTenantFilterTemplateOutcome;
}
