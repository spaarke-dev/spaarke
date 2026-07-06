namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// A suspended side-effecting invocation awaiting user confirmation — the generalized
/// entry shape of THE unified pending store (<c>PendingPlanManager</c>), per design
/// decision D12 / FR-P2-02 of <c>spaarke-ai-architecture-redesign-r1</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE gate (ADR-039)</b>: every suspended side-effecting invocation — loop write-tool
/// calls, must-click capability confirmations (FR-48), compound plans — suspends into the
/// unified store as one of these entries (or as the plan-shaped <see cref="PendingPlan"/>,
/// which is a presentation variant of the same gate). Gating decisions are driven by the
/// tool's declared <c>sprk_sideeffectclass</c> plus the Binding's <c>sprk_risk</c> —
/// never by tool-name lists.
/// </para>
/// <para>
/// <b>Ledger correlation (ADR-040)</b>: <see cref="GateId"/> is the correlation key with
/// the session ledger's <c>SessionGate</c> entries. The pending marker is written to the
/// ledger BEFORE any gate UI renders; resolution (confirmed / rejected) is a NEW ledger
/// entry with the same <see cref="GateId"/> (append-only rule).
/// </para>
/// <para>
/// <b>Storage</b>: Redis via <c>ITenantCache</c> (tenant-scoped keys per ADR-014),
/// 30-minute absolute TTL — the confirmation window is an interactive gate, not
/// long-lived state. <see cref="ArgsJson"/> carries the invocation arguments needed to
/// resume execution after confirmation; it lives ONLY in this Tier-3 session-state store
/// (ADR-015) and is never logged (NFR-07 — logs carry identifiers only).
/// </para>
/// </remarks>
public sealed record PendingInvocation
{
    /// <summary>Stable gate id correlating this store entry with its ledger <c>SessionGate</c> markers.</summary>
    public required string GateId { get; init; }

    /// <summary>Session the invocation was suspended in.</summary>
    public required string SessionId { get; init; }

    /// <summary>Tenant id — part of the Redis cache key (ADR-014 isolation).</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Identifier of the suspended tool / capability (namespaced <c>sprk_analysistool</c>
    /// id or LLM function name). Identifier only — display strings belong to presentation.
    /// </summary>
    public required string ToolId { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id the invocation targets, when dispatched via a Binding.</summary>
    public string? BindingId { get; init; }

    /// <summary>
    /// Declared side-effect class that drove the suspension
    /// (<c>read | write | communicate | pure</c> — ledger vocabulary). Null when the gate
    /// fired on Binding risk alone (e.g. <c>always-confirm</c> on an informational capability).
    /// </summary>
    public string? SideEffectClass { get; init; }

    /// <summary>
    /// Binding risk posture at suspension time (<c>none | confirm-when-uncertain | always-confirm</c>),
    /// recorded for audit; null when no Binding was involved.
    /// </summary>
    public string? Risk { get; init; }

    /// <summary>
    /// Invocation arguments as JSON, preserved so the invocation can resume after user
    /// confirmation without re-running the LLM. Store-only payload (Tier 3, ADR-015);
    /// MUST NOT be logged (NFR-07).
    /// </summary>
    public string ArgsJson { get; init; } = "{}";

    /// <summary>Human-readable title for the confirmation presentation (e.g. "Create matter record").</summary>
    public string? Title { get; init; }

    /// <summary>1-based session turn the invocation was suspended on (mirrors the ledger Gate entry's turn).</summary>
    public int Turn { get; init; }

    /// <summary>UTC timestamp the invocation was suspended.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
