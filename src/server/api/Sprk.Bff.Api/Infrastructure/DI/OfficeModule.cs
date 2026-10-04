using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Dependency injection module for Office add-in services.
/// </summary>
/// <remarks>
/// <para>
/// Registers services required for Office add-in operations:
/// - IOfficeService: Main service for save, share, search operations
/// - IOfficeRateLimitService: Rate limiting for Office endpoints (Task 031)
/// - Authorization filters for Office-specific policies
/// </para>
/// <para>
/// Per ADR-010, this module maintains minimal DI registrations.
/// Services are scoped to match the request lifecycle for proper
/// authorization context handling.
/// </para>
/// </remarks>
public static class OfficeModule
{
    /// <summary>
    /// Adds Office add-in services to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOfficeModule(this IServiceCollection services)
    {
        // ============================================================================
        // Office Add-in Focused Services (extracted from OfficeService)
        // ============================================================================
        // ADR-010: Concrete registration — no interfaces unless a seam is required.
        services.AddScoped<OfficeEmailEnricher>();
        services.AddScoped<OfficeDocumentPersistence>();
        services.AddScoped<OfficeJobQueue>();
        services.AddScoped<OfficeStorageUploader>();

        // Task 059: the add-in's Dataverse READS (entity search, matter types, the To Do's sprk_recordtype_ref
        // lookup), extracted from OfficeService. Concrete (ADR-010) and UNCONDITIONAL: the search routes map
        // unconditionally, and its deps (DataverseWebApiClient, IImpersonatedCommunicationQuery) are unconditional
        // singletons from SpeAdminModule and CommunicationModule.
        services.AddScoped<OfficeSearchService>();

        // Task 060 (#1084): the save's job record (create, transitions, the idempotency lookup, the status read, the
        // SSE stream), stored on the Dataverse row instead of a static in-memory dictionary. Concrete (ADR-010) and
        // UNCONDITIONAL: the job routes map unconditionally, and its deps (IProcessingJobService, IJobStatusService)
        // are unconditional. TimeProvider decides when a non-terminal job is abandoned; idempotent, as in the other
        // modules that use it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OfficeJobStatusService>();

        // Task 068 (#1086): the Generate Profile request, on the job queue instead of a Task.Run behind the 202.
        // Concrete (ADR-010) and UNCONDITIONAL: the route maps unconditionally, and JobSubmissionService is an
        // unconditional singleton (JobProcessingModule). Its optional IDocumentProfileAi is the AI gate: absent → 503.
        services.AddScoped<OfficeProfileQueue>();

        // FR-C3 content de-dup detector (Tier-1 exact quickXorHash). Concrete, scoped (ADR-010); reused by
        // every document-creating upload path (email-attachment today; Compose next). Non-fatal by design.
        services.AddScoped<ContentDedupDetector>();

        // FR-13 (spaarkeai-word-add-in-r1 task 030): shared server-side creation service — load-bearing owner, BU
        // defaults, matter-type lookup, Field Mapping Framework (no numbering: left to a planned separate
        // component, notes/030-numbering-handoff.md). Concrete (ADR-010: one
        // implementation, no seam) and UNCONDITIONAL: its consumer, OfficeService.QuickCreateAsync, serves an
        // unconditionally-mapped route, and its deps (IGenericEntityService, IFieldMappingDataverseService) are
        // unconditional GraphModule singletons — no §10 F.1 asymmetry.
        services.AddScoped<RecordCreationService>();

        // ============================================================================
        // Office Add-in Orchestrator Service
        // ============================================================================
        // Scoped lifetime - new instance per request for proper auth context
        services.AddScoped<IOfficeService, OfficeService>();

        // ============================================================================
        // Rate Limiting Configuration and Service (Task 031)
        // ============================================================================
        // Configuration with default values - can be overridden in appsettings.json
        // Rate limits per spec.md:
        //   - Save: 10 requests/minute/user
        //   - QuickCreate: 5 requests/minute/user
        //   - Search: 30 requests/minute/user
        //   - Jobs: 60 requests/minute/user
        //   (Share and Recent were removed with their routes by task 058.)
        services.AddOptions<OfficeRateLimitOptions>()
            .BindConfiguration(OfficeRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton for efficiency - state is stored in Redis, service is stateless
        // Uses IDistributedCache for distributed rate limit state
        services.AddSingleton<IOfficeRateLimitService, OfficeRateLimitService>();

        // ============================================================================
        // Job Status Service (Task 064)
        // ============================================================================
        // Singleton for efficient Redis pub/sub handling
        // Bridges background workers to SSE clients for real-time job status updates
        //
        // Per ADR-032 symmetric DI + spaarke-redis-cache-remediation-r1 task 005:
        // IConnectionMultiplexer is always registered (real impl when Redis is enabled,
        // NullConnectionMultiplexer P2 Quiet no-op when in-memory cache fallback is
        // active in Development). Use GetRequiredService — graceful degradation is
        // handled inside JobStatusService via IConnectionMultiplexer.IsConnected.
        services.AddSingleton<IJobStatusService, JobStatusService>();

        // ============================================================================
        // Authorization Filters (to be added in task 033)
        // ============================================================================
        // TRACKED: GitHub #228 - Add Office-specific authorization filters
        // services.AddScoped<OfficeAuthorizationFilter>();

        return services;
    }
}
