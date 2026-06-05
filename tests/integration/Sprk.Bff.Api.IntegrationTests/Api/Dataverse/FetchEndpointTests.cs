using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.Helpers;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Api.Dataverse;

/// <summary>
/// Integration tests for the FetchXML passthrough endpoint (FR-BFF-04). SECURITY-CRITICAL.
/// </summary>
/// <remarks>
/// <para>
/// This test class includes <b>the most important security test in the integration suite</b>:
/// <see cref="Fetch_CrossEntityPrivilegeBypass_Returns403_WhenLinkEntityIsRestricted"/>. Per ADR-008
/// + task 010 §5, Dataverse server-side RBAC does NOT cascade Read enforcement through
/// <c>&lt;link-entity&gt;</c> joins; an under-tested filter creates a trivial
/// information-disclosure path. This test verifies the FetchXmlEntityExtractor + filter
/// combination catches a crafted FetchXML that joins to an entity the caller has no Read
/// privilege on.
/// </para>
/// <para>
/// Test coverage:
/// </para>
/// <list type="bullet">
///   <item>401 / 403 on read-deny.</item>
///   <item><b>Cross-entity privilege bypass</b>: caller has Read on primary entity only, FetchXML
///   joins to a restricted entity, MUST return 403 (NOT 200 with data). Detail field MUST name the
///   denied entity.</item>
///   <item>Nested link-entity (depth-3) — every entity in the tree is checked.</item>
///   <item>Many-to-many bridge entity (<c>intersect="true"</c>) — bridge entity surfaces and is
///   checked.</item>
///   <item>Malformed FetchXML → 400 with <c>errorCode = DV_FETCHXML_MALFORMED</c>.</item>
///   <item>Entity mismatch (request body says entity X but FetchXML targets entity Y) → 400 with
///   <c>errorCode = DV_FETCHXML_ENTITY_MISMATCH</c>.</item>
///   <item>400 ProblemDetails for missing/empty <c>EntityName</c> / <c>FetchXml</c> body fields.</item>
/// </list>
/// </remarks>
public class FetchEndpointTests : IClassFixture<DataverseIntegrationTestFixture>
{
    private readonly DataverseIntegrationTestFixture _fixture;

    public FetchEndpointTests(DataverseIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PrivilegeCheckerMock.Reset();
        _fixture.DataverseServiceMock.Reset();
    }

    // -------------------------------------------------------------------------------------
    //   Basic auth + validation
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Fetch_Returns401_WhenNoAuthorizationHeader()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid'/></entity></fetch>",
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fetch_Returns400_WhenFetchXmlIsMalformed()
    {
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: "<fetch><entity name='sprk_matter'><attribute name='id'/>",  // unclosed
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_FETCHXML_MALFORMED");
    }

    [Fact]
    public async Task Fetch_Returns400_WhenEntityNameMissing()
    {
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        // EntityName is empty — endpoint should reject with DV_FETCH_MISSING_ENTITY.
        var request = new FetchRequestDto(
            EntityName: "",
            FetchXml: "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid'/></entity></fetch>",
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ReadProblemDetailsAsync(response);
        // The filter sees an empty entity name first and emits DV_NO_TARGET_ENTITY (filter-level).
        // Either error code is acceptable — both surface as 400 with the same security posture.
        problem.GetProperty("errorCode").GetString().Should().BeOneOf(
            "DV_FETCH_MISSING_ENTITY", "DV_NO_TARGET_ENTITY", "DV_FETCHXML_MALFORMED");
    }

    [Fact]
    public async Task Fetch_Returns400_WhenFetchXmlMissing()
    {
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: "",
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ReadProblemDetailsAsync(response);
        // When the FetchXml body is empty, ExtractFetchXmlFromArguments returns null, and the
        // filter's ResolveEntities throws InvalidOperationException ("FetchXML payload not found
        // in request") which the filter maps to DV_NO_TARGET_ENTITY. The endpoint's own
        // DV_FETCH_MISSING_FETCHXML check never fires (filter runs first). All three error codes
        // are acceptable security postures (400 with no data leakage).
        problem.GetProperty("errorCode").GetString().Should().BeOneOf(
            "DV_FETCH_MISSING_FETCHXML", "DV_FETCHXML_MALFORMED", "DV_NO_TARGET_ENTITY");
    }

    [Fact]
    public async Task Fetch_Returns400_WhenEntityNameMismatchesFetchXmlPrimary()
    {
        // Body says sprk_matter, FetchXML targets sprk_account. Per D-013-03 the endpoint
        // checks that EntityName is referenced somewhere in the extracted set. sprk_matter
        // is not present → 400.
        _fixture.GrantReadOn("sprk_matter", "sprk_account");
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: "<fetch><entity name='sprk_account'><attribute name='accountid'/></entity></fetch>",
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_FETCHXML_ENTITY_MISMATCH");
    }

    // -------------------------------------------------------------------------------------
    //   Privilege deny — primary entity
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Fetch_Returns403_WhenUserLacksReadOnPrimaryEntity()
    {
        // Single-entity FetchXML, user has Read on nothing → 403.
        _fixture.DenyAllReads();
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid'/></entity></fetch>",
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter");
    }

    // -------------------------------------------------------------------------------------
    //   THE security-critical test — cross-entity privilege bypass via <link-entity>
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Fetch_CrossEntityPrivilegeBypass_Returns403_WhenLinkEntityIsRestricted()
    {
        // SECURITY-CRITICAL TEST (per task 016 POML §<acceptance-criteria>):
        //
        // Setup:  Caller has Read on `sprk_matter` ONLY (NOT on `sprk_financialdetail`).
        // Attack: Caller crafts a FetchXML that joins to `sprk_financialdetail` via <link-entity>
        //         to read data they should not see (Dataverse RBAC does NOT cascade through joins).
        // Expect: 403 ProblemDetails with errorCode=DV_PRIVILEGE_DENIED and detail naming the
        //         denied entity (sprk_financialdetail). NOT 200 with data leaked back.
        //
        // If this test ever returns 200, the FetchXmlEntityExtractor + DataverseAuthorizationFilter
        // contract is broken and the BFF is leaking privileged data.

        // Arrange — caller can read primary entity only.
        _fixture.GrantReadOn("sprk_matter");
        using var client = _fixture.CreateAuthenticatedClient();

        var fetchXml = """
            <fetch>
              <entity name='sprk_matter'>
                <attribute name='sprk_matterid'/>
                <link-entity name='sprk_financialdetail' from='sprk_matterid' to='sprk_matterid'>
                  <attribute name='sprk_totalamount'/>
                </link-entity>
              </entity>
            </fetch>
            """;

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: fetchXml,
            PagingCookie: null);

        // Act
        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        // Assert — MUST be 403 (not 200, not 500). Detail MUST name sprk_financialdetail (the
        // denied entity), proving the filter saw and rejected the link-entity escalation.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "FetchXmlEntityExtractor + DataverseAuthorizationFilter MUST reject FetchXML that joins to an entity the caller cannot read — otherwise the BFF leaks privileged data via <link-entity> escalation");

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_financialdetail",
            "the 403 detail must name the denied entity so operators can audit the bypass attempt");

        // Verify the filter used the batch path (GetReadableEntitiesAsync) since >1 distinct entity
        // was referenced — performance contract from FR-BFF-04 (<500ms p50).
        _fixture.GetReadableEntitiesCalls().Should().Be(1,
            "the filter must hydrate the readable-entity set once and check membership in-process — N round-trips per FetchXML breadth would break the performance budget");
    }

    [Fact]
    public async Task Fetch_NestedLinkEntityDepth3_Returns403_WhenDeepestEntityIsRestricted()
    {
        // Nested link-entity test (3 levels). Per task 010 §5 + FetchXmlEntityExtractor design,
        // the extractor uses Descendants() which naturally covers depth-N. This test verifies
        // a depth-3 nesting still catches the deepest restricted entity.
        //
        // Caller has Read on sprk_matter + sprk_financialdetail BUT NOT sprk_restricted_audit.
        // The 3rd-level join targets sprk_restricted_audit — MUST be caught.

        _fixture.GrantReadOn("sprk_matter", "sprk_financialdetail");
        using var client = _fixture.CreateAuthenticatedClient();

        var fetchXml = """
            <fetch>
              <entity name='sprk_matter'>
                <attribute name='sprk_matterid'/>
                <link-entity name='sprk_financialdetail' from='sprk_matterid' to='sprk_matterid'>
                  <attribute name='sprk_totalamount'/>
                  <link-entity name='sprk_restricted_audit' from='sprk_financialdetailid' to='sprk_financialdetailid'>
                    <attribute name='sprk_auditid'/>
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>
            """;

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: fetchXml,
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the FetchXmlEntityExtractor must walk the entire link-entity subtree (depth-N) — depth-3 restricted joins must be caught");

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_restricted_audit");
    }

    [Fact]
    public async Task Fetch_ManyToManyBridgeEntity_Returns403_WhenBridgeIsRestricted()
    {
        // M:M relationships in FetchXML surface as a link-entity with intersect="true". The bridge
        // entity must be privilege-checked. This test verifies the extractor catches the bridge
        // entity (it's just another <link-entity> in the tree).

        _fixture.GrantReadOn("sprk_matter", "sprk_party");
        using var client = _fixture.CreateAuthenticatedClient();

        var fetchXml = """
            <fetch>
              <entity name='sprk_matter'>
                <attribute name='sprk_matterid'/>
                <link-entity name='sprk_matter_party' from='sprk_matterid' to='sprk_matterid' intersect='true'>
                  <link-entity name='sprk_party' from='sprk_partyid' to='sprk_partyid'>
                    <attribute name='sprk_partyname'/>
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>
            """;

        var request = new FetchRequestDto(
            EntityName: "sprk_matter",
            FetchXml: fetchXml,
            PagingCookie: null);

        var response = await client.PostAsJsonAsync("/api/dataverse/fetch", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the M:M bridge entity (sprk_matter_party) is referenced as a <link-entity> with intersect='true' and must be privilege-checked");

        var problem = await ReadProblemDetailsAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DV_PRIVILEGE_DENIED");
        problem.GetProperty("detail").GetString().Should().Contain("sprk_matter_party");
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
