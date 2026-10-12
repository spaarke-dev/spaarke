using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// F-165-RB (live gate 2026-10-11) — the SPE admin plane's per-container rule with the REAL ownership guard (task 227d)
/// in front of the business-unit binding (task 165): a container this stamp does not own is the ONE uniform 404, exactly
/// like an id that names nothing; an ownership read that gets no answer is the 503; a container this stamp owns, bound
/// to a unit the caller reaches, is acted on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> The guard refuses a container that is not this stamp's with an <c>SdapProblemException</c>
/// (<c>spe_container_not_owned</c>). The per-container rule read that refusal as "the binding could not be read" and
/// answered <b>503</b> <c>spe.admin.deny.scope_unverifiable</c> ("try again shortly") — for another customer's container,
/// for an unmarked one, for a soft-deleted one (Graph returns no marker from <c>deletedContainers/{id}</c>, measured on dev
/// 2026-10-11), and for an id that names nothing. A refusal is a final answer about the id, so it is now the uniform 404
/// of task 165 §11.3. The 404 and the 503 are both refusals; nothing is acted on either way.
/// </para>
/// <para>
/// Every other test of this plane runs a guard that admits every container (<see cref="AdminSurfaceHostFixture"/>), which
/// is why the interaction was never observed. Containers here (fake Graph): <c>rb-own</c> (this stamp's marker, root
/// unit), <c>rb-foreign</c> (another customer's marker), <c>rb-unmarked</c> (no marker — what Graph returns for a
/// deleted container today), <c>rb-unitb</c> (this stamp's marker, Unit B), <c>rb-fault</c> (the marker read fails),
/// <c>rb-nowhere</c> (Graph knows no such container). ADR-038 §2 path #1 (security-auth KEEP).
/// </para>
/// </remarks>
public sealed class SpeAdminContainerOwnershipRefusalTests : IClassFixture<AdminSurfaceRealOwnershipHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string DeletedPath = "/storage/fileStorage/deletedContainers";
    private const string ContainerCode = "spe.admin.deny.container_out_of_scope";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";
    private const string OtherCustomer = "cust2";
    private const string TypeT = "77777777-0000-0000-0000-000000000077";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");
    private static readonly Guid Config = Guid.Parse("ca000000-0000-0000-0000-00000000000a");

    private readonly AdminSurfaceRealOwnershipHostFixture _fixture;

    public SpeAdminContainerOwnershipRefusalTests(AdminSurfaceRealOwnershipHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string, string> RecycleBinRoutesOnNotOwnedContainers() => new()
    {
        { "POST",   "rb-foreign" },
        { "DELETE", "rb-foreign" },
        { "POST",   "rb-unmarked" },
        { "DELETE", "rb-unmarked" },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Not this stamp's → the uniform 404, the same as an id that names nothing
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RecycleBinRoutesOnNotOwnedContainers))]
    public async Task RestoreOrPurge_OfADeletedContainerThisStampDoesNotOwn_IsTheUniformRecycleBin404_LikeARandomId(
        string method, string containerId)
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var notOwned = await Problem(await client.SendAsync(RecycleBinRequest(method, containerId)));
        var nowhere = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-nowhere")));

        AssertUniformNotFound(notOwned, containerId, recycleBin: true);
        AssertUniformNotFound(nowhere, "rb-nowhere", recycleBin: true);
        notOwned.Keys.Should().BeEquivalentTo(nowhere.Keys);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET",
            "only ownership was read; nothing was restored or purged");
    }

    [Theory]
    [InlineData("rb-foreign")]
    [InlineData("rb-unmarked")]
    public async Task AnActiveContainerRoute_OnAContainerThisStampDoesNotOwn_IsTheUniform404_LikeARandomId(string containerId)
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var notOwned = await Problem(await client.GetAsync($"/api/spe/containers/{containerId}?configId={Config}"));
        var nowhere = await Problem(await client.GetAsync($"/api/spe/containers/rb-nowhere?configId={Config}"));

        AssertUniformNotFound(notOwned, containerId, recycleBin: false);
        AssertUniformNotFound(nowhere, "rb-nowhere", recycleBin: false);
        notOwned.Keys.Should().BeEquivalentTo(nowhere.Keys);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RestoreOrPurge_OfThisStampsDeletedContainerInAnotherUnit_IsTheSame404ForALeafAdmin(string method)
    {
        // NOT live behaviour for the marker: live Graph returns no customProperties for a deleted container (#1652 comment
        // 6117086899, dev 2026-10-12), so live this is refused one step earlier (not owned) — the same 404 either way. The
        // stubbed marker on rb-unitb isolates the business-unit refusal; a readable marker live is #1652 part 1.
        Seed(callerUnit: UnitA);
        using var client = Admin();

        var otherUnit = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-unitb")));
        var nowhere = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-nowhere")));

        AssertUniformNotFound(otherUnit, "rb-unitb", recycleBin: true);
        otherUnit.Keys.Should().BeEquivalentTo(nowhere.Keys);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET", "nothing was restored or purged");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fail closed: an ownership read with no answer is the 503, never an act
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RestoreOrPurge_WhenTheOwnershipReadGetsNoAnswer_Is503_AndNothingIsActedOn(string method)
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(RecycleBinRequest(method, "rb-fault")), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET", "nothing was restored or purged");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Positive control: this stamp's container, in the caller's reach, is acted on
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_OfThisStampsDeletedContainer_ForARootAdmin_IsRestored()
    {
        // NOT live behaviour: on live Graph today this restore answers the uniform 404, not 200. Graph discards a
        // container's customProperties on soft delete (measured on dev 2026-10-12, #1652 comment 6117086899), so the
        // ownership marker this stub returns from deletedContainers/{id} is a state live Graph never produces. The test pins
        // the decision GIVEN a readable marker; making it reachable live is #1652 part 1.
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", "{\"id\":\"rb-own\"}");
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "POST" && r.Path == $"{DeletedPath}/rb-own/restore");
    }

    [Fact]
    public async Task RecycleBinList_ShowsOnlyThisStampsDeletedContainersInTheCallersReach()
    {
        // Pins the trim GIVEN a readable marker. Live Graph returns no customProperties from deletedContainers/{id}
        // (F-165-RB, measured on dev 2026-10-11; confirmed 2026-10-12 that soft delete discards them, #1652 comment
        // 6117086899), so on dev every deleted container is dropped here today — that half of
        // the defect needs an owner decision (see the issue) and is not fixed by this change.
        Seed(callerUnit: UnitA);
        _fixture.Graph.StubGetExact(DeletedPath,
            "{\"value\":[" + string.Join(",", new[] { "rb-leafown", "rb-unitb", "rb-foreign", "rb-unmarked" }.Select(id =>
                "{\"id\":\"" + id + "\",\"displayName\":\"Deleted " + id + "\",\"containerTypeId\":\"" + TypeT + "\"}")) + "]}");
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/recyclebin?configId={Config}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).Should().Equal("rb-leafown");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private static HttpRequestMessage RecycleBinRequest(string method, string containerId) =>
        new(new HttpMethod(method), method == "POST"
            ? $"/api/spe/recyclebin/{containerId}/restore?configId={Config}"
            : $"/api/spe/recyclebin/{containerId}?configId={Config}");

    private void Seed(Guid callerUnit)
    {
        var dv = _fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow());
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });
        dv.Add("systemusers", new()
        {
            ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
            ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000001"),
            ["_businessunitid_value"] = callerUnit,
        });
        // The config is the caller's own unit's, so the config rule admits it and the per-container rule decides.
        dv.Add(ConfigSet, new()
        {
            ["sprk_specontainertypeconfigid"] = Config,
            ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
            ["sprk_name"] = "Config rb",
            ["_sprk_businessunit_value"] = callerUnit,
            ["sprk_containertypeid"] = TypeT,
            ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
            ["sprk_keyvaultsecretname"] = "spe-owning-app-shared",
            ["statecode"] = 0,
        });

        var own = AdminSurfaceRealOwnershipHostFixture.Customer;
        StubBoth("rb-own", own, Root);
        StubBoth("rb-leafown", own, UnitA);
        StubBoth("rb-unitb", own, UnitB);
        StubBoth("rb-foreign", OtherCustomer, Root);
        StubBoth("rb-unmarked", null, Root);
        _fixture.Graph.StubGet($"{DeletedPath}/rb-fault", """{"error":{"code":"serviceNotAvailable","message":"x"}}""", 503);
    }

    /// <summary>The container, active and in the bin, carrying <paramref name="marker"/> (null = none) and the unit stamp.</summary>
    private void StubBoth(string id, string? marker, Guid unit)
    {
        var properties = new List<string>
        {
            "\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + unit + "\",\"isSearchable\":false}",
        };
        if (marker is not null)
        {
            properties.Add("\"" + SpeContainerOwnershipGuard.MarkerPropertyName + "\":{\"value\":\"" + marker + "\",\"isSearchable\":false}");
        }

        var json = "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"containerTypeId\":\"" + TypeT +
                   "\",\"status\":\"active\",\"customProperties\":{" + string.Join(",", properties) + "}}";
        _fixture.Graph.StubGet($"{ContainersPath}/{id}", json);
        _fixture.Graph.StubGet($"{DeletedPath}/{id}", json);
    }

    private static void AssertUniformNotFound(Dictionary<string, JsonElement> problem, string containerId, bool recycleBin)
    {
        problem["status"].GetInt32().Should().Be(404);
        problem["title"].GetString().Should().Be("Not Found");
        problem["detail"].GetString().Should().Be(recycleBin
            ? $"Container '{containerId}' was not found in the recycle bin."
            : $"Container '{containerId}' was not found.");
        problem["errorCode"].GetString().Should().Be(ContainerCode);
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
}
