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
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-097-ontology-r1")]
public class EventReadPathTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid MatterTypeRefId = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");

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
            new DataverseCreateEventRequest { Name = "Filing", Priority = EventPriority.High });

        payload["sprk_priority"].Should().Be(100000002);
        DocumentedPriorities().Values.Should().Contain((int)payload["sprk_priority"]!);
    }

    // ── Regarding: the record type is a LOOKUP; binds use navigation properties and entity sets ───────────────

    [Fact]
    public void CreatePayload_Regarding_NeverWritesTheRecordTypeAsAnInt_AndBindsTheLookupByNavProp()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(new DataverseCreateEventRequest
        {
            Name = "Filing",
            RegardingRecordType = RegardingRecordType.Matter,
            RegardingRecordId = MatterId.ToString(),
            RegardingRecordName = "Matter A",
        });

        payload.Keys.Should().NotContain("sprk_regardingrecordtype",
            "it is a lookup to sprk_recordtype_ref — Dataverse rejected the int with 'property does not exist'");
        payload.Should().ContainKey("sprk_RegardingMatter@odata.bind")
            .WhoseValue.Should().Be($"/sprk_matters({MatterId:D})");
        payload.Keys.Should().NotContain(k => k.StartsWith("sprk_regardingmatter@", StringComparison.Ordinal),
            "an @odata.bind key must be the case-sensitive navigation property");
    }

    [Fact]
    public void CreatePayload_Regarding_BindsTheRecordTypeCatalogRow_WhenResolved()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(new DataverseCreateEventRequest
        {
            Name = "Filing",
            RegardingRecordType = RegardingRecordType.Matter,
            RegardingRecordId = MatterId.ToString(),
        }, MatterTypeRefId);

        payload["sprk_RegardingRecordType@odata.bind"].Should().Be($"/sprk_recordtype_refs({MatterTypeRefId:D})");
    }

    [Theory]
    [InlineData(RegardingRecordType.Project, "sprk_regardingproject", "sprk_projects")]
    [InlineData(RegardingRecordType.Matter, "sprk_regardingmatter", "sprk_matters")]
    [InlineData(RegardingRecordType.Invoice, "sprk_regardinginvoice", "sprk_invoices")]
    [InlineData(RegardingRecordType.Analysis, "sprk_regardinganalysis", "sprk_analysises")] // not sprk_analysiss
    [InlineData(RegardingRecordType.Account, "sprk_regardingaccount", "accounts")]
    [InlineData(RegardingRecordType.Contact, "sprk_regardingcontact", "contacts")]
    [InlineData(RegardingRecordType.WorkAssignment, "sprk_regardingworkassignment", "sprk_workassignments")]
    [InlineData(RegardingRecordType.Budget, "sprk_regardingbudget", "sprk_budgets")]
    public void RegardingMaps_UseTheDocumentedNavPropAndTheLiveEntitySet(int type, string column, string entitySet)
    {
        var doc = DocumentedColumns();

        RegardingRecordType.GetLookupFieldName(type).Should().Be(column);
        doc.Should().ContainKey(column);
        RegardingRecordType.GetNavigationPropertyName(type).Should().Be(doc[column],
            "for these lookups the navigation property is the attribute SchemaName (verified live)");
        RegardingRecordType.GetEntitySetName(type).Should().Be(entitySet);
        doc[RegardingRecordType.RecordTypeNavigationProperty.ToLowerInvariant()]
            .Should().Be(RegardingRecordType.RecordTypeNavigationProperty);
    }

    [Fact]
    public void UpdatePayload_Regarding_BindsTheNewParent_AndClearsTheOtherMappedLookups()
    {
        var payload = DataverseWebApiService.BuildUpdateEventPayload(new DataverseUpdateEventRequest
        {
            RegardingRecordType = RegardingRecordType.Matter,
            RegardingRecordId = MatterId.ToString(),
            RegardingRecordName = "Matter A",
        }, MatterTypeRefId);

        payload["sprk_RegardingMatter@odata.bind"].Should().Be($"/sprk_matters({MatterId:D})");
        payload["sprk_RegardingRecordType@odata.bind"].Should().Be($"/sprk_recordtype_refs({MatterTypeRefId:D})");
        payload["sprk_RegardingProject@odata.bind"].Should().BeNull();
        payload.Should().ContainKey("sprk_RegardingProject@odata.bind", "ADR-024: at most one specific lookup is set");
        payload.Keys.Should().NotContain("sprk_regardingrecordtype");
    }

    // ── Read URLs: every column exists, the expand uses the navigation property, no $skip ───────────────────

    [Fact]
    public void QueryUrl_SelectsOnlyLiveColumns_ExpandsByNavProp_AndNeverSendsSkip()
    {
        var url = DataverseWebApiService.BuildQueryEventsUrl(
            RegardingRecordType.Matter, null, null, EventStatusCode.Open, EventPriority.High,
            null, null, skip: 50, top: 50, ownerUserId: null);

        AssertLiveColumns(url);
        url.Should().NotContain("$skip", "Dataverse: 'Skip Clause is not supported in CRM'");
        url.Should().Contain("$top=100", "skip + top rows are fetched and the page is cut in memory");
        url.Should().Contain("_sprk_regardingmatter_value ne null");
        url.Should().NotContain("sprk_regardingrecordtype eq");
        url.Should().Contain("sprk_priority eq 100000002");
    }

    [Fact]
    public void GetUrl_SelectsOnlyLiveColumns_AndExpandsByNavProp()
    {
        var url = DataverseWebApiService.BuildGetEventUrl(Guid.NewGuid());

        AssertLiveColumns(url);
        url.Should().NotContain("_sprk_relatedevent_value");
    }

    [Fact]
    public void Mapper_ReadsTheExpandedEventType_AndDerivesTheRegardingType()
    {
        var row = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>($$"""
            {
              "sprk_eventid": "{{Guid.NewGuid()}}",
              "sprk_eventname": "Filing",
              "statecode": 0, "statuscode": 659490001, "sprk_priority": 100000002,
              "_sprk_regardingmatter_value": "{{MatterId}}",
              "_sprk_regardingrecordtype_value": "{{MatterTypeRefId}}",
              "sprk_EventType_Ref": { "sprk_name": "Task" }
            }
            """)!;

        var entity = DataverseWebApiService.MapToEventEntity(row);

        entity.EventTypeName.Should().Be("Task");
        entity.RegardingRecordType.Should().Be(RegardingRecordType.Matter);
        entity.RegardingMatterId.Should().Be(MatterId);
        entity.Priority.Should().Be(100000002);
    }

    // ── External SPA events: sprk_eventname / sprk_eventstatus, never sprk_name / sprk_status ────────────────

    [Fact]
    public void ExternalEventsQuery_SelectsOnlyLiveColumns()
    {
        var query = ExternalDataService.BuildEventsQuery(Guid.NewGuid());

        var select = Regex.Match(query, @"\$select=([^&]+)").Groups[1].Value.Split(',');
        select.Should().Contain("sprk_eventname").And.Contain("sprk_eventstatus");
        select.Should().NotContain("sprk_name").And.NotContain("sprk_status");
        select.Select(Column).Should().OnlyContain(c => DocumentedColumns().ContainsKey(c));
    }

    [Fact]
    public void ExternalCreateEventBody_WritesTheLiveColumns()
    {
        var projectId = Guid.NewGuid();
        var body = ExternalDataService.BuildCreateEventBody(projectId,
            new CreateExternalEventRequest { SprkName = "Kick-off", SprkDuedate = "2026-10-20", SprkStatus = 1 });

        body["sprk_eventname"].Should().Be("Kick-off");
        body["sprk_eventstatus"].Should().Be(1);
        body.Keys.Should().NotContain("sprk_name").And.NotContain("sprk_status");
        body["sprk_RegardingProject@odata.bind"].Should().Be($"/sprk_projects({projectId})");
        body.Keys.Where(k => !k.Contains('@')).Should().OnlyContain(k => DocumentedColumns().ContainsKey(k));
        DocumentedColumns()["sprk_regardingproject"].Should().Be("sprk_RegardingProject");
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
    private static Dictionary<string, string> DocumentedColumns() =>
        File.ReadLines(DocPath())
            .Select(l => Regex.Match(l, @"^\|\s*Event\s*\|\s*sprk_event\s*\|\s*([a-z0-9_]+)\s*\|\s*([A-Za-z0-9_]+)\s*\|"))
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
