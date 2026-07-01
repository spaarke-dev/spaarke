using Azure.Monitor.OpenTelemetry.AspNetCore;      // R7-S7: UseAzureMonitor() extension
using Sprk.Bff.Api.Api.Membership;                 // R3 task 035 — AddMembershipApi() pairing
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Api.Dataverse;                  // Dataverse passthrough endpoints (Phase B)
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Startup;         // R2 FR-06: AzureMonitorGuard
using Sprk.Bff.Api.Services.Dataverse.Extensions;  // Dataverse DI extension methods (Phase B)
using Sprk.Bff.Api.Workers.Office;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----
builder.Services.AddConfigurationModule(builder.Configuration);

// ---- Services Registration ----

// OpenTelemetry → Azure Monitor (replaces classic Application Insights SDK).
// Per spaarke-redis-cache-remediation-r1 R7-S7 (2026-06-26): the classic SDK
// didn't auto-instrument StackExchange.Redis and the OTel pipeline had no
// exporter — neither Redis dependency telemetry nor the 12 Sprk.Bff.Api.* Meters
// reached App Insights. UseAzureMonitor() wires both pipelines to the same
// App Insights resource pointed at by APPLICATIONINSIGHTS_CONNECTION_STRING.
//
// Guard (R2 FR-06 — spaarke-redis-cache-remediation-r2 task 006):
// Non-Development env + missing APPLICATIONINSIGHTS_CONNECTION_STRING → throw at
// startup via AzureMonitorGuard (mirrors CacheModule 4-branch fail-fast pattern,
// R1 FR-03). Development env preserves dev-convenience pass-through. Without this
// guard, missing conn string in Production silently skipped UseAzureMonitor() and
// no telemetry reached App Insights — invisible failure.
var aiConnString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
if (AzureMonitorGuard.ShouldWireExporter(builder.Environment.EnvironmentName, aiConnString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();

// Managed identity credential — singleton, pinned to the UAMI ClientId from
// Graph:ManagedIdentity:ClientId (or ManagedIdentity:ClientId) when configured. All services that
// need to authenticate outbound to Dataverse / Cosmos / OpenAI / AI Foundry inject this via
// constructor instead of constructing their own DefaultAzureCredential. See
// ManagedIdentityCredentialFactory + ADR-028 + the 2026-05-24 multi-identity-ambiguity fix.
builder.Services.AddSingleton<Azure.Core.TokenCredential>(sp =>
    Sprk.Bff.Api.Infrastructure.Auth.ManagedIdentityCredentialFactory.Create(
        sp.GetRequiredService<IConfiguration>()));

// Data Access Layer - Document storage resolution
builder.Services.AddScoped<Sprk.Bff.Api.Infrastructure.Dataverse.IDocumentStorageResolver, Sprk.Bff.Api.Infrastructure.Dataverse.DocumentStorageResolver>();

// Authentication & Authorization (Azure AD JWT + authorization policies)
builder.Services.AddAuthorizationModule(builder.Configuration);

// External access (Power Pages portal token validation, Contact participation service)
builder.Services.AddExternalAccess();

// Scorecard Calculator Service
builder.Services.AddScoped<Sprk.Bff.Api.Services.ScorecardCalculatorService>();

// Documents module (endpoints + filters)
builder.Services.AddDocumentsModule();

// Dataverse passthrough — Spaarke DataGrid Framework R1, Phase B
// (5 endpoints: savedquery x2, metadata, fetch, record; shared authorization filter
//  with cross-entity FetchXML privilege-bypass mitigation per task 010 design)
builder.Services.AddDataverseSavedQueryServices();   // task 011: shared infra (filter + privilege checker) + savedquery pair
builder.Services.AddDataverseMetadataServices();     // task 012: metadata endpoint (6h cache)
builder.Services.AddDataverseFetchServices();        // task 013: fetch endpoint + FetchXmlEntityExtractor (security-critical)
builder.Services.AddDataverseRecordServices();       // task 014: record endpoint ($select projection)

// Workers module (Service Bus + BackgroundService)
builder.Services.AddWorkersModule(builder.Configuration);

// Office Add-in module
builder.Services.AddOfficeModule();

// Reporting module (Power BI Embedded — ReportingEmbedService + ReportingProfileManager)
builder.Services.AddReportingModule(builder.Configuration);

// Legal Operations Workspace module
builder.Services.AddWorkspaceServices(builder.Configuration);

// Finance Intelligence module
builder.Services.AddFinanceModule(builder.Configuration);

// Communication module (email sending via Graph API)
builder.Services.AddCommunicationModule(builder.Configuration);

// Membership module (R3 Part 1, task 012) — binds MembershipOptions from the
// "Membership" appsettings section. Service registrations (discovery + resolver
// + endpoints) arrive in later P4 tasks. ADR-010 + bff-extensions.md §A.
builder.Services.AddMembership(builder.Configuration);

// R3 task 035 — user-facing membership endpoint surface (FR-1A.9). Currently a
// no-op pairing for the canonical Add+Map convention; reserved for future
// endpoint-specific service registrations (e.g., a CurrentUserResolver extracted
// from MembershipEndpoints.cs when a second consumer surfaces). Endpoint mapping
// itself happens in EndpointMappingExtensions.MapDomainEndpoints via MapMembershipApi().
builder.Services.AddMembershipApi();

// Todo Graph sync scaffolding (smart-todo-decoupling-r3 Phase 6, task 018) — registers
// ITodoGraphSyncHandler / ISpaarkeListProvisioner / ITodoSubscriptionManager / ITodoSyncBackfiller
// UNCONDITIONALLY with Null-Object fallbacks per ADR-032 P2. Feature-gated by
// Spaarke:Graph:TodoSync:Enabled (default false). Real impls arrive in Phase 7
// (tasks 061/062/063/065); placeholders throw until then.
builder.Services.AddTodoSync(builder.Configuration);

// Demo Registration module (self-service demo access provisioning)
builder.Services.AddRegistrationModule(builder.Configuration);

// SPE Admin module (environments, container type configs, Graph service, audit logging, dashboard sync)
builder.Services.AddSpeAdminModule(builder.Configuration);

// Office Service Bus client and workers
builder.Services.AddOfficeServiceBus(builder.Configuration);
builder.Services.AddOfficeWorkers(builder.Configuration);

// Distributed cache (Redis or in-memory)
builder.Services.AddCacheModule(builder.Configuration, builder.Logging, builder.Environment);

// Graph API resilience, client factory, and Dataverse service
builder.Services.AddGraphModule(builder.Configuration);

// Document Intelligence, Analysis, Playbook, Builder, RAG, and Record Matching services
builder.Services.AddAnalysisServicesModule(builder.Configuration);

// Consumer→playbook routing (Phase 1R per chat-routing-redesign-r1 spec FR-1R-02).
// Replaces Workspace__*PlaybookId env vars with Dataverse-backed `sprk_playbookconsumer`
// routing table. Registered UNCONDITIONALLY: routing is always-on (no kill-switch); on
// Dataverse error the impl returns null and the caller falls back to typed-options env
// var during the FR-1R-06 deprecation window. See Infrastructure/DI/RoutingModule.cs.
builder.Services.AddRoutingModule();

// Spaarke Insights Engine — Zone A extraction post-processing primitives per SPEC §3.5.
// Phase 1: D-P10 confidence-threshold gating + per-field Observation emission
// (admin-tunable per D-63 via IOptionsMonitor on ConfidenceThresholdOptions).
// Future Wave-3 additions: D-P9 GroundingVerifier wiring, D-P12 node executors.
builder.Services.AddInsightsExtractionModule(builder.Configuration);

// Spaarke Insights Engine — Zone A universal ingest pipeline per SPEC §3 (D-P7, task 040).
// Post Wave C-G4 (task 022): the legacy code-defined IIngestOrchestrator path has been
// retired; universal-ingest now runs entirely through the universal-ingest@v1 JPS playbook
// (sanitize → layer1Classify → checkLayer2Gate → layer2Extract → groundingVerify →
// emitObservations). This module registers the supporting services consumed by the
// playbook node executors (SanitizerNodeExecutor, ObservationEmitterNodeExecutor,
// IIngestDocumentSource, IObservationIndexUpserter, IObservationMirror).
// Must precede AddInsightsFacadeModule which ctor-injects IIngestDocumentSource.
builder.Services.AddInsightsIngestModule();

// Spaarke Insights Engine — Zone A public facade per SPEC §3.5 (task 042).
// IInsightsAi → InsightsOrchestrator: the ONLY Zone-A surface Zone B code may import.
// Wraps IPlaybookExecutionEngine + IInsightsPlaybookExecutionCache (D-P13) + IOpenAiClient
// + IPlaybookOrchestrationService behind a 3-method facade (AnswerQuestionAsync /
// RunIngestAsync / EmbedTextAsync). RunIngestAsync invokes universal-ingest@v1 via the
// orchestration service; the per-env playbook Guid is resolved through
// InsightsPlaybookNameMapOptions (post Wave C-G4 / task 022).
// Must follow AnalysisServicesModule which registers the engine + D-P13 cache, AND
// AddInsightsIngestModule which registers IIngestDocumentSource.
builder.Services.AddInsightsFacadeModule();

// AI Platform R2: safety perimeter (content safety, prompt shield, groundedness)
builder.Services.AddAiSafetyModule(builder.Configuration);

// AI Platform R2: Cosmos DB persistence (sessions, prompts, audit, memory, feedback)
builder.Services.AddAiPersistenceModule(builder.Configuration);

// AI Platform R2: agent and chat extensions (ISprkAgent impls, orchestration)
builder.Services.AddAiChatModule(builder.Configuration);

// Spaarke Insights Engine — Zone B domain services per SPEC §3.5 facade boundary.
// Phase 1: IInsightGraph + StubInsightGraph (D-P17 swap-path preservation;
// CosmosNoSqlInsightGraph deferred to Phase 1.5 per SPEC §3.3).
builder.Services.AddInsightsModule();

// Email-to-Document conversion services
builder.Services.AddEmailServicesModule(builder.Configuration);

// Job processing (handlers, Service Bus, background services, AI platform options)
builder.Services.AddJobProcessingModule(builder.Configuration, builder.Logging);

// R3 task 020 (FR-2.6) — in-process Spaarke.Scheduling registry + run-history store
// for the /api/admin/jobs/* admin surface. Unconditional per bff-extensions.md §F.1
// (the endpoints map unconditionally; their dependencies must too).
builder.Services.AddSchedulingModule();

// M365 Copilot Agent module (gateway, auth, cards, conversation, playbook invocation, telemetry)
builder.Services.AddAgentModule(builder.Configuration);

// Compose drafting workspace module (spaarkeai-compose-r1) — registers the three Compose
// orchestration services (IComposeService, IComposeDocumentService, ComposeSessionService)
// UNCONDITIONALLY per bff-extensions.md §F.1. The /api/compose/* endpoint group maps
// unconditionally in EndpointMappingExtensions.MapDomainEndpoints, so the DI side MUST
// match to avoid the RB-T028-03/04/05/06 asymmetric-registration anti-pattern. R1 has
// no feature gates per project CLAUDE.md.
builder.Services.AddComposeModule();

// CORS (secure, fail-closed configuration)
builder.Services.AddCorsModule(builder.Configuration, builder.Environment);

// Rate limiting (per-user/per-IP traffic control)
builder.Services.AddRateLimitingModule();

// OpenTelemetry, health checks, and circuit breaker
builder.Services.AddTelemetryModule(builder.Configuration);

var app = builder.Build();

// ---- Startup Diagnostics ----
app.RunStartupDiagnostics();

// ---- Middleware Pipeline ----
app.UseSpaarkeMiddleware();

// ---- Endpoint Groups ----
app.MapSpaarkeEndpoints();

// Dataverse passthrough endpoints (Spaarke DataGrid Framework R1, Phase B)
app.MapSavedQueryEndpoints();   // task 011: GET /api/dataverse/savedquery/{id} + GET /api/dataverse/savedqueries/{entity}
app.MapMetadataEndpoints();     // task 012: GET /api/dataverse/metadata/{entity}
app.MapFetchEndpoints();        // task 013: POST /api/dataverse/fetch (cross-entity privilege check)
app.MapRecordEndpoints();       // task 014: GET /api/dataverse/record/{entity}/{id}

app.Run();

// Expose Program for WebApplicationFactory in tests
public partial class Program
{
}
