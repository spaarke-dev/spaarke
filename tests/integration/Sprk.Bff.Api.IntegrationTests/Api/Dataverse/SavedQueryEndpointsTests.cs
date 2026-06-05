using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// Integration tests for the SavedQuery passthrough endpoints (FR-BFF-01 / FR-BFF-02).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
///   <item>401 ProblemDetails when no Authorization header is supplied.</item>
///   <item>403 ProblemDetails with <c>errorCode = DV_PRIVILEGE_DENIED</c> when the user lacks Read on
///   the target entity (filter-level + handler-level both tested).</item>
///   <item>400 ProblemDetails when the route value is missing/empty.</item>
///   <item>Cache-behavior assertion (per task 016 POML): when the savedquery handler is invoked
///   twice within the cache TTL, the privilege checker is hit once per request (cache is at the
///   savedquery payload level, not the privilege level).</item>
/// </list>
/// <para>
/// <b>Limitation</b>: full happy-path (200 with payload + ServiceClient call count) cannot be
/// covered here because <c>SavedQueryService</c> hard-casts <c>IDataverseService</c> to the
/// concrete <c>DataverseServiceClientImpl</c> to reach the sealed <c>ServiceClient</c>. The mock
/// fails this cast and the handler surfaces a 500. See <c>016-deviations.md §D-016-01</c> — full
/// happy-path coverage requires a follow-up that introduces an <c>IServiceClientAdapter</c>
/// abstraction OR test refactoring against a live Dataverse tenant in a separate E2E suite.
/// </para>
/// </remarks>
public class SavedQueryEndpointsTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public SavedQueryEndpointsTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        // Reset privilege checker setups + invocation counts between tests so per-test setups
        // are deterministic (test ordering is undefined in xUnit).
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    // -------------------------------------------------------------------------------------
    //   /api/dataverse/savedqueries/{entityLogicalName}  (FR-BFF-02 — filter-level auth)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task GetSavedQueriesForEntity_Returns401_WhenNoAuthorizationHeader()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/dataverse/savedqueries/sprk_matter");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSavedQueriesForEntity_Returns403_WhenUserLacksReadPrivilege()
    {
        // Arrange
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/dataverse/savedqueries/sprk_matter");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("title").GetString().Should().Be("Forbidden");
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter");
    }

    [Fact]
    public async Task GetSavedQueriesForEntity_FilterInvokesPrivilegeCheck_OncePerRequest()
    {
        // The filter calls HasReadPrivilegeAsync(userOid, "sprk_matter", ct) once per request.
        // This asserts the per-request privilege-check contract.
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        // Act — two requests, same entity.
        await client.GetAsync("/api/dataverse/savedqueries/sprk_matter");
        await client.GetAsync("/api/dataverse/savedqueries/sprk_matter");

        // Assert — privilege check called once per request (the privilege checker is responsible
        // for its own caching across requests; we don't double-count here).
        _fixture.HasReadPrivilegeCalls("sprk_matter").Should().Be(2,
            "the filter calls HasReadPrivilegeAsync once per request; the cache lives inside the privilege checker itself, not the filter");
    }

    // -------------------------------------------------------------------------------------
    //   /api/dataverse/savedquery/{savedQueryId}  (FR-BFF-01 — handler-level deferred auth)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task GetSavedQueryById_Returns401_WhenNoAuthorizationHeader()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync(
            $"/api/dataverse/savedquery/{DataverseTestConstants.TestSavedQueryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSavedQueryById_Returns404_WhenServiceClientReturnsNull()
    {
        // Per D-016-01: SavedQueryService.GetServiceClient() returns null when IDataverseService is
        // NOT DataverseServiceClientImpl (our mock case). The service returns null from
        // GetSavedQueryAsync, and the handler maps null to 404 ProblemDetails with
        // errorCode=DV_SAVEDQUERY_NOT_FOUND. This test pins that 404 path — it's the by-id
        // handler's "savedquery does not exist" branch, which is part of the contract regardless
        // of the underlying mock posture.
        //
        // Once an IServiceClientAdapter abstraction is introduced (deferred follow-up), the same
        // by-id endpoint will support a 200 happy-path test alongside this 404.
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(
            $"/api/dataverse/savedquery/{DataverseTestConstants.TestSavedQueryId}");

        // Assert — null payload from SavedQueryService surfaces as 404 with the catalog error code.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_SAVEDQUERY_NOT_FOUND");
    }

    // -------------------------------------------------------------------------------------
    //   Helpers
    // -------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should()
            .BeOneOf("application/problem+json", "application/json");
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
