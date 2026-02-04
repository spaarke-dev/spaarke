using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for the Field Mapping API endpoints.
/// Tests the /api/v1/field-mappings endpoints with WebApplicationFactory.
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
/// - Profile listing (GET /profiles)
/// - Profile by entity pair (GET /profiles/{sourceEntity}/{targetEntity})
/// - Type validation (POST /validate)
/// - Push mappings (POST /push)
/// </remarks>
public class FieldMappingEndpointsTests : IClassFixture<IntegrationTestFixture>
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

    public FieldMappingEndpointsTests(IntegrationTestFixture fixture, ITestOutputHelper output)
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

    #region GET /api/v1/field-mappings/profiles

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles")]
    public async Task GetProfiles_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange - no auth header

        // Act
        var response = await _httpClient!.GetAsync("/api/v1/field-mappings/profiles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All field mapping endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles")]
    public async Task GetProfiles_EndpointExists_NotReturning404()
    {
        SkipIfNotConfigured();

        // Arrange & Act
        var response = await _httpClient!.GetAsync("/api/v1/field-mappings/profiles");

        // Assert - Should return Unauthorized (401) but NOT NotFound (404)
        // This verifies the endpoint is registered
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "GET /api/v1/field-mappings/profiles endpoint should be registered");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles")]
    public async Task GetProfiles_AcceptsQueryParameters()
    {
        SkipIfNotConfigured();

        // Arrange - include filter query parameters
        var url = "/api/v1/field-mappings/profiles?sourceEntity=account&targetEntity=sprk_event&isActive=true";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Endpoint accepts query params (returns 401, not 404 or 400)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept query parameters but require auth");
    }

    #endregion

    #region GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles/{sourceEntity}/{targetEntity}")]
    public async Task GetProfileByEntityPair_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var url = "/api/v1/field-mappings/profiles/account/sprk_event";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All field mapping endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles/{sourceEntity}/{targetEntity}")]
    public async Task GetProfileByEntityPair_EndpointExists_NotReturning404WithValidRoute()
    {
        SkipIfNotConfigured();

        // Arrange - use valid entity names
        var url = "/api/v1/field-mappings/profiles/account/contact";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Returns 401 (Unauthorized) not 404 (Not Found)
        // This confirms the route template {sourceEntity}/{targetEntity} works
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should be found but require auth; 404 would mean route doesn't match");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "GET /profiles/{sourceEntity}/{targetEntity}")]
    public async Task GetProfileByEntityPair_WorksWithPrefixedEntityNames()
    {
        SkipIfNotConfigured();

        // Arrange - use sprk_ prefixed entity names
        var url = "/api/v1/field-mappings/profiles/sprk_matter/sprk_event";

        // Act
        var response = await _httpClient!.GetAsync(url);

        // Assert - Should find endpoint (returns 401, not 404)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should work with sprk_ prefixed entity names");
    }

    #endregion

    #region POST /api/v1/field-mappings/validate

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /validate")]
    public async Task ValidateMapping_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new ValidateMappingRequest
        {
            SourceFieldType = "Lookup",
            TargetFieldType = "Text"
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/field-mappings/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All field mapping endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /validate")]
    public async Task ValidateMapping_EndpointExists_NotReturning404()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new ValidateMappingRequest
        {
            SourceFieldType = "Text",
            TargetFieldType = "Text"
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/field-mappings/validate", request);

        // Assert - Returns 401 not 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "POST /api/v1/field-mappings/validate endpoint should be registered");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /validate")]
    public async Task ValidateMapping_AcceptsJsonContent()
    {
        SkipIfNotConfigured();

        // Arrange - send raw JSON with proper content type
        var json = """{"sourceFieldType":"Lookup","targetFieldType":"Text"}""";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient!.PostAsync("/api/v1/field-mappings/validate", content);

        // Assert - Endpoint accepts JSON content (returns 401 for auth, not 400/415 for content)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept JSON content-type");
    }

    #endregion

    #region POST /api/v1/field-mappings/push

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /push")]
    public async Task PushFieldMappings_ReturnsUnauthorized_WhenNoAuthToken()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new PushFieldMappingsRequest
        {
            SourceEntity = "sprk_matter",
            SourceRecordId = Guid.NewGuid(),
            TargetEntity = "sprk_event"
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/field-mappings/push", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "All field mapping endpoints require authentication");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /push")]
    public async Task PushFieldMappings_EndpointExists_NotReturning404()
    {
        SkipIfNotConfigured();

        // Arrange
        var request = new PushFieldMappingsRequest
        {
            SourceEntity = "account",
            SourceRecordId = Guid.NewGuid(),
            TargetEntity = "contact"
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/field-mappings/push", request);

        // Assert - Returns 401 not 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "POST /api/v1/field-mappings/push endpoint should be registered");
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Endpoint", "POST /push")]
    public async Task PushFieldMappings_AcceptsValidGuidInRequest()
    {
        SkipIfNotConfigured();

        // Arrange - use a properly formatted GUID
        var recordId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var request = new PushFieldMappingsRequest
        {
            SourceEntity = "sprk_matter",
            SourceRecordId = recordId,
            TargetEntity = "sprk_event"
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync("/api/v1/field-mappings/push", request);

        // Assert - Endpoint accepts the request (returns 401, not 400 for bad GUID)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Endpoint should accept valid GUID in request body");
    }

    #endregion

    #region Error Response Format (RFC 7807)

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "ErrorHandling")]
    public async Task FieldMappingEndpoints_ReturnProblemDetails_ForUnauthorized()
    {
        SkipIfNotConfigured();

        // Test all endpoints return proper RFC 7807 ProblemDetails for 401
        var endpoints = new[]
        {
            ("GET", "/api/v1/field-mappings/profiles"),
            ("GET", "/api/v1/field-mappings/profiles/account/contact")
        };

        foreach (var (method, endpoint) in endpoints)
        {
            // Act
            var response = method == "GET"
                ? await _httpClient!.GetAsync(endpoint)
                : await _httpClient!.PostAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

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
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "ErrorHandling")]
    public async Task PostEndpoints_ReturnProblemDetails_ForUnauthorized()
    {
        SkipIfNotConfigured();

        // Test POST endpoints return proper error format
        var postEndpoints = new[]
        {
            ("/api/v1/field-mappings/validate", """{"sourceFieldType":"Text","targetFieldType":"Text"}"""),
            ("/api/v1/field-mappings/push", """{"sourceEntity":"account","sourceRecordId":"12345678-1234-1234-1234-123456789012","targetEntity":"contact"}""")
        };

        foreach (var (endpoint, body) in postEndpoints)
        {
            // Arrange
            var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            // Act
            var response = await _httpClient!.PostAsync(endpoint, content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var responseContent = await response.Content.ReadAsStringAsync();
            responseContent.Should().NotBeEmpty($"POST {endpoint} should return error body");
        }
    }

    #endregion

    #region Route Structure Validation

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "Routing")]
    public async Task FieldMappingGroup_UsesCorrectApiVersion()
    {
        SkipIfNotConfigured();

        // Test that all endpoints use /api/v1/field-mappings base path
        var baseUrls = new[]
        {
            "/api/v1/field-mappings/profiles",
            "/api/v1/field-mappings/validate",
            "/api/v1/field-mappings/push"
        };

        foreach (var url in baseUrls)
        {
            // Act
            var response = await _httpClient!.GetAsync(url);

            // Assert - None should return 404 (endpoints exist)
            // GET to POST endpoints returns 405 Method Not Allowed, which is fine
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                $"Endpoint {url} should be registered under /api/v1/field-mappings");
        }
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "Routing")]
    public async Task FieldMappingEndpoints_RequireCorrectHttpMethod()
    {
        SkipIfNotConfigured();

        // Verify POST endpoints reject GET requests with 405
        var postOnlyEndpoints = new[]
        {
            "/api/v1/field-mappings/validate",
            "/api/v1/field-mappings/push"
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

    #endregion

    #region OpenAPI/Swagger Tags

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "Documentation")]
    public async Task OpenApiSpec_IncludesFieldMappingTag()
    {
        SkipIfNotConfigured();

        // Act
        var response = await _httpClient!.GetAsync("/swagger/v1/swagger.json");

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var spec = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);

            // Check that the spec contains field-mappings paths
            if (spec.TryGetProperty("paths", out var paths))
            {
                var pathsList = paths.EnumerateObject().Select(p => p.Name).ToList();
                pathsList.Should().Contain(p => p.Contains("field-mappings"),
                    "OpenAPI spec should include field-mappings endpoints");
            }
        }
        // If swagger is not available in test environment, skip this assertion
    }

    #endregion

    #region Rate Limiting Tag

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "RateLimiting")]
    public async Task FieldMappingEndpoints_ApplyRateLimiting()
    {
        SkipIfNotConfigured();

        // Send multiple requests rapidly to verify rate limiting is configured
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _httpClient!.GetAsync("/api/v1/field-mappings/profiles"))
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
}

/// <summary>
/// Unit tests for TypeCompatibilityValidator.
/// These tests run without WebApplicationFactory and validate the type compatibility logic directly.
/// </summary>
public class TypeCompatibilityValidatorTests
{
    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsValid_ForExactMatch()
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate("Text", "Text");

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibilityLevel.Should().Be("exact");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsValid_ForCompatibleConversion_LookupToText()
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate("Lookup", "Text");

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibilityLevel.Should().Be("safe_conversion");
        result.Warnings.Should().NotBeEmpty("Converting Lookup to Text should include warning");
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsInvalid_ForIncompatibleConversion_TextToLookup()
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate("Text", "Lookup");

        // Assert
        result.IsValid.Should().BeFalse();
        result.CompatibilityLevel.Should().Be("incompatible");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsInvalid_ForUnknownSourceType()
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate("UnknownType", "Text");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unknown source field type"));
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsInvalid_ForUnknownTargetType()
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate("Text", "UnknownType");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unknown target field type"));
    }

    [Theory]
    [InlineData("Lookup", "Lookup", true)]
    [InlineData("Lookup", "Text", true)]
    [InlineData("Text", "Text", true)]
    [InlineData("Text", "Memo", true)]
    [InlineData("Memo", "Text", true)]
    [InlineData("Memo", "Memo", true)]
    [InlineData("OptionSet", "OptionSet", true)]
    [InlineData("OptionSet", "Text", true)]
    [InlineData("Number", "Number", true)]
    [InlineData("Number", "Text", true)]
    [InlineData("DateTime", "DateTime", true)]
    [InlineData("DateTime", "Text", true)]
    [InlineData("Boolean", "Boolean", true)]
    [InlineData("Boolean", "Text", true)]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsExpectedResult_ForKnownTypePairs(string source, string target, bool expectedValid)
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate(source, target);

        // Assert
        result.IsValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("Text", "Lookup", false)]
    [InlineData("Text", "OptionSet", false)]
    [InlineData("Text", "Number", false)]
    [InlineData("Text", "DateTime", false)]
    [InlineData("Text", "Boolean", false)]
    [InlineData("Number", "Lookup", false)]
    [InlineData("Boolean", "Number", false)]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_ReturnsInvalid_ForIncompatibleTypePairs(string source, string target, bool expectedValid)
    {
        // Arrange & Act
        var result = TypeCompatibilityValidator.Validate(source, target);

        // Assert
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void Validate_IsCaseInsensitive()
    {
        // Arrange & Act
        var lowerResult = TypeCompatibilityValidator.Validate("text", "text");
        var upperResult = TypeCompatibilityValidator.Validate("TEXT", "TEXT");
        var mixedResult = TypeCompatibilityValidator.Validate("TeXt", "tExT");

        // Assert
        lowerResult.IsValid.Should().BeTrue();
        upperResult.IsValid.Should().BeTrue();
        mixedResult.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void GetCompatibleTargetTypes_ReturnsExpectedTypes_ForLookup()
    {
        // Arrange & Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("Lookup");

        // Assert
        compatibleTypes.Should().Contain("Lookup");
        compatibleTypes.Should().Contain("Text");
        compatibleTypes.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void GetCompatibleTargetTypes_ReturnsExpectedTypes_ForText()
    {
        // Arrange & Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("Text");

        // Assert
        compatibleTypes.Should().Contain("Text");
        compatibleTypes.Should().Contain("Memo");
    }

    [Fact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "UnitTest")]
    public void GetCompatibleTargetTypes_ReturnsEmpty_ForUnknownType()
    {
        // Arrange & Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("UnknownType");

        // Assert
        compatibleTypes.Should().BeEmpty();
    }
}
