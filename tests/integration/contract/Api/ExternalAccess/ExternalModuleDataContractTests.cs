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
using System.Text.Json;
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

        // spaarke-SPA-external-access-platform-r2 Task 016 (2026-08-06) registered sprk_matter as a
        // module (an ALWAYS-EMPTY Tier-2 predicate — see ExternalAccessModule.cs D-016-1), so it no
        // longer exercises the "no registered module" fail-closed branch this test targets. `contact`
        // is a stable stand-in: an OOB Dataverse entity that will never have an external module
        // registered (no widget reads Contact rows via this seam) — same fail-closed assertion.
        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "contact", // no module registered for contact
            fetchXml = "<fetch><entity name=\"contact\"><attribute name=\"contactid\"/></entity></fetch>",
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

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_FETCHXML_ENTITY_MISMATCH",
            "the pre-existing entity-identity check must remain the one that fires for a FOREIGN entity; " +
            "asserting only the 400 would still pass if that check were deleted, because task 011's join " +
            "detection also rejects this fetch");
    }

    // ── A-17 / FR-10 — a SAME-ENTITY self-join is rejected, not scoped ───────────────────────────

    [Fact]
    public async Task ModuleFetch_WhenFetchXmlSelfJoinsTheModuleEntity_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // Finding A-17: a self-join names NO foreign entity, so the entity-identity guard admits it. But
        // Tier-2 scoping filters PRIMARY rows only, so the aliased columns would be sourced from projects
        // the caller has no access to and ride out on their in-scope rows — cross-client disclosure.
        var selfJoinFetchXml =
            "<fetch><entity name=\"sprk_project\">" +
            "<attribute name=\"sprk_projectid\"/>" +
            "<link-entity name=\"sprk_project\" from=\"statecode\" to=\"statecode\" alias=\"leak\">" +
            "<attribute name=\"sprk_name\"/></link-entity>" +
            "</entity></fetch>";

        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_project",
            fetchXml = selfJoinFetchXml,
            pagingCookie = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "FR-10: the self-join is REJECTED, not scoped");

        // This is the wiring assertion. Unit tests prove the guard refuses; only an HTTP-observable
        // errorCode proves the ENDPOINT actually consults it — and that the refusal came from join
        // detection rather than incidentally from the entity check.
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED");
    }

    [Fact]
    public async Task ModuleFetch_WhenFetchXmlIsSingleEntityWithNoJoin_IsNotRejectedByTheGuard()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // The join guard must not swallow legitimate reads. This request proceeds PAST the guard; it
        // cannot reach a live Dataverse in-process, so the assertion is narrowly that it is not one of
        // the guard's 400s. Without this, a guard that rejected everything would look "secure" and green.
        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_project",
            fetchXml = ProjectFetchXml,
            pagingCookie = (string?)null,
        });

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            var errorCode = problem.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
            new[] { "DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED", "DV_FETCHXML_ENTITY_MISMATCH", "DV_FETCHXML_MALFORMED" }
                .Should().NotContain(errorCode,
                    "a plain single-entity read of the module's own entity must pass the FetchXML guard");
        }
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
