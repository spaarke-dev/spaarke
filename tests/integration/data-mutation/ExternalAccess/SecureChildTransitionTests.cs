using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Ai.Membership;
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
        // A ROOT of its own reachable from the record — filed under the record's EVENT. Task 158 (owner round 6) secures a
        // work assignment filed DIRECTLY under a secure matter or project (its own provisioning; SecureRootInheritanceTests),
        // so this decoy is filed through an intermediate, which neither the child pass (roots are never walked) nor the
        // round-6 rule (a matter or project parent only) touches. Before task 158 it named the project directly.
        world.Add("sprk_workassignment", family.ChildRoot,
            ("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", GeneralTeam)), ("sprk_issecure", true),
            ("sprk_regardingevent", new Microsoft.Xrm.Sdk.EntityReference("sprk_event", family.Event)));
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
                "a child's sharees are exactly the record's, at the record's rights (Share included since round 91)");
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

    /// <summary>
    /// AC 3 + ADR-003 on the already-provisioned branch (task 148 r2, verifier item 2): a repeat call on a PROVISIONED record
    /// whose child pass cannot complete answers <c>children_incomplete</c> with counts — never 409 <c>already_provisioned</c>
    /// (nothing written) and never 200 <c>childrenOnly</c> (something written), both of which a client reads as "done".
    /// The refused child keeps its owner and gets no share; a third call, the fault cleared, completes it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provisioning_RepeatedOnAProvisionedRecord_WhoseChildPassIsIncomplete_IsNeverASuccess(bool anotherChildMoves)
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        SeedFamily("sprk_project", rootId, childrenIsolated: false);
        var client = _fixture.CreateAuthenticatedClient();
        (await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Children filed after provisioning by a writer that did not secure them; Dataverse refuses one re-own.
        var stuck = Guid.NewGuid();
        _fixture.ChildWorld.OrdinaryChild("sprk_event", stuck, ("sprk_regardingproject", "sprk_project", rootId))
            .RefusingOwnerWritesOf(stuck);
        var moves = Guid.NewGuid();
        if (anotherChildMoves)
            _fixture.ChildWorld.OrdinaryChild("sprk_memo", moves, ("sprk_regardingproject", "sprk_project", rootId));

        var again = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        again.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await again.Content.ReadAsStringAsync());
        var problem = await JsonOf(again);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete,
            anotherChildMoves
                ? "a pass that wrote something but did not finish is not '200 childrenOnly'"
                : "a pass that wrote nothing because it could not is not '409 already provisioned'");
        problem.GetProperty("childrenReowned").GetInt32().Should().Be(anotherChildMoves ? 1 : 0);
        problem.GetProperty("childrenRemaining").GetInt32().Should().BeGreaterThan(0);
        _fixture.ChildWorld.OwnerOf("sprk_event", stuck).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(stuck).Should().BeEmpty("a child that did not move is not mirrored");
        if (anotherChildMoves)
            _fixture.ChildWorld.OwnerOf("sprk_memo", moves).Should().Be(DataversePrincipalRef.Team(SecureTeam));

        _fixture.ChildWorld.ClearOwnerWriteFaults();
        var completing = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        completing.StatusCode.Should().Be(HttpStatusCode.OK, await completing.Content.ReadAsStringAsync());
        (await JsonOf(completing)).GetProperty("childrenOnly").GetBoolean().Should().BeTrue();
        _fixture.ChildWorld.OwnerOf("sprk_event", stuck).Should().Be(DataversePrincipalRef.Team(SecureTeam));
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
    /// Batch 4 integration, task 132 (C12) x task 148: the record's owner moved in Step 3, so an unsecure whose child pass
    /// is incomplete (children_incomplete, flag and shares kept) still evicts the record's owner change, exactly once —
    /// the colleagues of the new owner's business unit gain the record by ownership whether or not the children finished.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenTheChildPassIsIncomplete_StillEvictsTheRecordsOwnerChange()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: true);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.ChildWorld.RefusingOwnerWritesOf(family.DocDirect);
        var recorder = new OwnerChangeRecorder();
        using var host = _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(recorder);
        }));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await JsonOf(response)).GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.IsSecureOf(rootId).Should().BeTrue("precondition: the pass stopped before the flag was cleared");
        recorder.OwnerChanges.Where(c => c.RecordId == rootId).Should().Equal(new[] { ("sprk_project", "sprk_projects", rootId) },
            "the record's own owner change is evicted exactly once");
        recorder.OwnerChanges.Should().Contain(("sprk_document", "sprk_documents", family.DocDirect),
            "148 × 132: the refused child re-own was ATTEMPTED, and a write that reports failure can have committed");
        _fixture.ChildWorld.ClearOwnerWriteFaults();
    }

    // ── Batch-4 integration, 148 × 132: every child OWNER change is evicted through the ONE hook ───────────────────────

    /// <summary>A host whose owner-change hook is <paramref name="recorder"/> (the module boundary), signed in.</summary>
    private HttpClient ClientRecording(OwnerChangeRecorder recorder, out IDisposable host)
    {
        var factory = _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(recorder);
        }));
        host = factory;
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");
        return client;
    }

    private static (string, string, Guid) Evicted(string table, Guid id) =>
        (table, Sprk.Bff.Api.Services.Access.SecureChildLineage.Children[table].EntitySet, id);

    [Fact(DisplayName = "148×132: provisioning's child pass evicts every child it re-owns into isolation")]
    public async Task Provisioning_EvictsEveryChildItReowns()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: false);
        var recorder = new OwnerChangeRecorder();
        var client = ClientRecording(recorder, out var host);
        using var _ = host;

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        recorder.OwnerChanges.Should().Contain(family.Children().Select(c => Evicted(c.Table, c.Id)),
            "each child the pass moved onto the Secure team changed owner");
        recorder.OwnerChanges.Should().Contain(("sprk_project", "sprk_projects", rootId), "the record's own move is evicted too");
    }

    [Fact(DisplayName = "148×132: the unsecure's Step 3.5 evicts every child it re-owns out of isolation")]
    public async Task Unsecure_EvictsEveryChildItReowns()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: true);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        var recorder = new OwnerChangeRecorder();
        var client = ClientRecording(recorder, out var host);
        using var _ = host;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        recorder.OwnerChanges.Should().Contain(family.Children().Select(c => Evicted(c.Table, c.Id)),
            "each child the pass moved off the Secure team changed owner");
    }

    [Fact(DisplayName = "148×132: the unsecure-completion branch evicts the stranded child it re-owns")]
    public async Task UnsecureCompletion_EvictsTheStrandedChildItReowns()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, isSecure: false);
        _fixture.UseChildWorldForRoots();
        var stranded = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_event", stranded, ("sprk_regardingproject", "sprk_project", rootId));
        var recorder = new OwnerChangeRecorder();
        var client = ClientRecording(recorder, out var host);
        using var _ = host;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_event", stranded).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        recorder.OwnerChanges.Should().Equal(new[] { Evicted("sprk_event", stranded) },
            "the stranded child is the only owner change: the record itself was already ordinary");
    }

    [Fact(DisplayName = "148×132: SecureChildReconciliationJob evicts every child it re-owns, and a report-only run evicts nothing")]
    public async Task TheSweep_EvictsEveryChildItReowns_AndAReportOnlyRunEvictsNothing()
    {
        var recorder = new OwnerChangeRecorder();
        var sweep = new SweepHarness(world: null, shares: null, webApi: null, accessCacheInvalidator: recorder);
        var root = Guid.NewGuid();
        var (c1, c2) = (Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root)
            .OrdinaryChild("sprk_document", c1, ("sprk_workassignment", "sprk_workassignment", root))
            .OrdinaryChild("sprk_event", c2, ("sprk_regardingworkassignment", "sprk_workassignment", root));

        (await sweep.RunAsync(writesEnabled: null)).GetProperty("wouldChange").GetInt32().Should().Be(2);
        recorder.OwnerChanges.Should().BeEmpty("report-only writes no owner, so nothing is stale");

        (await sweep.RunAsync(writesEnabled: "true")).GetProperty("changed").GetInt32().Should().Be(2);
        recorder.OwnerChanges.Should().BeEquivalentTo(new[] { Evicted("sprk_document", c1), Evicted("sprk_event", c2) });
    }

    /// <summary>Records every owner-change eviction (the hook at its module boundary).</summary>
    private sealed class OwnerChangeRecorder : IMembershipCacheInvalidator
    {
        public ConcurrentQueue<(string Entity, string EntitySet, Guid RecordId)> OwnerChanges { get; } = new();

        public Task PublishInvalidationAsync(Guid personId, string entityLogicalName, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRecordOwnerChangeAsync(
            string entityLogicalName, string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
        {
            OwnerChanges.Enqueue((entityLogicalName, entitySetName, recordId));
            return Task.CompletedTask;
        }

        public Task InvalidateRecordShareChangeAsync(string entitySetName, Guid recordId, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// A pass whose child re-own landed but whose mirrored-share removal failed is incomplete (the record keeps its shares and
    /// flag), and the child is put BACK on the Secure team — so the second call, which (owner round 22) only removes shares
    /// from a child it finds isolated, still finds it isolated, moves it out again and removes the rest of its mirror. Left
    /// out of isolation with its mirror, the next pass would read it as a never-isolated child and keep its former sharees on
    /// it after the record's own shares were gone.
    /// </summary>
    [Fact]
    public async Task Unsecure_WhenAChildsMirrorCannotBeRemoved_IsIncomplete_PutsItBack_AndTheSecondCallRemovesIt()
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
        _fixture.ChildWorld.OwnerWrites.Where(w => w.Id == child).Select(w => w.Owner).Should().Equal(
            new[] { DataversePrincipalRef.Team(GeneralTeam), DataversePrincipalRef.Team(SecureTeam) },
            "moved out first (ownership before shares), then put back when its mirror could not all be removed");
        _fixture.ChildWorld.OwnerOf("sprk_event", child).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "isolated again with part of its mirror — an under-share, never a business-unit row carrying former sharees");
        _fixture.ShareMaskOf(child, Colleague).Should().NotBe(0);
        _fixture.IsSecureOf(rootId).Should().BeTrue();
        _fixture.Revokes.Should().NotContain(r => r.RecordId == rootId);

        _fixture.FailRevokeForPrincipal = null;
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_event", child).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(child).Should().BeEmpty("the resumed pass finds the child isolated and removes the rest of its mirror");
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
    /// AC 2 + AC 5 (task 148 r1, verifier item 1): a STAMPED grandchild — filed under the record by its FR-26 stamp AND under
    /// one of the record's other children — sits at that child's level, and its table can sort BEFORE the child's. Decided
    /// first, it read that parent still on the Secure team (secure-if-any) and stayed isolated with its mirror while the call
    /// answered Completed, revoked the record's shares and cleared the flag: a former sharee kept a child the record no
    /// longer gave them, and its business unit could not read it. Leaving isolation now repeats to a fixpoint, so the
    /// grandchild follows its parent out and loses its mirror before the record's shares go.
    /// </summary>
    [Theory]
    [InlineData("sprk_analysis", "sprk_documentid", "sprk_document")]
    [InlineData("sprk_communication", "sprk_regardingevent", "sprk_event")]
    [InlineData("sprk_agreement", "sprk_regardingdocument", "sprk_document")]
    [InlineData("sprk_analysis", "sprk_regardingcommunication", "sprk_communication")]
    [InlineData("sprk_analysis", "sprk_regardinginvoice", "sprk_invoice")]
    public async Task Unsecure_ReleasesAStampedGrandchild_WhoseTableSortsBeforeItsParents(
        string grandchildTable, string parentLookup, string parentTable)
    {
        string.CompareOrdinal(grandchildTable, parentTable).Should().BeNegative("the shape under test: the grandchild is decided first");
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var parent = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var parentToRoot = parentTable is "sprk_document" or "sprk_invoice" ? "sprk_project" : "sprk_regardingproject";
        _fixture.ChildWorld
            .SecureChild(parentTable, parent, (parentToRoot, "sprk_project", rootId))
            .SecureChild(grandchildTable, grandchild,
                (parentLookup, parentTable, parent), ("sprk_regardingproject", "sprk_project", rootId));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        foreach (var id in new[] { parent, grandchild })
        {
            _fixture.SeedShare(id, DataversePrincipalRef.User(Creator), "ReadAccess,WriteAccess,AppendAccess,AppendToAccess");
            _fixture.SeedShare(id, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        }

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        foreach (var (table, id) in new[] { (parentTable, parent), (grandchildTable, grandchild) })
        {
            _fixture.ChildWorld.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
                $"{table} {id} is a child of a record that is no longer secure");
            _fixture.SharesOn(id).Should().BeEmpty($"{table} {id} keeps none of the record's former sharees");
        }

        var children = (await JsonOf(response)).GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        children.GetProperty("reowned").GetInt32().Should().Be(2);
        children.GetProperty("tables").EnumerateArray().Should().OnlyContain(t => t.GetProperty("alreadyCorrect").GetInt32() == 0,
            "no related record is reported 'already correct' while it is still isolated");
        _fixture.IsSecureOf(rootId).Should().BeFalse();

        var grandchildRevokes = _fixture.Revokes.Where(r => r.RecordId == grandchild).Select(r => r.Sequence).ToList();
        grandchildRevokes.Should().NotBeEmpty();
        _fixture.Revokes.Where(r => r.RecordId == rootId).Min(r => r.Sequence).Should().BeGreaterThan(grandchildRevokes.Max(),
            "the record's shares go only after the grandchild's mirror");
    }

    /// <summary>
    /// The fixpoint follows the resolver's look-through: an analysis filed under a USER-owned communication (a run-as-user
    /// message — the resolver reads it through to its own filing) which is filed under an isolated event is kept isolated by
    /// that event. The event moves after the analysis was decided, and the communication itself never moves (it is not
    /// isolated) — the analysis must still be decided again, and leave.
    /// </summary>
    [Fact]
    public async Task Unsecure_ReleasesARowKeptIsolatedThroughAUserOwnedParent()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var (@event, message, analysis) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.ChildWorld
            .SecureChild("sprk_event", @event, ("sprk_regardingproject", "sprk_project", rootId))
            .UserOwnedChild("sprk_communication", message,
                ("sprk_regardingevent", "sprk_event", @event), ("sprk_regardingproject", "sprk_project", rootId))
            .SecureChild("sprk_analysis", analysis,
                ("sprk_regardingcommunication", "sprk_communication", message), ("sprk_regardingproject", "sprk_project", rootId));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.SeedShare(analysis, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_analysis", analysis).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(analysis).Should().BeEmpty();
        _fixture.ChildWorld.OwnerOf("sprk_communication", message).Should().Be(
            DataversePrincipalRef.User(SecureChildShareWorld.SomeUser), "a row that was never isolated is not moved");
        _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == message,
            "it is never pulled INTO isolation behind an event that is about to leave it (moves out are made first)");
        (await JsonOf(response)).GetProperty("children").GetProperty("status").GetString().Should().Be("Completed");
    }

    /// <summary>
    /// A move OUT of isolation widens access, so it is made only once it is certain: an isolated memo filed under an
    /// ordinary document that is itself moving IN (it is also filed under a second secure record) is not released while
    /// that document is on its way — released and then pulled back, its business unit would have read it in between.
    /// </summary>
    [Fact]
    public async Task Unsecure_NeverReleasesARowWhoseSupportIsMovingIn()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var (otherSecure, document, memo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.ChildWorld
            .SecureRoot("sprk_matter", otherSecure)
            .OrdinaryChild("sprk_document", document, ("sprk_project", "sprk_project", rootId), ("sprk_matter", "sprk_matter", otherSecure))
            .SecureChild("sprk_memo", memo, ("sprk_regardingdocument", "sprk_document", document));
        _fixture.SeedShare(memo, DataversePrincipalRef.User(Creator), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "also filed under a second secure record (secure-if-any)");
        _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == memo, "the memo never leaves isolation, not even briefly");
        _fixture.ChildWorld.OwnerOf("sprk_memo", memo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.Revokes.Should().NotContain(r => r.RecordId == memo);
    }

    /// <summary>
    /// A row the pass pulls INTO isolation (it rested on an isolated row) and whose support then leaves goes back to the
    /// owner it had when the pass began — not to a team the rule picks for an ordinary row, and with every share it had.
    /// Shape (unsecure): a user-owned document also filed under a SECOND secure record moves in; an isolated event filed
    /// only under an analysis the rule refuses (it is also filed under a record flagged secure but not isolated) waits for
    /// that document, then leaves; the ordinary memo filed under the event was pulled in behind it, and is put back.
    /// </summary>
    [Fact]
    public async Task Unsecure_PutsARowItPulledIntoIsolationBackOnItsOwner_WhenItsSupportLeaves()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var (otherSecure, halfProvisioned) = (Guid.NewGuid(), Guid.NewGuid());
        var (document, analysis, @event, memo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.ChildWorld
            .SecureRoot("sprk_matter", otherSecure)
            .FlaggedNotIsolatedRoot("sprk_project", halfProvisioned)
            .UserOwnedChild("sprk_document", document, ("sprk_project", "sprk_project", rootId), ("sprk_matter", "sprk_matter", otherSecure))
            .OrdinaryChild("sprk_analysis", analysis,
                ("sprk_documentid", "sprk_document", document), ("sprk_regardingproject", "sprk_project", halfProvisioned))
            .SecureChild("sprk_event", @event, ("sprk_regardinganalysis", "sprk_analysis", analysis))
            .UserOwnedChild("sprk_memo", memo, ("sprk_regardingevent", "sprk_event", @event));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.SeedShare(memo, DataversePrincipalRef.User(Outsider), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "the refused analysis leaves the pass incomplete");
        _fixture.ChildWorld.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "it is also filed under a second secure record (secure-if-any)");
        _fixture.ChildWorld.OwnerOf("sprk_event", @event).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.ChildWorld.OwnerWrites.Where(w => w.Id == memo).Select(w => w.Owner).Should().Equal(
            new[] { DataversePrincipalRef.Team(SecureTeam), DataversePrincipalRef.User(SecureChildShareWorld.SomeUser) },
            "pulled in behind the isolated event, then put back on its own owner (its user — not the business unit's " +
            "team the rule would give an ordinary row) once the event left");
        _fixture.ShareMaskOf(memo, Outsider).Should().NotBe(0, "it was never isolated, so its share is its user's (round 22)");
        _fixture.IsSecureOf(rootId).Should().BeTrue();
    }

    /// <summary>
    /// Owner round 22 (verifier item 8): during an unsecure, a share is removed only from a child the Secure team owned when
    /// the pass began (its shares are task 149's mirror). A child of the record that was never isolated — a user-owned
    /// document someone shared from the model-driven app — keeps that share, even one held by the record's own sharee.
    /// </summary>
    [Fact]
    public async Task Unsecure_KeepsTheSharesOfAChildThatWasNeverIsolated_EvenOneOfTheRecordsSharees()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var isolated = Guid.NewGuid();
        var neverIsolated = Guid.NewGuid();
        _fixture.ChildWorld
            .SecureChild("sprk_event", isolated, ("sprk_regardingproject", "sprk_project", rootId))
            .UserOwnedChild("sprk_document", neverIsolated, ("sprk_project", "sprk_project", rootId));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.SeedShare(isolated, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);
        _fixture.SeedShare(neverIsolated, DataversePrincipalRef.User(Colleague), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_event", isolated).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.SharesOn(isolated).Should().BeEmpty("it was isolated, so its share was the mirror");
        _fixture.ChildWorld.OwnerOf("sprk_document", neverIsolated).Should().Be(
            DataversePrincipalRef.User(SecureChildShareWorld.SomeUser), "an ordinary row is not this transition's to move");
        _fixture.ShareMaskOf(neverIsolated, Colleague).Should().NotBe(0,
            "a share on a child that was never isolated is its user's own intent (owner round 22)");
        _fixture.Revokes.Should().NotContain(r => r.RecordId == neverIsolated);
    }

    /// <summary>
    /// AC 3 (ADR-003), the unsecure Step 2.5 refusal: when the rows the record's ownership move cascades to (its SharePoint
    /// document locations) cannot be read, nothing is changed — the record stays with the Secure team, keeps its shares and
    /// its flag, no related record moves — and the answer names the state (transient vs. a refusal an administrator fixes).
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unreadable")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    public async Task Unsecure_WhenTheRowsItsMoveCascadesToCannotBeRead_RefusesBeforeAnyWrite(HttpStatusCode status, string state)
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var family = SeedFamily("sprk_project", rootId, childrenIsolated: true);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.CascadeChildSnapshotReadFailsWith = status;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonCascadeChildrenUnreadable);
        problem.GetProperty("cascadeChildState").GetString().Should().Be(state);
        _fixture.Updates.Should().NotContain(u => u.RecordId == rootId, "the record is not moved");
        _fixture.OwningTeamOf(rootId).Should().Be(SecureTeam);
        _fixture.IsSecureOf(rootId).Should().BeTrue();
        _fixture.ChildWorld.OwnerWrites.Should().BeEmpty();
        _fixture.Revokes.Should().BeEmpty();
        foreach (var (table, id) in family.Children())
            _fixture.ChildWorld.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// AC 3 (ADR-003) for both transitions: a pass that cannot READ the record's related records decides nothing and is never
    /// a success — Failed, so unsecure stops before the record's shares and flag (they stay), and provisioning answers
    /// children_incomplete. No related record is written.
    /// </summary>
    [Theory]
    [InlineData(UnsecureRoute)]
    [InlineData(ProvisionRoute)]
    public async Task ATransitionWhoseRelatedRecordsCannotBeRead_IsFailed_NeverASuccess(string route)
    {
        var unsecure = route == UnsecureRoute;
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: unsecure ? SecureTeam : null);
        _fixture.UseChildWorldForRoots();
        SeedFamily("sprk_project", rootId, childrenIsolated: unsecure);
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        _fixture.ChildWorld.FailingQueriesOf("sprk_event");

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(route, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(
            unsecure ? UnsecureProjectEndpoint.ReasonChildrenIncomplete : ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("detail").GetString().Should().Contain("related records could not be read");
        _fixture.ChildWorld.OwnerWrites.Should().BeEmpty("nothing is decided from a partial read");
        if (unsecure)
        {
            _fixture.IsSecureOf(rootId).Should().BeTrue("the flag keeps meaning 'related records may still be isolated'");
            _fixture.Revokes.Should().NotContain(r => r.RecordId == rootId);
        }
    }

    /// <summary>
    /// AC 3 on the already-not-secure branch (task 148 completes what a pre-148 unsecure left isolated): a child it cannot
    /// re-own makes that call children_incomplete too — never "already unsecure", 200.
    /// </summary>
    [Fact]
    public async Task Unsecure_OnAnAlreadyUnsecuredRecord_WhoseStrandedChildCannotBeReowned_IsIncomplete()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, isSecure: false);
        _fixture.UseChildWorldForRoots();
        var stranded = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_event", stranded, ("sprk_regardingproject", "sprk_project", rootId))
            .RefusingOwnerWritesOf(stranded);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("childrenRemaining").GetInt32().Should().Be(1);
        _fixture.ChildWorld.OwnerOf("sprk_event", stranded).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// AC 3: the reconciler's own read of the rows the record's Assign cascades to (its SharePoint document locations) is
    /// part of the pass — when it fails, the pass is incomplete, never Completed. Shown on the already-not-secure branch,
    /// the one trigger whose only read of those rows is the reconciler's.
    /// </summary>
    [Fact]
    public async Task Unsecure_OnAnAlreadyUnsecuredRecord_WhoseCascadeRowsCannotBeRead_IsIncomplete()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, isSecure: false);
        _fixture.UseChildWorldForRoots();
        _fixture.CascadeChildSnapshotReadFailsWith = HttpStatusCode.ServiceUnavailable;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(UnsecureRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty("childTables").EnumerateArray()
            .Single(t => t.GetProperty("table").GetString() == "sharepointdocumentlocation")
            .GetProperty("failed").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The exemption the resolver gives a record mid-unsecure is only for a record whose move OFF the Secure team has
    /// landed: asked to take a record's children out of isolation while the record itself still reads as isolated, the
    /// reconciler refuses (Failed) and writes nothing.
    /// </summary>
    [Fact]
    public async Task TheReconciler_RefusesToUnsecureTheChildrenOfARecordStillIsolated()
    {
        var world = SecureChildShareWorld.Standard();
        var shares = new FakeRecordShareTable();
        var (root, child) = (Guid.NewGuid(), Guid.NewGuid());
        world.SecureRoot("sprk_workassignment", root)
            .SecureChild("sprk_event", child, ("sprk_regardingworkassignment", "sprk_workassignment", root));
        shares.Seed("sprk_event", child, DataversePrincipalRef.User(Creator), 23);

        var report = await SecureChildShareWorld.ReconcilerOver(() => world, shares, webApi: null!)
            .ReconcileAsync("sprk_workassignment", root, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Failed);
        report.IsComplete.Should().BeFalse();
        world.OwnerWrites.Should().BeEmpty();
        shares.WriteLog.Should().BeEmpty();
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

        var removal = await world.Synchronizer(shares).RemoveMirrorAsync("sprk_event", child, CancellationToken.None);

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
        sweep.Shares.MaskOf("sprk_document", c1, DataversePrincipalRef.User(Creator)).Should().Be(262167,
            "the record's sharee is mirrored onto the re-owned child at the record's rights, Share included (round 91)");
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
        first.GetProperty("startPosition").GetInt32().Should().Be(1, "the first run of a pass begins at the first secure record");
        first.GetProperty("passComplete").GetBoolean().Should().BeFalse();
        first.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{ids[0]:D}");

        var second = await sweep.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        second.GetProperty("rootsInRun").GetInt32().Should().Be(1);
        second.GetProperty("startPosition").GetInt32().Should().Be(2,
            "a run that resumes says where it began, so a reader can tell a whole pass from a tail (the backfill's -Verify)");
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
    /// AC 7 for the roots whose Assign cascades (task 148 r1, verifier item 2 V3): the sweep's default, report-only mode over a
    /// secure PROJECT or MATTER writes nothing — no owner, no share, and not the SharePoint document location its Assign
    /// cascades to — and still lists every change it would make, that location included, with its current owner.
    /// </summary>
    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    public async Task TheSweep_ReportOnly_OverAProjectOrMatter_WritesNothing_NotEvenItsCascadeRows(string rootTable)
    {
        var rootId = Guid.NewGuid();
        SeedRoot(rootTable, rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        var (document, @event) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.ChildWorld
            .OrdinaryChild("sprk_document", document, (rootTable, rootTable, rootId))
            .OrdinaryChild("sprk_event", @event, (RegardingColumn(rootTable), rootTable, rootId));
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        var location = Guid.NewGuid();
        _fixture.SharePointDocumentReadRefused = false;
        _fixture.SeedCascadeChild(rootId, "sharepointdocumentlocation", location, DataversePrincipalRef.Team(GeneralTeam));
        var sweep = new SweepHarness(
            () => _fixture.ChildWorld,
            _fixture.Services.GetRequiredService<IDataverseRecordShareService>(),
            _fixture.Services.GetRequiredService<DataverseWebApiClient>());

        var report = await sweep.RunAsync(writesEnabled: null);

        report.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        report.GetProperty("rootsTotal").GetInt32().Should().Be(1);
        report.GetProperty("wouldChange").GetInt32().Should().Be(3, "the document, the event and the cascade location");
        report.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == location)
            .GetProperty("previousOwner").GetString().Should().Be($"teams({GeneralTeam:D})");
        _fixture.Updates.Should().BeEmpty("report-only writes no row through the Web API — not the cascade location either");
        _fixture.OwnerOfCascadeChild(location).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _fixture.ChildWorld.OwnerWrites.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
        _fixture.Revokes.Should().BeEmpty();
        _fixture.Modifies.Should().BeEmpty();
    }

    /// <summary>
    /// AC 7: the sweep runs over the records flagged <c>sprk_issecure = true</c> only — an ordinary record is not one of them.
    /// </summary>
    [Fact]
    public async Task TheSweep_ListsOnlyTheRecordsFlaggedSecure()
    {
        var sweep = new SweepHarness();
        var (secure, ordinary) = (Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", secure).OrdinaryRoot("sprk_workassignment", ordinary)
            .OrdinaryChild("sprk_event", Guid.NewGuid(), ("sprk_regardingworkassignment", "sprk_workassignment", secure))
            .OrdinaryChild("sprk_event", Guid.NewGuid(), ("sprk_regardingworkassignment", "sprk_workassignment", ordinary));

        var report = await sweep.RunAsync(writesEnabled: null);

        report.GetProperty("rootsTotal").GetInt32().Should().Be(1);
        report.GetProperty("examined").GetInt32().Should().Be(1, "only the secure record's child is examined");
    }

    /// <summary>
    /// AC 3 + AC 7 (verifier item 2 V9): a related record whose owner cannot be decided because a read FAILED is a failure of
    /// the run — counted failed, the record incomplete, the run unsuccessful — never "untouched" and never written.
    /// </summary>
    [Fact]
    public async Task TheSweep_WhenARowsOwnerCannotBeDecided_CountsItFailed_AndTheRunIsNotASuccess()
    {
        var sweep = new SweepHarness();
        var (root, otherProject, otherEvent, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root).OrdinaryRoot("sprk_project", otherProject)
            .OrdinaryChild("sprk_event", otherEvent, ("sprk_regardingproject", "sprk_project", otherProject))
            .OrdinaryChild("sprk_todo", todo,
                ("sprk_regardingworkassignment", "sprk_workassignment", root), ("sprk_regardingevent", "sprk_event", otherEvent))
            .FailingRowReadsOf("sprk_event", otherEvent);

        var report = await sweep.RunAsync(writesEnabled: "true");

        report.GetProperty("failed").GetInt32().Should().Be(1);
        report.GetProperty("untouched").GetInt32().Should().Be(0);
        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(1);
        report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo)
            .GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.Failed));
        sweep.LastResult!.Success.Should().BeFalse();
        sweep.World.OwnerWrites.Should().BeEmpty();
    }

    /// <summary>
    /// AC 3 + AC 7 (verifier item 2 V10): a record the sweep cannot read — or an environment whose Secure Record owner team
    /// cannot be read — is Failed: reported incomplete, the run unsuccessful, nothing written. Never counted complete.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSweep_WhenTheRecordOrTheSecureTeamCannotBeRead_IsFailed_AndWritesNothing(bool teamUnreadable)
    {
        var sweep = new SweepHarness();
        var (root, child) = (Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root)
            .OrdinaryChild("sprk_event", child, ("sprk_regardingworkassignment", "sprk_workassignment", root));
        if (teamUnreadable)
            sweep.World.FailingQueriesOf("team");
        else
            sweep.World.FailingRowReadsOf("sprk_workassignment", root);

        var report = await sweep.RunAsync(writesEnabled: "true");

        report.GetProperty("incompleteRoots").EnumerateArray().Single().GetString().Should()
            .Contain(nameof(SecureChildReconcileStatus.Failed));
        sweep.LastResult!.Success.Should().BeFalse();
        sweep.World.OwnerWrites.Should().BeEmpty();
        sweep.Shares.WriteLog.Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 24 item 2 (task 148 r2; supersedes r1 verifier item 9): the sweep NEVER releases an isolated row to its
    /// business unit. A to-do of a secure record that the rule would hand an ordinary team (it is filed only under a document
    /// of an ordinary project, reached from the record through that document's current version) stays on the Secure team
    /// with its shares; it is reported needs-f3 — only an unsecure, an F3 holder's act, releases it — and the run is still a
    /// success (no repeat of the sweep could ever move it, so it is not work left undone).
    /// </summary>
    [Fact]
    public async Task TheSweep_NeverReleasesAnIsolatedRow_ItReportsItNeedsF3_AndKeepsItsShares()
    {
        var sweep = new SweepHarness();
        var (root, recordsDocument, version, ordinaryProject, ordinaryDocument, todo) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root).OrdinaryRoot("sprk_project", ordinaryProject)
            .SecureChild("sprk_document", recordsDocument, ("sprk_workassignment", "sprk_workassignment", root))
            .SecureChild("sprk_fileversion", version, ("sprk_document", "sprk_document", recordsDocument))
            .OrdinaryChild("sprk_document", ordinaryDocument,
                ("sprk_currentversionid", "sprk_fileversion", version), ("sprk_project", "sprk_project", ordinaryProject))
            .SecureChild("sprk_todo", todo, ("sprk_regardingdocument", "sprk_document", ordinaryDocument));
        sweep.Shares.Seed("sprk_workassignment", root, DataversePrincipalRef.User(Creator), 262167);
        sweep.Shares.Seed("sprk_todo", todo, DataversePrincipalRef.User(Creator), 23);

        var report = await sweep.RunAsync(writesEnabled: "true");

        sweep.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "only an unsecure releases an isolated child (owner round 24)");
        sweep.World.OwnerWrites.Should().NotContain(w => w.Id == todo);
        sweep.Shares.MaskOf("sprk_todo", todo, DataversePrincipalRef.User(Creator)).Should().Be(23, "held as it was");
        report.GetProperty("needsF3").GetInt32().Should().Be(1);
        report.GetProperty("changed").GetInt32().Should().Be(0);
        report.GetProperty("mirrorsRevoked").GetInt32().Should().Be(0);
        var listed = report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo);
        listed.GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.NeedsF3));
        listed.GetProperty("previousOwner").GetString().Should().Be($"teams({SecureChildShareWorld.SecureTeam:D})");
        listed.GetProperty("targetTeam").GetGuid().Should().Be(GeneralTeam, "the team an unsecure would give it");
        sweep.LastResult!.Success.Should().BeTrue();

        // The dry run reports it the same way and plans no move for it.
        var plan = await sweep.RunAsync(writesEnabled: null);
        plan.GetProperty("needsF3").GetInt32().Should().Be(1);
        plan.GetProperty("wouldChange").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Owner round 24 item 2 for provisioning (Write-gated): provisioning a record never releases an isolated row it reaches
    /// either — a to-do on the Secure team that the rule would hand an ordinary team (filed only under another, ordinary
    /// record's document, reached through the record's document's version) stays isolated with its shares, the record is
    /// provisioned (200), and the response counts it under <c>needsF3</c>.
    /// </summary>
    [Fact]
    public async Task Provisioning_NeverReleasesAnIsolatedRow_ItReportsItNeedsF3()
    {
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var (recordsDocument, version, ordinaryProject, ordinaryDocument, todo) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.ChildWorld.OrdinaryRoot("sprk_project", ordinaryProject)
            .OrdinaryChild("sprk_document", recordsDocument, ("sprk_project", "sprk_project", rootId))
            .OrdinaryChild("sprk_fileversion", version, ("sprk_document", "sprk_document", recordsDocument))
            .OrdinaryChild("sprk_document", ordinaryDocument,
                ("sprk_currentversionid", "sprk_fileversion", version), ("sprk_project", "sprk_project", ordinaryProject))
            .SecureChild("sprk_todo", todo, ("sprk_regardingdocument", "sprk_document", ordinaryDocument));
        _fixture.SeedShare(todo, DataversePrincipalRef.User(Outsider), RecordShareLevels.ViewOnlyRights);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ChildWorld.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "provisioning never releases an isolated child to its business unit (owner round 24)");
        _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == todo);
        _fixture.ShareMaskOf(todo, Outsider).Should().NotBe(0, "held as it was");
        _fixture.ChildWorld.OwnerOf("sprk_document", recordsDocument).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the record's own document still goes into isolation");
        var children = (await JsonOf(response)).GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        children.GetProperty("needsF3").GetInt32().Should().Be(1);
        children.GetProperty("tables").EnumerateArray()
            .Single(t => t.GetProperty("table").GetString() == "sprk_todo")
            .GetProperty("needsF3").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// AC 7 + owner round 24 item 1 (task 148 r2, verifier item 1): the report-only sweep reports EVERY change the writing
    /// sweep makes, on a three-level tree whose lower levels are reached only through rows the same pass moves: a secure
    /// work assignment's ordinary document, an analysis of that document, and a to-do filed only under the analysis. None
    /// of the lower two is isolated by anything the dry run READS; each is isolated by what the pass WILL do to its parent.
    /// Decided once against stored owners, the dry run planned the document only (then the apply changed all three). It now
    /// plans to the same fixpoint over planned owners, and the plan equals the applied result.
    /// </summary>
    [Fact]
    public async Task TheSweep_ReportOnly_PlansEveryLevelOfATreeReachedOnlyThroughRowsItWouldMove()
    {
        var sweep = new SweepHarness();
        var (root, document, analysis, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root)
            .OrdinaryChild("sprk_document", document, ("sprk_workassignment", "sprk_workassignment", root))
            .OrdinaryChild("sprk_analysis", analysis, ("sprk_documentid", "sprk_document", document))
            .OrdinaryChild("sprk_todo", todo, ("sprk_regardinganalysis", "sprk_analysis", analysis));

        var plan = await sweep.RunAsync(writesEnabled: null);

        plan.GetProperty("wouldChange").GetInt32().Should().Be(3, "the document, the analysis under it and the to-do under that");
        var planned = plan.GetProperty("changes").EnumerateArray().ToList();
        planned.Select(c => c.GetProperty("id").GetGuid()).Should().BeEquivalentTo(new[] { document, analysis, todo });
        var plannedTodo = planned.Single(c => c.GetProperty("id").GetGuid() == todo);
        plannedTodo.GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.WouldChange));
        plannedTodo.GetProperty("previousOwner").GetString().Should().Be($"teams({GeneralTeam:D})");
        sweep.World.OwnerWrites.Should().BeEmpty("report-only writes nothing");
        sweep.Shares.WriteLog.Should().BeEmpty();

        var applied = await sweep.RunAsync(writesEnabled: "true");

        applied.GetProperty("changed").GetInt32().Should().Be(3);
        applied.GetProperty("changes").EnumerateArray()
            .Select(c => (c.GetProperty("id").GetGuid(), c.GetProperty("previousOwner").GetString(), c.GetProperty("targetTeam").GetGuid()))
            .Should().BeEquivalentTo(
                planned.Select(c => (c.GetProperty("id").GetGuid(), c.GetProperty("previousOwner").GetString(), c.GetProperty("targetTeam").GetGuid())),
                "the dry run lists exactly the changes the writing run makes");
        foreach (var (table, id) in new[] { ("sprk_document", document), ("sprk_analysis", analysis), ("sprk_todo", todo) })
            sweep.World.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
    }

    /// <summary>
    /// AC 7 (task 148 r2, verifier item 1): the plan equals the apply on the hardest shape the fixpoint handles — a row moving
    /// IN (also filed under a second secure record), a row moving OUT only once that move is certain, a row the pass pulls
    /// into isolation and puts back when its support leaves, a grandchild of THAT row pulled in and put back with it, and a
    /// refused row. The report-only pass plans every move against the owners its earlier plans give (the put-back drops its
    /// plan); the apply over the same state then changes exactly the rows planned, to the same teams. (An unsecure — the one
    /// trigger that may release — so the OUT move is made.)
    /// </summary>
    [Fact]
    public async Task TheReportOnlyPlan_IsExactlyWhatTheApplyChanges_ThroughAPutBack()
    {
        var world = SecureChildShareWorld.Standard();
        var shares = new FakeRecordShareTable();
        var (root, otherSecure, halfProvisioned) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (document, analysis, @event, message, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // Mid-unsecure: the record is off the Secure team (moved, read back) and still flagged.
        world.FlaggedNotIsolatedRoot("sprk_workassignment", root)
            .SecureRoot("sprk_matter", otherSecure)
            .FlaggedNotIsolatedRoot("sprk_project", halfProvisioned)
            .UserOwnedChild("sprk_document", document,
                ("sprk_workassignment", "sprk_workassignment", root), ("sprk_matter", "sprk_matter", otherSecure))
            .OrdinaryChild("sprk_analysis", analysis,
                ("sprk_documentid", "sprk_document", document), ("sprk_regardingproject", "sprk_project", halfProvisioned))
            .SecureChild("sprk_event", @event, ("sprk_regardinganalysis", "sprk_analysis", analysis))
            .UserOwnedChild("sprk_communication", message, ("sprk_regardingevent", "sprk_event", @event))
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingcommunication", "sprk_communication", message));
        var reconciler = SecureChildShareWorld.ReconcilerOver(() => world, shares, webApi: null!);

        var plan = await reconciler.ReconcileAsync(
            "sprk_workassignment", root, SecureChildReconcileMode.ReportOnly, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty("report-only writes nothing");
        shares.WriteLog.Should().BeEmpty();
        var planned = plan.Changes.Where(c => c.Outcome == SecureChildRowOutcome.WouldChange)
            .Select(c => (c.Table, c.Id, c.TargetTeamId)).ToList();
        planned.Should().BeEquivalentTo(new[]
        {
            ("sprk_document", document, (Guid?)SecureChildShareWorld.SecureTeam),
            ("sprk_event", @event, (Guid?)GeneralTeam),
        }, "the message and its to-do are pulled in and put back — no net change — and the analysis is refused");
        plan.Changes.Single(c => c.Id == analysis).Outcome.Should().Be(SecureChildRowOutcome.Refused);

        var applied = await reconciler.ReconcileAsync(
            "sprk_workassignment", root, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        applied.Changes.Where(c => c.Outcome == SecureChildRowOutcome.Changed)
            .Select(c => (c.Table, c.Id, c.TargetTeamId))
            .Should().BeEquivalentTo(planned, "the apply changes exactly what the dry run planned");
        world.OwnerOf("sprk_communication", message).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        world.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        world.OwnerWrites.Should().Contain(w => w.Id == todo, "the apply did pull the to-do in and put it back");
    }

    /// <summary>
    /// Verifier item 5: a run's report COUNTS every change and lists at most <see cref="SecureChildReconciliationJob.MaxSampledChanges"/>
    /// of them — so a run that plans more than it lists says so (changesTotal &gt; changesListed) and a shortened list never
    /// passes for the whole plan. A row held for an F3 holder is listed (outcome NeedsF3) and counted in needsF3, even once
    /// the list no longer reaches it.
    /// </summary>
    [Fact]
    public async Task TheSweepReport_CountsEveryChange_EvenBeyondTheListedOnes()
    {
        var sweep = new SweepHarness();
        var (root, recordsDocument, version, ordinaryProject, ordinaryDocument, todo) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        sweep.World.SecureRoot("sprk_workassignment", root).OrdinaryRoot("sprk_project", ordinaryProject)
            .SecureChild("sprk_document", recordsDocument, ("sprk_workassignment", "sprk_workassignment", root))
            .SecureChild("sprk_fileversion", version, ("sprk_document", "sprk_document", recordsDocument))
            .OrdinaryChild("sprk_document", ordinaryDocument,
                ("sprk_currentversionid", "sprk_fileversion", version), ("sprk_project", "sprk_project", ordinaryProject))
            .SecureChild("sprk_todo", todo, ("sprk_regardingdocument", "sprk_document", ordinaryDocument));

        var held = await sweep.RunAsync(writesEnabled: null);

        held.GetProperty("changesTotal").GetInt32().Should().Be(1);
        held.GetProperty("changesListed").GetInt32().Should().Be(1);
        held.GetProperty("needsF3").GetInt32().Should().Be(1);

        // More planned changes than a report lists.
        var extra = SecureChildReconciliationJob.MaxSampledChanges + 1;
        for (var i = 0; i < extra; i++)
        {
            sweep.World.OrdinaryChild("sprk_event", Guid.NewGuid(),
                ("sprk_regardingworkassignment", "sprk_workassignment", root));
        }

        var plan = await sweep.RunAsync(writesEnabled: null);

        plan.GetProperty("wouldChange").GetInt32().Should().Be(extra);
        plan.GetProperty("needsF3").GetInt32().Should().Be(1, "counted even though the list no longer reaches it");
        plan.GetProperty("changesTotal").GetInt32().Should().Be(extra + 1);
        plan.GetProperty("changesListed").GetInt32().Should().Be(SecureChildReconciliationJob.MaxSampledChanges);
        plan.GetProperty("changes").GetArrayLength().Should().Be(SecureChildReconciliationJob.MaxSampledChanges);
        sweep.World.OwnerWrites.Should().BeEmpty();
    }

    /// <summary>
    /// The sweep job over a world: ONE job instance (it keeps its progress cursor, as the singleton the scheduler holds), a
    /// real reconciler resolved per run from a scope, and the writes switch and cap read from configuration per run. By
    /// default its own world and share table (work assignments only — they cascade nothing on Assign, so the platform-cascade
    /// client is never called); or a host fixture's world, share seam and Web API client (projects and matters).
    /// </summary>
    private sealed class SweepHarness
    {
        private readonly Func<SecureChildShareWorld> _world;
        public SecureChildShareWorld World => _world();
        public FakeRecordShareTable Shares { get; } = new();
        public JobRunResult? LastResult { get; private set; }
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly ServiceProvider _provider;
        private readonly SecureChildReconciliationJob _job;

        public SweepHarness()
            : this(world: null, shares: null, webApi: null)
        {
        }

        public SweepHarness(
            Func<SecureChildShareWorld>? world, IDataverseRecordShareService? shares, DataverseWebApiClient? webApi,
            IMembershipCacheInvalidator? accessCacheInvalidator = null)
        {
            var own = SecureChildShareWorld.Standard();
            _world = world ?? (() => own);
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(_world).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(_world, shares ?? Shares, webApi!,
                accessCacheInvalidator: accessCacheInvalidator));
            // Task 147: the job's recent-changes pass finds the records above changed rows through the synchronizer.
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(_world, shares ?? Shares));
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(_world)); // task 173: as the host registers it
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
