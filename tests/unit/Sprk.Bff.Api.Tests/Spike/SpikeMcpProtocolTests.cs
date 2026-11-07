using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Mcp.Spike;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Spike;

/// <summary>
/// SPIKE (spaarkeai-mcp-bff-api-r1 task 001, spike branch only): in-process protocol checks for the Phase 0 MCP host.
/// Real MCP SDK transport + the spike's own <c>AddSpikeMcp</c>/<c>MapSpikeMcp</c> over a real JwtBearer scheme with a
/// locally signed token. The BFF's real authentication wiring (AuthorizationModule) is deliberately not loaded; the live
/// 401 against the deployed BFF is part of task 003.
/// </summary>
public sealed class SpikeMcpProtocolTests : IAsyncLifetime
{
    private const string Issuer = "https://spike.test/issuer";
    private const string Audience = "api://spike-bff";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("spike-test-signing-key-0123456789-0123456789"));
    private static readonly Guid CallerSystemUserId = Guid.Parse("a1b2c3d4-0000-4000-8000-00000000abcd");
    private static readonly Guid CallerTid = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid CallerOid = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly RecordingBodyFeatureMiddleware.State _bodyFeature = new();
    private FakeDataverseUser _dataverse = new(succeed: true);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:ClientId"] = "spike-bff" });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IDataverseUserClient>(sp => _dataverse);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    IssuerSigningKey = SigningKey,
                };
            });
        builder.Services.AddAuthorization(); // no FallbackPolicy on purpose: the routes' own .RequireAuthorization() is what is under test
        SpikeMcpEndpoints.AddSpikeMcp(builder.Services);

        _app = builder.Build();
        _app.UseMiddleware<RecordingBodyFeatureMiddleware>(_bodyFeature);
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapSpikeMcp();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---- tool sets per route (T13, local) -----------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_LegalOperations_ReturnsExactlyEchoAndWhoAmI_BothReadOnly()
    {
        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/list", new { });

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(["spike_echo", "spike_whoami"], tools.Select(t => t.GetProperty("name").GetString()!).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(tools, t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.GetProperty("title").GetString())));
    }

    [Fact]
    public async Task ToolsList_AreaB_ReturnsExactlyEchoB()
    {
        var result = await RpcAsync(SpikeMcpEndpoints.AreaBRoute, "tools/list", new { });

        var names = result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["spike_echo_b"], names);
    }

    [Fact]
    public async Task ToolsCall_ToolOfTheOtherRoute_IsRefused()
    {
        var response = await PostRpcAsync(SpikeMcpEndpoints.AreaBRoute, "tools/call", new { name = "spike_whoami", arguments = new { } });
        var body = await response.Content.ReadAsStringAsync();

        // A protocol error or a tool-error result, never a success carrying a user id.
        Assert.DoesNotContain(CallerSystemUserId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _dataverse.Calls);
    }

    [Fact]
    public async Task ToolsCall_Echo_ReturnsStructuredContent()
    {
        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/call", new { name = "spike_echo", arguments = new { message = "hi" } });

        Assert.Equal("hi", result.GetProperty("structuredContent").GetProperty("echo").GetString());
        Assert.Equal("legal-operations", result.GetProperty("structuredContent").GetProperty("route").GetString());
    }

    // ---- transport and auth negatives ---------------------------------------------------------------------------

    [Theory]
    [InlineData(SpikeMcpEndpoints.LegalOperationsRoute)]
    [InlineData(SpikeMcpEndpoints.AreaBRoute)]
    public async Task Anonymous_Request_Gets401WithWwwAuthenticate(string route)
    {
        var request = NewRpc(route, "tools/list", new { }, token: null);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Theory]
    [InlineData(SpikeMcpEndpoints.LegalOperationsRoute)]
    [InlineData(SpikeMcpEndpoints.AreaBRoute)]
    public async Task MissingDualAcceptHeader_Gets406(string route)
    {
        var request = NewRpc(route, "tools/list", new { }, MintToken());
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task Request_DisablesResponseBuffering()
    {
        await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/list", new { });

        Assert.True(_bodyFeature.DisableBufferingCalled);
    }

    // ---- widget resources (size test inputs) --------------------------------------------------------------------

    [Theory]
    [InlineData(SpikeMcpEndpoints.WidgetUri, 1)]
    [InlineData(SpikeMcpEndpoints.Widget1MbUri, 1)]
    [InlineData(SpikeMcpEndpoints.Widget3MbUri, 3)]
    [InlineData(SpikeMcpEndpoints.Widget6MbUri, 6)]
    public async Task ResourcesRead_ReturnsMcpAppMimeTypeEmptyCspAndTargetSize(string uri, int megabytes)
    {
        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "resources/read", new { uri });

        var content = result.GetProperty("contents").EnumerateArray().Single();
        Assert.Equal("text/html;profile=mcp-app", content.GetProperty("mimeType").GetString());
        var csp = content.GetProperty("_meta").GetProperty("ui").GetProperty("csp");
        Assert.Equal(JsonValueKind.Object, csp.ValueKind);
        Assert.Empty(csp.EnumerateObject());

        var bytes = Encoding.UTF8.GetByteCount(content.GetProperty("text").GetString()!);
        var target = megabytes * 1024 * 1024;
        Assert.InRange(bytes, target - 2048, target + 2048);
    }

    [Fact]
    public async Task ResourcesList_AdvertisesMimeTypeAndEmptyCsp()
    {
        var result = await RpcAsync(SpikeMcpEndpoints.AreaBRoute, "resources/list", new { });

        var resources = result.GetProperty("resources").EnumerateArray().ToList();
        Assert.Contains(resources, r => r.GetProperty("uri").GetString() == SpikeMcpEndpoints.WidgetUri);
        Assert.All(resources, r =>
        {
            Assert.Equal("text/html;profile=mcp-app", r.GetProperty("mimeType").GetString());
            Assert.Empty(r.GetProperty("_meta").GetProperty("ui").GetProperty("csp").EnumerateObject());
        });
    }

    [Fact]
    public async Task EchoTool_AdvertisesWidgetUri()
    {
        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/list", new { });

        var echo = result.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "spike_echo");
        Assert.Equal(SpikeMcpEndpoints.WidgetUri, echo.GetProperty("_meta").GetProperty("ui").GetProperty("resourceUri").GetString());
    }

    // ---- spike_whoami (OBO as the caller; ADR-015) ---------------------------------------------------------------

    [Fact]
    public async Task WhoAmI_ReturnsCallerSystemUserId_AndNoTokenOrClaimTextInResultOrLogs()
    {
        var token = MintToken();

        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/call", new { name = "spike_whoami", arguments = new { } }, token);

        var sc = result.GetProperty("structuredContent");
        Assert.Equal("ok", sc.GetProperty("status").GetString());
        Assert.Equal(CallerSystemUserId.ToString(), sc.GetProperty("systemUserId").GetString());
        var claims = sc.GetProperty("claims");
        Assert.True(claims.GetProperty("aud").GetProperty("present").GetBoolean());
        Assert.True(claims.GetProperty("scp").GetProperty("present").GetBoolean());
        Assert.True(claims.GetProperty("scp").GetProperty("hasUserImpersonation").GetBoolean());
        Assert.Equal(CallerTid.ToString(), claims.GetProperty("tid").GetProperty("id").GetString());
        Assert.Equal(CallerOid.ToString(), claims.GetProperty("oid").GetProperty("id").GetString());

        var everything = result.GetRawText() + "\n" + string.Join("\n", _logs.Messages);
        Assert.DoesNotContain(token, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("user_impersonation", result.GetRawText(), StringComparison.Ordinal); // scope text is never echoed
        Assert.DoesNotContain(Audience, everything, StringComparison.Ordinal);                      // audience text is never echoed
        Assert.Equal(1, _dataverse.Calls);
    }

    [Fact]
    public async Task WhoAmI_WhenOboFails_ReportsFailureWithoutAFallbackIdentity()
    {
        _dataverse = new FakeDataverseUser(succeed: false);

        var result = await RpcAsync(SpikeMcpEndpoints.LegalOperationsRoute, "tools/call", new { name = "spike_whoami", arguments = new { } });

        var sc = result.GetProperty("structuredContent");
        Assert.Equal("failed", sc.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, sc.GetProperty("systemUserId").ValueKind);
        Assert.Equal(DataverseUserClientErrorCodes.OboExchangeFailed, sc.GetProperty("errorCode").GetString());
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static string MintToken() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["scp"] = "user_impersonation",
            ["tid"] = CallerTid.ToString(),
            ["oid"] = CallerOid.ToString(),
        },
    });

    private static HttpRequestMessage NewRpc(string route, string method, object parameters, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private Task<HttpResponseMessage> PostRpcAsync(string route, string method, object parameters, string? token = null) =>
        _client.SendAsync(NewRpc(route, method, parameters, token ?? MintToken()));

    /// <summary>Calls a method and returns the JSON-RPC <c>result</c> (parsing the SSE or JSON body the transport answers with).</summary>
    private async Task<JsonElement> RpcAsync(string route, string method, object parameters, string? token = null)
    {
        var response = await PostRpcAsync(route, method, parameters, token);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");

        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join(
                "\n",
                body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        Assert.False(doc.RootElement.TryGetProperty("error", out var error), $"JSON-RPC error: {error}");
        return doc.RootElement.GetProperty("result").Clone();
    }

    private sealed class FakeDataverseUser(bool succeed) : IDataverseUserClient
    {
        public int Calls { get; private set; }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("WhoAmI", relativePath);
            if (!succeed)
            {
                return Task.FromResult(DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.OboExchangeFailed, "obo failed"));
            }

            using var doc = JsonDocument.Parse($"{{\"UserId\":\"{CallerSystemUserId}\"}}");
            return Task.FromResult(DataverseUserResponse.Ok(200, doc.RootElement.Clone()));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._messages)
                {
                    owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>Wraps the response body feature so the test can see whether the endpoint disabled buffering.</summary>
    private sealed class RecordingBodyFeatureMiddleware(RequestDelegate next, RecordingBodyFeatureMiddleware.State state)
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
