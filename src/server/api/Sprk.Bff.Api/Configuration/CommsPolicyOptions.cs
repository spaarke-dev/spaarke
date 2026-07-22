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
    /// threshold on the matched row always wins over this default. Range 0–1; default 0.8.
    /// </summary>
    public double DefaultConfidenceThreshold { get; set; } = 0.8;
}
