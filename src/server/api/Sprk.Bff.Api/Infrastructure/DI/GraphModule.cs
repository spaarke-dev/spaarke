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

        // Named HttpClient for Graph upload sessions (pre-authorized URLs, no auth headers needed)
        services.AddHttpClient("GraphUploadSession");

        // Singleton GraphServiceClient factory (uses IHttpClientFactory with resilience handler)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Dataverse service - Singleton for ServiceClient connection reuse
        services.AddSingleton<IDataverseService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
            return new DataverseServiceClientImpl(config, logger);
        });

        // DataverseWebApiService - uses REST/HttpClient (no WCF). Handles event operations
        // which require full OData query support not available in DataverseServiceClientImpl stub.
        services.AddHttpClient("DataverseWebApi");
        services.AddSingleton<DataverseWebApiService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient("DataverseWebApi");
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<DataverseWebApiService>>();
            return new DataverseWebApiService(httpClient, config, logger);
        });

        // Narrow interface forwarding registrations (ADR-010: forwarding delegates don't count as new types).
        // IDataverseService is a composite interface inheriting all 9 narrow interfaces.
        // These forwarding registrations allow consumers to inject only the narrowest applicable interface.
        services.AddSingleton<IDocumentDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<IAnalysisDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<IGenericEntityService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<IProcessingJobService>(sp => sp.GetRequiredService<IDataverseService>());
        // Events use DataverseWebApiService (real implementation) instead of DataverseServiceClientImpl (stub).
        services.AddSingleton<IEventDataverseService>(sp => sp.GetRequiredService<DataverseWebApiService>());
        services.AddSingleton<IFieldMappingDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<IKpiDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<ICommunicationDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
        services.AddSingleton<IDataverseHealthService>(sp => sp.GetRequiredService<IDataverseService>());

        return services;
    }
}
