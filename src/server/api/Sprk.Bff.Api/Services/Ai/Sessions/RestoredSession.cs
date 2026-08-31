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
/// <param name="UploadedFiles">
/// Minimal projection of the session's uploaded-files manifest
/// (<see cref="StoredSession.UploadedFiles"/>), so the client can rehydrate the attachment chip
/// on restore (spaarkeai-assistant-enhancements-r2 FR-D5). Identifier/display metadata ONLY —
/// deliberately excludes SummaryText / Sections / Citations (ADR-015 Tier-2: never ship enriched
/// content through the restore projection; ADR-040: read the existing manifest, no new store).
/// Empty when the session has no uploaded files.
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
    long RestoreLatencyMs,
    IReadOnlyList<SessionMessage> RecentMessages,
    IReadOnlyList<RestoredUploadedFile> UploadedFiles);

/// <summary>
/// Minimal per-file projection carried on <see cref="RestoredSession"/> for client attachment-chip
/// rehydration (FR-D5). A strict subset of <see cref="StoredUploadedFile"/> — identifier + display
/// fields only. Enriched fields (summary, sections, citations, extracted text) are intentionally NOT
/// projected: the chip needs a name + size + type, nothing more (ADR-015 Tier-2 minimisation).
/// </summary>
/// <param name="FileId">Stable session-scoped file id.</param>
/// <param name="FileName">Original upload file name (chip label).</param>
/// <param name="ContentType">MIME content type as reported on upload.</param>
/// <param name="SizeBytes">Original (uncompressed) file size in bytes.</param>
/// <param name="ContentAvailable">
/// spaarkeai-compose-r8 FR-B05 (task 062) — the SERVER's answer to "is this file's content still
/// usable?", replacing R7's client-side ~24h guess.
/// <list type="bullet">
///   <item><c>true</c> — a durable byte copy exists for this tenant, so the content survives for as
///     long as the session does (re-indexed on demand by
///     <see cref="SessionFileRehydrationService"/> if the hot chunks were evicted).</item>
///   <item><c>false</c> — the durable store is configured and holds no copy. Content is not guaranteed
///     beyond the hot index's own window.</item>
///   <item><c>null</c> — the server cannot answer (the durable store is not configured in this
///     deployment, or the probe failed). Clients MUST render this as unknown and MUST NOT substitute a
///     guess: FR-B05 requires exactly one availability source, and this is it.</item>
/// </list>
/// Still identifier/display-class metadata, so it does not weaken the ADR-015 Tier-2 minimisation this
/// projection was built for — it says whether content exists, never what the content is.
/// </param>
public record RestoredUploadedFile(
    string FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool? ContentAvailable = null);
