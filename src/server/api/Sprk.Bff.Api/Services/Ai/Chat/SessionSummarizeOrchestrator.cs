using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Convergence orchestrator for the chat-driven Summarize-for-Chat vertical slice. Bridges
/// the two valid entry points for chat-driven Summarize — the direct endpoint
/// (<c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>) AND the
/// natural-language agent-tool dispatch — by ensuring BOTH paths delegate to a SINGLE
/// convergence method (<see cref="SummarizeSessionFilesAsync"/>) on this class, which now
/// forwards to <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>R6 Pillar 4 (D-A-17) refactor</b>: prior to R6 task 025, this orchestrator owned the
/// full chat-Summarize streaming pipeline (RAG retrieval + Structured Outputs +
/// <see cref="Streaming.IncrementalJsonParser"/> + telemetry) AND loaded the action seed
/// via the <c>sprk_actioncode = "SUM-CHAT@v1"</c> alternate-key bypass because the playbook
/// → node → action FK chain was broken. R6 task 024 (D-A-16) fixed the FK chain in
/// Dataverse; R6 task 025 (this file) moves the streaming pipeline INTO
/// <see cref="PlaybookExecutionEngine"/> and refactors this orchestrator to a thin
/// pass-through that forwards to <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>.
/// </para>
/// <para>
/// <b>What stayed here</b> (orchestrator boundary responsibilities):
/// <list type="bullet">
///   <item>Public <see cref="SummarizeSessionFilesAsync"/> signature — UNCHANGED for downstream
///         consumers (<c>SummarizeSessionEndpoint</c> + <c>InvokeSummarizePlaybookTool</c>).</item>
///   <item>Session lookup via <see cref="ChatSessionManager.GetSessionAsync"/> — orchestrator
///         remains the chat-session boundary; the engine receives the resolved files manifest.</item>
///   <item>Argument validation at the chat-orchestration boundary (tenant + session non-empty;
///         null request; null/empty file list semantics).</item>
///   <item>Null-Object kill-switch subclass (<see cref="NullSessionSummarizeOrchestrator"/>) —
///         construction via the protected ctor still works; override behavior unchanged.</item>
/// </list>
/// </para>
/// <para>
/// <b>What moved to <see cref="PlaybookExecutionEngine"/></b> (D-A-17):
/// <list type="bullet">
///   <item>Action config resolution — now FK-chain via
///         <see cref="INodeService.GetNodesAsync"/> + <see cref="Spaarke.Dataverse.IGenericEntityService.RetrieveAsync"/>
///         on the FK-resolved action ID; NO alternate-key lookup, NO
///         <c>SummarizeActionCode</c> / <c>ActionEntityLogicalName</c> constants.</item>
///   <item>RAG retrieval (with session-files filter preserved via
///         <see cref="RagSearchOptions.SessionId"/>).</item>
///   <item>Structured Outputs streaming + <see cref="Streaming.IncrementalJsonParser"/>
///         field-delta emission (byte-equivalent <see cref="AnalysisChunk"/> shape preserved).</item>
///   <item>FR-04 multi-file combined-summary interjection.</item>
///   <item>R5 Summarize telemetry recording.</item>
/// </list>
/// </para>
/// <para>
/// <b>FR-26 invariant</b>: the alternate-key bypass (<c>sprk_actioncode = "SUM-CHAT@v1"</c>)
/// is fully removed from the chat /summarize code path. Verification: no remaining
/// <c>SummarizeActionCode</c>, <c>ActionEntityLogicalName</c>, or
/// <c>RetrieveByAlternateKeyAsync</c> reference in this orchestrator.
/// </para>
/// <para>
/// <b>FR-05 stable-ID resolution</b> (chat-routing-redesign-r1 task 015): the prior
/// hardcoded <c>44285d15-1360-f111-ab0b-70a8a59455f4</c> GUID constant has been removed.
/// The playbook is now resolved at runtime by looking up
/// <see cref="WorkspaceOptions.ChatSummarizePlaybookId"/> (typed-options per ADR-018)
/// through <see cref="IPlaybookLookupService.GetByIdAsync"/>, which queries the
/// <c>sprk_playbookid</c> alternate key on <c>sprk_analysisplaybook</c> (Q&amp;A 2026-06-22 Q1)
/// with 1-hour caching (ADR-014). This is the FIRST stable-ID consumer migration (spec
/// FR-05) — no config override existed previously. Fail-fast on missing config — no
/// hardcoded fallback at this convergence point per FR-26.
/// </para>
/// <para>
/// <b>ADR-010</b>: concrete class with NO orchestrator-authored interface (unit tests target the
/// concrete type per ADR-010 — interface-for-testability-alone is explicitly forbidden).
/// Non-sealed to permit the <see cref="NullSessionSummarizeOrchestrator"/> kill-switch subclass
/// (ADR-030 P3 Fail-Fast, registered in
/// <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c>).
/// </para>
/// </remarks>
public class SessionSummarizeOrchestrator
{
    private readonly ChatSessionManager _sessionManager;
    private readonly IPlaybookExecutionEngine _executionEngine;
    private readonly IPlaybookLookupService _playbookLookup;
    private readonly IOptions<WorkspaceOptions> _workspaceOptions;
    private readonly ILogger<SessionSummarizeOrchestrator> _logger;

    public SessionSummarizeOrchestrator(
        ChatSessionManager sessionManager,
        IPlaybookExecutionEngine executionEngine,
        IPlaybookLookupService playbookLookup,
        IOptions<WorkspaceOptions> workspaceOptions,
        ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _playbookLookup = playbookLookup ?? throw new ArgumentNullException(nameof(playbookLookup));
        _workspaceOptions = workspaceOptions ?? throw new ArgumentNullException(nameof(workspaceOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullSessionSummarizeOrchestrator"/> so the
    /// kill-switch subclass can be constructed when the compound AI gate is OFF and
    /// AI dependencies (<see cref="IPlaybookExecutionEngine"/>) are absent or not yet
    /// resolvable. The Null override never reads the nulled fields — it throws
    /// <see cref="Configuration.FeatureDisabledException"/> before they are dereferenced.
    /// Matches the canonical pattern in <see cref="SprkChatAgentFactory"/> /
    /// <see cref="PendingPlanManager"/>.
    /// </summary>
    protected SessionSummarizeOrchestrator(ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = null!;
        _executionEngine = null!;
        _playbookLookup = null!;
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
    /// <see cref="AnalysisChunk.FromDelta"/> events → terminal
    /// <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> or
    /// <see cref="AnalysisChunk.FromError"/>). The exact chunk shapes and ordering are
    /// produced by <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/> — this
    /// orchestrator forwards them unchanged.
    /// </returns>
    public virtual async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
        SummarizeSessionFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");

        // NFR-02 — early hard cap (defense-in-depth at the orchestrator boundary; the engine
        // re-checks symmetrically). Surfaces ArgumentException to the endpoint mapping layer
        // for 400 ProblemDetails — same shape callers expected pre-R6 task 025.
        if (request.FileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Session Summarize request exceeds the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {request.FileIds.Count} file IDs.",
                nameof(request));
        }

        // Load the chat session at the orchestrator boundary. The engine accepts a resolved
        // files manifest; resolving here keeps the engine session-store-agnostic and matches
        // the pre-R6 behavior (session-not-found surfaces as InvalidOperationException →
        // endpoint maps to 404).
        ChatSession? session = await _sessionManager
            .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            // Preserved pre-R6 behavior — endpoint catches InvalidOperationException and maps
            // to 404 ProblemDetails. Do NOT silently turn into an SSE error chunk.
            throw new InvalidOperationException(
                $"Chat session '{request.SessionId}' not found for tenant '{request.TenantId}'.");
        }

        var uploadedFiles = session.UploadedFiles ?? Array.Empty<ChatSessionFile>();

        // FR-05 stable-ID resolution (chat-routing-redesign-r1 task 015):
        // Resolve the chat-summarize playbook GUID at runtime via the stable-ID alternate key
        // (sprk_playbookid) per Q&A 2026-06-22 Q1, replacing the prior hardcoded
        // 44285d15-1360-f111-ab0b-70a8a59455f4 GUID. The lookup service caches results
        // for 1 hour (ADR-014); per-environment values come from WorkspaceOptions.
        // Fail-fast on missing config — there is no hardcoded fallback at the orchestrator
        // boundary (this is the chat /summarize convergence point per R6 FR-26 and must
        // not silently downgrade). Matches the InvoiceExtractionJobHandler empty-string
        // guard pattern (commit 34aef1d01) adapted to throw rather than mark-failed.
        var configuredPlaybookId = _workspaceOptions.Value.ChatSummarizePlaybookId;
        if (string.IsNullOrWhiteSpace(configuredPlaybookId))
        {
            _logger.LogError(
                "Workspace:ChatSummarizePlaybookId is not configured. Cannot resolve chat-summarize " +
                "playbook for tenant={TenantId} session={SessionId}. Configure the per-environment " +
                "GUID (mirrors sprk_analysisplaybookid PK) for the summarize-document-for-chat@v1 row.",
                request.TenantId, request.SessionId);
            throw new InvalidOperationException(
                "Workspace:ChatSummarizePlaybookId is not configured. Chat /summarize cannot resolve " +
                "its playbook without per-environment configuration.");
        }

        var playbook = await _playbookLookup
            .GetByIdAsync(configuredPlaybookId, cancellationToken)
            .ConfigureAwait(false);
        var resolvedPlaybookId = playbook.Id;

        var engineRequest = new ChatSummarizeRequest(
            TenantId: request.TenantId,
            SessionId: request.SessionId,
            FileIds: request.FileIds,
            StyleHint: request.StyleHint,
            UploadedFiles: uploadedFiles,
            Path: request.Path,
            CorrelationId: request.CorrelationId);

        _logger.LogDebug(
            "R6 SessionSummarizeOrchestrator forwarding to PlaybookExecutionEngine.ExecuteChatSummarizeAsync " +
            "(playbookId={PlaybookId} tenant={TenantId} session={SessionId} path={Path})",
            resolvedPlaybookId, request.TenantId, request.SessionId, request.Path.ToTelemetryValue());

        // Forward the engine stream unchanged. The engine owns chunk-shape contract, telemetry,
        // and all preserved behaviors (FR-04 interjection, ADR-014 session filter, Structured
        // Outputs schema, field-delta streaming, mid-stream error → FromError, terminal Completed).
        await foreach (var chunk in _executionEngine
            .ExecuteChatSummarizeAsync(resolvedPlaybookId, engineRequest, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}

/// <summary>
/// Request shape consumed by <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>.
/// The orchestrator forwards this (plus the resolved session-files manifest) to
/// <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>.
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

    /// <summary>Agent-tool dispatch (<c>InvokeSummarizePlaybookTool</c>).</summary>
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
