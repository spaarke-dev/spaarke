using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;
using Standing = Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync.StandingResult;
using Jit = Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync.JitResult;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 171, strengthened for the <c>mutation-nightly</c> quality alert (#1821): what
/// <see cref="SpeContainerMembershipSyncJob"/> REPORTS when it drives the two container-role passes. The passes' own effects
/// (who is granted or removed on which container) are pinned by <c>SpeContainerMembershipSyncTests</c>; this class pins the
/// verdict an operator and the scheduler act on (ADR-036 A1):
/// <list type="bullet">
/// <item>the run result: <c>Success</c> (every way a run is not clean), <c>ErrorMessage</c>, <c>ProcessedItems</c> (what the
/// passes WROTE) and <c>Duration</c>;</item>
/// <item>a pass that cannot start is a recorded fault, never a lost run: the other pass still runs, the fault is named in
/// <c>ResultJson</c> and logged at Error; only when NEITHER could start does the run throw, so the host retries;</item>
/// <item>cancellation is not a fault (the run stops), but a cancelled-looking exception while the run's own token is live is;</item>
/// <item>the one heartbeat per attempt, carrying the attempt number and each pass's counts.</item>
/// </list>
/// <para>Seam: the two passes are the job's collaborator (<see cref="SpeContainerMembershipSync"/>, both virtual), mocked here
/// so each case scripts exactly the pass outcome it is about; <see cref="TimeProvider"/> is a <see cref="FakeTimeProvider"/>.</para>
/// </summary>
public class SpeContainerMembershipSyncJobTests
{
    private static readonly Standing CleanStanding = new(0, 0, 0, 0, 0, 0, 0, false, []);
    private static readonly Jit CleanJit = new(0, 0, 0, 0, 0, 0, 0, 0, false, []);

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingLogger<SpeContainerMembershipSyncJob> _log = new();

    private readonly Mock<SpeContainerMembershipSync> _sync = new(
        new Mock<IGenericEntityService>().Object,
        new Mock<Membership>(TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<Membership>.Instance).Object,
        new Mock<ISecurableEntityRegistry>().Object,
        new Mock<IDataverseRecordShareService>().Object,
        new ExternalVisibilitySeamStub().Seam,
        new ConfigurationBuilder().Build(),
        NullLogger<SpeContainerMembershipSync>.Instance);

    private SpeContainerMembershipSyncJob NewJob()
    {
        var services = new ServiceCollection().AddScoped(_ => _sync.Object).BuildServiceProvider();
        return new SpeContainerMembershipSyncJob(services.GetRequiredService<IServiceScopeFactory>(), _time, _log);
    }

    private void StandingReturns(Standing result)
        => _sync.Setup(s => s.SyncStandingWritersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private void StandingThrows(Exception ex)
        => _sync.Setup(s => s.SyncStandingWritersAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);

    private void JitReturns(Jit result)
        => _sync.Setup(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private void JitThrows(Exception ex)
        => _sync.Setup(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);

    private Task<JobRunResult> RunAsync(int attempt = 1, CancellationToken ct = default)
        => NewJob().ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr", JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt), ct);

    // ── The verdict ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1821: a run where both passes finished clean SUCCEEDS with no error message")]
    public async Task ExecuteAsync_BothPassesClean_SucceedsWithNoErrorMessage()
    {
        StandingReturns(CleanStanding with { Containers = 4, AlreadyHeld = 6 });
        JitReturns(CleanJit with { SecureContainers = 3, Kept = 2 });

        var run = await RunAsync();

        run.Success.Should().BeTrue();
        run.ErrorMessage.Should().BeNull();
        run.ProcessedItems.Should().Be(0, "keeps and already-held users are not writes");
    }

    /// <summary>
    /// MUTATION: any of the four terms dropped, subtracted or defaulted: each is a distinct prime, so a wrong sum is a wrong
    /// number. Counted: standing grants + standing removals + JIT removals + flagged-role removals. Not counted: what was
    /// merely seen, kept, held or cleared.
    /// </summary>
    [Fact(DisplayName = "#1821: ProcessedItems is exactly what the passes WROTE — grants + standing removals + JIT removals + flagged-role removals")]
    public async Task ExecuteAsync_BothPassesWrite_ProcessedItemsSumsEveryWrite()
    {
        StandingReturns(CleanStanding with { Containers = 11, Granted = 2, AlreadyHeld = 13, Removed = 3, MarkersCleared = 17, Unknown = 0 });
        JitReturns(CleanJit with { SecureContainers = 19, GrantsSeen = 23, Removed = 5, MarkersCleared = 29, Kept = 31, ItemGrantsSeen = 37, FlaggedRoleSweep = true, FlaggedRolesRemoved = 7 });

        var run = await RunAsync();

        run.ProcessedItems.Should().Be(2 + 3 + 5 + 7);
    }

    [Fact(DisplayName = "#1821: ProcessedItems counts the passes that ran when one pass could not start (no phantom writes from the other)")]
    public async Task ExecuteAsync_StandingPassFaulted_ProcessedItemsIsTheJitPassAlone()
    {
        StandingThrows(new InvalidOperationException("business units unreadable"));
        JitReturns(CleanJit with { Removed = 5, FlaggedRolesRemoved = 7 });

        var run = await RunAsync();

        run.ProcessedItems.Should().Be(12);
    }

    [Fact(DisplayName = "#1821: ProcessedItems counts the standing pass alone when the JIT pass could not start")]
    public async Task ExecuteAsync_JitPassFaulted_ProcessedItemsIsTheStandingPassAlone()
    {
        StandingReturns(CleanStanding with { Granted = 2, Removed = 3 });
        JitThrows(new InvalidOperationException("secure records unreadable"));

        var run = await RunAsync();

        run.ProcessedItems.Should().Be(5);
    }

    /// <summary>
    /// MUTATION: each condition of the success expression dropped, or <c>&amp;&amp;</c> turned <c>||</c>. Every row is clean
    /// except the one thing it names, so a verdict that ignores that thing reads green.
    /// </summary>
    [Theory(DisplayName = "#1821: a run is NOT a success while any container failed or a pass was capped or truncated")]
    [InlineData("standing failed")]
    [InlineData("jit failed")]
    [InlineData("standing capped")]
    [InlineData("jit truncated")]
    public async Task ExecuteAsync_OnePassIncomplete_FailsWithAnErrorMessageTheOperatorCanRead(string incomplete)
    {
        StandingReturns(incomplete switch
        {
            "standing failed" => CleanStanding with { Failed = 1 },
            "standing capped" => CleanStanding with { Capped = true },
            _ => CleanStanding,
        });
        JitReturns(incomplete switch
        {
            "jit failed" => CleanJit with { Failed = 1 },
            "jit truncated" => CleanJit with { Truncated = true },
            _ => CleanJit,
        });

        var run = await RunAsync();

        run.Success.Should().BeFalse(incomplete);
        run.ErrorMessage.Should().NotBeNullOrWhiteSpace(incomplete).And.Contain("ResultJson", "the message points the operator at the detail");
    }

    [Fact(DisplayName = "#1821: a clean run is not marked failed because the other pass wrote — writes alone never fail a run")]
    public async Task ExecuteAsync_PassesWroteAndAreComplete_Succeeds()
    {
        StandingReturns(CleanStanding with { Granted = 2, Removed = 3 });
        JitReturns(CleanJit with { Removed = 5, FlaggedRolesRemoved = 7 });

        var run = await RunAsync();

        run.Success.Should().BeTrue();
        run.ErrorMessage.Should().BeNull();
    }

    [Fact(DisplayName = "#1821: ResultJson carries both passes' results (camelCase) and no fault when none occurred")]
    public async Task ExecuteAsync_BothPassesReturn_ResultJsonCarriesBothResults()
    {
        StandingReturns(CleanStanding with { Containers = 4, Granted = 2, Problems = ["a problem"] });
        JitReturns(CleanJit with { SecureContainers = 3, Removed = 5 });

        var run = await RunAsync();

        using var doc = JsonDocument.Parse(run.ResultJson!);
        var root = doc.RootElement;
        root.GetProperty("standing").GetProperty("containers").GetInt32().Should().Be(4);
        root.GetProperty("standing").GetProperty("granted").GetInt32().Should().Be(2);
        root.GetProperty("standing").GetProperty("problems")[0].GetString().Should().Be("a problem");
        root.GetProperty("jit").GetProperty("secureContainers").GetInt32().Should().Be(3);
        root.GetProperty("jit").GetProperty("removed").GetInt32().Should().Be(5);
        root.GetProperty("standingFault").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("jitFault").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "#1821: Duration is the elapsed time of both passes, and the heartbeat reports it in milliseconds")]
    public async Task ExecuteAsync_PassesTakeTime_DurationIsTheirElapsedTime()
    {
        _sync.Setup(s => s.SyncStandingWritersAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { _time.Advance(TimeSpan.FromSeconds(3)); return Task.FromResult(CleanStanding); });
        _sync.Setup(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { _time.Advance(TimeSpan.FromMilliseconds(4500)); return Task.FromResult(CleanJit); });

        var run = await RunAsync();

        run.Duration.Should().Be(TimeSpan.FromMilliseconds(7500));
        Heartbeat().Should().Contain("durationMs=7500");
    }

    // ── A pass that cannot start ───────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1821: when the standing pass cannot start the JIT pass still runs; the run fails, names the fault, and logs it at Error")]
    public async Task ExecuteAsync_StandingPassCannotStart_JitPassStillRunsAndTheFaultIsRecorded()
    {
        var fault = new InvalidOperationException("business units unreadable");
        StandingThrows(fault);
        JitReturns(CleanJit with { SecureContainers = 3, Removed = 5 });

        var run = await RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        _sync.Verify(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("standing").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("standingFault").GetString().Should().Be(nameof(InvalidOperationException));
        doc.RootElement.GetProperty("jit").GetProperty("removed").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("jitFault").ValueKind.Should().Be(JsonValueKind.Null);
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Should().Match<LogEntry>(e => ReferenceEquals(e.Exception, fault) && e.Message.Contains("standing-writer pass could not start"));
    }

    [Fact(DisplayName = "#1821: when the JIT pass cannot start the standing pass's work stands; the run fails, names the fault, and logs it at Error")]
    public async Task ExecuteAsync_JitPassCannotStart_StandingResultIsKeptAndTheFaultIsRecorded()
    {
        var fault = new TimeoutException("secure records unreadable");
        StandingReturns(CleanStanding with { Containers = 4, Granted = 2 });
        JitThrows(fault);

        var run = await RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("standing").GetProperty("granted").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("standingFault").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("jit").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("jitFault").GetString().Should().Be(nameof(TimeoutException));
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Should().Match<LogEntry>(e => ReferenceEquals(e.Exception, fault) && e.Message.Contains("just-in-time removal pass could not start"));
    }

    [Fact(DisplayName = "#1821: when NEITHER pass can start the run THROWS (so the host retries), carrying both faults, and the heartbeat is still logged")]
    public async Task ExecuteAsync_NeitherPassCanStart_ThrowsWithBothFaultsForTheHostToRetry()
    {
        var standingFault = new InvalidOperationException("business units unreadable");
        var jitFault = new TimeoutException("secure records unreadable");
        StandingThrows(standingFault);
        JitThrows(jitFault);

        var act = () => RunAsync(attempt: 2);

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().Contain("could not start either pass");
        thrown.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().Equal(standingFault, jitFault);
        _log.Entries.Count(e => e.Level == LogLevel.Error).Should().Be(2);
        Heartbeat().Should().Contain("attempt=2");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUTATION: the exception filter's <c>||</c> to <c>&amp;&amp;</c>, <c>is not</c> to <c>is</c>, or the <c>!</c> dropped.
    /// A cancelled run stops (the host sees the cancellation); it is not recorded as a pass that "could not start".
    /// </summary>
    [Fact(DisplayName = "#1821: a cancelled run during the standing pass stops with the cancellation — the JIT pass does not run and no fault is recorded")]
    public async Task ExecuteAsync_CancelledDuringStandingPass_ThrowsCancellationAndSkipsTheJitPass()
    {
        using var cts = new CancellationTokenSource();
        StandingCancels(cts);

        var act = () => RunAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _sync.Verify(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "#1821: a cancelled run during the JIT pass stops with the cancellation — it is not recorded as a fault and no result is reported")]
    public async Task ExecuteAsync_CancelledDuringJitPass_ThrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        StandingReturns(CleanStanding with { Granted = 2 });
        _sync.Setup(s => s.RemoveRevokedJitGrantsAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { cts.Cancel(); return Task.FromException<Jit>(new OperationCanceledException(cts.Token)); });

        var act = () => RunAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "#1821 (must not fire): a cancellation-shaped exception while the run's own token is LIVE (a Graph or Dataverse timeout) is a recorded fault, not a stop")]
    public async Task ExecuteAsync_TimeoutShapedCancellationWithLiveToken_IsARecordedFault()
    {
        StandingThrows(new TaskCanceledException("the request timed out"));
        JitThrows(new OperationCanceledException("the request timed out"));

        // Both passes faulted with a live token: the run throws for the host's retry (it did not stop as a cancellation).
        var act = () => RunAsync();

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ((AggregateException)thrown.InnerException!).InnerExceptions.Should().HaveCount(2);
        _log.Entries.Count(e => e.Level == LogLevel.Error).Should().Be(2);
    }

    [Fact(DisplayName = "#1821 (must not fire): a timeout-shaped exception in one pass with a live token does not stop the other pass and names its type")]
    public async Task ExecuteAsync_TimeoutShapedCancellationInOnePass_TheOtherPassStillRuns()
    {
        StandingReturns(CleanStanding with { Granted = 2 });
        JitThrows(new TaskCanceledException("the request timed out"));

        var run = await RunAsync();

        run.Success.Should().BeFalse();
        run.ProcessedItems.Should().Be(2);
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("jitFault").GetString().Should().Be(nameof(TaskCanceledException));
    }

    [Fact(DisplayName = "#1821 (must not fire): an ordinary fault is recorded even after the run's token was cancelled")]
    public async Task ExecuteAsync_OrdinaryFaultAfterCancellation_IsStillARecordedFault()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _sync.Setup(s => s.SyncStandingWritersAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        JitReturns(CleanJit);

        var run = await RunAsync(ct: cts.Token);

        run.Success.Should().BeFalse();
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("standingFault").GetString().Should().Be(nameof(InvalidOperationException));
    }

    // ── The heartbeat ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1821: one heartbeat per attempt at Information, carrying the attempt number and both passes' counts")]
    public async Task ExecuteAsync_Run_LogsOneInformationHeartbeatWithAttemptAndCounts()
    {
        StandingReturns(CleanStanding with { Containers = 4, Granted = 2, AlreadyHeld = 6, Removed = 3, Unknown = 8, Failed = 1, Capped = true });
        JitReturns(CleanJit with { SecureContainers = 9, GrantsSeen = 10, ItemGrantsSeen = 12, Removed = 5, Kept = 14, Unknown = 15, Failed = 16, Unresolvable = 18, Truncated = true, FlaggedRoleSweep = true, FlaggedRolesRemoved = 7 });

        await RunAsync(attempt: 3);

        var heartbeat = _log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat")).Which;
        heartbeat.Level.Should().Be(LogLevel.Information);
        heartbeat.Message.Should().Contain("attempt=3")
            .And.Contain("standing={containers=4, granted=2, alreadyHeld=6, removed=3, unknown=8, failed=1, capped=True}")
            .And.Contain("jit={secureContainers=9, grants=10, itemGrants=12, removed=5, kept=14, unknown=15, failed=16, unresolvable=18, truncated=True, flaggedRoleSweep=True, flaggedRolesRemoved=7}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private void StandingCancels(CancellationTokenSource cts)
        => _sync.Setup(s => s.SyncStandingWritersAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { cts.Cancel(); return Task.FromException<Standing>(new OperationCanceledException(cts.Token)); });

    private string Heartbeat() => _log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat")).Which.Message;

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
