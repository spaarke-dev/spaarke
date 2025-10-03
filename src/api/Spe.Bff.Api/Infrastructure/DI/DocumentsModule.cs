using Spaarke.Core.Auth;
using Spe.Bff.Api.Api.Filters;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE specialized operation classes (Task 3.2, enhanced Task 4.4)
        services.AddScoped<ContainerOperations>();
        services.AddScoped<DriveItemOperations>();
        services.AddScoped<UploadSessionManager>();
        services.AddScoped<UserOperations>();

        // SPE file store facade (delegates to specialized classes)
        services.AddScoped<SpeFileStore>();

        // Document authorization filters
        services.AddScoped<DocumentAuthorizationFilter>(provider =>
            new DocumentAuthorizationFilter(
                provider.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>(),
                "read"));

        return services;
    }
}
