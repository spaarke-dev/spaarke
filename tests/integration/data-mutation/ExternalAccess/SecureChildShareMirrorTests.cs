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
            world.Synchronizer(_shares), Context(), NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> Unshare(SecureChildShareWorld world, Guid user) =>
        InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("project", ProjectR, user),
            _shares, _users.Client, _flags, _cache.Object, world.Synchronizer(_shares), Context(),
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
