using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.RecordMatching;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.Contract.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 — a real BFF host for the operator surfaces this task gates:
/// <c>/api/spe/**</c> (SPE admin, business-unit tenant scope), <c>/api/admin/record-matching/**</c> and the
/// other groups behind the "SystemAdmin" policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a real host.</b> The defects closed here are about WHETHER a gate runs on a route (a filter that
/// never saw the configId; a group with a bare <c>RequireAuthorization()</c>), and
/// <c>AddEndpointFilter</c> leaves no endpoint metadata to reflect over (see
/// <c>SpeAdminContainerItemRouteGateTests</c>). Only a request through the real pipeline observes it.
/// </para>
/// <para>
/// <b>The caller.</b> Chosen per request by headers: <see cref="RolesHeader"/> (app roles, or
/// <see cref="NoRoles"/> for a signed-in caller holding none) and <see cref="ScopesHeader"/> (the delegated
/// scope claim). No roles header = anonymous. The oid is fixed (<see cref="CallerOid"/>); which business
/// unit it belongs to is DATA in <see cref="Dataverse"/>, so a test states the caller's unit by seeding a
/// <c>systemusers</c> row.
/// </para>
/// <para>
/// <b>Dataverse</b> is substituted at the <see cref="DataverseWebApiClient"/> class boundary (ADR-038 §4,
/// the <c>DelegationRuleTestFixture</c> pattern — never <c>Mock&lt;HttpMessageHandler&gt;</c>), backed by
/// <see cref="FakeDataverseTables"/>: in-memory rows serialized through the production row types, so the
/// production query → deserialize → decide path is what runs. <b>Record matching</b> is substituted at
/// <see cref="IDataverseIndexSyncService"/> by a recording fake.
/// </para>
/// </remarks>
public class AdminSurfaceHostFixture : WorkspaceTestFixture
{
    /// <summary>
    /// The value of <c>SpeAdmin:PlatformOperatorEnvironment</c> the host is started with, or null for no setting at all
    /// (owner round 49 item 1). This host is a Spaarke-operated environment (dev) — <c>"true"</c>; the customer-environment
    /// and unmarked hosts override it.
    /// </summary>
    protected virtual string? PlatformOperatorEnvironmentSetting => "true";
    internal const string RolesHeader = "X-Test-App-Roles";
    internal const string ScopesHeader = "X-Test-Scopes";

    /// <summary>Overrides the caller's <c>oid</c> (default <see cref="CallerOid"/>) — a second administrator.</summary>
    internal const string OidHeader = "X-Test-Oid";
    internal const string NoRoles = "(none)";
    internal const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

    /// <summary>The caller's Entra object id (the <c>oid</c> claim).</summary>
    public static readonly Guid CallerOid = Guid.Parse("0b7d1f60-9c3a-4d21-8f5e-2a6b7c8d9e01");

    public FakeDataverseTables Dataverse { get; } = new();

    /// <summary>The tenant the SPE admin tests seed on their environments (<c>sprk_tenantid</c>) — the BFF's own here.</summary>
    internal const string SeededTenant = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// The SPE environment a config must be linked to for the BFF to act on it (master bb8ba7251's tenant guard: the config's
    /// environment tenant must be the BFF's own). Seeded by the SPE admin tests that drive the admin plane to Graph.
    /// </summary>
    internal static readonly Guid BffEnvironmentId = Guid.Parse("e0000000-0000-0000-0000-0000000000bf");

    /// <summary>The <see cref="BffEnvironmentId"/> row, carrying <see cref="SeededTenant"/>.</summary>
    internal static Dictionary<string, object?> BffEnvironmentRow() => new()
    {
        ["sprk_speenvironmentid"] = BffEnvironmentId,
        ["sprk_name"] = "BFF tenant",
        ["sprk_tenantid"] = SeededTenant,
    };

    /// <summary>The BFF's app-only Graph client is the fake Graph; delegated clients are not used by these hosts.</summary>
    private sealed class FakeGraphClientFactory(GraphWireMockFixture graph) : IGraphClientFactory
    {
        public Microsoft.Graph.GraphServiceClient ForApp() => graph.CreateGraphClient();

        public Task<Microsoft.Graph.GraphServiceClient> ForUserAsync(Microsoft.AspNetCore.Http.HttpContext ctx, CancellationToken ct = default) =>
            Task.FromResult(graph.CreateGraphClient());

        public Task<Microsoft.Graph.GraphServiceClient> ForUserBetaAsync(Microsoft.AspNetCore.Http.HttpContext ctx, CancellationToken ct = default) =>
            Task.FromResult(graph.CreateGraphClient());
    }

    public RecordingIndexSyncService IndexSync { get; } = new();

    /// <summary>
    /// A fake Microsoft Graph (WireMock) for the SPE admin plane (task 165, owner round 20). The BFF's app-only client is
    /// pointed at it for every config: the REAL <see cref="SpeAdminGraphService"/> sends its requests
    /// here — real requests, real response mapping, no credentials (ADR-038 §4 HTTP-fake boundary).
    /// </summary>
    public GraphWireMockFixture Graph { get; } = new();

    /// <summary>
    /// Every log line the host writes (task 165, owner round 35 item 2: a refused container is logged with its reason —
    /// <c>unbound</c> among them — while the caller sees one uniform 404, so the reason is observable only here).
    /// </summary>
    public RecordingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (PlatformOperatorEnvironmentSetting is { } marker)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpeAdmin:PlatformOperatorEnvironment"] = marker,
            }));
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AdminSurfaceAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminSurfaceAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AdminSurfaceAuthHandler>(
                AdminSurfaceAuthHandler.SchemeName, _ => { });

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(Dataverse.CreateClient());

            // Registered only when DocumentIntelligence:RecordMatchingEnabled is true (it is, in the base
            // host configuration), so the routes are mapped; the recording fake replaces the real service.
            services.RemoveAll<IDataverseIndexSyncService>();
            services.AddSingleton<IDataverseIndexSyncService>(IndexSync);

            services.AddSingleton<ILoggerProvider>(Logs);

            // Batch-4 integration (master bb8ba7251 x task 165): SPE Admin runs as the BFF's OWN app-only Graph client
            // (through SpeContainerOwnershipGuard since T227d) for every config, so this host points that one client at the fake Graph. The
            // tenant guard compares a config's environment tenant with the BFF's, so the BFF tenant here is the one the
            // tests seed on their environments (SeededTenant).
            services.RemoveAll<SpeAdminGraphService>();
            services.AddSingleton(sp => new SpeAdminGraphService(
                sp.GetRequiredService<DataverseWebApiClient>(),
                new ConfigurationBuilder()
                    .AddConfiguration(sp.GetRequiredService<IConfiguration>())
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["TENANT_ID"] = SeededTenant })
                    .Build(),
                sp.GetRequiredService<ILogger<SpeAdminGraphService>>(),
                new FakeGraphClientFactory(Graph),
                // T227d: every container-scoped call asks the ownership guard; these tests are about business-unit scope,
                // so the guard admits every container (tests/integration/tenant covers the refusals).
                TestSpeOwnership.AllowAll(new FakeGraphClientFactory(Graph))));
        });
    }

    /// <summary>A signed-in caller holding exactly these app roles (none by default) and optional scopes.</summary>
    public HttpClient CreateCaller(string[]? roles = null, string? scopes = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(RolesHeader, roles is { Length: > 0 } ? string.Join(",", roles) : NoRoles);
        if (scopes is not null)
        {
            client.DefaultRequestHeaders.Add(ScopesHeader, scopes);
        }

        return client;
    }

    /// <summary>A caller who is not signed in.</summary>
    public HttpClient CreateAnonymous() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>The live singleton the bulk endpoints enqueue into (hosted loop removed by the base host).</summary>
    public BulkOperationService BulkOperations => Services.GetRequiredService<BulkOperationService>();

    private int _bulkProcessorStarted;

    /// <summary>
    /// Starts the bulk processing loop the base host removed (once per host), so a test can drive a bulk request end to
    /// end — enqueue, the job's per-container decisions, the status a caller polls.
    /// </summary>
    public Task StartBulkProcessorOnceAsync() =>
        Interlocked.Exchange(ref _bulkProcessorStarted, 1) == 0
            ? BulkOperations.StartAsync(CancellationToken.None)
            : Task.CompletedTask;

    /// <summary>Clears every row, fault, recorded call, Graph stub and sync-service behaviour.</summary>
    public void Reset()
    {
        Dataverse.Reset();
        IndexSync.Reset();
        Graph.Reset();
        Logs.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A bulk loop started by StartBulkProcessorOnceAsync stops with the host: the container disposes the
            // singleton, and BackgroundService.Dispose cancels its loop.
            Graph.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Authenticates from the <see cref="AdminSurfaceHostFixture"/> headers. No roles header = anonymous.</summary>
internal sealed class AdminSurfaceAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminSurfaceAuth";

    public AdminSurfaceAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AdminSurfaceHostFixture.RolesHeader, out var rolesHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var oid = Request.Headers.TryGetValue(AdminSurfaceHostFixture.OidHeader, out var oidOverride)
            ? oidOverride.ToString()
            : AdminSurfaceHostFixture.CallerOid.ToString("D");
        var claims = new List<Claim>
        {
            new("oid", oid),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, "Admin Surface Test Caller"),
            new("tid", "11111111-2222-3333-4444-555555555555"),
        };

        claims.AddRange(
            rolesHeader.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(role => role != AdminSurfaceHostFixture.NoRoles)
                .Select(role => new Claim("roles", role)));

        if (Request.Headers.TryGetValue(AdminSurfaceHostFixture.ScopesHeader, out var scopes))
        {
            // The claim type the JwtBearer handler maps "scp" to — the type the "SystemAdmin" policy read.
            claims.Add(new Claim(AdminSurfaceHostFixture.ScopeClaimType, scopes.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, "roles");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>
/// In-memory Dataverse tables behind a Moq class mock of <see cref="DataverseWebApiClient"/>
/// (<c>CallBase = false</c>; the five data methods are virtual). Rows are dictionaries keyed by OData
/// attribute name and are round-tripped through JSON into whatever row type the production code asks
/// for, so the private production row shapes are exercised unchanged.
/// </summary>
public sealed class FakeDataverseTables
{
    private readonly ConcurrentDictionary<string, List<Dictionary<string, object?>>> _tables = new();

    /// <summary>Every call, in order.</summary>
    public ConcurrentQueue<DataverseCall> Calls { get; } = new();

    /// <summary>Entity sets whose QueryAsync throws (a simulated Dataverse fault).</summary>
    public ConcurrentDictionary<string, bool> FaultingQueries { get; } = new();

    /// <summary>
    /// Entity sets whose WHOLE-TABLE QueryAsync (no filter) throws while their filtered reads still answer — so a test can
    /// fault only a rule that reads the whole table (task 165, owner round 57 item 3: the container-type rule's own read).
    /// </summary>
    public ConcurrentDictionary<string, bool> FaultingUnfilteredQueries { get; } = new();

    /// <summary>
    /// Ids a RetrieveAsync does not find although QueryAsync still returns the row — a record deleted between the
    /// tenant-scope filter's read and the handler's own read (the race the handlers' not-found paths answer). The value
    /// chooses HOW it is missing: false = the client returns null; true = it throws the Web API's 404.
    /// </summary>
    public ConcurrentDictionary<Guid, bool> RetrieveMisses { get; } = new();

    public void Reset()
    {
        _tables.Clear();
        Calls.Clear();
        FaultingQueries.Clear();
        FaultingUnfilteredQueries.Clear();
        RetrieveMisses.Clear();
    }

    public void Add(string entitySet, Dictionary<string, object?> row) =>
        _tables.GetOrAdd(entitySet, _ => new List<Dictionary<string, object?>>()).Add(row);

    public void FaultQueriesOn(string entitySet) => FaultingQueries[entitySet] = true;

    public void FaultUnfilteredQueriesOn(string entitySet) => FaultingUnfilteredQueries[entitySet] = true;

    public IReadOnlyList<DataverseCall> CallsOn(string entitySet, params string[] operations) =>
        Calls.Where(c => c.EntitySet == entitySet && (operations.Length == 0 || operations.Contains(c.Operation)))
             .ToList();

    internal DataverseWebApiClient CreateClient()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // Takes the managed-identity branch, whose credential is constructed lazily and never
            // authenticates — this client is fully stubbed (same as DelegationRuleTestFixture).
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

        // Positional null! for the two optional credential slots: Moq selects class-proxy ctors exactly.
        var mock = new Mock<DataverseWebApiClient>(
            configuration, NullLogger<DataverseWebApiClient>.Instance, null!, null!)
        { CallBase = false };

        mock.Setup(c => c.QueryAsync<It.IsAnyType>(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(Query));

        mock.Setup(c => c.RetrieveAsync<It.IsAnyType>(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(Retrieve));

        mock.Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns((string set, object _, CancellationToken _) =>
            {
                Calls.Enqueue(new DataverseCall("Create", set, null, null));
                return Task.FromResult(Guid.NewGuid());
            });

        mock.Setup(c => c.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns((string set, Guid id, object _, CancellationToken _) =>
            {
                Calls.Enqueue(new DataverseCall("Update", set, id, null));
                return Task.CompletedTask;
            });

        mock.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string set, Guid id, CancellationToken _) =>
            {
                Calls.Enqueue(new DataverseCall("Delete", set, id, null));
                return Task.CompletedTask;
            });

        return mock.Object;
    }

    private object Query(IInvocation invocation)
    {
        var rowType = invocation.Method.GetGenericArguments()[0];
        var entitySet = (string)invocation.Arguments[0];
        var filter = (string?)invocation.Arguments[1];
        var top = (int?)invocation.Arguments[3];

        Calls.Enqueue(new DataverseCall("Query", entitySet, null, filter));

        if (FaultingQueries.ContainsKey(entitySet)
            || (string.IsNullOrWhiteSpace(filter) && FaultingUnfilteredQueries.ContainsKey(entitySet)))
        {
            throw new HttpRequestException(
                $"Simulated Dataverse fault on {entitySet}.", null, HttpStatusCode.ServiceUnavailable);
        }

        var rows = Rows(entitySet).Where(row => Matches(row, filter));
        if (top is { } limit) rows = rows.Take(limit);

        var listType = typeof(List<>).MakeGenericType(rowType);
        var list = JsonSerializer.Deserialize(JsonSerializer.Serialize(rows.ToList()), listType)!;
        return FromResult(listType, list);
    }

    private object Retrieve(IInvocation invocation)
    {
        var rowType = invocation.Method.GetGenericArguments()[0];
        var entitySet = (string)invocation.Arguments[0];
        var id = (Guid)invocation.Arguments[1];

        Calls.Enqueue(new DataverseCall("Retrieve", entitySet, id, null));

        var key = PrimaryKey(entitySet);
        if (RetrieveMisses.TryGetValue(id, out var throwsNotFound))
        {
            if (throwsNotFound)
            {
                throw new HttpRequestException($"Simulated 404 retrieving {entitySet}({id}).", null, HttpStatusCode.NotFound);
            }

            return FromResult(rowType, null);
        }

        var row = Rows(entitySet).FirstOrDefault(r =>
            r.TryGetValue(key, out var value) && string.Equals(value?.ToString(), id.ToString("D"), StringComparison.OrdinalIgnoreCase));

        var result = row is null ? null : JsonSerializer.Deserialize(JsonSerializer.Serialize(row), rowType);
        return FromResult(rowType, result);
    }

    private IEnumerable<Dictionary<string, object?>> Rows(string entitySet) =>
        _tables.TryGetValue(entitySet, out var rows) ? rows.ToList() : Enumerable.Empty<Dictionary<string, object?>>();

    /// <summary>"sprk_specontainertypeconfigs" → "sprk_specontainertypeconfigid".</summary>
    private static string PrimaryKey(string entitySet) =>
        (entitySet.EndsWith("s", StringComparison.Ordinal) ? entitySet[..^1] : entitySet) + "id";

    /// <summary>
    /// Supports exactly the filters the code under test sends: null, or <c>field eq value</c> clauses
    /// joined by <c>and</c>. Anything else fails loudly rather than matching silently.
    /// </summary>
    private static bool Matches(Dictionary<string, object?> row, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        foreach (var clause in filter.Split(" and ", StringSplitOptions.TrimEntries))
        {
            var parts = clause.Split(" eq ", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Contains('(') || parts[0].Contains(' '))
            {
                throw new NotSupportedException($"FakeDataverseTables does not support the filter clause '{clause}'.");
            }

            var expected = parts[1].Trim('\'');
            if (!row.TryGetValue(parts[0], out var actual)
                || !string.Equals(actual?.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static object FromResult(Type resultType, object? value) =>
        typeof(Task).GetMethod(nameof(Task.FromResult), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(resultType)
            .Invoke(null, new[] { value })!;
}

public sealed record DataverseCall(string Operation, string EntitySet, Guid? Id, string? Filter);

/// <summary>A logger provider that records every formatted line (category, level, message) the host writes.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();

    public IReadOnlyList<LogLine> Lines => _lines.ToArray();

    public void Clear() => _lines.Clear();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    public sealed record LogLine(string Category, LogLevel Level, string Message);

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogLine> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lines.Enqueue(new LogLine(category, logLevel, formatter(state, exception)));
            }
        }
    }
}

/// <summary>Records every call; returns canned results, or throws <see cref="ThrowOnCall"/> when set.</summary>
public sealed class RecordingIndexSyncService : IDataverseIndexSyncService
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public Exception? ThrowOnCall { get; set; }

    public void Reset()
    {
        Calls.Clear();
        ThrowOnCall = null;
    }

    public Task<IndexSyncResult> BulkSyncAsync(IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(nameof(BulkSyncAsync));
        if (ThrowOnCall is { } ex) throw ex;
        return Task.FromResult(new IndexSyncResult { Success = true, RecordsProcessed = 3, RecordsIndexed = 3 });
    }

    public Task<IndexSyncResult> IncrementalSyncAsync(DateTimeOffset since, IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(nameof(IncrementalSyncAsync));
        if (ThrowOnCall is { } ex) throw ex;
        return Task.FromResult(new IndexSyncResult { Success = true, RecordsProcessed = 1, RecordsIndexed = 1 });
    }

    public Task SyncRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(nameof(SyncRecordAsync));
        return Task.CompletedTask;
    }

    public Task RemoveRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(nameof(RemoveRecordAsync));
        return Task.CompletedTask;
    }

    public Task<IndexSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(nameof(GetStatusAsync));
        if (ThrowOnCall is { } ex) throw ex;
        return Task.FromResult(new IndexSyncStatus { IndexName = "spaarke-records-test", DocumentCount = 3, IsHealthy = true });
    }
}

/// <summary>
/// A CUSTOMER environment's host (owner round 49 item 1): <c>SpeAdmin:PlatformOperatorEnvironment</c> is <c>false</c> — the
/// tenant-wide and type-wide SPE admin routes are refused even for its root-unit admin.
/// </summary>
public sealed class AdminSurfaceCustomerEnvironmentHostFixture : AdminSurfaceHostFixture
{
    protected override string? PlatformOperatorEnvironmentSetting => "false";
}

/// <summary>A host with NO <c>SpeAdmin:PlatformOperatorEnvironment</c> setting at all — the default is false (fail closed).</summary>
public sealed class AdminSurfaceUnmarkedHostFixture : AdminSurfaceHostFixture
{
    protected override string? PlatformOperatorEnvironmentSetting => null;
}

/// <summary>A host whose marker is not a boolean — it must not start (the options are validated on start).</summary>
public sealed class AdminSurfaceMalformedMarkerHostFixture : AdminSurfaceHostFixture
{
    protected override string? PlatformOperatorEnvironmentSetting => "yes";
}