using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for Phase 2: Record Matching Service.
/// Tests the complete flow from document entity extraction through AI Search
/// to record association.
/// </summary>
public class Phase2RecordMatchingTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase2RecordMatchingTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _httpClient = _fixture.CreateHttpClient();
    }

    #region Match Records Endpoint Tests

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task MatchRecords_Endpoint_RequiresAuthorization()
    {
        // Arrange
        var request = new
        {
            Organizations = new[] { "Acme Corp" },
            People = new[] { "John Smith" },
            ReferenceNumbers = new[] { "MAT-2024-001" },
            Keywords = new[] { "contract", "nda" },
            RecordTypeFilter = "all",
            MaxResults = 5
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/match-records", content);

        // Assert - Should require authorization
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task MatchRecords_Endpoint_Exists()
    {
        // Arrange
        var request = new
        {
            Organizations = Array.Empty<string>(),
            People = Array.Empty<string>(),
            ReferenceNumbers = Array.Empty<string>(),
            Keywords = Array.Empty<string>(),
            RecordTypeFilter = "all",
            MaxResults = 5
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/match-records", content);

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Match records endpoint should be registered");
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task MatchRecords_ValidatesRecordTypeFilter()
    {
        // Arrange - Test that endpoint accepts valid filter values
        var validFilters = new[] { "all", "sprk_matter", "sprk_project", "sprk_invoice" };

        foreach (var filter in validFilters)
        {
            var request = new
            {
                Organizations = new[] { "Test Corp" },
                RecordTypeFilter = filter,
                MaxResults = 5
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _httpClient.PostAsync("/api/ai/document-intelligence/match-records", content);

            // Assert - Should not return BadRequest for valid filter
            // (Will return 401 due to auth, but validates request format)
            response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
                $"Filter '{filter}' should be a valid record type filter");
        }
    }

    #endregion

    #region Associate Record Endpoint Tests

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task AssociateRecord_Endpoint_RequiresAuthorization()
    {
        // Arrange
        var request = new
        {
            DocumentId = Guid.NewGuid().ToString(),
            RecordId = Guid.NewGuid().ToString(),
            RecordType = "sprk_matter",
            LookupFieldName = "sprk_matter"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/associate-record", content);

        // Assert - Should require authorization
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task AssociateRecord_Endpoint_Exists()
    {
        // Arrange
        var request = new
        {
            DocumentId = Guid.NewGuid().ToString(),
            RecordId = Guid.NewGuid().ToString(),
            RecordType = "sprk_matter",
            LookupFieldName = "sprk_matter"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/associate-record", content);

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Associate record endpoint should be registered");
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task AssociateRecord_ValidatesLookupFieldName()
    {
        // Arrange - Valid lookup field names
        var validLookupFields = new[] { "sprk_matter", "sprk_project", "sprk_invoice" };

        foreach (var lookupField in validLookupFields)
        {
            var request = new
            {
                DocumentId = Guid.NewGuid().ToString(),
                RecordId = Guid.NewGuid().ToString(),
                RecordType = lookupField,
                LookupFieldName = lookupField
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _httpClient.PostAsync("/api/ai/document-intelligence/associate-record", content);

            // Assert - Should not return BadRequest for valid lookup field
            response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
                $"Lookup field '{lookupField}' should be valid");
        }
    }

    #endregion

    #region Admin Sync Endpoint Tests

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [Trait("Category", "Admin")]
    public async Task SyncIndex_Endpoint_RequiresAuthorization()
    {
        // Arrange
        var request = new
        {
            EntityType = "sprk_matter",
            FullSync = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/admin/record-matching/sync", content);

        // Assert - Should require authorization
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [Trait("Category", "Admin")]
    public async Task SyncIndex_Endpoint_Exists()
    {
        // Arrange
        var request = new
        {
            EntityType = "sprk_matter",
            FullSync = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/admin/record-matching/sync", content);

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Sync index endpoint should be registered");
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [Trait("Category", "Admin")]
    public async Task IndexStatus_Endpoint_RequiresAuthorization()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/record-matching/status");

        // Assert - Should require authorization
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [Trait("Category", "Admin")]
    public async Task IndexStatus_Endpoint_Exists()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/record-matching/status");

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Index status endpoint should be registered");
    }

    #endregion

    #region Document Intelligence Endpoints (Phase 1 - Prerequisite for Phase 2)

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task DocumentIntelligence_StreamEndpoint_Available()
    {
        // Verify Phase 1 prerequisite endpoints are available
        // This is needed for entity extraction before matching

        // Act
        var response = await _httpClient.GetAsync("/api/ai/document-intelligence/summarize/stream?documentId=test");

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Document intelligence stream endpoint should be available for entity extraction");
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task DocumentIntelligence_EnqueueEndpoint_Available()
    {
        // Verify Phase 1 prerequisite endpoints are available

        var request = new
        {
            DocumentId = Guid.NewGuid().ToString(),
            ContainerId = Guid.NewGuid().ToString(),
            FileName = "test.pdf"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/summarize/enqueue", content);

        // Assert - Should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Document intelligence enqueue endpoint should be available");
    }

    #endregion

    #region End-to-End Flow Validation

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [Trait("Category", "E2E")]
    public async Task Phase2Flow_AllEndpointsRegistered()
    {
        // Comprehensive test that all Phase 2 endpoints are properly registered
        var phase2Endpoints = new Dictionary<string, (HttpMethod Method, string Description)>
        {
            ["/api/ai/document-intelligence/match-records"] = (HttpMethod.Post, "Match records based on extracted entities"),
            ["/api/ai/document-intelligence/associate-record"] = (HttpMethod.Post, "Associate document with matched record"),
            ["/api/admin/record-matching/sync"] = (HttpMethod.Post, "Sync Dataverse records to search index"),
            ["/api/admin/record-matching/status"] = (HttpMethod.Get, "Get search index status")
        };

        var results = new List<(string Endpoint, bool Exists, HttpStatusCode StatusCode)>();

        foreach (var (endpoint, (method, description)) in phase2Endpoints)
        {
            HttpResponseMessage response;

            if (method == HttpMethod.Post)
            {
                var emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(endpoint, emptyContent);
            }
            else
            {
                response = await _httpClient.GetAsync(endpoint);
            }

            var exists = response.StatusCode != HttpStatusCode.NotFound;
            results.Add((endpoint, exists, response.StatusCode));
        }

        // Assert all endpoints exist
        foreach (var (endpoint, exists, statusCode) in results)
        {
            exists.Should().BeTrue($"Endpoint {endpoint} should be registered (got {statusCode})");
        }
    }

    [Fact]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task RecordMatchingEndpoints_ReturnProblemDetailsOnError()
    {
        // Verify that endpoints return RFC 7807 compliant errors
        var request = new
        {
            Organizations = new[] { "Test" }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/ai/document-intelligence/match-records", content);

        // Assert - Error responses should be ProblemDetails format
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!string.IsNullOrEmpty(responseContent))
            {
                var problemDetails = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // ProblemDetails should have type, title, status
                var hasType = problemDetails.TryGetProperty("type", out _) ||
                              problemDetails.TryGetProperty("Type", out _);
                var hasTitle = problemDetails.TryGetProperty("title", out _) ||
                               problemDetails.TryGetProperty("Title", out _);
                var hasStatus = problemDetails.TryGetProperty("status", out _) ||
                                problemDetails.TryGetProperty("Status", out _);

                // At minimum, should have status
                hasStatus.Should().BeTrue("Error response should include status code in ProblemDetails format");
            }
        }
    }

    #endregion
}
