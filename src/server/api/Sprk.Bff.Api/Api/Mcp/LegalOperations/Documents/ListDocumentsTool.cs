using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 023 (spec FR-03, NFR-02, NFR-05, NFR-07; design §3).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IDocumentDataverseService, the service the document REST routes use, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/v1/documents (DataverseDocumentsEndpoints.ListDocumentsAsync, keyed by container) and
//     IDocumentDataverseService.GetDocumentsByMatterAsync (a matter's documents by their sprk_matter lookup).
//   Extension: this IS the extension: it calls that service and the same two decisions the list route makes (Read on the owning record
//     as the caller, then the external document narrowing, once over the whole list). The REST route is keyed by container, has no
//     caller in the repository, and cannot serve the attachment picker (task 011 asks by matter); notes/023-decisions.md D1.
//   Cost of doing nothing: Copilot users cannot list a matter's documents, and the email widget (011, 040) has no attachment source.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_list_documents</c> (FR-03): the Spaarke documents filed under one matter, as the signed-in caller may see them. Read-only,
/// visible to the model and to the email widget's attachment picker (task 011, <c>notes/011-decisions.md</c> D4: arguments
/// <c>regardingEntityType: 'sprk_matter'</c> and <c>regardingId</c>; result <c>structuredContent.documents[{ id, name, fileType?, sizeBytes? }]</c>).
/// </summary>
/// <remarks>
/// <para><b>Authorization is the list route's, asked for the matter.</b> <c>GET /api/v1/documents?containerId=</c> is gated by Read on the
/// record that owns the container (as the caller) and then trimmed by the external document narrowing, once over the list, before paging
/// (<c>ContainerDocumentAuthorizationFilter</c>, <c>NarrowContainerListAsync</c>). This tool asks the same two questions of the matter it is
/// given: <see cref="RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync"/> (the one fixed-entity-set decision the route filters and
/// the events capability also use) for Read on the matter, then <see cref="InternalPlaneDocumentNarrowing.ForCallerAsync"/> over the rows.
/// A matter the caller cannot read and a matter that does not exist give the same not-found (<see cref="DocumentToolSupport.NotFound"/>).
/// Per-document Read is not asked per row (50 sequential caller-rights reads would pass the NFR-01 read budget of 3 s); it is asked when a
/// document is opened (<see cref="GetDocumentTool"/>) and when it is attached to a send (task 040's filter).</para>
/// <para><b>Caller (ADR-028 A10).</b> The request context is <c>IHttpContextAccessor.HttpContext</c>, passed through unchanged; a null
/// context or an unauthenticated caller is <c>UserContextRequired</c> before anything is read, and nothing falls back to app-only.</para>
/// <para><b>Result.</b> <c>structuredContent</c> is <c>{ regarding, documents, count, hasMore }</c>, newest first. <c>hasMore</c> is true when
/// the service returned its row limit, so the matter may hold more documents than listed. <c>content</c> is one line per document. No
/// document content, storage pointer or summary is returned (ADR-015), and a name is clipped so 50 rows stay under 64 KiB.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class ListDocumentsTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_list_documents";

    internal const string RegardingEntityTypeArgument = "regardingEntityType";
    internal const string RegardingIdArgument = "regardingId";

    /// <summary>The one regarding record type v1 lists documents for (task 011 D4).</summary>
    internal const string MatterEntityType = "sprk_matter";

    /// <summary>
    /// How many rows <c>IDocumentDataverseService.GetDocumentsByMatterAsync</c> returns at most (its query's TopCount): a result of exactly
    /// this many may be cut. Keep it equal to <c>DataverseServiceClientImpl.GetDocumentsByLookupAsync</c>'s TopCount.
    /// </summary>
    internal const int ServiceRowLimit = 50;

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [RegardingEntityTypeArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(JsonValue.Create(MatterEntityType)),
                ["description"] = "The kind of record the documents are filed under. Only 'sprk_matter' (a matter) is supported.",
            },
            [RegardingIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the matter.",
            },
        },
        ["required"] = new JsonArray(RegardingEntityTypeArgument, RegardingIdArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ListAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "List a matter's documents",
                Description =
                    "Use for: listing the Spaarke documents filed under one matter, newest first: each document's name, file type, size and "
                    + "modified date, and its id to open it with spaarke_get_document or attach it to an email. Needs the matter's id. Returns up "
                    + "to 50 documents; hasMore says when there may be more. Only documents the signed-in user may see are listed. "
                    + "Not for: finding documents by what they say or by name (use the search tools); the contents of a document; documents "
                    + "filed under a project or other record; changing a document. A matter that does not exist and one the user cannot read "
                    + "return the same not-found answer.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = documents listed).</summary>
    internal static Task<CallToolResult> ListAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IDocumentDataverseService documents,
        CallerRecordAccessProbe probe,
        TimeProvider time,
        ILogger<ListDocumentsTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, documents, probe, logger, cancellationToken),
            RowsOf);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IDocumentDataverseService documents,
        CallerRecordAccessProbe probe,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. Checked before anything else, with the test every decision tool applies.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(
                arguments, ToolName, [RegardingEntityTypeArgument, RegardingIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!string.Equals(values[0], MatterEntityType, StringComparison.OrdinalIgnoreCase))
        {
            return DocumentToolSupport.InvalidArgument(
                $"'{RegardingEntityTypeArgument}' must be '{MatterEntityType}': documents are listed for a matter. Send the call again with that value.", http);
        }

        if (!Guid.TryParse(values[1], out var matterId) || matterId == Guid.Empty)
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{RegardingIdArgument}' must be the matter's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        // 1. Read on the matter, as the caller (the list route's owning-record gate). Not allowed, not found and a fault are one answer.
        EntityAccessFilter.TryResolveEntitySet(MatterEntityType, out var matterEntitySet);
        var decision = await RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync(
            http!, probe, "read", matterEntitySet, matterId, logger, cancellationToken);
        if (decision.Access != RecordRouteAccessAuthorizationFilter.RouteRecordAccess.Allowed)
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http);
        }

        // 2. The matter's documents, then the external document narrowing once over the whole list (the list route's second gate).
        // A fault in either read is "could not answer" (503), never an empty list and never an escaped exception; only the caller's own
        // cancellation propagates (a timeout that is not theirs, such as an HttpClient's TaskCanceledException, is a fault).
        List<DocumentEntity> rows;
        List<DocumentRow> listed;
        try
        {
            rows = (await documents.GetDocumentsByMatterAsync(matterId, null, cancellationToken)).ToList();
            var visibility = await InternalPlaneDocumentNarrowing.ForCallerAsync(http!, rows.Select(d => d.Id), "mcp-document-list", logger);
            listed = (visibility.Narrowed ? rows.Where(d => visibility.IsShown(d.Id)) : rows)
                .OrderByDescending(d => d.ModifiedOn)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .Select(DocumentToolSupport.RowOf)
                .ToList();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return DocumentToolSupport.Fault(ex, ToolName, "the document list read", http, logger);
        }

        var body = new ListBody(
            new RegardingRecord(MatterEntityType, matterId.ToString("D")),
            listed,
            listed.Count,
            rows.Count >= ServiceRowLimit);

        // One neutral line for every empty list. "No documents are filed" would be false when rows came back and were all narrowed away
        // (or hidden by a narrowing that failed closed), and a different line for that case would tell an external caller that hidden
        // documents exist, which the narrowing exists to hide (023 re-check K3).
        var summary = listed.Count == 0
            ? "No documents to show under this matter."
            : $"{listed.Count} document(s) filed under this matter, newest first"
              + (body.HasMore ? "; there may be more than this." : ".")
              + "\n" + string.Join("\n", listed.Select(r => "- " + DocumentToolSupport.LineOf(r)));
        return DecisionToolCall.Success(summary, body);
    }

    /// <summary>The row count of a result for the telemetry line (a count only, never the rows).</summary>
    private static int RowsOf(CallToolResult result) =>
        result.IsError != true && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("count", out var count) && count.TryGetInt32(out var n)
            ? n
            : 0;

    /// <summary><c>structuredContent</c> of <c>spaarke_list_documents</c> (the shape task 011's host reads: <c>documents[{ id, name, sizeBytes?, fileType? }]</c>).</summary>
    internal sealed record ListBody(RegardingRecord Regarding, IReadOnlyList<DocumentRow> Documents, int Count, bool HasMore);
}
