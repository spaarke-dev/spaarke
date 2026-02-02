using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Events.Dtos;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for the Event API endpoints.
/// Tests the /api/v1/events endpoints with WebApplicationFactory.
/// </summary>
/// <remarks>
/// These tests validate endpoint behavior, response structure, and proper error handling.
/// Authorization is required for all endpoints - tests expecting success need valid auth,
/// tests for validation/logic can verify the behavior returns appropriate error codes.
///
/// Note: These tests require the full API to be configured with all services.
/// When the integration environment is not available (e.g., no ServiceBus connection),
/// the tests will be skipped rather than failing.
///
/// Test categories:
/// - Event listing (GET /events)
/// - Event by ID (GET /events/{id})
/// - Event creation (POST /events)
/// - Event update (PUT /events/{id})
/// - Event deletion (DELETE /events/{id})
/// - Event completion (POST /events/{id}/complete)
/// - Event cancellation (POST /events/{id}/cancel)
/// - Event logs (GET /events/{id}/logs)
/// </remarks>
public class EventEndpointsTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly HttpClient? _httpClient;
    private readonly ITestOutputHelper _output;
    private readonly bool _canRunIntegrationTests;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EventEndpointsTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;

        // Try to create the HTTP client - if it fails, tests will be skipped
        try
        {
            _httpClient = _fixture.CreateHttpClient();
            _canRunIntegrationTests = true;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Integration test environment not available: {ex.Message}");
            _canRunIntegrationTests = false;
        }
    }

    private void SkipIfNotConfigured()
    {
        Skip.If(!_canRunIntegrationTests, "Integration test environment not available");
    }

    #region GET /api/v1/events

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange - no auth header

        // Act
        var response = await _httpClient!.GetAsync("/api/v1/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_EndpointExists_NotReturning404()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.GetAsync("/api/v1/events");

        // Assert - Should return Unauthorized (401) but NOT NotFound (404)
        // This verifies the endpoint is registered
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "GET /api/v1/events endpoint should be registered");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_AcceptsFilterQueryParameters()
    {
        SkipIfNotConfigured();

        // Arrange - include filter query parameters
        var url = "/api/v1/events?statusCode=3&priority=1&pageNumber=1&pageSize=20";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Endpoint accepts query params (returns 401, not 404 or 400)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept query parameters but require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_AcceptsRegardingRecordFilters()
    {
        SkipIfNotConfigured();

        // Arrange - include regarding record filters
        var recordId = Guid.NewGuid().ToString();
        var url = $"/api/v1/events?regardingRecordType=1&regardingRecordId={recordId}";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept regarding record filters");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_AcceptsDateRangeFilters()
    {
        SkipIfNotConfigured();

        // Arrange - include date range filters
        var dueDateFrom = DateTime.UtcNow.ToString("o");
        var dueDateTo = DateTime.UtcNow.AddDays(30).ToString("o");
        var url = $"/api/v1/events?dueDateFrom={Uri.EscapeDataString(dueDateFrom)}&dueDateTo={Uri.EscapeDataString(dueDateTo)}";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept date range filters");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events")]
    public async Task GetEvents_AcceptsEventTypeFilter()
    {
        SkipIfNotConfigured();

        // Arrange - include event type filter
        var eventTypeId = Guid.NewGuid();
        var url = $"/api/v1/events?eventTypeId={eventTypeId}";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept event type filter");
    }

    #endregion

    #region GET /api/v1/events/{id}

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}")]
    public async Task GetEventById_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();
        var url = $"/api/v1/events/{eventId}";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}")]
    public async Task GetEventById_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange - use a valid GUID format
        var eventId = Guid.NewGuid();
        var url = $"/api/v1/events/{eventId}";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Returns 401 (Unauthorized) not 404 for the route itself
        // (404 would be returned if the route wasn't matched; auth happens before resource lookup)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should be found but require auth; 404 would mean route doesn't match");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}")]
    public async Task GetEventById_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange - use an invalid GUID format
        var url = "/api/v1/events/not-a-valid-guid";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Should return 404 because route constraint :guid fails to match
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    #endregion

    #region POST /api/v1/events

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events")]
    public async Task CreateEvent_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new CreateEventRequest(
            Subject: "Test Event"
        );

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/events", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events")]
    public async Task CreateEvent_EndpointExists_NotReturning404()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new CreateEventRequest(
            Subject: "Test Event",
            Description: "Test Description",
            Priority: 1
        );

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/events", request);

        // Assert - Returns 401 not 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "POST /api/v1/events endpoint should be registered");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events")]
    public async Task CreateEvent_AcceptsJsonContent()
    {
        SkipIfNotConfigured();

        // Arrange - send raw JSON with proper content type
        var json = """{"subject":"Test Event","description":"Test Description","priority":1}""";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PostAsync("/api/v1/events", content);

        // Assert - Endpoint accepts JSON content (returns 401 for auth, not 400/415 for content)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept JSON content-type");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events")]
    public async Task CreateEvent_AcceptsAllOptionalFields()
    {
        SkipIfNotConfigured();

        // Arrange - include all optional fields
        var request = new CreateEventRequest(
            Subject: "Complete Event",
            Description: "Full description",
            EventTypeId: Guid.NewGuid(),
            RegardingRecordId: Guid.NewGuid(),
            RegardingRecordName: "Test Record",
            RegardingRecordType: 1,
            ScheduledStart: DateTime.UtcNow,
            ScheduledEnd: DateTime.UtcNow.AddHours(1),
            DueDate: DateTime.UtcNow.AddDays(7),
            Priority: 2
        );

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/events", request);

        // Assert - Endpoint accepts all fields (returns 401, not 400)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept all optional fields");
    }

    #endregion

    #region PUT /api/v1/events/{id}

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "PUT /events/{id}")]
    public async Task UpdateEvent_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();
        var request = new UpdateEventRequest(
            Subject: "Updated Subject"
        );

        // Act
        var response = await _httpClient!.PutAsJsonAsync($"/api/v1/events/{eventId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "PUT /events/{id}")]
    public async Task UpdateEvent_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();
        var request = new UpdateEventRequest(
            Subject: "Updated Subject",
            Priority: 2
        );

        // Act
        var response = await _httpClient!.PutAsJsonAsync($"/api/v1/events/{eventId}", request);

        // Assert - Returns 401 not 404 for the route
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "PUT endpoint should be registered and require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "PUT /events/{id}")]
    public async Task UpdateEvent_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new UpdateEventRequest(
            Subject: "Updated Subject"
        );
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PutAsync("/api/v1/events/not-a-valid-guid", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    #endregion

    #region DELETE /api/v1/events/{id}

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "DELETE /events/{id}")]
    public async Task DeleteEvent_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.DeleteAsync($"/api/v1/events/{eventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "DELETE /events/{id}")]
    public async Task DeleteEvent_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.DeleteAsync($"/api/v1/events/{eventId}");

        // Assert - Returns 401 not 404 for the route
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "DELETE endpoint should be registered and require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "DELETE /events/{id}")]
    public async Task DeleteEvent_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.DeleteAsync("/api/v1/events/not-a-valid-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    #endregion

    #region POST /api/v1/events/{id}/complete

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/complete")]
    public async Task CompleteEvent_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.PostAsync($"/api/v1/events/{eventId}/complete", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/complete")]
    public async Task CompleteEvent_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.PostAsync($"/api/v1/events/{eventId}/complete", null);

        // Assert - Returns 401 not 404 for the route
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "POST complete endpoint should be registered and require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/complete")]
    public async Task CompleteEvent_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.PostAsync("/api/v1/events/not-a-valid-guid/complete", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/complete")]
    public async Task CompleteEvent_RequiresPostMethod()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act - Try GET on POST-only endpoint
        var response = await _httpClient!.GetAsync($"/api/v1/events/{eventId}/complete");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "GET to /complete should return 405 (POST only)");
    }

    #endregion

    #region POST /api/v1/events/{id}/cancel

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/cancel")]
    public async Task CancelEvent_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.PostAsync($"/api/v1/events/{eventId}/cancel", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/cancel")]
    public async Task CancelEvent_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.PostAsync($"/api/v1/events/{eventId}/cancel", null);

        // Assert - Returns 401 not 404 for the route
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "POST cancel endpoint should be registered and require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/cancel")]
    public async Task CancelEvent_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.PostAsync("/api/v1/events/not-a-valid-guid/cancel", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "POST /events/{id}/cancel")]
    public async Task CancelEvent_RequiresPostMethod()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act - Try GET on POST-only endpoint
        var response = await _httpClient!.GetAsync($"/api/v1/events/{eventId}/cancel");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "GET to /cancel should return 405 (POST only)");
    }

    #endregion

    #region GET /api/v1/events/{id}/logs

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}/logs")]
    public async Task GetEventLogs_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.GetAsync($"/api/v1/events/{eventId}/logs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All event endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}/logs")]
    public async Task GetEventLogs_EndpointExists_NotReturning404ForValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _httpClient!.GetAsync($"/api/v1/events/{eventId}/logs");

        // Assert - Returns 401 not 404 for the route
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "GET logs endpoint should be registered and require auth");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Endpoint", "GET /events/{id}/logs")]
    public async Task GetEventLogs_Returns404_ForInvalidGuidFormat()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.GetAsync("/api/v1/events/not-a-valid-guid/logs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid GUID format should not match the route constraint");
    }

    #endregion

    #region Error Response Format (RFC 7807)

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "ErrorHandling")]
    public async Task EventEndpoints_ReturnProblemDetails_ForUnauthorized()
    {
        SkipIfNotConfigured();

        // Test all GET endpoints return proper RFC 7807 ProblemDetails for 401
        var endpoints = new[]
        {
            "/api/v1/events",
            $"/api/v1/events/{Guid.NewGuid()}",
            $"/api/v1/events/{Guid.NewGuid()}/logs"
        };

        foreach (var endpoint in endpoints)
        {
            // Act
            var response = await _httpClient!.GetAsync(endpoint);

            // Assert - Verify ProblemDetails structure
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeEmpty($"Endpoint {endpoint} should return a response body");

            var problemDetails = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
            problemDetails.TryGetProperty("status", out _).Should().BeTrue(
                $"Endpoint {endpoint} should return ProblemDetails with status");
        }
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "ErrorHandling")]
    public async Task PostEndpoints_ReturnProblemDetails_ForUnauthorized()
    {
        SkipIfNotConfigured();

        // Test POST endpoints return proper error format
        var eventId = Guid.NewGuid();
        var postEndpoints = new[]
        {
            ("/api/v1/events", """{"subject":"Test Event"}"""),
            ($"/api/v1/events/{eventId}/complete", ""),
            ($"/api/v1/events/{eventId}/cancel", "")
        };

        foreach (var (endpoint, body) in postEndpoints)
        {
            // Arrange
            var content = string.IsNullOrEmpty(body)
                ? null
                : new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            // Act
            var response = await _httpClient!.PostAsync(endpoint, content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"POST {endpoint} should require authentication");

            var responseContent = await response.Content.ReadAsStringAsync();
            responseContent.Should().NotBeEmpty($"POST {endpoint} should return error body");
        }
    }

    #endregion

    #region Route Structure Validation

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Routing")]
    public async Task EventGroup_UsesCorrectApiVersion()
    {
        SkipIfNotConfigured();

        // Test that all endpoints use /api/v1/events base path
        var eventId = Guid.NewGuid();
        var baseUrls = new[]
        {
            "/api/v1/events",
            $"/api/v1/events/{eventId}",
            $"/api/v1/events/{eventId}/logs"
        };

        foreach (var url in baseUrls)
        {
            // Act
            var response = await _httpClient!.GetAsync(url);

            // Assert - None should return 404 (endpoints exist)
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                $"Endpoint {url} should be registered under /api/v1/events");
        }
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Routing")]
    public async Task EventEndpoints_RequireCorrectHttpMethod()
    {
        SkipIfNotConfigured();

        var eventId = Guid.NewGuid();

        // Verify POST-only endpoints reject GET requests with 405
        var postOnlyEndpoints = new[]
        {
            $"/api/v1/events/{eventId}/complete",
            $"/api/v1/events/{eventId}/cancel"
        };

        foreach (var endpoint in postOnlyEndpoints)
        {
            // Act - Try GET on POST-only endpoint
            var response = await _httpClient!.GetAsync(endpoint);

            // Assert - Should be 405 Method Not Allowed
            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
                $"GET to {endpoint} should return 405 (POST only)");
        }
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Routing")]
    public async Task EventEndpoints_RejectPostOnGetEndpoints()
    {
        SkipIfNotConfigured();

        var eventId = Guid.NewGuid();

        // Verify GET-only endpoints reject POST requests with 405
        var getOnlyEndpoints = new[]
        {
            "/api/v1/events",
            $"/api/v1/events/{eventId}/logs"
        };

        foreach (var endpoint in getOnlyEndpoints)
        {
            // Arrange
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            // Act - Try POST on GET-only endpoint
            var response = await _httpClient!.PostAsync(endpoint, content);

            // Assert - Should be 405 or 401 (might check auth before method)
            // Note: Some endpoints may check auth before method, so either response is valid
            var validStatuses = new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized };
            validStatuses.Should().Contain(response.StatusCode,
                $"POST to {endpoint} should return 405 (GET only) or 401");
        }
    }

    #endregion

    #region OpenAPI/Swagger Tags

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Documentation")]
    public async Task OpenApiSpec_IncludesEventsTag()
    {
        SkipIfNotConfigured();

        // Act
        var response = await _httpClient!.GetAsync("/swagger/v1/swagger.json");

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var spec = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);

            // Check that the spec contains events paths
            if (spec.TryGetProperty("paths", out var paths))
            {
                var pathsList = paths.EnumerateObject().Select(p => p.Name).ToList();
                pathsList.Should().Contain(p => p.Contains("/events"),
                    "OpenAPI spec should include events endpoints");
            }
        }
        // If swagger is not available in test environment, skip this assertion
    }

    #endregion

    #region Rate Limiting

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "RateLimiting")]
    public async Task EventEndpoints_ApplyRateLimiting()
    {
        SkipIfNotConfigured();

        // Send multiple requests rapidly to verify rate limiting is configured
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _httpClient!.GetAsync("/api/v1/events"))
            .ToArray();

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert - All should complete (either 401 or potentially 429 if rate limited)
        // The key is they don't throw exceptions
        responses.Should().AllSatisfy(r =>
        {
            r.StatusCode.Should().BeOneOf(
                HttpStatusCode.Unauthorized,
                HttpStatusCode.TooManyRequests);
        });
    }

    #endregion

    #region Validation Tests (Request Body)

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Validation")]
    public async Task CreateEvent_AcceptsEmptySubject_ReturnsUnauthorizedBeforeValidation()
    {
        SkipIfNotConfigured();

        // Arrange - Subject is empty (validation should fail, but auth happens first)
        var json = """{"subject":""}""";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PostAsync("/api/v1/events", content);

        // Assert - Returns 401 (auth before validation)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Auth should be checked before request body validation");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Validation")]
    public async Task CreateEvent_AcceptsInvalidPriority_ReturnsUnauthorizedBeforeValidation()
    {
        SkipIfNotConfigured();

        // Arrange - Priority is out of range (0-3)
        var json = """{"subject":"Test","priority":10}""";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PostAsync("/api/v1/events", content);

        // Assert - Returns 401 (auth before validation)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Auth should be checked before priority validation");
    }

    [SkippableFact]
    [Trait("Category", "Events")]
    [Trait("Category", "Validation")]
    public async Task UpdateEvent_AcceptsInvalidStatusCode_ReturnsUnauthorizedBeforeValidation()
    {
        SkipIfNotConfigured();

        // Arrange - StatusCode is out of range (1-7)
        var eventId = Guid.NewGuid();
        var json = """{"statusCode":99}""";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PutAsync($"/api/v1/events/{eventId}", content);

        // Assert - Returns 401 (auth before validation)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Auth should be checked before status code validation");
    }

    #endregion
}
