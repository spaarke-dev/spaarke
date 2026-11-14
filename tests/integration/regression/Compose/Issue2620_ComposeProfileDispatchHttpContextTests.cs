// Issue #2620 (ISS-002) regression — the detached Compose profile dispatch must not clear the
// ORIGINATING request's HttpContext.
//
// Root cause. ComposeProfileDispatcher.Dispatch fires `Task.Run(RunAsync)`, and Task.Run flows the
// request's ExecutionContext into the task. RunAsync then assigned `IHttpContextAccessor.HttpContext`
// to its rebuilt detached context. In ASP.NET Core 10.0 the accessor keeps the context in a static
// `AsyncLocal<HttpContextHolder>`, and its setter FIRST does `_httpContextCurrent.Value?.Context = null`
// — on the holder that flowed in, which is the very holder object the originating request is still
// reading — and only then installs a new holder for the task's own flow. So the background task nulled
// the save (or load / refresh-profile) request's HttpContext while that request was still running:
// everything after `ComposeService.cs:2323` (memory capture, response) and after the G10 reload
// re-trigger read `IHttpContextAccessor.HttpContext == null`, and caller-dependent code such as
// DataverseUserClient fails closed with DATAVERSE_USER_CONTEXT_REQUIRED.
//
// Fix. The detached work starts under `ExecutionContext.SuppressFlow()`, so it begins with no flowed
// AsyncLocals and its accessor assignment installs a holder in ITS OWN flow only. The two flowed values
// the background profile relied on for operability — the request's trace parent (Activity) and the
// audit log scope (oid/appid/obo/tenantId/correlationId) — are re-established explicitly in the
// detached flow. The tests below pin the defect AND both preserved behaviours.
//
// Deterministic by construction: the recording facade completes its TaskCompletionSource only AFTER
// RunAsync has assigned the accessor (the facade is called after the assignment), so awaiting it
// guarantees the hazardous write has already happened — no sleeps, no timing windows.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — "every bug = one regression
// file". Compiled into Sprk.Bff.Api.Tests via the `..\..\integration\regression\**\*.cs` glob. Uses the
// REAL HttpContextAccessor and the REAL ComposeProfileDispatcher (InternalsVisibleTo, ADR-038 A2); only
// the AI facade (IDocumentProfileAi, the ADR-013 module boundary) is doubled.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Compose;

public sealed class Issue2620_ComposeProfileDispatchHttpContextTests
{
    private const string UserOid = "11111111-2620-4000-8000-000000000001";
    private const string UserTid = "22222222-2620-4000-8000-000000000002";
    private const string BearerHeader = "Bearer user-assertion-2620";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Dispatch_WhenTheDetachedProfileSetsTheAccessor_TheOriginatingRequestStillSeesItsOwnHttpContext()
    {
        var harness = new Harness();
        var requestContext = NewRequestContext();
        var accessor = new HttpContextAccessor();
        accessor.HttpContext = requestContext; // what DefaultHttpContextFactory does at request start

        harness.Dispatcher.Dispatch(Guid.NewGuid(), requestContext);
        await harness.Facade.Observed.Task.WaitAsync(Wait); // RunAsync has assigned the accessor by now

        // Identity is compared with ReferenceEquals: FluentAssertions cannot format a DefaultHttpContext
        // object graph in a failure message (it throws inside its formatter), which hid the real failure.
        var seenByRequest = accessor.HttpContext;
        seenByRequest.Should().NotBeNull(
            "the background profile must never clear the HttpContext of the request that dispatched it — " +
            "code after the dispatch (and DataverseUserClient) reads it and would fail closed on null");
        ReferenceEquals(seenByRequest, requestContext).Should().BeTrue("the request keeps its OWN context");
    }

    [Fact]
    public async Task Dispatch_DetachedProfile_SeesItsOwnDetachedContextThroughTheAccessor_CarryingTheCallersTokenAndUser()
    {
        var harness = new Harness();
        var requestContext = NewRequestContext();
        new HttpContextAccessor().HttpContext = requestContext;

        harness.Dispatcher.Dispatch(Guid.NewGuid(), requestContext);
        var seen = await harness.Facade.Observed.Task.WaitAsync(Wait);

        ReferenceEquals(seen.AccessorContext, seen.ArgumentContext).Should().BeTrue(
            "AnalysisDocumentLoader's tenant-cache scoping reads the accessor inside the detached work");
        ReferenceEquals(seen.ArgumentContext, requestContext).Should().BeFalse(
            "the detached work never touches the request's context");
        seen.Authorization.Should().Be(BearerHeader, "the OBO user assertion is carried into the detached context");
        seen.Oid.Should().Be(UserOid, "the caller's claims are carried into the detached context");
    }

    [Fact]
    public async Task Dispatch_DetachedProfile_RunsUnderTheOriginatingRequestsTrace()
    {
        var harness = new Harness();
        var requestContext = NewRequestContext();
        using var requestActivity = new Activity("issue-2620-request").Start();

        harness.Dispatcher.Dispatch(Guid.NewGuid(), requestContext);
        var seen = await harness.Facade.Observed.Task.WaitAsync(Wait);

        seen.TraceId.Should().Be(requestActivity.TraceId,
            "the background profile's telemetry stays correlated with the save that dispatched it");
    }

    [Fact]
    public async Task Dispatch_DetachedProfileLogs_CarryTheCallersAuditScope()
    {
        var harness = new Harness();
        var requestContext = NewRequestContext();

        // The scope AuditEnrichmentMiddleware opens around every authenticated request.
        using (harness.Logger.BeginScope(new Dictionary<string, object?>
        {
            ["oid"] = UserOid,
            ["tenantId"] = UserTid,
            ["correlationId"] = requestContext.TraceIdentifier,
        }))
        {
            harness.Dispatcher.Dispatch(Guid.NewGuid(), requestContext);
        }

        await harness.Facade.Observed.Task.WaitAsync(Wait);

        var starting = harness.Logger.Entries.Single(e => e.Message.Contains("starting best-effort OBO profile"));
        var auditScope = starting.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .Select(s => s.ToDictionary(kv => kv.Key, kv => kv.Value))
            .Should().ContainSingle(s => s.ContainsKey("oid"),
                "the SIEM audit fields stay on the background profile's log records").Subject;
        auditScope["oid"].Should().Be(UserOid);
        auditScope["tenantId"].Should().Be(UserTid);
        auditScope["correlationId"].Should().Be(requestContext.TraceIdentifier);
    }

    [Fact]
    public async Task Dispatch_FromACallerThatAlreadySuppressedFlow_StillDispatchesTheProfile()
    {
        // ExecutionContext.SuppressFlow() throws when flow is already suppressed; the dispatcher must
        // tolerate a caller that suppressed it, not fault the save.
        var harness = new Harness();
        var requestContext = NewRequestContext();

        StoredStepSignal signal;
        using (ExecutionContext.SuppressFlow())
        {
            signal = harness.Dispatcher.Dispatch(Guid.NewGuid(), requestContext);
        }

        signal.Started.Should().BeTrue("the profile was dispatched (non-terminal Running signal)");
        (await harness.Facade.Observed.Task.WaitAsync(Wait)).Authorization.Should().Be(BearerHeader);
    }

    private static DefaultHttpContext NewRequestContext()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-issue-2620" };
        context.Request.Headers.Authorization = BearerHeader;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("oid", UserOid),
                new Claim("tid", UserTid),
                new Claim("scp", "user_impersonation"),
            },
            authenticationType: "Test"));
        return context;
    }

    /// <summary>The real dispatcher over a real DI container whose only double is the AI facade.</summary>
    private sealed class Harness
    {
        public RecordingProfileAi Facade { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public ComposeProfileDispatcher Dispatcher { get; }

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddHttpContextAccessor();
            services.AddScoped<IDocumentProfileAi>(_ => Facade);
            var provider = services.BuildServiceProvider();

            Dispatcher = new ComposeProfileDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(), Facade, appLifetime: null, Logger);
        }
    }

    private sealed record Observation(
        HttpContext? AccessorContext, HttpContext ArgumentContext, string? Authorization, string? Oid, ActivityTraceId? TraceId);

    /// <summary>The ADR-013 AI facade boundary, doubled: records what the detached work can see, then
    /// signals. Called by RunAsync strictly AFTER the accessor assignment.</summary>
    private sealed class RecordingProfileAi : IDocumentProfileAi
    {
        private readonly HttpContextAccessor _accessor = new();

        public TaskCompletionSource<Observation> Observed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DocumentProfileOutcome> ProfileDocumentAsUserAsync(
            Guid documentId, HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            Observed.TrySetResult(new Observation(
                _accessor.HttpContext,
                httpContext,
                httpContext.Request.Headers.Authorization.ToString(),
                httpContext.User.FindFirst("oid")?.Value,
                Activity.Current?.TraceId));
            return Task.FromResult(DocumentProfileOutcome.Succeeded());
        }
    }

    /// <summary>Records each log line with the scopes active when it was written. Scopes ride an
    /// AsyncLocal (LoggerExternalScopeProvider) exactly as the production LoggerFactory's do.</summary>
    private sealed class RecordingLogger : ILogger
    {
        private readonly LoggerExternalScopeProvider _scopes = new();

        public ConcurrentQueue<(string Message, List<object?> Scopes)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<object?>();
            _scopes.ForEachScope((scope, list) => list.Add(scope), scopes);
            Entries.Enqueue((formatter(state, exception), scopes));
        }
    }
}
