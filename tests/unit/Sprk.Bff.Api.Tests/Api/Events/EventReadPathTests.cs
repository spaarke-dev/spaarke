// spaarke-ontology-platform-r1 task 097, round 2 (2026-10-05) — the rest of the sprk_event Web API surface.
//
// Reproduced live against spaarkedev1 before the fix:
//   - GET sprk_events / GET sprk_events({id}) as the BFF built them → HTTP 400 ("Could not find a property named
//     'sprk_eventtype_ref'", then 'sprk_regardingrecordtype', '_sprk_relatedevent_value'; "Skip Clause is not
//     supported in CRM"). Every events route that reads first (list, by id, PUT, complete, cancel, DELETE) failed;
//   - sprk_priority = 0..3 → 400 "outside the valid range" (live: 100000000..100000003);
//   - sprk_regardingrecordtype written as an int → 400 (it is a LOOKUP to sprk_recordtype_ref);
//   - the external SPA's events read/create used sprk_name / sprk_status, which sprk_event does not have → 400.
//
// ADR-038 rule 1: every column, navigation property and option value asserted here is checked against
// docs/data-model/sprk_event-related-tables.md, whose sprk_event attribute set was re-synced with the live metadata on
// 2026-10-05 (stale rows removed, missing live columns added).

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-097-ontology-r1")]
public class EventReadPathTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid MatterTypeRefId = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid TeamId = Guid.Parse("0f0f0f0f-0097-4097-8097-000000000097");

    // ── Priority: one source of truth, equal to the live option set ───────────────────────────────────────────

    [Fact]
    public void EventPriority_Constants_MatchTheLiveVerifiedPriorityRow()
    {
        // Reflection over every type named EventPriority the BFF ships, so this compiles against any revision — on
        // master it finds the 0..3 constants and fails.
        var types = new[] { typeof(EventEntity).Assembly, typeof(Program).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "EventPriority" && t.IsAbstract && t.IsSealed)
            .ToList();

        types.Should().ContainSingle("one source of truth for sprk_event priority");
        var constants = types[0].GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);

        constants.Should().BeEquivalentTo(DocumentedPriorities(),
            "the BFF writes these values; any other is rejected by Dataverse");
    }

    [Fact]
    public void CreatePayload_WritesTheLivePriorityValue()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Filing", Priority = EventPriority.High, OwningTeamId = TeamId });

        payload["sprk_priority"].Should().Be(100000002);
        DocumentedPriorities().Values.Should().Contain((int)payload["sprk_priority"]!);
    }

    // ── Read URLs: every column exists, the expand uses the navigation property, no $skip ───────────────────

    [Fact]
    public void QueryUrl_SelectsOnlyLiveColumns_ExpandsByNavProp_AndNeverSendsSkip()
    {
        var url = DataverseWebApiService.BuildEventQueryUrl(
            RegardingRecordType.Matter, null, MatterTypeRefId, null, EventStatusCode.Open, EventPriority.High,
            null, null, skip: 50, top: 50, mine: null);

        AssertLiveColumns(url);
        url.Should().NotContain("$skip", "Dataverse: 'Skip Clause is not supported in CRM'");
        url.Should().Contain("$top=100", "skip + top rows are fetched and the page is cut in memory");
        url.Should().Contain("sprk_eventid asc", "review F4: a stable tiebreaker so in-memory pages never overlap or skip rows");
        url.Should().Contain($"_sprk_regardingrecordtype_value eq {MatterTypeRefId:D}");
        url.Should().Contain("sprk_priority eq 100000002");
    }

    // ── "My events" (OWNER DECISION B, 2026-10-06): owner OR assigned contact OR sprk_createdbyperson ──────────

    [Fact]
    public void QueryUrl_MyEvents_IsOwnerOrAssignedContactOrCreatedByPerson()
    {
        var caller = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var url = DataverseWebApiService.BuildEventQueryUrl(
            null, null, null, null, null, null, null, null, skip: 0, top: 50,
            mine: Sprk.Bff.Api.Api.Events.EventEndpoints.MyEventsScope(caller, contact));

        Uri.UnescapeDataString(url).Should().Contain(
            $"(_ownerid_value eq {caller:D} or _sprk_assignedto_value eq {contact:D} or _sprk_createdbyperson_value eq {caller:D})");
        DocumentedColumns().Should().ContainKey("sprk_assignedto");
    }

    [Fact]
    public void QueryUrl_MyEvents_ACallerWithNoLinkedContact_IsOwnerOrCreatedByPerson()
    {
        var caller = Guid.NewGuid();
        var url = DataverseWebApiService.BuildEventQueryUrl(
            null, null, null, null, null, null, null, null, skip: 0, top: 50,
            mine: Sprk.Bff.Api.Api.Events.EventEndpoints.MyEventsScope(caller, null));

        url.Should().Contain($"(_ownerid_value eq {caller:D} or _sprk_createdbyperson_value eq {caller:D})");
        url.Should().NotContain("_sprk_assignedto_value");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("assigned")]
    [InlineData("createdby")]
    public void MyEvents_EachBranchAloneSelectsTheRow(string branch)
    {
        // A TEAM-owned event the BFF created for the caller (owner = team, created-by person = caller) is "mine" through
        // the created-by branch; an event assigned to the caller's contact through the assigned branch; a row the
        // caller owns through the owner branch. Each branch is evaluated on its own row.
        var caller = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var team = Guid.NewGuid();
        var row = branch switch
        {
            "owner" => (Owner: caller, Assigned: (Guid?)null, CreatedBy: (Guid?)null),
            "assigned" => (Owner: team, Assigned: contact, CreatedBy: (Guid?)null),
            _ => (Owner: team, Assigned: (Guid?)null, CreatedBy: (Guid?)caller),
        };

        var filter = Sprk.Bff.Api.Api.Events.EventEndpoints.MyEventsScope(caller, contact)
            .ToFilter(System.Globalization.CultureInfo.InvariantCulture)!;

        EvaluateMine(filter, row.Owner, row.Assigned, row.CreatedBy).Should().BeTrue();
        EvaluateMine(filter, team, null, null).Should().BeFalse("a team-owned event nobody assigned to or created as the caller is not theirs");
    }

    /// <summary>Evaluates the three-way OR clause against one row the way Dataverse would.</summary>
    private static bool EvaluateMine(string filter, Guid owner, Guid? assigned, Guid? createdBy) =>
        Regex.Matches(filter, @"(_[a-z_]+_value) eq ([0-9a-f-]{36})").Any(m => m.Groups[1].Value switch
        {
            "_ownerid_value" => owner == Guid.Parse(m.Groups[2].Value),
            "_sprk_assignedto_value" => assigned == Guid.Parse(m.Groups[2].Value),
            "_sprk_createdbyperson_value" => createdBy == Guid.Parse(m.Groups[2].Value),
            _ => false,
        });

    // ── External SPA events: sprk_eventname / statuscode (review F2), never sprk_name / sprk_status ─────────

    [Fact]
    public void ExternalEventsQuery_SelectsOnlyLiveColumns_AndStatuscodeAsTheStatusOfRecord()
    {
        var query = ExternalDataService.BuildEventsQuery(Guid.NewGuid());

        var select = Regex.Match(query, @"\$select=([^&]+)").Groups[1].Value.Split(',');
        select.Should().Contain("sprk_eventname").And.Contain("statuscode");
        select.Should().NotContain("sprk_name").And.NotContain("sprk_status").And.NotContain("sprk_eventstatus",
            "review F2: statuscode is the status of record (what /complete writes), not sprk_eventstatus");
        select.Select(Column).Should().OnlyContain(c => DocumentedColumns().ContainsKey(c));
    }

    [Fact]
    public void ExternalCreateEventBody_WritesTheLiveColumns_AndDefaultsToOpen()
    {
        var projectId = Guid.NewGuid();
        var body = ExternalDataService.BuildEventCreatePayload(projectId,
            new CreateExternalEventRequest { SprkName = "Kick-off", SprkDuedate = "2026-10-20" }, TeamId);

        body["sprk_eventname"].Should().Be("Kick-off");
        body["statuscode"].Should().Be(EventStatusCode.Open, "omitted ⇒ Open, like POST /api/v1/events");
        body["statecode"].Should().Be(0);
        body.Keys.Should().NotContain("sprk_name").And.NotContain("sprk_status").And.NotContain("sprk_eventstatus");
        body["sprk_RegardingProject@odata.bind"].Should().Be($"/sprk_projects({projectId})");
        body["ownerid@odata.bind"].Should().Be($"/teams({TeamId})", "task 146: the project's team owns it");
        body.Keys.Where(k => !k.Contains('@')).Should().OnlyContain(k => DocumentedColumns().ContainsKey(k));
        DocumentedColumns()["sprk_regardingproject"].Should().Be("sprk_RegardingProject");
    }

    [Theory]
    [InlineData(1)]          // Draft — allowed
    [InlineData(659490001)]  // Open — allowed
    public void ExternalCreateEventBody_AcceptsDraftOrOpen(int status)
    {
        var body = ExternalDataService.BuildEventCreatePayload(Guid.NewGuid(),
            new CreateExternalEventRequest { SprkName = "Kick-off", SprkStatus = status }, TeamId);

        body["statuscode"].Should().Be(status);
        body["statecode"].Should().Be(0);
    }

    [Theory]
    [InlineData(2)]          // No Further Action
    [InlineData(5)]          // fictional
    [InlineData(659490002)]  // Completed
    [InlineData(659490004)]  // Cancelled
    public void ExternalCreateEventBody_RefusesAnyOtherStatus(int status)
    {
        // Review F9: an external caller must not be able to write an arbitrary status integer.
        var act = () => ExternalDataService.BuildEventCreatePayload(Guid.NewGuid(),
            new CreateExternalEventRequest { SprkName = "Kick-off", SprkStatus = status }, TeamId);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    // ── Paging (review F4) and server-side status exclusion (review F5) ─────────────────────────────────────

    [Fact]
    public void QueryUrl_BeyondTheServableWindow_IsRefused_NeverAnEmptyPage()
    {
        var act = () => DataverseWebApiService.BuildEventQueryUrl(
            null, null, null, null, null, null, null, null, skip: 4990, top: 50, mine: null);

        act.Should().Throw<ArgumentOutOfRangeException>();
        DataverseWebApiService.BuildEventQueryUrl(null, null, null, null, null, null, null, null, skip: 4950, top: 50, mine: null)
            .Should().Contain("$top=5000");
    }

    [Fact]
    public void QueryUrl_ExcludedStatuses_AreFilteredInTheQuery()
    {
        var url = DataverseWebApiService.BuildEventQueryUrl(
            null, null, null, null, null, null, null, new DateTime(2026, 10, 4), skip: 0, top: 100, mine: null,
            excludeStatusCodes: new[] { EventStatusCode.Completed, EventStatusCode.Cancelled });

        url.Should().Contain("statuscode ne 659490002").And.Contain("statuscode ne 659490004");
        AssertLiveColumns(url);
    }

    // ── sprk_eventlog (review F1): only live columns, description folded into the name ───────────────────────

    [Fact]
    public void EventLogPayload_WritesOnlyLiveSprkEventLogColumns_WithTheDescriptionInTheName()
    {
        var eventId = Guid.NewGuid();
        var payload = DataverseWebApiService.BuildCreateEventLogPayload(
            eventId, EventLogAction.Completed, "Status changed from Open to Completed", new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

        var doc = DocumentedColumns("sprk_eventlog");
        doc.Should().NotContainKey("sprk_description", "live sprk_eventlog has no description column");
        payload.Keys.Where(k => !k.Contains('@')).Should().OnlyContain(k => doc.ContainsKey(k));
        payload.Should().ContainKey("sprk_Event@odata.bind").WhoseValue.Should().Be($"/sprk_events({eventId})");
        doc["sprk_event"].Should().Be("sprk_Event");
        payload["sprk_action"].Should().Be(2);
        ((string)payload["sprk_eventlogname"]!).Should().Be("Completed - 2026-10-05 12:00:00 UTC - Status changed from Open to Completed");
    }

    [Fact]
    public void EventLogPayload_ALongDescription_IsCutToTheLiveColumnLength()
    {
        var payload = DataverseWebApiService.BuildCreateEventLogPayload(
            Guid.NewGuid(), EventLogAction.Updated, new string('x', 2000), DateTime.UtcNow);

        ((string)payload["sprk_eventlogname"]!).Length.Should().Be(850);
    }

    [Fact]
    public void EventLogActions_MatchTheLiveOptionSet()
    {
        var row = File.ReadLines(DocPath()).Single(l => Regex.IsMatch(l, @"\|\s*sprk_eventlog\s*\|\s*sprk_action\s*\|"));
        var documented = Regex.Matches(row, @"(\d): ([A-Za-z]+)").ToDictionary(m => m.Groups[2].Value, m => int.Parse(m.Groups[1].Value));

        documented.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["Created"] = EventLogAction.Created, ["Updated"] = EventLogAction.Updated, ["Completed"] = EventLogAction.Completed,
            ["Cancelled"] = EventLogAction.Cancelled, ["Deleted"] = EventLogAction.Deleted,
        });
    }
    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static void AssertLiveColumns(string url)
    {
        var doc = DocumentedColumns();
        var select = Regex.Match(url, @"\$select=([^&]+)").Groups[1].Value.Split(',');
        select.Should().NotBeEmpty();
        select.Select(Column).Should().OnlyContain(c => doc.ContainsKey(c),
            "a column sprk_event does not have makes Dataverse answer 400 for the whole request");

        var expand = Regex.Match(url, @"\$expand=([A-Za-z_]+)\(").Groups[1].Value;
        expand.Should().Be(doc["sprk_eventtype_ref"], "the expand must use the case-sensitive navigation property");
    }

    /// <summary>`_x_value` → `x`.</summary>
    private static string Column(string selectItem) =>
        selectItem.StartsWith('_') && selectItem.EndsWith("_value", StringComparison.Ordinal)
            ? selectItem[1..^"_value".Length]
            : selectItem;

    /// <summary>sprk_event logical name → schema name, from the live-verified data-model doc.</summary>
    private static Dictionary<string, string> DocumentedColumns(string entity = "sprk_event") =>
        File.ReadLines(DocPath())
            .Select(l => Regex.Match(l, $@"^\|[^|]*\|\s*{entity}\s*\|\s*([a-z0-9_]+)\s*\|\s*([A-Za-z0-9_]+)\s*\|"))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    /// <summary>sprk_event lookup columns → their documented targets.</summary>
    private static Dictionary<string, string> DocumentedLookups() =>
        File.ReadLines(DocPath())
            .Select(l => Regex.Match(l, @"^\|[^|]*\|\s*sprk_event\s*\|\s*([a-z0-9_]+)\s*\|[^|]*\|[^|]*\|\s*Lookup\s*\|.*Targets:<br><br>([a-z0-9_]+)"))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    /// <summary>Label → value from the doc's sprk_priority row.</summary>
    private static Dictionary<string, int> DocumentedPriorities()
    {
        var row = File.ReadLines(DocPath()).Single(l => Regex.IsMatch(l, @"\|\s*sprk_event\s*\|\s*sprk_priority\s*\|"));
        return Regex.Matches(row, @"(\d{9}): ([A-Za-z]+)")
            .ToDictionary(m => m.Groups[2].Value, m => int.Parse(m.Groups[1].Value));
    }

    private static string DocPath() => Path.Combine(RepoRoot(), "docs", "data-model", "sprk_event-related-tables.md");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
