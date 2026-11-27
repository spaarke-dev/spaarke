// Ontology task 226 (#2015, DI-104; owner decision D-231): the Decision Records tab (GET /api/v1/signals/worklist?set=decided) reads
// Decision Records FIRST. Before it, the set listed decided Signals and attached the record each Signal's own sprk_decisionrecord lookup named,
// so a Signal reviewed twice (Send inquiry, then the decision) showed only the later record, and an email-gate record (D-139, no Signal) never
// appeared. Through the REAL route and SignalReadService over the same scripted caller Dataverse (World) as SignalReadRouteTests. This file is a
// part of SignalReadRouteTests so it reuses that harness; it lives under tests/integration/regression/ (a KEEP path) and is compiled into the
// IntegrationTests assembly by the link in its csproj, and removed from the unit-test assembly's regression glob.
//
// Must-fire: both reviews of a Signal reviewed twice are rows, each with its own record; a gate record the caller can read is listed with its
// summary and no item (nothing to Open). Must-not-fire: a record the writer did not create (WriterCreatedRecord), a secure-child record of a
// family the caller is not in, a no-core record that is not the caller's, a gate record on a core record or communication the caller cannot
// read, a half or non-core stamp, and a record decided before the window.
//
// Fix round 1 (review F1, K2 scope): the gate records are their own read, joined to the communication so only those whose email the caller
// reads come back and count toward its bound; the records that resolved Signals are a read of their own, bounded as before. A writer record
// that resolved no Signal and is not a gate record (billing, Record Procedure, assign-firm) is neither listed nor counted.
// Fix round 2: the no-Signal LIKE needs no escape; a 403 on the gate read alone; a record that resolved a Signal under an email is not a
// gate record.
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    private static IEnumerable<JsonElement> AllRecords(JsonElement body) =>
        body.GetProperty("lanes").EnumerateArray().SelectMany(l => l.GetProperty("groups").EnumerateArray())
            .SelectMany(g => g.TryGetProperty("records", out var records) ? records.EnumerateArray() : Enumerable.Empty<JsonElement>()).ToList();

    private static List<(Guid Signal, string? Number)> DecidedRows(JsonElement body) =>
        AllItems(body).Select(i => (i.GetProperty("signalId").GetGuid(), i.GetProperty("decision").GetProperty("number").GetString())).ToList();

    private static List<string?> RecordNumbers(JsonElement body) =>
        AllRecords(body).Select(r => r.GetProperty("number").GetString()).ToList();

    [Fact]
    public async Task DecidedSet_ASignalReviewedTwice_YieldsTwoRows_EachWithItsOwnRecord()
    {
        // The inquiry review left the Signal Acknowledged; the decision later resolved it and overwrote the Signal's lookup.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var inquiry = world.Decision(readers: [Alice], number: "DR-00010", decidedOn: "2026-10-01T09:00:00Z", steps: StepsEnvelope);
        var decision = world.Decision(readers: [Alice], number: "DR-00011", decidedOn: "2026-10-12T09:00:00Z");
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddDays(-2), decisionRecord: decision);
        world.Resolves(inquiry, s);

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00011"), (s, "DR-00010")], "each review is its own row, newest decision first");
        var earlier = AllItems(body).Single(i => i.GetProperty("decision").GetProperty("number").GetString() == "DR-00010");
        earlier.GetProperty("decision").GetProperty("steps").EnumerateArray().Select(x => x.GetProperty("code").GetString())
            .Should().Equal("reassign", "extend");
        AllRecords(body).Should().BeEmpty();
    }

    [Fact]
    public async Task DecidedSet_ARecordThatResolvedTwoSignals_IsARowUnderEach_AsBefore()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var record = world.Decision(readers: [Alice], number: "DR-00012");
        var primary = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: record);
        var alsoResolved = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: record);

        DecidedRows(await OkAsync(world, Alice, "?set=decided")).Should().BeEquivalentTo([(primary, "DR-00012"), (alsoResolved, "DR-00012")]);
    }

    [Fact]
    public async Task DecidedSet_AnInquiryRecord_WhoseSignalHasNoResolvedOnDate_IsListed()
    {
        // The Signal-first query windowed on sprk_resolvedon, which an inquiry never sets; the window is now the record's sprk_decidedon.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var inquiry = world.Decision(readers: [Alice], number: "DR-00013", decidedOn: "2026-10-13T09:00:00Z");
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], status: 100000001, decisionRecord: inquiry);

        DecidedRows(await OkAsync(world, Alice, "?set=decided")).Should().Equal([(s, "DR-00013")]);
    }

    [Fact]
    public async Task DecidedSet_AnEmailGateRecord_IsListedInItsCoreRecordGroup_WithItsSummary_AndNoItemToOpen()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        var gate = world.GateDecision(readers: [Alice], core: (MatterType, m), name: "Judgement: Send the reply");

        var body = await OkAsync(world, Alice, "?set=decided");

        AllItems(body).Should().BeEmpty("a gate record resolved no Signal, so there is no Work Item to open (D-231)");
        var group = Lane(body, "Decide").GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("notFiled").GetBoolean().Should().BeFalse();
        group.GetProperty("core").GetProperty("recordId").GetGuid().Should().Be(m);
        group.GetProperty("core").GetProperty("name").GetString().Should().Be("Acme v Widget");
        var record = group.GetProperty("records").EnumerateArray().Single();
        record.GetProperty("recordId").GetGuid().Should().Be(gate);
        record.GetProperty("number").GetString().Should().Be("DR-00900");
        record.GetProperty("summary").GetString().Should().Be("Judgement: Send the reply");
        record.GetProperty("recordClass").GetString().Should().Be("Judgement");
        record.GetProperty("steps").ValueKind.Should().Be(JsonValueKind.Null, "nothing replays a record with no Open");
        record.GetProperty("followOns").GetArrayLength().Should().Be(0);
        Lane(body, "Do").GetProperty("groups").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DecidedSet_AGateRecordAndAReviewOnOneMatter_ShareTheMattersGroup()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice], number: "DR-00014"));
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00901");

        var group = Lane(await OkAsync(world, Alice, "?set=decided"), "Decide").GetProperty("groups").EnumerateArray().Single();

        group.GetProperty("items").EnumerateArray().Single().GetProperty("signalId").GetGuid().Should().Be(s);
        group.GetProperty("records").EnumerateArray().Single().GetProperty("number").GetString().Should().Be("DR-00901");
    }

    [Fact]
    public async Task DecidedSet_ANoCoreGateRecord_IsListedNotFiled_OnlyToACallerWhoReadsItsCommunication()
    {
        var world = new World();
        var alicesMessage = world.Record("sprk_communication", [Alice]);
        var bobsMessage = world.Record("sprk_communication", [Bob]);
        // Dataverse returns both records to both callers (a colleague's Deep read, D-144's accepted native exposure); only the communication
        // the caller reads as themselves (its owner, or a person it is shared with) lists the record (D-35/D-39).
        world.GateDecision(readers: [Alice, Bob], communication: alicesMessage, number: "DR-00901");
        world.GateDecision(readers: [Alice, Bob], communication: bobsMessage, number: "DR-00902");
        world.GateDecision(readers: [Alice, Bob], number: "DR-00903", withoutCommunication: true); // no core, no communication: never listed

        var alice = await OkAsync(world, Alice, "?set=decided");
        var group = Lane(alice, "Decide").GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("notFiled").GetBoolean().Should().BeTrue();
        group.GetProperty("core").ValueKind.Should().Be(JsonValueKind.Null);
        RecordNumbers(alice).Should().Equal("DR-00901");

        RecordNumbers(await OkAsync(world, Bob, "?set=decided")).Should().Equal("DR-00902");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ARecordTheWriterDidNotCreate_IsNeverListed()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice], number: "DR-00001"));
        var forged = world.Decision(readers: [Alice], number: "DR-66666", by: "Pat Partner", createdBy: SomePerson);
        world.Resolves(forged, s);
        var onBehalf = world.Decision(readers: [Alice], number: "DR-66667", createdOnBehalfBy: SomePerson);
        world.Resolves(onBehalf, s);
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-66668", createdBy: SomePerson);

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00001")]);
        AllRecords(body).Should().BeEmpty();
        body.ToString().Should().NotContain("DR-6666").And.NotContain("Pat Partner");
        var fetch = Uri.UnescapeDataString(world.Requests.First(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal))).Replace('\'', '"');
        fetch.Should().Contain($"<condition attribute=\"createdby\" operator=\"eq\" value=\"{WriterUser:D}\" />")
            .And.Contain("<condition attribute=\"createdonbehalfby\" operator=\"null\" />")
            .And.Contain("<condition attribute=\"sprk_decidedon\" operator=\"last-x-days\" value=\"90\" />");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ASecureChildRecordOfAFamilyTheCallerIsNotIn_IsNeverListed()
    {
        // Dataverse trims the caller's record read by the mirrored secure-child shares (D-33/D-123): the fake's readers stand for them.
        var world = new World();
        var m = world.Matter(readers: [Alice, Bob]);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice, Bob], number: "DR-00001", decidedOn: "2026-10-12T09:00:00Z"));
        var familyOnly = world.Decision(readers: [Bob], number: "DR-00002", decidedOn: "2026-10-13T09:00:00Z");
        world.Resolves(familyOnly, s);
        world.GateDecision(readers: [Bob], core: (MatterType, m), number: "DR-00903");

        var alice = await OkAsync(world, Alice, "?set=decided");
        DecidedRows(alice).Should().Equal([(s, "DR-00001")]);
        AllRecords(alice).Should().BeEmpty();
        alice.ToString().Should().NotContain("DR-00002").And.NotContain("DR-00903");

        var bob = await OkAsync(world, Bob, "?set=decided");
        DecidedRows(bob).Should().Equal([(s, "DR-00002"), (s, "DR-00001")]);
        RecordNumbers(bob).Should().Equal("DR-00903");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_AGateRecordOnACoreRecordTheCallerCannotRead_OrAHalfOrNonCoreStamp_IsNeverListed()
    {
        var world = new World();
        var hiddenMatter = world.Matter(readers: [Bob]);
        var invoice = world.Invoice([Alice], VendorOrg);
        world.GateDecision(readers: [Alice, Bob], core: (MatterType, hiddenMatter), number: "DR-00904");
        world.GateDecision(readers: [Alice], core: (MatterType, Guid.Empty), number: "DR-00905"); // a half pair: type without id
        world.GateDecision(readers: [Alice], core: (NotCoreType, invoice), number: "DR-00906"); // not an access-control core type (K2)

        AllRecords(await OkAsync(world, Alice, "?set=decided")).Should().BeEmpty();
        RecordNumbers(await OkAsync(world, Bob, "?set=decided")).Should().Equal("DR-00904");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ANoCoreReviewOfSomeoneElsesWork_IsNeverListed()
    {
        // D-35/D-39: a no-core item is its owner's; the record of its review follows its Signal's rule (the Do-lane "mine" read).
        var world = new World();
        var bobsTodo = world.Todo(owner: Bob, readers: [Alice, Bob]); // Alice is in the same BU and can even read the To Do
        var record = world.Decision(readers: [Alice, Bob], number: "DR-00020");
        var s = world.Signal(Do, core: null, subject: (TodoType, bobsTodo), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: record);

        var alice = await OkAsync(world, Alice, "?set=decided");
        AllItems(alice).Should().BeEmpty();
        alice.ToString().Should().NotContain("DR-00020");

        DecidedRows(await OkAsync(world, Bob, "?set=decided")).Should().Equal([(s, "DR-00020")]);
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ARecordOfASignalTheCallerCannotSee_IsNeverListed()
    {
        var world = new World();
        var hiddenMatter = world.Matter(readers: [Bob]);
        var record = world.Decision(readers: [Alice, Bob], number: "DR-00021");
        world.Signal(Decide, core: (MatterType, hiddenMatter), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: record);

        var alice = await OkAsync(world, Alice, "?set=decided");
        AllItems(alice).Should().BeEmpty();
        AllRecords(alice).Should().BeEmpty("a record that resolved Signals is listed only under a Signal the caller's worklist read admits");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ARecordDecidedBeforeTheWindow_IsNotListed()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var old = world.Decision(readers: [Alice], number: "DR-00030", decidedOn: world.Now.AddDays(-91).ToString("o"));
        var recent = world.Decision(readers: [Alice], number: "DR-00031", decidedOn: world.Now.AddDays(-89).ToString("o"));
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: recent);
        world.Resolves(old, s);
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00907", decidedOn: world.Now.AddDays(-91).ToString("o"));

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00031")]);
        AllRecords(body).Should().BeEmpty();
    }

    [Fact]
    public async Task DecidedSet_TheRecordRead_IsPaged_AndRefusedPastMaxPages()
    {
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00908", decidedOn: "2026-10-13T09:00:00Z");
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00909", decidedOn: "2026-10-12T09:00:00Z");

        RecordNumbers(await OkAsync(world, Alice, "?set=decided")).Should().Equal("DR-00908", "DR-00909");

        for (var i = 0; i < SignalReadService.MaxPages; i++)
        {
            world.GateDecision(readers: [Alice], core: (MatterType, m), number: $"DR-0100{i}");
        }

        (await GetAsync(world, Alice, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "never a shorter history");
    }

    [Theory]
    [InlineData("500")]
    [InlineData("401")]
    public async Task DecidedSet_ARecordReadFault_Is503(string fault)
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.GateDecision(readers: [Alice], core: (MatterType, m));
        if (fault == "500") world.FaultingTables.Add("sprk_decisionrecord"); else world.UnauthorizedTables.Add("sprk_decisionrecord");

        (await GetAsync(world, Alice, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DecidedSet_GateRecordsOnEmailTheCallerCannotRead_AreNotReturned_NorCounted_SoTheTabAnswers()
    {
        // F1: a Console User's Deep read returns every gate record of the business unit (D-144), and the gate writes one per assessed email.
        // Only the records whose email the caller reads come back (the inner join), so a busy unit's volume never 503s the tab.
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice, Bob]);
        for (var i = 0; i < SignalReadService.MaxPages + 2; i++)
        {
            world.GateDecision(readers: [Alice, Bob], core: (MatterType, m), communication: world.Record("sprk_communication", [Bob]),
                number: $"DR-0200{i}");
        }

        world.GateDecision(readers: [Alice, Bob], core: (MatterType, m), communication: world.Record("sprk_communication", [Alice]),
            number: "DR-00910", decidedOn: "2026-10-14T10:00:00Z");

        RecordNumbers(await OkAsync(world, Alice, "?set=decided")).Should().Equal("DR-00910");
        var fetch = Uri.UnescapeDataString(world.Requests.Last(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal)
            && r.Contains("sprk_communication", StringComparison.Ordinal))).Replace('\'', '"');
        fetch.Should().Contain("<link-entity name=\"sprk_communication\" from=\"sprk_communicationid\" to=\"sprk_regardingcommunication\" link-type=\"inner\"")
            .And.NotContain("\"sprk_steps\" />").And.NotContain("\"sprk_followons\" />");

        // Must fire: the caller who CAN read them all is over the bound, and is told so (503), never shown a shorter history.
        (await GetAsync(world, Bob, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_AWriterRecordThatResolvedNoSignal_AndIsNotAGateRecord_IsNeitherListedNorCounted()
    {
        // K2 scope (D-231 decided only gate records): a Record Procedure, billing or assign-firm record with no Signal is not listed, and
        // the records that resolved Signals are bounded on their own, so a volume of these never 503s the tab. The owner decides the rest.
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < SignalReadService.MaxPages + 2; i++)
        {
            world.Decision(readers: [Alice], number: $"DR-0300{i}", core: (MatterType, m),
                steps: "{\"reviewId\":\"0b7a9b5e-2222-4c2e-9a54-000000000226\",\"resolvedSignalIds\":[],\"steps\":[]}");
        }

        // A person's review filed under an email it resolved no Signal on (a confirming user): not a gate record either.
        world.Decision(readers: [Alice], number: "DR-03999", communication: world.Record("sprk_communication", [Alice]),
            steps: "{\"reviewId\":\"0b7a9b5e-3333-4c2e-9a54-000000000226\",\"resolvedSignalIds\":[],\"steps\":[]}");
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice], number: "DR-00040"));

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00040")]);
        AllRecords(body).Should().BeEmpty();
        body.ToString().Should().NotContain("DR-0300").And.NotContain("DR-03999");
    }

    [Fact]
    public async Task DecidedSet_ACallerRefusedTheGateRead_StillGetsTheRecordsThatResolvedSignals_AndNoGateRecord()
    {
        // Fix round 2 (F2): no Read on sprk_communication refuses only the query that inner-joins it (403 on page 1: omitted, not a fault).
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice], number: "DR-00050"));
        world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00950");
        world.TableDenied[Alice] = ["sprk_communication"];

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00050")]);
        AllRecords(body).Should().BeEmpty();
        body.ToString().Should().NotContain("DR-00950");
    }

    [Fact]
    public async Task DecidedSet_ARecordThatResolvedASignal_FiledUnderAnEmail_WithNoConfirmer_IsListedOnlyUnderItsSignal()
    {
        // Fix round 2 (K3): an automated record filed under a communication that DID resolve a Signal (a nightly condition-cleared close of a
        // communication-subject Signal) is never a gate record: the gate read's "no Signal resolved" condition keeps it out of records.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var cleared = world.Decision(readers: [Alice], number: "DR-00060", by: null, communication: world.Record("sprk_communication", [Alice]),
            steps: "{\"reviewId\":\"0b7a9b5e-4444-4c2e-9a54-000000000226\",\"resolvedSignalIds\":[],\"steps\":[]}");
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: cleared);

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00060")]);
        AllRecords(body).Should().BeEmpty("it resolved a Signal, so it is that Signal's row, never also a record with no Open");
    }

    [Fact]
    public void TheNoSignalPattern_NeedsNoEscape_AndMatchesOnlyTheEmptyList()
    {
        // Fix round 2: `[` is a wildcard set in a Dataverse LIKE; the pattern uses none, so it means the same on Dataverse as here.
        SignalReadService.NoResolvedSignalsLike.Should().NotContain("[");
        SignalReadService.GateRecordsFetch(WriterUser).Should().Contain("resolvedSignalIds&quot;:_]%");
        SignalReadService.DecidedRecordsFetch(WriterUser).Should().Contain("operator=\"not-like\" value=\"%&quot;resolvedSignalIds&quot;:_]%\"");
    }

    [Fact]
    public async Task DecidedSet_TheRecordsThatResolvedSignals_AreStillBounded_503()
    {
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
            decisionRecord: world.Decision(readers: [Alice]));
        for (var i = 0; i < SignalReadService.MaxPages; i++)
        {
            world.Resolves(world.Decision(readers: [Alice], number: $"DR-0400{i}"), s);
        }

        (await GetAsync(world, Alice, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
