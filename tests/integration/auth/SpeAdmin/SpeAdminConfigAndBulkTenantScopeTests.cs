using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 — the SPE admin business-unit boundary
/// (<c>SpeAdminTenantScopeFilter</c> + <c>SpeAdminTenantScope</c>) now runs on, and holds for,
/// <c>GET/PUT/DELETE /api/spe/configs/{configId}</c>, <c>POST /api/spe/configs</c>,
/// <c>POST /api/spe/bulk/delete</c> and <c>POST /api/spe/bulk/permissions</c> (sweep findings #44, #45,
/// #72, #73, #74, #75), fails closed, and projects the dashboard aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tenant.</b> Root → {Unit A, Unit B}. The caller is an SPE admin (app role <c>Admin</c>) whose
/// Dataverse user sits in Unit A, a leaf: the customer administrator the boundary exists for. Config A
/// is in Unit A, Config B in Unit B, Config N has no business unit (the compatibility rule: visible to
/// every admin). Every request goes through the real BFF pipeline (<see cref="AdminSurfaceHostFixture"/>);
/// Dataverse is the in-memory fake at the <c>DataverseWebApiClient</c> boundary, so "no read / no write
/// ran" is observable as the absence of a recorded call.
/// </para>
/// <para>ADR-038 §2 path #1 (security-auth KEEP). ADR-008: the decision is the group filter's.</para>
/// </remarks>
public sealed class SpeAdminConfigAndBulkTenantScopeTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");

    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("cb000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("c0000000-0000-0000-0000-00000000000e");

    private static readonly Guid NoSuchUnit = Guid.Parse("1f000000-0000-0000-0000-0000000000ff");
    private static readonly Guid NoSuchConfig = Guid.Parse("cf000000-0000-0000-0000-0000000000ff");

    // Config B's identity — what a Unit-A admin must not borrow.
    private const string TypeB = "bbbbbbbb-0000-0000-0000-00000000000b";
    private const string AppB = "b0b0b0b0-0000-0000-0000-00000000000b";
    private const string SecretB = "spe-owning-app-unit-b";
    private const string ConsumingAppB = "cbcbcbcb-0000-0000-0000-00000000000b";
    // Conforming (round 35 item 3), so naming it as one's OWN keyVaultSecretName reaches the identity rule (403), not the
    // prefix rule (400).
    private const string ConsumingSecretB = "spe-owning-app-unit-b-consuming";

    private const string TypeA = "aaaaaaaa-0000-0000-0000-00000000000a";
    private const string AppA = "a0a0a0a0-0000-0000-0000-00000000000a";
    private const string SecretA = "spe-owning-app-unit-a";
    private const string AppN = "e0e0e0e0-0000-0000-0000-00000000000e";

    private const string OutOfScopeCode = "spe.admin.deny.config_out_of_scope";
    private const string AmbiguousCode = "spe.admin.deny.config_id_ambiguous";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";
    private const string BusinessUnitCode = "spe.admin.deny.business_unit_out_of_scope";
    private const string IdentityCode = "spe.admin.deny.config_identity_out_of_scope";

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminConfigAndBulkTenantScopeTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/configs/{configId} — finding #73
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_OutOfScopeConfig_AnswersTheSame404AsAnUnknownConfig_AndReadsNothing()
    {
        using var client = Admin();
        var unknown = Guid.NewGuid();

        var outOfScope = await Problem(await client.GetAsync($"/api/spe/configs/{ConfigB}"));
        var missing = await Problem(await client.GetAsync($"/api/spe/configs/{unknown}"));

        AssertUniformNotFound(outOfScope, ConfigB);
        AssertUniformNotFound(missing, unknown);
        outOfScope.Keys.Should().BeEquivalentTo(missing.Keys, "the extension key set must not differ either");

        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().BeEmpty(
            "the handler's app-only read of the config must not run for a config the caller cannot reach");
    }

    [Fact]
    public async Task Get_InScopeConfig_IsServed()
    {
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/configs/{ConfigA}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ConfigRoute_WhoseQueryConfigIdDiffersFromTheRoute_Is400_BeforeAnyRead(string method)
    {
        using var client = Admin();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/spe/configs/{ConfigB}?configId={ConfigA}");
        if (method == "PUT") request.Content = JsonContent.Create(new { name = "renamed" });

        var problem = await Problem(await client.SendAsync(request), HttpStatusCode.BadRequest);

        problem["errorCode"].GetString().Should().Be(AmbiguousCode);
        _fixture.Dataverse.CallsOn(ConfigSet).Should().BeEmpty(
            "no route may authorize ?configId= while acting on the route value — nothing is read or written");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/spe/configs/{configId} — finding #45
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_OutOfScopeConfig_IsTheUniform404_AndNothingIsWritten()
    {
        using var client = Admin();

        var problem = await Problem(await client.PutAsJsonAsync(
            $"/api/spe/configs/{ConfigB}", new { businessUnitId = UnitA }));

        AssertUniformNotFound(problem, ConfigB);
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    public static TheoryData<string> UnreachableBusinessUnits() => new()
    {
        UnitB.ToString(),
        NoSuchUnit.ToString(),   // a GUID that is no business unit at all
    };

    [Theory]
    [MemberData(nameof(UnreachableBusinessUnits))]
    public async Task Put_ThatMovesAnInScopeConfigToAnUnreachableUnit_Is403_AndNothingIsWritten(string businessUnitId)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new { businessUnitId }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(BusinessUnitCode);
        problem["detail"].GetString().Should().Be("The business unit is not one you administer.",
            "another unit and a GUID that is no unit get ONE answer");
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    [Theory]
    [InlineData("consumingAppId", ConsumingAppB)]
    [InlineData("consumingAppKeyVaultSecret", ConsumingSecretB)]
    [InlineData("owningAppId", ConsumingAppB)]              // B's per-customer app as my owning app
    [InlineData("keyVaultSecretName", ConsumingSecretB)]    // B's per-customer secret as my owning secret
    public async Task Put_ThatRepointsAnIdentityFieldToAnotherUnitsPerCustomerIdentity_Is403_AndNothingIsWritten(
        string field, string value)
    {
        using var client = Admin();
        var body = new Dictionary<string, object?> { [field] = value };

        var problem = await Problem(
            await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", body),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(IdentityCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 20 item 3: configs of different customers may share a container type and its owning app (Model 1).
    /// Sharing them opens no other customer's containers — those are authorized per container
    /// (<c>SpeAdminPerContainerScopeTests</c>) — so a PUT that moves a config onto them is served.
    /// </summary>
    [Theory]
    [InlineData("containerTypeId", TypeB)]
    [InlineData("owningAppId", AppB)]
    [InlineData("keyVaultSecretName", SecretB)]
    public async Task Put_ThatSharesAnotherUnitsContainerTypeOrOwningApp_IsServed_Model1(string field, string value)
    {
        using var client = Admin();

        var response = await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new Dictionary<string, object?> { [field] = value });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().ContainSingle();
    }

    [Fact]
    public async Task Put_TheShippedClientsFullBody_WithUnchangedValues_IsServed_EvenWhenAnUnreachableConfigSharesOne()
    {
        // Config B (Unit B) also carries Config A's stored owning app — a value the caller is not
        // introducing. The shipped client's PUT re-sends every field unchanged (formStateToUpsert).
        _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.NewGuid(), UnitB, "other-type", AppA, "spe-owning-app-other"));
        using var client = Admin();

        var response = await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new
        {
            name = "Config A",
            containerTypeId = TypeA,
            owningAppId = AppA.ToUpperInvariant(),   // case-only difference is not a change
            keyVaultSecretName = SecretA,
            businessUnitId = UnitA,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().ContainSingle();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/spe/configs/{configId} — finding #75
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_OutOfScopeAndUnknownConfigs_AreTheUniform404_AndNothingIsDeleted()
    {
        using var client = Admin();
        var unknown = Guid.NewGuid();

        AssertUniformNotFound(await Problem(await client.DeleteAsync($"/api/spe/configs/{ConfigB}")), ConfigB);
        AssertUniformNotFound(await Problem(await client.DeleteAsync($"/api/spe/configs/{unknown}")), unknown);

        _fixture.Dataverse.CallsOn(ConfigSet, "Delete").Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_InScopeConfig_IsDeleted()
    {
        using var client = Admin();

        var response = await client.DeleteAsync($"/api/spe/configs/{ConfigA}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Dataverse.CallsOn(ConfigSet, "Delete").Should().ContainSingle(c => c.Id == ConfigA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/spe/configs — finding #74 (business unit)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Post_WithoutABusinessUnit_Is400_BeforeAnyDataverseCall(string? businessUnitId)
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(businessUnitId: businessUnitId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Dataverse.Calls.Should().BeEmpty("validation answers before any Dataverse call of any kind");
    }

    [Theory]
    [MemberData(nameof(UnreachableBusinessUnits))]
    public async Task Post_IntoAnUnreachableBusinessUnit_Is403_AndNothingIsCreated(string businessUnitId)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(businessUnitId: businessUnitId)),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(BusinessUnitCode);
        problem["detail"].GetString().Should().Be("The business unit is not one you administer.");
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Fact]
    public async Task Post_ByACallerWithNoResolvableBusinessUnit_IsTheSame403()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: null);
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(businessUnitId: UnitA.ToString())),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(BusinessUnitCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Fact]
    public async Task Post_InScope_WithAnUnborrowedIdentity_IsCreated()
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(businessUnitId: UnitA.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().ContainSingle();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/spe/configs — finding #74 (app identity)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("consumingAppId", ConsumingAppB)]
    [InlineData("consumingAppKeyVaultSecret", ConsumingSecretB)]
    [InlineData("consumingAppId", "  CBCBCBCB-0000-0000-0000-00000000000B  ")]   // trimmed, case-insensitive
    [InlineData("owningAppId", ConsumingAppB)]                                  // B's per-customer app as my OWNING app
    [InlineData("keyVaultSecretName", ConsumingSecretB)]                        // B's per-customer secret as my owning secret
    public async Task Post_BorrowingAnotherUnitsPerCustomerIdentity_Is403_AndNamesNeitherConfigNorUnit(
        string field, string value)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString(), (field, value))),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(IdentityCode);
        var detail = problem["detail"].GetString();
        detail.Should().NotContain(ConfigB.ToString()).And.NotContain(UnitB.ToString());
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 20 item 3 (Model 1): a config may share another customer's container type, owning app and that app's
    /// secret, and may name the shared owning app as its consuming app.
    /// </summary>
    [Theory]
    [InlineData("containerTypeId", TypeB)]
    [InlineData("owningAppId", AppB)]
    [InlineData("keyVaultSecretName", SecretB)]
    [InlineData("consumingAppId", AppB)]
    public async Task Post_SharingAnotherUnitsContainerTypeOwningAppOrItsSecret_IsCreated_Model1(string field, string value)
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString(), (field, value)));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().ContainSingle();
    }

    /// <summary>
    /// Another unit's GUID-valued identity, spelled in a non-canonical GUID form. The columns are free text
    /// and the request need not send the canonical <c>D</c> form, so a raw string compare would let these
    /// through as "new" values (verifier finding 6).
    /// </summary>
    public static TheoryData<string, string> AnotherUnitsGuidInANonCanonicalSpelling()
    {
        var data = new TheoryData<string, string>();
        foreach (var format in new[] { "N", "B", "P" })
        {
            // B's PER-CUSTOMER app, in each column it could be borrowed into (owner round 20 item 3 narrowed the
            // check to the per-customer identity; the shared type and owning app are Model 1).
            data.Add("consumingAppId", Guid.Parse(ConsumingAppB).ToString(format));
            data.Add("owningAppId", Guid.Parse(ConsumingAppB).ToString(format).ToUpperInvariant());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AnotherUnitsGuidInANonCanonicalSpelling))]
    public async Task Post_BorrowingAnotherUnitsGuid_InANonCanonicalSpelling_Is403(string field, string value)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString(), (field, value))),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(IdentityCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AnotherUnitsGuidInANonCanonicalSpelling))]
    public async Task Put_RepointingToAnotherUnitsGuid_InANonCanonicalSpelling_Is403(string field, string value)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new Dictionary<string, object?> { [field] = value }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(IdentityCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    [Fact]
    public async Task Post_WhenTheOtherUnitStoredItsGuidNonCanonically_TheCanonicalSpellingIsStill403()
    {
        // The STORED side may be the odd spelling: unit B's row holds its CONSUMING app id with no hyphens.
        var storedN = Guid.Parse("b1b1b1b1-0000-0000-0000-00000000001b");
        _fixture.Dataverse.Add(ConfigSet, ConfigRow(
            Guid.NewGuid(), UnitB, "other-type-n", "o1o1o1o1-0000-0000-0000-00000000001b", "spe-owning-app-other-n", storedN.ToString("N")));
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString(), ("consumingAppId", storedN.ToString("D")))),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(IdentityCode);
    }

    [Fact]
    public async Task Put_ReSendingItsOwnStoredGuid_InANonCanonicalSpelling_IsNotAChange_AndIsServed()
    {
        // Config B also carries Config A's owning app. Re-sending A's own value as {B}-braced is not a new
        // borrowing, exactly as a case-only difference is not (D1).
        _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.NewGuid(), UnitB, "other-type", AppA, "spe-owning-app-other"));
        using var client = Admin();

        var response = await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new
        {
            owningAppId = Guid.Parse(AppA).ToString("B"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(AppN)]   // carried only by a config with NO business unit (compatibility rule)
    [InlineData(AppA)]   // carried only by a config in a reachable unit
    public async Task Post_WithAValueCarriedOnlyByABusinessUnitLessOrReachableConfig_IsCreated(string owningAppId)
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync(
            "/api/spe/configs", NewConfig(UnitA.ToString(), ("owningAppId", owningAppId)));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_WhenTheIdentityReadFaults_Is503_AndNothingIsCreated()
    {
        _fixture.Dataverse.FaultQueriesOn(ConfigSet);
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString())),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Fact]
    public async Task Post_WhenTheIdentityReadReturnsAFullPage_Is503_AndNothingIsCreated()
    {
        // 5000 rows = the read limit: the check cannot prove it saw every row.
        for (var i = 0; i < 5000; i++)
        {
            _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.NewGuid(), null, $"t{i}", $"a{i}", $"s{i}"));
        }

        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString())),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/spe/bulk/delete + /bulk/permissions — findings #44, #72
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> BulkRoutesAndScopedOutConfigs() => new()
    {
        { "/api/spe/bulk/delete", ConfigB.ToString() },
        { "/api/spe/bulk/delete", NoSuchConfig.ToString() },
        { "/api/spe/bulk/permissions", ConfigB.ToString() },
        { "/api/spe/bulk/permissions", NoSuchConfig.ToString() },
    };

    [Theory]
    [MemberData(nameof(BulkRoutesAndScopedOutConfigs))]
    public async Task Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued(
        string route, string configId)
    {
        using var client = Admin();
        var before = _fixture.BulkOperations.TrackedOperationCount;

        var problem = await Problem(await client.PostAsJsonAsync(route, BulkBody(route, configId)));

        AssertUniformNotFound(problem, Guid.Parse(configId));
        _fixture.BulkOperations.TrackedOperationCount.Should().Be(before, "a refused request enqueues nothing");
    }

    [Theory]
    [InlineData("/api/spe/bulk/delete")]
    [InlineData("/api/spe/bulk/permissions")]
    public async Task Bulk_WithAnInScopeBodyConfigId_IsAccepted_AndEnqueuesOne(string route)
    {
        using var client = Admin();
        var before = _fixture.BulkOperations.TrackedOperationCount;

        var response = await client.PostAsJsonAsync(route, BulkBody(route, ConfigA.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        _fixture.BulkOperations.TrackedOperationCount.Should().Be(before + 1);
    }

    [Theory]
    [InlineData("/api/spe/bulk/delete")]
    [InlineData("/api/spe/bulk/permissions")]
    public async Task Bulk_WhoseQueryAndBodyConfigIdsDisagree_Is400_BeforeAnyScopeRead(string route)
    {
        using var client = Admin();
        var before = _fixture.BulkOperations.TrackedOperationCount;

        var differing = await Problem(
            await client.PostAsJsonAsync($"{route}?configId={ConfigA}", BulkBody(route, ConfigB.ToString())),
            HttpStatusCode.BadRequest);
        var unparseableQuery = await Problem(
            await client.PostAsJsonAsync($"{route}?configId=not-a-guid", BulkBody(route, ConfigA.ToString())),
            HttpStatusCode.BadRequest);

        differing["errorCode"].GetString().Should().Be(AmbiguousCode);
        unparseableQuery["errorCode"].GetString().Should().Be(AmbiguousCode);
        _fixture.Dataverse.Calls.Where(c => c.Operation == "Query").Should().BeEmpty(
            "no scope query may run on either value");
        _fixture.BulkOperations.TrackedOperationCount.Should().Be(before);
    }

    [Theory]
    [InlineData("/api/spe/bulk/delete")]
    [InlineData("/api/spe/bulk/permissions")]
    public async Task Bulk_WithAnUnparseableBodyConfigIdAndNoQuery_GetsTheHandlersOwn400(string route)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync(route, BulkBody(route, "not-a-guid")),
            HttpStatusCode.BadRequest);

        problem["detail"].GetString().Should().Be("configId is required and must be a valid GUID.");
        problem.Should().NotContainKey("errorCode");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fail closed — every filtered route, both reads the decision depends on
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> FilteredRoutesAndFaults()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var fault in new[] { ConfigSet, "businessunits" })
        {
            data.Add("GET", $"/api/spe/configs/{ConfigA}", fault);
            data.Add("PUT", $"/api/spe/configs/{ConfigA}", fault);
            data.Add("DELETE", $"/api/spe/configs/{ConfigA}", fault);
            data.Add("POST", "/api/spe/bulk/delete", fault);
            data.Add("POST", "/api/spe/bulk/permissions", fault);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FilteredRoutesAndFaults))]
    public async Task FilteredRoute_WhenTheScopeCannotBeRead_Is503_AndNothingRunsOnTheConfig(
        string method, string url, string faultingSet)
    {
        _fixture.Dataverse.FaultQueriesOn(faultingSet);
        using var client = Admin();
        var before = _fixture.BulkOperations.TrackedOperationCount;

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT") request.Content = JsonContent.Create(new { name = "x" });
        if (method == "POST") request.Content = JsonContent.Create(BulkBody(url, ConfigA.ToString()));

        var problem = await Problem(await client.SendAsync(request), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve", "Create", "Update", "Delete").Should().BeEmpty();
        _fixture.BulkOperations.TrackedOperationCount.Should().Be(before);
    }

    [Fact]
    public async Task Post_WhenTheHierarchyCannotBeRead_Is503_AndNothingIsCreated()
    {
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA.ToString())),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Fact]
    public async Task ListConfigs_WhenTheHierarchyCannotBeRead_KeepsItsExisting500()
    {
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var response = await client.GetAsync("/api/spe/configs");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layer 1 unchanged, and the regression control
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> TheSixRoutes() => new()
    {
        { "GET", $"/api/spe/configs/{ConfigA}" },
        { "PUT", $"/api/spe/configs/{ConfigA}" },
        { "DELETE", $"/api/spe/configs/{ConfigA}" },
        { "POST", "/api/spe/configs" },
        { "POST", "/api/spe/bulk/delete" },
        { "POST", "/api/spe/bulk/permissions" },
    };

    [Theory]
    [MemberData(nameof(TheSixRoutes))]
    public async Task TheSixRoutes_RefuseANonAdmin_WithTheRoleFilters403_AndAnAnonymousCaller_With401(
        string method, string url)
    {
        using var nonAdmin = _fixture.CreateCaller();
        using var anonymous = _fixture.CreateAnonymous();

        var forbidden = await Problem(await nonAdmin.SendAsync(Body(method, url)), HttpStatusCode.Forbidden);
        var unauthorized = await anonymous.SendAsync(Body(method, url));

        forbidden["reasonCode"].GetString().Should().Be("sdap.access.deny.role_insufficient");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnotherConfigScopedRoute_ForAnInScopeConfig_StillReachesItsHandler()
    {
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/containers?configId={ConfigA}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        body.Should().NotContain(OutOfScopeCode).And.NotContain(UnverifiableCode).And.NotContain(AmbiguousCode,
            "the filter must let an in-scope ?configId= through to the handler");

        // Positive proof the HANDLER ran (a routing 404 or a pre-handler 500 would satisfy the lines above):
        // the filter only QUERIES the config table; the handler's SpeAdminGraphService.ResolveConfigAsync
        // RETRIEVES the named config by id.
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().Contain(c => c.Id == ConfigA,
            "the containers handler resolves the in-scope config it was given");
    }

    [Fact]
    public async Task AnotherConfigScopedRoute_ForAnOutOfScopeConfig_NeverReachesItsHandler()
    {
        // The negative twin of the test above: same route, same probe, a config in Unit B.
        using var client = Admin();

        AssertUniformNotFound(await Problem(await client.GetAsync($"/api/spe/containers?configId={ConfigB}")), ConfigB);

        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().BeEmpty(
            "the handler's config resolution must not run for a config the caller cannot reach");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The config handlers' OWN not-found paths give the filter's exact 404 (owner round 25 item 5; verifier V20)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A config deleted between the filter's read and the handler's own read reaches the handler's not-found path. That
    /// answer must be the filter's uniform 404 (errorCode + traceId; no correlationId) — otherwise "gone just now" and
    /// "not yours" would differ, and the handler path is otherwise unreachable, so nothing else would notice it drift.
    /// </summary>
    [Theory]
    [InlineData("GET", false)]
    [InlineData("GET", true)]     // the Web API's 404 rather than an empty read
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    public async Task AConfigGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404(string method, bool asWebApi404)
    {
        _fixture.Dataverse.RetrieveMisses[ConfigA] = asWebApi404;
        using var client = Admin();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/spe/configs/{ConfigA}");
        if (method == "PUT") request.Content = JsonContent.Create(new { name = "renamed" });

        var fromHandler = await Problem(await client.SendAsync(request));
        var fromFilter = await Problem(await client.GetAsync($"/api/spe/configs/{ConfigB}"));

        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().Contain(c => c.Id == ConfigA,
            "the filter let the in-scope config through and the HANDLER's read missed — its own not-found path ran");
        AssertUniformNotFound(fromHandler, ConfigA);
        fromHandler.Keys.Should().BeEquivalentTo(fromFilter.Keys, "the handler's 404 must be the filter's, key for key");
        _fixture.Dataverse.CallsOn(ConfigSet, "Update", "Delete").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // containertypes register — the scope rules only (batch-4 integration, round 65)
    // ─────────────────────────────────────────────────────────────────────────

    // Task 165's sharePointAdminUrl host check is RETIRED with what it guarded. It existed because register sent an
    // APP-ONLY token to that host; master bb8ba7251 made register a delegated Graph grant that ignores the URL, so no
    // token goes anywhere the caller names. The type-wide rule (root admin of a Spaarke-operated environment) stays.
    [Fact]
    public async Task Register_ByARootAdmin_PassesEverySpeAdminRule_AndTheUrlIsNotConsulted()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        using var client = Admin();

        var response = await client.PostAsJsonAsync(
            $"/api/spe/containertypes/{TypeA}/register?configId={ConfigA}",
            RegisterBody("https://evil.example.com"));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("spe.admin.deny.", "the request got past every SPE admin rule — " + body);
        body.Should().NotContain("sharepoint_url", "the URL is ignored since registration became a Graph grant");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private void SeedTenant(Guid? callerUnit)
    {
        var dv = _fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });

        if (callerUnit is { } unit)
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
                ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000001"),
                ["_businessunitid_value"] = unit,
            });
        }

        dv.Add(ConfigSet, ConfigRow(ConfigA, UnitA, TypeA, AppA, SecretA));
        dv.Add(ConfigSet, ConfigRow(ConfigB, UnitB, TypeB, AppB, SecretB, ConsumingAppB, ConsumingSecretB));
        dv.Add(ConfigSet, ConfigRow(ConfigN, null, "eeeeeeee-0000-0000-0000-00000000000e", AppN, "spe-owning-app-no-unit"));
    }

    private static Dictionary<string, object?> ConfigRow(
        Guid id, Guid? unit, string type, string app, string secret,
        string? consumingApp = null, string? consumingSecret = null) => new()
        {
            ["sprk_specontainertypeconfigid"] = id,
            ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
            ["sprk_name"] = $"Config {id.ToString()[..2]}",
            ["_sprk_businessunit_value"] = unit,
            ["sprk_containertypeid"] = type,
            ["sprk_owningappid"] = app,
            ["sprk_keyvaultsecretname"] = secret,
            ["sprk_consumingappid"] = consumingApp,
            ["sprk_consumingappkvsecret"] = consumingSecret,
            ["sprk_billingclassification"] = 100000001,
            ["statecode"] = 0,
        };

    private static Dictionary<string, object?> NewConfig(
        string? businessUnitId, params (string Field, string Value)[] overrides)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "New config",
            ["containerTypeId"] = "dddddddd-0000-0000-0000-00000000000d",
            ["owningAppId"] = "d0d0d0d0-0000-0000-0000-00000000000d",
            ["keyVaultSecretName"] = "spe-owning-app-new-config",
            ["businessUnitId"] = businessUnitId,
        };

        foreach (var (field, value) in overrides) body[field] = value;
        return body;
    }

    private static object BulkBody(string route, string configId) =>
        route.EndsWith("/permissions", StringComparison.Ordinal)
            ? new { containerIds = new[] { "b!container-1" }, configId, userId = "u-1", role = "owner" }
            : new { containerIds = new[] { "b!container-1" }, configId };

    private static object RegisterBody(string sharePointAdminUrl) => new
    {
        appId = "f0f0f0f0-0000-0000-0000-00000000000f",
        sharePointAdminUrl,
        delegatedPermissions = new[] { "ReadContent" },
        applicationPermissions = Array.Empty<string>(),
    };

    private static HttpRequestMessage Body(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = url.Contains("/bulk/", StringComparison.Ordinal)
                ? JsonContent.Create(BulkBody(url, ConfigA.ToString()))
                : JsonContent.Create(NewConfig(UnitA.ToString()));
        }

        return request;
    }

    private static void AssertUniformNotFound(Dictionary<string, JsonElement> problem, Guid configId)
    {
        problem["status"].GetInt32().Should().Be(404);
        problem["title"].GetString().Should().Be("Not Found");
        problem["detail"].GetString().Should().Be($"Container type config '{configId}' was not found.");
        problem["errorCode"].GetString().Should().Be(OutOfScopeCode);
        problem.Keys.Except(new[] { "type", "title", "status", "detail" })
            .Should().BeEquivalentTo(new[] { "errorCode", "traceId" });
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.NotFound)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement;
    }
}
