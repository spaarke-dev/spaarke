using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Safety pipeline middleware (AIPU2-065).
///
/// Wraps an inner <see cref="ISprkChatAgent"/> with a two-phase safety perimeter:
///
/// PRE-LLM (before <see cref="ISprkChatAgent.SendMessageAsync"/> is called):
///   1. Builds a <see cref="PromptShieldRequest"/> from the user message and any
///      retrieved document passages held in <see cref="ISprkChatAgent.Citations"/>.
///   2. Calls <see cref="IPromptShieldService.ScanAsync"/> (100 ms hard timeout, fail-open).
///   3. If blocked → emits an "error" SSE event and yields no further updates (hard block).
///   4. If safe → delegates to the inner agent stream.
///
/// POST-LLM (after the inner stream completes):
///   1. Buffers the full response text from all streaming chunks.
///   2. Runs in parallel (Task.WhenAll):
///      a. <see cref="IGroundednessCheckService.CheckAsync"/> — checks each claim against
///         the RAG source passages.
///      b. <see cref="CitationSafetyCheck.CheckResponseAsync"/> — extracts and verifies
///         legal citations in the response.
///   3. Calls <see cref="IConfidenceScoringService.Score"/> (synchronous) to produce a
///      composite confidence level from the groundedness + source-count inputs.
///   4. Emits a <c>safety_annotation</c> SSE event via <see cref="R2SseEventEmitter"/>.
///   5. Writes an <see cref="AuditEntry"/> (SHA-256 response hash + safety results) to
///      the Cosmos DB audit log via <see cref="IAuditLogService"/> (fire-and-forget).
///
/// Design constraints:
///   - The middleware MUST NOT modify or suppress the inner agent's streaming tokens.
///     Tokens flow through to the caller unchanged.
///   - Post-LLM safety work runs only after all tokens have been yielded, so it adds
///     zero latency to perceived first-token time.
///   - All services are fail-open: any exception in the safety path is caught and logged;
///     the chat response is never suppressed by a safety service failure.
///   - ADR-015: prompt text, response text, and document passages MUST NOT appear in logs.
///     Only counts, IDs, outcome flags, and latency values are logged.
///
/// Lifetime: Transient — one instance per agent session, created by
/// <see cref="SprkChatAgentFactory.WrapWithMiddleware"/> (task 061 integration).
/// </summary>
public sealed class SafetyPipelineMiddleware : ISprkChatAgent
{
    // =========================================================================
    // Dependencies
    // =========================================================================

    private readonly ISprkChatAgent _inner;
    private readonly IPromptShieldService _promptShield;
    private readonly IGroundednessCheckService _groundednessCheck;
    private readonly CitationSafetyCheck _citationCheck;
    private readonly IConfidenceScoringService _confidenceScoring;
    private readonly IAuditLogService _auditLog;
    private readonly Func<ChatSseEvent, CancellationToken, Task>? _sseWriter;
    private readonly string _sessionId;
    private readonly string _tenantId;
    private readonly string _userId;
    private readonly ILogger<SafetyPipelineMiddleware> _logger;

    /// <summary>
    /// SSE event type for prompt-injection blocked events.
    /// The inner "error" type is the R1 wire-format type expected by the chat client.
    /// </summary>
    private const string SseErrorType = "error";

    /// <summary>
    /// Human-readable message emitted to the client when prompt injection is detected.
    /// ADR-015: must not disclose which document triggered the block.
    /// </summary>
    private const string InjectionBlockedMessage =
        "Your message could not be processed due to a security policy violation. " +
        "Please rephrase and try again.";

    // =========================================================================
    // Constructor
    // =========================================================================

    /// <summary>
    /// Initialises the safety pipeline middleware.
    /// </summary>
    /// <param name="inner">The inner agent to wrap (must not be null).</param>
    /// <param name="promptShield">Pre-LLM prompt injection detection (scoped).</param>
    /// <param name="groundednessCheck">Post-LLM groundedness annotation (scoped).</param>
    /// <param name="citationCheck">Post-LLM citation extraction and verification (scoped).</param>
    /// <param name="confidenceScoring">Post-LLM confidence level computation (singleton).</param>
    /// <param name="auditLog">Compliance audit writer (fire-and-forget, singleton).</param>
    /// <param name="sseWriter">
    /// Optional SSE writer delegate for emitting <c>safety_annotation</c> events.
    /// When null (e.g. background processing), SSE emission is skipped but audit logging
    /// still runs.
    /// </param>
    /// <param name="sessionId">Opaque session identifier for audit correlation.</param>
    /// <param name="tenantId">Tenant ID for audit partition key (ADR-014).</param>
    /// <param name="userId">Azure AD object ID of the requesting user (audit only).</param>
    /// <param name="logger">Logger (ADR-015: counts and IDs only, never content).</param>
    public SafetyPipelineMiddleware(
        ISprkChatAgent inner,
        IPromptShieldService promptShield,
        IGroundednessCheckService groundednessCheck,
        CitationSafetyCheck citationCheck,
        IConfidenceScoringService confidenceScoring,
        IAuditLogService auditLog,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter,
        string sessionId,
        string tenantId,
        string userId,
        ILogger<SafetyPipelineMiddleware> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _promptShield = promptShield ?? throw new ArgumentNullException(nameof(promptShield));
        _groundednessCheck = groundednessCheck ?? throw new ArgumentNullException(nameof(groundednessCheck));
        _citationCheck = citationCheck ?? throw new ArgumentNullException(nameof(citationCheck));
        _confidenceScoring = confidenceScoring ?? throw new ArgumentNullException(nameof(confidenceScoring));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _sseWriter = sseWriter;
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _userId = userId ?? throw new ArgumentNullException(nameof(userId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // ISprkChatAgent pass-throughs
    // =========================================================================

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <inheritdoc />
    public CitationContext? Citations => _inner.Citations;

    /// <inheritdoc />
    public Task<IReadOnlyList<FunctionCallContent>> DetectToolCallsAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        CancellationToken cancellationToken)
        => _inner.DetectToolCallsAsync(message, history, cancellationToken);

    // =========================================================================
    // Core pipeline
    // =========================================================================

    /// <summary>
    /// Executes the full safety pipeline around a single agent turn.
    ///
    /// Execution order:
    ///   1. PRE-LLM: prompt shield scan (hard block on detection).
    ///   2. Inner agent streaming (tokens pass through unchanged).
    ///   3. POST-LLM: groundedness + citation checks + confidence scoring.
    ///   4. POST-LLM: safety_annotation SSE event emission.
    ///   5. POST-LLM: audit log write (fire-and-forget).
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ── PHASE 1: Pre-LLM prompt shield ────────────────────────────────────

        PromptShieldResult shieldResult;
        try
        {
            var shieldRequest = BuildShieldRequest(message);
            shieldResult = await _promptShield.ScanAsync(shieldRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Prompt shield itself threw unexpectedly — fail-open.
            _logger.LogWarning(ex,
                "SafetyPipeline: PromptShield threw unexpectedly for session={SessionId}. " +
                "Failing open — request will proceed to LLM.",
                _sessionId);
            shieldResult = PromptShieldResult.FailOpen(0);
        }

        if (shieldResult.IsBlocked)
        {
            // Hard block: emit error event and stop. Do not call the inner agent.
            _logger.LogWarning(
                "SafetyPipeline: prompt injection BLOCKED for session={SessionId}. " +
                "BlockReason={BlockReason}, AttackType={AttackType}",
                _sessionId, shieldResult.BlockReason, shieldResult.DetectedAttackType);

            await EmitBlockedErrorAsync(cancellationToken).ConfigureAwait(false);

            // Write audit entry for the blocked turn (no response text → empty hash).
            await WriteAuditEntryAsync(
                responseHash: AuditHashHelper.HashResponse(string.Empty),
                shieldPassed: false,
                groundednessScore: 1.0,
                citationsVerified: 0,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Yield the error SSE as a ChatResponseUpdate so the caller can stream it.
            var errorUpdate = new ChatResponseUpdate { Role = ChatRole.Assistant };
            errorUpdate.Contents.Add(new TextContent(InjectionBlockedMessage));
            yield return errorUpdate;
            yield break;
        }

        _logger.LogInformation(
            "SafetyPipeline: prompt shield PASSED for session={SessionId}. " +
            "LatencyMs={LatencyMs:F1}",
            _sessionId, shieldResult.LatencyMs);

        // ── PHASE 2: Stream inner agent, buffer response ───────────────────────

        var responseBuilder = new StringBuilder();

        await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken)
                           .ConfigureAwait(false))
        {
            // Buffer the text for post-LLM checks while passing tokens through unchanged.
            if (update.Text is not null)
            {
                responseBuilder.Append(update.Text);
            }

            yield return update;
        }

        // ── PHASE 3: Post-LLM safety checks ───────────────────────────────────

        var fullResponse = responseBuilder.ToString();
        var sourceDocs = ExtractSourceDocuments();
        var sourceDocCount = sourceDocs.Count;

        // Run groundedness + citation checks concurrently — they are independent services
        // that emit different SSE event types and access different APIs.
        GroundednessResult groundednessResult;
        CitationSafetyAnnotation citationAnnotation;

        try
        {
            var groundednessTask = _groundednessCheck.CheckAsync(
                new GroundednessRequest(
                    LlmResponse: fullResponse,
                    SourceDocuments: sourceDocs,
                    Query: message),
                cancellationToken);

            var citationTask = _citationCheck.CheckResponseAsync(fullResponse, cancellationToken);

            await Task.WhenAll(groundednessTask, citationTask).ConfigureAwait(false);

            groundednessResult = await groundednessTask.ConfigureAwait(false);
            citationAnnotation = await citationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Post-LLM checks failed — fail-open: the user already received the response,
            // we just can't annotate it. Log, then proceed to emit a degraded annotation.
            _logger.LogWarning(ex,
                "SafetyPipeline: post-LLM checks threw for session={SessionId}. " +
                "Emitting default (grounded) annotation. SourceDocCount={SourceDocCount}",
                _sessionId, sourceDocCount);

            groundednessResult = GroundednessResult.AssumeGrounded(0);
            citationAnnotation = new CitationSafetyAnnotation([]);
        }

        // Confidence scoring is synchronous — compute inline.
        ConfidenceScoringResult confidenceResult;
        try
        {
            confidenceResult = _confidenceScoring.Score(new ConfidenceScoringRequest(
                SourcePassageCount: sourceDocCount,
                GroundednessResult: groundednessResult,
                ResponseLength: fullResponse.Length,
                CitationCount: citationAnnotation.Citations.Count));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SafetyPipeline: ConfidenceScoring threw for session={SessionId}. " +
                "Using Low confidence default.",
                _sessionId);
            confidenceResult = new ConfidenceScoringResult(
                Level: ConfidenceLevel.Low,
                Score: 0f,
                Rationale: "Confidence scoring failed; defaulting to Low.");
        }

        _logger.LogInformation(
            "SafetyPipeline: post-LLM complete for session={SessionId}. " +
            "Grounded={IsGrounded}, UngroundedSegments={UngroundedSegments}, " +
            "Citations={CitationCount}, Confidence={ConfidenceLevel}, Score={Score:F3}, " +
            "SourceDocs={SourceDocCount}",
            _sessionId,
            groundednessResult.IsGrounded,
            groundednessResult.UngroundedSegments.Count,
            citationAnnotation.Citations.Count,
            confidenceResult.Level,
            confidenceResult.Score,
            sourceDocCount);

        // ── PHASE 4: Emit safety_annotation SSE event ─────────────────────────

        if (_sseWriter is not null)
        {
            await EmitSafetyAnnotationAsync(
                groundednessResult,
                citationAnnotation,
                confidenceResult,
                cancellationToken).ConfigureAwait(false);
        }

        // ── PHASE 5: Fire-and-forget audit log write ───────────────────────────

        var groundednessScore = groundednessResult.IsGrounded
            ? 1.0 - ((double)groundednessResult.UngroundedSegments.Count /
                     Math.Max(groundednessResult.UngroundedSegments.Count + 1, 1))
            : 0.0;

        var verifiedCitationCount = citationAnnotation.Citations.Count(c => c.IsVerified);

        await WriteAuditEntryAsync(
            responseHash: AuditHashHelper.HashResponse(fullResponse),
            shieldPassed: true,
            groundednessScore: groundednessScore,
            citationsVerified: verifiedCitationCount,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds a <see cref="PromptShieldRequest"/> from the current user message and any
    /// retrieved document passages available through the inner agent's citation context.
    ///
    /// Document passages are extracted from <see cref="CitationContext"/> source excerpts —
    /// these are the same RAG chunks injected into the LLM context, making them the most
    /// relevant content to shield against indirect injection attacks.
    /// </summary>
    private PromptShieldRequest BuildShieldRequest(string userMessage)
    {
        var docs = ExtractSourceDocuments();

        // ADR-015: log count only, never message or doc text.
        _logger.LogDebug(
            "SafetyPipeline: building shield request for session={SessionId}. " +
            "DocCount={DocCount}",
            _sessionId, docs.Count);

        return new PromptShieldRequest(userMessage, docs.Count > 0 ? docs : null);
    }

    /// <summary>
    /// Extracts plain-text source passages from the inner agent's <see cref="CitationContext"/>.
    ///
    /// The citation context is populated by DocumentSearchTools and KnowledgeRetrievalTools
    /// during the current turn. Between turns, <see cref="CitationContext.Reset"/> is called
    /// by <see cref="SprkChatAgent"/>, so these excerpts are always turn-scoped.
    ///
    /// When no citations are registered (e.g. the LLM answered without search tools),
    /// the returned list is empty. Both PromptShieldService and GroundednessCheckService
    /// handle empty lists gracefully (skip check / assume grounded).
    /// </summary>
    private IReadOnlyList<string> ExtractSourceDocuments()
    {
        var citations = _inner.Citations;
        if (citations is null)
        {
            return [];
        }

        // Map each registered citation entry's Excerpt (if non-empty) to a document passage.
        // ADR-015: excerpts are sent to Azure Content Safety API, not logged here.
        var passages = citations.GetCitations()
            .Select(c => c.Excerpt)
            .Where(excerpt => !string.IsNullOrWhiteSpace(excerpt))
            .Select(excerpt => excerpt!)
            .ToList()
            .AsReadOnly();

        return passages;
    }

    /// <summary>
    /// Emits an SSE error event notifying the client that the turn was blocked by the
    /// prompt shield. Uses the raw SSE writer to stay within the existing R1 event format.
    ///
    /// ADR-015: the error message must not reveal which document triggered the block.
    /// </summary>
    private async Task EmitBlockedErrorAsync(CancellationToken cancellationToken)
    {
        if (_sseWriter is null)
        {
            return;
        }

        try
        {
            var errorEvent = new ChatSseEvent(SseErrorType, InjectionBlockedMessage);
            await _sseWriter(errorEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SafetyPipeline: failed to emit blocked-error SSE event for session={SessionId}.",
                _sessionId);
        }
    }

    /// <summary>
    /// Emits a <c>safety_annotation</c> SSE event via <see cref="R2SseEventEmitter"/>.
    ///
    /// The annotation carries:
    ///   - Severity: "warning" when ungrounded segments were detected; "info" otherwise.
    ///   - Category: "groundedness" (primary annotation type for post-stream events).
    ///   - Groundedness score and rationale from <see cref="ConfidenceScoringResult"/>.
    ///   - Citation verification summary (verified / unverified normalised keys).
    ///
    /// Emission failures are caught and logged — the annotation MUST NOT affect streaming.
    /// </summary>
    private async Task EmitSafetyAnnotationAsync(
        GroundednessResult groundednessResult,
        CitationSafetyAnnotation citationAnnotation,
        ConfidenceScoringResult confidenceResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var emitter = new R2SseEventEmitter(_sseWriter!, _logger);

            var severity = groundednessResult.IsGrounded ? "info" : "warning";
            var action = groundednessResult.IsGrounded ? "logged" : "logged";

            // Groundedness payload: use confidence score as the 0-1 groundedness score
            // (high confidence = well-grounded, low = poorly grounded).
            var groundedness = new SafetyGroundedness(
                Score: confidenceResult.Score,
                Rationale: confidenceResult.Rationale);

            // Citation payload: split verified vs unverified by normalised key.
            SafetyCitations? citations = null;
            if (citationAnnotation.HasCitations)
            {
                var verified = citationAnnotation.Citations
                    .Where(c => c.IsVerified)
                    .Select(c => c.Normalized)
                    .ToList()
                    .AsReadOnly();

                var unverified = citationAnnotation.Citations
                    .Where(c => !c.IsVerified)
                    .Select(c => c.Normalized)
                    .ToList()
                    .AsReadOnly();

                citations = new SafetyCitations(
                    Verified: verified.Count > 0 ? verified : null,
                    Unverified: unverified.Count > 0 ? unverified : null);
            }

            // Human-readable user message — must not reference content (ADR-015).
            var userMessage = groundednessResult.IsGrounded
                ? $"Response confidence: {confidenceResult.Level}."
                : $"Some claims in this response could not be fully verified against source documents. " +
                  $"Confidence: {confidenceResult.Level}.";

            await emitter.EmitSafetyAnnotationAsync(
                severity: severity,
                category: "groundedness",
                action: action,
                userMessage: userMessage,
                groundedness: groundedness,
                citations: citations,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SafetyPipeline: failed to emit safety_annotation SSE event for session={SessionId}.",
                _sessionId);
        }
    }

    /// <summary>
    /// Writes a compliance audit entry to Cosmos DB (fire-and-forget via <see cref="IAuditLogService"/>).
    ///
    /// The entry includes:
    ///   - SHA-256 hash of the response text (ADR-015: no raw text).
    ///   - Whether the prompt shield passed.
    ///   - Groundedness score (0.0–1.0).
    ///   - Count of verified citations.
    ///
    /// Audit write failures are caught and logged inside <see cref="AuditLogService"/> —
    /// they never propagate to this method's caller.
    /// </summary>
    private async ValueTask WriteAuditEntryAsync(
        string responseHash,
        bool shieldPassed,
        double groundednessScore,
        int citationsVerified,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new AuditEntry
            {
                TenantId = _tenantId,
                UserId = _userId,
                SessionId = _sessionId,
                Action = "chat_response",
                ResponseHash = responseHash,
                SafetyResults = new SafetyCheckResult
                {
                    PromptShieldPassed = shieldPassed,
                    GroundednessScore = groundednessScore,
                    CitationsVerified = citationsVerified,
                },
            };

            await _auditLog.LogInteractionAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // AuditLogService is already fire-and-forget, but guard here too.
            _logger.LogWarning(ex,
                "SafetyPipeline: unexpected error preparing audit entry for session={SessionId}.",
                _sessionId);
        }
    }
}
