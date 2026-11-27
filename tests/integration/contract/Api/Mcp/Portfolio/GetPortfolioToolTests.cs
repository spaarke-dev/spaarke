// CONTRACT — spaarke_get_portfolio on POST /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 026; spec FR-08, NFR-05; ADR-015, ADR-028).
// KEEP path: endpoint-contract.
//
// WHAT RUNS: the REAL BFF host (Program, through McpProgramHostFactory: production AddMcpModule, WorkspaceModule, the A10 route filter, the
// per-tool policy) with the PRODUCTION PortfolioService and WorkspaceEndpoints, over module-boundary doubles only: the people-targeted
// membership resolver, the caller-context Dataverse read (IImpersonatedCommunicationQuery) and the oid -> systemuserid resolver. The tool
// and GET /api/workspace/portfolio are both called over HTTP with the same token, so "structuredContent equals the REST body" compares two
// real answers. NOT HERE: PortfolioService's own aggregation and fail-closed reads (PortfolioServiceTests); the result-size bound (the
// result is one fixed-shape record of seven numbers, far under 64 KiB); the MCP protocol suite (the mcp-conformance CI job).

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Portfolio;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Portfolio;

public sealed class GetPortfolioToolTests : IClassFixture<PortfolioProgramHostFactory>
{
    private const string RestRoute = "/api/workspace/portfolio";

    // Two callers with different matters: the answer must follow the token, not a shared or earlier one.
    private static readonly Guid OidA = Guid.Parse("0260aaaa-0000-4000-8000-000000000026");
    private static readonly Guid OidB = Guid.Parse("0260bbbb-0000-4000-8000-000000000026");
    private static readonly Guid OidNobody = Guid.Parse("0260cccc-0000-4000-8000-000000000026");

    private const string SecretMatterName = "Confidential Matter Alpha";

    private readonly PortfolioProgramHostFactory _factory;

    public GetPortfolioToolTests(PortfolioProgramHostFactory factory) => _factory = factory;

    // ---- tools/list -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_GetPortfolio_IsReadOnly_Idempotent_ModelVisible_WithNoWidget_AndAnEmptyClosedSchema()
    {
        using var client = _factory.CreateClient();

        var tools = await McpCall.ListToolsAsync(client, PortfolioProgramHostFactory.MintToken(OidA));

        var tool = tools[GetPortfolioTool.Name];
        tool.GetProperty("title").GetString().Should().Be("Get my matter portfolio");
        var description = tool.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1024);
        description.Should().Contain("Use for:").And.Contain("Not for:");
        var annotations = tool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();

        // No widget: no _meta.ui at all, so the tool is model-visible by default and carries no resourceUri.
        var hasUi = tool.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("ui", out _);
        hasUi.Should().BeFalse("FR-08: the portfolio tool attaches no widget");

        var schema = tool.GetProperty("inputSchema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").EnumerateObject().Should().BeEmpty();
    }

    // ---- parity with REST --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPortfolio_StructuredContent_EqualsTheRestBody_ForTheSameCaller_WhicheverIsCalledFirst(bool restFirst)
    {
        using var client = _factory.CreateClient();
        var oid = restFirst ? Guid.Parse("0260dddd-0000-4000-8000-000000000001") : Guid.Parse("0260dddd-0000-4000-8000-000000000002");
        _factory.Seed.Add(oid, CallerA());
        var token = PortfolioProgramHostFactory.MintToken(oid);

        string rest;
        JsonElement tool;
        if (restFirst)
        {
            rest = await GetRestAsync(client, token);
            tool = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, token);
        }
        else
        {
            tool = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, token);
            rest = await GetRestAsync(client, token);
        }

        tool.TryGetProperty("isError", out var isError).Should().BeFalse("a success result carries no isError (got " + isError + ")");
        var structured = tool.GetProperty("structuredContent");
        AssertSameJson(structured, rest, "FR-08: the tool answers what the REST route answers for the same caller");

        // Not a coincidence of two empty answers: the seeded numbers are in it (2 matters, spend 1700 of 2000, 2 overdue tasks, both at risk).
        structured.GetProperty("activeMatters").GetInt32().Should().Be(2);
        structured.GetProperty("totalSpend").GetDecimal().Should().Be(1700m);
        structured.GetProperty("totalBudget").GetDecimal().Should().Be(2000m);
        structured.GetProperty("utilizationPercent").GetDecimal().Should().Be(85m);
        structured.GetProperty("overdueEvents").GetInt32().Should().Be(2);
        structured.GetProperty("mattersAtRisk").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetPortfolio_AnswersForTheTokensCaller_NotAnotherCaller()
    {
        using var client = _factory.CreateClient();
        var oidA = Guid.Parse("0260eeee-0000-4000-8000-00000000000a");
        var oidB = Guid.Parse("0260eeee-0000-4000-8000-00000000000b");
        _factory.Seed.Add(oidA, CallerA());
        _factory.Seed.Add(oidB, CallerB());

        var readsBefore = _factory.Seed.QueryCallers.Count;
        var asA = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, PortfolioProgramHostFactory.MintToken(oidA));
        var asB = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, PortfolioProgramHostFactory.MintToken(oidB));

        asA.GetProperty("structuredContent").GetProperty("activeMatters").GetInt32().Should().Be(2);
        asB.GetProperty("structuredContent").GetProperty("activeMatters").GetInt32().Should().Be(1);
        asB.GetProperty("structuredContent").GetProperty("totalSpend").GetDecimal().Should().Be(100m);

        // Every Dataverse read was made as the caller whose token asked (the systemuserid of that oid), never app-only or the other caller.
        var readAs = _factory.Seed.QueryCallers.Skip(readsBefore).Distinct().ToList();
        readAs.Should().BeEquivalentTo([_factory.Seed.SystemUserOf(oidA), _factory.Seed.SystemUserOf(oidB)]);
    }

    [Fact]
    public async Task GetPortfolio_ACallerThePlatformCannotResolve_GetsTheSameEmptyPortfolioAsRest_NotAnUnfilteredOne()
    {
        using var client = _factory.CreateClient();
        var token = PortfolioProgramHostFactory.MintToken(OidNobody); // no seed: the oid has no systemuser

        var tool = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, token);
        var rest = await GetRestAsync(client, token);

        AssertSameJson(tool.GetProperty("structuredContent"), rest, "an unresolvable caller gets REST's empty portfolio");
        tool.GetProperty("structuredContent").GetProperty("activeMatters").GetInt32().Should().Be(0);
    }

    // ---- text ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPortfolio_Text_IsAtMostFiveLinesOfCountsAndTotals_WithNoMatterNames()
    {
        using var client = _factory.CreateClient();
        var oid = Guid.Parse("0260ffff-0000-4000-8000-000000000001");
        _factory.Seed.Add(oid, CallerA());

        var result = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, PortfolioProgramHostFactory.MintToken(oid));

        var text = result.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString()!;
        text.Split('\n').Length.Should().BeLessThanOrEqualTo(5);
        text.Should().Contain("Active matters: 2").And.Contain("Matters at risk: 2").And.Contain("Overdue tasks: 2")
            .And.Contain("1,700.00").And.Contain("2,000.00").And.Contain("85%");
        text.Should().NotContain(SecretMatterName);
        result.GetProperty("structuredContent").GetRawText().Should().NotContain(SecretMatterName, "ADR-015: counts and totals only");
    }

    // ---- arguments ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPortfolio_AnArgument_IsAnIsErrorResult_NotIgnored_AndReadsNothing()
    {
        using var client = _factory.CreateClient();
        var oid = Guid.Parse("0260ffff-0000-4000-8000-000000000002");
        _factory.Seed.Add(oid, CallerA());
        var readsBefore = _factory.Seed.QueryCallers.Count;

        var result = await McpCall.CallAsync(client, GetPortfolioTool.Name, new { matterId = "x" }, PortfolioProgramHostFactory.MintToken(oid));

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(GetPortfolioTool.InvalidParameterReason);
        error.GetProperty("status").GetInt32().Should().Be(400);
        _factory.Seed.QueryCallers.Count.Should().Be(readsBefore, "a refused call reads nothing");
    }

    // ---- no caller (ADR-028, FR-10) ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPortfolio_WithNoRequestContext_IsUserContextRequired_AndReadsNothing()
    {
        using var scope = _factory.Services.CreateScope();
        var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioService>();
        var readsBefore = _factory.Seed.QueryCallers.Count;

        var result = await GetPortfolioTool.RunAsync(null, null, portfolio, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        AssertUserContextRequired(result);
        _factory.Seed.QueryCallers.Count.Should().Be(readsBefore);
    }

    [Theory]
    [InlineData(false, true)] // not authenticated, has an oid claim
    [InlineData(true, false)] // authenticated, no oid claim (the REST filter's 401)
    public async Task GetPortfolio_WithAnUnauthenticatedCallerOrNoOid_IsUserContextRequired_AndReadsNothing(bool authenticated, bool hasOid)
    {
        using var scope = _factory.Services.CreateScope();
        var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioService>();
        var claims = hasOid ? new[] { new Claim("oid", OidA.ToString()) } : Array.Empty<Claim>();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null)) };
        var readsBefore = _factory.Seed.QueryCallers.Count;

        var result = await GetPortfolioTool.RunAsync(null, http, portfolio, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        AssertUserContextRequired(result);
        _factory.Seed.QueryCallers.Count.Should().Be(readsBefore, "no caller, no read, no app-only fallback");
    }

    // ---- failure and telemetry ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPortfolio_AServiceFault_IsAnIsErrorResult_NotATransportError_AndLeaksNoExceptionText()
    {
        await using var faulty = new PortfolioProgramHostFactory(failingCache: true);
        using var client = faulty.CreateClient();
        var token = PortfolioProgramHostFactory.MintToken(OidA);

        var result = await McpCall.CallAsync(client, GetPortfolioTool.Name, null, token);
        var rest = await client.SendAsync(RestRequest(token));

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(GetPortfolioTool.UnavailableReason);
        error.GetProperty("status").GetInt32().Should().Be(500);
        error.GetRawText().Should().NotContain(PortfolioProgramHostFactory.CacheFaultText);
        ((int)rest.StatusCode).Should().Be(500, "the REST route answers the same fault with a 500");
    }

    [Fact]
    public async Task GetPortfolio_WritesOneTelemetryLine_WithToolOutcomeAndCorrelation_AndNoIdentity()
    {
        var logs = new PortfolioProgramHostFactory.CapturedLogs();
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var client = factory.CreateClient();
        var oid = Guid.Parse("0260ffff-0000-4000-8000-000000000003");
        _factory.Seed.Add(oid, CallerA());

        await McpCall.CallAsync(client, GetPortfolioTool.Name, null, PortfolioProgramHostFactory.MintToken(oid));

        var line = logs.Messages.Where(m => m.Contains("MCP tool spaarke_get_portfolio completed", StringComparison.Ordinal)).Should().ContainSingle().Subject;
        line.Should().Contain("outcome ok").And.Contain("correlation");
        logs.Messages.Where(m => m.Contains("MCP tool", StringComparison.Ordinal)).Should().OnlyContain(m => !m.Contains(oid.ToString()));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------

    private static void AssertUserContextRequired(ModelContextProtocol.Protocol.CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        body.GetProperty("status").GetInt32().Should().Be(401);
    }

    /// <summary>The same JSON value (the two hosts may escape characters such as '+' differently; the value is what FR-08 compares).</summary>
    private static void AssertSameJson(JsonElement tool, string restBody, string because)
    {
        var expected = System.Text.Json.Nodes.JsonNode.Parse(restBody);
        var actual = System.Text.Json.Nodes.JsonNode.Parse(tool.GetRawText());
        System.Text.Json.Nodes.JsonNode.DeepEquals(actual, expected).Should().BeTrue(because + $"; tool {actual?.ToJsonString()} vs REST {expected?.ToJsonString()}");
    }

    private static HttpRequestMessage RestRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, RestRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> GetRestAsync(HttpClient client, string token)
    {
        using var response = await client.SendAsync(RestRequest(token));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>Caller A: two active matters, spend 1,700 of 2,000, two overdue tasks on the first.</summary>
    private static PortfolioProgramHostFactory.CallerData CallerA()
    {
        var m1 = Guid.Parse("a0000000-0000-4000-8000-000000000001");
        var m2 = Guid.Parse("a0000000-0000-4000-8000-000000000002");
        return new PortfolioProgramHostFactory.CallerData(
            [new(m1, SecretMatterName, 800m, 1000m), new(m2, "Second Matter", 900m, 1000m)],
            OverdueOn: [m1, m1]);
    }

    /// <summary>Caller B: one matter, spend 100 of 400, nothing overdue.</summary>
    private static PortfolioProgramHostFactory.CallerData CallerB() =>
        new([new(Guid.Parse("b0000000-0000-4000-8000-000000000001"), "Other Matter", 100m, 400m)], OverdueOn: []);
}

/// <summary>
/// TESTS ONLY (task 026). The real BFF host with the seeded module-boundary doubles <see cref="PortfolioSeed"/> in place of Dataverse: the
/// people-targeted matter set, the caller-context read and the oid -> systemuserid resolver. Everything between them is production code.
/// </summary>
public sealed class PortfolioProgramHostFactory : McpProgramHostFactory
{
    internal const string CacheFaultText = "redis-exploded-secret-detail";

    private readonly bool _failingCache;

    public PortfolioProgramHostFactory() : this(failingCache: false)
    {
    }

    internal PortfolioProgramHostFactory(bool failingCache) => _failingCache = failingCache;

    internal PortfolioSeed Seed { get; } = new();

    protected override void ConfigureSeededServices(IServiceCollection services)
    {
        services.RemoveAll<IMembershipResolverService>();
        services.AddSingleton<IMembershipResolverService>(Seed);
        services.RemoveAll<IImpersonatedCommunicationQuery>();
        services.AddSingleton<IImpersonatedCommunicationQuery>(Seed);
        services.RemoveAll<ISystemUserIdentityResolver>();
        services.AddSingleton<ISystemUserIdentityResolver>(Seed);
        if (_failingCache)
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, ThrowingCache>();
        }
    }

    /// <summary>One matter row of a caller's seed.</summary>
    internal sealed record MatterSeed(Guid Id, string Name, decimal Spend, decimal Budget);

    /// <summary>A caller's seed: their matters, and the matter id of each overdue open task (one id per task).</summary>
    internal sealed record CallerData(IReadOnlyList<MatterSeed> Matters, IReadOnlyList<Guid> OverdueOn);

    /// <summary>The boundary doubles in one object; <see cref="QueryCallers"/> records the systemuserid of every caller-context read.</summary>
    internal sealed class PortfolioSeed : IMembershipResolverService, IImpersonatedCommunicationQuery, ISystemUserIdentityResolver
    {
        private readonly Dictionary<string, (Guid SystemUser, CallerData Data)> _byOid = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private readonly List<Guid> _queryCallers = new();

        public IReadOnlyList<Guid> QueryCallers
        {
            get
            {
                lock (_gate)
                {
                    return _queryCallers.ToList();
                }
            }
        }

        public void Add(Guid oid, CallerData data)
        {
            lock (_gate)
            {
                // The systemuserid is a different id from the oid on purpose: the two id spaces must never be confused.
                _byOid[oid.ToString("D")] = (Guid.NewGuid(), data);
            }
        }

        public Guid SystemUserOf(Guid oid)
        {
            lock (_gate)
            {
                return _byOid[oid.ToString("D")].SystemUser;
            }
        }

        public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_byOid.TryGetValue(oid, out var entry) ? (Guid?)entry.SystemUser : null);
            }
        }

        public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<MembershipResponse> ResolveAsync(Guid systemUserId, string entityType, MembershipResolveOptions? options, CancellationToken ct)
        {
            var ids = DataOf(systemUserId)?.Matters.Select(m => m.Id).ToArray() ?? [];
            return Task.FromResult(new MembershipResponse(
                EntityType: entityType,
                PersonIdentity: new PersonIdentity(systemUserId),
                Ids: ids,
                ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
                Count: ids.Length,
                CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
                ContinuationToken: null));
        }

        public Task<MembershipResponse> ResolveByContactAsync(Guid contactId, string entityType, MembershipResolveOptions? options, CancellationToken ct) =>
            throw new NotSupportedException("The portfolio reads the people (systemuser) plane only.");

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            lock (_gate)
            {
                _queryCallers.Add(callerSystemUserId);
            }

            var data = DataOf(callerSystemUserId);
            var query = odataQuery ?? string.Empty;
            IReadOnlyList<Dictionary<string, JsonElement>> rows = entitySetName switch
            {
                "sprk_matters" when data is not null => data.Matters
                    .Where(m => query.Contains(m.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    .Select(m => Row(new Dictionary<string, object?>
                    {
                        ["sprk_matterid"] = m.Id.ToString("D"),
                        ["sprk_mattername"] = m.Name,
                        ["sprk_totalspendtodate"] = m.Spend,
                        ["sprk_totalbudget"] = m.Budget,
                    })).ToList(),
                "sprk_events" when data is not null => data.OverdueOn
                    .Where(id => query.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    .Select(id => Row(new Dictionary<string, object?>
                    {
                        ["sprk_eventid"] = Guid.NewGuid().ToString("D"),
                        ["_sprk_regardingmatter_value"] = id.ToString("D"),
                    })).ToList(),
                _ => [],
            };
            return Task.FromResult(rows);
        }

        private CallerData? DataOf(Guid systemUserId)
        {
            lock (_gate)
            {
                return _byOid.Values.FirstOrDefault(v => v.SystemUser == systemUserId).Data;
            }
        }

        private static Dictionary<string, JsonElement> Row(Dictionary<string, object?> values) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;
    }

    /// <summary>A cache that faults on every read: the service's exception reaches the route and the tool.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException(CacheFaultText);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException(CacheFaultText);

        public void Refresh(string key) => throw new InvalidOperationException(CacheFaultText);

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException(CacheFaultText);

        public void Remove(string key) => throw new InvalidOperationException(CacheFaultText);

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException(CacheFaultText);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException(CacheFaultText);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException(CacheFaultText);
    }

    /// <summary>Collects the formatted text of every log entry the host writes.</summary>
    public sealed class CapturedLogs : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new Capture(_messages);

        public void Dispose()
        {
        }

        private sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue(formatter(state, exception));
        }
    }
}

/// <summary>One JSON-RPC call over the host's HTTP client (the <c>McpHostContractTests</c> request shape).</summary>
internal static class McpCall
{
    public static async Task<IReadOnlyDictionary<string, JsonElement>> ListToolsAsync(HttpClient client, string token)
    {
        var result = await RpcAsync(client, "tools/list", new { }, token);
        return result.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t);
    }

    public static Task<JsonElement> CallAsync(HttpClient client, string tool, object? arguments, string token) =>
        RpcAsync(client, "tools/call", arguments is null ? new { name = tool } : new { name = tool, arguments }, token);

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object parameters, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
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
