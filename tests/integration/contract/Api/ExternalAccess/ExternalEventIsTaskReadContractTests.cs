using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 008 (uac-r2 review 2026-11-13, condition C): the three event lists
/// (<c>GET /api/v1/external/{projects|matters|workassignments}/{id}/events</c>) return <c>isTask</c> — true only for an event
/// whose <c>sprk_eventtype_ref</c> is the Task row. The lookup is read and compared server-side; its id never reaches the wire.
/// </summary>
/// <remarks>
/// The service under test is the REAL <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/>,
/// which projects each row through the request's <c>$select</c> as Dataverse does: a list that did not select the type lookup would
/// read every event as untyped and the must-fire test fails. Placement: <c>tests/integration/contract/**</c> (ADR-038).
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalEventIsTaskReadContractTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999008");
    private static readonly Guid Record = Guid.Parse("a0080000-0000-4000-8000-00000000f00a");

    /// <summary>Another <c>sprk_eventtype_ref</c> row (a Meeting, say): typed, but not a Task.</summary>
    private static readonly Guid OtherEventType = Guid.Parse("a0080000-0000-4000-8000-00000000f0b0");

    private const string TypeColumn = "_sprk_eventtype_ref_value";

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalEventIsTaskReadContractTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> Roots => new() { "projects", "matters", "workassignments" };

    private static CallerPrincipal Reader(string segment) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = segment == "projects"
            ? new[] { CallerProjectAccess.FromLevel(Record, ExternalAccessLevel.ViewOnly) }
            : Array.Empty<CallerProjectAccess>(),
        MatterAccess = segment == "matters"
            ? new Dictionary<Guid, AccessRights> { [Record] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = segment == "workassignments"
            ? new Dictionary<Guid, AccessRights> { [Record] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
    };

    private void SeedTyped(string segment, string name, Guid? type)
    {
        var id = _fixture.Data.Add(segment, Record, name);
        _fixture.Data.SetColumn("sprk_events", "sprk_eventid", id, TypeColumn, type?.ToString());
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_IsTask_IsTrueOnlyForTaskTypedEvents(string segment)
    {
        _fixture.Principal = Reader(segment);
        SeedTyped(segment, "A task", EventTypeRef.Task);
        SeedTyped(segment, "A meeting", OtherEventType);
        SeedTyped(segment, "An untyped event", null);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>(
            $"/api/v1/external/{segment}/{Record}/events");

        body!.Value.ToDictionary(e => e.SprkName, e => e.IsTask).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["A task"] = true,
            ["A meeting"] = false,
            ["An untyped event"] = false,
        });
        _fixture.Data.Urls.Should().Contain(u => u.Contains("/sprk_events?", StringComparison.Ordinal) && u.Contains(TypeColumn, StringComparison.Ordinal),
            "the type is read server-side by the list query");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_TheTypeLookup_NeverReachesTheWire_OnlyTheBoolean(string segment)
    {
        _fixture.Principal = Reader(segment);
        SeedTyped(segment, "A task", EventTypeRef.Task);
        SeedTyped(segment, "A meeting", OtherEventType);

        using var client = _fixture.CreateAuthenticatedClient();
        var raw = await client.GetStringAsync($"/api/v1/external/{segment}/{Record}/events");

        raw.Should().NotContainEquivalentOf(EventTypeRef.TaskId, "the Task row id stays server-side");
        raw.Should().NotContainEquivalentOf(OtherEventType.ToString(), "no type id is ever emitted");
        raw.Should().NotContainEquivalentOf("eventtype", "neither the lookup column nor a type name is on the wire");
        using var doc = JsonDocument.Parse(raw);
        foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
            row.GetProperty("isTask").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Theory]
    [InlineData(true, "124f5fc9-98ff-f011-8406-7c1e525abd8b")]
    [InlineData(false, "not-a-guid")]
    public async Task Events_WhenTheTypeColumnComesBackMaskedOrMalformed_AreListed_AsNotTasks(bool masked, string stored)
    {
        // An unreadable type is "not known to be a Task": the event is still listed (the type decides no access), never a Task.
        _fixture.Principal = Reader("matters");
        var id = _fixture.Data.Add("matters", Record, "Unreadable type");
        _fixture.Data.SetColumn("sprk_events", "sprk_eventid", id, TypeColumn, stored);
        if (masked)
            _fixture.Data.MaskedColumns.Add(TypeColumn);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Record}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>();
        body!.Value.Should().ContainSingle().Which.IsTask.Should().BeFalse();
        body.Truncated.Should().BeFalse("the type is display data, not a scoping column");
    }

    // ── Review round 1 (K4): the create's 201 body carries isTask from the row Dataverse returns ─────────────────────────

    [Theory]
    [InlineData("task", true)]
    [InlineData(null, false)]
    public async Task CreateEventAsync_ReturnsIsTask_FromTheCreatedRow(string? eventType, bool expected)
    {
        // The REAL service over the fake Web API, which answers a return=representation create with the row it would store (the
        // type bind comes back as _sprk_eventtype_ref_value), so the 201 body's isTask is the mapping of what Dataverse wrote.
        var service = new ExternalDataService(
            new HttpClient(_fixture.Data),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
            NullLogger<ExternalDataService>.Instance,
            _fixture.Data.FilingReader,
            visibility: Sprk.Bff.Api.Tests.TestInfrastructure.ExternalVisibilitySeams.AllVisible);
        var request = new CreateExternalEventRequest { SprkName = "Send the draft", EventType = eventType };

        var created = await service.CreateEventAsync(
            ExternalDataService.TodoRootKind.Matter, Record, request, Guid.Parse("a0080000-0000-4000-8000-00000000f0aa"),
            Reader("matters"));

        created.IsTask.Should().Be(expected);
        created.SprkName.Should().Be("Send the draft", "the row came back from the create, not a default");
        _fixture.Data.Requests.Should().ContainSingle(r => r.StartsWith("POST ", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildEventsQuery_SelectsTheTypeLookup()
    {
        var query = Uri.UnescapeDataString(ExternalDataService.BuildEventsQuery(ExternalDataService.TodoRootKind.Matter, Record));

        query.Should().Contain($",{TypeColumn},").And.NotContain("$expand", "the lookup id is compared, never its row expanded");
    }
}
