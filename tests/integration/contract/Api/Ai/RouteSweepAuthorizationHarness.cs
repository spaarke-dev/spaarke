using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Tests.Api.Ai;

// =====================================================================================================
// Shared harness for the unified-access-control-r2 task 163 route-authorization contract tests
// (RagEndpointsAuthorizationContractTests, InsightsRouteAuthorizationContractTests,
// WorkspaceAiSummaryAuthorizationContractTests). One in-process host boots the REAL Program — real
// Map*Endpoints, real declaration filters, real AuthorizationService, real AiAuthorizationService — and
// substitutes only module boundaries (ADR-038): what Dataverse would answer about the caller's rights
// (IAccessDataSource), the RAG / file-indexing / document / entity / Insights / routing services.
// =====================================================================================================

/// <summary>
/// What Dataverse would answer about THIS caller's rights — deny-by-default, recorded, and able to fault.
/// </summary>
/// <remarks>
/// A superset of <see cref="ProgrammableRecordAccessSource"/> (task 063), which this task cannot reuse as
/// is: the per-row trim goes through the real <see cref="AiAuthorizationService"/>, which asks
/// <see cref="IAccessDataSource.GetUserAccessAsync"/> (that source deliberately throws there), and the
/// fail-closed criteria need a seam that FAULTS. Anything unstated is <see cref="AccessRights.None"/> — a
/// stub that allowed unstated cases would reproduce the defect inside the harness.
/// </remarks>
public sealed class CallerAccessSeam : IAccessDataSource
{
    private readonly Dictionary<Guid, AccessRights> _rights = [];
    private AccessRights? _everything;

    /// <summary>Every record-scoped question actually asked (entity-generic path), in order.</summary>
    public List<(string UserId, string EntitySetName, Guid RecordId, string? UserAccessToken)> RecordChecks { get; } = [];

    /// <summary>Every document-scoped question actually asked (sprk_documents path), in order.</summary>
    public List<(string UserId, string ResourceId, string? UserAccessToken)> DocumentChecks { get; } = [];

    /// <summary>When set, every question throws — the fault every fail-closed path must turn into a deny.</summary>
    public bool ThrowOnCheck { get; set; }

    /// <summary>
    /// When set, the record-scoped question with this 1-based ordinal and every later one throw — so a test can let an
    /// earlier question (e.g. a route's Read gate) answer and fault a later one (e.g. the Write question).
    /// </summary>
    public int? ThrowFromCheckNumber { get; set; }

    public CallerAccessSeam Grant(Guid recordId, AccessRights rights)
    {
        _rights[recordId] = rights;
        return this;
    }

    /// <summary>The "reader of everything" baseline that pre-existing route tests run under.</summary>
    public CallerAccessSeam GrantEverything(AccessRights rights)
    {
        _everything = rights;
        return this;
    }

    public static CallerAccessSeam ReaderOfEverything() => new CallerAccessSeam().GrantEverything(AccessRights.Read);

    public void Reset()
    {
        _rights.Clear();
        _everything = null;
        ThrowOnCheck = false;
        ThrowFromCheckNumber = null;
        RecordChecks.Clear();
        DocumentChecks.Clear();
    }

    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
    {
        RecordChecks.Add((userId, entitySetName, recordId, userAccessToken));
        if (ThrowOnCheck || RecordChecks.Count >= ThrowFromCheckNumber)
        {
            throw new InvalidOperationException("simulated RetrievePrincipalAccess fault");
        }

        return Task.FromResult(Snapshot(userId, recordId.ToString(), RightsFor(recordId)));
    }

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
    {
        DocumentChecks.Add((userId, resourceId, userAccessToken));
        if (ThrowOnCheck)
        {
            throw new InvalidOperationException("simulated RetrievePrincipalAccess fault");
        }

        var rights = Guid.TryParse(resourceId, out var id) ? RightsFor(id) : AccessRights.None;
        return Task.FromResult(Snapshot(userId, resourceId, rights));
    }

    private AccessRights RightsFor(Guid recordId) =>
        _rights.TryGetValue(recordId, out var rights) ? rights : _everything ?? AccessRights.None;

    private static AccessSnapshot Snapshot(string userId, string resourceId, AccessRights rights) =>
        new() { UserId = userId, ResourceId = resourceId, AccessRights = rights };
}

/// <summary>
/// Playbook node shapes for hosts that drive <c>POST /api/insights/ask</c> (task 163): the route reads the node list to
/// decide whether the run can write to its subject (owner round 16 item 1).
/// </summary>
public static class RouteSweepNodeShapes
{
    /// <summary>
    /// The repo's predict-matter-cost shape — no node that can write references the subject's <c>{{matterId}}</c> — so a
    /// reader of the subject may run it. For wire-contract hosts whose callers are readers.
    /// </summary>
    public static INodeService NonPersistingNodeService()
    {
        var nodes = new Mock<INodeService>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Sprk.Bff.Api.Models.Ai.PlaybookNodeDto
                {
                    SprkExecutortype = Sprk.Bff.Api.Services.Ai.Nodes.ExecutorType.LiveFact,
                    ConfigJson = "{\"subject\":\"matter:{{matterId}}\"}",
                },
                new Sprk.Bff.Api.Models.Ai.PlaybookNodeDto
                {
                    SprkExecutortype = Sprk.Bff.Api.Services.Ai.Nodes.ExecutorType.AgentService,
                    ConfigJson = "{\"tenantId\":\"{{tenantId}}\"}",
                },
            ]);
        return nodes.Object;
    }
}

/// <summary>
/// The per-row trim seam: the REAL <see cref="AiAuthorizationService"/> (as the caller, over
/// <see cref="CallerAccessSeam"/>), with a switch that makes the whole call fault and a record of every
/// id list it was asked about.
/// </summary>
public sealed class SwitchableAiAuthorizationService : IAiAuthorizationService
{
    private readonly AiAuthorizationService _inner;

    public SwitchableAiAuthorizationService(IAccessDataSource accessDataSource, ILogger<AiAuthorizationService> logger)
    {
        _inner = new AiAuthorizationService(accessDataSource, logger);
    }

    public List<IReadOnlyList<Guid>> Calls { get; } = [];

    public bool ThrowOnCall { get; set; }

    public void Reset()
    {
        Calls.Clear();
        ThrowOnCall = false;
    }

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, IReadOnlyList<Guid> documentIds, HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        Calls.Add(documentIds.ToList());
        if (ThrowOnCall)
        {
            throw new InvalidOperationException("simulated authorization seam fault");
        }

        return _inner.AuthorizeAsync(user, documentIds, httpContext, cancellationToken);
    }
}

/// <summary>
/// The shared in-process host for the task 163 route tests. Substitutes ONLY module boundaries.
/// </summary>
public sealed class RouteSweepAuthorizationFixture : WebApplicationFactory<Program>
{
    /// <summary>The token's tenant — contains hex letters so a case change in the body is observable.</summary>
    public const string CallerTenantId = "16333333-aaaa-5555-bbbb-7777cccc7777";

    public const string CallerObjectId = "16388888-9999-aaaa-bbbb-cccccccccccc";

    public const string BearerToken = "task-163-caller-token";

    public const string TenantDefaultIndexName = "tenant-default-index";

    public CallerAccessSeam Access { get; } = new();

    public Mock<IRagService> Rag { get; } = new(MockBehavior.Loose);

    public Mock<IFileIndexingService> FileIndexing { get; } = new(MockBehavior.Loose);

    public Mock<IDocumentDataverseService> Documents { get; } = new(MockBehavior.Loose);

    public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Loose);

    public Mock<IInsightsAi> InsightsAi { get; } = new(MockBehavior.Loose);

    public Mock<IConsumerRoutingService> Routing { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// A playbook's node list — the source the route filter reads to decide whether a run can write (task 163, owner
    /// round 16 item 1). Reset to "no playbook has nodes" (an empty list, which the one rule treats as CAN write — fail
    /// closed), so a test states the node shape it relies on.
    /// </summary>
    public Mock<INodeService> Nodes { get; } = new(MockBehavior.Loose);

    /// <summary>Every request that actually reached the file-indexing pipeline.</summary>
    public List<FileIndexRequest> IndexedFiles { get; } = [];

    /// <summary>The trim seam, resolved from the host so tests can switch and inspect it.</summary>
    public SwitchableAiAuthorizationService AiAuthorization => Services.GetRequiredService<SwitchableAiAuthorizationService>();

    public void ResetBoundaries()
    {
        Access.Reset();
        Rag.Reset();
        FileIndexing.Reset();
        Documents.Reset();
        Entities.Reset();
        InsightsAi.Reset();
        Routing.Reset();
        Nodes.Reset();
        Nodes.Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        IndexedFiles.Clear();
        AiAuthorization.Reset();

        FileIndexing
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) => IndexedFiles.Add(req))
            .ReturnsAsync((FileIndexRequest req, HttpContext _, CancellationToken _) =>
                FileIndexingResult.Succeeded(chunksIndexed: 2, duration: TimeSpan.FromMilliseconds(5), documentId: req.DocumentId));
    }

    /// <summary>
    /// A client for this caller. <paramref name="roles"/> become "roles" claims (e.g. "SystemAdmin");
    /// <paramref name="withBearer"/> false authenticates WITHOUT forwarding a bearer token (the
    /// "no caller token" fail-closed case); <paramref name="withOid"/> false omits the oid claim.
    /// </summary>
    public HttpClient CreateCallerClient(string? roles = null, bool withBearer = true, bool withOid = true, string? accept = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (withBearer)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", BearerToken);
        }
        else
        {
            client.DefaultRequestHeaders.Add(RouteSweepFakeAuthHandler.AuthenticateWithoutBearerHeader, "1");
        }

        if (!withOid)
        {
            client.DefaultRequestHeaders.Add(RouteSweepFakeAuthHandler.OmitOidHeader, "1");
        }

        if (!string.IsNullOrEmpty(roles))
        {
            client.DefaultRequestHeaders.Add(RouteSweepFakeAuthHandler.RolesHeader, roles);
        }

        if (!string.IsNullOrEmpty(accept))
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        }

        return client;
    }

    /// <summary>
    /// The problem body with the per-request ids removed — what "identical apart from correlation/trace
    /// ids" compares.
    /// </summary>
    public static async Task<string> NormalizedProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var kept = doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("correlationId" or "traceId"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|{string.Join("|", kept)}";
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["Analysis:UseStubResolver"] = "true",
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
                ["AiSearch:KnowledgeIndexName"] = TenantDefaultIndexName,
            });
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
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RouteSweepFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RouteSweepFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RouteSweepFakeAuthHandler>(RouteSweepFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = RouteSweepFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RouteSweepFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverseService = new Mock<IDataverseService>();
            dataverseService.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseService.Object);

            // ── The module boundaries this harness substitutes. ──
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);

            services.AddSingleton<SwitchableAiAuthorizationService>();
            services.RemoveAll<IAiAuthorizationService>();
            services.AddSingleton<IAiAuthorizationService>(sp => sp.GetRequiredService<SwitchableAiAuthorizationService>());

            services.RemoveAll<IRagService>();
            services.AddSingleton(Rag.Object);

            services.RemoveAll<IFileIndexingService>();
            services.AddSingleton(FileIndexing.Object);

            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(Documents.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(Entities.Object);

            services.RemoveAll<IInsightsAi>();
            services.AddSingleton(InsightsAi.Object);

            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(Routing.Object);

            services.RemoveAll<INodeService>();
            services.AddSingleton(Nodes.Object);
        });
    }
}

/// <summary>
/// Fake auth handler for the task 163 harness. Authenticates a request that carries a bearer token, or the
/// explicit <see cref="AuthenticateWithoutBearerHeader"/> (so the "authenticated, but no caller token was
/// forwarded" fail-closed case is reachable). Emits oid + tid, and "roles" from <see cref="RolesHeader"/>.
/// </summary>
internal sealed class RouteSweepFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RouteSweep163FakeAuth";
    public const string RolesHeader = "X-Test-Roles";
    public const string OmitOidHeader = "X-Test-Omit-Oid";
    public const string AuthenticateWithoutBearerHeader = "X-Test-Authenticated-Without-Bearer";

    public RouteSweepFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization") && !Request.Headers.ContainsKey(AuthenticateWithoutBearerHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var claims = new List<Claim>
        {
            new("tid", RouteSweepAuthorizationFixture.CallerTenantId),
            new(ClaimTypes.Name, "Task 163 Test User"),
        };

        if (!Request.Headers.ContainsKey(OmitOidHeader))
        {
            claims.Add(new Claim("oid", RouteSweepAuthorizationFixture.CallerObjectId));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, RouteSweepAuthorizationFixture.CallerObjectId));
        }

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("roles", role));
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
