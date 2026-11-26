// CONTRACT — the bytes of GET /api/ai/playbooks and GET /api/ai/playbooks/public (spaarkeai-mcp-bff-api-r2 task 024, D-024-extract).
// KEEP path: endpoint-contract.
//
// WHAT RUNS: the REAL MapPlaybookEndpoints on PlaybookAuthHost (routing, RequireAuthorization, binding, the handlers, the JSON writer),
// substituted only at the IPlaybookService interface and the WhoAmI probe (ADR-038). Each case freezes status, Content-Type, body bytes
// (ProblemDetails' traceId removed: it differs per request) and the query the service was asked, as they were BEFORE task 024 moved
// the two handlers' rules into PlaybookEndpoints.ListPlaybooksForCallerAsync. Task 024 ran this file against the pre-change code
// (all green) and again after the change (all green): the extraction did not move a byte.

using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Ai;

public sealed class PlaybookListRoutesFrozenBytesTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000024";
    private static readonly Guid SystemUserId = Guid.Parse("0024aaaa-0000-4000-8000-000000000001");
    private static readonly Guid First = Guid.Parse("0024bbbb-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0024bbbb-0000-4000-8000-000000000002");
    private static readonly Guid OutputType = Guid.Parse("0024cccc-0000-4000-8000-000000000001");

    private static PlaybookListResponse Page(int page, int pageSize) => new()
    {
        Items =
        [
            new PlaybookSummary
            {
                Id = First, Name = "NDA review", Description = "Reviews an NDA", OutputTypeId = OutputType, IsPublic = false,
                IsTemplate = false, OwnerId = SystemUserId, ModifiedOn = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc),
            },
            new PlaybookSummary
            {
                Id = Second, Name = "Lease \"abstract\" <v2>", Description = null, IsPublic = true, IsTemplate = true,
                OwnerId = Guid.Empty, ModifiedOn = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc),
            },
        ],
        TotalCount = 41,
        Page = page,
        PageSize = pageSize,
    };

    private const string PageBody =
        "{\"items\":[{\"id\":\"0024bbbb-0000-4000-8000-000000000001\",\"name\":\"NDA review\",\"description\":\"Reviews an NDA\","
        + "\"outputTypeId\":\"0024cccc-0000-4000-8000-000000000001\",\"isPublic\":false,\"isTemplate\":false,"
        + "\"ownerId\":\"0024aaaa-0000-4000-8000-000000000001\",\"modifiedOn\":\"2026-11-20T09:30:00Z\"},"
        + "{\"id\":\"0024bbbb-0000-4000-8000-000000000002\",\"name\":\"Lease \\\"abstract\\\" <v2>\",\"description\":null,"
        + "\"outputTypeId\":null,\"isPublic\":true,\"isTemplate\":true,\"ownerId\":\"00000000-0000-0000-0000-000000000000\","
        + "\"modifiedOn\":\"2026-11-19T08:00:00Z\"}],\"totalCount\":41,\"page\":2,\"pageSize\":20,\"totalPages\":3,\"hasNextPage\":true,"
        + "\"hasPreviousPage\":true}";

    [Fact]
    public async Task OwnedList_Resolved_IsTheServicePage_ForTheCallersSystemUserId_AndTheQueryAsBound()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = SystemUserId;
        PlaybookQueryParameters? asked = null;
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(SystemUserId, It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, PlaybookQueryParameters q, CancellationToken _) => asked = q)
            .ReturnsAsync(Page(2, 20));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks?page=2&nameFilter=NDA&sortBy=name&sortDescending=false&outputTypeId=" + OutputType);

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(PageBody);
        asked.Should().Be(new PlaybookQueryParameters
        {
            Page = 2,
            PageSize = 20,
            NameFilter = "NDA",
            OutputTypeId = OutputType,
            PublicOnly = false,
            SortBy = "name",
            SortDescending = false,
        });
    }

    [Fact]
    public async Task OwnedList_UnresolvableSystemUserId_IsAnEmptyPage_WithTheRawPage_AndTheNormalizedPageSize_AndNoQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = null;

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks?page=0&pageSize=500");

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(
            "{\"items\":[],\"totalCount\":0,\"page\":0,\"pageSize\":100,\"totalPages\":0,\"hasNextPage\":false,\"hasPreviousPage\":false}");
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnedList_NonGuidOid_Is401_UserIdentityNotFound_AndNothingIsAsked()
    {
        await using var host = await PlaybookAuthHost.StartAsync();

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks", "not-a-guid");

        status.Should().Be(HttpStatusCode.Unauthorized);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.2\",\"title\":\"Unauthorized\",\"status\":401,"
            + "\"detail\":\"User identity not found\"}");
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnedList_ServiceFault_Is500_FailedToListPlaybooks()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down: secret-internal-detail"));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks");

        status.Should().Be(HttpStatusCode.InternalServerError);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500,"
            + "\"detail\":\"Failed to list playbooks\"}");
    }

    [Theory]
    [InlineData(CallerOid)]
    [InlineData("not-a-guid")] // the public list never read the oid
    public async Task PublicList_IsTheServicePage_ForTheQueryAsBound_WithPublicOnly(string oid)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        PlaybookQueryParameters? asked = null;
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Callback((PlaybookQueryParameters q, CancellationToken _) => asked = q)
            .ReturnsAsync(Page(2, 20));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks/public?page=2&pageSize=20&nameFilter=Lease", oid);

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(PageBody);
        asked.Should().Be(new PlaybookQueryParameters { Page = 2, PageSize = 20, NameFilter = "Lease", PublicOnly = true });
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task PublicList_ServiceFault_Is500_FailedToListPublicPlaybooks()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down: secret-internal-detail"));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks/public");

        status.Should().Be(HttpStatusCode.InternalServerError);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500,"
            + "\"detail\":\"Failed to list public playbooks\"}");
    }

    // ── cancellation (task 024 verifier finding: the routes now pass RequestAborted to the service) ────────────────

    [Theory]
    [InlineData("/api/ai/playbooks")]
    [InlineData("/api/ai/playbooks/public")]
    public async Task List_TheClientAbortsTheRequest_Is499_LoggedAtDebugWithoutAStack_NeverA500OrAnErrorLog(string path)
    {
        var logs = new RecordingLogs();
        var outcome = new ResponseOutcome();
        await using var host = await PlaybookAuthHost.StartAsync(services => Observe(services, logs, outcome));
        host.Probe.SystemUserId = SystemUserId;
        using var abort = new CancellationTokenSource();
        // The service parks until the request is aborted; the client aborts as soon as the service has been asked.
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, PlaybookQueryParameters _, CancellationToken ct) => ParkUntilAborted(abort, ct));
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookQueryParameters _, CancellationToken ct) => ParkUntilAborted(abort, ct));

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
        var send = () => host.SendAsync(request, HttpCompletionOption.ResponseContentRead, abort.Token);
        await send.Should().ThrowAsync<OperationCanceledException>("the client that aborted never sees the answer");

        await outcome.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        outcome.Status.Should().Be(499, "an abandoned request is not a fault");
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error, "an abort is not logged as an error");
        logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Debug && e.Message.Contains("abandoned by the client", StringComparison.Ordinal))
            .Which.Exception.Should().BeNull("no stack for an abandoned request");
    }

    [Theory]
    [InlineData("/api/ai/playbooks", "Failed to list playbooks")]
    [InlineData("/api/ai/playbooks/public", "Failed to list public playbooks")]
    public async Task List_ACancellationTheRequestDidNotCause_StaysTheRoutes500_AndIsLoggedAsAnError(string path, string detail)
    {
        var logs = new RecordingLogs();
        await using var host = await PlaybookAuthHost.StartAsync(services => Observe(services, logs, new ResponseOutcome()));
        host.Probe.SystemUserId = SystemUserId;
        // A timeout inside the service (HttpClient.Timeout): a TaskCanceledException while the request is still alive.
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var (status, type, body) = await SendAsync(host, path);

        status.Should().Be(HttpStatusCode.InternalServerError);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500,"
            + $"\"detail\":\"{detail}\"}}");
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception is TaskCanceledException);
    }

    private static async Task<PlaybookListResponse> ParkUntilAborted(CancellationTokenSource clientAbort, CancellationToken requestAborted)
    {
        await clientAbort.CancelAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan, requestAborted);
        return Page(1, 20);
    }

    private static void Observe(IServiceCollection services, RecordingLogs logs, ResponseOutcome outcome)
    {
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILoggerProvider>(logs);
        services.AddSingleton<IStartupFilter>(outcome);
    }

    /// <summary>The status the server wrote, seen by a middleware in front of the whole pipeline (the aborted client cannot see it).</summary>
    private sealed class ResponseOutcome : IStartupFilter
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Status { get; private set; }

        public Task Completed => _completed.Task;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                try
                {
                    await pipeline();
                }
                finally
                {
                    Status = context.Response.StatusCode;
                    _completed.TrySetResult();
                }
            });
            next(app);
        };
    }

    private sealed class RecordingLogs : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public void Dispose()
        {
        }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<(LogLevel, string, Exception?)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>Status, Content-Type and the body bytes, with a ProblemDetails' per-request <c>traceId</c> removed.</summary>
    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> SendAsync(
        PlaybookAuthHost host, string path, string oid = CallerOid)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, oid);
        using var response = await host.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var node = JsonNode.Parse(body)!.AsObject();
            node.Remove("traceId");
            body = node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
            });
        }

        return (response.StatusCode, response.Content.Headers.ContentType?.ToString(), body);
    }
}
