using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2762: the <c>CopilotAuth</c> <c>OnAuthenticationFailed</c> handler decoded every bearer token that failed the default
/// (workforce) scheme and logged its audience, issuer and appid/azp plus the validation exception message, on every route
/// (<c>/mcp</c> included). ADR-028 A10 and ADR-015 allow ids, sizes, timings and reason codes in logs, not token claims.
/// The handler now logs a failure class, whether the audience is a configured one (a bool), and the trace id.
/// <para>A REAL request through <see cref="CustomWebAppFactory"/> with the default scheme pointed back at JwtBearer (the
/// factory swaps in a fake handler): the token is signed with a local key, so signature validation passes and only the
/// lifetime or audience check fails. Token times are fixed constants, never the wall clock.</para>
/// </summary>
public class Issue2762_AuthFailureLogTests : IClassFixture<CustomWebAppFactory>
{
    private const string DistinctAudience = "api://aud-2762-must-not-be-logged";
    private const string DistinctIssuer = "https://iss-2762-must-not-be-logged.example.com/";
    private const string DistinctAppId = "appid-2762-must-not-be-logged";
    private const string CopilotAuthCategory = "CopilotAuth";

    // Fixed instants: an expired token lived in 2020; a live one runs to 2099. No wall-clock reads.
    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FarFuture = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "issue2762-test-key" };

    private readonly CustomWebAppFactory _factory;

    public Issue2762_AuthFailureLogTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ExpiredToken_LogsFailureClass_NotAudienceIssuerOrAppId()
    {
        var logs = new CapturingLoggerProvider();
        // The audience is a configured one, so the lifetime check is the only thing that can fail.
        using var client = CreateClient(logs, audienceIsConfigured: true);

        var status = await GetAsync(client, Token(DistinctAudience, expires: LongAgo.AddDays(1)));

        status.Should().Be(HttpStatusCode.Unauthorized);
        var entry = logs.Entries.Where(IsAuthFailureEntry).Should().ContainSingle().Subject;
        AssertNoTokenClaims(entry);
        entry.Message.Should().Contain("FailureClass=expired").And.Contain("AudienceConfigured=True");
        entry.Level.Should().Be(LogLevel.Information, "clients present an expired token on every refresh");
    }

    [Fact]
    public async Task WrongAudienceToken_LogsFailureClass_NotAudienceIssuerOrAppId()
    {
        var logs = new CapturingLoggerProvider();
        using var client = CreateClient(logs, audienceIsConfigured: false);

        var status = await GetAsync(client, Token(DistinctAudience, expires: FarFuture));

        status.Should().Be(HttpStatusCode.Unauthorized);
        var entry = logs.Entries.Where(IsAuthFailureEntry).Should().ContainSingle().Subject;
        AssertNoTokenClaims(entry);
        entry.Message.Should().Contain("FailureClass=bad_audience").And.Contain("AudienceConfigured=False");
        entry.Level.Should().Be(LogLevel.Warning);
    }

    // The CopilotAuth category has a second writer, the DEBUG "Token on {Method} {Path}" middleware in
    // MiddlewarePipelineExtensions, which logs the same claims on every /api request. It is tracked separately (#1574) and
    // is not part of this fix, so this test selects the OnAuthenticationFailed entry by its message.
    private static bool IsAuthFailureEntry(LogEntry e)
        => e.Category == CopilotAuthCategory && e.Message.StartsWith("JWT auth failed", StringComparison.Ordinal);

    private static void AssertNoTokenClaims(LogEntry entry)
    {
        entry.Message.Should().NotContain(DistinctAudience).And.NotContain(DistinctIssuer).And.NotContain(DistinctAppId);
        entry.Exception.Should().BeNull("the exception message embeds the token's audience and issuer");
        entry.State.Should().NotContain(v => v != null && (
            v.ToString()!.Contains(DistinctAudience) || v.ToString()!.Contains(DistinctIssuer) || v.ToString()!.Contains(DistinctAppId)));
    }

    private HttpClient CreateClient(ILoggerProvider logs, bool audienceIsConfigured)
        => _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                // CustomWebAppFactory makes a fake handler the default scheme; point it back at the real JwtBearer
                // scheme (and its CopilotAuth failure handler) so the token is actually validated.
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                });

                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    // Signing keys come from a local RSA key so no request reaches login.microsoftonline.com.
                    var metadata = new OpenIdConnectConfiguration { Issuer = DistinctIssuer };
                    metadata.SigningKeys.Add(SigningKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                    // Issuer validation is not under test; accept any issuer so only lifetime / audience decide.
                    options.TokenValidationParameters.IssuerValidator = (issuer, _, _) => issuer;
                    if (audienceIsConfigured)
                    {
                        options.TokenValidationParameters.ValidAudiences =
                            (options.TokenValidationParameters.ValidAudiences ?? []).Append(DistinctAudience).ToList();
                    }
                });
            });
        }).CreateClient();

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string Token(string audience, DateTime expires)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DistinctIssuer,
            Audience = audience,
            IssuedAt = LongAgo,
            NotBefore = LongAgo,
            Expires = expires,
            Subject = new ClaimsIdentity(new List<Claim>
            {
                new("ver", "1.0"),
                new("appid", DistinctAppId),
                new("azp", DistinctAppId),
                new("oid", "00000000-0000-4000-8000-000000002762"),
                new("sub", "sub-2762"),
            }),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<object?> State);

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
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.Select(p => p.Value).ToList()
                    : new List<object?>();
                lock (owner._entries)
                {
                    owner._entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception, values));
                }
            }
        }
    }
}
