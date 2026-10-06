using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 — the SPE admin dashboard is projected onto the configs a caller reaches, and the
/// sync job (an <c>IScheduledJob</c> since this task) attributes every container to the config of the business unit that
/// owns it, so a leaf admin's view carries ITS configs' storage (owner round 25 item 5) and never another customer's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tenant.</b> Root → {Unit A → {Unit A-sub}, Unit B}. Config A (Unit A) and Config B (Unit B) share container
/// type T (Model 1); Config N has no unit. Containers of type T: <c>c-own</c> (A, 100 B), <c>c-sub</c> (A-sub, 50 B),
/// <c>c-other</c> (B, 1000 B), <c>c-unbound</c> (5 B), <c>c-foreign</c> (a unit this environment does not know, 7 B).
/// </para>
/// <para>
/// The end-to-end tests run the REAL job through <c>ScheduledJobHost.TriggerNowAsync</c> (the refresh route) against the
/// fake Dataverse and the WireMock Graph; the projection tests seed the cache directly. ADR-038 §2 path #1.
/// </para>
/// </remarks>
public sealed class SpeAdminDashboardScopeTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";
    private const string CompletenessReason =
        "3 config record(s) skipped as incomplete (missing container type, owning app, secret name, or environment tenant).";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASub = Guid.Parse("1a100000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");
    private static readonly Guid ForeignUnit = Guid.Parse("f0000000-0000-0000-0000-0000000000ff");

    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("cb000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("c0000000-0000-0000-0000-00000000000e");
    private static readonly Guid Environment = Guid.Parse("e0000000-0000-0000-0000-0000000000e1");

    private const string TypeT = "77777777-0000-0000-0000-000000000077";

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminDashboardScopeTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        Cache.Remove(SpeDashboardSyncService.CacheKey);
        SeedTenant(callerUnit: UnitA);
    }

    private IDistributedCache Cache => _fixture.Services.GetRequiredService<IDistributedCache>();

    // ─────────────────────────────────────────────────────────────────────────
    // Projection of a cached aggregate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ALeafAdmin_SeesOnlyItsConfigs_WithItsOwnStorage_NotZero()
    {
        await SeedAggregateAsync();
        using var client = Admin();

        var metrics = await Json(await client.GetAsync("/api/spe/dashboard/metrics"));

        metrics.GetProperty("containerCountByConfig").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { ConfigA.ToString(), ConfigN.ToString() });
        metrics.GetProperty("totalContainerCount").GetInt32().Should().Be(5);
        metrics.GetProperty("totalStorageUsedInBytes").GetInt64().Should().Be(300,
            "owner round 25 item 5: a partial view reports its own configs' storage (A 100 + N 200), not 0");
        metrics.GetProperty("storageReportingContainerCount").GetInt32().Should().Be(3);
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(0,
            "unbound containers are a root-unit administrator's only");
        metrics.GetRawText().Should().NotContain(ConfigB.ToString()).And.NotContain(CompletenessReason);
        metrics.GetProperty("syncHealth").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task APlatformOperator_WhoReachesEveryConfig_SeesTheWholeAggregate_IncludingUnattributedContainers()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        await SeedAggregateAsync();
        using var client = Admin();

        var metrics = await Json(await client.GetAsync("/api/spe/dashboard/metrics"));

        metrics.GetProperty("totalContainerCount").GetInt32().Should().Be(14);
        metrics.GetProperty("totalStorageUsedInBytes").GetInt64().Should().Be(4096);
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(2);
        metrics.GetRawText().Should().Contain(ConfigB.ToString()).And.Contain(CompletenessReason);
    }

    [Fact]
    public async Task ALeafAdmin_WhoReachesEveryConfig_SeesTheCompletenessCount_ButNeverUnattributedContainers()
    {
        // Config B is gone: Unit A's admin reaches every config (A and the unit-less N) — but is not a platform
        // operator, so the unbound containers (root-unit admins only) stay out of the view.
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA, includeConfigB: false);
        await SeedAggregateAsync();
        using var client = Admin();

        var metrics = await Json(await client.GetAsync("/api/spe/dashboard/metrics"));

        metrics.GetRawText().Should().Contain(CompletenessReason, "every config counted is one this caller reaches");
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(0);
        metrics.GetProperty("totalContainerCount").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task WhenTheConfigReadMayBeTruncated_TheDashboardIs503_NeverTheTenantWideFigures()
    {
        // GetReachableConfigIdsAsync's full-page guard. 5000 unit-less configs come FIRST, Unit B's config after: a
        // read capped at 5000 rows would see only configs Unit A's admin reaches and judge them to reach EVERY config —
        // handing a leaf admin the tenant-wide completeness count. A full page must refuse instead.
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA, includeConfigs: false);
        for (var i = 0; i < 5000; i++)
        {
            _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.NewGuid(), null, $"t{i}"));
        }

        _fixture.Dataverse.Add(ConfigSet, ConfigRow(ConfigB, UnitB, TypeT));
        await SeedAggregateAsync();
        using var client = Admin();

        var problem = await Problem(await client.GetAsync("/api/spe/dashboard/metrics"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
    }

    [Theory]
    [InlineData("GET", "/api/spe/dashboard/metrics")]
    [InlineData("POST", "/api/spe/dashboard/refresh")]
    public async Task WhenTheScopeCannotBeRead_BothRoutesAre503_NeverTheUnprojectedAggregate(string method, string url)
    {
        await SeedAggregateAsync();
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // End to end: the scheduled job attributes containers per config by their binding
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RunsTheJob_AndALeafAdminSeesOnlyItsSubtreesContainersAndStorage()
    {
        StubGraph();
        using var client = Admin();

        var metrics = await Json(await client.PostAsync("/api/spe/dashboard/refresh", content: null));

        metrics.GetProperty("containerCountByConfig").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetInt32())
            .Should().BeEquivalentTo(new Dictionary<string, int> { [ConfigA.ToString()] = 2, [ConfigN.ToString()] = 0 },
                "c-own and c-sub belong to Config A; Config B's c-other, the unbound and the foreign container do not");
        metrics.GetProperty("totalStorageUsedInBytes").GetInt64().Should().Be(150);
        metrics.GetProperty("storageUsedInBytesByConfig").GetProperty(ConfigA.ToString()).GetInt64().Should().Be(150);
        metrics.GetRawText().Should().NotContain(ConfigB.ToString());
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Refresh_ForAPlatformOperator_CountsEveryContainerOnce_AndNoUnboundOrForeignContainer()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubGraph();
        using var client = Admin();

        var metrics = await Json(await client.PostAsync("/api/spe/dashboard/refresh", content: null));

        metrics.GetProperty("containerCountByConfig").GetProperty(ConfigA.ToString()).GetInt32().Should().Be(2);
        metrics.GetProperty("containerCountByConfig").GetProperty(ConfigB.ToString()).GetInt32().Should().Be(1,
            "Config A and Config B list the same shared type — each container is counted ONCE, under its owner");
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(1,
            "c-rootless only — owner round 41 item 2: c-unbound is in no view, the platform operator's aggregate included " +
            "(under Model 1 an unbound container of a shared type may be another customer's)");
        metrics.GetProperty("unattributedStorageUsedInBytes").GetInt64().Should().Be(3);
        metrics.GetProperty("totalContainerCount").GetInt32().Should().Be(4,
            "c-own, c-sub, c-other, c-rootless — c-foreign belongs to no unit of this environment, c-unbound to none at all");
        metrics.GetProperty("totalStorageUsedInBytes").GetInt64().Should().Be(100 + 50 + 1000 + 3);
    }

    [Fact]
    public async Task Refresh_WhenAContainersBindingCannotBeRead_CountsItForNobody_AndNamesTheConcern()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubGraph(faultOn: "c-other");
        using var client = Admin();

        var metrics = await Json(await client.PostAsync("/api/spe/dashboard/refresh", content: null));

        metrics.GetProperty("containerCountByConfig").GetProperty(ConfigB.ToString()).GetInt32().Should().Be(0);
        metrics.GetProperty("unattributedContainerCount").GetInt32().Should().Be(1,
            "only c-rootless — a container whose binding could not be read is not counted, and neither is c-unbound");
        metrics.GetProperty("totalContainerCount").GetInt32().Should().Be(3, "c-own, c-sub, c-rootless");
        metrics.GetRawText().Should().Contain(SpeDashboardSyncService.BindingConcernPrefix);
        metrics.GetProperty("syncHealth").GetString().Should().Be("Degraded");
    }

    [Fact]
    public async Task Refresh_AConfigWhoseStoredSecretNameDoesNotConform_IsStillCounted()
    {
        // Round 65 item 1: the job reads no secret by this name (SPE Admin runs as the BFF's own identity, master
        // bb8ba7251), so round 35 item 3's refusal of a non-conforming stored name was removed with the read it guarded.
        // A config such as dev's Model 1 (literal "null") is counted like any other.
        _fixture.Reset();
        SeedTenant(callerUnit: Root, includeConfigB: false);
        var badB = ConfigRow(ConfigB, UnitB, TypeT);
        badB["sprk_keyvaultsecretname"] = "null";
        _fixture.Dataverse.Add(ConfigSet, badB);
        StubGraph();
        using var client = Admin();

        var metrics = await Json(await client.PostAsync("/api/spe/dashboard/refresh", content: null));

        metrics.GetRawText().Should().NotContain(SpeConfigSecretNamePolicy.NotAllowedReasonCode);
        metrics.GetProperty("containerCountByConfig").GetProperty(ConfigA.ToString()).GetInt32().Should().Be(2);
        metrics.GetProperty("containerCountByConfig").TryGetProperty(ConfigB.ToString(), out _).Should().BeTrue(
            "the config with the non-conforming stored name is synced, not refused");
    }

    [Fact]
    public async Task TheFirstViewWithNothingCached_StartsASync_AndALaterViewSeesItsResult()
    {
        StubGraph();
        using var client = Admin();

        (await client.GetAsync("/api/spe/dashboard/metrics")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement? metrics = null;
        for (var attempt = 0; attempt < 60 && metrics is null; attempt++)
        {
            await Task.Delay(250);
            var response = await client.GetAsync("/api/spe/dashboard/metrics");
            if (response.StatusCode == HttpStatusCode.OK) metrics = await Json(response);
        }

        metrics.Should().NotBeNull("the first view started the job, which filled the cache");
        metrics!.Value.GetProperty("containerCountByConfig").GetProperty(ConfigA.ToString()).GetInt32().Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private void SeedTenant(Guid? callerUnit, bool includeConfigB = true, bool includeConfigs = true)
    {
        var dv = _fixture.Dataverse;
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitASub, ["_parentbusinessunitid_value"] = UnitA });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });
        dv.Add("sprk_speenvironments", new() { ["sprk_speenvironmentid"] = Environment, ["sprk_tenantid"] = "11111111-2222-3333-4444-555555555555" });

        if (callerUnit is { } unit)
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
                ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000001"),
                ["_businessunitid_value"] = unit,
            });
        }

        if (includeConfigs)
        {
            dv.Add(ConfigSet, ConfigRow(ConfigA, UnitA, TypeT));
            if (includeConfigB) dv.Add(ConfigSet, ConfigRow(ConfigB, UnitB, TypeT));
            dv.Add(ConfigSet, ConfigRow(ConfigN, null, "99999999-0000-0000-0000-000000000099"));
        }

    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit, string type) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["sprk_name"] = $"Config {id.ToString()[..2]}",
        ["_sprk_businessunit_value"] = unit,
        ["_sprk_environment_value"] = Environment,
        ["sprk_containertypeid"] = type,
        ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
        ["sprk_keyvaultsecretname"] = "spe-owning-app-shared",
        ["statecode"] = 0,
    };

    /// <summary>The shared type's containers, each with storage, and each one's binding on its single GET.</summary>
    private void StubGraph(string? faultOn = null)
    {
        var containers = new (string Id, string? Stamp, long Bytes)[]
        {
            ("c-own", UnitA.ToString(), 100),
            ("c-sub", UnitASub.ToString(), 50),
            ("c-other", UnitB.ToString(), 1000),
            ("c-unbound", null, 5),
            ("c-foreign", ForeignUnit.ToString(), 7),
            ("c-rootless", Root.ToString(), 3),    // bound to this environment's root, where no config of type T sits
        };

        _fixture.Graph.StubGetExact(ContainersPath,
            "{\"value\":[" + string.Join(",", containers.Select(c =>
                "{\"id\":\"" + c.Id + "\",\"displayName\":\"" + c.Id + "\",\"containerTypeId\":\"" + TypeT +
                "\",\"storageUsedInBytes\":" + c.Bytes + "}")) + "]}");

        foreach (var (id, stamp, _) in containers)
        {
            if (id == faultOn)
            {
                _fixture.Graph.StubGet($"{ContainersPath}/{id}", """{"error":{"code":"serviceNotAvailable","message":"x"}}""", 500);
                continue;
            }

            var properties = stamp is null
                ? "{}"
                : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + stamp + "\",\"isSearchable\":false}}";
            _fixture.Graph.StubGet($"{ContainersPath}/{id}",
                "{\"id\":\"" + id + "\",\"containerTypeId\":\"" + TypeT + "\",\"customProperties\":" + properties + "}");
        }
    }

    /// <summary>A cached aggregate as the job writes it: per-config counts and storage plus the unattributed figures.</summary>
    private async Task SeedAggregateAsync()
    {
        var metrics = new
        {
            totalContainerCount = 14,
            totalStorageUsedInBytes = 4096L,
            storageReportingContainerCount = 9,
            containerCountByConfig = new Dictionary<string, int>
            {
                [ConfigA.ToString()] = 3,
                [ConfigB.ToString()] = 7,
                [ConfigN.ToString()] = 2,
            },
            storageUsedInBytesByConfig = new Dictionary<string, long>
            {
                [ConfigA.ToString()] = 100,
                [ConfigB.ToString()] = 3000,
                [ConfigN.ToString()] = 200,
            },
            storageReportingContainerCountByConfig = new Dictionary<string, int>
            {
                [ConfigA.ToString()] = 2,
                [ConfigB.ToString()] = 5,
                [ConfigN.ToString()] = 1,
            },
            unattributedContainerCount = 2,
            unattributedStorageUsedInBytes = 796L,
            unattributedStorageReportingContainerCount = 1,
            lastSyncedAt = DateTimeOffset.UtcNow,
            syncSucceeded = false,
            syncStatus = "2 of 5 concern(s) failed",
            syncHealth = "Degraded",
            concerns = new object[]
            {
                new { concern = "Dataverse container-type configs", succeeded = true },
                new { concern = "Dataverse config completeness", succeeded = false, reason = CompletenessReason },
                new { concern = $"Graph containers (config {ConfigA})", succeeded = true },
                new { concern = $"Graph containers (config {ConfigB})", succeeded = false, reason = $"Container list failed for {ConfigB}" },
                new { concern = $"Graph containers (config {ConfigN})", succeeded = true },
            },
        };

        await Cache.SetStringAsync(SpeDashboardSyncService.CacheKey, JsonSerializer.Serialize(metrics));
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
