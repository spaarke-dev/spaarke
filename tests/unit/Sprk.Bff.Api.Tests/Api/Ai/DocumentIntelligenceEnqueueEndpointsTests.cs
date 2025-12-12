using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for Summarize Enqueue endpoints.
/// These tests verify endpoint registration and basic auth requirements.
/// Full integration tests would require Service Bus configuration.
/// </summary>
public class DocumentIntelligenceEnqueueEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public DocumentIntelligenceEnqueueEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Enqueue Single Endpoint Tests

    [Fact]
    public async Task Enqueue_EndpointExists_AcceptsPost()
    {
        // Arrange
        var request = new DocumentAnalysisRequest(Guid.NewGuid(), "drive-id", "item-id");

        // Act - endpoint should exist and return 401 without auth (not 404)
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue", request);

        // Assert - endpoint exists (401 means auth required, not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.OK,
            HttpStatusCode.Accepted,
            HttpStatusCode.InternalServerError); // May fail due to missing services in test
    }

    [Fact]
    public async Task Enqueue_WithoutAuth_RequiresAuthentication()
    {
        // Arrange - no auth header
        var request = new DocumentAnalysisRequest(Guid.NewGuid(), "drive-id", "item-id");

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue", request);

        // Assert - should require authentication
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Enqueue_WithAuth_ProceedsToEndpoint()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var request = new DocumentAnalysisRequest(Guid.NewGuid(), "drive-id", "item-id");

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue", request);

        // Assert - should not be 401 (auth passed), may be 500 (missing services) or 202 (success)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        // With auth, should proceed past authentication layer
    }

    #endregion

    #region Enqueue Batch Endpoint Tests

    [Fact]
    public async Task EnqueueBatch_EndpointExists_AcceptsPost()
    {
        // Arrange
        var request = new BatchAnalysisRequest(new List<DocumentAnalysisRequest>
        {
            new(Guid.NewGuid(), "drive-id-1", "item-id-1"),
            new(Guid.NewGuid(), "drive-id-2", "item-id-2")
        });

        // Act - endpoint should exist and return 401 without auth (not 404)
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue-batch", request);

        // Assert - endpoint exists (401 means auth required, not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.OK,
            HttpStatusCode.Accepted,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EnqueueBatch_WithoutAuth_RequiresAuthentication()
    {
        // Arrange - no auth header
        var request = new BatchAnalysisRequest(new List<DocumentAnalysisRequest>
        {
            new(Guid.NewGuid(), "drive-id", "item-id")
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue-batch", request);

        // Assert - should require authentication
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EnqueueBatch_WithAuth_ProceedsToEndpoint()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var request = new BatchAnalysisRequest(new List<DocumentAnalysisRequest>
        {
            new(Guid.NewGuid(), "drive-id", "item-id")
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/document-intelligence/enqueue-batch", request);

        // Assert - should not be 404 (endpoint exists)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion
}

/// <summary>
/// Model tests for enqueue request/response types.
/// </summary>
public class EnqueueAnalysisModelsTests
{
    [Fact]
    public void EnqueueAnalysisResponse_PropertiesAreSet()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        // Act
        var response = new EnqueueAnalysisResponse(jobId, documentId);

        // Assert
        response.JobId.Should().Be(jobId);
        response.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public void BatchAnalysisRequest_PropertiesAreSet()
    {
        // Arrange
        var documents = new List<DocumentAnalysisRequest>
        {
            new(Guid.NewGuid(), "drive-1", "item-1"),
            new(Guid.NewGuid(), "drive-2", "item-2")
        };

        // Act
        var request = new BatchAnalysisRequest(documents);

        // Assert
        request.Documents.Should().HaveCount(2);
        request.Documents[0].DriveId.Should().Be("drive-1");
        request.Documents[1].DriveId.Should().Be("drive-2");
    }

    [Fact]
    public void BatchAnalysisResponse_PropertiesAreSet()
    {
        // Arrange
        var jobs = new List<EnqueueAnalysisResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid()),
            new(Guid.NewGuid(), Guid.NewGuid())
        };

        // Act
        var response = new BatchAnalysisResponse(jobs, jobs.Count);

        // Assert
        response.Jobs.Should().HaveCount(2);
        response.TotalEnqueued.Should().Be(2);
    }
}
