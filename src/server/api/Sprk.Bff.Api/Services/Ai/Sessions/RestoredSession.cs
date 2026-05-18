namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// The result of a session restore operation performed by <see cref="ISessionRestoreService"/>.
///
/// Encapsulates everything the caller needs to resume an AI chat session:
/// the reconstructed LLM context window, the widget states for the three-pane UI,
/// and any staleness warnings for Dataverse entity references.
///
/// Injected into the next streaming request as the pre-populated context (decision D-08:
/// data-refreshed restore, not stale snapshot — staleness is surfaced, not silently ignored).
/// </summary>
/// <param name="SessionId">Unique identifier of the restored session.</param>
/// <param name="TenantId">Tenant identifier (ADR-015 Tier 3 tenant isolation).</param>
/// <param name="PlaybookId">Playbook governing the session's agent behaviour, if set.</param>
/// <param name="ReconstructedContext">
/// Ready-to-inject LLM context string. Format:
/// <c>[CONVERSATION SUMMARY]\n{summary}\n\n[RECENT MESSAGES]\n{last 10 verbatim messages}</c>
/// When no summary exists: <c>[RECENT MESSAGES]\n{last N verbatim messages}</c>.
/// </param>
/// <param name="StaleEntityRefs">
/// Entity references whose ETag has changed since the session was saved.
/// Empty when all entities are current. Caller should surface a warning to the user
/// and optionally refresh the context before the next LLM call.
/// </param>
/// <param name="WidgetStates">
/// Serialised widget state dictionary keyed by widget instance ID.
/// Passed to the frontend so the three-pane UI can restore each widget's last state.
/// </param>
/// <param name="WasSummarized">
/// True when the reconstructed context uses an LLM-generated summary as the base
/// (session had a stored summary). False when context uses verbatim messages only.
/// </param>
/// <param name="RestoredAt">UTC timestamp of the restore operation.</param>
/// <param name="RestoreLatencyMs">
/// Wall-clock time in milliseconds for the complete restore (load + staleness + reconstruct).
/// Logged and surfaced for NFR compliance: &lt;500ms p95 target.
/// </param>
public record RestoredSession(
    string SessionId,
    string TenantId,
    Guid? PlaybookId,
    string ReconstructedContext,
    IReadOnlyList<SessionEntityRef> StaleEntityRefs,
    IReadOnlyDictionary<string, string> WidgetStates,
    bool WasSummarized,
    DateTimeOffset RestoredAt,
    long RestoreLatencyMs);
