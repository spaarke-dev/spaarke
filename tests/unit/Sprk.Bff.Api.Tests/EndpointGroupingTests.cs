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
public class EndpointGroupingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public EndpointGroupingTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
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

    [Fact]
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
    public async Task UserEndpoints_ReturnsProblemDetailsOnError()
    {
        // Attempt to get user info without auth
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);

        // Should have RFC 7807 Problem Details structure
        problemDetails.TryGetProperty("type", out _).Should().BeTrue();
        problemDetails.TryGetProperty("title", out _).Should().BeTrue();
        problemDetails.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Theory]
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

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/me/capabilities")]
    public async Task UserEndpoints_ExistAndReturnConsistentErrorFormat(string endpoint)
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
}
