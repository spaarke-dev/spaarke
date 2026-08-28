// -----------------------------------------------------------------------------
// IE2EInvariantVerifier.cs
//
// L2 abstraction over the 5 §4D tenant-isolation invariant SAMPLE probes H13
// performs. Complements the compile-time enforcement (ArchTests I1–I5, r1
// task 064) with a RUNTIME sample assertion — belt-and-braces per POML notes.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls exist by design (production invariant verifier + test fakes per
//   unit test). One aggregate seam mirrors IE2ETrapVerifier's shape.
//
// LIVE-PROBE POSTURE:
//   I1 grep provisioning scripts on-disk for tenant-shaped GUID defaults (no
//     live dependency; deterministic).
//   I2 sample AI Search query per index — asserts the query URL/body carries
//     the unconditional tenantId eq filter (queries the customer's AI Search
//     endpoint if reachable; falls back to InfraFault if the endpoint is not
//     reachable from the test host).
//   I3 sample Cosmos query — asserts partition-key predicate present (probes
//     the customer's Cosmos endpoint).
//   I4 SPE container-ID resolution — asserts the customer's BFF resolves via
//     ITenantContainerResolver (queries a BFF diagnostic endpoint or the
//     runtime code path).
//   I5 Graph token acquisition — asserts the sample-request path includes an
//     explicit tenantId.
//   Same posture as trap verifier: per-probe infra faults surface as
//   InfraFault so the handler can classify Resumable vs QuarantineRequired.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Sample-verifies all 5 §4D tenant-isolation invariants (I1–I5). Production
/// impl issues live probes; test impls return canned results.
/// </summary>
public interface IE2EInvariantVerifier
{
    /// <summary>
    /// Runs all 5 invariant probes in a bounded fan-out and returns per-
    /// invariant outcomes. Individual probe failures do NOT throw — the handler
    /// decides how to react (Quarantined on any invariant failure per §4D
    /// CATASTROPHIC severity).
    /// </summary>
    Task<InvariantCatalogVerificationResult> VerifyAllAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single invariant-catalog verification pass. Immutable record.
/// </summary>
/// <param name="CustomerId">Customer id — probes scope to the customer stamp.</param>
/// <param name="RunId">RunId — for log correlation.</param>
/// <param name="TenantId">Explicit tenantId — probes MUST use this scope (§4D I1).</param>
/// <param name="SubscriptionId">Customer subscription id — probes scope to this subscription.</param>
/// <param name="AiSearchEndpoint">Customer AI Search endpoint URI (H2a output) for I2.</param>
/// <param name="CosmosEndpoint">Customer Cosmos endpoint URI (H2a output) for I3.</param>
/// <param name="BffApiUrl">BFF API URL for I4 (SPE container resolver diagnostic).</param>
/// <param name="ProvisioningScriptsDirectory">Path to <c>scripts/</c> on disk for I1 grep probe.</param>
public sealed record InvariantVerificationRequest(
    string CustomerId,
    string RunId,
    string TenantId,
    string SubscriptionId,
    string AiSearchEndpoint,
    string CosmosEndpoint,
    string BffApiUrl,
    string ProvisioningScriptsDirectory);

/// <summary>
/// The 5 §4D tenant-isolation invariants, enumerated (matches design.md §4D
/// verbatim).
/// </summary>
public enum InvariantKind
{
    /// <summary>I1 — no hardcoded tenant-shaped GUID defaults in provisioning scripts (FR-28).</summary>
    I1NoHardcodedTenant = 1,

    /// <summary>I2 — every AI Search query includes unconditional <c>tenantId eq</c> filter (FR-29).</summary>
    I2AiSearchTenantFilter = 2,

    /// <summary>I3 — every Cosmos read/write includes partition-key predicate (FR-30).</summary>
    I3CosmosPartitionKey = 3,

    /// <summary>I4 — SPE container IDs resolve via <c>ITenantContainerResolver</c> (FR-31).</summary>
    I4SpeContainerResolver = 4,

    /// <summary>I5 — Graph tokens are per-tenant scoped (FR-32).</summary>
    I5GraphTokenTenant = 5,
}

/// <summary>
/// Per-invariant outcome. Discriminated union: Passed / Failed / InfraFault.
/// </summary>
public abstract record InvariantVerificationOutcome
{
    private InvariantVerificationOutcome() { }

    /// <summary>Invariant sample probe ran and confirmed the invariant holds.</summary>
    public sealed record Passed(InvariantKind Kind) : InvariantVerificationOutcome;

    /// <summary>Invariant sample probe ran and confirmed the invariant is VIOLATED — QuarantineRequired per §4D CATASTROPHIC severity.</summary>
    /// <param name="Kind">Which invariant is violated.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic — MUST cite BOTH observed state AND expected state.</param>
    public sealed record Failed(InvariantKind Kind, string Diagnostic) : InvariantVerificationOutcome;

    /// <summary>Invariant sample probe could not conclusively run (endpoint unreachable, sample not exercisable from test host) — Resumable.</summary>
    /// <param name="Kind">Which invariant could not be verified.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic — MUST identify the specific infra fault.</param>
    public sealed record InfraFault(InvariantKind Kind, string Diagnostic) : InvariantVerificationOutcome;
}

/// <summary>
/// Aggregate result of an invariant-catalog verification pass. The list
/// contains exactly one entry per <see cref="InvariantKind"/>, in enum-
/// declaration order.
/// </summary>
public sealed record InvariantCatalogVerificationResult(IReadOnlyList<InvariantVerificationOutcome> Outcomes)
{
    /// <summary>True iff no invariant returned <see cref="InvariantVerificationOutcome.Failed"/>.</summary>
    public bool AllInvariantsClear => Outcomes.All(o => o is not InvariantVerificationOutcome.Failed);

    /// <summary>True iff at least one invariant could not be verified due to infra fault.</summary>
    public bool AnyInfraFault => Outcomes.Any(o => o is InvariantVerificationOutcome.InfraFault);

    /// <summary>The first FAILED invariant (if any).</summary>
    public InvariantVerificationOutcome.Failed? FirstFailure
        => Outcomes.OfType<InvariantVerificationOutcome.Failed>().FirstOrDefault();

    /// <summary>The first INFRA FAULT (if any).</summary>
    public InvariantVerificationOutcome.InfraFault? FirstInfraFault
        => Outcomes.OfType<InvariantVerificationOutcome.InfraFault>().FirstOrDefault();

    /// <summary>Convenience helper for logs: comma-separated <c>{Kind}={Status}</c> per invariant.</summary>
    public string ToLogSummary()
        => string.Join(", ", Outcomes.Select(o => o switch
        {
            InvariantVerificationOutcome.Passed p => $"{p.Kind}=Passed",
            InvariantVerificationOutcome.Failed f => $"{f.Kind}=Failed",
            InvariantVerificationOutcome.InfraFault i => $"{i.Kind}=InfraFault",
            _ => o.ToString() ?? "unknown",
        }));
}
