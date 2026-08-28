using System.Net;
// using System.Net.Http.Headers; — orphaned by task 073's removal of UploadEndpoints_RequiresValidPath,
// which was the file's only AuthenticationHeaderValue user.
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Tests for endpoint grouping and consistent ProblemDetails responses.
/// Ensures endpoints are properly organized and return RFC 7807 compliant errors.
/// </summary>
[Trait("status", "repaired")]
public class EndpointGroupingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public EndpointGroupingTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - endpoint returns 404 without proper registration")]
    public async Task DocumentsEndpoints_ReturnsProblemDetailsOnError()
    {
        // Attempt to create container without proper auth/data
        var response = await _client.PostAsync("/api/containers", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        // Should have RFC 7807 Problem Details structure
        problemDetails.TryGetProperty("type", out _).Should().BeTrue();
        problemDetails.TryGetProperty("title", out _).Should().BeTrue();
        problemDetails.TryGetProperty("status", out _).Should().BeTrue();
    }

    // UploadEndpoints_ReturnsProblemDetailsOnError REMOVED 2026-08-26 (unified-access-control-r2
    // task 073): it exercised POST /api/containers/{containerId}/upload, one of the three app-only
    // container-keyed write routes retired with Api/UploadEndpoints.cs. It was already
    // [Fact(Skip=...)], so it proved nothing — and per the same reasoning recorded below for
    // DocumentsEndpoints_ListContainersRequiresValidContainerTypeId, a skipped test against a route
    // that no longer exists is worse than no test, because it reads as coverage.
    // Replacement (route ABSENCE, which is what is worth protecting now):
    // tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs.

    [Fact]
    public async Task UserEndpoints_WithoutAuth_Return401()
    {
        // /api/me requires authorization (task 023): an unauthenticated request is
        // short-circuited by the auth middleware with a bare 401 challenge (no body),
        // consistent with every other .RequireAuthorization() endpoint in the app.
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // DocumentsEndpoints_ListContainersRequiresValidContainerTypeId REMOVED 2026-08-25
    // (spaarke-auth-v4-dataverse-MI task 090): it exercised GET /api/containers, one of the six
    // dead endpoints deleted with obligation 031-A. It was already [Fact(Skip=...)], so it was
    // proving nothing — a skipped test against a route that no longer exists is worse than no test,
    // because it reads as coverage.
    // UploadEndpoints_RequiresValidPath REMOVED 2026-08-26 (unified-access-control-r2 task 073):
    // same subject as above — POST /api/containers/{containerId}/upload, retired with
    // Api/UploadEndpoints.cs. Also already [Fact(Skip=...)].

    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - endpoint returns 404 without proper registration")]
    public async Task UserEndpoints_CapabilitiesRequiresContainerId()
    {
        // Missing containerId parameter
        var response = await _client.GetAsync("/api/me/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        problemDetails.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().Contain("containerId");
    }

    // DocumentEndpoints_ExistAndReturnConsistentErrorFormat REMOVED 2026-08-26
    // (unified-access-control-r2 task 073). Its assertion was `NotBe(404)` — "these routes exist" —
    // over three InlineData cases, and ALL THREE subjects have since been deleted:
    //   /api/containers                  — one of the six dead endpoints removed by
    //                                      spaarke-auth-v4-dataverse-MI task 090 (obligation 031-A)
    //   /api/containers/test-id/drive    — GET /api/containers/{containerId}/drive, deleted
    //                                      2026-08-25 (commit c17e856f4)
    //   /api/containers/test-id/upload   — retired by THIS task with Api/UploadEndpoints.cs
    // So the test asserted the existence of nothing, while [Theory(Skip=...)] kept it from ever
    // saying so. Route ABSENCE is now the invariant worth holding, and it is asserted in
    // tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs (this task) and
    // tests/integration/regression/OboDriveKeyedRouteRetirementTests.cs (task 071).

    [Fact]
    public async Task EmlRenderEndpoint_Unauthenticated_Returns401AndLeaksNoHtml()
    {
        // Fail-closed (task 010 / FR-07 / NFR-03): the /api/documents group RequireAuthorization() rejects an
        // unauthenticated caller BEFORE the handler runs — so no sanitized .eml HTML is ever produced or leaked.
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/eml-render");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("<html", "no email HTML body may be returned on the unauthorized path");
        content.Should().NotContain("<script", "no email HTML body may be returned on the unauthorized path");
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/me/capabilities")]
    public async Task UserEndpoints_ExistAndRequireAuth(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);

        // Should return a valid HTTP status (not 404) — the endpoint exists...
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);

        // ...and now requires authorization (task 023): an unauthenticated request is a
        // bare 401 auth challenge (no body), consistent with the app's other secured endpoints.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
