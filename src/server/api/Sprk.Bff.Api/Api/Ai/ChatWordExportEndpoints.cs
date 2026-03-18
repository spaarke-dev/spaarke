using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Export;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Endpoint for exporting chat analysis content to Word format (FR-15: Open in Word).
///
/// Pipeline: markdown content → DOCX generation (OpenXML SDK via DocxExportService)
///           → SPE upload (via SpeFileStore facade) → Word Online URL construction.
///
/// ADR-001: Minimal API pattern.
/// ADR-007: SpeFileStore facade for all SPE operations.
/// ADR-008: Endpoint filter for authorization.
/// ADR-013: ChatHostContext flows through pipeline for container resolution.
/// </summary>
public static class ChatWordExportEndpoints
{
    /// <summary>
    /// Registers the Word export endpoint under the /api/ai/chat route group.
    /// Called from EndpointMappingExtensions.MapDomainEndpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapChatWordExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat/export")
            .RequireAuthorization()
            .WithTags("AI Chat Export");

        group.MapPost("/word", ExportToWordAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-export")
            .WithName("ChatExportToWord")
            .WithSummary("Export chat content to Word — generates DOCX, uploads to SPE, returns Word Online URL")
            .WithDescription(
                "Converts markdown content from a chat analysis to a DOCX file using OpenXML SDK, " +
                "uploads it to the entity's SPE container, and returns a Word Online URL for direct editing. " +
                "Requires a valid chat session for authorization and container resolution.")
            .Produces<WordExportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// POST /api/ai/chat/export/word
    ///
    /// 5-step pipeline:
    ///   1. Validate request (content non-empty, filename ends in .docx)
    ///   2. Resolve ChatHostContext from session → get SPE container ID
    ///   3. Generate DOCX via DocxExportService.GenerateFromMarkdownAsync
    ///   4. Upload DOCX to SPE container via SpeFileStore.UploadSmallAsUserAsync
    ///   5. Construct Word Online URL from uploaded file's WebUrl
    /// </summary>
    private static async Task<IResult> ExportToWordAsync(
        WordExportRequest request,
        ChatSessionManager sessionManager,
        DocxExportService docxExportService,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<DocxExportService> logger,
        CancellationToken cancellationToken)
    {
        // ─── Step 1: Validate request ───────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: "Content must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Filename) ||
            !request.Filename.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: "Filename must end with '.docx'.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "SessionId is required.");
        }

        // ─── Step 2: Resolve session and ChatHostContext ────────────────────
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, request.SessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Chat session '{request.SessionId}' not found.");
        }

        // Resolve the SPE container for file upload.
        // Priority: session's HostContext EntityId → configuration fallback.
        var containerId = ResolveContainerId(session, configuration);
        if (string.IsNullOrEmpty(containerId))
        {
            logger.LogWarning(
                "Word export failed: no SPE container resolvable for session {SessionId} (entity={EntityType}/{EntityId})",
                request.SessionId, session.HostContext?.EntityType, session.HostContext?.EntityId);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: "Cannot resolve SPE container for file upload. Ensure the chat session has a valid host context.");
        }

        logger.LogInformation(
            "Word export: session={SessionId}, container={ContainerId}, filename={Filename}",
            request.SessionId, containerId, request.Filename);

        try
        {
            // ─── Step 3: Generate DOCX from markdown ────────────────────────
            var docxBytes = await docxExportService.GenerateFromMarkdownAsync(
                request.Content,
                Path.GetFileNameWithoutExtension(request.Filename),
                request.Metadata,
                cancellationToken);

            // ─── Step 4: Upload to SPE container ────────────────────────────
            // Resolve container to drive ID (containers are GUIDs, drives start with "b!")
            var driveId = await speFileStore.ResolveDriveIdAsync(containerId, cancellationToken);

            // Upload using user's OBO context for proper authorization
            using var docxStream = new MemoryStream(docxBytes);
            var uploadPath = $"exports/{request.Filename}";

            var uploadResult = await speFileStore.UploadSmallAsUserAsync(
                httpContext,
                driveId,
                uploadPath,
                docxStream,
                cancellationToken);

            if (uploadResult is null)
            {
                logger.LogError(
                    "Word export upload failed: SPE returned null for container={ContainerId}, path={Path}",
                    containerId, uploadPath);

                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Failed to upload DOCX file to SharePoint Embedded storage.");
            }

            // ─── Step 5: Construct Word Online URL ──────────────────────────
            // The uploaded file's WebUrl from Graph API points to the SharePoint location.
            // Word Online URL: append ?action=default to open in edit mode,
            // or use the /_layouts/15/Doc.aspx?sourcedoc={itemId} pattern.
            var wordOnlineUrl = ConstructWordOnlineUrl(uploadResult.WebUrl, uploadResult.Id);

            logger.LogInformation(
                "Word export complete: session={SessionId}, fileId={FileId}, size={Size} bytes",
                request.SessionId, uploadResult.Id, docxBytes.Length);

            var response = new WordExportResponse(
                SpeFileId: uploadResult.Id,
                Filename: request.Filename,
                WordOnlineUrl: wordOnlineUrl,
                SizeBytes: docxBytes.Length,
                GeneratedAt: DateTimeOffset.UtcNow);

            return Results.Created($"/api/ai/chat/export/word/{uploadResult.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Word export failed: container resolution error for session={SessionId}",
                request.SessionId);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Word export failed: unexpected error for session={SessionId}",
                request.SessionId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An error occurred during Word export.");
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Resolves the SPE container ID for file upload.
    /// Uses the staging container from configuration as the export destination.
    /// </summary>
    private static string? ResolveContainerId(ChatSession session, IConfiguration configuration)
    {
        // Use the staging container for exports (consistent with MatterPreFillService pattern).
        // The staging container is accessible to all authenticated users via OBO.
        var stagingContainerId = configuration["SharePointEmbedded:StagingContainerId"];
        if (!string.IsNullOrEmpty(stagingContainerId))
        {
            return stagingContainerId;
        }

        // Fallback: default container from email processing config
        var defaultContainerId = configuration["EmailProcessing:DefaultContainerId"];
        return defaultContainerId;
    }

    /// <summary>
    /// Constructs a Word Online URL from the uploaded file's WebUrl or item ID.
    /// Word Online opens .docx files for editing directly in the browser.
    /// </summary>
    private static string ConstructWordOnlineUrl(string? webUrl, string fileId)
    {
        // If we have the WebUrl from Graph, it's the most reliable path to Word Online.
        // SharePoint Embedded files return a WebUrl like:
        //   https://{tenant}.sharepoint.com/contentstorage/{containerId}/Document/{filename}
        // Appending ?web=1 forces it to open in Word Online instead of downloading.
        if (!string.IsNullOrEmpty(webUrl))
        {
            // Ensure the URL opens in Word Online (edit mode)
            var separator = webUrl.Contains('?') ? "&" : "?";
            return $"{webUrl}{separator}web=1";
        }

        // Fallback: return the file ID for the client to construct the URL
        // (the client may know the SharePoint hostname from its environment)
        return $"spe://file/{fileId}";
    }

    /// <summary>
    /// Extracts the tenant ID from the JWT 'tid' claim (ADR-014) with X-Tenant-Id header fallback.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        var tenantId = httpContext.User.FindFirst("tid")?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        return tenantId;
    }
}
