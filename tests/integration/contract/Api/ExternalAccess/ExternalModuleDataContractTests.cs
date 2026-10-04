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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
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

    // ── View scope (unified-access-control-r2 task 157, F1) — only registered views, 404 otherwise ──────
    // Every production module grid is inline and registers no view, so both routes answer 404 over HTTP, and they
    // answer it before any Dataverse read. The pipeline itself is covered in
    // tests/integration/auth/UnifiedAccessControl/ExternalModuleSavedQueryScopeTests.cs; the rows here prove the ROUTES
    // run it. The by-id row that bites a route-level bypass is
    // ModuleSavedQuery_WhenTheIdIsAnInternalViewOfARegisteredEntity_Returns404: an unknown id 404s whether or not the
    // allow-list runs, so only a view the app's own read actually serves can tell the two apart.

    [Theory]
    [InlineData("sprk_project")]   // a registered entity: its internal MDA views are not listed
    [InlineData("contact")]        // an entity with no module
    public async Task ModuleSavedQueries_WhenNoViewIsRegisteredForTheEntity_Returns404(string entity)
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.GetAsync($"/api/v1/external/api/dataverse/savedqueries/{entity}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_SAVEDQUERY_NOT_FOUND");
    }

    [Fact]
    public async Task ModuleSavedQuery_WhenTheViewIsNotRegistered_Returns404()
    {
        // An unknown id: no view exists for it. The same 404 as a refused id (an unknown and a denied id look alike).
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var response = await client.GetAsync($"/api/v1/external/api/dataverse/savedquery/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_SAVEDQUERY_NOT_FOUND");
    }

    [Fact]
    public async Task ModuleSavedQuery_WhenTheIdIsAnInternalViewOfARegisteredEntity_Returns404()
    {
        // F1 over the real route. The app's own SavedQueryService serves an INTERNAL sprk_project view for this id: it
        // is planted in the distributed cache that service reads first, because its Dataverse read needs a live
        // ServiceClient. sprk_project has an external module, so the pre-157 route served this view with a 200; the
        // route must now refuse it, because no module grid is registered to use it.
        var internalView = Guid.NewGuid();
        const string viewName = "All Projects (internal MDA view)";
        var view = new SavedQueryDto(
            EntityName: "sprk_project",
            FetchXml: "<fetch><entity name='sprk_project'><attribute name='ownerid'/></entity></fetch>",
            LayoutXml: "<grid name='resultset'><row name='result' id='sprk_projectid'/></grid>",
            Name: viewName);
        var cache = _fixture.Services.GetRequiredService<IDistributedCache>();
        var cacheKey = $"sdap:dv:savedquery:{internalView:D}";
        await cache.SetStringAsync(
            cacheKey, JsonSerializer.Serialize(view, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        try
        {
            // Precondition: the read the route would make really returns the view. Without it a 404 below could come
            // from an absent view, which is the blind spot of the unknown-id row above.
            using (var scope = _fixture.Services.CreateScope())
            {
                var served = await scope.ServiceProvider.GetRequiredService<SavedQueryService>()
                    .GetSavedQueryAsync(internalView, CancellationToken.None);
                served.Should().Be(view, "the fixture must serve the internal view, or this test cannot see a bypass");
            }

            using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

            var response = await client.GetAsync($"/api/v1/external/api/dataverse/savedquery/{internalView}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "a view no external module grid is registered to use is refused, even on an entity that has a module");
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(viewName).And.NotContain("ownerid", "no part of the view definition leaves the BFF");
            JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString().Should().Be("DV_SAVEDQUERY_NOT_FOUND");
        }
        finally
        {
            await cache.RemoveAsync(cacheKey);
        }
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
            new[]
                {
                    "DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED", "DV_FETCHXML_ENTITY_MISMATCH", "DV_FETCHXML_MALFORMED",
                    "DV_FETCHXML_COLUMN_NOT_PERMITTED",
                }
                .Should().NotContain(errorCode,
                    "a plain single-entity read of the module's own allow-listed column must pass the FetchXML guard");
        }
    }

    // ── Column scope (task 134 · defect C6) — wiring: the ENDPOINT consults the column allow-list ──
    // Guard semantics (every position, alias, aggregate, ordering, fail-closed) are covered against the
    // real guard in tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs.
    // These two prove the production registration + route actually apply it over HTTP.

    [Fact]
    public async Task ModuleFetch_WhenFetchXmlSelectsAnSpePointerColumn_Returns400ColumnNotPermitted()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // C6 verbatim: a granted caller asks for the Graph item id on its in-scope documents. Before task
        // 134 this executed app-only (field security bypassed) and returned the pointer.
        var response = await client.PostAsJsonAsync(FetchPath, new
        {
            entityName = "sprk_document",
            fetchXml = "<fetch><entity name=\"sprk_document\">" +
                       "<attribute name=\"sprk_documentid\"/><attribute name=\"sprk_project\"/>" +
                       "<attribute name=\"sprk_graphitemid\"/></entity></fetch>",
            pagingCookie = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_FETCHXML_COLUMN_NOT_PERMITTED");
    }

    [Fact]
    public async Task ModuleRecord_WhenSelectNamesANonReadableColumn_Returns400ColumnNotPermitted()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // C6's /record half: a granted ROOT record's container pointer via $select.
        var response = await client.GetAsync(
            $"/api/v1/external/api/dataverse/record/sprk_project/{ProjectA}?$select=sprk_projectname,sprk_containerid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("DV_RECORD_COLUMN_NOT_PERMITTED");
    }

    [Fact]
    public async Task ModuleRecord_WhenSelectIsReadable_PassesTheColumnCheckAndReachesTheRead()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        // The fixture's IDataverseService double returns no entity, so a request that clears the column
        // check AND the Tier-2 gate surfaces as 404 from the read itself. A column check that refused
        // everything would answer 400 here instead.
        var response = await client.GetAsync(
            $"/api/v1/external/api/dataverse/record/sprk_project/{ProjectA}?$select=sprk_projectname");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    // ── Task 136 · defect C2 — a grant row with NO level confers nothing (owner 2026-09-30) ──────────────
    //
    // Matter and work-assignment grant rows with a null sprk_accesslevel (only writable outside the BFF) were
    // kept as None-rights keys, and the module scope dimensions read the KEY views — so /fetch scoped rows to
    // that matter's documents and invoices, and /record admitted it. The dimensions now read Read-gated views
    // (task 136 does not touch the registry or ExternalAccessModule; they change through CallerPrincipal).

    private static readonly Guid NullLevelMatter = Guid.Parse("4d000000-0000-0000-0000-000000000136");
    private static readonly Guid NullLevelWorkAssignment = Guid.Parse("4e000000-0000-0000-0000-000000000136");
    private static readonly Guid LevelledMatter = Guid.Parse("4d000000-0000-0000-0000-000000000137");

    public static TheoryData<string> BothPlanes() => new() { "ciam", "workforce" };

    [Theory]
    [MemberData(nameof(BothPlanes))]
    public async Task ModuleFetchAndRecord_MatterAndWorkAssignmentGrantRowsWithNoLevel_ConferNothing(string plane)
    {
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: Array.Empty<Guid>(),
            workforce: plane == "workforce",
            nullLevelMatters: new[] { NullLevelMatter },
            nullLevelWorkAssignments: new[] { NullLevelWorkAssignment });

        foreach (var (entity, column) in new[] { ("sprk_document", "sprk_documentid"), ("sprk_invoice", "sprk_invoiceid") })
        {
            var fetch = await client.PostAsJsonAsync(FetchPath, new
            {
                entityName = entity,
                fetchXml = $"<fetch><entity name=\"{entity}\"><attribute name=\"{column}\"/></entity></fetch>",
                pagingCookie = (string?)null,
            });

            // 200 with 0 rows WITHOUT a query: every scope dimension is empty. Had the null-level ids reached a
            // dimension, the scoped query would have run — and, offline, failed rather than answered 200.
            fetch.StatusCode.Should().Be(HttpStatusCode.OK, $"{plane}: {entity} fetch with an empty accessible set");
            var body = await fetch.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("entities").GetArrayLength().Should().Be(0,
                $"{plane}: a level-less grant row scopes no {entity} rows to its matter / work assignment");
        }

        (await client.GetAsync($"/api/v1/external/api/dataverse/record/sprk_matter/{NullLevelMatter}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{plane}: no level = not granted (matter)");
        (await client.GetAsync($"/api/v1/external/api/dataverse/record/sprk_workassignment/{NullLevelWorkAssignment}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{plane}: no level = not granted (work assignment)");
    }

    [Theory]
    [MemberData(nameof(BothPlanes))]
    public async Task ModuleRecord_MatterGrantRowWithALevel_PassesTheTier2Gate(string plane)
    {
        // Control for the test above: the same route admits a matter whose grant row carries a level, so the
        // 403 there is the missing level and not a route that refuses every matter.
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: Array.Empty<Guid>(), workforce: plane == "workforce");
        client.DefaultRequestHeaders.Add("X-Test-Matters", LevelledMatter.ToString());

        var response = await client.GetAsync(
            $"/api/v1/external/api/dataverse/record/sprk_matter/{LevelledMatter}?$select=sprk_mattername");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "past the Tier-2 gate the offline read finds nothing (404); a refusal would be 403");
    }
}
