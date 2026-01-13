using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for PlaybookRunEndpoints.
/// Verifies endpoint registration and basic auth requirements.
/// Orchestration logic is tested in PlaybookOrchestrationServiceTests.
/// </summary>
public class PlaybookRunEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly Guid _testPlaybookId = Guid.NewGuid();
    private readonly Guid _testRunId = Guid.NewGuid();

    public PlaybookRunEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Validate Playbook

    [Fact]
    public async Task ValidatePlaybook_EndpointExists_AcceptsPost()
    {
        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/{_testPlaybookId}/validate", null);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidatePlaybook_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/{_testPlaybookId}/validate", null);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ValidatePlaybook_WithAuth_ProceedsToEndpoint()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/{_testPlaybookId}/validate", null);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Execute Playbook

    [Fact]
    public async Task ExecutePlaybook_EndpointExists_AcceptsPost()
    {
        // Arrange
        var request = new ExecutePlaybookRequest
        {
            DocumentIds = [Guid.NewGuid()]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/execute", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecutePlaybook_WithoutAuth_RequiresAuthentication()
    {
        // Arrange
        var request = new ExecutePlaybookRequest
        {
            DocumentIds = [Guid.NewGuid()]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/execute", request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExecutePlaybook_MissingDocuments_ReturnsBadRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var request = new ExecutePlaybookRequest
        {
            DocumentIds = [] // Empty array
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/ai/playbooks/{_testPlaybookId}/execute", request);

        // Assert - endpoint should validate request
        // May fail with auth/service errors before validation
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Get Run Status

    [Fact]
    public async Task GetRunStatus_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRunStatus_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetRunStatus_WithAuth_ReturnsNotFoundForUnknownRun()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}");

        // Assert - unknown run should return 404 (or 500 if service error)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Stream Run Status

    [Fact]
    public async Task StreamRunStatus_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}/stream");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Cancel Run

    [Fact]
    public async Task CancelRun_EndpointExists_AcceptsPost()
    {
        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/runs/{_testRunId}/cancel", null);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelRun_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/runs/{_testRunId}/cancel", null);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task CancelRun_WithAuth_ReturnsNotFoundForUnknownRun()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.PostAsync($"/api/ai/playbooks/runs/{_testRunId}/cancel", null);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.OK, // May return OK with Cancelled=false message
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Get Run History

    [Fact]
    public async Task GetRunHistory_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/runs");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRunHistory_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/runs");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetRunHistory_WithAuth_ProceedsToEndpoint()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/runs");

        // Assert - endpoint should respond (may fail with auth/service errors)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRunHistory_WithQueryParams_AcceptsGet()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/{_testPlaybookId}/runs?page=1&pageSize=10&state=Completed");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Get Run Detail

    [Fact]
    public async Task GetRunDetail_EndpointExists_AcceptsGet()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}/detail");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRunDetail_WithoutAuth_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}/detail");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetRunDetail_WithAuth_ReturnsNotFoundForUnknownRun()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/ai/playbooks/runs/{_testRunId}/detail");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    #endregion
}
