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
    private const string DocumentA = "00000000-0000-0000-0000-0000000000aa";
    private const string DocumentB = "00000000-0000-0000-0000-0000000000bb";

    public SemanticSearchAuthorizationTests(SemanticSearchAuthorizationTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    #region Tenant Isolation Tests

    [Fact]
    public async Task Search_WithValidTenantTokenAndParentAccess_Returns_Ok()
    {
        // Arrange — a tenant claim alone is no longer sufficient; the caller must hold Read on the
        // parent. That is the whole point of task 070, so this test now grants it explicitly.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_TenantIdFromToken_IsEnforced()
    {
        // Arrange - User from Tenant A makes request
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

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

    // unified-access-control-r2 task 070.
    //
    // The three tests this region replaced asserted that entity, documentIds AND `all` scopes were
    // each "IsAllowed" and returned 200 for any authenticated caller. That was an accurate description
    // of the code — every branch of the filter returned allow — which is precisely why they passed
    // while the route disclosed every document in the tenant. They were the vulnerability, written
    // down as an expectation. The tests below assert the caller's access decides the outcome.

    [Fact]
    public async Task Search_EntityScope_WhenCallerHasReadOnParent_ReturnsOk()
    {
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_EntityScope_WhenCallerHasNoAccessToParent_Returns403()
    {
        // The core regression. A caller with no rights on the parent matter must not receive its
        // documents — this is the disclosure proven end-to-end on 2026-08-25, where a non-admin denied
        // Read on all 442 documents by Dataverse still listed, opened and downloaded a matter's files.
        var callerId = Guid.NewGuid().ToString();
        // Deliberately no grant.
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_EntityScope_WhenParentTypeIsNotAuthorizable_Returns403()
    {
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "systemuser",
            EntityId = TestEntityId
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_DocumentIdsScope_WhenCallerCanReadNone_Returns403()
    {
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { DocumentA, DocumentB }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_DocumentIdsScope_WhenCallerCanReadSome_ReturnsOnlyReadableDocuments()
    {
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantDocument(callerId, DocumentA, AccessRights.Read);
        // DocumentB deliberately not granted.
        _fixture.Search.Results =
        [
            ResultFor(DocumentA),
            ResultFor(DocumentB)
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { DocumentA, DocumentB }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Select(r => r.DocumentId).Should().BeEquivalentTo([DocumentA]);
        content.Metadata.ReturnedResults.Should().Be(1);
        // The count must not report the document the caller cannot read.
        content.Metadata.TotalResults.Should().Be(1);
    }

    [Fact]
    public async Task Search_ScopeAll_Returns403()
    {
        // Refused outright rather than reduced to the caller's accessible set. At HEAD this branch
        // carried the comment "R3: scope=all is now supported for system-wide document search" and
        // returned allow, which handed any authenticated non-admin every document in the tenant.
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest { Query = "test query", Scope = "all" };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-scope")]
    public async Task Search_WhenScopeIsEmptyOrUnknown_IsRefusedAndNeverExecutesTheSearch(string? scope)
    {
        // The `default:` branch previously returned ALLOW with "let endpoint handle validation", so an
        // absent or unrecognised scope was an unauthorized read whose only remaining gate was shape
        // validation. It is now refused in the filter.
        //
        // The status is 400 (malformed request), not 403 — only three scopes exist and all three are
        // handled explicitly, so reaching the default branch means the scope was not a scope. What
        // matters for security is asserted separately below: the search never runs.
        _fixture.Search.Results = [ResultFor(DocumentA)];
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest { Query = "test query", Scope = scope! };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The refusal must happen BEFORE the search executes — a 400 that still ran the query and
        // discarded the rows would be a different bug wearing the same status code.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(DocumentA);
    }

    [Fact]
    public async Task Search_EntityScope_WhenResultBelongsToADifferentParent_DropsIt()
    {
        // Result-level authorization. The Azure AI Search index is a separate data plane with no ACL
        // data and no freshness guarantee: if a document is reparented in Dataverse and the index still
        // carries the old parent, a parent-scoped query returns a row outside the authorized scope. A
        // filter expression is a query predicate, not an authorization decision.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Search.Results =
        [
            ResultFor(DocumentA, parentId: TestEntityId),
            ResultFor(DocumentB, parentId: Guid.NewGuid().ToString())
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Select(r => r.DocumentId).Should().BeEquivalentTo([DocumentA]);
    }

    [Fact]
    public async Task Search_WhenAuthorized_DoesNotReturnSpePointers()
    {
        // Broker-only: no client receives raw SPE pointers. File access goes through document-id-keyed
        // BFF routes that carry the standard gate; returning driveId/speFileId invites clients to
        // address SPE directly, which is how the ungated drive-keyed routes came to exist.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Search.Results =
        [
            ResultFor(DocumentA, parentId: TestEntityId, driveId: "drive-1", speFileId: "item-1")
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Results.Should().ContainSingle();
        content.Results[0].DriveId.Should().BeNull();
        content.Results[0].SpeFileId.Should().BeNull();
    }

    [Fact]
    public async Task Count_EntityScope_WhenCallerHasNoAccessToParent_Returns403()
    {
        // The count endpoint carries the same filter and must reach the same decision — a count is a
        // disclosure about content the caller cannot see.
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search/count", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("documentIds")]
    [InlineData("documentids")]
    [InlineData("DOCUMENTIDS")]
    [InlineData("DocumentIds")]
    public async Task Search_DocumentIdsScope_IsMatchedCaseInsensitively(string scope)
    {
        // Regression for a defect the allow-by-default `default:` branch was hiding. The filter
        // lower-cased the incoming scope and switched over the SearchScope constants — but
        // SearchScope.DocumentIds is the camel-cased literal "documentIds", so a lower-cased value
        // could never match that label. Every scope=documentIds request fell into `default:`, which
        // returned allow, so nothing looked wrong. With `default:` denying, a match failure would
        // instead lock legitimate callers out. This pins the comparison as case-insensitive.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantDocument(callerId, DocumentA, AccessRights.Read);
        _fixture.Search.Results = [ResultFor(DocumentA)];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = scope,
            DocumentIds = new List<string> { DocumentA }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static SemanticSearchRequest EntityScopeRequest() => new()
    {
        Query = "test query",
        Scope = "entity",
        EntityType = TestEntityType,
        EntityId = TestEntityId
    };

    private static SearchResult ResultFor(
        string documentId,
        string? parentId = null,
        string? driveId = null,
        string? speFileId = null) => new()
        {
            DocumentId = documentId,
            Name = $"{documentId}.pdf",
            CombinedScore = 0.5,
            ParentEntityType = TestEntityType,
            ParentEntityId = parentId ?? TestEntityId,
            DriveId = driveId,
            SpeFileId = speFileId
        };

    #endregion

    #region Multiple Tenant Tests

    [Fact]
    public async Task Search_DifferentTenants_AreIsolated()
    {
        // Arrange — both callers hold Read on the parent, so the only variable left is the tenant.
        var callerA = Guid.NewGuid().ToString();
        var callerB = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerA, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Access.GrantRecord(callerB, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);

        var clientTenantA = _fixture.CreateAuthenticatedClient(TenantA, callerA);
        var clientTenantB = _fixture.CreateAuthenticatedClient(TenantB, callerB);

        // Act
        var responseA = await clientTenantA.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);
        var responseB = await clientTenantB.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

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
    public async Task Count_WithValidAuthAndParentAccess_Returns_Ok()
    {
        // Arrange
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", EntityScopeRequest(), _jsonOptions);

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

    #region Malformed-input Tests

    [Fact]
    public async Task Search_EntityScope_WhenEntityIdIsNotAGuid_Returns400()
    {
        // Was `Search_EntityScope_AuthorizationGranted`, which passed `EntityId = "test-entity-id"` and
        // asserted 200 — a non-GUID entity id could not have identified any record, so the 200 it
        // asserted was evidence that nothing was being resolved or checked.
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "matter",
            EntityId = "test-entity-id"
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_EntityScope_WhenEntityIdIsEmptyGuid_Returns400()
    {
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "matter",
            EntityId = Guid.Empty.ToString()
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}

/// <summary>
/// Test fixture for semantic search authorization tests.
/// </summary>
public class SemanticSearchAuthorizationTestFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// The programmable access source. Tests grant rights explicitly; anything not granted is denied,
    /// so a test that forgets to grant sees a denial rather than an accidental allow. That default is
    /// the point — the bug this fixture now covers was an allow-by-default.
    /// </summary>
    public StubAccessDataSource Access { get; } = new();

    /// <summary>The search stub, so tests can control the rows the authorization layer must filter.</summary>
    public MockAuthTestSearchService Search { get; } = new();

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
                .AddScheme<TestAuthOptions, TestAuthorizationHandler>("Test", options => { });
        });

        // Use ConfigureTestServices to replace services AFTER the app's services are registered
        builder.ConfigureTestServices(services =>
        {
            // Apply shared test service mocks (Dataverse, IChatClient, hosted services, etc.)
            TestHostConfiguration.ConfigureSharedTestServices(services);

            // Override Microsoft Identity Web's PostConfigure which replaces our
            // DefaultAuthenticateScheme/DefaultChallengeScheme. This forces the
            // test authentication handler to be used throughout the request pipeline.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });

            // Replace the real semantic search service with mock
            services.RemoveAll<ISemanticSearchService>();
            services.AddSingleton<ISemanticSearchService>(Search);

            // Replace the access data source so tests can state, per caller and per record, exactly
            // what Dataverse would answer. Mocked at the module boundary (ADR-038 permits this; the
            // banned shape is transport-level mocking such as Mock<HttpMessageHandler>).
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
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
/// A programmable <see cref="IAccessDataSource"/>: tests state what Dataverse would answer for a given
/// caller and record. Anything not granted is <see cref="AccessRights.None"/>.
/// </summary>
/// <remarks>
/// Deny-by-default is deliberate. The defect this fixture exists to cover was an allow-by-default
/// authorization filter, so a stub that allowed unstated cases would reproduce the bug inside the test
/// harness and every negative test would pass for the wrong reason.
/// </remarks>
public sealed class StubAccessDataSource : IAccessDataSource
{
    private readonly Dictionary<string, AccessRights> _recordRights = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AccessRights> _documentRights = new(StringComparer.OrdinalIgnoreCase);

    public void GrantRecord(string userId, string entitySetName, Guid recordId, AccessRights rights) =>
        _recordRights[$"{userId}|{entitySetName}|{recordId}"] = rights;

    public void GrantDocument(string userId, string documentId, AccessRights rights) =>
        _documentRights[$"{userId}|{documentId}"] = rights;

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
    {
        var rights = _documentRights.TryGetValue($"{userId}|{resourceId}", out var r)
            ? r
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = rights
        });
    }

    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId, string entitySetName, Guid recordId, string? userAccessToken,
        CancellationToken ct = default)
    {
        var rights = _recordRights.TryGetValue($"{userId}|{entitySetName}|{recordId}", out var r)
            ? r
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = recordId.ToString(),
            AccessRights = rights
        });
    }
}

/// <summary>
/// Mock search service for authorization tests. <see cref="Results"/> lets a test supply the rows the
/// authorization layer is then expected to filter, so result-level enforcement is observable.
/// </summary>
public class MockAuthTestSearchService : ISemanticSearchService
{
    public IReadOnlyList<SearchResult> Results { get; set; } = [];

    public Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SemanticSearchResponse
        {
            Results = Results,
            Metadata = new SearchMetadata
            {
                TotalResults = Results.Count,
                ReturnedResults = Results.Count,
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
