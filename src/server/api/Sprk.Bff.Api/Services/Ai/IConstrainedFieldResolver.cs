namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Deterministic constrained-field resolver — the field-value analogue of ADR-039 capability grounding
/// (spec FR-B1). Given a proposed value for a closed, system-owned field (an option-set, or a lookup to a
/// reference entity), it resolves the value to its canonical option value / record id against Dataverse
/// metadata using a deterministic match ladder. The LLM NEVER emits the final closed-set value: it proposes
/// a human LABEL, and this resolver turns that label into the concrete value a wizard/form pre-selects.
/// </summary>
/// <remarks>
/// <para>
/// <b>No model call anywhere.</b> The ladder is exactly: exact (case-insensitive) → normalized
/// (trim / punctuation-fold / whitespace-collapse / configured synonyms) → fuzzy (similarity above a
/// configured threshold) → none. Confidence maps: exact/normalized → <see cref="ResolutionConfidence.High"/>
/// (caller pre-selects <see cref="ConstrainedFieldResolution.Resolved"/>); fuzzy →
/// <see cref="ResolutionConfidence.Low"/> (caller defaults a picker to the top candidate); no match →
/// <see cref="ResolutionConfidence.None"/> (caller shows the picker).
/// </para>
/// <para>
/// A field with no closed set (free text, number, date, …) or an unknown attribute returns
/// <see cref="ResolutionConfidence.None"/> with an empty candidate list and does NOT throw
/// (ADR-032 quiet no-op). Consumed by task 011 (constrained-field exclusion from LLM arg-filling) and
/// task 013 (smart pre-seed into the wizard hand-off envelope's <c>resolvedLookups</c> slot).
/// </para>
/// </remarks>
public interface IConstrainedFieldResolver
{
    /// <summary>
    /// Resolve a proposed closed-set value to its canonical option value / record id.
    /// </summary>
    /// <param name="entityLogicalName">The entity that owns the field (e.g. <c>sprk_matter</c>).</param>
    /// <param name="attributeLogicalName">The closed field's attribute logical name (e.g. <c>sprk_practicearea</c>).</param>
    /// <param name="proposedValue">The LLM- or user-proposed label to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConstrainedFieldResolution> ResolveAsync(
        string entityLogicalName,
        string attributeLogicalName,
        string proposedValue,
        CancellationToken cancellationToken = default);
}

/// <summary>Confidence of a constrained-field resolution.</summary>
public enum ResolutionConfidence
{
    /// <summary>No candidate matched — the caller shows a picker (candidates supplied for rendering).</summary>
    None = 0,

    /// <summary>Fuzzy match above the configured threshold — the caller defaults a picker to the top candidate.</summary>
    Low = 1,

    /// <summary>Exact or normalized match — the caller pre-selects <see cref="ConstrainedFieldResolution.Resolved"/>.</summary>
    High = 2,
}

/// <summary>
/// A single candidate from a closed field's valid set. <see cref="Value"/> is the option value (option-sets)
/// or record id GUID (lookups); <see cref="Label"/> is the display text the proposal is matched against.
/// </summary>
public sealed record FieldCandidate(string Value, string Label);

/// <summary>The outcome of resolving a proposed value against a closed field's valid set.</summary>
public sealed record ConstrainedFieldResolution
{
    /// <summary>
    /// The resolved option value / record id when <see cref="Confidence"/> is
    /// <see cref="ResolutionConfidence.High"/> or <see cref="ResolutionConfidence.Low"/> (the top candidate);
    /// <c>null</c> when <see cref="ResolutionConfidence.None"/>.
    /// </summary>
    public string? Resolved { get; init; }

    /// <summary>Confidence of the resolution — drives whether the caller pre-selects or shows a picker.</summary>
    public ResolutionConfidence Confidence { get; init; }

    /// <summary>
    /// The candidate set for picker rendering. For <see cref="ResolutionConfidence.Low"/> the resolved top
    /// candidate is first. Empty for a field with no closed set.
    /// </summary>
    public IReadOnlyList<FieldCandidate> Candidates { get; init; } = [];

    /// <summary>A no-match / no-closed-set result (ADR-032 quiet no-op).</summary>
    public static ConstrainedFieldResolution NoneResult(IReadOnlyList<FieldCandidate> candidates) =>
        new() { Resolved = null, Confidence = ResolutionConfidence.None, Candidates = candidates };
}

/// <summary>Deterministic tuning for the match ladder. All fields have safe defaults.</summary>
public sealed record ConstrainedFieldMatchOptions
{
    /// <summary>
    /// Minimum normalized similarity (0..1) for a fuzzy match to count as <see cref="ResolutionConfidence.Low"/>.
    /// Below this, the result is <see cref="ResolutionConfidence.None"/>. Default 0.82.
    /// </summary>
    public double FuzzyThreshold { get; init; } = 0.82;

    /// <summary>
    /// Optional normalized-input → canonical-normalized-form synonym map applied during the normalized tier
    /// (e.g. <c>"litigation and disputes"</c> → <c>"litigation"</c>). Keys and values must already be in
    /// normalized form (lowercase, punctuation-folded, whitespace-collapsed). Null/empty = no synonyms.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Synonyms { get; init; }
}
