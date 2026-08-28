using System.Net;
using FluentAssertions;
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
/// WHY THE ASSERTION IS 404 AND NOT 401: ASP.NET Core routes BEFORE it authorizes. An unauthenticated
/// request to a route that EXISTS and carries <c>RequireAuthorization()</c> returns 401; an
/// unauthenticated request to a route that does NOT exist returns 404. So 404 proves absence and 401
/// proves the route came back. <see cref="SurvivingOboUploadRoute_WithoutBearer_Returns401"/> is the
/// positive control that keeps this discrimination honest — without it, a fixture change that made
/// every request 404 would silently turn all the absence assertions into vacuous passes.
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
    private readonly HttpClient _client;

    public OboDriveKeyedRouteRetirementTests(CustomWebAppFactory factory)
    {
        // Deliberately NO Authorization header: routing precedes authorization, so an absent route
        // answers 404 while a present one answers 401. See the class summary.
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RetiredOboEnumerateContainerRoute_WhenRequested_Returns404NotRouted()
    {
        var response = await _client.GetAsync("/api/obo/containers/test-container/children");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "GET /api/obo/containers/{id}/children was deleted by task 071 — it enumerated a whole "
            + "container with no per-document decision (and converted a Graph 404 into 200 with an "
            + "empty list). A 401 here means the route was re-added.");
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
            + "AddDocumentAuthorizationFilter(\"write\"). A 401 here means the route was re-added.");
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
            + "AddDocumentAuthorizationFilter(\"read\"). A 401 here means the route was re-added.");
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
            + "A 401 here means the route was re-added.");
    }

    /// <summary>
    /// POSITIVE CONTROL. Proves the 404s above mean "route absent" rather than "fixture returns 404
    /// for everything". This route SURVIVED task 071 and is intentionally still ungated — it CREATES
    /// content, so no <c>sprk_document</c> row exists yet to authorize against, and its authorization
    /// object is the owning record / container. That seam is owned by tasks 075 + 076 (record-aware
    /// container resolver) and must land with task 073, which gates the app-only twin
    /// <c>PUT /api/containers/{containerId}/files/{*path}</c>. Task 074's route-authorization ArchTest
    /// carries a NAMED WAIVER for it until then.
    ///
    /// When 075/076 land, this test should keep passing (401 without a bearer is orthogonal to the
    /// per-record gate). If the route is ever retired instead, flip this to NotFound and say so here.
    /// </summary>
    [Fact]
    public async Task SurvivingOboUploadRoute_WithoutBearer_Returns401()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await _client.PutAsync("/api/obo/containers/test-container/files/f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "PUT /api/obo/containers/{id}/files/{*path} is still mapped (11 live wizard call sites). "
            + "If this returns 404, the route was removed and ~7 Create*Wizard surfaces plus "
            + "DocumentUploadWizard are broken; if it returns 200, authorization stopped running.");
    }
}
