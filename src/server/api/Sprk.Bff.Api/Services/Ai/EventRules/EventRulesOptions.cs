namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// Platform-setting BOUNDS for the Event entry path (FR-P1-03 / NFR-09,
/// spaarke-ai-architecture-redesign-r1 task 022). Bound from the
/// <c>EventRules</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 boundary (load-bearing)</b>: these values are BOUNDS/POLICY, not
/// routing. WHICH capabilities fire on WHICH surface event lives exclusively in
/// <c>sprk_playbookconsumer.sprk_oneventbindings</c> (the Binding table — the
/// only routing surface). Nothing in this options class may ever name an event,
/// a capability, a consumer type, or a binding id — adding such a member would
/// recreate the "appsettings routing map" anti-pattern the audit found four of.
/// </para>
/// <para>
/// <b>Why bounds are config, not catalog data</b>: the daily cost cap and the
/// classify-confidence threshold are operator-owned safety dials (NFR-09 /
/// ADR-016 budget rules; canonical §7.1 "the surviving D1 dial"), not
/// maker-owned behavior. Per the task-022 POML, bounds MAY live in platform
/// settings; the Binding JSON column shapes (<c>[{event, order}]</c>) are pinned
/// by the task-003 column dictionary and are NOT extended here.
/// </para>
/// </remarks>
public sealed class EventRulesOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "EventRules";

    /// <summary>
    /// NFR-09 / ADR-016 — per-user daily Event-path budget, counted in capability
    /// executions (each event-rule member that reaches the prompted executor = 1).
    /// Execution count is the P1 cost proxy: every member is a single bounded LLM
    /// call, so the cap bounds spend linearly. When the remaining budget cannot
    /// cover the rule's member count, the rule defers gracefully with a chip
    /// (never a silent drop) and the denial is telemetered.
    /// </summary>
    public int DailyExecutionCap { get; set; } = 50;

    /// <summary>
    /// M4 classify-confidence policy threshold (canonical §7.1 / §3.10 — the
    /// surviving D1 dial). Classify-member confidence at or above this value lets
    /// the next member run silently; below it, the rule suspends into an M4
    /// confirmation turn ("This looks like an NDA — is that correct?") and the
    /// remaining members run only via the user's confirm chip (Click path).
    /// Default 0.85 per the canonical walkthrough (§3.10 step 3).
    /// </summary>
    public double ClassifyConfidenceThreshold { get; set; } = 0.85;

    /// <summary>
    /// TTL for the per-user opt-out marker in the tenant cache (days).
    /// The opt-out store is Redis-backed at P1 (see <see cref="EventPathUserState"/>
    /// remarks for the durability trade-off decision).
    /// </summary>
    public int OptOutTtlDays { get; set; } = 365;

    /// <summary>
    /// G-P1 UAT round-1 Defect 3 (2026-07-05): when the document-uploaded event
    /// arrives before every requested file id is visible in the session manifest
    /// (upload 202 → manifest write → cache propagation can lag the client's
    /// count-complete batch fire), the service re-reads the session up to this
    /// many times before degrading to the <c>no-attachments</c> notice.
    /// Semantics: wait-briefly-or-degrade — total added latency is bounded by
    /// <c>ReadinessProbeAttempts × ReadinessProbeDelayMs</c> (default ~5s).
    /// 0 disables the re-check (single read, previous behavior).
    /// </summary>
    public int ReadinessProbeAttempts { get; set; } = 5;

    /// <summary>Delay between manifest readiness probes (ms). See <see cref="ReadinessProbeAttempts"/>.</summary>
    public int ReadinessProbeDelayMs { get; set; } = 1000;
}
