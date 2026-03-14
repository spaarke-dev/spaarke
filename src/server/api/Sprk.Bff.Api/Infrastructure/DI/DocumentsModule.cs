using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ============================================================================
        // Phase 4: Token Caching (ADR-009: Redis-First Caching)
        // ============================================================================
        // Register CacheMetrics as Singleton (stateless, tracks metrics across all requests)
        // Provides OpenTelemetry-compatible metrics for cache hits, misses, and latency
        services.AddSingleton<CacheMetrics>();

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
