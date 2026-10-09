using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 175 (owner round 84, refined by round 87: "the parent sets a FLOOR; a child may be
/// stricter") — the STORED Secure flag and Access Permission of a filed work assignment or project are never looser than its
/// parents'; an inherited value follows them both ways, a value set on the child stays, and a re-file never loosens. Driven
/// through the REAL routes (<c>/unsecure-project</c>, <c>/provision-project</c>), the REAL <see cref="SecureRootInheritance"/>
/// and the REAL <see cref="SecureRootInheritanceJob"/> over the provisioning fixture's Dataverse and its
/// <see cref="SecureChildShareWorld"/>.
/// </summary>
/// <remarks>The way back from one secure parent and the route lock's basic shape are in <see cref="SecureRootInheritanceTests"/>
/// ("the way back"). This class covers the POML's other acceptance cases and the fail-closed ordering.</remarks>
[Trait("status", "task-175-uac-r2")]
public class ChildAccessCascadeTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";
    private const int Standard = InheritedAccessPermission.Standard;
    private const int Restricted = InheritedAccessPermission.Restricted;

    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootInheritanceJobRunner _job;

    public ChildAccessCascadeTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _job = new SecureRootInheritanceJobRunner(_fixture);
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    private int? PermissionOf(string table, Guid id) => World.ValueOf<OptionSetValue>(table, id, "sprk_accesspermission")?.Value;

    private void SetPermission(string table, Guid id, int value) =>
        World.Set(table, id, "sprk_accesspermission", new OptionSetValue(value));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private Task<HttpResponseMessage> PostAsync(string route, object body) =>
        _fixture.CreateAuthenticatedClient().PostAsJsonAsync(route, body);

    /// <summary>An ordinary matter (owned by the caller in the general business unit, not secure).</summary>
    private void OrdinaryMatter(Guid id, int permission = Standard, string? name = null)
    {
        _fixture.SeedMatter(id, isSecure: false);
        SetPermission("sprk_matter", id, permission);
        if (name is not null)
            World.Set("sprk_matter", id, "sprk_mattername", name);
    }

    // ── AC 1: unsecuring the parent — the child follows; another secure parent keeps it secure; through a project ──────────

    /// <summary>
    /// AC 1 (second half): a work assignment filed under the matter being un-secured AND under another secure record (a
    /// secure project, by its typed lookup) keeps the more restrictive state: it stays secure and is listed as still secure.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_LeavesAChildAlsoFiledUnderAnotherSecureRecord_Secure()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureProject(_fixture, project);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("the project it is also filed under is still secure");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        (await JsonOf(response)).GetProperty("relatedSecureRecords").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetGuid()).Should().Equal(workAssignment);
    }

    /// <summary>
    /// AC 1, down a chain in ONE call: a project filed under the matter (its pair) and the work assignment filed under that
    /// project both follow the matter out of secure — top-down, so the project is ordinary before the work assignment is
    /// decided.
    /// </summary>
    [Fact]
    public async Task UnsecuringTheMatter_CascadesThroughAProject_ToTheWorkAssignmentFiledUnderIt()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureProject(_fixture, project);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, containerId: $"b!wa-{workAssignment:N}", isSecure: true);
        _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        foreach (var id in new[] { project, workAssignment })
        {
            _fixture.IsSecureOf(id).Should().BeFalse("it follows the matter, through the project");
            _fixture.OwningTeamOf(id).Should().Be(SecureChildShareWorld.GeneralTeam);
        }
    }


    // ── AC 2: the Access Permission follows the parent, both ways; correct rows are not written ──────────────────────────

    /// <summary>
    /// AC 2: changing the matter from Standard to Restricted (a form edit — outside the BFF) changes its filed work
    /// assignment's stored Access Permission within one job run; a second run writes nothing; and back to Standard follows
    /// too. A parentless work assignment's own value is never written.
    /// </summary>
    [Fact]
    public async Task TheJob_MakesAChildsAccessPermissionFollowItsMatter_BothWays_AndWritesNothingWhenInStep()
    {
        var (matter, child, parentless) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter);
        FiledWorkAssignment(_fixture, child, "sprk_regardingmatter", "sprk_matter", matter);
        SetPermission("sprk_workassignment", child, Standard);
        _fixture.SeedWorkAssignment(parentless, isSecure: false);
        SetPermission("sprk_workassignment", parentless, Restricted);

        SetPermission("sprk_matter", matter, Restricted);
        var first = await _job.RunAsync();

        first.Success.Should().BeTrue(first.ErrorMessage);
        PermissionOf("sprk_workassignment", child).Should().Be(Restricted);
        PermissionOf("sprk_workassignment", parentless).Should().Be(Restricted, "a parentless record keeps its own value");
        World.AccessPermissionWrites.Should().ContainSingle(w => w.Id == child);

        var writes = World.AccessPermissionWrites.Count;
        (await _job.RunAsync()).Success.Should().BeTrue();
        World.AccessPermissionWrites.Should().HaveCount(writes, "a row already in step is not written");

        SetPermission("sprk_matter", matter, Standard);
        (await _job.RunAsync()).Success.Should().BeTrue();
        PermissionOf("sprk_workassignment", child).Should().Be(Standard, "Restricted back to Standard follows too");
        World.AccessPermissionWrites.Should().NotContain(w => w.Id == parentless);
        _fixture.IsSecureOf(child).Should().BeFalse("nothing about its Secure flag changed");
    }


    // ── Round 87: own vs inherited; a re-file never loosens; the floor lock ────────────────────────────────────────────

    /// <summary>The access record (sprk_accessinheritance) as a test seeds it: what the record's values were derived from.</summary>
    private void SeedRecord(string table, Guid id, bool floorSecure, int floorPermission, bool ownSecure, int? ownPermission,
        params (string Table, Guid Id)[] parents) =>
        World.Set(table, id, AccessInheritance.Column, new AccessInheritance(
            parents.Select(p => $"{p.Table}:{p.Id:D}").ToList(), floorSecure, floorPermission, ownSecure, ownPermission).Serialize());

    private AccessInheritance? RecordOf(string table, Guid id) =>
        AccessInheritance.Parse(World.ValueOf<string>(table, id, AccessInheritance.Column)).Value;

    /// <summary>
    /// Round 87 item 2 (the owner's example): a work assignment made secure BY HAND under an ordinary matter keeps it when the
    /// matter is later secured and then un-secured — Make Secure on a child is allowed (it only makes it stricter) and records
    /// the designation as the record's own; only an inherited Secure follows a parent down.
    /// </summary>
    [Fact]
    public async Task AChildMadeSecureByHand_StaysSecure_WhenItsParentIsSecuredAndThenUnsecured()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, name: "Harbour");
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);

        var madeSecure = await PostAsync(ProvisionRoute, new { recordType = "workassignment", recordId = workAssignment, transition = "make-secure" });
        madeSecure.StatusCode.Should().Be(HttpStatusCode.OK, await madeSecure.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("tightening a child is never refused");
        RecordOf("sprk_workassignment", workAssignment)!.OwnSecure.Should().BeTrue("Make Secure records it as set on the record");

        (await PostAsync(ProvisionRoute, new { recordType = "matter", recordId = matter })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _job.RunAsync()).Success.Should().BeTrue();
        (await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = matter })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.IsSecureOf(matter).Should().BeFalse();
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("its own secure designation stays when the parent loosens");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
    }

    /// <summary>
    /// Round 87 item 3: an Access Permission set on the child (Limited, chosen on the form above a Standard floor) is its own.
    /// The matter turning Restricted raises the child to Restricted (the floor); the matter going back to Standard brings it
    /// back to its own Limited — not to Standard.
    /// </summary>
    [Fact]
    public async Task AnAccessPermissionSetOnTheChild_IsKeptUnderTheFloor_AndComesBackWhenTheParentLoosens()
    {
        var (matter, child) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard);
        FiledWorkAssignment(_fixture, child, "sprk_regardingmatter", "sprk_matter", matter);
        SetPermission("sprk_workassignment", child, Standard);
        (await _job.RunAsync()).Success.Should().BeTrue(); // the record: inherited Standard

        SetPermission("sprk_workassignment", child, InheritedAccessPermission.Limited); // a user, on the form
        (await _job.RunAsync()).Success.Should().BeTrue();
        PermissionOf("sprk_workassignment", child).Should().Be(InheritedAccessPermission.Limited, "a stricter choice is allowed");
        RecordOf("sprk_workassignment", child)!.OwnPermission.Should().Be(InheritedAccessPermission.Limited);

        SetPermission("sprk_matter", matter, Restricted);
        (await _job.RunAsync()).Success.Should().BeTrue();
        PermissionOf("sprk_workassignment", child).Should().Be(Restricted, "never looser than the floor");

        SetPermission("sprk_matter", matter, Standard);
        (await _job.RunAsync()).Success.Should().BeTrue();
        PermissionOf("sprk_workassignment", child).Should().Be(InheritedAccessPermission.Limited,
            "only the inherited part follows the parent down; the value set on the child stays");
    }

    /// <summary>Round 87: a child's Access Permission set LOWER than the floor (a grid edit past the form lock) is put back.</summary>
    [Fact]
    public async Task AnAccessPermissionBelowTheFloor_IsPutBack()
    {
        var (matter, child) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Restricted);
        FiledWorkAssignment(_fixture, child, "sprk_regardingmatter", "sprk_matter", matter);
        SetPermission("sprk_workassignment", child, Restricted);
        (await _job.RunAsync()).Success.Should().BeTrue();

        SetPermission("sprk_workassignment", child, Standard);
        (await _job.RunAsync()).Success.Should().BeTrue();

        PermissionOf("sprk_workassignment", child).Should().Be(Restricted);
    }

    /// <summary>
    /// Round 87 item 5 (verifier F1): a re-file NEVER loosens a child. A work assignment secure and Restricted through secure
    /// matter M, re-filed (a form edit) to ordinary Standard matter N, stays secure and Restricted; both become its own, so a
    /// later loosening of N does not take them either. And a secure parentless work assignment filed under an ordinary matter
    /// stays secure (the owner's example; the backfill rule for a record with no access record yet).
    /// </summary>
    [Fact]
    public async Task ReFiling_NeverLoosensAChild()
    {
        var (secureMatter, ordinaryMatter, workAssignment, parentless) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secureMatter);
        SetPermission("sprk_matter", secureMatter, Restricted);
        OrdinaryMatter(ordinaryMatter, Standard);
        SecureFiledWorkAssignment(_fixture, workAssignment, secureMatter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        _fixture.SeedWorkAssignment(parentless, owningTeamId: SecureTeam, containerId: $"b!wa-{parentless:N}", isSecure: true);
        (await _job.RunAsync()).Success.Should().BeTrue(); // records: the filed one inherited, the parentless one its own
        RecordOf("sprk_workassignment", workAssignment)!.OwnSecure.Should().BeFalse("secure through the matter");

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", ordinaryMatter));
        World.Set("sprk_workassignment", parentless, "sprk_regardingmatter", new EntityReference("sprk_matter", ordinaryMatter));
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        foreach (var id in new[] { workAssignment, parentless })
        {
            _fixture.IsSecureOf(id).Should().BeTrue("a re-file never un-secures");
            _fixture.OwningTeamOf(id).Should().Be(SecureTeam);
            RecordOf("sprk_workassignment", id)!.OwnSecure.Should().BeTrue("what it held beyond the new floor is its own now");
        }

        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted, "a re-file never loosens the Access Permission");
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment || u.RecordId == parentless, "no un-secure step ran");
    }

    /// <summary>
    /// Round 87 floor lock on a BFF write: an Access Permission LOWER than the parents' is refused (<c>access_follows_parent</c>,
    /// naming the matter); an equal or stricter one passes; once the parent is removed any value passes.
    /// </summary>
    [Fact]
    public async Task ABffWrite_BelowTheFloorIsRefused_AtOrAboveItPasses_AndWithoutAParentAnythingPasses()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, InheritedAccessPermission.Limited, name: "Falcon");
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        static KeyValuePair<string, object?>[] Write(int value) =>
            new[] { new KeyValuePair<string, object?>("sprk_accesspermission", new OptionSetValue(value)) };

        using var scope = _fixture.Services.CreateScope();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();

        var refused = await inheritance.CheckRefileAsync("sprk_workassignment", workAssignment, Write(Standard), CancellationToken.None);
        refused!.RefusalCode.Should().Be(AccessFollowsParent.ReasonCode);
        refused.Reason.Should().Contain("Falcon");
        (await inheritance.CheckRefileAsync("sprk_workassignment", workAssignment, Write(InheritedAccessPermission.Limited), CancellationToken.None))
            .Should().BeNull("equal to the floor");
        (await inheritance.CheckRefileAsync("sprk_workassignment", workAssignment, Write(Restricted), CancellationToken.None))
            .Should().BeNull("stricter than the floor: a child may be stricter");

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        (await inheritance.CheckRefileAsync("sprk_workassignment", workAssignment, Write(Standard), CancellationToken.None))
            .Should().BeNull("a parentless record's values are its own");
    }

    /// <summary>
    /// Verifier K1: the floor lock's own fail-closed branch — a BFF write of the Access Permission on a work assignment whose
    /// matter cannot be read is refused (<c>parent_undetermined</c>), never let through as "no floor".
    /// </summary>
    [Fact]
    public async Task ABffWrite_WhenWhatTheRecordIsFiledUnderCannotBeRead_IsRefused()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Restricted);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        World.FailingRowReadsOf("sprk_matter", matter);

        using var scope = _fixture.Services.CreateScope();
        var refused = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>().CheckRefileAsync(
            "sprk_workassignment", workAssignment,
            new[] { new KeyValuePair<string, object?>("sprk_accesspermission", new OptionSetValue(Standard)) }, CancellationToken.None);

        refused.Should().NotBeNull();
        refused!.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    /// <summary>
    /// Round 87: removing a Secure designation set on the child by hand (its parent is NOT secure) is allowed to its F3
    /// holder (here its creator), as on a parentless record; removing one that comes from a secure parent is refused 409
    /// (covered by <see cref="SecureRootInheritanceTests"/>).
    /// </summary>
    [Fact]
    public async Task RemoveSecure_OnAChildWhoseSecureIsItsOwn_IsAllowedLikeAParentlessRecord()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);

        var response = await PostAsync(UnsecureRoute, new { recordType = "workassignment", recordId = workAssignment });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    // ── AC 6: a fault mid-cascade leaves the child at the more restrictive state; the next run completes it ─────────────

    /// <summary>
    /// AC 6 + the seeding proof of the fail-closed ORDER: the work assignment's Secure and Restricted are INHERITED (its
    /// record says so) from a matter that is now ordinary and Standard, but its ownership move does not take effect
    /// (Dataverse accepts the owner PATCH and ignores it). The un-secure stops at the read-back: still flagged secure, still
    /// owned by the Secure team, its shares kept AND still Restricted (the looser Access Permission is written only after the
    /// un-secure completes); the run fails naming it. Once the fault clears, the next run completes it.
    /// </summary>
    [Fact]
    public async Task AFaultMidCascade_LeavesTheChildSecureAndRestricted_Reports_AndTheNextRunCompletes()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard); // un-secured already (its own unsecure is not under test here)
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        SeedRecord("sprk_workassignment", workAssignment, floorSecure: true, Restricted, ownSecure: false, ownPermission: null,
            ("sprk_matter", matter));
        var sharesBefore = _fixture.SharesOn(workAssignment);

        _fixture.OwnershipPatchIsApplied = false;
        var failed = await _job.RunAsync();

        failed.Success.Should().BeFalse();
        failed.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("the flag is cleared LAST, never before the owner move is read back");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        _fixture.SharesOn(workAssignment).Should().BeEquivalentTo(sharesBefore, "no share is revoked before the owner move holds");
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted,
            "the looser Access Permission waits for the un-secure (fail closed: the more restrictive state)");
        RecordOf("sprk_workassignment", workAssignment)!.OwnSecure.Should().BeFalse("a failed un-secure never turns into 'its own'");

        _fixture.OwnershipPatchIsApplied = true;
        var completed = await _job.RunAsync();

        completed.Success.Should().BeTrue(completed.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Standard);
    }

    /// <summary>
    /// Fail closed on the owner: a parent whose flag was cleared OUTSIDE the BFF but which is still owned by the Secure Record
    /// Owners team would hand an inherited-secure child to that memberless team — reachable by nobody once its shares go. The
    /// child is left secure, untouched, and reported.
    /// </summary>
    [Fact]
    public async Task AParentStillOwnedByTheSecureTeam_NeverHandsTheChildToIt_TheChildStaysSecure()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: SecureTeam, containerId: $"b!matter-{matter:N}", isSecure: false);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SeedRecord("sprk_workassignment", workAssignment, floorSecure: true, Standard, ownSecure: false, ownPermission: null,
            ("sprk_matter", matter));

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment);
    }

    /// <summary>
    /// ADR-003: an ancestry that cannot be read loosens nothing, and the run reports it (<c>undetermined</c>, named in
    /// <c>problems</c>) without failing — task 173's precedent: one undecidable row must not fail every run.
    /// </summary>
    [Fact]
    public async Task AnUnreadableParent_LoosensNothing_AndIsReported()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        World.FailingRowReadsOf("sprk_matter", matter);

        var run = await _job.RunAsync();

        using (var doc = JsonDocument.Parse(run.ResultJson!))
        {
            var follow = doc.RootElement.GetProperty("followParents");
            follow.GetProperty("undetermined").GetInt32().Should().Be(1);
            follow.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).Should()
                .Contain(p => p!.Contains(workAssignment.ToString("D")));
        }

        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted);
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment);
    }


    // ── #1478: the readers that decide Restricted from the stored column see Restricted through a parent ──────────────────

    /// <summary>
    /// #1478: <see cref="EffectiveRootFlags.RestrictedThroughFilingAsync"/> — the ONE helper the five stored-column readers
    /// (provisioning's creator rule, the child-share mirror, SPE membership, Office edit) now consult: Restricted through a
    /// matter, not through a Standard one, unknown (null) when the chain cannot be read, and never for a matter. The mirror's
    /// barred set is driven end to end (it is the reader with a throwing contract); the other three call the same helper.
    /// </summary>
    [Fact]
    public async Task RestrictedThroughFiling_IsSeenByTheStoredColumnReaders()
    {
        var (restrictedMatter, standardMatter, unreadableMatter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (underRestricted, underStandard, underUnreadable) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(restrictedMatter, Restricted);
        OrdinaryMatter(standardMatter, Standard);
        OrdinaryMatter(unreadableMatter, Restricted);
        FiledWorkAssignment(_fixture, underRestricted, "sprk_regardingmatter", "sprk_matter", restrictedMatter);
        FiledWorkAssignment(_fixture, underStandard, "sprk_regardingmatter", "sprk_matter", standardMatter);
        FiledWorkAssignment(_fixture, underUnreadable, "sprk_regardingmatter", "sprk_matter", unreadableMatter);
        World.FailingRowReadsOf("sprk_matter", unreadableMatter);
        var entities = SecureChildShareWorld.EntitiesOver(() => World).Object;
        var logger = NullLogger.Instance;

        (await EffectiveRootFlags.RestrictedThroughFilingAsync(entities, logger, "sprk_workassignment", underRestricted, default)).Should().BeTrue();
        (await EffectiveRootFlags.RestrictedThroughFilingAsync(entities, logger, "sprk_workassignment", underStandard, default)).Should().BeFalse();
        (await EffectiveRootFlags.RestrictedThroughFilingAsync(entities, logger, "sprk_workassignment", underUnreadable, default)).Should().BeNull();
        (await EffectiveRootFlags.RestrictedThroughFilingAsync(entities, logger, "sprk_matter", restrictedMatter, default)).Should().BeFalse();

        var external = Guid.NewGuid();
        World.Add("systemuser", external, ("sprk_isexternal", true));
        var synchronizer = World.Synchronizer(new FakeRecordShareTable());
        var answer = await synchronizer.RestrictedExternalPrincipalsOrThrowAsync(
            "sprk_workassignment", underRestricted, new[] { DataversePrincipalRef.User(external) }, default);
        answer.IsRestricted.Should().BeTrue("Restricted through its matter, before its own column catches up");
        answer.Barred.Should().Contain(DataversePrincipalRef.User(external));

        var unknown = async () => await synchronizer.RestrictedExternalPrincipalsOrThrowAsync(
            "sprk_workassignment", underUnreadable, new[] { DataversePrincipalRef.User(external) }, default);
        await unknown.Should().ThrowAsync<InvalidOperationException>("a fault is never 'nobody is barred'");
    }
}
