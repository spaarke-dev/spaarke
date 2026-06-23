using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Shared constants for the deprecation telemetry integration tests (task 024 / FR-03).
/// </summary>
internal static class PlaybookByNameDeprecationTestConstants
{
    public const string TestUserOid = "11111111-1111-1111-1111-111111111111";
    public const string TestBearerToken = "playbook-by-name-deprecation-test-bearer";
    public const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string KnownGoodName = "Chat-Summarize Document";
    public const string TestUserAgent = "spaarke-deprecation-test/1.0";
}

/// <summary>
/// WebApplicationFactory for <c>GET /api/ai/playbooks/by-name/{name}</c> deprecation
/// telemetry tests (task 024 / FR-03).
/// </summary>
/// <remarks>
/// <para>
/// Substitutes <see cref="IPlaybookService"/> with a Moq instance whose
/// <see cref="IPlaybookService.GetByNameAsync(string, CancellationToken)"/> returns a
/// canned <see cref="PlaybookResponse"/> so the test focuses on telemetry side effects
/// (warning log + Activity tag), not Dataverse resolution.
/// </para>
/// <para>
/// Captures logs via <see cref="CapturingLoggerProvider"/> registered on the host's
/// <see cref="ILoggerFactory"/> so tests can assert the warning entry's exact shape
/// (ADR-015 tier-1 audit — name, tenant id, user-agent only).
/// </para>
/// </remarks>
public class PlaybookByNameDeprecationTestFixture : WebApplicationFactory<Program>
{
    /// <summary>In-memory log capture — cleared between tests.</summary>
    public CapturingLoggerProvider LoggerProvider { get; } = new CapturingLoggerProvider();

    /// <summary>Reset state between tests.</summary>
    public void Reset() => LoggerProvider.Reset();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Minimal config so module validators pass. Mirrors PlaybookByIdIntegrationTestFixture.
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["SpeAdmin:KeyVaultUri"] = "https://test-keyvault.vault.azure.net/",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["Redis:Enabled"] = "false",
                ["OfficeRateLimit:Enabled"] = "false",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
            };
            config.AddInMemoryCollection(settings);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // In-memory cache.
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Fake JWT auth — tenant id from X-Test-Tenant header.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = PlaybookByNameDeprecationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PlaybookByNameDeprecationFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, PlaybookByNameDeprecationFakeAuthHandler>(
                PlaybookByNameDeprecationFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = PlaybookByNameDeprecationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PlaybookByNameDeprecationFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            // Substitute IDataverseService (production impl tries to MSAL token-acquire).
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(new Mock<IDataverseService>(MockBehavior.Loose).Object);

            // Substitute IPlaybookService with a Moq instance returning a canned response.
            services.RemoveAll<IPlaybookService>();
            var playbookServiceMock = new Mock<IPlaybookService>(MockBehavior.Loose);
            playbookServiceMock
                .Setup(s => s.GetByNameAsync(
                    PlaybookByNameDeprecationTestConstants.KnownGoodName,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaybookResponse
                {
                    Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Name = PlaybookByNameDeprecationTestConstants.KnownGoodName,
                    Description = "Test playbook for deprecation telemetry.",
                    PlaybookCode = "PB-CHAT-SUMMARIZE",
                    ConfigJson = "{}",
                    IsActive = true,
                    StatusCode = 1,
                    CreatedOn = DateTime.UtcNow.AddDays(-30),
                    ModifiedOn = DateTime.UtcNow.AddDays(-1),
                    OwnerId = Guid.NewGuid(),
                });
            services.AddSingleton(playbookServiceMock.Object);

            // Capture logs in-memory so tests can assert warning entries.
            services.AddSingleton<ILoggerProvider>(LoggerProvider);
        });
    }

    /// <summary>
    /// Creates an authenticated HttpClient with a deterministic User-Agent header so the
    /// telemetry test can assert the value flows through to the log entry.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string tenantId = PlaybookByNameDeprecationTestConstants.TenantA,
        string userAgent = PlaybookByNameDeprecationTestConstants.TestUserAgent)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", PlaybookByNameDeprecationTestConstants.TestBearerToken);
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }
}

/// <summary>
/// Fake auth handler for the deprecation telemetry tests. Reads X-Test-Tenant for the
/// <c>tid</c> claim.
/// </summary>
internal sealed class PlaybookByNameDeprecationFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlaybookByNameDeprecationFakeAuth";

    public PlaybookByNameDeprecationFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var tenantId = Request.Headers.TryGetValue("X-Test-Tenant", out var tidHeader)
            ? tidHeader.ToString()
            : PlaybookByNameDeprecationTestConstants.TenantA;

        var claims = new[]
        {
            new Claim("oid", PlaybookByNameDeprecationTestConstants.TestUserOid),
            new Claim("tid", tenantId),
            new Claim(ClaimTypes.NameIdentifier, PlaybookByNameDeprecationTestConstants.TestUserOid),
            new Claim(ClaimTypes.Name, "Playbook By-Name Deprecation Test User"),
            new Claim("name", "Playbook By-Name Deprecation Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// In-memory <see cref="ILoggerProvider"/> that captures log entries for assertion. Thread-safe.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public void Reset()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    public ILogger CreateLogger(string categoryName) =>
        new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<CapturedLogEntry> _entries;

        public CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries)
        {
            _category = category;
            _entries = entries;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _entries.Enqueue(new CapturedLogEntry(_category, logLevel, message, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

/// <summary>Captured log entry shape for assertion.</summary>
public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
