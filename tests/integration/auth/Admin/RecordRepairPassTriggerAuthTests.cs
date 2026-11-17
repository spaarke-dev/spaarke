using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Api.Admin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Admin;

/// <summary>
/// field-mapping-server-write-path-r1 task 052c (spec FR-25, NFR-01): <c>POST /api/admin/jobs/record-repair-pass/runs</c> — the
/// repair pass for ONE table over ONE <c>createdon</c> window. Only an administrator starts it (the group's SystemAdmin policy);
/// every request rule is checked before the job's lease is taken, and a refused request starts no run.
/// </summary>
/// <remarks>
/// Real host (<see cref="AdminJobsTestFixture"/>: the real SystemAdmin policy, the real scheduler host, registry and in-memory run
/// store, the real <see cref="RecordRepairPassJob"/>). Only the Dataverse boundary is doubled
/// (<see cref="RecordRepairRunHostFixture.Entities"/>): its WhoAmI names no user, so a started run decides no product principals
/// and reads nothing — the run's own behaviour is pinned in <c>RecordRepairPassJobTests</c>.
/// </remarks>
[Trait("status", "new")]
public sealed class RecordRepairPassTriggerAuthTests : IClassFixture<RecordRepairRunHostFixture>
{
    private const string Route = "/api/admin/jobs/record-repair-pass/runs";
    private const string JobId = RecordRepairPassJob.JobIdConstant;

    private readonly RecordRepairRunHostFixture _fixture;

    public RecordRepairPassTriggerAuthTests(RecordRepairRunHostFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static string Iso(DateTimeOffset at) => at.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    /// <summary>A valid window: five days, well after the host's go-live and before now.</summary>
    private static object ValidBody(string table = "sprk_event") =>
        new { table, createdFrom = Iso(Now.AddDays(-10)), createdTo = Iso(Now.AddDays(-5)) };

    // =====================================================================================
    // AC1: who may start a run
    // =====================================================================================

    [Fact]
    public async Task AnUnauthenticatedCaller_Gets401_AndNoRunStarts()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var before = Runs();

        var response = await client.PostAsJsonAsync(Route, ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Runs().Should().Be(before);
    }

    [Fact]
    public async Task AnAuthenticatedNonAdministrator_Gets403_AndNoRunStarts()
    {
        using var client = _fixture.CreateNonAdminClient();
        var before = Runs();

        var response = await client.PostAsJsonAsync(Route, ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Runs().Should().Be(before);
    }

    [Fact]
    public async Task AnAdministrator_Gets202WithTheRunId_AndTheJobRunsOnDemandForThatTableAndWindow()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(Route, ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var trigger = await response.Content.ReadFromJsonAsync<TriggerResponse>();
        trigger!.RunId.Should().NotBeEmpty();
        response.Headers.Location!.ToString().Should().Be($"{Route}/{trigger.RunId}");

        var run = await _fixture.CompletedRunAsync(trigger.RunId);
        run.JobId.Should().Be(JobId);
        run.Trigger.Should().Be(JobRunTrigger.ManualAdmin);
        using var json = JsonDocument.Parse(run.Result!.ResultJson!);
        json.RootElement.GetProperty("mode").GetString().Should().Be(RecordRepairPassJob.ModeOnDemand);
        json.RootElement.GetProperty("table").GetString().Should().Be("sprk_event");
        run.Result.Success.Should().BeFalse("this host's WhoAmI names no user, so the run decides no principals and reads nothing");
        _fixture.Scans.Should().BeEmpty();
    }

    // =====================================================================================
    // AC2 (must fire): every request rule, before any lease; a refused request starts no run
    // =====================================================================================

    public static TheoryData<string, string> RefusedBodies()
    {
        var now = Now;
        var goLive = RecordRepairRunHostFixture.GoLive;
        return new TheoryData<string, string>
        {
            { Body("sprk_nosuchtable", now.AddDays(-10), now.AddDays(-5)), RecordRepairRunReason.TableNotRepaired },
            { Body("task", now.AddDays(-10), now.AddDays(-5)), RecordRepairRunReason.TableNotRepaired },
            { Body("account", now.AddDays(-10), now.AddDays(-5)), RecordRepairRunReason.TableNotRepaired },
            { Body("sprk_decisionrecord", now.AddDays(-10), now.AddDays(-5)), RecordRepairRunReason.TableNotRepaired },
            { Body("sprk_event", now.AddDays(-5), now.AddDays(-5)), RecordRepairRunReason.WindowEmpty },
            { Body("sprk_event", now.AddDays(-5), now.AddDays(-10)), RecordRepairRunReason.WindowEmpty },
            { Body("sprk_event", now.AddDays(-45), now.AddDays(-5)), RecordRepairRunReason.WindowTooLong },
            { Body("sprk_event", now.AddDays(-2), now.AddDays(1)), RecordRepairRunReason.WindowInFuture },
            { Body("sprk_event", goLive.AddDays(-1), goLive.AddDays(5)), RecordRepairRunReason.BeforeGoLive },
            {
                JsonSerializer.Serialize(new
                {
                    table = "sprk_event",
                    createdFrom = now.AddDays(-10).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                    createdTo = Iso(now.AddDays(-5)),
                }),
                RecordRepairRunReason.TimestampOffsetMissing
            },
            {
                JsonSerializer.Serialize(new { table = "sprk_event", createdFrom = "last tuesday", createdTo = Iso(now.AddDays(-5)) }),
                RecordRepairRunReason.TimestampInvalid
            },
            { JsonSerializer.Serialize(new { table = "sprk_event" }), RecordRepairRunReason.TimestampInvalid },
            { "not json", RecordRepairRunReason.BodyInvalid },
            { "null", RecordRepairRunReason.BodyInvalid },
            { "[]", RecordRepairRunReason.BodyInvalid },
            { "\"a string\"", RecordRepairRunReason.BodyInvalid },
            { JsonSerializer.Serialize(new { table = "sprk_event", createdFrom = Iso(now.AddDays(-10)), createdTo = Iso(now.AddDays(-5)), afterId = "not-a-guid" }), RecordRepairRunReason.AfterIdInvalid },
            { JsonSerializer.Serialize(new { table = "sprk_event", createdFrom = Iso(now.AddDays(-10)), createdTo = Iso(now.AddDays(-5)), afterId = Guid.Empty.ToString("D") }), RecordRepairRunReason.AfterIdInvalid },
        };
    }

    [Theory]
    [MemberData(nameof(RefusedBodies))]
    public async Task ARefusedRequest_Gets400WithItsReasonCode_AndStartsNoRun(string body, string reasonCode)
    {
        using var client = _fixture.CreateAdminClient();
        var before = Runs();
        var startedLogs = StartedLogs();

        var response = await client.PostAsync(Route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        ReasonCode(text).Should().Be(reasonCode);
        text.Should().NotContain("Exception").And.NotContain("   at ");
        Runs().Should().Be(before, "a refused request starts no run");
        StartedLogs().Should().Be(startedLogs, "a refused request is not logged as a started run");
    }

    // =====================================================================================
    // AC6: the job's lease
    // =====================================================================================

    [Fact]
    public async Task WhileTheJobsLeaseIsHeld_TheRouteAnswers409_AndStartsNoRun()
    {
        using var client = _fixture.CreateAdminClient();
        var lease = _fixture.Services.GetRequiredService<IScheduledJobLease>();
        var grant = await lease.TryAcquireAsync(JobId, occurrenceUtc: null, TimeSpan.FromMinutes(5), CancellationToken.None);
        grant.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        var before = Runs();

        try
        {
            var response = await client.PostAsJsonAsync(Route, ValidBody());

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain(JobId);
            Runs().Should().Be(before);
        }
        finally
        {
            await lease.ReleaseAsync(JobId, grant.Token!, CancellationToken.None);
        }
    }

    // =====================================================================================
    // Review F3: who started a run is logged, once, after it started
    // =====================================================================================

    [Fact]
    public async Task AStartedRun_IsLoggedOnce_WithTheCallersObjectId_TheRunIdAndTheRequest()
    {
        using var client = _fixture.CreateAdminClient();
        var afterId = Guid.Parse("a052c000-0000-4000-8000-0000000000bb");
        var from = Now.AddDays(-10);
        var to = Now.AddDays(-5);

        var response = await client.PostAsJsonAsync(Route, new
        {
            table = "sprk_event",
            createdFrom = Iso(from),
            createdTo = Iso(to),
            afterId = afterId.ToString("D"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var trigger = await response.Content.ReadFromJsonAsync<TriggerResponse>();
        var entry = _fixture.Logs.Entries
            .Where(e => e.Message.StartsWith("Record repair run started", StringComparison.Ordinal) && Equals(e.Properties["RunId"], trigger!.RunId))
            .Should().ContainSingle("one entry per started run", "a started run is logged once").Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["Oid"].Should().Be("test-admin-jobs-user-id");
        entry.Properties["RunId"].Should().Be(trigger!.RunId);
        entry.Properties["Table"].Should().Be("sprk_event");
        entry.Properties["CreatedFrom"].Should().Be(DateTimeOffset.Parse(Iso(from), CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture));
        entry.Properties["CreatedTo"].Should().Be(DateTimeOffset.Parse(Iso(to), CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture));
        entry.Properties["AfterId"].Should().Be(afterId.ToString("D"));
        entry.Message.Should().NotContain("admin-token", "a token is never logged");
        await _fixture.CompletedRunAsync(trigger.RunId);
    }

    [Fact]
    public async Task ARequestWhoseLeaseIsHeld_IsNotLoggedAsAStartedRun()
    {
        using var client = _fixture.CreateAdminClient();
        var lease = _fixture.Services.GetRequiredService<IScheduledJobLease>();
        var grant = await lease.TryAcquireAsync(JobId, occurrenceUtc: null, TimeSpan.FromMinutes(5), CancellationToken.None);
        grant.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        var startedLogs = StartedLogs();

        try
        {
            var response = await client.PostAsJsonAsync(Route, ValidBody());

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            StartedLogs().Should().Be(startedLogs, "the log says started only after the host accepted the run");
        }
        finally
        {
            await lease.ReleaseAsync(JobId, grant.Token!, CancellationToken.None);
        }
    }

    // =====================================================================================
    // Review F3: a client that goes away while its body is read is not a server error
    // =====================================================================================

    [Fact]
    public async Task AClientThatAbortsWhileItsBodyIsRead_StartsNoRun_AndIsLoggedAsCancelled_NotAsAFault()
    {
        using var client = _fixture.CreateAdminClient();
        using var cts = new CancellationTokenSource();
        var before = Runs();
        var errors = _fixture.Logs.Entries.Count(e => e.Level >= LogLevel.Error);
        var content = new StallingContent();
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var request = client.PostAsync(Route, content, cts.Token);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await FluentActions.Awaiting(() => request).Should().ThrowAsync<OperationCanceledException>();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline
               && !_fixture.Logs.Entries.Any(e => e.Message.Contains("cancelled by the caller", StringComparison.Ordinal)))
        {
            await Task.Delay(25);
        }

        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("cancelled by the caller", StringComparison.Ordinal));
        _fixture.Logs.Entries.Count(e => e.Level >= LogLevel.Error).Should().Be(errors, "an abort is not a server error");
        Runs().Should().Be(before);
    }

    /// <summary>A request body that has begun (an open brace) and then waits, so the server is reading when the client leaves.</summary>
    private sealed class StallingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync("{\"table\":"u8.ToArray());
            await stream.FlushAsync();
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private int StartedLogs() =>
        _fixture.Logs.Entries.Count(e => e.Message.StartsWith("Record repair run started", StringComparison.Ordinal));

    private int Runs() => _fixture.Store.RunRecords.Count(r => r.JobId == JobId);

    private static string Body(string table, DateTimeOffset from, DateTimeOffset to) =>
        JsonSerializer.Serialize(new { table, createdFrom = Iso(from), createdTo = Iso(to) });

    internal static string? ReasonCode(string problemJson)
    {
        using var json = JsonDocument.Parse(problemJson);
        return json.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }
}

/// <summary>Task 052c: with go-live unset the pass is off — an administrator's request is refused and starts no run.</summary>
[Trait("status", "new")]
public sealed class RecordRepairPassTriggerNotConfiguredTests : IClassFixture<RecordRepairRunUnconfiguredHostFixture>
{
    private readonly RecordRepairRunUnconfiguredHostFixture _fixture;

    public RecordRepairPassTriggerNotConfiguredTests(RecordRepairRunUnconfiguredHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WithGoLiveUnset_AnAdministratorsRequest_Gets409NotConfigured_AndStartsNoRun()
    {
        using var client = _fixture.CreateAdminClient();
        var now = DateTimeOffset.UtcNow;

        var response = await client.PostAsJsonAsync("/api/admin/jobs/record-repair-pass/runs", new
        {
            table = "sprk_event",
            createdFrom = now.AddDays(-10).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            createdTo = now.AddDays(-5).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
        });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, text);
        RecordRepairPassTriggerAuthTests.ReasonCode(text).Should().Be(RecordRepairRunReason.NotConfigured);
        _fixture.Store.RunRecords.Should().NotContain(r => r.JobId == RecordRepairPassJob.JobIdConstant);
    }
}

/// <summary>
/// Task 052c: the admin-jobs host with the repair pass configured (<see cref="GoLive"/>) and its Dataverse boundary doubled: WhoAmI
/// names no user (a started run reads nothing) and every scan is recorded in <see cref="Scans"/>.
/// </summary>
public class RecordRepairRunHostFixture : AdminJobsTestFixture
{
    /// <summary>The host's go-live: sixty days before the fixture was built.</summary>
    public static readonly DateTimeOffset GoLive = DateTimeOffset.UtcNow.AddDays(-60);

    /// <summary>Every log entry the host wrote (the route's own lines are asserted on).</summary>
    public CapturingLogs Logs { get; } = new();

    /// <summary>Every query a run sent to Dataverse.</summary>
    public List<QueryExpression> Scans { get; } = [];

    /// <summary>The value of <c>RecordRepair:Pass:GoLive</c>; empty = unset.</summary>
    protected virtual string GoLiveSetting => GoLive.ToString("O", CultureInfo.InvariantCulture);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RecordRepairPassJob.GoLiveConfigKey] = GoLiveSetting,
        }));

        builder.ConfigureTestServices(services =>
        {
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.GetCurrentIdentityIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.Empty);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Callback((QueryExpression query, CancellationToken _) =>
                {
                    lock (Scans)
                    {
                        Scans.Add(query);
                    }
                })
                .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection());
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(entities.Object);
        });
    }

    /// <summary>The run's record once the job has finished (the trigger returns before it does).</summary>
    public async Task<InMemoryBackgroundJobStore.RunRecord> CompletedRunAsync(Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Store.RunRecords.FirstOrDefault(r => r.RunId == runId && r.CompletedAtUtc is not null) is { } done)
                return done;

            await Task.Delay(25);
        }

        throw new TimeoutException($"Test: run {runId} did not finish.");
    }
}

/// <summary>Task 052c: the same host with <c>RecordRepair:Pass:GoLive</c> unset.</summary>
public sealed class RecordRepairRunUnconfiguredHostFixture : RecordRepairRunHostFixture
{
    protected override string GoLiveSetting => string.Empty;
}

/// <summary>Task 052c: the same host with <c>RecordRepair:Pass:GoLive</c> set to a value that is not an instant.</summary>
public sealed class RecordRepairRunGoLiveInvalidHostFixture : RecordRepairRunHostFixture
{
    protected override string GoLiveSetting => "the first of november";
}

/// <summary>Task 052c (review F4): a go-live that does not parse leaves the pass off: the request is refused and no run starts.</summary>
[Trait("status", "new")]
public sealed class RecordRepairPassTriggerGoLiveInvalidTests : IClassFixture<RecordRepairRunGoLiveInvalidHostFixture>
{
    private readonly RecordRepairRunGoLiveInvalidHostFixture _fixture;

    public RecordRepairPassTriggerGoLiveInvalidTests(RecordRepairRunGoLiveInvalidHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WithAnUnparseableGoLive_AnAdministratorsRequest_Gets409GoLiveInvalid_AndStartsNoRun()
    {
        using var client = _fixture.CreateAdminClient();
        var now = DateTimeOffset.UtcNow;

        var response = await client.PostAsJsonAsync("/api/admin/jobs/record-repair-pass/runs", new
        {
            table = "sprk_event",
            createdFrom = now.AddDays(-10).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            createdTo = now.AddDays(-5).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
        });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, text);
        RecordRepairPassTriggerAuthTests.ReasonCode(text).Should().Be(RecordRepairRunReason.GoLiveInvalid);
        text.Should().NotContain("the first of november", "the setting's value is not echoed");
        _fixture.Store.RunRecords.Should().NotContain(r => r.JobId == RecordRepairPassJob.JobIdConstant);
    }
}

/// <summary>One captured log entry: its level, formatted message and structured properties.</summary>
public sealed record CapturedLog(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

/// <summary>A logger provider that keeps every entry the host writes.</summary>
public sealed class CapturingLogs : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];

    /// <summary>A snapshot of the entries so far.</summary>
    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value)
                : new Dictionary<string, object?>();
            lock (owner._entries)
            {
                owner._entries.Add(new CapturedLog(logLevel, formatter(state, exception), properties));
            }
        }
    }
}
