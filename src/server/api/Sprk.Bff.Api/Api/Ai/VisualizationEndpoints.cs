using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Document visualization endpoints for relationship discovery.
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint filters), and ADR-013 (AI architecture).
/// </summary>
/// <remarks>
/// <para>
/// Provides endpoints for:
/// - Finding related documents using vector similarity search
/// - Visualizing document relationship graphs
/// </para>
///
/// <para>
/// <strong>Authorization:</strong>
/// Uses VisualizationAuthorizationFilter to verify read access to source document.
/// </para>
///
/// <para>
/// <strong>Rate Limiting:</strong>
/// Applies ai-batch policy for search operations.
/// </para>
/// </remarks>
public static class VisualizationEndpoints
{
    /// <summary>
    /// Maps visualization endpoints to the application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapVisualizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/visualization")
            .RequireAuthorization()
            .WithTags("AI Visualization");

        // GET /api/ai/visualization/related/{documentId} - Find related documents
        group.MapGet("/related/{documentId:guid}", GetRelatedDocuments)
            .AddVisualizationAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("VisualizationGetRelatedDocuments")
            .WithSummary("Find documents related to a source document")
            .WithDescription("Uses vector similarity search to find semantically related documents and returns a graph structure for visualization.")
            .Produces<DocumentGraphResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // POST /api/ai/visualization/related-from-content - Upload file for similarity comparison
        group.MapPost("/related-from-content", IndexTemporaryContent)
            .AddVisualizationContentAuthorizationFilter()
            .RequireRateLimiting("ai-upload")
            .DisableAntiforgery()
            .WithName("VisualizationIndexTemporaryContent")
            .WithSummary("Upload a file to find similar documents")
            .WithDescription("Accepts a PDF, DOCX, TXT, or MD file (max 50 MB), extracts text, generates embedding, "
                + "and indexes a temporary entry in AI Search. Returns a temporary documentId for use with the "
                + "GET /related/{documentId} endpoint.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ContentUploadResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(413)
            .ProducesProblem(422)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Find documents related to a source document using vector similarity.
    /// </summary>
    /// <param name="documentId">The source document ID (sprk_document GUID).</param>
    /// <param name="query">Query parameters for visualization options.</param>
    /// <param name="visualizationService">The visualization service.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph response with nodes, edges, and metadata.</returns>
    /// <remarks>
    /// <c>internal</c> so the task-059 tenant boundary is asserted against THIS handler rather than a
    /// copy of its resolution branch.
    /// </remarks>
    internal static async Task<IResult> GetRelatedDocuments(
        Guid documentId,
        [AsParameters] VisualizationQueryParameters query,
        HttpContext httpContext,
        IVisualizationService visualizationService,
        IAiAuthorizationService authorizationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // The filter's decision. Absent = the filter did not run: refuse rather than serve unfiltered.
        // This is the forcing function, copied from RecordSearchEndpoints — if someone detaches
        // AddVisualizationAuthorizationFilter, this route stops answering instead of quietly reverting to
        // the tenant-wide neighbour enumeration it used to be.
        if (RefuseIfNotRowAuthorized(httpContext, logger) is { } refusal)
        {
            return refusal;
        }

        // Validate required parameters
        if (documentId == Guid.Empty)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Document ID is required",
                Status = 400
            });
        }

        // Tenant comes from the caller's token, never from the request (task 059). Before that task
        // this handler read `?tenantId=` and nothing else — no claim was consulted at all — so any
        // authenticated user could read any tenant's document-relationship graph by editing the URL.
        // The DocumentRelationshipViewer PCF still sends the parameter; it is now simply ignored,
        // which is why the property was deleted from VisualizationQueryParameters rather than left
        // readable.
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.");
        }

        // `countOnly` is honoured in the RESPONSE, never in the QUERY. The service's count-only fast path
        // returns a bare `TotalResults` and no nodes — and a count of rows nobody authorized is the same
        // disclosure as the rows, one bit at a time: on a matter the caller is denied, it answers "how
        // many documents resemble this one in a matter you cannot open". There is nothing to trim if
        // nothing is materialised, so the fast path is not taken and the count is computed from the
        // PERMITTED rows. The saving it existed for is real but not ours to spend here; the latency cost
        // is recorded in notes/032-authorization-hardening.md.
        var countOnly = query.CountOnly ?? false;

        // Build visualization options from query parameters
        var options = new VisualizationOptions
        {
            TenantId = tenantId,
            Threshold = query.Threshold ?? 0.65f,
            Limit = Math.Clamp(query.Limit ?? 25, 1, 50),
            Depth = Math.Clamp(query.Depth ?? 1, 1, 3),
            IncludeKeywords = query.IncludeKeywords ?? true,
            DocumentTypes = query.DocumentTypes?.ToList(),
            IncludeParentEntity = query.IncludeParentEntity ?? true,
            RelationshipTypeFilter = query.RelationshipTypes?.ToList(),
            CountOnly = false
        };

        try
        {
            logger.LogInformation(
                "[VISUALIZATION] Getting related documents: DocumentId={DocumentId}, TenantId={TenantId}, Threshold={Threshold}, Limit={Limit}, Depth={Depth}",
                documentId, options.TenantId, options.Threshold, options.Limit, options.Depth);

            var response = await visualizationService.GetRelatedDocumentsAsync(
                documentId,
                options,
                cancellationToken);

            response = await AuthorizeRowsAsync(
                response, httpContext.User, httpContext, authorizationService, logger, cancellationToken);

            logger.LogInformation(
                "[VISUALIZATION] Found related documents: DocumentId={DocumentId}, NodeCount={NodeCount}, EdgeCount={EdgeCount}, Latency={Latency}ms",
                documentId, response.Nodes.Count, response.Edges.Count, response.Metadata.SearchLatencyMs);

            return Results.Ok(countOnly ? ToCountOnly(response) : response);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullVisualizationService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex,
                "[VISUALIZATION] Source document not found: DocumentId={DocumentId}",
                documentId);

            return Results.NotFound(new ProblemDetails
            {
                Title = "Document Not Found",
                Detail = $"Source document with ID {documentId} was not found or has no embedding",
                Status = 404
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[VISUALIZATION] Failed to get related documents: DocumentId={DocumentId}",
                documentId);

            return Results.Problem(
                title: "Visualization Failed",
                detail: "An error occurred while retrieving related documents",
                statusCode: 500);
        }
    }

    // =========================================================================
    // File upload constants
    // =========================================================================

    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".txt", ".md"
    };

    /// <summary>
    /// Upload a file and index it as temporary content for similarity comparison.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> so the task-059 tenant boundary is asserted against
    /// THIS handler rather than a re-implementation of its resolution branch — the same reason
    /// <see cref="ChatEndpoints.DeleteSessionAsync"/> is internal. A cross-tenant reachability test
    /// that exercises a copy of the code proves nothing about the code that ships.
    /// </remarks>
    internal static async Task<IResult> IndexTemporaryContent(
        HttpContext httpContext,
        IVisualizationService visualizationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // The same forcing function as GetRelatedDocuments, on a route that returns no rows of its own.
        //
        // That is deliberate, and it is the reason the check is here rather than only where rows are
        // served. This route is the ENTRY to the similarity surface: it indexes the caller's file into
        // the tenant's search partition and hands back a documentId whose only purpose is to be passed to
        // GET /related/{documentId}. If its filter is ever detached, the route that leaks is the other
        // one — and it would still look gated. Refusing here keeps the pair's obligation single: both
        // routes assert the same signal, so neither can be un-gated in isolation without going silent.
        if (RefuseIfNotRowAuthorized(httpContext, logger) is { } refusal)
        {
            return refusal;
        }

        // Task 059. The `?tenantId=` query parameter used to take precedence OVER the tid claim here,
        // so a caller authenticated in tenant A could index content into tenant B's search partition
        // just by naming it in the URL. It is gone: the tenant is the caller's, or the request fails.
        var effectiveTenantId = TenantResolution.ResolveTenantId(httpContext.User);

        if (string.IsNullOrWhiteSpace(effectiveTenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.");
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Request must be multipart/form-data with a 'file' field",
                Status = 400
            });
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "No file provided. Include a 'file' field in the multipart form data.",
                Status = 400
            });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            var sizeMb = file.Length / (1024.0 * 1024.0);
            return Results.Problem(
                statusCode: 413,
                title: "Request Entity Too Large",
                detail: $"File size ({sizeMb:F1} MB) exceeds the 50 MB limit");
        }

        var fileName = file.FileName ?? "document";
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedExtensions.Contains(extension))
        {
            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: $"File type '{extension}' is not supported. Allowed types: PDF, DOCX, TXT, MD.");
        }

        logger.LogInformation(
            "[VISUALIZATION] Processing file upload for similarity: FileName={FileName}, Size={SizeBytes}, TenantId={TenantId}",
            fileName, file.Length, effectiveTenantId);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await visualizationService.IndexTemporaryContentAsync(
                stream, fileName, effectiveTenantId, cancellationToken);

            if (!result.Success)
            {
                return Results.Problem(
                    statusCode: 422,
                    title: "Unprocessable Entity",
                    detail: result.ErrorMessage ?? "Failed to process uploaded file");
            }

            return Results.Ok(result);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullVisualizationService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VISUALIZATION] Failed to process file upload: FileName={FileName}", fileName);

            return Results.Problem(
                title: "Processing Failed",
                detail: "An error occurred while processing the uploaded file for similarity search",
                statusCode: 500);
        }
    }

    // =========================================================================
    // Per-row authorization (word-add-in-r1 task 032 — NFR-02, finding F-b)
    // =========================================================================

    /// <summary>
    /// Returns a 500 result when the per-row-authorization obligation is absent from
    /// <see cref="HttpContext.Items"/>, and <c>null</c> when the route may proceed.
    /// </summary>
    /// <remarks>
    /// Copied in shape from <c>RecordSearchEndpoints</c>'s forcing function. Refusing outright is the
    /// point: the alternative that was considered — log a warning and serve — restores the exact defect,
    /// because the log is read after the rows have already been sent.
    /// </remarks>
    private static IResult? RefuseIfNotRowAuthorized(HttpContext httpContext, ILogger logger)
    {
        if (httpContext.Items[VisualizationAuthorization.HttpContextItemsKey]
                is VisualizationAuthorization authorization
            && authorization.RequiresPerRowDocumentAuthorization)
        {
            return null;
        }

        logger.LogError(
            "[VISUALIZATION] A visualization route reached its handler with no authorization decision — "
            + "the visualization authorization filter is not applied to this route. Refusing.");

        return Results.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "Authorization context not available.");
    }

    /// <summary>
    /// Upper bound on document-access checks spent assembling one graph.
    /// </summary>
    /// <remarks>
    /// Set to <c>MaxTotalNodes</c> (100) — the ceiling the visualization service itself works to —
    /// deliberately, so a full graph is always fully evaluated and the budget never truncates a
    /// legitimate result. A row past the budget is DROPPED, never served unevaluated: an unchecked row
    /// is exactly what this task exists to stop, and "we ran out of budget" is not a permission.
    /// The per-request memo below plus <c>CachedAccessDataSource</c>'s 60 s key absorb the repeats.
    /// </remarks>
    private const int MaxDocumentAuthorizationChecks = 100;

    /// <summary>
    /// Authorizes every RESULT row in the graph against its own <c>sprk_document</c> record and drops
    /// what the caller cannot read, along with the edges and hubs that referenced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row is the subject.</b> Cross-record document search authorizes a row's PARENT because its
    /// rows carry <c>ParentEntityType</c>/<c>ParentEntityId</c>. This surface's rows do not — the
    /// visualization service hard-codes both to <c>null</c> (GitHub #233) — so the parent hop is not
    /// available here and would be a weaker check even if it were: the document IS a Dataverse row, and
    /// <c>IAiAuthorizationService</c> already answers "may this caller Read this document" as the caller,
    /// through the same <c>IAccessDataSource</c>. Reusing it rather than building a second path is what
    /// CLAUDE.md §11 asks for, and it is the service the source-document filter on this very route
    /// already consults.
    /// </para>
    /// <para>
    /// <b>What is NOT a result row.</b> Two node classes are kept without a check, and both are the
    /// source's own information rather than a neighbour's: the source node itself (depth 0), authorized
    /// by the filter before the handler ran; and the parent HUB nodes, which
    /// <c>CreateParentHubNode</c> builds exclusively from the SOURCE document's own matter/project/
    /// invoice/email lookups. A hub discloses nothing about a document the caller cannot see — but its
    /// mere EXISTENCE would, since a hub is only created when at least one neighbour of that type was
    /// found. So a hub whose every document was withheld is dropped with them; see below.
    /// </para>
    /// <para>
    /// <b>Fail closed per row (ADR-003).</b> A row is kept only if its node id parses as a non-empty GUID
    /// AND Dataverse says the caller holds Read. Orphan-file nodes — indexed SPE files with no Dataverse
    /// record, whose node id is a file id rather than a document GUID — therefore never survive: there is
    /// no record against which to evaluate access, and "no answer" must not resolve to "serve it".
    /// </para>
    /// <para>
    /// <b>Batched, not concurrent.</b> The distinct ids go to <c>IAiAuthorizationService</c> in ONE call,
    /// which walks them sequentially. That sequencing is not an oversight: <c>DataverseAccessDataSource</c>
    /// assigns <c>_httpClient.DefaultRequestHeaders.Authorization</c> before each request and the client
    /// is shared within a request scope, so concurrent checks would mutate the header mid-send —
    /// <c>HttpHeaders</c> is not thread-safe and the failure presents as an intermittent phantom denial.
    /// </para>
    /// <para>
    /// <b>ADR-044.</b> Ids reach this method as the bare-lowercase form <c>VisualizationDocument
    /// .GetUniqueId()</c> produces; <c>Guid.TryParse</c> canonicalizes any other spelling and rejects what
    /// is not a GUID. Nothing here interpolates an id into an OData predicate — the access lookup takes a
    /// typed <see cref="Guid"/>.
    /// </para>
    /// </remarks>
    private static async Task<DocumentGraphResponse> AuthorizeRowsAsync(
        DocumentGraphResponse response,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IAiAuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (response.Nodes.Count == 0)
        {
            return response;
        }

        var resultRows = response.Nodes.Where(IsResultRow).ToList();
        if (resultRows.Count == 0)
        {
            // Nothing to trim — but the count is still normalised rather than passed through. Today the
            // service's own empty response already reports 0, so this changes no observable behaviour;
            // it is here because "no rows" and "a count of rows" are produced by different code paths,
            // and a future divergence between them would be a count with no rows to check it against.
            return response with { Metadata = response.Metadata with { TotalResults = 0 } };
        }

        // Distinct document ids, in the order the graph presents them, bounded by the check budget.
        var toCheck = new List<Guid>();
        var seen = new HashSet<Guid>();
        var unresolvable = 0;
        var budgetExhausted = false;

        foreach (var node in resultRows)
        {
            if (!Guid.TryParse(node.Id, out var rowDocumentId) || rowDocumentId == Guid.Empty)
            {
                unresolvable++;
                continue;
            }

            if (!seen.Add(rowDocumentId))
            {
                continue;
            }

            if (toCheck.Count >= MaxDocumentAuthorizationChecks)
            {
                budgetExhausted = true;
                break;
            }

            toCheck.Add(rowDocumentId);
        }

        var permitted = new HashSet<Guid>();
        if (toCheck.Count > 0)
        {
            var decision = await authorizationService.AuthorizeAsync(
                user, toCheck, httpContext, cancellationToken);

            // Success / Partial / Denied all carry the permitted SUBSET here; the flag distinguishes
            // "everything you asked for" from "some of it", and neither is a reason to serve more.
            foreach (var authorizedId in decision.AuthorizedDocumentIds)
            {
                permitted.Add(authorizedId);
            }
        }

        var keptNodes = response.Nodes
            .Where(n => !IsResultRow(n)
                        || (Guid.TryParse(n.Id, out var id) && permitted.Contains(id)))
            .ToList();

        var droppedRows = resultRows.Count - keptNodes.Count(IsResultRow);

        // A hub that no longer connects a single surviving document is dropped: it exists only because a
        // neighbour of that relationship type was found, so leaving it behind would announce the count it
        // no longer shows. Evaluated against the SURVIVING document nodes, not the original ones.
        var survivingRowIds = keptNodes
            .Where(IsResultRow)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hubsWithSurvivors = response.Edges
            .Where(e => survivingRowIds.Contains(e.Source) || survivingRowIds.Contains(e.Target))
            .SelectMany(e => new[] { e.Source, e.Target })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        keptNodes = keptNodes
            .Where(n => !NodeTypes.IsParentHub(n.Type) || hubsWithSurvivors.Contains(n.Id))
            .ToList();

        var keptNodeIds = keptNodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keptEdges = response.Edges
            .Where(e => keptNodeIds.Contains(e.Source) && keptNodeIds.Contains(e.Target))
            .ToList();

        var permittedRowCount = keptNodes.Count(IsResultRow);
        var hubCount = keptNodes.Count(n => NodeTypes.IsParentHub(n.Type));

        if (droppedRows > 0 || unresolvable > 0 || budgetExhausted)
        {
            logger.LogInformation(
                "[VISUALIZATION] Row authorization kept {Permitted} of {Total} result rows "
                + "({Checks} distinct documents evaluated, {Unresolvable} rows had no authorizable "
                + "document id, budgetExhausted={BudgetExhausted})",
                permittedRowCount, resultRows.Count, toCheck.Count, unresolvable, budgetExhausted);
        }

        return response with
        {
            Nodes = keptNodes,
            Edges = keptEdges,
            Metadata = response.Metadata with
            {
                // The count of rows in THIS response. Leaving the pre-trim total would report how many
                // related documents exist beyond what was returned — a smaller leak than the documents
                // themselves, but the same kind: on this surface the count alone reveals how many
                // documents a matter the caller cannot open holds.
                TotalResults = permittedRowCount,

                // Mirrors BuildGraphResponseWithHubTopology's own arithmetic, recomputed rather than
                // carried, for the same reason as TotalResults: the shape of the graph is a count too.
                NodesPerLevel = permittedRowCount > 0 || hubCount > 0
                    ? new List<int> { 1, hubCount, permittedRowCount }
                    : new List<int> { 1 },
                MaxDepthReached = hubCount > 0 ? 2 : (permittedRowCount > 0 ? 1 : 0)
            }
        };
    }

    /// <summary>
    /// True for nodes that are a RESULT — a neighbour document surfaced by the search — as opposed to the
    /// source the filter already authorized or a hub built from the source's own lookups.
    /// </summary>
    private static bool IsResultRow(DocumentNode node) =>
        node.Type != NodeTypes.Source && !NodeTypes.IsParentHub(node.Type);

    /// <summary>
    /// Projects an authorized graph down to the count-only response shape, AFTER trimming.
    /// </summary>
    /// <remarks>
    /// The count-only contract is "metadata with a total and no graph", and this preserves it exactly —
    /// what it does not preserve is the service's count-only SHORTCUT, which would have produced the
    /// total from unauthorized rows. See the note at the <c>countOnly</c> capture in the handler.
    /// </remarks>
    private static DocumentGraphResponse ToCountOnly(DocumentGraphResponse response) =>
        response with
        {
            Nodes = [],
            Edges = [],
            Metadata = response.Metadata with
            {
                NodesPerLevel = [],
                MaxDepthReached = response.Metadata.TotalResults > 0 ? 1 : 0
            }
        };
}

/// <summary>
/// Query parameters for visualization endpoints.
/// Mapped from URL query string parameters.
/// </summary>
public class VisualizationQueryParameters
{
    // A `tenantId` query parameter was REMOVED here by task 059. It was the sole tenant source for
    // GetRelatedDocuments and outranked the tid claim on IndexTemporaryContent, which made every
    // visualization route a cross-tenant read/write for any authenticated caller. The property is
    // deleted rather than left bound-but-ignored so that reading it again is a COMPILE error, not a
    // silently reintroduced boundary hole. Callers may still send the parameter — model binding
    // ignores unknown query keys, so the DocumentRelationshipViewer PCF needs no redeploy.

    /// <summary>
    /// Minimum similarity score threshold (0.0-1.0).
    /// Default: 0.65
    /// </summary>
    [FromQuery(Name = "threshold")]
    public float? Threshold { get; init; }

    /// <summary>
    /// Maximum number of related documents per level.
    /// Default: 25, Max: 50
    /// </summary>
    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    /// <summary>
    /// Relationship depth (1-3 levels).
    /// Default: 1
    /// </summary>
    [FromQuery(Name = "depth")]
    public int? Depth { get; init; }

    /// <summary>
    /// Whether to include shared keywords in edge data.
    /// Default: true
    /// </summary>
    [FromQuery(Name = "includeKeywords")]
    public bool? IncludeKeywords { get; init; }

    /// <summary>
    /// Optional filter by document types.
    /// </summary>
    [FromQuery(Name = "documentTypes")]
    public string[]? DocumentTypes { get; init; }

    /// <summary>
    /// Whether to include parent entity (Matter/Project) information.
    /// Default: true
    /// </summary>
    [FromQuery(Name = "includeParentEntity")]
    public bool? IncludeParentEntity { get; init; }

    /// <summary>
    /// Filter by relationship types.
    /// Valid values: same_email, same_thread, same_matter, same_project, same_invoice, semantic
    /// Default: all types (no filter)
    /// </summary>
    [FromQuery(Name = "relationshipTypes")]
    public string[]? RelationshipTypes { get; init; }

    /// <summary>
    /// When true, returns only metadata with totalResults count.
    /// Skips graph node/edge computation for fast count queries.
    /// Default: false
    /// </summary>
    [FromQuery(Name = "countOnly")]
    public bool? CountOnly { get; init; }
}
