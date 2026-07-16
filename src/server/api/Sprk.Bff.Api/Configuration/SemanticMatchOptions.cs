using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Association Engine's semantic-match rung (rung 4, FR-14). Bound from the
/// <c>Communication:SemanticMatch</c> section and consumed via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rung 4 runs hybrid (vector + keyword) search over the <c>spaarke-records-index</c> (matter / project /
/// invoice) via the <c>IRecordMatchingAi</c> facade when the deterministic rungs (0–3) did not auto-file,
/// and surfaces fuzzy candidates as <b>Suggested</b> — it NEVER auto-files (enforced by the mapper, which
/// computes auto-file eligibility from the deterministic-only reinforced confidence).
/// </para>
/// <para>
/// <b><see cref="ScoreCeiling"/> rationale:</b> semantic confidence is capped below the default auto-file
/// threshold (0.85) so a single strong semantic hit lands firmly in the Suggested band and two strong
/// semantic hits do not spuriously trip the ≥threshold conflict → Ambiguous rule at the default threshold.
/// The mapper's AI-never-auto-file guard is the load-bearing invariant; this ceiling is a secondary,
/// human-legible guard.
/// </para>
/// </remarks>
public class SemanticMatchOptions
{
    public const string SectionName = "Communication:SemanticMatch";

    /// <summary>
    /// Operational kill-switch. <c>true</c> (default) runs rung 4; <c>false</c> makes the rung a no-op
    /// (returns no matches) without a redeploy. The rung is registered unconditionally (ADR-010); this
    /// flag gates its behavior, so no Null-Object peer is required.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max results requested from the record search per evaluation (1–50, default 10).</summary>
    [Range(1, 50)]
    public int Limit { get; set; } = 10;

    /// <summary>Max candidate matches the rung emits per evaluation, across all record types (default 5).</summary>
    [Range(1, 50)]
    public int MaxCandidates { get; set; } = 5;

    /// <summary>
    /// Minimum confidence a hit must clear to be surfaced as a candidate (0–1, default 0.50).
    /// <c>RecordSearchService</c> normalizes the semantic reranker score (0–4) to 0–1, so 0.50 ≈ a
    /// reranker score of ~2.0 (a "decent" match). If a deployment lacks the semantic reranker the raw
    /// score is less calibrated and hits skew low — the rung then simply surfaces fewer candidates
    /// (fail-safe: rung 4 only ever proposes Suggested), and operators can lower this without a redeploy.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinScore { get; set; } = 0.50;

    /// <summary>
    /// Ceiling applied to each emitted candidate's confidence (0–1, default 0.80) — keeps semantic
    /// candidates in the Suggested band, below the default 0.85 auto-file threshold. See remarks.
    /// </summary>
    [Range(0.0, 1.0)]
    public double ScoreCeiling { get; set; } = 0.80;

    /// <summary>Max characters of the composed query (subject + body) sent to search (1–1000, default 1000).</summary>
    [Range(1, 1000)]
    public int MaxQueryChars { get; set; } = 1000;
}
