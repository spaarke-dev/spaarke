// spaarke-ontology-platform-r1 task 222 (built by spaarkeai-mcp-bff-api-r1, NFR-01 fixes B and D), the parts that need the new
// behaviour: (B) the single Work Item read returns exactly the lane's row for every Signal the lane shows and nothing for every Signal a
// lane rule hides, with no lane-wide Signal fetch; (D) the lane read's concurrency is bounded, and faster than one request at a time.
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    private Task<SignalReadResult> ReadOneAsync(
        World world, Guid actingAs, Guid signalId, WorklistSet set = WorklistSet.Lanes, IDictionary<Guid, WorklistItemExtras>? extras = null) =>
        WithServiceAsync(world, actingAs, s => s.ReadAsync(set, Signed(actingAs), CancellationToken.None, extras, onlySignalId: signalId));

    private static IReadOnlyList<WorklistItemRow> RowsOf(SignalReadResult result) =>
        [.. result.Response!.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items)];

    // ---------------------------------------------------------------------------------------------------------------
    // B: the single Work Item read
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorklistSet.Lanes)]
    [InlineData(WorklistSet.ClosedToday)]
    public async Task Task222_EveryRowTheSetShows_ComesBackIdentical_FromTheSingleRowRead(WorklistSet set)
    {
        var world = Representative().World;
        var laneExtras = new Dictionary<Guid, WorklistItemExtras>();
        var lane = await WithServiceAsync(world, Alice, s => s.ReadAsync(set, Signed(Alice), CancellationToken.None, laneExtras));
        var shown = RowsOf(lane);
        shown.Should().NotBeEmpty();

        foreach (var row in shown)
        {
            var extras = new Dictionary<Guid, WorklistItemExtras>();
            var one = await ReadOneAsync(world, Alice, row.SignalId, set, extras);

            one.Outcome.Should().Be(SignalReadOutcome.Ok, one.Reason);
            var only = RowsOf(one).Should().ContainSingle().Subject;
            JsonSerializer.Serialize(only, JsonSerializerOptions.Web).Should().Be(JsonSerializer.Serialize(row, JsonSerializerOptions.Web));
            JsonSerializer.Serialize(extras.Single(), JsonSerializerOptions.Web)
                .Should().Be(JsonSerializer.Serialize(laneExtras.Single(e => e.Key == row.SignalId), JsonSerializerOptions.Web));
        }
    }

    [Fact]
    public async Task Task222_ASignalAnyLaneRuleHides_ComesBackAsNothing_ExactlyLikeAnUnknownId()
    {
        var fixture = Representative();
        var unknown = await ReadOneAsync(fixture.World, Alice, Guid.NewGuid());
        unknown.Outcome.Should().Be(SignalReadOutcome.Ok);
        RowsOf(unknown).Should().BeEmpty();
        var nothing = Json(unknown.Response);

        foreach (var (rule, signalId) in fixture.Hidden)
        {
            var one = await ReadOneAsync(fixture.World, Alice, signalId);

            one.Outcome.Should().Be(SignalReadOutcome.Ok, rule);
            Json(one.Response).Should().Be(nothing, $"a Signal hidden by \"{rule}\" is answered as an unknown id is");
        }
    }

    [Fact]
    public async Task Task222_TheSingleRowRead_SendsNoLaneWideSignalFetch_AndNarrowsEveryFetchWithAnAnd()
    {
        var fixture = Representative();
        var world = fixture.World;
        var target = fixture.Names.Single(n => n.Value == "notFiledDo").Key;

        var one = await ReadOneAsync(world, Alice, target);

        RowsOf(one).Select(r => r.SignalId).Should().Equal(target);
        var fetches = world.Requests.Where(r => r.StartsWith("sprk_signals?fetchXml=", StringComparison.Ordinal))
            .Select(r => System.Xml.Linq.XDocument.Parse(Uri.UnescapeDataString(r["sprk_signals?fetchXml=".Length..])))
            .ToList();
        fetches.Should().HaveCount(2, "one per saved fetch of the lanes set, and no other Signal read");
        foreach (var fetch in fetches)
        {
            var entity = fetch.Root!.Element("entity")!;
            var top = entity.Elements("filter").Should().ContainSingle("the id and the configuration's filters are one AND").Subject;
            ((string?)top.Attribute("type")).Should().Be("and");
            top.Elements("condition").Should().ContainSingle(c => (string?)c.Attribute("attribute") == "sprk_signalid"
                && (string?)c.Attribute("operator") == "eq" && (string?)c.Attribute("value") == target.ToString("D"));
            top.Elements("filter").Should().ContainSingle("the configuration's own filter, unchanged, inside the AND");
        }
    }

    [Fact]
    public void Task222_Narrowing_AddsOnlyTheIdCondition_AndKeepsTheConfigurationsFiltersInsideOneAnd()
    {
        var id = Guid.Parse("12345678-0000-4000-8000-000000000222");
        const string fetch = "<fetch><entity name='sprk_signal'><attribute name='sprk_signalid' />"
            + "<filter type='or'><condition attribute='sprk_lane' operator='eq' value='1' /><condition attribute='sprk_lane' operator='eq' value='2' /></filter>"
            + "<filter><condition attribute='statecode' operator='eq' value='0' /></filter>"
            + "<link-entity name='x' from='a' to='b'><filter><condition attribute='c' operator='null' /></filter></link-entity>"
            + "<order attribute='sprk_signalid' /></entity></fetch>";

        var narrowed = System.Xml.Linq.XDocument.Parse(SignalReadService.NarrowedToSignal(fetch, id)).Root!.Element("entity")!;

        var top = narrowed.Elements("filter").Should().ContainSingle().Subject;
        ((string?)top.Attribute("type")).Should().Be("and", "a top-level OR filter can never widen past the id");
        top.Elements("condition").Single().ToString().Should().Contain(id.ToString("D"));
        top.Elements("filter").Select(f => (string?)f.Attribute("type")).Should().Equal("or", null);
        narrowed.Element("link-entity")!.Element("filter").Should().NotBeNull("link-entity filters are untouched");
        narrowed.Elements("order").Should().ContainSingle();
        narrowed.Elements("attribute").Should().ContainSingle();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D: bounded, and faster
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>A lane with many core types and chunks: 230 matters (3 chunks), 120 projects (2 chunks), 110 To Dos (2 chunks).</summary>
    private static World ManyChunks()
    {
        var world = new World();
        for (var i = 0; i < 230; i++) world.Signal(Decide, core: (MatterType, world.Matter([Alice])));
        for (var i = 0; i < 120; i++) world.Signal(Decide, core: (ProjectType, world.Record("sprk_project", [Alice])));
        for (var i = 0; i < 110; i++) world.Signal(Do, core: null, subject: (TodoType, world.Todo(owner: Alice)));
        return world;
    }

    [Fact]
    public async Task Task222_TheLaneRead_NeverHasMoreThanTheGatesPermitsInFlight_AndDoesOverlap()
    {
        var world = ManyChunks();
        world.Latency = TimeSpan.FromMilliseconds(15);

        var result = await ReadAsync(world, Alice, WorklistSet.Lanes);

        result.Outcome.Should().Be(SignalReadOutcome.Ok);
        world.PeakInFlight.Should().BeLessThanOrEqualTo(SignalReadService.DefaultMaxConcurrentReads, "the read is bounded");
        world.PeakInFlight.Should().BeGreaterThan(1, "independent reads overlap");
    }

    [Fact]
    public async Task Task222_WithOnePermit_TheReadSendsOneRequestAtATime_TheSameRequests_AndTheSameAnswer()
    {
        var concurrent = ManyChunks();
        concurrent.Latency = TimeSpan.FromMilliseconds(5);
        var fast = await ReadAsync(concurrent, Alice, WorklistSet.Lanes);

        var serial = ManyChunks();
        serial.Latency = TimeSpan.FromMilliseconds(5);
        var slow = await WithServiceAsync(serial, Alice, s =>
        {
            s.MaxConcurrentReads = 1;
            return s.ReadAsync(WorklistSet.Lanes, Signed(Alice), CancellationToken.None);
        });

        serial.PeakInFlight.Should().Be(1);
        RowsOf(slow).Should().HaveSameCount(RowsOf(fast));
        serial.Requests.Count.Should().Be(concurrent.Requests.Count);
        serial.AppCalls.Should().Be(concurrent.AppCalls);
    }

    [Fact]
    public async Task Task222_LatencyHarness_TheRepresentativeLane_IsFasterConcurrentThanOneRequestAtATime()
    {
        const int latencyMs = 40;
        var timings = new List<(int Permits, int Caller, int App, int Peak, long Ms)>();
        foreach (var permits in new[] { 1, SignalReadService.DefaultMaxConcurrentReads })
        {
            var world = Representative().World;
            (await ReadAsync(world, Alice, WorklistSet.Lanes)).Outcome.Should().Be(SignalReadOutcome.Ok, "warm-up: the host is built outside the measurement");
            world.Requests.Clear();
            var appBefore = world.AppCalls;
            world.ResetPeakInFlight();
            world.Latency = TimeSpan.FromMilliseconds(latencyMs);
            var clock = Stopwatch.StartNew();
            var result = await WithServiceAsync(world, Alice, s =>
            {
                s.MaxConcurrentReads = permits;
                return s.ReadAsync(WorklistSet.Lanes, Signed(Alice), CancellationToken.None);
            });
            clock.Stop();
            result.Outcome.Should().Be(SignalReadOutcome.Ok);
            var app = world.AppCalls - appBefore;
            timings.Add((permits, world.Requests.Count, app, world.PeakInFlight, clock.ElapsedMilliseconds));
            _output?.WriteLine(
                $"Task 222 harness ({latencyMs} ms per call), {permits} permit(s): {world.Requests.Count} caller + {app} app-only calls, "
                + $"peak {world.PeakInFlight} in flight, {clock.ElapsedMilliseconds} ms.");
        }

        var (one, many) = (timings[0], timings[1]);
        (many.Caller, many.App).Should().Be((one.Caller, one.App), "the same reads, only overlapped");
        many.Ms.Should().BeLessThan(one.Ms, "overlapped reads finish sooner");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // B + D: one Work Item, quickly
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Task222_LatencyHarness_TheSingleRowRead_IsFewerRoundTripsThanTheLane()
    {
        var fixture = Representative();
        var target = fixture.Names.Single(n => n.Value == "m1Critical").Key;
        var world = fixture.World;
        (await ReadAsync(world, Alice, WorklistSet.Lanes)).Outcome.Should().Be(SignalReadOutcome.Ok, "warm-up: the host is built outside the measurement");
        world.Requests.Clear();
        var laneAppBefore = world.AppCalls;
        world.Latency = TimeSpan.FromMilliseconds(40);

        var laneClock = Stopwatch.StartNew();
        await ReadAsync(world, Alice, WorklistSet.Lanes);
        laneClock.Stop();
        var (laneCaller, laneApp) = (world.Requests.Count, world.AppCalls - laneAppBefore);

        world.Requests.Clear();
        var appBefore = world.AppCalls;
        var oneClock = Stopwatch.StartNew();
        var one = await ReadOneAsync(world, Alice, target);
        oneClock.Stop();
        var (oneCaller, oneApp) = (world.Requests.Count, world.AppCalls - appBefore);

        RowsOf(one).Select(r => r.SignalId).Should().Equal(target);
        (oneCaller + oneApp).Should().BeLessThan(laneCaller + laneApp);
        _output?.WriteLine(
            $"Task 222 harness (40 ms per call): lane {laneCaller} caller + {laneApp} app-only calls, {laneClock.ElapsedMilliseconds} ms; "
            + $"one Work Item {oneCaller} caller + {oneApp} app-only calls, {oneClock.ElapsedMilliseconds} ms.");
    }
}
