using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
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
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// Integration tests for <c>GET /api/workspace/state</c> (R6 Pillar 6a / D-C-03 / FR-33 / task 052).
/// </summary>
/// <remarks>
/// <para>
/// <b>What's tested in-process</b>: the full Minimal API pipeline — auth
/// (<c>RequireAuthorization()</c>), rate-limit policy registration
/// (<c>ai-context</c>), validation, ADR-019 ProblemDetails error shapes,
/// happy-path 200 with <see cref="WorkspaceStateResponse"/> envelope, empty-session
/// 200 with empty tabs list. <see cref="IWorkspaceStateService"/> is mocked so each
/// test asserts against deterministic service behavior.
/// </para>
/// <para>
/// <b>Coverage by POML acceptance criterion (4 criteria mapped here, others
/// architectural — verified by code review)</b>:
/// <list type="bullet">
///   <item>401 unauth — <see cref="GetState_Unauthenticated_Returns401"/></item>
///   <item>401 missing-tid (interpreted "403 tenant mismatch" branch — see code
///   doc comment in WorkspaceStateEndpoints.cs) —
///   <see cref="GetState_MissingTidClaim_Returns401"/></item>
///   <item>200 happy path with tabs —
///   <see cref="GetState_AuthenticatedWithTabs_Returns200WithTabsAndNullExtensionFields"/></item>
///   <item>200 empty session —
///   <see cref="GetState_AuthenticatedEmptySession_Returns200WithEmptyTabsAndNullExtensionFields"/></item>
/// </list>
/// 429 rate-limit + 400 bad-request not exercised at the per-request level —
/// 429 is shared <c>RateLimitingModule.OnRejected</c> contract (verified
/// architecturally by registering the policy); 400 missing-sessionId is a
/// trivial branch with no business logic.
/// </para>
/// <para>
/// <b>"403 tenant mismatch" deferral note</b>: the original POML lists 403 as a
/// distinct status. The endpoint's design (tenantId derived ONLY from the
/// caller's <c>tid</c> claim, never from a query param) makes a true cross-tenant
/// mismatch impossible to express. The closest equivalent — a malformed token
/// lacking <c>tid</c> — returns 401, consistent with the canonical
/// <c>InsightEndpoints.Ask</c> precedent. Documented in code + evidence note.
/// </para>
/// </remarks>
public class WorkspaceStateEndpointsTests : IClassFixture<WorkspaceStateEndpointsTestFixture>
{
    private readonly WorkspaceStateEndpointsTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public WorkspaceStateEndpointsTests(WorkspaceStateEndpointsTestFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // 401 UNAUTHENTICATED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetState_Unauthenticated_Returns401()
    {
        // Arrange — no Authorization header
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/state?sessionId=s-1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // 401 MISSING tid — the "tenant mismatch" surrogate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetState_MissingTidClaim_Returns401()
    {
        // Arrange — authenticated, but token has no 'tid' claim.
        // Reset shared mock so "Times.Never" reflects ONLY this test's invocations.
        _fixture.WorkspaceStateMock.Reset();
        using var client = _fixture.CreateAuthenticatedClientWithoutTenantClaim();

        // Act
        var response = await client.GetAsync("/api/workspace/state?sessionId=s-1");

        // Assert — handler explicitly checks for tid and returns 401 ProblemDetails
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tid", "ProblemDetails detail mentions the missing claim by name");

        // The IWorkspaceStateService should NOT have been called — the auth gate
        // short-circuits before the service is invoked.
        _fixture.WorkspaceStateMock.Verify(
            s => s.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "missing-tid request must short-circuit before service invocation");
    }

    // -------------------------------------------------------------------------
    // 200 HAPPY PATH — tabs returned, extension fields null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetState_AuthenticatedWithTabs_Returns200WithTabsAndNullExtensionFields()
    {
        // Arrange — mock returns two tabs for (tenant, session)
        _fixture.WorkspaceStateMock.Reset();
        var sampleTabs = new List<WorkspaceTab>
        {
            BuildSampleTab(
                id: "tab-1",
                widgetType: "Summary",
                sessionId: "s-happy",
                tenantId: WorkspaceStateEndpointsTestFixture.TestTenantId),
            BuildSampleTab(
                id: "tab-2",
                widgetType: "DocumentViewer",
                sessionId: "s-happy",
                tenantId: WorkspaceStateEndpointsTestFixture.TestTenantId),
        };

        _fixture.WorkspaceStateMock
            .Setup(s => s.GetTabsAsync(
                WorkspaceStateEndpointsTestFixture.TestTenantId,
                "s-happy",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleTabs);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        // Act
        var response = await client.GetAsync("/api/workspace/state?sessionId=s-happy");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Shape: { tabs: [...], activeTabId: null, userSelection: null }
        root.GetProperty("tabs").GetArrayLength().Should().Be(2, "service returned 2 tabs");
        root.GetProperty("activeTabId").ValueKind.Should().Be(JsonValueKind.Null,
            "C-G1 reserves activeTabId for Phase C-G2");
        root.GetProperty("userSelection").ValueKind.Should().Be(JsonValueKind.Null,
            "C-G1 reserves userSelection for Phase C-G6");

        // Sanity: first tab's id + widgetType serialize correctly
        var firstTab = root.GetProperty("tabs")[0];
        firstTab.GetProperty("id").GetString().Should().Be("tab-1");
        firstTab.GetProperty("widgetType").GetString().Should().Be("Summary");

        // Service was called with the caller's tenant + the requested session
        _fixture.WorkspaceStateMock.Verify(
            s => s.GetTabsAsync(
                WorkspaceStateEndpointsTestFixture.TestTenantId,
                "s-happy",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 200 EMPTY SESSION — { tabs: [], activeTabId: null, userSelection: null }
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetState_AuthenticatedEmptySession_Returns200WithEmptyTabsAndNullExtensionFields()
    {
        // Arrange — mock returns empty list
        _fixture.WorkspaceStateMock.Reset();
        _fixture.WorkspaceStateMock
            .Setup(s => s.GetTabsAsync(
                WorkspaceStateEndpointsTestFixture.TestTenantId,
                "s-empty",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkspaceTab>());

        using var client = _fixture.CreateAuthenticatedTenantClient();

        // Act
        var response = await client.GetAsync("/api/workspace/state?sessionId=s-empty");

        // Assert — 200 OK (NOT 404), empty array + null extension fields
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("tabs").GetArrayLength().Should().Be(0, "empty session has zero tabs");
        root.GetProperty("activeTabId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("userSelection").ValueKind.Should().Be(JsonValueKind.Null);

        _fixture.WorkspaceStateMock.Verify(
            s => s.GetTabsAsync(
                WorkspaceStateEndpointsTestFixture.TestTenantId,
                "s-empty",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 400 MISSING sessionId — validation branch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetState_MissingSessionId_Returns400()
    {
        // Arrange — authenticated but no sessionId query param
        // Reset is critical: WorkspaceStateMock is a singleton in DI, so prior tests
        // (e.g., happy path) have already invoked GetTabsAsync. We assert "never called
        // FROM THIS POINT FORWARD" by resetting just before the act.
        _fixture.WorkspaceStateMock.Reset();
        using var client = _fixture.CreateAuthenticatedTenantClient();

        // Act
        var response = await client.GetAsync("/api/workspace/state");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The service should NOT have been called — validation rejects pre-dispatch
        _fixture.WorkspaceStateMock.Verify(
            s => s.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Test helpers
    // =========================================================================

    private static WorkspaceTab BuildSampleTab(
        string id, string widgetType, string sessionId, string tenantId)
    {
        WorkspaceTabWidgetData widgetData = widgetType switch
        {
            "Summary" => new SummaryTabWidgetData
            {
                Body = "Sample summary body",
                Tldr = "TL;DR",
                HasUserEdits = false,
            },
            "DocumentViewer" => new DocumentViewerTabWidgetData
            {
                DocumentId = "doc-1",
                Filename = "sample.pdf",
                MimeType = "application/pdf",
                SizeBytes = 1024,
                HasSelection = false,
            },
            "Dashboard" => new DashboardTabWidgetData
            {
                LayoutId = "layout-1",
                DashboardName = "Sample Dashboard",
                LastViewedSection = null,
            },
            "Table" => new TableTabWidgetData
            {
                RowCount = 0,
                SortColumn = null,
                FilteredColumns = Array.Empty<string>(),
                SelectedRows = Array.Empty<string>(),
            },
            _ => throw new ArgumentException($"Unknown widgetType: {widgetType}"),
        };

        return new WorkspaceTab
        {
            Id = id,
            WidgetType = widgetType,
            WidgetData = widgetData,
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "user",
                CreatedBy = "test-user-oid",
                CreatedAt = "2026-06-09T12:00:00Z",
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = "matter-1",
                MatterName = "Sample Matter",
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-06-09T12:00:00Z",
            UpdatedAt = "2026-06-09T12:00:00Z",
        };
    }
}

// =============================================================================
// Test fixture — self-contained, mirrors InsightEndpointsTestFixture pattern
// =============================================================================

/// <summary>
/// In-process WebApplicationFactory fixture for <see cref="WorkspaceStateEndpointsTests"/>.
/// Replaces <see cref="IWorkspaceStateService"/> with a mock, registers a fake auth
/// handler that emits <c>oid</c> (+ optional <c>tid</c>) so the tid-extraction code
/// path can be exercised.
/// </summary>
public class WorkspaceStateEndpointsTestFixture : WebApplicationFactory<Program>
{
    public Mock<IWorkspaceStateService> WorkspaceStateMock { get; } = new(MockBehavior.Loose);

    public const string TestTenantId = "00000000-0000-0000-0000-0000000ten052";
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-0000ws00052";
    public const string TestBearerToken = "workspace-state-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Mirrored from WorkspaceTestFixture + InsightEndpointsTestFixture so all
            // option validators pass when the full BFF pipeline boots in-process.
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
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
                ["Redis:AllowInMemoryFallback"] = "true",
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
                ["CosmosPersistence:Endpoint"] = "https://test-cosmos.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): switch to Development for in-memory
        // cache fallback; disable ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace IDistributedCache with in-memory.
            var cacheDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDistributedCache));
            if (cacheDescriptor != null)
                services.Remove(cacheDescriptor);
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Replace IWorkspaceStateService with our mock — drives the handler
            // behavior deterministically.
            services.RemoveAll<IWorkspaceStateService>();
            services.AddSingleton(WorkspaceStateMock.Object);

            // Remove background hosted services that depend on external infrastructure.
            services.RemoveAll<IHostedService>();

            // Mock IDataverseService so incidental Dataverse-touching registrations
            // don't blow up at construction time.
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);
        });
    }

    /// <summary>
    /// Authenticated client whose token includes a <c>tid</c> claim — the standard
    /// tenant-user path. The handler reads <c>tid</c> to scope the state lookup.
    /// </summary>
    public HttpClient CreateAuthenticatedTenantClient()
        => CreateClientWithTid(includeTid: true, includeAuth: true);

    /// <summary>
    /// Authenticated client whose token has NO <c>tid</c> claim — exercises the
    /// 401-missing-tid path (the surrogate for the original POML's "403 tenant
    /// mismatch" branch, which is impossible by design — no query-param override
    /// of tenant exists).
    /// </summary>
    public HttpClient CreateAuthenticatedClientWithoutTenantClaim()
        => CreateClientWithTid(includeTid: false, includeAuth: true);

    /// <summary>
    /// Client with NO Authorization header — exercises the <c>RequireAuthorization()</c>
    /// 401 path.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
        => CreateClientWithTid(includeTid: true, includeAuth: false);

    private HttpClient CreateClientWithTid(bool includeTid, bool includeAuth)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new WorkspaceStateAuthOptions(includeTid));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = WorkspaceStateFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = WorkspaceStateFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, WorkspaceStateFakeAuthHandler>(
                    WorkspaceStateFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = WorkspaceStateFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = WorkspaceStateFakeAuthHandler.SchemeName;
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (includeAuth)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestBearerToken);
        }

        return client;
    }
}

/// <summary>
/// Toggle injected into the test auth handler to control whether the test
/// principal carries a <c>tid</c> claim. Required because the endpoint requires
/// <c>tid</c> in addition to <c>oid</c>.
/// </summary>
internal sealed class WorkspaceStateAuthOptions
{
    public bool IncludeTid { get; }
    public WorkspaceStateAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

/// <summary>
/// Fake auth handler that emits <c>oid</c> (+ optional <c>tid</c>) for workspace
/// state endpoint tests. Mirrors the InsightsAskTenantFakeAuthHandler pattern.
/// </summary>
internal sealed class WorkspaceStateFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "WorkspaceStateFakeAuth";

    private readonly WorkspaceStateAuthOptions _opts;

    public WorkspaceStateFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        WorkspaceStateAuthOptions opts)
        : base(options, logger, encoder)
    {
        _opts = opts;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        var claims = new List<Claim>
        {
            new("oid", WorkspaceStateEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, WorkspaceStateEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Workspace State Test User"),
            new("name", "Workspace State Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", WorkspaceStateEndpointsTestFixture.TestTenantId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
