using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 148 (C10 part 2, transitions + backfill) — a record's EXISTING children follow it into
/// isolation when it is provisioned secure, leave isolation when it is unsecured, and the sweep brings every secure record's
/// children into line. Driven through the REAL endpoints, the REAL <see cref="SecureChildReconciler"/>, the REAL ownership
/// resolver and the REAL share synchronizer, over one in-memory Dataverse (<see cref="SecureChildShareWorld"/>) that the
/// provisioning fixture keeps in step with the root it moves (<see cref="ProvisionProjectTestFixture.UseChildWorldForRoots"/>).
/// </summary>
/// <remarks>
/// Every test seeds DECOYS beside the children it moves — an unfiled document, a document of another record, a per-user
/// Direct thread, a child ROOT (a work assignment under the project) and that root's own to-do — and asserts they are never
/// written: a pass that walked too wide, or a rule that moved an ordinary row, fails here.
/// </remarks>
public class SecureChildTransitionTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";

    private static readonly Guid Creator = ProvisionProjectTestFixture.CallerSystemUserId;
    private static readonly Guid SecureTeam = ProvisionProjectTestFixture.SecureOwnerTeamId;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid Colleague = Guid.Parse("b0000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Outsider = Guid.Parse("e0000000-0000-0000-0000-0000000000e5");

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureChildTransitionTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ── The shape of a root's children ──────────────────────────────────────────────────────────────────────────────

    private sealed record Family(
        string RootTable, Guid RootId,
        Guid DocDirect, Guid? DocRelated, Guid Event, Guid TodoUnderDocument, Guid Communication, Guid Memo, Guid MessageInDirectThread,
        Guid UnfiledDocument, Guid OtherRecordsDocument, Guid DirectThread, Guid ChildRoot, Guid ChildRootsTodo)
    {
        /// <summary>The children the transition must move.</summary>
        public IEnumerable<(string Table, Guid Id)> Children()
        {
            yield return ("sprk_document", DocDirect);
            if (DocRelated is { } related)
                yield return ("sprk_document", related);
            yield return ("sprk_event", Event);
            yield return ("sprk_todo", TodoUnderDocument);
            yield return ("sprk_communication", Communication);
            yield return ("sprk_memo", Memo);
            yield return ("sprk_communication", MessageInDirectThread);
        }

        /// <summary>The rows the transition must never write.</summary>
        public IEnumerable<(string Table, Guid Id)> Decoys()
        {
            yield return ("sprk_document", UnfiledDocument);
            yield return ("sprk_document", OtherRecordsDocument);
            yield return ("sprk_communicationthread", DirectThread);
            yield return ("sprk_workassignment", ChildRoot);
            yield return ("sprk_todo", ChildRootsTodo);
        }
    }

    private static string RegardingColumn(string rootTable) => "sprk_regarding" + rootTable["sprk_".Length..];

    /// <summary>
    /// Seeds a root's children (owned by an ordinary team, or by the Secure team when <paramref name="childrenIsolated"/>)
    /// and the decoys.
    /// The communication is USER-owned in the ordinary case — a run-as-user / pre-146 row is a child like any other.
    /// </summary>
    private Family SeedFamily(string rootTable, Guid rootId, bool childrenIsolated)
    {
        var world = _fixture.ChildWorld;
        var regarding = RegardingColumn(rootTable);
        var family = new Family(
            rootTable, rootId,
            DocDirect: Guid.NewGuid(),
            DocRelated: rootTable == "sprk_project" ? Guid.NewGuid() : null,
            Event: Guid.NewGuid(), TodoUnderDocument: Guid.NewGuid(), Communication: Guid.NewGuid(), Memo: Guid.NewGuid(),
            MessageInDirectThread: Guid.NewGuid(),
            UnfiledDocument: Guid.NewGuid(), OtherRecordsDocument: Guid.NewGuid(), DirectThread: Guid.NewGuid(),
            ChildRoot: Guid.NewGuid(), ChildRootsTodo: Guid.NewGuid());

        void Child(string table, Guid id, params (string, string, Guid)[] lookups)
        {
            if (childrenIsolated)
                world.SecureChild(table, id, lookups);
            else
                world.OrdinaryChild(table, id, lookups);
        }

        Child("sprk_document", family.DocDirect, (rootTable, rootTable, rootId));
        if (family.DocRelated is { } related)
            Child("sprk_document", related, ("sprk_relatedproject", "sprk_project", rootId));
        Child("sprk_event", family.Event, (regarding, rootTable, rootId));
        // A GRANDCHILD: filed under the document only, no stamp pointing at the root.
        Child("sprk_todo", family.TodoUnderDocument, ("sprk_regardingdocument", "sprk_document", family.DocDirect));
        if (childrenIsolated)
            world.SecureChild("sprk_communication", family.Communication, (regarding, rootTable, rootId));
        else
            world.UserOwnedChild("sprk_communication", family.Communication, (regarding, rootTable, rootId));
        Child("sprk_memo", family.Memo, (regarding, rootTable, rootId));

        // A message on the record, in a per-user Direct thread: the message is the record's child; the thread is not.
        world.UserOwnedChild("sprk_communicationthread", family.DirectThread);
        Child("sprk_communication", family.MessageInDirectThread,
            (regarding, rootTable, rootId), ("sprk_communicationthread", "sprk_communicationthread", family.DirectThread));

        // Decoys.
        world.OrdinaryChild("sprk_document", family.UnfiledDocument);
        var otherProject = Guid.NewGuid();
        world.OrdinaryRoot("sprk_project", otherProject);
        world.OrdinaryChild("sprk_document", family.OtherRecordsDocument, ("sprk_project", "sprk_project", otherProject));
        world.Add("sprk_workassignment", family.ChildRoot,
            ("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", GeneralTeam)), ("sprk_issecure", true),
            ("sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference("sprk_project", rootId)));
        world.OrdinaryChild("sprk_todo", family.ChildRootsTodo,
            ("sprk_regardingworkassignment", "sprk_workassignment", family.ChildRoot));

        return family;
    }

    private void SeedRoot(string rootTable, Guid rootId, Guid? owningTeam = null, bool isSecure = true)
    {
        switch (rootTable)
        {
            case "sprk_project": _fixture.SeedProject(rootId, owningTeamId: owningTeam, isSecure: isSecure); break;
            case "sprk_matter": _fixture.SeedMatter(rootId, owningTeamId: owningTeam, isSecure: isSecure); break;
            default: _fixture.SeedWorkAssignment(rootId, owningTeamId: owningTeam, isSecure: isSecure); break;
        }
    }

    private static string RecordType(string rootTable) => rootTable switch
    {
        "sprk_project" => "project",
        "sprk_matter" => "matter",
        _ => "workassignment",
    };

    private static int MirrorOf(string rightsCsv) => RecordShareLevels.ChildMirrorMask(RecordShareLevels.MaskForRightsCsv(rightsCsv));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private void AssertDecoysUntouched(Family family, IReadOnlyDictionary<(string, Guid), DataversePrincipalRef?> before)
    {
        foreach (var decoy in family.Decoys())
        {
            _fixture.ChildWorld.OwnerOf(decoy.Table, decoy.Id).Should().Be(before[decoy],
                $"{decoy.Table} {decoy.Id} is not this record's child (unfiled, another record's, per-user, or a root of its own)");
            _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == decoy.Id);
            _fixture.SharesOn(decoy.Id).Should().BeEmpty();
        }
    }

    private Dictionary<(string, Guid), DataversePrincipalRef?> OwnersOf(IEnumerable<(string Table, Guid Id)> rows) =>
        rows.ToDictionary(r => (r.Table, r.Id), r => _fixture.ChildWorld.OwnerOf(r.Table, r.Id));

    // ── PROVISIONING: existing children follow the record into isolation ─────────────────────────────────────────────

    /// <summary>
    /// AC 1 + AC 4: provisioning a record that already has children — direct lookups (both project lookups), FR-26 regarding
    /// lookups, a user-owned communication, a grandchild reached only through a document, a message in a Direct thread —
    /// leaves every one of them owned by the named Secure team (read back), shared with exactly the record's sharees (the
    /// creator here, never with Share), reported per table; the decoys are never written.
    /// </summary>
    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task Provisioning_ReownsEveryExistingChild_AndMirrorsTheRecordsSharees_AndTouchesNoDecoy(string rootTable)
    {
        var rootId = Guid.NewGuid();
        SeedRoot(rootTable, rootId);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily(rootTable, rootId, childrenIsolated: false);
        var decoysBefore = OwnersOf(family.Decoys());

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = RecordType(rootTable), recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var creatorMirror = MirrorOf(ProvisionProjectEndpoint.CreatorAccessRights);
        foreach (var (table, id) in family.Children())
        {
            _fixture.ChildWorld.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureTeam),
                $"{table} {id} is an existing child of the record now secure");
            _fixture.SharesOn(id).Should().Equal(
                new Dictionary<DataversePrincipalRef, int> { [DataversePrincipalRef.User(Creator)] = creatorMirror },
                "a child's sharees are exactly the record's, at the record's rights without Share");
        }

        AssertDecoysUntouched(family, decoysBefore);

        var body = await JsonOf(response);
        var children = body.GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        children.GetProperty("reowned").GetInt32().Should().Be(family.Children().Count());
        children.GetProperty("remaining").GetInt32().Should().Be(0);
        children.GetProperty("tables").EnumerateArray()
            .Single(t => t.GetProperty("table").GetString() == "sprk_document")
            .GetProperty("reowned").GetInt32().Should().Be(rootTable == "sprk_project" ? 2 : 1);
    }

    /// <summary>
    /// AC 3 + AC 5 (provisioning): a child whose re-own Dataverse refuses makes the pass incomplete — a stable reason code and
    /// re-owned/remaining counts, never a success and never a bare 500. The record stays secured and shared; the refused
    /// child keeps its old owner (under-shared, never readable by a new principal); no principal outside the record's sharees
    /// is granted anything. The record keeps its container (the pass runs after it). A second call — the record is now
    /// provisioned — completes the pass (200 <c>childrenOnly</c>) and creates no second container.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenAChildCannotBeReowned_IsIncompleteWithCounts_AndASecondCallCompletesIt()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: false);
        _fixture.ChildWorld.RefusingOwnerWritesOf(family.Event);
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId, sharePrincipalIds = new[] { Colleague } });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("childrenReowned").GetInt32().Should().Be(family.Children().Count() - 1);
        problem.GetProperty("childrenRemaining").GetInt32().Should().BeGreaterThan(0);
        problem.GetProperty("detail").GetString().Should().Contain("re-owned").And.Contain("remaining");

        _fixture.OwningTeamOf(rootId).Should().Be(SecureTeam, "the record's own steps are not undone");
        _fixture.SomeoneCanOpen(rootId).Should().BeTrue();
        _fixture.ContainerIdOf(rootId).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId,
            "the pass runs after the container: a related record the rule cannot place never blocks the record's storage");
        _fixture.ChildWorld.OwnerOf("sprk_event", family.Event).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "the refused child keeps its old owner — under-shared, never handed to anyone new");
        _fixture.Grants.Select(g => g.Principal).Distinct().Should().OnlyContain(
            p => p == DataversePrincipalRef.User(Creator) || p == DataversePrincipalRef.User(Colleague),
            "nothing is ever granted to a principal the record is not shared with");
        _fixture.SharesOn(family.Event).Should().BeEmpty("a child that did not move is not mirrored");

        _fixture.ChildWorld.ClearOwnerWriteFaults();
        var second = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var completed = await JsonOf(second);
        completed.GetProperty("childrenOnly").GetBoolean().Should().BeTrue(
            "the record was provisioned by the first call; the second only completes its related records");
        completed.GetProperty("children").GetProperty("reowned").GetInt32().Should().Be(1);
        _fixture.ChildWorld.OwnerOf("sprk_event", family.Event).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.SharesOn(family.Event).Keys.Should().BeEquivalentTo(
            new[] { DataversePrincipalRef.User(Creator), DataversePrincipalRef.User(Colleague) });
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle("no second container is created");
    }

    /// <summary>
    /// Every assign is verified by read-back: a re-own Dataverse ACCEPTS but does not apply (the silent
    /// <c>ownerid</c> failure) is not counted as done — the pass is incomplete and the child is not mirrored.
    /// </summary>
    [Fact]
    public async Task Provisioning_WhenAReownIsAcceptedButDoesNotReadBack_IsIncomplete_AndThatChildIsNotMirrored()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: false);
        _fixture.ChildWorld.IgnoringOwnerWritesOf(family.Memo);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.ChildWorld.OwnerWrites.Should().Contain(w => w.Id == family.Memo, "the re-own was sent");
        _fixture.ChildWorld.OwnerOf("sprk_memo", family.Memo).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(family.Memo).Should().BeEmpty();
    }

    /// <summary>
    /// AC 6: once provisioned, a repeat call finds nothing to do — it writes nothing and answers 409 as before; and a record
    /// that was already provisioned with children left behind (secured before task 148) gets them completed by the same
    /// call (200 <c>childrenOnly</c>), still without touching the record itself.
    /// </summary>
    [Fact]
    public async Task Provisioning_RepeatedOnAProvisionedRecord_WritesNothing_ButCompletesChildrenLeftBehind()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        SeedFamily("sprk_project", rootId, childrenIsolated: false);
        var client = _fixture.CreateAuthenticatedClient();
        (await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId })).StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerWrites = _fixture.ChildWorld.OwnerWrites.Count;
        var grants = _fixture.Grants.Count;

        var again = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonOf(again)).GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonAlreadyProvisioned);
        _fixture.ChildWorld.OwnerWrites.Should().HaveCount(ownerWrites, "a second pass over the same state changes nothing");
        _fixture.Grants.Should().HaveCount(grants);

        // A child filed afterwards by a writer that did not secure it (the pre-148 state).
        var late = Guid.NewGuid();
        _fixture.ChildWorld.OrdinaryChild("sprk_event", late, ("sprk_regardingproject", "sprk_project", rootId));

        var completing = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        completing.StatusCode.Should().Be(HttpStatusCode.OK, await completing.Content.ReadAsStringAsync());
        var body = await JsonOf(completing);
        body.GetProperty("childrenOnly").GetBoolean().Should().BeTrue();
        body.GetProperty("children").GetProperty("reowned").GetInt32().Should().Be(1);
        _fixture.ChildWorld.OwnerOf("sprk_event", late).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.SharesOn(late).Keys.Should().Equal(DataversePrincipalRef.User(Creator));
    }

    // ── UNSECURE: children leave isolation before the record's shares go ──────────────────────────────────────────────

    /// <summary>
    /// AC 2 (call order) + owner round 13 item 1: unsecure re-owns every isolated child to the owner the rule gives a child
    /// of an ordinary record (its business unit's team), THEN removes the mirrored child shares, THEN revokes the record's
    /// shares, THEN clears the flag. The SharePoint document location the record's Assign cascaded to the new owner is
    /// placed by the same rule, not left with that user. Decoys untouched.
    /// </summary>
    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task Unsecure_ReownsChildren_ThenRemovesTheirMirror_ThenRevokesTheRecord_ThenClearsTheFlag(string rootTable)
    {
        var rootId = Guid.NewGuid();
        SeedRoot(rootTable, rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily(rootTable, rootId, childrenIsolated: true);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        foreach (var (_, id) in family.Children())
        {
            _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), "ReadAccess,WriteAccess,AppendAccess,AppendToAccess");
            _fixture.SeedShare(id, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        }

        var location = Guid.NewGuid();
        if (rootTable != "sprk_workassignment")
        {
            _fixture.SharePointDocumentReadRefused = false;
            _fixture.SeedCascadeChild(rootId, "sharepointdocumentlocation", location, DataversePrincipalRef.Team(SecureTeam));
        }

        var decoysBefore = OwnersOf(family.Decoys());

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(UnsecureRoute, new { recordType = RecordType(rootTable), recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        foreach (var (table, id) in family.Children())
        {
            _fixture.ChildWorld.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
                "a child of an ordinary record is owned by its business unit's team (task 146's rule)");
            _fixture.SharesOn(id).Should().BeEmpty("its mirrored shares go with the record's");
        }

        if (rootTable != "sprk_workassignment")
            _fixture.OwnerOfCascadeChild(location).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
                "owner round 13 item 1: the cascade row takes the rule's owner, not the record's new owning user");

        _fixture.IsSecureOf(rootId).Should().BeFalse();
        AssertDecoysUntouched(family, decoysBefore);

        // The order, over one sequence: child re-owns → child share removals → record share revokes → flag cleared.
        var childIds = family.Children().Select(c => c.Id).ToHashSet();
        var lastChildReown = _fixture.ChildWorld.OwnerWrites.Where(w => childIds.Contains(w.Id)).Max(w => w.Sequence);
        var childRevokes = _fixture.Revokes.Where(r => childIds.Contains(r.RecordId)).Select(r => r.Sequence).ToList();
        var rootRevokes = _fixture.Revokes.Where(r => r.RecordId == rootId).Select(r => r.Sequence).ToList();
        var flagCleared = _fixture.Updates.Single(u => u.RecordId == rootId && u.Payload.ContainsKey("sprk_issecure")).Sequence;
        childRevokes.Should().NotBeEmpty();
        rootRevokes.Should().NotBeEmpty();
        childRevokes.Min().Should().BeGreaterThan(lastChildReown, "ownership first: a child is never reachable by nobody");
        rootRevokes.Min().Should().BeGreaterThan(childRevokes.Max(), "the record's shares go after its children's");
        flagCleared.Should().BeGreaterThan(rootRevokes.Max(), "the flag is cleared last");

        var children = (await JsonOf(response)).GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        children.GetProperty("reowned").GetInt32().Should().Be(family.Children().Count() + (rootTable == "sprk_workassignment" ? 0 : 1));
    }

    /// <summary>
    /// AC 3 (unsecure): a child that cannot be re-owned stops the transition before the record's shares and flag — 500 with
    /// the reason code and counts; <c>sprk_issecure</c> stays set; the record's sharees keep the isolated child (it is never
    /// reachable by nobody). A second call completes it and clears the flag.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenAChildCannotBeReowned_KeepsTheFlagAndTheRecordsShares_AndASecondCallCompletesIt()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: true);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        foreach (var (_, id) in family.Children())
            _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), "ReadAccess,WriteAccess,AppendAccess,AppendToAccess");
        _fixture.ChildWorld.RefusingOwnerWritesOf(family.DocDirect);
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("childrenRemaining").GetInt32().Should().BeGreaterThan(0);
        _fixture.IsSecureOf(rootId).Should().BeTrue("the flag keeps meaning 'related records may still be isolated'");
        _fixture.Revokes.Should().NotContain(r => r.RecordId == rootId, "the record's shares stay until its children are out");
        _fixture.ChildWorld.OwnerOf("sprk_document", family.DocDirect).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.ShareMaskOf(family.DocDirect, Creator).Should().NotBe(0, "the isolated child keeps a reader");

        _fixture.ChildWorld.ClearOwnerWriteFaults();
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_document", family.DocDirect).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(family.DocDirect).Should().BeEmpty();
        _fixture.IsSecureOf(rootId).Should().BeFalse();
    }

    /// <summary>
    /// A pass whose child re-owns all landed but whose mirrored-share removal failed is incomplete (the record keeps its
    /// shares and flag); the second call finds those children ALREADY in their business unit and still removes the record's
    /// sharees from them — so a resumed unsecure leaves no stale mirror behind.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenAChildsMirrorCannotBeRemoved_IsIncomplete_AndTheSecondCallRemovesIt()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var child = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_event", child, ("sprk_regardingproject", "sprk_project", rootId));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.SeedShare(child, DataversePrincipalRef.User(Creator), "ReadAccess,WriteAccess,AppendAccess,AppendToAccess");
        _fixture.SeedShare(child, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.FailRevokeForPrincipal = Colleague;
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(first)).GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.ChildWorld.OwnerOf("sprk_event", child).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.ShareMaskOf(child, Colleague).Should().NotBe(0);
        _fixture.IsSecureOf(rootId).Should().BeTrue();

        _fixture.FailRevokeForPrincipal = null;
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        _fixture.SharesOn(child).Should().BeEmpty("the resumed pass removes the record's sharees from a child already out");
        _fixture.IsSecureOf(rootId).Should().BeFalse();
    }

    /// <summary>
    /// AC 4 (another root): a child filed under the unsecured record AND under a second, still-secure record stays isolated
    /// — the rule is secure-if-any — and keeps its shares.
    /// </summary>
    [Fact]
    public async Task Unsecure_LeavesAChildOfASecondSecureRecordIsolated()
    {
        var rootId = Guid.NewGuid();
        var otherSecure = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        _fixture.ChildWorld.SecureRoot("sprk_matter", otherSecure);
        var shared = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_document", shared,
            ("sprk_project", "sprk_project", rootId), ("sprk_matter", "sprk_matter", otherSecure));
        _fixture.SeedShare(shared, DataversePrincipalRef.User(Creator), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_document", shared).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == shared);
        _fixture.ShareMaskOf(shared, Creator).Should().NotBe(0);
    }

    /// <summary>
    /// Owner round 11 item 3: a record unsecured before task 148 left its children with the Secure team and their mirrored
    /// shares. Calling unsecure on it again (it answers "already not secure") now COMPLETES that: the children are re-owned to
    /// the business unit's team and every share the Secure-team-owned child carried is removed.
    /// </summary>
    [Fact]
    public async Task Unsecure_OnAnAlreadyUnsecuredRecord_CompletesTheChildrenAnEarlierUnsecureLeftIsolated()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, isSecure: false);
        _fixture.UseChildWorldForRoots();
        var stranded = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_event", stranded, ("sprk_regardingproject", "sprk_project", rootId));
        _fixture.SeedShare(stranded, DataversePrincipalRef.User(Outsider), RecordShareLevels.ViewOnlyRights);
        // An ordinary child owned by a user (a run-as-user row of an ordinary record): not this transition's to move.
        var usersOwn = Guid.NewGuid();
        _fixture.ChildWorld.UserOwnedChild("sprk_document", usersOwn, ("sprk_project", "sprk_project", rootId));
        _fixture.SeedShare(usersOwn, DataversePrincipalRef.User(Outsider), RecordShareLevels.ViewOnlyRights);
        // An ordinary record's SharePoint location on its owning user: no transition, so not moved.
        var location = Guid.NewGuid();
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.SeedCascadeChild(rootId, "sharepointdocumentlocation", location, DataversePrincipalRef.User(Creator));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonOf(response);
        body.GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();
        body.GetProperty("children").GetProperty("reowned").GetInt32().Should().Be(1);
        _fixture.ChildWorld.OwnerOf("sprk_event", stranded).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(stranded).Should().BeEmpty("a former sharee's mirror does not outlive the isolation");
        _fixture.ChildWorld.OwnerOf("sprk_document", usersOwn).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "an ordinary row the rule would hand another ordinary team never crossed the isolation boundary");
        _fixture.ShareMaskOf(usersOwn, Outsider).Should().NotBe(0, "a share on an ordinary row is not the mirror's");
        _fixture.OwnerOfCascadeChild(location).Should().Be(DataversePrincipalRef.User(Creator),
            "a repeat unsecure is no transition for the record's own SharePoint rows");
    }

    /// <summary>
    /// Ownership first, enforced where the shares come off too: the synchronizer refuses to remove the mirrored shares of a
    /// child the Secure team still owns (it would be readable by nobody) — whoever calls it.
    /// </summary>
    [Fact]
    public async Task TheMirrorIsNeverRemovedFromAChildStillIsolated()
    {
        var world = SecureChildShareWorld.Standard();
        var shares = new FakeRecordShareTable();
        var child = Guid.NewGuid();
        world.SecureRoot("sprk_project", Guid.NewGuid()).SecureChild("sprk_event", child);
        shares.Seed("sprk_event", child, DataversePrincipalRef.User(Creator), 1);

        var removal = await world.Synchronizer(shares).RemoveMirrorAsync("sprk_event", child, principals: null, CancellationToken.None);

        removal.IsComplete.Should().BeFalse();
        shares.WriteLog.Should().BeEmpty();
        shares.MaskOf("sprk_event", child, DataversePrincipalRef.User(Creator)).Should().Be(1);
    }

    // ── AUTHORIZATION ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>AC 8: both transitions keep their delegation gate — a caller without Write gets 403 and no child is touched.</summary>
    [Theory]
    [InlineData(ProvisionRoute, false)]
    [InlineData(UnsecureRoute, true)]
    public async Task ACallerWithoutWriteOnTheRecord_Gets403_AndNoChildIsTouched(string route, bool isolated)
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: isolated ? SecureTeam : null);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: isolated);
        var before = OwnersOf(family.Children());
        _fixture.CallerHoldsWrite = false;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(route, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.ChildWorld.OwnerWrites.Should().BeEmpty();
        OwnersOf(family.Children()).Should().Equal(before);
        _fixture.Grants.Should().BeEmpty();
        _fixture.Revokes.Should().BeEmpty();
    }

    // ── THE SWEEP: report-only by default, writes when enabled, resumable ────────────────────────────────────────────

    /// <summary>
    /// AC 7 + AC 6: report-only (the default — no configuration) writes NOTHING and reports every change it would make with the
    /// row's current owner; with writes enabled the same run applies them (read back, previous owners reported); a run after
    /// that reports zero changes.
    /// </summary>
    [Fact]
    public async Task TheSweep_ReportsOnlyByDefault_AppliesWhenEnabled_AndThenFindsNothingToDo()
    {
        var sweep = new SweepHarness();
        var (r1, r2) = (Guid.NewGuid(), Guid.NewGuid());
        var (c1, c2) = (Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", r1).SecureRoot("sprk_workassignment", r2)
            .OrdinaryChild("sprk_document", c1, ("sprk_workassignment", "sprk_workassignment", r1))
            .OrdinaryChild("sprk_event", c2, ("sprk_regardingworkassignment", "sprk_workassignment", r2));
        sweep.Shares.Seed("sprk_workassignment", r1, DataversePrincipalRef.User(Creator), 262167);

        var report = await sweep.RunAsync(writesEnabled: null);

        report.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        report.GetProperty("wouldChange").GetInt32().Should().Be(2);
        report.GetProperty("changed").GetInt32().Should().Be(0);
        sweep.World.OwnerWrites.Should().BeEmpty("report-only writes nothing");
        sweep.Shares.WriteLog.Should().BeEmpty();
        report.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == c1)
            .GetProperty("previousOwner").GetString().Should().Be($"teams({GeneralTeam:D})");

        var applied = await sweep.RunAsync(writesEnabled: "true");

        applied.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        applied.GetProperty("changed").GetInt32().Should().Be(2);
        sweep.World.OwnerOf("sprk_document", c1).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        sweep.World.OwnerOf("sprk_event", c2).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        sweep.Shares.MaskOf("sprk_document", c1, DataversePrincipalRef.User(Creator)).Should().Be(23,
            "the record's sharee is mirrored onto the re-owned child, without Share");
        applied.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == c2)
            .GetProperty("previousOwner").GetString().Should().Be($"teams({GeneralTeam:D})");

        var writes = sweep.World.OwnerWrites.Count;
        var again = await sweep.RunAsync(writesEnabled: "true");

        again.GetProperty("changed").GetInt32().Should().Be(0);
        again.GetProperty("wouldChange").GetInt32().Should().Be(0);
        sweep.World.OwnerWrites.Should().HaveCount(writes, "a second sweep over the same state changes nothing");
    }

    /// <summary>
    /// The per-run cap and the progress log: with a cap of one, the first run reconciles the first secure record (in id
    /// order) and records where it stopped; the next run continues after it and reports the pass complete.
    /// </summary>
    [Fact]
    public async Task TheSweep_IsCappedPerRun_AndTheNextRunResumesAfterTheLastRecord()
    {
        var sweep = new SweepHarness();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }.OrderBy(g => g).ToArray();
        foreach (var id in ids)
            sweep.World.SecureRoot("sprk_workassignment", id)
                .OrdinaryChild("sprk_event", Guid.NewGuid(), ("sprk_regardingworkassignment", "sprk_workassignment", id));

        var first = await sweep.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        first.GetProperty("rootsInRun").GetInt32().Should().Be(1);
        first.GetProperty("passComplete").GetBoolean().Should().BeFalse();
        first.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{ids[0]:D}");

        var second = await sweep.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        second.GetProperty("rootsInRun").GetInt32().Should().Be(1);
        second.GetProperty("passComplete").GetBoolean().Should().BeTrue();
        second.GetProperty("resumeAfter").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("changed").GetInt32().Should().Be(1, "the second run reconciled the SECOND record, not the first again");
    }

    /// <summary>
    /// The sweep over a record flagged secure but not isolated (a failed provisioning, or an unsecure interrupted after its
    /// move) never hands its children an ordinary owner: the rule refuses, the run reports the record incomplete, and nothing
    /// is written.
    /// </summary>
    [Fact]
    public async Task TheSweep_OverAFlaggedButNotIsolatedRecord_RefusesItsChildren_AndWritesNothing()
    {
        var sweep = new SweepHarness();
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        sweep.World.FlaggedNotIsolatedRoot("sprk_workassignment", root)
            .OrdinaryChild("sprk_event", child, ("sprk_regardingworkassignment", "sprk_workassignment", root));

        var report = await sweep.RunAsync(writesEnabled: "true");

        report.GetProperty("refused").GetInt32().Should().Be(1);
        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(1);
        sweep.World.OwnerWrites.Should().BeEmpty();
        sweep.LastResult!.Success.Should().BeFalse();
    }

    /// <summary>
    /// The sweep job over a world: ONE job instance (it keeps its progress cursor, as the singleton the scheduler holds), a
    /// real reconciler resolved per run from a scope, and the writes switch and cap read from configuration per run.
    /// </summary>
    private sealed class SweepHarness
    {
        public SecureChildShareWorld World { get; } = SecureChildShareWorld.Standard();
        public FakeRecordShareTable Shares { get; } = new();
        public JobRunResult? LastResult { get; private set; }
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly ServiceProvider _provider;
        private readonly SecureChildReconciliationJob _job;

        public SweepHarness()
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => World).Object);
            // Work assignments cascade nothing on Assign, so the platform-cascade client is never called here.
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => World, Shares, webApi: null!));
            _provider = services.BuildServiceProvider();
            _job = new SecureChildReconciliationJob(
                _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        public async Task<JsonElement> RunAsync(string? writesEnabled, string? maxRootsPerRun = null)
        {
            _configuration[SecureChildReconciliationJob.WritesEnabledConfigKey] = writesEnabled;
            _configuration[SecureChildReconciliationJob.MaxRootsPerRunConfigKey] = maxRootsPerRun;

            LastResult = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);

            using var doc = JsonDocument.Parse(LastResult.ResultJson!);
            return doc.RootElement.Clone();
        }
    }
}
