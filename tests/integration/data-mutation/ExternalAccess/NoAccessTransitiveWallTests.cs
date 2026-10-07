using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.NoAccessEnforcementTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Batch-4 integration — main-session round 61 item 1 (class a, security): a No Access entry on a secure parent reaches
/// EVERY secure record filed below it, at any depth, through the ONE walk (<see cref="SecureRootInheritance"/>'s upward
/// <c>ReadSecureParentsAsync</c> and downward <c>ListFiledRootsBelowAsync</c>), level by level, cycle-safe and bounded by
/// <see cref="SecureRootInheritance.MaxFilingDepth"/> (past it: children-incomplete / Unverifiable, fail closed).
/// </summary>
/// <remarks>Driven through the REAL enforcer (task 143) and the REAL guard over task 148's in-memory world. A project is
/// filed under a matter or project by the polymorphic pair; a work assignment under a project by its typed lookup.</remarks>
public class NoAccessTransitiveWallTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";
    private const string WorkAssignment = "sprk_workassignment";
    private const int CollaborateMask = 262167;
    private const string Tenant = "00000000-0000-0000-0000-0000000000cc";

    private static readonly Guid Walled = Guid.Parse("16116116-1611-6116-1161-1611611611a1");
    private static readonly Guid Colleague = Guid.Parse("16116116-1611-6116-1161-1611611611a2");
    private static readonly Guid Author = Guid.Parse("16116116-1611-6116-1161-1611611611a3");
    private static readonly Guid SecureMatter = Guid.Parse("16116116-1611-6116-1161-1611611611b1");
    private static readonly Guid MiddleProject = Guid.Parse("16116116-1611-6116-1161-1611611611c1");
    private static readonly Guid Grandchild = Guid.Parse("16116116-1611-6116-1161-1611611611d1");
    private static readonly Guid MatterType = Guid.Parse("16116116-1611-6116-1161-1611611611e1");
    private static readonly Guid ProjectType = Guid.Parse("16116116-1611-6116-1161-1611611611e2");

    private readonly Harness _h = new();
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();

    public NoAccessTransitiveWallTests()
    {
        _h.Store.Person(Author);
        _h.Store.Person(Walled);
        _h.Store.Person(Colleague);
        _h.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot(Matter, SecureMatter)
            .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", Matter))
            .Add("sprk_recordtype_ref", ProjectType, ("sprk_recordlogicalname", Project));
        _h.Participations.RecordTables[SecureMatter] = Matter;
        _h.Store.Rights[(Author, SecureMatter)] = AccessRights.Read | AccessRights.Write;
    }

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    /// <summary>A secure project filed under <paramref name="parentTable"/> <paramref name="parentId"/> by the pair.</summary>
    private void SecureProjectUnder(Guid project, string parentTable, Guid parentId)
    {
        _h.ChildWorld.Add(Project, project,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)),
            ("sprk_issecure", true),
            ("sprk_regardingrecordid", parentId.ToString("D")),
            ("sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", parentTable == Matter ? MatterType : ProjectType)));
        Authored(project);
    }

    /// <summary>A secure work assignment filed under a project by its typed lookup.</summary>
    private void SecureWorkAssignmentUnder(Guid workAssignment, Guid project)
    {
        _h.ChildWorld.Add(WorkAssignment, workAssignment,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)),
            ("sprk_issecure", true),
            ("sprk_regardingproject", new EntityReference(Project, project)));
        Authored(workAssignment);
    }

    /// <summary>The entry's author may change it (N5) and the colleague still reads it (S5 does not stop a removal).</summary>
    private void Authored(Guid record)
    {
        _h.Store.Rights[(Author, record)] = AccessRights.Read | AccessRights.Write;
        _h.Participations.Flags[record] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
    }

    /// <summary>The matter → project → work assignment chain (two levels below the matter).</summary>
    private void TheGrandchildChain()
    {
        SecureProjectUnder(MiddleProject, Matter, SecureMatter);
        SecureWorkAssignmentUnder(Grandchild, MiddleProject);
        _h.Shares.Seed(WorkAssignment, Grandchild, User(Colleague), CollaborateMask);
    }

    private Task<NoAccessEnforcementReport> EnforceOnTheMatter()
    {
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);
        return _h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);
    }

    /// <summary>The REAL guard over the same world, with <see cref="Walled"/> on the matter's No Access list.</summary>
    private SecureShareNoAccessGuard GuardWithTheMatterWalled()
    {
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);
        var identities = new Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InMemoryContactIdentityStore();
        identities.AddSystemUser(Walled, oid: null, email: null);
        identities.AddSystemUser(Colleague, oid: null, email: null);
        return new SecureShareNoAccessGuard(
            new GrantPolicyTestDoubles.FlagStubParticipationService(defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false)),
            _denyList, identities, _h.Entities(), NullLogger<SecureShareNoAccessGuard>.Instance);
    }

    [Fact(DisplayName = "Round 61 item 1: the matter's entry removes the walled person's DIRECT share on a work assignment two levels below it")]
    public async Task AMattersEntry_RemovesTheWalledPersonsShare_OnAGrandchild()
    {
        TheGrandchildChain();
        _h.Shares.Seed(WorkAssignment, Grandchild, User(Walled), CollaborateMask);

        var report = await EnforceOnTheMatter();

        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
        report.Removed.Should().Contain(new NoAccessRemovedShare(Walled, WorkAssignment, Grandchild, CollaborateMask));
        _h.Shares.MaskOf(WorkAssignment, Grandchild, User(Walled)).Should().BeNull("the wall reaches every level below the matter");
        _h.Shares.MaskOf(WorkAssignment, Grandchild, User(Colleague)).Should().Be(CollaborateMask);
    }

    [Fact(DisplayName = "Round 61 item 1: a share on the grandchild is refused at share time by the matter's list (every secure ancestor)")]
    public async Task AShareOnTheGrandchild_IsRefused_ByTheMattersList()
    {
        TheGrandchildChain();
        var guard = GuardWithTheMatterWalled();

        var decision = await guard.CheckRecordAndSecureParentsAsync(
            WorkAssignment, Grandchild, Walled, SecureWallRecordScope.AsFlagged, CancellationToken.None);

        decision.Outcome.Should().Be(SecureShareWallOutcome.Walled);
        decision.ParentTable.Should().Be(Matter, "the list that decided it is the matter's, two levels up");
        decision.ParentId.Should().Be(SecureMatter);
    }

    [Fact(DisplayName = "Round 61 item 1: a filing given as the write will leave it (direct parent only) is still checked against every ancestor")]
    public async Task ASuppliedDirectFiling_IsCheckedAgainstEveryAncestor()
    {
        TheGrandchildChain();
        var guard = GuardWithTheMatterWalled();
        var direct = new SecureParentsAnswer(new[] { new SecureFilingParent(Project, MiddleProject) }, null);

        var decision = await guard.CheckRecordAndSecureParentsAsync(
            WorkAssignment, Grandchild, Walled, SecureWallRecordScope.BeingSecured, direct, CancellationToken.None);

        decision.Outcome.Should().Be(SecureShareWallOutcome.Walled);
        decision.ParentId.Should().Be(SecureMatter);
    }

    [Fact(DisplayName = "Round 61 item 1: a cycle (projects filed under one another) ends the walk; each record in it is reached once")]
    public async Task ACycle_EndsTheWalk_AndEachRecordInItIsReachedOnce()
    {
        // A under C, C under B, B under A — a cycle the walk must not loop on; a work assignment under B.
        var projectA = Guid.Parse("16116116-1611-6116-1161-1611611611c2");
        var projectB = Guid.Parse("16116116-1611-6116-1161-1611611611c3");
        var projectC = Guid.Parse("16116116-1611-6116-1161-1611611611c4");
        SecureProjectUnder(projectA, Project, projectC);
        SecureProjectUnder(projectB, Project, projectA);
        SecureProjectUnder(projectC, Project, projectB);
        SecureWorkAssignmentUnder(Grandchild, projectB);
        foreach (var (table, id) in new[] { (Project, projectA), (Project, projectB), (Project, projectC), (WorkAssignment, Grandchild) })
        {
            _h.Shares.Seed(table, id, User(Colleague), CollaborateMask);
            _h.Shares.Seed(table, id, User(Walled), CollaborateMask);
        }

        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, projectA), modifiedBy: Author);

        var report = await _h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
        report.Removed.Select(r => r.RecordId).Should().BeEquivalentTo(new[] { projectA, projectB, projectC, Grandchild },
            "each record in the cycle (and below it) is reached exactly once");

        // Upward: C climbs to B, then A, then meets C again — A's list decides; a cycle is not a chain past the bound.
        _denyList.DenySystemUserOnRecord(Walled, projectA);
        var guard = GuardWithTheMatterWalled();
        var decision = await guard.CheckRecordAndSecureParentsAsync(
            Project, projectC, Walled, SecureWallRecordScope.AsFlagged, CancellationToken.None);
        decision.Outcome.Should().Be(SecureShareWallOutcome.Walled, decision.Fault);
        decision.ParentId.Should().Be(projectA);

        var unwalled = await guard.CheckRecordAndSecureParentsAsync(
            Project, projectC, Colleague, SecureWallRecordScope.AsFlagged, CancellationToken.None);
        unwalled.Outcome.Should().Be(SecureShareWallOutcome.NotWalled,
            "a cycle ends the climb — it is not a chain past the bound (which would be Unverifiable)");
    }

    [Fact(DisplayName = "Round 61 item 1: a chain deeper than the bound fails closed — children-incomplete below, Unverifiable above")]
    public async Task AChainDeeperThanTheBound_FailsClosed_BothWays()
    {
        // matter → P1 → P2 → … → P(Max+1): the deepest project is past the bound.
        var parentTable = Matter;
        var parentId = SecureMatter;
        var chain = new List<Guid>();
        for (var i = 1; i <= SecureRootInheritance.MaxFilingDepth + 1; i++)
        {
            var project = Guid.Parse($"16116116-1611-6116-1161-16116116{i:D4}");
            SecureProjectUnder(project, parentTable, parentId);
            _h.Shares.Seed(Project, project, User(Colleague), CollaborateMask);
            chain.Add(project);
            (parentTable, parentId) = (Project, project);
        }

        _h.Shares.Seed(Project, chain[^1], User(Walled), CollaborateMask);

        var report = await EnforceOnTheMatter();

        report.Complete.Should().BeFalse("a record filed deeper than the walk follows was not reached");
        report.Failures.Should().Contain(f => f.Kind == "children-incomplete");
        _h.Shares.MaskOf(Project, chain[^1], User(Walled)).Should().Be(CollaborateMask, "past the bound nothing is removed");

        var guard = GuardWithTheMatterWalled();
        var decision = await guard.CheckRecordAndSecureParentsAsync(
            Project, chain[^1], Walled, SecureWallRecordScope.AsFlagged, CancellationToken.None);
        decision.Outcome.Should().Be(SecureShareWallOutcome.Unverifiable, "an ancestry past the bound is never 'not walled'");
    }
}
