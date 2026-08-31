// -----------------------------------------------------------------------------
// StateReconcilerServiceTests.cs
//
// L2 CONTROL-PLANE tests for the state-reconciler BackgroundService
// (task 058, Wave C5).
//
// TESTED BEHAVIORS (POML acceptance criteria):
//
//   AC #2  Given active run with completedPhases = [H0], reconciler enqueues
//          H1 via Service Bus with a deterministic MessageId.
//   AC #3  Given TWO reconciler instances polling concurrently on the SAME
//          run, EXACTLY ONE Service Bus message survives (verified via
//          MessageId-dedup mock — level-1 idempotency per FR-22).
//   AC #4  Given a run with terminal status, the reconciler does NOT enqueue
//          (defense-in-depth — the scanner filter should already exclude, but
//          the advancer also refuses).
//   AC #5  Reconciler uses TimeProvider (injected) — verified structurally at
//          the ctor level + by the POML step 8 grep gate.
//   AC #6  Cosmos-unreachable does NOT crash the L2 App Service — the tick
//          logs a warning and returns cleanly; the next tick retries.
//   Ancillary: enqueue failure on ONE handler does not block SIBLINGS in the
//              same tick.
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   All three collaborator seams (<see cref="IActiveRunScanner"/>,
//   <see cref="IDagAdvancer"/>, <see cref="IHandlerEnqueuer"/>) are hand-
//   rolled in-memory test doubles. No Moq for these — the seams are simple
//   enough that a purpose-built double reads better + matches the L2
//   convention (see H14SecretRedactionTests etc.). A local
//   <see cref="TestTimeProvider"/> avoids the
//   Microsoft.Extensions.TimeProvider.Testing package (not referenced in the
//   test csproj; local 3-line double is the tests/CLAUDE.md-approved pattern
//   per <c>InMemoryTenantTokenLedgerTests.MutableTimeProvider</c>).
//
// The MessageId-dedup enqueuer replicates the Service Bus wire-level dedup
// semantics: two calls with an identical envelope resolve to the same
// deterministic MessageId (per <see cref="ServiceBusHandlerEnqueuer.ComputeMessageId"/>);
// the mock's HashSet retains ONE per unique key. This is EXACTLY the
// production dedup contract at unit-test resolution.
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
/// Unit tests for <see cref="StateReconcilerService"/>. Drives the internal
/// <c>RunTickAsync</c> directly so the assertions are deterministic — no
/// PeriodicTimer time-dependence.
/// </summary>
public sealed class StateReconcilerServiceTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";

    // -----------------------------------------------------------------------
    // AC #2 — Given completedPhases = [H0], reconciler enqueues H1 with a
    //         deterministic MessageId.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Tick_WithH0Completed_EnqueuesH1WithDeterministicMessageId()
    {
        // Arrange
        var run = MakeRun(RunStatus.Running, "H0");
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        // Act
        await sut.RunTickAsync(CancellationToken.None);

        // Assert
        enqueuer.TotalCalls.Should().Be(1, "H0 completed unlocks exactly H1.");
        enqueuer.DistinctEnvelopes.Should().ContainSingle(e =>
            e.HandlerId == "H1" && e.RunId == TestRunId && e.CustomerId == TestCustomerId);

        var expectedMessageId = ServiceBusHandlerEnqueuer.ComputeMessageId(enqueuer.DistinctEnvelopes[0]);
        enqueuer.DistinctMessageIds.Should().ContainSingle().Which.Should().Be(expectedMessageId,
            "level-1 idempotency: MessageId is deterministic per (HandlerId, RunId, CustomerId, paramHash).");
    }

    [Fact]
    public async Task Tick_WithH2aCompleted_EnqueuesH2b_H4_H5_ThreeWayFanOut()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunTickAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(3);
        enqueuer.DistinctEnvelopes.Select(e => e.HandlerId).Should().BeEquivalentTo(
            new[] { "H2b", "H4", "H5" },
            "design.md §4.1 3-way fan-out after H2a.");
    }

    // -----------------------------------------------------------------------
    // AC #3 — Concurrency: two reconciler instances polling the same run
    //          -> exactly one Service Bus message survives dedup.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Tick_TwoConcurrentInstancesSameRun_ProducesExactlyOneDistinctMessageIdPerReadyHandler()
    {
        // Arrange — SHARED dedup-enqueuer mimics Service Bus wire-level dedup.
        // Both reconciler instances get their own DI graph but share the SAME
        // enqueuer instance (same Service Bus queue in production).
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");
        var sharedEnqueuer = new DedupingRecordingEnqueuer();

        var scanner1 = new StubActiveRunScanner(new[] { run });
        var scanner2 = new StubActiveRunScanner(new[] { run });
        var sut1 = BuildSut(scanner1, sharedEnqueuer, out _);
        var sut2 = BuildSut(scanner2, sharedEnqueuer, out _);

        // Act — both reconcilers tick concurrently.
        await Task.WhenAll(
            sut1.RunTickAsync(CancellationToken.None),
            sut2.RunTickAsync(CancellationToken.None));

        // Assert — both instances invoked EnqueueAsync for each of the 3
        // ready handlers (6 total call attempts) but the dedup-enqueuer
        // collapses to 3 distinct MessageIds. This is EXACTLY the production
        // Service Bus dedup contract: identical envelopes -> identical
        // MessageId -> queue retains one.
        sharedEnqueuer.TotalCalls.Should().Be(6,
            "each of 2 reconciler instances sends 3 envelopes.");
        sharedEnqueuer.DistinctMessageIds.Should().HaveCount(3,
            "level-1 idempotency at Service Bus wire dedups the 6 calls to 3 unique messages " +
            "(one per ready handler H2b/H4/H5).");
        sharedEnqueuer.DistinctEnvelopes.Select(e => e.HandlerId).Should().BeEquivalentTo(
            new[] { "H2b", "H4", "H5" });
    }

    [Fact]
    public async Task Tick_FiveConcurrentInstancesSameRun_StillProducesOneDistinctMessageIdPerReadyHandler()
    {
        // Stress the concurrency guard at N=5 (production scale-out ceiling).
        var run = MakeRun(RunStatus.Running, "H0");
        var sharedEnqueuer = new DedupingRecordingEnqueuer();

        var reconcilers = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var scanner = new StubActiveRunScanner(new[] { run });
                return BuildSut(scanner, sharedEnqueuer, out _);
            })
            .ToArray();

        await Task.WhenAll(reconcilers.Select(r => r.RunTickAsync(CancellationToken.None)));

        sharedEnqueuer.TotalCalls.Should().Be(5, "5 reconcilers each dispatch H1 once.");
        sharedEnqueuer.DistinctMessageIds.Should().ContainSingle(
            "level-1 idempotency collapses all 5 identical dispatches to a single Service Bus message.");
        sharedEnqueuer.DistinctEnvelopes.Should().ContainSingle().Which.HandlerId.Should().Be("H1");
    }

    // -----------------------------------------------------------------------
    // AC #4 — Terminal runs are skipped (scanner filter should exclude them,
    //          but defense-in-depth: the advancer also returns empty).
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public async Task Tick_TerminalStatusRun_DoesNotEnqueue(RunStatus terminalStatus)
    {
        // If the scanner accidentally returns a terminal-status run (e.g. a
        // race where status advanced between scan projection and read), the
        // reconciler MUST still skip it.
        var run = MakeRun(terminalStatus, "H0", "H1");  // H2a would be ready if Running
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunTickAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0,
            "terminal-status run {0} must not participate in DAG advancement.", terminalStatus);
    }

    [Fact]
    public async Task Tick_NoActiveRuns_DoesNotEnqueue()
    {
        var scanner = new StubActiveRunScanner(Array.Empty<ProvisioningRun>());
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _);

        await sut.RunTickAsync(CancellationToken.None);

        enqueuer.TotalCalls.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // AC #6 — Cosmos unreachable does NOT crash the reconciler.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Tick_ScannerThrows_DoesNotPropagateException_LogsWarning()
    {
        // Scanner represents "Cosmos unreachable" — throws on QueryActiveRunsAsync.
        var scanner = new ThrowingActiveRunScanner(new InvalidOperationException("Cosmos endpoint unreachable"));
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out var loggerFactory);

        // Act — MUST NOT throw.
        var act = async () => await sut.RunTickAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "POML acceptance #6: reconciler tick failure MUST NOT crash the L2 App Service; " +
            "logs a warning and retries next tick.");

        // Assert — no enqueue attempt; warning was logged.
        enqueuer.TotalCalls.Should().Be(0);
        var records = loggerFactory.GetLogRecords();
        records.Any(r => r.Level == LogLevel.Warning && r.Message.Contains("Reconciler tick failed"))
            .Should().BeTrue("a warning-level log entry SHOULD describe the tick failure.");
    }

    [Fact]
    public async Task Tick_EnqueuerThrowsForOneHandler_OtherHandlersStillEnqueue()
    {
        // Sibling-isolation: an enqueue failure on one handler in a 3-way
        // fan-out must not block the other two.
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new SelectivelyThrowingEnqueuer(throwForHandlerId: "H4");
        var sut = BuildSut(scanner, enqueuer, out _);

        var act = async () => await sut.RunTickAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        enqueuer.SuccessfullyEnqueued.Select(e => e.HandlerId).Should().BeEquivalentTo(
            new[] { "H2b", "H5" },
            "H4 threw but H2b + H5 in the same fan-out must still enqueue.");
    }

    // -----------------------------------------------------------------------
    // AC #5 — TimeProvider is injected (structural).
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_UsesInjectedTimeProvider()
    {
        // Simply constructing the SUT with a TestTimeProvider proves the DI
        // seam is a TimeProvider (not DateTime.UtcNow / Stopwatch). The POML
        // step 8 grep gate independently verifies zero DateTime.UtcNow /
        // Stopwatch usage inside Reconciler/*.cs — this test asserts the ctor
        // shape is compatible with FakeTimeProvider-style overrides.
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
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
        var frozenNow = DateTimeOffset.Parse("2026-08-17T12:34:56Z");
        var timeProvider = new TestTimeProvider(frozenNow);
        var sut = BuildSut(
            new StubActiveRunScanner(Array.Empty<ProvisioningRun>()),
            new DedupingRecordingEnqueuer(),
            out _,
            timeProvider: timeProvider);
        var run = MakeRun(RunStatus.Running, "H0");

        var envelope = sut.BuildEnvelope("H1", run);

        envelope.EnqueuedAt.Should().Be(frozenNow,
            "EnqueuedAt MUST come from the injected TimeProvider — never DateTime.UtcNow.");
    }

    // -----------------------------------------------------------------------
    // Disabled kill switch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ExitsCleanlyWithoutTicking()
    {
        var scanner = new StubActiveRunScanner(new[] { MakeRun(RunStatus.Running, "H0") });
        var enqueuer = new DedupingRecordingEnqueuer();
        var sut = BuildSut(scanner, enqueuer, out _, options: new ReconcilerOptions
        {
            Enabled = false,
            PollInterval = TimeSpan.FromSeconds(5),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await sut.StartAsync(cts.Token);
        // ExecuteAsync should return immediately when disabled.
        await sut.StopAsync(cts.Token);

        enqueuer.TotalCalls.Should().Be(0, "Enabled=false suppresses the ExecuteAsync tick loop.");
    }

    // -----------------------------------------------------------------------
    // ReconcilerOptions.Validate
    // -----------------------------------------------------------------------

    [Fact]
    public void ReconcilerOptions_Validate_RejectsSubSecondPollInterval()
    {
        var options = new ReconcilerOptions { PollInterval = TimeSpan.FromMilliseconds(500) };
        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PollInterval*must be >= 1 second*");
    }

    [Fact]
    public void ReconcilerOptions_Validate_AcceptsFiveSecondDefault()
    {
        var options = new ReconcilerOptions();
        var act = () => options.Validate();

        act.Should().NotThrow();
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.Enabled.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers + test doubles
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="StateReconcilerService"/> wired to the given
    /// scanner + enqueuer + optional TimeProvider/options. The IDagAdvancer
    /// is always the real production impl (pure function; already covered by
    /// DagAdvancerTests).
    /// </summary>
    private static StateReconcilerService BuildSut(
        IActiveRunScanner scanner,
        IHandlerEnqueuer enqueuer,
        out RecordingLoggerFactory loggerFactory,
        ReconcilerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        loggerFactory = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton(scanner);
        services.AddSingleton(enqueuer);
        services.AddSingleton<IDagAdvancer, DagAdvancer>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new StateReconcilerService(
            scopeFactory,
            Options.Create(options ?? new ReconcilerOptions()),
            timeProvider ?? TimeProvider.System,
            loggerFactory.CreateLogger<StateReconcilerService>());
    }

    /// <summary>Minimal ProvisioningRun with the specified completed phases.</summary>
    private static ProvisioningRun MakeRun(RunStatus status, params string[] completedPhases)
    {
        var run = new ProvisioningRun
        {
            RunId = TestRunId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model2Dedicated",
            Profile = "spaarke-hosted-model2",
            Status = status,
        };
        var now = DateTimeOffset.UtcNow;
        foreach (var phase in completedPhases)
        {
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = phase,
                StartedAt = now,
                CompletedAt = now,
                IdempotencyKey = $"{phase.ToLowerInvariant()}-{TestCustomerId}-test",
                JobId = TestRunId,
            });
        }
        return run;
    }

    // ---------------- IActiveRunScanner test doubles ----------------

    private sealed class StubActiveRunScanner : IActiveRunScanner
    {
        private readonly IReadOnlyList<ProvisioningRun> _runs;
        public StubActiveRunScanner(IEnumerable<ProvisioningRun> runs) => _runs = runs.ToList();
        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => Task.FromResult(_runs);
    }

    private sealed class ThrowingActiveRunScanner : IActiveRunScanner
    {
        private readonly Exception _exception;
        public ThrowingActiveRunScanner(Exception exception) => _exception = exception;
        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => throw _exception;
    }

    // ---------------- IHandlerEnqueuer test doubles ----------------

    /// <summary>
    /// Thread-safe recording enqueuer that DEDUPS on MessageId — replicates the
    /// production Service Bus wire-level dedup contract (level-1 idempotency
    /// per FR-22). <see cref="TotalCalls"/> counts every call attempt;
    /// <see cref="DistinctMessageIds"/> retains one entry per unique
    /// deterministic MessageId (parity with the SB dedup window).
    /// </summary>
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

    /// <summary>
    /// Enqueuer that throws for a specific HandlerId; records successful
    /// enqueues for all others. Verifies sibling-isolation on failure.
    /// </summary>
    private sealed class SelectivelyThrowingEnqueuer : IHandlerEnqueuer
    {
        private readonly string _throwFor;
        private readonly List<HandlerEnvelope> _success = new();

        public SelectivelyThrowingEnqueuer(string throwForHandlerId) => _throwFor = throwForHandlerId;
        public IReadOnlyList<HandlerEnvelope> SuccessfullyEnqueued => _success.ToArray();

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken ct)
        {
            if (string.Equals(envelope.HandlerId, _throwFor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated Service Bus failure for {_throwFor}");
            }
            _success.Add(envelope);
            return Task.CompletedTask;
        }
    }

    // ---------------- TimeProvider double ----------------

    /// <summary>
    /// Minimal TimeProvider double for tests. Mirrors the shape of the
    /// <c>MutableTimeProvider</c> in <c>InMemoryTenantTokenLedgerTests</c>
    /// so we avoid adding <c>Microsoft.Extensions.TimeProvider.Testing</c>
    /// as a new package dep (per tests/CLAUDE.md guidance).
    /// </summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset initial) => _now = initial;
        public void Set(DateTimeOffset next) => _now = next;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    // ---------------- Logger recording double ----------------

    /// <summary>
    /// LoggerFactory that captures every log call into a shared list so tests
    /// can assert on level + message content (needed for the "warning logged
    /// on Cosmos outage" acceptance).
    /// </summary>
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
