using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.SpeAdmin;

/// <summary>
/// Tests for the SearchItems endpoint (POST /api/spe/search/items?configId=…).
///
/// Tests cover:
/// - Authentication (401 without a caller)
/// - Input validation (missing / unparseable configId → 400; empty or whitespace query → 400)
/// - The tenant-scope boundary every /api/spe route shares: an unknown config gets the same 404 as an
///   out-of-scope one (unified-access-control-r2 task 165, owner round 9; round 16 item 5)
/// </summary>
/// <remarks>
/// <para>
/// <b>Host.</b> <see cref="AdminSurfaceHostFixture"/>: the real BFF pipeline, with Dataverse substituted at the
/// <c>DataverseWebApiClient</c> class boundary by the in-memory <see cref="FakeDataverseTables"/>. Until task 165
/// these tests ran on <c>CustomWebAppFactory</c> with the REAL <c>DataverseWebApiClient</c> pointed at
/// <c>test.crm.dynamics.com</c> — a real outbound call whose result depended on network state (see the
/// 2026-08-27 note in git history) — and the empty/whitespace-query tests reached their handler only because the
/// tenant-scope filter used to fail OPEN when its config lookup failed. The filter now fails closed (503), so the
/// tests seed an in-scope config instead: the validation they assert runs for a config the caller CAN reach.
/// </para>
/// <para>
/// <b>The caller</b> is an SPE admin (app role <c>Admin</c>) whose Dataverse user sits in a leaf business unit
/// that holds one config.
/// </para>
/// </remarks>
public class SearchItemsTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string SearchRoute = "/api/spe/search/items";

    private static readonly Guid Root = Guid.Parse("30000000-0000-0000-0000-000000000000");
    private static readonly Guid CallerUnit = Guid.Parse("3a000000-0000-0000-0000-000000000000");
    private static readonly Guid OtherUnit = Guid.Parse("3b000000-0000-0000-0000-000000000000");

    /// <summary>A config in the caller's own business unit.</summary>
    private static readonly Guid InScopeConfig = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>A config in another customer's business unit.</summary>
    private static readonly Guid OutOfScopeConfig = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    /// <summary>A config id that exists nowhere.</summary>
    private static readonly Guid UnknownConfig = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    private readonly AdminSurfaceHostFixture _fixture;

    public SearchItemsTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        SeedTenant();
    }

    /// <summary>
    /// Verifies that POST /api/spe/search/items requires authentication (returns 401 without a caller).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithoutAuthentication_Returns401()
    {
        using var client = _fixture.CreateAnonymous();

        var response = await client.PostAsJsonAsync($"{SearchRoute}?configId={InScopeConfig}", Request("test"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that missing configId returns 401 without a caller (auth runs before validation in this route group).
    /// </summary>
    [Fact]
    public async Task SearchItems_MissingConfigId_Returns401WithoutToken()
    {
        using var client = _fixture.CreateAnonymous();

        var response = await client.PostAsJsonAsync(SearchRoute, Request("test"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies the endpoint route is registered at POST /api/spe/search/items.
    /// When authenticated, missing configId returns the handler's 400 (not 404).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_MissingConfigId_Returns400()
    {
        using var client = Admin();

        var problem = await Problem(await client.PostAsJsonAsync(SearchRoute, Request("test")), HttpStatusCode.BadRequest);

        problem["detail"].GetString().Should().Be("configId is required and must be a valid GUID.");
    }

    /// <summary>
    /// Verifies that an empty query string returns 400 Bad Request — for a config the caller can reach.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_EmptyQuery_Returns400()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"{SearchRoute}?configId={InScopeConfig}", Request("")),
            HttpStatusCode.BadRequest);

        // Empty query is rejected (per acceptance criteria) by the handler, not by the scope filter.
        problem["detail"].GetString().Should().Be("Query is required and must not be empty.");
    }

    /// <summary>
    /// Verifies that a whitespace-only query string returns 400 Bad Request — for a config the caller can reach.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_WhitespaceQuery_Returns400()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"{SearchRoute}?configId={InScopeConfig}", Request("   ")),
            HttpStatusCode.BadRequest);

        problem["detail"].GetString().Should().Be("Query is required and must not be empty.");
    }

    /// <summary>
    /// Verifies that an invalid (non-GUID) configId returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_InvalidConfigId_Returns400()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"{SearchRoute}?configId=not-a-guid", Request("contract.pdf")),
            HttpStatusCode.BadRequest);

        problem["detail"].GetString().Should().Be("configId is required and must be a valid GUID.");
    }

    /// <summary>
    /// A valid configId that does not exist gets the UNIFORM 404 — the same answer, byte for byte apart from
    /// the trace id, as a config in another customer's business unit (owner round 9: an unknown id and a denied
    /// id get the same answer; round 16 item 5). Until task 165 this test asserted "400 or 500": the handler's
    /// own config-not-found answer, which differed from the out-of-scope answer — an existence oracle.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_ValidConfigIdNotFound_IsTheUniform404_SameAsAnOutOfScopeConfig()
    {
        using var client = Admin();

        var unknown = await Problem(
            await client.PostAsJsonAsync($"{SearchRoute}?configId={UnknownConfig}", Request("contract.pdf")),
            HttpStatusCode.NotFound);
        var outOfScope = await Problem(
            await client.PostAsJsonAsync($"{SearchRoute}?configId={OutOfScopeConfig}", Request("contract.pdf")),
            HttpStatusCode.NotFound);

        unknown["errorCode"].GetString().Should().Be("spe.admin.deny.config_out_of_scope");
        unknown["title"].GetString().Should().Be(outOfScope["title"].GetString());
        unknown["detail"].GetString().Should().Be($"Container type config '{UnknownConfig}' was not found.");
        outOfScope["detail"].GetString().Should().Be($"Container type config '{OutOfScopeConfig}' was not found.");
        unknown.Keys.Should().BeEquivalentTo(outOfScope.Keys);

        _fixture.Dataverse.CallsOn("sprk_specontainertypeconfigs", "Retrieve").Should().BeEmpty(
            "the handler's config resolution must not run for either");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private static SearchItemsEndpoints.SearchItemsRequest Request(string query) =>
        new(Query: query, ContainerId: null, FileType: null, PageSize: null, SkipToken: null);

    private void SeedTenant()
    {
        var dv = _fixture.Dataverse;
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = CallerUnit, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = OtherUnit, ["_parentbusinessunitid_value"] = Root });

        dv.Add("systemusers", new()
        {
            ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
            ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000003"),
            ["_businessunitid_value"] = CallerUnit,
        });

        dv.Add("sprk_specontainertypeconfigs", new()
        {
            ["sprk_specontainertypeconfigid"] = InScopeConfig,
            ["_sprk_businessunit_value"] = CallerUnit,
            // A name the BFF may resolve (task 165, round 35 item 3) — otherwise the filter answers 409 first.
            ["sprk_keyvaultsecretname"] = "spe-owning-app-search",
        });
        dv.Add("sprk_specontainertypeconfigs", new()
        {
            ["sprk_specontainertypeconfigid"] = OutOfScopeConfig,
            ["_sprk_businessunit_value"] = OtherUnit,
        });
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }
}
