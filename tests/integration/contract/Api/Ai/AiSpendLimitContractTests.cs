using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Metering;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Task 254 — the stamp's optional monthly OpenAI spend limit, through the REAL Program (routes, filters, global
/// exception handler, DI container): an over-limit chat turn on the caller's own session is a 429 ProblemDetails with
/// <c>Retry-After</c> and the stable code, refused before any agent turn runs; without a limit the turn runs; and both AI
/// client seams the container hands out — the <see cref="IChatClient"/> pipeline and <see cref="OpenAiClient"/> — refuse
/// an over-limit call (task 077's gate was lost once by a merge dropping exactly this wiring). Reuses the chat
/// route-proof host with one setting added (CLAUDE.md §11 — no new fixture).
/// </summary>
public sealed class AiSpendLimitContractTests : IClassFixture<ChatAgentRouteProofFixture>
{
    private static readonly DateTimeOffset MidMonth = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly ChatAgentRouteProofFixture _fixture;

    public AiSpendLimitContractTests(ChatAgentRouteProofFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ChatTurn_OverTheLimit_Is429ProblemDetails_WithRetryAfterToTheNextUtcMonth()
    {
        using var host = OverLimitHost();
        var sessionId = OwnedSession();
        using var client = Caller(host);

        var response = await client.PostAsJsonAsync($"/api/ai/chat/sessions/{sessionId}/messages", new { message = "hi" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.RetryAfter!.Delta.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero) - MidMonth);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["extensions"]!["code"]!.GetValue<string>().Should().Be(AiSpendLimitExceededException.ErrorCode);
        problem["status"]!.GetValue<int>().Should().Be(429);
        _fixture.Agents.CreatedFor.Should().BeEmpty("the turn is refused before any agent turn runs");
    }

    [Fact]
    public async Task ChatTurn_WithoutALimit_RunsTheTurn()
    {
        using var client = Caller(_fixture);
        _fixture.Services.GetRequiredService<IAiSpendLedger>().Add(1_000_000m, DateTimeOffset.UtcNow);
        var sessionId = OwnedSession();

        var response = await client.PostAsJsonAsync($"/api/ai/chat/sessions/{sessionId}/messages", new { message = "hi" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "no limit is configured — the default (G37)");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        (await response.Content.ReadAsStringAsync()).Should().Contain(ChatAgentRouteProofFixture.AgentAnswer, "the turn ran");
    }

    [Fact]
    public async Task TheContainersChatClient_OverTheLimit_RefusesBeforeCallingTheModel()
    {
        using var host = OverLimitHost();
        var chatClient = host.Services.GetRequiredService<IChatClient>();

        // AiSpendLimitExceededException is sealed and thrown only by the limit: nothing else satisfies the assertion.
        await chatClient.Invoking(c => c.GetResponseAsync("hi")).Should().ThrowAsync<AiSpendLimitExceededException>();
    }

    [Fact]
    public async Task TheContainersOpenAiClient_OverTheLimit_RefusesBeforeCallingTheModel()
    {
        using var host = OverLimitHost();
        var openAi = host.Services.GetRequiredService<OpenAiClient>();

        await openAi.Invoking(c => c.GetCompletionAsync("hi")).Should().ThrowAsync<AiSpendLimitExceededException>();
    }

    /// <summary>The route-proof host with a $5 limit, a fixed clock, and $6 already spent this month.</summary>
    private WebApplicationFactory<Program> OverLimitHost()
    {
        var host = _fixture.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["AiSpendLimit:MonthlyLimitUsd"] = "5" }));
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(MidMonth));
            });
        });
        host.Services.GetRequiredService<IAiSpendLedger>().Add(6m, MidMonth);
        return host;
    }

    /// <summary>A session the route-sweep caller owns, so the ownership filter admits the request to the handler.</summary>
    private string OwnedSession()
    {
        _fixture.ResetBoundaries();
        var sessionId = Guid.NewGuid().ToString("N");
        _fixture.Repo
            .Setup(r => r.GetSessionAsync(RouteSweepAuthorizationFixture.CallerTenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession(
                SessionId: sessionId,
                TenantId: RouteSweepAuthorizationFixture.CallerTenantId,
                DocumentId: null,
                PlaybookId: null,
                CreatedAt: DateTimeOffset.UtcNow,
                LastActivity: DateTimeOffset.UtcNow,
                Messages: [])
            { OwnerOid = RouteSweepAuthorizationFixture.CallerObjectId });
        return sessionId;
    }

    private static HttpClient Caller(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RouteSweepAuthorizationFixture.BearerToken);
        return client;
    }
}
