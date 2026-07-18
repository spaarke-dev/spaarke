using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Association Engine's deterministic record-name/number match rung
/// (<see cref="Sprk.Bff.Api.Services.Communication.Engine.RungKind.RecordNameMatch"/>). Bound from the
/// <c>Communication:RecordNameMatch</c> section and consumed via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The rung retrieves candidate records from the <c>spaarke-records-index</c> (keyword ranking, no semantic
/// reranker) and then deterministically VERIFIES that a candidate's <b>name</b> or <b>reference number</b>
/// appears verbatim (normalized) in the email subject/body. It surfaces every verified record type (matter,
/// project, invoice) as a high-confidence review candidate but — per owner spec (2026-07-17) — NEVER
/// auto-files: the reviewer picks the primary. Precision is protected by <see cref="MinNameLength"/> +
/// <see cref="MinNameTokens"/> (so short/common record names cannot false-match).
/// </para>
/// </remarks>
public class RecordNameMatchOptions
{
    public const string SectionName = "Communication:RecordNameMatch";

    /// <summary>
    /// Operational kill-switch. <c>true</c> (default) runs the rung; <c>false</c> makes it a no-op without a
    /// redeploy. Registered unconditionally (ADR-010); this flag gates behavior.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max candidate records pulled from the index per evaluation before verification (1–50, default 25).</summary>
    [Range(1, 50)]
    public int Limit { get; set; } = 25;

    /// <summary>Max verified candidates the rung emits per evaluation, across all record types (default 10).</summary>
    [Range(1, 50)]
    public int MaxCandidates { get; set; } = 10;

    /// <summary>
    /// Minimum length (chars, after normalization) a record NAME must have to be eligible for containment
    /// matching. Guards against a short/common name false-matching arbitrary email text (default 5).
    /// </summary>
    [Range(1, 100)]
    public int MinNameLength { get; set; } = 5;

    /// <summary>
    /// Minimum token count a record NAME must have to be eligible for containment matching. A single-word
    /// common name (e.g. "Agreement") is too weak a signal for a deterministic match; the semantic rung still
    /// covers those fuzzily (default 2).
    /// </summary>
    [Range(1, 20)]
    public int MinNameTokens { get; set; } = 2;

    /// <summary>
    /// Minimum length (chars, alphanumeric-collapsed) a reference NUMBER must have to be eligible for
    /// containment matching. Guards against tiny numbers false-matching (default 4).
    /// </summary>
    [Range(1, 100)]
    public int MinNumberLength { get; set; } = 4;

    /// <summary>Confidence emitted for a verified NAME appearance (0–1, default 0.90). High — an exact, normalized name match — but below the 0.85 auto-file bar is NOT required here because the rung is excluded from auto-file eligibility in the mapper.</summary>
    [Range(0.0, 1.0)]
    public double NameConfidence { get; set; } = 0.90;

    /// <summary>Confidence emitted for a verified reference-NUMBER appearance (0–1, default 0.95) — a reference number is more discriminating than a name.</summary>
    [Range(0.0, 1.0)]
    public double NumberConfidence { get; set; } = 0.95;

    /// <summary>Max characters of the composed query (subject + body) sent to the index (1–1000, default 1000).</summary>
    [Range(1, 1000)]
    public int MaxQueryChars { get; set; } = 1000;
}
