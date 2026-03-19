using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Integration tests verifying ADR-016 rate limiting compliance for all R2 AI endpoints.
///
/// Tests verify:
/// - 429 responses are returned with ProblemDetails format (ADR-019) when rate limits are exceeded
/// - Retry-After headers are included in 429 responses
/// - Content-Type: application/problem+json on rate limit error responses
/// - All R2 AI rate limiting policies (ai-stream, ai-upload, ai-export, ai-persist,
///   ai-context, ai-indexing, ai-batch) are registered and functional
/// - Policies are per-user (partitioned by 'oid' claim) and independent of each other
///
/// Strategy: We create a minimal test host that registers ONLY the RateLimitingModule
/// (the same production code) along with lightweight test endpoints bound to each policy.
/// This avoids the complexity of booting the full BFF API while testing the real
/// rate limiting middleware pipeline end-to-end.
///
/// 429 rejection testing uses the ai-upload policy (FixedWindow, QueueLimit=0) because
/// it rejects immediately when permits are exhausted — no queue waiting. The OnRejected
/// handler is shared across ALL policies, so verifying ProblemDetails format on ai-upload
/// confirms it works for all policies.
///
/// For policies with QueueLimit > 0 (ai-stream, ai-export, etc.), we verify they are
/// registered and accept requests within limits. The rate limiting middleware is the
/// same for all policies — only the limiter parameters differ.
/// </summary>
public class RateLimitingIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CreateTestHostAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>
    /// Creates a minimal test host with the production RateLimitingModule and
    /// lightweight test endpoints for each rate limiting policy.
    /// </summary>
    private static async Task<IHost> CreateTestHostAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, RateLimitTestAuthHandler>(
                            "Test", _ => { });
                    services.AddAuthorization();
                    services.AddLogging();

                    // Register the PRODUCTION rate limiting module — this is what we're testing
                    services.AddRateLimitingModule();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseRateLimiter();

                    app.UseEndpoints(endpoints =>
                    {
                        // Minimal handler — rate limiting middleware intercepts BEFORE
                        // the handler when the limit is exceeded, returning 429.
                        Delegate okHandler = () => Results.Ok("ok");

                        // Test endpoints — one per rate limiting policy
                        endpoints.MapPost("/test/ai-upload", okHandler)
                            .RequireRateLimiting("ai-upload");

                        endpoints.MapGet("/test/ai-stream", okHandler)
                            .RequireRateLimiting("ai-stream");

                        endpoints.MapPost("/test/ai-export", okHandler)
                            .RequireRateLimiting("ai-export");

                        endpoints.MapPost("/test/ai-persist", okHandler)
                            .RequireRateLimiting("ai-persist");

                        endpoints.MapGet("/test/ai-context", okHandler)
                            .RequireRateLimiting("ai-context");

                        endpoints.MapPost("/test/ai-indexing", okHandler)
                            .RequireRateLimiting("ai-indexing");

                        endpoints.MapGet("/test/ai-batch", okHandler)
                            .RequireRateLimiting("ai-batch");

                        endpoints.MapPost("/test/upload-heavy", okHandler)
                            .RequireRateLimiting("upload-heavy");
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    // =========================================================================
    // 429 Response Format Tests (ADR-019: ProblemDetails)
    //
    // Uses ai-upload (FixedWindow, PermitLimit=5, QueueLimit=0) because it
    // rejects immediately when permits are exhausted — deterministic and fast.
    // The OnRejected handler is shared across all policies.
    // =========================================================================

    [Fact]
    public async Task RateLimitResponse_HasProblemDetailsFormat()
    {
        // Arrange — exhaust 5 permits
        using var client = CreateClientForUser("pd-format-user");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/test/ai-upload", null);
        }

        // Act — 6th request rejected
        var response = await client.PostAsync("/test/ai-upload", null);

        // Assert — full ProblemDetails format verification per ADR-019
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "rate limit exceeded should return 429");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "rate limit error responses MUST use application/problem+json (ADR-019)");

        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;

        root.TryGetProperty("type", out var typeValue).Should().BeTrue(
            "ProblemDetails MUST have a 'type' field");
        typeValue.GetString().Should().Contain("rfc6585",
            "type should reference RFC 6585 section 4 (429 Too Many Requests)");

        root.TryGetProperty("title", out var titleValue).Should().BeTrue(
            "ProblemDetails MUST have a 'title' field");
        titleValue.GetString().Should().Be("Too Many Requests");

        root.TryGetProperty("status", out var statusValue).Should().BeTrue(
            "ProblemDetails MUST have a 'status' field");
        statusValue.GetInt32().Should().Be(429);

        root.TryGetProperty("detail", out _).Should().BeTrue(
            "ProblemDetails MUST have a 'detail' field");

        root.TryGetProperty("retryAfter", out _).Should().BeTrue(
            "ProblemDetails body should include retryAfter guidance");
    }

    [Fact]
    public async Task RateLimitResponse_IncludesRetryAfterHeader()
    {
        using var client = CreateClientForUser("retry-after-user");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/test/ai-upload", null);
        }

        var response = await client.PostAsync("/test/ai-upload", null);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull(
            "429 responses MUST include Retry-After header (ADR-016)");
    }

    [Fact]
    public async Task OnRejected_IncludesInstancePath()
    {
        using var client = CreateClientForUser("instance-path-user");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/test/ai-upload", null);
        }

        var response = await client.PostAsync("/test/ai-upload", null);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;

        root.TryGetProperty("instance", out var instanceValue).Should().BeTrue(
            "ProblemDetails should include 'instance' field with the request path");
        instanceValue.GetString().Should().Contain("/test/ai-upload",
            "instance should contain the actual request path");
    }

    [Fact]
    public async Task OnRejected_WritesCorrectContentType()
    {
        using var client = CreateClientForUser("content-type-user");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/test/ai-upload", null);
        }

        var response = await client.PostAsync("/test/ai-upload", null);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "OnRejected handler MUST set Content-Type to application/problem+json (ADR-019)");
    }

    // =========================================================================
    // Rate Limit Rejection Test (ai-upload: QueueLimit=0)
    // =========================================================================

    [Fact]
    public async Task AiUploadPolicy_Returns429_WhenRateLimited()
    {
        // ai-upload: FixedWindow, PermitLimit=5, QueueLimit=0
        using var client = CreateClientForUser("upload-429-user");

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsync("/test/ai-upload", null);
            ok.StatusCode.Should().Be(HttpStatusCode.OK,
                $"request {i + 1} of 5 should succeed within limit");
        }

        var response = await client.PostAsync("/test/ai-upload", null);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "ai-upload policy must return 429 when 5-request limit exceeded");
        response.Headers.RetryAfter.Should().NotBeNull(
            "429 from ai-upload MUST include Retry-After header (ADR-016)");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "rate limit error MUST use ProblemDetails format (ADR-019)");
    }

    // =========================================================================
    // Policy Registration Verification
    //
    // Each policy is verified by sending a single request and confirming 200 OK.
    // A 200 means the rate limiting middleware recognized the policy name and
    // the request was within limits. An invalid policy name would cause a
    // startup failure or 500 error.
    // =========================================================================

    [Theory]
    [InlineData("ai-stream", "GET", "SSE streaming (ChatEndpoints, AnalysisEndpoints, PlaybookRunEndpoints)")]
    [InlineData("ai-upload", "POST", "Document upload (ChatDocumentEndpoints)")]
    [InlineData("ai-export", "POST", "Word export (ChatWordExportEndpoints)")]
    [InlineData("ai-persist", "POST", "SPE persistence (ChatDocumentEndpoints persist)")]
    [InlineData("ai-context", "GET", "Analysis chat context (AnalysisChatContextEndpoints)")]
    [InlineData("ai-indexing", "POST", "Playbook embedding indexing (PlaybookEmbeddingEndpoints)")]
    [InlineData("ai-batch", "GET", "Background AI operations (RAG, search, knowledge base)")]
    public async Task R2AiEndpoint_HasRateLimitingPolicy(
        string policyName, string httpMethod, string endpointDescription)
    {
        using var client = CreateClientForUser($"policy-exists-{policyName}");

        var response = httpMethod == "POST"
            ? await client.PostAsync($"/test/{policyName}", null)
            : await client.GetAsync($"/test/{policyName}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"rate limiting policy '{policyName}' should be registered for {endpointDescription}");
    }

    // =========================================================================
    // Policy Configuration Tests
    // =========================================================================

    [Theory]
    [InlineData("ai-stream", "GET", 3)]
    [InlineData("ai-upload", "POST", 3)]
    [InlineData("ai-export", "POST", 3)]
    [InlineData("ai-persist", "POST", 3)]
    [InlineData("ai-indexing", "POST", 3)]
    [InlineData("ai-context", "GET", 3)]
    [InlineData("ai-batch", "GET", 3)]
    public async Task Policy_AllowsMultipleRequestsWithinLimit(
        string policyName, string httpMethod, int requestCount)
    {
        using var client = CreateClientForUser($"within-limit-{policyName}");

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < requestCount; i++)
        {
            var response = httpMethod == "POST"
                ? await client.PostAsync($"/test/{policyName}", null)
                : await client.GetAsync($"/test/{policyName}");
            responses.Add(response);
        }

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            $"all {requestCount} requests within {policyName} limit should succeed");
    }

    [Fact]
    public async Task DifferentPolicies_HaveIndependentLimits()
    {
        using var client = CreateClientForUser("policy-isolation-user");

        // Exhaust ai-upload (5 permits)
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/test/ai-upload", null);
        }

        // ai-stream should still be available (independent policy)
        var streamResponse = await client.GetAsync("/test/ai-stream");
        streamResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "ai-stream should not be affected when ai-upload is exhausted — policies are independent");

        // Confirm ai-upload is exhausted
        var uploadResponse = await client.PostAsync("/test/ai-upload", null);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "ai-upload should be exhausted after 5 requests");
    }

    [Fact]
    public async Task DifferentUsers_HaveIndependentLimits()
    {
        using var clientA = CreateClientForUser("user-a-isolation");
        using var clientB = CreateClientForUser("user-b-isolation");

        // Exhaust ai-upload for user A
        for (var i = 0; i < 5; i++)
        {
            await clientA.PostAsync("/test/ai-upload", null);
        }

        // User B should still have permits
        var response = await clientB.PostAsync("/test/ai-upload", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "user B should not be affected by user A's exhausted rate limit — limits are per-user");
    }

    [Fact]
    public async Task AiUploadPolicy_ExactPermitLimit_Is5()
    {
        // Verify the exact permit limit matches ADR-016 specification (5 uploads/minute/user)
        using var client = CreateClientForUser("exact-limit-user");

        // First 5 should succeed
        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsync("/test/ai-upload", null);
            ok.StatusCode.Should().Be(HttpStatusCode.OK,
                $"request {i + 1} should succeed (within 5-permit limit)");
        }

        // 6th should fail
        var rejected = await client.PostAsync("/test/ai-upload", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "6th request should be rejected — ADR-016 specifies 5 uploads/minute/user");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Creates an HttpClient with a unique user identity to isolate rate limit state.
    /// The RateLimitingModule partitions rate limits by the 'oid' claim, so each
    /// test user gets independent limits.
    /// </summary>
    private HttpClient CreateClientForUser(string userId)
    {
        var client = _host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userId);
        return client;
    }
}

/// <summary>
/// Authentication handler that assigns a unique user identity per request based
/// on the X-Test-User-Id header. Enables per-test rate limit isolation since
/// <see cref="RateLimitingModule"/> partitions rate limits by the 'oid' claim.
/// </summary>
public class RateLimitTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public RateLimitTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Test-User-Id"].FirstOrDefault()
            ?? "default-rate-limit-test-user";

        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim("sub", userId),
            new Claim("tid", "test-tenant-id"),
            new Claim(ClaimTypes.Name, $"Rate Limit Test User ({userId})"),
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
