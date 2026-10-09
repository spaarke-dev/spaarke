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
        InheritedAccessRecord(_fixture, "sprk_project", project, ("sprk_matter", matter));
        InheritedAccessRecord(_fixture, "sprk_workassignment", workAssignment, ("sprk_project", project));

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


    // ── Fix round (verifier F1-1, a–e): the access record is the cascade's alone, and an empty one never loosens ──────────

    /// <summary>
    /// F1-1 item 2: a caller-supplied write that names <c>sprk_accessinheritance</c> — the forged record that would make a
    /// hand-secured child's Secure look inherited, so that its ordinary parent "takes it away" — is refused by the gate every
    /// BFF writer calls (update and create, any spelling, alone or with other columns), and the job then never un-secures the
    /// child: no un-secure without F3.
    /// </summary>
    [Fact]
    public async Task AForgedAccessRecord_IsRefusedByEveryBffWriter_AndNeverLeadsToAnUnsecure()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter); // secure by hand: its parent is ordinary
        var forged = new AccessInheritance(new[] { $"sprk_matter:{matter:D}" }, true, Standard, false, null).Serialize();

        using var scope = _fixture.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<SecureRootFilingGate>();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();
        foreach (var key in new[] { "sprk_accessinheritance", "SPRK_AccessInheritance", " sprk_accessinheritance " })
        {
            var alone = new[] { new KeyValuePair<string, object?>(key, forged) };
            (await gate.CheckAsync("sprk_workassignment", workAssignment, alone, CancellationToken.None))!
                .RefusalCode.Should().Be(AccessInheritance.ServerOnlyReasonCode, $"an update naming {key} is refused");
            (await inheritance.CheckRefileAsync("sprk_workassignment", workAssignment, alone, CancellationToken.None))!
                .RefusalCode.Should().Be(AccessInheritance.ServerOnlyReasonCode);
        }

        var withOthers = new[]
        {
            new KeyValuePair<string, object?>("sprk_name", "Renamed"),
            new KeyValuePair<string, object?>("sprk_accessinheritance", forged),
        };
        (await gate.CheckAsync("sprk_project", Guid.NewGuid(), withOthers, CancellationToken.None))!
            .RefusalCode.Should().Be(AccessInheritance.ServerOnlyReasonCode, "on a project too, and among other columns");
        var create = await gate.PlanCreateAsync("sprk_workassignment", withOthers, Creator, CancellationToken.None);
        create.Refusal!.RefusalCode.Should().Be(AccessInheritance.ServerOnlyReasonCode, "a create never carries one");
        (await gate.CheckAsync("sprk_matter", matter, withOthers, CancellationToken.None))
            .Should().BeNull("a matter has no access record; its writes are not this gate's");

        // Fix round 2 (K3): the secure flag, either value, on update or create, is refused the same way.
        foreach (var flag in new[] { true, false })
        {
            var setsFlag = new[] { new KeyValuePair<string, object?>("sprk_issecure", flag) };
            (await gate.CheckAsync("sprk_workassignment", workAssignment, setsFlag, CancellationToken.None))!
                .RefusalCode.Should().Be(AccessFollowsParent.SecureFlagReasonCode);
            (await gate.PlanCreateAsync("sprk_project", setsFlag, Creator, CancellationToken.None))
                .Refusal!.RefusalCode.Should().Be(AccessFollowsParent.SecureFlagReasonCode);
        }

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("its Secure is its own; only an F3 holder may remove it");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        RecordOf("sprk_workassignment", workAssignment)!.OwnSecure.Should().BeTrue();
    }

    /// <summary>
    /// Fix round 2 (K1, round 87 item 1 for data from before the deploy): a work assignment secured through its matter BEFORE
    /// access records existed (none on the row) is recorded as INHERITED by the matter's own <c>/unsecure-project</c> before the
    /// matter's flag is cleared — so it follows the matter out in the same call.
    /// </summary>
    [Fact]
    public async Task AChildSecuredBeforeTheDeploy_IsRecordedAsInheritedBeforeItsParentIsUnsecured_AndFollowsIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);

        var response = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = matter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        World.AccessRecordWrites.Should().Contain(w => w.Id == workAssignment, "recorded before the matter stopped being secure");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse("its Secure was the matter's: it follows it out");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
    }

    /// <summary>
    /// F1-1 item 4 (empty never loosens): a work assignment secured through its matter before access records existed, whose
    /// matter was un-secured OUTSIDE the BFF before the job first saw it, stays secure — the backfill rule never loosens — and
    /// the job records the Secure as its own. Its F3 holder can still remove it (its parent is ordinary).
    /// </summary>
    [Fact]
    public async Task AnEmptyAccessRecord_NeverLoosens_WhenTheParentWasUnsecuredOutsideTheBff()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);

        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.IsSecureOf(workAssignment).Should().BeTrue("no access record: nothing it holds is taken away");
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted);
        RecordOf("sprk_workassignment", workAssignment)!.OwnSecure.Should().BeTrue("the backfill rule: stricter than the floor is own");
    }

    /// <summary>
    /// Fix (a): a work assignment the job secures BY INHERITANCE gets its access record at once (Secure inherited), so when its
    /// matter is un-secured later it follows — the backfill rule never has to guess for a record secured after deployment.
    /// </summary>
    [Fact]
    public async Task ARecordSecuredByInheritance_GetsItsAccessRecordAtOnce_AndFollowsItsParentOut()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);

        (await _job.RunAsync()).Success.Should().BeTrue();

        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        var record = RecordOf("sprk_workassignment", workAssignment);
        record.Should().NotBeNull("written when it was secured");
        record!.FloorSecure.Should().BeTrue();
        record.OwnSecure.Should().BeFalse("secured through its matter: inherited");

        (await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = matter })).StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.IsSecureOf(workAssignment).Should().BeFalse("an inherited Secure follows its parent out");
    }

    /// <summary>
    /// F1 (fix round 2) + F4: field security hides the column from the BFF (it can WRITE it but not READ it), so every record
    /// reads EMPTY, and the platform does not vouch for the BFF's read either. Nothing is decided on such a read: no record is
    /// overwritten by the backfill rule's guess — not one whose record says "inherited" under a now-ordinary matter, and not
    /// one whose Secure is its OWN under a secure matter — nothing is written at all, and the run fails naming
    /// <c>access_record_hidden</c>. The inline path (the secure matter's own <c>/unsecure-project</c>: its records-below step
    /// and its cascade) writes nothing either, and the child stays secure.
    /// </summary>
    [Fact]
    public async Task AnAccessRecordTheBffCannotRead_IsNeverOverwritten_NothingIsWritten_AndTheRunFails()
    {
        var (ordinary, secure, inherited, own) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(ordinary);
        SecureFiledWorkAssignment(_fixture, inherited, ordinary);
        SeedRecord("sprk_workassignment", inherited, floorSecure: true, Standard, ownSecure: false, ownPermission: null, ("sprk_matter", ordinary));
        SecureMatter(_fixture, secure);
        SecureFiledWorkAssignment(_fixture, own, secure);
        SetPermission("sprk_workassignment", own, Restricted);
        SeedRecord("sprk_workassignment", own, floorSecure: true, Standard, ownSecure: true, ownPermission: Restricted, ("sprk_matter", secure));
        var recordsBefore = new[] { inherited, own }.ToDictionary(id => id, id => World.ValueOf<string>("sprk_workassignment", id, AccessInheritance.Column));

        World.HidesAccessRecords = true;
        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(SecureRootInheritance.ReasonMarkerHidden);
        var unsecure = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = secure });
        unsecure.StatusCode.Should().Be(HttpStatusCode.OK, await unsecure.Content.ReadAsStringAsync());

        foreach (var id in new[] { inherited, own })
        {
            _fixture.IsSecureOf(id).Should().BeTrue("an unreadable record is never decided on");
            _fixture.OwningTeamOf(id).Should().Be(SecureTeam);
            World.ValueOf<string>("sprk_workassignment", id, AccessInheritance.Column).Should().Be(recordsBefore[id],
                "the stored record is never overwritten by a guess");
        }

        PermissionOf("sprk_workassignment", own).Should().Be(Restricted);
        World.AccessRecordWrites.Should().BeEmpty("nothing is written over an empty read the BFF cannot vouch for");
        World.AccessPermissionWrites.Should().BeEmpty();
        _fixture.Updates.Should().NotContain(u => u.RecordId == inherited || u.RecordId == own);
    }

    /// <summary>
    /// F4, round 2: the platform says the BFF may read the column, but reads come back EMPTY. The first record written (a
    /// project raised to its matter's Restricted) reads back empty, so the run stops trusting records: the work assignment
    /// filed under that project — which round 1 and round 2 (the project changed) would both revisit — is never written, its
    /// own Secure record is kept, and the run fails naming <c>access_record_hidden</c>.
    /// </summary>
    [Fact]
    public async Task OnceARecordReadsBackEmpty_NeitherRoundWritesAnotherRecord()
    {
        var (matter, project, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Restricted);
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        SetPermission("sprk_project", project, Standard);
        _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, containerId: $"b!wa-{workAssignment:N}", isSecure: true);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        SeedRecord("sprk_workassignment", workAssignment, floorSecure: false, Standard, ownSecure: true, ownPermission: Restricted,
            ("sprk_project", project));
        var before = World.ValueOf<string>("sprk_workassignment", workAssignment, AccessInheritance.Column);

        World.HidesAccessRecords = true;
        _fixture.AccessRecordReadableByProfile = true;
        _fixture.BffIsSystemAdministrator = true; // the platform vouches for the read; the reads still come back empty
        using var scope = _fixture.Services.CreateScope();
        var pass = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .FollowParentsPassAsync("trace-175r2", null, 25, 500, CancellationToken.None);

        pass.Untrusted.Should().Be(SecureRootInheritance.ReasonMarkerHidden);
        pass.Problems.Should().Contain(p => p.Contains(SecureRootInheritance.ReasonMarkerHidden));
        World.AccessRecordWrites.Should().NotContain(w => w.Id == workAssignment);
        World.ValueOf<string>("sprk_workassignment", workAssignment, AccessInheritance.Column).Should().Be(before);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
    }

    /// <summary>
    /// Fix round 2 (K2): <c>sprk_accessinheritance</c> is not field-secured (anyone with Write could have forged a record), so
    /// no record is trusted: the job follows nothing — a work assignment whose record says "inherited" under a now-ordinary
    /// matter stays secure — writes nothing and fails naming <c>access_record_not_secured</c>; a parent's own un-secure leaves
    /// the record below it secure too.
    /// </summary>
    [Fact]
    public async Task WhenTheColumnIsNotFieldSecured_NoAccessRecordIsTrusted_NothingIsFollowed_AndTheRunFails()
    {
        var (ordinary, secure, first, second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(ordinary);
        SecureFiledWorkAssignment(_fixture, first, ordinary);
        SeedRecord("sprk_workassignment", first, floorSecure: true, Standard, ownSecure: false, ownPermission: null, ("sprk_matter", ordinary));
        SecureMatter(_fixture, secure);
        SecureFiledWorkAssignment(_fixture, second, secure);
        SeedRecord("sprk_workassignment", second, floorSecure: true, Standard, ownSecure: false, ownPermission: null, ("sprk_matter", secure));

        _fixture.AccessRecordColumnSecured = false;
        var run = await _job.RunAsync();
        var unsecure = await PostAsync(UnsecureRoute, new { recordType = "matter", recordId = secure });

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(SecureRootInheritance.ReasonAccessRecordUnsecured);
        unsecure.StatusCode.Should().Be(HttpStatusCode.OK, await unsecure.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(secure).Should().BeFalse("the matter's own un-secure is its F3 holder's act");
        foreach (var id in new[] { first, second })
        {
            _fixture.IsSecureOf(id).Should().BeTrue("an untrusted record is never followed");
            _fixture.OwningTeamOf(id).Should().Be(SecureTeam);
        }

        World.AccessRecordWrites.Should().BeEmpty();
    }

    /// <summary>
    /// Fix round 2 (K1, the job): a record about to be recorded as INHERITED secure (no record yet, secure, under a secure
    /// matter) is written before any other record-only write — with room for one write, it is the one written.
    /// </summary>
    [Fact]
    public async Task TheJob_RecordsAnInheritedSecureFirst()
    {
        var (matter, parentless, inheritedSecure) = (Guid.NewGuid(),
            Guid.Parse("00000000-0000-0000-0000-000000000175"), Guid.Parse("ffffffff-0000-0000-0000-000000000175"));
        SecureMatter(_fixture, matter);
        SecureFiledWorkAssignment(_fixture, inheritedSecure, matter);
        _fixture.SeedWorkAssignment(parentless, isSecure: false);

        using var scope = _fixture.Services.CreateScope();
        var pass = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .FollowParentsPassAsync("trace-175k1", null, 25, maxRecordWrites: 1, CancellationToken.None);

        pass.Deferred.Should().Be(1);
        RecordOf("sprk_workassignment", inheritedSecure).Should().NotBeNull("the inherited secure is recorded first");
        RecordOf("sprk_workassignment", inheritedSecure)!.OwnSecure.Should().BeFalse();
        RecordOf("sprk_workassignment", parentless).Should().BeNull("deferred to the next run");
    }

    /// <summary>
    /// Fix (b), pinning the unreadable-record guard: an access record that is not one (corrupt text) is reported
    /// (<c>access_record_unreadable</c>, undetermined), loosens nothing, and is NOT overwritten — it is kept for whoever
    /// investigates, rather than replaced by the backfill rule's guess.
    /// </summary>
    [Fact]
    public async Task ACorruptAccessRecord_IsReported_LoosensNothing_AndIsKept()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        World.Set("sprk_workassignment", workAssignment, AccessInheritance.Column, "{not an access record");

        var run = await _job.RunAsync();

        run.Success.Should().BeTrue("one undecidable row does not fail every run (task 173's precedent)");
        using (var doc = JsonDocument.Parse(run.ResultJson!))
        {
            var follow = doc.RootElement.GetProperty("followParents");
            follow.GetProperty("undetermined").GetInt32().Should().Be(1);
            follow.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).Should()
                .Contain(p => p!.Contains(workAssignment.ToString("D")) && p.Contains(SecureRootInheritance.ReasonMarkerUnreadable));
        }

        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted);
        World.ValueOf<string>("sprk_workassignment", workAssignment, AccessInheritance.Column).Should().Be("{not an access record");
        World.AccessRecordWrites.Should().NotContain(w => w.Id == workAssignment);
    }

    /// <summary>
    /// Fix (c): a user's edit that lands between the cascade's read and its write is never overwritten — the column is read
    /// again just before the write and, changed, nothing is written (<c>changed_concurrently</c>). The next run decides again.
    /// </summary>
    [Fact]
    public async Task AUsersEditBetweenTheReadAndTheWrite_IsNeverOverwritten_AndTheNextRunDecidesAgain()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Restricted);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        SetPermission("sprk_workassignment", workAssignment, Standard);
        var asked = World.QueriedTables.Count(t => t == "sprk_workassignment");
        World.AfterQueriesOf("sprk_workassignment", asked + 1,
            w => w.Set("sprk_workassignment", workAssignment, "sprk_accesspermission", new OptionSetValue(InheritedAccessPermission.Limited)));

        using var scope = _fixture.Services.CreateScope();
        var follow = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .FollowParentsAsync("sprk_workassignment", workAssignment, "trace-175c", CancellationToken.None);

        follow.Outcome.Should().Be(FollowParentsOutcome.Incomplete);
        follow.ReasonCode.Should().Be(SecureRootInheritance.ReasonChangedConcurrently);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(InheritedAccessPermission.Limited, "the user's edit stands");
        World.AccessPermissionWrites.Should().NotContain(w => w.Id == workAssignment);
        World.AccessRecordWrites.Should().NotContain(w => w.Id == workAssignment);

        (await _job.RunAsync()).Success.Should().BeTrue();
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted, "decided again: below the floor is put back");
    }

    /// <summary>
    /// Fix (d): un-secures in the LATER rounds (a record filed under a project that was un-secured in this run) count against
    /// the run's un-secure bound: with a bound of two, the project's un-secure and ONE of the two work assignments below it use
    /// it, the other is deferred (still secure), and the next pass un-secures it.
    /// </summary>
    [Fact]
    public async Task UnsecuresInTheLaterRounds_CountAgainstTheRunsBound()
    {
        var (matter, project) = (Guid.NewGuid(), Guid.NewGuid());
        var below = new[] { Guid.NewGuid(), Guid.NewGuid() };
        OrdinaryMatter(matter);
        SecureProject(_fixture, project);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        InheritedAccessRecord(_fixture, "sprk_project", project, ("sprk_matter", matter));
        foreach (var workAssignment in below)
        {
            _fixture.SeedWorkAssignment(workAssignment, owningTeamId: SecureTeam, containerId: $"b!wa-{workAssignment:N}", isSecure: true);
            _fixture.SeedShare(workAssignment, DataversePrincipalRef.User(Creator), ProvisionProjectEndpoint.CreatorAccessRights);
            World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", project));
            InheritedAccessRecord(_fixture, "sprk_workassignment", workAssignment, ("sprk_project", project));
        }

        using var scope = _fixture.Services.CreateScope();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();
        var first = await inheritance.FollowParentsPassAsync("trace-175d", null, maxUnsecures: 2, maxRecordWrites: 500, CancellationToken.None);

        first.Unsecured.Should().Be(2, "the project (round 1) and ONE work assignment below it (round 2)");
        first.Deferred.Should().Be(1);
        _fixture.IsSecureOf(project).Should().BeFalse();
        below.Count(id => _fixture.IsSecureOf(id) == true).Should().Be(1, "the round-2 un-secure counted; the second is deferred, still secure");

        var second = await inheritance.FollowParentsPassAsync("trace-175d", first.UnsecureResumeAfter, 2, 500, CancellationToken.None);

        second.Unsecured.Should().Be(1);
        below.Should().OnlyContain(id => _fixture.IsSecureOf(id) == false);
    }

    /// <summary>
    /// Fix (e): an access record that cannot be written is a PROBLEM in the run result (<c>access_record_not_written</c>; the
    /// run fails), not counted as in step. The Access Permission it raised stays raised.
    /// </summary>
    [Fact]
    public async Task AnAccessRecordThatCannotBeWritten_IsReportedAsAProblem()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Restricted);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        SetPermission("sprk_workassignment", workAssignment, Standard);
        World.RefusingAccessRecordWritesOf(workAssignment);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D")).And.Contain(SecureRootInheritance.ReasonMarkerNotWritten);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted, "the raise held; only its record is missing");
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("followParents").GetProperty("notCompleted").GetInt32().Should().Be(1);
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
