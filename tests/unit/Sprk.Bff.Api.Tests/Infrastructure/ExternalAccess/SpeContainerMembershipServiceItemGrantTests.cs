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
/// mail, read back, and recorded under <see cref="Membership.ItemMarkerKey"/>; only a permission that is still exactly what
/// this code made is ever removed. Pinned:
/// <list type="bullet">
/// <item>The invite body (recipient by object id, roles exactly <c>write</c>, sign-in required, no mail) and that no
/// container permission is POSTed.</item>
/// <item>Reuse of a standing grant; a stale marker cleared; a marker recorded against another item undone only while it is
/// still ours; a permission a person changed never deleted.</item>
/// <item>A direct permission the user already holds is never recorded (HeldByOther / HeldReadOnly).</item>
/// <item>A grant that cannot be read back or recorded is deleted again (Failed); an incomplete permission read grants nothing.</item>
/// <item>The flagged-role sweep's DELETE carries <c>Prefer: onlyRemoveContainerScopedPermission</c>, so item grants stay.</item>
/// </list>
/// </summary>
/// <remarks>Mocking boundary (ADR-038 §4): a real <see cref="GraphServiceClient"/> over a hand-written routed transport that
/// records every request (no <c>Mock&lt;HttpMessageHandler&gt;</c>); the ownership guard is <see cref="TestSpeOwnership.AllowAll"/>
/// (its refusal is pinned in <c>SpeAppOnlyContainerIsolationTests</c>).</remarks>
public class SpeContainerMembershipServiceItemGrantTests
{
    private const string Base = "https://graph.test/v1.0";
    private const string Container = "secure-container";
    private const string Item = "item-1";
    private const string OtherItem = "item-0";
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

    private readonly Wire _graph = new();

    private Membership Service()
        => new(TestSpeOwnership.AllowAll(new Factory(_graph.Client)), NullLogger<Membership>.Instance);

    private static string Perm(string id, Guid holder, params string[] roles)
        => JsonSerializer.Serialize(new { id, roles, grantedToV2 = new { user = new { id = holder.ToString("D") } } });

    private static string PermInherited(string id, Guid holder, params string[] roles)
        => JsonSerializer.Serialize(new { id, roles, inheritedFrom = new { id = "root" }, grantedToV2 = new { user = new { id = holder.ToString("D") } } });

    private Wire ContainerWithMarkers(params (string Key, string Value)[] markers)
    {
        var props = markers.ToDictionary(m => m.Key, m => (object)new { value = m.Value, isSearchable = false });
        return _graph.Respond("GET", ContainerPath, JsonSerializer.Serialize(new { id = Container, customProperties = props }));
    }

    /// <summary>The happy path's wire: no marker, no existing direct permission, the invite answers "new", it reads back.</summary>
    private void FreshGrantRoutes()
    {
        ContainerWithMarkers();
        _graph.Respond("GET", ItemPermissions(Item), JsonSerializer.Serialize(new { value = new object[0] }));
        _graph.Respond("POST", InvitePath, $$"""{"value":[{{Perm("new", ObjectId, "write")}}]}""");
        _graph.Respond("GET", ItemPermission(Item, "new"), Perm("new", ObjectId, "write"));
        _graph.Respond("PATCH", MarkerPatchPath, "{}");
        _graph.Respond("DELETE", ItemPermission(Item, "new"), "", HttpStatusCode.NoContent);
    }

    private Task<Membership.ItemGrantOutcome> Grant()
        => Service().GrantMarkedItemWriterAsync(Container, Item, DocumentId, SystemUserId, ObjectId);

    private IEnumerable<Sent> Writes => _graph.Requests.Where(r => r.Method != "GET");

    private static JsonElement MarkerPatch(Sent patch)
    {
        using var doc = JsonDocument.Parse(patch.Body!);
        return doc.RootElement.Clone();
    }

    // ── The grant ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "191g: the grant is ONE app-only item invite — recipient by object id, roles exactly 'write', sign-in required, no mail — read back and recorded; no container permission is touched")]
    public async Task Grant_InvitesWriteOnTheItem_ReadsBack_Records()
    {
        FreshGrantRoutes();

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        var invite = _graph.Requests.Should().ContainSingle(r => r.Method == "POST").Subject;
        invite.Path.Should().Be(InvitePath);
        using (var body = JsonDocument.Parse(invite.Body!))
        {
            var root = body.RootElement;
            root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("write");
            root.GetProperty("requireSignIn").GetBoolean().Should().BeTrue();
            root.GetProperty("sendInvitation").GetBoolean().Should().BeFalse("no invitation mail is ever sent to the guest");
            var recipient = root.GetProperty("recipients").EnumerateArray().Should().ContainSingle().Subject;
            recipient.GetProperty("objectId").GetString().Should().Be(ObjectId.ToString("D"));
            recipient.TryGetProperty("email", out _).Should().BeFalse("the existing identity only — never an email invitation");
        }

        _graph.Requests.Should().Contain(r => r.Method == "GET" && r.Path == ItemPermission(Item, "new"), "the grant is read back");
        var patch = Writes.Where(r => r.Method == "PATCH").Should().ContainSingle().Subject;
        patch.Path.Should().Be(MarkerPatchPath);
        MarkerPatch(patch).GetProperty(Key).GetProperty("value").GetString().Should().Be(Membership.ItemMarkerValue(Item, "new"));
        _graph.Requests.Should().NotContain(r => r.Path.Contains("/fileStorage/containers/") && r.Path.Contains("/permissions"),
            "a flagged caller never gets a container role");
    }

    [Fact(DisplayName = "191g: a marker naming a write permission of ours on this item that still stands is REUSED — nothing written")]
    public async Task Grant_StandingMarkedGrant_IsReused()
    {
        ContainerWithMarkers((Key, Membership.ItemMarkerValue(Item, "ours")));
        _graph.Respond("GET", ItemPermission(Item, "ours"), Perm("ours", ObjectId, "write"));

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Reused);

        Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "191g: a marker whose permission is GONE is cleared, then a fresh grant is made")]
    public async Task Grant_MarkerPermissionGone_ClearsAndRegrants()
    {
        FreshGrantRoutes();
        ContainerWithMarkers((Key, Membership.ItemMarkerValue(Item, "gone")));   // GET permissions/gone is not routed → 404

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        Writes.Should().NotContain(r => r.Method == "DELETE");
        var patches = Writes.Where(r => r.Method == "PATCH").ToList();
        patches.Should().HaveCount(2);
        MarkerPatch(patches[0]).GetProperty(Key).ValueKind.Should().Be(JsonValueKind.Null, "the stale marker is cleared first");
        MarkerPatch(patches[1]).GetProperty(Key).GetProperty("value").GetString().Should().Be(Membership.ItemMarkerValue(Item, "new"));
    }

    [Fact(DisplayName = "191g: a marker recorded against ANOTHER item of the document, still exactly ours, is undone before the new grant")]
    public async Task Grant_MarkerOnAnotherItem_StillOurs_IsUndone()
    {
        FreshGrantRoutes();
        ContainerWithMarkers((Key, Membership.ItemMarkerValue(OtherItem, "old")));
        _graph.Respond("GET", ItemPermission(OtherItem, "old"), Perm("old", ObjectId, "write"));
        _graph.Respond("DELETE", ItemPermission(OtherItem, "old"), "", HttpStatusCode.NoContent);

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);

        Writes.Where(r => r.Method == "DELETE").Select(r => r.Path).Should().Equal(ItemPermission(OtherItem, "old"));
    }

    public static TheoryData<string, string> ChangedBySomeone() => new()
    {
        { "downgraded to read", Perm("ours", ObjectId, "read") },
        { "upgraded to owner", Perm("ours", ObjectId, "owner") },
        { "a role added", Perm("ours", ObjectId, "write", "read") },
        { "now inherited", PermInherited("ours", ObjectId, "write") },
    };

    public static TheoryData<string, string> ChangedOrReassigned() => new()
    {
        { "downgraded to read", Perm("ours", ObjectId, "read") },
        { "upgraded to owner", Perm("ours", ObjectId, "owner") },
        { "a role added", Perm("ours", ObjectId, "write", "read") },
        { "now inherited", PermInherited("ours", ObjectId, "write") },
        { "another holder", Perm("ours", SomeoneElse, "write") },
    };

    /// <summary>MUTATION: deleting the marked permission whenever it is not reusable (item permission ids are identity-derived,
    /// so the id can name a permission a person re-made).</summary>
    [Theory(DisplayName = "191g: a marked permission someone CHANGED is never deleted — only the marker is cleared")]
    [MemberData(nameof(ChangedOrReassigned))]
    public async Task Grant_MarkedPermissionChanged_IsNeverDeleted(string because, string changed)
    {
        FreshGrantRoutes();
        ContainerWithMarkers((Key, Membership.ItemMarkerValue(Item, "ours")));
        _graph.Respond("GET", ItemPermission(Item, "ours"), changed);
        _graph.Respond("DELETE", ItemPermission(Item, "ours"), "", HttpStatusCode.NoContent);

        await Grant();

        Writes.Should().NotContain(r => r.Method == "DELETE" && r.Path == ItemPermission(Item, "ours"), because);
        MarkerPatch(Writes.First(r => r.Method == "PATCH")).GetProperty(Key).ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory(DisplayName = "191g: a DIRECT permission the user already holds is never recorded — an editing one is HeldByOther, read-only is HeldReadOnly")]
    [InlineData("write", Membership.ItemGrantOutcome.HeldByOther)]
    [InlineData("owner", Membership.ItemGrantOutcome.HeldByOther)]
    [InlineData("read", Membership.ItemGrantOutcome.HeldReadOnly)]
    public async Task Grant_UserAlreadyHoldsDirectPermission_NothingWritten(string role, Membership.ItemGrantOutcome expected)
    {
        ContainerWithMarkers();
        _graph.Respond("GET", ItemPermissions(Item), $$"""{"value":[{{Perm("theirs", ObjectId, role)}}]}""");

        (await Grant()).Should().Be(expected);

        Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "191g: a user holding a read AND a write direct permission is HeldByOther, whatever the order")]
    public async Task Grant_ReadAndWriteHeld_IsHeldByOther()
    {
        ContainerWithMarkers();
        _graph.Respond("GET", ItemPermissions(Item),
            $$"""{"value":[{{Perm("r", ObjectId, "read")}},{{Perm("w", ObjectId, "write")}}]}""");

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.HeldByOther);
    }

    [Fact(DisplayName = "191g (must not fire): an INHERITED permission (a container role) or another user's permission does not count as held")]
    public async Task Grant_InheritedOrOthersPermission_StillGrants()
    {
        FreshGrantRoutes();
        _graph.Respond("GET", ItemPermissions(Item),
            $$"""{"value":[{{PermInherited("inh", ObjectId, "read")}},{{Perm("x", SomeoneElse, "write")}}]}""");

        (await Grant()).Should().Be(Membership.ItemGrantOutcome.Granted);
    }

    [Fact(DisplayName = "191g: an item permission list that cannot be read to its end grants nothing")]
    public async Task Grant_PermissionsPagedBeyondBound_Fails()
    {
        ContainerWithMarkers();
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
            case "read-back-read": _graph.Respond("GET", ItemPermission(Item, "new"), Perm("new", ObjectId, "read")); break;
            case "marker-patch-fails": _graph.Error("PATCH", MarkerPatchPath, HttpStatusCode.InternalServerError); break;
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

    [Theory(DisplayName = "191g: blank inputs grant nothing and call nothing")]
    [InlineData("", Item)]
    [InlineData(Container, "")]
    public async Task Grant_BlankInputs_Fail(string container, string item)
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
        Writes.Should().Contain(r => r.Method == "PATCH" && r.Path == MarkerPatchPath);
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
        _graph.Respond("PATCH", MarkerPatchPath, "{}");
        using var cts = new CancellationTokenSource();
        _graph.OnRequest = r => { if (r.Method == "POST") cts.Cancel(); };

        var outcome = await Service().GrantMarkedWriterAsync(
            Container, Membership.JitWriterMarkerPrefix, SystemUserId, "u@contoso.example", cts.Token);

        outcome.Should().Be(Membership.MarkedGrantOutcome.Granted);
        var patch = Writes.Should().ContainSingle(r => r.Method == "PATCH").Subject;
        MarkerPatch(patch).GetProperty(Membership.MarkerKey(Membership.JitWriterMarkerPrefix, SystemUserId))
            .GetProperty("value").GetString().Should().Be("role-new");
        Writes.Should().NotContain(r => r.Method == "DELETE");
    }

    // ── The removal ────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<Membership.MarkedRemovalOutcome> Remove()
        => Service().RemoveMarkedItemGrantAsync(Container, Key, Item, "ours");

    [Fact(DisplayName = "191g: removing a marked item grant that is still ours deletes that ONE item permission, then clears the marker")]
    public async Task Remove_StillOurs_DeletesThenClears()
    {
        _graph.Respond("GET", ItemPermission(Item, "ours"), Perm("ours", ObjectId, "write"));
        _graph.Respond("DELETE", ItemPermission(Item, "ours"), "", HttpStatusCode.NoContent);
        _graph.Respond("PATCH", MarkerPatchPath, "{}");

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.Removed);

        Writes.Select(r => (r.Method, r.Path)).Should().Equal(("DELETE", ItemPermission(Item, "ours")), ("PATCH", MarkerPatchPath));
        MarkerPatch(Writes.Last()).GetProperty(Key).ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "191g: removing a marked item grant that is already gone clears the marker — MarkerCleared")]
    public async Task Remove_AlreadyGone_ClearsMarker()
    {
        _graph.Respond("PATCH", MarkerPatchPath, "{}");

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.MarkerCleared);

        Writes.Select(r => r.Method).Should().Equal("PATCH");
    }

    /// <summary>MUTATION: dropping the plain-writer check in RemoveMarkedItemGrantAsync.</summary>
    [Theory(DisplayName = "191g: a marked item permission someone CHANGED is never deleted by the sync — only the marker is cleared")]
    [MemberData(nameof(ChangedBySomeone))]
    public async Task Remove_ChangedBySomeone_IsNeverDeleted(string because, string changed)
    {
        _ = because;   // the row name is the case
        _graph.Respond("GET", ItemPermission(Item, "ours"), changed);
        _graph.Respond("DELETE", ItemPermission(Item, "ours"), "", HttpStatusCode.NoContent);
        _graph.Respond("PATCH", MarkerPatchPath, "{}");

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.MarkerCleared);

        Writes.Should().NotContain(r => r.Method == "DELETE");
    }

    [Theory(DisplayName = "191g: a removal Graph cannot complete is Failed and keeps the marker (the next pass retries)")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Remove_GraphFault_FailsAndKeepsMarker(string failing)
    {
        _graph.Respond("GET", ItemPermission(Item, "ours"), Perm("ours", ObjectId, "write"));
        _graph.Respond("DELETE", ItemPermission(Item, "ours"), "", HttpStatusCode.NoContent);
        _graph.Respond("PATCH", MarkerPatchPath, "{}");
        _graph.Error(failing, ItemPermission(Item, "ours"), HttpStatusCode.ServiceUnavailable);

        (await Remove()).Should().Be(Membership.MarkedRemovalOutcome.Failed);

        Writes.Should().NotContain(r => r.Method == "PATCH");
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

    /// <summary>Answers exact (method, path) routes (404 otherwise) and records every request, including its Prefer header.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        private readonly Dictionary<(string, string), (HttpStatusCode Status, string Body)> _routes = new();
        private readonly List<Sent> _requests = new();

        public Wire() => Client = new GraphServiceClient(new HttpClient(this), new AnonymousAuthenticationProvider(), Base);

        public GraphServiceClient Client { get; }

        public IReadOnlyList<Sent> Requests => _requests;

        /// <summary>Runs after each request is recorded, before it is answered.</summary>
        public Action<Sent>? OnRequest { get; set; }

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

            var (status, json) = _routes.TryGetValue((request.Method.Method, path), out var route)
                ? route
                : (HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"not routed"}}""");

            return new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.NoContent ? null : new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
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
