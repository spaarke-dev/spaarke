using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.CitationVerification;

/// <summary>
/// Mechanical, zero-LLM citation verifier. Checks that each <see cref="EvidenceRef.Quote"/>
/// can be found (exactly or via sliding-window approximate match) in the supplied source chunks.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 deliverable <b>D-P9</b> per SPEC §3.1, decision <b>D-47</b> per decisions.md, and
/// LAVERN ADR 10.6 (citation verification standard). Mirrors the lavern source-code reference
/// (<c>src/mcp/tools/grounding-verifier.ts</c>): regex-extract quoted fragments → substring-match
/// against parsed document → fuzzy sliding-window fallback for paraphrase tolerance.
/// </para>
/// <para>
/// <b>Zone A placement</b> per ADR-013 / SPEC §3.5 — the verifier lives under
/// <c>Services/Ai/</c> so it can be DI-injected into Zone A node executors (D-P12
/// <c>GroundingVerifyNode</c>) and the Action Engine (LAVERN ADR 10.6 shared platform
/// primitive). The interface itself imports no AI internals; it is mechanical text matching.
/// </para>
/// <para>
/// <b>DoS bound</b>: per-citation source-chunk input over <see cref="MaxSourceChunkLength"/>
/// characters is rejected with an <c>InvalidInput</c> verdict. The cap mirrors the lavern
/// 10K-char protection and prevents quadratic worst-case from sliding-window scanning over
/// pathologically large inputs.
/// </para>
/// <para>
/// <b>Honesty contract enforcement</b>: closes the D-04 provenance-as-contract gap. Before
/// D-P9, the Insights envelope guaranteed only that <c>evidence[]</c> existed (shape level).
/// After D-P9, every quote-bearing citation is mechanically verified before persistence /
/// emission, so a hallucinated quote is detected at the boundary instead of leaking into
/// downstream Observations or Inferences.
/// </para>
/// </remarks>
public interface IGroundingVerifier
{
    /// <summary>
    /// Maximum length (in characters) of a single source-chunk input. Inputs above this
    /// threshold are rejected per-citation with <see cref="VerificationVerdict.InvalidInput"/>.
    /// Matches the lavern source default; aligns with chunk sizes produced by the existing
    /// <c>SemanticDocumentChunker</c> (typically &lt;2K chars per chunk).
    /// </summary>
    const int MaxSourceChunkLength = 10_000;

    /// <summary>
    /// Verifies each citation's <see cref="EvidenceRef.Quote"/> against the supplied source chunks.
    /// </summary>
    /// <param name="citations">
    /// The citations to verify. Citations whose <see cref="EvidenceRef.Quote"/> is null/empty
    /// are returned as <see cref="VerificationVerdict.NoQuote"/> (no claim to verify, not a
    /// failure — applies to <c>fact-source</c>, <c>comparable-matter</c>, etc. ref types).
    /// </param>
    /// <param name="sourceChunks">
    /// Candidate source chunks to match quotes against. Any chunk over
    /// <see cref="MaxSourceChunkLength"/> characters causes citations checked against it
    /// to return <see cref="VerificationVerdict.InvalidInput"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One <see cref="VerificationResult"/> per input citation, in the same order. Never null;
    /// length equals citations count.
    /// </returns>
    Task<IReadOnlyList<VerificationResult>> VerifyAsync(
        IEnumerable<EvidenceRef> citations,
        IEnumerable<ChunkRef> sourceChunks,
        CancellationToken cancellationToken);
}

/// <summary>
/// A source chunk against which citations may be verified. Typically projected from
/// <c>spaarke-files-index</c> chunks consumed by the D-P7 ingest pipeline.
/// </summary>
/// <param name="ChunkId">
/// Stable identifier for the chunk (e.g., <c>spe://drive/abc/item/xyz#chunk-3</c>).
/// Recorded on the matched <see cref="VerificationResult"/> for downstream attribution.
/// </param>
/// <param name="Text">The chunk text content. May be normalized (whitespace-collapsed) but should preserve substantive characters.</param>
public sealed record ChunkRef(string ChunkId, string Text);

/// <summary>
/// Outcome of verifying a single citation.
/// </summary>
public sealed record VerificationResult
{
    /// <summary>The citation that was verified.</summary>
    public required EvidenceRef Citation { get; init; }

    /// <summary>The verdict — see <see cref="VerificationVerdict"/>.</summary>
    public required VerificationVerdict Verdict { get; init; }

    /// <summary>
    /// Identifier of the source chunk that contained the match, if any.
    /// Populated for <see cref="VerificationVerdict.Verified"/> and
    /// <see cref="VerificationVerdict.VerifiedApproximate"/>; null otherwise.
    /// </summary>
    public string? MatchedChunkId { get; init; }

    /// <summary>
    /// Reason for the verdict — short human-readable explanation (e.g., "exact substring",
    /// "sliding-window match at window offset 50", "no overlap above threshold", "input too large").
    /// Used for diagnostics + downstream annotation by <c>GroundingVerifyNode</c>.
    /// </summary>
    public required string Reason { get; init; }
}

/// <summary>
/// The possible verification outcomes for a citation.
/// </summary>
public enum VerificationVerdict
{
    /// <summary>Exact substring match against a source chunk. The strongest verdict.</summary>
    Verified = 0,

    /// <summary>
    /// Approximate match via sliding-window overlap — the quote was found at high enough
    /// overlap within a window of one of the source chunks. Tolerates light paraphrase
    /// (word-order tweaks within the window) but not free-form rewrites.
    /// </summary>
    VerifiedApproximate = 1,

    /// <summary>
    /// The quote could not be located in any source chunk via either exact or approximate
    /// matching. Caller annotates as "[citation could not be verified]" per D-47 / LAVERN ADR 10.6.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// The citation has no <c>Quote</c> to verify (e.g., <c>fact-source</c> or
    /// <c>comparable-matter</c> ref types). Not a failure; just nothing to check.
    /// </summary>
    NoQuote = 3,

    /// <summary>
    /// Input was rejected (e.g., a source chunk exceeded <see cref="IGroundingVerifier.MaxSourceChunkLength"/>
    /// characters). DoS protection — treat like <see cref="NotFound"/> from a consumer perspective
    /// (annotate / strip) but log distinctly so operations can investigate runaway chunk sizes.
    /// </summary>
    InvalidInput = 4
}
