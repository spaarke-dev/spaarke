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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Xunit;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;

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
        var payload = DataverseWebApiService.BuildCreateEventPayload(new DataverseCreateEventRequest { Name = "Filing" });

        payload["statuscode"].Should().Be(659490001, "live Open — the former 3 does not exist in the option set");
        payload["statecode"].Should().Be(0);
    }

    [Fact]
    public void StatusPayload_Completed_IsLiveCompleted_AndStaysActive_WithTheCompletedDate()
    {
        var payload = DataverseWebApiService.BuildUpdateEventStatusPayload(
            EventStatusCode.Completed, new DateTime(2026, 10, 5, 14, 0, 0, DateTimeKind.Utc));

        payload["statuscode"].Should().Be(659490002);
        payload["statecode"].Should().Be(0, "Completed is an ACTIVE status reason; pairing it with Inactive is rejected");
        payload["sprk_completeddate"].Should().Be("2026-10-05");
    }

    [Fact]
    public void StatusPayload_Cancelled_IsLiveCancelled_Inactive()
    {
        var payload = DataverseWebApiService.BuildUpdateEventStatusPayload(EventStatusCode.Cancelled, null);

        payload["statuscode"].Should().Be(659490004);
        payload["statecode"].Should().Be(1);
        payload.Should().NotContainKey("sprk_completeddate");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void StatusPayload_AValueOutsideTheLiveSet_IsRefusedBeforeDataverse(int fictional)
    {
        var act = () => DataverseWebApiService.BuildUpdateEventStatusPayload(fictional, null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(659490002, 0)] // Completed — Active
    [InlineData(659490003, 0)] // Closed — Active
    [InlineData(659490004, 1)] // Cancelled — Inactive
    [InlineData(2, 1)]         // No Further Action — Inactive
    public void UpdatePayload_WithAStatus_PairsItsLiveState(int statusCode, int expectedState)
    {
        var payload = DataverseWebApiService.BuildUpdateEventPayload(new DataverseUpdateEventRequest { StatusCode = statusCode });

        payload["statuscode"].Should().Be(statusCode);
        payload["statecode"].Should().Be(expectedState);
    }

    [Fact]
    public void UpdatePayload_DueDateOnly_WritesNoStatus()
    {
        var payload = DataverseWebApiService.BuildUpdateEventPayload(
            new DataverseUpdateEventRequest { DueDate = new DateTime(2026, 10, 20) });

        payload.Should().ContainKey("sprk_duedate").WhoseValue.Should().Be("2026-10-20");
        payload.Should().NotContainKey("statuscode").And.NotContainKey("statecode");
    }

    // ── The endpoints, executed ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]          // Draft
    [InlineData(659490001)]  // Open — refused with 400 before task 097
    [InlineData(659490006)]  // On Hold — refused with 400 before task 097
    [InlineData(659490007)]  // Reassigned
    public async Task Complete_FromAnOpenWorkStatus_WritesLiveCompleted(int currentStatus)
    {
        var dv = EventService(currentStatus);

        var result = await EventEndpoints.CompleteEventAsync(EventId, dv.Object, NullLogger<Program>.Instance, default);

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

        var result = await EventEndpoints.CompleteEventAsync(EventId, dv.Object, NullLogger<Program>.Instance, default);

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
        dv.Verify(d => d.UpdateEventStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_AnOpenEvent_WritesLiveCancelled()
    {
        var dv = EventService(EventStatusCode.Open);

        var result = await EventEndpoints.CancelEventAsync(EventId, dv.Object, NullLogger<Program>.Instance, default);

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, 659490004, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SoftDelete_WritesLiveCancelled_AsTheRouteDescriptionPromises()
    {
        var dv = EventService(EventStatusCode.Open);

        var result = await EventEndpoints.DeleteEventAsync(EventId, dv.Object, NullLogger<Program>.Instance, default);

        StatusOf(result).Should().Be(StatusCodes.Status204NoContent);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, 659490004, null, It.IsAny<CancellationToken>()), Times.Once,
            "there is no Deleted status reason; the former 7 was rejected");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<IEventDataverseService> EventService(int currentStatus)
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
        dv.Setup(d => d.CreateEventLogAsync(EventId, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return dv;
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
