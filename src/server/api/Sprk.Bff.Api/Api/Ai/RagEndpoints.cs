using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// RAG (Retrieval-Augmented Generation) endpoints for knowledge base operations.
/// Follows ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// Provides endpoints for:
/// - Hybrid search (keyword + vector + semantic ranking)
/// - Document indexing
/// - Document deletion
///
/// Multi-tenant support via tenantId in request/options.
/// </remarks>
public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/rag")
            .RequireAuthorization()
            .WithTags("AI RAG");

        // ─── Authorization on this group (unified-access-control-r2 task 163, sweep findings #4, #5,
        //     #6, #30; takes over GitHub #1041) ─────────────────────────────────────────────────────
        //
        // The group's RequireAuthorization() means only "signed in", and AddTenantAuthorizationFilter()
        // only compares a tenant the request names with the token's `tid` (it passes through when the
        // request names none). Neither decides anything about a RECORD. So every route below states its
        // own decision:
        //   - /search            per-row trim in the handler (IAiAuthorizationService, as the caller),
        //                        plus a Read gate on Options.ParentEntityId when one is named;
        //   - /index, /{id}      the SystemAdmin policy — they take a caller-chosen index KEY / chunk key,
        //                        which no per-record check can scope (operator maintenance surfaces);
        //   - /index-file        Write on the named sprk_document and AppendTo on the named parent, as
        //                        the caller, before any download or stamp (see AddTargetedRecordAuthorizationFilter);
        // and every route that writes or deletes takes its partition from the TOKEN, rejecting a body or
        // query tenant that disagrees (the send-to-index precedent, task 063 / F2).
        //
        // DELETED by task 163 (owner round 10 item 1 — no caller in the repo, in no published API
        // description): POST /index/batch and DELETE /source/{sourceDocumentId}. Evidence in
        // projects/unified-access-control-r2/notes/task-163-search-rag-knowledge-insights-authorization.md §2.

        // POST /api/ai/rag/search - Hybrid search
        group.MapPost("/search", Search)
            .AddTenantAuthorizationFilter()
            .AddTargetedRecordAuthorizationFilter(SearchNamesParentRecord, ResolveSearchParentTargets)
            .RequireRateLimiting("ai-batch")
            .WithName("RagSearch")
            .WithSummary("Search knowledge base using hybrid search")
            .WithDescription("Executes hybrid search combining keyword, vector, and semantic ranking for optimal relevance. Results are trimmed to documents the caller can read.")
            .Produces<RagSearchResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // POST /api/ai/rag/index - Index a document chunk (operator surface: SystemAdmin)
        group.MapPost("/index", IndexDocument)
            .RequireAuthorization("SystemAdmin")
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagIndexDocument")
            .WithSummary("Index a document chunk into the knowledge base")
            .WithDescription("Operator surface (SystemAdmin). Generates embedding and indexes the document chunk into the caller's own tenant partition.")
            .Produces<KnowledgeDocument>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // DELETE /api/ai/rag/{documentId} - Delete a chunk by its index key (operator surface: SystemAdmin)
        group.MapDelete("/{documentId}", DeleteDocument)
            .RequireAuthorization("SystemAdmin")
            .AddTenantAuthorizationFilter()
            .WithName("RagDeleteDocument")
            .WithSummary("Delete a document chunk from the knowledge base")
            .WithDescription("Operator surface (SystemAdmin). Deletes the chunk with this index key only when it is in the caller's own tenant partition.")
            .Produces<RagDeleteResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // GET /api/ai/rag/embedding - Generate embedding for text (utility endpoint)
        group.MapPost("/embedding", GetEmbedding)
            .RequireRateLimiting("ai-batch")
            .WithName("RagGetEmbedding")
            .WithSummary("Generate embedding for text content")
            .WithDescription("Generates a vector embedding for the provided text. Uses caching when available.")
            .Produces<EmbeddingResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/index-file - Index a file via unified pipeline
        group.MapPost("/index-file", IndexFile)
            .AddTenantAuthorizationFilter()
            .AddTargetedRecordAuthorizationFilter(IndexFileNamesRecord, ResolveIndexFileTargets)
            .RequireRateLimiting("ai-batch")
            .WithName("RagIndexFile")
            .WithSummary("Index a file into the knowledge base via unified pipeline")
            .WithDescription("Downloads file via OBO authentication, extracts text, chunks, generates embeddings, and indexes to Azure AI Search in the caller's own tenant partition. A named document requires Write and a named parent requires AppendTo, evaluated as the caller.")
            .Produces<FileIndexingResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(500);

        // POST /api/ai/rag/send-to-index - Index documents by ID (for Dataverse ribbon button)
        // Uses user OBO authentication to access files
        // Updates Dataverse sprk_searchindexed fields after successful indexing
        //
        // Authorization is a PAIR (task 063, finding F2 — see the remarks on SendToIndex):
        //   - the tenant binding is route-level, in AddTenantAuthorizationFilter (ADR-008), and
        //   - the per-document Write check is in the handler, because this route's contract is a
        //     per-document result list and a filter can only allow or deny the whole request.
        group.MapPost("/send-to-index", SendToIndex)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagSendToIndex")
            .WithSummary("Index documents by ID for semantic search")
            .WithDescription("Indexes one or more documents by DocumentId. Gets file details from Dataverse, indexes via OBO auth, and updates Dataverse tracking fields. Designed for Dataverse ribbon button integration.")
            .Produces<SendToIndexResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // POST /api/ai/rag/enqueue-indexing - Enqueue a file for background RAG indexing
        // Auth: Named RagApiKey scheme (task AUTHV2-045). Mapped on `app` (not `group`) so
        // the group's `.RequireAuthorization()` (JWT default) does NOT compose with the API
        // key policy — API key callers do not present a JWT, so AND-composition would 401 them.
        // Job handler uses app-only auth (Pattern 6) for SPE file access.
        app.MapPost("/api/ai/rag/enqueue-indexing", EnqueueIndexing)
            .RequireAuthorization(AuthPolicies.RagApiKey)
            // Task AUTHV2-049 — Use api-key-rag (300/min per API-key scheme) instead of ai-batch
            // (which keys on user oid). API-key callers have no oid claim, so ai-batch would have
            // bucketed all callers into the same partition under the "unknown" fallback key.
            .RequireRateLimiting("api-key-rag")
            .WithName("RagEnqueueIndexing")
            .WithTags("AI RAG")
            .WithSummary("Enqueue a file for background RAG indexing")
            .WithDescription("Validates API key (X-Api-Key) via the named RagApiKey scheme and enqueues file for async indexing. Used for background jobs, scheduled indexing, bulk operations, and automated testing.")
            .Produces<EnqueueIndexingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(500);

        // ═══════════════════════════════════════════════════════════════════════════
        // Admin Bulk Indexing Endpoints
        // ═══════════════════════════════════════════════════════════════════════════

        var adminGroup = group.MapGroup("/admin")
            .RequireAuthorization("SystemAdmin")
            .WithTags("AI RAG Admin");

        // POST /api/ai/rag/admin/bulk-index - Submit a bulk RAG indexing job
        adminGroup.MapPost("/bulk-index", SubmitBulkIndexingJob)
            .WithName("RagSubmitBulkIndexing")
            .WithSummary("Submit a bulk RAG indexing job")
            .WithDescription("Queries documents matching criteria and indexes them in bulk with progress tracking. Returns job ID for status monitoring.")
            .Produces<BulkIndexingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/ai/rag/admin/bulk-index/{jobId}/status - Get bulk job status
        adminGroup.MapGet("/bulk-index/{jobId}/status", GetBulkIndexingJobStatus)
            .WithName("RagGetBulkIndexingStatus")
            .WithSummary("Get bulk indexing job status")
            .WithDescription("Returns progress and status of a bulk indexing job including processed count, errors, and estimated time remaining.")
            .Produces<BulkIndexingStatusResponse>()
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Search knowledge base using hybrid search.
    /// </summary>
    /// <remarks>
    /// <para><b>Authorization (unified-access-control-r2 task 163, sweep finding #5).</b> Before this, any
    /// signed-in caller received the indexed TEXT of every document in the tenant: the handler searched
    /// with no caller principal, and the privilege-group filter that a principal would feed is never
    /// populated by any indexer (finding A-21). Now:</para>
    /// <list type="bullet">
    ///   <item>every returned row is trimmed to documents the CALLER can Read, through ONE call to
    ///   <see cref="IAiAuthorizationService.AuthorizeAsync"/> for the page (the same seam the analysis
    ///   routes use). A row with no usable sprk_document id is dropped, never served; a denied or failed
    ///   authorization keeps no rows; <see cref="RagSearchResponse.TotalCount"/> reports the rows
    ///   actually returned, never the index count;</item>
    ///   <item>a named <c>Options.ParentEntityId</c> is Read-gated as the caller by the route's filter;</item>
    ///   <item>the partition is the token's tenant, and a body tenant that disagrees is rejected 403;</item>
    ///   <item><c>Options.SessionId</c> is refused: no ownership check exists for session files here.</item>
    /// </list>
    /// <para><c>CallerPrincipal</c> is set so the privilege filter sees the caller once groups are stamped;
    /// it is NOT relied on as the trim.</para>
    /// </remarks>
    private static async Task<IResult> Search(
        RagSearchRequest request,
        IRagService ragService,
        IAiAuthorizationService aiAuthorizationService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Query is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.Options?.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required in options",
                Status = 400
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Options.SessionId))
        {
            // No ownership check exists for session files on this route (the session-files index is
            // filtered only by tenant + the session id the caller names), and no in-repo caller sends one.
            return Results.Problem(
                statusCode: 400,
                title: "Invalid Request",
                detail: "Options.SessionId is not accepted on this route.",
                extensions: RagProblemExtensions("RAG_SESSION_ID_NOT_ACCEPTED", httpContext));
        }

        var tenantProblem = CheckCallerTenant(httpContext, request.Options.TenantId, logger, "search", out var callerTenantId);
        if (tenantProblem is not null)
        {
            return tenantProblem;
        }

        // multi-container-multi-index-r1 FR-BFF-07 (task 016): the client supplies `searchIndexName` at the
        // top level of `RagSearchRequest`; `IRagService.SearchAsync` consumes it from
        // `RagSearchOptions.SearchIndexName`. A request without one keeps the options' own value (NFR-02).
        // Task 163: the options are now ALWAYS rebuilt — the partition is the token's tenant and the
        // caller principal is attached — so the caller's options object is never passed through verbatim.
        var effectiveOptions = request.Options with
        {
            TenantId = callerTenantId,
            CallerPrincipal = httpContext.User,
            SearchIndexName = !string.IsNullOrWhiteSpace(request.SearchIndexName)
                ? request.SearchIndexName
                : request.Options.SearchIndexName,
        };

        RagSearchResponse response;
        try
        {
            response = await ragService.SearchAsync(request.Query, effectiveOptions, cancellationToken);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException)
        {
            // multi-container-multi-index-r1 FR-BFF-07 (task 016) — rethrow so the
            // global `UseExceptionHandler` middleware (MiddlewarePipelineExtensions)
            // renders the canonical ProblemDetails JSON per ADR-019. Without this,
            // the generic `catch (Exception)` below would convert
            // `INDEX_NOT_ALLOWED` (statusCode 400) into a 500 response — breaking
            // NFR-08 (rejected index name MUST surface as ProblemDetails 400).
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RAG search failed. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            return ServerError("Search Failed", "The search could not be completed.", httpContext);
        }

        var readable = await TrimToReadableDocumentsAsync(
            response.Results, aiAuthorizationService, httpContext, logger, cancellationToken);

        return Results.Ok(response with { Results = readable, TotalCount = readable.Count });
    }

    /// <summary>
    /// Index a document chunk into the knowledge base.
    /// </summary>
    /// <remarks>
    /// Operator surface (task 163, sweep finding #6): the route requires SystemAdmin because the caller
    /// supplies the whole chunk including its index KEY, so merge-or-upload can overwrite any chunk and no
    /// per-record check could scope it. The partition is the token's tenant; a body tenant that disagrees
    /// is rejected 403 (the filter checks it too — this check keeps detaching the filter from reopening it).
    /// </remarks>
    private static async Task<IResult> IndexDocument(
        KnowledgeDocument document,
        IRagService ragService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Document ID is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(document.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(document.Content))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Content is required",
                Status = 400
            });
        }

        var tenantProblem = CheckCallerTenant(httpContext, document.TenantId, logger, "index", out var callerTenantId);
        if (tenantProblem is not null)
        {
            return tenantProblem;
        }

        // The partition key is the TOKEN's tenant. The body value was proven equal above and is overwritten
        // so a caller-supplied string never reaches the partition key, even when it happens to match.
        document.TenantId = callerTenantId;

        try
        {
            var indexed = await ragService.IndexDocumentAsync(document, cancellationToken);
            return Results.Ok(indexed);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RAG chunk indexing failed. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            return ServerError("Indexing Failed", "The document chunk could not be indexed.", httpContext);
        }
    }

    /// <summary>
    /// Delete a document chunk from the knowledge base, by its index KEY.
    /// </summary>
    /// <remarks>
    /// Operator surface (task 163, sweep finding #30): requires SystemAdmin, because the route value is a
    /// CHUNK key, not a Dataverse record, so there is no record whose rights could decide it. The partition
    /// is the token's tenant: a query tenant that disagrees is rejected 403, and
    /// <see cref="IRagService.DeleteDocumentAsync"/> deletes a chunk only when the chunk with that key
    /// carries that tenant — a chunk of another tenant and an absent chunk both report "nothing deleted",
    /// so the response cannot tell them apart.
    /// </remarks>
    private static async Task<IResult> DeleteDocument(
        string documentId,
        [AsParameters] DeleteDocumentQuery query,
        IRagService ragService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        if (string.IsNullOrWhiteSpace(query.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId query parameter is required",
                Status = 400
            });
        }

        var tenantProblem = CheckCallerTenant(httpContext, query.TenantId, logger, "delete", out var callerTenantId);
        if (tenantProblem is not null)
        {
            return tenantProblem;
        }

        try
        {
            var deleted = await ragService.DeleteDocumentAsync(documentId, callerTenantId, cancellationToken);
            return Results.Ok(new RagDeleteResult { Deleted = deleted, Count = deleted ? 1 : 0 });
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RAG chunk delete failed. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            return ServerError("Delete Failed", "The document chunk could not be deleted.", httpContext);
        }
    }

    // DELETE /source/{sourceDocumentId} (handler DeleteBySourceDocument) and POST /index/batch (handler
    // IndexDocumentsBatch) were DELETED by task 163 under owner round 10 item 1: no caller in the repo and
    // in no published API description. IRagService.DeleteBySourceDocumentAsync / IndexDocumentsBatchAsync
    // remain — RagIndexingPipeline and FileIndexingService call them server-side with server-built input.

    /// <summary>
    /// Generate embedding for text content.
    /// </summary>
    private static async Task<IResult> GetEmbedding(
        EmbeddingRequest request,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Text is required",
                Status = 400
            });
        }

        try
        {
            var embedding = await ragService.GetEmbeddingAsync(request.Text, cancellationToken);
            return Results.Ok(new EmbeddingResult
            {
                Embedding = embedding,
                Dimensions = embedding.Length
            });
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Embedding Generation Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Index a file into the knowledge base via unified pipeline.
    /// Uses OBO authentication to access user's files.
    /// </summary>
    /// <remarks>
    /// <para>When DocumentId is provided, Dataverse tracking fields (sprk_searchindexed,
    /// sprk_searchindexedon, sprk_searchindexname) are updated after successful indexing.</para>
    /// <para><b>Authorization (unified-access-control-r2 task 163, sweep finding #4; takes over GitHub
    /// #1041).</b> The file download was always OBO, so SPE decided the caller's READ of the bytes. What
    /// the caller also chose, unchecked, was where the chunks went and what they claimed to be:</para>
    /// <list type="bullet">
    ///   <item>the PARTITION — now the token's tenant; a body tenant that disagrees is rejected 403
    ///   (TenantAuthorizationFilter's FileIndexRequest case, and again here so detaching the filter cannot
    ///   reopen it);</item>
    ///   <item>the DOCUMENT stamped app-only — the route filter requires Write on that sprk_document, as the
    ///   caller, before anything runs; the row is then read and must point at the same drive item;</item>
    ///   <item>the PARENT the chunks are attributed to — AppendTo on it, as the caller; when the row
    ///   carries a matter/project/invoice the body parent must be one of them, and the chunks carry the
    ///   row's value;</item>
    ///   <item>the KNOWLEDGE SOURCE — refused: no caller sends it, and it would attribute chunks to a
    ///   knowledge source's grounding.</item>
    /// </list>
    /// <para>With neither a document nor a parent named, the file is indexed with no attribution and no
    /// Dataverse write, as before — the OBO download is then the only decision, and it is the caller's.</para>
    /// </remarks>
    private static async Task<IResult> IndexFile(
        FileIndexRequest request,
        IFileIndexingService fileIndexingService,
        IDocumentDataverseService dataverseService,
        ISearchIndexNameResolver searchIndexNameResolver,
        DocumentIndexParentResolver parentResolver,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.DriveId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DriveId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "ItemId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "FileName is required",
                Status = 400
            });
        }

        if (!string.IsNullOrWhiteSpace(request.KnowledgeSourceId) || !string.IsNullOrWhiteSpace(request.KnowledgeSourceName))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Invalid Request",
                detail: "KnowledgeSourceId and KnowledgeSourceName are not accepted on this route.",
                extensions: RagProblemExtensions("INDEX_FILE_KNOWLEDGE_SOURCE_NOT_ACCEPTED", httpContext));
        }

        var tenantProblem = CheckCallerTenant(httpContext, request.TenantId, logger, "index-file", out var callerTenantId);
        if (tenantProblem is not null)
        {
            return tenantProblem;
        }

        try
        {
            // The partition key is the TOKEN's tenant (task 163 / #1041), never the body's — the body value
            // was proven equal above and is deliberately not passed on.
            var indexRequest = request with { TenantId = callerTenantId };

            if (!string.IsNullOrEmpty(request.DocumentId))
            {
                // The route filter has already required Write on sprk_documents(DocumentId), as the caller.
                // Read the row (as SendToIndex Step 1) and hold the body to it.
                var document = await dataverseService.GetDocumentAsync(request.DocumentId, cancellationToken);
                if (document is null)
                {
                    // Absent between the check and now: the same answer the filter gives for an absent id.
                    return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
                }

                var (rowProblem, rowParent) = await HoldIndexFileRequestToRowAsync(request, document, parentResolver, cancellationToken);
                if (rowProblem is not null)
                {
                    logger.LogWarning(
                        "index-file refused ({Reason}): the request does not match the document row it names. "
                        + "CorrelationId={CorrelationId}",
                        rowProblem, httpContext.TraceIdentifier);

                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Conflict",
                        detail: "The request does not match the document record it names.",
                        extensions: RagProblemExtensions(rowProblem, httpContext));
                }

                indexRequest = indexRequest with
                {
                    DriveId = document.GraphDriveId!,
                    ItemId = document.GraphItemId!,
                    // The record that governs the document (DocumentIndexParentResolver, task 177) when the row
                    // names one; the body parent the filter authorized (AppendTo) only when the row names none.
                    ParentEntity = rowParent,
                };
            }

            var result = await fileIndexingService.IndexFileAsync(indexRequest, httpContext, cancellationToken);

            if (!result.Success)
            {
                // The pipeline's own message can describe the drive item; it stays in the server log.
                logger.LogWarning(
                    "index-file pipeline reported failure: {Error}. CorrelationId={CorrelationId}",
                    result.ErrorMessage, httpContext.TraceIdentifier);
                return ServerError("Indexing Failed", "The file could not be indexed.", httpContext);
            }

            // Update Dataverse tracking fields when DocumentId is provided.
            // R3 FR-3H3.2 dual-write: set new sprk_searchindexcompletedon AND keep legacy
            // sprk_searchindexed=true + sprk_searchindexedon for the transition window
            // (R3 + one sprint per spec assumption line 366). Removal deferred to R4.
            if (!string.IsNullOrEmpty(request.DocumentId))
            {
                var indexName = searchIndexNameResolver.GetDefaultIndexName();
                var completedAt = DateTime.UtcNow;
                var updateRequest = new UpdateDocumentRequest
                {
                    // New canonical lifecycle marker (R3+)
                    SearchIndexCompletedOn = completedAt,
                    // Legacy dual-write (preserved during transition)
                    SearchIndexed = true,
                    SearchIndexedOn = completedAt,
                    // Index routing (unchanged)
                    SearchIndexName = indexName
                };

                await dataverseService.UpdateDocumentAsync(request.DocumentId, updateRequest, cancellationToken);
            }

            return Results.Ok(result);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullFileIndexingService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException)
        {
            // e.g. 400 INDEX_NOT_ALLOWED for a SearchIndexName outside the allow-list — rendered by the global
            // ProblemDetails middleware (ADR-019), not swallowed into a 500.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "index-file failed. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            return ServerError("Indexing Failed", "The file could not be indexed.", httpContext);
        }
    }

    /// <summary>
    /// Holds an index-file request that names a document to that document's ROW. Returns the reason code for the 409, or
    /// <c>null</c> and the parent the chunks carry.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>The row must carry a file (GraphDriveId + GraphItemId) — otherwise there is nothing the
    ///   request can legitimately be indexing for this row.</item>
    ///   <item>The body DriveId/ItemId must be the row's — otherwise the caller would have the app stamp
    ///   row A as indexed with the contents of file B.</item>
    ///   <item>When the row names records (work assignment, project, matter, invoice, a related event), the body
    ///   ParentEntity (if any) must be one of them: type compared case-insensitively with the "sprk_" prefix ignored,
    ///   ids compared as GUIDs. The chunks then carry the record that GOVERNS the document
    ///   (<see cref="DocumentIndexParentResolver"/>, task 177), whichever of them the body named — or none, when that
    ///   decision fails closed.</item>
    ///   <item>When the row names no record, the body parent the route filter authorized (AppendTo) is used. When the row
    ///   could not be read, no parent is used, never the body's.</item>
    /// </list>
    /// </remarks>
    internal static async Task<(string? Problem, ParentEntityContext? Parent)> HoldIndexFileRequestToRowAsync(
        FileIndexRequest request, DocumentEntity document, DocumentIndexParentResolver parentResolver, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            return ("INDEX_FILE_DOCUMENT_HAS_NO_FILE", null);
        }

        if (!string.Equals(request.DriveId, document.GraphDriveId, StringComparison.Ordinal)
            || !string.Equals(request.ItemId, document.GraphItemId, StringComparison.Ordinal))
        {
            return ("INDEX_FILE_ITEM_MISMATCH", null);
        }

        var decision = await parentResolver.DecideAsync(document, ct);
        if (decision.Named.Count == 0)
        {
            return (null, decision.Decided ? request.ParentEntity : null);
        }

        if (request.ParentEntity is not null)
        {
            var bodyType = NormalizeParentType(request.ParentEntity.EntityType);
            var matched = Guid.TryParse(request.ParentEntity.EntityId, out var bodyId)
                && decision.Named.Any(p =>
                    string.Equals(p.EntityType, bodyType, StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(p.EntityId, out var rowId)
                    && rowId == bodyId);
            if (!matched)
            {
                return ("INDEX_FILE_PARENT_MISMATCH", null);
            }
        }

        return (null, decision.Parent);
    }

    private static string NormalizeParentType(string? entityType)
    {
        var trimmed = (entityType ?? string.Empty).Trim();
        return trimmed.StartsWith("sprk_", StringComparison.OrdinalIgnoreCase) ? trimmed[5..] : trimmed;
    }

    /// <summary>The Dataverse entity set the documents on this route live in.</summary>
    private const string DocumentEntitySetName = "sprk_documents";

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization helpers (unified-access-control-r2 task 163)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs <see cref="FinanceAuthorizationFilter"/>'s per-route declaration check — as it is, with the
    /// uniform 404 denial — but ONLY when the request names a Dataverse record (<paramref name="namesRecordTarget"/>).
    /// </summary>
    /// <remarks>
    /// <para>Why conditional: the declaration filter denies a request whose declaration yields no check, which
    /// is right for routes that always act on a record. Two routes here act on a record only SOMETIMES:
    /// <c>/search</c> (a named parent) and <c>/index-file</c> (a named document and/or parent). When they name
    /// none, the per-row trim (search) or the caller's own OBO download (index-file) is the decision, and
    /// there is no record whose rights could add anything. When they name one, the full declaration check
    /// runs — and a target that cannot be resolved denies, exactly as on every other declaration route.</para>
    /// <para>No second check loop: the evaluation is <see cref="FinanceAuthorizationFilter.InvokeAsync"/>
    /// itself (task 130). A presence probe that throws is treated as "names a record", so the declaration
    /// check runs and fails closed.</para>
    /// </remarks>
    private static RouteHandlerBuilder AddTargetedRecordAuthorizationFilter(
        this RouteHandlerBuilder builder,
        Func<EndpointFilterInvocationContext, bool> namesRecordTarget,
        Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> resolveTargets)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            bool targeted;
            try
            {
                targeted = namesRecordTarget(context);
            }
            catch (Exception)
            {
                targeted = true;
            }

            if (!targeted)
            {
                return await next(context);
            }

            var services = context.HttpContext.RequestServices;
            var gate = new FinanceAuthorizationFilter(
                services.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>(),
                resolveTargets,
                FinanceDenial.UniformNotFound);
            return await gate.InvokeAsync(context, next);
        });
    }

    /// <summary>/search names a record when <c>Options.ParentEntityId</c> is non-empty.</summary>
    internal static bool SearchNamesParentRecord(EndpointFilterInvocationContext context) =>
        !string.IsNullOrWhiteSpace(
            context.Arguments.OfType<RagSearchRequest>().FirstOrDefault()?.Options?.ParentEntityId);

    /// <summary>
    /// /search with a named parent: Read on that parent, as the caller. An unrecognized type or a non-GUID id
    /// declares no check, which the filter denies with the uniform 404.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveSearchParentTargets(EndpointFilterInvocationContext context)
    {
        var options = context.Arguments.OfType<RagSearchRequest>().FirstOrDefault()?.Options;

        if (options is null
            || !EntityAccessFilter.TryResolveEntitySet(options.ParentEntityType, out var entitySet)
            || !Guid.TryParse(options.ParentEntityId, out var parentId)
            || parentId == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Authorize();
        }

        return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = entitySet,
            RecordId = parentId,
            Operation = "read",
            Source = "body.options.parentEntityId",
        });
    }

    /// <summary>/index-file names a record when it carries a DocumentId or a ParentEntity.</summary>
    internal static bool IndexFileNamesRecord(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<FileIndexRequest>().FirstOrDefault();
        return request is not null
            && (!string.IsNullOrEmpty(request.DocumentId) || request.ParentEntity is not null);
    }

    /// <summary>
    /// /index-file: Write ("write") on sprk_documents(DocumentId) — the row is stamped app-only after indexing —
    /// and AppendTo ("entity.associate_document") on the named parent, whose id is written onto every chunk.
    /// Both run when both are named. Any named target that cannot be resolved (non-GUID document id,
    /// unrecognized parent type, non-GUID parent id) declares NO check at all, so the whole request is denied
    /// with the uniform 404 rather than authorized on its other half.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveIndexFileTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<FileIndexRequest>().FirstOrDefault();
        if (request is null)
        {
            return FinanceAuthorizationTargets.Authorize();
        }

        var checks = new List<FinanceAuthorizationCheck>(2);

        if (!string.IsNullOrEmpty(request.DocumentId))
        {
            if (!Guid.TryParse(request.DocumentId, out var documentId) || documentId == Guid.Empty)
            {
                return FinanceAuthorizationTargets.Authorize();
            }

            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = DocumentEntitySetName,
                RecordId = documentId,
                Operation = "write",
                Source = "body.documentId",
            });
        }

        if (request.ParentEntity is not null)
        {
            if (!EntityAccessFilter.TryResolveEntitySet(request.ParentEntity.EntityType, out var parentSet)
                || !Guid.TryParse(request.ParentEntity.EntityId, out var parentId)
                || parentId == Guid.Empty)
            {
                return FinanceAuthorizationTargets.Authorize();
            }

            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = parentSet,
                RecordId = parentId,
                Operation = "entity.associate_document",
                Source = "body.parentEntity",
            });
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
    }

    /// <summary>
    /// The tenant binding shared by every route here that reads or writes a partition: the partition is the
    /// TOKEN's tenant (<see cref="TenantResolution.ResolveTenantId"/>), and a request value that disagrees
    /// is REJECTED 403 — never silently corrected (task 063 / F2 precedent). The token's tenant is in the
    /// caller's own token, so the 403 is not an existence oracle. Returns <c>null</c> when they agree.
    /// </summary>
    private static IResult? CheckCallerTenant(
        HttpContext httpContext, string? requestedTenantId, ILogger logger, string route, out string callerTenantId)
    {
        callerTenantId = TenantResolution.ResolveTenantId(httpContext.User) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(callerTenantId))
        {
            logger.LogWarning("rag/{Route} refused: the caller carries no tenant claim, so the partition cannot be established.", route);
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token.",
                extensions: RagProblemExtensions("RAG_NO_TENANT_CLAIM", httpContext));
        }

        if (!string.Equals(requestedTenantId, callerTenantId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("rag/{Route} refused: the requested tenant does not match the caller's tenant claim.", route);
            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "The requested tenant does not match your authenticated tenant.",
                extensions: RagProblemExtensions("RAG_TENANT_MISMATCH", httpContext));
        }

        return null;
    }

    /// <summary>
    /// The extensions of a problem task 163 added to this file: the stable <c>errorCode</c> ADR-019 requires, the
    /// same value under this file's existing <c>code</c> key (the SendToIndex precedent its clients read), and the
    /// correlation id.
    /// </summary>
    private static Dictionary<string, object?> RagProblemExtensions(string code, HttpContext httpContext) =>
        new()
        {
            ["errorCode"] = code,
            ["code"] = code,
            ["correlationId"] = httpContext.TraceIdentifier,
        };

    /// <summary>
    /// A 500 with a FIXED detail (ADR-019): the exception message can disclose internals and record
    /// existence, so it goes to the server log, never the response.
    /// </summary>
    private static IResult ServerError(string title, string detail, HttpContext httpContext) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: RagProblemExtensions("RAG_INTERNAL_ERROR", httpContext));

    /// <summary>
    /// Trims a result page to the rows whose document the CALLER can Read (task 163, owner round 9 "lists
    /// trimmed"): ONE <see cref="IAiAuthorizationService.AuthorizeAsync"/> call with the distinct parsed
    /// sprk_document ids of the page; a row is kept only when its DocumentId parses as a non-empty GUID that
    /// the seam returned as authorized.
    /// </summary>
    /// <remarks>
    /// Fail closed per row (ADR-003): a null or non-GUID DocumentId (an orphan file, a knowledge-source chunk)
    /// has no record to evaluate and is dropped, never served; when no row has a usable id the seam is not
    /// called (it throws on an empty list) and nothing is returned; a denied, partial or faulted
    /// authorization keeps only what it explicitly authorized — none on a fault. The seam's Reason text names
    /// document ids and is never returned.
    /// </remarks>
    internal static async Task<IReadOnlyList<RagSearchResult>> TrimToReadableDocumentsAsync(
        IReadOnlyList<RagSearchResult> rows,
        IAiAuthorizationService aiAuthorizationService,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var distinctIds = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (Guid.TryParse(row.DocumentId, out var id) && id != Guid.Empty && seen.Add(id))
            {
                distinctIds.Add(id);
            }
        }

        if (distinctIds.Count == 0)
        {
            return Array.Empty<RagSearchResult>();
        }

        HashSet<Guid> permitted;
        try
        {
            var decision = await aiAuthorizationService.AuthorizeAsync(
                httpContext.User, distinctIds, httpContext, cancellationToken);
            permitted = decision.AuthorizedDocumentIds.ToHashSet();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "RAG search row authorization faulted; returning no rows (fail closed). CorrelationId={CorrelationId}",
                httpContext.TraceIdentifier);
            return Array.Empty<RagSearchResult>();
        }

        var kept = rows
            .Where(r => Guid.TryParse(r.DocumentId, out var id) && permitted.Contains(id))
            .ToList();

        if (kept.Count != rows.Count)
        {
            logger.LogInformation(
                "RAG search row authorization kept {Kept} of {Total} rows ({Distinct} distinct documents evaluated). "
                + "CorrelationId={CorrelationId}",
                kept.Count, rows.Count, distinctIds.Count, httpContext.TraceIdentifier);
        }

        return kept;
    }

    /// <summary>
    /// Index documents by DocumentId for semantic search.
    /// Designed for Dataverse ribbon button integration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// - Gets document details from Dataverse (including parent entity lookups)
    /// - Indexes file via OBO authentication
    /// - Updates Dataverse with search index tracking fields (sprk_searchindexed, etc.)
    /// - Returns results for each document processed
    /// </para>
    /// <para>
    /// <b>Authorization (spaarkeai-word-add-in-r1 task 063, Fable finding F2).</b> Two things are
    /// checked here that were not checked before, and the tests that pin them are
    /// <c>tests/integration/contract/Api/Ai/SendToIndexAuthorizationContractTests.cs</c>.
    /// </para>
    /// <para>
    /// (1) <b>The tenant partition is the token's, not the body's.</b> It is resolved through
    /// <see cref="TenantResolution.ResolveTenantId"/> — the BFF's single answer to "which tenant is
    /// this caller in?" — and a body <c>TenantId</c> that disagrees is REJECTED with 403 rather than
    /// silently overridden. Silently correcting it would tell a caller their chunks landed in the
    /// partition they named when they landed somewhere else. The field is retained in the request
    /// contract because both in-repo callers send it (the Dataverse ribbon in
    /// <c>sprk_DocumentOperations.js</c> and the Word add-in's Find view), and both source it from
    /// their MSAL account's tenant — i.e. the same value as <c>tid</c>, so neither breaks.
    /// </para>
    /// <para>
    /// (2) <b>Every document is authorized for Write before its row is stamped</b>, evaluated AS THE
    /// CALLER through <see cref="Spaarke.Core.Auth.AuthorizationService.GetCallerRecordAccessAsync"/>
    /// — the entity-generic caller-evaluated evaluator the record- and semantic-search surfaces
    /// already use, which fails closed without the caller's bearer token. <b>Write</b>, not Read:
    /// this route MUTATES the row (<c>sprk_searchindexed</c>, <c>sprk_searchindexedon</c>,
    /// <c>sprk_searchindexcompletedon</c>, <c>sprk_searchindexname</c>) app-only, and read access is
    /// not consent to be written to. Before this, the effective gate was "can you read the file's SPE
    /// container", which the row-level Dataverse rights need not agree with.
    /// </para>
    /// <para>
    /// <b>Why the per-document check is here and not in a filter</b> (ADR-008's default shape). A
    /// filter can only allow or deny the WHOLE request, and this route's contract is a per-document
    /// result list. The partial-permission behaviour below — index the permitted, refuse the denied
    /// in place — is only expressible in the handler. The tenant binding, which IS a whole-request
    /// decision, does live in the filter (<see cref="TenantAuthorizationFilter"/>); the duplicate
    /// check here is the forcing function that keeps detaching that filter from re-opening the hole.
    /// </para>
    /// <para>
    /// <b>Partial permission, stated exactly.</b> Authorization for every requested id is decided
    /// FIRST, in one pass, before any Dataverse read or file download. If the caller may write none
    /// of them the whole request is refused with 403 and no row is read — a 200 reporting "0 of N
    /// succeeded" is indistinguishable from an indexing outage, and both in-repo callers render that
    /// as "try again". If the caller may write at least one, the response is 200 and each denied
    /// document appears as its own failed result (<c>Success=false</c>, a denial message, and
    /// <b>no</b> <c>ParentEntityType</c>/<c>ParentEntityId</c> — that parent identifier is one of the
    /// things F2 says a caller should not learn); its row is never read, never indexed and never
    /// stamped. One unauthorized id must not deny service to the rest of a legitimate batch.
    /// </para>
    /// <para>
    /// <b>Cost.</b> One extra Dataverse round trip per DISTINCT requested id, memoized within the
    /// request and absorbed across requests by <c>CachedAccessDataSource</c>'s 60 s key. This route
    /// already spends, per document, one Dataverse read + an SPE download + extraction + embedding +
    /// an index write + a Dataverse write, so the check is a small fraction of existing per-document
    /// cost — unlike a keystroke-driven typeahead, where task 062 rejected exactly this mechanism for
    /// exactly that reason. Sequential, because <c>DataverseAccessDataSource</c> mutates a shared
    /// request-scoped <c>HttpClient</c>'s auth header per call.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SendToIndex(
        [FromBody] SendToIndexRequest request,
        IFileIndexingService fileIndexingService,
        IDocumentDataverseService dataverseService,
        ISearchIndexNameResolver searchIndexNameResolver,
        DocumentIndexParentResolver parentResolver,
        Spaarke.Core.Auth.AuthorizationService authorizationService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        // Validate request
        if (request.DocumentIds == null || request.DocumentIds.Count == 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DocumentIds is required and must contain at least one document ID",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        // ── The tenant partition comes from the token (task 063 / F2). ──
        var callerTenantId = TenantResolution.ResolveTenantId(httpContext.User);
        if (string.IsNullOrWhiteSpace(callerTenantId))
        {
            logger.LogWarning(
                "send-to-index refused: the caller is authenticated but carries no tenant claim, so "
                + "the index partition cannot be established. A tenant that cannot be established is "
                + "not one that can be guessed.");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token.",
                extensions: new Dictionary<string, object?> { ["code"] = "SEND_TO_INDEX_NO_TENANT_CLAIM" });
        }

        if (!string.Equals(request.TenantId, callerTenantId, StringComparison.OrdinalIgnoreCase))
        {
            // Rejected, never silently corrected. See the remarks on this method.
            logger.LogWarning(
                "send-to-index refused: body TenantId does not match the caller's tenant claim. "
                + "DocumentCount={DocumentCount}",
                request.DocumentIds.Count);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "The requested tenant does not match your authenticated tenant.",
                extensions: new Dictionary<string, object?> { ["code"] = "SEND_TO_INDEX_TENANT_MISMATCH" });
        }

        // ── Every document is authorized for Write, as the caller, before anything is read. ──
        var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);
        var callerToken = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);

        if (string.IsNullOrEmpty(callerObjectId) || string.IsNullOrEmpty(callerToken))
        {
            // Without both, access can only be evaluated app-only — which on this surface answers
            // "yes" for every caller. Refusing is the only alternative to reopening the finding.
            logger.LogWarning(
                "send-to-index refused: no caller object id or no bearer token, so per-document "
                + "access cannot be evaluated as the caller. Refusing rather than evaluating app-only.");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "A caller identity and bearer token are required to authorize this operation.",
                extensions: new Dictionary<string, object?> { ["code"] = "SEND_TO_INDEX_NO_CALLER_CONTEXT" });
        }

        // One verdict per REQUESTED POSITION, memoized by the PARSED record id. Keying the memo on
        // the parsed id rather than on the raw string means "{ABC-…}" and "abc-…" share one decision
        // instead of costing two round trips, and it keeps a null or malformed entry out of the map
        // entirely — such an entry is denied below without ever being used as a key.
        var writePermitted = new bool[request.DocumentIds.Count];
        var decisions = new Dictionary<Guid, bool>();

        for (var i = 0; i < request.DocumentIds.Count; i++)
        {
            // An id that is not a usable record id is denied rather than probed: there is no record
            // to evaluate, and "no answer" must not resolve to "proceed".
            if (!Guid.TryParse(request.DocumentIds[i], out var recordId) || recordId == Guid.Empty)
            {
                writePermitted[i] = false;
                continue;
            }

            if (!decisions.TryGetValue(recordId, out var permitted))
            {
                var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                    callerObjectId, DocumentEntitySetName, recordId, callerToken, cancellationToken);

                permitted = snapshot.AccessRights.HasFlag(AccessRights.Write);
                decisions[recordId] = permitted;
            }

            writePermitted[i] = permitted;
        }

        if (!writePermitted.Any(p => p))
        {
            logger.LogWarning(
                "send-to-index refused: the caller may write none of the {DocumentCount} requested "
                + "documents. No row was read.",
                request.DocumentIds.Count);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have permission to index any of the requested documents.",
                extensions: new Dictionary<string, object?> { ["code"] = "SEND_TO_INDEX_FORBIDDEN" });
        }

        var results = new List<SendToIndexDocumentResult>();

        for (var i = 0; i < request.DocumentIds.Count; i++)
        {
            var documentId = request.DocumentIds[i];

            if (!writePermitted[i])
            {
                // Refused in place. The row is not read, not indexed, not stamped, and nothing about
                // it — including its parent entity — is disclosed.
                results.Add(new SendToIndexDocumentResult
                {
                    DocumentId = documentId,
                    Success = false,
                    ErrorMessage = "Access denied: indexing updates this document's record, which requires Write permission."
                });
                continue;
            }

            try
            {
                // Step 1: Get document from Dataverse
                var document = await dataverseService.GetDocumentAsync(documentId, cancellationToken);
                if (document == null)
                {
                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = "Document not found"
                    });
                    continue;
                }

                // Step 2: Validate document has file
                if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
                {
                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = "Document does not have an associated file (missing DriveId or ItemId)"
                    });
                    continue;
                }

                // Step 3: the parent the chunks are filed under is the record whose access GOVERNS the document — the
                // ONE decision (DocumentIndexParentResolver, #1510 / task 177). Among the records the row names (work
                // assignment, project, matter, invoice, typed or sprk_related*; else the core stamps of a related event
                // or communication): the one secure record (effective), several of one secure family -> the most
                // specific of them; none secure -> the most specific; two different secure roots or an unreadable
                // secure state -> no parent (fail closed). A row naming one record keeps the parent it always had.
                var parentEntity = await parentResolver.ResolveAsync(document, cancellationToken);

                // Step 3b (task 033 fix): resolve the per-record AI Search index name BEFORE
                // building the index request. Previously this handler called ONLY
                // searchIndexNameResolver.GetDefaultIndexName() (the tenant default), so a document
                // whose own sprk_searchindexname (or a parent/BU's AI Search Index lookup) named a
                // different index was silently routed to the tenant-default index instead — the
                // write landed in the wrong place, not just the wrong tracking stamp. Mirrors
                // RagIndexingJobHandler's precedence exactly: resolver chain (document's own
                // sprk_ai_search_index lookup, falling back to the legacy sprk_searchindexname text
                // column → parent → parent's owning BU) first; GetDefaultIndexName() is now only the
                // final fallback when the chain resolves nothing.
                var resolvedIndexName = await searchIndexNameResolver.ResolveAsync(
                    documentId, parentEntity?.EntityType, parentEntity?.EntityId, cancellationToken);

                // Step 4: Build file index request
                var indexRequest = new FileIndexRequest
                {
                    // Task 063 / F2: the partition key is the TOKEN's tenant. The body's TenantId was
                    // proven equal to it above and is deliberately not read here — a value the caller
                    // supplies must not reach the partition key even when it happens to be correct.
                    TenantId = callerTenantId,
                    DriveId = document.GraphDriveId,
                    ItemId = document.GraphItemId,
                    FileName = document.FileName ?? document.Name,
                    DocumentId = documentId,
                    ParentEntity = parentEntity,
                    // Null/whitespace here falls through to IRagService's own tenant-default chain
                    // (byte-for-byte backward compatible), so the ACTUAL write — not just the
                    // Dataverse stamp below — honors the per-record index.
                    SearchIndexName = resolvedIndexName,
                    // Task 048 (spaarkeai-word-add-in-r1): this route re-indexes an EXISTING Dataverse
                    // document by id ("Send to Index" / the Dataverse ribbon button) — the item may
                    // already carry chunks from an earlier index. Trim any leftover tail after the new
                    // chunks land; a first index (never-indexed document) finds nothing to trim.
                    ReplaceStaleChunks = true,
                };

                // Step 5: Index via OBO authentication
                var indexResult = await fileIndexingService.IndexFileAsync(indexRequest, httpContext, cancellationToken);

                if (indexResult.Success)
                {
                    // Step 6: Update Dataverse with search index fields.
                    // R3 FR-3H3.2 dual-write: set new sprk_searchindexcompletedon AND keep legacy
                    // sprk_searchindexed=true + sprk_searchindexedon for the transition window
                    // (R3 + one sprint per spec assumption line 366). Removal deferred to R4.
                    //
                    // Stamp the index the file actually landed in (task 033 fix, mirrors
                    // RagIndexingJobHandler): the per-record resolved value when the chain found
                    // one, otherwise the single canonical tenant default.
                    var stampedIndexName = resolvedIndexName ?? searchIndexNameResolver.GetDefaultIndexName();
                    var completedAt = DateTime.UtcNow;
                    var updateRequest = new UpdateDocumentRequest
                    {
                        // New canonical lifecycle marker (R3+)
                        SearchIndexCompletedOn = completedAt,
                        // Legacy dual-write (preserved during transition)
                        SearchIndexed = true,
                        SearchIndexedOn = completedAt,
                        // Index routing (task 033: per-record when resolved, else tenant default)
                        SearchIndexName = stampedIndexName
                    };

                    await dataverseService.UpdateDocumentAsync(documentId, updateRequest, cancellationToken);

                    logger.LogInformation(
                        "Document {DocumentId} indexed successfully: {ChunksIndexed} chunks to {IndexName}",
                        documentId, indexResult.ChunksIndexed, stampedIndexName);

                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = true,
                        ChunksIndexed = indexResult.ChunksIndexed,
                        IndexName = stampedIndexName,
                        ParentEntityType = parentEntity?.EntityType,
                        ParentEntityId = parentEntity?.EntityId
                    });
                }
                else
                {
                    logger.LogWarning(
                        "Document {DocumentId} indexing failed: {Error}",
                        documentId, indexResult.ErrorMessage);

                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = indexResult.ErrorMessage
                    });
                }
            }
            catch (FeatureDisabledException ex)
            {
                // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullFileIndexingService surfaced.
                // Kill-switch state is request-global, not per-document — short-circuit the whole batch
                // with a 503 ProblemDetails rather than recording per-document failures that would mislead
                // operators into chasing N "indexing failed" entries when the root cause is one kill switch.
                return ex.AsFeatureDisabled503();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing document {DocumentId} for indexing", documentId);
                results.Add(new SendToIndexDocumentResult
                {
                    DocumentId = documentId,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return Results.Ok(new SendToIndexResponse
        {
            TotalRequested = request.DocumentIds.Count,
            SuccessCount = results.Count(r => r.Success),
            FailedCount = results.Count(r => !r.Success),
            Results = results
        });
    }

    /// <summary>
    /// Enqueue a file for background RAG indexing via job queue.
    /// Authorization enforced upstream by the <see cref="AuthPolicies.RagApiKey"/> policy
    /// (task AUTHV2-045 — named API key scheme). The job handler
    /// (RagIndexingJobHandler) uses Pattern 6 (app-only auth) for SPE file access.
    /// </summary>
    /// <remarks>
    /// Returns 202 Accepted with job tracking information for async processing.
    /// </remarks>
    private static async Task<IResult> EnqueueIndexing(
        [FromBody] FileIndexRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");
        var traceId = httpRequest.HttpContext.TraceIdentifier;
        var correlationId = httpRequest.Headers["X-Correlation-Id"].FirstOrDefault() ?? traceId;

        // API key validation is performed by ApiKeyAuthenticationHandler (RagApiKey scheme)
        // bound via .RequireAuthorization(AuthPolicies.RagApiKey) on the endpoint registration.

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.DriveId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DriveId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "ItemId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "FileName is required",
                Status = 400
            });
        }

        try
        {
            // Step 3: Build job payload
            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingJobPayload
            {
                TenantId = request.TenantId,
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                FileName = request.FileName,
                DocumentId = request.DocumentId,
                KnowledgeSourceId = request.KnowledgeSourceId,
                KnowledgeSourceName = request.KnowledgeSourceName,
                Metadata = request.Metadata,
                ParentEntity = request.ParentEntity,
                Source = "EnqueueEndpoint",
                EnqueuedAt = DateTimeOffset.UtcNow,
                // Task 048 (spaarkeai-word-add-in-r1): thread the caller's own value through instead of
                // silently dropping it. FileIndexRequest.ReplaceStaleChunks has been a bindable field on
                // this request body since task 029; before this fix a caller that set it true on the
                // wire had that intent discarded here. This endpoint has no first-class "this is a
                // re-index" signal of its own (unlike SendToIndex / the Knowledge Base reindex route), so
                // the caller decides, same as the sibling IndexFile endpoint.
                ReplaceStaleChunks = request.ReplaceStaleChunks,
            }));

            // Step 4: Create and submit job
            var idempotencyKey = $"rag-index-{request.DriveId}-{request.ItemId}";
            var job = new JobContract
            {
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = request.ItemId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
                Payload = jobPayload,
                MaxAttempts = 3
            };

            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Enqueued RAG indexing job {JobId} for file {FileName} (DriveId: {DriveId}, ItemId: {ItemId})",
                job.JobId, request.FileName, request.DriveId, request.ItemId);

            return Results.Accepted(
                value: new EnqueueIndexingResponse
                {
                    Accepted = true,
                    JobId = job.JobId,
                    CorrelationId = correlationId,
                    IdempotencyKey = idempotencyKey,
                    Message = $"File {request.FileName} queued for RAG indexing"
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue RAG indexing job for {FileName}", request.FileName);
            return Results.Problem(
                title: "Enqueue Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Submit a bulk RAG indexing job.
    /// Admin endpoint that queries documents matching criteria and enqueues them for indexing.
    /// </summary>
    private static async Task<IResult> SubmitBulkIndexingJob(
        [FromBody] BulkIndexingRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        BatchJobStatusStore statusStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");
        var correlationId = httpRequest.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpRequest.HttpContext.TraceIdentifier;

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        try
        {
            // Build job payload from request
            var payload = new BulkRagIndexingPayload
            {
                TenantId = request.TenantId,
                Filter = request.Filter,
                MatterId = request.MatterId,
                CreatedAfter = request.CreatedAfter,
                CreatedBefore = request.CreatedBefore,
                DocumentType = request.DocumentType,
                MaxDocuments = request.MaxDocuments,
                MaxConcurrency = request.MaxConcurrency,
                ForceReindex = request.ForceReindex,
                Source = "Admin"
            };

            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(payload));

            // Create job contract
            var job = new JobContract
            {
                JobType = BulkRagIndexingJobHandler.JobTypeName,
                SubjectId = request.TenantId,
                CorrelationId = correlationId,
                IdempotencyKey = $"bulk-rag-{request.TenantId}-{DateTimeOffset.UtcNow.Ticks}",
                Payload = jobPayload,
                MaxAttempts = 1 // Bulk jobs should not auto-retry
            };

            // Create initial job status in cache (estimated total is 0 until job starts)
            // Note: BatchFiltersApplied is reused from email batch - we map RAG filters to compatible fields
            await statusStore.CreateJobStatusAsync(
                job.JobId.ToString(),
                new BatchFiltersApplied
                {
                    StartDate = request.CreatedAfter ?? DateTime.MinValue,
                    EndDate = request.CreatedBefore ?? DateTime.MaxValue,
                    StatusFilter = request.Filter // "unindexed", "all", etc.
                },
                estimatedTotalEmails: 0, // Actual count determined when job starts
                cancellationToken);

            // Submit job to queue
            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Submitted bulk RAG indexing job {JobId} for tenant {TenantId}, filter={Filter}, maxDocs={MaxDocs}",
                job.JobId, request.TenantId, request.Filter, request.MaxDocuments);

            var statusUrl = $"/api/ai/rag/admin/bulk-index/{job.JobId}/status";

            return Results.Accepted(
                value: new BulkIndexingResponse
                {
                    JobId = job.JobId,
                    Status = "Pending",
                    Message = $"Bulk indexing job submitted. Use status endpoint to monitor progress.",
                    StatusUrl = statusUrl
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit bulk RAG indexing job for tenant {TenantId}", request.TenantId);
            return Results.Problem(
                title: "Submit Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get status of a bulk RAG indexing job.
    /// </summary>
    private static async Task<IResult> GetBulkIndexingJobStatus(
        string jobId,
        BatchJobStatusStore statusStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Job ID is required",
                Status = 400
            });
        }

        var status = await statusStore.GetJobStatusAsync(jobId, cancellationToken);

        if (status == null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Bulk indexing job '{jobId}' not found",
                Status = 404
            });
        }

        // Map BatchJobStatusResponse to BulkIndexingStatusResponse
        var response = new BulkIndexingStatusResponse
        {
            JobId = status.JobId,
            Status = status.Status.ToString(),
            TotalDocuments = status.TotalEmails, // TotalEmails is reused for documents
            ProcessedCount = status.ProcessedCount,
            ErrorCount = status.ErrorCount,
            SkippedCount = status.SkippedCount,
            PercentComplete = status.ProgressPercent,
            StartedAt = status.StartedAt.HasValue ? new DateTimeOffset(status.StartedAt.Value, TimeSpan.Zero) : null,
            CompletedAt = status.CompletedAt.HasValue ? new DateTimeOffset(status.CompletedAt.Value, TimeSpan.Zero) : null,
            RecentErrors = status.RecentErrors.Select(e => e.Message).ToList()
        };

        return Results.Ok(response);
    }
}

/// <summary>
/// Request model for RAG search.
/// </summary>
public record RagSearchRequest
{
    /// <summary>
    /// The search query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Search options including tenant, filters, and limits.
    /// </summary>
    public required RagSearchOptions Options { get; init; }

    /// <summary>
    /// Optional explicit Azure AI Search index name to target for this request. When
    /// provided (non-null and non-empty), the BFF resolver MUST use this index in place
    /// of the Dataverse / appsettings fallback chain — subject to the allow-list in
    /// <c>appsettings.AiSearch.AllowedIndexes</c>. When omitted (null or empty), the
    /// existing 2-tier resolver chain (<c>sprk_aiknowledgedeployment</c> Dataverse entity
    /// then <c>appsettings.AiSearch.KnowledgeIndexName</c>) is used unchanged.
    /// JSON deserialization is forward-compatible: requests without this field continue
    /// to work as today (FR-BFF-05, NFR-02).
    /// </summary>
    public string? SearchIndexName { get; init; }
}

/// <summary>
/// Query parameters for delete operations.
/// </summary>
public class DeleteDocumentQuery
{
    /// <summary>
    /// Tenant ID for routing to correct index.
    /// </summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// Result of a delete operation.
/// </summary>
public record RagDeleteResult
{
    /// <summary>
    /// Whether any documents were deleted.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Number of documents deleted.
    /// </summary>
    public int Count { get; init; }
}

/// <summary>
/// Request model for embedding generation.
/// </summary>
public record EmbeddingRequest
{
    /// <summary>
    /// The text to generate an embedding for.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Result of embedding generation.
/// </summary>
public record EmbeddingResult
{
    /// <summary>
    /// The generated embedding vector.
    /// </summary>
    public ReadOnlyMemory<float> Embedding { get; init; }

    /// <summary>
    /// Number of dimensions in the embedding.
    /// </summary>
    public int Dimensions { get; init; }
}

/// <summary>
/// Response from enqueue indexing endpoint.
/// </summary>
public record EnqueueIndexingResponse
{
    /// <summary>
    /// Whether the job was accepted for processing.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// The unique job identifier for tracking.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key for deduplication.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request model for bulk RAG indexing.
/// </summary>
public record BulkIndexingRequest
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Filter type: "unindexed" (default), "all".
    /// </summary>
    public string Filter { get; init; } = "unindexed";

    /// <summary>
    /// Optional Matter ID to filter documents.
    /// </summary>
    public string? MatterId { get; init; }

    /// <summary>
    /// Optional: Only index documents created after this date.
    /// </summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>
    /// Optional: Only index documents created before this date.
    /// </summary>
    public DateTime? CreatedBefore { get; init; }

    /// <summary>
    /// Optional document type filter (e.g., ".pdf", ".docx").
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Maximum number of documents to process (default: 1000).
    /// </summary>
    public int MaxDocuments { get; init; } = 1000;

    /// <summary>
    /// Maximum concurrent document processing (default: 5).
    /// </summary>
    public int MaxConcurrency { get; init; } = 5;

    /// <summary>
    /// If true, reindex documents even if they have been indexed before.
    /// </summary>
    public bool ForceReindex { get; init; } = false;
}

/// <summary>
/// Response from bulk indexing submission.
/// </summary>
public record BulkIndexingResponse
{
    /// <summary>
    /// The unique job identifier for tracking.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Status of the job (Pending, InProgress, etc.).
    /// </summary>
    public string Status { get; init; } = "Pending";

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// URL to poll for status updates.
    /// </summary>
    public string StatusUrl { get; init; } = string.Empty;
}

/// <summary>
/// Response for bulk indexing job status.
/// </summary>
public record BulkIndexingStatusResponse
{
    /// <summary>
    /// The unique job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Current job status (Pending, InProgress, Completed, PartiallyCompleted, Failed).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Total number of documents to process.
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Number of documents successfully processed.
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// Number of documents that failed to process.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Number of documents skipped (already indexed).
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Percentage of completion (0-100).
    /// </summary>
    public double PercentComplete { get; init; }

    /// <summary>
    /// When the job was started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When the job was completed (if finished).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Last few errors encountered (for debugging).
    /// </summary>
    public List<string> RecentErrors { get; init; } = [];
}

/// <summary>
/// Request model for send-to-index endpoint (Dataverse ribbon button integration).
/// </summary>
public record SendToIndexRequest
{
    /// <summary>
    /// List of Dataverse document IDs to index.
    /// </summary>
    public required List<string> DocumentIds { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public required string TenantId { get; init; }
}

/// <summary>
/// Response from send-to-index endpoint.
/// </summary>
public record SendToIndexResponse
{
    /// <summary>
    /// Total number of documents requested for indexing.
    /// </summary>
    public int TotalRequested { get; init; }

    /// <summary>
    /// Number of documents successfully indexed.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of documents that failed to index.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Per-document results.
    /// </summary>
    public List<SendToIndexDocumentResult> Results { get; init; } = [];
}

/// <summary>
/// Result for a single document in send-to-index operation.
/// </summary>
public record SendToIndexDocumentResult
{
    /// <summary>
    /// The document ID that was processed.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Whether the indexing succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of chunks indexed (if successful).
    /// </summary>
    public int ChunksIndexed { get; init; }

    /// <summary>
    /// Name of the search index used.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>
    /// Parent entity type if document was associated with an entity.
    /// </summary>
    public string? ParentEntityType { get; init; }

    /// <summary>
    /// Parent entity ID if document was associated with an entity.
    /// </summary>
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// Error message if indexing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
