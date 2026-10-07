using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// CONTRACT — the liveness probes' rate limit (owner rounds 12 item 1 and 14 item 1, unified-access-control-r2
/// task 167 f1).
///
/// <para><b>The contract.</b> <c>GET /healthz</c>, <c>GET /healthz/catalog</c> and <c>GET /ping</c> are anonymous, so
/// a mandatory compensating control is required (round 12 item 1): they ARE rate limited per client IP. But the limit
/// is the dedicated <c>"health-probe"</c> policy (120 per minute, sliding window, no queue —
/// <c>RateLimitingModule.cs</c>), not the shared <c>"anonymous"</c> policy (10 per minute, fixed window). Under
/// "anonymous" a 5-second deploy poller (<c>deploy-bff-api.yml</c> 12 x 5 s; <c>Deploy-BffApi.ps1</c> and the
/// control plane's H9 probe 24 x 5 s) lost its 11th and 12th poll of a window to 429, so a production verify could
/// roll back a good deploy; a 429 on the slot-swap warm-up ping STOPS the swap. Every other anonymous route keeps the
/// strict policy.</para>
///
/// <para><b>Why the REAL app.</b> The property that matters is that the three real routes carry the probe policy and
/// the others do not — a minimal host would prove only that the policy object exists. Each test drives one client IP
/// of its own (<see cref="TestServer.SendAsync(Action{HttpContext}, CancellationToken)"/> sets
/// <c>Connection.RemoteIpAddress</c>, the partition key both policies use), so the tests are independent of each
/// other and of the order they run in. The route-to-policy wiring is also pinned at build time by
/// <c>RouteAuthorizationGuardTests.TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes</c>.</para>
/// </summary>
[Trait("status", "repaired")]
public class HealthProbeRateLimitContractTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public HealthProbeRateLimitContractTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpResponse> GetAsync(string path, string clientIp)
    {
        var context = await _factory.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = path;
            c.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
        });
        return context.Response;
    }

    [Fact]
    public async Task AFiveSecondDeployPoller_NeverSees429_OnTheLivenessProbes()
    {
        // 24 polls each of /healthz and /ping from ONE address: Deploy-BffApi.ps1's whole budget (24 x 5 s) on both
        // probes at once, compressed into well under a minute — 48 requests, more than four times the old 10/min.
        const string poller = "203.0.113.10";
        var statuses = new List<int>();
        for (var i = 0; i < 24; i++)
        {
            statuses.Add((await GetAsync("/healthz", poller)).StatusCode);
            statuses.Add((await GetAsync("/ping", poller)).StatusCode);
        }

        statuses.Should().NotContain(StatusCodes.Status429TooManyRequests,
            "the liveness probes use the dedicated \"health-probe\" policy (120/min per client IP); under the shared "
            + "\"anonymous\" policy (10/min) the 11th poll of a window was refused and a production verify could roll "
            + "back a good deploy (owner round 14 item 1)");
        statuses.Where((_, i) => i % 2 == 1).Should().OnlyContain(s => s == StatusCodes.Status200OK, "/ping answers pong");
    }

    [Fact]
    public async Task TheProbes_AreStillRateLimited_PerClientIp()
    {
        // Round 12 item 1 still holds: a probe flood from one address is refused, at once (no queue), with Retry-After.
        // The limiter runs on the wall clock (a sliding window returns a segment's permits 60 s later), so the 120 calls
        // must land inside one window; on a starved machine an attempt that took longer is repeated from a fresh address
        // rather than read as a pass or a fail.
        string flooder = string.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            flooder = $"203.0.113.{20 + attempt}";
            var started = DateTime.UtcNow;
            for (var i = 1; i <= 120; i++)
            {
                (await GetAsync("/ping", flooder)).StatusCode.Should().Be(StatusCodes.Status200OK, $"request {i} of 120 is within the budget");
            }

            if (DateTime.UtcNow - started < TimeSpan.FromSeconds(50))
            {
                break;
            }
        }

        var refused = await GetAsync("/ping", flooder);
        refused.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
            "the 121st probe in a minute from one address exceeds \"health-probe\" — the anonymous probe keeps a MANDATORY control");
        refused.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty("a 429 tells the poller when to come back (ADR-016)");

        (await GetAsync("/ping", "203.0.113.99")).StatusCode.Should().Be(StatusCodes.Status200OK,
            "the budget is per client IP: one address's flood does not refuse another's probe");
    }

    [Fact]
    public async Task NegativeControl_TheOtherAnonymousRoutes_KeepTheStrictAnonymousPolicy()
    {
        // Proves the limiter is live in this host (so the no-429 assertion above is not vacuous) and that the looser
        // probe budget did not spread: GET /status, an anonymous metadata probe, still refuses the 11th call.
        string scraper = string.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            scraper = $"203.0.113.{30 + attempt}";
            var started = DateTime.UtcNow;
            for (var i = 1; i <= 10; i++)
            {
                (await GetAsync("/status", scraper)).StatusCode.Should().Be(StatusCodes.Status200OK, $"request {i} of 10 is within \"anonymous\"");
            }

            if (DateTime.UtcNow - started < TimeSpan.FromSeconds(50))
            {
                break;   // inside one fixed window (see the flood test for why a slow attempt is repeated)
            }
        }

        (await GetAsync("/status", scraper)).StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
            "/status keeps the shared \"anonymous\" policy (10/min per client IP)");
    }
}
