using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Analysis endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides AI-driven document analysis with configurable actions, scopes, and output types.
/// Uses Server-Sent Events (SSE) for real-time streaming responses.
/// </summary>
public static class AnalysisEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/analysis")
            .RequireAuthorization()
            .WithTags("AI Analysis");

        // POST /api/ai/analysis/create - Create analysis record and associate N:N scopes
        group.MapPost("/create", CreateAnalysis)
            .AddAnalysisExecuteAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("CreateAnalysis")
            .WithSummary("Create analysis record with scope associations")
            .WithDescription("Creates an sprk_analysis record in Dataverse and associates N:N scope items (skills, knowledge, tools). Returns the new analysis ID.")
            .Produces<CreateAnalysisResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/fork - Fork-on-analysis (ai-advanced-capabilities-analysis-hub-r1
        // task 021 / UQ-1 Option B / §6.5 Path A). ONE handler atomically composes existing
        // server-owned seams: create sprk_analysis → mint a new session BOUND to it (task-020
        // sprk_aichatsummary.sprk_analysis FK) → snapshot + archive the prior session. Returns
        // { analysisId, newSessionId, archivedSessionId }; the client only stores newSessionId +
        // shows the warning (server mints session GUIDs, so the fork MUST be server-side).
        group.MapPost("/fork", ForkAnalysis)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("ForkAnalysis")
            .WithSummary("Fork a running chat into a new Analysis + bound session")
            .WithDescription("Atomically creates an sprk_analysis, mints a new chat session bound to it via the sprk_aichatsummary.sprk_analysis FK, and archives + snapshots the prior session (transcript preserved in the Cosmos store-of-record per ADR-040). Returns { analysisId, newSessionId, archivedSessionId }.")
            .Produces<AnalysisForkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/promote - Explicit promotion (ai-advanced-capabilities-analysis-hub-r1
        // task 023 / spec FR-07 / two-tier session model). Lighter sibling of /fork: binds an
        // EXISTING loose session to a NEW sprk_analysis via the task-020 FK — NO new session mint, NO
        // archive. A casual/ad-hoc chat is NEVER auto-promoted; this is the only path that associates
        // a session with an sprk_analysis after the fact.
        group.MapPost("/promote", PromoteSession)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("PromoteSession")
            .WithSummary("Promote a loose chat session into a new, named Analysis")
            .WithDescription("Binds an EXISTING loose (non-Analysis) chat session to a NEW sprk_analysis record via the task-020 sprk_aichatsummary.sprk_analysis FK. This is a BIND on the session's existing Dataverse row — no new session is minted and no prior session is archived (contrast with /fork, task 021). Returns { analysisId, sessionId }.")
            .Produces<AnalysisPromoteResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/execute - Execute new analysis with SSE streaming
        group.MapPost("/execute", ExecuteAnalysis)
            .AddAnalysisExecuteAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ExecuteAnalysis")
            .WithSummary("Execute document analysis with SSE streaming")
            .WithDescription("Executes AI-driven analysis on documents with configurable actions, skills, knowledge, and tools. Returns results via Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        // POST /api/ai/analysis/{analysisId}/save - Save working document to SPE
        group.MapPost("/{analysisId:guid}/save", SaveWorkingDocument)
            .AddAnalysisRecordAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("SaveAnalysisDocument")
            .WithSummary("Save working document to SharePoint Embedded")
            .WithDescription("Saves the analysis working document to SPE and creates a new Document record in Dataverse.")
            .Produces<SavedDocumentResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/{analysisId}/export - Export analysis output
        group.MapPost("/{analysisId:guid}/export", ExportAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("ExportAnalysis")
            .WithSummary("Export analysis to various destinations")
            .WithDescription("Exports analysis output as email, Teams message, PDF, or DOCX.")
            .Produces<ExportResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/ai/analysis/{analysisId} - Get analysis with history
        group.MapGet("/{analysisId:guid}", GetAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .WithName("GetAnalysis")
            .WithSummary("Get analysis record with chat history")
            .WithDescription("Retrieves an analysis record including working document, final output, and chat history.")
            .Produces<AnalysisDetailResult>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    /// <summary>
    /// Create a new analysis record and associate N:N scope items.
    /// POST /api/ai/analysis/create
    /// </summary>
    private static async Task<IResult> CreateAnalysis(
        CreateAnalysisRequest request,
        IAnalysisDataverseService dataverseService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Analysis name is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (request.DocumentId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "A valid documentId is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation(
            "Creating analysis '{Name}' for document {DocumentId} with {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            request.Name, request.DocumentId,
            request.SkillIds.Length, request.KnowledgeIds.Length, request.ToolIds.Length);

        try
        {
            // Step 1: Create the sprk_analysis record
            var analysisId = await dataverseService.CreateAnalysisAsync(
                request.DocumentId,
                request.Name,
                playbookId: request.PlaybookId,
                ct: cancellationToken);

            // Step 2: Associate N:N scope items (skills, knowledge, tools)
            if (request.SkillIds.Length > 0 || request.KnowledgeIds.Length > 0 || request.ToolIds.Length > 0)
            {
                await dataverseService.AssociateScopesAsync(
                    analysisId,
                    request.SkillIds,
                    request.KnowledgeIds,
                    request.ToolIds,
                    cancellationToken);
            }

            logger.LogInformation("Created analysis {AnalysisId} for document {DocumentId}", analysisId, request.DocumentId);

            return Results.Created($"/api/ai/analysis/{analysisId}", new CreateAnalysisResponse(analysisId));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("scope item"))
        {
            // Scope association partially failed — analysis was created but scopes may be incomplete
            logger.LogWarning(ex, "Analysis created but scope association failed for document {DocumentId}", request.DocumentId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Scope Association Failed",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create analysis for document {DocumentId}", request.DocumentId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create analysis record.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Execute a new analysis with SSE streaming.
    /// POST /api/ai/analysis/execute
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>R7 Wave 4 (FR-11)</b>: This endpoint dispatches the canonical
    /// <c>IPlaybookOrchestrationService.ExecuteAsync</c> facade per ADR-013 Invariant 1
    /// (the facade triangle was the canonical AI invocation surface). Task 041 migrated
    /// this endpoint from the legacy direct-invocation path; task 042 deleted the legacy
    /// pipeline entirely (see <c>notes/spikes/executeanalysisasync-caller-audit.md</c>).
    /// </para>
    /// <para>
    /// <b>SSE chunk-shape adapter</b>: <c>IPlaybookOrchestrationService.ExecuteAsync</c>
    /// emits <see cref="PlaybookStreamEvent"/>; Code Page consumers parse <see cref="AnalysisStreamChunk"/>.
    /// <see cref="BridgePlaybookEventToAnalysisChunk"/> maps the playbook event surface onto the
    /// stable client SSE contract (PlaybookId-typed); the <c>[DONE]</c> terminator and post-stream
    /// completion notification semantics are preserved unchanged.
    /// </para>
    /// <para>
    /// <b>Required-PlaybookId</b>: the canonical orchestrator requires a real
    /// <c>sprk_analysisplaybook</c> record in Dataverse. The legacy "raw OpenAI call" path (no
    /// PlaybookId, ActionId only) is rejected with 400 BadRequest. Existing consumers always
    /// supply a PlaybookId per the Analysis Code Page flow (endpoint is feature-gated by
    /// <see cref="AnalysisOptions.Enabled"/>).
    /// </para>
    /// </remarks>
    private static async Task ExecuteAnalysis(
        AnalysisExecuteRequest request,
        IPlaybookOrchestrationService playbookOrchestrationService,
        AnalysisDocumentLoader documentLoader,
        IOptions<AnalysisOptions> options,
        IConsumerRoutingService consumerRouting,
        IActionResolver actionResolver,
        IDocumentTextSource documentTextSource,
        IActionRunner actionRunner,
        IDocumentDataverseService documentDataverseService,
        IPostUploadIndexingEnqueuer indexingEnqueuer,
        NotificationService notificationService,
        IGenericEntityService entityService,
        HttpContext context,
        ILogger<AnalysisOrchestrationService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Check if Analysis feature is enabled
        if (!options.Value.Enabled)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await response.WriteAsJsonAsync(new { error = "Analysis feature is disabled" }, cancellationToken);
            return;
        }

        // Phase 1: Only single document supported
        if (request.DocumentIds.Length > 1 && !options.Value.MultiDocumentEnabled)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Multi-document analysis coming in Phase 2. Currently only single document is supported." }, cancellationToken);
            return;
        }

        // R7 Wave 4 (FR-11): PlaybookId is REQUIRED for the canonical orchestrator path.
        // The legacy raw-OpenAI/ActionId-only path was deleted by task 042 (no transition shim
        // per spec Q6). All Analysis Code Page flows always supply a PlaybookId per the contract.
        if (!request.PlaybookId.HasValue)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new
            {
                error = "PlaybookId is required. The legacy ActionId-only analysis path was removed by R7 FR-11. " +
                        "Provide a PlaybookId referencing an sprk_analysisplaybook record."
            }, cancellationToken);
            return;
        }

        // Set SSE headers + disable response buffering for real-time streaming
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no"; // Disable reverse proxy buffering (nginx/ARR)

        // Disable ASP.NET response buffering so FlushAsync sends immediately
        var bufferingFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        logger.LogInformation(
            "Starting analysis execution for documents [{DocumentIds}], ActionId={ActionId}, PlaybookId={PlaybookId}, TraceId={TraceId}",
            string.Join(",", request.DocumentIds), request.ActionId, request.PlaybookId, context.TraceIdentifier);

        // R7 Wave 12 (2026-07-02): Linear AI Consumer dispatch. When the incoming
        // playbookId is the Document Profile playbook, route through the prompted-executor
        // document-profile pipeline below rather than the Playbook Engine — same client SSE
        // contract, no interpreter tax. Fall-through preserves engine dispatch for all
        // other consumers (Chat / Insight Engine / etc.). See
        // docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md. FR-P3-05
        // (task 044): the consumer-specific wrapper class was absorbed into this endpoint —
        // the pipeline composes the executor primitives directly.
        //
        // FR-P3-01 (spaarke-ai-architecture-redesign-r1 task 040): the LinearConsumers
        // config reverse-lookup was replaced by a compare against the document-profile
        // Binding row's sprk_playbook (single routing surface, ADR-039 / NFR-08). Clients
        // still submit the legacy playbookId; ResolveAsync is cached ~5 min, so the
        // per-request call is fine.
        var documentProfilePlaybookId = await consumerRouting.ResolveAsync(
            ConsumerTypes.DocumentProfile, cancellationToken: cancellationToken);

        if (documentProfilePlaybookId.HasValue &&
            documentProfilePlaybookId.Value == request.PlaybookId.Value)
        {
            if (request.DocumentIds.Length != 1)
            {
                await WriteSSEAsync(response,
                    AnalysisStreamChunk.FromError("Document Profile requires exactly one documentId."),
                    cancellationToken);
                await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                return;
            }

            try
            {
                await foreach (var chunk in ExecuteDocumentProfilePipelineAsync(
                    request.DocumentIds[0], context, parentEntity: null,
                    actionResolver, documentTextSource, actionRunner,
                    documentDataverseService, indexingEnqueuer, logger, cancellationToken))
                {
                    await WriteSSEAsync(response, chunk, cancellationToken);
                }
                await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);

                logger.LogInformation(
                    "Linear Document Profile completed for TraceId={TraceId}", context.TraceIdentifier);

                _ = SendAnalysisCompleteNotificationAsync(
                    context.User, request, notificationService, entityService, logger, context.TraceIdentifier);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation(
                    "Client disconnected during Linear Document Profile, TraceId={TraceId}",
                    context.TraceIdentifier);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Linear Document Profile, TraceId={TraceId}", context.TraceIdentifier);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await WriteSSEAsync(response, AnalysisStreamChunk.FromError(ex.Message), CancellationToken.None);
                }
            }
            return;
        }

        // R7 task 041 (FR-11): Construct PlaybookRunRequest from AnalysisExecuteRequest. Per audit
        // R-040-3, AnalysisId is not part of PlaybookRunRequest (the existing AnalysisRecord is
        // referenced elsewhere by AnalysisOrchestrationService.ExecutePlaybookAsync via
        // AdditionalContext upstream; AnalysisId pass-through review left for follow-on as needed).
        //
        // R7 W12 2026-07-01: Pre-load DocumentContext when a single documentId is supplied.
        // AiAnalysisNodeExecutor.Validate() requires context.Document != null with non-empty
        // ExtractedText; the R2 dispatch refactor removed the AnalysisOrchestrationService
        // legacy path that used to load documents itself. Callers of /api/ai/analysis/execute
        // (e.g., Document Upload wizard's useAiSummary hook) supply documentIds but expect the
        // BFF to load + extract text. Fail-soft: if load fails, we still invoke the orchestrator
        // and let node validation surface a specific error to the client.
        DocumentContext? documentContext = null;
        if (request.DocumentIds.Length == 1)
        {
            var documentId = request.DocumentIds[0].ToString();
            try
            {
                var document = await documentLoader.GetDocumentAsync(documentId, cancellationToken);
                if (document != null)
                {
                    var extractedText = await documentLoader.ExtractDocumentTextAsync(document, context, cancellationToken);
                    documentContext = new DocumentContext
                    {
                        DocumentId = Guid.TryParse(document.Id, out var docGuid) ? docGuid : request.DocumentIds[0],
                        Name = document.Name ?? "Unknown",
                        FileName = document.FileName,
                        ExtractedText = extractedText,
                        // Metadata carries downstream-node inputs. GraphDriveId + GraphItemId are
                        // required by DeliverToIndexNodeExecutor (executortype=41) to enqueue the
                        // SPE-scoped RAG indexing job. Populated when the DocumentEntity has SPE
                        // linkage; nodes that don't need them ignore the entries.
                        Metadata = new Dictionary<string, object?>
                        {
                            ["GraphDriveId"] = document.GraphDriveId,
                            ["GraphItemId"] = document.GraphItemId,
                        },
                    };
                    logger.LogInformation(
                        "Loaded document context for analysis. DocumentId={DocumentId}, TextLength={TextLength}",
                        documentId, extractedText?.Length ?? 0);
                }
                else
                {
                    logger.LogWarning("Document {DocumentId} not found in Dataverse; proceeding without document context.", documentId);
                }
            }
            catch (Exception loadEx)
            {
                logger.LogWarning(loadEx,
                    "Failed to load document context for {DocumentId}; proceeding without it. Node validation may reject the run.",
                    documentId);
            }
        }

        var playbookRequest = new PlaybookRunRequest
        {
            PlaybookId = request.PlaybookId.Value,
            DocumentIds = request.DocumentIds,
            Document = documentContext
        };

        try
        {
            await foreach (var evt in playbookOrchestrationService.ExecuteAsync(playbookRequest, context, cancellationToken))
            {
                var chunk = BridgePlaybookEventToAnalysisChunk(evt, request.PlaybookId.Value);
                if (chunk is null)
                {
                    continue; // Event type has no AnalysisStreamChunk equivalent (e.g., section_* events)
                }
                await WriteSSEAsync(response, chunk, cancellationToken);
            }

            // Send SSE terminator so clients know the stream is complete
            await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);

            logger.LogInformation("Analysis execution completed for TraceId={TraceId}", context.TraceIdentifier);

            // Fire-and-forget: notify requesting user that analysis is complete.
            // Must not block the SSE response per project constraint.
            _ = SendAnalysisCompleteNotificationAsync(
                context.User,
                request,
                notificationService,
                entityService,
                logger,
                context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during analysis execution, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during analysis execution, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Include exception type and inner details for diagnostics
                var errorDetail = $"{ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    errorDetail += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                // Include first relevant stack frame for quick diagnosis
                var stackLines = ex.StackTrace?.Split('\n') ?? [];
                var relevantFrame = stackLines.FirstOrDefault(l => l.Contains("Sprk.Bff.Api"));
                if (relevantFrame != null)
                    errorDetail += $" | At: {relevantFrame.Trim()}";

                var errorChunk = new AnalysisStreamChunk("error", null, true, Error: errorDetail);
                await WriteSSEAsync(response, errorChunk, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// R7 task 041 (FR-11 / audit R-040-1): Map a <see cref="PlaybookStreamEvent"/> onto the
    /// client-facing <see cref="AnalysisStreamChunk"/> SSE wire shape. Preserves the stable
    /// Code Page consumer contract (<c>metadata / progress / chunk / done / error</c> types)
    /// while routing through the canonical playbook orchestrator.
    /// </summary>
    /// <param name="evt">Playbook event emitted by <see cref="IPlaybookOrchestrationService.ExecuteAsync"/>.</param>
    /// <param name="playbookId">Originating playbook ID, used as a stable surrogate for the
    /// analysis metadata when the orchestrator does not provide a discrete <c>sprk_analysis</c>
    /// record ID upstream.</param>
    /// <returns>An <see cref="AnalysisStreamChunk"/> for events with a wire-contract equivalent,
    /// or <c>null</c> for events that have no Code Page consumer impact (e.g., section_*
    /// composite events, NodeStarted/NodeSkipped which do not surface in the existing UI).</returns>
    private static AnalysisStreamChunk? BridgePlaybookEventToAnalysisChunk(
        PlaybookStreamEvent evt,
        Guid playbookId)
    {
        return evt.Type switch
        {
            // RunStarted → emit metadata so the Code Page renders the "analysis starting" frame.
            // PlaybookRunRequest does not surface a sprk_analysis record id at this layer; the
            // RunId substitutes as a stable correlation key (the Code Page treats it opaquely).
            PlaybookEventType.RunStarted =>
                AnalysisStreamChunk.Metadata(evt.RunId, $"playbook:{playbookId}"),

            // NodeProgress carries streamed token content — surface as a chunk.
            PlaybookEventType.NodeProgress when !string.IsNullOrEmpty(evt.Content) =>
                AnalysisStreamChunk.TextChunk(evt.Content!),

            // NodeCompleted with text output — surface as a chunk so partial results render.
            PlaybookEventType.NodeCompleted when evt.NodeOutput?.TextContent is { Length: > 0 } text =>
                AnalysisStreamChunk.TextChunk(text),

            // RunCompleted → terminator chunk with token usage from metrics.
            PlaybookEventType.RunCompleted =>
                AnalysisStreamChunk.Completed(
                    evt.RunId,
                    new TokenUsage(
                        Input: evt.Metrics?.TotalTokensIn ?? 0,
                        Output: evt.Metrics?.TotalTokensOut ?? 0)),

            // RunFailed → error chunk; preserves the existing client error path.
            PlaybookEventType.RunFailed =>
                AnalysisStreamChunk.FromError(evt.Error ?? "Playbook execution failed"),

            // NodeFailed → error chunk (single-node failure terminates the run per orchestrator semantics).
            PlaybookEventType.NodeFailed =>
                AnalysisStreamChunk.FromError(
                    $"Node '{evt.NodeName ?? evt.NodeId?.ToString() ?? "unknown"}' failed: {evt.Error ?? "no detail"}"),

            // RunCancelled — client already saw the OperationCanceledException path or will see
            // the next [DONE]; no chunk to emit (mirrors legacy behavior which had no Cancelled
            // chunk type either).
            PlaybookEventType.RunCancelled => null,

            // Events with no Code Page wire-contract counterpart (NodeStarted, NodeSkipped,
            // UnrenderedTemplateDetected, SectionStarted/Data/Completed). Silently drop —
            // these are observability events the Code Page does not consume today.
            _ => null
        };
    }

    /// <summary>
    /// Save working document to SPE and create Document record.
    /// POST /api/ai/analysis/{analysisId}/save
    /// </summary>
    private static async Task<IResult> SaveWorkingDocument(
        Guid analysisId,
        AnalysisSaveRequest request,
        IAnalysisOrchestrationService orchestrationService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Saving working document for analysis {AnalysisId}, FileName={FileName}, Format={Format}",
            analysisId, request.FileName, request.Format);

        try
        {
            var result = await orchestrationService.SaveWorkingDocumentAsync(analysisId, request, cancellationToken);

            logger.LogInformation("Saved document {DocumentId} for analysis {AnalysisId}",
                result.DocumentId, analysisId);

            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Export analysis output to various destinations.
    /// POST /api/ai/analysis/{analysisId}/export
    /// </summary>
    private static async Task<IResult> ExportAnalysis(
        Guid analysisId,
        AnalysisExportRequest request,
        IAnalysisOrchestrationService orchestrationService,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        // Validate export format is enabled
        var analysisOptions = options.Value;
        var validationError = ValidateExportFormat(request.Format, analysisOptions);
        if (validationError != null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        logger.LogInformation("Exporting analysis {AnalysisId} to {Format}",
            analysisId, request.Format);

        try
        {
            var result = await orchestrationService.ExportAnalysisAsync(analysisId, request, cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning("Export failed for analysis {AnalysisId}: {Error}",
                    analysisId, result.Error);
                return Results.BadRequest(new { error = result.Error ?? "Export failed" });
            }

            // File formats (Docx, Pdf) return bytes for direct browser download.
            if (result.FileBytes != null)
            {
                logger.LogInformation("Streaming {Format} export for analysis {AnalysisId}: {Size} bytes",
                    request.Format, analysisId, result.FileBytes.Length);
                return Results.File(
                    result.FileBytes,
                    result.FileContentType ?? "application/octet-stream",
                    result.FileName ?? $"analysis.{request.Format.ToString().ToLowerInvariant()}");
            }

            // Other formats (Email, Teams) return JSON result.
            logger.LogInformation("Exported analysis {AnalysisId} to {Format}: Status={Status}",
                analysisId, request.Format, result.Details?.Status);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get analysis record with chat history.
    /// GET /api/ai/analysis/{analysisId}
    /// </summary>
    private static async Task<IResult> GetAnalysis(
        Guid analysisId,
        IAnalysisOrchestrationService orchestrationService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Retrieving analysis {AnalysisId}", analysisId);

        try
        {
            var result = await orchestrationService.GetAnalysisAsync(analysisId, cancellationToken);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
    }

    /// <summary>
    /// Sends an in-app notification after AI analysis completes successfully.
    /// Runs as fire-and-forget so the SSE response is not blocked.
    /// </summary>
    private static async Task SendAnalysisCompleteNotificationAsync(
        ClaimsPrincipal user,
        AnalysisExecuteRequest request,
        NotificationService notificationService,
        IGenericEntityService entityService,
        ILogger logger,
        string traceIdentifier)
    {
        try
        {
            var dataverseUserId = await ResolveDataverseUserIdAsync(user, entityService, logger);
            if (!dataverseUserId.HasValue)
            {
                logger.LogWarning(
                    "Cannot send analysis-complete notification — unable to resolve Dataverse user, TraceId={TraceId}",
                    traceIdentifier);
                return;
            }

            // Retirement task 060 (spec §13.5 / FR-18): repoints this notification's
            // actionUrl from the now-retired legacy Analysis Workspace code page to the
            // SpaarkeAi three-pane surface (sprk_spaarkeai), mirroring the openSpaarkeAi
            // deep-link shape (launch-resolver.ts SpaarkeAiLaunchParams). Prefer analysisId
            // when the request is bound to an existing sprk_analysis record (task 052 entry
            // case 2d/2c semantics — existing analysis, no Create-new cards); fall back to
            // document entity context (entityLogicalName=sprk_document + entityId) when no
            // analysis record is bound yet, so the notification is still actionable. Guids
            // Empty is treated the same as null (mirrors BuildDocumentUploadWizardUrl(Guid?)'s
            // established degrade-to-unscoped convention elsewhere in this codebase).
            var documentId = request.DocumentIds.FirstOrDefault();
            string? actionUrl = request.AnalysisId is { } boundAnalysisId && boundAnalysisId != Guid.Empty
                ? BuildSpaarkeAiActionUrl(new Dictionary<string, string>
                {
                    ["analysisId"] = boundAnalysisId.ToString()
                })
                : documentId != Guid.Empty
                    ? BuildSpaarkeAiActionUrl(new Dictionary<string, string>
                    {
                        ["entityLogicalName"] = "sprk_document",
                        ["entityId"] = documentId.ToString()
                    })
                    : null;

            var aiMetadata = new Dictionary<string, object?>
            {
                ["analysisType"] = "document-analysis",
                ["documentCount"] = request.DocumentIds.Length,
                ["actionId"] = request.ActionId?.ToString(),
                ["confidence"] = "ai-generated"
            };

            await notificationService.CreateNotificationAsync(
                userId: dataverseUserId.Value,
                title: "AI analysis complete",
                body: $"Analysis results are ready for your document ({request.DocumentIds.Length} document(s) analyzed).",
                category: "analysis",
                actionUrl: actionUrl,
                regardingId: documentId != Guid.Empty ? documentId : null,
                aiMetadata: aiMetadata);

            logger.LogDebug(
                "Sent analysis-complete notification to user {UserId}, TraceId={TraceId}",
                dataverseUserId.Value, traceIdentifier);
        }
        catch (Exception ex)
        {
            // Log but never throw — notification failure must not affect the caller.
            logger.LogError(
                ex,
                "Failed to send analysis-complete notification, TraceId={TraceId} — {ErrorMessage}",
                traceIdentifier, ex.Message);
        }
    }

    /// <summary>
    /// Builds a relative deep-link to the SpaarkeAi three-pane surface (<c>sprk_spaarkeai</c>),
    /// mirroring the <c>openSpaarkeAi</c> query-param shape (<c>launch-resolver.ts</c>
    /// <c>buildLaunchUrl</c>) so an in-app notification action opens the same surface the
    /// client-side launcher would produce. Retirement task 060 (spec §13.5 / FR-18) repoints
    /// this from the now-retired legacy Analysis Workspace code page. Relative (no Dataverse
    /// base URL) to match the existing notification actionUrl convention used elsewhere in
    /// this file and in <c>WorkAssignmentEndpoints</c>.
    /// </summary>
    private static string BuildSpaarkeAiActionUrl(Dictionary<string, string> parameters)
    {
        var dataString = string.Join("&",
            parameters.Select(kvp => $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));
        var encodedData = HttpUtility.UrlEncode(dataString);

        return $"/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai&data={encodedData}";
    }

    /// <summary>
    /// Resolves the Dataverse systemuserid from the Azure AD OID in the user's claims.
    /// Dataverse systemuserid != Azure AD oid; they are linked via azureactivedirectoryobjectid.
    /// </summary>
    private static async Task<Guid?> ResolveDataverseUserIdAsync(
        ClaimsPrincipal user,
        IGenericEntityService entityService,
        ILogger logger)
    {
        var oidClaim = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrEmpty(oidClaim) || !Guid.TryParse(oidClaim, out var azureAdOid))
        {
            logger.LogWarning("No valid Azure AD OID found in user claims for notification lookup");
            return null;
        }

        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("azureactivedirectoryobjectid", ConditionOperator.Equal, azureAdOid)
                }
            }
        };

        var result = await entityService.RetrieveMultipleAsync(query, CancellationToken.None);
        if (result.Entities.Count == 0)
        {
            logger.LogWarning("No Dataverse systemuser found for Azure AD OID {AzureAdOid}", azureAdOid);
            return null;
        }

        return result.Entities[0].Id;
    }

    /// <summary>
    /// Validate export format against enabled options.
    /// </summary>
    private static string? ValidateExportFormat(ExportFormat format, AnalysisOptions options)
    {
        return format switch
        {
            ExportFormat.Email when !options.EnableEmailExport => "Email export is disabled",
            ExportFormat.Teams when !options.EnableTeamsExport => "Teams export is disabled",
            ExportFormat.Pdf when !options.EnablePdfExport => "PDF export is disabled",
            ExportFormat.Docx when !options.EnableDocxExport => "DOCX export is disabled",
            _ => null
        };
    }

    /// <summary>
    /// Write a chunk in SSE format: "data: {json}\n\n"
    /// </summary>
    private static async Task WriteSSEAsync(
        HttpResponse response,
        AnalysisStreamChunk chunk,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    // =========================================================================
    // Document Profile pipeline (FR-P3-05 wrapper absorption — task 044)
    // =========================================================================

    /// <summary>
    /// Executes the Document Profile flow on the prompted executor: resolve the
    /// document-profile Binding row's Action, extract the document text (SPE download +
    /// Redis ETag cache), run the LLM via the Action's prompt + schema, persist the
    /// mapped fields via the SDK-based document service, and enqueue RAG indexing
    /// (best-effort). Behavior preserved verbatim from the deleted wrapper class;
    /// emits <see cref="AnalysisStreamChunk"/> events so the endpoint writes SSE
    /// identically to the engine path.
    /// </summary>
    private static async IAsyncEnumerable<AnalysisStreamChunk> ExecuteDocumentProfilePipelineAsync(
        Guid documentId,
        HttpContext httpContext,
        ParentEntityContext? parentEntity,
        IActionResolver actionResolver,
        IDocumentTextSource textSource,
        IActionRunner actionRunner,
        IDocumentDataverseService documentService,
        IPostUploadIndexingEnqueuer indexingEnqueuer,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return AnalysisStreamChunk.Metadata(documentId, $"document-profile:{documentId}");

        // Build the per-request context. TenantId is required for RAG indexing —
        // resolve it from the caller's JWT (same claim path AnalysisDocumentLoader uses).
        var tenantId = httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.DocumentProfile,
            CorrelationId = httpContext.TraceIdentifier,
            TenantId = tenantId,
        };

        // Step 1: Resolve the Action row (SystemPrompt + OutputSchemaJson + Temperature).
        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        AnalysisAction? action = null;
        string? actionError = null;
        try
        {
            action = await actionResolver.ResolveAsync(ConsumerTypes.DocumentProfile, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Document Profile action");
            actionError = $"Failed to resolve action: {ex.Message}";
        }
        if (actionError != null)
        {
            yield return AnalysisStreamChunk.FromError(actionError);
            yield break;
        }

        // Step 2: Extract document text (SPE download + text extraction with Redis ETag cache).
        yield return AnalysisStreamChunk.Progress("extracting_text", "Extracting document text…");
        DocumentText? docText = null;
        string? textError = null;
        try
        {
            docText = await textSource.ExtractFromDocumentIdAsync(documentId, httpContext, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract text for document {DocumentId}", documentId);
            textError = $"Failed to extract document text: {ex.Message}";
        }
        if (textError != null)
        {
            yield return AnalysisStreamChunk.FromError(textError);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(docText!.ExtractedText))
        {
            logger.LogWarning("Document {DocumentId} has no extractable text; skipping profile", documentId);
            yield return AnalysisStreamChunk.FromError("Document has no extractable text.");
            yield break;
        }

        // Step 3: Run the LLM via the Action's prompt + schema.
        yield return AnalysisStreamChunk.Progress("calling_llm", "Analyzing document with AI…");
        JsonElement aiOutput = default;
        string? llmError = null;
        try
        {
            aiOutput = await actionRunner.RunAsync(action!, docText, runContext, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM call failed for document {DocumentId}", documentId);
            llmError = $"AI analysis failed: {ex.Message}";
        }
        if (llmError != null)
        {
            yield return AnalysisStreamChunk.FromError(llmError);
            yield break;
        }

        // Emit the summary text as a chunk so the client's summary display area renders
        // the actual content (matches the engine path's streaming-tokens behavior).
        // Prefer sprk_filesummary; fall back to sprk_filetldr.
        if (TryGetStringProperty(aiOutput, "sprk_filesummary", out var summaryText)
            || TryGetStringProperty(aiOutput, "sprk_filetldr", out summaryText))
        {
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                yield return AnalysisStreamChunk.TextChunk(summaryText);
            }
        }

        // Step 4: Build Dataverse field mapping directly from the AI output (the output
        // schema uses sprk_* property names natively — typed Choice coercion for
        // sprk_documenttype + a computed search profile).
        var fields = BuildDocumentProfileFields(aiOutput, docText.FileName, parentEntity, logger);

        logger.LogInformation(
            "Document {DocumentId} profile — mapped {FieldCount} fields for update: {Fields}",
            documentId, fields.Count, string.Join(",", fields.Keys));

        // Step 5: Persist via the SDK-based document service (no PATCH construction).
        yield return AnalysisStreamChunk.Progress("updating_record", "Updating document record…");
        string? updateError = null;
        try
        {
            await documentService.UpdateDocumentFieldsAsync(documentId.ToString(), fields, cancellationToken);
            logger.LogInformation(
                "Updated document {DocumentId} with {FieldCount} profile fields",
                documentId, fields.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update document {DocumentId} with profile fields", documentId);
            updateError = $"Failed to update document record: {ex.Message}";
        }
        if (updateError != null)
        {
            yield return AnalysisStreamChunk.FromError(updateError);
            yield break;
        }

        // Step 6: Enqueue RAG indexing (best-effort — failure is logged but non-fatal).
        yield return AnalysisStreamChunk.Progress("enqueuing_indexing", "Queuing document for search indexing…");
        await TryEnqueueDocumentProfileIndexingAsync(
            documentId, docText, tenantId, parentEntity, indexingEnqueuer, httpContext, logger, cancellationToken);

        // Terminator — clients look for Type="done" then [DONE] SSE terminator to close.
        yield return AnalysisStreamChunk.Completed(
            documentId,
            new TokenUsage(Input: 0, Output: 0));
    }

    private static async Task<bool> TryEnqueueDocumentProfileIndexingAsync(
        Guid documentId,
        DocumentText docText,
        string? tenantId,
        ParentEntityContext? parentEntity,
        IPostUploadIndexingEnqueuer indexingEnqueuer,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(docText.GraphDriveId) || string.IsNullOrEmpty(docText.GraphItemId))
        {
            logger.LogInformation(
                "Skipping RAG indexing for document {DocumentId}: missing SPE identifiers", documentId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning(
                "Skipping RAG indexing for document {DocumentId}: no tenantId in caller claims", documentId);
            return false;
        }

        var request = new PostUploadIndexingRequest(
            TenantId: tenantId,
            DriveId: docText.GraphDriveId,
            ItemId: docText.GraphItemId,
            FileName: docText.FileName,
            FileSizeBytes: null,
            ContentType: null,
            DocumentId: documentId.ToString(),
            ParentEntity: parentEntity,
            SearchIndexName: null,
            Source: "LinearDocumentProfile",
            CorrelationId: httpContext.TraceIdentifier);

        try
        {
            var result = await indexingEnqueuer.EnqueueIfApplicableAsync(request, httpContext, cancellationToken);
            return result.JobSubmitted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RAG indexing enqueue threw for document {DocumentId}", documentId);
            return false;
        }
    }

    /// <summary>
    /// Case-insensitive top-level string property accessor for the AI structured
    /// output. Returns false when the property is absent or not a string.
    /// </summary>
    private static bool TryGetStringProperty(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                value = prop.Value.GetString();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Directly maps the AI structured output (whose top-level property names are the
    /// target sprk_document field names per the Action's <c>sprk_outputschemajson</c>)
    /// into a Dataverse-ready field dictionary. Special handling:
    /// <c>sprk_documenttype</c> → Choice coercion via <see cref="DocumentTypeMapper"/>;
    /// arrays/objects → JSON blobs; <c>sprk_searchprofile</c> computed via
    /// <see cref="DocumentProfileFieldMapper.BuildSearchProfile"/>.
    /// </summary>
    private static Dictionary<string, object?> BuildDocumentProfileFields(
        JsonElement root,
        string fileName,
        ParentEntityContext? parentEntity,
        ILogger logger)
        => DocumentProfileOutputMapper.BuildFields(root, fileName, parentEntity, logger);

    // =========================================================================
    // Fork-on-analysis (task 021 / UQ-1 Option B / §6.5 Path A)
    // =========================================================================

    /// <summary>Logical name of the Analysis anchor entity — used for compensation delete.</summary>
    private const string AnalysisEntityLogicalName = "sprk_analysis";

    /// <summary>
    /// HostContext.EntityType sentinel that flags an Analysis-owned chat session. Setting this on
    /// the new session's <see cref="ChatHostContext"/> is what makes
    /// <c>ChatDataverseRepository.CreateSessionAsync</c> write the <c>sprk_aichatsummary.sprk_analysis</c>
    /// lookup FK (task 020, spec FR-05). MUST match <c>ChatDataverseRepository.AnalysisHostContextEntityType</c>.
    /// </summary>
    private const string AnalysisHostContextEntityType = "sprk_analysisoutput";

    /// <summary>
    /// Fork-on-analysis. <c>POST /api/ai/analysis/fork</c>.
    ///
    /// <para>
    /// Composes existing server-owned seams in ONE handler (ADR-013 — no <c>Services/Ai/</c>
    /// orchestration fork; consumes <see cref="IAnalysisDataverseService"/>,
    /// <see cref="ChatSessionManager"/>, <see cref="IChatDataverseRepository"/>):
    /// </para>
    /// <list type="number">
    ///   <item><b>Snapshot + verify prior</b> — <see cref="ChatSessionManager.GetSessionAsync"/>
    ///     materialises the prior transcript into the Cosmos store-of-record (ADR-040 Path A) and
    ///     confirms existence <i>before</i> any write, so a missing prior can never orphan a new
    ///     Analysis.</item>
    ///   <item><b>Create Analysis</b> — <see cref="IAnalysisDataverseService.CreateAnalysisAsync"/>.</item>
    ///   <item><b>Mint bound session</b> — <see cref="ChatSessionManager.CreateSessionAsync"/> with
    ///     an Analysis <see cref="ChatHostContext"/> so the task-020 <c>sprk_analysis</c> FK write
    ///     fires. If the mint throws, the Analysis is compensated (deleted) — no orphan.</item>
    ///   <item><b>Archive prior</b> — <see cref="IChatDataverseRepository.ArchiveSessionAsync"/>
    ///     (the durable <c>sprk_isarchived</c> flip is task 022's scope, AIPL-054). We deliberately
    ///     do NOT call <see cref="ChatSessionManager.DeleteSessionAsync"/> — that hard-deletes the
    ///     Cosmos transcript (GDPR erasure path) and would LOSE the prior transcript, both the
    ///     forbidden "archived-but-transcript-lost" orphan and an ADR-040 violation.</item>
    /// </list>
    ///
    /// <para>
    /// Partial-failure model mirrors <c>CreateSessionAsync</c> (Redis authoritative; a Dataverse
    /// write failure is tolerated, not fatal): the FK bind failing inside the mint leaves the new
    /// session live in Redis (bound in-memory), not orphaned; the archive-marker failing leaves the
    /// fork durable with the transcript preserved. The only compensating rollback is deleting the
    /// Analysis when the mint itself throws.
    /// </para>
    /// </summary>
    private static async Task<IResult> ForkAnalysis(
        AnalysisForkRequest request,
        IAnalysisDataverseService analysisService,
        ChatSessionManager sessionManager,
        IChatDataverseRepository chatRepository,
        IGenericEntityService entityService,
        HttpContext httpContext,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        // ---- Validate ----
        if (request is null || string.IsNullOrWhiteSpace(request.PriorSessionId))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request", "A priorSessionId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request", "Analysis name is required.");
        }
        if (request.DocumentId == Guid.Empty)
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request", "A valid documentId is required.");
        }

        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                "Tenant ID not found in token claims (tid).");
        }

        var correlationId = httpContext.TraceIdentifier;

        // ---- Step 1: snapshot + verify the prior session (read-only — NO mutation yet) ----
        var priorSession = await sessionManager.GetSessionAsync(tenantId, request.PriorSessionId, cancellationToken);
        if (priorSession is null)
        {
            // Nothing has been written yet — a missing/expired prior cannot orphan anything.
            return Results.NotFound(new { error = "Prior session not found", correlationId });
        }
        var priorMessageCount = priorSession.Messages?.Count ?? 0;

        // ---- Step 2: create the new Analysis anchor ----
        Guid analysisId;
        try
        {
            analysisId = await analysisService.CreateAnalysisAsync(
                request.DocumentId, request.Name, playbookId: request.PlaybookId, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Fork: failed to create Analysis for document {DocumentId} (corr={CorrelationId})",
                request.DocumentId, correlationId);
            return AnalysisProblem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to create the analysis record.");
        }

        // ---- Step 3: mint the new session BOUND to the Analysis (task-020 FK write fires) ----
        ChatSession newSession;
        try
        {
            var analysisHostContext = new ChatHostContext(
                EntityType: AnalysisHostContextEntityType,
                EntityId: analysisId.ToString(),
                EntityName: request.Name,
                WorkspaceType: request.HostContext?.WorkspaceType,
                PageType: request.HostContext?.PageType);

            newSession = await sessionManager.CreateSessionAsync(
                tenantId,
                request.DocumentId.ToString(),
                request.PlaybookId,
                analysisHostContext,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Compensation: the Analysis was created but the new session could not be minted
            // (e.g. Redis hot-cache unavailable — CreateSessionAsync tolerates a Dataverse write
            // failure but not a cache-write failure). Roll back the Analysis so no dangling anchor
            // remains. Compensation uses CancellationToken.None so client abort cannot skip cleanup.
            logger.LogError(ex,
                "Fork: new-session mint failed after Analysis {AnalysisId} create — compensating by deleting the Analysis (corr={CorrelationId})",
                analysisId, correlationId);
            try
            {
                await entityService.DeleteAsync(AnalysisEntityLogicalName, analysisId, CancellationToken.None);
            }
            catch (Exception compensationEx)
            {
                logger.LogError(compensationEx,
                    "Fork: COMPENSATION FAILED — Analysis {AnalysisId} may be orphaned; manual cleanup required (corr={CorrelationId})",
                    analysisId, correlationId);
            }
            return AnalysisProblem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to create the forked chat session.");
        }

        // ---- Step 4: archive the prior session (transcript-preserving; see method remarks) ----
        try
        {
            await chatRepository.ArchiveSessionAsync(tenantId, request.PriorSessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal — the fork is already durable (Analysis + new bound session exist) and the
            // prior transcript is preserved in the Cosmos store-of-record. The durable archived-flag
            // flip is task 022 (AIPL-054); the archive-marker call is best-effort here.
            logger.LogWarning(ex,
                "Fork: prior-session archive-marker write failed for the archived session — fork already durable " +
                "(analysis={AnalysisId} newSession={NewSessionId}); transcript preserved; durable archived-flag is task 022 (corr={CorrelationId})",
                analysisId, newSession.SessionId, correlationId);
        }

        logger.LogInformation(
            "Fork complete: analysis={AnalysisId} newSession={NewSessionId} archivedPriorMsgs={PriorMessageCount} (corr={CorrelationId})",
            analysisId, newSession.SessionId, priorMessageCount, correlationId);

        return Results.Created(
            $"/api/ai/analysis/{analysisId}",
            new AnalysisForkResponse(analysisId, newSession.SessionId, request.PriorSessionId));
    }

    // =========================================================================
    // Explicit promotion (task 023 / spec FR-07 / two-tier session model)
    // =========================================================================

    /// <summary>
    /// Explicit promotion. <c>POST /api/ai/analysis/promote</c>.
    ///
    /// <para>
    /// Lighter sibling of <see cref="ForkAnalysis"/>: BIND-only, no mint, no archive. Composes the
    /// SAME two seams the fork endpoint uses (<see cref="IAnalysisDataverseService.CreateAnalysisAsync"/>
    /// + the task-020 FK write, here via <see cref="ChatSessionManager.PromoteSessionToAnalysisAsync"/>),
    /// but never creates a second session and never archives the original — the loose session simply
    /// BECOMES Analysis-owned in place, keeping its session id.
    /// </para>
    /// <list type="number">
    ///   <item><b>Fetch + verify</b> — <see cref="ChatSessionManager.GetSessionAsync"/> confirms the
    ///     loose session exists and is not already Analysis-owned, before any write.</item>
    ///   <item><b>Create Analysis</b> — <see cref="IAnalysisDataverseService.CreateAnalysisAsync"/>,
    ///     document-anchored (the request's <c>documentId</c> or the session's own).</item>
    ///   <item><b>Bind in place</b> — <see cref="ChatSessionManager.PromoteSessionToAnalysisAsync"/>
    ///     writes the <c>sprk_analysis</c> FK onto the EXISTING <c>sprk_aichatsummary</c> row and
    ///     updates the session's <see cref="ChatHostContext"/>. If the bind throws or the session
    ///     vanished, the Analysis is compensated (deleted) — no orphan, same discipline as the fork.</item>
    /// </list>
    /// </summary>
    private static async Task<IResult> PromoteSession(
        AnalysisPromoteRequest request,
        IAnalysisDataverseService analysisService,
        ChatSessionManager sessionManager,
        IGenericEntityService entityService,
        HttpContext httpContext,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        // ---- Validate ----
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request", "A sessionId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                "A name is required to promote a session to an Analysis.");
        }

        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                "Tenant ID not found in token claims (tid).");
        }

        var correlationId = httpContext.TraceIdentifier;

        // ---- Step 1: fetch + verify the loose session (read-only — NO mutation yet) ----
        var session = await sessionManager.GetSessionAsync(tenantId, request.SessionId, cancellationToken);
        if (session is null)
        {
            // Nothing has been written yet — a missing/expired session cannot orphan anything.
            return Results.NotFound(new { error = "Session not found", correlationId });
        }

        // Guard: a session already bound to an Analysis MUST NOT be promoted again — promotion is a
        // ONE-TIME bind (re-promoting would silently re-parent the session away from its first
        // Analysis, orphaning that FK history). Re-run against the SAME convention CreateSessionAsync
        // checks at create time (task 020).
        if (session.HostContext?.EntityType == AnalysisHostContextEntityType)
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                "This session is already bound to an Analysis and cannot be promoted again.");
        }

        // ---- Resolve the anchor: a regarding target (FR-D9) and/or a document ----
        // FR-D9 ("Set related record"): promotion may associate the new Analysis to an EXISTING
        // matter/project (ADR-024 regarding) so it surfaces on that record's Analyses tab, OR anchor it
        // to a document (the "regarding = document" path, via sprk_documentid). At least one is required.
        AnalysisRegardingTarget? regarding = null;
        if (!string.IsNullOrWhiteSpace(request.RegardingEntityType) || request.RegardingEntityId is not null)
        {
            // Both parts must be present; the entity type is a closed set (matter | project) — a document
            // association is expressed via DocumentId, not here.
            var entityType = request.RegardingEntityType?.Trim().ToLowerInvariant();
            if (entityType is not ("sprk_matter" or "sprk_project") ||
                request.RegardingEntityId is not { } regId || regId == Guid.Empty)
            {
                return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                    "A regarding association requires regardingEntityType ('sprk_matter' or 'sprk_project') and a non-empty regardingEntityId.");
            }
            regarding = new AnalysisRegardingTarget(entityType, regId, request.RegardingEntityName);
        }

        // Document anchor: prefer the request's explicit documentId; fall back to the session's own
        // DocumentId (set at session-create time from the chat's attached document, if any). Now OPTIONAL
        // — a document-less analysis is valid when a regarding target was supplied (FR-D9 relaxation).
        Guid? documentId = null;
        if (request.DocumentId is { } requestDocId && requestDocId != Guid.Empty)
        {
            documentId = requestDocId;
        }
        else if (!string.IsNullOrEmpty(session.DocumentId) &&
                 Guid.TryParse(session.DocumentId, out var sessionDocId) &&
                 sessionDocId != Guid.Empty)
        {
            documentId = sessionDocId;
        }

        if (documentId is null && regarding is null)
        {
            return AnalysisProblem(StatusCodes.Status400BadRequest, "Bad Request",
                "This session has no associated document; promoting to an Analysis requires a documentId or a regarding target (matter/project).");
        }

        // ---- Step 2: create the new Analysis anchor ----
        Guid analysisId;
        try
        {
            analysisId = await analysisService.CreateAnalysisAsync(
                documentId, request.Name, playbookId: request.PlaybookId ?? session.PlaybookId,
                regarding: regarding, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Promote: failed to create Analysis for session {SessionId} document {DocumentId} (corr={CorrelationId})",
                request.SessionId, documentId, correlationId);
            return AnalysisProblem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to create the analysis record.");
        }

        // ---- Step 3: bind the EXISTING session to the new Analysis (no new session minted) ----
        ChatSession? promoted;
        try
        {
            promoted = await sessionManager.PromoteSessionToAnalysisAsync(
                tenantId, request.SessionId, analysisId, request.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Promote: bind failed after Analysis {AnalysisId} create for session {SessionId} — compensating by deleting the Analysis (corr={CorrelationId})",
                analysisId, request.SessionId, correlationId);
            await CompensatePromoteAnalysisDeleteAsync(entityService, analysisId, correlationId, logger);
            return AnalysisProblem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to bind the session to the new analysis.");
        }

        if (promoted is null)
        {
            // The session disappeared between the read (Step 1) and the bind (Step 3) — e.g. a Redis
            // eviction racing the request. Compensate the Analysis so no dangling anchor remains
            // (same no-orphan discipline as ForkAnalysis's mint-failure compensation).
            logger.LogWarning(
                "Promote: session {SessionId} disappeared before bind — compensating by deleting Analysis {AnalysisId} (corr={CorrelationId})",
                request.SessionId, analysisId, correlationId);
            await CompensatePromoteAnalysisDeleteAsync(entityService, analysisId, correlationId, logger);
            return Results.NotFound(new { error = "Session not found", correlationId });
        }

        logger.LogInformation(
            "Promote complete: analysis={AnalysisId} session={SessionId} (corr={CorrelationId})",
            analysisId, request.SessionId, correlationId);

        return Results.Created(
            $"/api/ai/analysis/{analysisId}",
            new AnalysisPromoteResponse(analysisId, request.SessionId));
    }

    /// <summary>Compensating delete for the promote endpoint — mirrors the fork endpoint's rollback.</summary>
    private static async Task CompensatePromoteAnalysisDeleteAsync(
        IGenericEntityService entityService, Guid analysisId, string correlationId, ILogger logger)
    {
        try
        {
            await entityService.DeleteAsync(AnalysisEntityLogicalName, analysisId, CancellationToken.None);
        }
        catch (Exception compensationEx)
        {
            logger.LogError(compensationEx,
                "Promote: COMPENSATION FAILED — Analysis {AnalysisId} may be orphaned; manual cleanup required (corr={CorrelationId})",
                analysisId, correlationId);
        }
    }

    /// <summary>Builds a canonical ProblemDetails result for the fork endpoint.</summary>
    private static IResult AnalysisProblem(int statusCode, string title, string detail) => Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: detail,
        type: statusCode == StatusCodes.Status400BadRequest
            ? "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            : "https://tools.ietf.org/html/rfc7231#section-6.6.1");

    /// <summary>
    /// Extracts the tenant ID from the JWT <c>tid</c> claim (ADR-014).
    /// Tenant comes from the caller's authenticated principal and from nothing else (task 059 — see Infrastructure/Authentication/TenantResolution).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        return tenantId;
    }
}

/// <summary>
/// SSE stream chunk for analysis execution.
/// </summary>
/// <param name="Type">Event type: "metadata", "chunk", "done", "error"</param>
/// <param name="Content">Text content for chunk events.</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="AnalysisId">Analysis record ID (set on metadata and done events).</param>
/// <param name="DocumentName">Source document name (set on metadata event).</param>
/// <param name="TokenUsage">Token usage statistics (set on done event).</param>
/// <param name="Error">Error message (set on error event).</param>
/// <param name="PartialStorage">Whether storage partially succeeded (outputs in sprk_analysisoutput but field mapping failed). Set on done event for Document Profile.</param>
/// <param name="StorageMessage">User-friendly message about storage result. Set when PartialStorage is true.</param>
/// <param name="Step">Pipeline step identifier for progress events (e.g. "extracting_text"). Set on progress events.</param>
public record AnalysisStreamChunk(
    string Type,
    string? Content,
    bool Done,
    Guid? AnalysisId = null,
    string? DocumentName = null,
    TokenUsage? TokenUsage = null,
    string? Error = null,
    bool? PartialStorage = null,
    string? StorageMessage = null,
    string? Step = null)
{
    public static AnalysisStreamChunk Metadata(Guid analysisId, string documentName) =>
        new("metadata", null, false, AnalysisId: analysisId, DocumentName: documentName);

    public static AnalysisStreamChunk TextChunk(string content) =>
        new("chunk", content, false);

    public static AnalysisStreamChunk Completed(
        Guid analysisId,
        TokenUsage tokenUsage,
        bool? partialStorage = null,
        string? storageMessage = null) =>
        new("done", null, true,
            AnalysisId: analysisId,
            TokenUsage: tokenUsage,
            PartialStorage: partialStorage,
            StorageMessage: storageMessage);

    public static AnalysisStreamChunk FromError(string error) =>
        new("error", null, true, Error: error);

    public static AnalysisStreamChunk Progress(string step, string message) =>
        new("progress", message, false, Step: step);

    /// <summary>Final structured result payload (used by non-analysis SSE endpoints e.g. summarize).</summary>
    public static AnalysisStreamChunk Result(string jsonContent) =>
        new("result", jsonContent, false);
}

/// <summary>
/// Token usage statistics for an analysis.
/// </summary>
public record TokenUsage(int Input, int Output);

/// <summary>
/// Detailed analysis result including chat history.
/// </summary>
public record AnalysisDetailResult
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = string.Empty;
    public AnalysisActionInfo Action { get; init; } = null!;
    public string Status { get; init; } = string.Empty;
    public string? WorkingDocument { get; init; }
    public string? FinalOutput { get; init; }
    public ChatMessageInfo[] ChatHistory { get; init; } = [];
    public TokenUsage? TokenUsage { get; init; }
    public DateTime? StartedOn { get; init; }
    public DateTime? CompletedOn { get; init; }
}

/// <summary>
/// Analysis action info for response.
/// </summary>
public record AnalysisActionInfo(Guid Id, string Name);

/// <summary>
/// Chat message info for response.
/// </summary>
public record ChatMessageInfo(string Role, string Content, DateTime Timestamp);
