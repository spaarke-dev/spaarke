namespace Sprk.Bff.Api.Models.Documents;

/// <summary>
/// Request body for POST /api/documents/bulk-download (FR-BFF-02).
/// </summary>
/// <remarks>
/// <para>
/// Used by the Documents PCF bulk-action bar "Download selected" (FR-DOC-02).
/// Maximum 500 document IDs per request — HTTP 413 returned above that cap.
/// </para>
/// </remarks>
public sealed class BulkDownloadRequest
{
    /// <summary>
    /// Dataverse document IDs (sprk_document.sprk_documentid as GUID strings) to include in the zip.
    /// Maximum 500 per request.
    /// </summary>
    public List<string> DocumentIds { get; set; } = new();
}
