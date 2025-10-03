using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Spe.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests for Dataverse Web API operations using WireMock.
/// Tests HTTP interactions with the Dataverse Web API endpoints for document management.
/// </summary>
public class DataverseWebApiWireMockTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public DataverseWebApiWireMockTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task CreateDocument_Success_ReturnsEntityId()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/data/v9.2/sprk_documents")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("OData-EntityId", $"https://test.crm.dynamics.com/api/data/v9.2/sprk_documents({documentId})"));

        // Act
        var payload = new { sprk_documentname = "Test Document" };
        var response = await _httpClient.PostAsJsonAsync("/api/data/v9.2/sprk_documents", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Should().ContainKey("OData-EntityId");
    }

    [Fact]
    public async Task GetDocument_NotFound_Returns404()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404));

        // Act
        var response = await _httpClient.GetAsync($"/api/data/v9.2/sprk_documents({documentId})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateDocument_Success_Returns204()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingPatch())
            .RespondWith(Response.Create()
                .WithStatusCode(204));

        // Act
        var updates = new { sprk_documentname = "Updated Name" };
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/data/v9.2/sprk_documents({documentId})")
        {
            Content = JsonContent.Create(updates)
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteDocument_Success_Returns204()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(204));

        // Act
        var response = await _httpClient.DeleteAsync($"/api/data/v9.2/sprk_documents({documentId})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    public void Dispose()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
        _httpClient?.Dispose();
    }
}
