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
        var shared = new World();
        var service = shared.Service(shared.NewRequest());
        var together = new List<IReadOnlyDictionary<Guid, AccessRights>>();
        foreach (var entity in new[] { WorkAssignmentEntity, ProjectEntity, MatterEntity })
        {
            together.Add((await service.ComposeAsync(SystemUser(UserA), entity, CancellationToken.None)).Rights);
        }

        var alone = new List<IReadOnlyDictionary<Guid, AccessRights>>();
        foreach (var entity in new[] { WorkAssignmentEntity, ProjectEntity, MatterEntity })
        {
            var fresh = new World();
            alone.Add((await fresh.Service(fresh.NewRequest()).ComposeAsync(SystemUser(UserA), entity, CancellationToken.None)).Rights);
        }

        together.Should().HaveCount(3);
        for (var i = 0; i < 3; i++)
        {
            together[i].Should().Equal(alone[i]);
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
                new WallingReader(WalledContacts),
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

        private Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query)
        {
            var ids = query.Criteria.Conditions
                .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                .SelectMany(c => c.Values.Select(v => (Guid)v))
                .ToHashSet();
            var kind = query.ColumnSet.Columns.Contains("sprk_accesspermission") ? "parent" : "facts";
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
    private sealed class WallingReader(ConcurrentDictionary<Guid, Guid> walled) : INoAccessListReader
    {
        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            Guid? contactId, IReadOnlyCollection<Guid> organizationIds, IReadOnlyCollection<NoAccessCandidateRecord> candidates,
            CancellationToken ct = default)
            => GetDeniedRecordsAsync(NoAccessSubjects.ForContact(contactId, organizationIds), candidates, ct);

        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            var denied = candidates
                .Where(c => subjects.Contacts.Any(contact => walled.TryGetValue(contact, out var record) && record == c.RecordId))
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

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds.Distinct())
            {
                _flagReads.AddOrUpdate(id, 1, (_, n) => n + 1);
            }

            IReadOnlyDictionary<Guid, RootRecordFlags> answer = recordIds.Distinct().ToDictionary(
                id => id, id => id == SecureMatter ? new RootRecordFlags(IsSecure: true, IsRestricted: false) : RootRecordFlags.None);
            return Task.FromResult(answer);
        }

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds.Distinct())
            {
                _referenceReads.AddOrUpdate(id, 1, (_, n) => n + 1);
            }

            IReadOnlyDictionary<Guid, ReferencedOrganizations> answer = recordIds.Distinct()
                .ToDictionary(id => id, _ => new ReferencedOrganizations(Array.Empty<Guid>(), Unreadable: false));
            return Task.FromResult(answer);
        }
    }
}
