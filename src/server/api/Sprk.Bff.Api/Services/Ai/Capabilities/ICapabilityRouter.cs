namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Three-tier capability router (AIPU2-012/013/014).
///
/// Routes an incoming user turn to one or more AI capabilities by applying layers
/// in priority order until a high-confidence result is produced:
///
///   Layer 1 — Keyword classifier (AIPU2-012): synchronous, zero LLM cost.
///   Layer 2 — LLM intent classifier (AIPU2-013): single GPT-4o-mini call.
///   Layer 3 — Broad superset fallback (AIPU2-014): synchronous, 0ms overhead.
///
/// ADR-015: implementations MUST NOT log or record user turn content.
/// </summary>
public interface ICapabilityRouter
{
    /// <summary>
    /// Runs Layer 1 keyword classification synchronously.
    ///
    /// NFR: must complete in under 50ms for up to 50 capabilities.
    /// No network I/O, no LLM calls — pure in-memory keyword matching.
    /// </summary>
    /// <param name="userMessage">The user turn text.</param>
    /// <param name="activePlaybookName">Optional active playbook name for bias.</param>
    /// <param name="intentHint">
    /// Optional closed-vocabulary soft-slash hint emitted by the frontend
    /// `SoftSlashRouter.decorateBody()` (`summarize` / `draft` /
    /// `extract-entities` / `analyze`). Retained for interface stability but
    /// IGNORED by the router as of Phase 5R task 116 / FR-20: the dict-based
    /// Layer 0.5 pre-pass has been removed because the same hint now biases
    /// the PlaybookDispatcher Phase B vector query (task 115) — slash and
    /// natural-language flows converge on the SAME path.
    /// Default null preserves backward compatibility for tests + legacy callers.
    /// </param>
    CapabilityRoutingResult RouteSync(string userMessage, string? activePlaybookName, string? intentHint = null);

    /// <summary>
    /// Full three-tier async routing: Layer 1 → Layer 2 (if uncertain) → Layer 3 fallback.
    ///
    /// Always returns a result — never throws for routing failures.
    /// </summary>
    /// <param name="userMessage">The user turn text.</param>
    /// <param name="activePlaybookName">Optional active playbook name for bias.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="intentHint">
    /// Optional soft-slash hint; see <see cref="RouteSync"/>. Retained for interface
    /// stability but IGNORED by the router (Phase 5R task 116 / FR-20). The same
    /// hint biases the PlaybookDispatcher Phase B vector query downstream.
    /// </param>
    Task<CapabilityRoutingResult> RouteAsync(
        string userMessage,
        string? activePlaybookName,
        CancellationToken ct = default,
        string? intentHint = null);

    /// <summary>
    /// Layer 3: synchronous broad superset fallback (AIPU2-014).
    ///
    /// Called when both Layer 1 and Layer 2 fail to classify.
    /// Never performs I/O — result is produced in under 1ms.
    /// OTEL: increments <c>ai_routing_layer3_hit</c> counter on every activation.
    /// </summary>
    CapabilityRoutingResult Layer3Fallback(string? activePlaybookName);
}
