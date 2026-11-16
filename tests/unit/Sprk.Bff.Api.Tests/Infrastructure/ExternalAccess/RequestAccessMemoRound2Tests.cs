// GitHub #2845 round 2 — per-ID request memoization of the access evaluator's batch reads, on the REAL composition.
//
// Round 1 made the subject-level reads (contact state, removed marker, grant set, organization memberships, identical deny-list
// chunks) once per request. The batch reads were still per composition: a work assignment's or project's secure ancestor (a
// matter) had its referenced organizations read again although the matter composition of the same request had just read
// them; the filing walk re-read the projects the project composition had walked; and the systemuser veto re-resolved the same
// principal's secure-record subjects in every composition. These tests pin, with counting wire doubles only:
//   1. call-count guards for the SYSTEMUSER plane (impersonated root sets) and the CIAM contact plane, three types in ONE request;
//   2. key isolation: two systemuser principals in one request each get their own (principal-dependent) answers;
//   3. every composed ACCESS answer equals the answer of the same composition made alone in a fresh request.

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public sealed class RequestAccessMemoRound2Tests
{
    private const string ProjectEntity = AccessibleRecordSetService.ProjectEntity;
    private const string MatterEntity = AccessibleRecordSetService.MatterEntity;
    private const string WorkAssignmentEntity = AccessibleRecordSetService.WorkAssignmentEntity;

    private static readonly Guid UserA = Guid.Parse("28452000-0000-4000-8000-0000000000a0");
    private static readonly Guid UserB = Guid.Parse("28452000-0000-4000-8000-0000000000b0");
    private static readonly Guid ContactOfA = Guid.Parse("28452000-0000-4000-8000-0000000000ca");
    private static readonly Guid ContactOfB = Guid.Parse("28452000-0000-4000-8000-0000000000cb");
    private static readonly Guid CiamContact = Guid.Parse("28452000-0000-4000-8000-0000000000cc");
    private static readonly Guid Firm = Guid.Parse("28452000-0000-4000-8000-0000000000f1");

    private static readonly Guid SecureMatter = Guid.Parse("28452000-0000-4000-8000-000000000101");
    private static readonly Guid OpenMatter = Guid.Parse("28452000-0000-4000-8000-000000000102");
    private static readonly Guid ProjectP = Guid.Parse("28452000-0000-4000-8000-000000000201");
    private static readonly Guid ProjectP2 = Guid.Parse("28452000-0000-4000-8000-000000000202");
    private static readonly Guid WorkUnderProject = Guid.Parse("28452000-0000-4000-8000-000000000301");
    private static readonly Guid WorkUnderSecureMatter = Guid.Parse("28452000-0000-4000-8000-000000000302");

    // ─────────────────────────────────────────────────────────────────────
    // 1. Call-count guards — three types composed in ONE request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SystemUserPlane_ThreeTypesInOneRequest_ReadsEachIdsBatchAnswerOnce()
    {
        var world = new World();
        var service = world.Service(world.NewRequest());

        var projects = await service.ComposeAsync(SystemUser(UserA), ProjectEntity, CancellationToken.None);
        var matters = await service.ComposeAsync(SystemUser(UserA), MatterEntity, CancellationToken.None);
        var workAssignments = await service.ComposeAsync(SystemUser(UserA), WorkAssignmentEntity, CancellationToken.None);

        // The access answers: nothing here is walled (ContactOfA is not on any list).
        projects.Rights.Keys.Should().BeEquivalentTo(new[] { ProjectP, ProjectP2 });
        matters.Rights.Keys.Should().BeEquivalentTo(new[] { SecureMatter, OpenMatter });
        workAssignments.Rights.Keys.Should().BeEquivalentTo(new[] { WorkUnderProject, WorkUnderSecureMatter });

        // Each id's own flags: once.
        world.Participations.FlagReadsOf(SecureMatter).Should().Be(1);
        world.Participations.FlagReadsOf(ProjectP).Should().Be(1);

        // The secure matter's referenced organizations: the matter composition's candidate AND the work-assignment
        // composition's secure ancestor — read ONCE per request (was 2).
        world.Participations.ReferenceReadsOf(SecureMatter).Should().Be(1,
            "a secure ancestor's organizations are the matter composition's own candidate read, shared per id");
        world.Participations.ReferenceReadsOf(ProjectP).Should().Be(1);

        // The project's filing: the project composition's level-1 row and the work assignment's level-2 row (it is filed
        // under the project) — read ONCE per request (was 2).
        world.Filing.FactsReadsOf(ProjectEntity, ProjectP).Should().Be(1,
            "the filing walk reads a row once per request, whichever composition reaches it first");
        world.Filing.FactsReadsOf(WorkAssignmentEntity, WorkUnderProject).Should().Be(1);
        world.Filing.ParentReadsOf(MatterEntity, SecureMatter).Should().Be(1);

        // The systemuser's secure-record subjects: needed by the matter composition (a secure matter) AND the work-assignment
        // composition (a work assignment below it) — resolved ONCE per request (was 2).
        world.SystemUserReadsOf(UserA).Should().Be(1, "the principal's secure-veto subjects are resolved once per request");
    }

    [Fact]
    public async Task CiamPlane_ThreeTypesInOneRequest_ReadsEachIdsBatchAnswerOnce()
    {
        var world = new World();
        var service = world.Service(world.NewRequest());

        var composed = (await CiamContactPrincipalStrategy.ComposeAsync(service, CiamContact, null, null, CancellationToken.None))
            .Principal;

        composed.ProjectAccess.Select(p => p.ProjectId).Should().BeEquivalentTo(new[] { ProjectP });
        composed.MatterAccess.Keys.Should().BeEquivalentTo(new[] { SecureMatter });
        composed.WorkAssignmentAccess.Keys.Should().BeEquivalentTo(new[] { WorkUnderSecureMatter });

        // The secure matter: the matter composition's candidate and the work assignment's secure ancestor — once (was 2).
        world.Participations.ReferenceReadsOf(SecureMatter).Should().Be(1,
            "a secure ancestor's organizations are the matter composition's own candidate read, shared per id");
        world.Participations.FlagReadsOf(SecureMatter).Should().Be(1);
        world.Participations.FlagReadsOf(WorkUnderSecureMatter).Should().Be(1);
        world.Participations.OrganizationMembershipReads.Should().Be(1, "round 1: the subject's memberships, once per request");
        world.Filing.ParentReadsOf(MatterEntity, SecureMatter).Should().Be(1);
    }

    [Fact]
    public async Task EveryComposedAnswer_EqualsTheSameCompositionMadeAloneInAFreshRequest()
    {
        // #2845 review T2: with a wall (ContactOfA off the secure matter) and unprovable answers (a project whose flags are
        // unreadable, a matter whose organizations are unresolved), so the veto and the fail-closed paths are compared too.
        static World Walled()
        {
            var world = new World();
            world.WalledContacts[ContactOfA] = SecureMatter;
            world.Participations.UnreadableFlags.Add(ProjectP2);
            world.Participations.UnresolvedReferences.Add(OpenMatter);
            return world;
        }

        var shared = Walled();
        var service = shared.Service(shared.NewRequest());
        var together = new List<IReadOnlyDictionary<Guid, AccessRights>>();
        foreach (var entity in new[] { WorkAssignmentEntity, ProjectEntity, MatterEntity })
        {
            together.Add((await service.ComposeAsync(SystemUser(UserA), entity, CancellationToken.None)).Rights);
        }

        var alone = new List<IReadOnlyDictionary<Guid, AccessRights>>();
        foreach (var entity in new[] { WorkAssignmentEntity, ProjectEntity, MatterEntity })
        {
            var fresh = Walled();
            alone.Add((await fresh.Service(fresh.NewRequest()).ComposeAsync(SystemUser(UserA), entity, CancellationToken.None)).Rights);
        }

        together.Should().HaveCount(3);
        for (var i = 0; i < 3; i++)
        {
            together[i].Should().Equal(alone[i]);
        }

        together[2].Should().NotContainKey(SecureMatter, "the wall applies to the composed answer as it does alone");
    }

    // ─────────────────────────────────────────────────────────────────────
    // T1. Write-time checks read live: never a per-id memo entry
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteTimeCheck_AfterAComposition_SeesAChangedReferencedOrganization_NotTheMemoizedOne()
    {
        var world = new World();
        var service = world.Service(world.NewRequest());

        // The composition fills the memo: the secure matter references no organization.
        (await service.ComposeAsync(SystemUser(UserA), MatterEntity, CancellationToken.None)).Rights.Keys
            .Should().Contain(SecureMatter);
        var readsByTheComposition = world.Participations.ReferenceReadsOf(SecureMatter);
        readsByTheComposition.Should().Be(1);

        // Dataverse changes: the matter now references a walled organization.
        world.Participations.References[SecureMatter] = new[] { Firm };
        world.WalledOrganizations[Firm] = true;

        var answer = await service.CheckGranteeNoAccessAsync(MatterEntity, SecureMatter, CiamContact, Array.Empty<Guid>(), CancellationToken.None);

        answer.Should().Be(NoAccessCheckAnswer.Denied, "the write-time check reads the organizations live, never from the memo");
        world.Participations.ReferenceReadsOf(SecureMatter).Should().Be(readsByTheComposition + 1);
    }

    [Fact]
    public async Task WriteTimeCheck_AfterAComposition_SeesAParentReadThatNowFaults_NotTheMemoizedParent()
    {
        var world = new World();
        var service = world.Service(world.NewRequest());

        // The composition fills the memo with the work assignment's secure parent (read once, proven).
        await service.ComposeAsync(SystemUser(UserA), WorkAssignmentEntity, CancellationToken.None);
        world.Filing.ParentReadsOf(MatterEntity, SecureMatter).Should().Be(1);

        world.Filing.ParentReadsFault = true;

        var answer = await service.CheckGranteeNoAccessAsync(
            WorkAssignmentEntity, WorkUnderSecureMatter, CiamContact, Array.Empty<Guid>(), CancellationToken.None);

        answer.Should().Be(NoAccessCheckAnswer.Unverifiable, "the write-time walk reads the parent live; its fault is reported");
    }

    // ─────────────────────────────────────────────────────────────────────
    // T3. Overlapping compositions: the join-in-flight path and a cancelled in-flight read
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CiamPlane_OverlappingCompositions_JoinTheInFlightRead_AndReadEachIdOnce()
    {
        var world = new World();
        var http = world.NewRequest();
        var service = world.Service(http);
        world.Participations.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Participations.HeldId = SecureMatter;

        var composing = CiamContactPrincipalStrategy.ComposeAsync(service, CiamContact, null, null, CancellationToken.None);

        // The secure matter's organizations: one composition reads them (held), the other — the work assignment's secure
        // ancestor, or the matter's own candidate — joins that read while it is still in flight.
        await world.Participations.HeldReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var memo = RequestAccessMemo.For(http.HttpContext)!;
        await WaitUntil(() => memo.InFlightJoins >= 1, "a second composition joins the held read");
        world.Participations.Hold.SetResult();

        var composed = (await composing).Principal;
        composed.MatterAccess.Keys.Should().BeEquivalentTo(new[] { SecureMatter });
        composed.WorkAssignmentAccess.Keys.Should().BeEquivalentTo(new[] { WorkUnderSecureMatter });
        world.Participations.ReferenceReadsOf(SecureMatter).Should().Be(1, "the joiner was served the in-flight read");
    }

    [Fact]
    public async Task ACancelledInFlightRead_IsReadAgainByTheCompositionThatJoinedIt()
    {
        var world = new World();
        var http = world.NewRequest();
        var service = world.Service(http);
        world.Participations.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Participations.HeldId = SecureMatter;

        // The matter composition owns the secure matter's organizations read (held) and is then cancelled by its caller.
        using var cancelMatter = new CancellationTokenSource();
        var matter = service.ComposeAsync(SystemUser(UserA), MatterEntity, cancelMatter.Token);
        await world.Participations.HeldReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The work-assignment composition reaches the same read (its secure ancestor) and joins it in flight.
        var memo = RequestAccessMemo.For(http.HttpContext)!;
        var workAssignments = service.ComposeAsync(SystemUser(UserA), WorkAssignmentEntity, CancellationToken.None);
        await WaitUntil(() => memo.InFlightJoins >= 1, "the work-assignment composition joins the held read");

        cancelMatter.Cancel();

        await FluentActions.Awaiting(() => matter).Should().ThrowAsync<OperationCanceledException>();
        var composed = await workAssignments;

        var fresh = new World();
        var alone = await fresh.Service(fresh.NewRequest()).ComposeAsync(SystemUser(UserA), WorkAssignmentEntity, CancellationToken.None);
        composed.Rights.Should().Equal(alone.Rights, "the joiner read the cancelled id itself and answered as it would alone");
        world.Participations.ReferenceReadsOf(SecureMatter).Should().Be(2, "the cancelled read, then the joiner's own");
    }

    [Fact]
    public async Task ConcurrentFirstTouches_ThroughTheRealComposer_ShareOneMemo_AndAnswerAsAlone()
    {
        const int threadsPerType = 6;
        var expected = new Dictionary<string, IReadOnlyDictionary<Guid, AccessRights>>();
        foreach (var entity in new[] { ProjectEntity, MatterEntity, WorkAssignmentEntity })
        {
            var fresh = new World();
            expected[entity] = (await fresh.Service(fresh.NewRequest()).ComposeAsync(SystemUser(UserA), entity, CancellationToken.None)).Rights;
        }

        for (var repetition = 0; repetition < 20; repetition++)
        {
            var world = new World();
            var http = world.NewRequest(); // nothing has touched Items; the test pre-creates nothing
            var service = world.Service(http);
            using var barrier = new Barrier(3 * threadsPerType);
            var entities = new[] { ProjectEntity, MatterEntity, WorkAssignmentEntity }
                .SelectMany(entity => Enumerable.Repeat(entity, threadsPerType)).ToList();

            var runs = entities.Select(entity => Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                        return service.ComposeAsync(SystemUser(UserA), entity, CancellationToken.None);
                    },
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
                .ToList();
            var sets = await Task.WhenAll(runs);

            for (var i = 0; i < sets.Length; i++)
            {
                sets[i].Rights.Should().Equal(expected[entities[i]], $"repetition {repetition}: {entities[i]} answers as it does alone");
            }

            http.HttpContext!.Items.Values.OfType<RequestAccessMemo>().Should().ContainSingle($"one memo for the request (repetition {repetition})");
        }
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting: {because}.");
            }

            await Task.Delay(5);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Key isolation — two principals, impersonated reads, one request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoSystemUsersInOneRequest_EachGetsItsOwnImpersonatedSetAndItsOwnSecureVetoSubjects()
    {
        // A's primary contact is walled off the secure matter; B's is not. Both read the same matters (Dataverse's answer,
        // impersonated per principal). Composed in ONE request, A's subjects must never answer for B, nor B's for A.
        var world = new World();
        world.WalledContacts[ContactOfA] = SecureMatter;
        world.ImpersonatedMatters[UserB] = new[] { SecureMatter };
        var service = world.Service(world.NewRequest());

        var a = await service.ComposeAsync(SystemUser(UserA, withContact: false), MatterEntity, CancellationToken.None);
        var b = await service.ComposeAsync(SystemUser(UserB, withContact: false), MatterEntity, CancellationToken.None);
        var aAgain = await service.ComposeAsync(SystemUser(UserA, withContact: false), MatterEntity, CancellationToken.None);

        a.Rights.Keys.Should().BeEquivalentTo(new[] { OpenMatter }, "A's own contact is walled off the secure matter");
        b.Rights.Keys.Should().BeEquivalentTo(new[] { SecureMatter }, "B's impersonated set, and B's own subjects (not walled)");
        aAgain.Rights.Keys.Should().BeEquivalentTo(new[] { OpenMatter }, "B's answers were never served to A");

        world.ImpersonatedReads.Should().BeEquivalentTo(new[] { (UserA, MatterEntity), (UserB, MatterEntity), (UserA, MatterEntity) },
            "each principal's root set is its own impersonated read");
        world.SystemUserReadsOf(UserA).Should().Be(1, "A's subjects, once in the request");
        world.SystemUserReadsOf(UserB).Should().Be(1, "B's subjects are read for B, not served from A's");
    }

    // ─────────────────────────────────────────────────────────────────────
    // The world: the real composer, the real filing walk, counting wire doubles
    // ─────────────────────────────────────────────────────────────────────

    private static WorkforcePrincipal SystemUser(Guid systemUserId, bool withContact = true) => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = systemUserId,
        ContactId = withContact ? (systemUserId == UserA ? ContactOfA : ContactOfB) : null,
        Oid = systemUserId.ToString("D"),
        TenantId = "tenant",
    };

    private sealed class World
    {
        private readonly ConcurrentDictionary<Guid, int> _systemUserReads = new();

        public World()
        {
            Participations = new CountingParticipations();
            Filing = new CountingFiling(
                Row(WorkAssignmentEntity, WorkUnderProject, ("sprk_regardingproject", ProjectEntity, ProjectP)),
                Row(WorkAssignmentEntity, WorkUnderSecureMatter, ("sprk_regardingmatter", MatterEntity, SecureMatter)),
                Row(ProjectEntity, ProjectP),
                Row(ProjectEntity, ProjectP2),
                ParentRow(ProjectEntity, ProjectP, isSecure: false),
                ParentRow(MatterEntity, SecureMatter, isSecure: true));
        }

        public FixedAccessor Accessor { get; } = new(null);

        public CountingParticipations Participations { get; }

        public CountingFiling Filing { get; }

        /// <summary>A contact → the record a No Access row walls it off.</summary>
        public ConcurrentDictionary<Guid, Guid> WalledContacts { get; } = new();

        /// <summary>Organizations a No Access row walls off: a candidate referencing one is denied (T1).</summary>
        public ConcurrentDictionary<Guid, bool> WalledOrganizations { get; } = new();

        public ConcurrentDictionary<Guid, Guid[]> ImpersonatedMatters { get; } = new();

        public ConcurrentQueue<(Guid, string)> ImpersonatedReads { get; } = new();

        public int SystemUserReadsOf(Guid systemUserId) => _systemUserReads.GetValueOrDefault(systemUserId);

        public FixedAccessor NewRequest()
        {
            Accessor.HttpContext = new DefaultHttpContext();
            return Accessor;
        }

        public AccessibleRecordSetService Service(IHttpContextAccessor accessor)
        {
            Participations.SetAccessor(accessor);

            var store = new Mock<IContactIdentityStore>(MockBehavior.Strict);
            store.Setup(s => s.GetSystemUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) =>
                {
                    _systemUserReads.AddOrUpdate(id, 1, (_, n) => n + 1);
                    return new SystemUserLookup(LookupStatus.Read,
                        new SystemUserIdentityRow(id, null, null, null, id == UserA ? ContactOfA : ContactOfB, null));
                });
            store.Setup(s => s.ReadRemovedMarkerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RemovedMarkerRead.NotRemoved);

            var roots = new Mock<IImpersonatedRootSetSource>(MockBehavior.Strict);
            roots.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, string entity, CancellationToken _) =>
                {
                    ImpersonatedReads.Enqueue((user, entity));
                    var ids = entity switch
                    {
                        ProjectEntity => new[] { ProjectP, ProjectP2 },
                        MatterEntity => ImpersonatedMatters.TryGetValue(user, out var own) ? own : new[] { SecureMatter, OpenMatter },
                        _ => new[] { WorkUnderProject, WorkUnderSecureMatter },
                    };
                    return new RootIdSet(ids.ToHashSet(), Truncated: false);
                });

            return new AccessibleRecordSetService(
                new Mock<IMembershipResolverService>(MockBehavior.Strict).Object,
                Participations,
                new Mock<ISubjectStandingGrantReader>(MockBehavior.Strict).Object,
                new WallingReader(WalledContacts, WalledOrganizations),
                store.Object,
                InternalSystemUsers(),
                Filing.Object,
                NullLogger<AccessibleRecordSetService>.Instance,
                httpContextAccessor: accessor,
                impersonatedRootSets: roots.Object,
                externalAccessOptions: Options.Create(new ExternalAccessOptions
                {
                    ImpersonatedRootSets = new ImpersonatedRootSetsOptions { Enabled = true },
                }));
        }
    }

    /// <summary>The contact's OWN grants (a direct level), which a secure record keeps.</summary>
    private static IReadOnlyList<ExternalRootGrant> DirectGrants(params Guid[] ids) => ids
        .Select(id => new ExternalRootGrant
        {
            RecordId = id,
            AccessLevel = ExternalAccessLevel.ViewOnly,
            DirectAccessLevel = ExternalAccessLevel.ViewOnly,
        })
        .ToList();

    private static Entity Row(string table, Guid id, params (string Column, string ParentTable, Guid ParentId)[] filedUnder)
    {
        var row = new Entity(table, id) { ["sprk_issecure"] = false };
        foreach (var (column, parentTable, parentId) in filedUnder)
        {
            row[column] = new EntityReference(parentTable, parentId);
        }

        return row;
    }

    /// <summary>A matter or project as the walk's PARENT read sees it (flag, name, Access Permission).</summary>
    private static Entity ParentRow(string table, Guid id, bool isSecure)
    {
        var row = new Entity(table, id)
        {
            ["sprk_issecure"] = isSecure,
            ["sprk_accesspermission"] = new OptionSetValue(100000000),
            [table == MatterEntity ? "sprk_mattername" : "sprk_projectname"] = $"{table} {id}",
        };
        return row;
    }

    /// <summary>The filing walk's reads, answered from rows by table and primary-id condition, counted per id and kind.</summary>
    private sealed class CountingFiling
    {
        private readonly Entity[] _rows;
        private readonly ConcurrentDictionary<(string Kind, string Table, Guid Id), int> _reads = new();

        public CountingFiling(params Entity[] rows)
        {
            _rows = rows;
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => RetrieveMultipleAsync(query));
            Object = entities.Object;
        }

        public IGenericEntityService Object { get; }

        public int FactsReadsOf(string table, Guid id) => _reads.GetValueOrDefault(("facts", table, id));

        public int ParentReadsOf(string table, Guid id) => _reads.GetValueOrDefault(("parent", table, id));

        /// <summary>When set, every parent (flag / name / Access Permission) read faults (T1).</summary>
        public bool ParentReadsFault { get; set; }

        private Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query)
        {
            var ids = query.Criteria.Conditions
                .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                .SelectMany(c => c.Values.Select(v => (Guid)v))
                .ToHashSet();
            var kind = query.ColumnSet.Columns.Contains("sprk_accesspermission") ? "parent" : "facts";
            if (kind == "parent" && ParentReadsFault)
            {
                return Task.FromException<EntityCollection>(new HttpRequestException("parent read faulted"));
            }

            foreach (var id in ids)
            {
                _reads.AddOrUpdate((kind, query.EntityName, id), 1, (_, n) => n + 1);
            }

            // A parent read projects the flag/name/permission row; a facts read the filing row (both keyed by table + id).
            var rows = _rows
                .Where(r => string.Equals(r.LogicalName, query.EntityName, StringComparison.OrdinalIgnoreCase) && ids.Contains(r.Id))
                .Where(r => kind == "parent" ? r.Attributes.ContainsKey("sprk_accesspermission") : !r.Attributes.ContainsKey("sprk_accesspermission"))
                .Select(r =>
                {
                    var projected = new Entity(r.LogicalName, r.Id);
                    foreach (var column in query.ColumnSet.Columns)
                    {
                        if (r.Attributes.TryGetValue(column, out var value)) projected[column] = value;
                    }

                    return projected;
                })
                .ToList();
            return Task.FromResult(new EntityCollection(rows));
        }
    }

    /// <summary>A deny-list reader that walls a contact off one record; anything else is not denied.</summary>
    private sealed class WallingReader(ConcurrentDictionary<Guid, Guid> walled, ConcurrentDictionary<Guid, bool> walledOrganizations)
        : INoAccessListReader
    {
        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            Guid? contactId, IReadOnlyCollection<Guid> organizationIds, IReadOnlyCollection<NoAccessCandidateRecord> candidates,
            CancellationToken ct = default)
            => GetDeniedRecordsAsync(NoAccessSubjects.ForContact(contactId, organizationIds), candidates, ct);

        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            var denied = candidates
                .Where(c => subjects.Contacts.Any(contact => walled.TryGetValue(contact, out var record) && record == c.RecordId)
                            || (c.ReferencedOrganizationIds ?? Array.Empty<Guid>()).Any(walledOrganizations.ContainsKey))
                .Select(c => c.RecordId)
                .ToHashSet();
            return Task.FromResult(new NoAccessListResult
            {
                DeniedRecordIds = denied,
                DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                DenyingSubjectKinds = denied.ToDictionary(id => id, _ => NoAccessSubjectKinds.Contact),
                FailedClosed = false,
            });
        }
    }

    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class ForwardingAccessor : IHttpContextAccessor
    {
        public IHttpContextAccessor? Target { get; set; }

        public HttpContext? HttpContext
        {
            get => Target?.HttpContext;
            set => throw new NotSupportedException();
        }
    }

    /// <summary>The participation reads, counted per id: the CIAM contact's grants, one firm, flags with one secure matter.</summary>
    private sealed class CountingParticipations : ExternalParticipationService
    {
        private readonly ForwardingAccessor _accessor;
        private readonly ConcurrentDictionary<Guid, int> _flagReads = new(), _referenceReads = new();
        private int _orgReads;

        public CountingParticipations()
            : this(new ForwardingAccessor())
        {
        }

        private CountingParticipations(ForwardingAccessor accessor)
            : base(new HttpClient(), Mock.Of<ITenantCache>(), new ConfigurationBuilder().Build(), credential: null!, accessor,
                   NullLogger<ExternalParticipationService>.Instance, NoFilingEntities())
            => _accessor = accessor;

        public void SetAccessor(IHttpContextAccessor accessor) => _accessor.Target = accessor;

        public int FlagReadsOf(Guid id) => _flagReads.GetValueOrDefault(id);

        public int ReferenceReadsOf(Guid id) => _referenceReads.GetValueOrDefault(id);

        public int OrganizationMembershipReads => Volatile.Read(ref _orgReads);

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactRecordState.Active);

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(contactId == CiamContact
                ? new ExternalGrantSet
                {
                    Projects = new[] { new ExternalParticipation { ProjectId = ProjectP, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = ExternalAccessLevel.Collaborate } },
                    MatterGrants = DirectGrants(SecureMatter),
                    WorkAssignmentGrants = DirectGrants(WorkUnderSecureMatter),
                }
                : new ExternalGrantSet
                {
                    Projects = Array.Empty<ExternalParticipation>(),
                    MatterGrants = NoRootGrants,
                    WorkAssignmentGrants = NoRootGrants,
                });

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _orgReads);
            return Task.FromResult(new ActiveOrgMemberships(new[] { Firm }, new[] { Firm }, Unreadable: false));
        }

        /// <summary>Ids whose own flags read as unreadable (T2).</summary>
        public HashSet<Guid> UnreadableFlags { get; } = new();

        /// <summary>Ids whose referenced organizations read as unresolved (T2).</summary>
        public HashSet<Guid> UnresolvedReferences { get; } = new();

        /// <summary>Each id's referenced organizations (default none); changeable between reads (T1).</summary>
        public ConcurrentDictionary<Guid, Guid[]> References { get; } = new();

        /// <summary>T3: when set, the FIRST referenced-organization read that names <see cref="HeldId"/> waits on this gate
        /// (honoring its token); later reads answer at once.</summary>
        public TaskCompletionSource? Hold { get; set; }

        public Guid HeldId { get; set; }

        /// <summary>Signalled when the held read has started.</summary>
        public TaskCompletionSource HeldReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _heldOnce;

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds.Distinct())
            {
                _flagReads.AddOrUpdate(id, 1, (_, n) => n + 1);
            }

            IReadOnlyDictionary<Guid, RootRecordFlags> answer = recordIds.Distinct().ToDictionary(
                id => id, id => UnreadableFlags.Contains(id)
                    ? RootRecordFlags.Unreadable
                    : id == SecureMatter ? new RootRecordFlags(IsSecure: true, IsRestricted: false) : RootRecordFlags.None);
            return Task.FromResult(answer);
        }

        public override async Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds.Distinct())
            {
                _referenceReads.AddOrUpdate(id, 1, (_, n) => n + 1);
            }

            if (Hold is { } hold && recordIds.Contains(HeldId) && Interlocked.Exchange(ref _heldOnce, 1) == 0)
            {
                HeldReadStarted.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }

            return recordIds.Distinct().ToDictionary(id => id, id => UnresolvedReferences.Contains(id)
                ? ReferencedOrganizations.Unresolved
                : new ReferencedOrganizations(References.TryGetValue(id, out var orgs) ? orgs : Array.Empty<Guid>(), Unreadable: false));
        }
    }
}
