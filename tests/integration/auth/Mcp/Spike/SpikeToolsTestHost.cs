// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; spike/specialist-agents-phase0, never merged; ADR-038: branch-only tests).
//
// TESTS ONLY. The task-020/022 host shape (DecisionToolsTestHost): the PRODUCTION AddMcpModule, MapMcpEndpoints and RateLimitingModule
// on a real Kestrel listener behind a real JwtBearer scheme and the A10 token filter, serving exactly the two spike tools over a boundary
// double of ISpecialistProfileSpikeAi (the caller resolver and the Action run) and a capturing logger. The profiles are the REAL embedded
// files, keyed by the real DEV systemuserids.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Mcp;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

internal sealed class SpikeToolsTestHost : IAsyncDisposable
{
    /// <summary>The owner's DEV systemuserid (profile A, NDA and patent), read by task 011 with WhoAmI.</summary>
    public static readonly Guid OwnerSystemUserId = Guid.Parse("1d02f31c-1872-f011-b4cb-7c1e52671ad0");

    /// <summary>The test user's (demo@) DEV systemuserid (profile B, NDA and patent).</summary>
    public static readonly Guid TestUserSystemUserId = Guid.Parse("849beec2-2b30-f111-88b5-7c1e520aa4df");

    /// <summary>Caller oids the double maps: owner, test user, a resolved user with no profile, a user the resolver cannot resolve.</summary>
    public static readonly Guid OwnerOid = Guid.Parse("0110aaaa-0000-4000-8000-000000000a01");
    public static readonly Guid TestUserOid = Guid.Parse("0110aaaa-0000-4000-8000-000000000b01");
    public static readonly Guid NoProfileOid = Guid.Parse("0110aaaa-0000-4000-8000-000000000c01");
    public static readonly Guid UnresolvedOid = Guid.Parse("0110aaaa-0000-4000-8000-000000000d01");
    public static readonly Guid NoProfileSystemUserId = Guid.Parse("0110aaaa-0000-4000-8000-000000000c02");

    private const string Issuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("spike-tools-test-signing-key-0123456789-0123456789-01"));

    private readonly WebApplication _app;

    private SpikeToolsTestHost(WebApplication app, HttpClient client, DecisionToolsTestHost.CapturingLoggerProvider logs, SpikeAiDouble spike)
    {
        _app = app;
        Client = client;
        Logs = logs;
        Spike = spike;
    }

    public HttpClient Client { get; }

    public DecisionToolsTestHost.CapturingLoggerProvider Logs { get; }

    public SpikeAiDouble Spike { get; }

    /// <summary>Starts the host over <paramref name="spike"/>, or over <paramref name="facade"/> when given (for example the AI-off Null facade).</summary>
    public static async Task<SpikeToolsTestHost> StartAsync(SpikeAiDouble? spike = null, ISpecialistProfileSpikeAi? facade = null)
    {
        spike ??= new SpikeAiDouble();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var logs = new DecisionToolsTestHost.CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(facade ?? spike);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = McpTestHost.BffClientId,
                IssuerSigningKey = SigningKey,
            });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimitingModule();
        builder.Services.AddMcpModule(builder.Configuration, new[] { typeof(SpikeGetProfileTool), typeof(SpikeReviewNdaTool) });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new SpikeToolsTestHost(app, new HttpClient { BaseAddress = new Uri(address) }, logs, spike);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>A valid A10 workforce token for the caller <paramref name="oid"/>.</summary>
    public static string MintToken(Guid oid) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = McpTestHost.BffClientId,
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["scp"] = "user_impersonation",
            ["oid"] = oid.ToString(),
            ["tid"] = McpTestHost.TenantId,
        },
    });

    /// <summary><c>tools/list</c>'s tools, by name.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>> ListToolsAsync(string token)
    {
        var result = await RpcAsync("tools/list", new { }, token, null);
        return result.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t);
    }

    /// <summary>One <c>tools/call</c>; <paramref name="extraHeaders"/> are added to the request as sent.</summary>
    public Task<JsonElement> CallAsync(string tool, object? arguments, string token, IReadOnlyDictionary<string, string>? extraHeaders = null) =>
        RpcAsync("tools/call", arguments is null ? new { name = tool } : new { name = tool, arguments }, token, extraHeaders);

    private async Task<JsonElement> RpcAsync(string method, object parameters, string token, IReadOnlyDictionary<string, string>? extraHeaders)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var (name, value) in extraHeaders ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await Client.SendAsync(request);
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
}

/// <summary>
/// Boundary double of <see cref="ISpecialistProfileSpikeAi"/>: resolves the caller's <c>oid</c> claim through a fixed map (the owner, the
/// test user, a resolved user with no profile; anything else is unresolved), and records every review request, answering with a review
/// that echoes the positions it was given.
/// </summary>
internal sealed class SpikeAiDouble : ISpecialistProfileSpikeAi
{
    private readonly Dictionary<Guid, Guid> _systemUsers = new()
    {
        [SpikeToolsTestHost.OwnerOid] = SpikeToolsTestHost.OwnerSystemUserId,
        [SpikeToolsTestHost.TestUserOid] = SpikeToolsTestHost.TestUserSystemUserId,
        [SpikeToolsTestHost.NoProfileOid] = SpikeToolsTestHost.NoProfileSystemUserId,
    };

    public ConcurrentQueue<SpikeReviewRequest> Reviews { get; } = new();

    public int Resolutions;

    /// <summary>The resolver reason for a caller the map does not know (the real resolver's identifiers, e.g. <c>lookup-failed</c>).</summary>
    public string UnresolvedReason { get; set; } = "no-matching-systemuser";

    /// <summary>The deployment a review reports (the facade resolves it from the tier).</summary>
    public const string Deployment = "test-reasoning-deployment";

    /// <summary>When set, a review throws this instead of answering.</summary>
    public Exception? ReviewFailure { get; set; }

    public Task<SpikeCallerResult> ResolveCallerAsync(ClaimsPrincipal? caller, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Resolutions);
        var oid = caller?.FindFirst("oid")?.Value ?? caller?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        return Task.FromResult(Guid.TryParse(oid, out var id) && _systemUsers.TryGetValue(id, out var user)
            ? SpikeCallerResult.Resolved(user.ToString("D"))
            : SpikeCallerResult.Unresolved(UnresolvedReason));
    }

    public Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken)
    {
        Reviews.Enqueue(request);
        if (ReviewFailure is not null)
        {
            throw ReviewFailure;
        }

        var review = JsonSerializer.SerializeToElement(new
        {
            findings = request.Positions.Select(p => new { topic = p.Key, position = p.Value }).ToArray(),
        });
        return Task.FromResult(new SpikeReviewResult(review, SpecialistProfileSpike.ActionCode, request.ModelTier, Deployment, [.. request.Positions.Keys]));
    }
}
