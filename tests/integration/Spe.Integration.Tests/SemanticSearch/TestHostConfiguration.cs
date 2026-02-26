using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Spaarke.Dataverse;

namespace Spe.Integration.Tests.SemanticSearch;

/// <summary>
/// Shared host configuration for semantic search integration test fixtures.
/// Provides all required configuration values so that Program.cs startup
/// validation passes without external dependencies.
/// </summary>
internal static class TestHostConfiguration
{
    /// <summary>
    /// Applies all required test configuration to the host builder.
    /// Must be called from <c>CreateHost(IHostBuilder)</c> to inject config
    /// BEFORE the host is built (before Program.cs service registration).
    /// </summary>
    public static void ConfigureTestHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                // ServiceBus connection string for early validation
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",

                // CORS
                ["Cors:AllowedOrigins"] = "https://localhost:5173",

                // Azure AD / UAMI
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",

                // AzureAd section for Microsoft Identity Web API authentication
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",

                // Graph options (required by GraphOptions validation)
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",

                // Dataverse options (required by DataverseOptions validation)
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",

                // ServiceBus options (required by ServiceBusOptions validation)
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",

                // DocumentIntelligence options (required for endpoint registration)
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",

                // AI Search options (required for IRagService)
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",

                // Office rate limiting (disabled for tests)
                ["OfficeRateLimit:Enabled"] = "false",

                // Redis options (disabled for tests)
                ["Redis:Enabled"] = "false",

                // ModelSelector options
                ["ModelSelector:DefaultModel"] = "gpt-4o",

                // Communication options (required for GraphSubscriptionManager startup)
                ["Communication:WebhookNotificationUrl"] = "https://test.example.com/api/webhooks/notifications",
                ["Communication:WebhookClientState"] = "test-client-state-secret",
                ["Communication:Enabled"] = "false"
            };
            config.AddInMemoryCollection(dict!);
        });
    }

    /// <summary>
    /// Configures test service replacements that must run AFTER app services are registered.
    /// Call from <c>ConfigureTestServices</c> in each fixture.
    /// Mocks external dependencies (Dataverse, IChatClient) and disables background workers.
    /// </summary>
    public static void ConfigureSharedTestServices(IServiceCollection services)
    {
        // Mock IDataverseService to avoid real Dataverse connection
        var dataverseServiceMock = new Mock<IDataverseService>();
        dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
        services.RemoveAll<IDataverseService>();
        services.AddSingleton(dataverseServiceMock.Object);

        // Register mock IChatClient so ChatEndpoints can infer parameters at startup.
        // Without this, endpoint metadata building fails with "UNKNOWN" parameter source.
        services.RemoveAll<IChatClient>();
        services.AddSingleton(new Mock<IChatClient>().Object);

        // Remove background hosted services that use fake ServiceBus connection.
        // The ServiceBusJobProcessor creates a real ServiceBusProcessor from the fake
        // connection string, which throws ObjectDisposedException during test teardown.
        RemoveHostedService<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Jobs.EmailPollingBackupService>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillService>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationService>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingService>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Communication.GraphSubscriptionManager>(services);
        RemoveHostedService<Sprk.Bff.Api.Services.Communication.InboundPollingBackupService>(services);
    }

    /// <summary>
    /// Removes a specific hosted service registration from the service collection.
    /// </summary>
    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }
}
