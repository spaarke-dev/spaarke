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
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// Endpoint contract tests for <c>POST /api/communications/threads</c> (R3 UAT 2026-07-23 item 9) — create a
/// NEW named, record-anchored thread. Exercises the REAL wired stack (CommunicationAuthorizationFilter + the
/// real <see cref="Sprk.Bff.Api.Services.Communication.ThreadResolver"/> create path) over two mocked module
/// boundaries: <see cref="ICallerSystemUserResolver"/> (caller resolution) and <see cref="IGenericEntityService"/>
/// (the Dataverse create). Covers the closed contract set: 401 (unauth), 403 (caller unresolved), 400 (missing
/// regarding), and 200 (happy path — Record-Anchored thread owned by the caller with the denormalized ADR-024
/// regarding pointer + a user-provided name stamped Edited).
/// </summary>
public class CommunicationCreateRecordThreadContractTests : IClassFixture<CommunicationCreateThreadTestWebAppFactory>
{
    private const int ThreadTypeRecordAnchored = 100000000;

    private readonly CommunicationCreateThreadTestWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly Guid CallerSystemUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public CommunicationCreateRecordThreadContractTests(CommunicationCreateThreadTestWebAppFactory factory)
    {
        _factory = factory;
        // The factory (IClassFixture) is shared across the 4 tests, so its mocks accumulate invocations.
        // Clear them per test (tests within a class run sequentially) so each `Verify(Times.Never)` counts
        // only its own test's invocations, not a prior test's happy-path CreateAsync.
        _factory.EntityServiceMock.Invocations.Clear();
        _factory.CallerResolverMock.Invocations.Clear();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateRecordThread_WithoutAuth_Returns401()
    {
        using var anonFactory = CommunicationCreateThreadTestWebAppFactory.CreateAnonymous();
        var anonClient = anonFactory.CreateClient();

        var response = await anonClient.PostAsJsonAsync(
            "/api/communications/threads",
            new { regardingEntityType = "sprk_matter", regardingRecordId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRecordThread_WhenCallerUnresolved_Returns403AndDoesNotCreate()
    {
        _factory.ResolveCallerUnresolved();

        var response = await _client.PostAsJsonAsync(
            "/api/communications/threads",
            new { regardingEntityType = "sprk_matter", regardingRecordId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.EntityServiceMock.Verify(
            e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRecordThread_WithEmptyRegardingRecordId_Returns400()
    {
        _factory.ResolveCaller(CallerSystemUserId);

        var response = await _client.PostAsJsonAsync(
            "/api/communications/threads",
            new { regardingEntityType = "sprk_matter", regardingRecordId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.EntityServiceMock.Verify(
            e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRecordThread_WithValidRegarding_Returns200_CreatesRecordAnchoredThreadOwnedByCaller()
    {
        var recordId = Guid.NewGuid();
        var newThreadId = Guid.NewGuid();
        _factory.ResolveCaller(CallerSystemUserId);

        DataverseEntity? created = null;
        _factory.EntityServiceMock
            .Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => created = entity)
            .ReturnsAsync(newThreadId);

        var response = await _client.PostAsJsonAsync(
            "/api/communications/threads",
            new
            {
                name = "  Discovery strategy  ",
                regardingEntityType = "sprk_matter",
                regardingRecordId = recordId,
                regardingRecordName = "Acme v Widgets",
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("threadId").GetGuid().Should().Be(newThreadId);

        created.Should().NotBeNull();
        created!.LogicalName.Should().Be("sprk_communicationthread");
        ((string)created["sprk_name"]).Should().Be("Discovery strategy"); // trimmed
        created.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeRecordAnchored);
        // Owner = the server-resolved caller so the new thread is visible in the caller's all-mode list.
        created.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(CallerSystemUserId);
        // TYPED ADR-024 regarding lookup — the exact field the by-regarding read filters on (sprk_matter →
        // sprk_regardingmatter via RegardingFieldMap). RB (R3 UAT 2026-07-24): the create previously wrote a
        // NON-EXISTENT 'sprk_regardingrecordtype' text attribute → Dataverse InvalidOperationException (500).
        // This asserts the typed lookup is set and the bogus text attribute is NOT written.
        created.GetAttributeValue<EntityReference>("sprk_regardingmatter").LogicalName.Should().Be("sprk_matter");
        created.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(recordId);
        created.Contains("sprk_regardingrecordtype").Should().BeFalse();
        // Denormalized display pointers (these text attributes DO exist on the thread).
        ((string)created["sprk_regardingrecordid"]).Should().Be(recordId.ToString());
        ((string)created["sprk_regardingrecordname"]).Should().Be("Acme v Widgets");
        // A user-provided name is Edited so the auto re-derive never overwrites it.
        ((bool)created["sprk_nameisautoderived"]).Should().BeFalse();
    }
}

/// <summary>
/// WebApplicationFactory for the create-record-thread contract tests. Mirrors the rename factory's host-config
/// baseline; swaps <see cref="ICallerSystemUserResolver"/> (caller resolution) and exposes the
/// <see cref="IGenericEntityService"/> mock to assert the create write.
/// </summary>
public sealed class CommunicationCreateThreadTestWebAppFactory : WebApplicationFactory<Program>
{
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();
    public Mock<ICallerSystemUserResolver> CallerResolverMock { get; } = new();

    private readonly bool _disableAuth;

    public CommunicationCreateThreadTestWebAppFactory() : this(disableAuth: false) { }

    private CommunicationCreateThreadTestWebAppFactory(bool disableAuth) => _disableAuth = disableAuth;

    public static CommunicationCreateThreadTestWebAppFactory CreateAnonymous() => new(disableAuth: true);

    public void ResolveCaller(Guid systemUserId) =>
        CallerResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(systemUserId.ToString("D")));

    public void ResolveCallerUnresolved() =>
        CallerResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("caller not mapped to a systemuser"));

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
                ["Graph:UseManagedIdentity"] = "false",
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
            if (_disableAuth)
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, CommCreateThreadDenyAllAuthHandler>("Test", _ => { });
            }
            else
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, CommCreateThreadAllowAllAuthHandler>("Test", _ => { });
            }

            services.AddDistributedMemoryCache();
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });

            services.RemoveAll<IHostedService>();

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddScoped(_ => CallerResolverMock.Object);

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }
}

internal sealed class CommCreateThreadAllowAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CommCreateThreadAllowAllAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("oid", "test-user-oid"),
            new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim("tid", "test-tenant-id"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}

internal sealed class CommCreateThreadDenyAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CommCreateThreadDenyAllAuthHandler(
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
