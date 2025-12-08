using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Dataverse-backed implementation of IDocumentStorageResolver.
/// Resolves Document GUID → (DriveId, ItemId) for SharePoint Embedded access.
/// Per senior dev spec (FILE-VIEWER-V5-FIX.md section 5.2).
/// </summary>
public sealed class DocumentStorageResolver : IDocumentStorageResolver
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DocumentStorageResolver> _logger;

    public DocumentStorageResolver(
        IDataverseService dataverseService,
        ILogger<DocumentStorageResolver> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving SPE pointers for document {DocumentId}", documentId);

        // Query Dataverse for document record
        var document = await _dataverseService.GetDocumentAsync(
            documentId.ToString(),
            cancellationToken);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in Dataverse", documentId);

            throw new SdapProblemException(
                code: "document_not_found",
                title: "Document not found",
                detail: $"Document with ID '{documentId}' does not exist.",
                statusCode: 404);
        }

        // Extract storage pointers
        var driveId = document.GraphDriveId;
        var itemId = document.GraphItemId;

        // Validate driveId format (SharePoint DriveId starts with "b!" and is 20+ chars)
        if (!IsLikelyDriveId(driveId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing DriveId: {DriveId}",
                documentId, driveId ?? "(null)");

            throw new SdapProblemException(
                code: "mapping_missing_drive",
                title: "Storage mapping incomplete",
                detail: "DriveId is not recorded or invalid for this document. The file may still be uploading.",
                statusCode: 409);
        }

        // Validate itemId format (Graph ItemId is alphanumeric, 20+ chars)
        if (!IsLikelyItemId(itemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing ItemId: {ItemId}",
                documentId, itemId ?? "(null)");

            throw new SdapProblemException(
                code: "mapping_missing_item",
                title: "Storage mapping incomplete",
                detail: "ItemId is not recorded or invalid for this document. The file may still be uploading.",
                statusCode: 409);
        }

        _logger.LogInformation(
            "Resolved document {DocumentId} to storage pointers (DriveId: {DriveIdPrefix}..., ItemId: {ItemIdPrefix}...)",
            documentId,
            driveId!.Length > 8 ? driveId.Substring(0, 8) : driveId,
            itemId!.Length > 8 ? itemId.Substring(0, 8) : itemId);

        return (driveId!, itemId!);
    }

    /// <summary>
    /// Heuristic check for SharePoint DriveId format.
    /// Valid: starts with "b!" and is 20+ characters
    /// Example: b!OWdlYTJh...
    /// </summary>
    private static bool IsLikelyDriveId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith("b!", StringComparison.Ordinal)
           && value.Length > 20;

    /// <summary>
    /// Heuristic check for Graph ItemId format.
    /// Valid: alphanumeric start, 20+ characters
    /// Example: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
    /// </summary>
    private static bool IsLikelyItemId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && char.IsLetterOrDigit(value[0])
           && value.Length > 20;
}
