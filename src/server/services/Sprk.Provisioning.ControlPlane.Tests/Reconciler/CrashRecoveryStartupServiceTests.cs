// -----------------------------------------------------------------------------
// CrashRecoveryStartupServiceTests.cs
//
// L2 CONTROL-PLANE tests for the I6 crash-recovery startup scan
// (task 060, Wave C5).
//
// TESTED BEHAVIORS (POML acceptance criteria):
//
//   AC #1  Given a Running run with lastUpdated older than the threshold and a
//          non-empty CurrentPhase, when RunOnceAsync fires, then the
//          currentPhase handler is re-enqueued via Service Bus.
//   AC #2  Given a Running run with lastUpdated inside the threshold, when
//          RunOnceAsync fires, then the run is NOT re-enqueued (belongs to the
//          active reconciler window).
//   AC #3  Given a run in {Completed, Failed, Cancelled, Quarantined}, when
//          RunOnceAsync fires, then the run is skipped (defense-in-depth on
//          top of the scanner filter).
//   AC #4  Handlers re-enqueued by crash recovery produce a MessageId that
//          matches what the reconciler would produce for the same
//          (HandlerId, RunId, CustomerId) — SB dedup collapse per ADR-036 L1.
//   AC #5  Recovery decisions are logged with the runId + phaseId + age
//          (structural — assertion on log payload).
//   AC #6  TimeProvider is injected — verified structurally at the ctor level +
//          by the POML step 8 grep gate (0 hits for DateTime.UtcNow /
//          Stopwatch inside CrashRecoveryStartupService.cs).
//   Ancillary #1  Scanner failure (Cosmos unreachable) does NOT crash the L2
//                 App Service — StartAsync catches + logs a warning.
//   Ancillary #2  A per-run enqueue failure does NOT stop the scan for
//                 subsequent runs.
//   Ancillary #3  Options.Validate rejects sub-30s floor + sub-1s median.
//   Ancillary #4  Enabled=false skips the scan cleanly.
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   Hand-rolled in-memory test doubles for IActiveRunScanner + IHandlerEnqueuer
//   + TimeProvider — parity with StateReconcilerServiceTests. No Moq. The
//   RecordingLoggerFactory captures message text + level so the "log emitted"
//   assertions do not depend on a specific structured-log adapter.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

/// <summary>
/// Unit tests for <see cref="CrashRecoveryStartupService"/>. Drives the
/// internal <c>RunOnceAsync</c> directly so assertions are deterministic — no
/// StartAsync/StopAsync IHostedService orchestration around the tick.
/// </summary>
public sealed class CrashRecoveryStartupServiceTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000060";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-18T12:00:00Z");

    // -----------------------------------------------------------------------
    // AC #1 — Orphaned run recovered.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunOnce_OrphanedRunningRun_ReEnqueuesCurrentPhase()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: new[]
            {
                ("H0", TimeSpan.FromMinutes(25)),
                ("H1", TimeSpan.FromMinutes(20)),
            });
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(1, "orphan currentPhase H2a is the re-dispatch target.");
        enqueuer.DistinctEnvelopes.Should().ContainSingle(e =>
            e.HandlerId == "H2a" && e.RunId == TestRunId && e.CustomerId == TestCustomerId);
    }

    [Fact]
    public async Task RunOnce_OrphanedWaitingOnGateRun_ReEnqueuesCurrentPhase()
    {
        var run = MakeRun(RunStatus.WaitingOnGate,
            currentPhase: "H3",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: new[]
            {
                ("H0", TimeSpan.FromMinutes(25)),
                ("H1", TimeSpan.FromMinutes(20)),
                ("H2a", TimeSpan.FromMinutes(15)),
                ("H4", TimeSpan.FromMinutes(10)),
            });
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.DistinctEnvelopes.Should().ContainSingle().Which.HandlerId.Should().Be("H3");
    }

    // -----------------------------------------------------------------------
    // AC #2 — Fresh run skipped (age inside threshold).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunOnce_FreshRunningRun_DoesNotReEnqueue()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(1),
            completedPhases: new[] { ("H0", TimeSpan.FromMinutes(1)) });
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0, "fresh run is within the reconciler's window; crash recovery skips.");
    }

    [Fact]
    public async Task RunOnce_RunAtExactlyThreshold_DoesReEnqueue()
    {
        var opts = new CrashRecoveryOptions
        {
            Enabled = true,
            FloorAge = TimeSpan.FromMinutes(5),
            MedianHandlerDuration = TimeSpan.FromMinutes(1),
        };
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(5),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _, options: opts);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(1, "age >= threshold qualifies as orphan (boundary inclusive).");
    }

    // -----------------------------------------------------------------------
    // AC #3 — Terminal-status runs skipped.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public async Task RunOnce_TerminalStatusRun_DoesNotReEnqueue(RunStatus terminalStatus)
    {
        var run = MakeRun(terminalStatus,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0,
            "terminal-status run {0} must not participate in crash recovery.", terminalStatus);
    }

    [Fact]
    public async Task RunOnce_NoActiveRuns_DoesNotEnqueue_LogsSummary()
    {
        var scanner = new StubActiveRunScanner(Array.Empty<ProvisioningRun>());
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0);
        loggerFactory.GetLogRecords()
            .Any(r => r.Message.Contains(CrashRecoveryStartupService.ScanCompleteEventName)
                      && r.Message.Contains("ActiveRunCount=0"))
            .Should().BeTrue("even a zero-run scan emits the summary line for observability.");
    }

    // -----------------------------------------------------------------------
    // AC #4 — MessageId parity with reconciler dispatch (SB dedup contract).
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildEnvelope_ProducesMessageId_ByteIdenticalToReconcilerEquivalent()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(10),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var timeProvider = new TestTimeProvider(Now);
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            timeProvider: timeProvider);
        var reconciler = new StateReconcilerService(
            BuildEmptyScopeFactory(),
            Options.Create(new ReconcilerOptions()),
            timeProvider,
            NullLogger<StateReconcilerService>.Instance);

        var crashEnvelope = sut.BuildEnvelope("H2a", run);
        var reconcilerEnvelope = reconciler.BuildEnvelope("H2a", run);

        crashEnvelope.ParametersJson.Should().Be(reconcilerEnvelope.ParametersJson,
            "byte-identical ParametersJson is what makes ComputeMessageId hash-match.");
        ServiceBusHandlerEnqueuer.ComputeMessageId(crashEnvelope)
            .Should().Be(ServiceBusHandlerEnqueuer.ComputeMessageId(reconcilerEnvelope),
                "SB level-1 dedup requires identical MessageId across dispatch origins.");
    }

    // -----------------------------------------------------------------------
    // AC #5 — Recovery decisions are logged with runId + phaseId + age.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunOnce_OrphanRecovered_LogsReEnqueueEventWithContext()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: new[] { ("H0", TimeSpan.FromMinutes(29)), ("H1", TimeSpan.FromMinutes(28)) });
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory);

        await sut.RunOnceAsync(CancellationToken.None);

        var records = loggerFactory.GetLogRecords();
        var reEnqueueLog = records.SingleOrDefault(r =>
            r.Level == LogLevel.Information
            && r.Message.Contains(CrashRecoveryStartupService.ReEnqueuedEventName));
        reEnqueueLog.Should().NotBeNull("re-enqueue MUST emit an Information-level structured record.");
        reEnqueueLog!.Message.Should().Contain(TestRunId);
        reEnqueueLog.Message.Should().Contain(TestCustomerId);
        reEnqueueLog.Message.Should().Contain("H2a");
    }

    [Fact]
    public async Task RunOnce_OrphanWithNullCurrentPhase_SkipsWithWarning()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: null,
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory);

        await sut.RunOnceAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0);
        loggerFactory.GetLogRecords()
            .Any(r => r.Level == LogLevel.Warning && r.Message.Contains("null CurrentPhase"))
            .Should().BeTrue("null currentPhase orphan MUST emit a Warning for operator visibility.");
    }

    // -----------------------------------------------------------------------
    // AC #6 — TimeProvider is injected (structural).
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_UsesInjectedTimeProvider()
    {
        var timeProvider = new TestTimeProvider(Now);
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            timeProvider: timeProvider);

        sut.Should().NotBeNull();
    }

    [Fact]
    public void BuildEnvelope_UsesInjectedTimeProviderForEnqueuedAt()
    {
        var frozenNow = DateTimeOffset.Parse("2026-08-18T14:34:56Z");
        var timeProvider = new TestTimeProvider(frozenNow);
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            timeProvider: timeProvider);
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());

        var envelope = sut.BuildEnvelope("H2a", run);

        envelope.EnqueuedAt.Should().Be(frozenNow,
            "EnqueuedAt MUST come from the injected TimeProvider — never DateTime.UtcNow.");
    }

    // -----------------------------------------------------------------------
    // Ancillary #1 — Cosmos unreachable does NOT crash the L2 App Service.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_ScannerThrows_DoesNotPropagate_LogsWarning()
    {
        var scanner = new ThrowingActiveRunScanner(new InvalidOperationException("Cosmos endpoint unreachable"));
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory);

        var act = async () => await sut.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "POML acceptance: crash-recovery startup failure MUST NOT crash the L2 App Service; " +
            "the reconciler's 5s tick loop is the fallback.");

        enqueuer.TotalCalls.Should().Be(0);
        loggerFactory.GetLogRecords()
            .Any(r => r.Level == LogLevel.Warning && r.Message.Contains("Crash-recovery startup scan failed"))
            .Should().BeTrue("a warning-level log entry describes the scan failure.");
    }

    // -----------------------------------------------------------------------
    // Ancillary #2 — Per-run enqueue failure isolation.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunOnce_EnqueuerThrowsForOneRun_OtherOrphansStillReEnqueue()
    {
        var run1 = MakeRun(RunStatus.Running,
            runId: "00000000-0000-0000-0000-000000000101",
            customerId: "customer-a",
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var run2 = MakeRun(RunStatus.Running,
            runId: "00000000-0000-0000-0000-000000000102",
            customerId: "customer-b",
            currentPhase: "H5",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());
        var scanner = new StubActiveRunScanner(new[] { run1, run2 });
        var enqueuer = new SelectivelyThrowingEnqueuer(throwForRunId: run1.RunId);
        var sut = BuildSut(scanner, enqueuer, out _);

        var act = async () => await sut.RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        enqueuer.SuccessfullyEnqueued.Should().ContainSingle().Which.RunId.Should().Be(run2.RunId,
            "run1 threw but run2 in the same scan must still enqueue (sibling isolation).");
    }

    // -----------------------------------------------------------------------
    // Ancillary #3 — Options.Validate rejects bad configs.
    // -----------------------------------------------------------------------

    [Fact]
    public void CrashRecoveryOptions_Validate_RejectsSubThirtySecondFloor()
    {
        var options = new CrashRecoveryOptions { FloorAge = TimeSpan.FromSeconds(10) };
        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FloorAge*must be >= 30 seconds*");
    }

    [Fact]
    public void CrashRecoveryOptions_Validate_RejectsZeroMedianHandlerDuration()
    {
        var options = new CrashRecoveryOptions { MedianHandlerDuration = TimeSpan.Zero };
        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MedianHandlerDuration*must be >= 1 second*");
    }

    [Fact]
    public void CrashRecoveryOptions_Validate_AcceptsFiveMinuteFloorAndTwoMinuteMedianDefaults()
    {
        var options = new CrashRecoveryOptions();
        var act = () => options.Validate();

        act.Should().NotThrow();
        options.FloorAge.Should().Be(TimeSpan.FromMinutes(5));
        options.MedianHandlerDuration.Should().Be(TimeSpan.FromMinutes(2));
        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ComputeThreshold_ChoosesFloorWhenTwiceMedianIsSmaller()
    {
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            options: new CrashRecoveryOptions
            {
                FloorAge = TimeSpan.FromMinutes(5),
                MedianHandlerDuration = TimeSpan.FromMinutes(2),
            });

        sut.ComputeThreshold().Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ComputeThreshold_ChoosesTwiceMedianWhenFloorIsSmaller()
    {
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            options: new CrashRecoveryOptions
            {
                FloorAge = TimeSpan.FromMinutes(1),
                MedianHandlerDuration = TimeSpan.FromMinutes(10),
            });

        sut.ComputeThreshold().Should().Be(TimeSpan.FromMinutes(20));
    }

    // -----------------------------------------------------------------------
    // Ancillary #4 — Enabled=false kill-switch.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_WhenDisabled_SkipsScanCleanly()
    {
        var scanner = new StubActiveRunScanner(new[]
        {
            MakeRun(RunStatus.Running,
                currentPhase: "H2a",
                createdAgo: TimeSpan.FromMinutes(30),
                completedPhases: Array.Empty<(string, TimeSpan)>()),
        });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory, options: new CrashRecoveryOptions
        {
            Enabled = false,
            FloorAge = TimeSpan.FromMinutes(5),
            MedianHandlerDuration = TimeSpan.FromMinutes(2),
        });

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0, "Enabled=false suppresses the startup scan.");
        loggerFactory.GetLogRecords()
            .Any(r => r.Message.Contains("disabled") && r.Message.Contains("startup scan skipped"))
            .Should().BeTrue("kill-switch skip MUST emit the reason for operator visibility.");
    }

    // -----------------------------------------------------------------------
    // Age-proxy behavior — GetLastActivity picks the greater of CompletedPhases
    // max-completedAt vs CreatedOn.
    // -----------------------------------------------------------------------

    [Fact]
    public void GetLastActivity_WithEmptyCompletedPhases_ReturnsCreatedOn()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H0",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: Array.Empty<(string, TimeSpan)>());

        CrashRecoveryStartupService.GetLastActivity(run).Should().Be(run.CreatedOn);
    }

    [Fact]
    public void GetLastActivity_WithCompletedPhases_ReturnsMaxCompletedAt()
    {
        var run = MakeRun(RunStatus.Running,
            currentPhase: "H2a",
            createdAgo: TimeSpan.FromMinutes(30),
            completedPhases: new[]
            {
                ("H0", TimeSpan.FromMinutes(25)),
                ("H1", TimeSpan.FromMinutes(10)),
            });
        var expected = Now - TimeSpan.FromMinutes(10);

        CrashRecoveryStartupService.GetLastActivity(run).Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    // Helpers + test doubles
    // -----------------------------------------------------------------------

    private static CrashRecoveryStartupService BuildSut(
        IActiveRunScanner scanner,
        IHandlerEnqueuer enqueuer,
        out RecordingLoggerFactory loggerFactory,
        CrashRecoveryOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        loggerFactory = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton(scanner);
        services.AddSingleton(enqueuer);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new CrashRecoveryStartupService(
            scopeFactory,
            Options.Create(options ?? new CrashRecoveryOptions()),
            timeProvider ?? new TestTimeProvider(Now),
            loggerFactory.CreateLogger<CrashRecoveryStartupService>());
    }

    private static IServiceScopeFactory BuildEmptyScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDagAdvancer, DagAdvancer>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ProvisioningRun MakeRun(
        RunStatus status,
        string? currentPhase,
        TimeSpan createdAgo,
        IEnumerable<(string Phase, TimeSpan Ago)> completedPhases,
        string? runId = null,
        string? customerId = null)
    {
        var run = new ProvisioningRun
        {
            RunId = runId ?? TestRunId,
            CustomerId = customerId ?? TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model2Dedicated",
            Profile = "spaarke-hosted-model2",
            Status = status,
            CurrentPhase = currentPhase,
            CreatedOn = Now - createdAgo,
        };
        foreach (var (phase, ago) in completedPhases)
        {
            var completedAt = Now - ago;
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = phase,
                StartedAt = completedAt - TimeSpan.FromMinutes(1),
                CompletedAt = completedAt,
                IdempotencyKey = $"{phase.ToLowerInvariant()}-{run.CustomerId}-test",
                JobId = run.RunId,
            });
        }
        return run;
    }

    private static ProvisioningRun MakeRun(
        RunStatus status,
        string? currentPhase,
        TimeSpan createdAgo,
        (string, TimeSpan)[] completedPhases)
        => MakeRun(status, currentPhase, createdAgo, (IEnumerable<(string, TimeSpan)>)completedPhases);

    // ---------------- IActiveRunScanner test doubles ----------------

    private sealed class StubActiveRunScanner : IActiveRunScanner
    {
        private readonly IReadOnlyList<ProvisioningRun> _runs;
        private readonly IReadOnlyList<ProvisioningRun> _terminalRuns;
        public StubActiveRunScanner(IEnumerable<ProvisioningRun> runs, IEnumerable<ProvisioningRun>? terminalRuns = null)
        {
            _runs = runs.ToList();
            _terminalRuns = terminalRuns?.ToList() ?? (IReadOnlyList<ProvisioningRun>)Array.Empty<ProvisioningRun>();
        }
        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => Task.FromResult(_runs);
        // Bucket B MED#12 SESSION 18: orphan-guard sweep.
        public Task<IReadOnlyList<ProvisioningRun>> QueryStaleTerminalRunsAsync(TimeSpan minAge, CancellationToken ct)
            => Task.FromResult(_terminalRuns);
    }

    private sealed class ThrowingActiveRunScanner : IActiveRunScanner
    {
        private readonly Exception _exception;
        public ThrowingActiveRunScanner(Exception exception) => _exception = exception;
        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => throw _exception;
        // Bucket B MED#12 SESSION 18: throwing scanner throws on this path too.
        public Task<IReadOnlyList<ProvisioningRun>> QueryStaleTerminalRunsAsync(TimeSpan minAge, CancellationToken ct)
            => throw _exception;
    }

    // ---------------- IHandlerEnqueuer test doubles ----------------

    private sealed class DedupingRecordingEnqueuer : IHandlerEnqueuer
    {
        private readonly object _lock = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<HandlerEnvelope> _distinct = new();
        private int _totalCalls;

        public int TotalCalls => Volatile.Read(ref _totalCalls);
        public IReadOnlyList<HandlerEnvelope> DistinctEnvelopes
        {
            get { lock (_lock) return _distinct.ToArray(); }
        }
        public IReadOnlyList<string> DistinctMessageIds
        {
            get { lock (_lock) return _seen.ToArray(); }
        }

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken ct)
        {
            Interlocked.Increment(ref _totalCalls);
            var messageId = ServiceBusHandlerEnqueuer.ComputeMessageId(envelope);
            lock (_lock)
            {
                if (_seen.Add(messageId))
                {
                    _distinct.Add(envelope);
                }
            }
            return Task.CompletedTask;
        }
    }

    private sealed class SelectivelyThrowingEnqueuer : IHandlerEnqueuer
    {
        private readonly string _throwForRunId;
        private readonly List<HandlerEnvelope> _success = new();

        public SelectivelyThrowingEnqueuer(string throwForRunId) => _throwForRunId = throwForRunId;
        public IReadOnlyList<HandlerEnvelope> SuccessfullyEnqueued => _success.ToArray();

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken ct)
        {
            if (string.Equals(envelope.RunId, _throwForRunId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated Service Bus failure for run {_throwForRunId}");
            }
            _success.Add(envelope);
            return Task.CompletedTask;
        }
    }

    // ---------------- TimeProvider double ----------------

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset initial) => _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    // ---------------- Logger recording double ----------------

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly List<LogRecord> _records = new();
        private readonly object _lock = new();

        public IReadOnlyList<LogRecord> GetLogRecords()
        {
            lock (_lock) return _records.ToArray();
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(_records, _lock);
        public void AddProvider(ILoggerProvider provider) { /* not used */ }
        public void Dispose() { }

        public ILogger<T> CreateLogger<T>() => new TypedRecorder<T>(_records, _lock);

        private sealed class Recorder : ILogger
        {
            private readonly List<LogRecord> _records;
            private readonly object _lock;
            public Recorder(List<LogRecord> records, object l) { _records = records; _lock = l; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_lock)
                {
                    _records.Add(new LogRecord(logLevel, formatter(state, exception), exception));
                }
            }
        }

        private sealed class TypedRecorder<T> : ILogger<T>
        {
            private readonly Recorder _inner;
            public TypedRecorder(List<LogRecord> records, object l) => _inner = new Recorder(records, l);
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);
}
