// SHARED WORLD — spaarke_run_playbook / spaarke_get_playbook_run and the detached caller run (spaarkeai-mcp-bff-api-r2 task 030).
// KEEP path: contract (shared by tests/integration/contract/Api/Mcp/Playbooks/PlaybookRunToolsContractTests.cs,
// tests/integration/auth/Mcp/Playbooks/PlaybookRunToolsAuthTests.cs and tests/integration/auth/Ai/DetachedPlaybookRunnerTests.cs).
//
// WHAT IS REAL: DetachedPlaybookRunner (the one detached-run helper), task 007's PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync with
// the REAL AuthorizationService / OperationAccessRule / external narrowing over PlaybookAuthHost's substitutes, McpIdempotency, the
// tools' RunAsync, HttpContextAccessor, HandoffUrlBuilder. SUBSTITUTED at the boundaries only: the engine (IPlaybookOrchestrationService:
// a scripted event stream and a stored-status table), the analysis create (IAnalysisStartAi, task 037a: Created by default), the Graph and
// Dataverse OBO factories (the pre-warm), the access-data source and
// WhoAmI probe (PlaybookAuthHost), the clock (FakeTimeProvider) and the host lifetime.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Ai.PlaybookRuns;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;

/// <summary>One test's world for a detached playbook run: the real runner and tools over boundary substitutes (see the file header).</summary>
internal sealed class PlaybookRunWorld : IAsyncDisposable
{
    public const string EnvironmentUrl = "https://spaarke-test.crm.dynamics.com";
    public const string CallerOid = "6f0c1a52-0000-4000-8000-000000000030";
    public const string OtherOid = "6f0c1a52-0000-4000-8000-000000000031";

    /// <summary>A marker inside the caller's bearer token: it must never reach a log, a cache entry, a record or a tool result.</summary>
    public const string TokenSecret = "tok-SECRET-030-9f1c";

    public const string Bearer = "Bearer caller-" + TokenSecret;

    private readonly ServiceProvider _root;

    private PlaybookRunWorld(PlaybookAuthHost host)
    {
        Host = host;
        Engine.Setup(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookRunRequest request, HttpContext http, CancellationToken ct) =>
            {
                var accessor = http.RequestServices?.GetService<IHttpContextAccessor>();
                Calls.Enqueue(new EngineCall(
                    request, http, http.User, http.Request.Headers.Authorization.ToString(), http.TraceIdentifier,
                    accessor?.HttpContext, ct,
                    http.RequestServices?.GetService<PlaybookRunRecordStore>()?.LinkedAnalysis));
                return Script(request, ct);
            });
        Engine.Setup(e => e.GetRunStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid runId, CancellationToken _) => StoredRuns.TryGetValue(runId, out var status) ? status : null);
        Analyses.Setup(a => a.StartAsCallerAsync(It.IsAny<HttpContext?>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisStartResult.Created(AnalysisId, 0));
        // Task 037b: any other OBO read answers as Dataverse answers a row the caller cannot see (404), unless a test sets it up.
        DataverseUser.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, "DATAVERSE_NOT_FOUND", "not found"));
        DataverseUser.Setup(d => d.GetAsync("WhoAmI", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, null));

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));

        // Task 007's real authorization stack over the host's substitutes (as PlaybookRunAuthorizationSharedMethodTests.DirectContext).
        services.AddSingleton<IAccessDataSource>(host.Access);
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        services.AddScoped<AuthorizationService>();
        services.AddStatedNarrowingCaller(() => host.NarrowingMode, () => host.World);
        services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
        services.AddSingleton(host.Playbooks.Object);
        services.AddSingleton(host.Nodes.Object);

        services.AddHttpContextAccessor();
        services.AddSingleton(Engine.Object);
        services.AddSingleton(Graph.Object);
        services.AddSingleton(DataverseUser.Object);
        services.AddSingleton(new HandoffUrlBuilder(EnvironmentUrl));
        services.AddSingleton<ITenantCache>(Cache);
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IHostApplicationLifetime>(Lifetime);
        services.AddSingleton(Analyses.Object);
        services.AddSingleton(RunRecords.Object);
        services.AddScoped<PlaybookRunRecordStore>();
        services.AddSingleton<IPlaybookRunAi, DetachedPlaybookRunner>();
        _root = services.BuildServiceProvider();
        Runs = _root.GetRequiredService<IPlaybookRunAi>();
        Accessor = _root.GetRequiredService<IHttpContextAccessor>();
    }

    public PlaybookAuthHost Host { get; }

    public WatchedTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

    public TestLifetime Lifetime { get; } = new();

    public Mock<IPlaybookOrchestrationService> Engine { get; } = new(MockBehavior.Strict);

    /// <summary>The id the analysis create returns by default (task 037a).</summary>
    public static readonly Guid AnalysisId = Guid.Parse("6f0c1a52-0037-4037-8037-00000000a001");

    /// <summary>The analysis create (task 037a): <see cref="AnalysisId"/> is created unless a test sets it up otherwise.</summary>
    public Mock<IAnalysisStartAi> Analyses { get; } = new();

    /// <summary>The run record store's Dataverse seam (the engine is substituted, so nothing writes through it here).</summary>
    public Mock<IGenericEntityService> RunRecords { get; } = new();

    public Mock<IGraphClientFactory> Graph { get; } = new();

    public Mock<IDataverseUserClient> DataverseUser { get; } = new();

    public LogCapture Logs { get; } = new();

    public RecordingTenantCache Cache { get; } = new();

    public ConcurrentQueue<EngineCall> Calls { get; } = new();

    public ConcurrentDictionary<Guid, PlaybookRunStatus> StoredRuns { get; } = new();

    public IPlaybookRunAi Runs { get; }

    public IHttpContextAccessor Accessor { get; }

    /// <summary>What the engine streams for the next run (default: a run that completes after <see cref="Gate"/> opens).</summary>
    public Func<PlaybookRunRequest, CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> Script { get; set; }
        = (_, _) => throw new InvalidOperationException("No engine script set.");

    /// <summary>Held closed by <see cref="GatedRun"/> until the test opens it: the run is "slow" until then.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<PlaybookRunWorld> StartAsync()
    {
        var world = new PlaybookRunWorld(await PlaybookAuthHost.StartAsync());
        world.Script = world.GatedRun;
        return world;
    }

    /// <summary>A public, read-only JPS playbook and one document the caller can read: a run 007 allows.</summary>
    public (Guid PlaybookId, Guid DocumentId) AllowedRun() => (Host.ReadOnlyPublicPlaybook(), Host.ReadableDocument());

    /// <summary>The caller's own request: the principal, the bearer header, a trace id, request services from a new scope.</summary>
    public HttpContext CallerContext(string oid = CallerOid, ClaimsPrincipal? principal = null, string traceId = "trace-030")
    {
        var http = new DefaultHttpContext
        {
            RequestServices = _root.CreateScope().ServiceProvider,
            User = principal ?? Principal(oid),
            TraceIdentifier = traceId,
        };
        http.Request.Headers.Authorization = Bearer;
        return http;
    }

    public static ClaimsPrincipal Principal(string oid) =>
        new(new ClaimsIdentity(new[] { new Claim("oid", oid), new Claim("tid", PlaybookAuthzTestAuthHandler.TenantId) }, "Test"));

    public Task<CallToolResult> RunToolAsync(HttpContext http, object arguments) =>
        RunPlaybookTool.RunAsync(Args(arguments), http, Cache, Runs, Time,
            _root.GetRequiredService<ILogger<RunPlaybookTool>>(), CancellationToken.None);

    public Task<CallToolResult> GetToolAsync(HttpContext http, Guid runId) =>
        GetPlaybookRunTool.RunAsync(Args(new { runId }), http, Runs, Time,
            _root.GetRequiredService<ILogger<GetPlaybookRunTool>>(), CancellationToken.None);

    public Task<CallToolResult> ResultsToolAsync(HttpContext http, Guid runId) =>
        GetPlaybookRunResultsTool.RunAsync(Args(new { runId }), http, Runs, Time,
            _root.GetRequiredService<ILogger<GetPlaybookRunResultsTool>>(), CancellationToken.None);

    // ---- task 037b: the results read (the analysis-read rule's app-only anchor read, the caller's OBO reads, the durable link) ------

    /// <summary>
    /// The analysis-read rule's app-only read of <paramref name="analysisId"/>'s anchors (ids only): anchored on <paramref name="documentId"/>,
    /// answering only the columns selected, as Dataverse does.
    /// </summary>
    public void SeedAnalysisAnchor(Guid analysisId, Guid documentId) =>
        RunRecords
            .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) =>
            {
                var row = new Microsoft.Xrm.Sdk.Entity("sprk_analysis", analysisId);
                if (columns.Contains("sprk_documentid"))
                {
                    row["sprk_documentid"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", documentId);
                }

                return row;
            });

    /// <summary>The caller's own OBO read of the analysis row: its name, its document and (when selected) its working document.</summary>
    public void CallerReadsAnalysis(Guid analysisId, Guid documentId, string? workingDocument) =>
        DataverseUser
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_analysises({analysisId:D})", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                AnalysisReads.Enqueue(path);
                var row = new Dictionary<string, object?>
                {
                    ["sprk_analysisid"] = analysisId.ToString("D"),
                    ["sprk_name"] = "Copilot playbook run 2026-11-26 09:00 UTC",
                    ["_sprk_documentid_value"] = documentId.ToString("D"),
                };
                if (path.Contains("sprk_workingdocument", StringComparison.Ordinal))
                {
                    row["sprk_workingdocument"] = workingDocument;
                }

                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(row));
            });

    /// <summary>Dataverse security refuses the caller's own read of the analysis row (<paramref name="status"/>: 403 or 404).</summary>
    public void CallerAnalysisReadRefused(Guid analysisId, int status = 403) =>
        DataverseUser
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_analysises({analysisId:D})", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(status, "DATAVERSE_ACCESS_DENIED", "denied"));

    /// <summary>The caller reads exactly these documents (Dataverse answers the OBO query with only the rows the caller may read).</summary>
    public void CallerReadsDocuments(params (Guid Id, string Name)[] readable) =>
        DataverseUser
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents?", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                var rows = readable
                    .Where(r => path.Contains(r.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    .Select(r => new Dictionary<string, object?> { ["sprk_documentid"] = r.Id.ToString("D"), ["sprk_documentname"] = r.Name })
                    .ToArray();
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows }));
            });

    /// <summary>The caller reads the playbook's name.</summary>
    public void CallerReadsPlaybookName(Guid playbookId, string name) =>
        DataverseUser
            .Setup(d => d.GetAsync($"sprk_analysisplaybooks({playbookId:D})?$select=sprk_name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { sprk_name = name })));

    /// <summary>Every path the caller's OBO reads of the analysis row asked for, in order.</summary>
    public ConcurrentQueue<string> AnalysisReads { get; } = new();

    /// <summary>
    /// A run this process does not hold: the engine's status (<see cref="StoredRuns"/>) and its <c>sprk_playbookrun</c> row (regarding and
    /// document ids), started by <paramref name="oid"/>, read through the run record store's Dataverse seam.
    /// </summary>
    public Guid SeedStoredRun(
        Guid playbookId, PlaybookRunState state, string oid, (string Type, Guid Id)? regarding, params Guid[] documentIds)
    {
        var runId = Guid.NewGuid();
        StoredRuns[runId] = new PlaybookRunStatus
        {
            RunId = runId,
            PlaybookId = playbookId,
            State = state,
            StartedAt = Time.GetUtcNow().AddMinutes(-20),
            CompletedAt = state is PlaybookRunState.Running or PlaybookRunState.Pending ? null : Time.GetUtcNow().AddMinutes(-15),
            StartedByOid = oid,
            Metrics = new PlaybookRunMetrics { TotalNodes = 2, CompletedNodes = 2 },
        };

        var systemUser = Guid.NewGuid();
        var row = new Microsoft.Xrm.Sdk.Entity(PlaybookRunRecordStore.RunEntity, runId)
        {
            [PlaybookRunRecordStore.PlaybookColumn] = new Microsoft.Xrm.Sdk.EntityReference(PlaybookRunRecordStore.PlaybookEntity, playbookId),
            [PlaybookRunRecordStore.TriggeredByColumn] = new Microsoft.Xrm.Sdk.EntityReference(PlaybookRunRecordStore.SystemUserEntity, systemUser),
            [PlaybookRunRecordStore.InputContextColumn] = JsonSerializer.Serialize(new { documentIds }),
        };
        if (regarding is { } link)
        {
            row[PlaybookRunRecordStore.RegardingEntityTypeColumn] = link.Type;
            row[PlaybookRunRecordStore.RegardingRecordIdColumn] = link.Id.ToString("D");
        }

        _storedRows[runId] = row;
        _systemUsers[systemUser] = Guid.Parse(oid);
        RunRecords
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) => AnswerRecordQuery(query));
        return runId;
    }

    private readonly ConcurrentDictionary<Guid, Microsoft.Xrm.Sdk.Entity> _storedRows = new();
    private readonly ConcurrentDictionary<Guid, Guid> _systemUsers = new();

    private Microsoft.Xrm.Sdk.EntityCollection AnswerRecordQuery(Microsoft.Xrm.Sdk.Query.QueryExpression query)
    {
        var id = query.Criteria.Conditions.Single().Values.Single() is Guid value ? value : Guid.Empty;
        var answer = new Microsoft.Xrm.Sdk.EntityCollection();
        if (query.EntityName == PlaybookRunRecordStore.RunEntity && _storedRows.TryGetValue(id, out var row))
        {
            var selected = new Microsoft.Xrm.Sdk.Entity(row.LogicalName, row.Id);
            foreach (var column in query.ColumnSet.Columns.Where(row.Attributes.ContainsKey))
            {
                selected[column] = row[column];
            }

            answer.Entities.Add(selected);
        }
        else if (query.EntityName == PlaybookRunRecordStore.SystemUserEntity && _systemUsers.TryGetValue(id, out var oid))
        {
            answer.Entities.Add(new Microsoft.Xrm.Sdk.Entity(PlaybookRunRecordStore.SystemUserEntity, id)
            {
                [PlaybookRunRecordStore.SystemUserOidColumn] = oid,
            });
        }

        return answer;
    }

    public static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    /// <summary>Waits (bounded, real time as a safety net only) until the detached run has finished and logged its outcome.</summary>
    public Task RunFinished(int count = 1) => Logs.WaitForAsync("Detached playbook run finished", count);

    public async Task<PlaybookRunView> ReadFinishedAsync(Guid runId, int count = 1)
    {
        await RunFinished(count);
        return (await Runs.ReadAsCallerAsync(CallerContext(), runId, CancellationToken.None))!;
    }

    // ---- engine scripts --------------------------------------------------------------------------------------------------------

    /// <summary>Accepted at once (RunStarted, 2 nodes), then waits for <see cref="Gate"/>, then two nodes and RunCompleted.</summary>
    public IAsyncEnumerable<PlaybookStreamEvent> GatedRun(PlaybookRunRequest request, CancellationToken ct) => GatedRunOn(Gate, request, ct);

    /// <summary>As <see cref="GatedRun"/>, held by <paramref name="gate"/> (a gate per run, so one run can finish while others are held).</summary>
    public static async IAsyncEnumerable<PlaybookStreamEvent> GatedRunOn(
        TaskCompletionSource gate, PlaybookRunRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, request.PlaybookId, 2);
        await gate.Task.WaitAsync(ct);
        yield return PlaybookStreamEvent.NodeCompleted(runId, request.PlaybookId, Guid.NewGuid(), "One", null!);
        yield return PlaybookStreamEvent.NodeCompleted(runId, request.PlaybookId, Guid.NewGuid(), "Two", null!);
        yield return PlaybookStreamEvent.RunCompleted(runId, request.PlaybookId, new PlaybookRunMetrics { TotalNodes = 2, CompletedNodes = 2 });
    }

    /// <summary>Never accepts the run: waits for its token, which <see cref="Cancelled"/> records.</summary>
    public static Func<PlaybookRunRequest, CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> NeverAccepts(TaskCompletionSource cancelled) =>
        (_, ct) => NeverAcceptsAsync(cancelled, ct);

    private static async IAsyncEnumerable<PlaybookStreamEvent> NeverAcceptsAsync(TaskCompletionSource cancelled, [EnumeratorCancellation] CancellationToken ct)
    {
        using (ct.Register(() => cancelled.TrySetResult()))
        {
            await UntilCancelled(ct);
        }

        yield break;
    }

    /// <summary>Waits for <paramref name="ct"/> only (no clock): throws <see cref="OperationCanceledException"/> when it is cancelled.</summary>
    private static Task UntilCancelled(CancellationToken ct) =>
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(ct);

    /// <summary>
    /// Accepted, then runs until its token is cancelled. <paramref name="endWithEvent"/>: the engine's own shape (it writes RunCancelled
    /// and ends the stream); otherwise the read throws <see cref="OperationCanceledException"/> (ReadAllAsync on a cancelled token).
    /// </summary>
    public static Func<PlaybookRunRequest, CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> RunsUntilCancelled(bool endWithEvent) =>
        (request, ct) => RunsUntilCancelledAsync(request, endWithEvent, ct);

    private static async IAsyncEnumerable<PlaybookStreamEvent> RunsUntilCancelledAsync(
        PlaybookRunRequest request, bool endWithEvent, [EnumeratorCancellation] CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, request.PlaybookId, 3);
        yield return PlaybookStreamEvent.NodeCompleted(runId, request.PlaybookId, Guid.NewGuid(), "One", null!);
        if (!endWithEvent)
        {
            await UntilCancelled(ct);
        }

        try
        {
            await UntilCancelled(ct);
        }
        catch (OperationCanceledException)
        {
        }

        yield return PlaybookStreamEvent.RunCancelled(runId, request.PlaybookId);
    }

    /// <summary>Accepted, then the stream throws <paramref name="failure"/> (a failure that escapes the engine mid-run).</summary>
    public Func<PlaybookRunRequest, CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> ThrowsAfterStart(Exception failure) =>
        (request, ct) => ThrowsAfterStartAsync(request, failure, ct);

    private async IAsyncEnumerable<PlaybookStreamEvent> ThrowsAfterStartAsync(
        PlaybookRunRequest request, Exception failure, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return PlaybookStreamEvent.RunStarted(Guid.NewGuid(), request.PlaybookId, 2);
        await Gate.Task.WaitAsync(ct);
        throw failure;
    }

    /// <summary>No run was started (the engine's person entry was never called).</summary>
    public void VerifyNothingStarted() =>
        Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never());

    /// <summary>The app-only entry was never called (an auth failure is never retried as the app).</summary>
    public void VerifyNoAppOnlyRun()
    {
        Engine.Verify(e => e.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        Graph.Verify(g => g.ForApp(), Times.Never());
    }

    public async ValueTask DisposeAsync()
    {
        Gate.TrySetResult();
        Lifetime.StopApplication();
        await _root.DisposeAsync();
        await Host.DisposeAsync();
    }

    /// <summary>One call of the engine's person-run entry, as the engine saw it.</summary>
    internal sealed record EngineCall(
        PlaybookRunRequest Request,
        HttpContext Context,
        ClaimsPrincipal User,
        string Authorization,
        string TraceIdentifier,
        HttpContext? AccessorDuringRun,
        CancellationToken Token,
        Guid? LinkedAnalysis);

    /// <summary>
    /// A <see cref="FakeTimeProvider"/> that also says when a timer of a given due time has been created, so a test advances the clock
    /// only after the code under test has armed its timer (an advance before that would be missed).
    /// </summary>
    internal sealed class WatchedTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly ConcurrentDictionary<TimeSpan, TaskCompletionSource> _armed = new();

        /// <summary>Completes once a timer due after <paramref name="dueTime"/> exists (bounded, real time as a safety net only).</summary>
        public Task TimerArmed(TimeSpan dueTime) => ArmedSource(dueTime).Task.WaitAsync(TimeSpan.FromSeconds(15));

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            ArmedSource(dueTime).TrySetResult();
            return timer;
        }

        private TaskCompletionSource ArmedSource(TimeSpan dueTime) =>
            _armed.GetOrAdd(dueTime, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>The host lifetime: <see cref="StopApplication"/> fires ApplicationStopping.</summary>
    internal sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }

    /// <summary>Every log entry at every level: the formatted message, each structured value and the exception's full text.</summary>
    internal sealed class LogCapture : ILoggerProvider
    {
        private readonly object _gate = new();
        private readonly List<string> _entries = new();
        private readonly List<(string Text, int Count, TaskCompletionSource Done)> _waits = new();

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        public Task WaitForAsync(string text, int count)
        {
            lock (_gate)
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waits.Add((text, count, done));
                Check();
                return done.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }

        private void Add(string entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
                Check();
            }
        }

        private void Check()
        {
            foreach (var (text, count, done) in _waits)
            {
                if (_entries.Count(e => e.Contains(text, StringComparison.Ordinal)) >= count)
                {
                    done.TrySetResult();
                }
            }
        }

        private sealed class Logger(LogCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(" | ", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                owner.Add($"{logLevel} {category}: {formatter(state, exception)} [{values}] {exception}");
            }
        }
    }

    /// <summary>An <see cref="InMemoryTenantCache"/> that also keeps the serialized form of every value written to it.</summary>
    internal sealed class RecordingTenantCache : ITenantCache
    {
        private readonly InMemoryTenantCache _inner = new();

        public ConcurrentQueue<string> Writes { get; } = new();

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue($"{tenantId}:{resource}:{id} {JsonSerializer.Serialize(value)}");
            return _inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);

        public Task<string?> GetStringAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.GetStringAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetStringAsync(string tenantId, string resource, string id, int version, string value, TimeSpan? ttl = null, TimeSpan? slidingExpiration = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue($"{tenantId}:{resource}:{id} {value}");
            return ((ITenantCache)_inner).SetStringAsync(tenantId, resource, id, version, value, ttl, slidingExpiration, cacheInstance, ct);
        }

        public Task RefreshAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.RefreshAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetSlidingAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan slidingExpiration, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue($"{tenantId}:{resource}:{id} {JsonSerializer.Serialize(value)}");
            return _inner.SetSlidingAsync(tenantId, resource, id, version, value, slidingExpiration, cacheInstance, ct);
        }
    }
}
