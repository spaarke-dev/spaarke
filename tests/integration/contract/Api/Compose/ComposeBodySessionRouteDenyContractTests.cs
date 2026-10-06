// Compose body-scoped session routes — the DENY branches through the real routes (unified-access-control-r2 task 166 r1).
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Compose/**`
//   - Justification: the task-166 verifier seeded two removals that NO test noticed — the REPLACE save route forwarding
//     body.SessionId unchecked, and the upload route answering a session-store fault with a 500 instead of the uniform
//     404 — because the deny cases were pinned only on the shared helper
//     (ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync), never through the routes that must call it. This
//     file hosts the REAL MapComposeMountEndpoints / MapComposeSaveEndpoints over substituted module boundaries
//     (IComposeService, ChatSessionManager's virtual GetSessionAsync, ITenantCache) so each route's own use of the
//     decision is what is asserted: what it answers, and what it never touches.
//
// Doubles are module boundaries only (ADR-038 §4). No Mock<HttpMessageHandler>, no DI-registration assertion, no
// constructor null-check. The allowed-path behaviour of both routes is covered by
// ComposeSessionAndContainerAuthorizationContractTests (real ComposeService) and the Compose seam suites.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Compose;

public sealed class ComposeBodySessionRouteDenyContractTests
{
    private const string Tenant = "tenant-uac166-r1";
    private const string CallerOid = "11111111-0000-4000-8000-0000000166a1";
    private const string OtherOid = "22222222-0000-4000-8000-0000000166a1";

    // =============================================================================================
    // POST /api/compose/upload — every deny shape answers the uniform 404 and touches neither the
    // retained-bytes cache nor the projection (which would also write a PDF-source marker).
    // =============================================================================================

    public static TheoryData<string, string> SpellingShapes => new()
    {
        // sent as             stored as
        { "D", "D" },
        { "D", "N" },
        { "N", "D" },
        { "N", "N" },
    };

    [Theory]
    [MemberData(nameof(SpellingShapes))]
    public async Task Upload_AnotherUsersSession_UnderEverySpelling_IsTheUniform404_AndReadsNothing(string sent, string stored)
    {
        var id = Guid.NewGuid();
        await using var host = await Host.StartAsync();
        host.Sessions[id.ToString(stored)] = Session(id.ToString(stored), OtherOid);

        var response = await host.Client.PostAsJsonAsync("/api/compose/upload",
            new { sessionId = id.ToString(sent), documentId = "doc-1" });

        await AssertUniformUpload404Async(host, response);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Upload_AnUnownedSession_IsTheUniform404_AndReadsNothing(string? owner)
    {
        var id = Guid.NewGuid().ToString("D");
        await using var host = await Host.StartAsync();
        host.Sessions[id] = Session(id, owner);

        var response = await host.Client.PostAsJsonAsync("/api/compose/upload", new { sessionId = id, documentId = "doc-1" });

        await AssertUniformUpload404Async(host, response);
    }

    [Fact]
    public async Task Upload_ASessionStoreFault_IsTheUniform404_NotA500_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();
        host.SessionStoreFault = new TimeoutException("redis timed out");

        var response = await host.Client.PostAsJsonAsync("/api/compose/upload",
            new { sessionId = Guid.NewGuid().ToString("D"), documentId = "doc-1" });

        await AssertUniformUpload404Async(host, response);
    }

    [Fact]
    public async Task Upload_AnUnknownSession_IsTheUniform404_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/compose/upload",
            new { sessionId = Guid.NewGuid().ToString("N"), documentId = "doc-1" });

        await AssertUniformUpload404Async(host, response);
    }

    [Fact]
    public async Task Upload_TheCallersOwnSession_ReadsTheBytesAndProjectsThem_ThePositiveControl()
    {
        var id = Guid.NewGuid().ToString("D");
        await using var host = await Host.StartAsync();
        host.Sessions[id] = Session(id, CallerOid);
        host.CacheBytes = "OWN-BYTES"u8.ToArray();

        var response = await host.Client.PostAsJsonAsync("/api/compose/upload", new { sessionId = id, documentId = "doc-1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.CacheReads.Should().NotBeEmpty();
        host.Compose.Verify(c => c.ProjectForMount(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), id), Times.Once);
    }

    private static async Task AssertUniformUpload404Async(Host host, HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        problem["title"]!.GetValue<string>().Should().Be("Uploaded File Not Available");

        var expired = JsonNode.Parse(await (await host.ExpiredBytesAnswerAsync()).Content.ReadAsStringAsync())!.AsObject();
        Normalize(problem).Should().Be(Normalize(expired), "every deny shape is byte-identical to the expired-bytes answer");

        host.CacheReads.Should().BeEmpty("the retained bytes are read only for the session's OWNER");
        host.Compose.Verify(c => c.ProjectForMount(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never, "no projection is built and no PDF-source marker is written on another user's session");
    }

    // =============================================================================================
    // POST /api/compose/documents/{documentSpeId}/save (REPLACE) and /documents/create-on-save
    // =============================================================================================

    public static TheoryData<string> SaveRoutes => new() { "replace", "create-on-save" };

    [Theory]
    [MemberData(nameof(SaveRoutes))]
    public async Task Save_AnotherUsersSession_RunsUnbound_TheServiceNeverSeesTheirSession(string route)
    {
        var othersSession = Guid.NewGuid().ToString("D");
        await using var host = await Host.StartAsync();
        host.Sessions[othersSession] = Session(othersSession, OtherOid);

        var response = await PostSaveAsync(host, route, othersSession);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.SavedRequests.Should().ContainSingle()
            .Which.SessionId.Should().BeEmpty("a session the caller does not own is never forwarded — no rebind, no "
                + "session-derived container, no session state read");
        host.SavedRequests[0].TenantId.Should().Be(Tenant, "the tenant is the CALLER's claim");
        (await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>())!.SessionId
            .Should().Be(othersSession, "the wire shape is unchanged: the sent id is echoed");
    }

    [Theory]
    [MemberData(nameof(SaveRoutes))]
    public async Task Save_TheCallersOwnSession_IsForwarded(string route)
    {
        var ownSession = Guid.NewGuid().ToString("D");
        await using var host = await Host.StartAsync();
        host.Sessions[ownSession] = Session(ownSession, CallerOid);

        var response = await PostSaveAsync(host, route, ownSession);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.SavedRequests.Should().ContainSingle().Which.SessionId.Should().Be(ownSession);
    }

    [Theory]
    [MemberData(nameof(SaveRoutes))]
    public async Task Save_ASessionStoreFault_RefusesTheSave_503_AndWritesNothing(string route)
    {
        await using var host = await Host.StartAsync();
        host.SessionStoreFault = new TimeoutException("redis timed out");

        var response = await PostSaveAsync(host, route, Guid.NewGuid().ToString("D"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "a fault is not 'not yours': saving UNBOUND would move a create-on-save of a secure-matter draft into the "
            + "shared business-unit container, and SPE permissions cannot be retracted (ADR-003)");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be("compose_session_unavailable");
        host.SavedRequests.Should().BeEmpty("nothing is written when ownership could not be established");
    }

    private static Task<HttpResponseMessage> PostSaveAsync(Host host, string route, string sessionId)
        => route == "replace"
            ? host.Client.PostAsJsonAsync("/api/compose/documents/spe-item-166/save", new
            {
                sessionId,
                tenantId = "a-foreign-body-tenant",
                driveId = "b!drive-166",
                content = new byte[] { 1, 2, 3 },
            })
            : host.Client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
            {
                sessionId,
                tenantId = "a-foreign-body-tenant",
                content = new byte[] { 1, 2, 3 },
            });

    // =============================================================================================
    // Harness
    // =============================================================================================

    private static ChatSession Session(string id, string? ownerOid) =>
        new(SessionId: id, TenantId: Tenant, DocumentId: null, PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow, LastActivity: DateTimeOffset.UtcNow, Messages: [])
        { OwnerOid = ownerOid };

    private static string Normalize(JsonObject problem)
    {
        var copy = JsonNode.Parse(problem.ToJsonString())!.AsObject();
        copy.Remove("traceId");
        copy.Remove("correlationId");
        return copy.ToJsonString();
    }

    /// <summary>A minimal in-process host over the REAL Compose mount + save route clusters.</summary>
    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;

        public Dictionary<string, ChatSession> Sessions { get; } = new(StringComparer.Ordinal);
        public Exception? SessionStoreFault { get; set; }
        public byte[]? CacheBytes { get; set; }
        public List<string> CacheReads { get; } = new();
        public List<SaveComposeDocumentRequest> SavedRequests { get; } = new();
        public Mock<IComposeService> Compose { get; } = new(MockBehavior.Loose);
        public HttpClient Client { get; private set; } = null!;

        public static async Task<Host> StartAsync()
        {
            var host = new Host();
            await host.InitializeAsync();
            return host;
        }

        /// <summary>The expired-bytes answer: the caller's OWN session with nothing retained — the reference 404.</summary>
        public async Task<HttpResponseMessage> ExpiredBytesAnswerAsync()
        {
            var ownKey = $"own-{Guid.NewGuid():N}";
            Sessions[ownKey] = Session(ownKey, CallerOid);
            var savedBytes = CacheBytes;
            var savedFault = SessionStoreFault;
            var reads = CacheReads.Count;
            CacheBytes = null;
            SessionStoreFault = null;
            try
            {
                return await Client.PostAsJsonAsync("/api/compose/upload", new { sessionId = ownKey, documentId = "doc-expired" });
            }
            finally
            {
                CacheBytes = savedBytes;
                SessionStoreFault = savedFault;
                CacheReads.RemoveRange(reads, CacheReads.Count - reads); // the reference call's own reads are not the test's
                Sessions.Remove(ownKey);
            }
        }

        private async Task InitializeAsync()
        {
            var sessions = new Mock<ChatSessionManager>(
                Mock.Of<ITenantCache>(), Mock.Of<IChatDataverseRepository>(), NullLogger<ChatSessionManager>.Instance, null!, null!);
            sessions
                .Setup(m => m.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string tenant, string id, CancellationToken _) =>
                {
                    if (SessionStoreFault is { } fault)
                    {
                        return Task.FromException<ChatSession?>(fault);
                    }

                    return Task.FromResult(tenant == Tenant && Sessions.TryGetValue(id, out var s) ? s : null);
                });

            var cache = new Mock<ITenantCache>(MockBehavior.Loose);
            cache
                .Setup(c => c.GetAsync<byte[]>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string resource, string id, int _, string _, CancellationToken _) =>
                {
                    CacheReads.Add($"{resource}:{id}");
                    return CacheBytes;
                });

            Compose
                .Setup(c => c.ProjectForMount(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
                .ReturnsAsync((ReadOnlyMemory<byte> bytes, string? _, CancellationToken _, string? _) => new ComposeMountProjection
                {
                    Content = bytes,
                    Minted = false,
                    Projection = new ComposeDocxProjection { Status = ComposeProjectionStatus.Success, CanEdit = true },
                });
            Compose
                .Setup(c => c.SaveAsync(It.IsAny<SaveComposeDocumentRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SaveComposeDocumentRequest request, HttpContext _, CancellationToken _) =>
                {
                    SavedRequests.Add(request);
                    return new SaveComposeDocumentResult
                    {
                        DocumentSpeId = request.DocumentSpeId ?? "spe-item-created",
                        DriveId = "b!drive-166",
                        SessionId = request.SessionId,
                        VersionId = "v2",
                        Outcome = ComposeSaveOutcome.Persisted,
                    };
                });

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = DenyHostAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = DenyHostAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, DenyHostAuthHandler>(DenyHostAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(o =>
            {
                o.AddPolicy("ai-context", _ => RateLimitPartition.GetNoLimiter("ai-context-test"));
                o.AddPolicy("ai-persist", _ => RateLimitPartition.GetNoLimiter("ai-persist-test"));
            });
            builder.Services.AddSingleton(sessions.Object);
            builder.Services.AddSingleton(cache.Object);
            builder.Services.AddSingleton(Compose.Object);
            builder.WebHost.UseTestServer();

            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapGroup("/api/compose").RequireAuthorization()
                .MapComposeMountEndpoints()
                .MapComposeSaveEndpoints();
            await _app.StartAsync();

            Client = _app.GetTestClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class DenyHostAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ComposeDenyHost";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim("oid", CallerOid), new Claim("tid", Tenant), new Claim("name", "Deny Host User") },
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
