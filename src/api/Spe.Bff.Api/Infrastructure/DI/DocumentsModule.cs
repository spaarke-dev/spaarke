using Spaarke.Core.Auth;
using Spe.Bff.Api.Api.Filters;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Services;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ============================================================================
        // Phase 4: Token Caching (ADR-009: Redis-First Caching)
        // ============================================================================
        // Register GraphTokenCache as Singleton (stateless, uses IDistributedCache which is also Singleton)
        // Reduces OBO token exchange latency by 97% (~200ms → ~5ms on cache hit)
        services.AddSingleton<GraphTokenCache>();

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
