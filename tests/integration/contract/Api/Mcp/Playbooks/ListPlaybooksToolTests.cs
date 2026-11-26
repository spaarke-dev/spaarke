// CONTRACT — spaarke_list_playbooks over /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 024, FR-04, D-209, NFR-02, ADR-028 A10).
// KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints on a real Kestrel listener behind a real JwtBearer scheme, serving the production
// ListPlaybooksTool, next to the REAL playbook REST routes (MapPlaybookEndpoints) in the same host, so "the tool's ids are the union of the
// two REST lists for the same caller" is compared over the wire, with the same token. Behind both: the REAL shared method
// (PlaybookEndpoints.ListPlaybooksForCallerAsync) and caller resolution (PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync).
// SUBSTITUTED (module boundaries, ADR-038): the IPlaybookService interface (the Dataverse reads) and the WhoAmI probe.
// NOT HERE: A10 token refusals (tests/integration/auth/Mcp, route-wide); the method's own parity with the routes
// (tests/integration/auth/Ai/PlaybookListSharedMethodTests.cs); the routes' bytes (PlaybookListRoutesFrozenBytesTests.cs).

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
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;

public sealed class ListPlaybooksToolTests
{
    private const int MaxBytes = 64 * 1024;

    private static readonly Guid MineOnly = Guid.Parse("0024dddd-0000-4000-8000-000000000001");
    private static readonly Guid MineAndPublic = Guid.Parse("0024dddd-0000-4000-8000-000000000002");
    private static readonly Guid PublicOnly = Guid.Parse("0024dddd-0000-4000-8000-000000000003");

    // ---- tools/list ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShowsTheToolReadOnlyAndModelVisible_WithAJpsQualifiedUseForNotForDescription_AndAClosedSchema()
    {
        await using var host = await ListPlaybooksHost.StartAsync();

        var list = await host.RpcAsync("tools/list", new { });

        var tool = list.GetProperty("tools").EnumerateArray().Should().ContainSingle().Subject;
        tool.GetProperty("name").GetString().Should().Be("spaarke_list_playbooks");
        tool.GetProperty("title").GetString().Should().Be("List my JPS playbooks");
        var annotations = tool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
        tool.TryGetProperty("_meta", out _).Should().BeFalse("no widget and no app-only visibility: the model calls it");
        var description = tool.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1024);
        description.Should().Contain("Use for:").And.Contain("Not for:").And.Contain("JPS playbooks (AI analysis recipes)");
        var schema = tool.GetProperty("inputSchema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("nameFilter", "page");
    }

    // ---- the union --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("lease", 2, "?page=2&nameFilter=lease")]
    public async Task List_IdsAreTheUnionOfTheTwoRestLists_ForTheSameCallerAndQuery_EachOnce(string? nameFilter, int? page, string restQuery)
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        host.Owned([Summary(MineOnly), Summary(MineAndPublic)]);
        host.Public([Summary(MineAndPublic), Summary(PublicOnly)]);
        var restOwned = await host.RestIdsAsync("/api/ai/playbooks" + restQuery);
        var restPublic = await host.RestIdsAsync("/api/ai/playbooks/public" + restQuery);

        var result = await host.CallAsync(new Dictionary<string, object?> { ["nameFilter"] = nameFilter, ["page"] = page });

        IsError(result).Should().BeFalse(result.GetRawText());
        var items = Items(result);
        items.Select(i => i.Id).Should().BeEquivalentTo(restOwned.Union(restPublic), "FR-04: the union of the two REST lists");
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items.Should().Equal(
            new[] { (MineOnly, "owned"), (MineAndPublic, "owned"), (PublicOnly, "public") },
            "owned first, then the public rows not already listed; a playbook in both lists is the caller's own");
        host.Queries.Should().OnlyContain(q => q.Page == (page ?? 1) && q.PageSize == 20 && q.NameFilter == nameFilter,
            "the tool asks what the routes ask for the same page and filter, at the routes' page size");
    }

    [Fact]
    public async Task List_APlaybookInBothLists_AppearsOnce_InTheRowsAndInTheText()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        host.Owned([Summary(MineAndPublic, "Shared NDA review")]);
        host.Public([Summary(MineAndPublic, "Shared NDA review")]);

        var result = await host.CallAsync(new Dictionary<string, object?>());

        Items(result).Should().ContainSingle().Which.Should().Be((MineAndPublic, "owned"));
        var text = Text(result);
        text.Should().StartWith("1 JPS playbook(s) on page 1: 1 owned, 0 public.");
        CountOf(text, MineAndPublic.ToString("D")).Should().Be(1);
    }

    // ---- no caller / no systemuser (ADR-028 A10) ---------------------------------------------------------------------

    [Fact]
    public async Task NoCallerContext_IsUserContextRequired_AndNothingIsRead()
    {
        var result = await ListPlaybooksTool.RunAsync(null, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        body.GetProperty("status").GetInt32().Should().Be(401);
    }

    [Fact]
    public async Task ACallerWithNoResolvableSystemUser_IsUserContextRequired_AndThePublicListIsNotRead()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        host.Probe.SystemUserId = null;
        host.Public([Summary(PublicOnly)]);

        var result = await host.CallAsync(new Dictionary<string, object?>());

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Queries.Should().BeEmpty("A10 serves only callers that resolve to a systemuser; nothing is listed for this one");
    }

    // ---- failures and arguments ----------------------------------------------------------------------------------------

    [Fact]
    public async Task APublicListFault_IsAnIsErrorWithTheRoutesStatus_NeverAPartialList()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        host.Owned([Summary(MineOnly)]);
        host.Service.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down: secret-internal-detail"));
        using var rest = await host.GetAsync("/api/ai/playbooks/public");

        var result = await host.CallAsync(new Dictionary<string, object?>());

        IsError(result).Should().BeTrue();
        var body = result.GetProperty("structuredContent");
        body.GetProperty("reasonCode").GetString().Should().Be(PlaybookListResult.ListFailedReasonCode);
        body.GetProperty("status").GetInt32().Should().Be((int)rest.StatusCode).And.Be(500);
        body.TryGetProperty("items", out _).Should().BeFalse();
        result.GetRawText().Should().NotContain("secret-internal-detail");
    }

    [Theory]
    [InlineData("{\"category\":\"all\"}")]
    [InlineData("{\"page\":0}")]
    [InlineData("{\"page\":1001}")]
    [InlineData("{\"page\":\"two\"}")]
    [InlineData("{\"nameFilter\":42}")]
    public async Task AnInvalidArgument_IsAnIsErrorThatNamesTheFix_AndNothingIsRead(string arguments)
    {
        await using var host = await ListPlaybooksHost.StartAsync();

        var result = await host.CallAsync(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!);

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(ListPlaybooksTool.InvalidArgumentReasonCode);
        result.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(400);
        host.Queries.Should().BeEmpty();
    }

    // ---- model-visible text (ADR-015) and size (NFR-02) ----------------------------------------------------------------

    [Fact]
    public async Task Rows_CarryOnlyIdNameShortDescriptionOwnerKindAndFormat_AndTheTextNoDescription()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        var longDescription = "Reviews the lease. " + new string('d', 400);
        host.Owned([Summary(MineOnly, "Lease abstract", longDescription)]);
        host.Public([]);

        var result = await host.CallAsync(new Dictionary<string, object?>());

        var row = result.GetProperty("structuredContent").GetProperty("items")[0];
        row.EnumerateObject().Select(p => p.Name).Should().Equal("id", "name", "description", "ownerKind", "format");
        row.GetProperty("format").GetString().Should().Be("JPS");
        var description = row.GetProperty("description").GetString()!;
        description.Length.Should().Be(ListPlaybooksTool.MaxDescriptionLength);
        description.Should().StartWith("Reviews the lease. ").And.EndWith("…");
        Text(result).Should().Contain("Lease abstract").And.NotContain("Reviews the lease");
    }

    [Fact]
    public async Task AtTheListMaximum_RealisticLongText_EveryRowIsReturned_Under64KiB()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        host.Owned(Many(20, 'o', new string('n', 200), new string('d', 2000)));
        host.Public(Many(20, 'p', new string('n', 200), new string('d', 2000)));

        var result = await host.CallAsync(new Dictionary<string, object?>());

        IsError(result).Should().BeFalse(result.GetRawText());
        Items(result).Should().HaveCount(40, "20 owned and 20 public, the routes' page size each");
        result.GetProperty("structuredContent").GetProperty("truncated").GetBoolean().Should().BeFalse();
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(MaxBytes);
    }

    [Fact]
    public async Task AtTheListMaximum_EscapeHeavyText_IsASuccessUnder64KiB_AndSaysTheListWasCut()
    {
        await using var host = await ListPlaybooksHost.StartAsync();
        string Heavy(int characters) => string.Concat(Enumerable.Repeat("\"é<", characters / 3));
        host.Owned(Many(20, 'o', Heavy(850), Heavy(2000)));
        host.Public(Many(20, 'p', Heavy(850), Heavy(2000)));

        var result = await host.CallAsync(new Dictionary<string, object?>());

        IsError(result).Should().BeFalse(result.GetRawText());
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(MaxBytes);
        Items(result).Should().NotBeEmpty();
        result.GetProperty("structuredContent").GetProperty("truncated").GetBoolean().Should().BeTrue();
        Text(result).Should().Contain("narrow it with nameFilter");
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static PlaybookSummary Summary(Guid id, string name = "Playbook", string? description = "A recipe") =>
        new() { Id = id, Name = name, Description = description, ModifiedOn = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc) };

    private static PlaybookSummary[] Many(int count, char kind, string name, string description) =>
        Enumerable.Range(0, count).Select(i => Summary(Guid.Parse($"0024{(kind == 'o' ? "eeee" : "ffff")}-0000-4000-8000-{i:D12}"), name, description)).ToArray();

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static List<(Guid Id, string OwnerKind)> Items(JsonElement result) =>
        result.GetProperty("structuredContent").GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("ownerKind").GetString()!)).ToList();

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}

/// <summary>
/// TESTS ONLY. The production MCP module serving <see cref="ListPlaybooksTool"/> and the REAL playbook REST routes on one Kestrel host behind
/// one JwtBearer scheme; the playbook service and the WhoAmI probe are the only substitutes.
/// </summary>
internal sealed class ListPlaybooksHost : IAsyncDisposable
{
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _token = McpTestHost.MintToken(Guid.Parse("0024aaaa-0000-4000-8000-0000000000c1"));

    private ListPlaybooksHost(WebApplication app, HttpClient client, Mock<IPlaybookService> service, PlaybookRouteAuthorizationContractTests.RecordingSystemUserProbe probe)
    {
        _app = app;
        _client = client;
        Service = service;
        Probe = probe;
    }

    public Mock<IPlaybookService> Service { get; }

    public PlaybookRouteAuthorizationContractTests.RecordingSystemUserProbe Probe { get; }

    /// <summary>Every query the playbook service was asked, in order (both lists, both hosts).</summary>
    public IEnumerable<PlaybookQueryParameters> Queries =>
        Service.Invocations.SelectMany(i => i.Arguments.OfType<PlaybookQueryParameters>());

    public static async Task<ListPlaybooksHost> StartAsync()
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

        var service = new Mock<IPlaybookService>(MockBehavior.Strict);
        var probe = new PlaybookRouteAuthorizationContractTests.RecordingSystemUserProbe();

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
        builder.Services.AddMcpModule(builder.Configuration, [typeof(ListPlaybooksTool)]);

        // The module boundaries: the playbook service (its Dataverse reads) and the WhoAmI probe.
        builder.Services.AddSingleton(service.Object);
        builder.Services.AddSingleton<CallerRecordAccessProbe>(probe);
        // Handler parameter types of the playbook routes these tests never call (the mapper needs them to be services).
        builder.Services.AddSingleton(new Mock<IPlaybookSharingService>(MockBehavior.Strict).Object);
        builder.Services.AddSingleton(new Mock<IPlaybookLookupService>(MockBehavior.Strict).Object);
        builder.Services.AddSingleton(new Mock<INodeService>(MockBehavior.Strict).Object);
        builder.Services.AddSingleton(new Mock<IEndpointResponseCache>(MockBehavior.Strict).Object);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        app.MapPlaybookEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new ListPlaybooksHost(app, new HttpClient { BaseAddress = new Uri(address) }, service, probe);
    }

    /// <summary>The caller's owned list (any page, any filter) is <paramref name="items"/>, 20 per page.</summary>
    public void Owned(PlaybookSummary[] items) =>
        Service.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, PlaybookQueryParameters q, CancellationToken _) => Page(items, q));

    /// <summary>The public list (any page, any filter) is <paramref name="items"/>, 20 per page.</summary>
    public void Public(PlaybookSummary[] items) =>
        Service.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookQueryParameters q, CancellationToken _) => Page(items, q));

    private static PlaybookListResponse Page(PlaybookSummary[] items, PlaybookQueryParameters q) =>
        new() { Items = items, TotalCount = items.Length, Page = q.Page, PageSize = q.GetNormalizedPageSize() };

    /// <summary>One JSON-RPC call; returns the <c>result</c> (the transport answers JSON or an SSE stream).</summary>
    public async Task<JsonElement> RpcAsync(string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

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

    /// <summary><c>tools/call</c> of the tool; null arguments are left out.</summary>
    public Task<JsonElement> CallAsync<T>(IDictionary<string, T> arguments) =>
        RpcAsync("tools/call", new
        {
            name = ListPlaybooksTool.Name,
            arguments = arguments.Where(a => a.Value is not null).ToDictionary(a => a.Key, a => (object?)a.Value),
        });

    /// <summary>A REST GET with the same token.</summary>
    public Task<HttpResponseMessage> GetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return _client.SendAsync(request);
    }

    /// <summary>The ids of a successful REST list.</summary>
    public async Task<List<Guid>> RestIdsAsync(string path)
    {
        using var response = await GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
