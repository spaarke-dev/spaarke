// CONTRACT - Host/Origin validation on POST /mcp/legal-operations (ISS-007 #2835). KEEP path: endpoint-contract.
//
// WHAT RUNS: the production AddMcpModule / MapMcpEndpoints on a real Kestrel listener behind a real JwtBearer scheme (the setup of
// McpTestHost, with the configuration and the log provider under test control), plus one non-MCP route. Every test sends a real HTTP request
// with a real Origin and Host header. The filter is judged only through the route: remove the `.RequireMcpOriginCheck()` line in
// McpEndpoints.MapMcpEndpoints and every Enforce "rejects" test below fails (the must-fire control).
//
// THE RULE (owner, 2026-11-26): Origin PRESENT and not allowed = 403; no Origin = passes; Host not an allowed host = 403; Mode LogOnly (the
// default) never rejects and logs the decision Enforce would make; an unauthenticated request is 401 whatever its Origin.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class McpOriginCheckTests
{
    private const string Evil = "https://evil.example.com";
    private const string Allowed = "https://allowed.example.com";
    private const string OwnHost = "mcp.example.test";
    private static readonly Guid Caller = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private static Dictionary<string, string?> Config(string? mode, bool withHosts = false)
    {
        var config = new Dictionary<string, string?>
        {
            ["Mcp:OriginCheck:AllowedOrigins:0"] = Allowed,
        };
        if (mode is not null)
        {
            config["Mcp:OriginCheck:Mode"] = mode;
        }

        if (withHosts)
        {
            config["Mcp:OriginCheck:AllowedHosts:0"] = OwnHost;
        }

        return config;
    }

    // ---- Enforce ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Enforce_BadOrigin_Returns403ProblemDetails_AndNoToolRuns()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: Evil, method: "tools/call", parameters: new { name = "probe_echo", arguments = new { message = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(McpOriginCheckFilter.OriginRefusedErrorCode);
        host.Probe.EchoInvocations.Should().Be(0, "a refused request never reaches the transport, so no tool runs");
    }

    [Fact]
    public async Task Enforce_AllowedOrigin_Passes()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: Allowed);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enforce_AllowedOrigin_MatchesCaseAndTrailingSlashInsensitively()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: "HTTPS://Allowed.Example.com/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enforce_NoOrigin_Passes()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Copilot and Cowork call server to server and send no Origin");
    }

    [Fact]
    public async Task Enforce_EmptyAllowList_RejectsEveryPresentOrigin_ButStillAllowsNoOrigin()
    {
        await using var host = await OriginHost.StartAsync(new Dictionary<string, string?> { ["Mcp:OriginCheck:Mode"] = "Enforce" });

        using var withOrigin = await host.CallAsync(origin: Allowed);
        using var withoutOrigin = await host.CallAsync(origin: null);

        withOrigin.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an empty allow-list allows no browser origin");
        withoutOrigin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enforce_NullOrigin_IsPresentAndRejected()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: "null"); // a sandboxed iframe or file:// page sends the literal "null"

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Enforce_BadHost_Returns403ProblemDetails()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce", withHosts: true));

        using var response = await host.CallAsync(origin: null, hostHeader: "evil.example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(McpOriginCheckFilter.HostRefusedErrorCode);
    }

    [Fact]
    public async Task Enforce_AllowedHost_Passes_WithOrWithoutAPort()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce", withHosts: true));

        using var plain = await host.CallAsync(origin: null, hostHeader: OwnHost);
        using var withPort = await host.CallAsync(origin: null, hostHeader: OwnHost + ":8443");

        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        withPort.StatusCode.Should().Be(HttpStatusCode.OK, "an entry without a port matches any port");
    }

    [Fact]
    public async Task Enforce_NoHostConfigured_DoesNotRestrictHost()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: null, hostHeader: "anything.example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "with no allowed host configured anywhere the Host is not restricted");
    }

    [Fact]
    public async Task Enforce_HostFallsBackToTheBffsOwnConfiguredPublicOrigin()
    {
        var config = Config("Enforce");
        config["Insights:CitationHref:BffBaseUrl"] = "https://" + OwnHost; // the existing BFF public-origin setting
        await using var host = await OriginHost.StartAsync(config);

        using var own = await host.CallAsync(origin: null, hostHeader: OwnHost);
        using var other = await host.CallAsync(origin: null, hostHeader: "evil.example.com");

        own.StatusCode.Should().Be(HttpStatusCode.OK);
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Enforce_UnauthenticatedWithABadOrigin_Is401_NotChangedByTheCheck()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var response = await host.CallAsync(origin: Evil, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "authentication runs first; the origin check never changes an auth outcome");
    }

    [Fact]
    public async Task UnrecognisedMode_FailsClosed_AsEnforce()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforced")); // a typo of "Enforce"

        using var response = await host.CallAsync(origin: Evil);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- LogOnly (the default) ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)] // the default: no Mode configured
    [InlineData("LogOnly")]
    public async Task LogOnly_NeverRejects_AndLogsTheDecisionEnforceWouldMake(string? mode)
    {
        await using var host = await OriginHost.StartAsync(Config(mode, withHosts: true));

        using var badOrigin = await host.CallAsync(origin: Evil, hostHeader: OwnHost);
        using var badHost = await host.CallAsync(origin: null, hostHeader: "evil.example.com");
        using var good = await host.CallAsync(origin: Allowed, hostHeader: OwnHost);

        badOrigin.StatusCode.Should().Be(HttpStatusCode.OK);
        badHost.StatusCode.Should().Be(HttpStatusCode.OK);
        good.StatusCode.Should().Be(HttpStatusCode.OK);

        var lines = host.Logs.ForCategory(typeof(McpOriginCheckFilter).FullName!);
        lines.Should().HaveCount(3, "exactly one Information line per MCP request");
        lines.Should().OnlyContain(l => l.Level == LogLevel.Information);

        lines[0].State["OriginPresent"].Should().Be(true);
        lines[0].State["Origin"].Should().Be(Evil);
        lines[0].State["Host"].Should().Be(OwnHost);
        lines[0].State["WouldReject"].Should().Be(true);
        lines[0].State["WouldReason"].Should().Be(McpOriginCheckFilter.OriginRefusedErrorCode);

        lines[1].State["OriginPresent"].Should().Be(false);
        lines[1].State["Origin"].Should().Be(string.Empty);
        lines[1].State["WouldReject"].Should().Be(true);
        lines[1].State["WouldReason"].Should().Be(McpOriginCheckFilter.HostRefusedErrorCode);

        lines[2].State["WouldReject"].Should().Be(false);
        lines[2].State["WouldReason"].Should().Be("allowed");
    }

    [Fact]
    public async Task LogOnly_LogLine_CarriesNoTokenAndNoOtherHeader()
    {
        await using var host = await OriginHost.StartAsync(Config("LogOnly"));
        var token = McpTestHost.MintToken(Caller);

        using var response = await host.CallAsync(origin: Evil, token: token, extraHeaders: new() { ["X-Spaarke-Probe"] = "secret-header-value" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var line = host.Logs.ForCategory(typeof(McpOriginCheckFilter).FullName!).Single();
        line.State.Keys.Should().BeEquivalentTo(["OriginPresent", "Origin", "Host", "WouldReject", "WouldReason", "{OriginalFormat}"]);
        line.Message.Should().NotContain(token).And.NotContain("secret-header-value").And.NotContain(Caller.ToString());
    }

    [Fact]
    public async Task LogOnly_VeryLongOrigin_IsCappedInTheLog()
    {
        await using var host = await OriginHost.StartAsync(Config("LogOnly"));

        using var response = await host.CallAsync(origin: "https://" + new string('a', 5000) + ".example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ((string)host.Logs.ForCategory(typeof(McpOriginCheckFilter).FullName!).Single().State["Origin"]!).Length.Should().Be(200);
    }

    // ---- Enforce logs only a refusal ----------------------------------------------------------------------------------

    [Fact]
    public async Task Enforce_LogsOneWarningPerRefusal_AndNothingForAPass()
    {
        await using var host = await OriginHost.StartAsync(Config("Enforce"));

        using var passed = await host.CallAsync(origin: Allowed);
        using var refused = await host.CallAsync(origin: Evil);

        var lines = host.Logs.ForCategory(typeof(McpOriginCheckFilter).FullName!);
        lines.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    // ---- other routes -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Enforce")]
    [InlineData("LogOnly")]
    public async Task OtherRoutes_AreUnaffected_ByABadOriginOrHost(string mode)
    {
        await using var host = await OriginHost.StartAsync(Config(mode, withHosts: true));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe/other");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpTestHost.MintToken(Caller));
        request.Headers.TryAddWithoutValidation("Origin", Evil);
        request.Headers.Host = "evil.example.com";
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Logs.ForCategory(typeof(McpOriginCheckFilter).FullName!).Should().BeEmpty("the filter is on the MCP route only");
    }

    // ---- host resolution (unit) ---------------------------------------------------------------------------------------

    [Fact]
    public void ResolveAllowedHosts_PrefersTheMcpList_ThenAspNetAllowedHosts_ThenTheBffOrigin_ThenNothing()
    {
        static IReadOnlyList<string> Resolve(Dictionary<string, string?> values) =>
            McpOriginCheckFilter.ResolveAllowedHosts(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        Resolve(new()
        {
            ["Mcp:OriginCheck:AllowedHosts:0"] = "mcp.example.test",
            ["AllowedHosts"] = "other.example.test",
        }).Should().Equal("mcp.example.test");

        Resolve(new() { ["AllowedHosts"] = "a.example.test;*.b.example.test", ["Insights:CitationHref:BffBaseUrl"] = "https://bff.example.test" })
            .Should().Equal("a.example.test", "*.b.example.test");

        Resolve(new() { ["AllowedHosts"] = "*", ["Insights:CitationHref:BffBaseUrl"] = "https://bff.example.test:8443" })
            .Should().Equal("bff.example.test:8443"); // the template default "*" names no host, so the BFF public origin is used

        Resolve(new() { ["AllowedHosts"] = "*" }).Should().BeEmpty();
        Resolve(new()).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WildcardHostEntry_MatchesSubdomainsOnly()
    {
        string[] hosts = ["*.example.test"];

        McpOriginCheckFilter.Evaluate(false, default, new HostString("a.example.test"), null, hosts).Should().BeNull();
        McpOriginCheckFilter.Evaluate(false, default, new HostString("example.test"), null, hosts).Should().Be(McpOriginCheckFilter.HostRefusedErrorCode);
        McpOriginCheckFilter.Evaluate(false, default, default, null, hosts).Should().Be(McpOriginCheckFilter.HostRefusedErrorCode, "a missing Host never matches a restricted list");
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errorCode").GetString();
    }

    // ---- the host -----------------------------------------------------------------------------------------------------

    /// <summary>The setup of <see cref="McpTestHost"/> (production MCP module and routes, real JwtBearer) with the configuration and the log sink under test control.</summary>
    private sealed class OriginHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private OriginHost(WebApplication app, HttpClient client, McpProbeState probe, CapturingLoggerProvider logs)
        {
            _app = app;
            Client = client;
            Probe = probe;
            Logs = logs;
        }

        public HttpClient Client { get; }

        public McpProbeState Probe { get; }

        public CapturingLoggerProvider Logs { get; }

        public static async Task<OriginHost> StartAsync(Dictionary<string, string?> extraConfig)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            var logs = new CapturingLoggerProvider();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            var config = new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
                ["AzureAd:TenantId"] = McpTestHost.TenantId,
                ["AzureAd:ClientId"] = McpTestHost.BffClientId,
            };
            foreach (var (key, value) in extraConfig)
            {
                config[key] = value;
            }

            builder.Configuration.AddInMemoryCollection(config);

            var probe = new McpProbeState();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(probe);
            builder.Services.AddSingleton<IDecisionCapability>(new McpProbeCapability());
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0",
                    ValidAudience = McpTestHost.BffClientId,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789")),
                });
            builder.Services.AddAuthorization(o => o.AddPolicy("SystemAdmin", p => p.RequireAssertion(_ => false)));
            builder.Services.AddRateLimitingModule();
            builder.Services.AddMcpModule(builder.Configuration, McpProbeTools.Served);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapMcpEndpoints();
            app.MapPost("/probe/other", () => Results.Ok("other")).RequireAuthorization();
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new OriginHost(app, new HttpClient { BaseAddress = new Uri(address) }, probe, logs);
        }

        /// <summary>One JSON-RPC call to the MCP route. <paramref name="origin"/> null sends no <c>Origin</c>; <paramref name="hostHeader"/> null keeps the real host.</summary>
        public Task<HttpResponseMessage> CallAsync(
            string? origin,
            string? hostHeader = null,
            string? token = "valid",
            string method = "initialize",
            object? parameters = null,
            Dictionary<string, string>? extraHeaders = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method,
                        @params = parameters ?? new
                        {
                            protocolVersion = "2025-06-18",
                            capabilities = new { },
                            clientInfo = new { name = "origin-check-test", version = "0.0.1" },
                        },
                    }),
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token == "valid" ? McpTestHost.MintToken(Caller) : token);
            }

            if (origin is not null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            if (hostHeader is not null)
            {
                request.Headers.Host = hostHeader;
            }

            foreach (var (name, value) in extraHeaders ?? [])
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Keeps every log entry (category, level, rendered message, structured state) so a test can read what the filter logged.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<Entry> _entries = new();

        public sealed record Entry(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

        public IReadOnlyList<Entry> ForCategory(string category) => _entries.Where(e => e.Category == category).ToList();

        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Capture(string category, ConcurrentQueue<Entry> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(p => p.Key, p => p.Value)
                    : new Dictionary<string, object?>();
                sink.Enqueue(new Entry(category, logLevel, formatter(state, exception), values));
            }
        }
    }
}
