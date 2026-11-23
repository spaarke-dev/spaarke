// spaarke-SPA-external-access-platform-r3 — ISS-001 (GitHub #1623)
//
// The default (workforce) JwtBearer scheme's Copilot-audience merge and its CopilotAuth sign-in-failure logging
// (the OnAuthenticationFailed wrap) are applied ONCE PER OPTIONS INSTANCE:
//   - every JwtBearerOptions("Bearer") instance gets the Copilot audience and the failure logging, not only the first
//     instance built in the process (a second test host, or an instance rebuilt after a configuration reload);
//   - configuring the SAME instance a second time adds nothing and does not wrap OnAuthenticationFailed again.
//
// Two layers of test, both against the REAL registrations made by AuthorizationModule.AddAuthorizationModule:
//   1. Instance level — the module's own PostConfigure<JwtBearerOptions>("Bearer") delegates are taken from the service
//      collection and applied to hand-made options instances, so "first instance", "second instance" and "same instance
//      again" are exact rather than inferred from host start order.
//   2. Host level — two independent TestServer hosts in one process, and an options instance rebuilt on one host (the
//      mechanical effect of a configuration reload: the options monitor drops its cached instance), with a Copilot
//      token sent over HTTP. This is the symptom the issue describes (Copilot tokens answered 401).
//
// Unlike AuthorizationModuleCiamTests, the host harness here does NOT re-add the Copilot audience: the module is the
// only thing that may put it there, which is exactly what is under test.
//
// The OnTokenValidated wraps made by the task 077 (A9) and task 010 (FR-23) delegates are separate, deliberately
// unconditional registrations and are not part of this contract; nothing below asserts on them.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public sealed class AuthorizationModuleCopilotAudienceTests
{
    private const string WorkforceTenantId = "11111111-1111-1111-1111-111111111111";
    private const string WorkforceBffClientId = "22222222-2222-2222-2222-222222222222";
    private const string WorkforceBffAudience = "api://" + WorkforceBffClientId;
    private const string CiamTenantId = "33333333-3333-3333-3333-333333333333";
    private const string StampCiamAppId = "44444444-4444-4444-4444-444444444444";
    private const string CopilotAudience = "api://auth-66666666-6666-6666-6666-666666666666";
    private const string UnrelatedAudience = "api://not-an-accepted-audience";
    private const string LegacySingularAudience = "api://legacy-singular-audience";

    private const string WorkforceIssuerV1 = $"https://sts.windows.net/{WorkforceTenantId}/";
    private const string WorkforceIssuerV2 = $"https://login.microsoftonline.com/{WorkforceTenantId}/v2.0";
    private const string WorkforceRoute = "/workforce";     // default policy → default (workforce) scheme

    private const string CopilotAuthLogCategory = "CopilotAuth";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "iss001-test-key" };
    private static readonly IssuerValidator IssuerValidatorSentinel = (issuer, _, _) => issuer;

    // The audiences a default-scheme instance accepts after the module has configured it: what was already there
    // (the plural list and the singular, which is folded into the plural), the Copilot audience, and the BFF app id in
    // both forms (task 077). WorkforceBffAudience IS api://{clientId}, so the set has four members.
    private static readonly string[] ExpectedAudiencesWithCopilot =
        [WorkforceBffAudience, LegacySingularAudience, CopilotAudience, WorkforceBffClientId];

    private static readonly string[] ExpectedAudiencesWithoutCopilot =
        [WorkforceBffAudience, LegacySingularAudience, WorkforceBffClientId];

    // ───────────────────────────── instance level ─────────────────────────────

    [Fact]
    public void EveryOptionsInstance_GetsTheCopilotAudience_NotOnlyTheFirstInTheProcess()
    {
        var delegates = ModuleDefaultSchemePostConfigures(Configuration());
        var instances = Enumerable.Range(0, 3).Select(_ => NewOptions()).ToList();

        foreach (var options in instances)
        {
            Apply(delegates, options);
        }

        foreach (var options in instances)
        {
            var parameters = options.TokenValidationParameters;
            parameters.ValidAudiences.Should().BeEquivalentTo(ExpectedAudiencesWithCopilot);
            parameters.ValidAudiences!.Count(a => string.Equals(a, CopilotAudience, StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, "the Copilot audience is added exactly once per instance");

            // Nothing but the audience list changes: the singular is folded in, issuers and validation flags are kept.
            parameters.ValidAudience.Should().BeNull();
            parameters.ValidIssuers.Should().BeEquivalentTo(new[] { WorkforceIssuerV2 });
            parameters.IssuerValidator.Should().BeSameAs(IssuerValidatorSentinel);
            parameters.ValidateAudience.Should().BeTrue();
            parameters.ValidateIssuer.Should().BeTrue();
        }
    }

    [Fact]
    public void CopilotAudienceNotConfigured_NoInstanceGetsOne()
    {
        // Control: the merge adds nothing when AgentToken:CopilotAudience is unset, on the first instance or any other.
        var delegates = ModuleDefaultSchemePostConfigures(Configuration(copilotAudience: null));
        var instances = Enumerable.Range(0, 2).Select(_ => NewOptions()).ToList();

        foreach (var options in instances)
        {
            Apply(delegates, options);
        }

        foreach (var options in instances)
        {
            options.TokenValidationParameters.ValidAudiences.Should().BeEquivalentTo(ExpectedAudiencesWithoutCopilot);
        }
    }

    [Fact]
    public async Task SameInstanceConfiguredTwice_OneCopilotAudienceEntry_AndFailureLoggingWrappedOnce()
    {
        var delegates = ModuleDefaultSchemePostConfigures(Configuration());
        var logs = new CapturingLoggerProvider();
        var innerCalls = 0;
        var options = NewOptions();
        // A handler installed before the module runs (the library's, in production) must survive and run once.
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = _ =>
            {
                innerCalls++;
                return Task.CompletedTask;
            },
        };

        Apply(delegates, options);
        var audiencesAfterFirstPass = options.TokenValidationParameters.ValidAudiences!.ToList();

        Apply(delegates, options); // the second pass on the SAME instance

        var audiences = options.TokenValidationParameters.ValidAudiences!.ToList();
        audiences.Should().BeEquivalentTo(audiencesAfterFirstPass, "a second pass changes nothing");
        audiences.Count(a => string.Equals(a, CopilotAudience, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "one Copilot audience entry, however many times the instance is configured");

        await FireAuthenticationFailedAsync(options, logs);

        logs.Entries.Where(e => e.Category == CopilotAuthLogCategory).Should().ContainSingle(
            "OnAuthenticationFailed is wrapped once; a second wrap would log the same failure twice");
        innerCalls.Should().Be(1, "the pre-existing handler is kept and invoked once per event");
    }

    [Fact]
    public async Task EachFreshInstance_GetsExactlyOneFailureLoggingWrap()
    {
        var delegates = ModuleDefaultSchemePostConfigures(Configuration());

        // Several instances in sequence, as a process sees across hosts or reloads; each must be wrapped, once.
        for (var i = 0; i < 3; i++)
        {
            var logs = new CapturingLoggerProvider();
            var innerCalls = 0;
            var options = NewOptions();
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = _ =>
                {
                    innerCalls++;
                    return Task.CompletedTask;
                },
            };

            Apply(delegates, options);
            await FireAuthenticationFailedAsync(options, logs);

            logs.Entries.Where(e => e.Category == CopilotAuthLogCategory).Should().ContainSingle(
                $"instance #{i + 1} is wrapped exactly once");
            innerCalls.Should().Be(1, $"instance #{i + 1} keeps its pre-existing handler");
        }
    }

    // ───────────────────────────── host level ─────────────────────────────

    [Fact]
    public async Task TwoIndependentHostsInOneProcess_BothAcceptACopilotAudienceToken()
    {
        using var first = await StartHostAsync(new CapturingLoggerProvider());
        using var second = await StartHostAsync(new CapturingLoggerProvider());

        foreach (var host in new[] { first, second })
        {
            var audiences = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters.ValidAudiences!;
            audiences.Count(a => string.Equals(a, CopilotAudience, StringComparison.OrdinalIgnoreCase)).Should().Be(1);

            (await GetAsync(host, WorkforceRoute, Token(CopilotAudience))).Should().Be(HttpStatusCode.OK);
            // Not widened: an audience nobody configured is still refused on every host.
            (await GetAsync(host, WorkforceRoute, Token(UnrelatedAudience))).Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task OptionsInstanceRebuiltOnTheSameHost_StillAcceptsCopilotAudience_AndLogsFailuresOnce()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);
        var monitor = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var original = monitor.Get(JwtBearerDefaults.AuthenticationScheme);

        // What a configuration reload does to the options: the monitor drops its cached instance and the next Get
        // builds a new one through the full configure / post-configure chain.
        host.Services.GetRequiredService<IOptionsMonitorCache<JwtBearerOptions>>()
            .TryRemove(JwtBearerDefaults.AuthenticationScheme).Should().BeTrue();
        var rebuilt = monitor.Get(JwtBearerDefaults.AuthenticationScheme);

        rebuilt.Should().NotBeSameAs(original);
        foreach (var options in new[] { original, rebuilt })
        {
            options.TokenValidationParameters.ValidAudiences!
                .Count(a => string.Equals(a, CopilotAudience, StringComparison.OrdinalIgnoreCase))
                .Should().Be(1);
        }

        (await GetAsync(host, WorkforceRoute, Token(CopilotAudience))).Should().Be(HttpStatusCode.OK);

        // The rebuilt instance carries the failure logging, once per failed request.
        (await GetAsync(host, WorkforceRoute, Token(UnrelatedAudience))).Should().Be(HttpStatusCode.Unauthorized);
        logs.Entries.Where(e => e.Category == CopilotAuthLogCategory).Should().ContainSingle();
    }

    // ───────────────────────────── harness ─────────────────────────────

    private static IConfiguration Configuration(string? copilotAudience = CopilotAudience)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = WorkforceTenantId,
            ["AzureAd:ClientId"] = WorkforceBffClientId,
            ["AzureAd:Audience"] = WorkforceBffAudience,
            ["Ciam:Instance"] = $"https://{CiamTenantId}.ciamlogin.com/",
            ["Ciam:TenantId"] = CiamTenantId,
            ["Ciam:Audience"] = StampCiamAppId,
        };
        if (copilotAudience is not null)
        {
            settings["AgentToken:CopilotAudience"] = copilotAudience;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>
    /// The PostConfigure delegates <see cref="AuthorizationModule.AddAuthorizationModule"/> registers for the default
    /// scheme, in registration order (the order the options factory runs them). The library's own registrations are
    /// not included: only <c>services.PostConfigure&lt;JwtBearerOptions&gt;("Bearer", action)</c> calls made by the module.
    /// </summary>
    private static IReadOnlyList<PostConfigureOptions<JwtBearerOptions>> ModuleDefaultSchemePostConfigures(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddAuthorizationModule(configuration);

        var delegates = services
            .Where(d => d.ServiceType == typeof(IPostConfigureOptions<JwtBearerOptions>))
            .Select(d => d.ImplementationInstance)
            .OfType<PostConfigureOptions<JwtBearerOptions>>()
            .Where(p => p.Name == JwtBearerDefaults.AuthenticationScheme && IsDeclaredInAuthorizationModule(p.Action))
            .ToList();

        delegates.Should().NotBeEmpty("the module registers PostConfigure delegates for the default scheme");
        return delegates;
    }

    // The module's lambdas live on compiler-generated closure classes nested in AuthorizationModule.
    private static bool IsDeclaredInAuthorizationModule(Action<JwtBearerOptions>? action)
    {
        for (var type = action?.Method.DeclaringType; type is not null; type = type.DeclaringType)
        {
            if (type == typeof(AuthorizationModule)) return true;
        }

        return false;
    }

    private static void Apply(IReadOnlyList<PostConfigureOptions<JwtBearerOptions>> delegates, JwtBearerOptions options)
    {
        foreach (var postConfigure in delegates)
        {
            postConfigure.PostConfigure(JwtBearerDefaults.AuthenticationScheme, options);
        }
    }

    // An options instance as the library leaves it before the module's post-configure runs.
    private static JwtBearerOptions NewOptions()
    {
        var options = new JwtBearerOptions();
        options.TokenValidationParameters.ValidAudience = LegacySingularAudience;
        options.TokenValidationParameters.ValidAudiences = new[] { WorkforceBffAudience };
        options.TokenValidationParameters.ValidIssuers = new[] { WorkforceIssuerV2 };
        options.TokenValidationParameters.IssuerValidator = IssuerValidatorSentinel;
        return options;
    }

    private static async Task FireAuthenticationFailedAsync(JwtBearerOptions options, ILoggerProvider loggerProvider)
    {
        var requestServices = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(loggerProvider))
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = requestServices };
        httpContext.Request.Headers.Authorization = "Bearer " + Token(UnrelatedAudience);

        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new AuthenticationFailedContext(httpContext, scheme, options)
        {
            Exception = new SecurityTokenInvalidAudienceException("audience not accepted"),
        };

        await options.Events.AuthenticationFailed(context);
    }

    private static async Task<IHost> StartHostAsync(ILoggerProvider loggerProvider)
    {
        var configuration = Configuration();

        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddLogging(logging => logging.AddProvider(loggerProvider));
                    services.AddRouting();
                    services.AddAuthorizationModule(configuration);
                    // ResourceAccessHandler serves per-record policies none of these routes use, and needs the
                    // Dataverse authorization stack; authentication is what is under test.
                    foreach (var descriptor in services
                                 .Where(d => d.ServiceType == typeof(IAuthorizationHandler) &&
                                             d.ImplementationType == typeof(ResourceAccessHandler))
                                 .ToList())
                    {
                        services.Remove(descriptor);
                    }

                    // Signing keys come from a local RSA key so no request reaches login.microsoftonline.com. The
                    // audience is deliberately NOT touched here (see the header): the module alone supplies Copilot's.
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var metadata = new OpenIdConnectConfiguration { Issuer = WorkforceIssuerV2 };
                        metadata.SigningKeys.Add(SigningKey);
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                        // Issuer validation is not under test; accept any issuer so only the audience decides.
                        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) => issuer;
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet(WorkforceRoute, () => Results.Ok()).RequireAuthorization());
                }))
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<HttpStatusCode> GetAsync(IHost host, string route, string token)
    {
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    // A v1 workforce access token (ver 1.0, appidacr) for the given audience, signed with the test key.
    private static string Token(string audience)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = WorkforceIssuerV1,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Subject = new ClaimsIdentity(new List<Claim>
            {
                new("ver", "1.0"),
                new("tid", WorkforceTenantId),
                new("oid", Guid.NewGuid().ToString()),
                new("sub", Guid.NewGuid().ToString()),
                new("scp", "user_impersonation"),
                new("appidacr", "0"),
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
                lock (owner._entries) owner._entries.Add(new LogEntry(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
