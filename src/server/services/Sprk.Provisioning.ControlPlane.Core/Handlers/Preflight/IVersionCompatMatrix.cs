// -----------------------------------------------------------------------------
// IVersionCompatMatrix.cs
//
// H0 upgrade-mode version-compatibility matrix query seam (Wave G-8 Batch 10 —
// closes FR-34 defect #24: the matrix doc existed but H0 never queried it).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34:
//       "H0 preflight (upgrade mode) reads sprk_bffversion + sprk_solutionversion
//       from registry, queries docs/deployment/version-compatibility-matrix.md,
//       blocks incompatible pairs (Red)."
//   - docs/deployment/version-compatibility-matrix.md §2 query semantics:
//       verdict = matrix cell (target BFF version, target Solution-set version).
//       Green → proceed; Yellow → allow with operator manual-step guidance
//       (U-CB-N class per §5); Red → block, requires intermediate release.
//   - design.md §14A.3 — two-dimensional matrix (BFF version × aggregate
//       Solution-set version), legend verbatim in the doc above.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11):
//   ≥2 impls by design: JsonFileVersionCompatMatrix (production — loads the
//   embedded/on-disk JSON mirror of the authoritative doc) + per-test fakes
//   in H0PreflightHandlerTests (verdict-forcing stubs so the H0 orchestration
//   branch is exercised without file IO).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// Compatibility verdict for a (BFF version, Solution-set version) matrix cell.
/// Mirrors the legend in <c>docs/deployment/version-compatibility-matrix.md</c> §4
/// (verbatim from design.md §14A.3).
/// </summary>
public enum VersionCompatVerdict
{
    /// <summary>Compatible; upgrade proceeds normally (either component can lead).</summary>
    Green,

    /// <summary>
    /// Compatible but requires an operator manual step + customer-comms
    /// notification (U-CB-N class per matrix doc §5). H0 warns + records a
    /// Pending gate entry but does NOT block the run.
    /// </summary>
    Yellow,

    /// <summary>
    /// Incompatible — do NOT deploy this pair. H0 fails the run fast
    /// (before any quota probe fires); the customer requires an intermediate
    /// release first.
    /// </summary>
    Red,
}

/// <summary>
/// A (BFF version, Solution-set version) pair. BFF version format per matrix
/// doc §3.1 (e.g. <c>1.0.0-net10</c>); Solution-set version per §3.2
/// (e.g. <c>S2026.08</c>). Comparison is case-insensitive at the matrix impl.
/// </summary>
/// <param name="BffVersion">BFF binary version (registry column <c>sprk_bffversion</c> / release manifest).</param>
/// <param name="SolutionVersion">Aggregate Solution-set version (registry column <c>sprk_solutionversion</c> / release manifest).</param>
public sealed record VersionPair(string BffVersion, string SolutionVersion);

/// <summary>
/// Outcome of a matrix lookup for one upgrade attempt.
/// </summary>
/// <param name="Verdict">Cell verdict per the matrix legend. A target pair NOT present in the matrix is reported as <see cref="VersionCompatVerdict.Red"/> (unknown pair = unsupported until the release manager appends the cell per matrix doc §6).</param>
/// <param name="Diagnostic">Operator-facing diagnostic: cites the current + target pairs, the verdict, and (for Yellow/Red) the remediation pointer into the matrix doc.</param>
/// <param name="UcbClasses">U-CB-N breaking-change classes attached to the cell (matrix doc §5), e.g. <c>["U-CB-3"]</c>. Empty for Green and for unknown pairs.</param>
public sealed record VersionCompatCheckResult(
    VersionCompatVerdict Verdict,
    string Diagnostic,
    IReadOnlyList<string> UcbClasses);

/// <summary>
/// Query surface over the version-compatibility matrix
/// (<c>docs/deployment/version-compatibility-matrix.md</c> — runtime mirror at
/// <c>Handlers/Preflight/version-compat-matrix.json</c>). Consumed by
/// <see cref="H0PreflightHandler"/> in upgrade mode ONLY (spec.md FR-34).
/// </summary>
public interface IVersionCompatMatrix
{
    /// <summary>
    /// Looks up the verdict for upgrading a customer from <paramref name="current"/>
    /// to <paramref name="target"/>. Per matrix doc §2 the verdict is the cell at
    /// (target BFF, target Solution-set); <paramref name="current"/> flows into
    /// the diagnostic (and an unknown CURRENT pair is annotated as a warning in
    /// the diagnostic without changing the verdict).
    /// </summary>
    /// <param name="current">The customer's currently-bound versions (registry-mirrored run parameters).</param>
    /// <param name="target">The incoming release's versions (release manifest).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict + diagnostic. Never throws on a domain outcome (unknown pair = Red result); throws only on matrix-source infrastructure faults (missing/corrupt matrix file), which H0 classifies as Resumable.</returns>
    Task<VersionCompatCheckResult> CheckPairAsync(
        VersionPair current,
        VersionPair target,
        CancellationToken cancellationToken);
}
