using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// Integration tests for the metadata passthrough endpoint (FR-BFF-03).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
///   <item>401 ProblemDetails when no Authorization header is supplied.</item>
///   <item>403 ProblemDetails with <c>errorCode = DV_PRIVILEGE_DENIED</c> when the user lacks Read.</item>
///   <item>Filter-level privilege check: per-request invocation count.</item>
/// </list>
/// <para>
/// <b>Limitation</b>: full happy-path (200 + payload-size assertion + 6h cache verification) cannot
/// be covered because <c>MetadataService</c> hard-casts <c>IDataverseService</c> to the concrete
/// <c>DataverseServiceClientImpl</c> to issue a <c>RetrieveEntityRequest</c>. See
/// <c>016-deviations.md §D-016-01</c> + §D-016-02 — payload-size budget is enforced by the projection
/// code in <c>MetadataService.ProjectToDto</c> (drops localized labels + privilege catalog), tested
/// in a separate unit test against synthetic <c>EntityMetadata</c> (deferred follow-up).
/// </para>
/// </remarks>
public class MetadataEndpointTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public MetadataEndpointTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    [Fact]
    public async Task GetMetadata_Returns401_WhenNoAuthorizationHeader()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/metadata/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMetadata_Returns403_WhenUserLacksReadPrivilege()
    {
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/metadata/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter");
    }

    [Fact]
    public async Task GetMetadata_FilterInvokesPrivilegeCheck_OncePerRequest()
    {
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        await client.GetAsync("/api/dataverse/metadata/sprk_matter");
        await client.GetAsync("/api/dataverse/metadata/sprk_matter");

        _fixture.HasReadPrivilegeCalls("sprk_matter").Should().Be(2);
    }

    [Fact]
    public async Task GetMetadata_FilterChecksTargetEntity_Not401Identity()
    {
        // The filter MUST reach the privilege check (it has a valid identity); the deny is on the
        // entity. This test pins the filter does its identity → entity-check flow correctly: 403,
        // not 401.
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/dataverse/metadata/sprk_matter");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should()
            .BeOneOf("application/problem+json", "application/json");
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
