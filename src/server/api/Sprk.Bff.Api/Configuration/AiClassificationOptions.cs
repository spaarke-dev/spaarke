using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Association Engine's AI extract+classify rung (rung 5, FR-15). Bound from the
/// <c>Communication:AiClassification</c> section and consumed via
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rung 5 runs an LLM extract+classify (via the <c>ICommunicationClassificationAi</c> facade) over the
/// normalized envelope when the deterministic rungs (0–3) AND the semantic rung (4) did not auto-file, and
/// emits <b>metadata-only signals</b> (category / obligations / rationale) — it resolves no regarding target,
/// so it NEVER auto-files and never forces a regarding write (FR-11 / ADR-045). Its value is the W5 triage
/// substrate (task 053).
/// </para>
/// <para>
/// <b><see cref="Enabled"/></b> is an operational kill-switch: the rung is registered unconditionally
/// (ADR-010) and self-gates on this flag (no redeploy). It complements the compound AI kill-switch that swaps
/// the facade to the Null-Object (ADR-032).
/// </para>
/// </remarks>
public class AiClassificationOptions
{
    public const string SectionName = "Communication:AiClassification";

    /// <summary>
    /// Operational kill-switch. <c>true</c> (default) runs rung 5; <c>false</c> makes the rung a no-op
    /// (returns no matches) without a redeploy.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Max candidate record TYPES the rung reflects into provenance from a single classification (1–20,
    /// default 5). Bounds an over-eager model; the classification's remaining fields are unaffected.
    /// </summary>
    [Range(1, 20)]
    public int MaxCandidateTypes { get; set; } = 5;

    /// <summary>
    /// Max characters of the composed classify input (subject + body) sent to the model (1–20000, default
    /// 8000). Bounds token cost per ADR-016; the body is truncated to fit.
    /// </summary>
    [Range(1, 20000)]
    public int MaxInputChars { get; set; } = 8000;
}
