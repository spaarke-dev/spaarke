using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// uac-r2 task 191g (owner Q2 = b) — the item-grant primitives of <see cref="Membership"/> on the wire: a flagged-external
/// caller's Office edit is ONE write permission on ONE drive item (never a container role), made app-only with no invitation
/// mail, read back, recorded under <see cref="Membership.ItemMarkerKey"/> (option B: the live-proven 39-character profile),
/// and the record read back; only the holder's own plain direct write is ever removed. Pinned:
/// <list type="bullet">
/// <item>The invite body (recipient by object id, roles exactly <c>write</c>, sign-in required, no mail); no container
/// permission is touched.</item>
/// <item>The marker is read back after it is written; a marker PATCH Graph refuses, or one that does not stick, undoes the
/// grant (the live test's 400).</item>
/// <item>Reuse of a standing grant; a stale marker cleared; a grant recorded on another item undone only while it is still
/// the holder's plain direct write.</item>
/// <item>A direct permission the user already holds is never recorded (HeldByOther / HeldReadOnly).</item>
/// <item>Removal deletes ONLY the holder's plain, direct, non-link write — never a link, an inherited permission, or another
/// holder's — and nothing on a partial permission list.</item>
/// <item>The flagged-role sweep's DELETE carries <c>Prefer: onlyRemoveContainerScopedPermission</c>, so item grants stay.</item>
/// </list>
/// </summary>
/// <remarks>Mocking boundary (ADR-038 §4): a real <see cref="GraphServiceClient"/> over a hand-written routed transport that
/// records every request and keeps the container's custom properties (no <c>Mock&lt;HttpMessageHandler&gt;</c>); the ownership
/// guard is <see cref="TestSpeOwnership.AllowAll"/> (its refusal is pinned in <c>SpeAppOnlyContainerIsolationTests</c>).</remarks>
public class SpeContainerMembershipServiceItemGrantTests
{
    private const string Base = "https://graph.test/v1.0";
    private const string Container = "secure-container";
    private const string Item = "016ITEM1";
    private const string OtherItem = "016ITEM0";
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ObjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SomeoneElse = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly string ContainerPath = $"/v1.0/storage/fileStorage/containers/{Container}";
    private static readonly string MarkerPatchPath = $"{ContainerPath}/customProperties";
    private static string ItemPermissions(string item) => $"/v1.0/drives/{Container}/items/{item}/permissions";
    private static string ItemPermission(string item, string id) => $"{ItemPermissions(item)}/{id}";
    private static readonly string InvitePath = $"/v1.0/drives/{Container}/items/{Item}/invite";

    private static readonly string Key = Membership.ItemMarkerKey(SystemUserId, DocumentId);
    private static string Value(string item) => Membership.ItemMarkerValue(SystemUserId, DocumentId, item)!;

    private readonly Wire _graph = new();

    private Membership Service()
        => new(TestSpeOwnership.AllowAll(new Factory(_graph.Client)), NullLogger<Membership>.Instance);

    private static object Perm(string id, Guid holder, params string[] roles)
        => new { id, roles, grantedToV2 = new { user = new { id = holder.ToString("D") } } };

    private static object PermInherited(string id, Guid holder, params string[] roles)
        => new { id, roles, inheritedFrom = new { id = "root" }, grantedToV2 = new { user = new { id = holder.ToString("D") } } };

    private static object PermLink(string id, Guid holder, params string[] roles)
        => new { id, roles, link = new { type = "edit", scope = "users" }, grantedToV2 = new { user = new { id = holder.ToString("D") } } };

    private void ItemHas(string item, params object[] permissions)
        => _graph.Respond("GET", ItemPermissions(item), JsonSerializer.Serialize(new { value = permissions }));

    /// <summary>The happy path's wire: no marker, no direct permission, the invite answers "new", it reads back.</summary>
    private void FreshGrantRoutes()
    {
        ItemHas(Item);
        _graph.Respond("POST", InvitePath, JsonSerializer.Serialize(new { value = new[] { Perm("new", ObjectId, "write") } }));
        _graph.Respond("GET", ItemPermission(Item, "new"), JsonSerializer.Serialize(Perm("new", ObjectId, "write")));
        _graph.Respond("DELETE", ItemPermission(Item, "new"), "", HttpStatusCode.NoContent);
    }

    private Task<Membership.ItemGrantOutcome> Grant()
        => Service().GrantMarkedItemWriterAsync(Container, Item, DocumentId, SystemUserId, ObjectId);

    private IEnumerable<Sent> Writes => _graph.Requests.Where(r => r.Method != "GET");

    private IEnumerable<Sent> Patches => Writes.Where(r => r.Method == "PATCH");

    private static JsonElement Body(Sent request)
    {
        using var doc = JsonDocument.Parse(request.Body!);
        return doc.RootElement.Clone();
    }

    // ── The grant ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "191g: the grant is ONE app-only item invite — recipient by object id, roles exactly 'write', sign-in required, no mail — read back, recorded and the record read back; no container permission is touched")]
    public async Task Grant_InvitesWriteOnTheItem_ReadsBack_Records()
    {
        FreshGrantRoutes();

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        var invite = _graph.Requests.Should().ContainSingle(r => r.Method == "POST").Subject;
        invite.Path.Should().Be(InvitePath);
        var root = Body(invite);
        root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("write");
        root.GetProperty("requireSignIn").GetBoolean().Should().BeTrue();
        root.GetProperty("sendInvitation").GetBoolean().Should().BeFalse("no invitation mail is ever sent to the guest");
        var recipient = root.GetProperty("recipients").EnumerateArray().Should().ContainSingle().Subject;
        recipient.GetProperty("objectId").GetString().Should().Be(ObjectId.ToString("D"));
        recipient.TryGetProperty("email", out _).Should().BeFalse("the existing identity only — never an email invitation");

        _graph.Requests.Should().Contain(r => r.Method == "GET" && r.Path == ItemPermission(Item, "new"), "the grant is read back");
        var patch = Patches.Should().ContainSingle().Subject;
        patch.Path.Should().Be(MarkerPatchPath);
        var marker = Body(patch).GetProperty(Key);
        marker.GetProperty("value").GetString().Should().Be(Value(Item));
        marker.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
        _graph.Requests.SkipWhile(r => r != patch).Should().Contain(r => r.Method == "GET" && r.Path == ContainerPath,
            "the marker is read back after it is written");
        _graph.Properties.Should().ContainKey(Key).WhoseValue.Should().Be(Value(Item));
        _graph.Requests.Should().NotContain(r => r.Path.Contains("/fileStorage/containers/") && r.Path.Contains("/permissions"),
            "a flagged caller never gets a container role");
    }

    [Theory(DisplayName = "191g: a marker PATCH Graph refuses (the live test's 400), or one that does not stick, UNDOES the grant — Failed, nothing recorded")]
    [InlineData("refused")]
    [InlineData("not-stored")]
    public async Task Grant_MarkerNotProven_IsUndone(string how)
    {
        FreshGrantRoutes();
        if (how == "refused")
            _graph.PatchStatus = HttpStatusCode.BadRequest;
        else
            _graph.PatchIgnored = true;

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Where(r => r.Method == "DELETE").Select(r => r.Path).Should().Equal(ItemPermission(Item, "new"));
        _graph.Properties.Should().NotContainKey(Key);
    }

    [Fact(DisplayName = "191g: a marker for this item whose holder still has a plain direct write is REUSED — nothing written")]
    public async Task Grant_StandingMarkedGrant_IsReused()
    {
        _graph.Properties[Key] = Value(Item);
        ItemHas(Item, Perm("ours", ObjectId, "write"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Reused);

        Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "191g: a marker whose grant is GONE is cleared, then a fresh grant is made and recorded")]
    public async Task Grant_MarkedGrantGone_ClearsAndRegrants()
    {
        FreshGrantRoutes();
        _graph.Properties[Key] = Value(Item);

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        Writes.Should().NotContain(r => r.Method == "DELETE");
        var patches = Patches.ToList();
        patches.Should().HaveCount(2);
        Body(patches[0]).GetProperty(Key).ValueKind.Should().Be(JsonValueKind.Null, "the stale marker is cleared first");
        _graph.Properties[Key].Should().Be(Value(Item));
    }

    [Fact(DisplayName = "191g: a marker recorded against ANOTHER item of the document is undone (the holder's plain write there) before the new grant")]
    public async Task Grant_MarkerOnAnotherItem_IsUndone()
    {
        FreshGrantRoutes();
        _graph.Properties[Key] = Value(OtherItem);
        ItemHas(OtherItem, Perm("old", ObjectId, "write"));
        _graph.Respond("DELETE", ItemPermission(OtherItem, "old"), "", HttpStatusCode.NoContent);

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        Writes.Where(r => r.Method == "DELETE").Select(r => r.Path).Should().Equal(ItemPermission(OtherItem, "old"));
        _graph.Properties[Key].Should().Be(Value(Item));
    }

    public static TheoryData<string, object> NotThePlainDirectWrite() => new()
    {
        { "downgraded to read", Perm("p", ObjectId, "read") },
        { "upgraded to owner", Perm("p", ObjectId, "owner") },
        { "a role added", Perm("p", ObjectId, "write", "read") },
        { "inherited", PermInherited("p", ObjectId, "write") },
        { "a sharing link", PermLink("p", ObjectId, "write") },
        { "another holder", Perm("p", SomeoneElse, "write") },
    };

    /// <summary>MUTATION: undoing the old item's permissions without the plain-direct-write check.</summary>
    [Theory(DisplayName = "191g: on the stale marker's OTHER item, nothing but the holder's plain direct write is ever deleted")]
    [MemberData(nameof(NotThePlainDirectWrite))]
    public async Task Grant_StaleMarkerOtherItem_OnlyThePlainDirectWriteIsDeleted(string because, object permission)
    {
        FreshGrantRoutes();
        _graph.Properties[Key] = Value(OtherItem);
        ItemHas(OtherItem, permission);
        _graph.Respond("DELETE", ItemPermission(OtherItem, "p"), "", HttpStatusCode.NoContent);

        await Grant();

        Writes.Should().NotContain(r => r.Method == "DELETE" && r.Path.Contains(OtherItem), because);
    }

    /// <summary>MUTATION (review 191g-A1): clearing the stale marker although its item's list was partial.</summary>
    [Fact(DisplayName = "191g: a stale marker whose OTHER item's permissions cannot be read to their end is KEPT — nothing deleted, nothing granted, Failed")]
    public async Task Grant_StaleMarkerOtherItemPartialList_KeepsMarker()
    {
        FreshGrantRoutes();
        _graph.Properties[Key] = Value(OtherItem);
        _graph.Respond("GET", ItemPermissions(OtherItem),
            $$"""{"value":[{{JsonSerializer.Serialize(Perm("old", ObjectId, "write"))}}],"@odata.nextLink":"{{Base}}/drives/{{Container}}/items/{{OtherItem}}/permissions?$skiptoken=x"}""");
        _graph.Respond("DELETE", ItemPermission(OtherItem, "old"), "", HttpStatusCode.NoContent);

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Should().BeEmpty("the old grant's only record stays and no new grant is made beside it");
        _graph.Properties.Should().ContainKey(Key).WhoseValue.Should().Be(Value(OtherItem));
    }

    /// <summary>MUTATION: acting on (clearing or reusing) a marker this code did not write.</summary>
    [Theory(DisplayName = "191g: a marker under our key that is malformed or whose ids do not derive the key is LEFT ALONE — nothing written, Failed")]
    [InlineData("malformed")]
    [InlineData("mismatched")]
    public async Task Grant_ForeignMarker_IsLeftAlone(string how)
    {
        FreshGrantRoutes();
        var foreign = how == "malformed"
            ? "not-a-marker"
            : Membership.ItemMarkerValue(SomeoneElse, DocumentId, Item)!;   // parses, but derives another key
        _graph.Properties[Key] = foreign;
        ItemHas(Item, Perm("ours", ObjectId, "write"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Should().BeEmpty();
        _graph.Properties[Key].Should().Be(foreign);
    }

    /// <summary>MUTATION (review 191g-A2): undoing the permission but leaving a marker whose write landed.</summary>
    [Fact(DisplayName = "191g: a marker that was written but cannot be READ BACK undoes the permission AND clears the marker")]
    public async Task Grant_MarkerReadBackFails_UndoesPermissionAndMarker()
    {
        FreshGrantRoutes();
        _graph.OnRequest = r => { if (r.Method == "PATCH") _graph.ContainerGetStatus = HttpStatusCode.InternalServerError; };

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Where(r => r.Method == "DELETE").Select(r => r.Path).Should().Equal(ItemPermission(Item, "new"));
        _graph.Properties.Should().NotContainKey(Key, "a marker must not outlive the permission it records");
    }

    /// <summary>MUTATION: clearing the marker although the permission could not be deleted.</summary>
    [Fact(DisplayName = "191g (must not fire): when the undo's DELETE fails, the marker that landed is KEPT — it is the record the sync removes the permission by")]
    public async Task Grant_UndoDeleteFails_KeepsMarker()
    {
        FreshGrantRoutes();
        _graph.Error("DELETE", ItemPermission(Item, "new"), HttpStatusCode.InternalServerError);
        _graph.OnRequest = r => { if (r.Method == "PATCH") _graph.ContainerGetStatus = HttpStatusCode.InternalServerError; };

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        _graph.Properties.Should().ContainKey(Key).WhoseValue.Should().Be(Value(Item));
    }

    [Fact(DisplayName = "191g: a marked grant a person DOWNGRADED to read is not ours any more — marker cleared, nothing deleted, HeldReadOnly")]
    public async Task Grant_MarkedGrantDowngraded_IsLeftAlone()
    {
        _graph.Properties[Key] = Value(Item);
        ItemHas(Item, Perm("p", ObjectId, "read"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.HeldReadOnly);

        Writes.Should().NotContain(r => r.Method == "DELETE" || r.Method == "POST");
        _graph.Properties.Should().NotContainKey(Key);
    }

    [Theory(DisplayName = "191g: a DIRECT permission the user already holds is never recorded — an editing one is HeldByOther, read-only is HeldReadOnly")]
    [InlineData("write", Membership.ItemGrantOutcome.HeldByOther)]
    [InlineData("owner", Membership.ItemGrantOutcome.HeldByOther)]
    [InlineData("read", Membership.ItemGrantOutcome.HeldReadOnly)]
    public async Task Grant_UserAlreadyHoldsDirectPermission_NothingWritten(string role, Membership.ItemGrantOutcome expected)
    {
        ItemHas(Item, Perm("theirs", ObjectId, role));

        (await Grant()).Should().Be(expected);

        Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "191g: a user holding a read AND a write direct permission is HeldByOther, whatever the order")]
    public async Task Grant_ReadAndWriteHeld_IsHeldByOther()
    {
        ItemHas(Item, Perm("r", ObjectId, "read"), Perm("w", ObjectId, "write"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.HeldByOther);
    }

    [Fact(DisplayName = "191g (must not fire): an INHERITED permission (a container role) or another user's permission does not count as held")]
    public async Task Grant_InheritedOrOthersPermission_StillGrants()
    {
        FreshGrantRoutes();
        ItemHas(Item, PermInherited("inh", ObjectId, "read"), Perm("x", SomeoneElse, "write"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);
    }

    [Fact(DisplayName = "191g: an item permission list that cannot be read to its end grants nothing")]
    public async Task Grant_PermissionsPagedBeyondBound_Fails()
    {
        // Every page links to itself: the read stops at the page bound and must not be treated as complete.
        _graph.Respond("GET", ItemPermissions(Item),
            $$"""{"value":[],"@odata.nextLink":"{{Base}}/drives/{{Container}}/items/{{Item}}/permissions?$skiptoken=x"}""");

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Should().BeEmpty();
    }

    [Theory(DisplayName = "191g: a grant that does not read back as the user's plain write, or whose marker cannot be written, is DELETED again — Failed")]
    [InlineData("read-back-404")]
    [InlineData("read-back-read")]
    [InlineData("marker-patch-fails")]
    public async Task Grant_UnprovenOrUnrecorded_IsUndone(string fault)
    {
        FreshGrantRoutes();
        switch (fault)
        {
            case "read-back-404": _graph.Error("GET", ItemPermission(Item, "new"), HttpStatusCode.NotFound); break;
            case "read-back-read": _graph.Respond("GET", ItemPermission(Item, "new"), JsonSerializer.Serialize(Perm("new", ObjectId, "read"))); break;
            case "marker-patch-fails": _graph.PatchStatus = HttpStatusCode.InternalServerError; break;
        }

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Where(r => r.Method == "DELETE").Select(r => r.Path).Should().Equal(ItemPermission(Item, "new"));
    }

    [Fact(DisplayName = "191g: an invite Graph refuses is Failed with nothing recorded")]
    public async Task Grant_InviteRefused_Fails()
    {
        FreshGrantRoutes();
        _graph.Error("POST", InvitePath, HttpStatusCode.Forbidden);

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Failed);

        Writes.Should().NotContain(r => r.Method == "PATCH" || r.Method == "DELETE");
    }

    [Theory(DisplayName = "191g: blank inputs, or an item id outside the marker profile, grant nothing and call nothing")]
    [InlineData("", Item)]
    [InlineData(Container, "")]
    [InlineData(Container, "item|1")]
    [InlineData(Container, "item.1")]
    public async Task Grant_BadInputs_Fail(string container, string item)
    {
        (await Service().GrantMarkedItemWriterAsync(container, item, DocumentId, SystemUserId, ObjectId))
            .Should().Be(Membership.ItemGrantOutcome.Failed);
        (await Service().GrantMarkedItemWriterAsync(Container, Item, DocumentId, SystemUserId, Guid.Empty))
            .Should().Be(Membership.ItemGrantOutcome.Failed);

        _graph.Requests.Should().BeEmpty();
    }

    /// <summary>MUTATION: passing the request's token to the invite, read-back or marker write (an abort after Graph made
    /// the permission would leave a write permission nothing records, so nothing could ever remove it).</summary>
    [Fact(DisplayName = "191g: a request cancelled once the invite is on the wire still reads back and RECORDS the grant — never an unrecorded write permission")]
    public async Task Grant_CancelledAfterInvite_StillRecords()
    {
        FreshGrantRoutes();
        using var cts = new CancellationTokenSource();
        _graph.OnRequest = r => { if (r.Method == "POST") cts.Cancel(); };

        var outcome = await Service().GrantMarkedItemWriterAsync(Container, Item, DocumentId, SystemUserId, ObjectId, cts.Token);

        outcome.Should().Be(Membership.ItemGrantOutcome.Granted);
        _graph.Properties[Key].Should().Be(Value(Item));
        Writes.Should().NotContain(r => r.Method == "DELETE");
    }

    /// <summary>MUTATION: the request's token on the task-171 container grant's POST or marker write (same gap, found in the
    /// 191g review and fixed with it).</summary>
    [Fact(DisplayName = "171/191g: a container JIT grant whose request is cancelled once the POST is on the wire is still RECORDED — never an unrecorded writer role")]
    public async Task ContainerGrant_CancelledAfterPost_StillRecords()
    {
        _graph.Respond("POST", $"{ContainerPath}/permissions",
            """{"id":"role-new","roles":["writer"],"grantedToV2":{"user":{"userPrincipalName":"u@contoso.example"}}}""",
            HttpStatusCode.Created);
        using var cts = new CancellationTokenSource();
        _graph.OnRequest = r => { if (r.Method == "POST") cts.Cancel(); };

        var outcome = await Service().GrantMarkedWriterAsync(
            Container, Membership.JitWriterMarkerPrefix, SystemUserId, "u@contoso.example", cts.Token);

        outcome.Should().Be(Membership.MarkedGrantOutcome.Granted);
        _graph.Properties.Should().ContainKey(Membership.MarkerKey(Membership.JitWriterMarkerPrefix, SystemUserId))
            .WhoseValue.Should().Be("role-new");
        Writes.Should().NotContain(r => r.Method == "DELETE");
    }

    // ── The removal ────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<Membership.MarkedRemovalOutcome> Remove()
        => Service().RemoveMarkedItemGrantAsync(Container, Key, Item, ObjectId);

    [Fact(DisplayName = "191g: removal deletes ONLY the holder's plain direct write among the item's permissions, then clears the marker")]
    public async Task Remove_DeletesOnlyThePlainDirectWrite_ThenClears()
    {
        _graph.Properties[Key] = Value(Item);
        ItemHas(Item,
            Perm("ours", ObjectId, "write"),
            PermLink("link", ObjectId, "write"),
            PermInherited("inh", ObjectId, "write"),
            Perm("other", SomeoneElse, "write"),
            Perm("mine-read", ObjectId, "read"));
        foreach (var id in new[] { "ours", "link", "inh", "other", "mine-read" })
            _graph.Respond("DELETE", ItemPermission(Item, id), "", HttpStatusCode.NoContent);

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.Removed);

        Writes.Select(r => (r.Method, r.Path)).Should().Equal(("DELETE", ItemPermission(Item, "ours")), ("PATCH", MarkerPatchPath));
        _graph.Properties.Should().NotContainKey(Key);
    }

    /// <summary>MUTATION: dropping any part of the plain-direct-write check in the removal (the main session's three
    /// must-not-fire cases: a link, an inherited permission, another holder — plus a changed role).</summary>
    [Theory(DisplayName = "191g (must not fire): removal NEVER deletes a link, an inherited permission, another holder's, or a changed role — only the marker is cleared")]
    [MemberData(nameof(NotThePlainDirectWrite))]
    public async Task Remove_NotThePlainDirectWrite_IsNeverDeleted(string because, object permission)
    {
        _graph.Properties[Key] = Value(Item);
        ItemHas(Item, permission);
        _graph.Respond("DELETE", ItemPermission(Item, "p"), "", HttpStatusCode.NoContent);

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.MarkerCleared);

        Writes.Should().NotContain(r => r.Method == "DELETE", because);
        _graph.Properties.Should().NotContainKey(Key);
    }

    [Fact(DisplayName = "191g: removal on an item that no longer exists clears the marker — MarkerCleared")]
    public async Task Remove_ItemGone_ClearsMarker()
    {
        _graph.Properties[Key] = Value(Item);   // GET permissions is not routed → 404

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.MarkerCleared);

        Writes.Select(r => r.Method).Should().Equal("PATCH");
    }

    [Fact(DisplayName = "191g: removal deletes nothing on a permission list it cannot read to its end, and keeps the marker — Failed")]
    public async Task Remove_PartialList_FailsAndKeepsMarker()
    {
        _graph.Properties[Key] = Value(Item);
        _graph.Respond("GET", ItemPermissions(Item),
            $$"""{"value":[{{JsonSerializer.Serialize(Perm("ours", ObjectId, "write"))}}],"@odata.nextLink":"{{Base}}/drives/{{Container}}/items/{{Item}}/permissions?$skiptoken=x"}""");

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.Failed);

        Writes.Should().BeEmpty();
        _graph.Properties.Should().ContainKey(Key);
    }

    [Theory(DisplayName = "191g: a removal Graph cannot complete is Failed and keeps the marker (the next pass retries)")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Remove_GraphFault_FailsAndKeepsMarker(string failing)
    {
        _graph.Properties[Key] = Value(Item);
        ItemHas(Item, Perm("ours", ObjectId, "write"));
        _graph.Respond("DELETE", ItemPermission(Item, "ours"), "", HttpStatusCode.NoContent);
        if (failing == "GET")
            _graph.Error("GET", ItemPermissions(Item), HttpStatusCode.ServiceUnavailable);
        else
            _graph.Error("DELETE", ItemPermission(Item, "ours"), HttpStatusCode.ServiceUnavailable);

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.Failed);

        Writes.Should().NotContain(r => r.Method == "PATCH");
        _graph.Properties.Should().ContainKey(Key);
    }

    [Fact(DisplayName = "191g: removal without the holder's object id does nothing — Failed")]
    public async Task Remove_NoHolder_Fails()
    {
        (await Service().RemoveMarkedItemGrantAsync(Container, Key, Item, Guid.Empty)).Should().Be(Membership.MarkedRemovalOutcome.Failed);

        _graph.Requests.Should().BeEmpty();
    }

    // ── The flagged-role sweep ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "191g: the flagged-role sweep deletes the container permission with Prefer: onlyRemoveContainerScopedPermission (item grants stay)")]
    public async Task RemoveFlaggedRole_SendsContainerScopedPrefer()
    {
        var path = $"{ContainerPath}/permissions/role-1";
        _graph.Respond("DELETE", path, "", HttpStatusCode.NoContent);

        (await Service().RemoveFlaggedUserContainerRoleAsync(Container, "role-1")).Should().BeTrue();

        var delete = Writes.Should().ContainSingle().Subject;
        (delete.Method, delete.Path).Should().Be(("DELETE", path));
        delete.Prefer.Should().Contain("onlyRemoveContainerScopedPermission");
    }

    [Theory(DisplayName = "191g: the flagged-role sweep counts an already-gone role as done and any other fault as not done")]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task RemoveFlaggedRole_Faults(HttpStatusCode status, bool expected)
    {
        _graph.Error("DELETE", $"{ContainerPath}/permissions/role-1", status);

        (await Service().RemoveFlaggedUserContainerRoleAsync(Container, "role-1")).Should().Be(expected);
    }

    // ── Transport ──────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Sent(string Method, string Path, string Query, string? Body, string Prefer);

    /// <summary>
    /// Answers exact (method, path) routes (404 otherwise) and records every request, including its Prefer header. The
    /// container itself is stateful: <see cref="Properties"/> are its custom properties — a GET of the container returns them,
    /// a PATCH of <c>customProperties</c> merges into them (null deletes) and answers 201 for a new name, 200 otherwise, as the
    /// Graph docs say — unless <see cref="PatchStatus"/> refuses it or <see cref="PatchIgnored"/> answers without storing.
    /// </summary>
    private sealed class Wire : HttpMessageHandler
    {
        private readonly Dictionary<(string, string), (HttpStatusCode Status, string Body)> _routes = new();
        private readonly List<Sent> _requests = new();

        public Wire() => Client = new GraphServiceClient(new HttpClient(this), new AnonymousAuthenticationProvider(), Base);

        public GraphServiceClient Client { get; }

        public IReadOnlyList<Sent> Requests => _requests;

        /// <summary>The container's custom properties (name → value).</summary>
        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

        /// <summary>When set, a customProperties PATCH answers this error status and stores nothing.</summary>
        public HttpStatusCode? PatchStatus { get; set; }

        /// <summary>When true, a customProperties PATCH answers success but stores nothing.</summary>
        public bool PatchIgnored { get; set; }

        /// <summary>Runs after each request is recorded, before it is answered.</summary>
        public Action<Sent>? OnRequest { get; set; }

        /// <summary>When set, a GET of the container answers this error status (its properties are kept).</summary>
        public HttpStatusCode? ContainerGetStatus { get; set; }

        public Wire Respond(string method, string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[(method, path)] = (status, body);
            return this;
        }

        public Wire Error(string method, string path, HttpStatusCode status)
            => Respond(method, path, $$$"""{"error":{"code":"{{{status}}}","message":"{{{status}}}"}}""", status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            var prefer = request.Headers.TryGetValues("Prefer", out var values) ? string.Join(",", values) : "";
            _requests.Add(new Sent(request.Method.Method, path, request.RequestUri.Query, body, prefer));
            OnRequest?.Invoke(_requests[^1]);

            if (request.Method == HttpMethod.Get && path == ContainerPath)
            {
                if (ContainerGetStatus is { } failing)
                    return Json(failing, $$$"""{"error":{"code":"serviceNotAvailable","message":"{{{failing}}}"}}""");
                var props = Properties.ToDictionary(p => p.Key, p => (object)new { value = p.Value, isSearchable = false });
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = Container, customProperties = props }));
            }

            if (request.Method == HttpMethod.Patch && path == MarkerPatchPath)
            {
                if (PatchStatus is { } refused)
                    return Json(refused, $$$"""{"error":{"code":"invalidRequest","message":"{{{refused}}}"}}""");
                var added = false;
                using var doc = JsonDocument.Parse(body!);
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (PatchIgnored)
                        continue;
                    if (entry.Value.ValueKind == JsonValueKind.Null)
                    {
                        Properties.Remove(entry.Name);
                    }
                    else
                    {
                        added |= !Properties.ContainsKey(entry.Name);
                        Properties[entry.Name] = entry.Value.GetProperty("value").GetString()!;
                    }
                }

                return Json(added ? HttpStatusCode.Created : HttpStatusCode.OK, "{}");
            }

            var (status, json) = _routes.TryGetValue((request.Method.Method, path), out var route)
                ? route
                : (HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"not routed"}}""");
            return Json(status, json);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = status == HttpStatusCode.NoContent ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Factory(GraphServiceClient appOnly) : IGraphClientFactory
    {
        public GraphServiceClient ForApp() => appOnly;

        public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Delegated Graph is not under test here.");

        public Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Delegated Graph is not under test here.");
    }
}
