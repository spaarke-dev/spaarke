using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>active-document</b> route: <c>POST /api/compose/active-document</c>.
///
/// <para><b>Reason to change</b>: the chat&lt;-&gt;Compose session-pointer semantics — what
/// "the document the user is acting on" means across multiple Compose tabs (the order-independent
/// register/withdraw pair), and when a client-minted document session must be materialized so a
/// dispatch resolves.</para>
/// </summary>
internal static class ComposeActiveDocumentEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeActiveDocumentEndpoints(this RouteGroupBuilder group)
    {
        // (16) POST /api/compose/active-document — task 113 (UAT defects 4/5): register the
        // session-scoped ACTIVE-DOCUMENT so both surfaces resolve "the document the user is acting
        // on" deterministically. Marks an already-landed session file (compose-direct Browse upload
        // or a chat upload — its bytes become a ChatSessionFile via the existing chat upload
        // endpoint, reused client-side) OR a stored sprk_document as active on the chat session.
        // Deterministic ChatSession write via ChatSessionManager (no parallel document store —
        // CLAUDE.md §11) — NOT AI dispatch (ADR-039) and NOT SPE/Graph access (ADR-007). Authz via
        // the group's RequireAuthorization() (ADR-008 / ADR-028).
        group.MapPost("/active-document", RegisterActiveDocument)
            .WithName("ComposeRegisterActiveDocument")
            .WithSummary("Register the session-scoped active document for the chat↔Compose bridge (task 113)")
            .RequireRateLimiting("ai-context")
            .Produces<ComposeActiveDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // task 113 (UAT defects 4/5): session-scoped active-document registration.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/compose/active-document — records which document the user is acting on
    /// (session-scoped) so (a) chat can resolve a Compose-direct upload ("summarize this
    /// document") and (b) <c>SendWorkspaceArtifactHandler</c> mounts the just-active document
    /// when the LLM supplies no explicit pointer ("edit in Compose"). Provide EXACTLY ONE of
    /// <c>sessionFileId</c> (a session-uploaded / compose-direct <see cref="ChatSessionFile"/>)
    /// or <c>documentId</c> (a stored <c>sprk_document</c> GUID). Deterministic
    /// <see cref="ChatSession"/> write via <see cref="ChatSessionManager"/> — no AI dispatch
    /// (ADR-039), no SPE/Graph (ADR-007). The compose-direct file's BYTES are landed as a
    /// ChatSessionFile by the EXISTING chat upload endpoint (reused client-side, CLAUDE.md §11);
    /// this endpoint only records the pointer.
    /// </summary>
    private static async Task<IResult> RegisterActiveDocument(
        [FromBody] ComposeActiveDocumentRequest? body,
        ChatSessionManager sessionManager,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required.");

        var hasSessionFile = !string.IsNullOrWhiteSpace(body.SessionFileId);
        var hasDocument = !string.IsNullOrWhiteSpace(body.DocumentId);
        if (!hasSessionFile && !hasDocument)
            return BadRequest("Provide sessionFileId (a session-uploaded / compose-direct file) or documentId (a stored sprk_document).");
        if (hasSessionFile && hasDocument)
            return BadRequest("Provide at most one of sessionFileId or documentId — they are mutually exclusive (upload vs stored).");

        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity not found in token claims.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var (session, sessionKey) = await ResolveSessionAsync(sessionManager, tenantId, body.SessionId, ct)
                .ConfigureAwait(false);

            // Issue #863 — this route takes its session id in the BODY, so SessionOwnershipFilter
            // (route-value based) does not cover it; the check lives here and the route is
            // enumerated in SessionOwnershipGuardTests.BodyScopedSessionRoutes. It matters twice
            // over: registering an active document MUTATES the named session, and the document
            // session minted below INHERITS this one's owner — so an unchecked parent would hand a
            // caller a child session owned by someone else.
            // Not-yours and not-found are deliberately the same answer (see the filter's remarks).
            if (session is not null
                && !string.Equals(
                    session.OwnerOid,
                    CallerResolution.ResolveObjectId(httpContext.User),
                    StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Compose active-document DENIED: session={SessionId} tenant={TenantId} is not " +
                    "owned by the caller. Answered 404. TraceId={TraceId}",
                    body.SessionId, tenantId, httpContext.TraceIdentifier);
                session = null;
            }

            if (session is null)
            {
                logger.LogWarning(
                    "Compose active-document: session not found tenant={TenantId} session={SessionId} TraceId={TraceId}",
                    tenantId, body.SessionId, httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Session Not Found",
                    detail: "The chat session was not found or has expired. Register the active document on an existing session.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            ActiveDocumentIdentity identity;
            if (hasSessionFile)
            {
                // Best-effort display name from the session manifest (the bytes were landed as a
                // ChatSessionFile by the existing chat upload endpoint — reused client-side).
                var file = session.UploadedFiles?
                    .FirstOrDefault(f => string.Equals(f.FileId, body.SessionFileId, StringComparison.Ordinal));
                var source = string.IsNullOrWhiteSpace(body.Source)
                    ? ActiveDocumentIdentity.SourceComposeDirect
                    : body.Source!;
                identity = new ActiveDocumentIdentity(
                    Source: source,
                    SessionFileId: body.SessionFileId,
                    FileName: body.FileName ?? file?.FileName,
                    RegisteredAt: DateTimeOffset.UtcNow,
                    DocumentSessionId: body.DocumentSessionId);
            }
            else
            {
                identity = new ActiveDocumentIdentity(
                    Source: ActiveDocumentIdentity.SourceStored,
                    SprkDocumentId: body.DocumentId,
                    SpeDriveItemId: body.SpeDriveItemId,
                    SpeDriveId: body.SpeDriveId,
                    FileName: body.FileName,
                    RegisteredAt: DateTimeOffset.UtcNow,
                    DocumentSessionId: body.DocumentSessionId);
            }

            // R3 WITHDRAW (spaarkeai-compose-r2 multi-Compose-tab): visible:false means "this document is
            // no longer the active tab" (its tab was hidden / another Compose tab became active). A
            // withdraw must NEVER set ActiveDocument to the withdrawing identity — that is the bug that
            // left the Assistant pinned to the first document after a tab switch (the hidden tab's
            // withdraw re-asserted itself as active). Clear ActiveDocument ONLY if it STILL points at
            // THIS document; if a newer tab already took over, leave it untouched. This makes the
            // register/withdraw pair that fires on every switch ORDER-INDEPENDENT.
            //
            // NOTE (race-narrowed, NOT race-free): the session store (ChatSessionManager
            // .UpdateSessionCacheAsync → ITenantCache.SetSlidingAsync) is last-writer-wins Redis with
            // NO optimistic-concurrency primitive (no etag/version/CAS). The stillActive guard below
            // reads session.ActiveDocument from the snapshot loaded at the top of THIS handler, so a
            // concurrent register(B) that commits between our load and our write can still be clobbered
            // by this withdraw's write (a classic read-modify-write lost update). The guard narrows the
            // window and makes the common tab-switch ordering safe; it does not eliminate the race.
            // Full safety would require a CAS/version on the session cache (out of scope — a
            // pre-existing store limitation this withdraw only slightly widens).
            if (body.Visible == false)
            {
                var current = session.ActiveDocument;
                var stillActive = current is not null && (
                    (hasSessionFile && string.Equals(current.SessionFileId, body.SessionFileId, StringComparison.Ordinal)) ||
                    (hasDocument && string.Equals(current.SprkDocumentId, body.DocumentId, StringComparison.Ordinal)));
                if (stillActive)
                {
                    await sessionManager.UpdateSessionCacheAsync(session with { ActiveDocument = null }, ct).ConfigureAwait(false);
                }
                logger.LogInformation(
                    "Compose active-document WITHDRAW: tenant={TenantId} session={SessionKey} kind={Kind} clearedActive={Cleared} TraceId={TraceId}",
                    tenantId, sessionKey, hasSessionFile ? "session-file" : "stored", stillActive, httpContext.TraceIdentifier);
                return Results.Ok(new ComposeActiveDocumentResponse(
                    SessionId: body.SessionId,
                    Source: identity.Source,
                    SessionFileId: identity.SessionFileId,
                    DocumentId: identity.SprkDocumentId,
                    FileName: identity.FileName,
                    CorrelationId: httpContext.TraceIdentifier,
                    DocumentSessionId: identity.DocumentSessionId));
            }

            var updated = session with { ActiveDocument = identity };
            await sessionManager.UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

            // DEF-11 doc-session dispatch fix (spaarkeai-compose-r2): the Compose "document session"
            // (identity.DocumentSessionId) is client-minted (crypto.randomUUID) and is NEVER created
            // via POST /api/ai/chat/sessions. A materializesInEditor compose dispatch therefore targets
            // POST /api/ai/chat/sessions/{documentSessionId}/dispatch and 404s, because
            // SessionDispatchOrchestrator loads GetSessionAsync(tenantId, documentSessionId) → null.
            // Idempotently ensure a minimal, resolvable ChatSession exists keyed by documentSessionId
            // so the dispatch resolves and OutputRouter can write its SessionOutput. This is a
            // deterministic session-store write (same UpdateSessionCacheAsync the pointer path uses) —
            // NOT AI dispatch (ADR-039) and NOT SPE/Graph (ADR-007). Session-creation stays OUT of the
            // dispatch seam; this is the single natural creation hook.
            await EnsureDocumentSessionResolvableAsync(
                sessionManager, tenantId, identity.DocumentSessionId, session, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Compose active-document registered: tenant={TenantId} session={SessionKey} source={Source} kind={Kind} docSession={DocumentSessionId} TraceId={TraceId}",
                tenantId, sessionKey, identity.Source, hasSessionFile ? "session-file" : "stored", identity.DocumentSessionId ?? "(none)", httpContext.TraceIdentifier);

            return Results.Ok(new ComposeActiveDocumentResponse(
                SessionId: body.SessionId,
                Source: identity.Source,
                SessionFileId: identity.SessionFileId,
                DocumentId: identity.SprkDocumentId,
                FileName: identity.FileName,
                CorrelationId: httpContext.TraceIdentifier,
                DocumentSessionId: identity.DocumentSessionId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose active-document: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while registering the active document.");
        }
    }

    /// <summary>
    /// DEF-11 (spaarkeai-compose-r2): idempotently ensures a resolvable <see cref="ChatSession"/>
    /// exists keyed by <paramref name="documentSessionId"/> so a compose <c>materializesInEditor</c>
    /// dispatch to <c>POST /api/ai/chat/sessions/{documentSessionId}/dispatch</c> resolves (200) rather
    /// than 404-ing. The Compose document session is client-minted and never created via the chat
    /// session-create endpoint, so this registration hook is its single natural creation point.
    ///
    /// <para><b>Idempotency (critical)</b>: if a session already resolves for
    /// <paramref name="documentSessionId"/>, this method is a no-op — it does NOT clobber the existing
    /// session. That preserves any <see cref="ChatSession.Outputs"/> (the compose-disposition ledger
    /// the editor materializes) written by prior dispatches; re-registration across the multiple mount
    /// doors (Browse, upload, stored-doc, DEF-08 draft) MUST NOT wipe pending redlines.</para>
    ///
    /// <para>When absent, a MINIMAL session is created (empty Messages/Outputs, timestamps=now,
    /// carrying over the chat session's <see cref="ChatSession.HostContext"/> and tenant) and persisted
    /// via the same <see cref="ChatSessionManager.UpdateSessionCacheAsync"/> write the pointer path uses.
    /// Compose EDIT actions bind their operand from dispatch args (the structured-operand path — no
    /// session files required), so a minimal persisted session is sufficient for the dispatch to run and
    /// write its <c>SessionOutput</c>.</para>
    /// </summary>
    private static async Task EnsureDocumentSessionResolvableAsync(
        ChatSessionManager sessionManager,
        string tenantId,
        string? documentSessionId,
        ChatSession chatSession,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentSessionId))
        {
            return;
        }

        // Preserve an existing doc session wholesale (Outputs / ledger) — never clobber.
        var (existing, _) = await ResolveSessionAsync(sessionManager, tenantId, documentSessionId, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var documentSession = new ChatSession(
            SessionId: documentSessionId,
            TenantId: tenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: [],
            HostContext: chatSession.HostContext)
        {
            // Issue #863 — the document session INHERITS its parent chat session's owner. It is a
            // child of that conversation, minted on the caller's behalf, and every later request to
            // it goes through SessionOwnershipFilter.
            //
            // Without this the session is created UNOWNED, which fails closed — so the very next
            // dispatch to the Compose document the user just registered would 404 for the user who
            // registered it. Caught by ComposeDocSessionDispatchSeamTests (the DEF-11 regression
            // guard), which is exactly the class of defect that guard exists to hold.
            OwnerOid = chatSession.OwnerOid,
        };

        await sessionManager.UpdateSessionCacheAsync(documentSession, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a chat session, probing the client-sent id then its GUID "N"/"D" normalizations —
    /// the same tolerance the Compose upload path applies, since a client may send either spelling.
    /// </summary>
    private static async Task<(ChatSession? Session, string? Key)> ResolveSessionAsync(
        ChatSessionManager sessionManager, string tenantId, string sessionId, CancellationToken ct)
    {
        foreach (var candidate in EnumerateSessionIdForms(sessionId))
        {
            var session = await sessionManager.GetSessionAsync(tenantId, candidate, ct).ConfigureAwait(false);
            if (session is not null) return (session, candidate);
        }
        return (null, null);
    }

    private static IEnumerable<string> EnumerateSessionIdForms(string sessionId)
    {
        yield return sessionId;
        if (Guid.TryParse(sessionId, out var g))
        {
            var n = g.ToString("N");
            var d = g.ToString("D");
            if (!string.Equals(n, sessionId, StringComparison.Ordinal)) yield return n;
            if (!string.Equals(d, sessionId, StringComparison.Ordinal)) yield return d;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/compose/active-document</c> (task 113 / UAT defects 4/5).
/// Registers the session-scoped active document on the chat session. Provide EXACTLY ONE of
/// <see cref="SessionFileId"/> (a session-uploaded / compose-direct <see cref="ChatSessionFile"/>)
/// or <see cref="DocumentId"/> (a stored <c>sprk_document</c> GUID, D form). <see cref="Source"/>
/// is an optional provenance discriminant (defaults to <c>compose-direct</c> for a session file,
/// <c>stored</c> for a document) — see <see cref="ActiveDocumentIdentity"/>.
/// </summary>
/// <param name="DocumentSessionId">
/// DEF-11 (spaarkeai-compose-r2) — OPTIONAL id of the separate, coordinated Compose "document
/// session" (<c>ComposeWorkspace.state.sessionId</c>) that hosts this document's compose-disposition
/// ledger outputs. Additive: older clients omitting this field are unaffected (the active-document
/// pointer still registers; <see cref="ActiveDocumentIdentity.DocumentSessionId"/> stays null and the
/// text-path capability dispatch falls back to the chat session per its fail-soft rule). When
/// supplied, <c>BindingCapabilityTool</c> routes text-path <c>compose</c>-disposition dispatches to
/// THIS session instead of the chat session, matching the DEF-09 Click-path precedent.
/// </param>
public sealed record ComposeActiveDocumentRequest(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("sessionFileId")] string? SessionFileId = null,
    [property: JsonPropertyName("documentId")] string? DocumentId = null,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("fileName")] string? FileName = null,
    [property: JsonPropertyName("speDriveItemId")] string? SpeDriveItemId = null,
    [property: JsonPropertyName("speDriveId")] string? SpeDriveId = null,
    [property: JsonPropertyName("documentSessionId")] string? DocumentSessionId = null,
    // R3 visibility (spaarkeai-compose-r2 multi-Compose-tab): false = WITHDRAW this document from the
    // session's active document (the tab was hidden / another Compose tab became active). Omitted/true
    // = register as active. The client has always sent this; the server previously had no property for
    // it, so it was silently dropped and EVERY post — including a hidden tab's withdraw — re-pinned that
    // document as active, leaving the Assistant stuck on the first doc after a tab switch (UAT 2026-07-14).
    [property: JsonPropertyName("visible")] bool? Visible = null);

/// <summary>Response shape for <c>POST /api/compose/active-document</c> (task 113) — echoes the
/// registered active-document pointer. <see cref="DocumentSessionId"/> added DEF-11 (spaarkeai-compose-r2).</summary>
public sealed record ComposeActiveDocumentResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sessionFileId")] string? SessionFileId,
    [property: JsonPropertyName("documentId")] string? DocumentId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("documentSessionId")] string? DocumentSessionId = null);
