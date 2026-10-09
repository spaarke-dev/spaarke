// CONTRACT — the rate limiter is live in the REAL request pipeline for AUTHENTICATED, per-user policies
// (ADR-016). KEEP path: endpoint-contract.
//
// WHY THIS FILE EXISTS
//   Route metadata tests (e.g. ChatMessageStreamingContractTests' "ai-stream" binding) prove a route NAMES a
//   policy; they cannot notice the middleware being removed (MiddlewarePipelineExtensions: app.UseRateLimiter())
//   or moved ahead of UseAuthentication — which would silently turn every per-user AI limit into one bucket
//   shared by every caller. This boots WebApplicationFactory<Program> and observes the 429 itself.
//
// WHY IT IS DETERMINISTIC
//   "ai-upload" (RateLimitingModule) is a FIXED window: PermitLimit = 5, Window = 1 minute, QueueLimit = 0,
//   partitioned by the caller's identity. A partition's limiter is created on that caller's first request and
//   its window runs from that moment, so six back-to-back requests from a brand-new caller always fall inside
//   one window; with no queue the sixth is refused at once instead of waiting. Each test run uses fresh caller
//   ids, so no state carries between tests or runs. The limiter runs BEFORE the handler and its filters, so
//   the status of requests 1-5 is irrelevant — only that none of them is a 429 and the sixth is.
//
// RELATION TO HealthProbeRateLimitContractTests
//   Those tests also see a 429 through the real host, but only on ANONYMOUS, per-IP policies. They keep passing
//   if UseRateLimiter() is moved ahead of UseAuthentication(); this test does not (the bystander assertion).

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.RateLimiting;

public sealed class RateLimiterPipelineContractTests : IClassFixture<RateLimiterPipelineFixture>
{
    /// <summary>A route bound to "ai-upload" (VisualizationEndpoints).</summary>
    private const string AiUploadRoute = "/api/ai/visualization/related-from-content";

    /// <summary>"ai-upload" PermitLimit (RateLimitingModule).</summary>
    private const int AiUploadPermitLimit = 5;

    private readonly RateLimiterPipelineFixture _fixture;

    public RateLimiterPipelineContractTests(RateLimiterPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AiUploadRoute_OneCallerExceedsThePermitLimit_IsRefusedWith429ProblemDetails_WhileAnotherCallerIsNot()
    {
        var flooder = Guid.NewGuid().ToString();
        var bystander = Guid.NewGuid().ToString();

        var withinBudget = new List<HttpStatusCode>();
        for (var i = 0; i < AiUploadPermitLimit; i++)
        {
            withinBudget.Add((await PostAsync(flooder)).StatusCode);
        }

        using var refused = await PostAsync(flooder);
        using var otherCaller = await PostAsync(bystander);

        withinBudget.Should().NotContain(HttpStatusCode.TooManyRequests,
            $"the first {AiUploadPermitLimit} calls in a window are inside \"ai-upload\"");
        withinBudget.Should().NotContain(HttpStatusCode.UnsupportedMediaType,
            "the probe requests must reach the rate-limited endpoint (not be rejected at endpoint selection)");
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the rate limiter middleware is in the pipeline and refuses the caller's 6th upload in the window (ADR-016)");
        refused.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "the rejection is the module's ProblemDetails response (ADR-019)");
        refused.Headers.RetryAfter.Should().NotBeNull("a 429 tells the caller when to come back");
        otherCaller.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the budget is per authenticated caller — the limiter runs after authentication, so one user's flood "
            + "does not refuse another user");
    }

    private async Task<HttpResponseMessage> PostAsync(string callerOid)
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add(RateLimiterFakeAuthHandler.CallerHeader, callerOid);
        // A WELL-FORMED multipart form with no 'file' field. It must be multipart: the route declares
        // Accepts("multipart/form-data"), so any other content type is answered 415 by endpoint selection and never
        // reaches the route's limiter. Past the limiter the handler answers its own clean 400 ("No file provided"),
        // keeping requests 1-5 cheap and side-effect free; their status is not asserted beyond "not 429".
        using var content = new MultipartFormDataContent { { new StringContent("rate-limit-probe"), "note" } };
        return await client.PostAsync(AiUploadRoute, content);
    }
}

/// <summary>
/// The full BFF host (<see cref="CustomWebAppFactory"/> config and boundary doubles) with an auth scheme whose
/// caller identity comes from a request header, so each test chooses its own rate-limit partition.
/// </summary>
public sealed class RateLimiterPipelineFixture : CustomWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, RateLimiterFakeAuthHandler>(RateLimiterFakeAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = RateLimiterFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RateLimiterFakeAuthHandler.SchemeName;
            });
        });
    }
}

/// <summary>Authenticates any request with a bearer header as the oid named in <see cref="CallerHeader"/>.</summary>
internal sealed class RateLimiterFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RateLimiterPipelineFakeAuth";
    public const string CallerHeader = "X-Test-Caller-Oid";

    public RateLimiterFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var oid = Request.Headers[CallerHeader].FirstOrDefault();
        if (!Request.Headers.ContainsKey("Authorization") || string.IsNullOrEmpty(oid))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header or caller"));
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new("sub", oid),
            new("tid", "tenant-rate-limiter-contract"),
            new(ClaimTypes.NameIdentifier, oid),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
