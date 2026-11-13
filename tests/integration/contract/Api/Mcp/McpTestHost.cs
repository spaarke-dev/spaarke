using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 020). An in-process host that boots the PRODUCTION <c>AddMcpModule</c>,
/// <c>MapMcpEndpoints</c> and <c>RateLimitingModule</c> on a real Kestrel listener (loopback, ephemeral port) behind a real JwtBearer scheme
/// that accepts the tokens <see cref="MintToken"/> signs. Kestrel, not TestServer, because only Kestrel enforces the 256 KiB request-body
/// limit (TestServer's request has no <c>IHttpMaxRequestBodySizeFeature</c>). It scans THIS assembly for tool classes, so the probe tools
/// below are the "tools" of the area; it registers no production tool and no Dataverse client.
/// </summary>
internal sealed class McpTestHost : IAsyncDisposable
{
    // A realistic ADR-028 A10 token (task 021): the route refuses any other shape. Spaarke tenant, exact v2 issuer, aud = the BFF app id.
    internal const string TenantId = "11111111-2222-4333-8444-555555555555";
    internal const string BffClientId = "66666666-7777-4888-9999-aaaaaaaaaaaa";
    private const string Issuer = "https://login.microsoftonline.com/" + TenantId + "/v2.0";
    private const string Audience = BffClientId;
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;

    private McpTestHost(WebApplication app, HttpClient client, McpProbeState probe, McpProbeCapability capability, BufferingRecorder.State buffering)
    {
        _app = app;
        Client = client;
        Probe = probe;
        Capability = capability;
        Buffering = buffering;
    }

    public HttpClient Client { get; }

    public McpProbeState Probe { get; }

    /// <summary>The <see cref="IDecisionCapability"/> the probe tool calls; records the <see cref="HttpContext"/> it was handed.</summary>
    public McpProbeCapability Capability { get; }

    public BufferingRecorder.State Buffering { get; }

    public static async Task<McpTestHost> StartAsync(bool mcpEnabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = mcpEnabled ? "true" : "false",
            ["AzureAd:TenantId"] = TenantId,
            ["AzureAd:ClientId"] = BffClientId,
        });

        var probe = new McpProbeState();
        var capability = new McpProbeCapability();
        var buffering = new BufferingRecorder.State();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton<IDecisionCapability>(capability);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = SigningKey,
            });
        // No FallbackPolicy: the route's own RequireAuthorization() is what is under test. "SystemAdmin" exists because the A10 suite's
        // admin probe tool (tests/integration/auth/Mcp) is in this assembly too; here it admits nobody.
        builder.Services.AddAuthorization(o => o.AddPolicy("SystemAdmin", p => p.RequireAssertion(_ => false)));
        builder.Services.AddRateLimitingModule();
        builder.Services.AddMcpModule(builder.Configuration, McpProbeTools.Served);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            probe.MiddlewareContext = context; // the HttpContext the request pipeline saw (last request)
            await next(context);
        });
        app.UseMiddleware<BufferingRecorder>(buffering);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        return new McpTestHost(app, client, probe, capability, buffering);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>A valid workforce-style test token for the caller whose <c>oid</c> is <paramref name="oid"/>.</summary>
    public static string MintToken(Guid oid) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["scp"] = "user_impersonation",
            ["oid"] = oid.ToString(),
            ["tid"] = TenantId,
        },
    });

    /// <summary>One JSON-RPC call. <paramref name="token"/> null sends no Authorization header; <paramref name="accept"/> false omits <c>Accept</c>.</summary>
    public Task<HttpResponseMessage> PostRpcAsync(string route, string method, object parameters, string? token, bool accept = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
                Encoding.UTF8,
                "application/json"),
        };
        if (accept)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return Client.SendAsync(request);
    }

    /// <summary>Calls a method and returns the JSON-RPC <c>result</c>, parsing the JSON or SSE body the transport answers with.</summary>
    public async Task<JsonElement> RpcAsync(string route, string method, object parameters, string token)
    {
        using var response = await PostRpcAsync(route, method, parameters, token);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
        }

        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join(
                "\n",
                body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC error: {error}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    /// <summary>Records whether the endpoint switched response buffering off (<c>IHttpResponseBodyFeature.DisableBuffering</c>).</summary>
    public sealed class BufferingRecorder(RequestDelegate next, BufferingRecorder.State state)
    {
        public sealed class State
        {
            public volatile bool DisableBufferingCalled;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var original = context.Features.Get<IHttpResponseBodyFeature>()!;
            context.Features.Set<IHttpResponseBodyFeature>(new Recording(original, state));
            await next(context);
        }

        private sealed class Recording(IHttpResponseBodyFeature inner, State state) : IHttpResponseBodyFeature
        {
            public Stream Stream => inner.Stream;

            public System.IO.Pipelines.PipeWriter Writer => inner.Writer;

            public void DisableBuffering()
            {
                state.DisableBufferingCalled = true;
                inner.DisableBuffering();
            }

            public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
                inner.SendFileAsync(path, offset, count, cancellationToken);

            public Task CompleteAsync() => inner.CompleteAsync();
        }
    }
}

/// <summary>
/// The tool scan set of every MCP test host in this assembly: all its types EXCEPT the tools in
/// <see cref="StartupRefusalNamespace"/>, which break the per-tool rule on purpose (task 021 fix round 1) and are served only by
/// the host that proves the app refuses to start on them.
/// </summary>
internal static class McpProbeTools
{
    internal const string StartupRefusalNamespace = "Sprk.Bff.Api.Tests.Auth.Mcp.StartupRefusals";

    public static IEnumerable<Type> Served =>
        typeof(McpProbeTools).Assembly.GetTypes().Where(t => !string.Equals(t.Namespace, StartupRefusalNamespace, StringComparison.Ordinal));
}

/// <summary>What the probe tools and the request pipeline saw in one <see cref="McpTestHost"/>.</summary>
internal sealed class McpProbeState
{
    private int _echoInvocations;

    /// <summary>How many times <c>probe_echo</c> actually ran. Stays 0 when the server is switched off.</summary>
    public int EchoInvocations => Volatile.Read(ref _echoInvocations);

    public void CountEcho() => Interlocked.Increment(ref _echoInvocations);

    /// <summary>The <c>HttpContext</c> the request middleware saw (the last request).</summary>
    public HttpContext? MiddlewareContext { get; set; }

    /// <summary><c>IHttpContextAccessor.HttpContext</c> as read inside the tool during the call.</summary>
    public HttpContext? AccessorContextInTool { get; set; }
}

/// <summary>A hand-written boundary double: records the <see cref="HttpContext"/> it is handed and answers a null-equivalent plan.</summary>
internal sealed class McpProbeCapability : IDecisionCapability
{
    public HttpContext? ReceivedContext { get; private set; }

    public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct)
    {
        ReceivedContext = http;
        return Task.FromResult(DecisionPlanReadResult.UserContextRequired);
    }

    public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes) =>
        throw new NotSupportedException();

    public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) =>
        throw new NotSupportedException();
}

/// <summary>Probe tool of the served area: counts its invocations.</summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class McpProbeEchoTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            Echo,
            new McpServerToolCreateOptions { Services = services, Name = "probe_echo", Title = "Probe echo", ReadOnly = true });

    private static string Echo(string message, McpProbeState state)
    {
        state.CountEcho();
        return message;
    }
}

/// <summary>Probe tool of the served area that does what every production tool must: hand the capability exactly <c>IHttpContextAccessor.HttpContext</c>.</summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class McpProbeHttpContextTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            RunAsync,
            new McpServerToolCreateOptions { Services = services, Name = "probe_http_context", Title = "Probe HttpContext", ReadOnly = true });

    private static async Task<string> RunAsync(
        IHttpContextAccessor accessor, IDecisionCapability capability, McpProbeState state, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        state.AccessorContextInTool = http;
        await capability.GetPlanAsync(http, Guid.Empty, cancellationToken);
        return "done";
    }
}

/// <summary>Probe tool of ANOTHER area: must never appear on <c>/mcp/legal-operations</c>.</summary>
[McpArea("zz-other-area")]
[Authorize]
internal sealed class McpProbeOtherAreaTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            Other,
            new McpServerToolCreateOptions { Services = services, Name = "probe_other_area", Title = "Probe other area", ReadOnly = true });

    private static string Other() => "other";
}
