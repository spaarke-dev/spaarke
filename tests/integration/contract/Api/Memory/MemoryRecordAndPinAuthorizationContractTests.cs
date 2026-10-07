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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Memory;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Memory;

/// <summary>
/// The per-record authorization of the pinned-memory routes, exercised through the REAL
/// <see cref="PinnedMemoryEndpoints.MapPinnedMemoryEndpoints"/> (unified-access-control-r2 task 166, sweep findings
/// S-43 / S-68, owner round 12 item 4).
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> A pin that names a matter is injected into every other user's prompt for that matter
/// (the memory compose path), yet the routes accepted any matter id from any caller — attaching content to a matter
/// the caller could not even read. And PUT / DELETE answered 404 for an unknown pin but 403 for another user's — a
/// pin-existence oracle across users.</para>
/// <para><b>What is real and what is substituted.</b> The mapper, the handlers, <see cref="OperationAccessPolicy"/>
/// and the problem shapes are production code. Substituted: <see cref="IPinnedContextRepository"/> (an in-memory
/// store) and <see cref="CallerRecordAccessProbe"/> at its designated virtual seam (a rights table that records
/// every question; like the real probe, no caller token answers <see cref="AccessRights.None"/>). No HTTP handler is
/// mocked (ADR-038).</para>
/// <para>The <c>GET /api/memory/records/{entityType}/{entityId}</c> route this task DELETED is pinned gone by
/// <c>DeadRouteRetirementTests</c>.</para>
/// </remarks>
public class MemoryRecordAndPinAuthorizationContractTests
{
    private const string Matters = "sprk_matters"; // live EntityDefinitions(LogicalName='sprk_matter').EntitySetName

    private static object PinBody(string pinType = "matter-fact", string? matterId = null) => new
    {
        title = "Clause 7 governs",
        content = "Always cite clause 7.",
        pinType,
        matterId,
    };

    // =========================================================================================
    // Create — the matter is authorized AS THE CALLER at AppendTo
    // =========================================================================================

    [Fact]
    public async Task CreatePin_OnAMatterTheCallerHoldsAppendTo_Is201_AndAsksSprkMattersWithTheCallersToken()
    {
        await using var host = await PinAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, AccessRights.AppendTo);

        var response = await host.SendAsync(Post(PinBody(matterId: matterId.ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Calls.Should().Equal(new ProbeCall(Matters, matterId, HasToken: true));
        host.Store.Values.Should().ContainSingle()
            .Which.MatterId.Should().Be(matterId.ToString(), "the id is stored as sent so GetByMatterAsync still matches");
    }

    public static TheoryData<AccessRights> RightsWithoutAppendTo => new()
    {
        AccessRights.None,
        AccessRights.Read,
        AccessRights.Read | AccessRights.Write,
        AccessRights.Read | AccessRights.Write | AccessRights.Append,
    };

    [Theory]
    [MemberData(nameof(RightsWithoutAppendTo))]
    public async Task CreatePin_WithoutAppendToOnTheMatter_Is403_AndWritesNothing(AccessRights rights)
    {
        await using var host = await PinAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, rights);

        var response = await host.SendAsync(Post(PinBody(matterId: matterId.ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(PinnedMemoryEndpoints.PinMatterDeniedDetail).And.NotContain(matterId.ToString());
        host.Store.Should().BeEmpty();
    }

    [Theory]
    [InlineData("user-preference")]
    [InlineData("system-rule")]
    public async Task CreatePin_OfAnyPinTypeNamingAMatter_IsAuthorizedToo(string pinType)
    {
        // The matter id is honoured on every pinType (the list filter and the compose path read it), so the check
        // cannot be limited to matter-fact pins.
        await using var host = await PinAuthHost.StartAsync();
        var matterId = Guid.NewGuid();

        var response = await host.SendAsync(Post(PinBody(pinType, matterId.ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Probe.Calls.Should().ContainSingle().Which.Id.Should().Be(matterId);
        host.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePin_AnUnknownMatterAndAForbiddenOne_AreTheSame403()
    {
        await using var host = await PinAuthHost.StartAsync();
        var unknown = Guid.NewGuid();
        var forbidden = Guid.NewGuid();
        host.Probe.Grant(Matters, forbidden, AccessRights.Read);

        var a = await host.SendAsync(Post(PinBody(matterId: unknown.ToString())));
        var b = await host.SendAsync(Post(PinBody(matterId: forbidden.ToString())));

        a.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        b.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Normalize(await a.Content.ReadAsStringAsync()).Should().Be(Normalize(await b.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task CreatePin_WhenTheRightsProbeThrows_Is403_NeverA500_AndWritesNothing()
    {
        await using var host = await PinAuthHost.StartAsync();
        host.Probe.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");

        var response = await host.SendAsync(Post(PinBody(matterId: Guid.NewGuid().ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePin_WithNoBearerToken_IsDenied_TheMatterCannotBeAskedAsTheCaller()
    {
        await using var host = await PinAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, AccessRights.AppendTo); // would be allowed WITH a token

        var request = Post(PinBody(matterId: matterId.ToString()));
        request.Headers.Authorization = null;

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Probe.Calls.Should().ContainSingle().Which.HasToken.Should().BeFalse();
        host.Store.Should().BeEmpty();
    }

    [Theory]
    [InlineData("matter-fact")]
    [InlineData("user-preference")]
    public async Task CreatePin_ANonGuidMatterId_IsTheFieldLevel400_AndAsksNothing(string pinType)
    {
        await using var host = await PinAuthHost.StartAsync();

        var response = await host.SendAsync(Post(PinBody(pinType, "matter-a")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject().Should().ContainKey("matterId");
        host.Probe.Calls.Should().BeEmpty("a malformed id must not spend a Dataverse rights query");
        host.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePin_WithNoMatter_IsAPrivatePin_AndAsksNothing()
    {
        await using var host = await PinAuthHost.StartAsync();

        var response = await host.SendAsync(Post(PinBody("user-preference")));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Calls.Should().BeEmpty();
    }

    // =========================================================================================
    // Update — ownership first (uniform 404), then the NEW matter at AppendTo
    // =========================================================================================

    [Fact]
    public async Task UpdatePin_RepointingAnOwnedPinAtAMatterWithoutAppendTo_Is403_AndUpdatesNothing()
    {
        await using var host = await PinAuthHost.StartAsync();
        var pinId = host.SeedPin(PinAuthzTestAuthHandler.CallerOid);
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, AccessRights.Read);

        var response = await host.SendAsync(Put(pinId, PinBody(matterId: matterId.ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Store[pinId].MatterId.Should().BeNull("the owned pin was not re-pointed");
    }

    [Fact]
    public async Task UpdatePin_RepointingAnOwnedPinAtAMatterWithAppendTo_Is200()
    {
        await using var host = await PinAuthHost.StartAsync();
        var pinId = host.SeedPin(PinAuthzTestAuthHandler.CallerOid);
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, AccessRights.AppendTo);

        var response = await host.SendAsync(Put(pinId, PinBody(matterId: matterId.ToString())));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Store[pinId].MatterId.Should().Be(matterId.ToString());
    }

    [Fact]
    public async Task UpdatePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNoMatterIsAsked()
    {
        await using var host = await PinAuthHost.StartAsync();
        var othersPin = host.SeedPin("someone-else-oid");
        var matterId = Guid.NewGuid();
        host.Probe.Grant(Matters, matterId, AccessRights.AppendTo);

        var others = await host.SendAsync(Put(othersPin, PinBody(matterId: matterId.ToString())));
        var unknown = await host.SendAsync(Put("no-such-pin", PinBody(matterId: matterId.ToString())));

        others.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalize(await others.Content.ReadAsStringAsync()).Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
        host.Probe.Calls.Should().BeEmpty("no matter question is asked about a pin the caller does not own");
        host.Store[othersPin].MatterId.Should().BeNull();
    }

    // =========================================================================================
    // Delete — owner round 12 item 4: a uniform 404
    // =========================================================================================

    [Fact]
    public async Task DeletePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNothingIsDeleted()
    {
        await using var host = await PinAuthHost.StartAsync();
        var othersPin = host.SeedPin("someone-else-oid");

        var others = await host.SendAsync(Delete(othersPin));
        var unknown = await host.SendAsync(Delete("no-such-pin"));

        others.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var othersBody = await others.Content.ReadAsStringAsync();
        Normalize(othersBody).Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
        othersBody.Should().Contain(PinnedMemoryEndpoints.PinNotFoundDetail).And.NotContain(othersPin);
        host.Store.Should().ContainKey(othersPin, "another user's pin is never deleted");
    }

    [Fact]
    public async Task DeletePin_TheCallersOwnPin_Is204()
    {
        await using var host = await PinAuthHost.StartAsync();
        var pinId = host.SeedPin(PinAuthzTestAuthHandler.CallerOid);

        var response = await host.SendAsync(Delete(pinId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Store.Should().NotContainKey(pinId);
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage Authenticated(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PinAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static HttpRequestMessage Post(object body)
    {
        var request = Authenticated(HttpMethod.Post, "/api/memory/pins");
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Put(string pinId, object body)
    {
        var request = Authenticated(HttpMethod.Put, $"/api/memory/pins/{pinId}");
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Delete(string pinId) => Authenticated(HttpMethod.Delete, $"/api/memory/pins/{pinId}");

    /// <summary>The body with its per-request identifiers removed — the only fields allowed to differ.</summary>
    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    internal sealed record ProbeCall(string Set, Guid Id, bool HasToken);

    /// <summary>
    /// <see cref="CallerRecordAccessProbe"/> at its designated virtual seam: answers from a rights table and records
    /// every question. Like the real probe, a missing caller token answers <see cref="AccessRights.None"/>.
    /// </summary>
    internal sealed class RecordingRightsProbe : CallerRecordAccessProbe
    {
        private readonly Dictionary<(string, Guid), AccessRights> _rights = new();

        public RecordingRightsProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public List<ProbeCall> Calls { get; } = new();

        public Exception? ThrowOnEveryCall { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _rights[(set, id)] = rights;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Calls.Add(new ProbeCall(entitySet, recordId, !string.IsNullOrEmpty(callerBearerToken)));
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<AccessRights>(ThrowOnEveryCall);
            }

            return Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && _rights.TryGetValue((entitySet, recordId), out var r)
                ? r
                : AccessRights.None);
        }
    }

    /// <summary>A minimal host over the REAL pinned-memory mapper.</summary>
    internal sealed class PinAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingRightsProbe Probe { get; } = new();

        /// <summary>The in-memory pin store, keyed by pin id (the caller's tenant only).</summary>
        public Dictionary<string, PinnedContextItem> Store { get; } = new(StringComparer.Ordinal);

        public static async Task<PinAuthHost> StartAsync()
        {
            var host = new PinAuthHost();
            await host.InitializeAsync();
            return host;
        }

        public string SeedPin(string ownerOid)
        {
            var pinId = Guid.NewGuid().ToString("N");
            Store[pinId] = new PinnedContextItem
            {
                Id = PinnedContextRepository.BuildDocumentId(PinAuthzTestAuthHandler.TenantId, pinId),
                DocumentType = PinnedContextRepository.DocumentTypeValue,
                TenantId = PinAuthzTestAuthHandler.TenantId,
                UserId = ownerOid,
                PinType = PinType.UserPreference,
                Title = "Seeded",
                Content = "Seeded content",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = ownerOid,
            };
            return pinId;
        }

        /// <summary>The pin id is the last segment of <c>PinnedContextRepository.BuildDocumentId</c>'s
        /// <c>{prefix}_{tenant}_{pinId}</c>.</summary>
        private static string PinIdOf(PinnedContextItem pin) => pin.Id[(pin.Id.LastIndexOf('_') + 1)..];

        private async Task InitializeAsync()
        {
            var repository = new Mock<IPinnedContextRepository>(MockBehavior.Strict);
            repository.Setup(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
                .Callback<PinnedContextItem, CancellationToken>((p, _) => Store[PinIdOf(p)] = p)
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.UpdateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
                .Callback<PinnedContextItem, CancellationToken>((p, _) => Store[PinIdOf(p)] = p)
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenant, string pinId, CancellationToken _) =>
                    tenant == PinAuthzTestAuthHandler.TenantId && Store.TryGetValue(pinId, out var p) ? p : null);
            repository.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, pinId, _) => Store.Remove(pinId))
                .Returns(Task.CompletedTask);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = PinAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = PinAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PinAuthzTestAuthHandler>(PinAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("ai-context", _ => RateLimitPartition.GetNoLimiter("ai-context-test")));
            builder.Services.AddSingleton(repository.Object);
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            builder.Services.AddSingleton(TimeProvider.System);

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapPinnedMemoryEndpoints();

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

/// <summary>Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra oid + tid,
/// independent of the Authorization header — so a test can present a caller whose bearer token is absent.</summary>
public sealed class PinAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PinAuthzTest";
    public const string CallerHeader = "X-Test-Caller";
    public const string CallerOid = "6f0c1a52-0000-4000-8000-000000000166";
    public const string TenantId = "7a1d2b63-0000-4000-8000-000000000166";

    public PinAuthzTestAuthHandler(
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

        var identity = new ClaimsIdentity(new[] { new Claim("oid", CallerOid), new Claim("tid", TenantId) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
