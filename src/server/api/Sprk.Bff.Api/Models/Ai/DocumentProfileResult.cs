namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of Document Profile execution.
/// Indicates success, partial success (soft failure), or complete failure.
/// </summary>
/// <remarks>
/// <para><strong>Success States:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Full Success</strong>: Success = true, PartialStorage = false - All outputs stored in both locations</description></item>
/// <item><description><strong>Partial Success (Soft Failure)</strong>: Success = true, PartialStorage = true - Outputs stored in sprk_analysisoutput but field mapping to sprk_document failed</description></item>
/// <item><description><strong>Failure</strong>: Success = false - Analysis execution or output storage failed</description></item>
/// </list>
/// <para><strong>Usage:</strong></para>
/// <code>
/// // Full success
/// var result = DocumentProfileResult.FullSuccess(analysisId);
///
/// // Partial success (soft failure)
/// var result = DocumentProfileResult.PartialSuccess(analysisId, "Field mapping failed but outputs preserved");
///
/// // Failure
/// var result = DocumentProfileResult.Failure("Analysis execution failed");
/// </code>
/// </remarks>
public record DocumentProfileResult
{
    /// <summary>
    /// Indicates overall success (true if analysis executed and outputs stored, even if field mapping failed).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Indicates partial storage success.
    /// True if outputs were stored in sprk_analysisoutput but field mapping to sprk_document fields failed.
    /// This is a "soft failure" - the analysis succeeded but some post-processing failed.
    /// </summary>
    public bool PartialStorage { get; init; }

    /// <summary>
    /// User-friendly message describing the result.
    /// For partial success, explains what succeeded and what failed.
    /// For failure, explains what went wrong.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Analysis ID for navigation to full results in Analysis Workspace.
    /// Null if analysis execution failed before creating the analysis record.
    /// </summary>
    public Guid? AnalysisId { get; init; }

    /// <summary>
    /// Creates a full success result.
    /// </summary>
    /// <param name="analysisId">The created analysis ID.</param>
    /// <returns>Result indicating complete success.</returns>
    public static DocumentProfileResult FullSuccess(Guid analysisId)
    {
        return new DocumentProfileResult
        {
            Success = true,
            PartialStorage = false,
            Message = "Document profile generated successfully",
            AnalysisId = analysisId
        };
    }

    /// <summary>
    /// Creates a partial success (soft failure) result.
    /// Analysis executed and outputs stored, but field mapping to document failed.
    /// </summary>
    /// <param name="analysisId">The created analysis ID.</param>
    /// <param name="message">Message explaining what failed (e.g., "Field mapping failed but outputs preserved in Analysis Workspace").</param>
    /// <returns>Result indicating partial success.</returns>
    public static DocumentProfileResult PartialSuccess(Guid analysisId, string message)
    {
        return new DocumentProfileResult
        {
            Success = true,
            PartialStorage = true,
            Message = message,
            AnalysisId = analysisId
        };
    }

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    /// <param name="message">User-friendly error message.</param>
    /// <param name="analysisId">Optional analysis ID if record was created before failure.</param>
    /// <returns>Result indicating failure.</returns>
    public static DocumentProfileResult Failure(string message, Guid? analysisId = null)
    {
        return new DocumentProfileResult
        {
            Success = false,
            PartialStorage = false,
            Message = message,
            AnalysisId = analysisId
        };
    }
}
