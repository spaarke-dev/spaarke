using System.Runtime.CompilerServices;
using System.Text.Json;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IFileSummarizeAi"/>: resolves the <c>summarize-file</c>
/// Action via <see cref="IActionResolver"/> and runs it via <see cref="IActionRunner"/> — the
/// SAME primitives <c>WorkspaceFileEndpoints.HandleSummarize</c> previously injected directly
/// (A-1). Relocating them behind this PublicContracts facade satisfies ADR-013 / BFF §10 bullet 3
/// while preserving the SSE chunk contract + 503 semantics byte-for-byte.
/// </summary>
public sealed class FileSummarizeAi : IFileSummarizeAi
{
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ILogger<FileSummarizeAi> _logger;

    public FileSummarizeAi(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        ILogger<FileSummarizeAi> logger)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// FR-P3-05 wrapper absorption, relocated behind the facade (task 024): resolve the Binding
    /// row's Action via <see cref="IActionResolver"/>, run it via <see cref="IActionRunner"/> —
    /// behavior preserved from the former endpoint-inline iterator. Resolution/LLM failures
    /// surface as error chunks so the stream never dies silently; <see cref="FeatureDisabledException"/>
    /// (ADR-032 P3 kill-switch) and <see cref="OperationCanceledException"/> propagate so the
    /// endpoint's catch blocks emit the canonical 503 / SSE-error-chunk / timeout pattern.
    /// </remarks>
    public async IAsyncEnumerable<AnalysisStreamChunk> SummarizeAsync(
        string extractedText,
        string fileName,
        string? tenantId,
        string correlationId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.SummarizeFile,
            CorrelationId = correlationId,
            TenantId = tenantId,
        };

        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        AnalysisAction? action = null;
        string? resolveError = null;
        try
        {
            action = await _actionResolver.ResolveAsync(ConsumerTypes.SummarizeFile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException)
        {
            // Kill-switch (ADR-032 P3): propagate so the endpoint's catch emits the
            // canonical 503 / SSE-error-chunk pattern (parity with the deleted Null wrapper).
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve action for consumerType={ConsumerType}. CorrelationId={CorrelationId}",
                ConsumerTypes.SummarizeFile, correlationId);
            resolveError = $"Failed to resolve action: {ex.Message}";
        }
        if (resolveError != null)
        {
            yield return AnalysisStreamChunk.FromError(resolveError);
            yield break;
        }

        var docText = new DocumentText
        {
            DocumentId = null,
            FileName = fileName,
            ExtractedText = extractedText,
        };

        yield return AnalysisStreamChunk.Progress("calling_llm", "Analyzing document(s) with AI…");
        JsonElement aiOutput = default;
        string? llmError = null;
        try
        {
            aiOutput = await _actionRunner.RunAsync(action!, docText, runContext, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LLM call failed for consumerType={ConsumerType}. CorrelationId={CorrelationId}",
                ConsumerTypes.SummarizeFile, correlationId);
            llmError = $"AI analysis failed: {ex.Message}";
        }
        if (llmError != null)
        {
            yield return AnalysisStreamChunk.FromError(llmError);
            yield break;
        }

        // Emit the entire structured output as a single SSE `result` chunk —
        // the client parses Content as JSON (contract unchanged from the wrapper).
        yield return AnalysisStreamChunk.Result(aiOutput.GetRawText());
    }
}
