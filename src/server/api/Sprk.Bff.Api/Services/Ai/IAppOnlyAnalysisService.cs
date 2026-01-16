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

    /// <summary>
    /// Analyze an email and its attachments as a combined context.
    /// Combines email metadata + body + attachment text and executes the "Email Analysis" playbook.
    /// Results are stored on the main .eml Document record (FR-11, FR-12).
    /// </summary>
    /// <param name="emailId">The Dataverse email activity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status. Results stored on the main .eml Document.</returns>
    Task<EmailAnalysisResult> AnalyzeEmailAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);
}
