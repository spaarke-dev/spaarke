namespace Sprk.Bff.Api.Services.Ai.Telemetry;

/// <summary>
/// R6 DEF-001 / task 095 Phase 3 — Per-request scoped relay that bridges the
/// singleton <see cref="ContextEventEmitter"/> to the chat SSE stream.
///
/// <para>
/// <b>Lifecycle</b>: scoped (per HTTP request). The chat SSE endpoint
/// (<c>ChatEndpoints.SendMessageAsync</c>) resolves this relay at the start of the
/// SSE stream, assigns <see cref="Writer"/> to a delegate that writes a
/// <c>"context_event"</c> SSE frame to the response, and clears
/// <see cref="Writer"/> in <c>finally</c> at stream end (or on exception). The
/// singleton emitter looks up this scoped service via
/// <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/> and, if
/// <see cref="Writer"/> is non-null, invokes it for each emission.
/// </para>
///
/// <para>
/// <b>Why scoped + per-request, not singleton + AsyncLocal</b>: AsyncLocal flows
/// through fire-and-forget background tasks (e.g., logging callbacks); a scoped relay
/// is bounded to the request scope and disposed with it — no cross-request leakage.
/// </para>
///
/// <para>
/// <b>Concurrency</b>: implementations MUST serialize concurrent invocations of
/// <see cref="Writer"/> so SSE frames never interleave (a chat turn may have
/// overlapping tool-completion + token-stream emissions on the same HTTP response).
/// </para>
///
/// <para>
/// <b>ADR-015 / ADR-030 / ADR-033</b>: see <see cref="ContextSseEventDto"/> class
/// header for the binding constraints inherited by this relay.
/// </para>
/// </summary>
public interface IContextSseRelay
{
    /// <summary>
    /// Optional writer delegate set by the SSE endpoint at stream start. When
    /// <see langword="null"/>, emissions are no-ops — the request is either outside
    /// the chat SSE stream (e.g., REST endpoint that triggers playbook execution but
    /// doesn't stream context events) or the stream has already ended.
    /// </summary>
    Func<ContextSseEventDto, CancellationToken, Task>? Writer { get; set; }

    /// <summary>
    /// Forward a context trace event to the SSE stream, if a <see cref="Writer"/>
    /// is attached. MUST be safe to call from any thread; implementations serialize
    /// concurrent invocations to preserve SSE frame ordering.
    ///
    /// <para>
    /// This method MUST swallow all exceptions — the side-channel exists to enrich
    /// the trace UX; emission failures must never propagate back to the emission site
    /// (which is itself inside a singleton emitter on the chat hot path).
    /// </para>
    /// </summary>
    Task TryWriteAsync(ContextSseEventDto dto, CancellationToken cancellationToken);
}
