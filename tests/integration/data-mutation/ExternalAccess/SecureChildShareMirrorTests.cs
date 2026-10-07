using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 149 (C10 part 2, sharees; #1071) — the people shared on a SECURE record see exactly its
/// children, at the root's rights and never wider; nobody else does.
/// </summary>
/// <remarks>
/// <para><b>What runs for real.</b> The REAL <see cref="SecureChildShareSynchronizer"/>, the REAL share endpoints and the
/// REAL reconcile job, over <see cref="SecureChildShareWorld"/> (an in-memory Dataverse that EVALUATES each query, with
/// ordinary decoy rows a dropped predicate would select) and <see cref="FakeRecordShareTable"/> (task 063's strict POA
/// double: GrantAccess is additive, Modify/Revoke of a share that does not exist throw). The substitution is the documented
/// seam, <see cref="IDataverseRecordShareService"/> (ADR-038).</para>
/// <para><b>Masks are Dataverse's numbers, as literals</b> (Read 1, Write 2, Append 4, AppendTo 16, Delete 65536, Share
/// 262144, Assign 524288) — a test that derived them from <see cref="RecordShareLevels"/> would pass whatever it said.</para>
/// <para>Placement: <c>tests/integration/data-mutation/**</c> — every test here is about which POA rows get written.</para>
/// </remarks>
public class SecureChildShareMirrorTests
{
    // ── Masks, as Dataverse stores them ─────────────────────────────────────────────────────────────────────────────
    private const int ViewOnly = 1;
    private const int Collaborate = 262167;      // R 1 + W 2 + A 4 + AT 16 + Share 262144 (task 139)
    private const int FullAccess = 327703;       // Collaborate + Delete 65536
    private const int CollaborateOnChild = 23;   // Collaborate without Share
    private const int FullAccessOnChild = 65559; // Full Access without Share
    private const int ShareBit = 262144;
    private const int AssignBit = 524288;

    private const string ViewCsv = "ReadAccess";
    private const string CollaborateChildCsv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";
    private const string FullChildCsv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess";

    // ── Records ────────────────────────────────────────────────────────────────────────────────────────────────────
    private static readonly Guid ProjectR = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectR2 = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid OrdinaryProject = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private static readonly Guid FlaggedProject = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    private static readonly Guid SecureMatter = Guid.Parse("a1000000-0000-4000-8000-000000000005");
    private static readonly Guid SecureWorkAssignment = Guid.Parse("a1000000-0000-4000-8000-000000000006");

    private static readonly Guid DocR = Guid.Parse("d0c00000-0000-4000-8000-000000000001");
    private static readonly Guid DocRelatedOnly = Guid.Parse("d0c00000-0000-4000-8000-000000000002");
    private static readonly Guid EventR = Guid.Parse("e0e00000-0000-4000-8000-000000000001");
    private static readonly Guid TodoR = Guid.Parse("70d00000-0000-4000-8000-000000000001");
    private static readonly Guid CommR = Guid.Parse("c0c00000-0000-4000-8000-000000000001");
    private static readonly Guid AttachmentR = Guid.Parse("a7a00000-0000-4000-8000-000000000001");
    private static readonly Guid DocR2 = Guid.Parse("d0c00000-0000-4000-8000-000000000003");
    private static readonly Guid DocOfOrdinary = Guid.Parse("d0c00000-0000-4000-8000-000000000004");
    private static readonly Guid SecureDocUnderOrdinary = Guid.Parse("d0c00000-0000-4000-8000-000000000005");
    private static readonly Guid DocOfMatter = Guid.Parse("d0c00000-0000-4000-8000-000000000006");
    private static readonly Guid TodoOfWorkAssignment = Guid.Parse("70d00000-0000-4000-8000-000000000002");
    private static readonly Guid LegacyDocOfR = Guid.Parse("d0c00000-0000-4000-8000-000000000007");

    // ── Principals ─────────────────────────────────────────────────────────────────────────────────────────────────
    private static readonly Guid UserA = Guid.Parse("11111111-0000-4000-8000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("11111111-0000-4000-8000-00000000000b");
    private static readonly Guid UserC = Guid.Parse("11111111-0000-4000-8000-00000000000c");
    private static readonly Guid UserD = Guid.Parse("11111111-0000-4000-8000-00000000000d");
    private static readonly Guid TeamT = Guid.Parse("44444444-0000-4000-8000-000000000001");

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    private readonly FakeRecordShareTable _shares = new();

    /// <summary>
    /// Secure project R with six children across five tables (one a grandchild via a communication, one filed ONLY through
    /// <c>sprk_relatedproject</c>); a second secure project R2 with a child; an ordinary project with an ordinary child and a
    /// Secure-team-owned child; a secure matter and a secure work assignment with a child each.
    /// </summary>
    private static SecureChildShareWorld World() => SecureChildShareWorld.Standard()
        .SecureRoot("sprk_project", ProjectR)
        .SecureRoot("sprk_project", ProjectR2)
        .OrdinaryRoot("sprk_project", OrdinaryProject)
        .SecureRoot("sprk_matter", SecureMatter)
        .SecureRoot("sprk_workassignment", SecureWorkAssignment)
        .SecureChild("sprk_document", DocR, ("sprk_project", "sprk_project", ProjectR))
        .SecureChild("sprk_document", DocRelatedOnly, ("sprk_relatedproject", "sprk_project", ProjectR))
        .SecureChild("sprk_event", EventR, ("sprk_regardingproject", "sprk_project", ProjectR))
        .SecureChild("sprk_todo", TodoR, ("sprk_regardingproject", "sprk_project", ProjectR))
        .SecureChild("sprk_communication", CommR, ("sprk_regardingproject", "sprk_project", ProjectR))
        .SecureChild("sprk_communicationattachment", AttachmentR, ("sprk_communication", "sprk_communication", CommR))
        .SecureChild("sprk_document", DocR2, ("sprk_project", "sprk_project", ProjectR2))
        .OrdinaryChild("sprk_document", DocOfOrdinary, ("sprk_project", "sprk_project", OrdinaryProject))
        .SecureChild("sprk_document", SecureDocUnderOrdinary, ("sprk_project", "sprk_project", OrdinaryProject))
        .SecureChild("sprk_document", DocOfMatter, ("sprk_matter", "sprk_matter", SecureMatter))
        .SecureChild("sprk_todo", TodoOfWorkAssignment, ("sprk_regardingworkassignment", "sprk_workassignment", SecureWorkAssignment))
        // A pre-146 child of R still owned by an ordinary team (task 148 backfills it): never mirrored here.
        .OrdinaryChild("sprk_document", LegacyDocOfR, ("sprk_project", "sprk_project", ProjectR));

    private static readonly (string Table, Guid Id)[] ChildrenOfR =
    {
        ("sprk_document", DocR), ("sprk_document", DocRelatedOnly), ("sprk_event", EventR), ("sprk_todo", TodoR),
        ("sprk_communication", CommR), ("sprk_communicationattachment", AttachmentR),
    };

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The mirror — reconcile (the mechanism for new children, re-files and MDA sharing)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reconcile_AChildCreatedAfterTheShare_IsReadableByTheSharee_AtTheRootsRightsWithoutShare()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);

        var result = await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserA)).Should().Be(CollaborateOnChild, $"{table} {id} is a child of R");

        _shares.WriteLog.Should().OnlyContain(w => w.Action == "GrantAccess" && w.Rights == CollaborateChildCsv,
            "every child mirror is a GrantAccess of Collaborate WITHOUT ShareAccess (trigger 6)");
        _shares.WriteLog.Should().NotContain(w => w.Rights != null && w.Rights.Contains("ShareAccess"));
    }

    [Fact]
    public async Task Reconcile_FullAccessOnTheRoot_MirrorsDeleteButNotShare()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), FullAccess);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, User(UserA)).Should().Be(FullAccessOnChild);
        _shares.WriteLog.Where(w => w.RecordId == DocR).Should().ContainSingle().Which.Rights.Should().Be(FullChildCsv);
    }

    [Fact]
    public async Task Reconcile_CoversMattersAndWorkAssignments()
    {
        _shares.Seed("sprk_matter", SecureMatter, User(UserA), ViewOnly);
        _shares.Seed("sprk_workassignment", SecureWorkAssignment, User(UserB), Collaborate);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocOfMatter, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", DocOfMatter, User(UserB)).Should().BeNull();
        _shares.MaskOf("sprk_todo", TodoOfWorkAssignment, User(UserB)).Should().Be(CollaborateOnChild);
        _shares.MaskOf("sprk_todo", TodoOfWorkAssignment, User(UserA)).Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_AGrandchildThroughAUserOwnedCommunication_IsMirrored()
    {
        // A run-as-user / client-created communication (not team-owned) filed to R, with a Secure-team-owned attachment.
        var userComm = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var world = World()
            .UserOwnedChild("sprk_communication", userComm, ("sprk_regardingproject", "sprk_project", ProjectR))
            .SecureChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", userComm));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_communicationattachment", attachment, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_communication", userComm, User(UserA)).Should().BeNull(
            "a user-owned row is looked through, never mirrored itself — it is not the Secure team's");
    }

    [Fact]
    public async Task Reconcile_ARowUnderAnOrdinaryTeamOwnedParent_IsNotMirrored()
    {
        // An ordinary-team-owned communication has been decided NOT secure by its owner, even though it names R.
        var ordinaryComm = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var world = World()
            .OrdinaryChild("sprk_communication", ordinaryComm, ("sprk_regardingproject", "sprk_project", ProjectR))
            .SecureChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", ordinaryComm));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_communicationattachment", attachment, User(UserA)).Should().BeNull();
        result.ChildrenOutsideSecureRoots.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Reconcile_RemovesAChildShareWhosePrincipalIsNotSharedOnTheRoot()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_document", DocR, User(UserC), FullAccessOnChild); // e.g. an OOB share of the child itself

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, User(UserC)).Should().BeNull();
        _shares.MaskOf("sprk_document", DocR, User(UserA)).Should().Be(ViewOnly);
    }

    [Fact]
    public async Task Reconcile_NarrowsAChildShareWiderThanTheRoot_AndStripsShareAccessFromAChild()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, User(UserB), Collaborate);
        _shares.Seed("sprk_document", DocR, User(UserA), FullAccessOnChild); // wider than A's View on R
        _shares.Seed("sprk_document", DocR, User(UserB), Collaborate);       // carries Share on the child

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(CollaborateOnChild);
        _shares.WriteLog.Where(w => w.RecordId == DocR).Select(w => w.Action).Should().OnlyContain(a => a == "ModifyAccess",
            "an existing share is changed with ModifyAccess (it replaces), never widened or narrowed by GrantAccess");
    }

    [Fact]
    public async Task Reconcile_AShareMadeOnTheRootInTheModelDrivenApp_ReachesEveryChild_AndItsUnshareLeavesThemAll()
    {
        var world = World();
        // The MDA writes the root's POA row directly — the BFF is not involved.
        _shares.Seed("sprk_project", ProjectR, User(UserD), ViewOnly);

        await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserD)).Should().Be(ViewOnly);

        // The MDA unshare: the root's row goes, out of band.
        await _shares.RevokeAccessAsync("sprk_projects", ProjectR, User(UserD));

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserD)).Should().BeNull($"{table} {id} must not outlive D's unshare of R");
    }

    [Fact]
    public async Task Reconcile_IsIdempotent_ASecondRunWritesNothing()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        var synchronizer = World().Synchronizer(_shares);
        await synchronizer.ReconcileAllAsync(CancellationToken.None);
        var writes = _shares.WriteLog.Count;

        var second = await synchronizer.ReconcileAllAsync(CancellationToken.None);

        _shares.WriteLog.Should().HaveCount(writes);
        second.ChildrenUpdated.Should().Be(0);
        second.ChildrenUnchanged.Should().Be(second.ChildrenInScope);
    }

    [Fact]
    public async Task Reconcile_MirrorsATeamSharedOnTheRoot_ButNeverTheSecureOwnerTeamItself()
    {
        _shares.Seed("sprk_project", ProjectR, DataversePrincipalRef.Team(TeamT), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam), FullAccess);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, DataversePrincipalRef.Team(TeamT)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", DocR, DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam)).Should().BeNull(
            "the owner team owns the child; a share to it would only widen what an added member could reach");
    }

    // ── Never wider (negatives) ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Never_AUserNotSharedOnTheRoot_GainsAChild()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), FullAccess);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().BeNull();
        _shares.WriteLog.Should().OnlyContain(w => w.Principal == User(UserA));
        _shares.WriteLog.Should().NotContain(w => w.RecordId == LegacyDocOfR,
            "only the Secure team's rows are mirrored; an ordinary-team-owned child of R is task 148's to re-own");
    }

    [Fact]
    public async Task Never_AUserSharedOnOneSecureRoot_GainsTheChildrenOfAnother()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), FullAccess);
        _shares.Seed("sprk_project", ProjectR2, User(UserB), ViewOnly);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR2, User(UserA)).Should().BeNull();
        _shares.MaskOf("sprk_document", DocR2, User(UserB)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().BeNull();
    }

    [Fact]
    public async Task Never_TheChildrenOfAnOrdinaryRoot_ReceiveOrLoseAMirroredShare()
    {
        _shares.Seed("sprk_project", OrdinaryProject, User(UserA), FullAccess);
        _shares.Seed("sprk_document", SecureDocUnderOrdinary, User(UserC), ViewOnly); // left alone: task 148's transition

        var result = await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.WriteLog.Should().NotContain(w => w.RecordId == DocOfOrdinary || w.RecordId == SecureDocUnderOrdinary);
        _shares.MaskOf("sprk_document", SecureDocUnderOrdinary, User(UserC)).Should().Be(ViewOnly);
        result.ChildrenOutsideSecureRoots.Should().Be(1);
    }

    [Fact]
    public async Task Never_ShareOrAssign_IsAddedToAChild_EvenWhenTheRootShareCarriesThem()
    {
        // A share made in the Dataverse UI can carry Assign; the child receives only what a mirror may carry.
        _shares.Seed("sprk_project", ProjectR, User(UserA), 1 | 2 | ShareBit | AssignBit);

        await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, User(UserA)).Should().Be(3);
        _shares.WriteLog.Should().OnlyContain(w => w.Rights == "ReadAccess,WriteAccess");
    }

    [Fact]
    public async Task TwoSecureRoots_TheChildGetsTheIntersection_AtTheLowerRights()
    {
        // Escalation trigger 2 — implemented as INTERSECTION (fail closed) until the owner decides.
        var both = Guid.NewGuid();
        var world = World().SecureChild("sprk_document", both,
            ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2));
        _shares.Seed("sprk_project", ProjectR, User(UserA), FullAccess);
        _shares.Seed("sprk_project", ProjectR2, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, User(UserB), FullAccess); // on R only

        await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", both, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", both, User(UserB)).Should().BeNull("B is not shared on R2, the child's other root");
    }

    // ── Fail closed ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheRootsSharesCannotBeRead_NoChildOfItIsWritten()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_document", DocR, User(UserC), ViewOnly); // would be revoked if the root were readable
        _shares.FailReadsOfRecord = ("sprk_project", ProjectR);

        var result = await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.WriteLog.Should().NotContain(w => ChildrenOfR.Any(c => c.Id == w.RecordId));
        _shares.MaskOf("sprk_document", DocR, User(UserC)).Should().Be(ViewOnly);
        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete);
        result.ChildrenNotUpdated.Should().Be(ChildrenOfR.Length);
    }

    [Fact]
    public async Task WhenAChildTablesSharesCannotBeRead_NothingOnThatTableIsWritten()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.FailBatchReadsOf = "sprk_document";

        var result = await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.WriteLog.Should().NotContain(w => w.EntitySet == "sprk_documents");
        _shares.MaskOf("sprk_event", EventR, User(UserA)).Should().Be(CollaborateOnChild, "other tables still mirror");
        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete);
    }

    [Fact]
    public async Task AChildWhoseRootsCannotBeDetermined_IsHeld_OnlyNarrowedNeverWidened()
    {
        // Filed to R AND to a project flagged secure whose provisioning never completed (C11).
        var heldDoc = Guid.NewGuid();
        var world = World()
            .FlaggedNotIsolatedRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", heldDoc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_document", heldDoc, User(UserB), FullAccessOnChild); // wider than R allows
        _shares.Seed("sprk_document", heldDoc, User(UserC), ViewOnly);          // not on R at all

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", heldDoc, User(UserA)).Should().BeNull("a held child is never granted to");
        _shares.MaskOf("sprk_document", heldDoc, User(UserB)).Should().Be(ViewOnly, "narrowed to what R allows");
        _shares.MaskOf("sprk_document", heldDoc, User(UserC)).Should().BeNull("revoked: not shared on R");
        result.ChildrenHeld.Should().Be(1);
        result.IsComplete.Should().BeFalse();
    }

    /// <summary>
    /// Task 150, round 17 item 3: a root whose <c>sprk_issecure</c> comes back EMPTY (masked from this identity) fails
    /// CLOSED — read as flagged, so a child filed under it is HELD exactly like one under a flagged-not-isolated root,
    /// never mirrored as if that root were ordinary. The explicit-<c>false</c> twin below is the control.
    /// </summary>
    [Fact]
    public async Task AChildUnderARootWhoseSecureFlagReadsEmpty_IsHeld_NeverGranted()
    {
        var heldDoc = Guid.NewGuid();
        var world = World()
            .MaskedFlagNotIsolatedRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", heldDoc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", heldDoc, User(UserA)).Should().BeNull(
            "an empty flag is 'could not tell', so the child's secure roots are undetermined and it is never granted to");
        result.ChildrenHeld.Should().Be(1);
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AChildUnderARootExplicitlyFlaggedNo_IsMirroredFromItsSecureRoot()
    {
        var doc = Guid.NewGuid();
        var world = World()
            .OrdinaryRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", doc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", doc, User(UserA)).Should().Be(ViewOnly,
            "an ordinary root (flag read as No) contributes nothing, so the child carries its secure root's sharees");
        result.ChildrenHeld.Should().Be(0);
    }

    /// <summary>
    /// An unshare of the ROOT that lands while a reconcile is mid-run (it read the root earlier) is not undone: every
    /// grant is re-checked against the roots read fresh, right before it is written.
    /// </summary>
    [Fact]
    public async Task ARootUnshareThatLandsMidRun_IsNotUndoneByAStaleGrant()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        var racing = new UnshareOnFirstChildGrant(_shares, ProjectR, User(UserB));

        var world = World();
        await SecureChildShareWorld.SynchronizerOver(() => world, racing).SyncRootAsync("sprk_project", ProjectR, CancellationToken.None);

        racing.Fired.Should().BeTrue();
        var childrenWithB = ChildrenOfR.Count(c => _shares.MaskOf(c.Table, c.Id, User(UserB)) is not null);
        childrenWithB.Should().BeLessThanOrEqualTo(1,
            "only a child written before the unshare landed may carry B; every later grant re-reads the root");
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserA)).Should().Be(CollaborateOnChild);
    }

    /// <summary>Removes one principal's ROOT share the first time a child is granted anything — an unshare landing mid-run.</summary>
    private sealed class UnshareOnFirstChildGrant(FakeRecordShareTable inner, Guid rootId, DataversePrincipalRef principal)
        : IDataverseRecordShareService
    {
        public bool Fired { get; private set; }

        public async Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, string rights, CancellationToken ct = default)
        {
            await inner.GrantAccessAsync(entitySetName, recordId, p, rights, ct);
            if (!Fired && recordId != rootId)
            {
                Fired = true;
                await inner.RevokeAccessAsync("sprk_projects", rootId, principal, ct);
            }
        }

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, string rights, CancellationToken ct = default)
            => inner.ModifyAccessAsync(entitySetName, recordId, p, rights, ct);

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, CancellationToken ct = default)
            => inner.RevokeAccessAsync(entitySetName, recordId, p, ct);

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default)
            => inner.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default)
            => inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => inner.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
    }

    [Fact]
    public async Task AWriteDataverseAcceptedButDidNotKeep_IsNotCountedAsUpdated()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.IgnoreWrites = true; // every write "succeeds", nothing is stored

        var result = await World().Synchronizer(_shares).SyncRootAsync("sprk_project", ProjectR, CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete, "every changed child is read back");
        result.ChildrenUpdated.Should().Be(0);
        result.ChildrenNotUpdated.Should().Be(ChildrenOfR.Length);
    }

    [Fact]
    public async Task AReadFaultOnTheChildren_IsFailed_AndWritesNothing()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);

        var result = await World().FailingQueriesOf("sprk_todo").Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Failed);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutASecureRecordBusinessUnit_NothingIsReadOrWritten()
    {
        var result = await SecureChildShareWorld.WithoutSecureBusinessUnit().Synchronizer(_shares)
            .ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.NotApplicable);
        _shares.StrictReads.Should().Be(0);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncRoot_OnAnOrdinaryRoot_ReadsNoShareAndWritesNothing()
    {
        _shares.Seed("sprk_project", OrdinaryProject, User(UserA), FullAccess);

        var result = await World().Synchronizer(_shares).SyncRootAsync("sprk_project", OrdinaryProject, CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.NotApplicable);
        _shares.StrictReads.Should().Be(0);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncRoot_TouchesOnlyTheChildrenOfThatRoot()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR2, User(UserB), ViewOnly);

        await World().Synchronizer(_shares).SyncRootAsync("sprk_project", ProjectR, CancellationToken.None);

        _shares.WriteLog.Select(w => w.RecordId).Should().BeSubsetOf(ChildrenOfR.Select(c => c.Id));
        _shares.MaskOf("sprk_document", DocR2, User(UserB)).Should().BeNull("R2 is another root's run");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The share endpoints fan out (the BFF share path)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private readonly InternalUserShareTests.FakeSystemUsers _users = NewUsers();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _flags =
        new(defaultFlags: RootRecordFlags.None with { IsSecure = true });
    private readonly Mock<ITenantCache> _cache = new();

    private static InternalUserShareTests.FakeSystemUsers NewUsers()
    {
        var users = new InternalUserShareTests.FakeSystemUsers();
        foreach (var id in new[] { UserA, UserB, UserC, UserD })
            users.SeedPerson(id, $"User {id.ToString()[^1]}");
        return users;
    }

    private const AccessRights CallerHoldsEverything =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
        | AccessRights.Share;

    private Task<IResult> Share(SecureChildShareWorld world, Guid user, ExternalAccessLevel level,
        AccessRights callerRights = CallerHoldsEverything) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("project", ProjectR, user, level),
            _shares, _users.Client, _cache.Object, new InternalUserShareTests.StubCallerRightsProbe(callerRights),
            world.Synchronizer(_shares), SecureChildShareWorld.NobodyWalled(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(), // batch 4 integration (task 142)
            Context(), NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> Unshare(SecureChildShareWorld world, Guid user) =>
        InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("project", ProjectR, user),
            _shares, _users.Client, _flags, _cache.Object, Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            world.Synchronizer(_shares), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), Context(),
            NullLogger<Program>.Instance, CancellationToken.None);

    [Fact]
    public async Task ShareUser_OnASecureRoot_SharesEveryChild_AtTheSharedLevel()
    {
        var result = await Share(World(), UserB, ExternalAccessLevel.Collaborate);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().Be(Collaborate);
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().Be(CollaborateOnChild);
        _shares.MaskOf("sprk_document", DocR2, User(UserB)).Should().BeNull();
    }

    [Fact]
    public async Task ShareUser_ChangingTheLevel_ChangesItOnEveryChild()
    {
        var world = World();
        await Share(world, UserB, ExternalAccessLevel.ViewOnly);
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().Be(ViewOnly);

        var result = await Share(world, UserB, ExternalAccessLevel.FullAccess);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().Be(FullAccessOnChild);
    }

    [Fact]
    public async Task UnshareUser_RemovesTheUserFromEveryChild()
    {
        var world = World();
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate); // someone keeps the secure record (S5)
        await Share(world, UserB, ExternalAccessLevel.Collaborate);

        var result = await Unshare(world, UserB);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>()
            .Which.Value!.Removed.Should().BeTrue();
        foreach (var (table, id) in ChildrenOfR)
        {
            _shares.MaskOf(table, id, User(UserB)).Should().BeNull();
            _shares.MaskOf(table, id, User(UserA)).Should().Be(CollaborateOnChild, "A is still shared on R");
        }
    }

    [Fact]
    public async Task UnshareUser_OfAUserWithNoRootShare_StillRemovesTheirChildShares()
    {
        // An earlier unshare whose fan-out was incomplete: repeating it completes it.
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_document", DocR, User(UserB), ViewOnly);

        var result = await Unshare(World(), UserB);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Which.Value!.Removed.Should().BeFalse();
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().BeNull();
    }

    [Fact]
    public async Task ShareUser_WhenAChildCannotBeWritten_Is500ChildrenIncomplete_TheRootShareStands_AndReconcileCompletesIt()
    {
        var world = World();
        _shares.FailWritesOnRecord = ("sprk_events", EventR);

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["childrenInScope"].Should().Be(ChildrenOfR.Length);
        problem.ProblemDetails.Extensions["childrenNotUpdated"].Should().Be(1);
        problem.ProblemDetails.Extensions["childrenUpdated"].Should().Be(ChildrenOfR.Length - 1);
        problem.ProblemDetails.Detail.Should().Contain($"1 of its {ChildrenOfR.Length} related records");
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().Be(ViewOnly, "the root share stands");

        _shares.FailWritesOnRecord = null;
        var reconcile = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        reconcile.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        _shares.MaskOf("sprk_event", EventR, User(UserB)).Should().Be(ViewOnly);
    }

    /// <summary>
    /// V11: the 500's "or you can try again" is true — repeating the SAME share (the root already right, so the "unchanged"
    /// path) still fans out and completes the children the first attempt could not update, without waiting for the schedule.
    /// </summary>
    [Fact]
    public async Task ShareUser_RepeatedAfterAnIncompleteFanOut_CompletesTheChildren_ThroughTheUnchangedPath()
    {
        var world = World();
        _shares.FailWritesOnRecord = ("sprk_events", EventR);
        var first = await Share(world, UserB, ExternalAccessLevel.ViewOnly);
        first.Should().BeOfType<ProblemHttpResult>().Which.ProblemDetails.Detail.Should().Contain("try again");
        _shares.MaskOf("sprk_event", EventR, User(UserB)).Should().BeNull();
        _shares.FailWritesOnRecord = null;

        var again = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        again.Should().BeOfType<Ok<ShareRecordWithUserResponse>>()
            .Which.Value!.Outcome.Should().Be(InternalShareEndpoints.OutcomeUnchanged, "the root already held the share");
        _shares.MaskOf("sprk_event", EventR, User(UserB)).Should().Be(ViewOnly, "the repeat completed the fan-out");
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().Be(ViewOnly);
    }

    [Fact]
    public async Task UnshareUser_WhenAChildCannotBeWritten_Is500_AndTheRootUnshareIsNotRolledBack()
    {
        var world = World();
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        await Share(world, UserB, ExternalAccessLevel.Collaborate);
        _shares.FailWritesOnRecord = ("sprk_todos", TodoR);

        var result = await Unshare(world, UserB);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["removed"].Should().Be(true);
        problem.ProblemDetails.Extensions["childrenNotUpdated"].Should().Be(1);
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().BeNull("removing access is never rolled back");
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().BeNull();
    }

    /// <summary>A held child is only narrowed: a share fans out to everything EXCEPT it, and the caller is told why.</summary>
    [Fact]
    public async Task ShareUser_WithAHeldChild_Is500ChildrenIncomplete_NamingTheHeldChild_AndNeverGrantsIt()
    {
        var heldDoc = Guid.NewGuid();
        var world = World()
            .FlaggedNotIsolatedRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", heldDoc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions["childrenHeld"].Should().Be(1);
        problem.ProblemDetails.Detail.Should().Contain("cannot be matched to their secure record");
        _shares.MaskOf("sprk_document", heldDoc, User(UserB)).Should().BeNull();
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(ViewOnly);
    }

    /// <summary>...but an UNSHARE does reach a held child (a principal the known roots do not share is revoked), so it succeeds.</summary>
    [Fact]
    public async Task UnshareUser_WithAHeldChild_RemovesTheUserFromIt_AndSucceeds()
    {
        var heldDoc = Guid.NewGuid();
        var world = World()
            .FlaggedNotIsolatedRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", heldDoc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_document", heldDoc, User(UserB), ViewOnly);

        var result = await Unshare(world, UserB);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Which.Value!.Removed.Should().BeTrue();
        _shares.MaskOf("sprk_document", heldDoc, User(UserB)).Should().BeNull();
    }

    [Fact]
    public async Task ShareUser_WhenTheChildrensSharesCannotBeRead_Is500ChildrenIncomplete_NotABare500()
    {
        _shares.FailBatchReadsOf = "sprk_document";

        var result = await Share(World(), UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
        _shares.WriteLog.Should().NotContain(w => w.EntitySet == "sprk_documents");
    }

    [Fact]
    public async Task ShareUser_WhenTheRootsSharesCannotBeRead_IsReadFailed_AndNoChildIsWritten()
    {
        _shares.FailReadsOfRecord = ("sprk_project", ProjectR);

        var result = await Share(World(), UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ReadFailedReasonCode);
        problem.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task ShareUser_WhenTheCallerCannotGrant_FansOutNothing()
    {
        var result = await Share(World(), UserB, ExternalAccessLevel.ViewOnly, callerRights: AccessRights.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task ShareUser_NarrowedToTheCallersRights_MirrorsTheNarrowedShare()
    {
        // The caller holds Read and Write only: Collaborate is narrowed on the root, and the children follow the ROOT.
        var result = await Share(World(), UserB, ExternalAccessLevel.Collaborate,
            callerRights: AccessRights.Read | AccessRights.Write);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Which.Value!.Narrowed.Should().BeTrue();
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().Be(3);
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(3);
    }

    [Fact]
    public async Task UnshareUser_OfTheLastReader_IsRefused_AndFansOutNothing()
    {
        var world = World();
        await Share(world, UserB, ExternalAccessLevel.Collaborate); // B is the only reader of secure R
        var writes = _shares.WriteLog.Count;

        var result = await Unshare(world, UserB);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(409);
        _shares.WriteLog.Should().HaveCount(writes);
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(CollaborateOnChild);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Task 149 r1 — failure outcomes at the endpoints (AC9: never a silent 200)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>F1: the synchronizer could not read the children at all (Failed) — the share stands, the caller is told.</summary>
    [Fact]
    public async Task ShareUser_WhenTheChildrenCannotBeReadAtAll_Is500ChildrenIncomplete_Failed_NotASilent200()
    {
        var result = await Share(World().FailingQueriesOf("sprk_todo"), UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["childrenStatus"].Should().Be(nameof(SecureChildShareSyncStatus.Failed));
        problem.ProblemDetails.Detail.Should().Contain("could not be read");
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().Be(ViewOnly, "the root share stands");
        _shares.WriteLog.Should().OnlyContain(w => w.RecordId == ProjectR, "nothing is written below an unread root");
    }

    /// <summary>
    /// F1: an UNSHARE whose fan-out could not read anything (here: the root's own row) — a silent 200 here would be an
    /// over-share nobody is told about, because the removed user keeps every child.
    /// </summary>
    [Fact]
    public async Task UnshareUser_WhenTheChildrenCannotBeReadAtAll_Is500ChildrenIncomplete_Failed_AndTheRootUnshareStands()
    {
        var world = World();
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        await Share(world, UserB, ExternalAccessLevel.Collaborate);
        world.FailingRowReadsOf("sprk_project", ProjectR);

        var result = await Unshare(world, UserB);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["childrenStatus"].Should().Be(nameof(SecureChildShareSyncStatus.Failed));
        problem.ProblemDetails.Extensions["removed"].Should().Be(true);
        problem.ProblemDetails.Detail.Should().Contain("may still open them");
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().BeNull("removing access is never rolled back");
    }

    /// <summary>
    /// F2: a child whose FILING cannot be read (here its second root's row) is not decided — nothing is written on it — and
    /// the unshare says so; it is never skipped silently while it still carries the removed user. The count is honest: it
    /// is one of the root's related records (found below it), so N of M has N ≤ M.
    /// </summary>
    [Fact]
    public async Task UnshareUser_WhenAChildsFilingCannotBeRead_Is500_NamingItInTheCounts_AndNothingIsWrittenOnIt()
    {
        var both = Guid.Parse("d0c00000-0000-4000-8000-0000000000b0");
        var world = World()
            .SecureChild("sprk_document", both,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2));
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_project", ProjectR2, User(UserB), ViewOnly);
        _shares.Seed("sprk_document", both, User(UserB), ViewOnly);
        world.FailingRowReadsOf("sprk_project", ProjectR2);

        var result = await Unshare(world, UserB);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["childrenNotUpdated"].Should().Be(1);
        problem.ProblemDetails.Extensions["childrenInScope"].Should().Be(ChildrenOfR.Length + 1);
        problem.ProblemDetails.Detail.Should().Contain($"1 of its {ChildrenOfR.Length + 1} related records");
        _shares.WriteLog.Should().NotContain(w => w.RecordId == both, "an undecided child is never written");
        _shares.MaskOf("sprk_document", both, User(UserB)).Should().Be(ViewOnly, "honestly reported, not hidden");
    }

    /// <summary>F2 (the reconcile): the same fault ends the run Incomplete, never Completed.</summary>
    [Fact]
    public async Task Reconcile_WhenAChildsFilingCannotBeRead_IsIncomplete_AndThatChildIsNotWritten()
    {
        _shares.Seed("sprk_project", ProjectR2, User(UserB), ViewOnly);
        _shares.Seed("sprk_document", DocR2, User(UserC), ViewOnly); // would be revoked if DocR2's filing were readable

        var result = await World().FailingRowReadsOf("sprk_project", ProjectR2).Synchronizer(_shares)
            .ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete);
        result.ChildrenNotUpdated.Should().Be(1);
        result.ChildrenNotUpdated.Should().BeLessThanOrEqualTo(result.ChildrenInScope);
        _shares.WriteLog.Should().NotContain(w => w.RecordId == DocR2);
        _shares.MaskOf("sprk_document", DocR2, User(UserC)).Should().Be(ViewOnly);
    }

    // ── F4 / F9: a scoped fan-out reads only the root's own related records ───────────────────────────────────────────

    /// <summary>
    /// F4/F9: a fault on an UNRELATED secure record (R2's row) does not touch a share on R — the scoped fan-out walks down
    /// from R and never reads R2's children, so neither their volume nor their faults reach this request.
    /// </summary>
    [Fact]
    public async Task ShareUser_IsNotAffectedByAFaultOnAnUnrelatedSecureRecord()
    {
        var world = World().FailingRowReadsOf("sprk_project", ProjectR2);

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserB)).Should().Be(ViewOnly);
        world.QueriedTables.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SyncRoot_ReachesAGrandchildThroughAUserOwnedRow_ButNotThroughAnOrdinaryTeamOwnedOne()
    {
        var userComm = Guid.Parse("c0c00000-0000-4000-8000-0000000000a1");
        var viaUser = Guid.Parse("a7a00000-0000-4000-8000-0000000000a1");
        var ordinaryComm = Guid.Parse("c0c00000-0000-4000-8000-0000000000a2");
        var viaOrdinary = Guid.Parse("a7a00000-0000-4000-8000-0000000000a2");
        var world = World()
            .UserOwnedChild("sprk_communication", userComm, ("sprk_regardingproject", "sprk_project", ProjectR))
            .SecureChild("sprk_communicationattachment", viaUser, ("sprk_communication", "sprk_communication", userComm))
            .OrdinaryChild("sprk_communication", ordinaryComm, ("sprk_regardingproject", "sprk_project", ProjectR))
            .SecureChild("sprk_communicationattachment", viaOrdinary, ("sprk_communication", "sprk_communication", ordinaryComm));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        await world.Synchronizer(_shares).SyncRootAsync("sprk_project", ProjectR, CancellationToken.None);

        _shares.MaskOf("sprk_communicationattachment", viaUser, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_communicationattachment", viaOrdinary, User(UserA)).Should().BeNull();
        _shares.WriteLog.Should().NotContain(w => w.RecordId == userComm || w.RecordId == ordinaryComm);
    }

    /// <summary>A child found below R that is ALSO under R2 still gets the intersection: the upward walk decides its roots.</summary>
    [Fact]
    public async Task ShareUser_OnAChildAlsoFiledUnderASecondSecureRoot_GivesOnlyTheIntersection()
    {
        var both = Guid.Parse("d0c00000-0000-4000-8000-0000000000b0");
        var world = World()
            .SecureChild("sprk_document", both,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2));

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_document", both, User(UserB)).Should().BeNull("B is not shared on R2, the child's other root");
    }

    // ── F7: an ordinary record is answered from its own row ────────────────────────────────────────────────────────────

    /// <summary>
    /// F7: on an ORDINARY record, a Secure Record setup that cannot be resolved (two business units carry the name) is
    /// irrelevant — the record's own owner team proves it is not secure, so the share answers 200 as before task 149.
    /// </summary>
    [Fact]
    public async Task ShareUser_OnAnOrdinaryRecord_IsNotRefused_WhenTheSecureRecordTeamCannotBeResolved()
    {
        var world = SecureChildShareWorld.Standard()
            .OrdinaryRoot("sprk_project", ProjectR)
            .Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName));

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        world.QueriedTables.Should().NotContain("businessunit", "an ordinary record never needs the Secure Record setup");
    }

    /// <summary>F7's twin: on a SECURE record the same ambiguity is still a refusal (fail closed), named to the caller.</summary>
    [Fact]
    public async Task ShareUser_OnASecureRecord_WhenTheSecureRecordTeamCannotBeResolved_Is500ChildrenIncomplete_Failed()
    {
        var world = World().Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName));

        var result = await Share(world, UserB, ExternalAccessLevel.ViewOnly);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        problem.ProblemDetails.Extensions["childrenStatus"].Should().Be(nameof(SecureChildShareSyncStatus.Failed));
        _shares.WriteLog.Should().OnlyContain(w => w.RecordId == ProjectR);
    }

    // ── F5: the fresh re-check intersects EVERY root ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// F5: a child under TWO secure roots; an unshare of B on EITHER root lands mid-run, after both roots' shares were
    /// read. The fresh re-check before a grant must intersect both roots, whichever one changed.
    /// </summary>
    [Theory]
    [InlineData("R")]
    [InlineData("R2")]
    public async Task ARootUnshareMidRun_OnEitherOfTwoRoots_IsNotUndoneByAStaleGrant(string changedRoot)
    {
        var first = Guid.Parse("d0c00000-0000-4000-8000-0000000000f1");  // granted first: fires the race
        var second = Guid.Parse("d0c00000-0000-4000-8000-0000000000f2"); // written after the race, from stale reads
        var world = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_project", ProjectR)
            .SecureRoot("sprk_project", ProjectR2)
            .SecureChild("sprk_document", first,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2))
            .SecureChild("sprk_document", second,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2));
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_project", ProjectR2, User(UserB), ViewOnly);
        var racing = new UnshareOnFirstChildGrant(_shares, changedRoot == "R" ? ProjectR : ProjectR2, User(UserB));

        await SecureChildShareWorld.SynchronizerOver(() => world, racing).ReconcileAllAsync(CancellationToken.None);

        racing.Fired.Should().BeTrue();
        _shares.MaskOf("sprk_document", second, User(UserB)).Should().BeNull(
            $"B left {changedRoot} before the second child was written; the fresh re-check must see it on every root");
    }

    // ── F6: inherited-only rows ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F6: a POA row with no DIRECT rights (mask 0 — inherited access only) is not a share: it is never revoked (it is
    /// not this synchronizer's), and a sharee holding only such a row is GRANTED (never ModifyAccess on a non-share).
    /// </summary>
    [Fact]
    public async Task Reconcile_IgnoresInheritedOnlyRows_NeitherRevokingThemNorModifyingThem()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_document", DocR, User(UserC), 0); // inherited only, not on R
        _shares.Seed("sprk_document", DocR, User(UserA), 0); // inherited only, A IS on R

        var result = await World().Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        var write = _shares.WriteLog.Where(w => w.RecordId == DocR).Should().ContainSingle().Subject;
        write.Action.Should().Be("GrantAccess", "a principal holding no DIRECT share is granted, never modified");
        write.Principal.Should().Be(User(UserA));
        write.Rights.Should().Be(ViewCsv);
        _shares.MaskOf("sprk_document", DocR, User(UserC)).Should().Be(0, "an inherited-only row is not revoked");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Task 149 r2 — the fail-closed guards the verifier could remove with every test still green
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// V6: the run read the root, but the FRESH re-read right before a grant fails. Nothing that adds a right is written —
    /// no grant, no widening — while what only removes access (a revoke, a narrowing) still is; every such child is "not
    /// updated", so the next run retries.
    /// </summary>
    [Fact]
    public async Task WhenTheFreshReReadOfTheRootFails_NothingIsGrantedOrWidened_ButRevokesAndNarrowingsStand()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_event", EventR, User(UserA), ViewOnly);           // needs a WIDENING to 23
        _shares.Seed("sprk_document", DocR, User(UserB), FullAccessOnChild); // needs a NARROWING to View
        _shares.Seed("sprk_document", DocR, User(UserC), ViewOnly);          // needs a REVOKE
        var race = new RootReadRace(_shares, "sprk_project", ProjectR, afterFirstRead: null, failLaterReads: true);

        var world = World();
        var result = await SecureChildShareWorld.SynchronizerOver(() => world, race).ReconcileAllAsync(CancellationToken.None);

        race.RootReads.Should().BeGreaterThan(1, "the grants reached the fresh re-read, which failed");
        _shares.WriteLog.Should().NotContain(w => w.Action == "GrantAccess", "a grant decided from a stale read is never written");
        _shares.MaskOf("sprk_event", EventR, User(UserA)).Should().Be(ViewOnly, "a widening is not written either");
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(ViewOnly, "a narrowing only removes access");
        _shares.MaskOf("sprk_document", DocR, User(UserC)).Should().BeNull("a revoke only removes access");
        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete);
        result.ChildrenNotUpdated.Should().Be(ChildrenOfR.Length);
    }

    /// <summary>
    /// V1: a WIDENING (ModifyAccess on an existing child share) is re-checked against the roots read fresh too — not only a
    /// grant. Every child of R already carries A at View, so the run plans widenings only; the root's A is narrowed or
    /// removed right after the run read it. No child may end up wider than the root as it now stands.
    /// </summary>
    [Theory]
    [InlineData("narrowed")]
    [InlineData("unshared")]
    public async Task ARootChangeThatLandsMidRun_IsNotUndoneByAStaleWidening(string change)
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        foreach (var (table, id) in ChildrenOfR)
            _shares.Seed(table, id, User(UserA), ViewOnly);
        var race = new RootReadRace(_shares, "sprk_project", ProjectR,
            afterFirstRead: change == "narrowed"
                ? () => _shares.ModifyAccessAsync("sprk_projects", ProjectR, User(UserA), ViewCsv)
                : () => _shares.RevokeAccessAsync("sprk_projects", ProjectR, User(UserA)),
            failLaterReads: false);

        var world = World();
        await SecureChildShareWorld.SynchronizerOver(() => world, race).ReconcileAllAsync(CancellationToken.None);

        foreach (var (table, id) in ChildrenOfR)
        {
            _shares.MaskOf(table, id, User(UserA)).Should().Be(change == "narrowed" ? ViewOnly : null,
                $"{table} {id} must follow R as it stands after the change, not the run's earlier read");
        }

        _shares.WriteLog.Where(w => w.RecordId != ProjectR)
            .Should().NotContain(w => w.Rights != null && w.Rights.Contains("WriteAccess"));
    }

    /// <summary>
    /// The FIRST strict share read of one root answers as the share table stood, then <c>afterFirstRead</c> runs (a change
    /// landing right after the run read the root); with <c>failLaterReads</c> every later strict read of that root throws
    /// (the fresh re-read failing). Every other call passes through.
    /// </summary>
    private sealed class RootReadRace(
        FakeRecordShareTable inner, string rootTable, Guid rootId, Func<Task>? afterFirstRead, bool failLaterReads)
        : IDataverseRecordShareService
    {
        public int RootReads { get; private set; }

        public async Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
            string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            if (entityLogicalName != rootTable || recordId != rootId)
                return await inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);

            RootReads++;
            if (RootReads > 1 && failLaterReads)
                throw new InvalidOperationException("Test: the fresh re-read of the root failed.");

            var answer = await inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);
            if (RootReads == 1 && afterFirstRead is not null)
                await afterFirstRead();
            return answer;
        }

        public Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, string rights, CancellationToken ct = default)
            => inner.GrantAccessAsync(entitySetName, recordId, p, rights, ct);

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, string rights, CancellationToken ct = default)
            => inner.ModifyAccessAsync(entitySetName, recordId, p, rights, ct);

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef p, CancellationToken ct = default)
            => inner.RevokeAccessAsync(entitySetName, recordId, p, ct);

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default)
            => inner.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => inner.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
    }

    /// <summary>
    /// V2: a child whose ONLY root does not exist (deleted, or a dangling lookup) is HELD, not "outside secure roots": with
    /// no known root nothing is shared, so every share on it — an ex-sharee's included — is revoked, and nobody is added.
    /// </summary>
    [Fact]
    public async Task AChildWhoseRootIsMissing_IsHeld_AndEveryShareOnItIsRevoked()
    {
        var missingProject = Guid.Parse("a1000000-0000-4000-8000-0000000000ee");
        var orphan = Guid.Parse("d0c00000-0000-4000-8000-0000000000c1");
        var world = World().SecureChild("sprk_document", orphan, ("sprk_project", "sprk_project", missingProject));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_document", orphan, User(UserC), FullAccessOnChild); // an ex-sharee's stale share

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", orphan, User(UserC)).Should().BeNull("with no known root, nobody keeps a share");
        _shares.MaskOf("sprk_document", orphan, User(UserA)).Should().BeNull("a held child is never granted to");
        result.ChildrenHeld.Should().Be(1);
        result.ChildrenOutsideSecureRoots.Should().Be(1, "only the Secure-team-owned document under the ORDINARY project");
        result.IsComplete.Should().BeFalse();
    }

    /// <summary>
    /// V3: a child filed under R through one lookup and under a MISSING intermediate record through another is held: its
    /// shares are only narrowed to what R allows — never granted or widened — because the missing record's root is unknown.
    /// </summary>
    [Fact]
    public async Task AChildWithAMissingIntermediateParent_IsHeld_OnlyNarrowedNeverWidened()
    {
        var missingCommunication = Guid.Parse("c0c00000-0000-4000-8000-0000000000ee");
        var attachment = Guid.Parse("a7a00000-0000-4000-8000-0000000000c1");
        var world = World().SecureChild("sprk_communicationattachment", attachment,
            ("sprk_communication", "sprk_communication", missingCommunication), ("sprk_document", "sprk_document", DocR));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);
        _shares.Seed("sprk_communicationattachment", attachment, User(UserB), FullAccessOnChild); // wider than R allows
        _shares.Seed("sprk_communicationattachment", attachment, User(UserC), ViewOnly);          // not on R at all

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_communicationattachment", attachment, User(UserA)).Should().BeNull("a held child is never granted to");
        _shares.MaskOf("sprk_communicationattachment", attachment, User(UserB)).Should().Be(ViewOnly, "narrowed to what R allows");
        _shares.MaskOf("sprk_communicationattachment", attachment, User(UserC)).Should().BeNull("revoked: not shared on R");
        result.ChildrenHeld.Should().Be(1);
    }

    /// <summary>
    /// V4: a child filed through a chain deeper than the walk follows is held, not "outside secure roots" — otherwise it
    /// would be left untouched and keep a stale share (an ex-sharee's) indefinitely. With no root reached, every share is
    /// revoked and nobody is added. One level shallower, the root is reached and the child is mirrored normally.
    /// </summary>
    [Fact]
    public async Task AChildFiledDeeperThanTheWalkFollows_IsHeld_AndItsStaleShareIsRevoked()
    {
        // events[0] → events[1] → … → events[^1] → R: events[0] is MaxLineageDepth + 1 lookups from R, events[1] one fewer.
        var events = Enumerable.Range(0, SecureChildShareSynchronizer.MaxLineageDepth + 1)
            .Select(i => Guid.Parse($"e0e00000-0000-4000-8000-0000000001{i:D2}"))
            .ToArray();
        var world = World();
        for (var i = 0; i < events.Length - 1; i++)
            world.SecureChild("sprk_event", events[i], ("sprk_regardingevent", "sprk_event", events[i + 1]));
        world.SecureChild("sprk_event", events[^1], ("sprk_regardingproject", "sprk_project", ProjectR));
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_event", events[0], User(UserC), ViewOnly); // an ex-sharee's stale share

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_event", events[0], User(UserC)).Should().BeNull("a too-deep child is held, and held keeps nobody unknown");
        _shares.MaskOf("sprk_event", events[0], User(UserA)).Should().BeNull("a held child is never granted to");
        _shares.MaskOf("sprk_event", events[1], User(UserA)).Should().Be(ViewOnly, "one level shallower R is reached");
        result.ChildrenHeld.Should().Be(1);
        result.ChildrenOutsideSecureRoots.Should().Be(1, "only the Secure-team-owned document under the ORDINARY project");
    }

    /// <summary>
    /// V5: a child table with more rows than the page ceiling is not mirrored in part — the run fails and writes nothing.
    /// </summary>
    [Fact]
    public async Task AChildTableLargerThanThePageCeiling_FailsTheRun_AndNothingIsWritten()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        var world = World().EndlessPagesOf("sprk_todo");

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Failed);
        _shares.WriteLog.Should().BeEmpty();
        world.QueriedTables.Count(t => t == "sprk_todo").Should().Be(SecureChildShareSynchronizer.MaxPages,
            "it stops at the ceiling rather than reading forever");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Task 149 r3 — the third verifier's findings 1 and 2, and the merge with task 143 (AC6)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finding 1: a HELD child is never widened. B already holds View on a document filed under R AND under a project
    /// flagged secure but never isolated (so its roots cannot be determined); R gives B Collaborate. The known roots would
    /// allow the widening — and the fresh re-check reads only the known roots, so it would let it through — but a held
    /// child is only ever narrowed: B stays at View.
    /// </summary>
    [Fact]
    public async Task AHeldChild_WhoseShareIsNarrowerThanTheKnownRootsAllow_IsNeverWidened()
    {
        var heldDoc = Guid.NewGuid();
        var world = World()
            .FlaggedNotIsolatedRoot("sprk_project", FlaggedProject)
            .SecureChild("sprk_document", heldDoc,
                ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", FlaggedProject));
        _shares.Seed("sprk_project", ProjectR, User(UserB), Collaborate);
        _shares.Seed("sprk_document", heldDoc, User(UserB), ViewOnly); // narrower than R allows

        var result = await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", heldDoc, User(UserB)).Should().Be(ViewOnly, "a held child is never widened");
        _shares.WriteLog.Should().NotContain(w => w.RecordId == heldDoc, "nothing on the held child needed narrowing");
        result.ChildrenHeld.Should().Be(1);
        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(CollaborateOnChild, "R's other children still mirror");
    }

    /// <summary>
    /// Finding 2: when the fresh re-read of the root fails, a MIXED change — one that adds rights and removes others — is
    /// not dropped whole. A holds Read + Delete on the event while R gives A Collaborate (no Delete): the widening part
    /// (Write, Append, AppendTo) is not written from the stale read, but the narrowing part (Delete) still goes.
    /// </summary>
    [Fact]
    public async Task WhenTheFreshReReadFails_AMixedChange_KeepsItsNarrowingPart_AndDropsOnlyItsWideningPart()
    {
        const int ReadAndDelete = 65537;
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_event", EventR, User(UserA), ReadAndDelete);
        var race = new RootReadRace(_shares, "sprk_project", ProjectR, afterFirstRead: null, failLaterReads: true);

        var world = World();
        var result = await SecureChildShareWorld.SynchronizerOver(() => world, race).ReconcileAllAsync(CancellationToken.None);

        race.RootReads.Should().BeGreaterThan(1, "the change reached the fresh re-read, which failed");
        _shares.MaskOf("sprk_event", EventR, User(UserA)).Should().Be(ViewOnly,
            "Delete is removed (R no longer gives it); Write/Append/AppendTo are not added from a stale read");
        var eventWrite = _shares.WriteLog.Where(w => w.RecordId == EventR).Should().ContainSingle().Subject;
        eventWrite.Action.Should().Be("ModifyAccess");
        eventWrite.Rights.Should().Be(ViewCsv);
        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete, "the widening still has to happen next run");
    }

    // ── AC6: the No Access list (task 143) before any child grant or widening ─────────────────────────────────────────

    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();
    private readonly Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InMemoryContactIdentityStore _identities = new();

    /// <summary>Task 143's REAL guard over secure flags, the deny list above and the users seeded in the identity store.</summary>
    private SecureShareNoAccessGuard SecureGuard(params Guid[] knownUsers)
    {
        foreach (var user in knownUsers)
            _identities.AddSystemUser(user, oid: null, email: null);
        return new SecureShareNoAccessGuard(
            new GrantPolicyTestDoubles.FlagStubParticipationService(defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false)),
            _denyList, _identities, AssignedAccessTestDoubles.NoFilingRows(), NullLogger<SecureShareNoAccessGuard>.Instance);
    }

    /// <summary>
    /// AC6 (merge-order obligation item 3): B is on R's No Access list, but the enforcer has not removed B's ROOT share yet
    /// (or kept it as the last reader). Neither the root's fan-out nor the reconcile ever puts B on a child; A, shared on
    /// R and not walled, gets every child.
    /// </summary>
    [Theory]
    [InlineData("fan-out")]
    [InlineData("reconcile")]
    public async Task AWalledUser_WhoseRootShareIsNotYetRemoved_IsNeverGrantedAChildShare(string trigger)
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), Collaborate);
        _shares.Seed("sprk_project", ProjectR, User(UserB), Collaborate);
        _denyList.DenySystemUserOnRecord(UserB, ProjectR);
        var synchronizer = World().Synchronizer(_shares, SecureGuard(UserA, UserB));

        var result = trigger == "fan-out"
            ? await synchronizer.SyncRootAsync("sprk_project", ProjectR, CancellationToken.None)
            : await synchronizer.ReconcileAllAsync(CancellationToken.None);

        foreach (var (table, id) in ChildrenOfR)
        {
            _shares.MaskOf(table, id, User(UserB)).Should().BeNull($"B is walled off R, so never on its {table}");
            _shares.MaskOf(table, id, User(UserA)).Should().Be(CollaborateOnChild);
        }

        _shares.WriteLog.Should().NotContain(w => w.Principal == User(UserB));
        _shares.MaskOf("sprk_project", ProjectR, User(UserB)).Should().Be(Collaborate, "the ROOT share is the enforcer's to remove");
        result.Status.Should().Be(SecureChildShareSyncStatus.Completed, "leaving a walled user out is the mirror, not a failure");
    }

    /// <summary>
    /// AC6: when the walled user's grant was the ONLY change a child needed, nothing is written and the child is counted
    /// as unchanged — never "updated" with no write behind it.
    /// </summary>
    [Fact]
    public async Task WhenAWalledUsersGrantWasTheOnlyChange_NothingIsWritten_AndTheChildrenCountAsUnchanged()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserB), Collaborate);
        _denyList.DenySystemUserOnRecord(UserB, ProjectR);

        var result = await World().Synchronizer(_shares, SecureGuard(UserB))
            .SyncRootAsync("sprk_project", ProjectR, CancellationToken.None);

        _shares.WriteLog.Should().BeEmpty();
        result.ChildrenUpdated.Should().Be(0);
        result.ChildrenUnchanged.Should().Be(ChildrenOfR.Length);
        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
    }

    /// <summary>
    /// AC6: a walled user who already holds child shares is never WIDENED on them — a narrower share stays as it is — but
    /// a change that only removes rights still applies (B's Delete on the event goes, Write is not added).
    /// </summary>
    [Fact]
    public async Task AWalledUser_IsNeverWidenedOnAChild_ButANarrowingStillApplies()
    {
        const int ReadAndDelete = 65537;
        _shares.Seed("sprk_project", ProjectR, User(UserB), Collaborate);
        _shares.Seed("sprk_document", DocR, User(UserB), ViewOnly);
        _shares.Seed("sprk_event", EventR, User(UserB), ReadAndDelete);
        _denyList.DenySystemUserOnRecord(UserB, ProjectR);

        await World().Synchronizer(_shares, SecureGuard(UserB)).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", DocR, User(UserB)).Should().Be(ViewOnly, "never widened");
        _shares.MaskOf("sprk_event", EventR, User(UserB)).Should().Be(ViewOnly, "Delete removed, nothing added");
        _shares.WriteLog.Where(w => w.Principal == User(UserB))
            .Should().OnlyContain(w => w.Action == "ModifyAccess" && w.Rights == ViewCsv && w.RecordId == EventR);
    }

    /// <summary>
    /// AC6, fail closed (ADR-003): when the No Access check cannot be answered for B (B's identity cannot be read), B is
    /// given nothing and every child that needed B counts as NOT updated (the next run asks again); A is still mirrored.
    /// </summary>
    [Fact]
    public async Task WhenTheNoAccessCheckCannotBeAnswered_NothingIsGivenToThatUser_AndTheChildrenAreNotUpdated()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", ProjectR, User(UserB), ViewOnly);

        var result = await World().Synchronizer(_shares, SecureGuard(UserA)) // B unknown: the check is unverifiable
            .ReconcileAllAsync(CancellationToken.None);

        foreach (var (table, id) in ChildrenOfR)
        {
            _shares.MaskOf(table, id, User(UserB)).Should().BeNull();
            _shares.MaskOf(table, id, User(UserA)).Should().Be(ViewOnly);
        }

        result.Status.Should().Be(SecureChildShareSyncStatus.Incomplete);
        result.ChildrenNotUpdated.Should().Be(ChildrenOfR.Length);
    }

    /// <summary>
    /// AC6, the multi-root half (task 149 r4, verifier finding A): the No Access guard is asked about EVERY secure root of
    /// a child — "refused for ANY of the child's secure roots" — not only the first one, and its answer is remembered per
    /// (root, user), never per user. A document is filed under R (<c>sprk_project</c>) AND R2 (<c>sprk_relatedproject</c>);
    /// A and B are shared View on both; B is walled on ONE of them only. B never gets the two-root document, whichever root
    /// walls B and whichever trigger runs (the fan-out runs from the root that does NOT wall B), while A does — the document
    /// is otherwise mirrored. The wall is per record: B still gets the single-root child of the root that does not wall
    /// them, and never the walled root's own child.
    /// </summary>
    [Theory]
    [InlineData("R", "fan-out")]
    [InlineData("R2", "fan-out")]
    [InlineData("R", "reconcile")]
    [InlineData("R2", "reconcile")]
    public async Task AUserWalledOnOnlyOneOfAChildsTwoSecureRoots_IsNeverGrantedThatChild(string walledOn, string trigger)
    {
        var both = Guid.Parse("d0c00000-0000-4000-8000-0000000000c1");
        var world = World().SecureChild("sprk_document", both,
            ("sprk_project", "sprk_project", ProjectR), ("sprk_relatedproject", "sprk_project", ProjectR2));
        foreach (var root in new[] { ProjectR, ProjectR2 })
        {
            _shares.Seed("sprk_project", root, User(UserA), ViewOnly);
            _shares.Seed("sprk_project", root, User(UserB), ViewOnly);
        }

        var walledRoot = walledOn == "R" ? ProjectR : ProjectR2;
        var openRoot = walledOn == "R" ? ProjectR2 : ProjectR;
        var walledRootsOwnChild = walledOn == "R" ? DocR : DocR2;
        var openRootsOwnChild = walledOn == "R" ? DocR2 : DocR;
        _denyList.DenySystemUserOnRecord(UserB, walledRoot);
        var synchronizer = world.Synchronizer(_shares, SecureGuard(UserA, UserB));

        var result = trigger == "fan-out"
            ? await synchronizer.SyncRootAsync("sprk_project", openRoot, CancellationToken.None)
            : await synchronizer.ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", both, User(UserB)).Should().BeNull(
            $"B is walled on {walledOn}, one of the document's two secure roots");
        _shares.WriteLog.Should().NotContain(w => w.RecordId == both && w.Principal == User(UserB));
        _shares.MaskOf("sprk_document", both, User(UserA)).Should().Be(ViewOnly, "A, walled on neither root, is mirrored");
        _shares.MaskOf("sprk_document", openRootsOwnChild, User(UserB)).Should().Be(ViewOnly,
            "the wall is per record: the root that does not wall B still gives B its own child");
        _shares.MaskOf("sprk_document", walledRootsOwnChild, User(UserB)).Should().BeNull();
        result.Status.Should().Be(SecureChildShareSyncStatus.Completed, "leaving a walled user out is the mirror, not a failure");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Task 149 r1 — F3: thread participants are never granted a SECURE message (DirectThreadAccessService)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private DirectThreadAccessService MessageAccess(SecureChildShareWorld world, params Guid[] participants)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => world);
        entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_communicationthread")
            {
                Id = ThreadId,
                ["sprk_threadtype"] = new Microsoft.Xrm.Sdk.OptionSetValue(100000000), // record-anchored
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", UserA),
            });

        var derivation = new Mock<IThreadMembershipDerivationService>();
        derivation
            .Setup(d => d.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadAuthorizedSet
            {
                ThreadId = ThreadId,
                Participants = participants
                    .Select(p => new AuthorizedParticipant
                    {
                        Participant = ParticipantReference.SystemUser(p),
                        Reason = AuthorizationReason.RecordMembership,
                    })
                    .ToList(),
            });

        return new DirectThreadAccessService(
            entities.Object, _shares, new Lazy<IThreadMembershipDerivationService>(() => derivation.Object),
            SecureChildShareWorld.Configuration(), NullLogger<DirectThreadAccessService>.Instance);
    }

    private static readonly Guid ThreadId = Guid.Parse("7e7e0000-0000-4000-8000-000000000001");

    /// <summary>
    /// F3: a message on a SECURE record's thread. Its participants come from the record's membership lookups (here A, a
    /// sharee, and C, who is not shared on R): granting them would show C the message until the next reconcile, and the
    /// two writers would fight on every message. Nothing is granted; the reconcile then gives exactly R's sharees.
    /// </summary>
    [Fact]
    public async Task MessageAccess_OnASecureRecordsMessage_GrantsNoParticipant_AndTheReconcileGivesOnlyTheSharees()
    {
        var world = World();
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        await MessageAccess(world, UserA, UserC).GrantMessageAccessAsync(CommR, ThreadId);

        _shares.WriteLog.Should().BeEmpty("a secure message's shares are the secure-child synchronizer's alone");

        await world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_communication", CommR, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_communication", CommR, User(UserC)).Should().BeNull();
        _shares.WriteLog.Should().NotContain(w => w.Principal == User(UserC), "C was never granted, so nothing had to be revoked");
    }

    /// <summary>F3's twin: an ORDINARY (team-owned, not the Secure team) message keeps its participant grants.</summary>
    [Fact]
    public async Task MessageAccess_OnAnOrdinaryTeamOwnedMessage_StillGrantsEveryParticipant()
    {
        var ordinaryComm = Guid.Parse("c0c00000-0000-4000-8000-0000000000a3");
        var world = World().OrdinaryChild("sprk_communication", ordinaryComm,
            ("sprk_regardingproject", "sprk_project", OrdinaryProject));

        await MessageAccess(world, UserA, UserC).GrantMessageAccessAsync(ordinaryComm, ThreadId);

        _shares.MaskOf("sprk_communication", ordinaryComm, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_communication", ordinaryComm, User(UserC)).Should().Be(ViewOnly);
    }

    /// <summary>F3, fail closed: when it cannot be told whether the message is secure, nobody is granted.</summary>
    [Fact]
    public async Task MessageAccess_WhenTheSecureRecordTeamIsAmbiguous_OrTheMessageIsMissing_GrantsNothing()
    {
        var ambiguous = World().Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName));
        await MessageAccess(ambiguous, UserA, UserC).GrantMessageAccessAsync(CommR, ThreadId);

        await MessageAccess(World(), UserA, UserC).GrantMessageAccessAsync(Guid.NewGuid(), ThreadId);

        _shares.WriteLog.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The scheduled reconcile
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static SecureChildShareReconciliationJob Job(SecureChildShareSynchronizer synchronizer)
    {
        var services = new ServiceCollection().AddSingleton(synchronizer).BuildServiceProvider();
        return new SecureChildShareReconciliationJob(
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<SecureChildShareReconciliationJob>.Instance);
    }

    private static JobRunContext RunContext() =>
        new(Guid.NewGuid(), "corr-149", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    [Fact]
    public async Task Job_MirrorsEverySecureChild_AndReportsSuccess()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);

        var run = await Job(World().Synchronizer(_shares)).ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue();
        run.ProcessedItems.Should().Be(ChildrenOfR.Length + 3); // R's six, R2's one, the matter's and the WA's
        foreach (var (table, id) in ChildrenOfR)
            _shares.MaskOf(table, id, User(UserA)).Should().Be(ViewOnly);
    }

    [Fact]
    public async Task Job_WhenSomeChildrenAreNotUpdated_ReportsFailureWithoutThrowing()
    {
        _shares.Seed("sprk_project", ProjectR, User(UserA), ViewOnly);
        _shares.FailWritesOnRecord = ("sprk_documents", DocR);

        var run = await Job(World().Synchronizer(_shares)).ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain("1 child(ren) not updated");
    }

    [Fact]
    public async Task Job_WhenTheChildrenCannotBeRead_Throws_SoTheSchedulerRetries()
    {
        var job = Job(World().FailingQueriesOf("sprk_event").Synchronizer(_shares));

        var run = () => job.ExecuteAsync(RunContext(), CancellationToken.None);

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Nothing was written*");
        _shares.WriteLog.Should().BeEmpty();
    }

    private static HttpContext Context() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "00000000-0000-0000-0000-0000000000cc"),
            new Claim("oid", "66666666-6666-6666-6666-666666666666"),
        }, "test")),
        TraceIdentifier = "trace-149",
    };
}
