// unified-access-control-r2 task 159 (#1098) — the sprk_event regarding WRITE and READ shape, asserted on the internal
// static builders (no HTTP double, ADR-038), plus the live-metadata pins the shape rests on.
//
// sprk_event.sprk_regardingrecordtype is a LOOKUP to sprk_recordtype_ref. Before this task the create payload (and the
// since-deleted update payload) wrote it as an integer, the create bound the typed lookup with its logical name as the navigation property
// and a pluralized set name, and the list filtered / selected the lookup by its logical name. Every name pinned below
// was read from live metadata (spaarkedev1, 2026-10-03; notes/task-159-events-authorization.md §0.2).

using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Events;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiEventStatusCode = Spaarke.Dataverse.EventStatusCode;

namespace Sprk.Bff.Api.Tests.Api.Events;

public class EventRegardingPayloadTests
{
    private static readonly Guid TargetId = Guid.Parse("A1B2C3D4-0000-4000-8000-000000000159");
    private static readonly Guid RecordTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid MatterStamp = Guid.Parse("b68299c6-bafb-f011-8407-7c1e520aa4df");
    // ── Live-metadata pins ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The 0-7 API types → (typed lookup, its live navigation property, its live entity set).</summary>
    public static TheoryData<int, string, string, string> RegardingTypes => new()
    {
        { 0, "sprk_regardingproject", "sprk_RegardingProject", "sprk_projects" },
        { 1, "sprk_regardingmatter", "sprk_RegardingMatter", "sprk_matters" },
        { 2, "sprk_regardinginvoice", "sprk_RegardingInvoice", "sprk_invoices" },
        { 3, "sprk_regardinganalysis", "sprk_RegardingAnalysis", "sprk_analysises" },
        { 4, "sprk_regardingaccount", "sprk_RegardingAccount", "accounts" },
        { 5, "sprk_regardingcontact", "sprk_RegardingContact", "contacts" },
        { 6, "sprk_regardingworkassignment", "sprk_RegardingWorkAssignment", "sprk_workassignments" },
        { 7, "sprk_regardingbudget", "sprk_RegardingBudget", "sprk_budgets" },
    };

    [Fact]
    public void LiveNames_TheEventRegardingFamily_AndItsNavigationProperties_ArePinned()
    {
        var expected = new Dictionary<string, string>
        {
            ["sprk_regardingaccount"] = "sprk_RegardingAccount",
            ["sprk_regardingagreement"] = "sprk_RegardingAgreement",
            ["sprk_regardinganalysis"] = "sprk_RegardingAnalysis",
            ["sprk_regardingbudget"] = "sprk_RegardingBudget",
            ["sprk_regardingcommunication"] = "sprk_RegardingCommunication",
            ["sprk_regardingcontact"] = "sprk_RegardingContact",
            ["sprk_regardingevent"] = "sprk_RegardingEvent",
            ["sprk_regardinginvoice"] = "sprk_RegardingInvoice",
            ["sprk_regardingmatter"] = "sprk_RegardingMatter",
            ["sprk_regardingorganization"] = "sprk_RegardingOrganization",
            ["sprk_regardingproject"] = "sprk_RegardingProject",
            ["sprk_regardingreportcard"] = "sprk_RegardingReportCard",
            ["sprk_regardingservicerequest"] = "sprk_RegardingServiceRequest",
            ["sprk_regardingworkassignment"] = "sprk_RegardingWorkAssignment",
        };

        foreach (var (lookup, navigation) in expected)
        {
            RegardingRecordType.GetEventNavigationProperty(lookup).Should().Be(navigation);
        }

        RegardingRecordType.EventRecordTypeNavigationProperty.Should().Be("sprk_RegardingRecordType");
        RegardingRecordType.RecordTypeRefEntitySet.Should().Be("sprk_recordtype_refs");
        EventEndpoints.EventEntitySet.Should().Be("sprk_events");
        EventEndpoints.CreateEventPrivilege.Should().Be("prvCreatesprk_Event");
    }

    [Theory]
    [InlineData(ApiEventStatusCode.Draft, 1, 0)]
    [InlineData(ApiEventStatusCode.Open, 659490001, 0)]
    [InlineData(ApiEventStatusCode.Completed, 659490002, 0)]
    [InlineData(ApiEventStatusCode.Closed, 659490003, 0)]
    [InlineData(ApiEventStatusCode.OnHold, 659490006, 0)]
    [InlineData(ApiEventStatusCode.Reassigned, 659490007, 0)]
    [InlineData(ApiEventStatusCode.NoFurtherAction, 2, 1)]
    [InlineData(ApiEventStatusCode.Cancelled, 659490004, 1)]
    [InlineData(ApiEventStatusCode.Transferred, 659490005, 1)]
    public void LiveNames_EventStatusCodes_AndTheStatecodeEachBelongsTo_ArePinned(int apiValue, int liveValue, int liveState)
    {
        apiValue.Should().Be(liveValue);
        Spaarke.Dataverse.EventStatusCode.GetStateCode(liveValue).Should().Be(liveState,
            "Dataverse refuses a statuscode written with a statecode it does not belong to (Completed is ACTIVE live)");
    }

    // ── Write shape: create (EventsCapability.BuildCreatePayload — the payload RecordCreationService receives) ────

    [Theory]
    [MemberData(nameof(RegardingTypes))]
    public void CreatePayload_BindsTheTypedLookupByTableAndId_AndTheRecordTypeAsALookup(
        int type, string lookup, string navigation, string entitySet)
    {
        var payload = Create(type, RecordTypeRef);

        payload[lookup].Should().BeEquivalentTo(Lookup(LogicalNameOf(type), TargetId));
        payload["sprk_regardingrecordtype"].Should().BeEquivalentTo(Lookup("sprk_recordtype_ref", RecordTypeRef));
        payload["sprk_regardingrecordid"].Should().Be(TargetId.ToString("D").ToLowerInvariant());
        payload["sprk_regardingrecordname"].Should().Be("Target");
        payload["sprk_regardingrecordurl"].Should().Be("/main.aspx?pagetype=entityrecord&etn=x&id=y");
        AssertNoNumericRecordTypeAndNoBindOrValueKeys(payload);
        payload.Keys.Should().NotContain($"{navigation}@odata.bind", "a navigation property is the service's to resolve");
        payload.Values.OfType<string>().Should().NotContain(v => v.Contains($"/{entitySet}(", StringComparison.Ordinal),
            "no entity-set path is built by the route");
    }

    [Fact]
    public void CreatePayload_NoRecordTypeRow_LeavesTheRecordTypeLookupOut()
    {
        var payload = Create(1, recordTypeRef: null);

        payload.Keys.Should().NotContain("sprk_regardingrecordtype");
        payload["sprk_regardingmatter"].Should().BeEquivalentTo(Lookup("sprk_matter", TargetId));
    }

    [Fact]
    public void CreatePayload_AnalysisNamesItsLogicalTable_NeverTheLogicalNamePlusS()
    {
        var payload = Create(3, RecordTypeRef);

        payload["sprk_regardinganalysis"].Should().BeEquivalentTo(Lookup("sprk_analysis", TargetId));
        payload.Values.OfType<Dictionary<string, object?>>()
            .Should().NotContain(d => d["relatedTable"] as string == "sprk_analysiss" || d["relatedTable"] as string == "sprk_analysises");
    }

    [Fact]
    public void CreatePayload_WritesOnlyTheTargetsOwnTypedLookup_TheCoreStampsAreTheServices()
    {
        var payload = Create(3, RecordTypeRef);

        payload.Keys.Where(k => k.StartsWith("sprk_regarding", StringComparison.Ordinal) && !k.StartsWith("sprk_regardingrecord", StringComparison.Ordinal))
            .Should().Equal("sprk_regardinganalysis");
        payload.Keys.Should().NotContain("sprk_regardingmatter", "the I-1 core stamps are derived by RecordCreationService, not the route");
    }

    [Fact]
    public void CreatePayload_AnUnknownRegardingType_IsRefused()
    {
        var act = () => Create(99, RecordTypeRef);

        act.Should().Throw<InvalidOperationException>("no table is known for the type, and the route never guesses one");
    }

    [Fact]
    public void CreatePayload_NoRegarding_WritesNoRegardingColumns()
    {
        var payload = EventsCapability.BuildCreatePayload(new ApiCreateEventRequest("x"), regarding: null, assignedToContactId: null);

        payload.Keys.Should().NotContain(k => k.StartsWith("sprk_regarding", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePayload_DefaultStatusIsTheLiveOpenWithItsStatecode()
    {
        var payload = EventsCapability.BuildCreatePayload(new ApiCreateEventRequest("x"), regarding: null, assignedToContactId: null);

        payload["statuscode"].Should().Be(659490001);
        payload["statecode"].Should().Be(0);
    }

    // ── Read shape: the shared URL builder ──────────────────────────────────────────────────────────────────

    [Fact]
    public void QueryUrl_ARegardingIdIsOnlyItsDForm_AndTheTypeAddsItsTypedLookup()
    {
        var url = DataverseWebApiService.BuildEventQueryUrl(
            regardingRecordType: 1, regardingRecordId: TargetId, regardingRecordTypeRefId: null,
            eventTypeId: null, statusCode: null, priority: null, dueDateFrom: null, dueDateTo: null,
            skip: 0, top: 50, mine: null);

        Filter(url).Should().Be(
            $"sprk_regardingrecordid eq '{TargetId:D}' and _sprk_regardingmatter_value eq {TargetId:D}");
    }

    [Fact]
    public void QueryUrl_ATypeAloneFiltersOnTheRecordTypeLookup_AndRefusesToBuildWithoutItsRow()
    {
        var url = DataverseWebApiService.BuildEventQueryUrl(
            0, null, RecordTypeRef, null, null, null, null, null, 0, 50, null);

        Filter(url).Should().Be($"_sprk_regardingrecordtype_value eq {RecordTypeRef:D}");

        var noRow = () => DataverseWebApiService.BuildEventQueryUrl(0, null, null, null, null, null, null, null, 0, 50, null);
        noRow.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QueryUrl_EveryClauseComesFromATypedValue()
    {
        var owner = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var eventType = Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b");

        var url = DataverseWebApiService.BuildEventQueryUrl(
            regardingRecordType: null, regardingRecordId: TargetId, regardingRecordTypeRefId: null,
            eventTypeId: eventType, statusCode: 659490001, priority: 2,
            dueDateFrom: new DateTime(2026, 1, 2), dueDateTo: new DateTime(2026, 3, 4),
            skip: 0, top: 50, mine: new Spaarke.Dataverse.EventOwnershipScope(owner, null, null));

        Filter(url).Should().Be(
            $"_ownerid_value eq {owner:D} and sprk_regardingrecordid eq '{TargetId:D}' and "
            + $"_sprk_eventtype_ref_value eq {eventType:D} and statuscode eq 659490001 and sprk_priority eq 2 and "
            + "sprk_duedate ge 2026-01-02 and sprk_duedate le 2026-03-04");
    }

    [Fact]
    public void QueryUrl_SelectsTheLookupAsALookup_ExpandsByNavigationProperty_AndPagesWithoutSkip()
    {
        var url = DataverseWebApiService.BuildEventQueryUrl(null, null, null, null, null, null, null, null, 100, 50, null);

        var select = Param(url, "$select").Split(',');
        select.Should().Contain("_sprk_regardingrecordtype_value").And.NotContain("sprk_regardingrecordtype");
        foreach (var lookup in new[] { "project", "matter", "invoice", "analysis", "account", "contact", "workassignment", "budget" })
        {
            select.Should().Contain($"_sprk_regarding{lookup}_value");
        }

        Param(url, "$expand").Should().Be("sprk_EventType_Ref($select=sprk_name)");
        url.Should().NotContain("$skip", "the Dataverse Web API rejects $skip");
        Param(url, "$top").Should().Be("150", "the page window is the first skip + top rows");
        Param(url, "$count").Should().Be("true");

        var tooFar = () => DataverseWebApiService.BuildEventQueryUrl(null, null, null, null, null, null, null, null, 4990, 50, null);
        tooFar.Should().Throw<ArgumentOutOfRangeException>();

        DataverseWebApiService.EventGetSelect.Split(',')
            .Should().Contain("_sprk_regardingrecordtype_value")
            .And.NotContain("sprk_regardingrecordtype")
            .And.NotContain("_sprk_relatedevent_value", "sprk_event has no sprk_relatedevent lookup (live 400)");
    }

    // ── Read shape: the mapper ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mapper_DerivesTheTypeFromTheTypedLookupWhoseValueEqualsTheRegardingId_EvenBesideACoreStamp()
    {
        var invoiceWithMatterStamp = Row($$"""
            {
              "sprk_eventid": "{{Guid.NewGuid()}}", "sprk_eventname": "Invoice review",
              "sprk_regardingrecordid": "{{TargetId:D}}",
              "_sprk_regardingrecordtype_value": "{{RecordTypeRef:D}}",
              "_sprk_regardingmatter_value": "{{MatterStamp:D}}",
              "_sprk_regardinginvoice_value": "{{TargetId:D}}",
              "statecode": 0, "statuscode": 659490001,
              "sprk_EventType_Ref": { "sprk_name": "Task" }
            }
            """);

        var entity = DataverseWebApiService.MapToEventEntity(invoiceWithMatterStamp);

        entity.RegardingRecordType.Should().Be(RegardingRecordType.Invoice);
        entity.EventTypeName.Should().Be("Task");
    }

    [Fact]
    public void Mapper_NoTypedLookupMatchingTheRegardingId_IsNull()
    {
        var row = Row($$"""
            {
              "sprk_eventid": "{{Guid.NewGuid()}}", "sprk_eventname": "Orphan",
              "sprk_regardingrecordid": "{{TargetId:D}}",
              "_sprk_regardingmatter_value": "{{MatterStamp:D}}",
              "statecode": 0, "statuscode": 1
            }
            """);

        DataverseWebApiService.MapToEventEntity(row).RegardingRecordType.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Create(int type, Guid? recordTypeRef) =>
        EventsCapability.BuildCreatePayload(
            new ApiCreateEventRequest("Hearing"),
            new EventsCapability.RegardingFields(
                type, TargetId, recordTypeRef, "Target", "/main.aspx?pagetype=entityrecord&etn=x&id=y", Number: null),
            assignedToContactId: null);

    /// <summary>The live table each 0-7 regarding type names (pinned here, not read back from the code under test).</summary>
    private static string LogicalNameOf(int type) => type switch
    {
        0 => "sprk_project",
        1 => "sprk_matter",
        2 => "sprk_invoice",
        3 => "sprk_analysis",
        4 => "account",
        5 => "contact",
        6 => "sprk_workassignment",
        7 => "sprk_budget",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>A lookup in the service's Web API payload: the target table and the record, no entity set or bind key.</summary>
    private static Dictionary<string, object?> Lookup(string relatedTable, Guid recordId) => new()
    {
        ["relatedTable"] = relatedTable,
        ["recordId"] = recordId.ToString("D"),
    };

    private static void AssertNoNumericRecordTypeAndNoBindOrValueKeys(Dictionary<string, object?> payload)
    {
        payload["sprk_regardingrecordtype"].Should().BeOfType<Dictionary<string, object?>>("the record type is a lookup, never a number");
        payload.Keys.Should().NotContain(k => k.EndsWith("@odata.bind", StringComparison.Ordinal),
            "the service resolves a lookup's navigation property and set itself; the route never names them");
        payload.Keys.Should().NotContain(k => k.StartsWith('_') && k.EndsWith("_value", StringComparison.Ordinal),
            "the Web API does not accept _x_value keys as a write");
    }

    private static string Param(string url, string name)
    {
        var query = url[(url.IndexOf('?') + 1)..];
        foreach (var part in query.Split('&'))
        {
            if (part.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return part[(name.Length + 1)..];
            }
        }

        return string.Empty;
    }

    private static string Filter(string url) => Param(url, "$filter");

    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
