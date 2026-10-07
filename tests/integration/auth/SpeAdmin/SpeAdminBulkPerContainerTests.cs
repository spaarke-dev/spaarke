using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 20 item 2 — the bulk routes authorize PER CONTAINER, end to end: the
/// request is accepted with the caller's reach captured, the background job decides every container against it, and
/// only the caller who started the operation can read its status.
/// </summary>
/// <remarks>
/// Root → {Unit A, Unit B}; Config A (Unit A) and Config B (Unit B) share container type T (Model 1). A leaf admin of
/// Unit A names, through Config A, its own container and Unit B's container of the same type. The real host, the real
/// <see cref="BulkOperationService"/> loop and the real <c>SpeAdminGraphService</c> against the WireMock Graph run; "no
/// write" is the absence of a recorded DELETE / permissions POST. ADR-038 §2 path #1.
/// </remarks>
public sealed class SpeAdminBulkPerContainerTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string ContainersPath = "/storage/fileStorage/containers";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("cb000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("c0000000-0000-0000-0000-00000000000e");
    private const string TypeT = "77777777-0000-0000-0000-000000000077";

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminBulkPerContainerTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        Seed();
    }

    [Fact]
    public async Task BulkDelete_ByALeafAdmin_DeletesItsOwnContainer_AndRefusesAnotherCustomersAndAnUnboundOne()
    {
        await _fixture.StartBulkProcessorOnceAsync();
        using var client = Admin();

        var operationId = await Accepted(await client.PostAsJsonAsync("/api/spe/bulk/delete",
            new { containerIds = new[] { "c-own", "c-other", "c-unbound" }, configId = ConfigA.ToString() }));
        var status = await FinishedStatusAsync(client, operationId);

        status.GetProperty("completed").GetInt32().Should().Be(1);
        status.GetProperty("failed").GetInt32().Should().Be(2);
        status.GetProperty("errors").EnumerateArray()
            .Select(e => (Id: e.GetProperty("containerId").GetString(), Message: e.GetProperty("errorMessage").GetString()))
            .Should().BeEquivalentTo(new[]
            {
                (Id: (string?)"c-other", Message: (string?)BulkOperationService.ContainerNotInScopeError),
                (Id: (string?)"c-unbound", Message: (string?)BulkOperationService.ContainerNotInScopeError),
            });

        Writes("DELETE").Select(r => r.Path).Should().BeEquivalentTo(new[] { $"{ContainersPath}/c-own" },
            "Unit B's container of the same type, and the unbound one, are never deleted");
    }

    [Fact]
    public async Task BulkPermissions_ByALeafAdmin_GrantsOnlyOnItsOwnContainer()
    {
        await _fixture.StartBulkProcessorOnceAsync();
        _fixture.Graph.StubPost($"{ContainersPath}/c-own/permissions",
            """{"id":"p1","roles":["owner"],"grantedToV2":{"user":{"id":"u-1","displayName":"U"}}}""", 201);
        using var client = Admin();

        var operationId = await Accepted(await client.PostAsJsonAsync("/api/spe/bulk/permissions",
            new { containerIds = new[] { "c-own", "c-other" }, configId = ConfigA.ToString(), userId = "u-1", role = "owner" }));
        var status = await FinishedStatusAsync(client, operationId);

        status.GetProperty("completed").GetInt32().Should().Be(1);
        status.GetProperty("failed").GetInt32().Should().Be(1);
        Writes("POST").Select(r => r.Path).Should().BeEquivalentTo(new[] { $"{ContainersPath}/c-own/permissions" });
    }

    [Fact]
    public async Task BulkDelete_WhenTheCallersScopeCannotBeRead_Is503_AndNothingIsEnqueued()
    {
        // Through the UNIT-LESS Config N: the configId rule admits it without reading the hierarchy (the compatibility
        // rule), so the 503 can only come from the bulk route's own capture of the caller's scope.
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        var before = _fixture.BulkOperations.TrackedOperationCount;
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/bulk/delete",
            new { containerIds = new[] { "c-own" }, configId = ConfigN.ToString() });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        body.Should().Contain("spe.admin.deny.scope_unverifiable");
        _fixture.BulkOperations.TrackedOperationCount.Should().Be(before);
    }

    [Fact]
    public async Task TheStatusOfAnOperation_IsOnlyForTheCallerWhoStartedIt_AnyoneElseGetsTheUnknownIds404()
    {
        using var starter = Admin();
        using var otherAdmin = Admin(oid: "0b7d1f60-9c3a-4d21-8f5e-2a6b7c8d9e02");

        var operationId = await Accepted(await starter.PostAsJsonAsync("/api/spe/bulk/delete",
            new { containerIds = new[] { "c-own" }, configId = ConfigA.ToString() }));

        (await starter.GetAsync($"/api/spe/bulk/{operationId}/status")).StatusCode.Should().Be(HttpStatusCode.OK);

        var asOther = await otherAdmin.GetAsync($"/api/spe/bulk/{operationId}/status");
        var unknown = await otherAdmin.GetAsync($"/api/spe/bulk/{Guid.NewGuid()}/status");

        asOther.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await asOther.Content.ReadAsStringAsync())!;
        var unknownBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await unknown.Content.ReadAsStringAsync())!;
        otherBody.Keys.Should().BeEquivalentTo(unknownBody.Keys, "another admin's operation must look exactly like none");
        (await asOther.Content.ReadAsStringAsync()).Should().NotContain("c-own");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private HttpClient Admin(string? oid = null)
    {
        var client = _fixture.CreateCaller(new[] { "Admin" });
        if (oid is not null) client.DefaultRequestHeaders.Add(AdminSurfaceHostFixture.OidHeader, oid);
        return client;
    }

    private void Seed()
    {
        var dv = _fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });

        foreach (var oid in new[] { AdminSurfaceHostFixture.CallerOid, Guid.Parse("0b7d1f60-9c3a-4d21-8f5e-2a6b7c8d9e02") })
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = oid,
                ["systemuserid"] = Guid.NewGuid(),
                ["_businessunitid_value"] = UnitA,
            });
        }

        dv.Add(ConfigSet, ConfigRow(ConfigA, UnitA));
        dv.Add(ConfigSet, ConfigRow(ConfigB, UnitB));
        dv.Add(ConfigSet, ConfigRow(ConfigN, null));

        StubContainer("c-own", UnitA.ToString());
        StubContainer("c-other", UnitB.ToString());
        StubContainer("c-unbound", null);
        _fixture.Graph.StubDelete($"{ContainersPath}/c-own");
    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
        ["sprk_name"] = $"Config {id.ToString()[..2]}",
        ["_sprk_businessunit_value"] = unit,
        ["sprk_containertypeid"] = TypeT,
        ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
        ["sprk_keyvaultsecretname"] = "spe-owning-app-shared",
        ["statecode"] = 0,
    };

    private void StubContainer(string id, string? stamp)
    {
        var properties = stamp is null
            ? "{}"
            : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + stamp + "\",\"isSearchable\":false}}";
        _fixture.Graph.StubGet($"{ContainersPath}/{id}",
            "{\"id\":\"" + id + "\",\"containerTypeId\":\"" + TypeT + "\",\"customProperties\":" + properties + "}");
    }

    private IEnumerable<Sprk.Bff.Api.Tests.Contract.SpeAdmin.RecordedGraphRequest> Writes(string method) =>
        _fixture.Graph.AllRequests.Where(r => string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase));

    private static async Task<Guid> Accepted(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetGuid();
    }

    private static async Task<JsonElement> FinishedStatusAsync(HttpClient client, Guid operationId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var response = await client.GetAsync($"/api/spe/bulk/{operationId}/status");
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            var status = JsonDocument.Parse(body).RootElement.Clone();
            if (status.GetProperty("isFinished").GetBoolean()) return status;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Bulk operation {operationId} did not finish.");
    }
}
