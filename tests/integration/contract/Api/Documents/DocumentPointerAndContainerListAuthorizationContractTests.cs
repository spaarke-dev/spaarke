using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// <c>GET /api/v1/documents?containerId=</c> through the REAL <c>MapDataverseDocumentsEndpoints</c>
/// (unified-access-control-r2 task 166, sweep finding S-66).
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The route is the query-keyed twin of <c>GET /api/v1/containers/{containerId}/documents</c>
/// — the same app-only <c>GetDocumentsByContainerAsync</c>, the same SPE pointers in every row — and it was exempted
/// from task 074's guard by a Permanent "collection read, result trimming" waiver although the caller names exactly
/// ONE container and no trimming exists. Any signed-in caller could list any container.</para>
/// <para><b>What is real and what is substituted.</b> The mapper, <see cref="ContainerDocumentAuthorizationFilter"/>
/// (now reading the QUERY parameter), the REAL <see cref="RecordContainerResolver"/> and the REAL
/// <see cref="AuthorizationService"/> are production code. Substituted: the resolver's two Dataverse seams (one SECURE
/// project owns <see cref="OwnedContainer"/>; nothing secure owns <see cref="SharedContainer"/>), the
/// <see cref="IAccessDataSource"/> boundary, and <see cref="IDocumentDataverseService"/> (records every listing).
/// Services the mapped siblings need but these requests never reach are registered as throwing factories, so a stray
/// call fails loudly.</para>
/// <para>The container ids are GUID-shaped on purpose: the list handler still validates <c>Guid.TryParse</c> (the
/// task 078 note §4 type bug, deliberately not fixed here), so only a GUID-shaped id lets an AUTHORIZED request reach
/// the listing and prove the gate is not "deny everything".</para>
/// <para><c>PUT /api/v1/documents/{id}</c> (kept at the batch-4 integration for task 147's Compose re-file) refusing the
/// pointer fields is pinned by <c>DocumentDestroyAuthorizationTests</c> and <c>DocumentRefileRestampRouteTests</c>.</para>
/// </remarks>
public class DocumentPointerAndContainerListAuthorizationContractTests
{
    private const string OwnedContainer = "1a6f0c52-0000-4000-8000-000000000166";
    private const string SharedContainer = "2b7f1d63-0000-4000-8000-000000000166";
    private const string Projects = "sprk_projects";
    private static readonly Guid OwningProjectId = Guid.Parse("16616616-0000-4000-8000-000000000166");

    public static TheoryData<string> BothListRoutes => new() { "query", "route" };

    private static string ListUrl(string shape, string containerId) => shape == "query"
        ? $"/api/v1/documents?containerId={containerId}"
        : $"/api/v1/containers/{containerId}/documents";

    [Theory]
    [MemberData(nameof(BothListRoutes))]
    public async Task List_ACallerWithNoReadOnTheOwningRecord_Is403_AndTheListingNeverRuns(string shape)
    {
        await using var host = await DocumentsListHost.StartAsync();

        var response = await host.SendAsync(Authenticated(ListUrl(shape, OwnedContainer)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("container_documents_access_denied");
        host.Access.Calls.Should().Equal(new[] { (Projects, OwningProjectId) },
            "the CALLER's rights on the container's OWNING record are what decide — never the container id itself");
        host.ListedContainers.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothListRoutes))]
    public async Task List_AContainerNoSecureRecordOwns_IsRefused_EvenForACallerWhoCouldReadEverything(string shape)
    {
        await using var host = await DocumentsListHost.StartAsync();
        host.Access.GrantEverything = true;

        var response = await host.SendAsync(Authenticated(ListUrl(shape, SharedContainer)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a shared container holds documents of many records — a per-container gate cannot answer it, so it refuses");
        host.ListedContainers.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothListRoutes))]
    public async Task List_ACallerWithReadOnTheOwningRecord_ReachesTheListing(string shape)
    {
        await using var host = await DocumentsListHost.StartAsync();
        host.Access.Grant(Projects, OwningProjectId, AccessRights.Read);

        var response = await host.SendAsync(Authenticated(ListUrl(shape, OwnedContainer)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the gate must not be 'deny everything'");
        host.ListedContainers.Should().Equal(OwnedContainer);
    }

    [Fact]
    public async Task List_TheTwoRoutesAgree_ForEveryCallerAndContainer()
    {
        // The finding WAS a disagreement: one route gated, its twin open. Same caller, same container — same answer.
        foreach (var (container, rights) in new[]
                 {
                     (OwnedContainer, AccessRights.None), (OwnedContainer, AccessRights.Read), (SharedContainer, AccessRights.Read),
                 })
        {
            await using var host = await DocumentsListHost.StartAsync();
            host.Access.Grant(Projects, OwningProjectId, rights);

            var viaQuery = await host.SendAsync(Authenticated(ListUrl("query", container)));
            var viaRoute = await host.SendAsync(Authenticated(ListUrl("route", container)));

            viaQuery.StatusCode.Should().Be(viaRoute.StatusCode, $"container {container}, rights {rights}");
        }
    }

    [Theory]
    [InlineData("/api/v1/documents")]
    [InlineData("/api/v1/documents?containerId=")]
    [InlineData("/api/v1/documents?containerId=%20%20")]
    public async Task List_WithoutAContainerId_IsTheRoutesOwn400_AndAsksNothing(string url)
    {
        await using var host = await DocumentsListHost.StartAsync();

        var response = await host.SendAsync(Authenticated(url));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ContainerDocumentAuthorizationFilter.MissingQueryContainerIdDetail,
            "the same sentence the handler always answered — the contract for a missing id is unchanged");
        host.ResolverQueries.Should().Be(0, "no resolver (and so no Dataverse) query for a request that names no container");
        host.Access.Calls.Should().BeEmpty();
        host.ListedContainers.Should().BeEmpty();
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage Authenticated(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(DocumentsListTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["errorCode"]?.GetValue<string>();

    internal sealed class RecordingAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<(string, Guid), AccessRights> _rights = new();

        public List<(string Set, Guid Id)> Calls { get; } = new();

        public bool GrantEverything { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _rights[(set, id)] = rights;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new NotSupportedException("the container gate never routes through the document-only path");

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            Calls.Add((entitySetName, recordId));
            var rights = GrantEverything
                ? AccessRights.Read | AccessRights.Write | AccessRights.Delete
                : _rights.TryGetValue((entitySetName, recordId), out var r) ? r : AccessRights.None;
            return Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = rights });
        }
    }

    /// <summary>A minimal host over the REAL documents mapper.</summary>
    internal sealed class DocumentsListHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;
        private int _resolverQueries;

        public RecordingAccessDataSource Access { get; } = new();

        public List<string> ListedContainers { get; } = new();

        public int ResolverQueries => _resolverQueries;

        public static async Task<DocumentsListHost> StartAsync()
        {
            var host = new DocumentsListHost();
            await host.InitializeAsync();
            return host;
        }

        private RecordContainerResolver BuildResolver()
        {
            var registry = new Mock<ISecurableEntityRegistry>();
            registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal) { "sprk_project" });

            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    Interlocked.Increment(ref _resolverQueries);
                    var result = new EntityCollection();
                    var secureProbe = query.Criteria.Conditions.Any(c =>
                        c.AttributeName == SecurableEntityRegistry.SecureFlagAttribute && c.Values.Count == 1 && c.Values[0] is true);
                    var like = query.Criteria.Conditions
                        .FirstOrDefault(c => c.AttributeName == "sprk_containerid" && c.Operator == ConditionOperator.Like)?
                        .Values.FirstOrDefault() as string;
                    if (secureProbe && like is not null && OwnedContainer.Contains(like.Trim('%'), StringComparison.Ordinal))
                    {
                        var row = new Entity("sprk_project", OwningProjectId)
                        {
                            [SecurableEntityRegistry.SecureFlagAttribute] = true,
                            ["sprk_containerid"] = OwnedContainer,
                        };
                        result.Entities.Add(row);
                    }

                    return result;
                });

            return new RecordContainerResolver(registry.Object, entities.Object, NullLogger<RecordContainerResolver>.Instance);
        }

        private async Task InitializeAsync()
        {
            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
            documents.Setup(d => d.GetDocumentsByContainerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, CancellationToken _) =>
                {
                    ListedContainers.Add(containerId);
                    return Enumerable.Empty<DocumentEntity>();
                });

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = DocumentsListTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = DocumentsListTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, DocumentsListTestAuthHandler>(DocumentsListTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            // The REAL authorization stack, substituted only at the access-data and Dataverse boundaries.
            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton(BuildResolver());
            builder.Services.AddSingleton(documents.Object);

            // Parameters of the mapped siblings (create / download) — registered so the endpoint model builds, never
            // reached by a list request. A stray call throws.
            builder.Services.AddSingleton<SpeFileStore>(_ => throw new NotSupportedException("not reached by the list routes"));
            builder.Services.AddSingleton<DocumentTelemetry>(_ => throw new NotSupportedException("not reached by the list routes"));
            builder.Services.AddSingleton(Mock.Of<IMembershipEventPublisher>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IRecordOwnershipResolver>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IGenericEntityService>(MockBehavior.Strict));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapDataverseDocumentsEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}

/// <summary>Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra oid.</summary>
public sealed class DocumentsListTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DocumentsListTest";
    public const string CallerHeader = "X-Test-Caller";
    public const string CallerOid = "6f0c1a52-0000-4000-8000-0000000d0166";

    public DocumentsListTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("oid", CallerOid) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
