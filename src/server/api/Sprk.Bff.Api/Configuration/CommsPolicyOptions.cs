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
    /// threshold on the matched row always wins over this default. Range 0–1; default 0.45.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>0.45, not 0.35.</b> 0.35 was the first attempt and it would have notified on almost everything:
    /// the neutral urgency weight for a missing/unrecognised priority is 0.5, giving
    /// <c>0.7 × 0.5 = 0.35</c> — exactly at a 0.35 gate — and **242 of 270** communications in spaarkedev1
    /// carry no triage priority at all. A 0.35 gate therefore authorizes ~90% of mail, which is not
    /// recall-first, it is indiscriminate. At <b>0.45</b>: High-and-above always surfaces regardless of
    /// association (High+0 = 0.525); Medium and no-priority surface only when reasonably associated
    /// (agreement ≳ 0.33); Low needs near-perfect association. The $145k High-priority email that motivated
    /// this whole change scores 0.525 and surfaces.
    /// </para>
    /// <para>
    /// 🔴 <b>Lowered from 0.8 on 2026-09-29</b> (<c>spaarke-ontology-platform-r1</c>, owner decision: "we
    /// would rather screen out the noise and make adjustments than miss a flag"). Two reasons, and the first
    /// alone makes 0.8 wrong:
    /// <list type="number">
    ///   <item><b>0.8 became unreachable.</b> <see cref="Services.Communication.RiConfidenceScorer"/> changed
    ///     from a product to a weighted sum on the same date, whose maximum at <c>High</c> urgency is
    ///     <c>0.7×0.75 + 0.3×1.0 = 0.825</c> — and <c>0.795</c> at a realistic 0.90 agreement. A 0.8 gate would
    ///     have denied every High-priority email no matter how well associated; only <c>Urgent</c> could pass.</item>
    ///   <item><b>Recall over precision for NOTIFYING.</b> See the paragraph above for what 0.45 admits.</item>
    /// </list>
    /// This gate only decides whether to SURFACE a communication. It does not affect auto-filing, whose 0.85
    /// threshold in the association engine is untouched — filing writes data, where a false positive is worse
    /// than a miss.
    /// </remarks>
    public double DefaultConfidenceThreshold { get; set; } = 0.45;

    /// <summary>
    /// Fallback for the RI task's <c>sprk_duedate</c>, in days from now, when the matched
    /// <c>sprk_communicationrule</c> row does not set <c>sprk_taskduedays</c>. Default 1 (next day).
    /// </summary>
    /// <remarks>
    /// Added 2026-09-29 (owner decision). The due dates are **declared on the rule row** — this is only the
    /// fallback, exactly as <see cref="DefaultConfidenceThreshold"/> is for the threshold.
    /// <para>
    /// These are not cosmetic. <c>DailyBriefingCollector</c>'s task channels **filter by date** on
    /// <c>sprk_duedate</c> (alone since D-27; <c>sprk_finalduedate</c> is informational) — so a task created
    /// without it cannot appear in the briefing at all. Every RI task before this change had both null.
    /// </para>
    /// </remarks>
    public int DefaultTaskDueDays { get; set; } = 1;

    /// <summary>
    /// Fallback for the RI task's <c>sprk_finalduedate</c>, in days from now, when the matched rule does not
    /// set <c>sprk_taskfinalduedays</c>. Default 3 — the outer "must be done by" bound, where
    /// <see cref="DefaultTaskDueDays"/> is the target.
    /// </summary>
    public int DefaultTaskFinalDueDays { get; set; } = 3;
}
