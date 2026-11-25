// spaarke-ontology-platform-r1 task 222 (built by spaarkeai-mcp-bff-api-r1, NFR-01 fix D): the worklist read runs its independent
// Dataverse reads concurrently, bounded. These tests pin today's output first and must pass before and after the change: the same
// rows, order, content, grouping and counts whatever order the reads complete in; a fault in any branch fails the read as before;
// cancellation is honoured and leaves no call running. The fake world adds a latency per call (World.Latency, World.LatencyOf) so
// concurrent reads really overlap and complete out of order.
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    /// <summary>A lane the caller (Alice) reads: the Decide and Do lanes, secure-family rows, no-core rows, a witness-withheld row,
    /// duplicate rows (one kept, one withheld), an invoice-line item, a closed-today item, and one Signal per hiding rule.</summary>
    /// <param name="Names">Every id the fixture made, by a stable name (the ids themselves are random).</param>
    /// <param name="Hidden">One Signal per rule that hides a row from the Lanes set, by the rule's name.</param>
    private sealed record RepresentativeLane(World World, Dictionary<Guid, string> Names, Dictionary<string, Guid> Hidden);

    private static RepresentativeLane Representative()
    {
        var world = new World { SecureTeamExists = true };
        var names = new Dictionary<Guid, string>();
        Guid Named(string name, Guid id)
        {
            names[id] = name;
            return id;
        }

        // Core records (distinct numbers and best-item ranks, so no order depends on a random id).
        var m1 = Named("m1", world.Matter([Alice], name: "Acme", number: "MAT-0001", highPriority: true));
        world.AppRoot("sprk_matter", m1, secure: false);
        var m2 = Named("m2", world.Matter([Alice], name: "Beta", number: "MAT-0002"));
        world.AppRoot("sprk_matter", m2, secure: false);
        var secureMatter = Named("secureMatter", world.Matter([Alice], name: "Gamma", number: "MAT-0003"));
        world.AppRoot("sprk_matter", secureMatter, secure: true);
        var project = Named("project", world.Record("sprk_project", [Alice], new()
        {
            ["sprk_projectname"] = "Project P",
            ["sprk_projectnumber"] = "PRJ-0001",
            ["sprk_highpriority"] = false,
        }));

        // Decide lane.
        Named("m1Critical", world.Signal(Decide, core: (MatterType, m1), severity: Critical, firstDetected: "2026-10-02T00:00:00Z"));
        Named("m1Info", world.Signal(Decide, core: (MatterType, m1), severity: Info, firstDetected: "2026-10-01T00:00:00Z"));
        Named("m2Warning", world.Signal(Decide, core: (MatterType, m2), severity: Warning, firstDetected: "2026-09-20T00:00:00Z"));
        Named("projectCritical", world.Signal(Decide, core: (ProjectType, project), severity: Critical, firstDetected: "2026-09-30T00:00:00Z"));

        // Secure family: a witness in the Signal's own secure family keeps its detail.
        var own = Named("ownWitness", world.AppCommunication(secureOwned: true, ("sprk_regardingmatter", "sprk_matter", secureMatter)));
        var secureRow = Named("secureRow", world.Signal(Decide, core: (MatterType, secureMatter), evidence: EvidenceQuoting(own),
            firstDetected: "2026-09-25T00:00:00Z"));
        world.AppSignal(secureRow, secureMatter, secureOwned: true);

        // Witness-withheld detail: one witness re-filed under a secure project, one ordinary.
        var secureProject = Guid.NewGuid();
        world.AppRoot("sprk_project", secureProject, secure: true);
        var refiled = Named("refiledWitness", world.AppCommunication(secureOwned: true,
            ("sprk_regardingmatter", "sprk_matter", m2), ("sprk_regardingproject", "sprk_project", secureProject)));
        var ordinary = Named("ordinaryWitness", world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m2)));
        var witnessRow = Named("witnessRow", world.Signal(Decide, core: (MatterType, m2), evidence: EvidenceQuoting(refiled, ordinary),
            firstDetected: "2026-09-21T00:00:00Z"));
        world.AppSignal(witnessRow, m2, secureOwned: false);

        // D-176 Duplicates on m1: one whose earlier invoice the caller reads (kept), one they cannot (withheld); and an invoice-line item.
        var current = Named("currentInvoice", world.Invoice([Alice], VendorOrg, matter: m1));
        var readableEarlier = Named("readableEarlier", world.Invoice([Alice], VendorOrg));
        var unreadableEarlier = Named("unreadableEarlier", world.Invoice([Bob], VendorOrg));
        Named("duplicateKept", world.Signal(Decide, core: (MatterType, m1), subject: (NotCoreType, current), title: DuplicateTitle,
            evidence: DuplicateEvidence(readableEarlier), sentence: QuotedSentence, firstDetected: "2026-09-10T00:00:00Z"));
        Named("duplicateWithheld", world.Signal(Decide, core: (MatterType, m1), subject: (NotCoreType, current), title: DuplicateTitle,
            evidence: DuplicateEvidence(unreadableEarlier), sentence: QuotedSentence, firstDetected: "2026-09-11T00:00:00Z"));
        var line = Named("line", world.BillingEvent([Alice], current));
        Named("lineRepeat", world.Signal(Decide, core: (MatterType, m1), subject: (BillingEventType, line), title: DuplicateTitle,
            evidence: SameInvoiceEvidence(), sentence: QuotedSentence, firstDetected: "2026-09-12T00:00:00Z"));

        // Do lane: an event on m1, a work assignment, a no-core To Do ("Not filed").
        var ev = Named("event", world.Event(owner: Alice));
        Named("eventDo", world.Signal(Do, core: (MatterType, m1), subject: (EventType, ev), firstDetected: "2026-10-03T00:00:00Z"));
        var wa = Named("workAssignment", world.WorkAssignment(createdBy: Alice, assignedToInternal: BobContact, readers: [Alice, Bob]));
        Named("workAssignmentDo", world.Signal(Do, core: (WorkAssignmentType, wa), subject: (WorkAssignmentType, wa), workType: 100000005,
            firstDetected: "2026-10-04T00:00:00Z"));
        var todo = Named("todo", world.Todo(owner: Alice));
        Named("notFiledDo", world.Signal(Do, core: null, subject: (TodoType, todo), firstDetected: "2026-10-05T00:00:00Z"));

        // Closed today (in the closedToday set only, so hidden from the lanes).
        var now = world.Now;
        Named("closedTodayDecide", world.Signal(Decide, core: (MatterType, m2), sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddHours(-1)));
        Named("closedTodayDo", world.Signal(Do, core: null, subject: (TodoType, world.Todo(owner: Alice)), sets: ["closedToday", "closedEarlier"],
            resolvedOn: now.AddHours(-2)));

        // One Signal per hiding rule.
        var hidden = new Dictionary<string, Guid>();
        var secret = Named("secretMatter", world.Matter([Bob]));
        hidden["core record the caller cannot read"] = world.Signal(Decide, core: (MatterType, secret));
        hidden["core type outside the access taxonomy"] = world.Signal(Decide, core: (NotCoreType, current));
        hidden["core type with no catalog row"] = world.Signal(Decide, core: (Guid.NewGuid(), m1));
        hidden["half core pair"] = world.Signal(Do, core: (MatterType, Guid.Empty), subject: (TodoType, world.Todo(owner: Alice)));
        hidden["no-core Decide row"] = world.Signal(Decide, core: null, subject: (CommunicationType, Guid.NewGuid()));
        var storedPair = world.Signal(Do, core: null, subject: (TodoType, world.Todo(owner: Alice)));
        world.StoredPairOnly[storedPair] = (MatterType, m1);
        hidden["no-core row whose stored pair the app-only read returns"] = storedPair;
        hidden["Do row that is not the caller's own work"] = world.Signal(Do, core: (MatterType, m1),
            subject: (TodoType, world.Todo(owner: Bob, readers: [Alice, Bob])));
        var otherUnit = world.Signal(Decide, core: (MatterType, m1));
        world.SignalReaders[otherUnit] = [Bob];
        hidden["Signal the caller's own read does not return"] = otherUnit;
        hidden["closed today (not in the lanes set)"] = names.Single(n => n.Value == "closedTodayDecide").Key;
        hidden["suppressed (not in the lanes set)"] = world.Signal(Decide, core: (MatterType, m1), sets: ["suppressed"],
            suppressedUntil: now.AddDays(3));
        foreach (var (rule, id) in hidden) names.TryAdd(id, "hidden:" + rule);

        return new RepresentativeLane(world, names, hidden);
    }

    /// <summary>The response as names: per lane, each group (its core by name and number, or Not filed) and its items in order, with
    /// what the read rules decided about each (a withheld Duplicate, how many witness lines are withheld, the invoice it is reviewed on).</summary>
    private static string Digest(WorklistResponse response, IReadOnlyDictionary<Guid, string> names)
    {
        string Name(Guid id) => names.TryGetValue(id, out var n) ? n : "?";
        var digest = new StringBuilder();
        foreach (var lane in response.Lanes)
        {
            digest.Append(lane.Lane).Append(':');
            foreach (var group in lane.Groups)
            {
                digest.Append(" [").Append(group.Core is { } core ? $"{Name(core.RecordId)} {core.Number}" : "Not filed").Append(':');
                digest.AppendJoin(',', group.Items.Select(i =>
                    Name(i.SignalId)
                    + (i.Sentence == EarlierInvoiceReadability.WithheldSentence ? " dup-withheld" : string.Empty)
                    + (i.EvidenceRefs is { } e && e.Contains("\"withheld\":true", StringComparison.Ordinal)
                        ? $" withheld-lines={e.Split("\"withheld\":true").Length - 1}" : string.Empty)
                    + (i.InvoiceReview is { } r ? $" review={Name(r.InvoiceId)}" : string.Empty)));
                digest.Append(']');
            }

            digest.Append('\n');
        }

        return digest.ToString();
    }

    /// <summary>Today's lanes for the representative fixture (pinned from the serial read before task 222 changed it).</summary>
    private const string RepresentativeLanesDigest =
        "Decide: [m1 MAT-0001:m1Critical,duplicateKept withheld-lines=4 review=currentInvoice,"
        + "duplicateWithheld dup-withheld withheld-lines=3 review=currentInvoice,lineRepeat withheld-lines=4 review=currentInvoice,m1Info]"
        + " [project PRJ-0001:projectCritical] [m2 MAT-0002:m2Warning,witnessRow withheld-lines=1] [secureMatter MAT-0003:secureRow]\n"
        + "Do: [m1 MAT-0001:eventDo] [workAssignment WA-PLACEHOLDER:workAssignmentDo] [Not filed:notFiledDo]\n";

    /// <summary>Today's closed-today set for the representative fixture.</summary>
    private const string RepresentativeClosedTodayDigest =
        "Decide: [m2 MAT-0002:closedTodayDecide]\n"
        + "Do: [Not filed:closedTodayDo]\n";

    private static ClaimsPrincipal Signed(Guid user) =>
        new(new ClaimsIdentity([new Claim("oid", user.ToString("D"))], authenticationType: "test"));

    /// <summary>Runs <paramref name="read"/> against the service as the host builds it (the same DI graph the route uses), as
    /// <paramref name="actingAs"/>, in <paramref name="world"/>.</summary>
    private async Task<T> WithServiceAsync<T>(World world, Guid actingAs, Func<SignalReadService, Task<T>> read)
    {
        _factory.World = world;
        world.ActingAs = actingAs;
        using var scope = _factory.Services.CreateScope();
        var result = await read(scope.ServiceProvider.GetRequiredService<SignalReadService>());
        world.PairReadShapeViolations.Should().BeEmpty();
        return result;
    }

    private Task<SignalReadResult> ReadAsync(World world, Guid actingAs, WorklistSet set, CancellationToken ct = default) =>
        WithServiceAsync(world, actingAs, s => s.ReadAsync(set, Signed(actingAs), ct));

    private static string Json(WorklistResponse? response) => JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

    /// <summary>A per-call latency (1-23 ms) from the call's name and <paramref name="seed"/>: the reads complete in an order unrelated
    /// to the order they were sent.</summary>
    private static Func<string, TimeSpan> Shuffled(int seed) =>
        name => TimeSpan.FromMilliseconds(1 + (int)((uint)HashCode.Combine(seed, StableHash(name)) % 23));

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text) hash = unchecked(hash * 31 + c);
        return hash;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // equivalence
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorklistSet.Lanes)]
    [InlineData(WorklistSet.ClosedToday)]
    public async Task Task222_TheRepresentativeSet_IsTheSame_WhateverOrderTheReadsCompleteIn(WorklistSet set)
    {
        var fixture = Representative();
        var world = fixture.World;
        var serial = await ReadAsync(world, Alice, set);
        serial.Outcome.Should().Be(SignalReadOutcome.Ok, serial.Reason);
        var pinned = set == WorklistSet.Lanes ? RepresentativeLanesDigest : RepresentativeClosedTodayDigest;
        var workAssignmentNumber = (string)world.Records[fixture.Names.Single(n => n.Value == "workAssignment").Key].Columns["sprk_workassignmentnumber"]!;
        Digest(serial.Response!, fixture.Names).Should().Be(pinned.Replace("WA-PLACEHOLDER", workAssignmentNumber, StringComparison.Ordinal));
        var serialRequests = world.Requests.Order(StringComparer.Ordinal).ToList();

        foreach (var seed in new[] { 1, 2, 3 })
        {
            world.Requests.Clear();
            world.LatencyOf = Shuffled(seed);
            var shuffled = await ReadAsync(world, Alice, set);

            shuffled.Outcome.Should().Be(SignalReadOutcome.Ok, shuffled.Reason);
            Json(shuffled.Response).Should().Be(Json(serial.Response), $"completion order {seed} changes nothing in the response");
            world.Requests.Order(StringComparer.Ordinal).Should().Equal(serialRequests, "the same reads, only overlapped");
        }

        if (set == WorklistSet.Lanes)
        {
            world.Requests.Count(r => r.StartsWith(BillingEventRead, StringComparison.Ordinal))
                .Should().Be(1, "one batched invoice-line read per worklist response (task 174, #2580)");
        }
    }

    [Fact]
    public async Task Task222_TheRoute_ReturnsTheSameBody_WithAndWithoutOverlappingReads()
    {
        var world = Representative().World;
        var serial = await (await GetAsync(world, Alice)).Content.ReadAsStringAsync();

        world.LatencyOf = Shuffled(7);
        var overlapped = await (await GetAsync(world, Alice)).Content.ReadAsStringAsync();

        overlapped.Should().Be(serial);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // must-not-fire: a fault in any branch fails the read as before
    // ---------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, string> BranchFaults => new()
    {
        { "first saved fetch", "signal_query_failed" },
        { "second saved fetch", "signal_query_failed" },
        { "core read of a later core type", "core record read failed" },
        { "core read throws", "fault" },
        { "app-only pair read", "core_pair_read_failed" },
        { "Do-lane subject read", "Do-lane subject read failed" },
        { "Decide-lane subject read", "Decide-lane subject read failed" },
        { "policy read", "fault" },
        { "external-caller check", "fault" },
        { "configuration", "configuration_invalid" },
    };

    private static void Break(World world, string branch)
    {
        switch (branch)
        {
            case "first saved fetch": world.FaultSignalSet = "decide"; break;
            case "second saved fetch": world.FaultSignalSet = "do"; break;
            case "core read of a later core type": world.FaultingTables.Add("sprk_project"); break;
            case "core read throws": world.ThrowingTables.Add("sprk_matter"); break;
            case "app-only pair read": world.FaultPairRead = true; break;
            case "Do-lane subject read": world.FaultingTables.Add("sprk_todo"); break;
            case "Decide-lane subject read": world.FaultingTables.Add("sprk_invoice"); break;
            case "policy read": world.FaultPolicyReadFrom = 1; break;
            case "external-caller check": world.ExternalFlagReadThrows = true; break;
            case "configuration": world.ConfigJson = "{ not json"; break;
            default: throw new ArgumentOutOfRangeException(nameof(branch));
        }
    }

    [Theory]
    [MemberData(nameof(BranchFaults))]
    public async Task Task222_AFaultInAnyBranch_FailsTheRead_AsBefore(string branch, string reason)
    {
        foreach (var latency in new Func<string, TimeSpan>?[] { null, Shuffled(11) })
        {
            var world = Representative().World;
            Break(world, branch);
            world.LatencyOf = latency;

            var result = await ReadAsync(world, Alice, WorklistSet.Lanes);

            result.Should().Be(SignalReadResult.Unavailable(reason), $"{branch} fails the read, never a shorter list (latency: {latency is not null})");
            world.InFlight.Should().Be(0, "no read is left running");
        }
    }

    [Fact]
    public async Task Task222_WhenTwoBranchesFail_TheFailureTheSerialReadMetFirst_Wins()
    {
        // The matter type is the first core type (its Signal is the first row), the project type a later one. The matter read answers 500
        // after the project read has thrown; the serial loop met the matter's failure first, so that is the answer.
        var world = Representative().World;
        world.FaultingTables.Add("sprk_matter");
        world.ThrowingTables.Add("sprk_project");
        world.FaultPairRead = true;
        world.LatencyOf = name => TimeSpan.FromMilliseconds(name.StartsWith("sprk_matters?", StringComparison.Ordinal) ? 60 : 1);

        var result = await ReadAsync(world, Alice, WorklistSet.Lanes);

        result.Should().Be(SignalReadResult.Unavailable("core record read failed"));
        world.InFlight.Should().Be(0);
    }

    [Fact]
    public async Task Task222_AWitnessReadThatThrows_StillWithholdsEveryDetail_AndTheListIsUnchanged()
    {
        var fixture = Representative();
        var serial = await ReadAsync(fixture.World, Alice, WorklistSet.Lanes);

        var world = Representative().World;
        world.ThrowingTables.Add("sprk_communication");
        world.LatencyOf = Shuffled(5);
        var faulted = await ReadAsync(world, Alice, WorklistSet.Lanes);

        faulted.Outcome.Should().Be(SignalReadOutcome.Ok);
        faulted.Response!.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items).Count()
            .Should().Be(serial.Response!.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items).Count());
        faulted.Response.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items)
            .Where(i => i.EvidenceRefs is { } e && e.Contains("sprk_communication:", StringComparison.Ordinal))
            .Should().OnlyContain(i => !i.EvidenceRefs!.Contains(WitnessSubject, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // must-not-fire: cancellation
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("app:sprk_gridconfiguration")]
    [InlineData("sprk_signals?")]
    [InlineData("sprk_matters?")]
    [InlineData("sprk_todos?")]
    [InlineData("sprk_communications?")]
    [InlineData("app:sprk_policy")]
    public async Task Task222_ACancellationDuringAnyBranch_Propagates_AndLeavesNoReadRunning(string cancelAt)
    {
        var world = Representative().World;
        using var cts = new CancellationTokenSource();
        world.Latency = TimeSpan.FromMilliseconds(5);
        world.OnCall = name =>
        {
            if (name.StartsWith(cancelAt, StringComparison.Ordinal)) cts.Cancel();
        };

        var read = () => ReadAsync(world, Alice, WorklistSet.Lanes, cts.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        world.InFlight.Should().Be(0, "every branch was awaited before the read ended");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // the latency harness
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Task222_LatencyHarness_ReportsRoundTripsAndWallClock_ForTheRepresentativeLane()
    {
        const int latencyMs = 50;
        var world = Representative().World;
        (await ReadAsync(world, Alice, WorklistSet.Lanes)).Outcome.Should().Be(SignalReadOutcome.Ok, "warm-up: the host is built outside the measurement");
        world.Requests.Clear();
        var appBefore = world.AppCalls;
        world.ResetPeakInFlight();
        world.Latency = TimeSpan.FromMilliseconds(latencyMs);

        var clock = Stopwatch.StartNew();
        var result = await ReadAsync(world, Alice, WorklistSet.Lanes);
        clock.Stop();

        result.Outcome.Should().Be(SignalReadOutcome.Ok);
        var app = world.AppCalls - appBefore;
        var calls = world.Requests.Count + app;
        _output?.WriteLine(
            $"Task 222 latency harness: {world.Requests.Count} caller round trips + {app} app-only calls = {calls} Dataverse calls, "
            + $"{latencyMs} ms each; serial sum {calls * latencyMs} ms; measured {clock.ElapsedMilliseconds} ms; peak in flight {world.PeakInFlight}.");
    }
}
