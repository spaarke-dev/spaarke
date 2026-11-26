// TESTS ONLY (spaarkeai-mcp-bff-api-r2 task 022). The one host the search tool tests share (the contract tests, the must-not-leak tests and the
// issue #3212 regression test):
//   - the PRODUCTION AddMcpModule / MapMcpEndpoints serving the two production search tools, behind a real JwtBearer scheme, on a real listener;
//   - the REAL REST search routes (MapSemanticSearchEndpoints, MapRecordSearchEndpoints) with their real filters, handlers and
//     AuthorizationService on the SAME container and the SAME token, so "the tool returns what the route returns for the same caller and
//     query" is asserted against the shipped route, not a re-statement of it;
//   - substituted only at module boundaries (ADR-038): the access-data source (what Dataverse answers for the caller), the two search
//     indexes and IRetrievalAccessTrim (the PublicContracts facade over the caller's Dataverse read). No HttpMessageHandler or
//     IServiceClient mock.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Search;

internal sealed class SearchToolsHost : IAsyncDisposable
{
    public const string Matters = "sprk_matters";
    public const string Documents = "sprk_documents";

    /// <summary>The Dataverse environment the open links are built on.</summary>
    public const string EnvironmentUrl = "https://org.crm.dynamics.com";

    public static readonly Guid CallerOid = Guid.Parse("6f0c1a52-0000-4000-8000-000000000022");

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private SearchToolsHost(WebApplication app, HttpClient client, SearchToolsWorld world)
    {
        _app = app;
        _client = client;
        World = world;
    }

    public SearchToolsWorld World { get; }

    public RecordingAccessDataSource Access => World.Access;

    public StubDocumentIndex DocumentIndex => World.DocumentIndex;

    public StubRecordIndex RecordIndex => World.RecordIndex;

    public StubTrim Trim => World.Trim;

    public static async Task<SearchToolsHost> StartAsync(SearchToolsWorld? world = null)
    {
        world ??= new SearchToolsWorld();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

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
        builder.Services.AddMcpModule(builder.Configuration, [typeof(SearchDocumentsTool), typeof(FindRecordsTool)]);

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.Configure<DataverseOptions>(o => o.EnvironmentUrl = EnvironmentUrl);

        builder.Services.AddSingleton<IAccessDataSource>(world.Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddStatedNarrowingCaller(() => NarrowingCallerMode.Internal, () => world.NarrowingWorld);
        builder.Services.AddSingleton<ISemanticSearchService>(world.DocumentIndex);
        builder.Services.AddSingleton<IRecordSearchService>(world.RecordIndex);
        builder.Services.AddSingleton<IRetrievalAccessTrim>(world.Trim);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        app.MapSemanticSearchEndpoints();
        app.MapRecordSearchEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new SearchToolsHost(app, new HttpClient { BaseAddress = new Uri(address) }, world);
    }

    // ---- the MCP route ---------------------------------------------------------------------------------------------

    public Task<JsonElement> CallToolAsync(string tool, object arguments) => RpcAsync("tools/call", new { name = tool, arguments });

    /// <summary>One JSON-RPC call over the production route; returns the <c>result</c> (the transport answers JSON or an SSE stream).</summary>
    public async Task<JsonElement> RpcAsync(string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpTestHost.MintToken(CallerOid));

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

    // ---- the REST routes (same container, same token) ---------------------------------------------------------------

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostRestAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpTestHost.MintToken(CallerOid));
        using var response = await _client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.Clone());
    }

    /// <summary>The route's own answer for the same input the tool sends: <c>POST /api/ai/search</c>.</summary>
    public Task<(HttpStatusCode Status, JsonElement Body)> RestDocumentsAsync(object body) => PostRestAsync("/api/ai/search", body);

    /// <summary>The route's own answer for the same input the tool sends: <c>POST /api/ai/search/records</c>.</summary>
    public Task<(HttpStatusCode Status, JsonElement Body)> RestRecordsAsync(object body) => PostRestAsync("/api/ai/search/records", body);

    // ---- direct handler calls ---------------------------------------------------------------------------------------

    /// <summary>A request context as the MCP route makes it for the caller: authenticated with <c>oid</c> and <c>tid</c>, a bearer token, the host's services.</summary>
    public DefaultHttpContext DirectContext(bool authenticated = true, bool withToken = true, bool withTenant = true)
    {
        var http = new DefaultHttpContext { RequestServices = _app.Services.CreateScope().ServiceProvider, TraceIdentifier = "trace-022" };
        var claims = new List<Claim> { new("oid", CallerOid.ToString("D")) };
        if (withTenant)
        {
            claims.Add(new Claim("tid", McpTestHost.TenantId));
        }

        http.User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity(claims));
        if (withToken)
        {
            http.Request.Headers.Authorization = "Bearer caller-token";
        }

        return http;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>The module-boundary doubles one host runs over, and the seeds the tests set.</summary>
internal sealed class SearchToolsWorld
{
    public RecordingAccessDataSource Access { get; } = new();

    public StubDocumentIndex DocumentIndex { get; } = new();

    public StubRecordIndex RecordIndex { get; } = new();

    public StubTrim Trim { get; } = new();

    public AppOnlyDocumentWorld NarrowingWorld { get; } = new();
}

/// <summary>The document index at its module boundary: returns the seeded rows and records every request it is sent.</summary>
internal sealed class StubDocumentIndex : ISemanticSearchService
{
    private SearchResult[] _rows = [];

    /// <summary>When set, a search throws it (the index or a Dataverse enrichment faulting).</summary>
    public Exception? Fault { get; set; }

    /// <summary>The unfiltered total the index reports (a number the tools must never show).</summary>
    public long ReportedTotal { get; set; } = 4242;

    public List<SemanticSearchRequest> Requests { get; } = [];

    public void Returns(params SearchResult[] rows) => _rows = rows;

    public Task<SemanticSearchResponse> SearchAsync(SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Fault is { } fault)
        {
            throw fault;
        }

        return Task.FromResult(new SemanticSearchResponse
        {
            Results = _rows,
            Metadata = new SearchMetadata { TotalResults = ReportedTotal, ReturnedResults = _rows.Length, ExecutedMode = "rrf" },
        });
    }

    public Task<SemanticSearchCountResponse> CountAsync(SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default) =>
        throw (Fault ?? new NotSupportedException("The search tools do not count."));
}

/// <summary>What the search tool tests read out of a tool result and build for a call.</summary>
internal static class SearchToolsTestSupport
{
    /// <summary>A JSON object as the <c>arguments</c> dictionary the tool handlers read.</summary>
    public static IDictionary<string, JsonElement> Args(object arguments) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!;

    /// <summary>A directly returned tool result as the JSON the MCP route would carry.</summary>
    public static JsonElement JsonOf(ModelContextProtocol.Protocol.CallToolResult result) =>
        JsonSerializer.SerializeToElement(result, ModelContextProtocol.McpJsonUtilities.DefaultOptions);

    public static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    public static string? Code(JsonElement result) => result.GetProperty("structuredContent").GetProperty("reasonCode").GetString();

    public static int Status(JsonElement result) => result.GetProperty("structuredContent").GetProperty("status").GetInt32();

    public static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    public static List<JsonElement> Items(JsonElement result) =>
        result.GetProperty("structuredContent").GetProperty("items").EnumerateArray().ToList();

    public static string[] ItemIds(JsonElement result) => Items(result).Select(i => i.GetProperty("id").GetString()!).ToArray();

    /// <summary>The result as bytes with the per-request correlation id blanked (the one thing two requests differ in).</summary>
    public static string Normalised(JsonElement result) =>
        System.Text.RegularExpressions.Regex.Replace(result.GetRawText(), "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"<id>\"");

    public static SearchResult Doc(Guid parent, string? name = null, string? parentName = "Acme v. Globex", Guid? id = null) => new()
    {
        DocumentId = (id ?? Guid.NewGuid()).ToString("D"),
        Name = name ?? "Master services agreement.docx",
        FileType = "docx",
        DocumentType = "Contract",
        ParentEntityType = "matter",
        ParentEntityId = parent.ToString("D"),
        ParentEntityName = parentName,
        Highlights = ["the <em>indemnity</em> clause survives termination"],
        DriveId = "b!SECRET-DRIVE",
        SpeFileId = "SECRET-ITEM",
        CombinedScore = 0.91,
        Summary = "SECRET-SUMMARY",
        Tldr = null,
        CreatedBy = "SECRET-AUTHOR",
    };

    public static RecordSearchResult Record(Guid id, string name = "Acme v. Globex", string type = RecordEntityType.Matter) => new()
    {
        RecordId = id.ToString("D"),
        RecordType = type,
        RecordName = name,
        RecordDescription = "Commercial dispute over the master services agreement",
        ConfidenceScore = 0.8,
        MatchReasons = ["SECRET-REASON"],
        Organizations = ["SECRET-ORG"],
        People = ["SECRET-PERSON"],
        Keywords = ["SECRET-KEYWORD"],
        ReferenceNumbers = ["MAT-2026-001"],
    };
}

/// <summary>The record index at its module boundary.</summary>
internal sealed class StubRecordIndex : IRecordSearchService
{
    private RecordSearchResult[] _rows = [];

    /// <summary>When set, a search throws it.</summary>
    public Exception? Fault { get; set; }

    public int ReportedTotal { get; set; } = 4242;

    public List<RecordSearchRequest> Requests { get; } = [];

    public void Returns(params RecordSearchResult[] rows) => _rows = rows;

    public Task<RecordSearchResponse> SearchAsync(RecordSearchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Fault is { } fault)
        {
            throw fault;
        }

        return Task.FromResult(new RecordSearchResponse
        {
            Results = _rows,
            Metadata = new RecordSearchMetadata { TotalCount = ReportedTotal, SearchTime = 1, HybridMode = RecordHybridSearchMode.Rrf },
        });
    }
}

/// <summary>
/// <see cref="IRetrievalAccessTrim"/> at its PublicContracts boundary: answers as the caller's Dataverse read would, keeping every row whose
/// document is not listed as unreadable.
/// </summary>
internal sealed class StubTrim : IRetrievalAccessTrim
{
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Calls { get; private set; }

    public bool CanEvaluate(string? callerObjectId) => !string.IsNullOrEmpty(callerObjectId);

    public Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows, Func<T, string?> documentIdOf, string? callerObjectId, CancellationToken cancellationToken = default)
    {
        Calls++;
        var kept = rows.Where(r => documentIdOf(r) is { } id && !Unreadable.Contains(id)).ToList();
        return Task.FromResult(new RetrievalTrimResult<T>(kept, RetrievalTrimOutcome.Evaluated, rows.Count - kept.Count));
    }

    public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
        IReadOnlyList<T> rows, Func<T, RetrievalRecordKey?> recordOf, string? callerObjectId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Document search trims by document.");
}
