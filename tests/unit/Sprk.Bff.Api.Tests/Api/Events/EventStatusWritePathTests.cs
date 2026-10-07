// spaarke-ontology-platform-r1 task 097 (2026-10-05) — the sprk_event status write paths.
//
// What was broken (each reproduced live against spaarkedev1 before the fix, Dataverse error 0x80048408):
//   - the BFF carried a fictional status set (Open = 3, Completed = 5, Cancelled = 6, Deleted = 7): create, complete,
//     cancel and soft-delete were all rejected ("5 is not a valid status code on sprk_event"), and the complete/cancel
//     gate refused a live Open (659490001) event before it even tried;
//   - Completed and Closed are ACTIVE (statecode 0) in the live option set. The BFF's `statusCode >= 5 ? 1 : 0` and the
//     EventDetailSidePane's STATUSCODE_STATECODE_MAP both paired them with Inactive, which Dataverse rejects
//     ("659490002 is not a valid status code for state code sprk_EventState.Inactive").
//
// ADR-038 project rule 1: an assertion on an option-set value is paired with the real schema. The schema here is
// docs/data-model/sprk_event-related-tables.md's statuscode row, re-verified against the live StatusAttributeMetadata
// (value, label AND state per option) on 2026-10-05; the parity tests below fail if the code and that row disagree.

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Xunit;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-097-ontology-r1")]
public class EventStatusWritePathTests
{
    private static readonly Guid EventId = Guid.Parse("97097097-0000-0000-0000-000000000097");

    // ── Schema parity: the code's option set IS the live one ──────────────────────────────────────────────────

    [Fact]
    public void EventStatusCode_MatchesTheLiveVerifiedDataModelRow_ValueLabelAndState()
    {
        var doc = DocumentedStatuses();

        doc.Should().NotBeEmpty("the statuscode row must be parseable — a silently empty parse would pass anything");
        EventStatusCode.All.Select(s => (s.Value, s.Label, s.State))
            .Should().BeEquivalentTo(doc, "every value the BFF writes must exist, with the statecode Dataverse pairs it with");
    }

    [Fact]
    public void EventDetailSidePane_StatusMap_PairsEveryStatusWithItsLiveState()
    {
        var violations = SidePaneMapViolations(
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "solutions", "EventDetailSidePane", "src", "App.tsx")),
            DocumentedStatuses());

        violations.Should().BeEmpty(
            "the side pane writes statecode from this map with every status change; a wrong state is a rejected save");
    }

    [Fact]
    public void EventDetailSidePane_StatusOptions_AreAllLiveValues()
    {
        var section = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "solutions", "EventDetailSidePane", "src", "components", "StatusSection.tsx"));
        var options = Regex.Matches(section, @"\{\s*value:\s*(\d+),\s*label:")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(v => EventStatusCode.IsDefined(v));
    }

    // The guard bites: the analyser on the map exactly as it shipped before task 097.
    [Fact]
    public void TheSidePaneAnalyser_FlagsThePreFixMap()
    {
        const string preFix = """
            const STATUSCODE_STATECODE_MAP: Record<number, number> = {
              1:         0, // Draft → Active
              659490001: 0, // Open → Active
              659490006: 0, // On Hold → Active
              659490002: 1, // Completed → Inactive
              659490003: 1, // Closed → Inactive
              659490004: 1, // Cancelled → Inactive
            };
            """;

        SidePaneMapViolations(preFix, DocumentedStatuses())
            .Should().BeEquivalentTo(new[] { 659490002, 659490003 });
    }

    // ── The payloads the BFF sends ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreatePayload_WritesLiveOpen_Active()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Filing", OwningTeamId = Guid.NewGuid() });

        payload["statuscode"].Should().Be(659490001, "live Open — the former 3 does not exist in the option set");
        payload["statecode"].Should().Be(0);
    }

    [Theory]
    [InlineData(659490002, 0)] // Completed — Active
    [InlineData(659490003, 0)] // Closed — Active
    [InlineData(659490004, 1)] // Cancelled — Inactive
    [InlineData(2, 1)]         // No Further Action — Inactive
    public void StateCode_IsTheOneTheLiveStatusBelongsTo(int statusCode, int expectedState) =>
        EventStatusCode.GetStateCode(statusCode).Should().Be(expectedState);

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void StateCode_AValueOutsideTheLiveSet_IsRefusedBeforeDataverse(int fictional)
    {
        var act = () => EventStatusCode.GetStateCode(fictional);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── OWNER DECISION A (2026-10-06): ONE predicate for "open work" — the complete gate AND To Do generation ──

    [Theory]
    [InlineData(1)]          // Draft
    [InlineData(659490001)]  // Open
    [InlineData(659490006)]  // On Hold
    [InlineData(659490007)]  // Reassigned — completable (owner decision A; uac-r2 task 159 had refused it)
    public async Task Complete_FromAnOpenWorkStatus_WritesLiveCompleted(int currentStatus)
    {
        var dv = EventService(currentStatus);

        var result = await Complete(dv, NullLogger<Program>.Instance);

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, 659490002, It.Is<DateTime?>(dt => dt.HasValue), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(659490002)] // Completed
    [InlineData(659490003)] // Closed
    [InlineData(659490004)] // Cancelled
    [InlineData(659490005)] // Transferred
    [InlineData(2)]         // No Further Action
    public async Task Complete_FromAFinishedStatus_Is400_AndWritesNothing(int currentStatus)
    {
        var dv = EventService(currentStatus);

        var result = await Complete(dv, NullLogger<Program>.Instance);

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
        dv.Verify(d => d.UpdateEventStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void TheCompleteGate_AndToDoGeneration_UseTheSamePredicate()
    {
        foreach (var status in EventStatusCode.All.Select(s => s.Value))
        {
            EventEndpoints.CanCompleteEvent(status).Should().Be(EventStatusCode.IsOpenWork(status));
            Sprk.Bff.Api.Services.Workspace.TodoGenerationService.ExcludedFromGeneration.Contains(status)
                .Should().Be(!EventStatusCode.IsOpenWork(status), "To Do generation excludes exactly what is not open work");
        }

        EventEndpoints.CanCompleteEvent(EventStatusCode.Reassigned).Should().BeTrue("owner decision A");
    }

    [Fact]
    public async Task Complete_AuditsCompleted_WithTheTransition()
    {
        var dv = EventService(EventStatusCode.Open);

        await Complete(dv, NullLogger<Program>.Instance);

        dv.Verify(d => d.CreateEventLogAsync(EventId, EventLogAction.Completed,
            It.Is<string?>(s => s == "Status changed from Open to Completed"), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Review F1: an audit-log failure never turns a committed write into a 500, and is never silent ─────────

    [Fact]
    public async Task Complete_WhenTheAuditWriteFails_StillReturns200_AndLogsTheStableErrorEvent()
    {
        var dv = EventService(EventStatusCode.Open, auditThrows: true);
        var log = new CapturingLogger();
        using var metric = new AuditFailureMetric();

        var result = await Complete(dv, log);

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId.Id == 9701
            && e.EventId.Name == "EventAuditLogWriteFailed", "never swallowed silently (the former helper logged a Warning)");
        metric.CountFor("Completed").Should().BeGreaterThanOrEqualTo(1);
    }

    private static Task<IResult> Complete(Mock<IEventDataverseService> dv, ILogger<Program> log) =>
        EventEndpoints.CompleteEventAsync(EventId, new DefaultHttpContext(), dv.Object,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), log, default);
    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<IEventDataverseService> EventService(int currentStatus, bool auditThrows = false)
    {
        var dv = new Mock<IEventDataverseService>(MockBehavior.Loose);
        dv.Setup(d => d.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventEntity
            {
                Id = EventId,
                Name = "zz-097 fixture",
                StatusCode = currentStatus,
                StateCode = EventStatusCode.IsDefined(currentStatus) ? EventStatusCode.GetStateCode(currentStatus) : 0,
            });
        var audit = dv.Setup(d => d.CreateEventLogAsync(EventId, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()));
        if (auditThrows)
            audit.ThrowsAsync(new HttpRequestException("400: Invalid property 'sprk_description'"));
        else
            audit.ReturnsAsync(Guid.NewGuid());
        return dv;
    }

    /// <summary>Listens to the event audit-failure counter (meter Sprk.Bff.Api.Events) for one test.</summary>
    private sealed class AuditFailureMetric : IDisposable
    {
        private readonly System.Diagnostics.Metrics.MeterListener _listener = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _byAction = new();

        public AuditFailureMetric()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EventEndpoints.MeterName && instrument.Name == "event_audit_log_write_failures_total")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                    if (tag.Key == "action" && tag.Value is string action)
                        _byAction.AddOrUpdate(action, value, (_, existing) => existing + value);
            });
            _listener.Start();
        }

        public long CountFor(string action) => _byAction.TryGetValue(action, out var n) ? n : 0;

        public void Dispose() => _listener.Dispose();
    }
    private sealed class CapturingLogger : ILogger<Program>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, eventId, formatter(state, exception)));
    }

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    /// <summary>(value, label, state) from the statuscode row of the live-verified sprk_event data-model doc.</summary>
    private static IReadOnlyList<(int Value, string Label, int State)> DocumentedStatuses()
    {
        var row = File.ReadLines(Path.Combine(RepoRoot(), "docs", "data-model", "sprk_event-related-tables.md"))
            .Single(l => Regex.IsMatch(l, @"\|\s*sprk_event\s*\|\s*statuscode\s*\|"));

        return Regex.Matches(row, @"(\d+): ([A-Za-z ]+?) \[(\d) (?:Active|Inactive)\]")
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value, int.Parse(m.Groups[3].Value)))
            .ToList();
    }

    /// <summary>Status values in the side pane's STATUSCODE_STATECODE_MAP whose state disagrees with the doc (or
    /// that the doc does not define).</summary>
    private static IReadOnlyList<int> SidePaneMapViolations(
        string appTsx, IReadOnlyList<(int Value, string Label, int State)> documented)
    {
        var block = Regex.Match(appTsx, @"STATUSCODE_STATECODE_MAP[^{]*\{(?<body>[^}]*)\}");
        block.Success.Should().BeTrue("the side pane's status → state map must be found to be checked");

        var entries = Regex.Matches(block.Groups["body"].Value, @"(?m)^\s*(\d+)\s*:\s*(\d)\s*,")
            .Select(m => (Value: int.Parse(m.Groups[1].Value), State: int.Parse(m.Groups[2].Value)))
            .ToList();
        entries.Should().NotBeEmpty();

        return entries
            .Where(e => !documented.Any(d => d.Value == e.Value && d.State == e.State))
            .Select(e => e.Value)
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
