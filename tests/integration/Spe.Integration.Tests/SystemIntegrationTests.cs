using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// System integration tests for the complete SDAP pipeline.
/// Tests API endpoints, Power Platform integration, and system workflows.
/// </summary>
public class SystemIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly HttpClient _httpClient;

    public SystemIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _httpClient = _fixture.CreateHttpClient();
    }

    [Fact]
    public async Task HealthCheck_ReturnsValidServiceInfo()
    {
        // Act
        var response = await _httpClient.GetAsync("/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var serviceInfo = JsonSerializer.Deserialize<JsonElement>(content);

        serviceInfo.TryGetProperty("service", out var serviceName).Should().BeTrue();
        serviceName.GetString().Should().Be("Sprk.Bff.Api");

        serviceInfo.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().NotBeNullOrEmpty();

        serviceInfo.TryGetProperty("environment", out var environment).Should().BeTrue();
        environment.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApiEndpoints_ReturnConsistentErrorFormat()
    {
        // Test multiple endpoints return RFC 7807 compliant errors
        var endpointsToTest = new[]
        {
            "/api/me",
            "/api/containers",
            "/api/containers/invalid-id/drive"
        };

        foreach (var endpoint in endpointsToTest)
        {
            // Act
            var response = await _httpClient.GetAsync(endpoint);

            // Assert - Should return error but in proper format
            response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeEmpty();

            var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);
            problemDetails.TryGetProperty("type", out _).Should().BeTrue($"Endpoint {endpoint} should return RFC 7807 compliant error");
            problemDetails.TryGetProperty("title", out _).Should().BeTrue($"Endpoint {endpoint} should have error title");
            problemDetails.TryGetProperty("status", out _).Should().BeTrue($"Endpoint {endpoint} should have status code");
        }
    }

    [Fact]
    public async Task EndpointGrouping_AllEndpointsAccessible()
    {
        // Test that all endpoint groups are properly registered
        var endpointGroups = new Dictionary<string, string[]>
        {
            ["User Endpoints"] = ["/api/me", "/api/me/capabilities"],
            ["Document Endpoints"] = ["/api/containers", "/api/drives/test/children"],
            ["Upload Endpoints"] = ["/api/containers/test/files/test.txt", "/api/upload-session/chunk"],
            ["OBO Endpoints"] = ["/api/obo/containers/test/children"]
        };

        foreach (var (groupName, endpoints) in endpointGroups)
        {
            foreach (var endpoint in endpoints)
            {
                // Act
                var response = await _httpClient.GetAsync(endpoint);

                // Assert - Should not return 404 (endpoint exists)
                response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                    $"Endpoint {endpoint} in group {groupName} should be accessible");
            }
        }
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnAllResponses()
    {
        // Act
        var response = await _httpClient.GetAsync("/ping");

        // Assert security headers are present
        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.Should().ContainKey("X-XSS-Protection");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    [Fact]
    public async Task CORS_ConfiguredCorrectly()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/ping");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    [Trait("Category", "PowerPlatform")]
    public async Task PowerPlatformIntegration_ValidatesPluginReadiness()
    {
        // This test would validate that Power Platform plugins are ready for integration
        // In a full implementation, this would test plugin deployment and registration

        // Arrange - Check if we're in an environment with Power Platform access
        var powerPlatformUrl = _fixture.Configuration["PowerPlatform:Url"];

        if (string.IsNullOrEmpty(powerPlatformUrl))
        {
            // Skip test if Power Platform is not configured
            return;
        }

        // Act & Assert - Placeholder for Power Platform integration validation
        // This would test:
        // 1. Plugin assembly deployment
        // 2. Plugin registration and step configuration
        // 3. Entity metadata validation
        // 4. Custom API availability

        await Task.CompletedTask; // Placeholder

        // For now, just verify the plugins build correctly
        var pluginAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Spaarke.Plugins.dll");
        File.Exists(pluginAssemblyPath).Should().BeTrue("Plugin assembly should be available for deployment");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ApiPerformance_MeetsResponseTimeRequirements()
    {
        // Test that critical endpoints meet performance requirements
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _httpClient.GetAsync("/ping");

        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "Health check should respond within 2 seconds");
    }
}
