using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Admin;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Endpoints;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Extension methods for mapping all API endpoint groups (post-Build phase).
/// Extracts health, domain, and fallback endpoint registrations from Program.cs.
/// </summary>
public static class EndpointMappingExtensions
{
    /// <summary>
    /// Maps all endpoint groups: health, domain endpoints, and SPA fallback.
    /// </summary>
    /// <remarks>
    /// Debug endpoints (/debug/*) were removed per Spaarke Auth v2 hardening (task 043 / audit C-2).
    /// Do not add new /debug/* routes; use structured logging + Application Insights for diagnostics.
    /// </remarks>
    public static void MapSpaarkeEndpoints(this WebApplication app)
    {
        MapHealthEndpoints(app);
        MapDomainEndpoints(app);
        MapSpaFallback(app);
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        // Anonymous client config endpoint — MSAL bootstrap fallback for direct URL access (AIPU-091)
        app.MapMsalConfigEndpoints();

        app.MapHealthChecks("/healthz").AllowAnonymous();

        app.MapGet("/healthz/dataverse", TestDataverseConnectionAsync);
        app.MapGet("/healthz/dataverse/crud", TestDataverseCrudOperationsAsync);

        app.MapGet("/healthz/dataverse/doc/{id}", async (string id, IDocumentDataverseService dataverseService, ILogger<Program> logger) =>
        {
            logger.LogInformation("[DEBUG-ENDPOINT] Testing document retrieval for {Id}", id);
            try
            {
                var doc = await dataverseService.GetDocumentAsync(id);
                if (doc == null)
                    return Results.Ok(new { status = "NOT_FOUND", documentId = id, message = "Document not found in Dataverse" });

                return Results.Ok(new
                {
                    status = "FOUND",
                    documentId = doc.Id,
                    name = doc.Name,
                    fileName = doc.FileName,
                    isEmailArchive = doc.IsEmailArchive,
                    parentDocumentId = doc.ParentDocumentId,
                    matterId = doc.MatterId,
                    projectId = doc.ProjectId,
                    invoiceId = doc.InvoiceId,
                    emailConversationIndex = doc.EmailConversationIndex
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG-ENDPOINT] Error retrieving document {Id}", id);
                return Results.Ok(new { status = "ERROR", documentId = id, error = ex.Message, innerError = ex.InnerException?.Message });
            }
        })
            .AllowAnonymous()
            .RequireRateLimiting("anonymous"); // Task AUTHV2-049 — anonymous + hits Dataverse; 10/min per IP

        app.MapGet("/ping", () => Results.Text("pong"))
            .AllowAnonymous()
            .WithTags("Health")
            .WithDescription("Lightweight health check for warm-up agents. Returns 'pong' without authentication.");

        app.MapGet("/status", () =>
        {
            return TypedResults.Json(new
            {
                service = "Sprk.Bff.Api",
                version = "1.0.2",
                timestamp = DateTimeOffset.UtcNow
            });
        })
            .AllowAnonymous()
            .RequireRateLimiting("anonymous") // Task AUTHV2-049 — anonymous, prevent spam scraping; 10/min per IP
            .WithTags("Health")
            .WithDescription("Service status with metadata (no sensitive info).");
    }

    private static void MapDomainEndpoints(WebApplication app)
    {
        app.MapUserEndpoints();
        app.MapPermissionsEndpoints();
        app.MapNavMapEndpoints();
        app.MapDataverseDocumentsEndpoints();
        app.MapFileAccessEndpoints();
        app.MapDocumentsEndpoints();
        app.MapDocumentsBulkEndpoints();
        app.MapUploadEndpoints();
        app.MapOBOEndpoints();
        app.MapDocumentOperationsEndpoints();
        app.MapEmailEndpoints();
        app.MapOfficeEndpoints();
        // smart-todo-decoupling-r3 task 070a — Office-scoped sprk_communication lookups
        // for Outlook taskpane (Create To Do ribbon + linked-todos banner).
        app.MapOfficeCommunicationsEndpoints();
        app.MapFieldMappingEndpoints();
        app.MapEventEndpoints();
        app.MapWorkAssignmentEndpoints();
        app.MapScorecardCalculatorEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapAnalysisEndpoints();
            app.MapPlaybookEndpoints();
            app.MapPlaybookEmbeddingEndpoints();
            app.MapAiPlaybookBuilderEndpoints();
            app.MapScopeEndpoints();
            app.MapNodeEndpoints();
            app.MapPlaybookRunEndpoints();
            app.MapModelEndpoints();
            app.MapHandlerEndpoints();
        }

        app.MapRagEndpoints();
        app.MapKnowledgeBaseEndpoints();
        // AIPU2-035: Prompt Library — Personal, Team, Org, System template CRUD + render
        app.MapPromptLibraryEndpoints();
        // AIPU2-036: Feedback — per-response thumbs up/down submit + aggregation by playbook/capability
        app.MapFeedbackEndpoints();
        app.MapChatEndpoints();

        // R5 task 014 (D2-04) — direct entry point for the chat-driven Summarize vertical
        // slice. Maps POST /api/ai/chat/sessions/{sessionId}/summarize and delegates to
        // SessionSummarizeOrchestrator (task 012). UNCONDITIONAL mapping per R5 §3.2 — the
        // orchestrator is also registered unconditionally in AnalysisServicesModule.cs
        // (asymmetric-registration rule R5 §10 F.1 satisfied). Sibling agent-tool path
        // (task 015) converges on the same orchestrator.
        app.MapSummarizeSessionEndpoint();

        try { app.MapChatDocumentEndpoints(); }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EndpointMapping");
            logger.LogError(ex, "MapChatDocumentEndpoints FAILED — document upload endpoints will be unavailable");
        }
        app.MapChatWordExportEndpoints();
        app.MapAnalysisChatContextEndpoints();
        app.MapStandaloneChatContextEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapSemanticSearchEndpoints();
            app.MapRecordSearchEndpoints();
        }

        app.MapVisualizationEndpoints();
        app.MapResilienceEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled"))
        {
            app.MapRecordMatchEndpoints();
            app.MapRecordMatchingAdminEndpoints();
        }

        // Admin endpoints that depend on Analysis services (ReferenceIndexingService, BuilderScopeImporter)
        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapAdminKnowledgeEndpoints();
            app.MapBuilderScopeAdminEndpoints();
        }

        app.MapWorkspaceEndpoints();
        app.MapWorkspaceLayoutEndpoints();
        app.MapWorkspaceAiEndpoints();
        app.MapWorkspaceMatterEndpoints();
        app.MapWorkspaceProjectEndpoints();
        app.MapWorkspaceFileEndpoints();
        // R6 Pillar 6a / D-C-03 / FR-33 (task 052) — GET /api/workspace/state.
        // Consumes IWorkspaceStateService registered in AnalysisServicesModule (task 051).
        // ai-context rate-limit + tid-claim tenant scope per InsightEndpoints precedent.
        app.MapWorkspaceStateEndpoints();

        // R6 Pillar 7 / Q7 SCOPE EXPANSION / task 070 PART A — /api/memory/pins CRUD pair.
        // Consumes IPinnedContextRepository registered in AnalysisServicesModule (task 065).
        // ai-context rate-limit + tid/oid-claim tenant+user scope. Ownership invariant
        // enforced at handler level (UserId match between caller's oid and pin's UserId).
        Sprk.Bff.Api.Api.Memory.PinnedMemoryEndpoints.MapPinnedMemoryEndpoints(app);

        app.MapDailyBriefingEndpoints();

        app.MapFinanceEndpoints();
        app.MapFinanceRollupEndpoints();
        app.MapCommunicationEndpoints();

        // Insights Engine admin endpoints (/api/insights/admin/*) — manual SME authoring
        // of Precedents (D-P3 Phase 1 mode of D-61). Zone B per SPEC §3.5 — consumes
        // IPrecedentBoard which calls IDataverseService directly, no AI internals.
        app.MapPrecedentAdminEndpoints();

        // Insights Engine public endpoint (/api/insights/ask) — D-P15 task 061 —
        // synthesizes an Inference InsightArtifact or returns a structured DeclineResponse
        // via the IInsightsAi facade (only Zone-A surface Zone B may import per SPEC §3.5).
        // Auth: any authenticated tenant user (no admin role). Rate limit: ai-context
        // policy (60/min sliding window per caller). Errors: ADR-019 ProblemDetails.
        app.MapInsightsAskEndpoint();

        // Insights Engine hybrid retrieval endpoint (/api/insights/search) — Wave E task 040
        // (D-P15-06 / FR-04 / SC-04) — open-ended NL query + RAG retrieval over
        // spaarke-insights-index + LLM-synthesized grounded summary. Same Zone B placement,
        // auth model, and rate-limit policy as /api/insights/ask. Kill-switch (ADR-032 P3):
        // when AI is disabled, NullRagService throws FeatureDisabledException → 503.
        app.MapInsightsSearchEndpoint();

        // Insights Engine unified Assistant tool-call endpoint (/api/insights/assistant/query)
        // — Wave E3 task 042 / FR-05. Single tool surface for the Spaarke Assistant; routes
        // internally to playbook OR RAG via the Wave E2 classifier (or caller forceMode
        // override). Zone B placement, same auth + rate-limit as /ask + /search. Kill-switch
        // (ADR-032 P3): FeatureDisabledException → 503 with stable errorCode
        // (ai.insights.disabled | ai.rag.disabled | ai.intent-classification.disabled).
        // Contract anchor: projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md.
        app.MapInsightsAssistantEndpoint();

        // SPE Admin endpoints (/api/spe/*) — environments, configs, business units, containers, audit log, dashboard
        app.MapSpeAdminEndpoints();

        // SPE container item endpoints (/api/spe/containers/{id}/items, /upload, /content, /preview, /versions, /thumbnails, /sharing, /folders)
        // Registered separately because ContainerItemEndpoints maps absolute paths (not relative to the /api/spe group).
        // Inherits auth via RequireAuthorization() called inside MapContainerItemEndpoints. (SPE-017 through SPE-021)
        app.MapContainerItemEndpoints();

        // M365 Copilot Agent gateway endpoints (/api/agent/*)
        app.MapAgentEndpoints();

        // External access endpoints:
        //   /api/v1/external/*        — Power Pages portal users (portal JWT auth)
        //   /api/v1/external-access/* — Internal management (Azure AD auth)
        app.MapExternalAccessEndpoints();

        // Reporting module endpoints (/api/reporting/*) — Power BI Embedded (App Owns Data)
        app.MapReportingEndpoints();

        // Registration endpoints (/api/registration/*) — demo request submission, approval, rejection
        app.MapRegistrationEndpoints();

        // R3 task 020 (FR-2.6) — Admin background-job inspection endpoints.
        // GET /api/admin/jobs               — list registered jobs + status summary
        // GET /api/admin/jobs/{jobId}/status — per-job detail + last 10 runs
        // Behind RequireAuthorization("SystemAdmin") per Q6 owner clarification.
        // Tasks 021 + 022 append their handlers to JobsEndpoints.cs in pre-reserved comment blocks.
        app.MapAdminJobsEndpoints();

        // R3 task 035 (FR-1A.9) — User-facing membership endpoint.
        // GET /api/users/me/memberships/{entityType} — resolves caller's memberships per
        // entity, grouped by role; supports filtering by roles/identityTypes + pagination.
        // Standard Spaarke Auth v2 OBO (ADR-028). Unconditional registration per
        // bff-extensions.md §F.1 (dependencies in MembershipModule.AddMembership are also
        // unconditional). Phase 1D includeRelated accepted-but-ignored until task 054.
        app.MapMembershipApi();

        // R3 task 036 (FR-1A.10 + FR-1A.11) — Admin membership-discovery audit + cache refresh.
        // GET  /api/admin/membership/discovered/{entityType} — operator audit (AC-1A.2)
        // POST /api/admin/membership/refresh-metadata        — cache invalidation (AC-1A.7)
        // Behind RequireAuthorization("SystemAdmin") per Q6 owner clarification.
        // Unconditional registration per bff-extensions.md §F.1 — IMembershipFieldDiscoveryService
        // is unconditionally registered in MembershipModule.AddMembership.
        app.MapAdminMembershipEndpoints();
    }

    private static void MapSpaFallback(WebApplication app)
    {
        app.MapFallback(context =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/playbook-builder/", StringComparison.OrdinalIgnoreCase) &&
                !Path.HasExtension(path))
            {
                context.Request.Path = "/playbook-builder/index.html";
                return context.RequestServices.GetRequiredService<IWebHostEnvironment>()
                    .WebRootFileProvider
                    .GetFileInfo("playbook-builder/index.html")
                    .Exists
                    ? Results.File(
                        Path.Combine(context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath!, "playbook-builder/index.html"),
                        "text/html").ExecuteAsync(context)
                    : Results.NotFound().ExecuteAsync(context);
            }
            return Results.NotFound().ExecuteAsync(context);
        });
    }

    private static async Task<IResult> TestDataverseConnectionAsync(IDataverseHealthService dataverseService)
    {
        try
        {
            var isConnected = await dataverseService.TestConnectionAsync();
            if (isConnected)
                return TypedResults.Ok(new { status = "healthy", message = "Dataverse connection successful" });
            else
                return TypedResults.Problem(detail: "Dataverse connection test failed", statusCode: 503, title: "Service Unavailable");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 503, title: "Dataverse Connection Error");
        }
    }

    private static async Task<IResult> TestDataverseCrudOperationsAsync(IDataverseHealthService dataverseService)
    {
        try
        {
            var testPassed = await dataverseService.TestDocumentOperationsAsync();
            if (testPassed)
                return TypedResults.Ok(new { status = "healthy", message = "Dataverse CRUD operations successful" });
            else
                return TypedResults.Problem(detail: "Dataverse CRUD operations test failed", statusCode: 503, title: "Service Unavailable");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 503, title: "Dataverse CRUD Test Error");
        }
    }
}
