using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;

namespace Spe.Integration.Tests.SemanticSearch;

/// <summary>
/// Integration tests for semantic search API endpoints.
/// Tests end-to-end flow from HTTP request through to response.
/// </summary>
public class SemanticSearchIntegrationTests : IClassFixture<SemanticSearchTestFixture>
{
    private readonly SemanticSearchTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestTenantId = "test-tenant-123";
    private const string TestEntityType = "matter";
    private const string TestEntityId = "00000000-0000-0000-0000-000000000001";

    public SemanticSearchIntegrationTests(SemanticSearchTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    #region POST /api/ai/search - Success Tests

    [Fact]
    public async Task Search_ValidEntityScope_Returns_Ok()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test search query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
        content.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_ValidDocumentIdsScope_Returns_Ok()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test search query",
            Scope = "documentIds",
            DocumentIds = new List<string> { "doc-1", "doc-2", "doc-3" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_ResponseIncludesMetadata()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.Should().NotBeNull();
        content.Metadata.TotalResults.Should().BeGreaterOrEqualTo(0);
        content.Metadata.ReturnedResults.Should().BeGreaterOrEqualTo(0);
        content.Metadata.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Search_ResponseIncludesAppliedFilters()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.AppliedFilters.Should().NotBeNull();
        content.Metadata.AppliedFilters!.Scope.Should().Be("entity");
        content.Metadata.AppliedFilters.EntityType.Should().Be(TestEntityType);
        content.Metadata.AppliedFilters.EntityId.Should().Be(TestEntityId);
    }

    #endregion

    #region POST /api/ai/search/count - Success Tests

    [Fact]
    public async Task Count_ValidRequest_Returns_Ok()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchCountResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Count.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Count_ReturnsAppliedFilters()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Scope = "documentIds",
            DocumentIds = new List<string> { "doc-1", "doc-2" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchCountResponse>(_jsonOptions);
        content!.AppliedFilters.Should().NotBeNull();
        content.AppliedFilters!.Scope.Should().Be("documentIds");
        content.AppliedFilters.DocumentIdCount.Should().Be(2);
    }

    #endregion

    #region POST /api/ai/search - Validation Tests (400)

    [Fact]
    public async Task Search_ScopeAll_Returns_200()
    {
        // Arrange - scope=all enabled in R3 for system-wide document search
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "all"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - scope=all is now supported in R3
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
        content.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_InvalidScope_Returns_400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "invalid"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid scope");
    }

    [Fact]
    public async Task Search_EntityScopeWithoutEntityType_Returns_400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityId = TestEntityId
            // Missing EntityType
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("entityType");
    }

    [Fact]
    public async Task Search_EntityScopeWithoutEntityId_Returns_400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType
            // Missing EntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("entityId");
    }

    [Fact]
    public async Task Search_DocumentIdsScopeWithEmptyList_Returns_400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string>() // Empty
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("documentIds");
    }

    #endregion

    #region POST /api/ai/search - Authentication Tests (401)

    [Fact]
    public async Task Search_WithoutAuthToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Count_WithoutAuthToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new SemanticSearchRequest
        {
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/ai/search - Hybrid Mode Tests

    [Theory]
    [InlineData("rrf")]
    [InlineData("vectorOnly")]
    [InlineData("keywordOnly")]
    public async Task Search_DifferentHybridModes_Returns_Ok(string hybridMode)
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            scope = "entity",
            entityType = TestEntityType,
            entityId = TestEntityId,
            options = new { hybridMode = hybridMode }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.ExecutedMode.Should().NotBeNull();
    }

    #endregion

    #region POST /api/ai/search - Scope=All Tests (R3)

    [Fact]
    public async Task Search_ScopeAll_ResponseIncludesMetadata()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new SemanticSearchRequest
        {
            Query = "find all documents across the system",
            Scope = "all"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.Should().NotBeNull();
        content.Metadata.TotalResults.Should().BeGreaterOrEqualTo(0);
        content.Metadata.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
        content.Metadata.AppliedFilters.Should().NotBeNull();
        content.Metadata.AppliedFilters!.Scope.Should().Be("all");
    }

    [Fact]
    public async Task Search_ScopeAll_WithEntityTypesFilter_Returns200()
    {
        // Arrange - scope=all with entityTypes filter narrows results to specific entity types
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "find documents",
            scope = "all",
            filters = new
            {
                entityTypes = new[] { "matter", "project" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
    }

    #endregion

    #region POST /api/ai/search - EntityTypes Filter Tests (R3)

    [Fact]
    public async Task Search_WithEntityTypesFilter_Returns200()
    {
        // Arrange - entityTypes filter restricts by parent entity type
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            scope = "entity",
            entityType = TestEntityType,
            entityId = TestEntityId,
            filters = new
            {
                entityTypes = new[] { "matter" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_WithMultipleEntityTypesFilter_Returns200()
    {
        // Arrange - multiple entity types in filter
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            scope = "entity",
            entityType = TestEntityType,
            entityId = TestEntityId,
            filters = new
            {
                entityTypes = new[] { "matter", "project", "invoice" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_WithInvalidEntityTypesFilter_Returns400()
    {
        // Arrange - invalid entity type in filter
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            scope = "entity",
            entityType = TestEntityType,
            entityId = TestEntityId,
            filters = new
            {
                entityTypes = new[] { "invalid_type" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("invalid_type");
    }

    [Fact]
    public async Task Search_WithEmptyEntityTypesFilter_Returns200()
    {
        // Arrange - empty entityTypes filter should be treated as no filter
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            scope = "entity",
            entityType = TestEntityType,
            entityId = TestEntityId,
            filters = new
            {
                entityTypes = Array.Empty<string>()
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - empty entityTypes is valid (no filtering applied)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}

/// <summary>
/// Test fixture for semantic search integration tests.
/// Configures test web application with mocked search service.
/// </summary>
public class SemanticSearchTestFixture : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        TestHostConfiguration.ConfigureTestHost(builder);
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Configure JWT authentication for testing
            services.AddAuthentication("Test")
                .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", options => { });
        });

        // Use ConfigureTestServices to replace services AFTER the app's services are registered
        builder.ConfigureTestServices(services =>
        {
            // Apply shared test service mocks (Dataverse, IChatClient, etc.)
            TestHostConfiguration.ConfigureSharedTestServices(services);

            // Replace the real semantic search service with mock
            services.RemoveAll<ISemanticSearchService>();
            services.AddSingleton<ISemanticSearchService>(new MockSemanticSearchService());
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateAuthenticatedClient(string tenantId, string? userId = null)
    {
        var client = CreateClient();
        var token = GenerateTestJwt(tenantId, userId ?? Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GenerateTestJwt(string tenantId, string userId)
    {
        var claims = new[]
        {
            new Claim("tid", tenantId),
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
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Mock semantic search service for integration testing.
/// Returns predictable responses for testing.
/// </summary>
internal class MockSemanticSearchService : ISemanticSearchService
{
    public Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SemanticSearchResponse
        {
            Results = new List<SearchResult>(),
            Metadata = new SearchMetadata
            {
                TotalResults = 0,
                ReturnedResults = 0,
                SearchDurationMs = 10,
                EmbeddingDurationMs = 5,
                ExecutedMode = request.Options?.HybridMode ?? "rrf",
                AppliedFilters = new AppliedFilters
                {
                    Scope = request.Scope,
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    DocumentIdCount = request.DocumentIds?.Count
                }
            }
        });
    }

    public Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SemanticSearchCountResponse
        {
            Count = 42,
            AppliedFilters = new AppliedFilters
            {
                Scope = request.Scope,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                DocumentIdCount = request.DocumentIds?.Count
            }
        });
    }
}

/// <summary>
/// Test authentication handler for integration testing.
/// </summary>
internal class TestAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthSchemeOptions>
{
    public TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestAuthSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        }

        var token = authHeader["Bearer ".Length..].Trim();

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Test");

            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail(ex));
        }
    }
}

internal class TestAuthSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
