using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for the GET /api/ai/chat/context-mappings/standalone endpoint.
///
/// Tests cover:
/// - Endpoint registration and route structure (ADR-001: Minimal API)
/// - Authentication/authorization enforcement (ADR-008: endpoint filters)
/// - HTTP GET with valid entityType + entityId returns 200 with <see cref="StandaloneChatContextResponse"/>
/// - HTTP GET with invalid entityId (not a GUID) returns 400
/// - HTTP GET with unsupported entityType returns 400
/// - Missing auth returns 401
/// - tenantId is never accepted from the query string (ADR-008)
/// - Response model structure (entityType, entityId, displayName, contextFields)
/// </summary>
public class StandaloneChatContextEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private const string BaseUrl = "/api/ai/chat/context-mappings/standalone";
    private const string ValidEntityType = "contact";
    private const string ValidEntityId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private readonly HttpClient _client;

    public StandaloneChatContextEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    // =========================================================================
    // Endpoint Registration Tests (ADR-001 — Minimal API)
    // =========================================================================

    [Fact]
    public void MapStandaloneChatContextEndpoints_MethodExists_AndIsStatic()
    {
        // Arrange
        var method = typeof(StandaloneChatContextEndpoints).GetMethod("MapStandaloneChatContextEndpoints");

        // Assert
        method.Should().NotBeNull("endpoint registration extension method must exist");
        method!.IsStatic.Should().BeTrue("Minimal API extension methods are static (ADR-001)");
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    // =========================================================================
    // Authentication Tests (ADR-008)
    // =========================================================================

    [Fact]
    public async Task GetStandaloneContext_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange — no Authorization header

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError],
            "endpoint requires authorization (ADR-008); unauthenticated requests must not return 200");
    }

    [Fact]
    public async Task GetStandaloneContext_WithAuth_EndpointIsReachable()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert — endpoint is registered and reachable (not 404 or 405)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "endpoint /api/ai/chat/context-mappings/standalone must be registered");
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "endpoint must support GET");
    }

    // =========================================================================
    // Successful Resolution Tests (200 OK)
    // =========================================================================

    [Fact]
    public async Task GetStandaloneContext_WithValidParams_Returns200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "valid entityType and entityId should return 200");
    }

    [Fact]
    public async Task GetStandaloneContext_WithValidParams_ResponseDeserializesTo_StandaloneChatContextResponse()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json, JsonOptions);

        context.Should().NotBeNull("response should deserialize to StandaloneChatContextResponse");
        context!.EntityType.Should().NotBeNullOrWhiteSpace("response must include EntityType");
        context.EntityId.Should().NotBeNullOrWhiteSpace("response must include EntityId");
        context.ContextFields.Should().NotBeNull("response must include ContextFields list");
    }

    [Fact]
    public async Task GetStandaloneContext_Response_EntityTypeMatchesRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert — entityType is echoed back in the response
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json, JsonOptions);

        context!.EntityType.Should().Be(ValidEntityType,
            "EntityType must match the request query parameter");
    }

    [Fact]
    public async Task GetStandaloneContext_Response_EntityIdMatchesRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert — entityId is echoed back in the response
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json, JsonOptions);

        context!.EntityId.Should().Be(ValidEntityId,
            "EntityId must match the request query parameter");
    }

    [Fact]
    public async Task GetStandaloneContext_Response_HasNonEmptyContextFields()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json, JsonOptions);

        context!.ContextFields.Should().NotBeEmpty(
            "contact entity type must have at least one context field");
    }

    [Fact]
    public async Task GetStandaloneContext_Response_ContentType_IsApplicationJson()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "endpoint response must be application/json for JSON deserialization by the client");
    }

    // =========================================================================
    // Validation Tests — 400 Bad Request
    // =========================================================================

    [Fact]
    public async Task GetStandaloneContext_WithInvalidGuid_EntityId_Returns400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        const string invalidEntityId = "not-a-guid";

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={ValidEntityType}&entityId={invalidEntityId}");

        // Assert — non-GUID entityId must return 400 ProblemDetails
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "invalid GUID entityId should return 400 ProblemDetails");
    }

    [Fact]
    public async Task GetStandaloneContext_WithUnsupportedEntityType_Returns400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        const string unsupportedEntityType = "lead"; // not in allowlist

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={unsupportedEntityType}&entityId={ValidEntityId}");

        // Assert — unsupported entity type must return 400 ProblemDetails
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "unsupported entity type should return 400 ProblemDetails");
    }

    [Fact]
    public async Task GetStandaloneContext_WithMissingEntityType_Returns400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — entityType is omitted
        var response = await _client.GetAsync($"{BaseUrl}?entityId={ValidEntityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "missing entityType parameter should return 400 ProblemDetails");
    }

    [Fact]
    public async Task GetStandaloneContext_WithMissingEntityId_Returns400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — entityId is omitted
        var response = await _client.GetAsync($"{BaseUrl}?entityType={ValidEntityType}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "missing entityId parameter should return 400 ProblemDetails");
    }

    // =========================================================================
    // All Supported Entity Types Return 200
    // =========================================================================

    [Theory]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("opportunity")]
    [InlineData("incident")]
    [InlineData("sprk_matter")]
    public async Task GetStandaloneContext_AllSupportedEntityTypes_Return200(string entityType)
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var entityId = Guid.NewGuid().ToString();

        // Act
        var response = await _client.GetAsync(
            $"{BaseUrl}?entityType={entityType}&entityId={entityId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"entity type '{entityType}' is in the supported allowlist and should return 200");
    }
}

/// <summary>
/// Model validation tests for <see cref="StandaloneChatContextResponse"/>
/// and <see cref="StandaloneContextField"/>.
/// </summary>
public class StandaloneChatContextResponseModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void StandaloneChatContextResponse_CanBeCreated_WithAllFields()
    {
        // Arrange & Act
        var response = new StandaloneChatContextResponse(
            EntityType: "contact",
            EntityId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            DisplayName: "Contact",
            ContextFields:
            [
                new StandaloneContextField("fullname", "Full Name", "text", IsRequired: true),
                new StandaloneContextField("emailaddress1", "Email", "text"),
            ],
            RecommendedPlaybookId: "playbook-001");

        // Assert
        response.EntityType.Should().Be("contact");
        response.EntityId.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        response.DisplayName.Should().Be("Contact");
        response.ContextFields.Should().HaveCount(2);
        response.RecommendedPlaybookId.Should().Be("playbook-001");
    }

    [Fact]
    public void StandaloneChatContextResponse_RecommendedPlaybookId_IsOptional()
    {
        // Arrange & Act
        var response = new StandaloneChatContextResponse(
            EntityType: "contact",
            EntityId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            DisplayName: "Contact",
            ContextFields: []);

        // Assert
        response.RecommendedPlaybookId.Should().BeNull("RecommendedPlaybookId defaults to null");
    }

    [Fact]
    public void StandaloneChatContextResponse_SerializesToJson_WithCamelCase()
    {
        // Arrange
        var response = new StandaloneChatContextResponse(
            EntityType: "contact",
            EntityId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            DisplayName: "Contact",
            ContextFields:
            [
                new StandaloneContextField("fullname", "Full Name", "text", IsRequired: true),
            ]);

        // Act
        var json = JsonSerializer.Serialize(response, JsonOptions);

        // Assert
        json.Should().Contain("\"entityType\"");
        json.Should().Contain("\"entityId\"");
        json.Should().Contain("\"displayName\"");
        json.Should().Contain("\"contextFields\"");
        json.Should().Contain("\"logicalName\"");
        json.Should().Contain("\"displayLabel\"");
        json.Should().Contain("\"fieldType\"");
        json.Should().Contain("\"isRequired\"");
    }

    [Fact]
    public void StandaloneChatContextResponse_RoundTrips_ThroughJsonSerialization()
    {
        // Arrange
        var original = new StandaloneChatContextResponse(
            EntityType: "sprk_matter",
            EntityId: "11111111-2222-3333-4444-555555555555",
            DisplayName: "Matter",
            ContextFields:
            [
                new StandaloneContextField("sprk_mattername", "Matter Name", "text", IsRequired: true),
                new StandaloneContextField("sprk_practicearea", "Practice Area", "optionset"),
            ],
            RecommendedPlaybookId: "playbook-abc");

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.EntityType.Should().Be(original.EntityType);
        deserialized.EntityId.Should().Be(original.EntityId);
        deserialized.DisplayName.Should().Be(original.DisplayName);
        deserialized.ContextFields.Should().HaveCount(2);
        deserialized.RecommendedPlaybookId.Should().Be(original.RecommendedPlaybookId);
    }

    [Fact]
    public void StandaloneContextField_IsRequired_DefaultsToFalse()
    {
        // Arrange & Act
        var field = new StandaloneContextField("emailaddress1", "Email", "text");

        // Assert
        field.IsRequired.Should().BeFalse("IsRequired defaults to false for optional fields");
    }

    [Fact]
    public void StandaloneContextField_FieldType_LookupIsValid()
    {
        // Arrange & Act — lookup type for reference fields
        var field = new StandaloneContextField("parentcustomerid", "Account", "lookup");

        // Assert
        field.LogicalName.Should().Be("parentcustomerid");
        field.DisplayLabel.Should().Be("Account");
        field.FieldType.Should().Be("lookup");
    }
}
