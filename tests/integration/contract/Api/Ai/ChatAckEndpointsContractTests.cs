using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for <c>POST /api/ai/chat/sessions/{sessionId}/ack</c> (D-F3 UI-action
/// truthfulness / FR-A1-08 / task AIR2-037).
/// </summary>
/// <remarks>
/// Hosting approach mirrors <c>SummarizeSessionEndpointContractTests</c>: a minimal
/// in-process <see cref="WebApplication"/> mapping ONLY <c>MapChatAckEndpoints()</c>, a
/// bearer-presence fake auth handler, and a mocked <see cref="IUiActionAckCoordinator"/> —
/// the endpoint's only collaborator.
/// </remarks>
public class ChatAckEndpointsContractTests : IClassFixture<ChatAckEndpointsTestFixture>
{
    private readonly ChatAckEndpointsTestFixture _fx;
    private const string TestSessionId = "22222222-3333-4444-5555-666666666666";

    public ChatAckEndpointsContractTests(ChatAckEndpointsTestFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Post_Ack_WithPendingWaiter_Returns200Acknowledged()
    {
        _fx.AckCoordinatorMock.Reset();
        _fx.AckCoordinatorMock
            .Setup(a => a.TryAcknowledge(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/ack",
            new { frameId = "frame-abc" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("acknowledged").GetBoolean().Should().BeTrue();

        // Session id normalization (dashes stripped) must match what the tool handler used
        // when it registered the wait (ChatSessionId.ToString("N")).
        _fx.AckCoordinatorMock.Verify(
            a => a.TryAcknowledge(TestSessionId.Replace("-", ""), "frame-abc"),
            Times.Once);
    }

    [Fact]
    public async Task Post_Ack_NoPendingWaiter_Returns200NotAcknowledged()
    {
        _fx.AckCoordinatorMock.Reset();
        _fx.AckCoordinatorMock
            .Setup(a => a.TryAcknowledge(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/ack",
            new { frameId = "frame-already-timed-out" });

        // A "nothing pending" ack is benign — never a 4xx/5xx (the client fire-and-forgets).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("acknowledged").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Post_Ack_MissingFrameId_Returns400()
    {
        _fx.AckCoordinatorMock.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/ack",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.AckCoordinatorMock.Verify(
            a => a.TryAcknowledge(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Post_Ack_Unauthenticated_Returns401()
    {
        _fx.AckCoordinatorMock.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/ack",
            new { frameId = "frame-abc" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fx.AckCoordinatorMock.Verify(
            a => a.TryAcknowledge(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Endpoint_IsRegistered_AtExpectedRoute()
    {
        _fx.AckCoordinatorMock.Reset();
        _fx.AckCoordinatorMock
            .Setup(a => a.TryAcknowledge(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/ack",
            new { frameId = "frame-abc" });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "MapChatAckEndpoints() must register POST /api/ai/chat/sessions/{sessionId}/ack");
    }
}

/// <summary>
/// Test fixture for <see cref="ChatAckEndpointsContractTests"/>. Hosts a minimal
/// <see cref="WebApplication"/> that maps ONLY
/// <see cref="ChatAckEndpoints.MapChatAckEndpoints"/> against a mocked
/// <see cref="IUiActionAckCoordinator"/>.
/// </summary>
public sealed class ChatAckEndpointsTestFixture : IAsyncLifetime, IDisposable
{
    public Mock<IUiActionAckCoordinator> AckCoordinatorMock { get; } = new();

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = ChatAckFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = ChatAckFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ChatAckFakeAuthHandler>(
                ChatAckFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        // ai-context policy — mirrors SummarizeSessionEndpoint's fixture: a no-op limiter so
        // .RequireRateLimiting("ai-context") doesn't throw at request time in this minimal host.
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-context", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-context-test"));
        });

        // Required by AddAiAuthorizationFilter() — this endpoint carries no document ids, so
        // the pass-through path activates; the service is still resolved via DI.
        builder.Services.AddSingleton(new Mock<IAiAuthorizationService>().Object);

        builder.Services.AddSingleton(AckCoordinatorMock.Object);

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapChatAckEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateUnauthenticatedClient() => _app!.GetTestClient();
}

/// <summary>Bearer-presence auth handler for the in-process WebApplication test fixture.</summary>
public sealed class ChatAckFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ChatAckFakeAuth";

    public ChatAckFakeAuthHandler(
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
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", "00000000-0000-0000-0000-000000000aaa"),
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000aaa"),
            new("tid", "00000000-0000-0000-0000-000000000abc"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
