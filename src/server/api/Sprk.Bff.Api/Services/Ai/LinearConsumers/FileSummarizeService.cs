using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Linear AI Consumer for the Workspace File Summarize flow.
/// Replaces the Playbook Engine dispatch of the "File Summary" playbook
/// (see <c>projects/spaarke-ai-platform-unification-r7/notes/wave12-linear-consumer-migration.md</c>).
/// </summary>
/// <remarks>
/// R7 Wave 12 Phase C (2026-07-02). Composes <see cref="IActionResolver"/> +
/// <see cref="IActionRunner"/>. No Dataverse writes, no downstream jobs — the
/// LLM output is emitted straight back to the client via a single SSE
/// <c>result</c> chunk (matching the engine path's client contract).
/// Endpoint already extracted text from the uploaded files before invoking us,
/// so we consume the pre-extracted text directly rather than owning extraction
/// (which is a per-file concern the endpoint handles today).
/// </remarks>
public sealed class FileSummarizeService
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
    /// Execute the File Summary linear pipeline. Emits progress + result chunks
    /// the caller (WorkspaceFileEndpoints) can write to the SSE response.
    /// </summary>
    /// <param name="extractedText">Pre-extracted, pre-concatenated text from the uploaded files.</param>
    /// <param name="fileName">Display filename for logging (typically first file or "combined").</param>
    /// <param name="httpContext">HTTP context (for tenant claims + correlation id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAsync(
        string extractedText,
        string fileName,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tenantId = httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.SummarizeFile,
            CorrelationId = httpContext.TraceIdentifier,
            TenantId = tenantId,
        };

        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        var (action, actionError) = await TryResolveActionAsync(cancellationToken);
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

    private async Task<(AnalysisAction? action, string? error)> TryResolveActionAsync(CancellationToken ct)
    {
        try
        {
            var action = await _actionResolver.ResolveAsync(ConsumerTypes.SummarizeFile, ct);
            return (action, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve File Summary action");
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
            _logger.LogError(ex, "LLM call failed for File Summary");
            return (default, $"AI analysis failed: {ex.Message}");
        }
    }
}
