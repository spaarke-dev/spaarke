using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// Integration tests for the sprk_gridconfiguration passthrough endpoint (DEF-002 of
/// <c>spaarke-dataset-grid-framework-r2</c>).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
///   <item>401 ProblemDetails when no Authorization header is supplied.</item>
///   <item>403 ProblemDetails with <c>errorCode = DV_PRIVILEGE_DENIED</c> when the user lacks Read on
///   the target entity (filter-level).</item>
///   <item>Per-request privilege-check contract (filter calls HasReadPrivilegeAsync once per request).</item>
/// </list>
/// <para>
/// Structurally identical to <see cref="SavedQueryEndpointsTests"/> (task 011 precedent). The 200
/// happy-path is unreachable in this fixture for the same reason as the SavedQuery list endpoint —
/// <see cref="Sprk.Bff.Api.Services.Dataverse.GridConfigurationService"/> hard-casts
/// <c>IDataverseService</c> to <c>DataverseServiceClientImpl</c> to reach the sealed
/// <c>ServiceClient</c>. The mock fails this cast, and <see cref="Sprk.Bff.Api.Services.Dataverse.GridConfigurationService.GetForEntityAsync"/>
/// returns <c>Array.Empty</c> before any real query is issued (documented in <c>016-deviations.md §D-016-01</c>
/// — the same follow-up applies here).
/// </para>
/// </remarks>
public class GridConfigurationEndpointsTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public GridConfigurationEndpointsTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    [Fact]
    public async Task GetGridConfigurationsForEntity_Returns401_WhenNoAuthorizationHeader()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/gridconfigurations/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetGridConfigurationsForEntity_Returns403_WhenUserLacksReadPrivilege()
    {
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/gridconfigurations/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("title").GetString().Should().Be("Forbidden");
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter");
    }

    [Fact]
    public async Task GetGridConfigurationsForEntity_FilterInvokesPrivilegeCheck_OncePerRequest()
    {
        // The filter calls HasReadPrivilegeAsync(userOid, "sprk_matter", ct) once per request.
        // Mirrors the SavedQueryEndpoints per-request contract test — DEF-002 endpoint reuses the
        // same DataverseAuthorizationFilter, so this asserts the shared filter is wired identically.
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        await client.GetAsync("/api/dataverse/gridconfigurations/sprk_matter");
        await client.GetAsync("/api/dataverse/gridconfigurations/sprk_matter");

        _fixture.HasReadPrivilegeCalls("sprk_matter").Should().Be(2,
            "the filter calls HasReadPrivilegeAsync once per request; the picker cache lives inside the privilege checker itself, not the filter");
    }

    [Fact]
    public async Task GetGridConfigurationsForEntity_ReturnsEmptyList_WhenUserGranted_ButServiceClientUnavailable()
    {
        // With auth granted, the request flows through the filter to the handler → service. The
        // service's GetServiceClient() returns null (mock case per D-016-01), and the service
        // returns Array.Empty<GridConfigurationSummaryDto>() rather than throwing. This asserts the
        // graceful-degradation contract — the wizard receives an empty picker instead of a 500,
        // which lets its "None (use default)" fallback render.
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/gridconfigurations/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        json.ValueKind.Should().Be(JsonValueKind.Array);
        json.GetArrayLength().Should().Be(0);
    }

    private static async Task<JsonElement> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should()
            .BeOneOf("application/problem+json", "application/json");
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
