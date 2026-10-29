// spaarke-ontology-platform-r1 task 038 (D-15, FR-24, FR-54; D-34..D-36, D-42, D-44; D-129, D-137) — the Signal read route
// GET /api/v1/signals/worklist through the REAL BFF pipeline (routing, RequireAuthorization, rate limiting, the handler,
// SignalReadService) with ONE boundary substituted: the caller's Dataverse. The fake below answers exactly what Dataverse
// would answer AS EACH USER: which Signals their business unit reads, which core records and subjects they may read, and it
// EVALUATES the OData the service sends (the In() id list, the "mine" clauses, the flag filter), so a missing or wrong
// filter in the service shows up as a wrong answer here. The worklist configuration is the REPO's task 050 row, verbatim.
// D-185 (task 163): the policy short name, work type and severity are read by the BFF (app-only), not joined as the caller. The fake
// returns the link-entity's columns only when the query carries the link, and refuses a query that carries it when the caller has no
// Read on sprk_policy (World.CallerHasNoPolicyRead, Dataverse 0x80040220), so the repo row is proven to need no policy Read.
// The opt-in live leg (SignalReadRouteLiveTests) runs the same route as real low-privilege users against spaarkedev1.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed class SignalReadRouteTests : IClassFixture<SignalReadRouteTests.WorklistFactory>
{
    private const string Route = "/api/v1/signals/worklist";
    private const int Decide = 100000000;
    private const int Do = 100000001;
    private const int Critical = 100000002;
    private const int Warning = 100000001;
    private const int Info = 100000000;

    // Catalog rows (sprk_recordtype_ref ids) — the ONLY place a core type is named, as data.
    private static readonly Guid MatterType = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ProjectType = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid WorkAssignmentType = Guid.Parse("11111111-0000-0000-0000-000000000003");
    private static readonly Guid ServiceRequestType = Guid.Parse("11111111-0000-0000-0000-000000000004");
    private static readonly Guid NotCoreType = Guid.Parse("11111111-0000-0000-0000-000000000005");
    private static readonly Guid EventType = Guid.Parse("11111111-0000-0000-0000-000000000006");
    private static readonly Guid TodoType = Guid.Parse("11111111-0000-0000-0000-000000000007");
    private static readonly Guid CommunicationType = Guid.Parse("11111111-0000-0000-0000-000000000008");

    // People. Alice and Bob are in the same BU tree (both read every Signal); only Alice reads matter M.
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AliceContact = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid BobContact = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000c1");
    private static readonly Guid Carol = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid CarolContact = Guid.Parse("cccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid OtherBuUser = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid Team = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    /// <summary>The ontology writer's own systemuserid (WhoAmI on its connection): the createdby of every genuine Decision Record.</summary>
    private static readonly Guid WriterUser = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid SomePerson = Guid.Parse("99999999-0000-0000-0000-000000000001");

    private readonly WorklistFactory _factory;

    public SignalReadRouteTests(WorklistFactory factory) => _factory = factory;

    // ---------------------------------------------------------------------------------------------------------------
    // Authentication, the single 403, faults
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NoToken_Is401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CallerWithNoDataverseUser_IsTheSingle403()
    {
        var world = new World();
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])));
        var response = await GetAsync(world, actingAs: null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Problem(response)).GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.caller_unresolved");
    }

    [Fact]
    public async Task UnknownSet_Is400()
    {
        var response = await GetAsync(new World(), Alice, "?set=everything");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CoreReadFault_Is503_NeverAShorterList()
    {
        var world = new World();
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])));
        world.FaultingTables.Add("sprk_matter");
        var response = await GetAsync(world, Alice);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Problem(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.signal.worklist_unavailable");
    }

    [Fact]
    public async Task CallerWithNoSignalRead_GetsAnEmptyList_NotAnError()
    {
        var world = new World();
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])));
        world.TableDenied[Alice] = ["sprk_signal"];
        _factory.Logs.Warnings.Clear();
        var body = await OkAsync(world, Alice);
        AllItems(body).Should().BeEmpty();
        body.GetProperty("lanes").GetArrayLength().Should().Be(2);
        // Task 149 (uac-r2 K3 on #1891): an empty worklist from a refused read looks exactly like "no work", so the refusal is
        // logged at Warning, the level an operator filters on (LogCapture keeps Warning and above).
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the caller has no read on sprk_signal", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Core-record check (D-15, D-34, D-36)
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MatterReader_GetsTheSignal_WithItsDisplayColumns()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Acme v Widget", number: "MAT-0042");
        var s = world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap",
            title: "Fee change without a budget revision", subjectName: "RE: revised fees", firstDetected: "2026-10-01T09:00:00Z",
            evidence: "[{\"type\":\"sprk_communication\"}]");

        var body = await OkAsync(world, Alice);

        var group = Lane(body, "Decide").GetProperty("groups").EnumerateArray().Single();
        var core = group.GetProperty("core");
        core.GetProperty("recordType").GetString().Should().Be("sprk_matter");
        core.GetProperty("recordId").GetGuid().Should().Be(m);
        core.GetProperty("typeLabel").GetString().Should().Be("Matter");
        core.GetProperty("name").GetString().Should().Be("Acme v Widget");
        core.GetProperty("number").GetString().Should().Be("MAT-0042");
        group.GetProperty("notFiled").GetBoolean().Should().BeFalse();

        var item = group.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("signalId").GetGuid().Should().Be(s);
        item.GetProperty("lane").GetString().Should().Be("Decide");
        item.GetProperty("status").GetString().Should().Be("Open");
        item.GetProperty("title").GetString().Should().Be("Fee change without a budget revision");
        item.GetProperty("subjectName").GetString().Should().Be("RE: revised fees");
        item.GetProperty("ruleShortName").GetString().Should().Be("Budget gap");
        item.GetProperty("workType").GetString().Should().Be("approveOrRebudget");
        item.GetProperty("pastDueWording").GetString().Should().Be("overdue");
        item.GetProperty("raisedOn").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-10-01T09:00:00Z"));
        item.GetProperty("evidenceRefs").GetString().Should().Contain("sprk_communication");
        item.GetProperty("policyVersionId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SameBuColleagueWhoCannotReadTheMatter_GetsNothing_AndNothingRevealsIt()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice], name: "Secret Matter", number: "MAT-SECRET");
        var s = world.Signal(Decide, core: (MatterType, m), title: "Hidden headline", subjectName: "Hidden subject");

        var response = await GetAsync(world, Bob);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();

        AllItems(JsonDocument.Parse(raw).RootElement).Should().BeEmpty();
        raw.Should().NotContain(m.ToString("D")).And.NotContain("Secret Matter").And.NotContain("MAT-SECRET")
            .And.NotContain(s.ToString("D")).And.NotContain("Hidden").And.NotContain("\"count\"");
    }

    [Fact]
    public async Task ReaderInAnotherBusinessUnit_DoesNotSeeTheSignal()
    {
        var world = new World();
        var s = world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice, OtherBuUser])));
        world.SignalReaders[s] = [Alice, Bob]; // Dataverse trims the Signal by its own BU: OtherBuUser is outside the tree

        AllItems(await OkAsync(world, OtherBuUser)).Should().BeEmpty();
        AllItems(await OkAsync(world, Alice)).Should().ContainSingle();
    }

    [Fact]
    public async Task EveryCoreType_GoesThroughTheSameCatalogDrivenCheck()
    {
        var world = new World();
        var p = world.Record("sprk_project", readers: [Alice]);
        var wa = world.Record("sprk_workassignment", readers: [Alice]);
        var sr = world.Record("sprk_servicerequest", readers: [Alice, Bob]);
        world.Signal(Decide, core: (ProjectType, p));
        world.Signal(Decide, core: (WorkAssignmentType, wa));
        world.Signal(Decide, core: (ServiceRequestType, sr));

        var alice = await OkAsync(world, Alice);
        Lane(alice, "Decide").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("core").GetProperty("recordType").GetString())
            .Should().BeEquivalentTo("sprk_project", "sprk_workassignment", "sprk_servicerequest");

        // D-34 / D-36: Bob reads the service request but not the project or the work assignment.
        var bob = await OkAsync(world, Bob);
        Lane(bob, "Decide").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("core").GetProperty("recordId").GetGuid())
            .Should().Equal(sr);
    }

    [Fact]
    public async Task ACoreTypeOutsideTheAccessTaxonomy_OrAHalfPair_IsDropped()
    {
        var world = new World();
        var notCore = world.Record("sprk_invoice", readers: [Alice]);
        world.Signal(Decide, core: (NotCoreType, notCore));             // K2: not a CoreAncestorResolver core type
        world.Signal(Decide, core: (Guid.NewGuid(), notCore));          // a type id with no catalog row
        world.Signal(Do, core: (MatterType, Guid.Empty), subject: (TodoType, world.Todo(owner: Alice))); // half pair

        AllItems(await OkAsync(world, Alice)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneBatchedReadPerCoreTypePerChunk_NotOnePerRow()
    {
        var world = new World();
        for (var i = 0; i < 150; i++)
        {
            world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])));
        }

        AllItems(await OkAsync(world, Alice)).Should().HaveCount(150);
        world.Requests.Count(r => r.StartsWith("sprk_matters?", StringComparison.Ordinal)
                && !r.Contains("sprk_highpriority", StringComparison.Ordinal))
            .Should().Be(2, "150 matter ids in chunks of SignalReadService.IdChunk (100), never one query per row");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Do lane: the caller's own work (D-15, D-44, D-129, D-137)
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EventDoRows_OwnerAssigneeAndCreatorSeeThem_AColleagueDoesNot()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice, Bob, Carol]);
        var owned = world.Event(owner: Alice);
        var assigned = world.Event(owner: Team, assignedTo: AliceContact);
        var created = world.Event(owner: Team, createdBy: Alice);
        var someoneElses = world.Event(owner: Team, assignedTo: CarolContact, createdBy: Carol);
        foreach (var e in new[] { owned, assigned, created, someoneElses })
        {
            world.Signal(Do, core: (MatterType, m), subject: (EventType, e));
        }

        DoSubjects(world, await OkAsync(world, Alice)).Should().BeEquivalentTo(new[] { owned, assigned, created });
        DoSubjects(world, await OkAsync(world, Bob)).Should().BeEmpty("Bob reads the matter and the events but owns none of the work");
        DoSubjects(world, await OkAsync(world, Carol)).Should().BeEquivalentTo(new[] { someoneElses });
    }

    [Fact]
    public void EventMine_IsTheTask097DecisionBClause()
    {
        // Parity with the events list: the generic "mine" clause for sprk_event is exactly EventOwnershipScope's.
        var rule = SignalReadService.DoLaneMineRules["sprk_event"];
        // The service wraps the clause in parentheses where it ANDs it with the id list; ToFilter wraps it itself.
        $"({SignalReadService.MineFilter(rule, Alice, AliceContact)})"
            .Should().Be(new EventOwnershipScope(Alice, AliceContact, Alice).ToFilter(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TodoDoRows_OwnerOrAssigneeSeeThem_AnyoneElseDoesNot()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice, Bob]);
        var mine = world.Todo(owner: Alice);
        var assignedToMe = world.Todo(owner: Team, assignedTo: AliceContact);
        var bobs = world.Todo(owner: Bob, readers: [Alice, Bob]);
        foreach (var t in new[] { mine, assignedToMe, bobs })
        {
            world.Signal(Do, core: (MatterType, m), subject: (TodoType, t));
        }

        DoSubjects(world, await OkAsync(world, Alice)).Should().BeEquivalentTo(new[] { mine, assignedToMe });
        DoSubjects(world, await OkAsync(world, Bob)).Should().BeEquivalentTo(new[] { bobs });
    }

    [Fact]
    public async Task WorkAssignment_AssignerAndAssigneeSeeIt_AThirdColleagueDoesNot()
    {
        var world = new World();
        var wa = world.WorkAssignment(createdBy: Alice, assignedToInternal: BobContact, readers: [Alice, Bob, Carol]);
        world.Signal(Do, core: (WorkAssignmentType, wa), subject: (WorkAssignmentType, wa), workType: 100000005);

        var assigner = await OkAsync(world, Alice);
        DoSubjects(world, assigner).Should().Equal(wa);
        Lane(assigner, "Do").GetProperty("groups")[0].GetProperty("items")[0].GetProperty("pastDueWording").GetString()
            .Should().Be("late", "a work assignment reads \"3d late\" (v4 W-3)");
        DoSubjects(world, await OkAsync(world, Bob)).Should().Equal(wa);
        DoSubjects(world, await OkAsync(world, Carol)).Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignment_ReadableButUnreadableCore_IsAbsent()
    {
        var world = new World();
        var wa = world.WorkAssignment(createdBy: Alice, assignedToInternal: AliceContact, readers: []);
        world.Signal(Do, core: (WorkAssignmentType, wa), subject: (WorkAssignmentType, wa));
        world.Signal(Decide, core: (ServiceRequestType, world.Record("sprk_servicerequest", readers: [Alice])));

        var body = await OkAsync(world, Alice);
        DoSubjects(world, body).Should().BeEmpty("the caller cannot read the work assignment (its own core record)");
        Lane(body, "Decide").GetProperty("groups").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task NoCoreSignal_ToDoOwnerGetsItNotFiled_ASameBuColleagueDoesNot()
    {
        var world = new World();
        var todo = world.Todo(owner: Alice, readers: [Alice, Bob]); // Bob is in the same BU and can even read the To Do
        var s = world.Signal(Do, core: null, subject: (TodoType, todo));
        world.Signal(Decide, core: null, subject: (CommunicationType, Guid.NewGuid())); // a no-core Decide row is never returned

        var alice = await OkAsync(world, Alice);
        var group = Lane(alice, "Do").GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("notFiled").GetBoolean().Should().BeTrue();
        group.GetProperty("core").ValueKind.Should().Be(JsonValueKind.Null);
        group.GetProperty("items")[0].GetProperty("signalId").GetGuid().Should().Be(s);
        Lane(alice, "Decide").GetProperty("groups").GetArrayLength().Should().Be(0);

        AllItems(await OkAsync(world, Bob)).Should().BeEmpty();
    }

    [Fact]
    public async Task DoRowWithAnUntypedSubject_IsJudgedByWhicheverDoTableHoldsIt()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var e = world.Event(owner: Alice);
        world.Signal(Do, core: (MatterType, m), subject: (null, e));
        DoSubjects(world, await OkAsync(world, Alice)).Should().Equal(e);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Task 154: the row's sentence and subject reference; Not filed last; no request of their own
    // ---------------------------------------------------------------------------------------------------------------

    private static bool IsNull(JsonElement item, string property) =>
        !item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    private static JsonElement ItemOf(JsonElement body, Guid signalId) =>
        AllItems(body).Single(i => i.GetProperty("signalId").GetGuid() == signalId);

    [Fact]
    public async Task EveryRow_CarriesItsSentenceAsStored_AndNullWhenEmpty()
    {
        // The route runs the configuration's FetchXML verbatim and the fake returns sprk_sentence only when a query selects it,
        // so this also proves the seed selects it.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var todo = world.Todo(owner: Alice);
        var decide = world.Signal(Decide, core: (MatterType, m), sentence: "Fees changed on 3 Oct with no budget revision.");
        var doRow = world.Signal(Do, core: (MatterType, m), subject: (TodoType, todo), sentence: "  Record the outcome by Friday.  ");
        var blank = world.Signal(Decide, core: (MatterType, m), sentence: "   ");
        var none = world.Signal(Decide, core: (MatterType, m));

        var body = await OkAsync(world, Alice);

        ItemOf(body, decide).GetProperty("sentence").GetString().Should().Be("Fees changed on 3 Oct with no budget revision.");
        ItemOf(body, doRow).GetProperty("sentence").GetString().Should().Be("  Record the outcome by Friday.  ", "as stored, never rewritten");
        IsNull(ItemOf(body, blank), "sentence").Should().BeTrue();
        IsNull(ItemOf(body, none), "sentence").Should().BeTrue();
    }

    [Fact]
    public async Task ADoRow_CarriesItsSubjectReference_FromTheTableWhoseMineReadReturnedIt_ADecideRowDoesNot()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var todo = world.Todo(owner: Alice);
        var ev = world.Event(owner: Alice);
        var typedTodo = world.Signal(Do, core: (MatterType, m), subject: (TodoType, todo));
        var untypedEvent = world.Signal(Do, core: (MatterType, m), subject: (null, ev));
        var notFiled = world.Signal(Do, core: null, subject: (TodoType, todo));
        var decide = world.Signal(Decide, core: (MatterType, m), subject: (MatterType, m));

        var body = await OkAsync(world, Alice);

        ItemOf(body, typedTodo).GetProperty("subjectRecordType").GetString().Should().Be("sprk_todo");
        ItemOf(body, typedTodo).GetProperty("subjectRecordId").GetGuid().Should().Be(todo);
        ItemOf(body, untypedEvent).GetProperty("subjectRecordType").GetString().Should().Be("sprk_event", "whichever Do table holds it");
        ItemOf(body, untypedEvent).GetProperty("subjectRecordId").GetGuid().Should().Be(ev);
        ItemOf(body, notFiled).GetProperty("subjectRecordType").GetString().Should().Be("sprk_todo");
        ItemOf(body, notFiled).GetProperty("subjectRecordId").GetGuid().Should().Be(todo);
        IsNull(ItemOf(body, decide), "subjectRecordType").Should().BeTrue("a Decide row's subject is not offered for opening");
        IsNull(ItemOf(body, decide), "subjectRecordId").Should().BeTrue();
    }

    [Fact]
    public async Task NotFiled_IsLastInTheDoLane_EvenWhenItsItemOutranksEveryFiledGroup()
    {
        var world = new World();
        var mA = world.Matter(readers: [Alice], number: "MAT-0002");
        var mB = world.Matter(readers: [Alice], number: "MAT-0001");
        var loose = world.Todo(owner: Alice);
        var notFiled = world.Signal(Do, core: null, subject: (TodoType, loose), severity: Critical, firstDetected: "2026-01-01T00:00:00Z");
        world.Signal(Do, core: (MatterType, mA), subject: (TodoType, world.Todo(owner: Alice)), severity: Warning);
        world.Signal(Do, core: (MatterType, mB), subject: (TodoType, world.Todo(owner: Alice)), severity: Info);

        var groups = Lane(await OkAsync(world, Alice), "Do").GetProperty("groups").EnumerateArray().ToList();

        groups.Select(g => g.GetProperty("notFiled").GetBoolean()).Should().Equal(false, false, true);
        groups.Take(2).Select(g => g.GetProperty("core").GetProperty("recordId").GetGuid()).Should().Equal([mA, mB], "filed groups keep D-42 order");
        groups[2].GetProperty("items")[0].GetProperty("signalId").GetGuid().Should().Be(notFiled);
    }

    [Fact]
    public async Task NotFiled_IsLastInTheDoLane_InTheClosedTodaySetToo()
    {
        // Adversarial pass 1 F4: the order holds in every set, not only the lanes. The closed query orders by the core pair first, so
        // the Not filed item (no core) is the first row the fake returns.
        var world = new World();
        var mA = world.Matter(readers: [Alice], number: "MAT-0002");
        var mB = world.Matter(readers: [Alice], number: "MAT-0001");
        var closed = world.Now.AddMinutes(-5);
        var notFiled = world.Signal(Do, core: null, subject: (TodoType, world.Todo(owner: Alice)), sets: ["closedToday", "closedEarlier"], resolvedOn: closed);
        world.Signal(Do, core: (MatterType, mA), subject: (TodoType, world.Todo(owner: Alice)), sets: ["closedToday", "closedEarlier"], resolvedOn: closed);
        world.Signal(Do, core: (MatterType, mB), subject: (TodoType, world.Todo(owner: Alice)), sets: ["closedToday", "closedEarlier"], resolvedOn: closed);

        var groups = Lane(await OkAsync(world, Alice, "?set=closedToday"), "Do").GetProperty("groups").EnumerateArray().ToList();

        groups.Select(g => g.GetProperty("notFiled").GetBoolean()).Should().Equal(false, false, true);
        groups.Take(2).Select(g => g.GetProperty("core").GetProperty("recordId").GetGuid()).Should().Equal([mA, mB], "filed groups keep the configuration's order");
        groups[2].GetProperty("items")[0].GetProperty("signalId").GetGuid().Should().Be(notFiled);
    }

    [Fact]
    public async Task TheWorklistsMineRead_NeverSelectsTheTodosNotes()
    {
        // Adversarial pass 1 K3: only the per-Signal gate's single-id read carries the inquiry hint column.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Do, core: (MatterType, m), subject: (TodoType, world.Todo(owner: Alice)));
        world.Signal(Do, core: (MatterType, m), subject: (TodoType, world.Todo(owner: Alice)));

        AllItems(await OkAsync(world, Alice)).Should().HaveCount(2);

        var mine = world.Requests.Where(r => r.StartsWith("sprk_todos?$select=", StringComparison.Ordinal)).ToList();
        mine.Should().NotBeEmpty();
        mine.Should().OnlyContain(r => !r.Contains("sprk_notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheNewFields_AddNoRequest_OneRowPerLaneAndManyRowsSendTheSameRequests()
    {
        static World Build(int perLane)
        {
            var world = new World();
            var m = world.Matter(readers: [Alice]);
            for (var i = 0; i < perLane; i++)
            {
                world.Signal(Decide, core: (MatterType, m), sentence: $"Decide {i}");
                world.Signal(Do, core: (MatterType, m), subject: (TodoType, world.Todo(owner: Alice)), sentence: $"Do {i}");
            }

            return world;
        }

        var one = Build(1);
        var many = Build(40);

        AllItems(await OkAsync(one, Alice)).Should().HaveCount(2);
        var body = await OkAsync(many, Alice);
        AllItems(body).Should().HaveCount(80);
        AllItems(body).Should().OnlyContain(i => i.GetProperty("sentence").GetString()!.Length > 0
            && (i.GetProperty("lane").GetString() == "Decide" || i.GetProperty("subjectRecordType").GetString() == "sprk_todo"));
        many.Requests.Count.Should().Be(one.Requests.Count, "the sentence and subject reference add no per-row read");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D-42 order
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LaneOrder_IsSeverityThenHighPriorityThenOldestThenRecordNumber_AndRepeatable()
    {
        var world = new World();
        var mA = world.Matter(readers: [Alice], number: "MAT-0002");
        var mB = world.Matter(readers: [Alice], number: "MAT-0001");
        var mC = world.Matter(readers: [Alice], number: "MAT-0003", highPriority: true);
        var mD = world.Matter(readers: [Alice], number: "MAT-0004");
        var mE = world.Matter(readers: [Alice], number: "MAT-0000");

        var aInfo = world.Signal(Decide, core: (MatterType, mA), severity: Info, firstDetected: "2026-09-01T00:00:00Z");
        var aCrit = world.Signal(Decide, core: (MatterType, mA), severity: Critical, firstDetected: "2026-10-05T00:00:00Z");
        var bCrit = world.Signal(Decide, core: (MatterType, mB), severity: Critical, firstDetected: "2026-10-05T00:00:00Z");
        var cWarnFlag = world.Signal(Decide, core: (MatterType, mC), severity: Warning, firstDetected: "2026-10-09T00:00:00Z",
            subject: (MatterType, mC));
        var dWarnOld = world.Signal(Decide, core: (MatterType, mD), severity: Warning, firstDetected: "2026-09-20T00:00:00Z");
        var eNone = world.Signal(Decide, core: (MatterType, mE), severity: null, firstDetected: "2026-01-01T00:00:00Z");

        var first = await OkAsync(world, Alice);
        var groups = Lane(first, "Decide").GetProperty("groups").EnumerateArray().ToList();

        // Groups by their best item: B and A tie on Critical + same day → record number (MAT-0001 < MAT-0002); then Warning with
        // the subject's sprk_highpriority before the older unflagged Warning; severity-less last.
        groups.Select(g => g.GetProperty("core").GetProperty("recordId").GetGuid()).Should().Equal(mB, mA, mC, mD, mE);
        // A's items stay together at its best item, best first.
        groups[1].GetProperty("items").EnumerateArray().Select(i => i.GetProperty("signalId").GetGuid()).Should().Equal(aCrit, aInfo);

        var second = await OkAsync(world, Alice);
        Ids(second).Should().Equal(Ids(first), "two calls over the same data give the same order (no model input)");
        _ = (bCrit, cWarnFlag, dWarnOld, eNone);
    }

    [Fact]
    public async Task WithinAGroup_OldestFirstAfterSeverityAndFlag()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var flagged = world.Todo(owner: Alice, highPriority: true);
        var plain = world.Todo(owner: Alice);
        var newer = world.Signal(Do, core: (MatterType, m), severity: Warning, firstDetected: "2026-10-10T00:00:00Z", subject: (TodoType, plain));
        var older = world.Signal(Do, core: (MatterType, m), severity: Warning, firstDetected: "2026-10-01T00:00:00Z", subject: (TodoType, world.Todo(owner: Alice)));
        var flaggedNewest = world.Signal(Do, core: (MatterType, m), severity: Warning, firstDetected: "2026-10-12T00:00:00Z", subject: (TodoType, flagged));

        var items = Lane(await OkAsync(world, Alice), "Do").GetProperty("groups")[0].GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("signalId").GetGuid());
        items.Should().Equal(flaggedNewest, older, newer);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Secondary sets (task 050)
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SecondarySets_EachReturnOnlyTheirSet_UnderTheSameCallerFilters()
    {
        var world = new World();
        var readable = world.Matter(readers: [Alice]);
        var hidden = world.Matter(readers: [Bob]);
        var now = world.Now;
        var today = world.Signal(Decide, core: (MatterType, readable), sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddSeconds(-1));
        var earlier = world.Signal(Decide, core: (MatterType, readable), sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddHours(-49));
        world.Signal(Decide, core: (MatterType, hidden), sets: ["closedToday", "closedEarlier"], resolvedOn: now.AddMinutes(-5));
        var suppressed = world.Signal(Decide, core: (MatterType, readable), sets: ["suppressed"], suppressedUntil: now.AddDays(10));
        world.Signal(Decide, core: (MatterType, hidden), sets: ["suppressed"], suppressedUntil: now.AddDays(10));
        var bobsTodo = world.Todo(owner: Bob, readers: [Alice, Bob]);
        world.Signal(Do, core: (MatterType, readable), subject: (TodoType, bobsTodo), sets: ["suppressed"], suppressedUntil: now.AddDays(3));
        var open = world.Signal(Decide, core: (MatterType, readable));

        Ids(await OkAsync(world, Alice, "?set=closedToday")).Should().Equal(today);
        Ids(await OkAsync(world, Alice, "?set=closedEarlier")).Should().Equal(earlier);
        var suppressedBody = await OkAsync(world, Alice, "?set=suppressed");
        Ids(suppressedBody).Should().Equal(suppressed);
        suppressedBody.GetProperty("set").GetString().Should().Be("suppressed");
        Ids(await OkAsync(world, Alice)).Should().Equal(open);
    }

    [Fact]
    public async Task ClosedSets_ADoItem_IsTodayOrEarlierByItsAssigneesZone_NotUtc()
    {
        // D-25 as amended by D-129. 02:30 UTC on Oct 14 is 22:30 on Oct 13 in New York. Resolved 23:00 UTC Oct 13 = 19:00 Oct 13 ET:
        // today for an Eastern assignee, earlier by the UTC date.
        var world = new World { Now = DateTimeOffset.Parse("2026-10-14T02:30:00Z", System.Globalization.CultureInfo.InvariantCulture) };
        world.EasternUserOf(AliceContact);
        var m = world.Matter(readers: [Alice]);
        var resolved = DateTimeOffset.Parse("2026-10-13T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var s = world.Signal(Do, core: (MatterType, m), subject: (EventType, world.Event(owner: Team, assignedTo: AliceContact)),
            sets: ["closedToday", "closedEarlier"], resolvedOn: resolved);

        Ids(await OkAsync(world, Alice, "?set=closedToday")).Should().Equal([s], "its assignee's day is still Oct 13");
        Ids(await OkAsync(world, Alice, "?set=closedEarlier")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClosedSets_ADecideItem_UsesItsSubjectsAssignedToZone_AndUtcWithoutOne()
    {
        // F4 (review): a Decide item's individual is its subject's Assigned To (D-129; sprk_assignedtointernal on a matter).
        var world = new World { Now = DateTimeOffset.Parse("2026-10-14T02:30:00Z", System.Globalization.CultureInfo.InvariantCulture) };
        world.EasternUserOf(BobContact);
        var assigned = world.Matter(readers: [Alice], assignedToInternal: BobContact);
        var unassigned = world.Matter(readers: [Alice]);
        var resolved = DateTimeOffset.Parse("2026-10-13T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var eastern = world.Signal(Decide, core: (MatterType, assigned), sets: ["closedToday", "closedEarlier"], resolvedOn: resolved);
        var utc = world.Signal(Decide, core: (MatterType, unassigned), sets: ["closedToday", "closedEarlier"], resolvedOn: resolved);

        Ids(await OkAsync(world, Alice, "?set=closedToday")).Should().Equal([eastern]);
        Ids(await OkAsync(world, Alice, "?set=closedEarlier")).Should().Equal([utc], "no assignee: the UTC date decides");
    }

    [Fact]
    public async Task DoRow_OnACoreTheCallerCannotRead_IsAbsentEvenWhenTheSubjectIsTheirOwn()
    {
        // F2 (review): isolates the core check for Do rows — the subject (Alice's own event) is readable and hers.
        var world = new World();
        var hidden = world.Matter(readers: [Bob]);
        var mine = world.Event(owner: Alice);
        world.Signal(Do, core: (MatterType, hidden), subject: (EventType, mine));
        AllItems(await OkAsync(world, Alice)).Should().BeEmpty("the core record (a matter Alice cannot open) decides, not the subject");
    }

    [Fact]
    public async Task ContactThatCannotBeRead_Is503_NeverADoLaneWithoutTheAssignedWork()
    {
        // F1 (review): team-owned work matches only on the assignee column, so a contact fault would otherwise hide it.
        foreach (var world in new[] { new World { ContactUnreadable = true }, new World { ContactThrows = true } })
        {
            var m = world.Matter(readers: [Alice]);
            world.Signal(Do, core: (MatterType, m), subject: (EventType, world.Event(owner: Team, assignedTo: AliceContact)));
            var response = await GetAsync(world, Alice);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await Problem(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.signal.worklist_unavailable");
        }
    }

    [Fact]
    public async Task ASignalPageRefusedAfterTheFirst_Is503_NotTheFirstPageAsTheList()
    {
        var world = new World { PageSize = 1, DenySignalPagesFrom = 2 };
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m));
        world.Signal(Decide, core: (MatterType, m));
        (await GetAsync(world, Alice)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The witness rule at read time (D-123; task 145 review F1, option B)
    // ---------------------------------------------------------------------------------------------------------------

    private const string WitnessSubject = "Rate increase from Firm LLP";
    private const string WitnessSender = "partner@firm.example";

    private static string EvidenceQuoting(params Guid[] communications) => JsonSerializer.Serialize(communications.Select((id, i) => new
    {
        kind = "communication",
        @ref = $"sprk_communication:{id:D}",
        tier = "Fact",
        text = WitnessSubject,
        source = WitnessSender,
        asOf = "2026-10-05T09:15:00Z",
        clause = $"all[{i}]",
    }));

    /// <summary>A line with no subject or sender that carries <c>"withheld": true</c>.</summary>
    private static bool IsWithheldLine(JsonElement line) =>
        !line.TryGetProperty("text", out _) && !line.TryGetProperty("source", out _)
        && line.TryGetProperty("withheld", out var withheld) && withheld.ValueKind == JsonValueKind.True;

    private static List<JsonElement> EvidenceOf(JsonElement body, Guid signalId) =>
        JsonSerializer.Deserialize<JsonElement>(AllItems(body).Single(i => i.GetProperty("signalId").GetGuid() == signalId)
            .GetProperty("evidenceRefs").GetString()!).EnumerateArray().ToList();

    [Fact]
    public async Task AWitnessReFiledUnderASecureFamily_IsShownWithoutSubjectOrSender_AndMarkedWithheld()
    {
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var secureProject = Guid.NewGuid();
        world.AppRoot("sprk_project", secureProject, secure: true);
        // Filed to M when the Signal was written; since re-filed under secure project X (and re-owned by the Secure team).
        var refiled = world.AppCommunication(secureOwned: true,
            ("sprk_regardingmatter", "sprk_matter", m), ("sprk_regardingproject", "sprk_project", secureProject));
        var ordinary = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(refiled, ordinary));
        world.AppSignal(s, m, secureOwned: false);

        var lines = EvidenceOf(await OkAsync(world, Alice), s);

        lines[0].TryGetProperty("text", out _).Should().BeFalse("the witness is in project X's family, not M's");
        lines[0].TryGetProperty("source", out _).Should().BeFalse();
        lines[0].GetProperty("withheld").GetBoolean().Should().BeTrue("so the UI says withheld, not 'no value recorded'");
        lines[0].GetProperty("ref").GetString().Should().Be($"sprk_communication:{refiled:D}");
        lines[0].GetProperty("asOf").GetString().Should().Be("2026-10-05T09:15:00Z");
        lines[0].GetProperty("clause").GetString().Should().Be("all[0]");
        lines[1].GetProperty("text").GetString().Should().Be(WitnessSubject, "an ordinary witness of an ordinary Signal keeps its detail");
        lines[1].GetProperty("source").GetString().Should().Be(WitnessSender);
        lines[1].TryGetProperty("withheld", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AWitnessInTheSignalsOwnSecureFamily_KeepsItsDetail()
    {
        var world = new World { SecureTeamExists = true };
        var secureMatter = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", secureMatter, secure: true);
        var own = world.AppCommunication(secureOwned: true, ("sprk_regardingmatter", "sprk_matter", secureMatter));
        var s = world.Signal(Decide, core: (MatterType, secureMatter), evidence: EvidenceQuoting(own));
        world.AppSignal(s, secureMatter, secureOwned: true);

        var line = EvidenceOf(await OkAsync(world, Alice), s).Single();
        line.GetProperty("text").GetString().Should().Be(WitnessSubject);
        line.GetProperty("source").GetString().Should().Be(WitnessSender);
        line.TryGetProperty("withheld", out _).Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheFamiliesCannotBeRead_EveryDetailIsWithheld_AndTheListIsUnchanged()
    {
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var ordinary = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(ordinary));
        // No app-only Signal row: its family cannot be placed, so nothing is shown (fail closed), and the item is still listed.

        var body = await OkAsync(world, Alice);
        Ids(body).Should().Equal(s);
        var line = EvidenceOf(body, s).Single();
        line.TryGetProperty("text", out _).Should().BeFalse();
        line.GetProperty("withheld").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task EvidenceThatIsNotAJsonArray_IsNotReturned()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: "{\"text\":\"" + WitnessSubject + "\"}");
        AllItems(await OkAsync(world, Alice)).Single().GetProperty("evidenceRefs").ValueKind.Should().Be(JsonValueKind.Null);
        _ = s;
    }

    [Fact]
    public async Task AWitnessFiledUnderSecureXButNotYetReOwned_IsWithheld()
    {
        // The re-own window (task 145's case; 038 pass 2 F1): the witness is filed under secure project X but still owned by an
        // ordinary team. Its family is X's (from its roots), so the ordinary matter's Signal does not show its detail.
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var secureProject = Guid.NewGuid();
        world.AppRoot("sprk_project", secureProject, secure: true);
        var windowed = world.AppCommunication(secureOwned: false,
            ("sprk_regardingmatter", "sprk_matter", m), ("sprk_regardingproject", "sprk_project", secureProject));
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(windowed));
        world.AppSignal(s, m, secureOwned: false);

        var line = EvidenceOf(await OkAsync(world, Alice), s).Single();
        line.TryGetProperty("text", out _).Should().BeFalse();
        line.GetProperty("withheld").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WhenTheAppOnlyFamilyReadFaults_EveryDetailIsWithheld_AndTheListIsUnchanged()
    {
        // 038 pass 2 F2: a FAULT (not a missing row) in the placement. Fail closed on the detail; the list is the same list.
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var ordinary = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(ordinary));
        var plain = world.Signal(Decide, core: (MatterType, m));
        world.AppSignal(s, m, secureOwned: false);
        world.FaultAppOnlyTables.Add("sprk_signal");

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([s, plain]);
        var line = EvidenceOf(body, s).Single();
        line.TryGetProperty("text", out _).Should().BeFalse();
        line.TryGetProperty("source", out _).Should().BeFalse();
        line.GetProperty("withheld").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DetailInAnyCasing_IsJudged_AndCollidingNamesWithholdThatSignalsEvidenceOnly()
    {
        // 038 pass 2 K1: "Text"/"SOURCE" are detail too; a line with both "text" and "Text" cannot be judged, so that Signal's
        // evidence is withheld (null) and the request is still 200.
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var secureProject = Guid.NewGuid();
        world.AppRoot("sprk_project", secureProject, secure: true);
        var refiled = world.AppCommunication(secureOwned: true,
            ("sprk_regardingmatter", "sprk_matter", m), ("sprk_regardingproject", "sprk_project", secureProject));
        var cased = world.Signal(Decide, core: (MatterType, m), evidence:
            $"[{{\"ref\":\"sprk_communication:{refiled:D}\",\"Text\":\"{WitnessSubject}\",\"SOURCE\":\"{WitnessSender}\",\"asOf\":\"2026-10-05T09:15:00Z\"}}]");
        world.AppSignal(cased, m, secureOwned: false);
        var colliding = world.Signal(Decide, core: (MatterType, m), evidence:
            $"[{{\"ref\":\"sprk_communication:{refiled:D}\",\"text\":\"a\",\"Text\":\"{WitnessSubject}\"}}]");
        world.AppSignal(colliding, m, secureOwned: false);

        var body = await OkAsync(world, Alice);
        var raw = AllItems(body).Single(i => i.GetProperty("signalId").GetGuid() == cased).GetProperty("evidenceRefs").GetString()!;
        raw.Should().NotContain(WitnessSubject).And.NotContain(WitnessSender);
        EvidenceOf(body, cased).Single().GetProperty("withheld").GetBoolean().Should().BeTrue();
        AllItems(body).Single(i => i.GetProperty("signalId").GetGuid() == colliding).GetProperty("evidenceRefs").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D-142 (task 151): a witness's detail is shown only to a caller whose own read returns the witness
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task D142_AWitnessTheCallerReads_OutsideAnySecureFamily_KeepsItsDetail()
    {
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var witness = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(witness, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(witness));
        world.AppSignal(s, m, secureOwned: false);

        var line = EvidenceOf(await OkAsync(world, Alice), s).Single();
        line.GetProperty("text").GetString().Should().Be(WitnessSubject);
        line.GetProperty("source").GetString().Should().Be(WitnessSender);
        line.TryGetProperty("withheld", out _).Should().BeFalse();
    }

    [Fact]
    public async Task D142_AWitnessReFiledToAnotherBusinessUnitsOrdinaryMatter_IsWithheld_FromACallerWhoCannotReadIt()
    {
        // Both families are ordinary, so the secure wall (D-123) keeps the detail; only the caller's own read withholds it.
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var otherBuMatter = world.Matter(readers: [OtherBuUser]);
        world.AppRoot("sprk_matter", otherBuMatter, secure: false);
        var refiled = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", otherBuMatter));
        world.OnlyReadableBy(refiled, OtherBuUser);
        var readable = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(readable, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(refiled, readable));
        world.AppSignal(s, m, secureOwned: false);

        var body = await OkAsync(world, Alice);
        Ids(body).Should().Equal(s);
        var lines = EvidenceOf(body, s);
        lines[0].TryGetProperty("text", out _).Should().BeFalse("Alice cannot read the communication in the other business unit");
        lines[0].TryGetProperty("source", out _).Should().BeFalse();
        lines[0].GetProperty("withheld").GetBoolean().Should().BeTrue();
        lines[0].GetProperty("ref").GetString().Should().Be($"sprk_communication:{refiled:D}");
        lines[0].GetProperty("clause").GetString().Should().Be("all[0]");
        lines[1].GetProperty("text").GetString().Should().Be(WitnessSubject, "a witness Alice reads keeps its detail on the same Signal");
        lines[1].TryGetProperty("withheld", out _).Should().BeFalse();
    }

    [Fact]
    public async Task D142_AnUnfiledPrivatelyOwnedWitness_IsWithheld_FromACallerWhoCannotReadIt()
    {
        // No Secure team in this environment: every family is ordinary, so only the caller's own read decides.
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        var unfiled = world.AppCommunication(secureOwned: false);
        world.OwnedByUser(unfiled, Bob);
        world.OnlyReadableBy(unfiled, Bob);
        world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(unfiled));

        var raw = AllItems(await OkAsync(world, Alice)).Single().GetProperty("evidenceRefs").GetString()!;
        raw.Should().NotContain(WitnessSubject).And.NotContain(WitnessSender);
        JsonSerializer.Deserialize<JsonElement>(raw).EnumerateArray().Single().GetProperty("withheld").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task D142_ASecureFamilyWitnessTheCallerReads_OnAnOrdinarySignal_IsStillWithheld()
    {
        var world = new World { SecureTeamExists = true };
        var m = world.Matter(readers: [Alice]);
        world.AppRoot("sprk_matter", m, secure: false);
        var secureProject = Guid.NewGuid();
        world.AppRoot("sprk_project", secureProject, secure: true);
        var secure = world.AppCommunication(secureOwned: true, ("sprk_regardingproject", "sprk_project", secureProject));
        world.OnlyReadableBy(secure, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(secure));
        world.AppSignal(s, m, secureOwned: false);

        var line = EvidenceOf(await OkAsync(world, Alice), s).Single();
        world.Requests.Should().Contain(r => r.StartsWith("sprk_communications?", StringComparison.Ordinal), "the caller's read ran and returned it");
        line.TryGetProperty("text", out _).Should().BeFalse("the secure wall (D-123) still applies to a witness the caller reads");
        line.GetProperty("withheld").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task D142_WhenTheCallersWitnessReadFaults_EveryDetailIsWithheld_AndTheListIsUnchanged()
    {
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        var witness = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(witness, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(witness, witness));
        var plain = world.Signal(Decide, core: (MatterType, m));
        // A witness in another table whose read succeeds: still withheld, because a fault withholds every detail on the page.
        var document = world.Record("sprk_document", readers: [Alice]);
        var onDocument = world.Signal(Decide, core: (MatterType, m),
            evidence: JsonSerializer.Serialize(new[] { new { @ref = $"sprk_document:{document:D}", text = "Engagement letter" } }));
        world.FaultingTables.Add("sprk_communication");
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([s, plain, onDocument]);
        EvidenceOf(body, s).Should().HaveCount(2).And.OnlyContain(l => IsWithheldLine(l));
        EvidenceOf(body, onDocument).Should().ContainSingle().Which.GetProperty("withheld").GetBoolean().Should().BeTrue();
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the caller's read of sprk_communication witnesses failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D142_WhenTheCallerHasNoReadOnTheWitnessTable_ThoseLinesAreWithheld_AndNothingIsLoggedAsAFault()
    {
        // A 403 on the witness table is a verdict ("none of these is readable"), not a fault: only that table's lines lose their
        // detail, the other table's lines keep theirs, the list is unchanged, and no WitnessReadFailed warning is logged.
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        var witness = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(witness, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(witness));
        var plain = world.Signal(Decide, core: (MatterType, m));
        var document = world.Record("sprk_document", readers: [Alice]);
        var onDocument = world.Signal(Decide, core: (MatterType, m),
            evidence: JsonSerializer.Serialize(new[] { new { @ref = $"sprk_document:{document:D}", text = "Engagement letter" } }));
        world.TableDenied[Alice] = ["sprk_communication"];
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([s, plain, onDocument]);
        var line = EvidenceOf(body, s).Single();
        line.TryGetProperty("text", out _).Should().BeFalse();
        line.TryGetProperty("source", out _).Should().BeFalse();
        line.GetProperty("withheld").GetBoolean().Should().BeTrue();
        EvidenceOf(body, onDocument).Single().GetProperty("text").GetString().Should().Be("Engagement letter");
        _factory.Logs.Warnings.Should().NotContain(w => w.Contains("witnesses", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D142_WhenTheCallersWitnessReadThrows_EveryDetailOnThePageIsWithheld_AndTheListIsUnchanged()
    {
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        var witness = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(witness, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(witness));
        var plain = world.Signal(Decide, core: (MatterType, m));
        var document = world.Record("sprk_document", readers: [Alice]);
        var onDocument = world.Signal(Decide, core: (MatterType, m),
            evidence: JsonSerializer.Serialize(new[] { new { @ref = $"sprk_document:{document:D}", text = "Engagement letter" } }));
        world.ThrowingTables.Add("sprk_document");
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([s, plain, onDocument]);
        EvidenceOf(body, s).Should().ContainSingle().Which.GetProperty("withheld").GetBoolean().Should().BeTrue();
        EvidenceOf(body, onDocument).Should().ContainSingle().Which.GetProperty("withheld").GetBoolean().Should().BeTrue();
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the caller's read of sprk_document witnesses faulted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D142_AWitnessRead401_IsAFault_NotNoReadOnTheTable_SoEveryDetailOnThePageIsWithheld()
    {
        // DataverseUserClient maps 401 and 403 to the same AccessDenied code; a 401 mid-request is the token failing, so the
        // witness read treats it as a fault (every detail withheld), where a 403 withholds only that table's lines.
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        var witness = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
        world.OnlyReadableBy(witness, Alice);
        var s = world.Signal(Decide, core: (MatterType, m), evidence: EvidenceQuoting(witness));
        var document = world.Record("sprk_document", readers: [Alice]);
        var onDocument = world.Signal(Decide, core: (MatterType, m),
            evidence: JsonSerializer.Serialize(new[] { new { @ref = $"sprk_document:{document:D}", text = "Engagement letter" } }));
        world.UnauthorizedTables.Add("sprk_communication");
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([s, onDocument]);
        EvidenceOf(body, s).Should().ContainSingle().Which.GetProperty("withheld").GetBoolean().Should().BeTrue();
        EvidenceOf(body, onDocument).Should().ContainSingle().Which.GetProperty("withheld").GetBoolean().Should().BeTrue();
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the caller's read of sprk_communication witnesses failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D142_OneCallerReadPerWitnessTablePerRequest_InListsOfAtMost200Ids()
    {
        var world = new World { SecureTeamExists = false };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < 24; i++)
        {
            var communication = world.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m));
            var document = world.Record("sprk_document", readers: [Alice]);
            var evidence = JsonSerializer.Serialize(new object[]
            {
                new { @ref = $"sprk_communication:{communication:D}", text = WitnessSubject, source = WitnessSender },
                new { @ref = $"sprk_document:{document:D}", text = "Engagement letter" },
            });
            world.Signal(Decide, core: (MatterType, m), evidence: evidence);
        }

        var body = await OkAsync(world, Alice);
        AllItems(body).Should().HaveCount(24);
        world.Requests.Count(r => r.StartsWith("sprk_communications?", StringComparison.Ordinal))
            .Should().Be(1, "one read per witness table, never one per witness");
        world.Requests.Count(r => r.StartsWith("sprk_documents?", StringComparison.Ordinal)).Should().Be(1);
        AllItems(body).SelectMany(i => JsonSerializer.Deserialize<JsonElement>(i.GetProperty("evidenceRefs").GetString()!).EnumerateArray())
            .Should().OnlyContain(l => !IsWithheldLine(l), "Alice reads every witness");

        // More than 200 witnesses in one table: In lists of at most 200 ids.
        var many = new World { SecureTeamExists = false };
        var m2 = many.Matter(readers: [Alice]);
        for (var i = 0; i < 201; i++)
        {
            many.Signal(Decide, core: (MatterType, m2),
                evidence: EvidenceQuoting(many.AppCommunication(secureOwned: false, ("sprk_regardingmatter", "sprk_matter", m2))));
        }

        AllItems(await OkAsync(many, Alice)).Should().HaveCount(201);
        var reads = many.Requests.Where(r => r.StartsWith("sprk_communications?", StringComparison.Ordinal)).ToList();
        reads.Should().HaveCount(2);
        reads.Select(r => Regex.Matches(r, "'[0-9a-f-]{36}'").Count).Should().BeEquivalentTo([200, 1]);

        // More than 200 distinct tracking numbers (across two vendor orgs): chunks of 200 and 1, never one read per org.
        var numbers = new World();
        var m3 = numbers.Matter(readers: [Alice]);
        for (var i = 0; i < 201; i++)
        {
            var org = i % 2 == 0 ? VendorOrg : OtherVendorOrg;
            var currentInOrg = numbers.Invoice([Alice], org);
            numbers.Submission([Alice], org, $"C-{i}", numbers.Invoice([Alice], org));
            BillingSignal(numbers, m3, (NotCoreType, currentInOrg), DuplicateEvidence(null, $"C-{i}"));
        }

        AllItems(await OkAsync(numbers, Alice)).Should().OnlyContain(i => i.GetProperty("sentence").GetString() == QuotedSentence);
        numbers.Requests.Count(r => r.StartsWith(SubmissionRead, StringComparison.Ordinal)).Should().Be(2, "201 numbers: chunks of 200 and 1");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D-176 (task 157): a billing Duplicate's earlier-invoice detail is shown only to a caller whose own read returns that invoice
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly Guid BillingEventType = Guid.Parse("11111111-0000-0000-0000-000000000009");
    private static readonly Guid VendorOrg = Guid.Parse("0f0f0f0f-0000-0000-0000-000000000001");
    private static readonly Guid OtherVendorOrg = Guid.Parse("0f0f0f0f-0000-0000-0000-000000000002");
    private const string EarlierNumber = "INV-77.1";
    private const string QuotedSentence = "Invoice INV-90 duplicates INV-77.1.";
    private const string DuplicateTitle = "Duplicate invoice";

    // The four D-176 reads, by path prefix: the earlier-invoice readability read, the current invoice's vendor org, the billing event's
    // invoice and the submission. The Decide-lane subject read of sprk_invoice (sprk_invoiceid,sprk_highpriority) matches none of them.
    private const string EarlierInvoiceRead = "sprk_invoices?$select=sprk_invoiceid&";
    private const string CurrentInvoiceRead = "sprk_invoices?$select=sprk_invoiceid,_sprk_vendororg_value&";
    private const string BillingEventRead = "sprk_billingevents?";
    private const string SubmissionRead = "sprk_invoicesubmissions?";

    /// <summary>A visible Duplicate's evidence as ebilling's mapper writes it (BillingFindingSignalMapper, work/ebilling-r1): the reason-set
    /// fingerprint (no detail), the reason with its rendered sentence, the current invoice's number, and the duplicateOf references.</summary>
    private static string DuplicateEvidence(Guid? earlierInvoice, string? trackingNumber = EarlierNumber, int? earlierLine = null)
    {
        var lines = BillingEvidence();
        if (trackingNumber is not null)
        {
            lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "duplicateOf.trackingNumber", ["tier"] = "Fact", ["text"] = trackingNumber });
        }

        if (earlierInvoice is { } id)
        {
            lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "duplicateOf.invoiceId", ["tier"] = "Fact", ["text"] = id.ToString("D") });
        }

        if (earlierLine is { } line)
        {
            lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "duplicateOf.lineIndex", ["tier"] = "Fact", ["text"] = line.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        return JsonSerializer.Serialize(lines);
    }

    private static List<Dictionary<string, object?>> BillingEvidence() =>
    [
        new() { ["kind"] = "billing.reasonset", ["ref"] = "duplicate_invoice_number|Flag", ["tier"] = "Fact" },
        new()
        {
            ["kind"] = "billing.reason", ["ref"] = "duplicate_invoice_number", ["tier"] = "Fact", ["text"] = QuotedSentence,
            ["source"] = "sprk_policyversion:7e57a5e5-0000-0000-0000-000000000001",
        },
        new()
        {
            ["kind"] = "billing.field", ["ref"] = "invoice.FirmInvoiceNumber", ["tier"] = "Observation", ["confidence"] = 0.98,
            ["text"] = "INV-90", ["source"] = "invoice.firmInvoiceNumber",
        },
    ];

    private static Guid BillingSignal(World world, Guid matter, (Guid Type, Guid Id) subject, string evidence, string sentence = QuotedSentence) =>
        world.Signal(Decide, core: (MatterType, matter), subject: (subject.Type, subject.Id), title: DuplicateTitle, evidence: evidence, sentence: sentence);

    private static JsonElement Item(JsonElement body, Guid signalId) =>
        AllItems(body).Single(i => i.GetProperty("signalId").GetGuid() == signalId);

    private static string? RefOf(JsonElement line) => line.TryGetProperty("ref", out var r) ? r.GetString() : null;

    /// <summary>This step left the row as (9) returned it: the stored sentence, every stored line (the duplicateOf references among
    /// them), and the detail-free fingerprint line not marked withheld.</summary>
    private static void ShouldKeepTheEarlierInvoice(JsonElement body, Guid signalId, int storedLines, string sentence = QuotedSentence)
    {
        Item(body, signalId).GetProperty("sentence").GetString().Should().Be(sentence);
        var lines = EvidenceOf(body, signalId);
        lines.Should().HaveCount(storedLines);
        lines.Select(RefOf).Should().Contain(r => r!.StartsWith("duplicateOf.", StringComparison.Ordinal));
        lines.Single(l => RefOf(l) == "duplicate_invoice_number|Flag").TryGetProperty("withheld", out _).Should().BeFalse();
    }

    /// <summary>ebilling's write-time withheld shape: no duplicateOf line, every other line withheld, the withheld sentence, the title kept.</summary>
    private static void ShouldWithholdTheEarlierInvoice(JsonElement body, Guid signalId, string quoted)
    {
        var item = Item(body, signalId);
        item.GetProperty("sentence").GetString().Should().Be(EarlierInvoiceReadability.WithheldSentence);
        item.GetProperty("title").GetString().Should().Be(DuplicateTitle);
        item.GetProperty("evidenceRefs").GetString().Should().NotContain(quoted);
        var lines = EvidenceOf(body, signalId);
        lines.Should().HaveCount(3).And.OnlyContain(l => IsWithheldLine(l));
        lines.Select(RefOf).Should().NotContain(r => r!.StartsWith("duplicateOf.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task D176_ADuplicateWhoseEarlierInvoiceTheCallerReads_KeepsItsSentenceAndEvidence()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var earlier = world.Invoice([Alice], VendorOrg);
        var s = BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(earlier));

        var body = await OkAsync(world, Alice);
        ShouldKeepTheEarlierInvoice(body, s, storedLines: 5);
        world.Requests.Count(r => r.StartsWith(EarlierInvoiceRead, StringComparison.Ordinal)).Should().Be(1);
        world.Requests.Should().NotContain(r => r.StartsWith(SubmissionRead, StringComparison.Ordinal), "an invoice id needs only the invoice read");
    }

    [Fact]
    public async Task D176_ADuplicateWhoseEarlierInvoiceTheCallerCannotRead_IsWithheld_InEbillingsWriteTimeShape()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var earlier = world.Invoice([Bob], VendorOrg);
        var s = BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(earlier));

        var body = await OkAsync(world, Alice);
        Ids(body).Should().Equal(s);
        ShouldWithholdTheEarlierInvoice(body, s, earlier.ToString("D"));
        Item(body, s).GetProperty("evidenceRefs").GetString().Should().NotContain(EarlierNumber);
    }

    [Fact]
    public async Task D176_ADuplicateWhoseEvidenceUsesOtherCasing_AndWhoseEarlierInvoiceIsUnreadable_IsWithheld()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var earlier = world.Invoice([Bob], VendorOrg);
        var lines = BillingEvidence();
        // One casing per object (a line with both "ref" and "Ref" cannot be judged); the kind, the ref and every property name differ in case.
        lines.Add(new() { ["Kind"] = "Billing.Reference", ["Ref"] = "DUPLICATEOF.INVOICEID", ["Tier"] = "Fact", ["Text"] = earlier.ToString("D") });
        var s = BillingSignal(world, m, (NotCoreType, current), JsonSerializer.Serialize(lines));

        var body = await OkAsync(world, Alice);
        Ids(body).Should().Equal(s);
        Item(body, s).GetProperty("sentence").GetString().Should().Be(EarlierInvoiceReadability.WithheldSentence);
        Item(body, s).GetProperty("evidenceRefs").GetString().Should().NotContain(earlier.ToString("D"));
        var kept = EvidenceOf(body, s);
        kept.Should().HaveCount(3).And.OnlyContain(l => IsWithheldLine(l));
        world.Requests.Count(r => r.StartsWith(EarlierInvoiceRead, StringComparison.Ordinal)).Should().Be(1, "the cased line is recognised as a Duplicate reference");
    }

    [Fact]
    public async Task D176_ADuplicateNamingTwoEarlierInvoices_OneReadableAndOneNot_IsWithheld()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var readable = world.Invoice([Alice], VendorOrg);
        var unreadable = world.Invoice([Bob], VendorOrg);
        var lines = BillingEvidence();
        lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "duplicateOf.invoiceId", ["tier"] = "Fact", ["text"] = readable.ToString("D") });
        lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "duplicateOf.invoiceId", ["tier"] = "Fact", ["text"] = unreadable.ToString("D") });
        var s = BillingSignal(world, m, (NotCoreType, current), JsonSerializer.Serialize(lines));

        var body = await OkAsync(world, Alice);
        Ids(body).Should().Equal(s);
        ShouldWithholdTheEarlierInvoice(body, s, unreadable.ToString("D"));
        Item(body, s).GetProperty("evidenceRefs").GetString().Should().NotContain(readable.ToString("D"));
    }

    [Fact]
    public async Task D176_ATrackingNumberOnly_IsResolvedThroughTheOneSubmissionOfTheCurrentInvoicesVendorOrg()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var readable = world.Invoice([Alice], VendorOrg);
        var unreadable = world.Invoice([Bob], VendorOrg);
        world.Submission([Alice], VendorOrg, "N-1", readable);
        world.Submission([Alice], VendorOrg, "N-2", unreadable);
        world.Submission([Alice], VendorOrg, "N-4", readable);
        world.Submission([Alice], VendorOrg, "N-4", readable);
        world.Submission([Bob], VendorOrg, "N-5", readable);
        var signal = new[] { "N-1", "N-2", "N-3", "N-4", "N-5" }
            .ToDictionary(n => n, n => BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(null, n)));
        // Any other subject type with only a tracking number has no current invoice to scope the number by (POML D-176 resolution).
        var onMatter = BillingSignal(world, m, (MatterType, m), DuplicateEvidence(null, "N-1"));

        var body = await OkAsync(world, Alice);
        ShouldKeepTheEarlierInvoice(body, signal["N-1"], storedLines: 4);
        ShouldWithholdTheEarlierInvoice(body, signal["N-2"], "N-2"); // resolves to an invoice Alice cannot read
        ShouldWithholdTheEarlierInvoice(body, signal["N-3"], "N-3"); // no submission
        ShouldWithholdTheEarlierInvoice(body, signal["N-4"], "N-4"); // two submissions: ambiguous
        ShouldWithholdTheEarlierInvoice(body, signal["N-5"], "N-5"); // a submission Alice cannot read
        ShouldWithholdTheEarlierInvoice(body, onMatter, "N-1");
        world.Requests.Count(r => r.StartsWith(SubmissionRead, StringComparison.Ordinal)).Should().Be(1, "one submission read per vendor org");
    }

    [Theory]
    [InlineData("500")]
    [InlineData("401")]
    [InlineData("unparseable")]
    [InlineData("throw")]
    public async Task D176_WhenTheEarlierInvoiceReadFaults_EveryDuplicateOnThePageIsWithheld_AndNoOtherRowChanges(string fault)
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var earlier = world.Invoice([Alice], VendorOrg);
        world.Submission([Alice], VendorOrg, EarlierNumber, earlier);
        var byId = BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(earlier));
        var byNumber = BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(null));
        var notDuplicate = BillingSignal(world, m, (NotCoreType, current), JsonSerializer.Serialize(BillingEvidence()));
        var plain = world.Signal(Decide, core: (MatterType, m), sentence: "The budget is over by 10%.");
        world.PathFaults[EarlierInvoiceRead] = fault;
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        Ids(body).Should().BeEquivalentTo([byId, byNumber, notDuplicate, plain]);
        ShouldWithholdTheEarlierInvoice(body, byId, earlier.ToString("D"));
        ShouldWithholdTheEarlierInvoice(body, byNumber, EarlierNumber);
        Item(body, notDuplicate).GetProperty("sentence").GetString().Should().Be(QuotedSentence);
        EvidenceOf(body, notDuplicate).Should().HaveCount(3);
        Item(body, plain).GetProperty("sentence").GetString().Should().Be("The budget is over by 10%.");
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the caller's read of sprk_invoice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D176_WhenTheCallerHasNoReadOnTheInvoiceTable_TheDuplicateIsWithheld_AndNothingIsLoggedAsAFault()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var earlier = world.Invoice([Alice], VendorOrg);
        var s = BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(earlier));
        world.TableDenied[Alice] = ["sprk_invoice"];
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);
        ShouldWithholdTheEarlierInvoice(body, s, earlier.ToString("D"));
        _factory.Logs.Warnings.Should().NotContain(w => w.Contains("Duplicate earlier-invoice check", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D176_ARowThatIsNotADuplicate_IsUnchanged_AndCausesNoInvoiceSubmissionOrBillingEventRead()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        // A billing row with only billing.field / billing.reason lines, and a billing.reference that is not a duplicateOf reference.
        var lines = BillingEvidence();
        lines.Add(new() { ["kind"] = "billing.reference", ["ref"] = "rateCard.id", ["tier"] = "Fact", ["text"] = Guid.NewGuid().ToString("D") });
        var billing = BillingSignal(world, m, (NotCoreType, current), JsonSerializer.Serialize(lines));
        var plain = world.Signal(Decide, core: (MatterType, m), sentence: "The budget is over by 10%.");

        var body = await OkAsync(world, Alice);
        Item(body, billing).GetProperty("sentence").GetString().Should().Be(QuotedSentence);
        EvidenceOf(body, billing).Should().HaveCount(4);
        EvidenceOf(body, billing).Single(l => RefOf(l) == "duplicate_invoice_number|Flag").TryGetProperty("withheld", out _).Should().BeFalse();
        Item(body, plain).GetProperty("sentence").GetString().Should().Be("The budget is over by 10%.");
        world.Requests.Should().NotContain(r => r.StartsWith(EarlierInvoiceRead, StringComparison.Ordinal)
            || r.StartsWith(CurrentInvoiceRead, StringComparison.Ordinal)
            || r.StartsWith(SubmissionRead, StringComparison.Ordinal)
            || r.StartsWith(BillingEventRead, StringComparison.Ordinal));
    }

    [Fact]
    public async Task D176_ALineLevelRepeatOfAnEarlierLine_IsKeptWhenTheCallerReadsItsEventAndInvoice_AndWithheldWhenNotTheEvent()
    {
        // DuplicateOfEarlierLine: the tracking number is the line's OWN invoice's, with the earlier line's index and no invoice id.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        world.Submission([Alice], VendorOrg, "INV-90.1", current);
        var readableEvent = world.BillingEvent([Alice], current);
        var unreadableEvent = world.BillingEvent([Bob], current);
        var kept = BillingSignal(world, m, (BillingEventType, readableEvent), DuplicateEvidence(null, "INV-90.1", earlierLine: 2));
        var withheld = BillingSignal(world, m, (BillingEventType, unreadableEvent), DuplicateEvidence(null, "INV-90.1", earlierLine: 2));

        var body = await OkAsync(world, Alice);
        ShouldKeepTheEarlierInvoice(body, kept, storedLines: 5);
        ShouldWithholdTheEarlierInvoice(body, withheld, "INV-90.1");
        world.Requests.Count(r => r.StartsWith(BillingEventRead, StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task D176_ALineLevelRepeatOfAPriorSubmissionLine_WithAnInvoiceId_IsJudgedByThatInvoice()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var current = world.Invoice([Alice], VendorOrg);
        var lineEvent = world.BillingEvent([Alice], current);
        var readable = world.Invoice([Alice], VendorOrg);
        var unreadable = world.Invoice([Bob], VendorOrg);
        var kept = BillingSignal(world, m, (BillingEventType, lineEvent), DuplicateEvidence(readable));
        var withheld = BillingSignal(world, m, (BillingEventType, lineEvent), DuplicateEvidence(unreadable));

        var body = await OkAsync(world, Alice);
        ShouldKeepTheEarlierInvoice(body, kept, storedLines: 5);
        ShouldWithholdTheEarlierInvoice(body, withheld, unreadable.ToString("D"));
        world.Requests.Should().NotContain(r => r.StartsWith(BillingEventRead, StringComparison.Ordinal) || r.StartsWith(SubmissionRead, StringComparison.Ordinal),
            "an invoice id needs neither the event nor the submission");
    }

    [Fact]
    public async Task D176_ReadsAreBatched_OneOfEachPerRequest_AndOneSubmissionReadPerChunkOf200Numbers_AcrossVendorOrgs()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var signals = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            var current = world.Invoice([Alice], VendorOrg);
            signals.Add(BillingSignal(world, m, (NotCoreType, current), DuplicateEvidence(world.Invoice([Alice], VendorOrg))));

            var org = i % 2 == 0 ? VendorOrg : OtherVendorOrg;
            var currentInOrg = world.Invoice([Alice], org);
            world.Submission([Alice], org, $"A-{i}", world.Invoice([Alice], org));
            signals.Add(BillingSignal(world, m, (NotCoreType, currentInOrg), DuplicateEvidence(null, $"A-{i}")));

            var lineInvoice = world.Invoice([Alice], VendorOrg);
            world.Submission([Alice], VendorOrg, $"B-{i}", world.Invoice([Alice], VendorOrg));
            signals.Add(BillingSignal(world, m, (BillingEventType, world.BillingEvent([Alice], lineInvoice)), DuplicateEvidence(null, $"B-{i}")));
        }

        var body = await OkAsync(world, Alice);
        foreach (var s in signals)
        {
            Item(body, s).GetProperty("sentence").GetString().Should().Be(QuotedSentence, "Alice reads every earlier invoice");
        }

        world.Requests.Count(r => r.StartsWith(BillingEventRead, StringComparison.Ordinal)).Should().Be(1);
        world.Requests.Count(r => r.StartsWith(CurrentInvoiceRead, StringComparison.Ordinal)).Should().Be(1);
        world.Requests.Count(r => r.StartsWith(SubmissionRead, StringComparison.Ordinal)).Should().Be(1, "two vendor orgs, one submission read");
        world.Requests.Count(r => r.StartsWith(EarlierInvoiceRead, StringComparison.Ordinal)).Should().Be(1);

        // More than 200 earlier invoices: In lists of at most 200 ids.
        var many = new World();
        var m2 = many.Matter(readers: [Alice]);
        var invoice = many.Invoice([Alice], VendorOrg);
        for (var i = 0; i < 201; i++)
        {
            BillingSignal(many, m2, (NotCoreType, invoice), DuplicateEvidence(many.Invoice([Alice], VendorOrg)));
        }

        AllItems(await OkAsync(many, Alice)).Should().HaveCount(201);
        var reads = many.Requests.Where(r => r.StartsWith(EarlierInvoiceRead, StringComparison.Ordinal)).ToList();
        reads.Should().HaveCount(2);
        reads.Select(r => Regex.Matches(r, "'[0-9a-f-]{36}'").Count).Should().BeEquivalentTo([200, 1]);
    }

    [Fact]
    public async Task ClosedSets_AnAssigneeDayThatCannotBeRead_FallsBackToUtc_WithAWarning()
    {
        // Outside-diff finding (038 pass 2): DataverseRecipientDays reports a read fault in RecipientDay.Error; the route logs it.
        var world = new World { Now = DateTimeOffset.Parse("2026-10-14T02:30:00Z", System.Globalization.CultureInfo.InvariantCulture) };
        world.EasternUserOf(AliceContact);
        world.FaultAppOnlyTables.Add("systemuser");
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Do, core: (MatterType, m), subject: (EventType, world.Event(owner: Team, assignedTo: AliceContact)),
            sets: ["closedToday", "closedEarlier"], resolvedOn: DateTimeOffset.Parse("2026-10-13T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        _factory.Logs.Warnings.Clear();

        Ids(await OkAsync(world, Alice, "?set=closedEarlier")).Should().Equal([s], "the UTC date decides when the zone cannot be read");
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("the assignee's day could not be read", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Configuration, paging
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheRepoWorklistRow_ParsesForEverySet_AndANonSignalQueryIsRefused()
    {
        var json = File.ReadAllText(WorklistFactory.ConfigPath);
        foreach (var set in Enum.GetValues<WorklistSet>())
        {
            SignalReadService.ParseFetches(json, set).Should().NotBeNullOrEmpty();
        }

        SignalReadService.ParseFetches(json, WorklistSet.Lanes).Should().HaveCount(2);
        SignalReadService.ParseFetches(json.Replace("<entity name='sprk_signal'>", "<entity name='sprk_matter'>"), WorklistSet.Lanes)
            .Should().BeNull("a configuration that queries another table is refused, never executed");
        SignalReadService.ParseFetches(json.Replace("\"kind\": \"worklist\"", "\"kind\": \"grid\""), WorklistSet.Lanes).Should().BeNull();
    }

    [Fact]
    public async Task MoreThanOnePage_IsReadWithThePagingCookie()
    {
        var world = new World { PageSize = 2 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < 5; i++)
        {
            world.Signal(Decide, core: (MatterType, m));
        }

        AllItems(await OkAsync(world, Alice)).Should().HaveCount(5);
        world.Requests.Count(r => r.StartsWith("sprk_signals?", StringComparison.Ordinal)
                && Uri.UnescapeDataString(r).Contains("paging-cookie=", StringComparison.Ordinal))
            .Should().Be(2, "pages 2 and 3 of the Decide query are asked for with the cookie the previous page returned");
    }

    [Fact]
    public async Task TooManyPages_Is503_NotATruncatedRank()
    {
        var world = new World { PageSize = 1 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < SignalReadService.MaxPages + 1; i++)
        {
            world.Signal(Decide, core: (MatterType, m));
        }

        (await GetAsync(world, Alice)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }


    [Fact]
    public void HighPriorityTables_CarryTheColumnInTheSolutionSource()
    {
        // ADR-038 project rule: a column list asserted in code is paired with the real schema (the solution source).
        var entities = Path.Combine(WorklistFactory.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        foreach (var table in SignalReadService.HighPriorityTables)
        {
            var folder = Directory.GetDirectories(entities).Single(d => Path.GetFileName(d).Equals(table, StringComparison.OrdinalIgnoreCase));
            File.ReadAllText(Path.Combine(folder, "Entity.xml")).Should().MatchRegex("(?i)PhysicalName=\"sprk_highpriority\"", table);
        }

        foreach (var rule in SignalReadService.DoLaneMineRules.Values)
        {
            SignalReadService.HighPriorityTables.Should().Contain(rule.Table);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Decision Records on closed items and the Decided set (task 045, D-47; #1959 item 2)
    // ---------------------------------------------------------------------------------------------------------------

    private const string StepsEnvelope =
        "{\"reviewId\":\"b3ef2ba5-a61f-45ac-a474-664b6dc08720\",\"resolvedSignalIds\":[],\"steps\":["
        + "{\"code\":\"reassign\",\"label\":\"Reassign\",\"taken\":true,\"values\":{\"person\":\"p-1\",\"note\":\"over to Sam\"},\"outcome\":\"Done\",\"written\":[]},"
        + "{\"code\":\"extend\",\"label\":\"Extend\",\"taken\":false,\"values\":null,\"outcome\":\"Skipped\",\"written\":[]}]}";

    [Fact]
    public async Task ClosedItem_CarriesTheDecisionRecord_ReadByTheCaller()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var record = world.Decision(readers: [Alice], number: "DR-00042", recordClass: 100000000, outcome: 100000000,
            decidedOn: "2026-10-12T09:30:00Z", by: "Dana Decider", reason: "Agreed with the firm", steps: StepsEnvelope,
            followOns: "[{\"actionCode\":\"notify\",\"entity\":\"sprk_communication\",\"id\":\"11111111-2222-3333-4444-555555555555\"}]");
        var s = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49), decisionRecord: record);

        var item = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single();

        item.GetProperty("signalId").GetGuid().Should().Be(s);
        var decision = item.GetProperty("decision");
        decision.GetProperty("recordId").GetGuid().Should().Be(record);
        decision.GetProperty("number").GetString().Should().Be("DR-00042");
        decision.GetProperty("recordClass").GetString().Should().Be("Judgement");
        decision.GetProperty("outcome").GetString().Should().Be("Authorized");
        decision.GetProperty("decidedOn").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-10-12T09:30:00Z", System.Globalization.CultureInfo.InvariantCulture));
        decision.GetProperty("decidedByName").GetString().Should().Be("Dana Decider", "from the record's own sprk_confirmedby, never the owner (D-129)");
        decision.GetProperty("reason").GetString().Should().Be("Agreed with the firm");
        var steps = decision.GetProperty("steps").EnumerateArray().ToList();
        steps.Select(x => x.GetProperty("code").GetString()).Should().Equal("reassign", "extend");
        steps[0].GetProperty("taken").GetBoolean().Should().BeTrue();
        steps[0].GetProperty("values").GetProperty("note").GetString().Should().Be("over to Sam");
        steps[1].GetProperty("taken").GetBoolean().Should().BeFalse();
        steps[1].GetProperty("values").ValueKind.Should().Be(JsonValueKind.Null, "a skipped step carries no values");
        decision.GetProperty("followOns").EnumerateArray().Single().GetProperty("code").GetString().Should().Be("notify");
        decision.TryGetProperty("factSnapshot", out _).Should().BeFalse("the fact snapshot holds witness values and is not returned");
    }

    [Fact]
    public async Task ClosedItem_WhoseDecisionRecordTheCallerCannotRead_StaysListed_WithNoDecision()
    {
        // A secure-child (D-33/D-123) or owner-only (D-35/D-39) record: Dataverse's own read does not return it to this caller.
        var world = new World();
        var m = world.Matter(readers: [Alice, Bob]);
        var secureRecord = world.Decision(readers: [Bob], number: "DR-00007");
        world.Signal(Decide, core: (MatterType, m), sets: ["closedToday"], resolvedOn: world.Now.AddSeconds(-1), decisionRecord: secureRecord);

        var alice = AllItems(await OkAsync(world, Alice, "?set=closedToday")).Single();
        alice.TryGetProperty("decision", out var aliceDecision).Should().BeTrue();
        aliceDecision.ValueKind.Should().Be(JsonValueKind.Null, "omitted, not withheld by name");
        alice.ToString().Should().NotContain("DR-00007");

        AllItems(await OkAsync(world, Bob, "?set=closedToday")).Single().GetProperty("decision").GetProperty("number").GetString()
            .Should().Be("DR-00007");
    }

    [Fact]
    public async Task ClosedItem_ClosedBySystem_HasNoDecision_AndNoRecordIsRequested()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49));

        var item = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single();

        item.GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null);
        world.Requests.Should().NotContain(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenLanes_NeverReadDecisionRecords()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m));

        await OkAsync(world, Alice);

        world.Requests.Should().NotContain(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DecisionRecordReadFault_Is503_NotItemsWithoutTheirRecord()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var record = world.Decision(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49), decisionRecord: record);
        world.FaultingTables.Add("sprk_decisionrecord");

        var response = await GetAsync(world, Alice, "?set=closedEarlier");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CallerWithNoDecisionRecordRead_GetsClosedItemsWithoutDecisions_AndAnEmptyDecidedSet()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var record = world.Decision(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddHours(-49), decisionRecord: record);
        world.TableDenied[Alice] = ["sprk_decisionrecord"];

        AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single().GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null);
        AllItems(await OkAsync(world, Alice, "?set=decided")).Should().BeEmpty();
    }

    [Fact]
    public async Task DecidedSet_ListsOnlyItemsWhoseRecordAndCoreRecordTheCallerReads()
    {
        var world = new World();
        var readable = world.Matter(readers: [Alice]);
        var hiddenMatter = world.Matter(readers: [Bob]);
        var good = world.Decision(readers: [Alice], number: "DR-00001");
        var hiddenRecord = world.Decision(readers: [Bob], number: "DR-00002");
        var onHiddenMatter = world.Decision(readers: [Alice], number: "DR-00003");
        var listed = world.Signal(Decide, core: (MatterType, readable), sets: ["decided"], resolvedOn: world.Now.AddDays(-20), decisionRecord: good);
        world.Signal(Decide, core: (MatterType, readable), sets: ["decided"], resolvedOn: world.Now.AddDays(-20), decisionRecord: hiddenRecord);
        world.Signal(Decide, core: (MatterType, hiddenMatter), sets: ["decided"], resolvedOn: world.Now.AddDays(-20), decisionRecord: onHiddenMatter);
        world.Signal(Decide, core: (MatterType, readable), sets: ["decided"], resolvedOn: world.Now.AddDays(-20)); // a system closure: no record

        var body = await OkAsync(world, Alice, "?set=decided");

        body.GetProperty("set").GetString().Should().Be("decided");
        Ids(body).Should().Equal(listed);
        AllItems(body).Single().GetProperty("decision").GetProperty("number").GetString().Should().Be("DR-00001");
    }

    [Fact]
    public async Task DecidedSet_ReturnsEachRecordClass_AndNullForAnOptionValueItDoesNotKnow()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        foreach (var (cls, outcome) in new[] { (100000000, 100000000), (100000001, 100000000), (100000002, 100000002), (999, 999) })
        {
            world.Signal(Decide, core: (MatterType, m), sets: ["decided"], resolvedOn: world.Now.AddDays(-1),
                decisionRecord: world.Decision(readers: [Alice], recordClass: cls, outcome: outcome));
        }

        var classes = AllItems(await OkAsync(world, Alice, "?set=decided"))
            .Select(i => i.GetProperty("decision").GetProperty("recordClass").GetString()).ToList();

        classes.Should().BeEquivalentTo(new string?[] { "Judgement", "Routine", "Dismissal", null });
    }

    [Fact]
    public async Task UnreadableStepsEnvelope_GivesNullSteps_NotAnEmptyReplay()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49),
            decisionRecord: world.Decision(readers: [Alice], steps: "not json {", followOns: "also not json"));

        var decision = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single().GetProperty("decision");

        decision.GetProperty("steps").ValueKind.Should().Be(JsonValueKind.Null, "null means no replay; [] would show every action as Skipped");
        decision.GetProperty("followOns").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ClosedAndDecidedQueries_SelectTheDecisionRecordLookup_WhateverTheLiveConfigurationVersion()
    {
        var json = File.ReadAllText(WorklistFactory.ConfigPath);
        foreach (var set in new[] { WorklistSet.ClosedToday, WorklistSet.ClosedEarlier, WorklistSet.Decided })
        {
            SignalReadService.ParseFetches(json, set)!.Single().Should().Contain("<attribute name=\"sprk_decisionrecord\" />", set.ToString());
        }

        var decided = SignalReadService.ParseFetches(json, WorklistSet.Decided)!.Single();
        decided.Should().Contain("operator=\"last-x-days\" value=\"90\"").And.Contain("attribute=\"sprk_decisionrecord\" operator=\"not-null\"");
        decided.Should().NotContain("last-x-hours");
        SignalReadService.ParseFetches(json, WorklistSet.Lanes)!.Should().OnlyContain(f => !f.Contains("sprk_decisionrecord"));
        SignalReadService.ParseFetches(json.Replace("sprk_resolvedon' operator='last-x-hours' value='720'", "sprk_x' operator='last-x-hours' value='720'"), WorklistSet.Decided)
            .Should().BeNull("a closedEarlier query with no resolved-on window cannot be widened: fail closed");
    }

    [Fact]
    public async Task ADecisionRecordNotWrittenByTheWriter_IsOmitted_LikeAnUnreadableOne()
    {
        // F1 (review of #2014): Console User can Create a row and Write a Signal's lookup, so a person's forged "Judgement, decided by
        // <partner>" must not be shown as a genuine record.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var forged = world.Decision(readers: [Alice], number: "DR-66666", by: "Pat Partner", createdBy: SomePerson);
        var genuine = world.Decision(readers: [Alice], number: "DR-00001");
        var forgedItem = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddHours(-49), decisionRecord: forged);
        var genuineItem = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddHours(-49), decisionRecord: genuine);

        var earlier = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).ToDictionary(i => i.GetProperty("signalId").GetGuid());
        earlier[forgedItem].GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null, "the item stays listed with no decision");
        earlier[genuineItem].GetProperty("decision").GetProperty("number").GetString().Should().Be("DR-00001");

        var decided = await OkAsync(world, Alice, "?set=decided");
        Ids(decided).Should().Equal([genuineItem], "the decided set drops it");
        decided.ToString().Should().NotContain("DR-66666").And.NotContain("Pat Partner");
    }

    [Fact]
    public async Task DecidedSet_IncludesAnInquirySentRecord_WhoseSignalStaysAcknowledged_ButNotOneWithNoRecord()
    {
        // F1 (review of #2014): a review that leaves its Signal open (inquiry sent, reminder, follow-on only) writes a record and the
        // Signal stays Acknowledged with the link.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var inquiry = world.Decision(readers: [Alice], number: "DR-00005", steps: StepsEnvelope);
        var sent = world.Signal(Decide, core: (MatterType, m), sets: ["decided"], status: 100000001, decisionRecord: inquiry);
        world.Signal(Decide, core: (MatterType, m), sets: ["decided"], status: 100000001); // acknowledged, no record
        world.Signal(Decide, core: (MatterType, m), sets: ["decided"], status: 100000000, decisionRecord: world.Decision(readers: [Alice])); // still open

        var item = AllItems(await OkAsync(world, Alice, "?set=decided")).Single();

        item.GetProperty("signalId").GetGuid().Should().Be(sent);
        item.GetProperty("status").GetString().Should().Be("Acknowledged");
        item.GetProperty("decision").GetProperty("number").GetString().Should().Be("DR-00005");
    }

    [Fact]
    public void OnlyTheDecidedQuery_WidensTheStatus_ClosedTodayAndClosedEarlierStayResolved()
    {
        var json = File.ReadAllText(WorklistFactory.ConfigPath);
        SignalReadService.ParseFetches(json, WorklistSet.Decided)!.Single().Replace('\'', '"')
            .Should().Contain("<condition attribute=\"sprk_signalstatus\" operator=\"in\"><value>100000001</value><value>100000002</value></condition>");
        foreach (var set in new[] { WorklistSet.ClosedToday, WorklistSet.ClosedEarlier })
        {
            SignalReadService.ParseFetches(json, set)!.Single().Replace('\'', '"')
                .Should().Contain("<condition attribute=\"sprk_signalstatus\" operator=\"eq\" value=\"100000002\" />").And.NotContain("operator=\"in\"");
        }
    }

    [Fact]
    public async Task WhoDecided_IsReadThroughTheConfirmingUserLink_AndTheRecordMustBeTheWriters()
    {
        // F3 (review of #2014): the fake returns a name only through an outer link to systemuser on sprk_confirmedby, so this fails if the
        // query links any other column; and the createdby condition must be on the query, with the writer's id.
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49),
            decisionRecord: world.Decision(readers: [Alice], by: "Dana Decider"));

        var item = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single();

        item.GetProperty("decision").GetProperty("decidedByName").GetString().Should().Be("Dana Decider");
        var fetch = Uri.UnescapeDataString(world.Requests.Single(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal))).Replace('\'', '"');
        fetch.Should().Contain("<link-entity name=\"systemuser\" from=\"systemuserid\" to=\"sprk_confirmedby\" link-type=\"outer\" alias=\"by\">")
            .And.Contain($"<condition attribute=\"createdby\" operator=\"eq\" value=\"{WriterUser:D}\" />");
        fetch.Should().NotContain("ownerid");
    }

    [Fact]
    public async Task DecisionRecordRead_401_Is503_And_403_IsOmittedWithAWarning()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier"], resolvedOn: world.Now.AddHours(-49), decisionRecord: world.Decision(readers: [Alice]));

        world.UnauthorizedTables.Add("sprk_decisionrecord");
        (await GetAsync(world, Alice, "?set=closedEarlier")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "a 401 is a fault, not 'no Read'");
        world.UnauthorizedTables.Clear();

        world.TableDenied[Alice] = ["sprk_decisionrecord"];
        _factory.Logs.Warnings.Clear();
        AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).Single().GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null);
        _factory.Logs.Warnings.Should().Contain(w => w.Contains("Decision Record read was refused (403)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASetThatIsNotOneOfTheFive_IsStill400()
    {
        (await GetAsync(new World(), Alice, "?set=decisions")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D-185 (task 163, #2285): the policy columns are read by the BFF, so no user needs Read on sprk_policy
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The repo row as it was before D-185: each query outer-links sprk_policy as alias <c>policy</c> (the same text the live
    /// dev row holds until the new configuration is applied).</summary>
    private static string LegacyConfigWithPolicyLink()
    {
        const string link = "<link-entity name='sprk_policy' from='sprk_policyid' to='sprk_policy' link-type='outer' alias='policy'>"
            + "<attribute name='sprk_shortname' /><attribute name='sprk_worktype' /><attribute name='sprk_severity' /></link-entity>";
        var json = File.ReadAllText(WorklistFactory.ConfigPath);
        var legacy = json.Replace("<attribute name='ownerid' />", "<attribute name='ownerid' />" + link);
        legacy.Should().NotBe(json);
        Regex.Matches(legacy, "alias='policy'").Count.Should().Be(5, "all five queries carry the link, as before D-185");
        return legacy;
    }

    private sealed record ItemView(Guid Id, string? Rule, string? WorkType);

    private static List<ItemView> Items(JsonElement body) =>
        body.GetProperty("lanes").EnumerateArray().SelectMany(l => l.GetProperty("groups").EnumerateArray())
            .SelectMany(g => g.GetProperty("items").EnumerateArray().Select(i => new ItemView(
                i.GetProperty("signalId").GetGuid(),
                i.GetProperty("ruleShortName").GetString(),
                i.GetProperty("workType").GetString())))
            .ToList();

    /// <summary>One fixture mixing severities, work types and shared/own policies: the same data is served under the new and the old configuration.</summary>
    private static (World World, Dictionary<string, Guid> Ids) MixedWorld()
    {
        var world = new World();
        var ids = new Dictionary<string, Guid>();
        var mA = world.Matter(readers: [Alice], number: "MAT-0002");
        var mB = world.Matter(readers: [Alice], number: "MAT-0001");
        var mC = world.Matter(readers: [Alice], number: "MAT-0003");
        var shared = world.Policy("Shared rule", 100000002, Critical);
        ids["aInfo"] = world.Signal(Decide, core: (MatterType, mA), severity: Info, shortName: "Info rule", workType: 100000004, firstDetected: "2026-09-01T00:00:00Z");
        ids["aCrit"] = world.Signal(Decide, core: (MatterType, mA), policy: shared, firstDetected: "2026-10-05T00:00:00Z");
        ids["bCrit"] = world.Signal(Decide, core: (MatterType, mB), policy: shared, firstDetected: "2026-10-05T00:00:00Z");
        ids["cWarn"] = world.Signal(Decide, core: (MatterType, mC), severity: Warning, shortName: "Budget gap", workType: 100000001, firstDetected: "2026-09-20T00:00:00Z");
        ids["dNone"] = world.Signal(Decide, core: (MatterType, mC), severity: null, shortName: "No severity", workType: 100000003, firstDetected: "2026-01-01T00:00:00Z");
        return (world, ids);
    }

    [Fact]
    public async Task NewConfiguration_WithNoPolicyReadForTheCaller_GivesTheRuleNameWorkTypeAndRankFromTheBff()
    {
        var (world, ids) = MixedWorld();
        world.CallerHasNoPolicyRead = true;

        var body = await OkAsync(world, Alice);

        world.Requests.Where(r => r.StartsWith("sprk_signals?", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(r => !Uri.UnescapeDataString(r).Contains("link-entity", StringComparison.Ordinal));
        var items = Items(body).ToDictionary(i => i.Id);
        items[ids["aCrit"]].Should().Be(new ItemView(ids["aCrit"], "Shared rule", "chaseReply"));
        items[ids["cWarn"]].Should().Be(new ItemView(ids["cWarn"], "Budget gap", "approveOrRebudget"));
        items[ids["aInfo"]].Should().Be(new ItemView(ids["aInfo"], "Info rule", "comingDue"));
        items[ids["dNone"]].Should().Be(new ItemView(ids["dNone"], "No severity", "finishOrReschedule"));
        // Ranking (D-42): the Critical group first (B before A on record number), then Warning, with the severity-less item last in its group.
        var groups = Lane(body, "Decide").GetProperty("groups").EnumerateArray()
            .Select(g => g.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("signalId").GetGuid()).ToList()).ToList();
        groups.Should().HaveCount(3);
        groups[0].Should().Equal(ids["bCrit"]);
        groups[1].Should().Equal(ids["aCrit"], ids["aInfo"]);
        groups[2].Should().Equal(ids["cWarn"], ids["dNone"]);
    }

    [Fact]
    public async Task NewConfiguration_ReturnsTheSameItemsOrderAndFields_AsTheOldConfigurationForTheSameData()
    {
        var (newWorld, newIds) = MixedWorld();
        var newBody = await OkAsync(newWorld, Alice);

        var (oldWorld, oldIds) = MixedWorld();
        oldWorld.ConfigJson = LegacyConfigWithPolicyLink();
        var oldBody = await OkAsync(oldWorld, Alice);

        // The ids differ between the two worlds; compare by the fixture's role names.
        static List<(string Key, string? Rule, string? WorkType)> ByRole(JsonElement body, Dictionary<string, Guid> ids) =>
            Items(body).Select(i => (ids.Single(kv => kv.Value == i.Id).Key, i.Rule, i.WorkType)).ToList();
        var shapeNew = ByRole(newBody, newIds);
        shapeNew.Should().HaveCount(5);
        ByRole(oldBody, oldIds).Should().Equal(shapeNew, "the same items, in the same order, with the same rule names and work types");
        // The old configuration really is the joined one.
        oldWorld.Requests.Where(r => r.StartsWith("sprk_signals?", StringComparison.Ordinal))
            .Should().OnlyContain(r => Uri.UnescapeDataString(r).Contains("alias=\"policy\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldConfiguration_WithTheLink_IsUnchanged_AndTheBffReadAgrees()
    {
        var world = new World { ConfigJson = LegacyConfigWithPolicyLink() };
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap");

        Items(await OkAsync(world, Alice)).Should().Equal(new ItemView(s, "Budget gap", "approveOrRebudget"));
    }

    [Fact]
    public async Task OldConfiguration_WithNoPolicyReadForTheCaller_IsTheEmptyListToday_WhichIsWhyTheRoleChangeFollowsTheNewConfiguration()
    {
        // Premise pin for the deploy order (D-185): Dataverse refuses a Signal query that links sprk_policy when the caller has no
        // Read on it, and the route reads that as "no read on sprk_signal" (an empty list and a Warning). So the live roles lose the
        // Read only after the repo configuration (no link) is applied; this is the failure that ordering prevents.
        var world = new World { ConfigJson = LegacyConfigWithPolicyLink(), CallerHasNoPolicyRead = true };
        world.Signal(Decide, core: (MatterType, world.Matter(readers: [Alice])));
        AllItems(await OkAsync(world, Alice)).Should().BeEmpty();
    }

    [Fact]
    public async Task APolicyReadFault_Is503_NeverRowsWithBlankSeverity()
    {
        var (world, _) = MixedWorld();
        world.FaultAppOnlyTables.Add("sprk_policy");

        var response = await GetAsync(world, Alice);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Problem(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.signal.worklist_unavailable");
    }

    [Fact]
    public async Task AFaultOnTheSecondPolicyChunk_Is503_NoRowsReturned()
    {
        // 150 distinct policies make two sprk_policy reads (100 + 50); only the second one throws.
        var world = new World { FaultPolicyReadFrom = 2 };
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < 150; i++)
        {
            world.Signal(Decide, core: (MatterType, m));
        }

        var response = await GetAsync(world, Alice);

        world.PolicyReads.Select(r => r.Length).Should().Equal(SignalReadService.IdChunk, 50);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await Problem(response);
        problem.GetProperty("reasonCode").GetString().Should().Be("ontology.signal.worklist_unavailable");
        problem.ToString().Should().NotContain("lanes", "no rows come back with a faulted read");
    }

    [Fact]
    public async Task AnExternalCaller_GetsNoPolicyColumns_AndNoPolicyQueryIsIssued()
    {
        var world = new World { CallerIsExternal = true };
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap");

        Items(await OkAsync(world, Alice)).Should().Equal(new ItemView(s, null, null));
        world.PolicyReads.Should().BeEmpty("an external caller triggers no sprk_policy read");
    }

    [Fact]
    public async Task AnInternalCaller_IsEnrichedAsBefore_WithOnePolicyQuery()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var s = world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap");

        Items(await OkAsync(world, Alice)).Should().Equal(new ItemView(s, "Budget gap", "approveOrRebudget"));
        world.PolicyReads.Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnresolvedOrNonGuidCaller_IsNotEnriched_ButTheRowsAreStillReturned()
    {
        // The resolver's "no answer" reasons (CallerSystemUserResolver.cs): no-claims-principal, no-oid-claim, no-matching-systemuser,
        // and the null-object's resolver-not-registered. All are 200 with no enrichment. lookup-failed is a fault and is NOT here.
        var worlds = new List<World>
        {
            new() { CallerResolvedToNonGuid = true },
        };
        foreach (var reason in new[] { "no-claims-principal", "no-oid-claim", "no-matching-systemuser", "resolver-not-registered" })
        {
            worlds.Add(new World { CallerUnresolved = true, CallerUnresolvedReason = reason });
        }

        foreach (var world in worlds)
        {
            var m = world.Matter(readers: [Alice]);
            var s = world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap");

            Items(await OkAsync(world, Alice)).Should().Equal(new ItemView(s, null, null));
            world.PolicyReads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AResolverFault_Is503_NotARowListWithBlankSeverity()
    {
        // F2 (fix round 2): a Dataverse fault resolving the caller (an exception from either resolver, or the resolver's own
        // "lookup-failed" answer) must not turn into a 200 whose rule names, work types and severities are blank for an internal user.
        var worlds = new[]
        {
            new World { CallerResolverThrows = true },
            new World { ExternalFlagReadThrows = true },
            new World { CallerUnresolved = true, CallerUnresolvedReason = SignalReadService.CallerLookupFailedReason },
        };
        foreach (var world in worlds)
        {
            var m = world.Matter(readers: [Alice]);
            world.Signal(Decide, core: (MatterType, m), severity: Critical, workType: 100000001, shortName: "Budget gap");

            var response = await GetAsync(world, Alice);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var problem = await Problem(response);
            problem.GetProperty("reasonCode").GetString().Should().Be("ontology.signal.worklist_unavailable");
            problem.ToString().Should().NotContain("lanes", "no rows come back when the caller cannot be classified");
            world.PolicyReads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TheExternalRule_RethrowsCancellation_AndAnyOtherFault_ButNotAnAnswer()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelling = new Mock<ICallerSystemUserResolver>();
        cancelling.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));
        var users = new Mock<ISystemUserIdentityResolver>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var act = () => SignalReadService.IsExternalOrUnknownCallerAsync(cancelling.Object, users.Object, null, logger, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The same exception while the token is NOT cancelled is any other fault: it propagates (the route answers 503).
        var notCancelled = () => SignalReadService.IsExternalOrUnknownCallerAsync(cancelling.Object, users.Object, null, logger, CancellationToken.None);
        await notCancelled.Should().ThrowAsync<OperationCanceledException>();

        var faulting = new Mock<ICallerSystemUserResolver>();
        faulting.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        var fault = () => SignalReadService.IsExternalOrUnknownCallerAsync(faulting.Object, users.Object, null, logger, CancellationToken.None);
        await fault.Should().ThrowAsync<TimeoutException>();

        // lookup-failed is the resolver's own Dataverse fault; every other unresolved reason is an answer (external/unknown).
        foreach (var (reason, expectThrow) in new[]
        {
            ("lookup-failed", true), ("no-claims-principal", false), ("no-oid-claim", false), ("no-matching-systemuser", false), ("resolver-not-registered", false),
        })
        {
            var r = new Mock<ICallerSystemUserResolver>();
            r.Setup(x => x.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Unresolved(reason));
            var call = () => SignalReadService.IsExternalOrUnknownCallerAsync(r.Object, users.Object, null, logger, CancellationToken.None);
            if (expectThrow)
            {
                await call.Should().ThrowAsync<InvalidOperationException>(reason);
            }
            else
            {
                (await call()).Should().BeTrue(reason);
            }
        }
    }

    [Fact]
    public async Task OneBatchedPolicyReadPerRequest_ChunkedByIdChunk_AndDistinctIdsOnly()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        for (var i = 0; i < 150; i++)
        {
            world.Signal(Decide, core: (MatterType, m));
        }

        AllItems(await OkAsync(world, Alice)).Should().HaveCount(150);
        world.PolicyReads.Select(r => r.Length).Should().Equal(SignalReadService.IdChunk, 50);

        var shared = new World();
        var m2 = shared.Matter(readers: [Alice]);
        var one = shared.Policy("One rule");
        for (var i = 0; i < 150; i++)
        {
            shared.Signal(Decide, core: (MatterType, m2), policy: one);
        }

        AllItems(await OkAsync(shared, Alice)).Should().HaveCount(150);
        shared.PolicyReads.Should().ContainSingle().Which.Should().Equal(one);
    }

    [Fact]
    public async Task ThePolicyIsReadOnlyForTheRowsTheCallerCanSee()
    {
        var world = new World();
        var visible = world.Matter(readers: [Alice]);
        var hidden = world.Matter(readers: [Bob]);
        var seen = world.Policy("Seen rule");
        var secret = world.Policy("Secret rule");
        world.Signal(Decide, core: (MatterType, visible), policy: seen);
        world.Signal(Decide, core: (MatterType, hidden), policy: secret);

        Items(await OkAsync(world, Alice)).Single().Rule.Should().Be("Seen rule");

        world.PolicyReads.Should().ContainSingle().Which.Should().Equal(new[] { seen }, "a policy named only by rows the caller cannot see is never read");
    }

    [Fact]
    public async Task APolicyThatNoLongerExists_KeepsTodaysMissingAliasBehaviour_NoNameNoWorkType_RankedLast()
    {
        var world = new World();
        var mA = world.Matter(readers: [Alice], number: "MAT-0001");
        var mB = world.Matter(readers: [Alice], number: "MAT-0002");
        var orphan = world.Signal(Decide, core: (MatterType, mA), policy: Guid.NewGuid(), firstDetected: "2026-01-01T00:00:00Z");
        var info = world.Signal(Decide, core: (MatterType, mB), severity: Info, shortName: "Info rule");

        var items = Items(await OkAsync(world, Alice));

        items.Select(i => i.Id).Should().Equal(info, orphan);
        items.Single(i => i.Id == orphan).Should().Be(new ItemView(orphan, null, null));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> GetAsync(World world, Guid? actingAs, string query = "")
    {
        _factory.World = world;
        world.ActingAs = actingAs;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        return await client.GetAsync(Route + query);
    }

    private async Task<JsonElement> OkAsync(World world, Guid actingAs, string query = "")
    {
        var response = await GetAsync(world, actingAs, query);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static JsonElement Lane(JsonElement body, string lane) =>
        body.GetProperty("lanes").EnumerateArray().Single(l => l.GetProperty("lane").GetString() == lane);

    private static IEnumerable<JsonElement> AllItems(JsonElement body) =>
        body.GetProperty("lanes").EnumerateArray().SelectMany(l => l.GetProperty("groups").EnumerateArray())
            .SelectMany(g => g.GetProperty("items").EnumerateArray()).ToList();

    private static List<Guid> Ids(JsonElement body) => AllItems(body).Select(i => i.GetProperty("signalId").GetGuid()).ToList();

    private static List<Guid> DoSubjects(World world, JsonElement body) =>
        Lane(body, "Do").GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray())
            .Select(i => world.SubjectOf[i.GetProperty("signalId").GetGuid()]).ToList();

    // ---------------------------------------------------------------------------------------------------------------
    // the fake world: what Dataverse would answer as each user
    // ---------------------------------------------------------------------------------------------------------------

    internal sealed class World
    {
        private int _number;

        public Guid? ActingAs { get; set; }
        public int PageSize { get; init; } = 5000;

        /// <summary>The route's clock (TimeProvider): noon UTC by default, so "today" never flakes around midnight.</summary>
        public DateTimeOffset Now { get; init; } = DateTimeOffset.Parse("2026-10-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The caller's contact could not be READ (identity service fault flags), or the resolve throws.</summary>
        public bool ContactUnreadable { get; init; }
        public bool ContactThrows { get; init; }

        /// <summary>From this page on, the Signal query is refused (0: never).</summary>
        public int DenySignalPagesFrom { get; init; }

        /// <summary>App-only Dataverse (the secure-family walk, the time-zone reads): rows by table, evaluated by condition.</summary>
        public List<Entity> AppRows { get; } = [];
        public bool SecureTeamExists { get; set; }

        /// <summary>App-only reads of these tables throw, like a Dataverse timeout.</summary>
        public HashSet<string> FaultAppOnlyTables { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, Guid> UserOfContact { get; } = [];
        public Dictionary<Guid, int> ZoneCodeOfUser { get; } = [];
        public List<string> Requests { get; } = [];
        public HashSet<string> FaultingTables { get; } = new(StringComparer.Ordinal);

        /// <summary>The caller's read of these tables throws (a transport exception) or answers 401 (token failed mid-request).</summary>
        public HashSet<string> ThrowingTables { get; } = new(StringComparer.Ordinal);
        public HashSet<string> UnauthorizedTables { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, string[]> TableDenied { get; } = [];

        /// <summary>D-176: the caller's read of a path that starts with a key answers as its value ("500", "401", "403", "unparseable"
        /// or "throw"), so one read of a table can fail while another read of the same table succeeds.</summary>
        public Dictionary<string, string> PathFaults { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, Guid[]> SignalReaders { get; } = [];
        public Dictionary<Guid, Guid> SubjectOf { get; } = [];
        public List<(Guid Id, string[] Sets, Dictionary<string, object?> Row)> Signals { get; } = [];
        public Dictionary<Guid, (string Table, Guid[] Readers, Dictionary<string, object?> Columns)> Records { get; } = [];

        /// <summary>Task 045: Decision Records by id, with who may read each (Dataverse trims the caller's FetchXML read by this).</summary>
        public Dictionary<Guid, (Guid[] Readers, Dictionary<string, object?> Row)> DecisionRecords { get; } = [];

        /// <summary>Who created each Decision Record (the row's createdby): the writer's service identity for a genuine one.</summary>
        public Dictionary<Guid, Guid> DecisionRecordCreator { get; } = [];

        public Guid Decision(
            Guid[] readers,
            string number = "DR-00001",
            int recordClass = 100000000,
            int outcome = 100000000,
            string decidedOn = "2026-10-14T09:30:00Z",
            string? by = "Dana Decider",
            string? reason = null,
            string? steps = null,
            string? followOns = null,
            Guid? createdBy = null)
        {
            var id = Guid.NewGuid();
            DecisionRecordCreator[id] = createdBy ?? WriterUser;
            DecisionRecords[id] = (readers, new Dictionary<string, object?>
            {
                ["sprk_decisionrecordid"] = id,
                ["sprk_decisionnumber"] = number,
                ["sprk_recordclass"] = recordClass,
                ["sprk_decisionoutcome"] = outcome,
                ["sprk_decidedon"] = decidedOn,
                ["sprk_reason"] = reason,
                ["sprk_steps"] = steps,
                ["sprk_followons"] = followOns ?? "[]",
                ["by.fullname"] = by,
            });
            return id;
        }

        public static readonly Dictionary<string, Guid> Contacts = new()
        {
            [Alice.ToString()] = AliceContact,
            [Bob.ToString()] = BobContact,
            [Carol.ToString()] = CarolContact,
        };

        public static readonly Dictionary<Guid, (string Logical, string Label, string? Display, string? Number)> Catalog = new()
        {
            [MatterType] = ("sprk_matter", "Matter", "sprk_mattername", "sprk_matternumber"),
            [ProjectType] = ("sprk_project", "Project", "sprk_projectname", "sprk_projectnumber"),
            [WorkAssignmentType] = ("sprk_workassignment", "Work Assignment", "sprk_name", "sprk_workassignmentnumber"),
            [ServiceRequestType] = ("sprk_servicerequest", "Service Request", "sprk_name", null),
            [NotCoreType] = ("sprk_invoice", "Invoice", "sprk_name", null),
            [EventType] = ("sprk_event", "Event", "sprk_eventname", null),
            [TodoType] = ("sprk_todo", "To Do", "sprk_name", null),
            [BillingEventType] = ("sprk_billingevent", "Billing Event", "sprk_name", null),
        };

        public Guid Record(string table, Guid[] readers, Dictionary<string, object?>? columns = null)
        {
            var id = Guid.NewGuid();
            Records[id] = (table, readers, columns ?? []);
            return id;
        }

        /// <summary>D-176: an invoice the readers read as themselves, in <paramref name="vendorOrg"/>.</summary>
        public Guid Invoice(Guid[] readers, Guid vendorOrg) =>
            Record("sprk_invoice", readers, new() { ["_sprk_vendororg_value"] = vendorOrg, ["sprk_highpriority"] = false });

        /// <summary>D-176: a billing event (an invoice line) of <paramref name="invoice"/>.</summary>
        public Guid BillingEvent(Guid[] readers, Guid? invoice) =>
            Record("sprk_billingevent", readers, new() { ["_sprk_invoice_value"] = invoice });

        /// <summary>D-176: an invoice submission (alternate key: vendor org + tracking number) of <paramref name="invoice"/>.</summary>
        public Guid Submission(Guid[] readers, Guid vendorOrg, string trackingNumber, Guid? invoice) =>
            Record("sprk_invoicesubmission", readers, new()
            {
                ["_sprk_vendororg_value"] = vendorOrg, ["sprk_trackingnumber"] = trackingNumber, ["_sprk_invoice_value"] = invoice,
            });

        public Guid Matter(Guid[] readers, string? name = null, string? number = null, bool highPriority = false, Guid? assignedToInternal = null) =>
            Record("sprk_matter", readers, new()
            {
                ["sprk_mattername"] = name ?? $"Matter {++_number}",
                ["sprk_matternumber"] = number ?? $"MAT-{_number:0000}",
                ["sprk_highpriority"] = highPriority,
                ["_sprk_assignedtointernal_value"] = assignedToInternal,
            });

        public Guid Event(Guid owner, Guid? assignedTo = null, Guid? createdBy = null, Guid[]? readers = null) =>
            Record("sprk_event", readers ?? [Alice, Bob, Carol], new()
            {
                ["_ownerid_value"] = owner,
                ["_sprk_assignedto_value"] = assignedTo,
                ["_sprk_createdbyperson_value"] = createdBy,
                ["sprk_highpriority"] = false,
            });

        public Guid Todo(Guid owner, Guid? assignedTo = null, Guid[]? readers = null, bool highPriority = false) =>
            Record("sprk_todo", readers ?? [owner == Team ? Alice : owner], new()
            {
                ["_ownerid_value"] = owner,
                ["_sprk_assignedto_value"] = assignedTo,
                ["sprk_highpriority"] = highPriority,
            });

        public Guid WorkAssignment(Guid createdBy, Guid assignedToInternal, Guid[] readers) =>
            Record("sprk_workassignment", readers, new()
            {
                ["_ownerid_value"] = Team,
                ["_sprk_createdbyperson_value"] = createdBy,
                ["_sprk_assignedtointernal_value"] = assignedToInternal,
                ["sprk_name"] = "WA",
                ["sprk_workassignmentnumber"] = $"WA-{++_number}",
                ["sprk_highpriority"] = false,
            });

        public Guid Signal(
            int lane,
            (Guid Type, Guid Id)? core,
            (Guid? Type, Guid Id)? subject = null,
            int? severity = Warning,
            int workType = 100000000,
            string shortName = "Rule",
            string? title = null,
            string? subjectName = null,
            string firstDetected = "2026-10-01T00:00:00Z",
            string? evidence = null,
            string[]? sets = null,
            DateTimeOffset? resolvedOn = null,
            DateTimeOffset? suppressedUntil = null,
            string? sentence = null,
            Guid? decisionRecord = null,
            int? status = null,
            Guid? policy = null)
        {
            var id = Guid.NewGuid();
            policy ??= Policy(shortName, workType, severity);
            var subjectPair = subject ?? (core is { } c ? (c.Type, c.Id) : (null, Guid.NewGuid()));
            SubjectOf[id] = subjectPair.Id;
            var row = new Dictionary<string, object?>
            {
                ["sprk_signalid"] = id,
                ["sprk_name"] = title ?? $"Signal {id:N}",
                ["sprk_lane"] = lane,
                ["sprk_signalstatus"] = status ?? (resolvedOn is null ? 100000000 : 100000002),
                ["sprk_firstdetected"] = firstDetected,
                ["sprk_regardingrecordid"] = subjectPair.Id.ToString("D"),
                ["sprk_regardingrecordname"] = subjectName,
                ["_sprk_policyversion_value"] = Guid.NewGuid(),
                ["_sprk_policy_value"] = policy,
                ["sprk_evidencerefs"] = evidence,
                ["sprk_sentence"] = sentence,
                ["policy.sprk_shortname"] = shortName,
                ["policy.sprk_worktype"] = workType,
                ["policy.sprk_severity"] = severity,
                ["sprk_resolvedon"] = resolvedOn?.ToString("o"),
                ["sprk_suppresseduntil"] = suppressedUntil?.ToString("o"),
                ["_sprk_decisionrecord_value"] = decisionRecord,
            };
            if (subjectPair.Type is { } st) row["_sprk_regardingrecordtype_value"] = st;
            if (core is { } pair)
            {
                row["_sprk_corerecordtype_value"] = pair.Type;
                row["sprk_corerecordid"] = pair.Id == Guid.Empty ? null : pair.Id.ToString("D");
            }

            Signals.Add((id, sets ?? [lane == Decide ? "decide" : "do"], row));
            return id;
        }

        /// <summary>A <c>sprk_policy</c> row (app-only Dataverse): its short name, work type and severity (no severity column when null).</summary>
        public Guid Policy(string shortName = "Rule", int workType = 100000000, int? severity = Warning)
        {
            var row = new Entity("sprk_policy", Guid.NewGuid())
            {
                ["sprk_shortname"] = shortName,
                ["sprk_worktype"] = new OptionSetValue(workType),
            };
            if (severity is { } sev) row["sprk_severity"] = new OptionSetValue(sev);
            AppRows.Add(row);
            return row.Id;
        }

        /// <summary>D-185: the caller has no Read on <c>sprk_policy</c>, so a Signal query that links it is refused by Dataverse.</summary>
        public bool CallerHasNoPolicyRead { get; set; }

        /// <summary>D-185 (uac-r2): how the caller reads for the external-or-unknown rule. Default: a resolved internal systemuser.</summary>
        public bool CallerIsExternal { get; set; }
        public bool CallerUnresolved { get; set; }
        /// <summary>The resolver's UnresolvedReason when <see cref="CallerUnresolved"/>; default no-matching-systemuser.</summary>
        public string CallerUnresolvedReason { get; set; } = "no-matching-systemuser";
        public bool CallerResolvedToNonGuid { get; set; }
        public bool CallerResolverThrows { get; set; }
        public bool ExternalFlagReadThrows { get; set; }

        /// <summary>From this sprk_policy app-only read on (1-based; 0: never) the read throws, like a Dataverse timeout.</summary>
        public int FaultPolicyReadFrom { get; set; }

        /// <summary>The worklist row's JSON as the BFF reads it; null: the repo row verbatim.</summary>
        public string? ConfigJson { get; set; }

        /// <summary>Every app-only read of <c>sprk_policy</c> the BFF made: the ids it asked for.</summary>
        public List<Guid[]> PolicyReads { get; } = [];

        // ── app-only Dataverse (the secure-family walk and the time-zone reads), evaluated by condition ─────────────────

        public static readonly Guid SecureTeamId = Guid.Parse("5ec0be00-0000-4000-8000-000000000001");
        public static readonly Guid OrdinaryTeamId = Guid.Parse("0e1a7100-0000-4000-8000-000000000002");
        private static readonly Guid SecureBuId = Guid.Parse("5ec0be00-0000-4000-8000-0000000000b1");
        private const int EasternZoneCode = 35;

        /// <summary>A root (matter or project) as uac-r2's walk reads it: <c>sprk_issecure</c> and its owner.</summary>
        public void AppRoot(string table, Guid id, bool secure)
        {
            var row = new Entity(table, id) { ["sprk_issecure"] = secure };
            if (secure) row["owningteam"] = new EntityReference("team", SecureTeamId);
            else row["owninguser"] = new EntityReference("systemuser", Guid.NewGuid());
            AppRows.Add(row);
        }

        /// <summary>A communication filed by <paramref name="lookups"/>, owned by the Secure team or an ordinary team. The caller's own
        /// read (task 151, D-142) returns it to Alice, Bob and Carol unless <see cref="OnlyReadableBy"/> says otherwise.</summary>
        public Guid AppCommunication(bool secureOwned, params (string Column, string Table, Guid Id)[] lookups)
        {
            var row = new Entity("sprk_communication", Guid.NewGuid())
            {
                ["owningteam"] = new EntityReference("team", secureOwned ? SecureTeamId : OrdinaryTeamId),
            };
            foreach (var (column, table, id) in lookups) row[column] = new EntityReference(table, id);
            AppRows.Add(row);
            Records[row.Id] = ("sprk_communication", [Alice, Bob, Carol], []);
            return row.Id;
        }

        /// <summary>Only <paramref name="readers"/> read the witness row as themselves (D-142).</summary>
        public void OnlyReadableBy(Guid witness, params Guid[] readers) => Records[witness] = Records[witness] with { Readers = readers };

        /// <summary>The app-only row is owned by a user (a privately owned record), not a team.</summary>
        public void OwnedByUser(Guid witness, Guid user)
        {
            var row = AppRows.Single(r => r.Id == witness);
            row.Attributes.Remove("owningteam");
            row["owninguser"] = new EntityReference("systemuser", user);
        }

        /// <summary>The Signal row as the app-only partition read sees it (as SignalWriter writes it: dedupe key, owner, lineage).</summary>
        public void AppSignal(Guid signalId, Guid matterId, bool secureOwned)
        {
            AppRows.Add(new Entity("sprk_signal", signalId)
            {
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey("POL-TEST", "sprk_matter", matterId.ToString("D"), 1),
                ["ownerid"] = new EntityReference("team", secureOwned ? SecureTeamId : OrdinaryTeamId),
                ["sprk_matter"] = new EntityReference("sprk_matter", matterId),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
            });
        }

        /// <summary>A user (linked to <paramref name="contact"/>) whose Dataverse zone is US Eastern.</summary>
        public void EasternUserOf(Guid contact)
        {
            var user = Guid.NewGuid();
            UserOfContact[contact] = user;
            ZoneCodeOfUser[user] = EasternZoneCode;
        }

        public EntityCollection AppOnly(QueryExpression q)
        {
            if (q.EntityName == "sprk_policy")
            {
                lock (PolicyReads) PolicyReads.Add(q.Criteria.Conditions.SelectMany(c => c.Values).OfType<Guid>().ToArray());
            }

            if (q.EntityName == "sprk_policy" && FaultPolicyReadFrom > 0 && PolicyReads.Count >= FaultPolicyReadFrom)
                throw new TimeoutException("app-only read of sprk_policy timed out");
            if (FaultAppOnlyTables.Contains(q.EntityName)) throw new TimeoutException($"app-only read of {q.EntityName} timed out");
            switch (q.EntityName)
            {
                case "businessunit":
                    return SecureTeamExists
                        ? new EntityCollection([new Entity("businessunit", SecureBuId) { ["name"] = SecureRecordOwnerTeam.DefaultBusinessUnitName }])
                        : new EntityCollection();
                case "team":
                    return SecureTeamExists
                        ? new EntityCollection([new Entity("team", SecureTeamId)
                        {
                            ["businessunitid"] = new EntityReference("businessunit", SecureBuId), ["name"] = SecureRecordOwnerTeam.DefaultOwnerTeamName,
                            ["teamtype"] = new OptionSetValue(SecureRecordOwnerTeam.OwnerTeamType), ["isdefault"] = false,
                        }])
                        : new EntityCollection();
                case "systemuser":
                    var contact = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_primarycontact")?.Values[0] as Guid?;
                    return contact is { } c2 && UserOfContact.TryGetValue(c2, out var user)
                        ? new EntityCollection([new Entity("systemuser", user)])
                        : new EntityCollection();
                case "timezonedefinition":
                    return q.Criteria.Conditions.Any(c => c.AttributeName == "timezonecode" && Equals(c.Values[0], EasternZoneCode))
                        ? new EntityCollection([new Entity("timezonedefinition", Guid.NewGuid()) { ["standardname"] = "Eastern Standard Time" }])
                        : new EntityCollection();
                default:
                    var result = new EntityCollection();
                    foreach (var row in AppRows.Where(r => r.LogicalName == q.EntityName && Matches(r, q.Criteria))) result.Entities.Add(Projected(row, q.ColumnSet));
                    return result;
            }
        }

        /// <summary>The row as Dataverse returns it for <paramref name="columns"/>: only the asked columns (all only for AllColumns), so
        /// code that reads a column it did not ask for sees null, as it would live.</summary>
        private static Entity Projected(Entity row, ColumnSet columns)
        {
            if (columns.AllColumns) return row;
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Columns)
            {
                if (row.Contains(column)) copy[column] = row[column];
            }

            return copy;
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            var parts = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
            return parts.Count == 0 || (filter.FilterOperator == LogicalOperator.Or ? parts.Any(p => p) : parts.All(p => p));
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            object? actual = c.AttributeName == row.LogicalName + "id" ? row.Id : (row.Contains(c.AttributeName) ? row[c.AttributeName] : null);
            actual = actual switch { OptionSetValue o => o.Value, EntityReference er => er.Id, _ => actual };
            return c.Operator switch
            {
                ConditionOperator.Equal => Equals(actual, c.Values[0]),
                ConditionOperator.In => c.Values.Any(v => Equals(actual, v)),
                ConditionOperator.Null => actual is null,
                ConditionOperator.NotNull => actual is not null,
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }

        public DataverseUserResponse Answer(string path)
        {
            lock (Requests) Requests.Add(path);
            if (path == "WhoAmI()")
            {
                return ActingAs is { } me
                    ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { UserId = me.ToString("D") }))
                    : DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired, "no user");
            }

            var me2 = ActingAs!.Value;
            foreach (var (prefix, fault) in PathFaults)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                return fault switch
                {
                    "500" => DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom"),
                    "401" => DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "token expired"),
                    "403" => DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied"),
                    "unparseable" => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { rows = 1 })),
                    "throw" => throw new TimeoutException("read timed out"),
                    _ => throw new ArgumentOutOfRangeException(nameof(path), fault, "unknown fault"),
                };
            }

            if (path.StartsWith("sprk_signals?fetchXml=", StringComparison.Ordinal))
            {
                if (TableDenied.TryGetValue(me2, out var denied) && denied.Contains("sprk_signal"))
                    return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied");
                var signalFetch = XDocument.Parse(Uri.UnescapeDataString(path["sprk_signals?fetchXml=".Length..]));
                // Dataverse refuses the whole query (0x80040220) when the caller lacks Read on a linked table (D-185).
                if (CallerHasNoPolicyRead && signalFetch.Descendants("link-entity").Any(l => (string?)l.Attribute("name") == "sprk_policy"))
                    return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "0x80040220");
                return SignalPage(signalFetch, me2);
            }

            if (path.StartsWith("sprk_decisionrecords?fetchXml=", StringComparison.Ordinal))
            {
                if (FaultingTables.Contains("sprk_decisionrecord")) return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom");
                if (UnauthorizedTables.Contains("sprk_decisionrecord")) return DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "token expired");
                if (TableDenied.TryGetValue(me2, out var drDenied) && drDenied.Contains("sprk_decisionrecord"))
                    return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied");
                var drFetch = XDocument.Parse(Uri.UnescapeDataString(path["sprk_decisionrecords?fetchXml=".Length..]));
                var wanted = drFetch.Descendants("value").Select(v => Guid.Parse(v.Value)).ToHashSet();
                // Like Dataverse, evaluate the createdby condition, and return the confirming user's name only through the outer link to
                // systemuser on sprk_confirmedby (so a link on any other column returns no name).
                var createdByCondition = drFetch.Descendants("condition").FirstOrDefault(c => (string?)c.Attribute("attribute") == "createdby" && (string?)c.Attribute("operator") == "eq");
                var createdBy = createdByCondition is null ? (Guid?)null : Guid.Parse((string)createdByCondition.Attribute("value")!);
                var nameLinked = drFetch.Descendants("link-entity").Any(l => (string?)l.Attribute("name") == "systemuser"
                    && (string?)l.Attribute("to") == "sprk_confirmedby" && (string?)l.Attribute("link-type") == "outer" && (string?)l.Attribute("alias") == "by");
                var found = DecisionRecords
                    .Where(r => wanted.Contains(r.Key) && r.Value.Readers.Contains(me2))
                    .Where(r => createdBy is null || DecisionRecordCreator[r.Key] == createdBy)
                    .Select(r => nameLinked ? r.Value.Row : r.Value.Row.Where(kv => kv.Key != "by.fullname").ToDictionary(kv => kv.Key, kv => kv.Value))
                    .ToList();
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = found }));
            }

            var set = path[..path.IndexOf('?')];
            var table = set[..^1];
            if (FaultingTables.Contains(table)) return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom");
            if (ThrowingTables.Contains(table)) throw new TimeoutException("witness read timed out");
            if (UnauthorizedTables.Contains(table)) return DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "token expired");
            if (TableDenied.TryGetValue(me2, out var d2) && d2.Contains(table))
                return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied");

            if (table == "sprk_invoicesubmission")
            {
                // D-176: like Dataverse, evaluate the tracking-number In list (a JSON array, text compared without regard to case) and,
                // when present, the vendor-org In list. A doubled-quote literal would be a 400 on Dataverse, so it is a 400 here.
                var odata = Uri.UnescapeDataString(path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..]);
                if (odata.Contains("''", StringComparison.Ordinal))
                    return DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.ServiceError, "Invalid JSON. A comma character ',' was expected in scope 'Array'.");
                var marker = "In(PropertyName='sprk_trackingnumber',PropertyValues=";
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(odata[(odata.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]));
                using var numberList = JsonDocument.ParseValue(ref reader);
                var numbers = numberList.RootElement.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var orgList = Regex.Match(odata, @"In\(PropertyName='sprk_vendororg',PropertyValues=\[([^\]]*)\]\)");
                var orgs = orgList.Success
                    ? Regex.Matches(orgList.Groups[1].Value, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value)).ToHashSet()
                    : null;
                var columns = path[(path.IndexOf("$select=", StringComparison.Ordinal) + 8)..path.IndexOf("&$filter", StringComparison.Ordinal)].Split(',');
                var submissions = Records.Where(r => r.Value.Table == table && r.Value.Readers.Contains(me2))
                    .Where(r => orgs is null || (r.Value.Columns["_sprk_vendororg_value"] is Guid g && orgs.Contains(g)))
                    .Where(r => r.Value.Columns["sprk_trackingnumber"] is string n && numbers.Contains(n))
                    .Select(r => columns.ToDictionary(c => c, c => c == $"{table}id" ? r.Key : r.Value.Columns.GetValueOrDefault(c)))
                    .ToList();
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = submissions }));
            }

            var filter = path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..];
            var inPart = filter[..(filter.IndexOf("])", StringComparison.Ordinal) + 2)];
            var rest = filter[inPart.Length..];
            var ids = Regex.Matches(inPart, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value)).ToHashSet();
            var mine = Regex.Matches(rest, @"(_\w+_value) eq ([0-9a-f-]{36})").Select(m => (m.Groups[1].Value, Guid.Parse(m.Groups[2].Value))).ToList();
            var flagOnly = rest.Contains("sprk_highpriority eq true", StringComparison.Ordinal);
            var select = path[(path.IndexOf("$select=", StringComparison.Ordinal) + 8)..path.IndexOf("&$filter", StringComparison.Ordinal)].Split(',');

            var rows = Records.Where(r => r.Value.Table == table && ids.Contains(r.Key) && r.Value.Readers.Contains(me2))
                .Where(r => mine.Count == 0 || mine.Any(c => r.Value.Columns.TryGetValue(c.Item1, out var v) && v is Guid g && g == c.Item2))
                .Where(r => !flagOnly || (r.Value.Columns.TryGetValue("sprk_highpriority", out var hp) && hp is true))
                .Select(r => select.ToDictionary(c => c, c => c == $"{table}id" ? r.Key : r.Value.Columns.GetValueOrDefault(c)))
                .ToList();
            return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows }));
        }

        private static bool StatusAllowed(XDocument fetch, Dictionary<string, object?> row)
        {
            var condition = fetch.Descendants("condition").FirstOrDefault(c => (string?)c.Attribute("attribute") == "sprk_signalstatus");
            if (condition is null || row["sprk_signalstatus"] is not int status) return true;
            return (string?)condition.Attribute("operator") switch
            {
                "in" => condition.Elements("value").Any(v => int.Parse(v.Value) == status),
                "eq" => int.Parse((string)condition.Attribute("value")!) == status,
                _ => true,
            };
        }

        private DataverseUserResponse SignalPage(XDocument fetch, Guid me)
        {
            var text = fetch.ToString(SaveOptions.DisableFormatting).Replace('\'', '"');
            var set = text.Contains("next-x-years") ? "suppressed"
                : text.Contains("last-x-hours\" value=\"26\"") ? "closedToday"
                : text.Contains("last-x-hours\" value=\"720\"") ? "closedEarlier"
                : text.Contains("last-x-days\" value=\"90\"") ? "decided"
                : text.Contains("\"sprk_lane\" operator=\"eq\" value=\"100000000\"") ? "decide"
                : text.Contains("\"sprk_lane\" operator=\"eq\" value=\"100000001\"") ? "do"
                : throw new InvalidOperationException("unrecognised worklist query");
            var all = Signals.Where(s => s.Sets.Contains(set))
                .Where(s => !SignalReaders.TryGetValue(s.Id, out var readers) || readers.Contains(me))
                .Select(s => s.Row)
                // Task 045: the Decided query asks for Signals that HAVE a Decision Record, like Dataverse evaluating not-null.
                .Where(r => set != "decided" || r["_sprk_decisionrecord_value"] is not null)
                // ...and evaluates the status condition the query carries (in (Acknowledged, Resolved) for decided, eq Resolved for the closed sets).
                .Where(r => StatusAllowed(fetch, r)).ToList();
            var page = int.TryParse(fetch.Root!.Attribute("page")?.Value, out var p) ? p : 1;
            if (DenySignalPagesFrom > 0 && page >= DenySignalPagesFrom)
            {
                return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied");
            }

            var slice = all.Skip((page - 1) * PageSize).Take(PageSize).ToList();
            // D-185: like Dataverse, the link-entity's columns (policy.*) come back only when the query carries the link.
            if (!text.Contains("alias=\"policy\"", StringComparison.Ordinal))
            {
                slice = slice.Select(r => r.Where(kv => !kv.Key.StartsWith("policy.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();
            }

            // Task 154: like Dataverse, a column comes back only when the query selects it (the route runs the FetchXML verbatim).
            if (!text.Contains("<attribute name=\"sprk_sentence\" />", StringComparison.Ordinal))
            {
                slice = slice.Select(r => r.Where(kv => kv.Key != "sprk_sentence").ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();
            }
            // Task 045: the Decision Record lookup is returned only when the query selects it (the route adds it to the closed sets).
            if (!text.Contains("<attribute name=\"sprk_decisionrecord\" />", StringComparison.Ordinal))
            {
                slice = slice.Select(r => r.Where(kv => kv.Key != "_sprk_decisionrecord_value").ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();
            }

            var body = new Dictionary<string, object?> { ["value"] = slice };
            if (all.Count > page * PageSize)
            {
                body["@Microsoft.Dynamics.CRM.fetchxmlpagingcookie"] =
                    $"<cookie pagenumber=\"{page + 1}\" pagingcookie=\"{Uri.EscapeDataString(Uri.EscapeDataString($"<cookie page=\"{page}\"></cookie>"))}\" istracking=\"False\" />";
            }

            return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));
        }
    }

    private sealed class FakeUserClient(WorklistFactory factory) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(factory.World.Answer(relativePath));

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read route never writes.");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read route never writes.");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read route never writes.");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read route never writes.");
    }

    private sealed class FakeIdentity(WorklistFactory factory) : IIdentityNormalizationService
    {
        public Task<PersonIdentity> ResolveAsync(Guid systemUserId, CancellationToken ct)
        {
            if (factory.World.ContactThrows) throw new TimeoutException("contact read timed out");
            return Task.FromResult(factory.World.ContactUnreadable
                // As IdentityNormalizationService reports a contact-binding read that failed: no contact, and the fault flag.
                ? new PersonIdentity(systemUserId) { Faults = IdentityReadFaults.ContactBinding }
                : new PersonIdentity(systemUserId, World.Contacts.TryGetValue(systemUserId.ToString(), out var c) ? c : null));
        }

        public Task InvalidateAsync(Guid systemUserId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeCallerResolver(WorklistFactory factory) : ICallerSystemUserResolver
    {
        public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct)
        {
            var world = factory.World;
            if (world.CallerResolverThrows) throw new TimeoutException("caller resolution timed out");
            if (world.CallerUnresolved)
                return Task.FromResult(new CallerSystemUserResolution { IsResolved = false, UnresolvedReason = world.CallerUnresolvedReason });
            return Task.FromResult(new CallerSystemUserResolution
            {
                IsResolved = true,
                SystemUserId = world.CallerResolvedToNonGuid ? "not-a-guid" : (world.ActingAs ?? Guid.Empty).ToString("D"),
            });
        }
    }

    private sealed class FakeSystemUsers(WorklistFactory factory) : ISystemUserIdentityResolver
    {
        public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default) => Task.FromResult<Guid?>(null);

        public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default)
        {
            if (factory.World.ExternalFlagReadThrows) throw new TimeoutException("external flag read timed out");
            return Task.FromResult(factory.World.CallerIsExternal);
        }
    }

    private sealed class WorldClock(WorklistFactory factory) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => factory.World.Now;
    }

    /// <summary>Every Warning the host logs, for the tests that pin a fallback's warning.</summary>
    internal sealed class LogCapture : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Warnings { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning) owner.Warnings.Enqueue(formatter(state, exception));
            }
        }
    }

    public sealed class WorklistFactory : DataverseIntegrationTestFixture
    {
        internal World World { get; set; } = new();

        internal LogCapture Logs { get; } = new();

        internal static string RepoRoot { get; } = FindRepoRoot();

        internal static string ConfigPath => Path.Combine(RepoRoot, "config", "worklist.gridconfiguration.json");

        public WorklistFactory()
        {
            var config = File.ReadAllText(ConfigPath);
            DataverseServiceMock
                .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
                {
                    "sprk_gridconfiguration" => new EntityCollection([new Entity("sprk_gridconfiguration", SignalReadService.WorklistConfigurationId)
                    {
                        ["sprk_configjson"] = World.ConfigJson ?? config, ["statecode"] = new OptionSetValue(0),
                    }]),
                    "sprk_recordtype_ref" => new EntityCollection(q.Criteria.Conditions[0].Values.Cast<Guid>()
                        .Where(World.Catalog.ContainsKey)
                        .Select(id => new Entity("sprk_recordtype_ref", id)
                        {
                            ["sprk_recordlogicalname"] = World.Catalog[id].Logical,
                            ["sprk_recorddisplayname"] = World.Catalog[id].Label,
                            ["sprk_recorddisplaynamefield"] = World.Catalog[id].Display,
                            ["sprk_regardingrecordnumberfield"] = World.Catalog[id].Number,
                        }).ToList()),
                    _ => World.AppOnly(q),
                });
            DataverseServiceMock
                .Setup(s => s.RetrieveAsync("usersettings", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid user, string[] _, CancellationToken _) =>
                    World.ZoneCodeOfUser.TryGetValue(user, out var code) ? new Entity("usersettings", user) { ["timezonecode"] = code } : null!);
            DataverseServiceMock
                .Setup(s => s.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logical, CancellationToken _) => logical + "s");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataverseUserClient>();
                services.AddScoped<IDataverseUserClient>(_ => new FakeUserClient(this));
                services.RemoveAll<IIdentityNormalizationService>();
                services.AddSingleton<IIdentityNormalizationService>(_ => new FakeIdentity(this));
                services.RemoveAll<ICallerSystemUserResolver>();
                services.AddScoped<ICallerSystemUserResolver>(_ => new FakeCallerResolver(this));
                services.RemoveAll<ISystemUserIdentityResolver>();
                services.AddSingleton<ISystemUserIdentityResolver>(_ => new FakeSystemUsers(this));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new WorldClock(this));
                // The writer's own identity (WhoAmI on its dedicated connection): the createdby of every genuine Decision Record.
                services.RemoveAll<OntologyWriterDataverseClient>();
                services.AddSingleton(_ =>
                {
                    var org = new Mock<IOrganizationServiceAsync2>();
                    org.Setup(o => o.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new Microsoft.Crm.Sdk.Messages.WhoAmIResponse { Results = { ["UserId"] = WriterUser } });
                    return new OntologyWriterDataverseClient(() => org.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<OntologyWriterDataverseClient>.Instance);
                });
                services.AddSingleton<ILoggerProvider>(Logs);
            });
        }

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "worklist.gridconfiguration.json")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Repository root (config/worklist.gridconfiguration.json) not found.");
        }
    }
}
