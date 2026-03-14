using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for Graph API resilience, client factory, and Dataverse service (ADR-010).
/// </summary>
public static class GraphModule
{
    /// <summary>
    /// Adds Graph API resilience handler, named HttpClient, GraphServiceClient factory,
    /// and Dataverse service.
    /// </summary>
    public static IServiceCollection AddGraphModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Graph API Resilience Configuration
        services
            .AddOptions<GraphResilienceOptions>()
            .Bind(configuration.GetSection(GraphResilienceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // GraphHttpMessageHandler for centralized resilience (retry, circuit breaker, timeout)
        services.AddTransient<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>();

        // Named HttpClient for Graph API with resilience handler
        services.AddHttpClient("GraphApiClient")
            .AddHttpMessageHandler<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

        // Singleton GraphServiceClient factory (uses IHttpClientFactory with resilience handler)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Dataverse service - Singleton for ServiceClient connection reuse
        services.AddSingleton<IDataverseService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
            return new DataverseServiceClientImpl(config, logger);
        });

        return services;
    }
}
