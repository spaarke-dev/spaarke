using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Metering;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Task 254 — the stamp's optional monthly OpenAI spend limit, through the REAL Program (routes, filters, global
/// exception handler): an over-limit chat turn on the caller's own session is a 429 ProblemDetails with
/// <c>Retry-After</c> and the stable code, refused before any agent turn runs; without a limit it is not refused for
/// spend. Reuses the chat route-proof host with one setting added (CLAUDE.md §11 — no new fixture).
/// </summary>
public sealed class AiSpendLimitContractTests : IClassFixture<ChatAgentRouteProofFixture>
{
    private readonly ChatAgentRouteProofFixture _fixture;

    public AiSpendLimitContractTests(ChatAgentRouteProofFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ChatTurn_OverTheLimit_Is429ProblemDetails_WithRetryAfter()
    {
        using var host = _fixture.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["AiSpendLimit:MonthlyLimitUsd"] = "5" })));
        host.Services.GetRequiredService<IAiSpendLedger>().Add(6m, DateTimeOffset.UtcNow);
        var sessionId = OwnedSession();
        using var client = Caller(host);

        var response = await client.PostAsJsonAsync($"/api/ai/chat/sessions/{sessionId}/messages", new { message = "hi" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.RetryAfter!.Delta!.Value.Should().BePositive()
            .And.BeLessThanOrEqualTo(TimeSpan.FromDays(31), "the limit resets at the next UTC month start");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["extensions"]!["code"]!.GetValue<string>().Should().Be(AiSpendLimitExceededException.ErrorCode);
        problem["status"]!.GetValue<int>().Should().Be(429);
        _fixture.Agents.CreatedFor.Should().BeEmpty("the turn is refused before any agent turn runs");
    }

    [Fact]
    public async Task ChatTurn_WithoutALimit_IsNotRefusedForSpend()
    {
        using var client = Caller(_fixture);
        _fixture.Services.GetRequiredService<IAiSpendLedger>().Add(1_000_000m, DateTimeOffset.UtcNow);
        var sessionId = OwnedSession();

        var response = await client.PostAsJsonAsync($"/api/ai/chat/sessions/{sessionId}/messages", new { message = "hi" });

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, "no limit is configured — the default (G37)");
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
