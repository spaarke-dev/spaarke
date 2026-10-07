using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Core.Auth;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // Clock for share-link expiry (unified-access-control-r2 task 072). TryAdd, matching the
        // idempotent convention in CommunicationModule / InsightsIngestModule / MembershipModule —
        // whichever module loads first wins and the others no-op. Registered HERE so the share-link
        // route does not depend on an unrelated module having been added.
        services.TryAddSingleton(TimeProvider.System);

        // ============================================================================
        // Phase 4: Token Caching (ADR-009: Redis-First Caching)
        // ============================================================================
        // CacheMetrics is a static class (FR-02 of spaarke-redis-cache-remediation-r2):
        // single canonical Meter("Sprk.Bff.Api.Cache") owner. No DI registration required —
        // consumers call CacheMetrics.RecordHit(...) / RecordMiss(...) directly. Per ADR-010
        // (DI minimalism), this eliminates an unnecessary instance class.

        // Register GraphTokenCache as Singleton (stateless, uses IDistributedCache which is also Singleton)
        // Reduces OBO token exchange latency by 97% (~200ms → ~5ms on cache hit)
        services.AddSingleton<GraphTokenCache>();

        // ============================================================================
        // Phase 3: Graph Metadata Caching (ADR-009: Redis-First, ADR-007: SpeFileStore Facade)
        // ============================================================================
        // Caches Graph API metadata: file metadata (5min), folder listings (2min),
        // container-to-drive mappings (24h). Expected 90%+ hit rate, ~5ms vs 100-300ms.
        services.AddSingleton<GraphMetadataCache>();

        // ============================================================================
        // SPE Operations (Phase 2: Service Layer Simplification)
        // ============================================================================
        // SPE specialized operation classes (Task 3.2, enhanced Task 4.4)
        services.AddScoped<ContainerOperations>();
        services.AddScoped<DriveItemOperations>();
        services.AddScoped<UploadSessionManager>();
        services.AddScoped<UserOperations>();

        // SPE file store facade (delegates to specialized classes)
        services.AddScoped<SpeFileStore>();
        // Register interface for DI into AI services (DocumentIntelligenceService)
        services.AddScoped<ISpeFileOperations>(sp => sp.GetRequiredService<SpeFileStore>());

        // unified-access-control-r2 task 166 f1 (owner round 21 item 1 (i)-(ii), round 26 item 3): the ONE writer of a
        // document's SPE pointer outside a path that uploads the bytes itself — the client pointer-attach route
        // (POST /api/v1/documents/{id}/file) and the relocation shared by the legacy migration and Make Secure. Scoped
        // like its dependencies (RecordContainerResolver, SpeFileStore); UNCONDITIONAL because the route that calls it
        // is mapped unconditionally (bff-extensions.md §F.1).
        // Owner round 54 item 1: its "may the author of a post-move edit write the document NOW?" is read FRESH — it is
        // given the UNCACHED DataverseAccessDataSource (registered by AddSpaarkeCore), never the 60-second
        // CachedAccessDataSource that IAccessDataSource resolves to: a Write answer cached just before Make Secure must not
        // make a non-writer's edit current. (The relocator also refuses any answer older than its question.)
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator>(
            sp, sp.GetRequiredService<Spaarke.Dataverse.DataverseAccessDataSource>()));

        // unified-access-control-r2 task 171 (owner rounds 69 + 70): the just-in-time Office-edit grant on a SECURE
        // container, used by GET /api/documents/{id}/office and /open-links. UNCONDITIONAL because FileAccessEndpoints maps
        // unconditionally (bff-extensions.md section F.1); its dependencies (RecordContainerResolver, CallerRecordAccessProbe,
        // SpeContainerMembershipService, IGenericEntityService) are registered unconditionally too.
        services.AddScoped<Sprk.Bff.Api.Services.Documents.OfficeEditAccessService>();

        // The legacy migration (ADR-036 IScheduledJob, ADR-052 "BFF, schedule"): registered DISABLED — it runs only when
        // scripts/Invoke-DocumentContainerMigration.ps1 triggers it through /api/admin/jobs (SystemAdmin), and writes
        // only while DocumentContainerMigration:WritesEnabled is set.
        services.AddScheduledJob<Sprk.Bff.Api.Services.Documents.DocumentContainerMigrationJob>(
            Sprk.Bff.Api.Services.Documents.DocumentContainerMigrationJob.DefaultCronSchedule, enabled: false);

        // ============================================================================
        // Document Checkout/Check-in Service (document-checkout-viewer project)
        // ============================================================================
        // Handles document locking, version tracking, and Office Online integration
        services.AddHttpClient<DocumentCheckoutService>();

        // ============================================================================
        // Authorization Filters
        // ============================================================================
        // Document authorization filters
        services.AddScoped<DocumentAuthorizationFilter>(provider =>
            new DocumentAuthorizationFilter(
                provider.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>(),
                "read"));

        return services;
    }
}
