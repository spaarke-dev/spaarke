using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// The THIN Event entry path (ADR-039 path 1 of 3; FR-P1-03,
/// spaarke-ai-architecture-redesign-r1 task 022): resolves a surface event to
/// the ordered capability invocations declared in
/// <c>sprk_playbookconsumer.sprk_oneventbindings</c> and executes them through
/// the canonical seams — prompted executor (<c>IActionRunner</c>) and OutputRouter
/// (ADR-040 write-before-render) — under the FR-P1-03 bounds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thinness invariant (design.md / task 022)</b>: this service contains ZERO
/// capability logic. It (1) checks preconditions + bounds, (2) reads the Binding
/// table's event memberships via
/// <see cref="PublicContracts.IConsumerRoutingService.ResolveEventBindingsAsync"/>,
/// (3) executes members in declared order through ActionRunner → OutputRouter, and
/// (4) applies the event-rule M4 confidence policy between members. What each
/// member DOES lives entirely in its Action row (prompt + schemas).
/// </para>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented)</b>: the interface exists
/// for the ADR-032 Null-Object kill-switch peer (<see cref="NullEventRulesService"/>)
/// — the event endpoint maps unconditionally while the real implementation's DI
/// graph (ActionRunner / ScopeResolver / OutputRouter) is compound-AI-gated.
/// Same justification as the dispatch seam's Null subclass;
/// interface chosen over subclass because no protected-ctor state machine is
/// needed here.
/// </para>
/// <para>
/// <b>CLAUDE.md §11 three-question justification (new surface)</b>:
/// (1) <i>Existing</i> — overlaps nothing: grep shows no upload auto-analysis path
/// on master (the r7 dispatch patches were dropped un-merged, task 025); the only
/// event-shaped config surface is <c>sprk_oneventbindings</c>, which this reads.
/// (2) <i>Extension</i> — cannot extend an existing service: the summarize boundary
/// is a single-capability chat boundary; the Event path is multi-member, ordered,
/// bounded, and capability-agnostic. The Binding QUERY was implemented as an
/// extension of the existing <c>IConsumerRoutingService</c> rather than a new query
/// path. (3) <i>Cost-of-doing-nothing</i> — the G-P1 headline behavior (upload,
/// type nothing, see classification + summary + chips) has no server entry point;
/// FR-P1-03 acceptance fails.
/// </para>
/// </remarks>
public interface IEventRulesService
{
    /// <summary>
    /// Fires the event rule for <paramref name="request"/> and streams the
    /// resulting <see cref="ChatSseEvent"/> items (contract:
    /// <see cref="EventRuleSseEvents"/>). Bound-denied and precondition-failed
    /// invocations yield a graceful <c>event_notice</c> — never a silent drop
    /// (NFR-09) and never an unbounded execution.
    /// </summary>
    IAsyncEnumerable<ChatSseEvent> FireAsync(
        SurfaceEventRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A surface event notification entering the Event path.
/// </summary>
/// <param name="EventName">
/// Closed platform event vocabulary token (canonical §7.1). P1 ships
/// <see cref="SurfaceEventNames.DocumentUploaded"/> only; the vocabulary lives
/// in Binding rows, not code — unknown tokens simply resolve zero members.
/// </param>
/// <param name="TenantId">Tenant claim (ADR-014).</param>
/// <param name="SessionId">Chat session the event occurred in.</param>
/// <param name="UserOid">Acting user object id — the budget/opt-out subject (NFR-09 is per-user).</param>
/// <param name="FileIds">
/// The uploaded batch (session-file ids). Null/empty falls back to the full
/// session manifest defensively; ids not present in the manifest are ignored.
/// </param>
/// <param name="TypedCommand">
/// Any command text the user typed alongside the upload. Non-empty ⇒
/// explicit-command supersede: the Text path wins and the rule does not fire.
/// </param>
/// <param name="CorrelationId">Optional correlation id (NFR-17 tracing).</param>
public sealed record SurfaceEventRequest(
    string EventName,
    string TenantId,
    string SessionId,
    string UserOid,
    IReadOnlyList<string>? FileIds,
    string? TypedCommand,
    string? CorrelationId = null);

/// <summary>Well-known surface event tokens (closed vocabulary, canonical §7.1).</summary>
public static class SurfaceEventNames
{
    /// <summary>A document (or batch) finished uploading into a chat session.</summary>
    public const string DocumentUploaded = "document_uploaded";
}
