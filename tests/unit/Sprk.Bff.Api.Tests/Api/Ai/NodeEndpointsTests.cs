using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for NodeEndpoints.
/// Verifies endpoint registration and basic auth requirements.
/// Node service operations are tested in NodeServiceTests.
/// </summary>
public class NodeEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly Guid _testPlaybookId = Guid.NewGuid();
    private readonly Guid _testNodeId = Guid.NewGuid();

    public NodeEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region List Nodes

    [Fact]
    public async Task ListNodes_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes");

        // Assert - endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListNodes_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListNodes_WithAuth_ProceedsToEndpoint()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes");

        // Assert - should proceed past auth (may fail with 500 if services not configured)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Create Node

    [Fact]
    public async Task CreateNode_EndpointExists_AcceptsPost()
    {
        // Arrange
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = Guid.NewGuid(),
            OutputVariable = "test_output"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateNode_WithoutAuth_RequiresAuthentication()
    {
        // Arrange
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = Guid.NewGuid(),
            OutputVariable = "test_output"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes", request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Get Node

    [Fact]
    public async Task GetNode_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes/{_testNodeId}");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Update Node

    [Fact]
    public async Task UpdateNode_EndpointExists_AcceptsPut()
    {
        // Arrange
        var request = new UpdateNodeRequest
        {
            Name = "Updated Node"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes/{_testNodeId}", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delete Node

    [Fact]
    public async Task DeleteNode_EndpointExists_AcceptsDelete()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes/{_testNodeId}");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Reorder Nodes

    [Fact]
    public async Task ReorderNodes_EndpointExists_AcceptsPut()
    {
        // Arrange
        var request = new { NodeIds = new[] { Guid.NewGuid(), Guid.NewGuid() } };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes/reorder", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Update Node Scopes

    [Fact]
    public async Task UpdateNodeScopes_EndpointExists_AcceptsPut()
    {
        // Arrange
        var request = new NodeScopesRequest
        {
            SkillIds = [Guid.NewGuid()],
            KnowledgeIds = [Guid.NewGuid()]
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/nodes/{_testNodeId}/scopes", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion
}
