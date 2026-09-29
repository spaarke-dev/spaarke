namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Dedicated options surface for the comms Responsive-Intelligence policy gate (spec FR-12). Mirrors the
/// SHAPE of <c>EventRulesOptions.ClassifyConfidenceThreshold</c> as a dedicated dial — it deliberately does
/// NOT extend or share <c>EventRulesOptions</c>' bound configuration section (that seam is chat/SSE-shaped).
/// Bound from the <c>Communication:Policy</c> section.
/// </summary>
public sealed class CommsPolicyOptions
{
    public const string SectionName = "Communication:Policy";

    /// <summary>
    /// Fallback minimum confidence to AUTHORIZE an RI action when a matching
    /// <c>sprk_communicationrule</c> row does not set its own <c>sprk_confidencethreshold</c>. A per-rule
    /// threshold on the matched row always wins over this default. Range 0–1; default 0.35.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Lowered 0.8 → 0.35 on 2026-09-29</b> (<c>spaarke-ontology-platform-r1</c>, owner decision: "we
    /// would rather screen out the noise and make adjustments than miss a flag"). Two reasons, and the first
    /// alone makes 0.8 wrong:
    /// <list type="number">
    ///   <item><b>0.8 became unreachable.</b> <see cref="Services.Communication.RiConfidenceScorer"/> changed
    ///     from a product to a weighted sum on the same date, whose maximum at <c>High</c> urgency is
    ///     <c>0.7×0.75 + 0.3×1.0 = 0.825</c> — and <c>0.795</c> at a realistic 0.90 agreement. A 0.8 gate would
    ///     have denied every High-priority email no matter how well associated; only <c>Urgent</c> could pass.</item>
    ///   <item><b>Recall over precision for NOTIFYING.</b> At 0.35, everything Medium-and-above notifies
    ///     regardless of association, and Low notifies only when reasonably associated (agreement ≳ 0.58).</item>
    /// </list>
    /// This gate only decides whether to SURFACE a communication. It does not affect auto-filing, whose 0.85
    /// threshold in the association engine is untouched — filing writes data, where a false positive is worse
    /// than a miss.
    /// </remarks>
    public double DefaultConfidenceThreshold { get; set; } = 0.35;
}
