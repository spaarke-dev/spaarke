// -----------------------------------------------------------------------------
// IAiSearchIndexVerifier.cs
//
// L2 abstraction over the per-index invariant + presence verifier. After the
// provisioner runs, it asserts per-index invariants (required filterable +
// vector + forbidden-fields-absent) per scripts/ai-search/Deploy-AllIndexes.ps1
// Invoke-PostDeployVerifier (spec.md FR-05 acceptance). Both tenancy models use
// it the same way since task 225b (the Model 1 verify-only role on a shared
// platform service is retired with D-12).
//
// PRODUCTION IMPL:
//   <see cref="RestApiAiSearchIndexVerifier"/> calls the AI Search REST API
//   directly (GET /indexes/{name}?api-version=...) with DefaultAzureCredential
//   token-based auth (ADR-028 UAMI-outbound; no admin-key credential per
//   ADR-028 MUST rule + the search service must have `disableLocalAuth: true`
//   or accept AAD tokens).
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   ≥2 impls: production REST-API verifier + test stubs returning canned
//   <see cref="AiSearchIndexVerifyResult"/> outcomes.
//
// FAILURE MODES:
//   - AnyMissing:    IndexProvisioningFailed (provisioner succeeded but index
//                    absent = drift).
//   - AnyInvariant:  IndexInvariantViolation.
//   - Infra fault:   MAY throw; handler classifies as Resumable per §4C.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Verifies the presence + per-index invariants of the 7 canonical AI Search
/// indexes against a given search endpoint. Production impl calls the AI
/// Search REST API; test impls return canned outcomes.
/// </summary>
public interface IAiSearchIndexVerifier
{
    /// <summary>
    /// Fetches each expected index from the search endpoint + asserts the
    /// per-index invariants. Returns a typed result carrying any missing or
    /// invariant-violating names. Infra faults (auth / network) MAY throw so
    /// the handler classifies per §4C.
    /// </summary>
    Task<AiSearchIndexVerifyResult> VerifyAsync(
        AiSearchIndexVerifyRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single verifier invocation.
/// </summary>
/// <param name="SearchEndpoint">Target AI Search endpoint URI — the stamp's own dedicated search service in both models (D-12; the Model 1 shared-platform target is retired).</param>
/// <param name="ExpectedIndexNames">Canonical 7 by default; H2b passes the FULL canonical set for both branches.</param>
public sealed record AiSearchIndexVerifyRequest(
    string SearchEndpoint,
    ImmutableArray<string> ExpectedIndexNames);

/// <summary>
/// Discriminated result of <see cref="IAiSearchIndexVerifier.VerifyAsync"/>.
/// </summary>
public abstract record AiSearchIndexVerifyResult
{
    private AiSearchIndexVerifyResult() { }

    /// <summary>All expected indexes present with invariants intact. Handler proceeds.</summary>
    public sealed record Ok : AiSearchIndexVerifyResult;

    /// <summary>
    /// One or more expected indexes are MISSING from the search endpoint.
    /// <paramref name="MissingIndexNames"/> lists each. H2b maps it to
    /// <see cref="AiSearchIndexRejectionCodes.IndexProvisioningFailed"/>
    /// (post-provisioner drift).
    /// </summary>
    public sealed record Missing(ImmutableArray<string> MissingIndexNames) : AiSearchIndexVerifyResult;

    /// <summary>
    /// One or more expected indexes are present but violate their per-index
    /// invariants. Each violation names the failing index + field + reason
    /// so the operator diagnostic can be assembled without a second round-trip.
    /// Maps to <see cref="AiSearchIndexRejectionCodes.IndexInvariantViolation"/>.
    /// </summary>
    public sealed record InvariantViolation(ImmutableArray<IndexInvariantIssue> Issues) : AiSearchIndexVerifyResult;
}

/// <summary>
/// Single invariant-verifier violation. All three fields feed the
/// operator-facing diagnostic; keep them terse + machine-parseable.
/// </summary>
/// <param name="IndexName">Failing index (from the canonical 7).</param>
/// <param name="FieldName">Field whose invariant failed (e.g. <c>tenantId</c>, <c>contentVector3072</c>, <c>domain</c>).</param>
/// <param name="Reason">Human-readable reason (e.g. "required filterable field missing", "vector dim 1536 (expected 3072)", "forbidden field present").</param>
public sealed record IndexInvariantIssue(
    string IndexName,
    string FieldName,
    string Reason);
