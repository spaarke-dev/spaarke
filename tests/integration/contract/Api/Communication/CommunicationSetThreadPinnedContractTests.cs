using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// Endpoint contract tests for <c>PATCH /api/communications/threads/{threadId}/pin</c> (task 041 / FR-24).
/// Exercises the REAL wired stack — CommunicationAuthorizationFilter + the real
/// <see cref="CommunicationThreadReadService"/> (impersonated visibility check) + the real
/// <see cref="ThreadResolver"/> (the <c>sprk_ispinned</c> write) — over three mocked module boundaries only:
/// <see cref="IImpersonatedCommunicationQuery"/> (visibility), <see cref="ICallerSystemUserResolver"/> (caller
/// resolution), and <see cref="IGenericEntityService"/> (the Dataverse write). No <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI-registration test. Mirrors <c>CommunicationRenameThreadContractTests</c>'s shape (same auth path).
/// <para>
/// Covers the closed contract set: 401 (unauth), <b>403 (caller cannot see the thread — the negative-auth case,
/// the correctness-critical NFR-01 guard)</b>, 200 pin (sprk_ispinned=true), and 200 unpin (sprk_ispinned=false).
/// </para>
/// <para>
/// Deliberately NOT <c>IClassFixture</c>-shared (unlike <c>CommunicationRenameThreadContractTests</c>): each test
/// method needs to fully own the wildcard-matched <see cref="IImpersonatedCommunicationQuery"/> visibility mock (it
/// matches on entity set only, not on a specific thread id), so a fresh factory per test avoids execution-order-
/// dependent cross-test Setup bleed on the 403 vs 200 cases.
/// </para>
/// </summary>
public class CommunicationSetThreadPinnedContractTests : IDisposable
{
    private readonly CommunicationPinTestWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly Guid CallerSystemUserId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public CommunicationSetThreadPinnedContractTests()
    {
        _factory = new CommunicationPinTestWebAppFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task SetThreadPinned_WithoutAuth_Returns401()
    {
        using var anonFactory = CommunicationPinTestWebAppFactory.CreateAnonymous();
        var anonClient = anonFactory.CreateClient();

        var response = await anonClient.PatchAsJsonAsync(
            $"/api/communications/threads/{Guid.NewGuid()}/pin", new { pinned = true });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetThreadPinned_WhenCallerCannotSeeThread_Returns403AndDoesNotWrite()
    {
        var threadId = Guid.NewGuid();
        _factory.ResolveCaller(CallerSystemUserId);
        // The IMPERSONATED visibility projection returns ZERO rows — the caller cannot see this thread.
        _factory.SetThreadVisibleRows(Array.Empty<Dictionary<string, JsonElement>>());

        var response = await _client.PatchAsJsonAsync(
            $"/api/communications/threads/{threadId}/pin", new { pinned = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // No pin write is ever attempted for a thread the caller cannot see.
        _factory.EntityServiceMock.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetThreadPinned_WhenCallerCanSeeThread_Pinned_Returns200AndSetsIsPinnedTrue()
    {
        var threadId = Guid.NewGuid();
        _factory.ResolveCaller(CallerSystemUserId);
        _factory.SetThreadVisibleRows(new[]
        {
            new Dictionary<string, JsonElement>
            {
                ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(threadId.ToString()),
            },
        });

        Dictionary<string, object>? written = null;
        _factory.EntityServiceMock
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);

        var response = await _client.PatchAsJsonAsync(
            $"/api/communications/threads/{threadId}/pin", new { pinned = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("threadId").GetGuid().Should().Be(threadId);
        payload.GetProperty("isPinned").GetBoolean().Should().BeTrue();

        written.Should().NotBeNull();
        ((bool)written!["sprk_ispinned"]).Should().BeTrue();
        _factory.EntityServiceMock.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetThreadPinned_WhenCallerCanSeeThread_Unpinned_Returns200AndSetsIsPinnedFalse()
    {
        var threadId = Guid.NewGuid();
        _factory.ResolveCaller(CallerSystemUserId);
        _factory.SetThreadVisibleRows(new[]
        {
            new Dictionary<string, JsonElement>
            {
                ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(threadId.ToString()),
            },
        });

        Dictionary<string, object>? written = null;
        _factory.EntityServiceMock
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);

        var response = await _client.PatchAsJsonAsync(
            $"/api/communications/threads/{threadId}/pin", new { pinned = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("isPinned").GetBoolean().Should().BeFalse();

        written.Should().NotBeNull();
        ((bool)written!["sprk_ispinned"]).Should().BeFalse();
    }
}

/// <summary>
/// WebApplicationFactory for the set-thread-pinned contract tests. Mirrors
/// <c>CommunicationRenameTestWebAppFactory</c>'s host-config baseline exactly (same auth path, same three swapped
/// module boundaries).
/// </summary>
public sealed class CommunicationPinTestWebAppFactory : WebApplicationFactory<Program>
{
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();
    public Mock<IImpersonatedCommunicationQuery> ImpersonatedQueryMock { get; } = new();
    public Mock<ICallerSystemUserResolver> CallerResolverMock { get; } = new();

    private readonly bool _disableAuth;

    public CommunicationPinTestWebAppFactory() : this(disableAuth: false) { }

    private CommunicationPinTestWebAppFactory(bool disableAuth) => _disableAuth = disableAuth;

    public static CommunicationPinTestWebAppFactory CreateAnonymous() => new(disableAuth: true);

    /// <summary>Programs the caller resolver to resolve the given systemuserid.</summary>
    public void ResolveCaller(Guid systemUserId) =>
        CallerResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(systemUserId.ToString("D")));

    /// <summary>Programs the impersonated thread-visibility projection to return the given rows.</summary>
    public void SetThreadVisibleRows(IReadOnlyList<Dictionary<string, JsonElement>> rows) =>
        ImpersonatedQueryMock
            .Setup(q => q.QueryAsync(
                "sprk_communicationthreads", It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:ManagedIdentity:Enabled"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            if (_disableAuth)
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, CommPinDenyAllAuthHandler>("Test", _ => { });
            }
            else
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, CommPinAllowAllAuthHandler>("Test", _ => { });
            }

            services.AddDistributedMemoryCache();
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });

            services.RemoveAll<IHostedService>();

            // Swap the three module boundaries the pin path crosses.
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);
            services.RemoveAll<IImpersonatedCommunicationQuery>();
            services.AddSingleton(ImpersonatedQueryMock.Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddScoped(_ => CallerResolverMock.Object);

            // Avoid a real Dataverse boot (mirrors OfficeCommunicationsTestWebAppFactory).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }
}

internal sealed class CommPinAllowAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CommPinAllowAllAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("oid", "test-user-oid"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim("tid", "test-tenant-id"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}

internal sealed class CommPinDenyAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CommPinDenyAllAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }
}
