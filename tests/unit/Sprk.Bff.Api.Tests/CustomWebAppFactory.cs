using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.Integration.Workspace;
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
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",

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

                // AzureOpenAI options (required by AiModule for IChatClient registration)
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",

                // Record Matching (required by AttachmentClassificationJobHandler)
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",

                // AiSearchResilience options (ValidateDataAnnotations)
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",

                // GraphResilience options
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",

                // SpeAdmin options (required by SpeAdminModule)
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",

                // ManagedIdentity options (required by DataverseWebApiClient in SpeAdminModule)
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id"
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use Testing environment (consistent with other test fixtures)
        // This disables ValidateScopes which catches pre-existing singleton→scoped
        // DI lifetime issues in the production codebase (not introduced by this PR)
        builder.UseEnvironment("Testing");

        // Use ConfigureTestServices to replace services AFTER the app's services are registered.
        // This ensures our fakes override the real implementations registered in Program.cs.
        builder.ConfigureTestServices(services =>
        {
            // ---------------------------------------------------------------
            // AUTHENTICATION: Replace JWT/OIDC with a fake handler that
            // injects a known test identity when an Authorization header is
            // present. This satisfies RequireAuthorization() on endpoints.
            // ---------------------------------------------------------------
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = FakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(
                FakeAuthHandler.SchemeName, _ => { });

            // Override Microsoft Identity Web's PostConfigure which replaces our
            // DefaultAuthenticateScheme/DefaultChallengeScheme. This forces the
            // fake authentication handler to be used throughout the request pipeline.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = FakeAuthHandler.SchemeName;
            });

            // ---------------------------------------------------------------
            // GRAPH CLIENT FACTORY: Replace with a fake that does NOT perform
            // real MSAL OBO token exchange against Azure AD. Without this,
            // any endpoint that calls SpeFileStore (OBO, upload, file ops)
            // would attempt a real token exchange with the test "Bearer test-token",
            // which Azure AD rejects. The global exception handler maps
            // MsalServiceException → 401, causing spurious auth failures.
            // ---------------------------------------------------------------
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // ---------------------------------------------------------------
            // HOSTED SERVICES: Remove background workers that depend on
            // services not fully configured in the test environment.
            // ---------------------------------------------------------------
            services.RemoveAll<IHostedService>();

            // Mock IDataverseService to avoid real Dataverse connection in tests
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }
}
