using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 022 (FR-08): fire-and-forget dispatch of the Document Profile for the
/// Office add-in's "Generate Profile" trigger, onto the SAME direct-Action facade
/// (<see cref="IDocumentProfileAi"/>) that Compose's <c>refresh-profile</c> uses — see
/// <c>docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md</c> Path B
/// (<c>DocumentProfileAi.ProfileDocumentAsUserAsync</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately NOT the node-playbook / Service-Bus path (Path C).</b> The known idempotency trap
/// (verified by task 023): <c>UploadFinalizationWorker</c> queues an <c>AppOnlyDocumentAnalysis</c> job
/// with idempotency key <c>analysis-{documentId}-documentprofile</c>, and the handler SKIPS an
/// already-processed key. Reusing that path for a user-invoked "Generate Profile" click could silently
/// no-op on a document that was already profiled (or that Failed and needs a genuine re-run) — the exact
/// failure the button must never produce. This dispatcher never touches that queue or that key: it calls
/// <see cref="IDocumentProfileAi.ProfileDocumentAsUserAsync"/> directly, which carries NO idempotency gate
/// of its own — every call re-profiles unconditionally. That is precisely the shipped Compose
/// <c>refresh-profile</c> semantics this trigger must mirror (fire-and-forget, 202, unconditional
/// overwrite, no confirmation prompt).
/// </para>
/// <para>
/// <b>Why this is a new class rather than reusing <c>Services/Compose/ComposeProfileDispatcher</c>.</b>
/// That class is <c>internal sealed</c> to the Compose module and hand-constructed (not DI-registered)
/// inside <c>ComposeService</c>'s own constructor — there is no DI seam to inject it from
/// <see cref="OfficeService"/> without changing a file under <c>Services/Compose/</c>, which the task 022
/// POML's escalation trigger forbids (<c>spaarkeai-compose-r8</c> is live on that spine). The two classes
/// run the SAME underlying AI pipeline through the SAME sanctioned facade
/// (<see cref="IDocumentProfileAi"/>) — only the minimal detached-DI-scope fire-and-forget plumbing
/// (necessarily per-module, since each captures its own module's <c>HttpContext</c>) is duplicated. See
/// the §11 reuse-vs-new-component reasoning in
/// <c>projects/spaarkeai-word-add-in-r1/notes/022-generate-profile-trigger.md</c>.
/// </para>
/// <para>
/// ADR-013: depends ONLY on the sanctioned <see cref="IDocumentProfileAi"/> facade under
/// <c>Services/Ai/PublicContracts/</c> — never an AI-internal type (<c>IOpenAiClient</c>,
/// <c>IPlaybookService</c>).
/// </para>
/// <para>
/// ADR-010: no new DI registration. <c>internal sealed</c>, constructed directly by
/// <see cref="OfficeService"/> from optional constructor parameters it already resolves via DI
/// (<see cref="IServiceScopeFactory"/>, <see cref="IDocumentProfileAi"/>,
/// <see cref="IHostApplicationLifetime"/> — all already registered for other consumers).
/// </para>
/// </remarks>
internal sealed class OfficeProfileDispatcher
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IDocumentProfileAi? _documentProfileAi;
    private readonly IHostApplicationLifetime? _appLifetime;
    private readonly ILogger _logger;

    internal OfficeProfileDispatcher(
        IServiceScopeFactory? scopeFactory,
        IDocumentProfileAi? documentProfileAi,
        IHostApplicationLifetime? appLifetime,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _documentProfileAi = documentProfileAi;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches the best-effort OBO document-profile to a detached background scope and returns
    /// immediately — never blocks the caller. The endpoint returns 202 regardless of this method's
    /// return value (mirrors Compose: dispatch is always best-effort, never a precondition of the
    /// response); the return value is for logging/diagnostics only.
    /// </summary>
    internal bool Dispatch(Guid documentId, HttpContext httpContext)
    {
        // Availability gate: no scope factory (unit-test host) or no facade registered (compound AI gate
        // off) — nothing to dispatch.
        if (_scopeFactory is null || _documentProfileAi is null)
        {
            _logger.LogWarning(
                "Office Generate Profile: profile facade unavailable (no IDocumentProfileAi / IServiceScopeFactory) " +
                "— not dispatched for document {DocumentId}.",
                documentId);
            return false;
        }

        // Capture the OBO user assertion (raw Authorization header) BEFORE the request scope disposes.
        // Non-throwing so a token-less request degrades cleanly rather than dispatching a doomed OBO call.
        var authorizationHeader = httpContext.Request?.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Office Generate Profile: no bearer Authorization header on the request for document {DocumentId} " +
                "— background OBO profile not dispatched (best-effort skip).",
                documentId);
            return false;
        }

        // Capture the remaining request-scoped context the profile facade reads. A ClaimsPrincipal is a
        // plain object with no request-tied lifetime, so retaining the reference across the response is safe.
        var user = httpContext.User;
        var correlationId = httpContext.TraceIdentifier;

        // Fire-and-forget: detach from the request scope AND the request CancellationToken. The task is
        // unobserved by design; RunAsync owns its own try/catch so nothing faults the finalizer thread.
        _ = Task.Run(() => RunAsync(documentId, authorizationHeader, user, correlationId));

        return true;
    }

    /// <summary>
    /// The detached (fire-and-forget) body: creates a NEW DI scope, rebuilds a minimal
    /// <see cref="HttpContext"/> from the captured OBO token + claims, resolves
    /// <see cref="IDocumentProfileAi"/> FROM THAT SCOPE, and runs the profile. NEVER throws — a
    /// background profiling failure must not crash the process.
    /// </summary>
    private async Task RunAsync(
        Guid documentId,
        string authorizationHeader,
        ClaimsPrincipal? user,
        string correlationId)
    {
        // App-shutdown token for the detached task — NEVER the (already-completed) request token.
        var ct = _appLifetime?.ApplicationStopping ?? CancellationToken.None;

        try
        {
            await using var scope = _scopeFactory!.CreateAsyncScope();

            var detachedContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                TraceIdentifier = correlationId,
            };
            detachedContext.Request.Headers.Authorization = authorizationHeader;
            if (user is not null)
            {
                detachedContext.User = user;
            }

            var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
            if (accessor is not null)
            {
                accessor.HttpContext = detachedContext;
            }

            // Resolve the facade from the NEW scope — never the request-scope service.
            var facade = scope.ServiceProvider.GetService<IDocumentProfileAi>();
            if (facade is null)
            {
                _logger.LogWarning(
                    "Office Generate Profile: IDocumentProfileAi did not resolve from the detached scope for " +
                    "document {DocumentId} (correlation={CorrelationId}) — skipped.",
                    documentId, correlationId);
                return;
            }

            _logger.LogInformation(
                "Office Generate Profile: starting best-effort OBO profile for document {DocumentId} on user " +
                "request (correlation={CorrelationId}).",
                documentId, correlationId);

            var result = await facade.ProfileDocumentAsUserAsync(documentId, detachedContext, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Office Generate Profile: document {DocumentId} — success={Success} (failure={Failure} " +
                "skip={Skip}) (correlation={CorrelationId}). Profile fields populate on the record now.",
                documentId, result.Success, result.FailureReason ?? "(none)", result.SkipReason ?? "(none)", correlationId);
        }
        catch (Exception ex)
        {
            // Unobserved-exception safe. The response already returned on its own terms; a best-effort
            // background profile failure is logged and swallowed, never rethrown.
            _logger.LogError(ex,
                "Office Generate Profile: threw while profiling document {DocumentId} (correlation={CorrelationId}) " +
                "— best-effort, the request is unaffected.",
                documentId, correlationId);
        }
    }
}
