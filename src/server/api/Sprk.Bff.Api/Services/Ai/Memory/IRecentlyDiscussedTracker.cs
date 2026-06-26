namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Tracks the recency of file discussion within a chat session. Read by future tools
/// (T2 manifest builder, prompt assembly) to surface "you were just discussing X" cues.
/// Per architecture §6.3 / §11.1 ("recently-discussed" cue layer).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Architecture binding</strong>: chat-routing-redesign-r1 §6.3 + §11.1. The
/// recently-discussed tracker is the substrate that future T2 manifest builders + prompt
/// assembly read to surface "you were just discussing X" cues without re-issuing a
/// session-files search.
/// </para>
/// <para>
/// <strong>ADR-014 / FR-05 isolation</strong>: storage keys are tenant-scoped via
/// <c>ITenantCache</c> as
/// <c>tenant:{tenantId}:recently-discussed:{sessionId}:v1</c>. Cross-session reads
/// remain structurally impossible (session id is unique within a tenant) and the
/// tenant prefix satisfies spaarke-redis-cache-remediation-r1 FR-05 mandatory tenant
/// scoping. The session id supplied by the caller is trusted to be authentic per the
/// upstream ChatSessionManager contract.
/// </para>
/// <para>
/// <strong>ADR-015 telemetry</strong>: implementations MUST log only sessionId + fileId +
/// operation name + duration. NEVER file content, query strings, or summary text.
/// </para>
/// <para>
/// <strong>ADR-032</strong>: this service is UNCONDITIONAL (memory is always on in MVP);
/// no Null-Object kill switch required. If a future kill switch is needed, apply the
/// ADR-032 Null-Object pattern rather than wrapping the registration in a feature flag.
/// </para>
/// </remarks>
public interface IRecentlyDiscussedTracker
{
    /// <summary>
    /// Marks a file as discussed in the given session at the current UTC time.
    /// Idempotent: repeated calls with the same (tenantId, sessionId, fileId) overwrite the timestamp.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (FR-05 mandatory tenant scoping).</param>
    /// <param name="sessionId">Session identifier (chat session GUID "N" format).</param>
    /// <param name="fileId">Stable file identifier from the session manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsync(string tenantId, string sessionId, string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recently-discussed file ids for the given session, ordered by most-recent first.
    /// Empty list if no files discussed yet. Bound by <paramref name="maxCount"/> (default 5).
    /// </summary>
    /// <param name="tenantId">Tenant identifier (FR-05 mandatory tenant scoping).</param>
    /// <param name="sessionId">Session identifier (chat session GUID "N" format).</param>
    /// <param name="maxCount">Maximum number of fileIds to return (default 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetRecentAsync(
        string tenantId,
        string sessionId,
        int maxCount = 5,
        CancellationToken cancellationToken = default);
}
