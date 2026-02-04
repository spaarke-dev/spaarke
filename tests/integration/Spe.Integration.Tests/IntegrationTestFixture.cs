using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;

namespace Spe.Integration.Tests;

/// <summary>
/// Test fixture for integration tests that configures the test environment
/// and provides HTTP clients for testing the API.
/// </summary>
public class IntegrationTestFixture : WebApplicationFactory<Program>
{
    public IConfiguration Configuration { get; private set; } = null!;

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
                ["ModelSelector:DefaultModel"] = "gpt-4o"
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true)
                  .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                  .AddEnvironmentVariables();

            Configuration = config.Build();
        });

        builder.ConfigureServices(services =>
        {
            // Override services for testing
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });
        });

        // Use ConfigureTestServices to replace services AFTER the app's services are registered
        // This is the correct way to mock services in WebApplicationFactory
        builder.ConfigureTestServices(services =>
        {
            // Mock IDataverseService for integration tests
            // This avoids needing a real Dataverse connection
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateHttpClient()
    {
        var client = CreateClient();

        // Configure client defaults
        client.DefaultRequestHeaders.Add("User-Agent", "SDAP-Integration-Tests/1.0");

        return client;
    }
}
