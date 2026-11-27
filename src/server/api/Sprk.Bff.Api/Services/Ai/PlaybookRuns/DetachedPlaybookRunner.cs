using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Identity.Client;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.PlaybookRuns;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 030 — THE detached caller run (owner decision D-030-run-model; ADR-052 amendment 2026-11-26).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). In the BFF, beside the run record store it
// shares the run with: the run is per-request work a user started (ADR-052: the placement is unchanged; the amendment allows only this
// run to finish after its request). No queue, job, BackgroundService or other host; no package; one singleton DI registration (the
// in-process run table must outlive a request scope). It reuses the engine's own person-run entry (IPlaybookOrchestrationService
// .ExecuteAsync, the REST route's entry), task 007's authorization and task 005's record writer unchanged (ADR-039: no engine edit).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: Services/Compose/ComposeProfileDispatcher (the house precedent: a detached scope and HttpContext for a best-effort
//     profile, with the #3224 accessor defect); PlaybookRunEndpoints.ExecutePlaybook (runs inside its request).
//   Extension: ComposeProfileDispatcher is a Compose collaborator built by ComposeService, not a shared service, runs a profile not a
//     playbook, suppresses no ExecutionContext flow (#3224) and has no cap, pre-warm or run table; the amendment names ONE helper for
//     this work, and Compose moves onto it in a follow-up (#3224), not here.
//   Cost of doing nothing: a playbook run started from Copilot either dies with the MCP request (the stateless host ends it at the
//     reply) or runs on the request's disposed HttpContext, which is cross-user OBO (task 007 verifier, F1).
// =====================================================================================================================

/// <summary>
/// The one shared detached-run helper (ADR-052 amendment 2026-11-26): runs a JPS playbook run a user started after that user's request
/// has answered, acting as the user, and keeps its state in this process's memory for the user to read.
/// </summary>
/// <remarks>
/// <para><b>The seven conditions, where each is met.</b></para>
/// <list type="number">
/// <item><b>Which work:</b> only <see cref="StartAsCallerAsync"/>, a JPS playbook run the caller started (no timer, webhook or system
/// entry).</item>
/// <item><b>One helper:</b> this class is the only production code that builds a detached <see cref="HttpContext"/> (ArchTest
/// <c>DetachedHttpContextGuardTests</c>, with the Compose and Decision baseline).</item>
/// <item><b>Memory only:</b> the caller's bearer header lives on the detached context and the captured launch only. It is never logged,
/// cached, stored or put in an exception; log lines carry the outcome, reason code, latency and correlation id (NFR-05).</item>
/// <item><b>No leak into the original request:</b> <see cref="ExecutionContext.SuppressFlow"/> wraps the launch, so setting
/// <see cref="IHttpContextAccessor.HttpContext"/> inside the detached flow never clears the request's own holder (the #3224 defect).</item>
/// <item><b>Bounded:</b> <see cref="PlaybookRunLimits.MaxRunDuration"/> (on <see cref="TimeProvider"/>) and
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/> cancel the run. The cap bounds the run; it does not keep the caller's
/// token valid (that token may have only minutes left at the start). Graph and Dataverse OBO tokens are obtained before the request
/// answers, and the run's later exchanges are answered from the caches that pre-warm fills (MSAL's per-assertion OBO cache; the Graph
/// Redis cache, TTL = the token's own expiry less 5 minutes, at most 55). A miss fails closed: a switch to another confidential client
/// in <c>OrderedCredentialClientProvider</c> starts with an empty cache, and a node may reach a Dataverse host other than the pre-warmed
/// one (<c>EnvironmentUrl</c> differing from <c>ServiceUrl</c>). An authentication failure ends the run as failed
/// (<see cref="AuthExpiredReasonCode"/>); nothing is retried, and nothing here calls the app-only entry. A caller may have at most
/// <see cref="PlaybookRunLimits.MaxConcurrentRunsPerCaller"/> runs running in this process (<see cref="RunLimitReasonCode"/>).</item>
/// <item><b>Authorized first:</b> task 007's method decides inside the request; the exact <c>documentIds</c> and <c>parameters</c>
/// instances it decided on are the run's, with <c>RunUserId</c> = its <c>RunUserId</c> and <c>StartedBy = Person</c>.</item>
/// <item><b>Visible:</b> this process's run table, then the engine's status read (task 005's durable record); a record left Running by a
/// lost run is mapped to interrupted by the status tool.</item>
/// </list>
/// </remarks>
internal sealed class DetachedPlaybookRunner : IPlaybookRunAi
{
    /// <summary>The caller's delegated access could not be obtained at the start (pre-warm failed). Nothing ran.</summary>
    internal const string AuthUnavailableReasonCode = PlaybookRunReasonCodes.AuthUnavailable;

    /// <summary>The run's delegated access failed mid-run (expired assertion, consent or Conditional Access challenge).</summary>
    internal const string AuthExpiredReasonCode = PlaybookRunReasonCodes.AuthExpired;

    /// <summary>The run reached <see cref="PlaybookRunLimits.MaxRunDuration"/>.</summary>
    internal const string TimeLimitReasonCode = PlaybookRunReasonCodes.TimeLimit;

    /// <summary>The app shut down while the run was in flight.</summary>
    internal const string HostStoppingReasonCode = PlaybookRunReasonCodes.HostStopping;

    /// <summary>The engine did not accept the run within <see cref="PlaybookRunLimits.AcceptBudget"/>.</summary>
    internal const string NotAcceptedReasonCode = PlaybookRunReasonCodes.NotAccepted;

    /// <summary>The run failed (the engine's own terminal code when it names none).</summary>
    internal const string FailedReasonCode = PlaybookRunReasonCodes.Failed;

    /// <summary>The caller already has <see cref="PlaybookRunLimits.MaxConcurrentRunsPerCaller"/> runs running here. Nothing ran.</summary>
    internal const string RunLimitReasonCode = PlaybookRunReasonCodes.RunLimit;

    /// <summary>Task 037d: a start that did not start deleted the empty analysis it created (as the caller).</summary>
    internal const string EmptyAnalysisDeletedOutcome = "playbook_run_empty_analysis_deleted";

    /// <summary>Task 037d: that delete failed or timed out; the empty analysis stays (the caller's own record).</summary>
    internal const string EmptyAnalysisDeleteFailedOutcome = "playbook_run_empty_analysis_delete_failed";

    /// <summary>Task 037d: the bound on the cleanup delete, which runs on its own token (the caller's may be the cancelled one).</summary>
    internal static readonly TimeSpan EmptyAnalysisDeleteBudget = TimeSpan.FromSeconds(10);

    /// <summary>How long a finished run stays in the run table (the durable record answers after that).</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, DetachedRun> _runs = new();
    private readonly Dictionary<string, int> _pendingStarts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _pendingGate = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly IAnalysisStartAi _analyses;
    private readonly ILogger<DetachedPlaybookRunner> _logger;

    public DetachedPlaybookRunner(
        IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime,
        TimeProvider time,
        IAnalysisStartAi analyses,
        ILogger<DetachedPlaybookRunner> logger)
    {
        _scopes = scopes;
        _lifetime = lifetime;
        _time = time;
        _analyses = analyses;
        _logger = logger;
    }

    /// <summary>The name of the analysis a Copilot run creates for its results (task 037a): fixed text and the start time, no record text.</summary>
    internal static string AnalysisNameOf(DateTimeOffset startedAt) =>
        $"Copilot playbook run {startedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC";

    /// <inheritdoc />
    public async Task<PlaybookRunStartResult> StartAsCallerAsync(
        HttpContext http,
        Guid playbookId,
        Guid[] documentIds,
        IReadOnlyDictionary<string, string>? parameters,
        string? userContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(documentIds);
        Prune();

        // Condition 5: at most MaxConcurrentRunsPerCaller runs per caller in this process, checked before anything else. Starts still
        // in flight (authorizing, pre-warming, waiting for the accept) hold a reservation, taken atomically with the count, so starts
        // that arrive together cannot all pass. A run is briefly counted twice (reserved and running) between its accept and the
        // release below; that errs towards refusing.
        var requester = CallerResolution.ResolveObjectId(http.User);
        if (requester is { } reserving && !TryReserveStart(reserving))
        {
            _logger.LogInformation("Detached playbook run not started - Outcome: {Outcome}, CorrelationId: {CorrelationId}", RunLimitReasonCode, http.TraceIdentifier);
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.RunLimit, null, null);
        }

        try
        {
            return await StartReservedAsync(http, playbookId, documentIds, parameters, userContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (requester is { } reserved)
            {
                ReleaseStart(reserved);
            }
        }
    }

    private bool TryReserveStart(string callerOid)
    {
        lock (_pendingGate)
        {
            _pendingStarts.TryGetValue(callerOid, out var pending);
            if (RunningCount(callerOid) + pending >= PlaybookRunLimits.MaxConcurrentRunsPerCaller)
            {
                return false;
            }

            _pendingStarts[callerOid] = pending + 1;
            return true;
        }
    }

    private void ReleaseStart(string callerOid)
    {
        lock (_pendingGate)
        {
            if (_pendingStarts.TryGetValue(callerOid, out var pending) && pending > 1)
            {
                _pendingStarts[callerOid] = pending - 1;
            }
            else
            {
                _pendingStarts.Remove(callerOid);
            }
        }
    }

    private async Task<PlaybookRunStartResult> StartReservedAsync(
        HttpContext http,
        Guid playbookId,
        Guid[] documentIds,
        IReadOnlyDictionary<string, string>? parameters,
        string? userContext,
        CancellationToken cancellationToken)
    {
        // Condition 6: authorized first, inside the request, by task 007's one method; no check of our own.
        var authorization = await PlaybookAuthorizationFilter
            .AuthorizePlaybookRunAsync(http, playbookId, documentIds, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.RunUserId is not { } runUserId)
        {
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.Refused, authorization, null);
        }

        // Condition 1: capture what the run acts with while the request is alive (principal, bearer header, trace id).
        var bearer = http.Request.Headers.Authorization.ToString();
        var callerOid = CallerResolution.ResolveObjectId(http.User);
        if (callerOid is null || !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.Refused, PlaybookRunAuthorization.UserContextRequired, null);
        }

        // Condition 5: the delegated downstream tokens, obtained while the request is still authenticated.
        if (!await PrewarmAsync(http, cancellationToken).ConfigureAwait(false))
        {
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.AuthUnavailable, authorization, null);
        }

        // Task 037a (D-037-engine): the analysis that holds the run's results, created as the caller by the route's own create
        // (IAnalysisStartAi), on the run's first document (007 refuses an empty list), BEFORE the run starts. A refused create starts
        // nothing. The id comes only from this create: no caller argument reaches PlaybookRunRequest.AnalysisId.
        if (documentIds.Length == 0)
        {
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.Refused, authorization, null);
        }

        // Task 037 review K-1: with AI off the engine is the null engine, so no run can start; answer Unavailable (what the launch would
        // end in) BEFORE the create, so no empty analysis is left behind. The engine is resolved as ReadAsCallerAsync resolves it.
        if (http.RequestServices?.GetService<IPlaybookOrchestrationService>() is NullPlaybookOrchestrationService)
        {
            _logger.LogInformation("Detached playbook run not started - Outcome: {Outcome}, CorrelationId: {CorrelationId}",
                nameof(PlaybookRunStartOutcome.Unavailable), http.TraceIdentifier);
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.Unavailable, authorization, null);
        }

        var analysis = await _analyses
            .StartAsCallerAsync(http, AnalysisNameOf(_time.GetUtcNow()), documentIds[0], playbookId, cancellationToken)
            .ConfigureAwait(false);
        if (analysis is not { Outcome: AnalysisStartOutcome.Created, AnalysisId: { } analysisId })
        {
            _logger.LogInformation("Detached playbook run not started - Outcome: {Outcome}, AnalysisOutcome: {AnalysisOutcome}, ReasonCode: {ReasonCode}, CorrelationId: {CorrelationId}",
                nameof(PlaybookRunStartOutcome.AnalysisRefused), analysis.Outcome, analysis.ReasonCode, http.TraceIdentifier);
            return new PlaybookRunStartResult(PlaybookRunStartOutcome.AnalysisRefused, authorization, null, analysis);
        }

        // Task 037d (D-037-cleanup): from here the analysis exists. Every exit that is not the Started return stops the run (when there
        // is one) and then deletes this analysis as the caller, so a start that does not start leaves nothing behind.
        Launch? launch = null;
        var started = false;
        try
        {
            launch = new Launch(
                new PlaybookRunRequest
                {
                    PlaybookId = playbookId,
                    DocumentIds = documentIds,         // the exact instance authorized above
                    Parameters = parameters,           // the exact instance authorized above
                    UserContext = userContext,         // the caller's focus text (task 038): in memory with the request only; never logged here
                    RunUserId = runUserId,             // 007's resolved caller, never Parameters["userId"]
                    StartedBy = PlaybookRunStartedBy.Person,
                    AnalysisId = analysisId,           // task 037a: the analysis created above for this caller, and nothing else
                },
                http.User,
                bearer,
                http.TraceIdentifier,
                callerOid,
                _time,
                _lifetime.ApplicationStopping);

            // Condition 4: no ExecutionContext flow, so the detached flow's accessor write never touches this request's holder.
            var detachedLaunch = launch;
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(() => RunDetachedAsync(detachedLaunch));
            }

            Guid? runId;
            try
            {
                runId = await launch.Accepted.Task.WaitAsync(PlaybookRunLimits.AcceptBudget, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return new PlaybookRunStartResult(PlaybookRunStartOutcome.NotAccepted, authorization, null, analysis);
            }

            // A caller who went away before the run was reported to anyone throws out of the wait above; the finally stops the run, so a
            // retry never leaves two runs, and deletes the analysis. The run id missing from the table is Unavailable (the finally cleans up).
            if (runId is { } id && _runs.TryGetValue(id, out var run))
            {
                started = true;
                return new PlaybookRunStartResult(PlaybookRunStartOutcome.Started, authorization, run.Snapshot(), analysis);
            }

            return new PlaybookRunStartResult(PlaybookRunStartOutcome.Unavailable, authorization, null, analysis);
        }
        finally
        {
            if (!started)
            {
                // The run is stopped BEFORE its analysis is deleted (Abandon is idempotent and harmless when the run already ended).
                launch?.Abandon();
                await DeleteEmptyAnalysisAsync(http, analysisId).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Task 037d (D-037-cleanup): deletes the analysis THIS start created, as the caller (the caller's OBO <see cref="IDataverseUserClient"/>
    /// on the request's services; never an app-only delete, ADR-028 A10), because the start did not end <see cref="PlaybookRunStartOutcome.Started"/>
    /// and so nothing will ever fill it. Never throws and never changes the start's answer: a failed delete is logged (outcome code, correlation
    /// id, analysis id, error type or Dataverse error code only; never message text, ADR-015) and the analysis stays, which is the caller's own
    /// empty record. The delete runs on its own bounded token, never the caller's (which may be the cancelled one) and never unbounded.
    /// </summary>
    private async Task DeleteEmptyAnalysisAsync(HttpContext http, Guid analysisId)
    {
        try
        {
            var dataverse = http.RequestServices?.GetService<IDataverseUserClient>();
            if (dataverse is null)
            {
                _logger.LogWarning("Detached playbook run left an empty analysis - Outcome: {Outcome}, AnalysisId: {AnalysisId}, ErrorType: {ErrorType}, CorrelationId: {CorrelationId}",
                    EmptyAnalysisDeleteFailedOutcome, analysisId, "ClientUnavailable", http.TraceIdentifier);
                return;
            }

            using var bound = new CancellationTokenSource(EmptyAnalysisDeleteBudget, _time);
            var deleted = await dataverse
                .DeleteAsync($"{PlaybookRunResultsReader.AnalysisEntitySet}({analysisId:D})", bound.Token)
                .ConfigureAwait(false);
            if (deleted is { IsSuccess: true })
            {
                _logger.LogInformation("Detached playbook run deleted its empty analysis - Outcome: {Outcome}, AnalysisId: {AnalysisId}, CorrelationId: {CorrelationId}",
                    EmptyAnalysisDeletedOutcome, analysisId, http.TraceIdentifier);
                return;
            }

            _logger.LogWarning("Detached playbook run left an empty analysis - Outcome: {Outcome}, AnalysisId: {AnalysisId}, ErrorCode: {ErrorCode}, CorrelationId: {CorrelationId}",
                EmptyAnalysisDeleteFailedOutcome, analysisId, deleted?.ErrorCode ?? "NoResponse", http.TraceIdentifier);
        }
        catch (Exception ex)
        {
            // The exception type only (the bounded token's own cancel included): a message is not ours to vouch for.
            _logger.LogWarning("Detached playbook run left an empty analysis - Outcome: {Outcome}, AnalysisId: {AnalysisId}, ErrorType: {ErrorType}, CorrelationId: {CorrelationId}",
                EmptyAnalysisDeleteFailedOutcome, analysisId, ex.GetType().Name, http.TraceIdentifier);
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookRunView?> ReadAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var callerOid = CallerResolution.ResolveObjectId(http.User);
        if (callerOid is null || runId == Guid.Empty)
        {
            return null;
        }

        Prune();
        if (_runs.TryGetValue(runId, out var live))
        {
            // Another caller's run is the same null as an unknown id (and is never read from the record either).
            return PlaybookRunRecordStore.IsSameOid(live.StartedByOid, callerOid) ? live.Snapshot() : null;
        }

        // The engine's own status read, as GET /api/ai/playbooks/runs/{runId}: in memory, then the durable record for its starter.
        var engine = http.RequestServices?.GetService<IPlaybookOrchestrationService>();
        if (engine is null)
        {
            return null;
        }

        var status = await engine.GetRunStatusAsync(runId, cancellationToken).ConfigureAwait(false);
        if (status is null || !PlaybookRunRecordStore.IsSameOid(status.StartedByOid, callerOid))
        {
            return null;
        }

        var metrics = status.Metrics;
        return new PlaybookRunView(
            status.RunId,
            status.PlaybookId,
            PhaseOf(status.State),
            status.State == PlaybookRunState.Completed ? 100 : null,
            metrics?.CompletedNodes ?? 0,
            metrics?.FailedNodes ?? 0,
            metrics?.SkippedNodes ?? 0,
            ReasonCode: null,
            status.StartedAt,
            status.CompletedAt,
            FromDurableRecord: true);
    }

    /// <inheritdoc />
    public async Task<PlaybookRunResults?> ReadResultsAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var callerOid = CallerResolution.ResolveObjectId(http.User);
        if (callerOid is null)
        {
            return null;
        }

        // The one owner rule (this process's run table, then the engine's status read of the durable record).
        var run = await ReadAsCallerAsync(http, runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        // The run's link to its results: the run table when the run is still in it, else the record's regarding columns, which the server
        // wrote at the start (task 037a). Owner-checked again on the record, so a link is never read for another caller's run.
        PlaybookRunLink? link;
        if (_runs.TryGetValue(runId, out var live) && PlaybookRunRecordStore.IsSameOid(live.StartedByOid, callerOid))
        {
            link = new PlaybookRunLink(
                live.AnalysisId is { } analysisId ? new PlaybookRunRegarding(PlaybookRunRecordStore.AnalysisEntity, analysisId) : null,
                live.DocumentIds);
        }
        else
        {
            var store = http.RequestServices?.GetService<PlaybookRunRecordStore>();
            link = store is null ? null : await store.GetOwnedRunLinkAsync(runId, callerOid, cancellationToken).ConfigureAwait(false);
        }

        return link is null
            ? null
            : await PlaybookRunResultsReader.ReadAsync(http, run, link, this, _logger, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string? RunConsoleLink(HttpContext http, Guid? documentId) =>
        http?.RequestServices?.GetService<HandoffUrlBuilder>()?.BuildConsoleUrl(documentId);

    private static PlaybookRunPhase PhaseOf(PlaybookRunState state) => state switch
    {
        PlaybookRunState.Completed => PlaybookRunPhase.Completed,
        PlaybookRunState.Failed => PlaybookRunPhase.Failed,
        PlaybookRunState.Cancelled => PlaybookRunPhase.Cancelled,
        _ => PlaybookRunPhase.Running,
    };

    /// <summary>
    /// Obtains the caller's delegated Graph and Dataverse tokens through the existing factories while the request is authenticated, so
    /// the run's later exchanges are answered from the caches those factories already keep (MSAL's OBO cache, keyed by the assertion;
    /// the Graph Redis cache, whose TTL follows the token's expiry). Correctness rests on these caches, not on the caller's token staying
    /// valid: an exchange that misses them later fails closed. Any failure here refuses the start: a run that cannot act as the caller
    /// does not start.
    /// </summary>
    private async Task<bool> PrewarmAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var graph = http.RequestServices?.GetService<IGraphClientFactory>();
        var dataverse = http.RequestServices?.GetService<IDataverseUserClient>();
        if (graph is null || dataverse is null)
        {
            _logger.LogWarning("Detached playbook run not started - Outcome: {Outcome}, CorrelationId: {CorrelationId}", AuthUnavailableReasonCode, http.TraceIdentifier);
            return false;
        }

        try
        {
            await graph.ForUserAsync(http, cancellationToken).ConfigureAwait(false);
            var whoAmI = await dataverse.GetAsync("WhoAmI", cancellationToken).ConfigureAwait(false);
            if (!whoAmI.IsSuccess)
            {
                _logger.LogWarning("Detached playbook run not started - Outcome: {Outcome}, ErrorCode: {ErrorCode}, CorrelationId: {CorrelationId}",
                    AuthUnavailableReasonCode, whoAmI.ErrorCode, http.TraceIdentifier);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception type only: an MSAL or HTTP message is never logged here (it is not ours to vouch for).
            _logger.LogWarning("Detached playbook run not started - Outcome: {Outcome}, ErrorType: {ErrorType}, CorrelationId: {CorrelationId}",
                AuthUnavailableReasonCode, ex.GetType().Name, http.TraceIdentifier);
            return false;
        }
    }

    /// <summary>The detached body: a new scope, a detached HttpContext, the engine's person-run entry, drained to the end. Never throws.</summary>
    private async Task RunDetachedAsync(Launch launch)
    {
        DetachedRun? run = null;
        var token = launch.Token;
        var started = _time.GetTimestamp();
        try
        {
            await using var scope = _scopes.CreateAsyncScope();

            // The only detached HttpContext in production code besides the ratchet baseline (condition 2).
            var detached = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = launch.User,
                TraceIdentifier = launch.CorrelationId,
                RequestAborted = token,
            };
            detached.Request.Headers.Authorization = launch.Bearer;

            // Inside the suppressed flow: this sets a holder that belongs to the detached flow alone (condition 4).
            var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
            if (accessor is not null)
            {
                accessor.HttpContext = detached;
            }

            try
            {
                // Task 037a: this scope's run records are regarding the analysis the start created (037-decisions §4: the analysis wins
                // over a record parameter). The store is scoped, so the link stays with this run.
                if (launch.Request.AnalysisId is { } analysisId)
                {
                    scope.ServiceProvider.GetService<PlaybookRunRecordStore>()?.LinkRunsToAnalysis(analysisId);
                }

                var engine = scope.ServiceProvider.GetRequiredService<IPlaybookOrchestrationService>();
                await foreach (var evt in engine.ExecuteAsync(launch.Request, detached, token).ConfigureAwait(false))
                {
                    run ??= Register(evt, launch);
                    run.Apply(evt, _time.GetUtcNow());
                    launch.Accepted.TrySetResult(run.RunId);
                }

                if (token.IsCancellationRequested)
                {
                    // The engine ended the stream itself on our token (RunCancelled, no exception): say which stop it was.
                    var (phase, reason) = launch.CancellationReason();
                    run?.End(phase, reason, _time.GetUtcNow(), overrideCancelled: true);
                }
                else
                {
                    // A stream that ended with no terminal event is not a completed run.
                    run?.End(PlaybookRunPhase.Failed, FailedReasonCode, _time.GetUtcNow(), overrideCancelled: false);
                }
            }
            finally
            {
                if (accessor is not null)
                {
                    accessor.HttpContext = null;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var (phase, reason) = launch.CancellationReason();
            run?.End(phase, reason, _time.GetUtcNow(), overrideCancelled: true);
        }
        catch (Exception ex) when (IsAuthenticationFailure(ex))
        {
            // Fail closed: the run ends here. Nothing is retried, and never as the app.
            run?.End(PlaybookRunPhase.Failed, AuthExpiredReasonCode, _time.GetUtcNow(), overrideCancelled: true);
            _logger.LogWarning("Detached playbook run ended - Outcome: {Outcome}, ErrorType: {ErrorType}, CorrelationId: {CorrelationId}",
                AuthExpiredReasonCode, ex.GetType().Name, launch.CorrelationId);
        }
        catch (Exception ex)
        {
            run?.End(PlaybookRunPhase.Failed, FailedReasonCode, _time.GetUtcNow(), overrideCancelled: true);
            _logger.LogWarning("Detached playbook run ended - Outcome: {Outcome}, ErrorType: {ErrorType}, CorrelationId: {CorrelationId}",
                FailedReasonCode, ex.GetType().Name, launch.CorrelationId);
        }
        finally
        {
            // A run that ended before the engine named it is reported to the waiting start as "no run".
            launch.Accepted.TrySetResult(run?.RunId);
            _logger.LogInformation("Detached playbook run finished - Outcome: {Outcome}, ReasonCode: {ReasonCode}, ElapsedMs: {ElapsedMs}, CorrelationId: {CorrelationId}",
                run?.Phase.ToString() ?? "NotStarted", run?.ReasonCode, (long)_time.GetElapsedTime(started).TotalMilliseconds, launch.CorrelationId);
            launch.Dispose();
        }
    }

    /// <summary>The caller's runs still running in this process's run table (the same oid key as <c>StartedByOid</c>).</summary>
    private int RunningCount(string callerOid) =>
        _runs.Values.Count(r => r.Phase == PlaybookRunPhase.Running && PlaybookRunRecordStore.IsSameOid(r.StartedByOid, callerOid));

    private DetachedRun Register(PlaybookStreamEvent first, Launch launch)
    {
        var run = new DetachedRun(
            first.RunId, first.PlaybookId, launch.CallerOid, _time.GetUtcNow(), launch.Request.AnalysisId, launch.Request.DocumentIds);
        _runs[run.RunId] = run;
        return run;
    }

    /// <summary>Drops finished runs older than <see cref="Retention"/> (and any entry older than the cap plus that), lazily, on use.</summary>
    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var (id, run) in _runs)
        {
            if (run.IsExpired(now))
            {
                _runs.TryRemove(id, out _);
            }
        }
    }

    /// <summary>
    /// An authentication failure: MSAL needs the user (consent, Conditional Access, claims challenge, an expired or revoked grant), or
    /// Entra refused the assertion (AADSTS500133, expired), anywhere in the exception chain.
    /// </summary>
    internal static bool IsAuthenticationFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case MsalUiRequiredException:
                    return true;
                case MsalServiceException service when !string.IsNullOrEmpty(service.Claims)
                                                      || string.Equals(service.ErrorCode, "invalid_grant", StringComparison.Ordinal)
                                                      || service.Message.Contains("AADSTS500133", StringComparison.Ordinal):
                    return true;
            }
        }

        return false;
    }

    /// <summary>What one start hands its detached flow, and the three ways the run can be stopped.</summary>
    private sealed class Launch : IDisposable
    {
        private readonly CancellationTokenSource _abandon = new();
        private readonly CancellationTokenSource _cap;
        private readonly CancellationToken _stopping;
        private readonly CancellationTokenSource _linked;

        public Launch(
            PlaybookRunRequest request,
            ClaimsPrincipal user,
            string bearer,
            string correlationId,
            string callerOid,
            TimeProvider time,
            CancellationToken stopping)
        {
            Request = request;
            User = user;
            Bearer = bearer;
            CorrelationId = correlationId;
            CallerOid = callerOid;
            _stopping = stopping;
            _cap = new CancellationTokenSource(PlaybookRunLimits.MaxRunDuration, time);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(_abandon.Token, _cap.Token, stopping);
        }

        public PlaybookRunRequest Request { get; }

        public ClaimsPrincipal User { get; }

        /// <summary>The caller's Authorization header: memory only, on the detached context; never logged or stored.</summary>
        public string Bearer { get; }

        public string CorrelationId { get; }

        public string CallerOid { get; }

        public CancellationToken Token => _linked.Token;

        public TaskCompletionSource<Guid?> Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Abandon()
        {
            try
            {
                _abandon.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already ended.
            }
        }

        /// <summary>Why the run's token was cancelled: shutdown first, then the cap, else the start gave up on it.</summary>
        public (PlaybookRunPhase Phase, string Reason) CancellationReason() =>
            _stopping.IsCancellationRequested ? (PlaybookRunPhase.Cancelled, HostStoppingReasonCode)
            : _cap.IsCancellationRequested ? (PlaybookRunPhase.Failed, TimeLimitReasonCode)
            : (PlaybookRunPhase.Cancelled, NotAcceptedReasonCode);

        public void Dispose()
        {
            _linked.Dispose();
            _cap.Dispose();
            _abandon.Dispose();
        }
    }

    /// <summary>
    /// One run in this process's run table: counts, state and identifiers only (ADR-015), guarded by one lock. Task 037b: the analysis the
    /// start created and the run's document ids, so a run still in this table is linked to its results without a record read.
    /// </summary>
    private sealed class DetachedRun
    {
        private readonly object _gate = new();
        private int _nodeCount;
        private int _completed;
        private int _failed;
        private int _skipped;
        private DateTimeOffset? _completedAt;

        public DetachedRun(Guid runId, Guid playbookId, string startedByOid, DateTimeOffset startedAt, Guid? analysisId, Guid[] documentIds)
        {
            RunId = runId;
            PlaybookId = playbookId;
            StartedByOid = startedByOid;
            StartedAt = startedAt;
            AnalysisId = analysisId;
            DocumentIds = documentIds;
        }

        /// <summary>Task 037b: the analysis the start created for this run (server-set, never a caller argument).</summary>
        public Guid? AnalysisId { get; }

        /// <summary>Task 037b: the document ids the run was authorized and started on.</summary>
        public Guid[] DocumentIds { get; }

        public Guid RunId { get; }

        public Guid PlaybookId { get; }

        public string StartedByOid { get; }

        public DateTimeOffset StartedAt { get; }

        public PlaybookRunPhase Phase { get; private set; } = PlaybookRunPhase.Running;

        public string? ReasonCode { get; private set; }

        public void Apply(PlaybookStreamEvent evt, DateTimeOffset now)
        {
            lock (_gate)
            {
                switch (evt.Type)
                {
                    case PlaybookEventType.RunStarted:
                        _nodeCount = evt.Metrics?.TotalNodes ?? 0;
                        break;
                    case PlaybookEventType.NodeCompleted:
                        _completed++;
                        break;
                    case PlaybookEventType.NodeFailed:
                        _failed++;
                        break;
                    case PlaybookEventType.NodeSkipped:
                        _skipped++;
                        break;
                    case PlaybookEventType.RunCompleted:
                        TakeMetrics(evt.Metrics);
                        Finish(PlaybookRunPhase.Completed, null, now);
                        break;
                    case PlaybookEventType.RunFailed:
                        TakeMetrics(evt.Metrics);
                        Finish(PlaybookRunPhase.Failed, string.IsNullOrEmpty(evt.ErrorCode) ? FailedReasonCode : evt.ErrorCode, now);
                        break;
                    case PlaybookEventType.RunCancelled:
                        Finish(PlaybookRunPhase.Cancelled, null, now);
                        break;
                }
            }
        }

        /// <summary>
        /// Ends a run that is still running. <paramref name="overrideCancelled"/> also re-labels a run the engine reported cancelled
        /// without a reason (our own token stopped it: the cap, a shutdown, or an abandoned start).
        /// </summary>
        public void End(PlaybookRunPhase phase, string reason, DateTimeOffset now, bool overrideCancelled)
        {
            lock (_gate)
            {
                if (Phase == PlaybookRunPhase.Running || (overrideCancelled && Phase == PlaybookRunPhase.Cancelled && ReasonCode is null))
                {
                    Finish(phase, reason, now);
                }
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (_gate)
            {
                return _completedAt is { } done
                    ? now - done > Retention
                    : now - StartedAt > PlaybookRunLimits.MaxRunDuration + Retention;
            }
        }

        public PlaybookRunView Snapshot()
        {
            lock (_gate)
            {
                int? progress = Phase == PlaybookRunPhase.Completed
                    ? 100
                    : _nodeCount > 0 ? Math.Min(99, (_completed + _failed + _skipped) * 100 / _nodeCount) : null;
                return new PlaybookRunView(
                    RunId, PlaybookId, Phase, progress, _completed, _failed, _skipped, ReasonCode, StartedAt, _completedAt, FromDurableRecord: false);
            }
        }

        private void TakeMetrics(PlaybookRunMetrics? metrics)
        {
            if (metrics is null)
            {
                return;
            }

            _completed = metrics.CompletedNodes;
            _failed = metrics.FailedNodes;
            _skipped = metrics.SkippedNodes;
        }

        private void Finish(PlaybookRunPhase phase, string? reason, DateTimeOffset now)
        {
            Phase = phase;
            ReasonCode = reason;
            _completedAt = now;
        }
    }
}
