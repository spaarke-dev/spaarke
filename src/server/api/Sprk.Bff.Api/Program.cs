using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Workers.Office;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----
builder.Services.AddConfigurationModule(builder.Configuration);

// ---- Services Registration ----

// Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();

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

// SPE Admin module (environments, container type configs, Graph service, audit logging, dashboard sync)
builder.Services.AddSpeAdminModule(builder.Configuration);

// Office Service Bus client and workers
builder.Services.AddOfficeServiceBus(builder.Configuration);
builder.Services.AddOfficeWorkers();

// Distributed cache (Redis or in-memory)
builder.Services.AddCacheModule(builder.Configuration, builder.Logging);

// Graph API resilience, client factory, and Dataverse service
builder.Services.AddGraphModule(builder.Configuration);

// Document Intelligence, Analysis, Playbook, Builder, RAG, and Record Matching services
builder.Services.AddAnalysisServicesModule(builder.Configuration);

// Email-to-Document conversion services
builder.Services.AddEmailServicesModule(builder.Configuration);

// Job processing (handlers, Service Bus, background services, AI platform options)
builder.Services.AddJobProcessingModule(builder.Configuration, builder.Logging);

// M365 Copilot Agent module (gateway, auth, cards, conversation, playbook invocation, telemetry)
builder.Services.AddAgentModule(builder.Configuration);

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

app.Run();

// Expose Program for WebApplicationFactory in tests
public partial class Program
{
}
