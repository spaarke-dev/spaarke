namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Response from enqueuing a single document for analysis.
/// </summary>
/// <param name="JobId">The job ID for tracking.</param>
/// <param name="DocumentId">The document that was enqueued.</param>
public record EnqueueAnalysisResponse(
    Guid JobId,
    Guid DocumentId);

/// <summary>
/// Request to enqueue multiple documents for batch analysis.
/// </summary>
/// <param name="Documents">The documents to analyze (max 10).</param>
public record BatchAnalysisRequest(
    IReadOnlyList<DocumentAnalysisRequest> Documents);

/// <summary>
/// Response from batch enqueuing multiple documents.
/// </summary>
/// <param name="Jobs">The enqueued jobs with their document IDs.</param>
/// <param name="TotalEnqueued">Total number of jobs successfully enqueued.</param>
public record BatchAnalysisResponse(
    IReadOnlyList<EnqueueAnalysisResponse> Jobs,
    int TotalEnqueued);
