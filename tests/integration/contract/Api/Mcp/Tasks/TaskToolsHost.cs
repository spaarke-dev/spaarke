// TESTS ONLY (spaarkeai-mcp-bff-api-r2 task 025). The host the task read tools' contract and must-not-leak tests share.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Auth.Events;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Tasks;

/// <summary>
/// The production MCP module serving <see cref="ListTasksTool"/> and <see cref="GetTaskTool"/> and the REAL events REST routes
/// (<c>MapEventEndpoints</c>) on one Kestrel host behind one JwtBearer scheme, over the REAL <see cref="EventsCapability"/> and the REAL
/// <see cref="Sprk.Bff.Api.Infrastructure.Dataverse.DataverseWebApiService"/> on task 006's row-secured <see cref="EventCallerScopeTests.FakeDataverse"/>.
/// So "the tool's ids are the route's ids for the same caller" is compared over the wire with the same token, and a list that went
/// out app-only (or as the wrong user) would return another caller's private event.
/// </summary>
/// <remarks>
/// SUBSTITUTED (module boundaries, ADR-038): Dataverse's wire (the hand-written <c>FakeDataverse</c>: no <c>Mock&lt;HttpMessageHandler&gt;</c>), the
/// caller-rights probe's <c>RetrievePrincipalAccess</c> seam (answered from the same rows, for the user the JWT's <c>oid</c> names), the
/// caller to systemuser lookup and the linked-contact lookup. Services the read path never reaches are strict doubles or factories that
/// throw, so a read that reached a write would fault and fail the test.
/// </remarks>
internal sealed class TaskToolsHost : IAsyncDisposable
{
    internal const string EnvironmentUrl = "https://spaarke-test.crm.dynamics.com";

    /// <summary>A signed-in caller who resolves to no Dataverse user (the list's fail-closed refusal).</summary>
    internal static readonly Guid OidUnresolved = Guid.Parse("a0250000-0000-4000-8000-0000000000cc");

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private TaskToolsHost(WebApplication app, HttpClient client, EventCallerScopeTests.FakeDataverse dataverse, CapturingLoggerProvider logs)
    {
        _app = app;
        _client = client;
        Dataverse = dataverse;
        Logs = logs;
    }

    public EventCallerScopeTests.FakeDataverse Dataverse { get; }

    /// <summary>Every log line the host wrote, as <c>category: message</c>.</summary>
    public CapturingLoggerProvider Logs { get; }

    public static async Task<TaskToolsHost> StartAsync(EventCallerScopeTests.FakeDataverse dataverse)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        var logs = new CapturingLoggerProvider();
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = $"https://login.microsoftonline.com/{McpTestHost.TenantId}/v2.0",
                ValidAudience = McpTestHost.BffClientId,
                IssuerSigningKey = SigningKey,
            });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimitingModule();
        builder.Services.AddMcpModule(builder.Configuration, [typeof(ListTasksTool), typeof(GetTaskTool)]);
        builder.Services.Configure<DataverseOptions>(o => o.EnvironmentUrl = EnvironmentUrl);

        // The module boundaries (see the class remarks).
        var events = new EventCallerScopeTests.OfflineDataverseService(dataverse);
        var probe = new JwtRowSecurityProbe(dataverse);
        var resolver = JwtCallerResolver().Object;
        var identity = EventCallerScopeTests.NoLinkedContacts().Object;
        var communication = new Mock<ICommunicationDataverseService>(MockBehavior.Strict).Object;
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict).Object;
        var user = new Mock<IDataverseUserClient>(MockBehavior.Strict).Object;
        var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object;
        builder.Services.AddSingleton<IEventDataverseService>(events);
        builder.Services.AddSingleton<CallerRecordAccessProbe>(probe);
        builder.Services.AddSingleton(resolver);
        builder.Services.AddSingleton(identity);
        builder.Services.AddSingleton(communication);
        builder.Services.AddSingleton(entities);
        builder.Services.AddSingleton(user);
        builder.Services.AddSingleton(ownership);
        builder.Services.AddSingleton<RecordCreationService>(_ => throw new InvalidOperationException("not on the read path"));
        builder.Services.AddSingleton<CoreAncestorRestamper>(_ => throw new InvalidOperationException("not on the read path"));
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Access.SecureChildReconciler>(_ => throw new InvalidOperationException("not on the read path"));

        // The capability, as production registers it (WorkspaceModule), but with no create path: a read that reached it would fault.
        builder.Services.AddScoped<IEventsCapability>(sp => new EventsCapability(
            events, communication, resolver, identity, entities, recordCreation: null!, user, ownership, probe,
            sp.GetRequiredService<ILogger<EventsCapability>>()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        app.MapEventEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TaskToolsHost(app, new HttpClient { BaseAddress = new Uri(address) }, dataverse, logs);
    }

    /// <summary>A valid workforce token for caller "A" or "B" (or the unresolved caller for any other name).</summary>
    public static string TokenOf(string who) => McpTestHost.MintToken(who switch
    {
        "A" => EventCallerScopeTests.OidA,
        "B" => EventCallerScopeTests.OidB,
        _ => OidUnresolved,
    });

    /// <summary>One JSON-RPC call as <paramref name="who"/>; returns the <c>result</c> (the transport answers JSON or an SSE stream).</summary>
    public async Task<JsonElement> RpcAsync(string who, string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf(who));

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        doc.RootElement.TryGetProperty("error", out var error).Should().BeFalse($"a JSON-RPC error is a transport failure, not a domain result: {error}");
        return doc.RootElement.GetProperty("result").Clone();
    }

    /// <summary><c>tools/call</c> of <paramref name="tool"/> as <paramref name="who"/>.</summary>
    public Task<JsonElement> CallAsync(string who, string tool, object arguments) =>
        RpcAsync(who, "tools/call", new { name = tool, arguments });

    /// <summary>A REST request as <paramref name="who"/>, with the same token.</summary>
    public Task<HttpResponseMessage> GetAsync(string who, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf(who));
        return _client.SendAsync(request);
    }

    /// <summary>The body of a successful REST GET.</summary>
    public async Task<JsonNode> RestJsonAsync(string who, string path)
    {
        using var response = await GetAsync(who, path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// Resolves the request's Entra object id to that caller's systemuserid, as the real resolver does. It reads the id with
    /// <see cref="CallerResolution.ResolveObjectId"/> (JwtBearer maps the token's <c>oid</c> to the schema claim type; task 006's resolver
    /// reads the raw name, which its own test scheme sets). Any other caller is unresolved.
    /// </summary>
    private static Mock<ICallerSystemUserResolver> JwtCallerResolver()
    {
        var resolver = new Mock<ICallerSystemUserResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Security.Claims.ClaimsPrincipal? user, CancellationToken _) => CallerResolution.ResolveObjectId(user) switch
            {
                var oid when oid == EventCallerScopeTests.OidA.ToString("D") => CallerSystemUserResolution.Resolved(EventCallerScopeTests.UserA.ToString("D")),
                var oid when oid == EventCallerScopeTests.OidB.ToString("D") => CallerSystemUserResolution.Resolved(EventCallerScopeTests.UserB.ToString("D")),
                _ => CallerSystemUserResolution.Unresolved("no-matching-oid"),
            });
        return resolver;
    }

    /// <summary>
    /// The caller-rights probe at its <c>RetrievePrincipalAccess</c> seam, answered from <see cref="EventCallerScopeTests.FakeDataverse"/>'s
    /// row security for the user the bearer token's <c>oid</c> names (task 006's probe maps fixed strings, which this host's JWTs are not).
    /// </summary>
    private sealed class JwtRowSecurityProbe(EventCallerScopeTests.FakeDataverse dataverse) : CallerRecordAccessProbe(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default) =>
            Task.FromResult(
                entitySet == EventEndpoints.EventEntitySet && UserOf(callerBearerToken) is { } user && dataverse.CanRead(user, recordId)
                    ? AccessRights.Read | AccessRights.Write
                    : AccessRights.None);

        public override Task<bool> CallerHoldsPrivilegeAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default) =>
            Task.FromResult(false);

        private static Guid? UserOf(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var oid = new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
            return oid == EventCallerScopeTests.OidA.ToString() ? EventCallerScopeTests.UserA
                : oid == EventCallerScopeTests.OidB.ToString() ? EventCallerScopeTests.UserB
                : null;
        }
    }
}

/// <summary>Keeps every log line, so a test can prove what the host did and did not write (NFR-05).</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToArray();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Capture(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Add($"{category}: {formatter(state, exception)}" + (exception is null ? string.Empty : " | exception " + exception));
            }
        }
    }
}
