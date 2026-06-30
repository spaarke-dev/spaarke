using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Default <see cref="IComposeSessionService"/> implementation — a thin façade over the
/// existing <see cref="ChatSessionManager"/> three-tier persistence pipeline.
///
/// <para>
/// <b>Reuse-not-duplicate (CLAUDE.md §11)</b>: zero new entities. Zero new Redis keys.
/// Zero new Cosmos partitions. Zero new Dataverse columns. Every method here is either
/// (a) a one-line delegation to <see cref="ChatSessionManager"/>, or (b) a one-decision
/// idempotency guard wrapping such a delegation.
/// </para>
///
/// <para>
/// <b>Lifetime</b>: registered <c>Scoped</c> by
/// <c>Program.cs</c> (task 025) to match the lifetime of the underlying
/// <see cref="ChatSessionManager"/>. One instance per HTTP request.
/// </para>
///
/// <para>
/// <b>ADR-013 conformance</b>: the only injected types are
/// <see cref="ChatSessionManager"/> (a CRUD-shaped session-store facade with zero AI
/// dependencies — see <c>AnalysisServicesModule.AddUnconditionalChatAndNotificationServices</c>)
/// and <see cref="ILogger{ComposeSessionService}"/>. NO <c>IOpenAiClient</c>,
/// <c>IPlaybookService</c>, or other AI-internal types are injected — this is
/// CRUD code and stays on the safe side of the refined ADR-013 boundary.
/// </para>
/// </summary>
/// <remarks>
/// Per refined ADR-013 (2026-05-20): this service MUST NOT inject <c>IOpenAiClient</c>,
/// <c>IPlaybookService</c>, or any other AI-internal type. <see cref="ChatSessionManager"/>
/// is a CRUD-shaped session-store facade and is explicitly the seam this service consumes
/// (registered unconditionally; no AI dependency in its constructor).
/// </remarks>
/// <summary>
/// Compose session-binding service over the existing <see cref="ChatSessionManager"/>
/// three-tier persistence pipeline (per CLAUDE.md §11 default-to-reuse).
///
/// Per ADR-010 strict compliance (collapsed from single-impl interface 2026-06-29
/// code-review cleanup): registered as concrete; methods are <c>virtual</c> ONLY to
/// preserve the Moq-based test boundary that mocks `RebindToDocumentIdAsync` in
/// <c>ComposeServiceTests</c>. R2 should remediate by rewriting those tests to use
/// a real instance + a <see cref="ChatSessionManager"/> double.
/// </summary>
public class ComposeSessionService
{
    private readonly ChatSessionManager _sessionManager;
    private readonly ILogger<ComposeSessionService> _logger;

    public ComposeSessionService(
        ChatSessionManager sessionManager,
        ILogger<ComposeSessionService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public virtual async Task<ChatSession> EnsureSessionForDocumentAsync(
        string tenantId,
        string documentId,
        Guid? playbookId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("documentId is required", nameof(documentId));
        }

        // FR-07: a Compose open does NOT look up by DocumentId — the canonical key is
        // SessionId. Compose-open endpoints (task 024) own the "find-or-create" decision
        // by tracking the session id on the client side. This method is the create-leg
        // of that decision — when the client has no session id, it asks us for a new one
        // bound to the document.
        //
        // CreateSessionAsync handles the full three-tier write: Dataverse insert (cold)
        // + Redis set (hot) + fire-and-forget Cosmos upsert (warm) per
        // ChatSessionManager.CreateSessionAsync.
        _logger.LogInformation(
            "Compose: creating session for document {DocumentId} (tenant={TenantId}, playbook={PlaybookId})",
            documentId, tenantId, playbookId);

        return await _sessionManager.CreateSessionAsync(
            tenantId: tenantId,
            documentId: documentId,
            playbookId: playbookId,
            hostContext: null,           // FR-07: HostContext is NOT extended in R1 (CLAUDE.md MUST rule)
            ct: ct);
    }

    public virtual Task<ChatSession?> GetSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId is required", nameof(sessionId));
        }

        // Pure delegation — ChatSessionManager.GetSessionAsync handles the full
        // Redis→Cosmos→Dataverse fallback chain unchanged.
        return _sessionManager.GetSessionAsync(tenantId, sessionId, ct);
    }

    public virtual async Task<ChatSession?> RebindToDocumentIdAsync(
        string tenantId,
        string sessionId,
        string currentDocumentId,
        string newDocumentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId is required", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(newDocumentId))
        {
            throw new ArgumentException("newDocumentId is required", nameof(newDocumentId));
        }

        // Idempotency short-circuit #1: the caller is explicitly asking for a no-op.
        // This catches the common case where the client re-submits Save with the same
        // promoted document id (e.g., a duplicate Save click after the network round-trip).
        if (string.Equals(currentDocumentId, newDocumentId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Compose: RebindToDocumentIdAsync no-op — currentDocumentId == newDocumentId ({DocumentId}, session={SessionId}, tenant={TenantId})",
                newDocumentId, sessionId, tenantId);

            return await _sessionManager.GetSessionAsync(tenantId, sessionId, ct);
        }

        // Load the canonical session state from the three-tier store.
        var session = await _sessionManager.GetSessionAsync(tenantId, sessionId, ct);
        if (session is null)
        {
            // FR-06 contract: the session MUST exist before promotion. Returning null
            // signals "hard error" to the caller (task 024 endpoint will translate to
            // 404 ProblemDetails).
            _logger.LogWarning(
                "Compose: RebindToDocumentIdAsync called for non-existent session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
            return null;
        }

        // Idempotency short-circuit #2: the stored binding is ALREADY at the target.
        // This catches the case where a previous Rebind succeeded but the client did not
        // observe the response (e.g., browser tab refreshed mid-promotion).
        if (string.Equals(session.DocumentId, newDocumentId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Compose: RebindToDocumentIdAsync no-op — session.DocumentId already == newDocumentId ({DocumentId}, session={SessionId}, tenant={TenantId})",
                newDocumentId, sessionId, tenantId);
            return session;
        }

        // Out-of-order Save race detector: the caller asserted the current binding was
        // X, but the store says it's Y. We still proceed with the rebind (new-value-wins
        // semantics — the post-promotion sprk_documentid is the truth) but emit a Warning
        // for operator visibility.
        if (!string.IsNullOrWhiteSpace(currentDocumentId) &&
            !string.Equals(session.DocumentId, currentDocumentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Compose: RebindToDocumentIdAsync — caller-asserted currentDocumentId ({CallerCurrent}) differs from stored DocumentId ({StoredCurrent}) for session {SessionId} (tenant={TenantId}); proceeding with rebind to {NewDocumentId} (new-value-wins).",
                currentDocumentId, session.DocumentId, sessionId, tenantId, newDocumentId);
        }

        _logger.LogInformation(
            "Compose: rebinding session {SessionId} DocumentId {From} -> {To} (tenant={TenantId})",
            sessionId, session.DocumentId, newDocumentId, tenantId);

        // Apply the rebind via record `with` expression (existing pattern in
        // ChatHistoryManager.AddMessageAsync), then push through the existing
        // UpdateSessionCacheAsync write path. This is the SAME persistence pipeline used
        // for every other ChatSession mutation — Redis hot-cache update + fire-and-forget
        // Cosmos upsert. No new write path is introduced.
        var rebound = session with
        {
            DocumentId = newDocumentId,
            LastActivity = DateTimeOffset.UtcNow,
        };

        await _sessionManager.UpdateSessionCacheAsync(rebound, ct);
        return rebound;
    }
}
