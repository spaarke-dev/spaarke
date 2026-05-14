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
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<DocumentStorageResolver> _logger;

    public DocumentStorageResolver(
        IDocumentDataverseService documentService,
        ILogger<DocumentStorageResolver> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving SPE pointers for document {DocumentId}", documentId);

        // Query Dataverse for document record
        var document = await _documentService.GetDocumentAsync(
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

        // Extract storage pointers. DriveId/ItemId presence is the source of truth
        // for whether a file exists in SPE — sprk_hasfile is a Dataverse-side flag
        // that can be stale (e.g., upload completed but flag never flipped). Use it
        // only as a hint when DriveId/ItemId is missing, to distinguish "never uploaded"
        // (HasFile=false) from "partial/failed upload" (HasFile=true).
        var driveId = document.GraphDriveId;
        var itemId = document.GraphItemId;

        // Validate driveId format (SharePoint DriveId starts with "b!" and is 20+ chars)
        if (!IsLikelyDriveId(driveId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing DriveId: {DriveId} (HasFile={HasFile})",
                documentId, driveId ?? "(null)", document.HasFile);

            if (!document.HasFile)
            {
                throw new SdapProblemException(
                    code: "no_file_attached",
                    title: "No file attached",
                    detail: "This document record does not have a file attached yet. Upload a file before accessing it.",
                    statusCode: 409);
            }

            throw new SdapProblemException(
                code: "mapping_missing_drive",
                title: "Storage mapping incomplete",
                detail: "Document is marked as having a file (sprk_hasfile=true) but the Graph Drive ID is empty. The upload may still be in progress or did not complete successfully.",
                statusCode: 409);
        }

        // Validate itemId format (Graph ItemId is alphanumeric, 20+ chars)
        if (!IsLikelyItemId(itemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing ItemId: {ItemId} (HasFile={HasFile})",
                documentId, itemId ?? "(null)", document.HasFile);

            if (!document.HasFile)
            {
                throw new SdapProblemException(
                    code: "no_file_attached",
                    title: "No file attached",
                    detail: "This document record does not have a file attached yet. Upload a file before accessing it.",
                    statusCode: 409);
            }

            throw new SdapProblemException(
                code: "mapping_missing_item",
                title: "Storage mapping incomplete",
                detail: "Document is marked as having a file (sprk_hasfile=true) but the Graph Item ID is empty. The upload may still be in progress or did not complete successfully.",
                statusCode: 409);
        }

        _logger.LogInformation(
            "Resolved document {DocumentId} to storage pointers (DriveId length: {DriveIdLength}, ItemId length: {ItemIdLength})",
            documentId,
            driveId!.Length,
            itemId!.Length);

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
