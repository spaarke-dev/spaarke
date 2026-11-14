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
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 022). The task-020 host shape (<see cref="McpTestHost"/>: the PRODUCTION
/// <c>AddMcpModule</c>, <c>MapMcpEndpoints</c> and <c>RateLimitingModule</c> on a real Kestrel listener behind a real JwtBearer scheme), serving
/// exactly the two production decision tools, <see cref="ShowDecisionsTool"/> and <see cref="ListDecisionPageTool"/>, over a
/// <see cref="DecideLaneDouble"/> (the <see cref="IDecisionCapability"/> boundary) and a capturing logger.
/// </summary>
internal sealed class DecisionToolsTestHost : IAsyncDisposable
{
    internal const string EnvironmentUrl = "https://spaarke-test.crm.dynamics.com";

    private const string Issuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("decision-tools-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;

    private DecisionToolsTestHost(WebApplication app, HttpClient client, CapturingLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Logs = logs;
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public static async Task<DecisionToolsTestHost> StartAsync(IDecisionCapability capability)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var logs = new CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(capability);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<DataverseOptions>(o => o.EnvironmentUrl = EnvironmentUrl);
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
        builder.Services.AddMcpModule(builder.Configuration, new[] { typeof(ShowDecisionsTool), typeof(ListDecisionPageTool) });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new DecisionToolsTestHost(app, new HttpClient { BaseAddress = new Uri(address) }, logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>A valid workforce-style token (the <see cref="McpTestHost.MintToken"/> shape) for the caller <paramref name="oid"/>.</summary>
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
        var result = await RpcAsync("tools/list", new { }, token);
        return result.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t);
    }

    /// <summary>One <c>tools/call</c>: the JSON-RPC <c>result</c> (an <c>isError</c> result included; a JSON-RPC error throws).</summary>
    public Task<JsonElement> CallAsync(string tool, object? arguments, string token) =>
        RpcAsync("tools/call", arguments is null ? new { name = tool } : new { name = tool, arguments }, token);

    private async Task<JsonElement> RpcAsync(string method, object parameters, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

    /// <summary>Every log entry written through the host's logging, with its structured state.</summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        public sealed record Entry(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

        private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(p => p.Key, p => p.Value)
                    : new Dictionary<string, object?>();
                entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), values));
            }
        }
    }
}

/// <summary>
/// A hand-written boundary double of <see cref="IDecisionCapability"/> over a seeded Decide lane. Pages go through the PRODUCTION
/// <see cref="DecideLanePaging.Page"/> and <see cref="DecideCursor"/> (Console order, the cursor, its stale-cursor rule); the privilege flag
/// is set from <see cref="Privileged"/>, as task 018's derivation sets it. Failures can be scripted per read.
/// </summary>
internal sealed class DecideLaneDouble : IDecisionCapability
{
    private int _laneReads;
    private int _pageReads;

    public DecideLaneDouble(DecideLaneSeed seed)
    {
        Groups = seed.Groups.ToList();
        Extras = seed.Extras;
        Privileged = seed.Privileged;
        DoLane = seed.DoLane;
    }

    /// <summary>The Decide lane's groups in Console order (mutable, to make a cursor stale).</summary>
    public List<WorklistGroup> Groups { get; }

    public IReadOnlyDictionary<Guid, WorklistItemExtras> Extras { get; }

    public IReadOnlySet<Guid> Privileged { get; }

    public IReadOnlyList<WorklistGroup> DoLane { get; }

    /// <summary>When set, <see cref="ListDecideAsync"/> answers it.</summary>
    public SignalReadResult? LaneFailure { get; set; }

    /// <summary>When set, <see cref="ListDecidePageAsync"/> answers it.</summary>
    public DecidePageResult? PageFailure { get; set; }

    /// <summary>The principal each capability read was handed, in call order (null when a call passed none).</summary>
    public ConcurrentQueue<ClaimsPrincipal?> Users { get; } = new();

    public int LaneReads => Volatile.Read(ref _laneReads);

    public int PageReads => Volatile.Read(ref _pageReads);

    public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes)
    {
        Interlocked.Increment(ref _laneReads);
        Users.Enqueue(user);
        return Task.FromResult(LaneFailure ?? new SignalReadResult(
            SignalReadOutcome.Ok,
            new WorklistResponse("lanes", [new WorklistLaneGroups("Decide", Groups.ToList()), new WorklistLaneGroups("Do", DoLane)])));
    }

    public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct)
    {
        Interlocked.Increment(ref _pageReads);
        Users.Enqueue(user);
        if (PageFailure is not null)
        {
            return Task.FromResult(PageFailure);
        }

        DecideCursor? after = null;
        if (!string.IsNullOrEmpty(cursor)) // the capability's own rule (DecisionCapability.ListDecidePageAsync): "" is page 1
        {
            if (!DecideCursor.TryDecode(cursor, out var decoded))
            {
                return Task.FromResult(DecidePageResult.CursorInvalid);
            }

            after = decoded;
        }

        var (items, cores, next) = DecideLanePaging.Page(Groups, Extras, after);
        var judged = items.Select(i => i with { PrivilegeFlagged = Privileged.Contains(i.SignalId) }).ToList();
        return Task.FromResult(DecidePageResult.Ok(judged, cores, next));
    }

    public Task<DecisionPlanReadResult> GetPlanAsync(Microsoft.AspNetCore.Http.HttpContext? http, Guid signalId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SuppressionReadResult> GetSuppressionAsync(Microsoft.AspNetCore.Http.HttpContext? http, Guid signalId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<DecisionCommitResult> CommitAsync(
        Microsoft.AspNetCore.Http.HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) =>
        throw new NotSupportedException();
}

/// <summary>
/// The seeded lane of task 022's tests: 200 Decide-lane items in Console order over four matters, P (10 items, all privileged; its name and
/// number carry <see cref="Marker"/>), then A (60), B (65) and C (65). In A, items 2, 5 and 7 are privileged with <see cref="Marker"/> in
/// their title, subject name and reason; item 7's subject is a communication (a Privileged communication flags it, task 018). Work types
/// cycle over the global index i: i % 5 == 0 billing (invoiceReview), 1 askOutsideCounsel, 2 approveOrRebudget, 3 chaseReply, 4 none
/// ("other"): 40 of each. One Do-lane item, which no Decide count includes. Visible items' subject names are "Subject {matter}-{n}".
/// </summary>
internal sealed record DecideLaneSeed(
    IReadOnlyList<WorklistGroup> Groups,
    IReadOnlyDictionary<Guid, WorklistItemExtras> Extras,
    IReadOnlySet<Guid> Privileged,
    IReadOnlyList<WorklistGroup> DoLane,
    IReadOnlyList<Guid> ConsoleOrder,
    Guid PrivilegedMatterId,
    Guid CommunicationItemId)
{
    /// <summary>The unique marker string in every privileged item's text and in the privileged-only matter's name and number. Short, and
    /// at the start of each text, so it survives the slim row's clipping (the paging tool must still carry it).</summary>
    public const string Marker = "ZQX7731";

    /// <summary>Page 1's privileged items: all of P (10) and A's items 2, 5 and 7.</summary>
    public const int PrivilegedOnPageOne = 13;

    private static readonly string?[] WorkTypeCycle = [null, "askOutsideCounsel", "approveOrRebudget", "chaseReply", null];

    /// <summary>The one extra privileged item <see cref="Build"/> adds past page 1 (Charlie matter, item 10: Console row 145).</summary>
    public const int LatePrivilegedIndexInC = 10;

    /// <param name="latePrivileged">Also flags item <see cref="LatePrivilegedIndexInC"/> of C (a row past page 1; privilege is derived per page, so the first page cannot count it).</param>
    public static DecideLaneSeed Build(bool latePrivileged = false)
    {
        var start = DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var extras = new Dictionary<Guid, WorklistItemExtras>();
        var privileged = new HashSet<Guid>();
        var order = new List<Guid>();
        var global = 0;
        var communicationItem = Guid.Empty;

        WorklistGroup Group(string code, string name, string number, int count, Func<int, bool> isPrivileged)
        {
            var core = new WorklistCoreRecord("sprk_matter", Guid.NewGuid(), "Matter", name, number);
            var items = new List<WorklistItemRow>();
            for (var n = 0; n < count; n++, global++)
            {
                var id = Guid.NewGuid();
                var flagged = isPrivileged(n);
                var billing = global % 5 == 0;
                var communication = code == "A" && n == 7;
                var row = new WorklistItemRow(
                    SignalId: id,
                    Lane: "Decide",
                    Status: "Active",
                    Title: flagged ? $"{Marker} title {code}-{n}" : $"Title {code}-{n}",
                    SubjectName: flagged ? $"{Marker} subject {code}-{n}" : $"Subject {code}-{n}",
                    RuleShortName: flagged ? $"{Marker} reason" : "Budget gap",
                    WorkType: billing ? null : WorkTypeCycle[global % 5],
                    PastDueWording: "late",
                    RaisedOn: start.AddMinutes(global),
                    DueDate: null,
                    EvidenceRefs: null,
                    PolicyVersionId: null,
                    SubjectRecordType: communication ? "sprk_communication" : null,
                    SubjectRecordId: communication ? Guid.NewGuid() : null,
                    InvoiceReview: billing ? new WorklistInvoiceReview(Guid.NewGuid(), n % 2 == 0 ? Guid.NewGuid() : null) : null);
                items.Add(row);
                extras[id] = new WorklistItemExtras(new RankKey(1, false, start.AddMinutes(global)), WorkItemPrivilegeSources.None);
                order.Add(id);
                if (flagged)
                {
                    privileged.Add(id);
                }

                if (communication)
                {
                    communicationItem = id;
                }
            }

            return new WorklistGroup(core, false, items);
        }

        var p = Group("P", $"{Marker} privileged matter", $"{Marker}-NUM", 10, _ => true);
        var a = Group("A", "Alpha matter", "MAT-0001", 60, n => n is 2 or 5 or 7);
        var b = Group("B", "Bravo matter", "MAT-0002", 65, _ => false);
        var c = Group("C", "Charlie matter", "MAT-0003", 65, n => latePrivileged && n == LatePrivilegedIndexInC);

        var doRow = new WorklistItemRow(
            Guid.NewGuid(), "Do", "Active", "Do title", "Do subject", "Do reason", "chaseReply", "late", start, null, null, null);
        var doLane = new[] { new WorklistGroup(b.Core, false, [doRow]) };

        return new DecideLaneSeed([p, a, b, c], extras, privileged, doLane, order, p.Core!.RecordId, communicationItem);
    }
}
