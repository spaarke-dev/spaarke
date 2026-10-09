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
/// unified-access-control-r2 task 175 (owner round 84: "Child access should always follow parent; if parent changes, then
/// child changes. ... if a child has a parent then the access cannot be changed manually") — the STORED Secure flag and Access
/// Permission of a filed work assignment or project follow its parents both ways, and are locked while it has a parent. Driven
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

    // ── AC 3: re-file — away from a secure parent it follows the new one; no parent keeps its values, editable ──────────

    /// <summary>
    /// AC 3: a secure work assignment re-filed (a form edit) from secure matter M to ordinary matter N is no longer secure
    /// after one job run (the cascade's path: owned by N's business unit team), and takes N's Access Permission.
    /// </summary>
    [Fact]
    public async Task ReFilingAChildFromASecureMatterToAnOrdinaryOne_MakesItNotSecure_InOneRun()
    {
        var (secureMatter, ordinaryMatter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secureMatter);
        SetPermission("sprk_matter", secureMatter, Restricted);
        OrdinaryMatter(ordinaryMatter, Standard);
        SecureFiledWorkAssignment(_fixture, workAssignment, secureMatter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", new EntityReference("sprk_matter", ordinaryMatter));
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Standard, "it follows its new matter");
        using var doc = JsonDocument.Parse(run.ResultJson!);
        doc.RootElement.GetProperty("followParents").GetProperty("unsecured").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// AC 3 (second half) + AC 4: a work assignment whose parent is REMOVED keeps its values (the job writes nothing to it)
    /// and becomes editable — a BFF write of its Access Permission passes the gate; while it had a parent the same write was
    /// refused <c>access_follows_parent</c>, naming the matter.
    /// </summary>
    [Fact]
    public async Task RemovingTheParent_KeepsTheValues_AndMakesThemEditable()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter, name: "Falcon");
        SetPermission("sprk_matter", matter, Restricted);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        var write = new[] { new KeyValuePair<string, object?>("sprk_accesspermission", new OptionSetValue(Standard)) };

        using (var scope = _fixture.Services.CreateScope())
        {
            var refused = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
                .CheckRefileAsync("sprk_workassignment", workAssignment, write, CancellationToken.None);
            refused!.RefusalCode.Should().Be(AccessFollowsParent.ReasonCode);
            refused.Reason.Should().Contain("Falcon");
        }

        World.Set("sprk_workassignment", workAssignment, "sprk_regardingmatter", null);
        var run = await _job.RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeTrue("a record left with no parent keeps its last values");
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Restricted);
        World.AccessPermissionWrites.Should().NotContain(w => w.Id == workAssignment);
        using (var scope = _fixture.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
                .CheckRefileAsync("sprk_workassignment", workAssignment, write, CancellationToken.None))
                .Should().BeNull("a parentless record's values are its own (F3 still applies to its Secure flag)");
        }
    }

    // ── AC 4: the routes refuse a child with a parent; a parentless record behaves as before ──────────────────────────────

    /// <summary>
    /// AC 4: Make Secure (<c>/provision-project</c>) on a work assignment filed under an ORDINARY matter is refused 409
    /// <c>access_follows_parent</c> naming the matter, nothing written; on a parentless work assignment it secures it as
    /// before.
    /// </summary>
    [Fact]
    public async Task MakeSecure_OnAChildWithAParent_IsRefusedNamingIt_AndOnAParentlessRecordSecuresAsBefore()
    {
        var (matter, child, parentless) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, name: "Harbour");
        FiledWorkAssignment(_fixture, child, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.SeedWorkAssignment(parentless, isSecure: false);

        var refused = await PostAsync(ProvisionRoute, new { recordType = "workassignment", recordId = child, transition = "make-secure" });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        var problem = await JsonOf(refused);
        problem.GetProperty("reasonCode").GetString().Should().Be(AccessFollowsParent.ReasonCode);
        problem.GetProperty("parentRecordType").GetString().Should().Be("matter");
        problem.GetProperty("parentRecordId").GetGuid().Should().Be(matter);
        problem.GetProperty("parentName").GetString().Should().Be("Harbour");
        _fixture.Updates.Should().NotContain(u => u.RecordId == child);
        _fixture.IsSecureOf(child).Should().BeFalse();

        var allowed = await PostAsync(ProvisionRoute, new { recordType = "workassignment", recordId = parentless, transition = "make-secure" });

        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        _fixture.IsSecureOf(parentless).Should().BeTrue("F3/Make Secure unchanged for a parentless record");
    }

    // ── AC 6: a fault mid-cascade leaves the child at the more restrictive state; the next run completes it ─────────────

    /// <summary>
    /// AC 6 + the seeding proof of the fail-closed ORDER: the matter is no longer secure and Standard, but the work
    /// assignment's ownership move does not take effect (Dataverse accepts the owner PATCH and ignores it). The un-secure
    /// stops at the read-back: the work assignment is still flagged secure, still owned by the Secure team, keeps its shares
    /// AND keeps Restricted (the looser Access Permission is written only after the un-secure completes); the run fails
    /// naming it. Once the fault clears, the next run completes it.
    /// </summary>
    [Fact]
    public async Task AFaultMidCascade_LeavesTheChildSecureAndRestricted_Reports_AndTheNextRunCompletes()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard); // un-secured already (its own unsecure is not under test here)
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
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

        _fixture.OwnershipPatchIsApplied = true;
        var completed = await _job.RunAsync();

        completed.Success.Should().BeTrue(completed.ErrorMessage);
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureChildShareWorld.GeneralTeam);
        PermissionOf("sprk_workassignment", workAssignment).Should().Be(Standard);
    }

    /// <summary>
    /// Fail closed on the owner: a parent whose flag was cleared OUTSIDE the BFF but which is still owned by the Secure Record
    /// Owners team would hand the child to that memberless team — reachable by nobody once its shares go. The child is
    /// left secure, untouched, and reported.
    /// </summary>
    [Fact]
    public async Task AParentStillOwnedByTheSecureTeam_NeverHandsTheChildToIt_TheChildStaysSecure()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: SecureTeam, containerId: $"b!matter-{matter:N}", isSecure: false);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain(workAssignment.ToString("D"));
        _fixture.IsSecureOf(workAssignment).Should().BeTrue();
        _fixture.OwningTeamOf(workAssignment).Should().Be(SecureTeam);
        _fixture.Updates.Should().NotContain(u => u.RecordId == workAssignment);
    }

    /// <summary>ADR-003: an ancestry that cannot be read loosens nothing (and the run reports it).</summary>
    [Fact]
    public async Task AnUnreadableParent_LoosensNothing_AndFailsTheRun()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        OrdinaryMatter(matter, Standard);
        SecureFiledWorkAssignment(_fixture, workAssignment, matter);
        SetPermission("sprk_workassignment", workAssignment, Restricted);
        World.FailingRowReadsOf("sprk_matter", matter);

        var run = await _job.RunAsync();

        run.Success.Should().BeFalse();
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
