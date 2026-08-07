// spaarke-SPA-external-access-platform-r2 Task 015 — contract tests for the FR-22 module-host
// widget-data read seam (/api/v1/external/api/dataverse/*), the endpoints the read-only
// BffDataverseClient consumes (ADR-028 A3).
//
// KEEP-path classification (ADR-038 §2 / tests/CLAUDE.md): endpoint-contract + security-auth. These
// assert the SECURITY layer that runs BEFORE the app-only Dataverse read services (which require a live
// ServiceClient and so cannot execute in-process): the generalized-resolver wiring, the per-module
// registration gate (fail-closed on an unregistered entity), the single-entity restriction that closes
// the link-entity over-read hole, and the Tier-2 record deny (NFR-08 — a non-participant caller gets
// nothing). The row-scoping happy path is unit-tested in ExternalModuleRegistryTests.
//
// Reuses ExternalAccessContractFixture (same file's fixture): the CIAM plane is selected via the fake
// token issuer; X-Test-Projects drives the caller's accessible project set (→ CallerPrincipal.ProjectAccess);
// the collaboration module (sprk_project) is registered by the real ExternalAccessModule.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration/ctor-null tests;
// every test asserts an HTTP-observable status. Names are {Method}_{Scenario}_{ExpectedResult}.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class ExternalModuleDataContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private const string FetchPath = "/api/v1/external/api/dataverse/fetch";
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalModuleDataContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static string ProjectFetchXml =>
        "<fetch><entity name=\"sprk_project\"><attribute name=\"sprk_projectid\"/></entity></fetch>";

    // ── Auth gate (generalized resolver inherited from the group) ────────────────────────────────

    [Fact]
    public async Task ModuleFetch_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_project",
            fetchXml = ProjectFetchXml,
            pagingCookie = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the module-data group inherits the ExternalCollaboration policy — no token ⇒ 401 before any handler");
    }

    // ── Per-module registration gate — fail-closed on an unregistered entity ─────────────────────

    [Fact]
    public async Task ModuleFetch_WhenEntityHasNoRegisteredModule_Returns403()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_matter", // no module registered for sprk_matter in task 015
            fetchXml = "<fetch><entity name=\"sprk_matter\"><attribute name=\"sprk_matterid\"/></entity></fetch>",
            pagingCookie = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an entity with no registered module is not readable via the seam — fail-closed (not unscoped)");
    }

    [Fact]
    public async Task ModuleMetadata_WhenEntityHasNoRegisteredModule_Returns403()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // An external caller must not enumerate schema for an arbitrary entity (e.g. systemuser).
        var response = await client.GetAsync("/api/v1/external/api/dataverse/metadata/systemuser");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Over-read defense (C1) — the FetchXml may reference ONLY the module entity ────────────────

    [Fact]
    public async Task ModuleFetch_WhenFetchXmlJoinsAnotherEntity_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // A <link-entity> to systemuser on an accessible project would otherwise ride internal columns
        // out with the scoped project row. The single-entity restriction rejects it before execution.
        var linkFetchXml =
            "<fetch><entity name=\"sprk_project\">" +
            "<attribute name=\"sprk_projectid\"/>" +
            "<link-entity name=\"systemuser\" from=\"systemuserid\" to=\"ownerid\" alias=\"u\">" +
            "<attribute name=\"internalemailaddress\"/></link-entity>" +
            "</entity></fetch>";

        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_project",
            fetchXml = linkFetchXml,
            pagingCookie = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "cross-entity joins are not permitted on the external read seam (broker over-read defense)");
    }

    // ── Tier-2 record deny (NFR-08) — a non-participant caller is denied ─────────────────────────

    [Fact]
    public async Task ModuleRecord_WhenRecordOutsideCallersAccessibleSet_Returns403()
    {
        // Caller participates in ProjectA only; requesting ProjectB (not in their Tier-2 set) is denied
        // BEFORE any Dataverse read.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.GetAsync($"/api/v1/external/api/dataverse/record/sprk_project/{ProjectB}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "being authenticated does not reveal records outside the caller's participation set (NFR-08)");
    }

    [Fact]
    public async Task ModuleRecord_WhenNonParticipant_Returns403()
    {
        // No participations at all → every record is outside the set.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var response = await client.GetAsync($"/api/v1/external/api/dataverse/record/sprk_project/{ProjectA}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
