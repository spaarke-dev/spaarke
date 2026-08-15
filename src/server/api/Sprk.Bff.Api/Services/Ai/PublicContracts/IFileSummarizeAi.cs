using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013 / BFF §10 bullet 3) for the workspace file-summarize capability
/// (the <c>summarize-file</c> catalog Action, routed via
/// <see cref="ConsumerTypes.SummarizeFile"/>). Resolves the summarize-file Binding row's
/// Action and runs it on the prompted executor, streaming progress + a single structured
/// <c>result</c> chunk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facade discipline.</b> The non-AI workspace endpoint
/// (<see cref="Sprk.Bff.Api.Api.Workspace.WorkspaceFileEndpoints"/>) MUST consume this
/// capability through THIS facade — never by injecting the Linear AI Consumer primitives
/// (<c>IActionResolver</c> / <c>IActionRunner</c>) directly. Internally this facade wraps
/// those SAME primitives (mirroring <see cref="ICommunicationTriageAi"/>, which legally wraps
/// resolver + runner behind PublicContracts). This REDUCES CRUD→AI coupling (moves the former
/// A-1 direct injection behind the sanctioned boundary).
/// </para>
/// <para>
/// <b>SSE chunk + 503 contract preserved byte-for-byte.</b> The method yields the exact same
/// <see cref="AnalysisStreamChunk"/> sequence the endpoint previously produced inline
/// (<c>resolving_action</c> progress → resolve → <c>calling_llm</c> progress → run → single
/// <c>result</c> chunk), surfaces resolution / LLM failures as <c>error</c> chunks with the
/// same messages, and RE-THROWS <c>FeatureDisabledException</c> (and
/// <c>OperationCanceledException</c>) so the endpoint's existing catch blocks emit the
/// canonical 503 / SSE-error-chunk kill-switch pattern (ADR-032). The endpoint retains
/// ownership of request validation, SSE headers, text extraction, the outer progress chunks,
/// and the <c>[DONE]</c> terminator.
/// </para>
/// </remarks>
public interface IFileSummarizeAi
{
    /// <summary>
    /// Resolve + run the <c>summarize-file</c> Action against the already-extracted document
    /// text, yielding progress chunks followed by a single structured <c>result</c> chunk.
    /// Resolution / LLM failures yield an <c>error</c> chunk then stop;
    /// <c>FeatureDisabledException</c> / <c>OperationCanceledException</c> propagate to the
    /// caller's 503 / timeout handling.
    /// </summary>
    /// <param name="extractedText">The already-extracted, concatenated document text.</param>
    /// <param name="fileName">Display file name carried on the document operand.</param>
    /// <param name="tenantId">Tenant id for model-deployment resolution (may be null).</param>
    /// <param name="correlationId">Correlation id for run-context tracing + logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<AnalysisStreamChunk> SummarizeAsync(
        string extractedText,
        string fileName,
        string? tenantId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
