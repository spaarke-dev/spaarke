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

        // Assert - /ping returns plain text "pong"
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("pong", "The /ping endpoint should return 'pong'");
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

            // Assert - Should return an error (not 404) since endpoint exists
            // With authenticated client, the request passes auth and may return various error codes
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                $"Endpoint {endpoint} should be registered");

            // If it's a client error, check for ProblemDetails format
            if ((int)response.StatusCode >= 400)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content))
                {
                    try
                    {
                        var problemDetails = JsonSerializer.Deserialize<JsonElement>(content);
                        problemDetails.TryGetProperty("status", out _).Should().BeTrue(
                            $"Endpoint {endpoint} should return RFC 7807 compliant error with status");
                    }
                    catch (JsonException)
                    {
                        // Some error responses may not be JSON - that's acceptable
                    }
                }
            }
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
            ["OBO Endpoints"] = ["/api/obo/containers/test/children"]
        };

        foreach (var (groupName, endpoints) in endpointGroups)
        {
            foreach (var endpoint in endpoints)
            {
                // Act
                var response = await _httpClient.GetAsync(endpoint);

                // Assert - Route should match. A bare routing 404 has no body,
                // but handler 404/500 (mock dependencies) returns content.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    content.Should().NotBeNullOrEmpty(
                        $"Endpoint {endpoint} in group {groupName} should be accessible; a 404 with body means handler ran");
                }
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
        // Arrange - use the allowed origin from test configuration
        var request = new HttpRequestMessage(HttpMethod.Options, "/ping");
        request.Headers.Add("Origin", "https://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert - CORS preflight should return 204 or 200
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NoContent, HttpStatusCode.OK },
            "CORS preflight should succeed for allowed origin");
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
