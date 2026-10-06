using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 071 (Phase 0c Secure Documents, Wave 1).
///
/// Task 071 DELETED four drive/container-keyed OBO routes from <c>Api/OBOEndpoints.cs</c>. Each one
/// reached EXISTING SharePoint Embedded content keyed by <c>(driveId, itemId)</c> or
/// <c>(containerId)</c> with NO per-document authorization decision — their only authority was the
/// caller's own SPE container permission. Under the broker-only decision
/// (<c>SECURE-DOCUMENTS-BUILD-PLAN.md</c> §1) no user is ever granted a container permission, so
/// these routes could not serve a legitimate user need; they existed only as a way around the
/// per-document gate that <c>FileAccessEndpoints</c> / <c>DocumentOperationsEndpoints</c> apply to
/// the document-id-keyed equivalents.
///
/// WHAT WOULD BREAK IF THIS TEST WERE DELETED: someone re-adds one of these routes — most plausibly
/// while "restoring" a client helper that still references them (see the dead-method list in
/// <c>projects/unified-access-control-r2/notes/task-071-obo-route-retirement.md</c> §5) — and the
/// bypass silently returns. Nothing else in the suite would notice, because a re-added route would
/// simply start answering.
///
/// WHY THE ASSERTION IS 404 WITH A BEARER (re-shaped 2026-10-04, task 167 f1): ASP.NET Core routes BEFORE it
/// authorizes, and these tests used to send NO token — an absent route answered 404 and a present one 401.
/// Owner round 14 item 2 gave the BFF an authorization FallbackPolicy (an authenticated user), which also
/// applies to a request that matches NO endpoint, so an anonymous request to an absent route now answers 401
/// too and that discrimination is gone. Absence is now proved two ways the fallback cannot blur:
/// <see cref="RetiredOboRoutes_AreAbsentFromTheEndpointTable"/> reads the endpoint table, and each HTTP test
/// sends a bearer — a signed-in request to an absent route answers 404, to a present one anything but 404.
/// <see cref="SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404"/> is the positive control that keeps
/// this honest — without it, a fixture change that made every request 404 would silently turn all the
/// absence assertions into vacuous passes.
///
/// EXTENDED 2026-09-03 (task 076): the container-keyed OBO upload route
/// <c>PUT /api/obo/containers/{id}/files/{*path}</c> — which USED to be this file's positive control —
/// was itself retired, and is now asserted absent alongside task 071's four. The positive control moved
/// to <c>PUT /api/obo/me/files/{*path}</c>, one of the three replacement routes, none of which accepts a
/// container parameter.
///
/// This file replaces two test files deleted in the same change, both of which existed only to
/// exercise the now-deleted routes:
///   - <c>tests/unit/Sprk.Bff.Api.Tests/FileOperationsTests.cs</c> (PATCH / DELETE / GET content)
///   - <c>tests/integration/contract/ListingEndpointsContractTests.cs</c> (GET children)
/// The second was on the deletion-protected <c>tests/integration/contract/**</c> KEEP path; this file
/// is its same-PR replacement per <c>tests/CLAUDE.md</c>. The contract it asserted no longer exists —
/// what is worth protecting now is the route's ABSENCE, which is what this file asserts.
/// </summary>
[Trait("status", "repaired")]
public class OboDriveKeyedRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _client;

    /// <summary>The retired route patterns, as they were registered, compared against the live endpoint table.</summary>
    private static readonly (string Verb, string Pattern)[] RetiredRoutes =
    {
        ("GET", "/api/obo/containers/{id}/children"),
        ("PATCH", "/api/obo/drives/{driveId}/items/{itemId}"),
        ("GET", "/api/obo/drives/{driveId}/items/{itemId}/content"),
        ("DELETE", "/api/obo/drives/{driveId}/items/{itemId}"),
        ("PUT", "/api/obo/containers/{id}/files/{*path}"),
    };

    public OboDriveKeyedRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;

        // WITH a bearer: CustomWebAppFactory's FakeAuthHandler authenticates any request carrying one, so the
        // authorization FallbackPolicy is satisfied and an absent route meets routing's 404. See the class summary.
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    }

    [Fact]
    public void RetiredOboRoutes_AreAbsentFromTheEndpointTable()
    {
        var endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        endpoints.Should().NotBeEmpty(
            "the endpoint table must be non-empty for this assertion to mean anything — an empty table would make "
            + "every 'route is absent' check trivially true");

        var survivors = new List<string>();
        foreach (var (verb, pattern) in RetiredRoutes)
        {
            var normalized = pattern.TrimStart('/');
            foreach (var match in endpoints.Where(e =>
                         string.Equals(e.RoutePattern.RawText?.TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                var verbs = match.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>();
                if (verbs.Count == 0 || verbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
                {
                    survivors.Add($"{verb} {pattern}  (registered as: {match.DisplayName})");
                }
            }
        }

        survivors.Should().BeEmpty(
            "these drive/container-keyed OBO routes were RETIRED by tasks 071 and 076 and must not be re-registered: "
            + "each reached existing SPE content with no per-document decision.\n\nRe-registered routes:\n  "
            + string.Join("\n  ", survivors));
    }

    [Fact]
    public async Task RetiredOboEnumerateContainerRoute_WhenRequested_Returns404NotRouted()
    {
        var response = await _client.GetAsync("/api/obo/containers/test-container/children");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "GET /api/obo/containers/{id}/children was deleted by task 071 — it enumerated a whole "
            + "container with no per-document decision (and converted a Graph 404 into 200 with an "
            + "empty list). Anything but 404 for a signed-in caller means the route was re-added.");
    }

    [Fact]
    public async Task RetiredOboPatchItemRoute_WhenRequested_Returns404NotRouted()
    {
        using var content = new StringContent(
            "{\"name\":\"renamed.txt\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/test-item", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "PATCH /api/obo/drives/{driveId}/items/{itemId} was deleted by task 071. Rename/move now "
            + "goes through DocumentOperationsEndpoints, which carries "
            + "AddDocumentAuthorizationFilter(\"write\"). Anything but 404 for a signed-in caller means the route was re-added.");
    }

    [Fact]
    public async Task RetiredOboDownloadContentRoute_WhenRequested_Returns404NotRouted()
    {
        var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-item/content");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "GET /api/obo/drives/{driveId}/items/{itemId}/content was deleted by task 071 — it "
            + "streamed file bytes with no per-document decision. Reads now go through "
            + "FileAccessEndpoints, whose eight routes each carry "
            + "AddDocumentAuthorizationFilter(\"read\"). Anything but 404 for a signed-in caller means the route was re-added.");
    }

    [Fact]
    public async Task RetiredOboDeleteItemRoute_WhenRequested_Returns404NotRouted()
    {
        var response = await _client.DeleteAsync("/api/obo/drives/test-drive/items/test-item");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "DELETE /api/obo/drives/{driveId}/items/{itemId} was deleted by task 071 — it destroyed "
            + "SPE content with no per-document decision. Deletes now go through "
            + "DocumentOperationsEndpoints, which carries AddDocumentAuthorizationFilter(\"delete\"). "
            + "Anything but 404 for a signed-in caller means the route was re-added.");
    }

    /// <summary>
    /// The container-keyed OBO upload route is now RETIRED TOO — 2026-09-03, task 076.
    ///
    /// This assertion is the direct successor of a POSITIVE control that used to live here asserting
    /// the OPPOSITE (401, "still mapped, 11 live wizard call sites"). The class summary above
    /// explicitly instructed this flip: <i>"If the route is ever retired instead, flip this to
    /// NotFound and say so here."</i> Saying so here:
    ///
    /// The route wrote bytes into a CALLER-NAMED container with no per-resource authorization
    /// decision. It could not be gated in place, because it CREATES content and no
    /// <c>sprk_document</c> row exists yet to authorize against. Task 076 replaced it with three
    /// routes that take no container at all — two record-keyed (the server derives the container from
    /// the record it authorized the caller against) and one record-LESS (the server derives the
    /// acting user's business-unit container). Every client moved;
    /// <see cref="SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404"/> below now guards one of the
    /// survivors instead.
    /// </summary>
    [Fact]
    public async Task RetiredOboContainerKeyedUploadRoute_WhenRequested_Returns404NotRouted()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await _client.PutAsync("/api/obo/containers/test-container/files/f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "PUT /api/obo/containers/{id}/files/{*path} was deleted by task 076 — it wrote bytes to a "
            + "container the CALLER named, with no per-resource decision behind the destination. Anything "
            + "but 404 for a signed-in caller means the route was re-added; if that is deliberate it needs an "
            + "authorization decision, not a waiver.");
    }

    /// <summary>
    /// POSITIVE CONTROL. Proves the 404s above mean "route absent" rather than "fixture returns 404
    /// for everything".
    ///
    /// RE-POINTED 2026-09-03 (task 076) from <c>PUT /api/obo/containers/{id}/files/{*path}</c>, which
    /// is now retired and asserted absent immediately above. The control must name a route that
    /// SURVIVES, or every absence assertion in this file becomes vacuous — so it now names the
    /// record-LESS upload route, which is the replacement for the deleted one on the one path that
    /// genuinely has no owning record when the bytes move.
    ///
    /// RE-SHAPED 2026-10-04 (task 167 f1): it used to assert 401 WITHOUT a bearer. Since the authorization
    /// FallbackPolicy, an anonymous request to an ABSENT route is 401 as well, so that no longer proved the
    /// route is present. With a bearer, the surviving route is routed (anything but 404) and an absent one is 404.
    /// </summary>
    [Fact]
    public async Task SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await _client.PutAsync("/api/obo/me/files/f.txt", content);

        // Deliberately asserts NOT-404 rather than a specific code: the authorized path's status depends on
        // Graph/Dataverse behaviour the fixture does not fully stand up. What must stay true is that the route
        // is ROUTED, which is what makes the retired routes' 404s meaningful.
        response.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            "PUT /api/obo/me/files/{*path} is mapped. If a signed-in request to it returns 404 the route was "
            + "removed — and every absence assertion in this file has become vacuous, because a fixture that "
            + "404s everything would look identical.");
    }
}
