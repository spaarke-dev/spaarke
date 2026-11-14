// spaarkeai-mcp-bff-api-r1 — Task 010 (2026-11-14): MCP caller-path verification (spec FR-10 as amended 2026-11-07, owner option C).
//
// WHAT THIS PROVES: an MCP tool on /mcp/legal-operations runs INSIDE the ASP.NET request, so the unchanged IHttpContextAccessor path
// (DataverseUserClient, CallerWhoAmIMemo, the decision services) serves an MCP caller exactly as it serves a REST caller:
//   test 1 — inside a tool, IHttpContextAccessor.HttpContext is the request's own instance and the tool's IServiceProvider is that
//            request's RequestServices;
//   test 2 — the REAL DataverseUserClient, called from a tool over the MCP route, asks WhoAmI as the caller (OBO from the MCP request's
//            own bearer token) and remembers the answer for that request only; with no bearer token, or with no HttpContext, it answers
//            UserContextRequired before any credential is used or any Dataverse call is sent;
//   test 5 — the transport is pinned: Stateless, PerSessionExecutionContext false, on the test host and on the real BFF host, whose
//            production DI also binds every caller-path parameter of every legal-operations tool as a service, never a tool argument.
// Tests 3 and 4 (the commit) are in tests/integration/data-mutation/Mcp/McpCommitCallerPathTests.cs.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints / RateLimitingModule and the A10 token filter on a real Kestrel listener
// (McpCallerPathHost below, the task-020 host shape), the REAL DataverseUserClient and OrderedCredentialClientProvider (MSAL). Only the
// wire is replaced: Dataverse by a hand-written HttpMessageHandler, the OBO exchange by an MSAL token cache seeded with one access token
// for the hash of the MCP request's own bearer token (MSAL's own OBO cache lookup, so a different caller token misses), and the BFF's
// managed-identity assertion by a counting stub. Tokens are opaque test strings (ADR-028; nothing real is asserted on or logged).

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp;

public sealed class McpCallerPathTests
{
    private static readonly Guid CallerOid = Guid.Parse("01000000-0000-4000-8000-000000000001");
    private static readonly Guid CallerSystemUserId = Guid.Parse("01000000-0000-4000-8000-000000000002");
    private const string DataverseUrl = "https://caller-path-010.crm.dynamics.com";

    /// <summary>The Dataverse access token the seeded MSAL cache holds for the caller's assertion: an opaque test string.</summary>
    private const string CallerDataverseToken = "test-dataverse-token-for-caller-010";

    // ---- test 1: the tool runs inside the request ---------------------------------------------------------------------------

    [Fact]
    public async Task Test1_InsideATool_TheAccessorHoldsTheRequestsOwnHttpContext_AndTheToolsProviderIsItsRequestServices()
    {
        await using var host = await McpCallerPathHost.StartAsync([typeof(CallerPathProbeTool)]);

        var result = await host.CallToolAsync(CallerPathProbeTool.ToolName, new { }, McpTestHost.MintToken(CallerOid));

        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(result.GetRawText());
        var seen = host.Recorder;
        seen.ToolCalls.Should().Be(1);
        seen.MiddlewareContext.Should().NotBeNull();
        seen.ToolAccessorContext.Should().NotBeNull("FR-10 option C: the tool reads the caller from IHttpContextAccessor; null would fail every MCP call closed");
        ReferenceEquals(seen.ToolAccessorContext, seen.MiddlewareContext).Should().BeTrue(
            "the accessor holds the very HttpContext the request pipeline created, not another instance");
        seen.ToolTraceIdentifier.Should().Be(seen.MiddlewareTraceIdentifier, "and it is that request's context while the tool runs");
        seen.ToolServices.Should().NotBeNull();
        ReferenceEquals(seen.ToolServices, seen.MiddlewareRequestServices).Should().BeTrue(
            "the SDK resolves a tool's services from the request's own RequestServices (ScopeRequests off), so scoped services are the request's");
        ReferenceEquals(seen.ToolAccessorRequestServices, seen.MiddlewareRequestServices).Should().BeTrue();
        seen.ToolUserOid.Should().Be(CallerOid.ToString("D"), "the context carries the authenticated MCP caller");
    }

    // ---- test 2: the real DataverseUserClient over the MCP route ------------------------------------------------------------

    [Fact]
    public async Task Test2_TheRealDataverseUserClient_InATool_AsksWhoAmIAsTheCaller_OncePerMcpRequest()
    {
        var token = McpTestHost.MintToken(CallerOid);
        var rig = await DataverseRig.StartAsync(seedTokenFor: token);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register);
        var mintsBefore = rig.Assertions.Mints;

        var first = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token);

        (first.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(first.GetRawText());
        var answer = first.GetProperty("structuredContent");
        answer.GetProperty("userId").GetString().Should().Be(CallerSystemUserId.ToString("D"), "WhoAmI answered for the caller");
        answer.GetProperty("asked").GetInt32().Should().Be(2, "the tool asked twice");
        rig.Wire.Requests.Should().ContainSingle("the second WhoAmI in the same MCP request is answered from CallerWhoAmIMemo");
        var request = rig.Wire.Requests.Single();
        (request.Method, request.Path).Should().Be(("GET", "/api/data/v9.2/WhoAmI()"));
        request.Authorization.Should().Be("Bearer " + CallerDataverseToken,
            "the Dataverse call carries the OBO token MSAL holds for THIS MCP request's bearer token (the seed is keyed by its hash)");
        rig.Assertions.Mints.Should().Be(mintsBefore, "the exchange was MSAL's OBO cache hit: no token request left the process");

        await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token);

        rig.Wire.Requests.Should().HaveCount(2, "a new MCP request is a new HttpContext: the memo never carries an answer across requests");
    }

    [Fact]
    public async Task Test2_Negative_AnAuthenticatedMcpCallerWithNoBearerToken_IsUserContextRequired_AndNothingIsSentOrMinted()
    {
        var rig = await DataverseRig.StartAsync(seedTokenFor: null);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register, headerlessCaller: true);

        var result = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token: null);

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Recorder.ToolUserOid.Should().Be(CallerOid.ToString("D"), "the A10 filter admitted an authenticated caller: only the bearer token was missing");
        rig.Wire.Requests.Should().BeEmpty("no Dataverse call is sent without the caller's token");
        rig.Assertions.Mints.Should().Be(0, "the BFF's own credential is never used: there is no app-only fallback");
    }

    [Fact]
    public async Task Test2_Negative_TheClientWithNoHttpContext_IsUserContextRequired_AndNothingIsSentOrMinted()
    {
        var rig = await DataverseRig.StartAsync(seedTokenFor: null);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register);

        // Outside any request (this test's own flow): the accessor is null.
        host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext.Should().BeNull();
        var response = await host.Services.GetRequiredService<IDataverseUserClient>().GetAsync("WhoAmI()", CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        rig.Wire.Requests.Should().BeEmpty();
        rig.Assertions.Mints.Should().Be(0, "no credential is used: there is no app-only fallback");
    }

    // ---- test 5: the transport pin ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test5_TheMcpTransport_IsStateless_WithPerRequestExecutionContext()
    {
        await using var host = await McpCallerPathHost.StartAsync([typeof(CallerPathProbeTool)]);

        var transport = host.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        transport.Stateless.Should().BeTrue("ADR-001: no session store, no affinity");
        transport.SessionMode.Should().Be(HttpServerSessionMode.Stateless);
#pragma warning disable MCP9006 // Pinning the value is this test's purpose (FR-10); the SDK marks the setting obsolete, not the read.
        transport.PerSessionExecutionContext.Should().BeFalse(
            "true runs tools on the session's execution context, where the SDK documents IHttpContextAccessor as unsupported (FR-10); " +
            "in stateless mode test 1 still passes with it on (task-010 notes), so this pin is the guard");
#pragma warning restore MCP9006
    }

    [Fact]
    public void Test5_TheRealBffHost_PinsTheTransport_AndBindsEveryCallerPathParameterAsAService()
    {
        using var factory = new CustomWebAppFactory().WithWebHostBuilder(builder => builder.UseSetting("Mcp:Enabled", "true"));

        var transport = factory.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        transport.Stateless.Should().BeTrue();
#pragma warning disable MCP9006 // Pinning the value is this test's purpose (FR-10); the SDK marks the setting obsolete, not the read.
        transport.PerSessionExecutionContext.Should().BeFalse();
#pragma warning restore MCP9006

        // The production assembly scan over the production DI: a caller-path parameter (IHttpContextAccessor, IDecisionCapability, ...)
        // that the real host did not register would be published as a tool ARGUMENT, so the caller path would not be the request's.
        var tools = factory.Services.GetRequiredService<McpToolCatalog>().ToolsFor(McpEndpoints.LegalOperationsArea)
            .ToDictionary(t => t.ProtocolTool.Name, t => t.ProtocolTool.InputSchema);
        var arguments = tools.ToDictionary(
            t => t.Key,
            t => t.Value.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray() : []);

        arguments.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["spaarke_show_decisions"] = ["category"],
            ["spaarke_list_decision_page"] = ["cursor"],
            ["spaarke_get_decision_plan"] = ["signalId"],
            ["spaarke_search_decision_lookup"] = ["parameterCode", "query", "signalId"],
            ["spaarke_complete_decision"] = ["review", "signalId"],
        }, options => options.WithStrictOrdering());
    }

    // ---- the Dataverse side of test 2 ---------------------------------------------------------------------------------------

    /// <summary>The REAL <see cref="DataverseUserClient"/> and <see cref="OrderedCredentialClientProvider"/> with only the wire replaced.</summary>
    private sealed class DataverseRig
    {
        private DataverseRig(OrderedCredentialClientProvider provider, CountingAssertions assertions)
        {
            Provider = provider;
            Assertions = assertions;
        }

        public OrderedCredentialClientProvider Provider { get; }

        public CountingAssertions Assertions { get; }

        public DataverseWire Wire { get; } = new();

        /// <param name="seedTokenFor">The MCP bearer token whose OBO exchange MSAL answers from its cache; null seeds nothing and builds no client.</param>
        public static async Task<DataverseRig> StartAsync(string? seedTokenFor)
        {
            var assertions = new CountingAssertions();
            var provider = new OrderedCredentialClientProvider(
                Options.Create(new CredentialSelectionOptions { Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) } }),
                new ConfigurationBuilder().Build(),
                NullLogger<OrderedCredentialClientProvider>.Instance,
                assertions);

            if (seedTokenFor is not null)
            {
                // The SAME client instance DataverseUserClient will get (the provider caches it per tenant, client and credential).
                var client = await provider.GetClientAsync(McpTestHost.TenantId, McpTestHost.BffClientId);
                SeedOboAccessToken(client, seedTokenFor);
            }

            return new DataverseRig(provider, assertions);
        }

        public void Register(IServiceCollection services) =>
            services.AddSingleton<IDataverseUserClient>(sp => new DataverseUserClient(
                new HttpClient(Wire),
                Options.Create(new DataverseOptions { EnvironmentUrl = DataverseUrl }),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IHttpContextAccessor>(),
                NullLogger<DataverseUserClient>.Instance,
                Provider));

        /// <summary>
        /// One OBO access token in MSAL's user token cache, keyed by the hash of <paramref name="userAssertion"/>: the entry MSAL itself
        /// writes after an OBO exchange (the shared MSAL cache schema). AcquireTokenOnBehalfOf for that assertion is then a cache hit, and
        /// for any other assertion a miss.
        /// </summary>
        private static void SeedOboAccessToken(IConfidentialClientApplication client, string userAssertion)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expires = (now + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var home = $"{CallerOid:D}.{McpTestHost.TenantId}";
            var cache = new JsonObject
            {
                ["AccessToken"] = new JsonObject
                {
                    ["seed"] = new JsonObject
                    {
                        ["home_account_id"] = home,
                        ["environment"] = "login.microsoftonline.com",
                        ["client_id"] = McpTestHost.BffClientId,
                        ["credential_type"] = "AccessToken",
                        ["realm"] = McpTestHost.TenantId,
                        ["target"] = $"{DataverseUrl}/.default",
                        ["secret"] = CallerDataverseToken,
                        ["cached_at"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["expires_on"] = expires,
                        ["extended_expires_on"] = expires,
                        ["token_type"] = "Bearer",
                        // MSAL's OBO cache key: base64url(SHA-256(assertion)), what MSAL computes for the incoming assertion.
                        ["user_assertion_hash"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(userAssertion))),
                    },
                },
                ["Account"] = new JsonObject(),
                ["IdToken"] = new JsonObject(),
                ["RefreshToken"] = new JsonObject(),
                ["AppMetadata"] = new JsonObject(),
            };
            var serialized = Encoding.UTF8.GetBytes(cache.ToJsonString());
            var seeded = 0;
            client.UserTokenCache.SetBeforeAccess(args =>
            {
                if (Interlocked.Exchange(ref seeded, 1) == 0)
                {
                    args.TokenCache.DeserializeMsalV3(serialized, shouldClearExistingCache: false);
                }
            });
        }
    }

    /// <summary>The BFF's managed-identity assertion, counted; an opaque test string, never a real credential.</summary>
    private sealed class CountingAssertions : IClientAssertionProvider
    {
        private int _mints;

        public int Mints => Volatile.Read(ref _mints);

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mints);
            return Task.FromResult("test-client-assertion-010");
        }
    }

    /// <summary>Dataverse's Web API on the wire (hand-written; ADR-038 bans a mocked handler): records each request, answers WhoAmI.</summary>
    private sealed class DataverseWire : HttpMessageHandler
    {
        public ConcurrentQueue<(string Method, string Path, string? Authorization)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString()));
            if (request.RequestUri.AbsolutePath == "/api/data/v9.2/WhoAmI()")
            {
                var body = JsonSerializer.Serialize(new
                {
                    BusinessUnitId = "01000000-0000-4000-8000-0000000000b1",
                    UserId = CallerSystemUserId.ToString("D"),
                    OrganizationId = "01000000-0000-4000-8000-0000000000c1",
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

/// <summary>
/// TESTS ONLY (task 010). The task-020 host shape: the PRODUCTION <c>AddMcpModule</c>, <c>MapMcpEndpoints</c> (A10 token filter, rate limit,
/// kill switch) and <c>RateLimitingModule</c> on a real Kestrel listener behind a JwtBearer scheme that accepts <see cref="McpTestHost.MintToken"/>,
/// serving exactly <c>tools</c>. A first middleware records each request's HttpContext, TraceIdentifier and RequestServices
/// (<see cref="CallerPathRecorder"/>). <c>headerlessCaller</c> swaps the default scheme for one that admits an A10-shaped caller from no
/// Authorization header at all: an authenticated principal whose request carries no bearer token.
/// </summary>
internal sealed class McpCallerPathHost : IAsyncDisposable
{
    private const string Issuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private McpCallerPathHost(WebApplication app, HttpClient client, CallerPathRecorder recorder)
    {
        _app = app;
        _client = client;
        Recorder = recorder;
    }

    public CallerPathRecorder Recorder { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<McpCallerPathHost> StartAsync(
        IEnumerable<Type> tools, Action<IServiceCollection>? services = null, bool headerlessCaller = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        var recorder = new CallerPathRecorder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(recorder);
        builder.Services.AddSingleton(TimeProvider.System);
        var authentication = builder.Services.AddAuthentication(headerlessCaller ? HeaderlessCallerHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        authentication.AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = McpTestHost.BffClientId,
            IssuerSigningKey = SigningKey,
        });
        if (headerlessCaller)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, HeaderlessCallerHandler>(HeaderlessCallerHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization();
        builder.Services.AddRateLimitingModule();
        services?.Invoke(builder.Services);
        builder.Services.AddMcpModule(builder.Configuration, tools);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            recorder.MiddlewareContext = context;
            recorder.MiddlewareTraceIdentifier = context.TraceIdentifier;
            await next(context);
            recorder.MiddlewareRequestServices = context.RequestServices; // still this request's, read before it ends
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new McpCallerPathHost(app, new HttpClient { BaseAddress = new Uri(address) }, recorder);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>One <c>tools/call</c> on <c>/mcp/legal-operations</c>; returns the JSON-RPC <c>result</c> (an isError result included).</summary>
    /// <param name="token">The bearer token; null sends no Authorization header.</param>
    public Task<JsonElement> CallToolAsync(string tool, object arguments, string? token) =>
        RpcAsync("tools/call", new { name = tool, arguments }, token);

    public async Task<JsonElement> RpcAsync(string method, object parameters, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
        }

        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC error: {error}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    /// <summary>
    /// Authenticates every request as an ADR-028 A10 caller (Spaarke tenant, exact v2 issuer, BFF audience, user_impersonation, an oid)
    /// WITHOUT reading any header, so the request reaches the tool authenticated but with no bearer token.
    /// </summary>
    private sealed class HeaderlessCallerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderlessA10Caller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim("tid", McpTestHost.TenantId),
                    new Claim("iss", Issuer),
                    new Claim("aud", McpTestHost.BffClientId),
                    new Claim("scp", "user_impersonation"),
                    new Claim("oid", "01000000-0000-4000-8000-000000000001"),
                ],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

/// <summary>What the request pipeline and the probe tools saw in one <see cref="McpCallerPathHost"/> (the last request).</summary>
internal sealed class CallerPathRecorder
{
    private int _toolCalls;

    public HttpContext? MiddlewareContext { get; set; }

    public string? MiddlewareTraceIdentifier { get; set; }

    public IServiceProvider? MiddlewareRequestServices { get; set; }

    public int ToolCalls => Volatile.Read(ref _toolCalls);

    public HttpContext? ToolAccessorContext { get; private set; }

    public string? ToolTraceIdentifier { get; private set; }

    public IServiceProvider? ToolServices { get; private set; }

    public IServiceProvider? ToolAccessorRequestServices { get; private set; }

    public string? ToolUserOid { get; private set; }

    public void RecordTool(IHttpContextAccessor accessor, IServiceProvider services)
    {
        Interlocked.Increment(ref _toolCalls);
        var http = accessor.HttpContext;
        ToolAccessorContext = http;
        ToolTraceIdentifier = http?.TraceIdentifier;
        ToolServices = services;
        ToolAccessorRequestServices = http?.RequestServices;
        ToolUserOid = http?.User.FindFirst("oid")?.Value ?? http?.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    }
}

/// <summary>
/// Probe tool (task 010, test 1): records <c>IHttpContextAccessor.HttpContext</c> and the <see cref="IServiceProvider"/> the SDK resolved
/// the call's services from. It looks its recorder up at run time, so a host that serves every tool of this assembly
/// (<see cref="McpProbeTools.Served"/>) gets a harmless no-op.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class CallerPathProbeTool : McpToolDefinition
{
    public const string ToolName = "probe_caller_path";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            Run,
            new McpServerToolCreateOptions { Services = services, Name = ToolName, Title = "Probe caller path", ReadOnly = true });

    private static string Run(IHttpContextAccessor accessor, IServiceProvider services)
    {
        services.GetService<CallerPathRecorder>()?.RecordTool(accessor, services);
        return "recorded";
    }
}

/// <summary>
/// Probe tool (task 010, test 2): asks the registered <see cref="IDataverseUserClient"/> <c>WhoAmI()</c> twice in one call, as a decision
/// tool's capability would, and returns the answer or the client's error code as an isError result.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class DataverseWhoAmIProbeTool : McpToolDefinition
{
    public const string ToolName = "probe_dataverse_whoami";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            RunAsync,
            new McpServerToolCreateOptions { Services = services, Name = ToolName, Title = "Probe Dataverse WhoAmI", ReadOnly = true });

    private static async Task<CallToolResult> RunAsync(IHttpContextAccessor accessor, IServiceProvider services, CancellationToken cancellationToken)
    {
        services.GetService<CallerPathRecorder>()?.RecordTool(accessor, services);
        if (services.GetService<IDataverseUserClient>() is not { } client)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "no Dataverse client in this host" }] };
        }

        DataverseUserResponse? last = null;
        var asked = 0;
        for (var i = 0; i < 2; i++)
        {
            last = await client.GetAsync("WhoAmI()", cancellationToken);
            asked++;
            if (!last.IsSuccess)
            {
                break;
            }
        }

        return last!.IsSuccess
            ? new CallToolResult
            {
                Content = [new TextContentBlock { Text = "ok" }],
                StructuredContent = JsonSerializer.SerializeToElement(new { userId = last.Body!.Value.GetProperty("UserId").GetString(), asked }),
            }
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = last.ErrorCode ?? "error" }],
                StructuredContent = JsonSerializer.SerializeToElement(new { reasonCode = last.ErrorCode }),
            };
    }
}
