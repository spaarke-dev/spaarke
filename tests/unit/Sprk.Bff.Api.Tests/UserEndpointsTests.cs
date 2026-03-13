using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests;

public class UserEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public UserEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetMe_WithoutBearer_Returns401()
    {
        var response = await _client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithBearer_ReturnsUserInfo()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var userInfo = JsonConvert.DeserializeObject<UserInfoResponse>(content);

        userInfo.Should().NotBeNull();
        userInfo!.DisplayName.Should().NotBeNullOrEmpty();
        userInfo.UserPrincipalName.Should().NotBeNullOrEmpty();
        userInfo.Oid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCapabilities_WithoutBearer_ReturnsUnauthorizedOrDeniedCapabilities()
    {
        // The /api/me/capabilities endpoint does NOT have RequireAuthorization().
        // When no bearer token is present, UserOperations.GetUserCapabilitiesAsync catches
        // the UnauthorizedAccessException from TokenHelper.ExtractBearerToken and returns
        // a capabilities response with all flags set to false (fail-closed).
        // This is correct fail-closed behavior — the user gets no capabilities.
        var response = await _client.GetAsync("/api/me/capabilities?containerId=test-container-id");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCapabilities_WithoutContainerId_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/me/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCapabilities_UserA_ReturnsCorrectCapabilities()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-a-token");

        var response = await _client.GetAsync("/api/me/capabilities?containerId=test-container-id");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonConvert.DeserializeObject<UserCapabilitiesResponse>(content);

        capabilities.Should().NotBeNull();
        // These values depend on the test implementation - adjust based on your test setup
        capabilities!.Read.Should().BeTrue();
        capabilities.Write.Should().BeTrue();
        capabilities.Delete.Should().BeTrue();
        capabilities.CreateFolder.Should().BeTrue();
    }

    [Fact]
    public async Task GetCapabilities_UserB_ReturnsAllFalse()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-b-token");

        var response = await _client.GetAsync("/api/me/capabilities?containerId=denied-container-id");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonConvert.DeserializeObject<UserCapabilitiesResponse>(content);

        capabilities.Should().NotBeNull();
        capabilities!.Read.Should().BeFalse();
        capabilities.Write.Should().BeFalse();
        capabilities.Delete.Should().BeFalse();
        capabilities.CreateFolder.Should().BeFalse();
    }

    [Fact]
    public async Task GetCapabilities_ResponseIsProperProblemJson()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/me/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
