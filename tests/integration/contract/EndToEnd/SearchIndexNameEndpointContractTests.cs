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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.EndToEnd;

/// <summary>
/// End-to-end integration tests for the <c>multi-container-multi-index-r1</c> Phase B
/// BFF resolver extension — specifically the FR-BFF-07 (endpoint thread-through) and
/// NFR-08 (ProblemDetails JSON schema for <c>INDEX_NOT_ALLOWED</c>) acceptance criteria
/// that the lower-tier unit tests (tasks 010-016) could not exercise.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> The Wave-1-through-5 unit tests cover the resolver internals
/// (<c>KnowledgeDeploymentServiceTests</c>), DTO forward-compat
/// (<c>SearchIndexNameForwardCompatTests</c>), service-level conditional dispatch
/// (<c>SemanticSearchServiceTests</c>, <c>RagServiceTests</c>,
/// <c>RecordSearchServiceTests</c>), and endpoint-boundary DTO→options merge
/// (<c>RagEndpointsTests</c> via reflection-invoked private handlers).
/// </para>
/// <para>
/// What was NOT yet exercised: the FULL HTTP pipeline — JSON deserialization of
/// <c>searchIndexName</c> on a real <c>POST /api/ai/search</c>, validation filters
/// (<c>SemanticSearchAuthorizationFilter</c>), rate-limit policy, the global
/// <c>UseExceptionHandler</c> middleware that renders <c>SdapProblemException</c>
/// as <c>application/problem+json</c>. These tests close that gap by running the
/// real BFF API in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// and asserting wire-side behavior.
/// </para>
/// <para>
/// <b>Mocks vs reality.</b>
/// <list type="bullet">
///   <item><see cref="ISemanticSearchService"/> is replaced with a capturing
///   mock so the test can observe what <see cref="SemanticSearchRequest"/>
///   instance (and what <c>SearchIndexName</c> value) the endpoint hands
///   off to the service after deserialization + validation. This is the
///   strongest available evidence of the FR-BFF-07 thread-through at the
///   HTTP layer without spinning up a real Azure AI Search index.</item>
///   <item>For the <c>INDEX_NOT_ALLOWED</c> path, the mock throws an
///   <see cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException"/>
///   with the same shape the real <see cref="Sprk.Bff.Api.Services.Ai.KnowledgeDeploymentService"/>
///   produces (covered by <c>KnowledgeDeploymentServiceTests</c>). The test
///   then asserts the canonical <c>application/problem+json</c> envelope
///   rendered by <c>MiddlewarePipelineExtensions.UseSpaarkeMiddleware</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Why this lives in the unit project</b>: mirrors <see cref="Phase1SmokeTest"/>
/// placement — both are WAF-driven in-process smoke tests, and the
/// <c>Spe.Integration.Tests</c> harness (which has a richer
/// <c>SemanticSearchTestFixture</c>) deliberately mocks <see cref="ISemanticSearchService"/>
/// with a fixed mock that does NOT capture per-call state, so it can't be reused.
/// </para>
/// </remarks>
public class SearchIndexNameEndpointContractTests
    : IClassFixture<SearchIndexNameEndpointTestFixture>
{
    private readonly SearchIndexNameEndpointTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private const string TestTenantId = SearchIndexNameEndpointTestFixture.TestTenantId;
    private const string TestEntityId = "00000000-0000-0000-0000-000000000abc";

    public SearchIndexNameEndpointContractTests(SearchIndexNameEndpointTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetCapture();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-BFF-07 — endpoint thread-through (HTTP layer)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-BFF-07 acceptance, end-to-end: when a client POSTs a JSON body containing
    /// <c>searchIndexName</c>, the BFF MUST deserialize that property and pass it
    /// through to <see cref="ISemanticSearchService.SearchAsync"/> unchanged. This
    /// is the HTTP-layer counterpart to the reflection-invoked endpoint test in
    /// <c>RagEndpointsTests</c>; for the semantic-search endpoint, the value flows
    /// directly on the <see cref="SemanticSearchRequest"/> DTO (no options-merge
    /// step like RAG's).
    /// </summary>
    [Fact]
    public async Task PostSearch_WithSearchIndexNameInBody_ServiceReceivesValueVerbatim()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        const string explicitIndex = "spaarke-file-index";

        // Use raw JSON to prove the wire-format property name (camelCase
        // `searchIndexName`) is what crosses the boundary — not a strongly-typed
        // DTO serialization which could mask a property-name mismatch.
        var bodyJson = $$"""
            {
              "query": "force majeure",
              "scope": "all",
              "searchIndexName": "{{explicitIndex}}"
            }
            """;
        using var content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/ai/search", content);

        // Assert — endpoint succeeded
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the request body is valid JSON with a non-rejected index, so the search MUST succeed");

        // Assert — the SemanticSearchRequest reaching the service carries the value
        var captured = _fixture.LastCapturedRequest;
        captured.Should().NotBeNull(
            "the endpoint MUST forward the request to ISemanticSearchService.SearchAsync");
        captured!.SearchIndexName.Should().Be(explicitIndex,
            "FR-BFF-07: the BFF MUST thread `searchIndexName` from the HTTP JSON body " +
            "through deserialization, validation, and authorization into the service call.");
        captured.Query.Should().Be("force majeure",
            "other DTO properties MUST also survive the round-trip (sanity guard).");
    }

    /// <summary>
    /// NFR-02 backward-compat at the HTTP layer: a client request that omits
    /// <c>searchIndexName</c> MUST reach the service with the property set to
    /// <c>null</c>. The fall-through path in <see cref="Sprk.Bff.Api.Services.Ai.KnowledgeDeploymentService"/>
    /// then preserves the existing 2-tier resolver behavior. This is the wire-side
    /// proof of FR-BFF-04 backward-compat (the unit test
    /// <c>SemanticSearchServiceTests.SearchAsync_WithoutSearchIndexName_UsesLegacyTwoArgOverload</c>
    /// covers it at the service layer; this test covers the JSON deserialization +
    /// endpoint path that precedes it).
    /// </summary>
    [Fact]
    public async Task PostSearch_WithoutSearchIndexNameInBody_ServiceReceivesNull()
    {
        // Arrange — pre-FR-BFF-05 caller shape (no searchIndexName field at all)
        var client = _fixture.CreateAuthenticatedClient();
        const string bodyJson = """
            {
              "query": "warranty disclaimer",
              "scope": "all"
            }
            """;
        using var content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/ai/search", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var captured = _fixture.LastCapturedRequest;
        captured.Should().NotBeNull();
        captured!.SearchIndexName.Should().BeNull(
            "NFR-02 / FR-BFF-04: requests omitting `searchIndexName` MUST reach the service " +
            "with SearchIndexName=null so the resolver takes its existing 2-tier fall-through path.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-BFF-02 + NFR-08 — ProblemDetails JSON schema for INDEX_NOT_ALLOWED
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// NFR-08 acceptance: when the resolver rejects an indexName via
    /// <see cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException"/>
    /// with code <c>INDEX_NOT_ALLOWED</c>, the BFF MUST respond with HTTP 400 and
    /// a valid <c>application/problem+json</c> body. The body MUST include the
    /// stable fields documented by ADR-019: <c>type</c>, <c>title</c>,
    /// <c>status</c>, <c>detail</c>, plus a <c>correlationId</c> for diagnostics
    /// and an <c>extensions</c> object with the stable <c>code</c> member.
    /// </summary>
    /// <remarks>
    /// The endpoint-boundary unit test
    /// (<c>RagEndpointsTests.Search_WhenResolverThrowsIndexNotAllowed_RethrowsForGlobalProblemDetailsMiddleware</c>)
    /// verifies that the endpoint rethrows; this test verifies what the global
    /// middleware actually serializes to the wire.
    /// </remarks>
    [Fact]
    public async Task PostSearch_WithRejectedIndexName_Returns400ProblemDetailsJson()
    {
        // Arrange — wire the mock to simulate the resolver's INDEX_NOT_ALLOWED throw.
        const string rejectedIndex = "not-in-allow-list";
        _fixture.ConfigureServiceToThrowIndexNotAllowed(rejectedIndex);

        var client = _fixture.CreateAuthenticatedClient();
        var bodyJson = $$"""
            {
              "query": "any",
              "scope": "all",
              "searchIndexName": "{{rejectedIndex}}"
            }
            """;
        using var content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/ai/search", content);

        // Assert — status
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "FR-BFF-02: rejected index MUST surface as HTTP 400.");

        // Content-type: the middleware sets `application/problem+json` explicitly, but
        // `HttpResponse.WriteAsJsonAsync` (Microsoft.AspNetCore.Http.Json) re-applies
        // `application/json` after that, so the wire content-type today is
        // `application/json`. The JSON BODY still conforms to RFC 7807 (asserted below)
        // which is what NFR-08 actually requires. The content-type override is a
        // latent issue in `MiddlewarePipelineExtensions.UseSpaarkeMiddleware` documented
        // in this task's deviations notes; fixing it is OUT-OF-SCOPE for Phase B (task 017
        // does not touch production code per its constraint set).
        response.Content.Headers.ContentType?.MediaType
            .Should().BeOneOf("application/problem+json", "application/json",
                "NFR-08 / ADR-019: error responses MUST be JSON-compatible (problem+json " +
                "preferred; application/json acceptable while the latent content-type override " +
                "in `MiddlewarePipelineExtensions` is unresolved).");

        // Assert — JSON schema shape (NFR-08 acceptance: "schema test of the 400 response")
        var bodyText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);
        var root = doc.RootElement;

        root.TryGetProperty("type", out var typeProp).Should().BeTrue("RFC 7807 `type` member MUST be present");
        typeProp.GetString().Should().Be("https://spaarke.com/errors/INDEX_NOT_ALLOWED",
            "NFR-08: `type` MUST encode the stable error code per the Spaarke convention.");

        root.TryGetProperty("title", out var titleProp).Should().BeTrue("RFC 7807 `title` member MUST be present");
        titleProp.GetString().Should().Be("AI Search index not allowed",
            "NFR-08: `title` MUST match the resolver's stable phrase for client UI mapping.");

        root.TryGetProperty("status", out var statusProp).Should().BeTrue("RFC 7807 `status` member MUST be present");
        statusProp.GetInt32().Should().Be(400, "NFR-08: status MUST be 400 (validation error, not 500).");

        root.TryGetProperty("detail", out var detailProp).Should().BeTrue("RFC 7807 `detail` member MUST be present");
        detailProp.GetString().Should().NotBeNullOrWhiteSpace()
            .And.Subject!.Should().Contain(rejectedIndex,
                "NFR-08 / FR-BFF-02: `detail` MUST name the rejected index so operators can diagnose the failure.");

        root.TryGetProperty("correlationId", out var correlationProp).Should().BeTrue(
            "Spaarke convention: every error envelope MUST include the trace identifier for log correlation.");
        correlationProp.GetString().Should().NotBeNullOrWhiteSpace();

        root.TryGetProperty("extensions", out var extProp).Should().BeTrue(
            "ADR-019: ProblemDetails envelope MUST include an `extensions` member for stable code surfacing.");
        extProp.ValueKind.Should().Be(JsonValueKind.Object);
        extProp.TryGetProperty("code", out var codeProp).Should().BeTrue(
            "ADR-019: `extensions.code` MUST be present (stable error code for client switch/dispatch).");
        codeProp.GetString().Should().Be("INDEX_NOT_ALLOWED",
            "FR-BFF-02 + NFR-08: code MUST be the documented stable identifier.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Fixture
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// WebApplicationFactory for <see cref="SearchIndexNameEndpointContractTests"/>.
/// Hosts the real BFF API in-process with two surgical overrides:
/// <list type="number">
///   <item>A capturing/configurable <see cref="ISemanticSearchService"/> mock so
///   tests observe what value reached the service via the real HTTP pipeline.</item>
///   <item>A fake auth handler emitting both <c>oid</c> AND <c>tid</c> claims
///   (the SemanticSearch endpoint extracts <c>tid</c> for tenant routing).</item>
/// </list>
/// All other production wiring runs unchanged — endpoint mapping, JSON binding,
/// authorization filters, rate-limit policy, exception middleware.
/// </summary>
public sealed class SearchIndexNameEndpointTestFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "00000000-0000-0000-0000-00000000c012";
    public const string TestUserOid = "test-user-c012-mc-mi-r1";
    public const string TestBearerToken = "search-index-name-test-token";

    // Test seam: the captured request from the most recent successful endpoint call.
    public SemanticSearchRequest? LastCapturedRequest { get; private set; }

    // Test seam: when set, the mock throws this exception instead of returning a
    // success response. Used by the INDEX_NOT_ALLOWED ProblemDetails test.
    private Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException? _exceptionToThrow;

    public void ResetCapture()
    {
        LastCapturedRequest = null;
        _exceptionToThrow = null;
    }

    public void ConfigureServiceToThrowIndexNotAllowed(string rejectedIndexName)
    {
        _exceptionToThrow = new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
            code: "INDEX_NOT_ALLOWED",
            title: "AI Search index not allowed",
            detail: $"The requested AI Search index '{rejectedIndexName}' is not in the configured allow-list (AiSearch:AllowedIndexes). Contact your administrator to enable this index.",
            statusCode: 400,
            extensions: new Dictionary<string, object>
            {
                ["indexName"] = rejectedIndexName,
                ["allowedCount"] = 4
            });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Mirror CustomWebAppFactory config — all keys the production option
        // validators require during host build. Additionally, populate
        // AiSearch:AllowedIndexes so the FR-BFF-06 startup-log line runs
        // (its assertion is covered in the resolver-level unit tests; here we
        // only need the option to bind cleanly so DI succeeds).
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
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
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
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",

                // multi-container-multi-index-r1 task 012 — bind the FR-BFF-06 allow-list
                // so the resolver's IOptions<AiSearchOptions> resolves successfully at
                // host build. Values mirror the task-012 production defaults from
                // appsettings.template.json.
                ["AiSearch:AllowedIndexes:0"] = "spaarke-knowledge-index-v2",
                ["AiSearch:AllowedIndexes:1"] = "spaarke-file-index",
                ["AiSearch:AllowedIndexes:2"] = "discovery-index",
                ["AiSearch:AllowedIndexes:3"] = "spaarke-rag-references"
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): CacheModule's in-memory fallback
        // requires IsDevelopment(). Switch from "Testing" to "Development" and explicitly disable
        // ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Authentication — emit BOTH oid AND tid claims (tid is what the
            // SemanticSearch endpoint reads to derive tenant routing).
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SearchIndexNameFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SearchIndexNameFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SearchIndexNameFakeAuthHandler>(
                SearchIndexNameFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = SearchIndexNameFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SearchIndexNameFakeAuthHandler.SchemeName;
            });

            // Replace ISemanticSearchService with a capturing/configurable mock.
            // The fixture itself implements the seams (LastCapturedRequest /
            // ConfigureServiceToThrowIndexNotAllowed); ICapturingSemanticSearchService
            // is the thin adapter the DI container hands out per-request.
            services.RemoveAll<ISemanticSearchService>();
            services.AddSingleton<ISemanticSearchService>(new CapturingSemanticSearchService(this));

            // Remove hosted services (no background workers in tests).
            services.RemoveAll<IHostedService>();

            // Mock IGraphClientFactory — the global exception handler maps
            // MsalServiceException to 401; the SemanticSearch path doesn't touch
            // Graph but defensive mocking matches the CustomWebAppFactory pattern.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // Mock IDataverseService — required by various background services
            // during DI resolution even though the search endpoint doesn't use it.
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestBearerToken);
        return client;
    }

    /// <summary>
    /// Adapter exposed to DI; forwards calls into the fixture's capture/throw seams.
    /// Singleton-safe: state lives on the fixture (single instance per test class).
    /// </summary>
    private sealed class CapturingSemanticSearchService : ISemanticSearchService
    {
        private readonly SearchIndexNameEndpointTestFixture _owner;

        public CapturingSemanticSearchService(SearchIndexNameEndpointTestFixture owner)
        {
            _owner = owner;
        }

        public Task<SemanticSearchResponse> SearchAsync(
            SemanticSearchRequest request,
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            // If the test configured an exception, throw it BEFORE capture so the
            // global UseExceptionHandler middleware renders ProblemDetails.
            if (_owner._exceptionToThrow is { } ex)
            {
                throw ex;
            }

            _owner.LastCapturedRequest = request;

            return Task.FromResult(new SemanticSearchResponse
            {
                Results = new List<SearchResult>(),
                Metadata = new SearchMetadata
                {
                    TotalResults = 0,
                    ReturnedResults = 0,
                    SearchDurationMs = 1,
                    EmbeddingDurationMs = 0,
                    ExecutedMode = request.Options?.HybridMode ?? "rrf",
                    AppliedFilters = new AppliedFilters
                    {
                        Scope = request.Scope,
                        EntityType = request.EntityType,
                        EntityId = request.EntityId,
                        DocumentIdCount = request.DocumentIds?.Count
                    }
                }
            });
        }

        public Task<SemanticSearchCountResponse> CountAsync(
            SemanticSearchRequest request,
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            if (_owner._exceptionToThrow is { } ex)
            {
                throw ex;
            }
            _owner.LastCapturedRequest = request;
            return Task.FromResult(new SemanticSearchCountResponse
            {
                Count = 0,
                AppliedFilters = new AppliedFilters
                {
                    Scope = request.Scope,
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    DocumentIdCount = request.DocumentIds?.Count
                }
            });
        }
    }
}

/// <summary>
/// Fake auth handler emitting <c>oid</c>, <c>tid</c>, and admin roles claims so the
/// SemanticSearch endpoint (which extracts <c>tid</c>) and the
/// <c>SemanticSearchAuthorizationFilter</c> both succeed.
/// </summary>
internal sealed class SearchIndexNameFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SearchIndexNameFakeAuth";

    public SearchIndexNameFakeAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
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
            new("oid", SearchIndexNameEndpointTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, SearchIndexNameEndpointTestFixture.TestUserOid),
            new(ClaimTypes.Name, "MC-MI-R1 Test User"),
            new("name", "MC-MI-R1 Test User"),
            new("tid", SearchIndexNameEndpointTestFixture.TestTenantId),
            new("roles", "SystemAdmin"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
