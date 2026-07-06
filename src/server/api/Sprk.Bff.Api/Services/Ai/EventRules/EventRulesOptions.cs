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
}
