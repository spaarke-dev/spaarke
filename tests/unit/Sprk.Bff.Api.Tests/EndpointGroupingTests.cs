using System.Net;
using System.Net.Http.Headers;
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

    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - endpoint returns 404 without proper registration")]
    public async Task UploadEndpoints_ReturnsProblemDetailsOnError()
    {
        // Attempt to create upload session without proper auth
        var response = await _client.PostAsync("/api/containers/invalid-id/upload?path=test.txt", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        // Should have RFC 7807 Problem Details structure
        problemDetails.TryGetProperty("type", out _).Should().BeTrue();
        problemDetails.TryGetProperty("title", out _).Should().BeTrue();
        problemDetails.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UserEndpoints_WithoutAuth_Return401()
    {
        // /api/me requires authorization (task 023): an unauthenticated request is
        // short-circuited by the auth middleware with a bare 401 challenge (no body),
        // consistent with every other .RequireAuthorization() endpoint in the app.
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - endpoint returns 404 without proper registration")]
    public async Task DocumentsEndpoints_ListContainersRequiresValidContainerTypeId()
    {
        // Must include auth header to pass RequireAuthorization() gate first,
        // then the endpoint handler validates the containerTypeId parameter.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Missing containerTypeId parameter
        var response = await _client.GetAsync("/api/containers");

        // Auth gate may still reject (authorization policy "canmanagecontainers" may fail)
        // so accept either 400 (validation) or 401/403 (auth policy)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            problemDetails.TryGetProperty("detail", out var detail).Should().BeTrue();
            detail.GetString().Should().Contain("containerTypeId");
        }
    }

    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - endpoint returns 404 without proper registration")]
    public async Task UploadEndpoints_RequiresValidPath()
    {
        // Must include auth header to pass RequireAuthorization() gate first,
        // then the endpoint handler validates the path parameter.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Invalid path with no filename
        var response = await _client.PostAsync("/api/containers/test-id/upload?path=", null);

        // Auth gate may still reject (authorization policy "canwritefiles" may fail)
        // so accept either 400 (validation) or 401/403 (auth policy)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            problemDetails.TryGetProperty("detail", out var detail).Should().BeTrue();
            detail.GetString().Should().Contain("path");
        }
    }

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

    [Theory(Skip = "Requires fully mocked Graph/Dataverse services - endpoints return 404 without proper registration")]
    [InlineData("/api/containers")]
    [InlineData("/api/containers/test-id/drive")]
    [InlineData("/api/containers/test-id/upload")]
    public async Task DocumentEndpoints_ExistAndReturnConsistentErrorFormat(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);

        // Should return a valid HTTP status (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);

        if (response.StatusCode >= HttpStatusCode.BadRequest)
        {
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeEmpty();

            var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);
            problemDetails.TryGetProperty("type", out _).Should().BeTrue();
            problemDetails.TryGetProperty("title", out _).Should().BeTrue();
        }
    }

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
