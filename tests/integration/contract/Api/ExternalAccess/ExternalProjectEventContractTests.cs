// unified-access-control-r2 — contract tests for the restored external calendar-event routes
// (GET/POST /api/v1/external/projects/{id}/events).
//
// WHY THESE ROUTES EXIST AGAIN: smart-todo-decoupling-r3 FR-29 retired the event-AS-todo model
// (sprk_event + sprk_todoflag standing in for a to-do) and deleted these routes along with it. That
// retirement was correct, but it also removed the only way an external user could read genuine
// CALENDAR events for their project — which the external SPA's EventsCalendar component still
// renders, producing a guaranteed 404 (sweep claims R10/R11). The restored routes serve real
// sprk_event records and never touch sprk_todoflag. To-dos remain exclusively on /todos.
//
// KEEP-path classification (ADR-038 §2 / tests/CLAUDE.md): endpoint-contract + security-auth. Like
// the sibling ExternalModuleDataContractTests, these assert ONLY the security/validation layer that
// runs BEFORE ExternalDataService touches Dataverse — the app-only read path needs a live
// ServiceClient and cannot execute in-process. Every assertion is an HTTP-observable status.
//
// The load-bearing test here is CreateEvent_WhenParticipantLacksCreateRight_Returns403: read access
// must never imply write access. A View-Only external collaborator may list a project's events but
// must not be able to add one. That is the property most likely to be silently lost in a future
// refactor of the two-stage gate, and it is the reason X-Test-AccessLevel was added to the fixture.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration tests, no
// ctor null-check tests. Names are {Method}_{Scenario}_{ExpectedResult}.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class ExternalProjectEventContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalProjectEventContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static string EventsPath(Guid projectId) => $"/api/v1/external/projects/{projectId}/events";

    // -----------------------------------------------------------------------
    // GET — read gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEvents_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync(EventsPath(ProjectA));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the external route group requires authorization before any handler runs");
    }

    [Fact]
    public async Task GetEvents_WhenNonParticipant_Returns403()
    {
        // No participations at all → the project is outside the caller's Tier-2 set.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var response = await client.GetAsync(EventsPath(ProjectA));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authentication alone must not expose a project's calendar");
    }

    [Fact]
    public async Task GetEvents_WhenParticipantOfADifferentProject_Returns403()
    {
        // Participates in ProjectA; asks for ProjectB. Denied BEFORE any Dataverse read — this is the
        // cross-tenant/cross-project disclosure case, where the BFF filter is the entire boundary
        // (project CLAUDE.md fact #1: these reads are app-only, so Dataverse RLS is inert).
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.GetAsync(EventsPath(ProjectB));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "holding one project's participation must not read another project's events");
    }

    // -----------------------------------------------------------------------
    // POST — write gate (two stages: participation, then the Create right)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateEvent_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(EventsPath(ProjectA), new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateEvent_WhenNonParticipant_Returns403()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var response = await client.PostAsJsonAsync(EventsPath(ProjectA), new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateEvent_WhenParticipantOfADifferentProject_Returns403()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.PostAsJsonAsync(EventsPath(ProjectB), new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a write must be scoped to a project the caller actually participates in");
    }

    [Fact]
    public async Task CreateEvent_WhenParticipantLacksCreateRight_Returns403()
    {
        // THE point of the second gate. A View-Only participant passes HasProjectAccess (they can read
        // the project) but must fail the AccessRights.Create check. If this test ever goes green on a
        // 400/201 instead, read access has been conflated with write access — see FR-07/FR-29: a
        // one-click write on a confidential project is exactly the escalation the gate prevents.
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            accessLevel: ExternalAccessLevel.ViewOnly);

        var response = await client.PostAsJsonAsync(EventsPath(ProjectA), new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "View-Only participation grants read but never create");
    }

    [Fact]
    public async Task CreateEvent_WhenNameMissing_Returns400()
    {
        // Positive control for the two gates above: an authorized caller with the Create right gets
        // PAST both checks and is stopped by field validation instead. Without this, every 403 above
        // would also be satisfied by a route that rejects all POSTs for some unrelated reason.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.PostAsJsonAsync(EventsPath(ProjectA), new { sprk_duedate = "2026-10-01T00:00:00Z" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an authorized caller reaches validation — proving the 403s above are authorization, not a blanket refusal");
    }
}
