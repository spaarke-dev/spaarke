using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Email;

/// <summary>
/// Request to convert a Dataverse email activity to a document.
/// Used by POST /api/emails/{emailId}/save-as-document
/// </summary>
public class ConvertEmailToDocumentRequest
{
    /// <summary>
    /// SPE container ID where the document will be stored.
    /// If not specified, uses the default container from configuration.
    /// </summary>
    public string? ContainerId { get; set; }

    /// <summary>
    /// Whether to include attachments embedded in the .eml file.
    /// Default: true
    /// </summary>
    public bool IncludeAttachments { get; set; } = true;

    /// <summary>
    /// Whether to create separate document records for each attachment.
    /// Default: true
    /// </summary>
    public bool CreateAttachmentDocuments { get; set; } = true;

    /// <summary>
    /// Whether to queue the documents for AI processing.
    /// Default: true (from config)
    /// </summary>
    public bool? QueueForAiProcessing { get; set; }
}

/// <summary>
/// Response from email-to-document conversion.
/// </summary>
public class ConvertEmailToDocumentResponse
{
    /// <summary>
    /// Whether the conversion was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The created document ID (sprk_document GUID).
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// The filename of the created .eml file.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Size of the .eml file in bytes.
    /// </summary>
    public long? FileSizeBytes { get; init; }

    /// <summary>
    /// Graph drive item ID for the uploaded file.
    /// </summary>
    public string? GraphItemId { get; init; }

    /// <summary>
    /// Number of attachments embedded in the .eml.
    /// </summary>
    public int AttachmentCount { get; init; }

    /// <summary>
    /// Document IDs for created attachment documents.
    /// Only populated if CreateAttachmentDocuments was true.
    /// </summary>
    public IReadOnlyList<AttachmentDocumentInfo> AttachmentDocuments { get; init; } = [];

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Create a success response.
    /// </summary>
    public static ConvertEmailToDocumentResponse Succeeded(
        Guid documentId,
        string fileName,
        long fileSizeBytes,
        string graphItemId,
        int attachmentCount,
        IReadOnlyList<AttachmentDocumentInfo> attachmentDocuments) => new()
        {
            Success = true,
            DocumentId = documentId,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            GraphItemId = graphItemId,
            AttachmentCount = attachmentCount,
            AttachmentDocuments = attachmentDocuments
        };

    /// <summary>
    /// Create a failure response.
    /// </summary>
    public static ConvertEmailToDocumentResponse Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Information about a created attachment document.
/// </summary>
public class AttachmentDocumentInfo
{
    /// <summary>
    /// The created document ID.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Original attachment filename.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Graph drive item ID.
    /// </summary>
    public string? GraphItemId { get; init; }
}

/// <summary>
/// Response for checking if an email has already been saved as a document.
/// </summary>
public class EmailDocumentStatusResponse
{
    /// <summary>
    /// Whether a document already exists for this email.
    /// </summary>
    public bool DocumentExists { get; init; }

    /// <summary>
    /// The existing document ID if one exists.
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// When the document was created.
    /// </summary>
    public DateTime? CreatedOn { get; init; }
}
