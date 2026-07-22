namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Policy dial for the proactive-suggestion gate (spec FR-15 / NFR-03). The ADR-041 "one
/// confirmation gate" is chat/SSE-shaped (<c>PendingPlanManager</c> — request-scoped, Redis
/// suspend/resume, needs a live <c>ChatSession</c>); a batch Daily-Briefing producer has no
/// session, so — per the task-041 precedent (owner decision 2026-07-22) — the producer reuses
/// the gate DISCIPLINE (a declared-metadata admit decision tagged <c>origin=proactive</c>) via
/// this options dial rather than re-entering that chat machinery. The emitted outbox row
/// REPRESENTS the pending confirmation; the actual user confirm is downstream (renderer 051 →
/// dispatch 052).
/// </summary>
public sealed class SuggestionGateOptions
{
    public const string SectionName = "Notifications:Suggestions";

    /// <summary>
    /// The proactive kill-switch. Default <c>false</c> — deny-by-default (mirrors task 041's
    /// confidence-0 posture): the spine carries NO proactive suggestion until an operator
    /// explicitly enables it (NFR-03 — nothing ungated reaches the spine, and nothing at all
    /// until turned on). ADR-032 degrade discipline.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Max <c>kind=suggestion</c> rows written per briefing run (volume cap so a busy
    /// briefing does not flood the spine). Default 3.</summary>
    public int MaxPerRun { get; set; } = 3;

    /// <summary>How long an emitted suggestion stays live before the outbox read-time expiry filter
    /// drops it (task 012). Default 24h.</summary>
    public int TtlHours { get; set; } = 24;
}
