using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Thin binding seam that ties a Compose editor session to the existing
/// <see cref="ChatSession"/> model via <c>DocumentId</c>.
///
/// <para>
/// <b>Why this exists (FR-07)</b>: Compose MUST reuse the existing
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager"/> + Redis/Cosmos/Dataverse
/// three-tier persistence pipeline — NOT introduce a new session entity, new Redis key
/// shape, or new Cosmos partition. This service is the load-bearing seam that gives the
/// Compose surface a stable, intention-revealing API while delegating all heavy lifting
/// (compaction at 15 messages, archive at 50, multi-tenant Tier 3 isolation) to the
/// underlying <see cref="ChatSession"/> infrastructure unchanged.
/// </para>
///
/// <para>
/// <b>DocumentId binding semantics (FR-06 + FR-07)</b>:
/// <list type="bullet">
///   <item><b>Path B (ephemeral)</b>: <c>DocumentId</c> is the SPE drive-item id of the
///         user's working document. No <c>sprk_document</c> row exists yet.</item>
///   <item><b>Path A (promoted)</b>: after first Save (FR-06 promotion), <c>DocumentId</c>
///         is updated to the <c>sprk_documentid</c> of the newly-created
///         <c>sprk_document</c> row. Promotion is idempotent — multiple Saves do not
///         create duplicate Document records, and multiple
///         <see cref="RebindToDocumentIdAsync"/> calls with the same target are a no-op.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>ADR boundaries</b>:
/// <list type="bullet">
///   <item><b>ADR-013 (refined)</b>: this is a CRUD-shaped binding service. It MUST NOT
///         inject AI-internal types (<c>IOpenAiClient</c>, <c>IPlaybookService</c>) — it
///         consumes <see cref="Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager"/>, which
///         is itself a session-store facade with zero AI dependencies (registered
///         unconditionally per <c>AnalysisServicesModule.AddUnconditionalChatAndNotificationServices</c>).</item>
///   <item><b>ADR-015 Tier 3 (multi-tenant isolation)</b>: inherited via the underlying
///         <see cref="ChatSession"/> infrastructure. This service does NOT bypass the
///         existing tenant-scope filters on Redis keys, Cosmos partitions, or Dataverse
///         queries — every method that hits the store accepts an explicit
///         <c>tenantId</c> and forwards it verbatim.</item>
/// </list>
/// </para>
/// </summary>
public interface IComposeSessionService
{
    /// <summary>
    /// Returns the active <see cref="ChatSession"/> bound to <paramref name="documentId"/>
    /// for the calling tenant, creating one via
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager.CreateSessionAsync"/>
    /// if none exists yet (idempotent — second call with the same document returns the
    /// same session unchanged).
    ///
    /// <para>
    /// Lookup strategy (FR-07): the <see cref="ChatSession.SessionId"/> is the canonical
    /// key in Redis / Cosmos / Dataverse, NOT the <c>DocumentId</c>. Callers that already
    /// know the session id should call <see cref="GetSessionAsync(string, string, CancellationToken)"/>
    /// directly. This convenience method is for the Compose-open flow where the document
    /// id is known but the session id may not be.
    /// </para>
    /// </summary>
    /// <param name="tenantId">Power Platform tenant id (ADR-015 Tier 3 isolation).</param>
    /// <param name="documentId">
    /// Either the SPE drive-item id (ephemeral Path B) or the promoted <c>sprk_documentid</c>
    /// (Path A). See class-level remarks for the binding semantics.
    /// </param>
    /// <param name="playbookId">
    /// Optional playbook to govern the agent's behaviour for this Compose session. When
    /// null, callers default to the system-wide Document Summary playbook
    /// (<c>47686eb1-9916-f111-8343-7c1e520aa4df</c>) per FR-09 / FR-10.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bound <see cref="ChatSession"/>.</returns>
    Task<ChatSession> EnsureSessionForDocumentAsync(
        string tenantId,
        string documentId,
        Guid? playbookId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="ChatSession"/> identified by <paramref name="sessionId"/>
    /// for the calling tenant, or <c>null</c> if no such session exists.
    /// </summary>
    /// <param name="tenantId">Power Platform tenant id (ADR-015 Tier 3 isolation).</param>
    /// <param name="sessionId">Session id to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChatSession?> GetSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Idempotently rebinds the session at <paramref name="sessionId"/> from the
    /// ephemeral <paramref name="currentDocumentId"/> (SPE drive-item id) to the
    /// promoted <paramref name="newDocumentId"/> (<c>sprk_documentid</c>) after first Save
    /// (FR-06 + FR-07).
    ///
    /// <para>
    /// Idempotency contract: if the session's existing <see cref="ChatSession.DocumentId"/>
    /// already equals <paramref name="newDocumentId"/> (including the case where
    /// <paramref name="currentDocumentId"/> == <paramref name="newDocumentId"/>), this is
    /// a no-op and returns the unchanged session. This is critical because multiple Save
    /// clicks before-and-after promotion MUST NOT corrupt the binding or duplicate
    /// <c>sprk_document</c> rows.
    /// </para>
    ///
    /// <para>
    /// Persistence: the rebind goes through the existing
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager"/> write path — Redis
    /// hot-cache update + Cosmos write-through + (eventually) Dataverse activity update.
    /// No new persistence path is introduced.
    /// </para>
    /// </summary>
    /// <param name="tenantId">Power Platform tenant id (ADR-015 Tier 3 isolation).</param>
    /// <param name="sessionId">Session id whose binding is being updated.</param>
    /// <param name="currentDocumentId">
    /// The session's expected current <see cref="ChatSession.DocumentId"/> before the
    /// rebind (the SPE drive-item id under Path B). When this does not match the stored
    /// value, the method still performs the rebind to <paramref name="newDocumentId"/>
    /// (the new value wins) but logs the mismatch at Warning so operators can spot
    /// out-of-order Save races.
    /// </param>
    /// <param name="newDocumentId">
    /// The new <see cref="ChatSession.DocumentId"/> value after promotion (the
    /// <c>sprk_documentid</c> of the just-created <c>sprk_document</c> row).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The session after rebind (or the unchanged session if the rebind was a no-op).
    /// Returns <c>null</c> if no session exists at <paramref name="sessionId"/> for the
    /// tenant — callers should treat this as a hard error (FR-06 requires the session to
    /// exist before promotion).
    /// </returns>
    Task<ChatSession?> RebindToDocumentIdAsync(
        string tenantId,
        string sessionId,
        string currentDocumentId,
        string newDocumentId,
        CancellationToken ct = default);
}
