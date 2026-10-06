using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// The internal Dataverse proxy routes <c>POST /api/dataverse/fetch</c> and
/// <c>GET /api/dataverse/record/{entityLogicalName}/{id}</c> are GONE, and stay gone.
/// </summary>
/// <remarks>
/// <para>
/// unified-access-control-r2 task 160 (GitHub #1099), route sweep findings #9 and #10 (both critical).
/// Both routes ran the caller's FetchXML / record id APP-ONLY behind an entity-level privilege check that
/// accepts Read at ANY depth, so any signed-in workforce user read every row and column of the org,
/// field-level-secured columns included. Neither had a caller in the repo nor an entry in any published
/// API description, so owner round 10 item 1 removed them rather than fixing them.
/// </para>
/// <para>
/// This is the routes' deny proof: an AUTHENTICATED caller who holds Read on every entity gets a 404
/// from the real host, and nothing reaches Dataverse or the privilege checker. Re-adding either route,
/// in any form, fails here. The external module seam (<c>/api/v1/external/api/dataverse/*</c>), which
/// reuses FetchService and RecordService app-only for contacts, is a different route group and is
/// pinned by its own tests (ExternalModuleDataContractTests and siblings).
/// </para>
/// </remarks>
public class DataverseProxyRoutesRemovedTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public DataverseProxyRoutesRemovedTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    [Fact]
    public async Task PostFetch_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity()
    {
        _fixture.GrantReadOn("sprk_matter", "sprk_project", "systemuser");
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", new
        {
            entityName = "sprk_matter",
            fetchXml = "<fetch><entity name='sprk_matter'><attribute name='sprk_name'/></entity></fetch>",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "POST /api/dataverse/fetch was deleted (task 160); an app-only FetchXML passthrough must not come back");
        _fixture.DataverseServiceMock.Invocations.Should().BeEmpty("no Dataverse read may be made");
        _fixture.PrivilegeCheckerMock.Invocations.Should().BeEmpty("the request must not reach any filter");
    }

    [Fact]
    public async Task GetRecord_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity()
    {
        _fixture.GrantReadOn("sprk_matter", "sprk_project", "systemuser");
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/dataverse/record/sprk_matter/{Guid.NewGuid()}?$select=sprk_name");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GET /api/dataverse/record/{entityLogicalName}/{id} was deleted (task 160); an app-only read-any-record-by-id route must not come back");
        _fixture.DataverseServiceMock.Invocations.Should().BeEmpty("no Dataverse read may be made");
        _fixture.PrivilegeCheckerMock.Invocations.Should().BeEmpty("the request must not reach any filter");
    }
}
