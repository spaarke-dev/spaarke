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
    private readonly HttpClient _unauthenticatedHttpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase2RecordMatchingTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _httpClient = _fixture.CreateHttpClient();
        _unauthenticatedHttpClient = _fixture.CreateUnauthenticatedClient();
    }

    #region Retired routes (unified-access-control-r2 task 164)

    // POST /api/ai/document-intelligence/match-records and /associate-record were RETIRED by unified-access-control-r2
    // task 164 (owner round 10 item 1): no caller in the repo and not in any published API description. They let
    // any signed-in user find secure matters' names by party name (sweep #31) and re-parent any document onto any
    // record (#32). The record-matching admin routes below are still mapped under the same flag.
    [Theory]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    [InlineData("/api/ai/document-intelligence/match-records")]
    [InlineData("/api/ai/document-intelligence/associate-record")]
    public async Task RetiredRecordMatchRoute_IsNotRouted(string path)
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(path, content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{path} was retired by task 164");
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

        // Act - use unauthenticated client to test auth requirement
        var response = await _unauthenticatedHttpClient.PostAsync("/api/admin/record-matching/sync", content);

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
        // Act - use unauthenticated client to test auth requirement
        var response = await _unauthenticatedHttpClient.GetAsync("/api/admin/record-matching/status");

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

    [Fact(Skip = "Endpoint /api/ai/document-intelligence/summarize/stream is not implemented")]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task DocumentIntelligence_StreamEndpoint_Available()
    {
        // Verify Phase 1 prerequisite endpoints are available
        // This is needed for entity extraction before matching
        // NOTE: This endpoint does not exist in the current codebase.

        // Act
        var response = await _httpClient.GetAsync("/api/ai/document-intelligence/summarize/stream?documentId=test");

        // Assert - Route should match
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Endpoint /api/ai/document-intelligence/summarize/enqueue is not implemented")]
    [Trait("Category", "Phase2")]
    [Trait("Category", "RecordMatching")]
    public async Task DocumentIntelligence_EnqueueEndpoint_Available()
    {
        // Verify Phase 1 prerequisite endpoints are available
        // NOTE: This endpoint does not exist in the current codebase.

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

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
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

    #endregion
}
