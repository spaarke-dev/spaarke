using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// Integration tests for the single-record endpoint (FR-BFF-05).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the other Dataverse passthrough endpoints, <c>RecordService.GetRecordAsync</c> uses
/// <c>IDataverseService.RetrieveAsync</c> directly when <c>$select</c> is provided (only the
/// default-column-resolution path falls back to <c>ServiceClient</c>). The happy path IS testable
/// via the <c>Mock&lt;IDataverseService&gt;</c>.
/// </para>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
///   <item>401 / 403.</item>
///   <item>Happy path with <c>$select</c>: 200 + projected fields + record id.</item>
///   <item>404 ProblemDetails for missing record (per D-014-04, also covers row-not-readable).</item>
///   <item>400 ProblemDetails for empty record id (Guid.Empty).</item>
///   <item><c>$select</c> parsing: comma-separated + whitespace tolerance.</item>
/// </list>
/// </remarks>
public class RecordEndpointTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public RecordEndpointTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    [Fact]
    public async Task GetRecord_Returns401_WhenNoAuthorizationHeader()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRecord_Returns403_WhenUserLacksReadPrivilege()
    {
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter");
    }

    [Fact]
    public async Task GetRecord_Returns200_WithProjectedFields_WhenSelectProvided()
    {
        // Happy path — caller has Read, $select supplied, mock returns a populated record.
        // RecordService.GetRecordAsync uses IDataverseService.RetrieveAsync directly when
        // selectFields is non-null (avoids the ServiceClient default-column-resolution path).
        var id = Guid.NewGuid();
        _fixture.GrantReadOn("sprk_matter");
        _fixture.ReturnRecord("sprk_matter", id, new Dictionary<string, object>
        {
            ["sprk_name"] = "Test Matter 001",
            ["sprk_status"] = "Active"
        });
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name,sprk_status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var dto = JsonDocument.Parse(body).RootElement;

        dto.GetProperty("id").GetGuid().Should().Be(id);
        dto.GetProperty("sprk_name").GetString().Should().Be("Test Matter 001");
        dto.GetProperty("sprk_status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetRecord_Returns200_WithWhitespaceTolerantSelectParsing()
    {
        // The endpoint's ParseSelect helper tolerates whitespace in the comma-separated select
        // string ("a, b , c" → ["a","b","c"]). Verify the projection still resolves correctly.
        var id = Guid.NewGuid();
        _fixture.GrantReadOn("sprk_matter");
        _fixture.ReturnRecord("sprk_matter", id, new Dictionary<string, object>
        {
            ["sprk_name"] = "Whitespace Test"
        });
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/dataverse/record/sprk_matter/{id}?$select=  sprk_name  ,  sprk_status  ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        dto.GetProperty("sprk_name").GetString().Should().Be("Whitespace Test");

        // Verify the parsed select was passed to RetrieveAsync as trimmed values (no leading/
        // trailing whitespace). The Moq verify pinpoints the contract.
        _fixture.DataverseServiceMock.Verify(d =>
            d.RetrieveAsync(
                "sprk_matter",
                id,
                It.Is<string[]>(arr => arr.Length == 2
                                       && arr[0] == "sprk_name"
                                       && arr[1] == "sprk_status"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the $select parser must trim whitespace from each comma-separated field");
    }

    [Fact]
    public async Task GetRecord_Returns404_WhenRecordNotFound()
    {
        // Per D-014-04, RecordService maps both genuine not-found AND row-not-readable to 404
        // (security: surfacing 403 would leak existence of records the caller can't see).
        var id = Guid.NewGuid();
        _fixture.GrantReadOn("sprk_matter");
        _fixture.ReturnRecordNotFound();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_RECORD_NOT_FOUND");
    }

    [Fact]
    public async Task GetRecord_Returns400_WhenIdIsGuidEmpty()
    {
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/dataverse/record/sprk_matter/{Guid.Empty}?$select=sprk_name");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_RECORD_MISSING_ID");
    }

    [Fact]
    public async Task GetRecord_FilterChecksTargetEntity_PerRequest()
    {
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();
        var id = Guid.NewGuid();

        await client.GetAsync($"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name");
        await client.GetAsync($"/api/dataverse/record/sprk_matter/{id}?$select=sprk_name");

        _fixture.HasReadPrivilegeCalls("sprk_matter").Should().Be(2,
            "the filter calls HasReadPrivilegeAsync once per request");
    }

    private static async Task<JsonElement> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should()
            .BeOneOf("application/problem+json", "application/json");
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
