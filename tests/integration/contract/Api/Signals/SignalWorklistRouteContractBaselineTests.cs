using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Integration.Contract.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

// spaarkeai-mcp-bff-api-r1 task 011 (FR-09): byte-level golden contract tests of GET /api/v1/signals/worklist, recorded from the code as
// it is before the decision-capability extraction (tasks 012-017). The route runs through the REAL BFF pipeline of SignalReadRouteTests
// (routing, RequireAuthorization, the handler, SignalReadService) over the same scripted caller Dataverse (World). This file is a part of
// SignalReadRouteTests so it reuses that harness; it lives under tests/integration/contract/ (a KEEP path) and is compiled into the
// IntegrationTests assembly by the link in its csproj, and removed from the unit-test assembly's contract glob.
//
// World mints its ids with Guid.NewGuid(), so these cases compare with generatedGuids: true (each GUID becomes <guid-N>, N the order of
// first appearance). Nothing else is normalised. The golden files are in Baselines/ and are never edited to make a refactor pass.
public sealed partial class SignalReadRouteTests
{
    private static async Task<GoldenContract.Observed> ObserveAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return await GoldenContract.ObserveAsync(response);
        }
    }

    private static void VerifyWorklist(string caseName, GoldenContract.Observed observed, int status, string? reasonCode = null)
    {
        observed.Status.Should().Be(status, observed.Body);
        if (reasonCode is not null)
        {
            JsonDocument.Parse(observed.Body).RootElement.GetProperty("reasonCode").GetString().Should().Be(reasonCode, observed.Body);
        }

        GoldenContract.AssertMatches(caseName, observed, generatedGuids: true);
    }

    /// <summary>One world that exercises every member of a row: a Decide matter item with its evidence and sentence, a Do item the
    /// caller owns, a billing line item and a billing invoice item (both carry invoiceReview, task 174), and one item in each of the
    /// closedToday, closedEarlier and suppressed sets.</summary>
    private static World ContractWorld()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap",
            title: "Fee change without a budget revision", subjectName: "RE: revised fees", firstDetected: "2026-10-01T09:00:00Z",
            evidence: "[{\"type\":\"sprk_communication\"}]", sentence: "Fees rose 12% with no budget revision on file.");
        world.Signal(Do, core: (MatterType, m), subject: (EventType, world.Event(owner: Alice)), severity: Warning, shortName: "Event follow-up",
            title: "Prepare for the hearing", subjectName: "Hearing", firstDetected: "2026-10-02T09:00:00Z");

        var invoice = world.Invoice([Alice], VendorOrg, matter: m);
        var line = world.BillingEvent([Alice], invoice);
        // Distinct raise dates, so the order of equal-severity rows never depends on a generated id.
        world.Signal(Decide, core: (MatterType, m), subject: (BillingEventType, line), title: DuplicateTitle, evidence: "[]", sentence: QuotedSentence,
            firstDetected: "2026-10-03T09:00:00Z");
        var invoiceSubject = world.Invoice([Alice], VendorOrg, matter: m);
        world.Signal(Decide, core: (MatterType, m), subject: (NotCoreType, invoiceSubject), title: DuplicateTitle, evidence: "[]", sentence: QuotedSentence,
            firstDetected: "2026-10-04T09:00:00Z");

        var now = world.Now;
        world.Signal(Decide, core: (MatterType, m), title: "Closed today item", shortName: "Closed rule", sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddSeconds(-1));
        world.Signal(Decide, core: (MatterType, m), title: "Closed earlier item", shortName: "Closed rule", sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddHours(-49));
        world.Signal(Decide, core: (MatterType, m), title: "Suppressed item", shortName: "Quiet rule", sets: ["suppressed"], suppressedUntil: now.AddDays(10));
        return world;
    }

    [Fact]
    public async Task ContractBaseline_Worklist_200_Lanes() =>
        VerifyWorklist("worklist.200.lanes", await ObserveAsync(await GetAsync(ContractWorld(), Alice)), 200);

    [Fact]
    public async Task ContractBaseline_Worklist_200_ClosedToday() =>
        VerifyWorklist("worklist.200.closed-today", await ObserveAsync(await GetAsync(ContractWorld(), Alice, "?set=closedToday")), 200);

    [Fact]
    public async Task ContractBaseline_Worklist_200_ClosedEarlier() =>
        VerifyWorklist("worklist.200.closed-earlier", await ObserveAsync(await GetAsync(ContractWorld(), Alice, "?set=closedEarlier")), 200);

    [Fact]
    public async Task ContractBaseline_Worklist_200_Suppressed() =>
        VerifyWorklist("worklist.200.suppressed", await ObserveAsync(await GetAsync(ContractWorld(), Alice, "?set=suppressed")), 200);

    [Fact]
    public async Task ContractBaseline_Worklist_200_DecidedAndEmpty() =>
        VerifyWorklist("worklist.200.decided-empty", await ObserveAsync(await GetAsync(new World(), Alice, "?set=decided")), 200);

    /// <summary>Task 226 (#2015, D-231): the Decided set read record-first. A Signal reviewed twice (an inquiry, then the decision) is
    /// two items, each with its own record; an email-gate record on the same matter is in the group's <c>records</c> with its summary;
    /// a gate record with no core record whose communication the caller reads is a Not filed group with only <c>records</c>.</summary>
    [Fact]
    public async Task ContractBaseline_Worklist_200_Decided()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        var inquiry = world.Decision(readers: [Alice], number: "DR-00010", decidedOn: "2026-10-01T09:00:00Z", steps: StepsEnvelope);
        var decision = world.Decision(readers: [Alice], number: "DR-00011", decidedOn: "2026-10-12T09:00:00Z");
        var s = world.Signal(Decide, core: (MatterType, m), shortName: "Budget gap", title: "Fee change without a budget revision",
            firstDetected: "2026-09-30T09:00:00Z", sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddDays(-2), decisionRecord: decision);
        world.Resolves(inquiry, s);
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00900", decidedOn: "2026-10-14T09:30:00Z");
        world.GateDecision(readers: [Alice], communication: world.Record("sprk_communication", [Alice]), number: "DR-00901",
            name: "Judgement: denied (recipient outside the matter)", decidedOn: "2026-10-13T09:30:00Z");

        VerifyWorklist("worklist.200.decided", await ObserveAsync(await GetAsync(world, Alice, "?set=decided")), 200);
    }

    /// <summary>Task 235 (#3283, D-234): the Decided set lists the writer's Record Procedure, billing and assign-firm records that resolved
    /// no Signal in their core record's <c>records</c>, with their summary and confirming user and no item to open, beside a Signal's item on
    /// the same matter; on a project with no item they are a group of their own. The existing Decided baseline above is unchanged.</summary>
    [Fact]
    public async Task ContractBaseline_Worklist_200_Decided_SignalLessRecords()
    {
        const string NoSignal = "{\"reviewId\":\"0b7a9b5e-5555-4c2e-9a54-000000000235\",\"resolvedSignalIds\":[],\"steps\":[]}";
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        var p = world.Record("sprk_project", [Alice], new() { ["sprk_projectname"] = "Office move", ["sprk_projectnumber"] = "PRJ-0007" });
        var decision = world.Decision(readers: [Alice], number: "DR-00011", decidedOn: "2026-10-12T09:00:00Z");
        world.Signal(Decide, core: (MatterType, m), shortName: "Budget gap", title: "Fee change without a budget revision",
            firstDetected: "2026-09-30T09:00:00Z", sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddDays(-2), decisionRecord: decision);
        world.Decision(readers: [Alice], number: "DR-00700", decidedOn: "2026-10-14T09:30:00Z", name: "Judgement: Assign Procedure",
            core: (MatterType, m), actionCode: "assign-procedure", steps: NoSignal);
        world.Decision(readers: [Alice], number: "DR-00701", decidedOn: "2026-10-13T09:30:00Z", name: "Judgement: Set firm",
            core: (MatterType, m), actionCode: "assign-firm", steps: NoSignal);
        world.Decision(readers: [Alice], number: "DR-00702", decidedOn: "2026-10-11T09:30:00Z", name: "Judgement: Accept line",
            core: (ProjectType, p), actionCode: "accept-line", steps: NoSignal);

        VerifyWorklist("worklist.200.decided-signal-less", await ObserveAsync(await GetAsync(world, Alice, "?set=decided")), 200);
    }

    [Fact]
    public async Task ContractBaseline_Worklist_400_UnknownSet() =>
        VerifyWorklist("worklist.400.unknown-set", await ObserveAsync(await GetAsync(new World(), Alice, "?set=everything")), 400);

    [Fact]
    public async Task ContractBaseline_Worklist_401_Anonymous()
    {
        using var client = _factory.CreateClient();
        VerifyWorklist("worklist.401.anonymous", await ObserveAsync(await client.GetAsync(Route)), 401);
    }

    [Fact]
    public async Task ContractBaseline_Worklist_403_CallerUnresolved()
    {
        var world = new World();
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])), title: "x");
        VerifyWorklist("worklist.403.caller-unresolved", await ObserveAsync(await GetAsync(world, actingAs: null)), 403, "sdap.access.deny.caller_unresolved");
    }

    [Fact]
    public async Task ContractBaseline_Worklist_503_Unavailable_ACoreReadFaults()
    {
        var world = new World();
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])), title: "x");
        world.FaultingTables.Add("sprk_matter");
        VerifyWorklist("worklist.503.unavailable", await ObserveAsync(await GetAsync(world, Alice)), 503, "ontology.signal.worklist_unavailable");
    }

    [Fact]
    public async Task ContractBaseline_Worklist_503_TooManySignals_MoreThanMaxPagesOfSignals()
    {
        // The service reports internal reason "too_many_signals"; the route answers the same 503 worklist_unavailable body as any fault
        // (the internal reason is logged, never returned). The case pins that: no truncated list, and no new reason code on the wire.
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < SignalReadService.MaxPages + 1; i++)
        {
            world.Signal(Decide, core: (MatterType, m), title: "Signal " + i);
        }

        VerifyWorklist("worklist.503.too-many-signals", await ObserveAsync(await GetAsync(world, Alice)), 503, "ontology.signal.worklist_unavailable");
    }
}
