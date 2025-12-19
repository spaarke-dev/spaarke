using System.IO;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.Preview;
using Microsoft.Graph.Models;
using Spaarke.Core.Auth;
using Spaarke.Core.Utilities;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// File access endpoints for SharePoint Embedded files using On-Behalf-Of (OBO) authentication.
/// Implements Microsoft's recommended patterns for SPE file access (Nov 2025).
///
/// Updated: OBO Refactor (Jan 2025) - Uses ForUserAsync for user-context authentication
///
/// References:
/// - Preview: https://learn.microsoft.com/en-us/graph/api/driveitem-preview
/// - Content: https://learn.microsoft.com/en-us/graph/api/driveitem-get-content
/// - Office: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences
/// </summary>
public static class FileAccessEndpoints
{
    public static IEndpointRouteBuilder MapFileAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var docs = app.MapGroup("/api/documents").RequireAuthorization();

        // Register endpoints using method groups (fixes CS1593 compilation error)
        docs.MapGet("/{documentId}/preview-url", GetPreviewUrl)
            .WithName("GetDocumentPreviewUrl")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/preview", GetPreview)
            .WithName("GetDocumentPreview")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/content", GetContent)
            .WithName("GetDocumentContent")
            .WithTags("File Access")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/office", GetOffice)
            .WithName("GetDocumentOfficeViewer")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/open-links", GetOpenLinks)
            .WithName("GetDocumentOpenLinks")
            .WithTags("File Access")
            .Produces<OpenLinksResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/view-url", GetViewUrl)
            .WithName("GetDocumentViewUrl")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return app;

        // Static local functions (method groups)

        /// <summary>
        /// GET /api/documents/{documentId}/preview-url
        /// Returns ephemeral preview URL using user's delegated permissions (OBO)
        /// Includes checkout status for PCF control to show lock indicators
        /// </summary>
        static async Task<IResult> GetPreviewUrl(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            DocumentCheckoutService checkoutService,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetPreviewUrl called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4. Create Graph client using OBO (user context)
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            // 5. Call Graph API to get preview URL
            // Request chromeless preview (no SharePoint header/toolbar)
            var previewRequest = new PreviewPostRequestBody
            {
                // Note: Graph SDK may not expose chromeless property directly
                // If not available, we'll modify the URL after receiving it
                AdditionalData = new Dictionary<string, object>
                {
                    { "chromeless", true },  // Hide SharePoint preview header
                    { "viewer", "onedrive" }  // Use OneDrive viewer
                }
            };

            var previewResponse = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .Preview
                .PostAsync(previewRequest, cancellationToken: ct);

            if (previewResponse == null || string.IsNullOrEmpty(previewResponse.GetUrl))
            {
                throw new SdapProblemException(
                    "preview_not_available",
                    "Preview Not Available",
                    $"Graph API did not return a preview URL for document {documentId}",
                    500
                );
            }

            logger.LogInformation("Preview URL retrieved successfully | TraceId: {TraceId}",
                context.TraceIdentifier);

            // 6. Modify preview URL to hide SharePoint banner/header
            // Use Microsoft-documented 'nb=true' parameter (no banner)
            // Reference: https://learn.microsoft.com/en-us/sharepoint/dev/
            var previewUrl = previewResponse.GetUrl;
            if (!string.IsNullOrEmpty(previewUrl))
            {
                var separator = previewUrl.Contains('?') ? '&' : '?';
                // nb=true hides the top banner/header in SharePoint embed.aspx
                previewUrl = $"{previewUrl}{separator}nb=true";
                logger.LogInformation("Modified preview URL with nb=true (no banner) | TraceId: {TraceId}",
                    context.TraceIdentifier);
            }

            // 7. Extract file extension from filename
            string? fileExtension = null;
            if (!string.IsNullOrEmpty(document.FileName))
            {
                var lastDot = document.FileName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < document.FileName.Length - 1)
                {
                    fileExtension = document.FileName.Substring(lastDot + 1);
                }
            }

            // 8. Get checkout status for the document
            CheckoutStatusInfo? checkoutStatus = null;
            try
            {
                checkoutStatus = await checkoutService.GetCheckoutStatusAsync(docGuid, context.User, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - checkout status is non-critical
                logger.LogWarning(ex, "Failed to get checkout status for document {DocumentId}", documentId);
            }

            // 9. Return PCF-compatible response (flat structure for SpeFileViewer/SpeDocumentViewer)
            return TypedResults.Ok(new
            {
                previewUrl = previewUrl,  // Modified URL with chromeless parameters
                documentInfo = new
                {
                    name = document.FileName ?? document.Name ?? "Unknown",
                    fileExtension = fileExtension,
                    size = document.FileSize,
                    lastModified = document.ModifiedOn.ToString("o") // ISO 8601 format
                },
                checkoutStatus = checkoutStatus != null ? new
                {
                    isCheckedOut = checkoutStatus.IsCheckedOut,
                    checkedOutBy = checkoutStatus.CheckedOutBy != null ? new
                    {
                        id = checkoutStatus.CheckedOutBy.Id,
                        name = checkoutStatus.CheckedOutBy.Name,
                        email = checkoutStatus.CheckedOutBy.Email
                    } : null,
                    checkedOutAt = checkoutStatus.CheckedOutAt?.ToString("o"),
                    isCurrentUser = checkoutStatus.IsCurrentUser
                } : null,
                correlationId = context.TraceIdentifier
            });
        }

        /// <summary>
        /// GET /api/documents/{documentId}/preview
        /// Returns embeddable preview URL for iframe scenarios using OBO
        /// </summary>
        static async Task<IResult> GetPreview(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetPreview called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            // 4. Get preview URL using OBO
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            var previewResponse = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .Preview
                .PostAsync(new PreviewPostRequestBody(), cancellationToken: ct);

            if (string.IsNullOrEmpty(previewResponse?.GetUrl))
            {
                throw new SdapProblemException(
                    "preview_not_available",
                    "Preview Not Available",
                    $"Graph API did not return a preview URL for document {documentId}",
                    500
                );
            }

            // 5. Redirect to preview page
            logger.LogInformation("Redirecting to preview URL for document {DocumentId}", documentId);
            return TypedResults.Redirect(previewResponse.GetUrl);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/content
        /// Returns file content stream using OBO
        /// </summary>
        static async Task<IResult> GetContent(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetContent called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            // 4. Download file content using OBO
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            var contentStream = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .Content
                .GetAsync(cancellationToken: ct);

            if (contentStream == null)
            {
                throw new SdapProblemException(
                    "content_not_found",
                    "File Content Not Found",
                    $"Graph API returned null content stream for document {documentId}",
                    500
                );
            }

            // 5. Return file stream with proper content type
            var contentType = document.MimeType ?? "application/octet-stream";
            var fileName = document.FileName ?? $"{documentId}.bin";

            logger.LogInformation("Returning file content | FileName: {FileName} | ContentType: {ContentType}",
                fileName, contentType);

            return TypedResults.Stream(contentStream, contentType, fileName);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/office
        /// Returns Office web viewer/editor URLs using OBO
        /// </summary>
        static async Task<IResult> GetOffice(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetOffice called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            // 4. Get Office web app URL using OBO
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            var driveItem = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "id", "name", "webUrl" };
                }, cancellationToken: ct);

            if (string.IsNullOrEmpty(driveItem?.WebUrl))
            {
                throw new SdapProblemException(
                    "office_url_not_available",
                    "Office URL Not Available",
                    $"Graph API did not return a webUrl for document {documentId}",
                    500
                );
            }

            logger.LogInformation("Office URL retrieved | WebUrl: {WebUrl}", driveItem.WebUrl);

            // 5. Return structured JSON response (not redirect)
            // Office Online will enforce actual permissions when user accesses the URL
            return TypedResults.Ok(new
            {
                officeUrl = driveItem.WebUrl,
                permissions = new
                {
                    canEdit = true,  // Unknown at BFF level - Office Online will enforce
                    canView = true,
                    role = "unknown"
                },
                correlationId = context.TraceIdentifier
            });
        }

        /// <summary>
        /// GET /api/documents/{documentId}/open-links
        /// Returns desktop protocol URL (ms-word:, ms-excel:, ms-powerpoint:) and web URL
        /// for opening documents in native Office applications.
        /// </summary>
        static async Task<IResult> GetOpenLinks(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetOpenLinks called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4. Create Graph client using OBO (user context)
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            // 5. Get DriveItem from Graph to retrieve URLs, parentReference, and mimeType
            var driveItem = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "id", "name", "webUrl", "webDavUrl", "file", "parentReference" };
                }, cancellationToken: ct);

            if (driveItem == null)
            {
                throw new SdapProblemException(
                    "item_not_found",
                    "Drive Item Not Found",
                    $"Graph API did not return drive item for document {documentId}",
                    404
                );
            }

            if (string.IsNullOrEmpty(driveItem.WebUrl))
            {
                throw new SdapProblemException(
                    "web_url_not_available",
                    "Web URL Not Available",
                    $"Graph API did not return a webUrl for document {documentId}",
                    500
                );
            }

            // 6. Extract MIME type from file facet
            var mimeType = driveItem.File?.MimeType ?? document.MimeType ?? "application/octet-stream";
            var fileName = driveItem.Name ?? document.FileName ?? "Unknown";

            // 7. Construct direct file URL for desktop protocol
            // The webUrl returns Doc.aspx (Office Online URL) which doesn't work well with ms-word: protocol
            // We need to construct a direct file URL from the parent path + filename
            string? directFileUrl = null;

            // Prefer webDavUrl if available (direct file URL)
            if (!string.IsNullOrEmpty(driveItem.WebDavUrl))
            {
                directFileUrl = driveItem.WebDavUrl;
            }
            // Otherwise construct from parent path
            else if (driveItem.ParentReference?.Path != null && !string.IsNullOrEmpty(fileName))
            {
                // ParentReference.Path format: /drives/{driveId}/root:/folder/path
                // Extract the path after "root:" and construct URL
                var pathParts = driveItem.ParentReference.Path.Split("root:");
                if (pathParts.Length > 1)
                {
                    var folderPath = pathParts[1].TrimStart('/');
                    // Get base SharePoint URL from webUrl (before /_layouts/)
                    var webUrlParts = driveItem.WebUrl!.Split("/_layouts/");
                    if (webUrlParts.Length > 0)
                    {
                        var baseUrl = webUrlParts[0];
                        directFileUrl = $"{baseUrl}/{folderPath}/{Uri.EscapeDataString(fileName)}";
                    }
                }
            }

            // Fall back to webUrl if we couldn't construct a direct URL
            var urlForDesktop = directFileUrl ?? driveItem.WebUrl;

            logger.LogInformation(
                "OpenLinks URL selection | WebUrl: {WebUrl} | WebDavUrl: {WebDavUrl} | DirectFileUrl: {DirectFileUrl} | UsingUrl: {UsingUrl}",
                driveItem.WebUrl, driveItem.WebDavUrl, directFileUrl, urlForDesktop);

            // 8. Generate desktop protocol URL using DesktopUrlBuilder
            var desktopUrl = DesktopUrlBuilder.FromMime(urlForDesktop, mimeType);

            logger.LogInformation(
                "OpenLinks generated | FileName: {FileName} | MimeType: {MimeType} | HasDesktopUrl: {HasDesktopUrl} | TraceId: {TraceId}",
                fileName, mimeType, desktopUrl != null, context.TraceIdentifier);

            // 8. Return response
            return TypedResults.Ok(new OpenLinksResponse(
                DesktopUrl: desktopUrl,
                WebUrl: driveItem.WebUrl,
                MimeType: mimeType,
                FileName: fileName
            ));
        }

        /// <summary>
        /// GET /api/documents/{documentId}/view-url
        /// Returns embeddable view URL using driveItem webUrl (not cached Preview action).
        /// Use this for real-time file viewing without the 30-60s Preview cache delay.
        /// Includes checkout status for PCF control to show lock indicators.
        /// </summary>
        static async Task<IResult> GetViewUrl(
            string documentId,
            IDataverseService dataverseService,
            IGraphClientFactory graphFactory,
            DocumentCheckoutService checkoutService,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetViewUrl called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4. Create Graph client using OBO (user context)
            var graphClient = await graphFactory.ForUserAsync(context, ct);

            // 5. Get driveItem metadata for file info
            var driveItem = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "id", "name", "webUrl", "size", "lastModifiedDateTime" };
                }, cancellationToken: ct);

            if (driveItem == null)
            {
                throw new SdapProblemException(
                    "view_url_not_available",
                    "View URL Not Available",
                    $"Graph API did not return drive item for document {documentId}",
                    500
                );
            }

            // 6. Use Preview action to get embeddable URL (works for SPE files)
            // The Preview action returns a properly authenticated URL that works in iframes
            // Note: Preview URLs are cached for 30-60 seconds by SharePoint, but this is
            // the only reliable way to get an embeddable URL for SPE containers
            var previewRequest = new PreviewPostRequestBody
            {
                AdditionalData = new Dictionary<string, object>
                {
                    { "chromeless", true },
                    { "viewer", "onedrive" }
                }
            };

            var previewResponse = await graphClient.Drives[document.GraphDriveId!]
                .Items[document.GraphItemId!]
                .Preview
                .PostAsync(previewRequest, cancellationToken: ct);

            string viewUrl;
            if (previewResponse != null && !string.IsNullOrEmpty(previewResponse.GetUrl))
            {
                // Use the preview URL with nb=true (no banner)
                viewUrl = previewResponse.GetUrl;
                var separator = viewUrl.Contains('?') ? '&' : '?';
                viewUrl = $"{viewUrl}{separator}nb=true";
                logger.LogInformation("Using Preview action URL for embedding");
            }
            else
            {
                // Fall back to webUrl if Preview fails
                viewUrl = driveItem.WebUrl ?? "";
                logger.LogWarning("Preview action failed, falling back to webUrl");
            }

            logger.LogInformation("View URL constructed | FileName: {FileName} | ViewUrl: {ViewUrl}",
                driveItem.Name, viewUrl);

            // 6. Extract file extension from filename
            string? fileExtension = null;
            var fileName = driveItem.Name ?? document.FileName ?? "Unknown";
            if (!string.IsNullOrEmpty(fileName))
            {
                var lastDot = fileName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < fileName.Length - 1)
                {
                    fileExtension = fileName.Substring(lastDot + 1);
                }
            }

            // 7. Get checkout status for the document
            CheckoutStatusInfo? checkoutStatus = null;
            try
            {
                checkoutStatus = await checkoutService.GetCheckoutStatusAsync(docGuid, context.User, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - checkout status is non-critical
                logger.LogWarning(ex, "Failed to get checkout status for document {DocumentId}", documentId);
            }

            // 8. Return PCF-compatible response (matches preview-url format for easy switching)
            return TypedResults.Ok(new
            {
                previewUrl = viewUrl,  // Named previewUrl for PCF compatibility
                documentInfo = new
                {
                    name = fileName,
                    fileExtension = fileExtension,
                    size = driveItem.Size ?? document.FileSize,
                    lastModified = (driveItem.LastModifiedDateTime ?? document.ModifiedOn).ToString("o")
                },
                checkoutStatus = checkoutStatus != null ? new
                {
                    isCheckedOut = checkoutStatus.IsCheckedOut,
                    checkedOutBy = checkoutStatus.CheckedOutBy != null ? new
                    {
                        id = checkoutStatus.CheckedOutBy.Id,
                        name = checkoutStatus.CheckedOutBy.Name,
                        email = checkoutStatus.CheckedOutBy.Email
                    } : null,
                    checkedOutAt = checkoutStatus.CheckedOutAt?.ToString("o"),
                    isCurrentUser = checkoutStatus.IsCurrentUser
                } : null,
                correlationId = context.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Validates SPE pointer format before calling Graph API.
    /// Throws SdapProblemException for invalid/missing pointers.
    /// </summary>
    private static void ValidateSpePointers(string? driveId, string? itemId, string documentId)
    {
        // Validate driveId exists
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new SdapProblemException(
                "mapping_missing_drive",
                "SPE Drive ID Missing",
                $"Document {documentId} does not have a Graph Drive ID (sprk_graphdriveid field is empty). " +
                $"Ensure the document has been uploaded to SharePoint Embedded.",
                409
            );
        }

        // Validate driveId format (SharePoint Embedded drives always start with "b!")
        if (!driveId.StartsWith("b!", StringComparison.Ordinal))
        {
            throw new SdapProblemException(
                "invalid_drive_id",
                "Invalid SPE Drive ID Format",
                $"Drive ID '{driveId}' does not start with 'b!' (expected SharePoint Embedded container format)",
                400
            );
        }

        // Validate itemId exists
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new SdapProblemException(
                "mapping_missing_item",
                "SPE Item ID Missing",
                $"Document {documentId} does not have a Graph Item ID (sprk_graphitemid field is empty). " +
                $"Ensure the document has been uploaded to SharePoint Embedded.",
                409
            );
        }

        // Validate itemId length (SharePoint item IDs are typically 20+ characters)
        if (itemId.Length < 20)
        {
            throw new SdapProblemException(
                "invalid_item_id",
                "Invalid SPE Item ID Format",
                $"Item ID '{itemId}' is too short (expected at least 20 characters)",
                400
            );
        }
    }
}
