// -----------------------------------------------------------------------------
// StubAiSearchTenantFilterTemplateProvisioner.cs
//
// Wave-C4 STUB production <see cref="IAiSearchTenantFilterTemplateProvisioner"/> —
// logs the intended template contents + returns Success. The template store
// target (Cosmos per-tenant doc / Search-Service scoring profile / BFF-owned
// template catalog) is a Wave C5+ decision per POML step 3 rationale.
//
// WHY A STUB IS THE RIGHT LEVEL FOR WAVE C4:
//   The H2b handler + tests validate the SEAM CONTRACT — that the
//   §4D I2 (FR-29) tenant-filter template is provisioned as part of Model 1
//   onboarding, that the request record carries the tenant id verbatim (never
//   defaults), that a template-provisioner failure is Resumable per §4C, and
//   that idempotency is asserted. Locking in the CONCRETE template store now
//   would prematurely commit us — Wave C5's reconciler + template consumers
//   need to be co-designed with the store.
//
// STRUCTURE-PRESERVING LOG:
//   The intended template literal predicate string is logged verbatim so an
//   auditor can grep production App Insights `traces` and prove the tenant
//   id was NOT defaulted (§4D I1 audit trail). Parity with H2a's runner
//   logging the deploy request.
//
// SWAP PATH (Wave C5+):
//   Replace this class with a REST-API or Cosmos-backed impl (e.g.
//   <c>CosmosTenantFilterTemplateProvisioner</c>). H2b + its tests do NOT
//   change — the seam contract is stable.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Wave-C4 stub — logs the intended template contents + returns Success.
/// See file header for swap-path rationale.
/// </summary>
public sealed class StubAiSearchTenantFilterTemplateProvisioner : IAiSearchTenantFilterTemplateProvisioner
{
    private readonly ILogger<StubAiSearchTenantFilterTemplateProvisioner> _logger;

    /// <summary>Constructs the wave-C4 stub.</summary>
    public StubAiSearchTenantFilterTemplateProvisioner(
        ILogger<StubAiSearchTenantFilterTemplateProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<AiSearchTenantFilterTemplateOutcome> ProvisionAsync(
        AiSearchTenantFilterTemplateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SearchEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);

        // Log the intended filter predicate verbatim so an audit-trail grep
        // can prove tenantId was explicit (§4D I1) — parity with H2a's
        // per-collaborator logging.
        var predicate = $"tenantId eq '{request.TenantId}'";
        _logger.LogInformation(
            "H2b tenant-filter template STUB — customerId={CustomerId} tenantId={TenantId} endpoint={Endpoint} " +
            "indexCount={IndexCount} filterPredicate={Predicate} " +
            "(wave-C4 stub — template store TBD; see file header)",
            request.CustomerId, request.TenantId, request.SearchEndpoint,
            request.IndexNames.Length, predicate);

        return Task.FromResult<AiSearchTenantFilterTemplateOutcome>(
            new AiSearchTenantFilterTemplateOutcome.Success());
    }
}
