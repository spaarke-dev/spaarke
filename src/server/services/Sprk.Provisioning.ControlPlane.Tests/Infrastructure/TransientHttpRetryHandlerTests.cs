// -----------------------------------------------------------------------------
// TransientHttpRetryHandlerTests.cs
//
// T186 audit — the Worker's Dataverse/Graph REST clients sent each request once,
// so a service-protection 429 or a gateway 503 failed (often quarantined) the
// handler. TransientHttpRetryHandler retries what the service did not process.
// ADR-038 path #1: the handler is driven through a real HttpClient pipeline.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Infrastructure.Http;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Infrastructure;

public sealed class TransientHttpRetryHandlerTests
{
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] script) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            var step = script[Math.Min(Calls, script.Length - 1)];
            Calls++;
            return await step(request);
        }
    }

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> Status(HttpStatusCode code, TimeSpan? retryAfter = null) =>
        _ =>
        {
            var response = new HttpResponseMessage(code);
            if (retryAfter is { } delta)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
            }
            return Task.FromResult(response);
        };

    private static (HttpClient Client, ScriptedHandler Inner) Build(int maxAttempts, params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] script)
    {
        var inner = new ScriptedHandler(script);
        var retry = new TransientHttpRetryHandler(
            NullLogger<TransientHttpRetryHandler>.Instance, maxAttempts, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5))
        { InnerHandler = inner };
        return (new HttpClient(retry) { BaseAddress = new Uri("https://spaarke-acme.crm.dynamics.com/") }, inner);
    }

    [Fact]
    public async Task Throttled429OnAPost_IsReplayedWithTheSameBody_UntilAccepted()
    {
        var (client, inner) = Build(5,
            Status(HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(2)),
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.NoContent));

        using var response = await client.PostAsync("api/data/v9.2/sprk_skills", JsonContent.Create(new { sprk_name = "summarize" }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "Dataverse rejected the throttled request without executing it");
        inner.Calls.Should().Be(3);
        inner.Bodies.Should().AllBe("{\"sprk_name\":\"summarize\"}", "a replay sends the same bytes");
    }

    [Fact]
    public async Task GatewayErrorOnAGet_IsRetried()
    {
        var (client, inner) = Build(5, Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.OK));

        using var response = await client.GetAsync("api/data/v9.2/sprk_skills");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task GatewayErrorOnAPost_IsNotReplayed_BecauseTheWriteMayHaveLanded()
    {
        var (client, inner) = Build(5, Status(HttpStatusCode.GatewayTimeout), Status(HttpStatusCode.NoContent));

        using var response = await client.PostAsync("api/data/v9.2/sprk_skills", JsonContent.Create(new { sprk_name = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task TransportFailureOnAGet_IsRetried()
    {
        var calls = 0;
        var (client, inner) = Build(5, _ => ++calls == 1 ? throw new HttpRequestException("connection reset") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var response = await client.GetAsync("api/data/v9.2/sprk_skills");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task PersistentThrottling_ReturnsTheLast429AfterMaxAttempts()
    {
        var (client, inner) = Build(3, Status(HttpStatusCode.TooManyRequests));

        using var response = await client.GetAsync("api/data/v9.2/sprk_skills");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Calls.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherStatuses_AreReturnedAtOnce(HttpStatusCode status)
    {
        var (client, inner) = Build(5, Status(status), Status(HttpStatusCode.OK));

        using var response = await client.GetAsync("api/data/v9.2/sprk_skills");

        response.StatusCode.Should().Be(status);
        inner.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, false, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, false)]
    [InlineData(HttpStatusCode.RequestTimeout, false, false)]
    [InlineData(HttpStatusCode.InternalServerError, true, false)]
    public void IsRetryable_RetriesOnlyWhatTheServiceDidNotProcess(HttpStatusCode status, bool idempotent, bool expected)
        => TransientHttpRetryHandler.IsRetryable(status, idempotent).Should().Be(expected);

    [Fact]
    public async Task RetryAfterLongerThanTheBudget_ReturnsThe429AtOnce_InsteadOfSleepingIntoTheClientTimeout()
    {
        var (client, inner) = Build(5, Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(45)), Status(HttpStatusCode.OK));

        using var response = await client.GetAsync("api/data/v9.2/sprk_skills");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the caller's own failure mapping sees the throttle");
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public void ProductionBudget_StaysWellUnderTheWorkersThirtySecondClientTimeout()
    {
        var handler = new TransientHttpRetryHandler(NullLogger<TransientHttpRetryHandler>.Instance);

        var worstSleep = TimeSpan.Zero;
        for (var attempt = 1; attempt < handler.MaxAttempts; attempt++)
        {
            var backoff = TimeSpan.FromTicks(handler.BaseDelay.Ticks * (1L << (attempt - 1)));
            worstSleep += backoff > handler.MaxDelay ? handler.MaxDelay : backoff;
        }

        worstSleep.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
