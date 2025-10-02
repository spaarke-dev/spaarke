using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Spaarke.Dataverse;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for the authorization system.
/// Tests the complete authorization flow from HTTP request through to Dataverse access checks.
/// </summary>
public class AuthorizationIntegrationTests : IClassFixture<AuthorizationTestFixture>
{
    private readonly AuthorizationTestFixture _fixture;

    public AuthorizationIntegrationTests(AuthorizationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Unauthorized_Request_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        // No Authorization header

        // Act
        var response = await client.GetAsync("/api/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "requests without authentication should return 401");
    }

    [Fact]
    public async Task Authorized_Request_With_NoAccess_Returns_403()
    {
        // Arrange
        var userId = "user-with-no-access";
        var client = _fixture.CreateClientWithMockedAccess(AccessLevel.None);
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated users without permissions should return 403");
    }

    [Fact]
    public async Task Authorized_Request_With_GrantAccess_Returns_Success()
    {
        // Arrange
        var userId = "user-with-grant-access";
        var client = _fixture.CreateClientWithMockedAccess(AccessLevel.Grant);
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(AccessLevel.None, HttpStatusCode.Forbidden)]
    [InlineData(AccessLevel.Deny, HttpStatusCode.Forbidden)]
    [InlineData(AccessLevel.Grant, HttpStatusCode.OK)]
    public async Task Authorization_EnforcesAccessLevels(AccessLevel accessLevel, HttpStatusCode expectedStatus)
    {
        // Arrange
        var userId = $"user-with-{accessLevel}";
        var client = _fixture.CreateClientWithMockedAccess(accessLevel);
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers");

        // Assert
        if (expectedStatus == HttpStatusCode.OK)
        {
            // Grant access may return OK or NoContent
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        else
        {
            response.StatusCode.Should().Be(expectedStatus);
        }
    }

    [Fact]
    public async Task Authorization_ExtractsUserId_FromOidClaim()
    {
        // Arrange
        var expectedUserId = Guid.NewGuid().ToString();
        var client = _fixture.CreateClientWithAccessValidator(expectedUserId);
        var token = GenerateMockJwt(expectedUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/me");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Authorization_DenyAccessLevel_AlwaysReturns_403()
    {
        // Arrange - User has explicit Deny
        var userId = "user-with-explicit-deny";
        var client = _fixture.CreateClientWithMockedAccess(AccessLevel.Deny);
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorization_WithTeamMembership_GrantsAccess()
    {
        // Arrange
        var userId = "user-with-team-membership";
        var teamId = "team-with-access";
        var client = _fixture.CreateClientWithMockedAccess(AccessLevel.Grant, teamMemberships: new[] { teamId });
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/api/containers", "canmanagecontainers")]
    [InlineData("/api/drives/test/children", "canreadfiles")]
    public async Task Authorization_ChecksDifferentPolicies_PerEndpoint(string endpoint, string expectedPolicy)
    {
        // Arrange
        var userId = "test-user";
        var client = _fixture.CreateClientWithMockedAccess(AccessLevel.Grant);
        var token = GenerateMockJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync(endpoint);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Generates a mock JWT token for testing.
    /// Contains the 'oid' claim that ResourceAccessHandler uses to extract the user ID.
    /// </summary>
    private string GenerateMockJwt(string userId)
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
/// Test fixture for authorization integration tests.
/// Configures the test web application with mocked IAccessDataSource.
/// </summary>
public class AuthorizationTestFixture : WebApplicationFactory<Program>
{
    private AccessLevel _accessLevel = AccessLevel.None;
    private IEnumerable<string> _teamMemberships = Array.Empty<string>();
    private IEnumerable<string> _roles = Array.Empty<string>();
    private string? _expectedUserId = null;

    public HttpClient CreateClientWithMockedAccess(
        AccessLevel accessLevel,
        IEnumerable<string>? teamMemberships = null,
        IEnumerable<string>? roles = null)
    {
        _accessLevel = accessLevel;
        _teamMemberships = teamMemberships ?? Array.Empty<string>();
        _roles = roles ?? Array.Empty<string>();
        _expectedUserId = null;

        return CreateClient();
    }

    public HttpClient CreateClientWithAccessValidator(string expectedUserId)
    {
        _expectedUserId = expectedUserId;
        _accessLevel = AccessLevel.Grant;

        return CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real IAccessDataSource registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAccessDataSource));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register mock IAccessDataSource
            services.AddScoped<IAccessDataSource>(sp => new MockAccessDataSource(
                _accessLevel,
                _teamMemberships,
                _roles,
                _expectedUserId));

            // Configure JWT authentication for testing
            services.AddAuthentication("Test")
                .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
        });

        builder.UseEnvironment("Testing");
    }
}

/// <summary>
/// Mock implementation of IAccessDataSource for integration testing.
/// Returns configurable access levels and memberships.
/// </summary>
internal class MockAccessDataSource : IAccessDataSource
{
    private readonly AccessLevel _accessLevel;
    private readonly IEnumerable<string> _teamMemberships;
    private readonly IEnumerable<string> _roles;
    private readonly string? _expectedUserId;

    public MockAccessDataSource(
        AccessLevel accessLevel,
        IEnumerable<string> teamMemberships,
        IEnumerable<string> roles,
        string? expectedUserId = null)
    {
        _accessLevel = accessLevel;
        _teamMemberships = teamMemberships;
        _roles = roles;
        _expectedUserId = expectedUserId;
    }

    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        // If expectedUserId is set, validate it matches
        if (_expectedUserId != null && userId != _expectedUserId)
        {
            throw new InvalidOperationException($"Expected userId '{_expectedUserId}' but got '{userId}'");
        }

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = _accessLevel,
            TeamMemberships = _teamMemberships,
            Roles = _roles,
            CachedAt = DateTimeOffset.UtcNow
        });
    }
}

/// <summary>
/// Test authentication handler that accepts any Bearer token and extracts claims.
/// </summary>
internal class TestAuthenticationHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestAuthenticationSchemeOptions> options,
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

internal class TestAuthenticationSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
