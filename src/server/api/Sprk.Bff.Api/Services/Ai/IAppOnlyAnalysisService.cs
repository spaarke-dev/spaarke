namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for app-only document analysis.
/// Enables unit testing of services that depend on app-only analysis.
/// </summary>
public interface IAppOnlyAnalysisService
{
    /// <summary>
    /// Default playbook name for Document Profile analysis.
    /// </summary>
    const string DefaultPlaybookName = "Document Profile";

    /// <summary>
    /// Analyze a document and update its Document Profile fields in Dataverse.
    /// Uses app-only authentication for all operations.
    /// </summary>
    /// <param name="documentId">The Dataverse Document ID to analyze.</param>
    /// <param name="playbookName">Optional playbook name override (default: "Document Profile").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status and any generated profile data.</returns>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        Guid documentId,
        string? playbookName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze a document from an existing stream (e.g., from email attachment).
    /// </summary>
    /// <param name="documentId">The Dataverse Document ID to update.</param>
    /// <param name="fileName">The file name for extension detection.</param>
    /// <param name="fileStream">The file content stream.</param>
    /// <param name="playbookName">Optional playbook name override (default: "Document Profile").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status and any generated profile data.</returns>
    Task<DocumentAnalysisResult> AnalyzeDocumentFromStreamAsync(
        Guid documentId,
        string fileName,
        Stream fileStream,
        string? playbookName = null,
        CancellationToken cancellationToken = default);
}
