using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Api.Filters;
using Spaarke.Core.Auth;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE file store facade
        services.AddScoped<SpeFileStore>();

        // Document authorization filters
        services.AddScoped<DocumentAuthorizationFilter>(provider =>
            new DocumentAuthorizationFilter(
                provider.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>(),
                "read"));

        return services;
    }
}