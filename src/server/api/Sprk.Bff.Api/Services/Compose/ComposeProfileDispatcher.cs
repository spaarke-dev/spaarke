using System.Diagnostics;
using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Logging;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 5b of the <c>ComposeService</c> decomposition (task 070): when a document gets
/// (re)indexed — the fire-and-forget background profile, and the step signals for the
/// profile-analysis and indexing steps.
///
/// <para><b>Its reason to change</b> is the background-work contract: what detaches from the request,
/// what it carries with it, and how a step that has not finished (or was never attempted) is
/// reported without poisoning the create-on-save aggregate. That is independent of how a save lands
/// bytes.</para>
///
/// <para><b>Why the signal factories live HERE rather than on <c>ComposeService</c>.</b> Three
/// callers outside this cluster use <see cref="ProfileNotAttempted"/> (the save path and two failure
/// projections). The alternative — leaving the factories on the service and calling back into it
/// from this collaborator — makes the dependency circular for no benefit. The signals *describe the
/// profile and indexing steps*, so they belong with the code that owns those steps, and the three
/// outside callers reference them here. One definition, in the place that explains it.</para>
///
/// <para><b><see cref="Indexing"/> is here deliberately.</b> Its single caller is the save path, and
/// it is about indexing rather than the background profile — but cluster 5's reason-to-change is
/// stated as "when a document gets (re)indexed", which covers both. Keeping it with the profile step
/// signals keeps all four step-signal shapes in one readable place rather than splitting a set.</para>
///
/// <para>An <c>internal sealed</c> collaborator built from dependencies <c>ComposeService</c> already
/// holds — <b>no new DI registration</b> (ADR-010). Behaviour is unchanged; this is a move.</para>
/// </summary>
internal sealed class ComposeProfileDispatcher
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IDocumentProfileAi? _documentProfileAi;
    private readonly IHostApplicationLifetime? _appLifetime;
    private readonly ILogger _logger;

    internal ComposeProfileDispatcher(
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
    /// Dispatches the best-effort OBO document-profile to a detached background scope and returns the
    /// step signal to report synchronously. Never blocks the save.
    /// </summary>
    internal StoredStepSignal Dispatch(Guid documentId, HttpContext httpContext)
    {
        // Availability gate: no scope factory (unit-test host) or no facade registered (compound AI gate
        // off) → nothing to dispatch. Report non-terminal not-attempted synchronously.
        if (_scopeFactory is null || _documentProfileAi is null)
        {
            return ProfileNotAttempted(
                "profile facade unavailable (no IDocumentProfileAi / IServiceScopeFactory) — profile not dispatched");
        }

        // Capture the OBO user assertion (raw Authorization header) BEFORE the request scope disposes.
        // Non-throwing (unlike TokenHelper.ExtractBearerToken) so a token-less save degrades cleanly.
        var authorizationHeader = httpContext.Request?.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // No user token to detach — a background OBO download would 401. Never ship a broken detach.
            _logger.LogWarning(
                "Compose create-on-save: no bearer Authorization header on the save request for document {DocumentId} — background OBO profile not dispatched (best-effort skip).",
                documentId);
            return ProfileNotAttempted(
                "no bearer Authorization header on the save request — background OBO profile not dispatched");
        }

        // Capture the remaining request-scoped context the profile facade reads. A ClaimsPrincipal is a
        // plain object with no request-tied lifetime, so retaining the reference across the response is safe.
        var user = httpContext.User;
        var correlationId = httpContext.TraceIdentifier;
        var requestActivity = Activity.Current;

        // Fire-and-forget: detach from the request scope AND the request CancellationToken (constraint #3).
        // The task is unobserved by design; RunAsync owns its own try/catch so nothing faults the
        // finalizer thread. The discard makes the intent explicit to reviewers + analyzers.
        _ = StartDetached(() => RunAsync(documentId, authorizationHeader, user, correlationId, requestActivity));

        return ProfileDispatched();
    }

    /// <summary>
    /// Starts <paramref name="work"/> on the thread pool WITHOUT flowing the caller's
    /// <see cref="ExecutionContext"/> (#2620).
    ///
    /// <para><b>Why flow is suppressed.</b> <see cref="RunAsync"/> assigns
    /// <see cref="IHttpContextAccessor.HttpContext"/>. <c>HttpContextAccessor</c> keeps the context in a
    /// static <c>AsyncLocal</c> holder object, and its setter first nulls the context in the holder
    /// currently flowed in — with a flowed context, that is the ORIGINATING request's holder — before
    /// installing a new one. A plain <c>Task.Run</c> therefore cleared the save / load / refresh-profile
    /// request's <c>HttpContext</c> while that request was still running, so anything after the dispatch
    /// that reads the accessor (e.g. <c>DataverseUserClient</c> → DATAVERSE_USER_CONTEXT_REQUIRED) saw
    /// null. With flow suppressed the detached work starts with no AsyncLocals, and its assignment
    /// installs a holder in its own flow only.</para>
    ///
    /// <para>Suppressing flow also drops the request's trace parent and audit log scope;
    /// <see cref="RunAsync"/> re-establishes both explicitly.</para>
    /// </summary>
    private static Task StartDetached(Func<Task> work)
    {
        // SuppressFlow throws when flow is already suppressed — then Task.Run already does not flow it.
        if (ExecutionContext.IsFlowSuppressed())
        {
            return Task.Run(work);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(work);
        }
    }

    /// <summary>
    /// The detached (fire-and-forget) body of the best-effort OBO document-profile. Creates a NEW DI
    /// scope, rebuilds a minimal <see cref="HttpContext"/> from the captured OBO token + claims, resolves
    /// the <see cref="IDocumentProfileAi"/> facade FROM THAT SCOPE, and runs the profile. NEVER throws —
    /// a background profiling failure must not crash the process (unobserved-exception safe, constraint #4).
    /// </summary>
    private async Task RunAsync(
        Guid documentId,
        string authorizationHeader,
        ClaimsPrincipal? user,
        string correlationId,
        Activity? requestActivity)
    {
        // Constraint #3: use the app-shutdown token, NOT the (already-completed) request token. A profile
        // in flight when the host stops is cut off cleanly; otherwise it runs to completion.
        var ct = _appLifetime?.ApplicationStopping ?? CancellationToken.None;

        // #2620: this runs with ExecutionContext flow suppressed (StartDetached), so re-establish what the
        // request flow used to supply. The trace parent keeps the profile's telemetry correlated with the
        // request — best-effort: Activity.Current rejects an already-stopped activity, which only happens if
        // the request finished before this task started. The audit scope is AuditEnrichmentMiddleware's
        // own (oid / appid / obo / tenantId / correlationId), rebuilt from the captured caller.
        if (requestActivity is { IsStopped: false })
        {
            Activity.Current = requestActivity;
        }

        var auditScope = AuditEnrichmentMiddleware.BuildAuditScope(user, correlationId);
        using var auditLogScope = auditScope is null ? null : _logger.BeginScope(auditScope);

        try
        {
            await using var scope = _scopeFactory!.CreateAsyncScope();

            // Rebuild a minimal HttpContext carrying ONLY what the profile path reads: the OBO user
            // assertion (Authorization header), the User claims (runContext.TenantId + tenant-cache
            // scoping), and the correlation id. Backed by the fresh scope's provider so any
            // RequestServices lookup resolves against the detached (non-disposed) scope.
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

            // Keep AnalysisDocumentLoader's IHttpContextAccessor-based tenant-cache scoping coherent
            // (else it degrades to the documented "system" sentinel — acceptable, but this is tidier).
            // Safe ONLY because this flow was started with ExecutionContext flow suppressed: the setter
            // nulls the holder currently flowed in, which must never be the originating request's (#2620).
            var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
            if (accessor is not null)
            {
                accessor.HttpContext = detachedContext;
            }

            // Constraint #1: resolve the facade from the NEW scope — never the request-scope service.
            var facade = scope.ServiceProvider.GetService<IDocumentProfileAi>();
            if (facade is null)
            {
                _logger.LogWarning(
                    "Compose background profile: IDocumentProfileAi did not resolve from the detached scope for document {DocumentId} (correlation={CorrelationId}) — skipped.",
                    documentId, correlationId);
                return;
            }

            _logger.LogInformation(
                "Compose background profile: starting best-effort OBO profile for document {DocumentId} (correlation={CorrelationId}).",
                documentId, correlationId);

            var result = await facade.ProfileDocumentAsUserAsync(documentId, detachedContext, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Compose background profile: document {DocumentId} — success={Success} (failure={Failure} skip={Skip}) (correlation={CorrelationId}). Profile fields populate on the record now.",
                documentId, result.Success, result.FailureReason ?? "(none)", result.SkipReason ?? "(none)", correlationId);
        }
        catch (Exception ex)
        {
            // Constraint #4: unobserved-exception safe. The save already returned on its own terms; a
            // best-effort background profile failure is logged and swallowed, never rethrown.
            _logger.LogError(ex,
                "Compose background profile: threw while profiling document {DocumentId} (correlation={CorrelationId}) — best-effort, the save is unaffected.",
                documentId, correlationId);
        }
    }

    /// <summary>Non-terminal (Running) profile-analysis signal for the fire-and-forget path: the profile
    /// was DISPATCHED to a background scope and runs best-effort AFTER the save returns (the 7
    /// sprk_document profile fields populate shortly after). <c>Started=true</c> + no stored status →
    /// projects to <see cref="JobAwareState.Running"/>, so the returned aggregate reads Partial (record +
    /// index exist, profile pending), never Completed-on-claim and never Failed.</summary>
    internal static StoredStepSignal ProfileDispatched() => new()
    {
        StepName = ComposeService.StepProfileAnalysis,
        StoredStatus = null,
        Started = true,
        Detail = "profile-analysis dispatched to background (best-effort); the profile fields populate shortly after the save returns",
    };

    /// <summary>A non-terminal profile-analysis signal for when the profile was NOT attempted
    /// (container/record step never produced a record, or no facade injected). Non-terminal so it
    /// never poisons the create-on-save aggregate.</summary>
    internal static StoredStepSignal ProfileNotAttempted(string detail) => new()
    {
        StepName = ComposeService.StepProfileAnalysis,
        StoredStatus = null,
        Started = false,
        Detail = detail,
    };

    /// <summary>Maps the indexing enqueue outcome to a stored step signal: submitted (sync-OBO ran)
    /// → Completed; failed → terminal Failed (single attempt, so never RetryPending); skipped →
    /// non-terminal (no stored outcome) so the record is never a success without an index.</summary>
    internal static StoredStepSignal Indexing(PostUploadIndexingResult result)
    {
        if (result.JobSubmitted)
        {
            return new StoredStepSignal { StepName = ComposeService.StepIndexing, StoredStatus = JobStatus.Completed, Started = true };
        }

        if (result.FailureReason is not null)
        {
            return new StoredStepSignal
            {
                StepName = ComposeService.StepIndexing,
                StoredStatus = JobStatus.Failed,
                Started = true,
                Attempt = 1,
                MaxAttempts = 1,   // no retry budget → terminal Failed, not RetryPending
                Detail = $"indexing failed: {result.FailureReason}",
            };
        }

        // Skipped (feature flag off / non-indexable / empty / missing tenant): not indexed →
        // not a terminal success. Keep it non-terminal so the aggregate never reads Completed.
        return new StoredStepSignal
        {
            StepName = ComposeService.StepIndexing,
            StoredStatus = null,
            Started = false,
            Detail = $"indexing skipped: {result.SkipReason}",
        };
    }
}
