using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// REGRESSION GUARD — spaarkeai-mcp-bff-api-r2 task 001 review follow-up (rule at AuthorizationModule, #2762).
///
/// <para>A DEBUG middleware ("Log auth header presence for Copilot API plugin diagnostics", logger category
/// <c>CopilotAuth</c>) ran BEFORE <c>UseAuthentication</c> and, on every <c>/api</c> request, decoded the UNVALIDATED
/// bearer token and logged its audience, issuer, appid/azp and scope at Warning. ADR-028 A10 and ADR-015 allow ids,
/// sizes, timings and reason codes in logs, never token claims; the middleware was removed.</para>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> the middleware (or a copy of it) returns and every
/// <c>/api</c> request writes the caller's token claims to the log, including requests the authentication layer then
/// rejects.</para>
///
/// <para>A REAL request through <see cref="CustomWebAppFactory"/> with a capturing logger provider. The token carries
/// distinctive audience, issuer and appid values; none of them may appear in any log entry, and the old middleware's
/// messages ("Token on", "No Authorization header") may not be written.</para>
/// </summary>
public class PreAuthTokenClaimLoggingRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private const string DistinctAudience = "api://aud-copilot-diag-must-not-be-logged";
    private const string DistinctIssuer = "https://iss-copilot-diag-must-not-be-logged.example.com/";
    private const string DistinctAppId = "appid-copilot-diag-must-not-be-logged";

    // Fixed instants, no wall-clock reads.
    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FarFuture = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "preauth-claim-logging-test-key" };

    private readonly CustomWebAppFactory _factory;

    public PreAuthTokenClaimLoggingRetirementTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ApiRequestWithABearerToken_LogsNoTokenClaims()
    {
        var logs = new CapturingLoggerProvider();
        using var client = CreateClient(logs);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());

        using var response = await client.SendAsync(request);

        logs.Entries.Should().NotBeEmpty("the capture must be wired, or the absence checks below prove nothing");
        logs.Entries.Should().NotContain(e =>
                e.Message.Contains(DistinctAudience) || e.Message.Contains(DistinctIssuer) || e.Message.Contains(DistinctAppId),
            "no log entry may carry a bearer token's audience, issuer or appid");
        logs.Entries.Should().NotContain(e => e.Category == "CopilotAuth" && e.Message.Contains("aud="),
            "the pre-authentication token-claim diagnostic was removed");
        logs.Entries.Should().NotContain(e => e.Message.StartsWith("Token on ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApiRequestWithoutAnAuthorizationHeader_WritesNoCopilotAuthDiagnostic()
    {
        var logs = new CapturingLoggerProvider();
        using var client = CreateClient(logs);

        using var response = await client.GetAsync("/api/me");

        logs.Entries.Should().NotContain(e =>
            e.Category == "CopilotAuth" && e.Message.StartsWith("No Authorization header", StringComparison.Ordinal));
    }

    private HttpClient CreateClient(ILoggerProvider logs)
        => _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }).CreateClient();

    private static string Token()
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DistinctIssuer,
            Audience = DistinctAudience,
            IssuedAt = LongAgo,
            NotBefore = LongAgo,
            Expires = FarFuture,
            Subject = new ClaimsIdentity(new List<Claim>
            {
                new("appid", DistinctAppId),
                new("azp", DistinctAppId),
                new("scp", "access_as_user"),
                new("oid", "00000000-0000-4000-8000-000000000001"),
            }),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                {
                    owner._entries.Add(new LogEntry(category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
