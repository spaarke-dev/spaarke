// Ontology task 235 (#3283, DI-180; owner decision D-234): the Decision Records tab (GET /api/v1/signals/worklist?set=decided) lists the
// writer's Record Procedure, billing and assign-firm Decision Records that resolved no Signal, each under its core record with its summary
// and no Open action, as task 226 (D-231) lists email-gate records. Before it, such a record was read by neither Decided query and not listed.
// Through the REAL route and SignalReadService over the same scripted caller Dataverse (World) as SignalReadRouteTests, of which this file is
// a part. It lives under tests/integration/regression/ (a KEEP path) and is compiled into the IntegrationTests assembly by the link in its
// csproj, and removed from the unit-test assembly's regression glob.
//
// Must-fire: one record of each kind the caller can read is listed in its core record's group, with no item to open. Must-not-fire: a
// record of a secure family the caller is not in, a record on a core record the caller cannot read (neither listed nor counted), a record
// that resolved a Signal (listed once, under its Signal), a record with no core record, and a record of no kind. Bound: each kind's read is
// its own, as the caller, paged to MaxPages and then 503, never a shorter list.
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    private const string ProcedureCode = "assign-procedure";
    private const string BillingCode = "accept-line";
    private const string NoSignalEnvelope = "{\"reviewId\":\"0b7a9b5e-5555-4c2e-9a54-000000000235\",\"resolvedSignalIds\":[],\"steps\":[]}";

    /// <summary>A writer record of one of D-234's kinds: it resolved no Signal, carries its catalog code, and is filed with its core.</summary>
    private static Guid KindDecision(World world, string code, Guid[] readers, (Guid Type, Guid Id)? core, string number,
        string name = "Judgement: Assign Procedure", string decidedOn = "2026-10-14T09:30:00Z") =>
        world.Decision(readers, number: number, decidedOn: decidedOn, name: name, core: core, actionCode: code, steps: NoSignalEnvelope);

    private static IEnumerable<JsonElement> DecideGroups(JsonElement body) => Lane(body, "Decide").GetProperty("groups").EnumerateArray();

    [Fact]
    public async Task DecidedSet_AProcedureABillingAndAnAssignFirmRecord_AreListedUnderTheirCoreRecord_WithTheirSummary_AndNoItemToOpen()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        var p = world.Record("sprk_project", [Alice], new() { ["sprk_projectname"] = "Office move", ["sprk_projectnumber"] = "PRJ-0007" });
        var procedure = KindDecision(world, ProcedureCode, [Alice], (MatterType, m), "DR-00700", decidedOn: "2026-10-12T09:30:00Z");
        var firm = KindDecision(world, InvoiceAssignFirmService.AssignFirmActionCode, [Alice], (MatterType, m), "DR-00701",
            name: "Judgement: Set firm", decidedOn: "2026-10-13T09:30:00Z");
        var billing = KindDecision(world, BillingCode, [Alice], (ProjectType, p), "DR-00702", name: "Judgement: Accept line");

        var body = await OkAsync(world, Alice, "?set=decided");

        AllItems(body).Should().BeEmpty("none of them resolved a Signal, so there is no Work Item to open (D-234)");
        var groups = DecideGroups(body).ToList();
        groups.Should().HaveCount(2);
        groups.Should().OnlyContain(g => !g.GetProperty("notFiled").GetBoolean());
        var matter = groups.Single(g => g.GetProperty("core").GetProperty("recordId").GetGuid() == m);
        matter.GetProperty("core").GetProperty("name").GetString().Should().Be("Acme v Widget");
        matter.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid())
            .Should().Equal([firm, procedure], "newest decision first");
        var project = groups.Single(g => g.GetProperty("core").GetProperty("recordId").GetGuid() == p);
        project.GetProperty("core").GetProperty("recordType").GetString().Should().Be("sprk_project");
        var listed = project.GetProperty("records").EnumerateArray().Single();
        listed.GetProperty("recordId").GetGuid().Should().Be(billing);
        listed.GetProperty("summary").GetString().Should().Be("Judgement: Accept line");
        listed.GetProperty("decidedByName").GetString().Should().Be("Dana Decider", "the record's own confirming user");
        listed.GetProperty("steps").ValueKind.Should().Be(JsonValueKind.Null, "nothing replays a record with no Open");
        listed.GetProperty("followOns").GetArrayLength().Should().Be(0);
        Lane(body, "Do").GetProperty("groups").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DecidedSet_AKindRecordAndAGateRecordOnOneMatter_ShareItsGroup_NewestFirst_AndTheGateListingIsUnchanged()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var gate = world.GateDecision(readers: [Alice], core: (MatterType, m), number: "DR-00900", decidedOn: "2026-10-14T09:30:00Z");
        var procedure = KindDecision(world, ProcedureCode, [Alice], (MatterType, m), "DR-00703", decidedOn: "2026-10-13T09:30:00Z");

        var group = DecideGroups(await OkAsync(world, Alice, "?set=decided")).Single();

        group.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid()).Should().Equal(gate, procedure);
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_AKindRecordOfASecureFamilyTheCallerIsNotIn_IsNeverListed()
    {
        // Dataverse trims the caller's record read by the mirrored secure-child shares (D-33/D-123), and the core join by the caller's read
        // of the secure matter: the fake's readers stand for both.
        var world = new World();
        var secure = world.Matter(readers: [Bob]);
        KindDecision(world, ProcedureCode, [Bob], (MatterType, secure), "DR-00710");
        KindDecision(world, BillingCode, [Bob], (MatterType, secure), "DR-00711");
        KindDecision(world, InvoiceAssignFirmService.AssignFirmActionCode, [Bob], (MatterType, secure), "DR-00712");

        var alice = await OkAsync(world, Alice, "?set=decided");
        AllRecords(alice).Should().BeEmpty();
        alice.ToString().Should().NotContain("DR-0071");

        RecordNumbers(await OkAsync(world, Bob, "?set=decided")).Should().BeEquivalentTo(["DR-00710", "DR-00711", "DR-00712"]);
    }

    [Fact]
    public async Task DecidedSet_KindRecordsOnACoreRecordTheCallerCannotRead_AreNotReturned_NorCounted_SoTheTabAnswers()
    {
        // The #3277 F1 lesson: a Console User's Deep read returns the business unit's records (D-144). Only the records whose core the
        // caller reads come back (the inner core join), so a busy unit's volume never 503s the tab.
        var world = new World { PageSize = 1 };
        var hidden = world.Matter(readers: [Bob]);
        var mine = world.Matter(readers: [Alice, Bob]);
        for (var i = 0; i < SignalReadService.MaxPages + 2; i++)
        {
            KindDecision(world, BillingCode, [Alice, Bob], (MatterType, hidden), $"DR-0500{i}");
        }

        KindDecision(world, BillingCode, [Alice, Bob], (MatterType, mine), "DR-00720", decidedOn: "2026-10-14T10:00:00Z");

        var alice = await OkAsync(world, Alice, "?set=decided");
        RecordNumbers(alice).Should().Equal("DR-00720");
        alice.ToString().Should().NotContain("DR-0500");

        // Must fire: the caller who CAN read them all is over the bound, and is told so (503), never shown a shorter history.
        (await GetAsync(world, Bob, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(ProcedureCode)]
    [InlineData(BillingCode)]
    [InlineData("assign-firm")]
    public async Task DecidedSet_EachKindsRead_IsItsOwn_RunAsTheCaller_PagedToMaxPages_Then503(string code)
    {
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        KindDecision(world, code, [Alice], (MatterType, m), "DR-00730", decidedOn: "2026-10-13T09:00:00Z");
        KindDecision(world, code, [Alice], (MatterType, m), "DR-00731", decidedOn: "2026-10-12T09:00:00Z");

        RecordNumbers(await OkAsync(world, Alice, "?set=decided")).Should().Equal("DR-00730", "DR-00731");
        // As the caller: the kind's query went through the caller's own Dataverse client, paged (a second page asked for).
        var kindFetches = world.Requests.Where(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal))
            .Select(r => Uri.UnescapeDataString(r).Replace('\'', '"'))
            .Where(f => f.Contains($"<value>{code}</value>", StringComparison.Ordinal) && f.Contains("name=\"sprk_matter\"", StringComparison.Ordinal))
            .ToList();
        kindFetches.Should().Contain(f => f.Contains("page=\"2\"", StringComparison.Ordinal));

        for (var i = 0; i < SignalReadService.MaxPages; i++)
        {
            KindDecision(world, code, [Alice], (MatterType, m), $"DR-0600{i}");
        }

        // Every other kind has nothing to read, so the 503 is this kind's own bound (the truncation signal: never a shorter history).
        (await GetAsync(world, Alice, "?set=decided")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DecidedSet_TwoKindsEachWithinTheirOwnBound_AreAllListed_NotOneSharedBound()
    {
        // The kinds are separate reads: MaxPages pages of each is within each read's bound, where one shared read would be over it (503).
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < SignalReadService.MaxPages; i++)
        {
            KindDecision(world, ProcedureCode, [Alice], (MatterType, m), $"DR-0700{i}");
            KindDecision(world, InvoiceAssignFirmService.AssignFirmActionCode, [Alice], (MatterType, m), $"DR-0710{i}");
        }

        RecordNumbers(await OkAsync(world, Alice, "?set=decided")).Should().HaveCount(2 * SignalReadService.MaxPages);
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_ABillingRecordThatResolvedASignal_IsListedOnce_UnderItsSignal()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var billing = world.Decision(readers: [Alice], number: "DR-00750", core: (MatterType, m), actionCode: BillingCode);
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1), decisionRecord: billing);

        var body = await OkAsync(world, Alice, "?set=decided");

        DecidedRows(body).Should().Equal([(s, "DR-00750")]);
        AllRecords(body).Should().BeEmpty("it resolved a Signal, so it is that Signal's row, never also a record with no Open");
    }

    [Fact]
    public async Task DecidedSet_MustNotFire_AKindRecordWithNoCoreRecord_OrOfNoKind_OrDecidedBeforeTheWindow_OrNotTheWriters_IsNeverListed()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        KindDecision(world, ProcedureCode, [Alice], core: null, "DR-00760"); // never listed Not filed (D-234: under the core record)
        KindDecision(world, "reassign", [Alice], (MatterType, m), "DR-00761"); // a Signals code: not one of the kinds
        KindDecision(world, ProcedureCode, [Alice], (MatterType, m), "DR-00762", decidedOn: world.Now.AddDays(-91).ToString("o"));
        world.Decision(readers: [Alice], number: "DR-00763", core: (MatterType, m), actionCode: BillingCode, steps: NoSignalEnvelope,
            createdBy: SomePerson); // a person's row, not the writer's

        var body = await OkAsync(world, Alice, "?set=decided");

        AllRecords(body).Should().BeEmpty();
        body.ToString().Should().NotContain("DR-0076");
    }

    [Fact]
    public async Task DecidedSet_ACallerRefusedOneCoreTable_StillGetsTheKindRecordsOnTheOthers()
    {
        // No Read on sprk_project refuses only the queries that inner-join it (403 on page 1: omitted, not a fault).
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var p = world.Record("sprk_project", [Alice]);
        KindDecision(world, ProcedureCode, [Alice], (MatterType, m), "DR-00770");
        KindDecision(world, ProcedureCode, [Alice], (ProjectType, p), "DR-00771");
        world.TableDenied[Alice] = ["sprk_project"];

        RecordNumbers(await OkAsync(world, Alice, "?set=decided")).Should().Equal("DR-00770");
    }

    [Fact]
    public void TheKinds_AreTheWritersCodeSets_Disjoint_AndEachReadIsBoundedByKindAndCore()
    {
        SignalReadService.SignalLessKinds.Select(k => k.Codes.ToHashSet()).Should().HaveCount(3);
        var procedure = SignalReadService.SignalLessKinds.Single(k => k.Codes.Contains(ProcedureCode));
        procedure.Codes.Should().BeEquivalentTo(DecisionActionCatalog.RecordProcedureActions.Select(d => d.Code));
        var firm = SignalReadService.SignalLessKinds.Single(k => k.Codes.Contains(InvoiceAssignFirmService.AssignFirmActionCode));
        firm.Codes.Should().Equal(InvoiceAssignFirmService.AssignFirmActionCode);
        var billing = SignalReadService.SignalLessKinds.Single(k => k.Codes.Contains(BillingCode));
        billing.Codes.Should().BeEquivalentTo(DecisionActionCatalog.BillingActions.Select(d => d.Code).Where(c => c != "assign-firm"));
        SignalReadService.SignalLessKinds.SelectMany(k => k.Codes).Should().OnlyHaveUniqueItems();
        SignalReadService.SignalLessKinds.Select(k => k.TooMany).Should().OnlyHaveUniqueItems();

        foreach (var kind in SignalReadService.SignalLessKinds)
        {
            foreach (var (core, lineage) in DecisionRecordWriter.LineageColumnByCoreEntity)
            {
                var fetch = SignalReadService.SignalLessRecordsFetch(WriterUser, kind, core);
                fetch.Should().Contain($"<link-entity name=\"{core}\" from=\"{core}id\" to=\"{lineage}\" link-type=\"inner\"")
                    .And.Contain($"<condition attribute=\"createdby\" operator=\"eq\" value=\"{WriterUser:D}\" />")
                    .And.Contain("<condition attribute=\"createdonbehalfby\" operator=\"null\" />")
                    .And.Contain("<condition attribute=\"sprk_decidedon\" operator=\"last-x-days\" value=\"90\" />")
                    .And.Contain("<condition attribute=\"sprk_steps\" operator=\"like\" value=\"%&quot;resolvedSignalIds&quot;:_]%\" />")
                    .And.Contain("<condition attribute=\"sprk_actioncode\" operator=\"in\">")
                    .And.NotContain("\"sprk_steps\" />").And.NotContain("\"sprk_followons\" />");
                foreach (var code in kind.Codes) fetch.Should().Contain($"<value>{code}</value>");
            }
        }
    }
}
