using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.Mocks;

namespace Sprk.Bff.Api.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Add ALL configuration BEFORE the host is built
        // This ensures config is available during Program.cs service registration
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
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",

                // Dataverse options (required by DataverseOptions validation)
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
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
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key"
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Configuration is set in CreateHost/ConfigureHostConfiguration
        // This method handles DI overrides for tests
        builder.ConfigureServices(services =>
        {
            if (Environment.GetEnvironmentVariable("USE_FAKE_GRAPH") == "1")
            {
                // Replace Graph client factory
                var graphFactory = services.SingleOrDefault(s => s.ServiceType == typeof(IGraphClientFactory));
                if (graphFactory != null) services.Remove(graphFactory);
                services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

                // OBO functionality now handled by SpeFileStore - no mock needed
            }
        });
    }
}
