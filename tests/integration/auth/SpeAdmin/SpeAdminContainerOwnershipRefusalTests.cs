using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// F-165-RB / #1652 — the SPE admin plane's per-container rule with the REAL ownership guard (task 227d) for an ACTIVE
/// container and the BFF's deleted-container ownership RECORDS for one in the recycle bin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why records.</b> Graph discards a container's <c>customProperties</c> on soft delete (measured on dev 2026-10-11/12,
/// #1652 comment 6117086899): a deleted container carries neither the ownership marker nor the business-unit stamp. So
/// the fake Graph here answers <c>deletedContainers/{id}</c> WITHOUT customProperties, as live does, and a container in
/// the bin is acted on only when (a) a valid ownership record exists (written by the BFF before it soft-deleted the
/// container), (b) Graph lists the id under <c>deletedContainers</c> for the config's container type, (c) the record
/// names this stamp's customer and (d) the caller's business-unit scope admits the record's unit. Anything else is the ONE
/// uniform 404, exactly like an id that names nothing; a store or Graph read with no answer is the 503. Nothing is acted
/// on either way.
/// </para>
/// <para>
/// Containers in the bin (Type T, all listed by Graph unless stated): <c>rb-own</c> (record: this customer, root unit),
/// <c>rb-leafown</c> (this customer, Unit A), <c>rb-unitb</c> (this customer, Unit B), <c>rb-foreign</c> (record naming
/// another customer), <c>rb-norecord</c> (no record — deleted outside the BFF or before records existed),
/// <c>rb-tampered</c> (the document is another container's record copied to this id), <c>rb-forged</c> (a document of
/// another document type), <c>rb-othertype</c> (record of another container type), <c>rb-unlisted</c> (a valid record, but
/// Graph does NOT list it as deleted), <c>rb-nowhere</c> (nothing anywhere). ADR-038 §2 path #1 (security-auth KEEP).
/// </para>
/// </remarks>
public sealed class SpeAdminContainerOwnershipRefusalTests : IClassFixture<AdminSurfaceRealOwnershipHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string DeletedPath = "/storage/fileStorage/deletedContainers";
    private const string ContainerCode = "spe.admin.deny.container_out_of_scope";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";
    private const string OwnCustomer = AdminSurfaceRealOwnershipHostFixture.Customer;
    private const string OtherCustomer = "cust2";
    private const string TypeT = "77777777-0000-0000-0000-000000000077";
    private const string TypeX = "88888888-0000-0000-0000-000000000088";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");
    private static readonly Guid Config = Guid.Parse("ca000000-0000-0000-0000-00000000000a");

    private static readonly string[] Listed =
    {
        "rb-own", "rb-leafown", "rb-unitb", "rb-foreign", "rb-norecord", "rb-tampered", "rb-forged", "rb-othertype",
    };

    private readonly AdminSurfaceRealOwnershipHostFixture _fixture;

    public SpeAdminContainerOwnershipRefusalTests(AdminSurfaceRealOwnershipHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>Every deleted container no valid record of this stamp's admits, by restore (POST) and purge (DELETE).</summary>
    public static TheoryData<string, string> RecycleBinRoutesOnContainersWithoutAValidRecord()
    {
        var data = new TheoryData<string, string>();
        foreach (var id in new[] { "rb-foreign", "rb-norecord", "rb-tampered", "rb-forged", "rb-othertype", "rb-unlisted" })
        {
            data.Add("POST", id);
            data.Add("DELETE", id);
        }

        return data;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // No valid record of this stamp's → the uniform 404, the same as an id that names nothing
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RecycleBinRoutesOnContainersWithoutAValidRecord))]
    public async Task RestoreOrPurge_OfADeletedContainerWithoutAValidRecordOfThisStamp_IsTheUniform404_AndNothingIsActedOn(
        string method, string containerId)
    {
        Seed(callerUnit: Root);
        var before = _fixture.DeletedRecords.DocumentOf(containerId);
        using var client = Admin();

        var refused = await Problem(await client.SendAsync(RecycleBinRequest(method, containerId)));
        var nowhere = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-nowhere")));

        AssertUniformNotFound(refused, containerId, recycleBin: true);
        AssertUniformNotFound(nowhere, "rb-nowhere", recycleBin: true);
        refused.Keys.Should().BeEquivalentTo(nowhere.Keys, "the caller cannot tell a refused container from a missing one");
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET" && r.Path == DeletedPath,
            "only the recycle bin's list was read; nothing was restored or purged");
        _fixture.DeletedRecords.CallsOf("Delete").Should().BeEmpty("a refused container's record is left alone");
        if (before is null)
        {
            _fixture.DeletedRecords.DocumentOf(containerId).Should().BeNull();
        }
        else
        {
            _fixture.DeletedRecords.DocumentOf(containerId).Should().BeSameAs(before);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RestoreOrPurge_OfThisStampsDeletedContainerInAnotherUnit_IsTheSame404ForALeafAdmin(string method)
    {
        Seed(callerUnit: UnitA);
        using var client = Admin();

        var otherUnit = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-unitb")));
        var nowhere = await Problem(await client.SendAsync(RecycleBinRequest(method, "rb-nowhere")));

        AssertUniformNotFound(otherUnit, "rb-unitb", recycleBin: true);
        otherUnit.Keys.Should().BeEquivalentTo(nowhere.Keys);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET", "nothing was restored or purged");
        _fixture.DeletedRecords.RecordOf("rb-unitb").Should().NotBeNull("the refused container's record is untouched");
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

    // ─────────────────────────────────────────────────────────────────────────
    // Fail closed: a read with no answer is the 503, never an act and never "not there"
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RestoreOrPurge_WhenTheRecordStoreGetsNoAnswer_Is503_AndNothingIsActedOn(string method)
    {
        Seed(callerUnit: Root);
        _fixture.DeletedRecords.FaultReads = true;
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(RecycleBinRequest(method, "rb-own")), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET", "nothing was restored or purged");
        _fixture.DeletedRecords.CallsOf("Delete").Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RestoreOrPurge_WhenGraphsDeletedListGetsNoAnswer_Is503_AndNothingIsActedOn(string method)
    {
        Seed(callerUnit: Root);
        _fixture.Graph.ReplaceGetExact(DeletedPath, """{"error":{"code":"serviceNotAvailable","message":"x"}}""", 503);
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(RecycleBinRequest(method, "rb-own")), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET", "nothing was restored or purged");
    }

    [Fact]
    public async Task RecycleBinList_WhenTheRecordStoreGetsNoAnswer_Is503_NeverAnEmptyOrUntrimmedList()
    {
        Seed(callerUnit: UnitA);
        _fixture.DeletedRecords.FaultReads = true;
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/recyclebin?configId={Config}"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Positive controls: this stamp's container, in the caller's reach, is acted on — and its record goes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_OfThisStampsDeletedContainer_ForARootAdmin_IsRestored_ReMarkedFromItsRecord_AndTheRecordRemoved()
    {
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", "{\"id\":\"rb-own\"}");
        _fixture.Graph.StubPatch($"{ContainersPath}/rb-own/customProperties", "{}");
        StubActive("rb-own", OwnCustomer, Root);   // the read-back after the re-mark
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "POST" && r.Path == $"{DeletedPath}/rb-own/restore");
        // Graph discards customProperties on soft delete: the live container gets its marker and stamp back from the record.
        var patch = _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "PATCH").Subject;
        patch.Path.Should().Be($"{ContainersPath}/rb-own/customProperties");
        var body = patch.BodyAsJson();
        body.GetProperty(SpeContainerOwnershipGuard.MarkerPropertyName).GetProperty("value").GetString().Should().Be(OwnCustomer);
        body.GetProperty(SpeContainerBusinessUnitStamp.PropertyName).GetProperty("value").GetString()
            .Should().BeEquivalentTo(Root.ToString());
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "DELETE");
        _fixture.DeletedRecords.DocumentOf("rb-own").Should().BeNull("a restored, re-marked container is live again; its record goes");
        _fixture.DeletedRecords.RecordOf("rb-leafown").Should().NotBeNull("no other record is touched");
    }

    [Fact]
    public async Task Restore_WhenTheRestoredContainerCannotBeReMarked_MovesItBackToTheBin_AndKeepsItsRecord()
    {
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", "{\"id\":\"rb-own\"}");
        _fixture.Graph.StubPatch($"{ContainersPath}/rb-own/customProperties",
            """{"error":{"code":"accessDenied","message":"x"}}""", 403);
        _fixture.Graph.StubDelete($"{ContainersPath}/rb-own");
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.IsSuccessStatusCode.Should().BeFalse("the restore did not leave a reachable container");
        _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "DELETE" && r.Path == $"{ContainersPath}/rb-own",
            "a live container with no marker or stamp is unreachable by every admin — it goes back to the recycle bin");
        var record = _fixture.DeletedRecords.RecordOf("rb-own");
        record.Should().NotBeNull("the record still admits the container in the bin, so the restore can be retried");
        record!.DeletedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5),
            "#1652 round 1: the record is re-written before the re-delete, so its ttl restarts with SPE's new retention");
    }

    // ── #1652 round 1 ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "10000000-0000-0000-0000-000000000000")]          // the marker did not read back
    [InlineData(OwnCustomer, "1b000000-0000-0000-0000-000000000000")]   // the stamp read back as another unit
    [InlineData(OtherCustomer, "10000000-0000-0000-0000-000000000000")] // another customer's marker read back
    public async Task Restore_WhenTheReadBackIsNotTheRecordsMarkerAndStamp_ReturnsTheContainerToTheBin(
        string? readBackMarker, string readBackUnit)
    {
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", "{\"id\":\"rb-own\"}");
        _fixture.Graph.StubPatch($"{ContainersPath}/rb-own/customProperties", "{}");
        StubActive("rb-own", readBackMarker, Guid.Parse(readBackUnit));
        _fixture.Graph.StubDelete($"{ContainersPath}/rb-own");
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.IsSuccessStatusCode.Should().BeFalse();
        _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "DELETE" && r.Path == $"{ContainersPath}/rb-own",
            "a container whose marker AND stamp do not both read back is not reachable as ours — back to the bin");
        _fixture.DeletedRecords.RecordOf("rb-own").Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_ByARootAdmin_OfAUnitBContainer_WritesTheRecordsUnit_NotTheCallers()
    {
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-unitb/restore", "{\"id\":\"rb-unitb\"}");
        _fixture.Graph.StubPatch($"{ContainersPath}/rb-unitb/customProperties", "{}");
        StubActive("rb-unitb", OwnCustomer, UnitB);
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-unitb"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "PATCH").Subject.BodyAsJson();
        body.GetProperty(SpeContainerBusinessUnitStamp.PropertyName).GetProperty("value").GetString()
            .Should().BeEquivalentTo(UnitB.ToString(), "the container goes back to the unit that owned it, not the restorer's");
        _fixture.DeletedRecords.DocumentOf("rb-unitb").Should().BeNull();
    }

    [Fact]
    public async Task Restore_WhenGraphSaysNotFound_ButTheContainerIsLiveAndBare_IsReMarkedFromTheRecord()
    {
        // Graph restored it, the 2xx was lost, and the SDK's retry read 404 (#1652 round 1, scenario 2).
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", """{"error":{"code":"itemNotFound","message":"x"}}""", 404);
        _fixture.Graph.StubGetSequence($"{ContainersPath}/rb-own",
            (LiveJson("rb-own", null, null), 200),
            (LiveJson("rb-own", OwnCustomer, Root), 200));
        _fixture.Graph.StubPatch($"{ContainersPath}/rb-own/customProperties", "{}");
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "PATCH").Subject.BodyAsJson();
        body.GetProperty(SpeContainerOwnershipGuard.MarkerPropertyName).GetProperty("value").GetString().Should().Be(OwnCustomer);
        body.GetProperty(SpeContainerBusinessUnitStamp.PropertyName).GetProperty("value").GetString()
            .Should().BeEquivalentTo(Root.ToString());
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "DELETE");
        _fixture.DeletedRecords.DocumentOf("rb-own").Should().BeNull();
    }

    [Theory]
    [InlineData(OtherCustomer, null)]                                     // another customer's marker
    [InlineData(null, "1b000000-0000-0000-0000-000000000000")]            // a stamp, no marker
    public async Task Restore_WhenGraphSaysNotFound_AndTheLiveContainerCarriesAnythingElse_IsUntouched_AndTheUniform404(
        string? liveMarker, string? liveUnit)
    {
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", """{"error":{"code":"itemNotFound","message":"x"}}""", 404);
        _fixture.Graph.StubGet($"{ContainersPath}/rb-own", LiveJson("rb-own", liveMarker, liveUnit is null ? null : Guid.Parse(liveUnit)));
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(RecycleBinRequest("POST", "rb-own")));

        AssertUniformNotFound(problem, "rb-own", recycleBin: true);
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "PATCH" || r.Method == "DELETE",
            "a live container carrying another marker or stamp is never written");
        _fixture.DeletedRecords.RecordOf("rb-own").Should().NotBeNull("the record is left as it was");
    }

    [Fact]
    public async Task Restore_WhenGraphGivesNoAnswer_AndTheLiveStateIsUnreadable_FailsLoud_WritesNothing_AndKeepsTheRecord()
    {
        // Graph may have restored it; nothing can tell. Nothing is written, the record stays, and an operator is told.
        Seed(callerUnit: Root);
        _fixture.Graph.StubPostExact($"{DeletedPath}/rb-own/restore", """{"error":{"code":"generalException","message":"x"}}""", 500);
        _fixture.Graph.StubGet($"{ContainersPath}/rb-own", """{"error":{"code":"generalException","message":"x"}}""", 500);
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("POST", "rb-own"));

        response.IsSuccessStatusCode.Should().BeFalse();
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "PATCH" || r.Method == "DELETE");
        _fixture.DeletedRecords.RecordOf("rb-own").Should().NotBeNull();
        _fixture.Logs.Lines.Should().Contain(l => l.Level == LogLevel.Critical && l.Message.Contains("rb-own"),
            "a container that may be live and unmarked is named where an operator sees it");
    }

    [Fact]
    public async Task Purge_OfThisStampsDeletedContainer_ForALeafAdminOfItsUnit_IsPurged_AndItsRecordRemoved()
    {
        Seed(callerUnit: UnitA);
        _fixture.Graph.StubDelete($"{DeletedPath}/rb-leafown");
        using var client = Admin();

        var response = await client.SendAsync(RecycleBinRequest("DELETE", "rb-leafown"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        _fixture.Graph.AllRequests.Should().ContainSingle(r => r.Method == "DELETE" && r.Path == $"{DeletedPath}/rb-leafown");
        _fixture.DeletedRecords.DocumentOf("rb-leafown").Should().BeNull("a purged container is gone; its record goes");
        _fixture.DeletedRecords.RecordOf("rb-own").Should().NotBeNull("no other record is touched");
    }

    [Fact]
    public async Task RecycleBinList_ShowsOnlyThisStampsRecordedDeletedContainersInTheCallersReach()
    {
        Seed(callerUnit: UnitA);
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/recyclebin?configId={Config}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).Should().Equal(new[] { "rb-leafown" },
                "rb-own is the root unit's, rb-unitb Unit B's; every other one has no valid record of this stamp");
    }

    [Fact]
    public async Task RecycleBinList_ForARootAdmin_ShowsEveryRecordedContainerOfThisStamp_AndNoUnrecordedOne()
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/recyclebin?configId={Config}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).Should().BeEquivalentTo(new[] { "rb-own", "rb-leafown", "rb-unitb" },
                "containers deleted outside the BFF (no record), another customer's, forged or tampered ones stay invisible");
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

        // Active containers (marker on the container — task 227d).
        StubActive("rb-foreign", OtherCustomer, Root);
        StubActive("rb-unmarked", null, Root);

        // The recycle bin as live Graph answers it: listed for Type T, and NO customProperties on deletedContainers/{id}.
        _fixture.Graph.ReplaceGetExact(DeletedPath,
            "{\"value\":[" + string.Join(",", Listed.Select(id =>
                "{\"id\":\"" + id + "\",\"displayName\":\"Deleted " + id + "\",\"containerTypeId\":\"" + TypeT + "\"}")) + "]}");

        var records = _fixture.DeletedRecords;
        records.Seed("rb-own", OwnCustomer, Root, TypeT, Config);
        records.Seed("rb-leafown", OwnCustomer, UnitA, TypeT, Config);
        records.Seed("rb-unitb", OwnCustomer, UnitB, TypeT, Config);
        records.Seed("rb-foreign", OtherCustomer, Root, TypeT, Config);
        records.Seed("rb-othertype", OwnCustomer, Root, TypeX, Config);
        records.Seed("rb-unlisted", OwnCustomer, Root, TypeT, Config);

        // Tampered: rb-own's genuine record copied to rb-tampered's id (its id and containerId name rb-own).
        records.Put("rb-tampered", records.DocumentOf("rb-own")!);

        // Forged: a well-formed record of rb-forged in every field but its document type (e.g. written as another kind).
        records.Seed("rb-forged", OwnCustomer, Root, TypeT, Config);
        records.DocumentOf("rb-forged")!.DocumentType = "workspace-tab";
    }

    /// <summary>A live container's single read, with optional marker and stamp (none of either = what Graph restores).</summary>
    private static string LiveJson(string id, string? marker, Guid? unit)
    {
        var properties = new List<string>();
        if (unit is { } u)
        {
            properties.Add("\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + u + "\",\"isSearchable\":false}");
        }

        if (marker is not null)
        {
            properties.Add("\"" + SpeContainerOwnershipGuard.MarkerPropertyName + "\":{\"value\":\"" + marker + "\",\"isSearchable\":false}");
        }

        return "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"containerTypeId\":\"" + TypeT +
               "\",\"status\":\"active\",\"customProperties\":{" + string.Join(",", properties) + "}}";
    }

    /// <summary>The ACTIVE container, carrying <paramref name="marker"/> (null = none) and the unit stamp.</summary>
    private void StubActive(string id, string? marker, Guid unit)
    {
        var properties = new List<string>
        {
            "\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + unit + "\",\"isSearchable\":false}",
        };
        if (marker is not null)
        {
            properties.Add("\"" + SpeContainerOwnershipGuard.MarkerPropertyName + "\":{\"value\":\"" + marker + "\",\"isSearchable\":false}");
        }

        _fixture.Graph.StubGet($"{ContainersPath}/{id}",
            "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"containerTypeId\":\"" + TypeT +
            "\",\"status\":\"active\",\"customProperties\":{" + string.Join(",", properties) + "}}");
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
