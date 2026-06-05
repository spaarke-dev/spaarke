namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R5 task 007 (D1-07) — producer-side contract for the session-files cleanup
/// in-process signal channel. Consumed by <see cref="ChatSessionManager.DeleteSessionAsync"/>
/// to trigger immediate eviction of indexed session-file chunks from
/// <c>spaarke-session-files</c> when a chat session ends (spec NFR-02
/// "Aggressive cleanup on session-end").
/// </summary>
/// <remarks>
/// <para>
/// This is the SINGLE seam this task introduces per R5 CLAUDE.md §3.3 (ADR-010
/// DI minimalism — concrete classes by default). The interface exists to keep
/// <see cref="ChatSessionManager"/> unit-testable in isolation — production code
/// resolves the concrete <see cref="SessionFilesCleanupSignal"/> via the same DI
/// container that hosts <see cref="SessionFilesCleanupJob"/>.
/// </para>
/// <para>
/// Calls are fire-and-forget by contract: implementations MUST NOT throw on
/// invalid input (null/empty tenant or session ID) and MUST NOT block.
/// <see cref="ChatSessionManager.DeleteSessionAsync"/> wraps the call in a
/// try/catch + log-and-swallow per the existing Cosmos-persist convention.
/// </para>
/// </remarks>
public interface ISessionFilesCleanupSignal
{
    /// <summary>
    /// Signals that a chat session has ended and its session-files index
    /// documents should be evicted immediately (no scheduled-sweep delay).
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation; required per ADR-014).</param>
    /// <param name="sessionId">Session ID to evict (required).</param>
    /// <remarks>
    /// Fire-and-forget. Returns immediately. Implementations SHOULD silently
    /// no-op on null/empty input rather than throw.
    /// </remarks>
    void SignalSessionEnded(string tenantId, string sessionId);
}
