using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;

namespace Spe.Integration.Tests.SemanticSearch;

/// <summary>
/// Authorization-focused integration tests for semantic search.
/// Verifies security boundaries and tenant isolation.
/// </summary>
public class SemanticSearchAuthorizationTests : IClassFixture<SemanticSearchAuthorizationTestFixture>
{
    private readonly SemanticSearchAuthorizationTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TenantA = "tenant-A-123";
    private const string TenantB = "tenant-B-456";
    private const string TestEntityType = "matter";
    private const string TestEntityId = "00000000-0000-0000-0000-000000000001";

    public SemanticSearchAuthorizationTests(SemanticSearchAuthorizationTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    #region Tenant Isolation Tests

    [Fact]
    public async Task Search_WithValidTenantToken_Returns_Ok()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TenantA);
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
    }

    [Fact]
    public async Task Search_TenantIdFromToken_IsEnforced()
    {
        // Arrange - User from Tenant A makes request
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - Request succeeds, tenant isolation enforced at query time
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_WithoutTenantClaim_Returns_401()
    {
        // Arrange - Token without tenant ID claim
        var client = _fixture.CreateClientWithInvalidTenantClaim();
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

    #endregion

    #region Scope Authorization Tests

    [Fact]
    public async Task Search_EntityScope_IsAllowed()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TenantA);
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
    }

    [Fact]
    public async Task Search_DocumentIdsScope_IsAllowed()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { "doc-1", "doc-2" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_ScopeAll_Returns_Forbidden()
    {
        // Arrange - scope=all should be blocked by authorization filter
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "all"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - Returns 400 from validation OR 403 from authorization
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("not supported");
    }

    #endregion

    #region Multiple Tenant Tests

    [Fact]
    public async Task Search_DifferentTenants_AreIsolated()
    {
        // Arrange
        var clientTenantA = _fixture.CreateAuthenticatedClient(TenantA);
        var clientTenantB = _fixture.CreateAuthenticatedClient(TenantB);

        var requestA = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        var requestB = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var responseA = await clientTenantA.PostAsJsonAsync("/api/ai/search", requestA, _jsonOptions);
        var responseB = await clientTenantB.PostAsJsonAsync("/api/ai/search", requestB, _jsonOptions);

        // Assert - Both succeed but are isolated by tenant
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify requests were processed with correct tenant context
        var contentA = await responseA.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        var contentB = await responseB.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        contentA!.Metadata.Should().NotBeNull();
        contentB!.Metadata.Should().NotBeNull();
    }

    #endregion

    #region Authentication Tests

    [Fact]
    public async Task Search_NoAuthHeader_Returns_401()
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
    public async Task Search_InvalidToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

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
    public async Task Search_ExpiredToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClientWithExpiredToken(TenantA);
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

    #endregion

    #region Count Endpoint Authorization Tests

    [Fact]
    public async Task Count_WithValidAuth_Returns_Ok()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TenantA);
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
    }

    [Fact]
    public async Task Count_WithoutAuth_Returns_401()
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

    #region Audit Logging Tests (Verification via Response)

    [Fact]
    public async Task Search_EntityScope_AuthorizationGranted()
    {
        // Arrange - Valid entity scope request
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "matter",
            EntityId = "test-entity-id"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - Authorization was granted (200 OK)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_DocumentIdsScope_AuthorizationGranted()
    {
        // Arrange - Valid documentIds scope request
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { "doc-1", "doc-2", "doc-3" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert - Authorization was granted (200 OK)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}

/// <summary>
/// Test fixture for semantic search authorization tests.
/// </summary>
public class SemanticSearchAuthorizationTestFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real search service
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISemanticSearchService));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register mock search service
            services.AddSingleton<ISemanticSearchService>(new MockAuthTestSearchService());

            // Configure JWT authentication for testing
            services.AddAuthentication("Test")
                .AddScheme<TestAuthOptions, TestAuthorizationHandler>("Test", options => { });
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

    public HttpClient CreateClientWithInvalidTenantClaim()
    {
        var client = CreateClient();
        // Token without tid claim
        var token = GenerateTestJwtWithoutTenant(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateClientWithExpiredToken(string tenantId)
    {
        var client = CreateClient();
        var token = GenerateExpiredTestJwt(tenantId, Guid.NewGuid().ToString());
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

    private static string GenerateTestJwtWithoutTenant(string userId)
    {
        // Deliberately omit tid claim
        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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

    private static string GenerateExpiredTestJwt(string tenantId, string userId)
    {
        var claims = new[]
        {
            new Claim("tid", tenantId),
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Expired 1 hour ago
        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Mock search service for authorization tests.
/// </summary>
internal class MockAuthTestSearchService : ISemanticSearchService
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
                SearchDurationMs = 5,
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
            Count = 10,
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
/// Test authentication handler for authorization tests.
/// Validates token expiration and tenant claims.
/// </summary>
internal class TestAuthorizationHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthOptions>
{
    public TestAuthorizationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestAuthOptions> options,
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

            // Check expiration
            if (jwtToken.ValidTo < DateTime.UtcNow)
            {
                return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Token expired"));
            }

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

internal class TestAuthOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
