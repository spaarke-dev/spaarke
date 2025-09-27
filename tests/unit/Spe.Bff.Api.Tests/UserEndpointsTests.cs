using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using System.Net;
using FluentAssertions;
using Newtonsoft.Json;
using Spe.Bff.Api.Models;
using Xunit;

namespace Spe.Bff.Api.Tests;

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
    public async Task GetCapabilities_WithoutBearer_Returns401()
    {
        var response = await _client.GetAsync("/api/me/capabilities?containerId=test-container-id");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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