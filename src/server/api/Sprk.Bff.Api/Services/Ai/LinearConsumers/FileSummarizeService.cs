using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Consumer-agnostic Linear AI Consumer for text → structured-summary flows.
/// Composes <see cref="IActionResolver"/> + <see cref="IActionRunner"/> and
/// emits an <see cref="AnalysisStreamChunk"/> SSE sequence. The
/// <c>consumerType</c> parameter selects which <c>sprk_analysisaction</c> row
/// is resolved (each consumer supplies its own prompt + output schema via the
/// <c>sprk_playbookconsumer</c> routing table — see <see cref="IActionResolver"/>).
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 Phase C (File Summary) + Phase 12.3 (Chat Summarize) — same
/// service serves two consumers by parameterizing consumer-type at call time.
/// See <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// </para>
/// <para>
/// The service consumes PRE-EXTRACTED text; each caller owns extraction
/// (Workspace endpoint uses <c>ITextExtractor</c> over uploaded files;
/// chat orchestrator uses <c>SessionFileTextSource</c> over the
/// <c>spaarke-session-files</c> RAG index). This keeps the service usable
/// as a step within a larger non-linear pipeline as well.
/// </para>
/// <para>
/// Name retained: "FileSummarizeService" — the service is a generic
/// text-to-summary primitive. The name reflects its origin; the interface
/// is intentionally consumer-agnostic.
/// </para>
/// </remarks>
public class FileSummarizeService
{
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ILogger<FileSummarizeService> _logger;

    public FileSummarizeService(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        ILogger<FileSummarizeService> logger)
    {
        _actionResolver = actionResolver;
        _actionRunner = actionRunner;
        _logger = logger;
    }

    /// <summary>
    /// Protected logger-only ctor for the ADR-032 Null-Object subclass
    /// (<see cref="NullFileSummarizeService"/>) — bypasses the AI deps so the Null peer is
    /// registrable when the compound AI gate is OFF. Production code MUST use the public ctor.
    /// Pattern mirrors <c>SessionSummarizeOrchestrator</c> / <c>SprkChatAgentFactory</c>.
    /// </summary>
    protected FileSummarizeService(ILogger<FileSummarizeService> logger)
    {
        _actionResolver = null!;
        _actionRunner = null!;
        _logger = logger;
    }

    /// <summary>
    /// Execute the summarize linear pipeline for the specified consumer.
    /// Emits progress + result chunks the caller can write to the SSE response.
    /// </summary>
    /// <param name="extractedText">Pre-extracted, pre-concatenated source text.</param>
    /// <param name="fileName">Display label for logging (e.g., first file name or "session-files-combined").</param>
    /// <param name="consumerType">Consumer-type key (e.g., <see cref="ConsumerTypes.SummarizeFile"/> or <see cref="ConsumerTypes.ChatSummarize"/>) — resolves the target Action row via the <c>sprk_playbookconsumer</c> routing table (<see cref="IActionResolver"/>).</param>
    /// <param name="httpContext">HTTP context (for tenant claims + correlation id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAsync(
        string extractedText,
        string fileName,
        string consumerType,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tenantId = httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var runContext = new LinearRunContext
        {
            ConsumerType = consumerType,
            CorrelationId = httpContext.TraceIdentifier,
            TenantId = tenantId,
        };

        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        var (action, actionError) = await TryResolveActionAsync(consumerType, cancellationToken);
        if (actionError != null)
        {
            yield return AnalysisStreamChunk.FromError(actionError);
            yield break;
        }

        var docText = new DocumentText
        {
            DocumentId = null,
            FileName = fileName,
            ExtractedText = extractedText,
        };

        yield return AnalysisStreamChunk.Progress("calling_llm", "Analyzing document(s) with AI…");
        var (aiOutput, llmError) = await TryRunLlmAsync(action!, docText, runContext, cancellationToken);
        if (llmError != null)
        {
            yield return AnalysisStreamChunk.FromError(llmError);
            yield break;
        }

        // Emit the entire structured output as a single SSE `result` chunk —
        // matches the engine path's contract; the client parses Content as JSON.
        yield return AnalysisStreamChunk.Result(aiOutput.GetRawText());
    }

    private async Task<(AnalysisAction? action, string? error)> TryResolveActionAsync(
        string consumerType, CancellationToken ct)
    {
        try
        {
            var action = await _actionResolver.ResolveAsync(consumerType, ct);
            return (action, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve action for consumerType={ConsumerType}", consumerType);
            return (null, $"Failed to resolve action: {ex.Message}");
        }
    }

    private async Task<(JsonElement output, string? error)> TryRunLlmAsync(
        AnalysisAction action, DocumentText docText, LinearRunContext ctx, CancellationToken ct)
    {
        try
        {
            var output = await _actionRunner.RunAsync(action, docText, ctx, ct);
            return (output, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for consumerType={ConsumerType}", ctx.ConsumerType);
            return (default, $"AI analysis failed: {ex.Message}");
        }
    }
}
