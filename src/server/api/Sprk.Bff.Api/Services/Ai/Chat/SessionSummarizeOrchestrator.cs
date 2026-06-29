using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Convergence orchestrator for the chat-driven Summarize-for-Chat vertical slice. Bridges
/// the two valid entry points for chat-driven Summarize — the direct endpoint
/// (<c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>) AND the
/// natural-language agent-tool dispatch — by ensuring BOTH paths delegate to a SINGLE
/// convergence method (<see cref="SummarizeSessionFilesAsync"/>) on this class, which now
/// dispatches through the canonical playbook-orchestration triangle per ADR-013 (R7 task 091).
/// </summary>
/// <remarks>
/// <para>
/// <b>R7 task 091 (FR-17) refactor</b>: removed the chat-streaming-specific
/// <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/> dispatch in favor of
/// the canonical <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> per ADR-013.
/// Per task 090 design (notes/spikes/chat-summarize-migration-design.md) Option 1: the
/// orchestrator injects <see cref="IPlaybookOrchestrationService"/> directly (in-zone
/// per ADR-013 — <c>Services/Ai/Chat/</c> is AI-internal territory; the
/// <see cref="IInvokePlaybookAi"/> facade aggregates internally and would have eliminated
/// the per-token <see cref="FieldDelta"/> progressive UX). An inline SSE adapter
/// translates <see cref="PlaybookStreamEvent"/> → <see cref="AnalysisChunk"/> sequence,
/// preserving byte-equivalent on-the-wire shape for the chat client.
/// </para>
/// <para>
/// <b>What stayed here</b> (orchestrator boundary responsibilities, unchanged from R5/R6):
/// <list type="bullet">
///   <item>Public <see cref="SummarizeSessionFilesAsync"/> signature — UNCHANGED for downstream
///         consumers (<c>SummarizeSessionEndpoint</c> + future agent-tool dispatch).</item>
///   <item>Session lookup via <see cref="ChatSessionManager.GetSessionAsync"/> — orchestrator
///         remains the chat-session boundary.</item>
///   <item>Argument validation at the chat-orchestration boundary (tenant + session non-empty;
///         null request; ≤20-file cap per NFR-02).</item>
///   <item>FR-1R-05 routing-table → typed-options fallback resolution chain (verbatim from
///         chat-routing-redesign-r1 task 028d).</item>
///   <item>Null-Object kill-switch subclass (<see cref="NullSessionSummarizeOrchestrator"/>) —
///         construction via the protected ctor still works; override behavior unchanged.</item>
/// </list>
/// </para>
/// <para>
/// <b>What changed (R7 task 091)</b>:
/// <list type="bullet">
///   <item>Constructor swaps <see cref="IPlaybookExecutionEngine"/> for
///         <see cref="IPlaybookOrchestrationService"/> +
///         <see cref="IHttpContextAccessor"/>.</item>
///   <item>FR-04 multi-file combined-summary interjection moved to this orchestrator — emitted
///         BEFORE the playbook orchestration stream (it was previously inside the engine).</item>
///   <item>Inline SSE adapter projects <see cref="PlaybookStreamEvent"/> events into
///         <see cref="AnalysisChunk"/> shape: <c>NodeProgress</c> → <c>FromContent</c>,
///         terminal <c>NodeCompleted</c> with structured output → <c>Completed</c>,
///         <c>RunFailed/RunCancelled/NodeFailed</c> → <c>FromError</c>. Section + node-level
///         events that don't map cleanly to the AnalysisChunk envelope are filtered out.</item>
/// </list>
/// </para>
/// <para>
/// <b>FR-26 invariant</b>: the alternate-key bypass (<c>sprk_actioncode = "SUM-CHAT@v1"</c>)
/// is fully removed from the chat /summarize code path. Verification: no remaining
/// <c>SummarizeActionCode</c>, <c>ActionEntityLogicalName</c>, or
/// <c>RetrieveByAlternateKeyAsync</c> reference in this orchestrator.
/// </para>
/// <para>
/// <b>FR-05 stable-ID resolution</b> (chat-routing-redesign-r1 task 015) + <b>FR-1R-05
/// routing-table migration</b> (chat-routing-redesign-r1 task 028d): playbook ID resolved
/// at runtime by preferring <see cref="IConsumerRoutingService.ResolveAsync"/> with
/// <see cref="ConsumerTypes.ChatSummarize"/>, falling back to
/// <see cref="WorkspaceOptions.ChatSummarizePlaybookId"/> when the routing table returns
/// null (graceful-degrade for the FR-1R-06 deprecation window). 5-min TTL routing-cache
/// (ADR-014) preserved; spec NFR-04 — no new invalidation logic introduced.
/// </para>
/// <para>
/// <b>ADR-010</b>: concrete class with NO orchestrator-authored interface (unit tests target the
/// concrete type per ADR-010 — interface-for-testability-alone is explicitly forbidden).
/// Non-sealed to permit the <see cref="NullSessionSummarizeOrchestrator"/> kill-switch subclass
/// (ADR-030 P3 Fail-Fast, registered in
/// <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c>).
/// </para>
/// <para>
/// <b>ADR-013 placement</b>: orchestrator lives in <c>Services/Ai/Chat/</c> (in-zone AI
/// territory). Per refined ADR-013 (2026-05-20), in-zone code MAY inject AI-internal types
/// (<see cref="IPlaybookOrchestrationService"/>) when the consumer use case demands it.
/// Per-token <see cref="FieldDelta"/> streaming UX is the use case here — the
/// <see cref="IInvokePlaybookAi"/> facade aggregates internally to a single
/// <c>PlaybookInvocationResult</c> and is unsuitable for progressive SSE rendering.
/// External CRUD code is still forbidden from injecting orchestration internals; the
/// facade rule applies to the CRUD boundary, not in-zone consumers.
/// </para>
/// </remarks>
public class SessionSummarizeOrchestrator
{
    /// <summary>FR-04 — multi-file combined-summary interjection emitted as the first
    /// <see cref="AnalysisChunk.FromContent"/> chunk when the resolved file set is ≥2.</summary>
    private const string CombinedSummaryInterjection =
        "Multiple files selected — generating a combined summary across all of them.";

    private readonly ChatSessionManager _sessionManager;
    private readonly IPlaybookOrchestrationService _orchestrationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPlaybookLookupService _playbookLookup;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IOptions<WorkspaceOptions> _workspaceOptions;
    private readonly ILogger<SessionSummarizeOrchestrator> _logger;

    public SessionSummarizeOrchestrator(
        ChatSessionManager sessionManager,
        IPlaybookOrchestrationService orchestrationService,
        IHttpContextAccessor httpContextAccessor,
        IPlaybookLookupService playbookLookup,
        IConsumerRoutingService consumerRouting,
        IOptions<WorkspaceOptions> workspaceOptions,
        ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _playbookLookup = playbookLookup ?? throw new ArgumentNullException(nameof(playbookLookup));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _workspaceOptions = workspaceOptions ?? throw new ArgumentNullException(nameof(workspaceOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullSessionSummarizeOrchestrator"/> so the
    /// kill-switch subclass can be constructed when the compound AI gate is OFF and
    /// AI dependencies (<see cref="IPlaybookOrchestrationService"/>) are absent or not yet
    /// resolvable. The Null override never reads the nulled fields — it throws
    /// <see cref="Configuration.FeatureDisabledException"/> before they are dereferenced.
    /// Matches the canonical pattern in <see cref="SprkChatAgentFactory"/> /
    /// <see cref="PendingPlanManager"/>.
    /// </summary>
    protected SessionSummarizeOrchestrator(ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = null!;
        _orchestrationService = null!;
        _httpContextAccessor = null!;
        _playbookLookup = null!;
        _consumerRouting = null!;
        _workspaceOptions = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// THE convergence method. Both the direct endpoint AND the agent-tool handler delegate
    /// to THIS method — no other entry point is exposed.
    /// </summary>
    /// <param name="request">The chat-session-scoped Summarize request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// SSE-shaped chunks (FR-04 interjection for multi-file requests → zero or more
    /// per-token / per-field events → terminal
    /// <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> or
    /// <see cref="AnalysisChunk.FromError"/>). The exact chunk shapes are produced by an
    /// inline adapter that projects <see cref="PlaybookStreamEvent"/> stream from
    /// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> into the
    /// <see cref="AnalysisChunk"/> envelope.
    /// </returns>
    public virtual async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
        SummarizeSessionFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");

        // NFR-02 — early hard cap (defense-in-depth at the orchestrator boundary). Surfaces
        // ArgumentException to the endpoint mapping layer for 400 ProblemDetails — same shape
        // callers expected pre-R7.
        if (request.FileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Session Summarize request exceeds the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {request.FileIds.Count} file IDs.",
                nameof(request));
        }

        // Load the chat session at the orchestrator boundary. Session-not-found surfaces as
        // InvalidOperationException → endpoint maps to 404 (pre-R7 behavior preserved).
        ChatSession? session = await _sessionManager
            .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Chat session '{request.SessionId}' not found for tenant '{request.TenantId}'.");
        }

        var uploadedFiles = session.UploadedFiles ?? Array.Empty<ChatSessionFile>();
        var resolvedFileIds = ResolveEffectiveFileIds(request.FileIds, uploadedFiles);

        // FR-1R-05 routing-table resolution (chat-routing-redesign-r1 task 028d): prefer
        // IConsumerRoutingService.ResolveAsync(ConsumerTypes.ChatSummarize) which reads the
        // owner-managed sprk_playbookconsumer Dataverse table (5-min TTL cache per ADR-014).
        // When the routing table returns null (no matching row), fall back to the FR-05
        // stable-ID resolution path (WorkspaceOptions.ChatSummarizePlaybookId via
        // IPlaybookLookupService.GetByIdAsync) for the FR-1R-06 deprecation window. When BOTH
        // are unavailable, fail fast — this is the chat /summarize convergence point per
        // R6 FR-26 / FR-30 and must not silently downgrade.
        //
        // Hardening (code-review S-5): pass ConsumerTypes.ChatSummarize, NEVER the literal
        // string "chat-summarize" — compile-time typo defense.
        Guid resolvedPlaybookId;
        var routedPlaybookId = await _consumerRouting
            .ResolveAsync(ConsumerTypes.ChatSummarize, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (routedPlaybookId.HasValue && routedPlaybookId.Value != Guid.Empty)
        {
            resolvedPlaybookId = routedPlaybookId.Value;
            _logger.LogDebug(
                "FR-1R-05: SessionSummarizeOrchestrator resolved chat-summarize playbook via " +
                "IConsumerRoutingService (playbookId={PlaybookId})",
                resolvedPlaybookId);
        }
        else
        {
            // Graceful-degrade to FR-05 typed-options path. 028e will tag this fallback
            // with deprecation telemetry; for now, the behavior matches pre-028d verbatim.
            var configuredPlaybookId = _workspaceOptions.Value.ChatSummarizePlaybookId;
            if (string.IsNullOrWhiteSpace(configuredPlaybookId))
            {
                _logger.LogError(
                    "FR-1R-05 fallback: IConsumerRoutingService returned null AND " +
                    "Workspace:ChatSummarizePlaybookId is not configured. Cannot resolve " +
                    "chat-summarize playbook for tenant={TenantId} session={SessionId}. " +
                    "Either seed a sprk_playbookconsumer row for consumertype='chat-summarize', " +
                    "or configure the per-environment GUID (mirrors sprk_analysisplaybookid PK) " +
                    "for the summarize-document-for-chat@v1 row.",
                    request.TenantId, request.SessionId);
                throw new InvalidOperationException(
                    "Chat /summarize cannot resolve its playbook: routing-table lookup returned " +
                    "null and Workspace:ChatSummarizePlaybookId fallback is not configured.");
            }

            var playbook = await _playbookLookup
                .GetByIdAsync(configuredPlaybookId, cancellationToken)
                .ConfigureAwait(false);
            resolvedPlaybookId = playbook.Id;
            _logger.LogDebug(
                "FR-1R-05 fallback: SessionSummarizeOrchestrator resolved chat-summarize playbook via " +
                "WorkspaceOptions.ChatSummarizePlaybookId + IPlaybookLookupService (playbookId={PlaybookId})",
                resolvedPlaybookId);
        }

        // FR-04 — multi-file combined-summary interjection emitted BEFORE the playbook stream.
        // Moved into the orchestrator at R7 task 091 (previously inside PlaybookExecutionEngine
        // when the orchestrator forwarded to ExecuteChatSummarizeAsync). The interjection is a
        // user-facing UX signal; the playbook stream that follows is the actual structured
        // summarization output.
        if (resolvedFileIds.Count >= 2)
        {
            yield return AnalysisChunk.FromContent(CombinedSummaryInterjection);
        }

        // Build the PlaybookRunRequest. Parameters carry the session-files manifest +
        // discriminators per task 090 design §3.4 — deterministic identifiers only (ADR-015).
        var playbookRequest = new PlaybookRunRequest
        {
            PlaybookId = resolvedPlaybookId,
            // Session-files filter passes through parameters (the chat-summarize playbook's
            // RAG node reads sessionFilesManifest + sessionId); DocumentIds remains empty.
            DocumentIds = Array.Empty<Guid>(),
            Parameters = BuildParameters(request, session, uploadedFiles, resolvedFileIds)
        };

        // Resolve HttpContext for OBO auth in downstream node executors (ADR-013 — HttpContext
        // is an ASP.NET primitive, not an AI-internal type per ADR-013 §). Per task 090 design
        // §3.3 Option A: inject IHttpContextAccessor (preserves the orchestrator's public
        // surface — caller doesn't need to change to pass HttpContext explicitly).
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "SessionSummarizeOrchestrator requires an active HttpContext for OBO authentication " +
                "in downstream playbook node executors. HttpContextAccessor.HttpContext was null " +
                "— is the orchestrator being invoked outside of an HTTP request scope?");

        _logger.LogDebug(
            "R7 SessionSummarizeOrchestrator dispatching to IPlaybookOrchestrationService.ExecuteAsync " +
            "(playbookId={PlaybookId} tenant={TenantId} session={SessionId} fileCount={FileCount} path={Path})",
            resolvedPlaybookId, request.TenantId, request.SessionId, resolvedFileIds.Count,
            request.Path.ToTelemetryValue());

        // Stream events from the orchestration service and project each into the AnalysisChunk
        // envelope via the inline SSE adapter (TranslateEventToChunk). Events that don't map
        // cleanly (e.g., NodeStarted, RunStarted) are filtered out — only events with a
        // user-visible payload reach the chat client. Per-token FieldDelta UX is preserved
        // because we yield as events arrive (no aggregation).
        await foreach (var ev in _orchestrationService
            .ExecuteAsync(playbookRequest, httpContext, cancellationToken)
            .ConfigureAwait(false))
        {
            var chunk = TranslateEventToChunk(ev);
            if (chunk is not null)
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// SSE adapter — projects a <see cref="PlaybookStreamEvent"/> into the
    /// <see cref="AnalysisChunk"/> envelope shape that <see cref="SummarizeSessionEndpoint"/>
    /// already writes to the wire. Returns <c>null</c> for events that have no chat-client-visible
    /// payload (lifecycle events like RunStarted / NodeStarted), so the caller can filter them
    /// without inflating the SSE stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mapping table</b> (R7 task 091):
    /// </para>
    /// <list type="table">
    ///   <listheader><term>PlaybookStreamEvent.Type</term><description>AnalysisChunk projection</description></listheader>
    ///   <item><term><see cref="PlaybookEventType.NodeProgress"/></term>
    ///     <description><see cref="AnalysisChunk.FromContent"/> with the event's Content payload —
    ///     this is the per-token streaming surface the chat UX relies on for progressive rendering.</description></item>
    ///   <item><term><see cref="PlaybookEventType.NodeCompleted"/> + DeliverOutput + StructuredData</term>
    ///     <description><see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> when the terminal
    ///     node's structured output deserializes to <see cref="DocumentAnalysisResult"/>; else
    ///     <see cref="AnalysisChunk.Completed(string)"/> with TextContent. Mid-run NodeCompleted
    ///     events are filtered (returns null) — only the terminal DeliverOutput surfaces a
    ///     "complete" event.</description></item>
    ///   <item><term><see cref="PlaybookEventType.RunFailed"/> / <see cref="PlaybookEventType.NodeFailed"/></term>
    ///     <description><see cref="AnalysisChunk.FromError"/> with the orchestration-layer error
    ///     message.</description></item>
    ///   <item><term><see cref="PlaybookEventType.RunCancelled"/></term>
    ///     <description><see cref="AnalysisChunk.FromError"/> ("Summarization was cancelled.").</description></item>
    ///   <item><term>All other event types</term>
    ///     <description><c>null</c> (filtered out — RunStarted/RunCompleted/NodeStarted/NodeSkipped/
    ///     SectionStarted/SectionData/SectionCompleted/UnrenderedTemplateDetected have no
    ///     AnalysisChunk equivalent in the chat-summarize wire contract).</description></item>
    /// </list>
    /// <para>
    /// <b>Per-token UX preservation</b>: the chat-summarize playbook's AI node emits
    /// <see cref="PlaybookEventType.NodeProgress"/> events per LLM-streamed token (the
    /// existing node-executor contract). Each maps to one <see cref="AnalysisChunk.FromContent"/>
    /// emission, preserving the byte-equivalent on-the-wire cadence the chat client UX depends on.
    /// </para>
    /// </remarks>
    private static AnalysisChunk? TranslateEventToChunk(PlaybookStreamEvent ev)
    {
        switch (ev.Type)
        {
            case PlaybookEventType.NodeProgress:
                // Per-token streaming surface. NodeProgress events without content
                // are filtered (defensive — should not happen in practice).
                return string.IsNullOrEmpty(ev.Content)
                    ? null
                    : AnalysisChunk.FromContent(ev.Content);

            case PlaybookEventType.NodeCompleted when ev.NodeOutput is { Success: true, IsDeliverOutput: true } output:
                // Terminal DeliverOutput → "complete" chunk. Prefer the structured payload
                // (binds to DocumentAnalysisResult); fall back to text-only when StructuredData
                // doesn't deserialize (model drift) or is absent.
                if (output.StructuredData.HasValue)
                {
                    try
                    {
                        var result = output.StructuredData.Value.Deserialize<DocumentAnalysisResult>();
                        if (result is not null)
                        {
                            return AnalysisChunk.Completed(result);
                        }
                    }
                    catch (JsonException)
                    {
                        // Graceful degrade — fall through to text-only completion.
                    }
                }

                return !string.IsNullOrEmpty(output.TextContent)
                    ? AnalysisChunk.Completed(output.TextContent)
                    : null;

            case PlaybookEventType.RunFailed:
                return AnalysisChunk.FromError(ev.Error ?? "Summarization failed.");

            case PlaybookEventType.NodeFailed:
                // Per-node failure surfaces as a stream-level error so the chat client renders
                // the failure rather than silently terminating mid-stream.
                return AnalysisChunk.FromError(ev.Error ?? "A summarization step failed.");

            case PlaybookEventType.RunCancelled:
                return AnalysisChunk.FromError("Summarization was cancelled.");

            default:
                // Filtered: RunStarted, NodeStarted, NodeSkipped, NodeCompleted (non-terminal),
                // SectionStarted, SectionData, SectionCompleted, RunCompleted (no chat payload —
                // the terminal NodeCompleted+DeliverOutput already emitted the Completed chunk),
                // UnrenderedTemplateDetected (server-side observability only).
                return null;
        }
    }

    /// <summary>
    /// Resolves the effective file-id list for the request: prefers explicit
    /// <see cref="SummarizeSessionFilesRequest.FileIds"/>, falls back to the session's full
    /// uploaded-files manifest (FR-08). Returns an empty list when neither carries any IDs.
    /// </summary>
    private static IReadOnlyList<string> ResolveEffectiveFileIds(
        IReadOnlyList<string>? explicitFileIds,
        IReadOnlyList<ChatSessionFile> uploadedFiles)
    {
        if (explicitFileIds is { Count: > 0 })
        {
            return explicitFileIds;
        }

        if (uploadedFiles.Count == 0)
        {
            return Array.Empty<string>();
        }

        return uploadedFiles.Select(f => f.FileId).ToList();
    }

    /// <summary>
    /// Builds the parameter dictionary forwarded to <see cref="IPlaybookOrchestrationService"/>
    /// per task 090 design §3.4. All keys + values are deterministic identifiers or enumerable
    /// shapes per ADR-015 — NEVER user message content.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildParameters(
        SummarizeSessionFilesRequest request,
        ChatSession session,
        IReadOnlyList<ChatSessionFile> uploadedFiles,
        IReadOnlyList<string> resolvedFileIds)
    {
        var manifest = uploadedFiles
            .Where(f => resolvedFileIds.Contains(f.FileId, StringComparer.Ordinal))
            .Select(f => new
            {
                f.FileId,
                f.FileName,
                f.ContentType,
                f.SizeBytes,
                f.SearchDocumentIdsCsv,
                UploadedAt = f.UploadedAt.ToString("O")
            })
            .ToList();

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Identity (ADR-014 tenant/session isolation)
            ["tenantId"] = request.TenantId,
            ["sessionId"] = request.SessionId,

            // Optional style hint (per FR-08)
            ["styleHint"] = request.StyleHint ?? string.Empty,

            // File manifest — JSON-serialized so the playbook's RAG node can filter on the
            // explicit session+file scope.
            ["sessionFilesManifest"] = JsonSerializer.Serialize(manifest),

            // Convenience scalars for {{template}} conditionals.
            ["fileCount"] = resolvedFileIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isMultiFile"] = (resolvedFileIds.Count >= 2).ToString().ToLowerInvariant(),

            // Path discriminator preserved for telemetry consistency
            ["invocationPath"] = request.Path.ToTelemetryValue(),

            // Correlation propagation (NFR-17)
            ["correlationId"] = request.CorrelationId ?? string.Empty
        };
    }
}

/// <summary>
/// Request shape consumed by <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>.
/// </summary>
/// <param name="TenantId">Tenant ID (ADR-014). Required.</param>
/// <param name="SessionId">Chat session ID (ADR-014; task 004 manifest key). Required.</param>
/// <param name="FileIds">
/// Optional subset of <see cref="ChatSession.UploadedFiles"/> to summarize. When null/empty,
/// defaults to ALL files in the session manifest (FR-08). Cap: 20 (NFR-02).
/// </param>
/// <param name="StyleHint">
/// Optional natural-language style hint passed through to the system prompt
/// (e.g., <c>executive</c>, <c>detailed</c>, <c>bullet-points</c>).
/// </param>
/// <param name="Path">
/// Invocation-path discriminator. <see cref="SummarizeInvocationPath.DirectEndpoint"/> when
/// dispatched from the direct endpoint; <see cref="SummarizeInvocationPath.AgentTool"/> when
/// dispatched from the agent-tool handler. Drives the <c>path</c> dimension on
/// <see cref="Telemetry.R5SummarizeTelemetry.RecordSummarizeInvocation"/>.
/// </param>
/// <param name="CorrelationId">
/// Optional correlation ID propagated to the distributed-tracing span (NFR-17).
/// </param>
public sealed record SummarizeSessionFilesRequest(
    string TenantId,
    string SessionId,
    IReadOnlyList<string>? FileIds,
    string? StyleHint,
    SummarizeInvocationPath Path,
    string? CorrelationId = null);

/// <summary>
/// Discriminator identifying which downstream caller is invoking
/// <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>. Drives the
/// <c>path</c> dimension on <see cref="Telemetry.R5SummarizeTelemetry.RecordSummarizeInvocation"/>.
/// </summary>
public enum SummarizeInvocationPath
{
    /// <summary>Direct endpoint dispatch (<c>POST /api/ai/chat/sessions/{id}/summarize</c>).</summary>
    DirectEndpoint = 0,

    /// <summary>Agent-tool dispatch (historical; reserved for future re-introduction).</summary>
    AgentTool = 1,
}

/// <summary>Extension helpers for <see cref="SummarizeInvocationPath"/>.</summary>
internal static class SummarizeInvocationPathExtensions
{
    /// <summary>
    /// Maps the enum to the locked <see cref="Telemetry.R5SummarizeTelemetry"/> <c>path</c>
    /// dimension value (<c>direct_endpoint</c> or <c>agent_tool</c>). Out-of-enum input
    /// throws — by design, the telemetry cardinality guard would reject it anyway.
    /// </summary>
    public static string ToTelemetryValue(this SummarizeInvocationPath path) => path switch
    {
        SummarizeInvocationPath.DirectEndpoint => "direct_endpoint",
        SummarizeInvocationPath.AgentTool => "agent_tool",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path,
            "Unknown SummarizeInvocationPath value — orchestrator/telemetry contract drift.")
    };
}
