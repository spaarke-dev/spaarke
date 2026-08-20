using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
using Sprk.Bff.Api.Api.Notifications;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Endpoints.Diagnostics;  // G-8 Batch 6 — I4 tenant-container-resolver diagnostic (customer-provisioning-r1)
using Sprk.Bff.Api.Endpoints.Onboarding;   // task 042 — H0.5 consent-callback (customer-provisioning-r1)

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
        // MapSpaFallback removed 2026-07-08 — the /playbook-builder SPA the BFF used to
        // serve (legacy canvas builder) is deleted; PlaybookBuilder ships as the
        // sprk_playbookbuilder Dataverse web resource.
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        // Anonymous client config endpoint — MSAL bootstrap fallback for direct URL access (AIPU-091)
        app.MapMsalConfigEndpoints();

        // Anonymous public runtime config endpoint (FR-36 — customer-provisioning-orchestration-r1
        // task 087). Returns { bffUrl, msalClientId, tenantId, featureFlags } short-cached (60s + ETag)
        // for external-spa + code-pages, closing the bake-at-build-time pattern.
        app.MapPublicConfigEndpoint();

        // /healthz is the App Service LIVENESS probe — it must not fail on catalog
        // drift (an unseeded catalog would recycle instances forever). The FR-P0-04
        // reconciliation check (tag "catalog") is exposed on its own endpoint below;
        // drift additionally logs at Error on startup via the hosted service.
        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = registration => !registration.Tags.Contains("catalog")
        }).AllowAnonymous();

        // FR-P0-04 catalog-reconciliation probe: Unhealthy on constants↔rows drift or
        // tool↔handler bijection violation. Verified green at gate task 014 after seeding.
        app.MapHealthChecks("/healthz/catalog", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("catalog")
        }).AllowAnonymous();

        // Anonymous smoke probes that hit Dataverse live — rate-limited to prevent abuse
        // (mirrors the /healthz/dataverse/doc/{id} sibling below). Task 023 (B-2): added
        // RequireRateLimiting and stopped echoing ex.Message (see handler methods below).
        app.MapGet("/healthz/dataverse", TestDataverseConnectionAsync)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous");
        app.MapGet("/healthz/dataverse/crud", TestDataverseCrudOperationsAsync)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous");

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
                // Task 023 (MF-3): do NOT echo ex.Message / InnerException to the anonymous
                // caller (information disclosure). The exception is logged server-side above.
                logger.LogError(ex, "[DEBUG-ENDPOINT] Error retrieving document {Id}", id);
                return Results.Ok(new { status = "ERROR", documentId = id, message = "An error occurred retrieving the document. See server logs." });
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

        // OBO (user-context) document version-history: list + open-prior-version, READ-ONLY —
        // spaarkeai-compose-r6 task 050 (FR-07 / Success Criterion 4). UNCONDITIONAL mapping per
        // bff-extensions.md §F.1; the backing ISpeFileOperations facade is registered
        // unconditionally in DocumentsModule (no new service, no flag). NOT the app-only admin
        // version surface (MapContainerItemEndpoints) — different auth path by design (ADR-028).
        app.MapDocumentVersionEndpoints();

        app.MapDocumentOperationsEndpoints();

        // Compose drafting workspace endpoints (/api/compose/*) — spaarkeai-compose-r1.
        // Seven endpoints: upload (R2-reserved 501), GET/{id}, save, promote, checkout/checkin
        // (Phase 5 stubs), and action/{consumerType} (AI dispatch via PublicContracts facade).
        // UNCONDITIONAL mapping per bff-extensions.md §F.1; matching unconditional DI is in
        // ComposeModule.AddComposeModule (called from Program.cs). R1 has no feature gates.
        app.MapComposeEndpoints();

        // MapEmailEndpoints removed (email-communication-solution-r4 task 007, DEC-2/FR-07):
        // the legacy OOB-`email`-activity subsystem (`/api/v1/emails/*`, the Dataverse
        // `PrimaryEntityName=="email"` webhook, and the self-built ConfidentialClientApplication
        // in EmailAssociationService) is retired. Inbound email is 100% Graph via
        // Services/Communication/IncomingCommunicationProcessor. See ADR-045.
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
            // FR-13 (ai-advanced-capabilities-agreements-r1 task 050) — Review Summary Memo
            // assembly + persistence. Mapped INSIDE this SAME compound gate (bff-extensions.md
            // §F.1 asymmetric-registration rule): AnalysisResultPersistence (this endpoint's
            // dependency) is registered only when this gate is ON (AnalysisServicesModule.cs).
            app.MapReviewMemoEndpoints();
            app.MapPlaybookEndpoints();
            // MapAiPlaybookBuilderEndpoints removed 2026-07-07 (redesign-r1 task 050, FR-P4-04 server
            // leg): /api/ai/playbook-builder/* had zero client callers after task 053 deleted the
            // canvas estate; the BA catalog editor saves via Dataverse Web API directly.
            app.MapScopeEndpoints();
            app.MapNodeEndpoints();
            app.MapPlaybookRunEndpoints();
            app.MapModelEndpoints();
            app.MapHandlerEndpoints();
        }

        app.MapRagEndpoints();
        app.MapKnowledgeBaseEndpoints();
        // UAT round-3 D3: NDA-standard clause text by ref (KNW-011 Part B) for the review comment hover.
        app.MapNdaStandardEndpoints();
        // AIPU2-035: Prompt Library — Personal, Team, Org, System template CRUD + render
        app.MapPromptLibraryEndpoints();
        // AIPU2-036: Feedback — per-response thumbs up/down submit + aggregation by playbook/capability
        app.MapFeedbackEndpoints();
        app.MapChatEndpoints();
        // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): client-ack endpoint for
        // UI-affecting tool results. UNCONDITIONAL mapping — IUiActionAckCoordinator is
        // registered unconditionally in AiChatModule (no compound-gate dependency).
        app.MapChatAckEndpoints();

        // FR-P1-01 (ai-architecture-redesign-r1 task 020) — catalog-driven chat-summarize.
        // Maps POST /api/ai/chat/sessions/{sessionId}/summarize and delegates to
        // the ONE dispatch seam (task 044), which resolves the chat-summarize Binding row and
        // executes the SUM-CHAT@v1 prompted Action via ActionRunner + PromptSchemaRenderer.
        // UNCONDITIONAL mapping — the orchestrator has a Null-Object mirror registered on
        // the compound-OFF branch (asymmetric-registration rule §10 F.1 satisfied).
        app.MapSummarizeSessionEndpoint();

        // FR-P1-04 (ai-architecture-redesign-r1 task 023b) — the Click entry path.
        // Maps POST /api/ai/chat/sessions/{sessionId}/dispatch and delegates to
        // SessionDispatchOrchestrator, which resolves the chip's binding_id against the
        // Binding table (ADR-039 — the id IS the routing decision) and executes the
        // prompted Action with the ADR-040 ledger write before render.
        // UNCONDITIONAL mapping — NullSessionDispatchOrchestrator mirror registered on
        // the compound-OFF branch (asymmetric-registration rule §10 F.1 satisfied).
        app.MapDispatchSessionEndpoint();

        // FR-A1-12 (spaarke-ai-architecture-redesign-r2 task 041) — capability-discovery
        // READ endpoint for deterministic soft-slash launchers (gate-038 deferral).
        // Maps GET /api/ai/capabilities. Depends ONLY on IConsumerRoutingService, which
        // RoutingModule registers UNCONDITIONALLY (always-on routing facade, no feature
        // flag) — so this mapping is symmetric with its dependency (§10 F.1) and does NOT
        // need to sit inside the DocumentIntelligence/Analysis compound-flag block below.
        app.MapCapabilityDiscoveryEndpoints();

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

        // Admin endpoints that depend on Analysis services (ReferenceIndexingService).
        // MapBuilderScopeAdminEndpoints removed 2026-07-07 (redesign-r1 task 050) with the
        // AiPlaybookBuilder estate — builder-scope import had no surviving consumer.
        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapAdminKnowledgeEndpoints();
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

        // AIR2-052 — /api/memory/{user,records} minimal governance surface (FR-B-03): user review/delete,
        // GDPR erase, and record-authorization-aligned record-memory read over IMemoryItemStore (task 050).
        // Unconditional registration (bff-extensions §F); consumes memory plumbing + existing authorization.
        Sprk.Bff.Api.Api.Memory.MemoryGovernanceEndpoints.MapMemoryGovernanceEndpoints(app);

        app.MapDailyBriefingEndpoints();

        app.MapFinanceEndpoints();
        app.MapFinanceRollupEndpoints();
        app.MapCommunicationEndpoints();

        // Email composer "insert template" render surface (email-communication-solution-r5). Thin,
        // additive endpoint that reuses IEmailTemplateService for fetch + {!entity.field} merge; app-only
        // Dataverse read (IGenericEntityService) + central TokenCredential — no new Dataverse client.
        app.MapCommunicationTemplateEndpoints();

        // Email composer AI "sparkle" drafting surface (email-communication-solution-r5 Wave E). Thin,
        // additive endpoint that consumes the IEmailDraftAi PublicContracts facade (ADR-013/§10) — always
        // resolvable via the ADR-032 Null-Object mirror, so mapping is unconditional.
        app.MapCommunicationDraftEndpoints();

        // Notification spine Layer-C negotiate endpoint (spaarke-notification-spine-r1 task 020 /
        // FR-04). Mapped UNCONDITIONALLY — its handler resolves SignalRDeliveryService, which is
        // registered unconditionally (real or Null-Object) by AddNotificationsModule, so metadata
        // generation succeeds at startup with SignalR OFF (ADR-032 — no asymmetric registration).
        app.MapNotificationsEndpoints();

        // ACS Event Grid inbound ingress (messaging-communication-app-r1 task 030 / FR-02). Public webhook
        // (AllowAnonymous — Event Grid presents no OAuth token); authenticity enforced inside the ingress
        // service (subscription-validation handshake + fail-closed topic allow-list + optional ?sig= secret).
        app.MapAcsEventGridEndpoints();

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

        // Onboarding — H0.5 consent-callback (customer-provisioning-orchestration-r1, task 042).
        // POST /api/onboarding/consent-callback — Anonymous + HMAC-SHA256 signature verified.
        // Captures the customer admin tid from the Microsoft admin-consent redirect and enqueues
        // the L2 provisioning pipeline via Service Bus. See Endpoints/Onboarding/OnboardingModule.cs
        // and design.md D18 + §4.3a.2 for the Anonymous+HMAC exception rationale.
        app.MapConsentCallbackEndpoint();

        // Diagnostics — I4 tenant-container-resolver (customer-provisioning-orchestration-r1,
        // G-8 Batch 6 fix #18). GET /api/diagnostics/tenant-container-resolver — JWT-authorized,
        // READ-ONLY; the L2 H13 I4 invariant probe's BFF-side dependency (without it, live H13
        // parks I4 at InfraFault via its 404 branch and Ready is unreachable). Contract locked
        // by SpeContainerResolverInvariantProbe (L2). See Endpoints/Diagnostics/.
        app.MapTenantContainerResolverEndpoint();

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

    private static async Task<IResult> TestDataverseConnectionAsync(IDataverseHealthService dataverseService, ILogger<Program> logger)
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
            // Task 023 (B-2): do NOT echo ex.Message to the anonymous caller (information
            // disclosure). Log server-side and return a generic detail.
            logger.LogError(ex, "Dataverse connection health probe failed");
            return TypedResults.Problem(detail: "Dataverse connection test failed. See server logs.", statusCode: 503, title: "Dataverse Connection Error");
        }
    }

    private static async Task<IResult> TestDataverseCrudOperationsAsync(IDataverseHealthService dataverseService, ILogger<Program> logger)
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
            // Task 023 (B-2): do NOT echo ex.Message to the anonymous caller (information
            // disclosure). Log server-side and return a generic detail.
            logger.LogError(ex, "Dataverse CRUD health probe failed");
            return TypedResults.Problem(detail: "Dataverse CRUD operations test failed. See server logs.", statusCode: 503, title: "Dataverse CRUD Test Error");
        }
    }
}
