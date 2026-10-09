// spaarke-ontology-platform-r1 task 106 (owner decision D-41, 2026-10-08) — sprk_todo.sprk_duedate is a calendar date.
//
// Live (spaarkedev1): the column was Format DateOnly / Behavior UserLocal; it is now Behavior DateOnly
// (docs/data-model/sprk_todo-date-columns.md). The Web API returns "yyyy-MM-dd" and REFUSES a timestamp on write
// (HTTP 400 "Cannot convert the literal … to the expected type 'Edm.Date'").
//
// What was wrong on the external to-do routes:
//   - the external SPA sent toISOString() of the picked day's NOON UTC, and ExternalDataService passed sprk_duedate
//     through to Dataverse unchanged on create AND update — every external to-do with a due date would now be a 400
//     behind a 500;
//   - a value that is not a date at all reached Dataverse too.
//   - completing a to-do from the external app sent statuscode 2 alone, which Dataverse refuses ("2 is not a valid
//     status code for state code sprk_TodoState.Active", HTTP 400 — found by this task's live leg); it does not move the
//     state itself, so "Mark as complete" never worked from the external app.
// Run against the pre-106 code, the payload tests fail (the timestamp is passed through verbatim; no statecode is
// written; the update builder did not exist) and the route tests fail (a non-date or non-status reached the service:
// 201/204, not 400).

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.TodoDates;

[Trait("status", "task-106-ontology-r1")]
public sealed class ExternalTodoWritePathTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private static readonly Guid Project = Guid.Parse("10610610-0000-0000-0000-000000000106");
    private static readonly Guid TodoId = Guid.Parse("10610610-0000-0000-0000-000000000107");
    private static readonly Guid OwnerTeam = Guid.Parse("10610610-0000-0000-0000-000000000108");

    private readonly ExternalTodoScopeTestFixture _fixture;

    public ExternalTodoWritePathTests(ExternalTodoScopeTestFixture fixture) => _fixture = fixture;

    // ── The bodies sent to Dataverse: only a calendar date ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-20")]
    [InlineData("2026-10-20T12:00:00.000Z")] // what earlier SPA builds sent: toISOString() of the picked day's NOON UTC
    [InlineData("2026-10-20T00:00:00Z")]
    public void CreateBody_WritesTheDueDateAs_yyyyMMdd(string sent) =>
        ExternalDataService.BuildTodoCreatePayload(
                new CreateExternalTodoRequest { SprkName = "zz-106 external", SprkDuedate = sent },
                ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Project)!,
                Project, "Project 106", recordTypeRefId: null, OwnerTeam)
            ["sprk_duedate"].Should().Be("2026-10-20", "Dataverse answers 400 to a timestamp for a Date Only column");

    [Theory]
    [InlineData("2026-10-20")]
    [InlineData("2026-10-20T12:00:00.000Z")]
    public void UpdateBody_WritesTheDueDateAs_yyyyMMdd(string sent) =>
        ExternalDataService.BuildTodoUpdatePayload(new UpdateExternalTodoRequest { SprkDuedate = sent })
            ["sprk_duedate"].Should().Be("2026-10-20", "a reschedule through the external app is a Date Only write too");

    [Fact]
    public void UpdateBody_WithoutADueDate_DoesNotTouchTheColumn() =>
        ExternalDataService.BuildTodoUpdatePayload(new UpdateExternalTodoRequest { Statuscode = 2 })
            .Should().NotContainKey("sprk_duedate", "completing a to-do must not rewrite its due date");

    // ── Complete / reopen / dismiss: the state is written with the status reason ──────────────────────────────

    [Theory]
    [InlineData(2, 1)]          // Completed → Inactive (the external app's "Mark as complete")
    [InlineData(659490002, 1)]  // Dismissed → Inactive
    [InlineData(1, 0)]          // Open → Active (the external app's "Mark as incomplete")
    [InlineData(659490001, 0)]  // In Progress → Active
    public void UpdateBody_WritesTheStateTheStatusReasonBelongsTo(int statuscode, int statecode)
    {
        var body = ExternalDataService.BuildTodoUpdatePayload(new UpdateExternalTodoRequest { Statuscode = statuscode });

        body["statuscode"].Should().Be(statuscode);
        body["statecode"].Should().Be(statecode, "Dataverse refuses a status reason outside the row's current state (HTTP 400)");
    }

    [Fact]
    public async Task UpdateRoute_RefusesAStatusReasonThatIsNotAToDoStatus_AndWritesNothing()
    {
        _fixture.Reset();
        _fixture.Principal = Collaborator();
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, Project, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new { statuscode = 659490004 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    // ── The routes refuse what is not a calendar date, before anything is written ─────────────────────────────

    [Theory]
    [InlineData("next tuesday")]
    [InlineData("20/10/2026")]
    [InlineData("")]
    public async Task CreateRoute_RefusesANonDate_WithA400_AndWritesNothing(string dueDate)
    {
        _fixture.Reset();
        _fixture.Principal = Collaborator();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{Project}/todos",
            new { sprk_name = "zz-106 external", sprk_duedate = dueDate });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Data.CreateCallCount.Should().Be(0, "a value Dataverse would refuse must not reach it");
    }

    [Fact]
    public async Task CreateRoute_AcceptsACalendarDate()
    {
        _fixture.Reset();
        _fixture.Principal = Collaborator();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{Project}/todos",
            new { sprk_name = "zz-106 external", sprk_duedate = "2026-10-20" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.LastCreateRequest!.SprkDuedate.Should().Be("2026-10-20");
    }

    [Theory]
    [InlineData("next tuesday")]
    [InlineData("")]
    public async Task UpdateRoute_RefusesANonDate_WithA400_AndWritesNothing(string dueDate)
    {
        _fixture.Reset();
        _fixture.Principal = Collaborator();
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, Project, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new { sprk_duedate = dueDate });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRoute_AcceptsACalendarDate()
    {
        _fixture.Reset();
        _fixture.Principal = Collaborator();
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, Project, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new { sprk_duedate = "2026-10-21" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Data.LastRequest!.SprkDuedate.Should().Be("2026-10-21");
    }

    private static CallerPrincipal Collaborator() =>
        new()
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.Parse("10610610-0000-0000-0000-000000000109"),
            Email = "external.user@example.test",
            ProjectAccess = [CallerProjectAccess.FromLevel(Project, ExternalAccessLevel.Collaborate)],
        };
}
