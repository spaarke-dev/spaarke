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
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Xunit;

namespace Spe.Integration.Tests.SemanticSearch;

/// <summary>
/// Integration tests for the POST /api/ai/search/records endpoint.
/// Validates authentication, request validation, and response contract.
/// </summary>
public class RecordSearchIntegrationTests : IClassFixture<RecordSearchTestFixture>
{
    private readonly RecordSearchTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestTenantId = "test-tenant-123";

    public RecordSearchIntegrationTests(RecordSearchTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    #region POST /api/ai/search/records - Authentication Tests

    [Fact]
    public async Task PostRecordSearch_WithValidAuth_Returns200()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new RecordSearchRequest
        {
            Query = "find all litigation matters",
            RecordTypes = new[] { RecordEntityType.Matter }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostRecordSearch_WithNoAuth_Returns401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new RecordSearchRequest
        {
            Query = "find all litigation matters",
            RecordTypes = new[] { RecordEntityType.Matter }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/ai/search/records - Request Validation Tests (400)

    [Fact]
    public async Task PostRecordSearch_WithEmptyQuery_Returns400ProblemDetails()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "",
            recordTypes = new[] { "sprk_matter" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Query Required");
        problem.Detail.Should().Contain("query");
    }

    [Fact]
    public async Task PostRecordSearch_WithWhitespaceQuery_Returns400ProblemDetails()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "   ",
            recordTypes = new[] { "sprk_matter" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("query");
    }

    [Fact]
    public async Task PostRecordSearch_WithEmptyRecordTypes_Returns400ProblemDetails()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            recordTypes = Array.Empty<string>()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Record Types Required");
        problem.Detail.Should().Contain("recordTypes");
    }

    [Fact]
    public async Task PostRecordSearch_WithNullRecordTypes_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query"
            // recordTypes intentionally omitted
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert - When recordTypes is omitted, the endpoint returns 400
        // (either from model binding validation or endpoint validation logic)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostRecordSearch_WithInvalidRecordTypes_Returns400ProblemDetails()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "test query",
            recordTypes = new[] { "invalid_entity" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid Record Types");
        problem.Detail.Should().Contain("invalid_entity");
    }

    #endregion

    #region POST /api/ai/search/records - Response Contract Tests

    [Fact]
    public async Task PostRecordSearch_WithValidRequest_ReturnsRecordSearchResponse()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new RecordSearchRequest
        {
            Query = "litigation matters for Contoso",
            RecordTypes = new[] { RecordEntityType.Matter, RecordEntityType.Project }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<RecordSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
        content.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task PostRecordSearch_ResponseContainsResultsAndMetadata()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new RecordSearchRequest
        {
            Query = "Contoso project",
            RecordTypes = new[] { RecordEntityType.Matter }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<RecordSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();

        // Results should be present (mock returns sample data)
        content!.Results.Should().NotBeNull();
        content.Results.Should().HaveCountGreaterOrEqualTo(0);

        // Metadata should contain expected fields
        content.Metadata.Should().NotBeNull();
        content.Metadata.TotalCount.Should().BeGreaterOrEqualTo(0);
        content.Metadata.SearchTime.Should().BeGreaterOrEqualTo(0);
        content.Metadata.HybridMode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostRecordSearch_WithSingleRecordType_Returns200()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new RecordSearchRequest
        {
            Query = "test",
            RecordTypes = new[] { RecordEntityType.Invoice }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostRecordSearch_WithAllRecordTypes_Returns200()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new RecordSearchRequest
        {
            Query = "search everything",
            RecordTypes = new[] { RecordEntityType.Matter, RecordEntityType.Project, RecordEntityType.Invoice }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<RecordSearchResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Results.Should().NotBeNull();
    }

    [Fact]
    public async Task PostRecordSearch_WithFilters_Returns200()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "litigation",
            recordTypes = new[] { "sprk_matter" },
            filters = new
            {
                organizations = new[] { "Contoso" },
                people = new[] { "John Doe" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostRecordSearch_WithOptions_Returns200()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new
        {
            query = "litigation",
            recordTypes = new[] { "sprk_matter" },
            options = new
            {
                limit = 10,
                offset = 0,
                hybridMode = "vectorOnly"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}

/// <summary>
/// Test fixture for record search integration tests.
/// Configures test web application with mocked record search service.
/// </summary>
/// <summary>
/// Row-level authorization tests for record search (unified-access-control-r2 task 077).
/// </summary>
/// <remarks>
/// <para>
/// These exist because the suite above could not fail. Its result assertions are
/// <c>Results.Should().NotBeNull()</c> and <c>HaveCountGreaterOrEqualTo(0)</c> — the latter is true of
/// every possible list — so it passed identically whether the route authorized each row or enumerated
/// the entire tenant. That is how <c>RecordSearchAuthorizationFilter</c> shipped for months reading the
/// <c>tid</c> claim, writing "authorization granted" to the log, and calling <c>next()</c>.
/// </para>
/// <para>
/// Every test here runs against <see cref="StubRecordAccessDataSource"/>, which DENIES BY DEFAULT.
/// </para>
/// </remarks>
public class RecordSearchAuthorizationTests : IClassFixture<RecordSearchTestFixture>
{
    private readonly RecordSearchTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string TenantId = "test-tenant-077";
    private const string MatterEntitySet = "sprk_matters";

    // The two rows MockRecordSearchService always returns.
    private static readonly Guid RecordOne = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RecordTwo = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public RecordSearchAuthorizationTests(RecordSearchTestFixture fixture) => _fixture = fixture;

    private static RecordSearchRequest MatterQuery() => new()
    {
        Query = "contoso litigation",
        RecordTypes = new[] { RecordEntityType.Matter }
    };

    [Fact]
    public async Task PostRecordSearch_ReturnsOnlyRecordsTheCallerCanRead()
    {
        // Arrange — the service returns both records; the caller may read only the first.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, RecordOne, AccessRights.Read);
        // RecordTwo deliberately not granted.

        var client = _fixture.CreateAuthenticatedClient(TenantId, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/records", MatterQuery(), _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Sample Record 1");
        body.Should().NotContain("Sample Record 2",
            "a record the caller cannot read must never be served — on this route the NAME is often the "
            + "sensitive fact");
        body.Should().NotContain(RecordTwo.ToString(),
            "not even the unreadable record's id may leak; that alone confirms it exists");
    }

    [Fact]
    public async Task PostRecordSearch_WithNoGrants_ReturnsNoRecords_ThoughTheSearchMatched()
    {
        // The strongest assertion here. The search DID match two records; the caller holds Read on
        // nothing. If any code path serves record rows without consulting access — a detached filter, an
        // early return, a refactor that drops the row pass — this is the test that fails.
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantId, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search/records", MatterQuery(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<RecordSearchResponse>(_jsonOptions);

        content!.Results.Should().BeEmpty();
        content.Metadata.TotalCount.Should().Be(0,
            "the count must not report records the caller cannot reach — on this route the count alone "
            + "reveals how many matters match a counterparty's name");
    }

    [Fact]
    public async Task PostRecordSearch_TotalCountReflectsOnlyPermittedRows()
    {
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, RecordOne, AccessRights.Read);

        var client = _fixture.CreateAuthenticatedClient(TenantId, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search/records", MatterQuery(), _jsonOptions);

        var content = await response.Content.ReadFromJsonAsync<RecordSearchResponse>(_jsonOptions);
        content!.Results.Should().HaveCount(1);
        content.Metadata.TotalCount.Should().Be(1,
            "the service reported 2; reporting 2 here would tell the caller a record exists that they "
            + "can never retrieve");
    }

    [Fact]
    public async Task PostRecordSearch_WriteAccessAloneIsNotEnoughToRead()
    {
        // Rights are flags, and the check is for Read specifically. A caller granted only Write must not
        // receive the record — a HasFlag check against the wrong flag, or a non-zero-rights shortcut,
        // would let this through.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, RecordOne, AccessRights.Write);

        var client = _fixture.CreateAuthenticatedClient(TenantId, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search/records", MatterQuery(), _jsonOptions);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Sample Record 1");
    }

    [Fact]
    public async Task PostRecordSearch_WithoutBearerToken_IsRefusedBeforeTheSearchRuns()
    {
        // Access must be evaluated AS THE CALLER, which needs the caller's token. Without one the filter
        // refuses rather than falling back to an app-only evaluation — the fallback that made this route
        // tenant-wide in the first place.
        var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ai/search/records", MatterQuery(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Sample Record 1", "the search must not have executed");
    }
}

public class RecordSearchTestFixture : WebApplicationFactory<Program>
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
                .AddScheme<RecordSearchTestAuthOptions, RecordSearchTestAuthHandler>("Test", options => { });
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

            // Replace the real record search service with mock
            services.RemoveAll<IRecordSearchService>();
            services.AddSingleton<IRecordSearchService>(new MockRecordSearchService());

            // unified-access-control-r2 task 077 — record search now authorizes every returned row
            // against its own record, so these tests need an access source. DENY BY DEFAULT: a test
            // must grant explicitly, which is what lets the authorization tests below prove denial
            // rather than accidentally passing against a permissive stub.
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
        });

        builder.UseEnvironment("Testing");
    }

    /// <summary>Deny-by-default access source. Grant explicitly per test.</summary>
    public StubRecordAccessDataSource Access { get; } = new();

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
/// Deny-by-default access source for record-search tests (task 077).
/// </summary>
/// <remarks>
/// Deny-by-default is the point. A permissive stub would let every authorization test pass whether or
/// not the endpoint authorizes anything — which is the failure mode this project keeps meeting, and the
/// reason the previous record-search tests could all pass while the route enumerated the tenant.
/// </remarks>
public sealed class StubRecordAccessDataSource : IAccessDataSource
{
    private readonly Dictionary<string, AccessRights> _recordRights = new(StringComparer.OrdinalIgnoreCase);

    public void GrantRecord(string userId, string entitySetName, Guid recordId, AccessRights rights) =>
        _recordRights[$"{userId}|{entitySetName}|{recordId}"] = rights;

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
        Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = AccessRights.None
        });

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
/// Mock record search service for integration testing.
/// Returns predictable responses with sample data.
/// </summary>
internal class MockRecordSearchService : IRecordSearchService
{
    public Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RecordSearchResult>
        {
            new RecordSearchResult
            {
                RecordId = "00000000-0000-0000-0000-000000000001",
                RecordType = request.RecordTypes.First(),
                RecordName = "Sample Record 1",
                RecordDescription = "A sample record for integration testing",
                ConfidenceScore = 0.95,
                MatchReasons = new[] { "High keyword relevance", "Semantic match" },
                Organizations = new[] { "Contoso" },
                People = new[] { "John Doe" },
                Keywords = new[] { "litigation", "contract" },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                ModifiedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new RecordSearchResult
            {
                RecordId = "00000000-0000-0000-0000-000000000002",
                RecordType = request.RecordTypes.First(),
                RecordName = "Sample Record 2",
                ConfidenceScore = 0.72,
                MatchReasons = new[] { "Keyword match" },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
                ModifiedAt = DateTimeOffset.UtcNow.AddDays(-5)
            }
        };

        return Task.FromResult(new RecordSearchResponse
        {
            Results = results,
            Metadata = new RecordSearchMetadata
            {
                TotalCount = 2,
                SearchTime = 42,
                HybridMode = request.Options?.HybridMode ?? "rrf"
            }
        });
    }
}

/// <summary>
/// Test authentication handler for record search integration tests.
/// </summary>
internal class RecordSearchTestAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<RecordSearchTestAuthOptions>
{
    public RecordSearchTestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<RecordSearchTestAuthOptions> options,
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

internal class RecordSearchTestAuthOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
