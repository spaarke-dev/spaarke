using System.IO;
using System.Text.Json;
using Spaarke.Core.Auth;
using Spaarke.Core.Utilities;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;

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

        // A-1 applies here identically (unified-access-control-r2 task 002, spec FR-01). GetContent
        // streams the document's bytes (`TypedResults.Stream`) from the same app-only SPE path as
        // /download, and had the same missing gate. Closing /download alone would have left the attack
        // scenario fully intact behind a different URL — the finding is the missing per-document
        // authorization, not the route name.
        docs.MapGet("/{documentId}/content", GetContent)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentContent")
            .WithTags("File Access")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
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

        // Recipient-openable SPE sharing link for a document (email-communication-solution-r5 R2 item
        // 12). OBO — the caller's own SPE access authorizes the createLink. Used by the email composer's
        // "Link" attachments so an emailed link opens the actual file (incl. for external recipients).
        docs.MapPost("/{documentId}/share-link", CreateShareLink)
            .WithName("CreateDocumentShareLink")
            .WithTags("File Access")
            .WithDescription("Create a recipient-openable SPE sharing link (Graph createLink, view/anonymous) for a document.")
            .Produces<ShareLinkResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway);

        docs.MapGet("/{documentId}/view-url", GetViewUrl)
            .WithName("GetDocumentViewUrl")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // Per-document authorization (unified-access-control-r2 task 002, spec FR-01, finding A-1).
        //
        // This route had NO per-document filter: the group's RequireAuthorization() asked only "are you
        // anyone?", and the handler then streamed app-only from SPE — so any authenticated caller could
        // download any document by GUID. That is R1's January-2026 attack scenario.
        //
        // The app-only SPE stream is NOT the defect and is deliberately unchanged: files written by the
        // managed identity can only be read back by it (auth constraints, Pattern 4 — Writer-Identity
        // Matching). What was missing is the Dataverse-level answer to "may THIS caller have this
        // document?", which the filter now supplies before any SPE call is made.
        //
        // Operation "read" matches the two routes that already do this correctly — the sibling
        // DataverseDocumentsEndpoints.cs `GET /api/v1/documents/{id}/download` and the eml-render route
        // below. Both download routes must reach the SAME decision for the same caller on the same
        // document; task 001 pinned their disagreement as the finding.
        docs.MapGet("/{documentId}/download", GetDownload)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentDownload")
            .WithTags("File Access")
            .WithDescription("Download document file. The caller is authorized against the document " +
                "first; the SPE stream itself is app-only because background-written files are only " +
                "readable by the identity that wrote them.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // GET /api/documents/{documentId}/eml-render (email-communication-solution-r5 task 010 / FR-07 / NFR-03).
        // Renders an archived .eml as sanitized, safe-to-display HTML for the reading pane. Unlike the sibling
        // routes above (which rely on the group's RequireAuthorization() + downstream Graph/Dataverse access),
        // this route ADDS a per-document DocumentAuthorizationFilter("read") because it is on the untrusted-
        // email-HTML path and MUST fail closed — an unauthorized/inaccessible document returns 403/404 with NO
        // HTML body (the filter/resolution rejects BEFORE any HTML is produced). ADR-008 (endpoint-filter authz).
        docs.MapGet("/{documentId}/eml-render", GetEmlRender)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentEmlRender")
            .WithTags("File Access")
            .WithDescription("Render an archived .eml as sanitized, safe HTML for the reading pane. " +
                "Server-side sanitization is the authoritative XSS boundary (NFR-03); fails closed on no-access.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
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
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4-5. Call Graph API (via SpeFileStore OBO facade) to get preview URL.
            // Request chromeless preview (no SharePoint header/toolbar). Per CICD-088b,
            // the Graph SDK request/response types stay inside Infrastructure.Graph.
            var rawPreviewUrl = await speFileStore.GetPreviewUrlAsUserAsync(
                context,
                document.GraphDriveId!,
                document.GraphItemId!,
                additionalData: new Dictionary<string, object>
                {
                    { "chromeless", true },  // Hide SharePoint preview header
                    { "viewer", "onedrive" }  // Use OneDrive viewer
                },
                ct: ct);

            if (string.IsNullOrEmpty(rawPreviewUrl))
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
            var previewUrl = rawPreviewUrl;
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
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // 4. Get preview URL using OBO (via SpeFileStore facade per CICD-088b)
            var previewUrl = await speFileStore.GetPreviewUrlAsUserAsync(
                context,
                document.GraphDriveId!,
                document.GraphItemId!,
                additionalData: null,
                ct: ct);

            if (string.IsNullOrEmpty(previewUrl))
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
            return TypedResults.Redirect(previewUrl);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/content
        /// Returns file content stream using OBO
        /// </summary>
        static async Task<IResult> GetContent(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // 4. Download file content using OBO (via SpeFileStore facade per CICD-088b)
            var contentStream = await speFileStore.GetContentStreamAsUserAsync(
                context, document.GraphDriveId!, document.GraphItemId!, ct);

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
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // 4. Get Office web app URL using OBO (via SpeFileStore facade per CICD-088b)
            var driveItem = await speFileStore.GetDriveItemAsUserAsync(
                context, document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl" }, ct: ct);

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
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4-5. Get DriveItem metadata via OBO (SpeFileStore facade per CICD-088b)
            var driveItem = await speFileStore.GetDriveItemAsUserAsync(
                context, document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl", "webDavUrl", "file", "parentReference" }, ct: ct);

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
            var mimeType = driveItem.MimeType ?? document.MimeType ?? "application/octet-stream";
            var fileName = string.IsNullOrEmpty(driveItem.Name) ? (document.FileName ?? "Unknown") : driveItem.Name;

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
            else if (driveItem.ParentReferencePath != null && !string.IsNullOrEmpty(fileName))
            {
                // ParentReference.Path format: /drives/{driveId}/root:/folder/path
                // Extract the path after "root:" and construct URL
                var pathParts = driveItem.ParentReferencePath.Split("root:");
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
        /// POST /api/documents/{documentId}/share-link
        /// Creates a recipient-openable SPE sharing link (Graph createLink, view/anonymous) for the
        /// document, so an emailed "Link" opens the actual file — including for external recipients
        /// (email-communication-solution-r5 R2 item 12). OBO: the caller's own SPE access authorizes it.
        /// </summary>
        static async Task<IResult> CreateShareLink(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("CreateShareLink called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            if (!Guid.TryParse(documentId, out _))
            {
                throw new SdapProblemException(
                    "invalid_id", "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format", 400);
            }

            var document = await dataverseService.GetDocumentAsync(documentId, ct);
            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found", "Document Not Found",
                    $"Document with ID '{documentId}' does not exist", 404);
            }

            // Reuses the same SPE-pointer validation as open-links (404/409 on missing/malformed pointers).
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            try
            {
                // view + anonymous: opens the file, works for external recipients (owner-approved scope,
                // R2 item 12). Non-expiring for now. Requires the tenant SPE/SharePoint external-sharing
                // policy to allow "Anyone" links; if disabled Graph throws → mapped to 502 below and the
                // caller (composer) falls back to the prior link (best-effort, never blocks the send).
                var url = await speFileStore.CreateSharingLinkAsUserAsync(
                    context, document.GraphDriveId!, document.GraphItemId!,
                    linkType: "view", scope: "anonymous", expiration: null, ct: ct);

                if (string.IsNullOrWhiteSpace(url))
                {
                    throw new SdapProblemException(
                        "share_link_unavailable", "Share Link Unavailable",
                        $"Graph returned no sharing link for document {documentId}", 502);
                }

                logger.LogInformation("CreateShareLink succeeded | DocumentId: {DocumentId} | TraceId: {TraceId}",
                    documentId, context.TraceIdentifier);
                return TypedResults.Ok(new ShareLinkResponse(url));
            }
            // ADR-007: endpoints must NOT reference Microsoft.Graph SDK types directly (the Graph
            // request/response + error types stay isolated in Infrastructure.Graph). We still want the
            // clean 502 mapping for a Graph OData error (most commonly: tenant policy forbids anonymous
            // links), so match it by type NAME via an exception filter — no `Microsoft.Graph.*` type
            // reference in this endpoint. Non-Graph exceptions propagate unchanged (behavior preserved).
            catch (Exception ex) when (ex.GetType().FullName?.Contains("ODataError", StringComparison.Ordinal) == true)
            {
                logger.LogWarning(ex,
                    "CreateShareLink Graph error | DocumentId: {DocumentId} | TraceId: {TraceId}",
                    documentId, context.TraceIdentifier);
                throw new SdapProblemException(
                    "share_link_failed", "Share Link Failed",
                    $"Could not create a sharing link (Graph): {ex.Message}", 502);
            }
        }

        /// <summary>
        /// GET /api/documents/{documentId}/view-url
        /// Returns embeddable view URL using driveItem webUrl (not cached Preview action).
        /// Use this for real-time file viewing without the 30-60s Preview cache delay.
        /// Includes checkout status for PCF control to show lock indicators.
        /// </summary>
        static async Task<IResult> GetViewUrl(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4-5. Get driveItem metadata for file info (via SpeFileStore facade per CICD-088b)
            var driveItem = await speFileStore.GetDriveItemAsUserAsync(
                context, document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl", "size", "lastModifiedDateTime" }, ct: ct);

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
            var previewUrlRaw = await speFileStore.GetPreviewUrlAsUserAsync(
                context, document.GraphDriveId!, document.GraphItemId!,
                additionalData: new Dictionary<string, object>
                {
                    { "chromeless", true },
                    { "viewer", "onedrive" }
                },
                ct: ct);

            string viewUrl;
            if (!string.IsNullOrEmpty(previewUrlRaw))
            {
                // Use the preview URL with nb=true (no banner)
                viewUrl = previewUrlRaw;
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

        /// <summary>
        /// GET /api/documents/{documentId}/download
        /// Downloads file content using app-only authentication via SpeFileStore.
        /// This is necessary for files uploaded by email-to-document automation,
        /// where users don't have direct SPE container permissions.
        /// </summary>
        static async Task<IResult> GetDownload(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetDownload called | DocumentId: {DocumentId} | TraceId: {TraceId}",
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 4. Download file stream from SPE using app-only auth
            var fileStream = await speFileStore.DownloadFileAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                ct);

            if (fileStream == null)
            {
                throw new SdapProblemException(
                    "file_not_found",
                    "File Not Found",
                    $"File content not found in storage for document {documentId}",
                    404
                );
            }

            // 5. Determine content type and filename
            var contentType = document.MimeType ?? "application/octet-stream";
            var fileName = document.FileName ?? $"{documentId}.bin";

            logger.LogInformation(
                "Streaming download | DocumentId: {DocumentId} | FileName: {FileName} | ContentType: {ContentType} | TraceId: {TraceId}",
                documentId, fileName, contentType, context.TraceIdentifier);

            // 6. Return streaming file response with proper headers
            return TypedResults.Stream(
                fileStream,
                contentType: contentType,
                fileDownloadName: fileName,
                enableRangeProcessing: true);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/eml-render
        /// Renders an archived .eml as sanitized, safe-to-display HTML for the reading pane (FR-07 / NFR-03).
        /// Reuses the GetDownload resolution shape (resolve document -> validate SPE pointers ->
        /// SpeFileStore.DownloadFileAsync), then parses with MimeKit (HTML-preserving), rewrites inline cid:
        /// images to data: URIs, and SANITIZES server-side (authoritative XSS boundary). Fails closed:
        /// unauthorized documents are rejected by DocumentAuthorizationFilter (403) and missing documents/files
        /// resolve to 404 — in neither case is any HTML body returned.
        /// </summary>
        static async Task<IResult> GetEmlRender(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            EmlToHtmlRenderer emlRenderer,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetEmlRender called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out _))
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
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // 4. Download the .eml stream from SPE via the EXISTING facade (app-only, per ADR-007) — no new
            //    download method, no GraphServiceClient injection.
            var fileStream = await speFileStore.DownloadFileAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                ct);

            // 5. Parse + cid:->data: rewrite + sanitize + shape the immutable-cacheable HTML response.
            return await BuildEmlRenderResponseAsync(fileStream, emlRenderer, documentId, ct);
        }
    }

    /// <summary>
    /// Parses the downloaded .eml stream into sanitized HTML and wraps it in a long-lived, immutable-cacheable
    /// response. A null stream (no .eml in SPE) throws a 404 SdapProblemException (the client degrades to
    /// sprk_body) — so NO HTML body is ever returned for a missing archive. Extracted (and internal) so the
    /// HTTP-shaping behavior (404-on-missing, 200 + immutable cache header + sanitized body) is unit-testable
    /// without mocking SpeFileStore/Graph (ADR-038).
    /// </summary>
    internal static async Task<IResult> BuildEmlRenderResponseAsync(
        Stream? emlStream,
        EmlToHtmlRenderer renderer,
        string documentId,
        CancellationToken ct)
    {
        if (emlStream == null)
        {
            throw new SdapProblemException(
                "file_not_found",
                "File Not Found",
                $"File content not found in storage for document {documentId}",
                404
            );
        }

        string sanitizedHtml;
        await using (emlStream)
        {
            sanitizedHtml = await renderer.RenderSanitizedHtmlAsync(emlStream, ct);
        }

        return new SanitizedEmlHtmlResult(sanitizedHtml);
    }

    /// <summary>
    /// IResult that writes sanitized email HTML with a long-lived immutable cache header. The archived .eml is
    /// content-immutable, so repeat opens hit the HTTP cache (spec NFR-01) — no bespoke server-side render cache.
    /// </summary>
    internal sealed class SanitizedEmlHtmlResult : IResult
    {
        // public: browser + shared caches may store the immutable per-document render (task 010 spec header).
        // Long-lived + immutable because the archived .eml never changes (spec NFR-01).
        internal static readonly string CacheControlValue =
            $"public, max-age={EmlToHtmlRenderer.ImmutableMaxAgeSeconds}, immutable";

        private readonly string _html;

        public SanitizedEmlHtmlResult(string html) => _html = html;

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            httpContext.Response.Headers.CacheControl = CacheControlValue;
            await httpContext.Response.WriteAsync(_html, System.Text.Encoding.UTF8, httpContext.RequestAborted);
        }
    }

    /// <summary>
    /// Validates SPE pointer format before calling Graph API.
    /// Throws SdapProblemException for invalid/missing pointers.
    /// DriveId/ItemId presence is the source of truth for whether a file exists in SPE.
    /// sprk_hasfile is a Dataverse-side flag that can be stale (upload completed but flag
    /// never flipped) — use it only to distinguish "never uploaded" (HasFile=false) from
    /// "partial/failed upload" (HasFile=true) when DriveId/ItemId is missing.
    /// </summary>
    private static void ValidateSpePointers(string? driveId, string? itemId, string documentId, bool hasFile)
    {
        // Validate driveId exists
        if (string.IsNullOrWhiteSpace(driveId))
        {
            if (!hasFile)
            {
                throw new SdapProblemException(
                    "no_file_attached",
                    "No File Attached",
                    $"Document {documentId} has no file attached yet (sprk_hasfile=false and sprk_graphdriveid is empty). Upload a file before accessing it.",
                    409
                );
            }

            throw new SdapProblemException(
                "mapping_missing_drive",
                "SPE Drive ID Missing",
                $"Document {documentId} is marked as having a file (sprk_hasfile=true) but the Graph Drive ID is empty. " +
                $"The upload may still be in progress or did not complete successfully.",
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

        // Validate itemId exists (same hasFile distinction as for DriveId)
        if (string.IsNullOrWhiteSpace(itemId))
        {
            if (!hasFile)
            {
                throw new SdapProblemException(
                    "no_file_attached",
                    "No File Attached",
                    $"Document {documentId} has no file attached yet (sprk_hasfile=false and sprk_graphitemid is empty). Upload a file before accessing it.",
                    409
                );
            }

            throw new SdapProblemException(
                "mapping_missing_item",
                "SPE Item ID Missing",
                $"Document {documentId} is marked as having a file (sprk_hasfile=true) but the Graph Item ID is empty. " +
                $"The upload may still be in progress or did not complete successfully.",
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
