using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages transient working document state during analysis refinement.
/// Handles Dataverse updates and SPE storage operations.
/// </summary>
public interface IWorkingDocumentService
{
    /// <summary>
    /// Update working document in Dataverse as chunks stream in.
    /// Uses optimistic concurrency to avoid conflicts.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="content">Current working document content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken);

    /// <summary>
    /// spaarkeai-mcp-bff-api-r2 task 037 review F-A: the same <c>sprk_workingdocument</c> write as
    /// <see cref="UpdateWorkingDocumentAsync"/>, but it REPORTS a failure instead of only logging it, so the final save of a run can skip
    /// the finalize when its content was not written. <see cref="UpdateWorkingDocumentAsync"/> keeps its swallow-and-log behaviour for its
    /// streaming callers.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="content">The working document content (the caller keeps it within the column's limit).</param>
    /// <param name="cancellationToken">Cancellation token; a cancelled write throws.</param>
    /// <returns><c>true</c> when the content was written; <c>false</c> when the write failed (logged with the exception type only).</returns>
    Task<bool> TryUpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark analysis as completed and copy working document to final output.
    /// Updates status, timestamps, and token usage.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="inputTokens">Input token count.</param>
    /// <param name="outputTokens">Output token count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FinalizeAnalysisAsync(
        Guid analysisId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken);

    // unified-access-control-r2 task 162 (owner round 10 item 1): SaveToSpeAsync was DELETED with its only
    // caller, POST /api/ai/analysis/{analysisId}/save (no caller in the repo, not in any published API description).

    /// <summary>
    /// Create a new working version record for version history.
    /// Called periodically during analysis refinement.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="content">Version content.</param>
    /// <param name="tokenDelta">Token change from previous version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created version ID.</returns>
    Task<Guid> CreateWorkingVersionAsync(
        Guid analysisId,
        string content,
        int tokenDelta,
        CancellationToken cancellationToken);

    // task 064 (ADR-040 Path A, spec §13.5 / FR-22): UpdateChatHistoryAsync (persisting chat
    // history JSON to sprk_analysis.sprk_chathistory) was removed here. Task 062 confirmed the
    // per-turn write in ChatEndpoints.SendMessage was the last production caller; task 064's
    // hand-trace confirmed the last reader (AnalysisDocumentLoader.GetOrReloadFromDataverseAsync)
    // was removed in the same task, so the write was provably dead. See
    // notes/task-064-chathistory-read-drop.md.
}
