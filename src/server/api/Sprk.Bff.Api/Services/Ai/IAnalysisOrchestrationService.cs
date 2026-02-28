using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;

// Note: AnalysisResumeResult is in Sprk.Bff.Api.Models.Ai namespace

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates analysis execution across multiple services.
/// Coordinates Dataverse entity operations, SPE file access, and Azure OpenAI calls.
/// Implements ADR-001 (BFF orchestration pattern) and ADR-013 (AI Architecture).
/// </summary>
public interface IAnalysisOrchestrationService
{
    /// <summary>
    /// Execute a new analysis with streaming results.
    /// Creates Analysis record in Dataverse and orchestrates:
    /// 1. Scope resolution (Skills, Knowledge, Tools)
    /// 2. Context building (prompt construction)
    /// 3. File extraction (via ITextExtractor) - supports multiple docs
    /// 4. AI execution (via IOpenAiClient)
    /// 5. Working document updates
    /// </summary>
    /// <param name="request">Analysis request with document IDs, action, and scopes.</param>
    /// <param name="httpContext">HTTP context for OBO authentication when downloading files from SPE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream chunks for SSE response.</returns>
    /// <remarks>
    /// Phase 1: Only request.DocumentIds[0] is processed.
    /// Phase 2: All documents in array are processed and synthesized.
    /// </remarks>
    IAsyncEnumerable<AnalysisStreamChunk> ExecuteAnalysisAsync(
        AnalysisExecuteRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Continue existing analysis via conversational chat.
    /// Loads analysis context + chat history and streams updated output.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="userMessage">User's refinement message.</param>
    /// <param name="httpContext">HTTP context for OBO authentication when downloading files from SPE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream chunks for SSE response.</returns>
    /// <exception cref="KeyNotFoundException">When analysis not found.</exception>
    IAsyncEnumerable<AnalysisStreamChunk> ContinueAnalysisAsync(
        Guid analysisId,
        string userMessage,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Save working document to SPE and create Document record.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="request">Save request with filename and format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Saved document result with IDs and URLs.</returns>
    /// <exception cref="KeyNotFoundException">When analysis not found.</exception>
    /// <exception cref="InvalidOperationException">When analysis has no working document.</exception>
    Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        AnalysisSaveRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Export analysis output to various destinations.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="request">Export request with format and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export result with status and details.</returns>
    /// <exception cref="KeyNotFoundException">When analysis not found.</exception>
    Task<ExportResult> ExportAnalysisAsync(
        Guid analysisId,
        AnalysisExportRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get analysis record with full details including chat history.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis detail result.</returns>
    /// <exception cref="KeyNotFoundException">When analysis not found.</exception>
    Task<AnalysisDetailResult> GetAnalysisAsync(
        Guid analysisId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resume an analysis session, restoring context and chat history.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="request">Resume request with document ID and chat history options.</param>
    /// <param name="httpContext">HTTP context for OBO authentication when downloading files from SPE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resume result with restored context.</returns>
    Task<AnalysisResumeResult> ResumeAnalysisAsync(
        Guid analysisId,
        AnalysisResumeRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute analysis using a playbook configuration.
    /// Loads playbook, resolves scopes, and orchestrates tool execution.
    /// </summary>
    /// <param name="request">Playbook execution request with playbook ID and document IDs.</param>
    /// <param name="httpContext">HTTP context for OBO authentication when downloading files from SPE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream chunks for SSE response.</returns>
    /// <remarks>
    /// This method:
    /// 1. Loads playbook configuration from Dataverse via IPlaybookService
    /// 2. Resolves scopes (Skills, Knowledge, Tools) via IScopeResolverService
    /// 3. Executes tools from playbook configuration via IToolHandlerRegistry
    /// 4. Streams results back to client
    /// </remarks>
    IAsyncEnumerable<AnalysisStreamChunk> ExecutePlaybookAsync(
        PlaybookExecuteRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request for playbook-based analysis execution.
/// </summary>
public record PlaybookExecuteRequest
{
    /// <summary>The playbook ID to execute.</summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>Document IDs to analyze.</summary>
    public required Guid[] DocumentIds { get; init; }

    /// <summary>Optional action ID override. If not specified, uses playbook's default action.</summary>
    public Guid? ActionId { get; init; }

    /// <summary>Additional context or instructions for the analysis.</summary>
    public string? AdditionalContext { get; init; }

    /// <summary>
    /// Optional existing analysis record ID to update in Dataverse.
    /// When provided, the orchestrator writes results to this existing record
    /// instead of generating a new GUID.
    /// </summary>
    public Guid? AnalysisId { get; init; }
}
