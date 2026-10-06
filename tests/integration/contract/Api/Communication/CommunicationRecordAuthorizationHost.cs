using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Services.Identity;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// In-process host over the REAL <c>MapCommunicationEndpoints</c> / <c>MapCommunicationTemplateEndpoints</c> (task 161).
/// Substitutes only the module boundaries the communication contract tests already use (ADR-038): the caller
/// resolver, the app-only entity service, the impersonated query, the identity resolver, the delegated user client,
/// the access data source behind the REAL AuthorizationService, the virtual seams of CallerRecordAccessProbe, the
/// channel senders, the document metadata service, the association rung set, the impersonated record-write
/// boundary (<c>IFieldMappingDataverseService</c>) and the SPE download. No HTTP mocks.
/// </summary>
public sealed class CommunicationRecordAuthorizationHost : WebApplicationFactory<Program>
{
    public static readonly Guid CallerSystemUserId = Guid.Parse("16116116-1611-6116-1161-161161161161");
    public const string CallerToken = "caller-token-161";

    public Mock<IGenericEntityService> Entities { get; } = new();
    public Mock<ICallerSystemUserResolver> Callers { get; } = new();
    public FakeImpersonatedQuery Query { get; } = new();
    public Mock<ISystemUserIdentityResolver> Identity { get; } = new();
    public RecordingProbe Probe { get; } = new();
    public RecordingAccessDataSource Access { get; } = new();
    public Mock<IDataverseUserClient> UserClient { get; } = new();
    public RecordingChannelSender EmailSender { get; } = new(CommunicationType.Email);
    public RecordingChannelSender MessageSender { get; } = new(CommunicationType.Message);
    public Mock<IDocumentDataverseService> Documents { get; } = new();
    public Mock<ICommunicationDataverseService> CommunicationData { get; } = new();
    public ScriptedRung Rung { get; } = new();
    public Mock<IEmailTemplateService> Templates { get; } = new();
    public Mock<IGraphClientFactory> Graph { get; } = new();

    /// <summary>The impersonated record-write boundary behind <c>IActionSeam.UpdateRecordAsync</c> (the Job B/C writes).</summary>
    public Mock<IFieldMappingDataverseService> FieldMapping { get; } = new();

    /// <summary>
    /// Task 146's ONE owner resolver at its module boundary (sweep integration): the app-only rows these routes write
    /// (messages, review/audit rows, tasks, threads) are owned by the team it names. Ownership is not this suite's subject —
    /// 146's own tests drive the real resolver — so a fixed team answers, and every authorized path completes as it did
    /// before 146 landed.
    /// </summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble Ownership { get; } = new();

    public List<(string DriveId, string ItemId)> Downloads { get; } = new();

    /// <summary>Restores every substitute to its defaults: a resolved internal caller, nothing granted, nothing visible.</summary>
    public void Reset()
    {
        Entities.Reset();
        Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        Entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid());
        // Any other app-only read answers an empty row of the asked-for record (tests override the ones they assert on).
        Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken _) => new DataverseEntity(logicalName, id));

        Callers.Reset();
        Callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        Identity.Reset();
        Identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Query.Reset();
        Probe.Reset();
        Access.Reset();
        UserClient.Reset();
        EmailSender.Reset();
        MessageSender.Reset();
        Documents.Reset();
        CommunicationData.Reset();
        CommunicationData.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserId);
        Rung.Reset();
        Templates.Reset();
        Graph.Reset();
        FieldMapping.Reset();
        Downloads.Clear();
    }

    // ── Request helpers ───────────────────────────────────────────────────────────────────────

    public static HttpRequestMessage Request(HttpMethod method, string url, object? body = null, bool withToken = true, bool withOid = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (withToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        }

        if (!withOid)
        {
            request.Headers.Add(RecordAuthTestAuthHandler.NoOidHeader, "1");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>A problem body with its correlation/trace identifiers removed — the only fields allowed to differ.</summary>
    public static async Task<string> NormalizedBodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(text);
        if (node is JsonObject obj)
        {
            obj.Remove("correlationId");
            obj.Remove("traceId");
            if (obj["extensions"] is JsonObject ext)
            {
                ext.Remove("correlationId");
                ext.Remove("traceId");
            }
        }

        return node?.ToJsonString() ?? text;
    }

    /// <summary>Seeds the Graph metadata cache so the attachment download's metadata read is a cache hit (no Graph).</summary>
    public async Task SeedAttachmentAsync(Guid documentId, string driveId, string itemId)
    {
        Documents.Setup(d => d.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = documentId.ToString(),
                Name = "brief.pdf",
                FileName = "brief.pdf",
                MimeType = "application/pdf",
                GraphDriveId = driveId,
                GraphItemId = itemId,
            });

        var cache = Services.GetRequiredService<GraphMetadataCache>();
        var now = DateTimeOffset.UtcNow;
        await cache.SetFileMetadataAsync(driveId, itemId,
            new FileHandleDto(itemId, "brief.pdf", null, 1024, now, now, null, false, null));
    }

    // ── Host ──────────────────────────────────────────────────────────────────────────────────

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
                // The shared-mailbox send resolves its sender from this list (ApprovedSenderValidator).
                ["Communication:ApprovedSenders:0:Email"] = "noreply@contoso.com",
                ["Communication:ApprovedSenders:0:DisplayName"] = "Contoso Notifications",
                ["Communication:ApprovedSenders:0:IsDefault"] = "true",
                ["Communication:DefaultMailbox"] = "noreply@contoso.com",
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
            services.UseStubTokenCredential();

            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, RecordAuthTestAuthHandler>("Test", _ => { });
            services.AddDistributedMemoryCache();
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });

            services.RemoveAll<IHostedService>();

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(Entities.Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddScoped(_ => Callers.Object);
            services.RemoveAll<IImpersonatedCommunicationQuery>();
            services.AddSingleton<IImpersonatedCommunicationQuery>(Query);
            services.RemoveAll<ISystemUserIdentityResolver>();
            services.AddSingleton(Identity.Object);
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(Probe);
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
            services.RemoveAll<IDataverseUserClient>();
            services.AddSingleton(UserClient.Object);
            services.RemoveAll<ICommunicationChannelSender>();
            services.AddSingleton<ICommunicationChannelSender>(EmailSender);
            services.AddSingleton<ICommunicationChannelSender>(MessageSender);
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(Documents.Object);
            // T227d: app-only SPE reads pass the ownership guard; these tests are about record authorization, so the guard
            // admits every container (tests/integration/tenant covers its refusals).
            services.RemoveAll<SpeContainerOwnershipGuard>();
            services.AddSingleton(sp => TestSpeOwnership.AllowAll(sp.GetRequiredService<IGraphClientFactory>()));
            services.RemoveAll<ICommunicationDataverseService>();
            services.AddSingleton(CommunicationData.Object);
            services.RemoveAll<IAssociationRung>();
            services.AddSingleton<IAssociationRung>(Rung);
            services.RemoveAll<IEmailTemplateService>();
            services.AddSingleton(Templates.Object);
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton(Graph.Object);
            services.RemoveAll<IFieldMappingDataverseService>();
            services.AddSingleton(FieldMapping.Object);
            services.RemoveAll<SpeFileStore>();
            services.AddScoped<SpeFileStore>(sp => new StubSpeFileStore(
                sp.GetRequiredService<ContainerOperations>(),
                sp.GetRequiredService<DriveItemOperations>(),
                sp.GetRequiredService<UploadSessionManager>(),
                sp.GetRequiredService<UserOperations>(),
                Downloads));

            // The FR-26 core-ancestor stamp probes live metadata; the in-memory resolver derives nothing.
            services.RemoveAll<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>();
            services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert());

            services.RemoveAll<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>();
            services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(Ownership);
            // Batch-4 integration (task 166): every app-only attachment download first verifies the document's pointer.
            // The seeded attachments live in "b!drive-161", which this world's business unit stamps; the pointer check is
            // not this host's subject (DocumentPointerContainerCheckTests is).
            services.RemoveAll<Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver>();
            services.AddSingleton(TestRecordContainerResolver.ForBusinessUnitContainers("b!drive-161"));

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }
}

/// <summary>Authenticates every request as one internal caller; a header strips the oid to exercise the identity filter.</summary>
internal sealed class RecordAuthTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string NoOidHeader = "X-Test-No-Oid";

    public RecordAuthTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new("tid", "test-tenant-id"),
            new("preferred_username", "caller@contoso.com"),
        };
        if (!Request.Headers.ContainsKey(NoOidHeader))
        {
            claims.Add(new Claim("oid", "0a1b2c3d-0000-4000-8000-000000000161"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}

/// <summary>
/// <see cref="IImpersonatedCommunicationQuery"/> answering from a table of rows the CALLER can see, keyed by
/// (entity set, the id in the query's <c>$filter</c>). An absent key is an empty result — exactly how Dataverse
/// answers an impersonated read of a row the caller cannot see, or one that does not exist.
/// </summary>
public sealed class FakeImpersonatedQuery : IImpersonatedCommunicationQuery
{
    private static readonly System.Text.RegularExpressions.Regex IdInFilter = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");

    private readonly Dictionary<(string Set, Guid Id), Dictionary<string, JsonElement>> _rows = new();

    public List<(string Set, string? OData, Guid Caller)> Calls { get; } = new();

    /// <summary>When set, a query on this entity set throws (a read fault).</summary>
    public string? ThrowOnSet { get; set; }

    /// <summary>When true, every query throws.</summary>
    public bool ThrowOnEverySet { get; set; }

    public void Reset()
    {
        _rows.Clear();
        Calls.Clear();
        ThrowOnSet = null;
        ThrowOnEverySet = false;
    }

    /// <summary>Makes a row visible to the caller. <paramref name="columns"/> is JSON for the row's other columns.</summary>
    public void Visible(string set, Guid id, string? columns = null)
    {
        var row = string.IsNullOrWhiteSpace(columns)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(columns)!;
        _rows[(set, id)] = row;
    }

    public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
    {
        Calls.Add((entitySetName, odataQuery, callerSystemUserId));
        if (ThrowOnEverySet || ThrowOnSet == entitySetName)
        {
            throw new HttpRequestException($"impersonated read of {entitySetName} faulted");
        }

        var match = IdInFilter.Match(odataQuery ?? string.Empty);
        if (match.Success && _rows.TryGetValue((entitySetName, Guid.Parse(match.Value)), out var row))
        {
            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(new[] { row });
        }

        return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(Array.Empty<Dictionary<string, JsonElement>>());
    }
}

/// <summary>
/// <see cref="CallerRecordAccessProbe"/> at its virtual seams: answers from a rights table and a held-privilege set and
/// records every question. Like the real probe, a missing caller token answers None / "not held".
/// </summary>
public sealed class RecordingProbe : CallerRecordAccessProbe
{
    private readonly Dictionary<(string Set, Guid Id), AccessRights> _rights = new();
    private readonly HashSet<string> _privileges = new(StringComparer.Ordinal);

    public RecordingProbe()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
    }

    public List<string> Calls { get; } = new();

    public Exception? ThrowOnEveryCall { get; set; }

    public void Reset()
    {
        _rights.Clear();
        _privileges.Clear();
        Calls.Clear();
        ThrowOnEveryCall = null;
    }

    public void Grant(string set, Guid id, AccessRights rights) => _rights[(set, id)] = rights;

    public void Hold(string privilege) => _privileges.Add(privilege);

    public override Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        Calls.Add($"rights {entitySet}({recordId})");
        return ThrowOnEveryCall is not null
            ? Task.FromException<AccessRights>(ThrowOnEveryCall)
            : Task.FromResult(Answer(callerBearerToken, entitySet, recordId));
    }

    public override Task<IReadOnlyList<AccessRights>> GetCallerRightsForRecordsAsync(
        string? callerBearerToken, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken ct = default)
    {
        foreach (var t in targets)
        {
            Calls.Add($"rights {t.EntitySet}({t.RecordId})");
        }

        return ThrowOnEveryCall is not null
            ? Task.FromException<IReadOnlyList<AccessRights>>(ThrowOnEveryCall)
            : Task.FromResult<IReadOnlyList<AccessRights>>(
                targets.Select(t => Answer(callerBearerToken, t.EntitySet, t.RecordId)).ToArray());
    }

    public override Task<bool> CallerHoldsPrivilegeAsync(
        string? callerBearerToken, string privilegeName, CancellationToken ct = default)
    {
        Calls.Add($"privilege {privilegeName}");
        return ThrowOnEveryCall is not null
            ? Task.FromException<bool>(ThrowOnEveryCall)
            : Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && _privileges.Contains(privilegeName));
    }

    private AccessRights Answer(string? token, string set, Guid id) =>
        string.IsNullOrEmpty(token) ? AccessRights.None : _rights.GetValueOrDefault((set, id), AccessRights.None);
}

/// <summary>
/// Hand-written <see cref="IAccessDataSource"/> behind the REAL AuthorizationService + OperationAccessRule: answers
/// the document path (operation "read") from a rights table and records every question.
/// </summary>
public sealed class RecordingAccessDataSource : IAccessDataSource
{
    private readonly Dictionary<Guid, AccessRights> _documents = new();

    public List<Guid> DocumentCalls { get; } = new();

    public Exception? ThrowOnEveryCall { get; set; }

    public void Reset()
    {
        _documents.Clear();
        DocumentCalls.Clear();
        ThrowOnEveryCall = null;
    }

    public void GrantDocument(Guid id, AccessRights rights) => _documents[id] = rights;

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
    {
        var id = Guid.Parse(resourceId);
        DocumentCalls.Add(id);
        if (ThrowOnEveryCall is not null)
        {
            return Task.FromException<AccessSnapshot>(ThrowOnEveryCall);
        }

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = string.IsNullOrEmpty(userAccessToken) ? AccessRights.None : _documents.GetValueOrDefault(id, AccessRights.None),
        });
    }

    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
        Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = AccessRights.None });
}

/// <summary>A channel sender that records what it was asked to send — no Graph, no ACS.</summary>
public sealed class RecordingChannelSender : ICommunicationChannelSender
{
    public RecordingChannelSender(CommunicationType type) => SupportedType = type;

    public CommunicationType SupportedType { get; }

    public List<ChannelSendRequest> Sent { get; } = new();

    public void Reset() => Sent.Clear();

    public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        return Task.FromResult(new ChannelSendResult
        {
            FromAddress = request.FromAddress,
            ProviderMessageId = "<sent-161@contoso.com>",
        });
    }
}

/// <summary>A deterministic association rung (the engine's DI plugin point) emitting scripted matches.</summary>
public sealed class ScriptedRung : IAssociationRung
{
    public RungKind Kind => RungKind.ExplicitReference;

    public int Order => 0;

    public List<RungMatch> Matches { get; } = new();

    public int Evaluations { get; private set; }

    public void Reset()
    {
        Matches.Clear();
        Evaluations = 0;
    }

    public Task<IReadOnlyList<RungMatch>> EvaluateAsync(NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        Evaluations++;
        return Task.FromResult<IReadOnlyList<RungMatch>>(Matches.ToArray());
    }
}

/// <summary>SPE facade with the download replaced (the metadata read is served from the seeded Graph metadata cache).</summary>
internal sealed class StubSpeFileStore : SpeFileStore
{
    private readonly List<(string DriveId, string ItemId)> _downloads;

    public StubSpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadManager,
        UserOperations userOps,
        List<(string DriveId, string ItemId)> downloads)
        : base(containerOps, driveItemOps, uploadManager, userOps)
    {
        _downloads = downloads;
    }

    public override Task<Stream?> DownloadFileAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        _downloads.Add((driveId, itemId));
        return Task.FromResult<Stream?>(new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
    }
}
