using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// Issue #2756 — the global rate limiter's <c>OnRejected</c> handler wrote
/// <c>HttpContext.User.Identity.Name</c> (the caller's UPN / email) and the client IP to App Insights at Warning on
/// every 429. ADR-015 allows identifiers, sizes, timings and reason codes in Tier 1 logs, not claim values.
/// The rejection is now logged against the opaque caller key the limiter partitions by (<c>oid</c>, else <c>sub</c>).
/// </summary>
/// <remarks>
/// Drives a real 429 through the full BFF pipeline: the <c>ai-upload</c> policy (5 per minute, no queue) on
/// <c>POST /api/ai/chat/sessions/{id}/documents</c>, called by a signed-in principal whose name is an email.
/// The limiter runs before the endpoint, so the request body is irrelevant and the first five calls simply 4xx.
/// </remarks>
public class Issue2756_RateLimitRejectionLogTests
{
    private const string CallerEmail = "jane.doe@contoso.example";
    private const string CallerOid = "5d1c6f0e-2b0f-4a43-9a45-2756a2756a27";
    private const string UploadPolicy = "ai-upload";
    private const int UploadPermitLimit = 5;
    private const string ClientIp = "203.0.113.77";

    [Fact]
    public async Task RateLimitRejection_ForSignedInUser_LogsOpaqueCallerKeyAndNoNameOrEmail()
    {
        // Arrange — full host, a signed-in principal whose Identity.Name is an email, and a log capture.
        var logLines = new ConcurrentQueue<string>();
        using var factory = new CustomWebAppFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(logLines)));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = EmailNamedUserAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = EmailNamedUserAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, EmailNamedUserAuthHandler>(
                        EmailNamedUserAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = EmailNamedUserAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = EmailNamedUserAuthHandler.SchemeName;
                });
                // TestServer has no remote address; give the caller one so the "no client IP in the log"
                // assertion below can actually fail.
                services.AddTransient<IStartupFilter, RemoteAddressStartupFilter>();
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — one more call than the policy permits; the last one is rejected by the limiter.
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call <= UploadPermitLimit; call++)
        {
            using var response = await client.PostAsync(
                $"/api/ai/chat/sessions/{Guid.NewGuid()}/documents", MultipartBody());
            statuses.Add(response.StatusCode);
        }

        // Assert — the 429 happened, was logged, and the log names no person.
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests, "statuses were {0}", string.Join(",", statuses));

        var rejection = logLines.Where(l => l.Contains("Rate limit exceeded")).ToList();
        rejection.Should().ContainSingle("exactly one request exceeded the limit");
        var line = rejection[0];

        line.Should().NotContain(CallerEmail, "a UPN / email is a claim value and ADR-015 forbids it in Tier 1 logs");
        line.Should().NotContain("contoso", "no part of the name may reach the log");
        line.Should().NotContain("@", "no email-shaped value may reach the log");
        line.Should().NotContain(ClientIp, "the client IP is not on ADR-015's allow-list");
        line.Should().Contain(CallerOid, "the opaque caller key (oid) identifies the caller instead");
        line.Should().Contain(UploadPolicy, "the policy that rejected the call is logged");
    }

    private static MultipartFormDataContent MultipartBody() =>
        // The route accepts multipart/form-data only; any other content type is answered 415 during endpoint
        // selection, before the rate limiter runs. The body is never read: the limiter precedes the endpoint.
        new() { { new ByteArrayContent([1]), "file", "x.txt" } };

    private sealed class RemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
                return nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class EmailNamedUserAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Issue2756Auth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
                return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

            // Like a real Entra token: Identity.Name resolves to the UPN / email.
            var identity = new ClaimsIdentity(
                [
                    new Claim("oid", CallerOid),
                    new Claim(ClaimTypes.Name, CallerEmail),
                    new Claim("preferred_username", CallerEmail),
                    new Claim("tid", "test-tenant-id"),
                ],
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}
