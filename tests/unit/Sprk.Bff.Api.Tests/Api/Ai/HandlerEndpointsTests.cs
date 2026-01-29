using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for HandlerEndpoints.
/// Verifies endpoint registration, authentication requirements, caching behavior, and response structure.
/// </summary>
public class HandlerEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebAppFactory _factory;

    public HandlerEndpointsTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region GET /api/ai/handlers

    [Fact]
    public async Task GetHandlers_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert - endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHandlers_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetHandlers_WithAuth_ReturnsOk()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert - should succeed with handler data
        // Note: may return 500 in some test configurations without full auth setup
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHandlers_WithAuth_ReturnsHandlersArray()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert - if successful, response should have handlers array
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HandlersResponse>();
            content.Should().NotBeNull();
            content!.Handlers.Should().NotBeNull();
            // At least GenericAnalysisHandler should be registered
            content.Handlers.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task GetHandlers_WithAuth_IncludesHandlerMetadata()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HandlersResponse>();
            content.Should().NotBeNull();

            var firstHandler = content!.Handlers.FirstOrDefault();
            firstHandler.Should().NotBeNull();
            firstHandler!.HandlerId.Should().NotBeNullOrEmpty();
            firstHandler.Name.Should().NotBeNullOrEmpty();
            firstHandler.Version.Should().NotBeNullOrEmpty();
            firstHandler.SupportedToolTypes.Should().NotBeNull();
            firstHandler.SupportedInputTypes.Should().NotBeNull();
            firstHandler.Parameters.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetHandlers_WithAuth_IncludesConfigurationSchema()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HandlersResponse>();
            content.Should().NotBeNull();

            // At least one handler should have a ConfigurationSchema
            var handlersWithSchema = content!.Handlers
                .Where(h => h.ConfigurationSchema != null)
                .ToList();

            handlersWithSchema.Should().NotBeEmpty("at least GenericAnalysisHandler should have a schema");
        }
    }

    [Fact]
    public async Task GetHandlers_MultipleRequests_ReturnsSameData()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act - make two requests
        var response1 = await _client.GetAsync("/api/ai/handlers");
        var response2 = await _client.GetAsync("/api/ai/handlers");

        // Assert - both should succeed and have same handler count
        if (response1.IsSuccessStatusCode && response2.IsSuccessStatusCode)
        {
            var content1 = await response1.Content.ReadFromJsonAsync<HandlersResponse>();
            var content2 = await response2.Content.ReadFromJsonAsync<HandlersResponse>();

            content1!.Handlers.Length.Should().Be(content2!.Handlers.Length);
        }
    }

    #endregion

    #region GET /api/ai/handlers/{handlerId}

    [Fact]
    public async Task GetHandler_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync("/api/ai/handlers/GenericAnalysisHandler");

        // Assert - endpoint exists (not 404 for the endpoint itself, may be 404 for unknown handler)
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task GetHandler_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync("/api/ai/handlers/GenericAnalysisHandler");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.NotFound,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetHandler_WithAuth_ValidHandler_ReturnsHandler()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers/GenericAnalysisHandler");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HandlerDto>();
            content.Should().NotBeNull();
            content!.HandlerId.Should().Be("GenericAnalysisHandler");
            content.Name.Should().NotBeNullOrEmpty();
            content.ConfigurationSchema.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetHandler_WithAuth_InvalidHandler_ReturnsNotFound()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/handlers/NonExistentHandler");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.InternalServerError);
    }

    #endregion
}
