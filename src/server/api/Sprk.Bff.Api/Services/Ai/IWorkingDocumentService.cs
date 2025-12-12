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

    /// <summary>
    /// Save working document to SharePoint Embedded and create Document record.
    /// </summary>
    /// <param name="analysisId">The analysis record ID.</param>
    /// <param name="fileName">Target file name.</param>
    /// <param name="content">File content bytes.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Saved document result with IDs and URLs.</returns>
    Task<SavedDocumentResult> SaveToSpeAsync(
        Guid analysisId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);

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
}
