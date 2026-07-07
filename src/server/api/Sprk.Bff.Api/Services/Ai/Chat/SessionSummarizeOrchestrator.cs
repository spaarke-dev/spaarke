using System.Runtime.CompilerServices;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Chat-session boundary for the catalog-driven <c>chat-summarize</c> capability
/// (FR-P1-01, spaarke-ai-architecture-redesign-r1 task 020). The single entry point
/// (<see cref="SummarizeSessionFilesAsync"/>) resolves the <c>chat-summarize</c>
/// <see cref="Binding"/> from the closed catalog, loads the target
/// <c>sprk_analysisaction</c> row (SUM-CHAT@v1, <c>kind: prompted</c>), and executes it
/// via the prompted executor (<see cref="IActionRunner"/> — ActionRunner +
/// PromptSchemaRenderer).
/// </summary>
/// <remarks>
/// <para>
/// <b>FR-P1-01 hard cutover (2026-07-05)</b>: the pre-redesign dual-path dispatch
/// (Linear-vs-Playbook-Engine conditional with a chat-summarize Workspace
/// typed-options config fallback) is DELETED, not shimmed (NFR-08).
/// The Binding row on <c>sprk_playbookconsumer</c> is the ONLY routing surface (ADR-039):
/// resolution goes through <see cref="IConsumerRoutingService.ResolveBindingAsync"/> and
/// fails fast when no enabled Binding with an Action target exists — there is no engine
/// fallback and no config fallback.
/// </para>
/// <para>
/// <b>What stays at this boundary</b> (chat-session responsibilities, unchanged):
/// <list type="bullet">
///   <item>Argument validation (tenant + session non-empty; null request; ≤20-file cap per NFR-02).</item>
///   <item>Session lookup via <see cref="ChatSessionManager.GetSessionAsync"/> — missing session
///         surfaces as <see cref="InvalidOperationException"/> ("not found") which
///         <c>SummarizeSessionEndpoint</c> maps to 404.</item>
///   <item>FR-08 file-id resolution (explicit subset or ALL session files).</item>
///   <item>FR-04 multi-file combined-summary interjection (emitted before the result chunk).</item>
///   <item>Session-file text retrieval via <see cref="ISessionFileTextSource"/> (inline
///         <c>ChatSessionFile.ExtractedText</c> first, RAG-index fallback).</item>
///   <item>Null-Object kill-switch subclass (<see cref="NullSessionSummarizeOrchestrator"/>) —
///         protected logger-only ctor preserved (ADR-030 P3 / ADR-032).</item>
/// </list>
/// </para>
/// <para>
/// <b>ADR-040 ledger seam (task 021 — LIVE, FR-P1-02)</b>: at the point marked
/// <c>ADR-040 SEAM</c> below the full structured output (<see cref="JsonElement"/>) and the
/// resolved <see cref="Binding.BindingId"/> + <see cref="Binding.Disposition"/> coexist and
/// nothing has rendered. <see cref="IOutputRouter.RouteAsync"/> writes the addressable
/// <see cref="Models.Ai.Chat.SessionOutput"/> (keyed <c>{bindingId}@t{n}</c>) through the
/// session store FIRST, then routes by disposition; the terminal result chunk is rendered
/// from the STORED entry's payload (render follows store — test-proven ordering).
/// </para>
/// <para>
/// <b>ADR-010</b>: concrete class, no orchestrator-authored interface. Non-sealed to permit
/// the <see cref="NullSessionSummarizeOrchestrator"/> kill-switch subclass, registered in
/// <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c>.
/// </para>
/// <para>
/// <b>ADR-013 placement</b>: lives in <c>Services/Ai/Chat/</c> (in-zone AI territory); in-zone
/// code MAY consume executor internals (<see cref="IActionRunner"/>,
/// <see cref="IScopeResolverService"/>). External CRUD code remains bound to the
/// <c>PublicContracts</c> facade.
/// </para>
/// </remarks>
public class SessionSummarizeOrchestrator
{
    /// <summary>FR-04 — multi-file combined-summary interjection emitted as the first
    /// <see cref="AnalysisChunk.FromContent"/> chunk when the resolved file set is ≥2.</summary>
    private const string CombinedSummaryInterjection =
        "Multiple files selected — generating a combined summary across all of them.";

    private readonly ChatSessionManager _sessionManager;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ISessionFileTextSource _sessionFileTextSource;
    private readonly IOutputRouter _outputRouter;
    private readonly ILogger<SessionSummarizeOrchestrator> _logger;

    public SessionSummarizeOrchestrator(
        ChatSessionManager sessionManager,
        IConsumerRoutingService consumerRouting,
        IScopeResolverService scopeResolver,
        IActionRunner actionRunner,
        ISessionFileTextSource sessionFileTextSource,
        IOutputRouter outputRouter,
        ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _sessionFileTextSource = sessionFileTextSource ?? throw new ArgumentNullException(nameof(sessionFileTextSource));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullSessionSummarizeOrchestrator"/> so the
    /// kill-switch subclass can be constructed when the compound AI gate is OFF and the
    /// AI dependencies are absent. The Null override never reads the nulled fields — it throws
    /// <see cref="Configuration.FeatureDisabledException"/> before they are dereferenced.
    /// Matches the canonical pattern in <see cref="SprkChatAgentFactory"/> /
    /// <see cref="PendingPlanManager"/>.
    /// </summary>
    protected SessionSummarizeOrchestrator(ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = null!;
        _consumerRouting = null!;
        _scopeResolver = null!;
        _actionRunner = null!;
        _sessionFileTextSource = null!;
        _outputRouter = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// THE convergence method. The direct endpoint (and any future dispatch leg) delegates
    /// to THIS method — no other entry point is exposed.
    /// </summary>
    /// <param name="request">The chat-session-scoped Summarize request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// SSE-shaped chunks: optional FR-04 interjection (multi-file requests) followed by a
    /// terminal <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> (or
    /// <see cref="AnalysisChunk.FromError"/>). Catalog/session resolution failures throw
    /// BEFORE the first chunk so the endpoint's pre-stream probe maps them to ProblemDetails.
    /// </returns>
    public virtual async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
        SummarizeSessionFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");

        // NFR-02 — early hard cap (defense-in-depth at the orchestrator boundary). Surfaces
        // ArgumentException to the endpoint mapping layer for 400 ProblemDetails.
        if (request.FileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Session Summarize request exceeds the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {request.FileIds.Count} file IDs.",
                nameof(request));
        }

        // Load the chat session at the orchestrator boundary. Session-not-found surfaces as
        // InvalidOperationException → endpoint maps to 404.
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

        // ── FR-P1-01 catalog resolution — the Binding table is THE routing surface (ADR-039).
        // Resolution goes through the full Binding contract (task 004); no engine fallback,
        // no typed-options fallback (hard cutover, NFR-08). Missing/misconfigured rows fail
        // fast with an operator-actionable message.
        //
        // NOTE (endpoint contract): these messages must NOT contain the phrase "not found" —
        // SummarizeSessionEndpoint maps InvalidOperationException messages containing
        // "not found" to 404 session-not-found (that mapping is reserved for the session
        // lookup above).
        var binding = await _consumerRouting
            .ResolveBindingAsync(ConsumerTypes.ChatSummarize, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (binding is null || binding.ActionId is null || binding.ActionId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "chat-summarize has no enabled Binding row with an Action target. Seed/enable the " +
                "sprk_playbookconsumer row (sprk_consumertype='chat-summarize', sprk_enabled=true) " +
                "and set its sprk_action lookup to the SUM-CHAT@v1 sprk_analysisaction row (ADR-039: " +
                "the Binding table is the only routing surface — there is no config fallback).");
        }

        if (binding.ActionKind != ActionKind.Prompted)
        {
            throw new InvalidOperationException(
                $"chat-summarize Binding {binding.BindingId} targets an Action of kind " +
                $"'{binding.ActionKind}'; the chat /summarize path executes prompted Actions only. " +
                "Set sprk_analysisaction.sprk_kind to Prompted (or leave it null — null defaults to Prompted).");
        }

        var action = await _scopeResolver
            .GetActionAsync(binding.ActionId.Value, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"chat-summarize Binding {binding.BindingId} references Action {binding.ActionId.Value}, " +
                "but that sprk_analysisaction row could not be loaded from Dataverse. Fix the Binding's " +
                "sprk_action lookup.");

        _logger.LogDebug(
            "FR-P1-01: chat-summarize resolved via catalog (bindingId={BindingId} ucid={Ucid} " +
            "actionId={ActionId} actionName={ActionName} disposition={Disposition}) " +
            "tenant={TenantId} session={SessionId} fileCount={FileCount} path={Path}",
            binding.BindingId, binding.Ucid, action.Id, action.Name, binding.Disposition,
            request.TenantId, request.SessionId, resolvedFileIds.Count, request.Path.ToTelemetryValue());

        var targetFiles = uploadedFiles
            .Where(f => resolvedFileIds.Contains(f.FileId, StringComparer.Ordinal))
            .ToList();

        if (targetFiles.Count == 0)
        {
            yield return AnalysisChunk.FromError(
                "No session files were available to summarize. Upload a file first, or pass a valid fileId subset.");
            yield break;
        }

        // FR-04 multi-file combined-summary interjection — emitted BEFORE the executor runs
        // so the chat client can render an early "combining files…" hint.
        if (targetFiles.Count >= 2)
        {
            yield return AnalysisChunk.FromContent(CombinedSummaryInterjection);
        }

        // Session-scoped text fetch — surface transport failures as a stream error, not an
        // unhandled throw (a FromError chunk is the intended UX once the stream has started).
        SessionFileText textResult = default!;
        string? fetchError = null;
        try
        {
            textResult = await _sessionFileTextSource
                .FetchAsync(request.TenantId, request.SessionId, targetFiles, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SessionSummarizeOrchestrator: session-file text retrieval failed. TenantId={TenantId} SessionId={SessionId}",
                request.TenantId, request.SessionId);
            fetchError = "Failed to retrieve session file content. Please try again.";
        }

        if (fetchError is not null)
        {
            yield return AnalysisChunk.FromError(fetchError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(textResult.ExtractedText))
        {
            yield return AnalysisChunk.FromError(
                "Session files contained no text to summarize. The RAG index may still be catching up — try again in a few seconds.");
            yield break;
        }

        // ── Prompted executor (FR-P1-01): ActionRunner renders the Action's SUM-CHAT@v1 JPS
        // SystemPrompt via PromptSchemaRenderer (document text in the "## Document" section)
        // and calls IOpenAiClient structured completion against the Action's OutputSchemaJson.
        var documentText = new DocumentText
        {
            DocumentId = null,
            FileName = textResult.DisplayName,
            ExtractedText = textResult.ExtractedText,
        };
        var runContext = new LinearRunContext
        {
            ConsumerType = binding.ConsumerType,
            CorrelationId = request.CorrelationId,
            TenantId = request.TenantId,
        };

        JsonElement output = default;
        string? llmError = null;
        try
        {
            output = await _actionRunner
                .RunAsync(action, documentText, runContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SessionSummarizeOrchestrator: prompted executor failed for binding {BindingId} action {ActionId}. TenantId={TenantId} SessionId={SessionId}",
                binding.BindingId, action.Id, request.TenantId, request.SessionId);
            llmError = "AI summarization failed. Please try again.";
        }

        if (llmError is not null)
        {
            yield return AnalysisChunk.FromError(llmError);
            yield break;
        }

        // ── ADR-040 SEAM (task 021 — universal ledger write-before-render, LIVE) ───────────
        // At this exact point the FULL structured output (`output`) and the Binding identity
        // (`binding.BindingId`, disposition `binding.Disposition`) are both in hand and NOTHING
        // has been rendered yet (the FR-04 interjection is a UX hint, not output). The
        // OutputRouter (FR-P1-02) writes the SessionOutput ledger entry (keyed {bindingId}@t{n})
        // through the session store FIRST, then routes by the Binding's declared disposition —
        // informational returns the STORED entry, which is what renders below (render follows
        // store). Non-informational dispositions throw loud P3 NotSupported stubs inside the
        // router — never a silent inline-render fallback. SourceRefs carry the grounding file
        // ids (identifiers only, NFR-07).
        var routed = await _outputRouter
            .RouteAsync(
                session,
                binding,
                output,
                sourceRefs: targetFiles.Select(f => f.FileId).ToList(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        yield return DeserializeResultChunk(routed.Entry.Payload.GetRawText());
    }

    /// <summary>
    /// Deserialize the LLM's structured JSON output into <see cref="DocumentAnalysisResult"/>.
    /// Falls back to text-only <see cref="AnalysisChunk.Completed(string)"/> on parse error
    /// so the client always sees a terminal complete chunk (never a silent EOF).
    /// </summary>
    private static AnalysisChunk DeserializeResultChunk(string jsonContent)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<DocumentAnalysisResult>(jsonContent);
            if (doc is not null)
            {
                doc.ParsedSuccessfully = true;
                return AnalysisChunk.Completed(doc);
            }
        }
        catch (JsonException)
        {
            // Graceful degrade — the schema-constrained output should always parse; model
            // drift falls back to a text completion rather than a dead stream.
        }
        return AnalysisChunk.Completed(jsonContent);
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
/// dispatched from the direct endpoint. Drives the <c>path</c> dimension on
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
}

/// <summary>Extension helpers for <see cref="SummarizeInvocationPath"/>.</summary>
internal static class SummarizeInvocationPathExtensions
{
    /// <summary>
    /// Maps the enum to the locked <see cref="Telemetry.R5SummarizeTelemetry"/> <c>path</c>
    /// dimension value (<c>direct_endpoint</c>). Out-of-enum input
    /// throws — by design, the telemetry cardinality guard would reject it anyway.
    /// </summary>
    public static string ToTelemetryValue(this SummarizeInvocationPath path) => path switch
    {
        SummarizeInvocationPath.DirectEndpoint => "direct_endpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path,
            "Unknown SummarizeInvocationPath value — orchestrator/telemetry contract drift.")
    };
}
