using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Signals;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 023 (spec FR-03, NFR-02, NFR-05, NFR-07; design §3).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IDocumentDataverseService and the file-link code of FileAccessEndpoints, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/v1/documents/{id}, GET /api/documents/{id}/preview-url and /open-links (their link steps are now
//     FileAccessEndpoints.MintPreviewUrlAsync and ResolveOpenLinksAsync, extracted unchanged under a frozen-bytes test).
//   Extension: this IS the extension: it makes the routes' per-document decision (DocumentAuthorizationFilter's, through
//     InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync, the one document-resource decision outside the filter), reads the row the
//     GET route reads, and calls the two extracted methods for the links.
//   Cost of doing nothing: Copilot users cannot open a matter document by id (FR-03), and the email widget cannot show an attachment.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_document</c> (FR-03): one Spaarke document's slim metadata and its preview and open links, for a caller who may read it.
/// Read-only; visible to the model and the widget.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The REST routes put <c>DocumentAuthorizationFilter("read")</c> in front of the row and the link
/// routes: the caller's own Read on the document, then the external document narrowing. The tool asks exactly that through
/// <see cref="InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync"/> (the ArchTest <c>EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper</c>
/// keeps every other site on it) and copies no check. A document the caller may not read, one the narrowing hides and one that does not
/// exist give the same <c>isError</c> not-found (<see cref="DocumentToolSupport.NotFound"/>): the REST routes already answer the first
/// and third alike (403, because Dataverse grants no rights on a row that does not exist), so a uniform result contradicts no contract.</para>
/// <para><b>Metadata</b> is the row <c>GET /api/v1/documents/{id}</c> returns (<c>IDocumentDataverseService.GetDocumentAsync</c>), reduced
/// to what the model may see (ADR-015, NFR-07): id, name, file type, size, modified date and the matter or project it is filed under. No
/// storage pointer, summary, description or content.</para>
/// <para><b>Links</b> come from <see cref="FileAccessEndpoints.MintPreviewUrlAsync"/> and <see cref="FileAccessEndpoints.ResolveOpenLinksAsync"/>,
/// the link steps of the two routes, so the tool and the routes cannot disagree. The tool does not do the open-links route's last step
/// (a just-in-time writer role on a secure container): it is read-only. When a link cannot be made (no file attached, the pointer's
/// container is refused, Graph fails) the metadata is still returned with <c>linksError</c> (<c>{ reasonCode, message }</c>), never a
/// whole-call failure.</para>
/// <para><b>Caller (ADR-028 A10).</b> The request context is <c>IHttpContextAccessor.HttpContext</c>; a null context or an unauthenticated
/// caller is <c>UserContextRequired</c> before anything is read, and nothing falls back to app-only. The bearer token is handed only to the
/// authorization service, as the REST filter does, and is never logged.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetDocumentTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_document";

    internal const string DocumentIdArgument = "documentId";

    /// <summary>The <c>reasonCode</c> of links that could not be made for a reason that is not a REST problem (a Graph or storage fault).</summary>
    internal const string LinksUnavailableReasonCode = "mcp.document_links_unavailable";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [DocumentIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the document, from spaarke_list_documents or a search result.",
            },
        },
        ["required"] = new JsonArray(DocumentIdArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Get a document",
                Description =
                    "Use for: opening one Spaarke document the signed-in user may read, by its id: its name, file type, size, last modified "
                    + "date, the matter or project it is filed under, a preview link and links to open it on the desktop (Word, Excel or "
                    + "PowerPoint) or in the browser. Not for: reading or summarising what the document says (use the search or analysis "
                    + "tools); listing a matter's documents (use spaarke_list_documents); downloading, editing or sharing a document. A "
                    + "document that does not exist and one the user may not read return the same not-found answer.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a document was returned).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IDocumentDataverseService documents,
        Spaarke.Core.Auth.IAuthorizationService authorization,
        SpeFileStore speFileStore,
        RecordContainerResolver containerResolver,
        TimeProvider time,
        ILogger<GetDocumentTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, documents, authorization, speFileStore, containerResolver, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IDocumentDataverseService documents,
        Spaarke.Core.Auth.IAuthorizationService authorization,
        SpeFileStore speFileStore,
        RecordContainerResolver containerResolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. Checked before anything else, with the test every decision tool applies.
        var userId = http is null ? null : CallerResolution.ResolveObjectId(http.User);
        if (!PlanAndLookupToolSupport.HasCaller(http) || string.IsNullOrEmpty(userId))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [DocumentIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!Guid.TryParse(values[0], out var documentGuid) || documentGuid == Guid.Empty)
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{DocumentIdArgument}' must be the document's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        // 1. The routes' per-document decision (Read as the caller, then the external narrowing). Denied, hidden and missing are one answer.
        var decision = await InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync(
            http!, authorization, userId, [documentGuid], "read", TokenHelper.ExtractBearerTokenOrNull(http!), "mcp-get-document", logger, cancellationToken);
        if (!decision.IsAllowed)
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http);
        }

        // 2. The row GET /api/v1/documents/{id} reads. A row gone between the decision and here is the same not-found.
        var documentId = documentGuid.ToString("D");
        var document = await documents.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http);
        }

        // 3. The links, from the routes' own link steps. A failure here costs the links, not the metadata.
        string? previewUrl = null;
        OpenLinksResponse? open = null;
        LinksError? linksError = null;
        try
        {
            previewUrl = await FileAccessEndpoints.MintPreviewUrlAsync(
                document, documentGuid, documentId, speFileStore, containerResolver, logger, http!.TraceIdentifier, cancellationToken);
            open = await FileAccessEndpoints.ResolveOpenLinksAsync(
                document, documentGuid, documentId, speFileStore, containerResolver, logger, http.TraceIdentifier, cancellationToken);
        }
        catch (SdapProblemException ex)
        {
            // The route's own problem (no file attached, pointer container refused, no preview): its code and title, never its detail.
            previewUrl = null;
            open = null;
            linksError = new LinksError(ex.Code, ex.Title);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ADR-015: the exception type only, never its message (a Graph fault message can carry a drive or item id).
            logger.LogWarning("MCP tool {ToolName}: the links of a document could not be made (a storage or Graph fault: {ExceptionType}).", ToolName, ex.GetType().Name);
            previewUrl = null;
            open = null;
            linksError = new LinksError(LinksUnavailableReasonCode, "The preview and open links could not be made right now. Try again.");
        }

        var row = DocumentToolSupport.RowOf(document);
        var body = new GetBody(
            row,
            RegardingOf("sprk_matter", document.MatterId, document.MatterName),
            RegardingOf("sprk_project", document.ProjectId, document.ProjectName),
            linksError is null ? new DocumentLinks(previewUrl, open?.DesktopUrl, open?.WebUrl) : null,
            linksError);

        var text = DocumentToolSupport.LineOf(row)
                   + (body.RegardingMatter?.Name is { } matter ? $"\nFiled under matter: {matter}" : string.Empty)
                   + (body.RegardingProject?.Name is { } project ? $"\nFiled under project: {project}" : string.Empty)
                   + (body.Links is { } links
                       ? $"\nPreview: {links.PreviewUrl}\nOpen in the browser: {links.WebUrl}"
                         + (links.DesktopUrl is null ? string.Empty : $"\nOpen on the desktop: {links.DesktopUrl}")
                       : $"\nLinks are not available: {linksError!.Message}");
        return DecisionToolCall.Success(text, body);
    }

    private static RegardingRecord? RegardingOf(string entityType, string? id, string? name) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : new RegardingRecord(entityType, id, WorklistItemSummary.Clip(name, DocumentToolSupport.NameBytes));

    /// <summary><c>structuredContent</c> of <c>spaarke_get_document</c>: <see cref="Links"/> or <see cref="LinksError"/> is set, not both.</summary>
    internal sealed record GetBody(
        DocumentRow Document, RegardingRecord? RegardingMatter, RegardingRecord? RegardingProject, DocumentLinks? Links, LinksError? LinksError);

    /// <summary>The links the routes return: the embeddable preview URL (preview-url), and the desktop-app and browser URLs (open-links).</summary>
    internal sealed record DocumentLinks(string? PreviewUrl, string? DesktopUrl, string? WebUrl);

    /// <summary>Why the links are missing: <c>{ reasonCode, message }</c>, the REST problem's code and title or this host's own.</summary>
    internal sealed record LinksError(string ReasonCode, string Message);
}
