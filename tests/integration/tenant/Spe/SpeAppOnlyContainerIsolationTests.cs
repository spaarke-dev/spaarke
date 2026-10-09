using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Tenant.Spe;

/// <summary>
/// App-only SharePoint Embedded calls reach only this stamp's own containers (task 227d, owner D28/D29).
/// </summary>
/// <remarks>
/// <para><b>The boundary.</b> Every Model 1 stamp shares one container type and its managed identity holds
/// application <c>full</c> on it, so an app-only token reaches every customer's container. The only thing
/// between customer A's BFF and customer B's documents is <see cref="SpeContainerOwnershipGuard"/>: a
/// container is A's when A configured it (H4b app settings) or when it carries A's
/// <c>spaarkeCustomerId</c> marker. Container ids reach the BFF through Dataverse fields the customer can
/// write, so these tests hand the production paths a FOREIGN container id and assert the call is refused
/// before any Graph request touches that container's content.</para>
/// <para><b>Real SDK, routed transport.</b> A real <see cref="GraphServiceClient"/> runs over
/// <see cref="RoutedGraph"/>, a hand-written handler that answers exact paths and records every request —
/// so "refused before Graph" is an observable fact (only the ownership read was sent), not an assumption
/// (ADR-038: no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
/// <para><b>No existence oracle.</b> The README for this category requires a cross-tenant miss to be
/// indistinguishable from "does not exist": a foreign container gets 404, exactly like a nonexistent one.</para>
/// </remarks>
public class SpeAppOnlyContainerIsolationTests
{
    private const string Customer = "cust1";
    private const string OtherCustomer = "cust2";
    private const string Base = "https://graph.test/v1.0";
    private const string BffTenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

    private const string OwnContainer = "own-container";
    private const string ForeignContainer = "foreign-container";
    private const string ConfiguredContainer = "configured-container";

    // ─────────────────────────────────────────────────────────────────────────
    // The ownership decision
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsOwned_ConfiguredStampContainer_IsOwnedWithoutAGraphCall()
    {
        var graph = new RoutedGraph();
        var guard = Guard(graph, settings: new() { ["Communication:ArchiveContainerId"] = ConfiguredContainer });

        (await guard.IsOwnedAsync(ConfiguredContainer)).Should().BeTrue();
        graph.Requests.Should().BeEmpty("a configured container is the stamp's by definition (written by H4b, not the customer)");
    }

    [Fact]
    public async Task IsOwned_MarkerNamesThisCustomer_IsOwned_AndReadsOnlyTheMarker()
    {
        var graph = new RoutedGraph().Container(OwnContainer, marker: Customer);
        var guard = Guard(graph);

        (await guard.IsOwnedAsync(OwnContainer)).Should().BeTrue();

        var read = graph.Requests.Should().ContainSingle().Subject;
        read.Method.Should().Be("GET");
        read.Path.Should().Be($"/v1.0/storage/fileStorage/containers/{OwnContainer}");
        Uri.UnescapeDataString(read.Query).Should().Contain("$select=id,customProperties");
    }

    [Theory]
    [InlineData(OtherCustomer)]   // another customer's container
    [InlineData(null)]            // no marker at all (created outside this stamp)
    [InlineData("CUST1")]         // ordinal — a case variant is not this customer
    public async Task IsOwned_MarkerMissingOrNamingSomeoneElse_IsNotOwned(string? marker)
    {
        var graph = new RoutedGraph().Container(ForeignContainer, marker);

        (await Guard(graph).IsOwnedAsync(ForeignContainer)).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task IsOwned_GraphCannotShowTheContainer_IsNotOwned(HttpStatusCode status)
    {
        var graph = new RoutedGraph().Error("GET", $"/v1.0/storage/fileStorage/containers/{ForeignContainer}", status);

        (await Guard(graph).IsOwnedAsync(ForeignContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task IsOwned_GraphFailsWithoutAnAnswer_Throws()
    {
        var graph = new RoutedGraph().Error("GET", $"/v1.0/storage/fileStorage/containers/{ForeignContainer}", HttpStatusCode.InternalServerError);

        var act = () => Guard(graph).IsOwnedAsync(ForeignContainer);

        await act.Should().ThrowAsync<SpaarkeStorageException>("no ownership decision is made without an answer from Graph, and no Graph type leaves the facade");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsOwned_BlankId_IsNotOwned_WithoutAGraphCall(string id)
    {
        var graph = new RoutedGraph();

        (await Guard(graph).IsOwnedAsync(id)).Should().BeFalse();
        graph.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task IsOwned_CustomerIdentityUnresolved_Throws()
    {
        var graph = new RoutedGraph().Container(OwnContainer, marker: Customer);
        var guard = TestSpeOwnership.Real(new StubFactory(graph.Client), customerId: "");

        var act = () => guard.IsOwnedAsync(OwnContainer);

        await act.Should().ThrowAsync<InvalidOperationException>("an empty customer id would match an empty marker");
    }

    [Fact]
    public async Task EnsureOwned_ForeignAndNonexistentContainers_GetTheSame404()
    {
        var graph = new RoutedGraph()
            .Container(ForeignContainer, marker: OtherCustomer)
            .Error("GET", "/v1.0/storage/fileStorage/containers/no-such-container", HttpStatusCode.NotFound);
        var guard = Guard(graph);

        var foreign = await Refusal(() => guard.EnsureOwnedAsync(ForeignContainer));
        var missing = await Refusal(() => guard.EnsureOwnedAsync("no-such-container"));

        foreign.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        foreign.Code.Should().Be(SpeContainerOwnershipGuard.NotOwnedErrorCode);
        (missing.StatusCode, missing.Code, missing.Detail).Should().Be((foreign.StatusCode, foreign.Code, foreign.Detail),
            "a foreign container must be indistinguishable from one that does not exist");
    }

    [Fact]
    public async Task MarkOwned_PatchesTheCustomPropertiesSubResource_WithThisCustomer()
    {
        var graph = new RoutedGraph().Respond("PATCH", "/v1.0/storage/fileStorage/containers/new-container/customProperties", "{}");

        await Guard(graph).MarkOwnedAsync("new-container");

        var patch = graph.Requests.Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(patch.Body!);
        var marker = body.RootElement.GetProperty(SpeContainerOwnershipGuard.MarkerPropertyName);
        marker.GetProperty("value").GetString().Should().Be(Customer);
        marker.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task MarkOwned_GraphRefuses_ThrowsNamingTheUnreachableContainer()
    {
        var graph = new RoutedGraph().Error("PATCH", "/v1.0/storage/fileStorage/containers/new-container/customProperties", HttpStatusCode.BadRequest);

        var act = () => Guard(graph).MarkOwnedAsync("new-container");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*new-container*spaarkeCustomerId*unreachable*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Every app-only path refuses a foreign container before touching it
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> FacadePaths => new()
    {
        "ListChildren", "Download", "Delete", "Metadata", "Versions", "Preview", "QuickXorHash",
        "UploadSmall", "ContainerDrive", "DriveSubscription", "DriveDelta",
        "MembershipGrant",
        // The app-only twins added by unified-access-control-r2 task 171 (broker-only SPE bytes).
        "ItemCreator", "MetadataUncached", "DownloadVersion", "CurrentVersionId", "EmbedPreview", "SharingLink", "DriveItem",
        "ReplaceContent", "UploadSession",
        "MembershipReadAccess", "MembershipReadMarkers", "MembershipGrantMarked", "MembershipRemoveMarked",
    };

    [Theory]
    [MemberData(nameof(FacadePaths))]
    public async Task AppOnlyPath_ForeignContainer_IsRefusedBeforeAnyContentCall(string path)
    {
        var graph = new RoutedGraph().Container(ForeignContainer, marker: OtherCustomer);
        var factory = new StubFactory(graph.Client);
        var guard = Guard(graph);

        var drive = new DriveItemOperations(factory, guard, NullLogger<DriveItemOperations>.Instance);
        var upload = new UploadSessionManager(factory, guard, new NoHttpClientFactory(), NullLogger<UploadSessionManager>.Instance);
        var containers = new ContainerOperations(factory, guard, NullLogger<ContainerOperations>.Instance);
        var store = new SpeFileStore(containers, drive, upload, new UserOperations(factory, NullLogger<UserOperations>.Instance), guard);
        var membership = new SpeContainerMembershipService(guard, NullLogger<SpeContainerMembershipService>.Instance);

        Func<Task> call = path switch
        {
            "ListChildren" => () => drive.ListChildrenAsync(ForeignContainer),
            "Download" => () => drive.DownloadFileAsync(ForeignContainer, "item"),
            "Delete" => () => drive.DeleteFileAsync(ForeignContainer, "item"),
            "Metadata" => () => drive.GetFileMetadataAsync(ForeignContainer, "item"),
            "Versions" => () => drive.ListFileVersionsAsync(ForeignContainer, "item"),
            "Preview" => () => drive.GetPreviewUrlAsync(ForeignContainer, "item"),
            "QuickXorHash" => () => drive.GetQuickXorHashAsync(ForeignContainer, "item"),
            "UploadSmall" => () => upload.UploadSmallAsync(ForeignContainer, "a.txt", new MemoryStream(new byte[] { 1 })),
            "ContainerDrive" => () => store.GetContainerDriveAsync(ForeignContainer),
            "DriveSubscription" => () => store.CreateDriveRootSubscriptionAsync(ForeignContainer, "https://hook.test", "state", DateTimeOffset.UtcNow.AddDays(1)),
            "DriveDelta" => () => store.EnumerateDriveDeltaAsync(ForeignContainer, null),
            "MembershipGrant" => () => membership.GrantMembershipAsync(ForeignContainer, "x@contoso.test", ExternalAccessLevel.ViewOnly),
            "ItemCreator" => () => drive.GetItemCreatorAsync(ForeignContainer, "item"),
            "MetadataUncached" => () => drive.GetFileMetadataUncachedAsync(ForeignContainer, "item"),
            "DownloadVersion" => () => drive.DownloadFileVersionAsync(ForeignContainer, "item", "1.0"),
            "CurrentVersionId" => () => drive.GetCurrentVersionIdAsync(ForeignContainer, "item"),
            "EmbedPreview" => () => drive.GetEmbedPreviewUrlAsync(ForeignContainer, "item"),
            "SharingLink" => () => drive.CreateSharingLinkAsync(ForeignContainer, "item", "view", "organization"),
            "DriveItem" => () => drive.GetDriveItemAsync(ForeignContainer, "item"),
            "ReplaceContent" => () => upload.ReplaceFileContentAsync(ForeignContainer, "item", new MemoryStream(new byte[] { 1 }), ifMatch: null),
            "UploadSession" => () => upload.CreateUploadSessionAsync(ForeignContainer, "a.txt", Sprk.Bff.Api.Models.ConflictBehavior.Fail),
            "MembershipReadAccess" => () => membership.ReadAccessAsync(ForeignContainer),
            "MembershipReadMarkers" => () => membership.ReadMarkersAsync(ForeignContainer),
            "MembershipGrantMarked" => () => membership.GrantMarkedWriterAsync(
                ForeignContainer, SpeContainerMembershipService.JitWriterMarkerPrefix, Guid.NewGuid(), "x@contoso.test"),
            "MembershipRemoveMarked" => () => membership.RemoveMarkedGrantAsync(
                ForeignContainer, "marker", "perm",
                new SpeContainerMembershipService.ContainerAccess(
                    Array.Empty<SpeContainerMembershipService.ContainerUserRole>(), true, new Dictionary<string, string>())),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };

        var refusal = await Refusal(call);

        refusal.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        graph.Requests.Should().OnlyContain(r => r.Method == "GET" && r.Path == $"/v1.0/storage/fileStorage/containers/{ForeignContainer}",
            "only the ownership read may be sent for a container this stamp does not own");
    }

    [Fact]
    public async Task MembershipRevokeAndRemove_ForeignContainer_FailEveryContact_WithoutThrowing_NoContentCall()
    {
        // Revoke and closure report per contact and must keep going (their grant-cache invalidation still runs), so a
        // refusal is a failed result here, not an exception — and still nothing but the ownership read reaches Graph.
        var graph = new RoutedGraph().Container(ForeignContainer, marker: OtherCustomer);
        var membership = new SpeContainerMembershipService(Guard(graph), NullLogger<SpeContainerMembershipService>.Instance);

        var revoked = await membership.RevokeMembershipAsync(ForeignContainer, "x@contoso.test");
        var removed = await membership.RemoveMembershipsAsync(ForeignContainer, new[] { "x@contoso.test", "y@contoso.test" });

        revoked.Success.Should().BeFalse();
        removed.Should().HaveCount(2).And.OnlyContain(r => !r.Value.Success);
        graph.Requests.Should().OnlyContain(r => r.Method == "GET" && r.Path == $"/v1.0/storage/fileStorage/containers/{ForeignContainer}");
    }

    [Fact]
    public async Task CreateContainer_BindsThenMarksTheNewContainerAsThisCustomers()
    {
        var businessUnit = Guid.NewGuid();
        var graph = NewContainerGraph("Matter 1", businessUnit);
        var containers = new ContainerOperations(new StubFactory(graph.Client), Guard(graph), NullLogger<ContainerOperations>.Instance);

        var created = await containers.CreateContainerAsync(Guid.NewGuid(), "Matter 1", businessUnit);

        created!.Id.Should().Be("new-container");
        graph.Requests.Select(r => $"{r.Method} {r.Path}").Should().Equal(
            "POST /v1.0/storage/fileStorage/containers",
            "PATCH /v1.0/storage/fileStorage/containers/new-container/customProperties",   // business-unit stamp (uac-r2 165)
            "GET /v1.0/storage/fileStorage/containers/new-container",                      // stamp read back
            "PATCH /v1.0/storage/fileStorage/containers/new-container/customProperties");  // ownership marker (T227d)
        graph.Requests[^1].Body.Should().Contain(SpeContainerOwnershipGuard.MarkerPropertyName).And.Contain(Customer);
    }

    /// <summary>Graph for a create: POST answers <c>new-container</c>; its business-unit stamp reads back.</summary>
    private static RoutedGraph NewContainerGraph(string displayName, Guid businessUnit)
        => new RoutedGraph()
            .Respond("POST", "/v1.0/storage/fileStorage/containers", $$$"""{"id":"new-container","displayName":"{{{displayName}}}"}""")
            .Respond("PATCH", "/v1.0/storage/fileStorage/containers/new-container/customProperties", "{}")
            .Respond("GET", "/v1.0/storage/fileStorage/containers/new-container",
                System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["id"] = "new-container",
                    ["customProperties"] = new Dictionary<string, object>
                    {
                        [Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName] =
                            new { value = businessUnit.ToString("D"), isSearchable = false },
                    },
                }));

    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/drives/other-drive/items/root/delta?token=abc")]
    [InlineData("https://evil.test/v1.0/drives/own-container/items/root/delta?token=abc")]
    public void DeltaLink_ForAnotherDriveOrHost_IsRefused(string deltaLink)
    {
        var act = () => SpeFileStore.EnsureDeltaLinkTargetsDrive(deltaLink, OwnContainer);

        act.Should().Throw<InvalidOperationException>("the ownership check vouched for one drive; the replayed link must target it");
    }

    [Fact]
    public void DeltaLink_ForTheCheckedDrive_IsAccepted()
    {
        var act = () => SpeFileStore.EnsureDeltaLinkTargetsDrive(
            $"https://graph.microsoft.com/v1.0/drives/{OwnContainer}/items/root/delta?token=abc", OwnContainer);

        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPE Admin on a stamp: own containers only (owner D29)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpeAdmin_ContainerScopedCall_OnAForeignContainer_IsRefused()
    {
        var graph = new RoutedGraph().Container(ForeignContainer, marker: OtherCustomer);

        var refusal = await Refusal(() => SpeAdmin(graph).ListContainerPermissionsForConfigAsync(Config(), ForeignContainer));

        refusal.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        graph.Requests.Should().OnlyContain(r => r.Path == $"/v1.0/storage/fileStorage/containers/{ForeignContainer}");
    }

    [Fact]
    public async Task SpeAdmin_ContainerList_ShowsOnlyThisStampsContainers()
    {
        var graph = new RoutedGraph()
            .Respond("GET", "/v1.0/storage/fileStorage/containers",
                $$"""{"value":[{"id":"{{OwnContainer}}","displayName":"Ours"},{"id":"{{ForeignContainer}}","displayName":"Theirs"}]}""")
            .Container(OwnContainer, marker: Customer)
            .Container(ForeignContainer, marker: OtherCustomer);

        var page = await SpeAdmin(graph).ListContainersPageForConfigAsync(Config(), Config().ContainerTypeId, top: null, skipToken: null);

        page.Items.Select(c => c.Id).Should().Equal(OwnContainer);
    }

    [Fact]
    public async Task SpeAdmin_UpdateCustomProperties_CannotChangeTheOwnershipMarker()
    {
        var graph = new RoutedGraph().Container(OwnContainer, marker: Customer);

        var act = () => SpeAdmin(graph).UpdateCustomPropertiesForConfigAsync(
            Config(), OwnContainer, [new CustomPropertyDto(SpeContainerOwnershipGuard.MarkerPropertyName, OtherCustomer, false)]);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        graph.Requests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task SpeAdmin_UpdateCustomProperties_UnchangedMarkerIsDroppedFromTheWrite()
    {
        // The editor saves every property it read — the marker included, unchanged.
        var graph = new RoutedGraph()
            .Container(OwnContainer, marker: Customer)
            .Respond("PATCH", $"/v1.0/storage/fileStorage/containers/{OwnContainer}/customProperties", "{}");

        await SpeAdmin(graph).UpdateCustomPropertiesForConfigAsync(Config(), OwnContainer,
        [
            new CustomPropertyDto(SpeContainerOwnershipGuard.MarkerPropertyName, Customer, false),
            new CustomPropertyDto("matterNumber", "M-1", true),
        ]);

        var patch = graph.Requests.Should().ContainSingle(r => r.Method == "PATCH").Subject;
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.TryGetProperty(SpeContainerOwnershipGuard.MarkerPropertyName, out _).Should().BeFalse();
        body.RootElement.GetProperty("matterNumber").GetProperty("value").GetString().Should().Be("M-1");
    }

    [Fact]
    public async Task SpeAdmin_ItemSearchAcrossTheType_KeepsOnlyThisStampsHits_AndDropsGraphsTotal()
    {
        var graph = new RoutedGraph()
            .Respond("POST", "/v1.0/search/query", SearchResponse(total: 40, more: false, (OwnContainer, "ours.docx"), (ForeignContainer, "theirs.docx")))
            .Container(OwnContainer, marker: Customer)
            .Container(ForeignContainer, marker: OtherCustomer);

        var page = await SpeAdmin(graph).SearchItemsForConfigAsync(Config(), "contract", containerId: null, fileType: null, pageSize: 25, skipToken: null);

        page.Items.Select(i => (i.Name, i.ContainerId)).Should().Equal(("ours.docx", OwnContainer));
        page.TotalCount.Should().Be(1, "Graph's total counts other customers' documents");
        page.NextSkipToken.Should().BeNull();
    }

    [Fact]
    public async Task SpeAdmin_ItemSearch_OnlyForeignHitsBehindTheFirstPage_ReturnsNoNextToken()
    {
        // Graph keeps saying "more results", but every hit is another customer's. A next-page token here would
        // tell the caller that other customers hold documents matching the query.
        var graph = new RoutedGraph()
            .Respond("POST", "/v1.0/search/query", SearchResponse(total: 900, more: true, (ForeignContainer, "theirs.docx")))
            .Container(ForeignContainer, marker: OtherCustomer);

        var page = await SpeAdmin(graph).SearchItemsForConfigAsync(Config(), "merger", containerId: null, fileType: null, pageSize: 5, skipToken: null);

        page.Items.Should().BeEmpty();
        page.NextSkipToken.Should().BeNull();
        graph.Requests.Count(r => r.Path == "/v1.0/search/query").Should().Be(SpeAdminGraphService.MaxSearchRoundsPerPage);
    }

    [Fact]
    public async Task SpeAdmin_DeletedContainerList_ShowsOnlyThisStampsContainers()
    {
        var graph = new RoutedGraph()
            .Respond("GET", "/v1.0/storage/fileStorage/deletedContainers",
                $$"""{"value":[{"id":"{{OwnContainer}}","displayName":"Ours"},{"id":"{{ForeignContainer}}","displayName":"Theirs"}]}""")
            .DeletedContainer(OwnContainer, marker: Customer)
            .DeletedContainer(ForeignContainer, marker: OtherCustomer);

        var deleted = await SpeAdmin(graph).ListDeletedContainersForConfigAsync(Config(), Config().ContainerTypeId);

        deleted.Select(c => c.Id).Should().Equal(OwnContainer);
    }

    [Fact]
    public async Task MarkOwned_GraphRefuses_DeletesTheUnmarkedContainer()
    {
        var graph = new RoutedGraph()
            .Error("PATCH", "/v1.0/storage/fileStorage/containers/new-container/customProperties", HttpStatusCode.BadRequest)
            .Respond("DELETE", "/v1.0/storage/fileStorage/containers/new-container", "", HttpStatusCode.NoContent);

        var act = () => Guard(graph).MarkOwnedAsync("new-container");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*deleted again*");
        graph.Requests.Should().Contain(r => r.Method == "DELETE" && r.Path == "/v1.0/storage/fileStorage/containers/new-container");
    }

    [Fact]
    public async Task IsOwned_WithTheCache_ReadsEachMarkerOnce_AndMarkingIsVisibleImmediately()
    {
        var graph = new RoutedGraph()
            .Container(OwnContainer, marker: Customer)
            .Respond("PATCH", "/v1.0/storage/fileStorage/containers/new-container/customProperties", "{}");
        var cache = new GraphMetadataCache(
            new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())),
            NullLogger<GraphMetadataCache>.Instance);
        var guard = TestSpeOwnership.Real(new StubFactory(graph.Client), Customer, cache: cache);

        (await guard.IsOwnedAsync(OwnContainer)).Should().BeTrue();
        (await guard.IsOwnedAsync(OwnContainer)).Should().BeTrue();
        await guard.MarkOwnedAsync("new-container");
        (await guard.IsOwnedAsync("new-container")).Should().BeTrue();

        graph.Requests.Count(r => r.Method == "GET").Should().Be(1, "the marker VALUE is cached, and marking writes it");
    }

    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/drives/other-drive/items/root/delta?x=/drives/own-container/")]
    [InlineData("https://graph.microsoft.com/v1.0/drives/own-container/../other-drive/items/root/delta")]
    public void DeltaLink_TheCheckedDriveOnlyInTheQueryOrBehindDotSegments_IsRefused(string deltaLink)
    {
        var act = () => SpeFileStore.EnsureDeltaLinkTargetsDrive(deltaLink, OwnContainer);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StampContainerIds_OnlyTheOwnedListIsSplit()
    {
        var configuration = TestSpeOwnership.Configuration(new Dictionary<string, string?>
        {
            ["EmailProcessing:DefaultContainerId"] = " b!default ",
            ["Communication:ArchiveContainerId"] = "b!a;b!b",                    // a single id, taken verbatim
            ["SharePointEmbedded:OwnedContainerIds"] = "b!old1, b!old2;;b!old3",
        });

        Sprk.Bff.Api.Infrastructure.DI.GraphModule.StampContainerIds(configuration)
            .Should().BeEquivalentTo("b!default", "b!a;b!b", "b!old1", "b!old2", "b!old3");
    }

    [Fact]
    public async Task SpeAdmin_BulkJob_ForeignContainer_IsTheSameItemErrorAsOutOfScope_NoContentCall()
    {
        // A bulk delete / permission job names caller-chosen containers: another customer's gets exactly the item error a
        // container outside the caller's business units gets — the batch reveals nothing and touches nothing of it.
        var graph = new RoutedGraph().Container(ForeignContainer, marker: OtherCustomer);
        var bulk = new BulkOperationService(SpeAdmin(graph), NullLogger<BulkOperationService>.Instance);

        var (client, error) = await bulk.ClientForContainerAsync(Config(), ForeignContainer, Guid.NewGuid(), CancellationToken.None);

        client.Should().BeNull();
        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        graph.Requests.Should().OnlyContain(r => r.Method == "GET" && r.Path == $"/v1.0/storage/fileStorage/containers/{ForeignContainer}");
    }

    [Fact]
    public async Task SpeAdmin_CreateContainer_MarksTheNewContainerAsThisCustomers()
    {
        var businessUnit = Guid.NewGuid();
        var graph = NewContainerGraph("New", businessUnit);

        await SpeAdmin(graph).CreateContainerForConfigAsync(Config(), Config().ContainerTypeId, "New", null, businessUnit);

        graph.Requests.Should().Contain(r => r.Method == "PATCH" && r.Path.EndsWith("/new-container/customProperties")
            && r.Body!.Contains(SpeContainerOwnershipGuard.MarkerPropertyName));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SpeContainerOwnershipGuard Guard(RoutedGraph graph, Dictionary<string, string?>? settings = null)
        => TestSpeOwnership.Real(new StubFactory(graph.Client), Customer, settings);

    private static async Task<SdapProblemException> Refusal(Func<Task> call)
        => (await call.Should().ThrowAsync<SdapProblemException>()).Which;

    private static SpeAdminGraphService.ContainerTypeConfig Config() => new(
        ConfigId: Guid.Parse("0f8c3c1e-0000-4000-8000-000000000001"),
        ContainerTypeId: "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
        ClientId: "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e",
        TenantId: BffTenant,
        SecretKeyVaultName: "");

    private static SpeAdminGraphService SpeAdmin(RoutedGraph graph)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            ["TENANT_ID"] = BffTenant,
        }).Build();
        var factory = new StubFactory(graph.Client);

        return new SpeAdminGraphService(
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            graphClientFactory: factory,
            ownership: Guard(graph));
    }

    private static string SearchResponse(int total, bool more, params (string Drive, string Name)[] hits)
    {
        var items = hits.Select((h, i) => new Dictionary<string, object>
        {
            ["hitId"] = i.ToString(),
            ["resource"] = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.driveItem",
                ["id"] = $"item{i}",
                ["name"] = h.Name,
                ["parentReference"] = new Dictionary<string, object> { ["driveId"] = h.Drive },
            },
        });
        return JsonSerializer.Serialize(new
        {
            value = new[] { new { hitsContainers = new[] { new { total, moreResultsAvailable = more, hits = items } } } },
        });
    }

    private sealed record Sent(string Method, string Path, string Query, string? Body);

    /// <summary>Answers exact (method, path) routes; anything else is a 404. Records every request.</summary>
    private sealed class RoutedGraph : HttpMessageHandler
    {
        private readonly Dictionary<(string, string), (HttpStatusCode Status, string Body)> _routes = new();
        private readonly List<Sent> _requests = new();

        public RoutedGraph()
            => Client = new GraphServiceClient(new HttpClient(this), new AnonymousAuthenticationProvider(), Base);

        public GraphServiceClient Client { get; }

        public IReadOnlyList<Sent> Requests => _requests;

        public RoutedGraph Container(string id, string? marker)
        {
            var props = marker is null
                ? "{}"
                : $$$"""{"{{{SpeContainerOwnershipGuard.MarkerPropertyName}}}":{"value":"{{{marker}}}","isSearchable":false}}""";
            return Respond("GET", $"/v1.0/storage/fileStorage/containers/{id}", $$$"""{"id":"{{{id}}}","customProperties":{{{props}}}}""");
        }

        public RoutedGraph DeletedContainer(string id, string? marker)
        {
            var props = marker is null
                ? "{}"
                : $$$"""{"{{{SpeContainerOwnershipGuard.MarkerPropertyName}}}":{"value":"{{{marker}}}","isSearchable":false}}""";
            return Respond("GET", $"/v1.0/storage/fileStorage/deletedContainers/{id}", $$$"""{"id":"{{{id}}}","customProperties":{{{props}}}}""");
        }

        public RoutedGraph Respond(string method, string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[(method, path)] = (status, body);
            return this;
        }

        public RoutedGraph Error(string method, string path, HttpStatusCode status)
            => Respond(method, path, $$$"""{"error":{"code":"{{{status}}}","message":"{{{status}}}"}}""", status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            _requests.Add(new Sent(request.Method.Method, path, request.RequestUri.Query, body));

            var (status, json) = _routes.TryGetValue((request.Method.Method, path), out var route)
                ? route
                : (HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"not routed"}}""");

            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StubFactory(GraphServiceClient appOnly) : IGraphClientFactory
    {
        public GraphServiceClient ForApp() => appOnly;

        public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Delegated Graph is not under test here.");

        public Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Delegated Graph is not under test here.");
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Upload sessions are not under test here.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from this test.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from this test.");
    }
}
