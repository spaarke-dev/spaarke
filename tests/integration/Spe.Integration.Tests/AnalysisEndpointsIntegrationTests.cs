using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for Analysis endpoints.
/// Tests end-to-end flow: request → playbook execution → SSE streaming → storage.
/// Validates Document Profile playbook support with soft failure handling.
/// </summary>
public class AnalysisEndpointsIntegrationTests : IClassFixture<AnalysisTestFixture>
{
    private readonly AnalysisTestFixture _fixture;

    public AnalysisEndpointsIntegrationTests(AnalysisTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region POST /api/ai/analysis/execute Tests

    [Fact]
    public async Task ExecuteAnalysis_WithValidRequest_ReturnsSSEStream()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClient();
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [Guid.NewGuid()],
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        response.Headers.CacheControl?.NoCache.Should().BeTrue();
        response.Headers.Connection.Should().Contain("keep-alive");
    }

    [Fact]
    public async Task ExecuteAnalysis_StreamsAnalysisStreamChunks()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClient();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        var chunks = new List<AnalysisStreamChunk>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                chunks.Add(chunk!);

                // Stop after done event
                if (chunk?.Done == true)
                {
                    break;
                }
            }
        }

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.Type == "metadata", "should include metadata event");
        chunks.Should().Contain(c => c.Type == "chunk", "should include text chunks");
        chunks.Should().Contain(c => c.Type == "done" && c.Done, "should include done event");

        var metadataChunk = chunks.First(c => c.Type == "metadata");
        metadataChunk.AnalysisId.Should().NotBeEmpty("metadata should include analysis ID");
        metadataChunk.DocumentName.Should().NotBeNullOrEmpty("metadata should include document name");

        var doneChunk = chunks.First(c => c.Type == "done");
        doneChunk.TokenUsage.Should().NotBeNull("done event should include token usage");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithDocumentProfilePlaybook_ExecutesSuccessfully()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClientWithPlaybook("Document Profile");
        var documentId = Guid.NewGuid();
        var playbookId = _fixture.DocumentProfilePlaybookId;
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        var chunks = new List<AnalysisStreamChunk>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                chunks.Add(chunk!);

                if (chunk?.Done == true)
                {
                    break;
                }
            }
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        chunks.Should().Contain(c => c.Type == "done" && c.Done);
    }

    [Fact]
    public async Task ExecuteAnalysis_WithSoftFailure_ReturnsPartialStorageTrue()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClientWithSoftFailure();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        AnalysisStreamChunk? doneChunk = null;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (chunk?.Type == "done")
                {
                    doneChunk = chunk;
                    break;
                }
            }
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doneChunk.Should().NotBeNull();
        doneChunk!.PartialStorage.Should().BeTrue("soft failure should set partialStorage=true");
        doneChunk.StorageMessage.Should().NotBeNullOrEmpty("soft failure should include storage message");
        doneChunk.StorageMessage.Should().Contain("outputs saved", "storage message should explain what succeeded");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithFullStorageSuccess_ReturnsPartialStorageNull()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClient();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        AnalysisStreamChunk? doneChunk = null;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (chunk?.Type == "done")
                {
                    doneChunk = chunk;
                    break;
                }
            }
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doneChunk.Should().NotBeNull();
        (doneChunk!.PartialStorage == null || doneChunk.PartialStorage == false)
            .Should().BeTrue("full success should not set partialStorage to true");
        doneChunk.StorageMessage.Should().BeNullOrEmpty("full success should not include storage message");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithUnauthorizedUser_Returns403()
    {
        // Arrange
        var client = _fixture.CreateUnauthorizedClient();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "users without document access should receive 403");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithMissingAuthorizationHeader_Returns401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [Guid.NewGuid()],
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "requests without authentication should return 401");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithPlaybookNotFound_ReturnsErrorChunk()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClientWithPlaybookNotFound();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid(); // Non-existent playbook
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        AnalysisStreamChunk? errorChunk = null;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (chunk?.Type == "error")
                {
                    errorChunk = chunk;
                    break;
                }
            }
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "SSE always returns 200, errors sent as events");
        errorChunk.Should().NotBeNull();
        errorChunk!.Type.Should().Be("error");
        errorChunk.Done.Should().BeTrue();
        errorChunk.Error.Should().ContainEquivalentOf("playbook");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithInvalidDocumentId_ReturnsErrorChunk()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClientWithDocumentNotFound();
        var documentId = Guid.NewGuid(); // Non-existent document
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = playbookId,
            ActionId = actionId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);
        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        AnalysisStreamChunk? errorChunk = null;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<AnalysisStreamChunk>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (chunk?.Type == "error")
                {
                    errorChunk = chunk;
                    break;
                }
            }
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "SSE always returns 200, errors sent as events");
        errorChunk.Should().NotBeNull();
        errorChunk!.Type.Should().Be("error");
        errorChunk.Done.Should().BeTrue();
        errorChunk.Error.Should().ContainEquivalentOf("document");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithMultipleDocuments_Returns400WhenDisabled()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClient();
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [Guid.NewGuid(), Guid.NewGuid()], // Multiple documents
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Multi-document", "should explain feature not available in Phase 1");
    }

    #endregion

    #region Authorization (FullUAC) Tests

    [Fact]
    public async Task ExecuteAnalysis_UsesFullUACAuthorization()
    {
        // Arrange
        var client = _fixture.CreateAuthorizedClient();
        var documentId = Guid.NewGuid();

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify authorization filter was called (fixture tracks this)
        _fixture.AuthorizationFilterCalled.Should().BeTrue(
            "AnalysisExecuteAuthorizationFilter should be applied to endpoint");
        _fixture.AuthorizedDocumentIds.Should().Contain(documentId,
            "document ID should be authorized via FullUAC");
    }

    [Fact]
    public async Task ExecuteAnalysis_WithPartialAuthorization_AuthorizesOnlyAccessibleDocuments()
    {
        // Arrange
        var authorizedDocId = Guid.NewGuid();
        var unauthorizedDocId = Guid.NewGuid();
        var client = _fixture.CreatePartiallyAuthorizedClient(authorizedDocId);

        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [authorizedDocId, unauthorizedDocId],
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", request);

        // Assert
        _fixture.AuthorizedDocumentIds.Should().Contain(authorizedDocId);
        _fixture.AuthorizedDocumentIds.Should().NotContain(unauthorizedDocId);
    }

    #endregion

    /// <summary>
    /// Generates a mock JWT token for testing with 'oid' claim for user ID extraction.
    /// </summary>
    private static string GenerateMockJwt(string userId)
    {
        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Test fixture for Analysis endpoints integration tests.
/// Configures WebApplicationFactory with mocked IAnalysisOrchestrationService.
/// </summary>
public class AnalysisTestFixture : WebApplicationFactory<Program>
{
    private TestScenario _scenario = TestScenario.Success;
    private readonly List<Guid> _authorizedDocumentIds = [];
    private readonly AuthorizationTracker _authorizationTracker = new();

    public Guid DocumentProfilePlaybookId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public IReadOnlyList<Guid> AuthorizedDocumentIds => _authorizedDocumentIds.AsReadOnly();
    public bool AuthorizationFilterCalled => _authorizationTracker.FilterCalled;

    public HttpClient CreateAuthorizedClient()
    {
        _scenario = TestScenario.Success;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-authorized");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAuthorizedClientWithPlaybook(string playbookName)
    {
        _scenario = TestScenario.DocumentProfilePlaybook;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-with-playbook");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAuthorizedClientWithSoftFailure()
    {
        _scenario = TestScenario.SoftFailure;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-soft-failure");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateUnauthorizedClient()
    {
        _scenario = TestScenario.Unauthorized;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-unauthorized");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAuthorizedClientWithPlaybookNotFound()
    {
        _scenario = TestScenario.PlaybookNotFound;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-no-playbook");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAuthorizedClientWithDocumentNotFound()
    {
        _scenario = TestScenario.DocumentNotFound;
        _authorizedDocumentIds.Clear();
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-no-document");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreatePartiallyAuthorizedClient(Guid authorizedDocId)
    {
        _scenario = TestScenario.PartialAuthorization;
        _authorizedDocumentIds.Clear();
        _authorizedDocumentIds.Add(authorizedDocId);
        _authorizationTracker.FilterCalled = false;

        var client = CreateClient();
        var token = GenerateMockJwt("test-user-partial-auth");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real IAnalysisOrchestrationService
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAnalysisOrchestrationService));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register mock IAnalysisOrchestrationService
            services.AddScoped<IAnalysisOrchestrationService>(sp =>
                new MockAnalysisOrchestrationService(_scenario, _authorizedDocumentIds, _authorizationTracker));

            // Configure test authentication
            services.AddAuthentication("Test")
                .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
        });

        builder.UseEnvironment("Testing");
    }

    private static string GenerateMockJwt(string userId)
    {
        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Test scenarios for mocking different analysis execution outcomes.
/// </summary>
internal enum TestScenario
{
    Success,
    DocumentProfilePlaybook,
    SoftFailure,
    Unauthorized,
    PlaybookNotFound,
    DocumentNotFound,
    PartialAuthorization
}

/// <summary>
/// Mock implementation of IAnalysisOrchestrationService for integration testing.
/// Returns SSE stream chunks based on configured test scenario.
/// </summary>
internal class MockAnalysisOrchestrationService : IAnalysisOrchestrationService
{
    private readonly TestScenario _scenario;
    private readonly List<Guid> _authorizedDocumentIds;
    private readonly AuthorizationTracker _authorizationTracker;

    public MockAnalysisOrchestrationService(
        TestScenario scenario,
        List<Guid> authorizedDocumentIds,
        AuthorizationTracker authorizationTracker)
    {
        _scenario = scenario;
        _authorizedDocumentIds = authorizedDocumentIds;
        _authorizationTracker = authorizationTracker;
    }

    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAnalysisAsync(
        AnalysisExecuteRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _authorizationTracker.FilterCalled = true;

        if (_scenario == TestScenario.Unauthorized)
        {
            yield return AnalysisStreamChunk.FromError("Forbidden: User does not have access to document");
            yield break;
        }

        if (_scenario == TestScenario.PlaybookNotFound)
        {
            yield return AnalysisStreamChunk.FromError($"Playbook {request.PlaybookId} not found");
            yield break;
        }

        if (_scenario == TestScenario.DocumentNotFound)
        {
            yield return AnalysisStreamChunk.FromError($"Document {request.DocumentIds[0]} not found");
            yield break;
        }

        if (_scenario == TestScenario.PartialAuthorization)
        {
            // Only authorize the documents in _authorizedDocumentIds
            foreach (var docId in request.DocumentIds)
            {
                if (_authorizedDocumentIds.Contains(docId))
                {
                    // Continue processing
                }
                else
                {
                    yield return AnalysisStreamChunk.FromError($"User does not have access to document {docId}");
                    yield break;
                }
            }
        }

        var analysisId = Guid.NewGuid();

        // Metadata event
        yield return AnalysisStreamChunk.Metadata(analysisId, "test-document.pdf");

        // Simulate streaming text chunks
        await Task.Delay(10, cancellationToken); // Simulate processing
        yield return AnalysisStreamChunk.TextChunk("Document analysis:");
        await Task.Delay(10, cancellationToken);
        yield return AnalysisStreamChunk.TextChunk(" Summary of key points");
        await Task.Delay(10, cancellationToken);
        yield return AnalysisStreamChunk.TextChunk(" and recommendations.");

        // Done event
        var tokenUsage = new TokenUsage(Input: 1500, Output: 750);

        if (_scenario == TestScenario.SoftFailure)
        {
            yield return AnalysisStreamChunk.Completed(
                analysisId,
                tokenUsage,
                partialStorage: true,
                storageMessage: "Analysis outputs saved to sprk_analysisoutput. Some fields could not be mapped to Document record.");
        }
        else
        {
            yield return AnalysisStreamChunk.Completed(analysisId, tokenUsage);
        }
    }

    // Other interface methods not used in these tests
    public Task<AnalysisDetailResult> GetAnalysisAsync(Guid analysisId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<AnalysisStreamChunk> ContinueAnalysisAsync(Guid analysisId, string message, HttpContext httpContext, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<SavedDocumentResult> SaveWorkingDocumentAsync(Guid analysisId, AnalysisSaveRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<ExportResult> ExportAnalysisAsync(Guid analysisId, AnalysisExportRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<AnalysisResumeResult> ResumeAnalysisAsync(Guid analysisId, AnalysisResumeRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<AnalysisStreamChunk> ExecutePlaybookAsync(PlaybookExecuteRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

/// <summary>
/// Wrapper class to allow passing authorization filter call tracking by reference.
/// </summary>
internal class AuthorizationTracker
{
    public bool FilterCalled { get; set; }
}
