using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Spe.Integration.Tests;

/// <summary>
/// Test fixture for integration tests that configures the test environment
/// and provides HTTP clients for testing the API.
/// </summary>
public class IntegrationTestFixture : WebApplicationFactory<Program>
{
    public IConfiguration Configuration { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: false)
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

            // Add test-specific service overrides here
            // For example, mock external dependencies like Graph API calls
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