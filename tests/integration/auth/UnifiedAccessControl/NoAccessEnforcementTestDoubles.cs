using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test doubles for the No Access enforcer (unified-access-control-r2 task 143). Every double sits at a module boundary
/// (ADR-038: no HTTP doubles): the store's internal-virtual reads, the participation service's virtual reads, the
/// identity row store, the POA share table and the tenant cache. The PRODUCTION <see cref="NoAccessShareEnforcer"/>
/// runs between them.
/// </summary>
internal static class NoAccessEnforcementTestDoubles
{
    /// <summary>The enforcer's Dataverse reads, answered from in-memory rows, each switchable to a fault.</summary>
    internal sealed class FakeEnforcementStore : NoAccessEnforcementStore
    {
        public FakeEnforcementStore()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessEnforcementStore>.Instance)
        {
        }

        public ConcurrentDictionary<Guid, NoAccessEntrySnapshot> Entries { get; } = new();

        public ConcurrentDictionary<Guid, string> RecordTypeNames { get; } = new();

        public ConcurrentDictionary<Guid, Guid> SystemUsersByOid { get; } = new();

        public ConcurrentDictionary<Guid, EnforcementSystemUser> People { get; } = new();

        public ConcurrentDictionary<Guid, HashSet<Guid>> TeamMembers { get; } = new();

        public ConcurrentDictionary<Guid, Guid> OwningTeams { get; } = new();

        /// <summary>(principal, record) → rights Dataverse reports; unseeded = None.</summary>
        public ConcurrentDictionary<(Guid Principal, Guid RecordId), AccessRights> Rights { get; } = new();

        public bool FailEntryRead { get; set; }

        public bool FailEntryScan { get; set; }

        public bool FailPeopleRead { get; set; }

        public bool FailRightsRead { get; set; }

        public int? ScanCeilingOverride { get; set; }

        /// <summary>Every (principal, record) RetrievePrincipalAccess was asked about.</summary>
        public ConcurrentBag<(Guid Principal, Guid RecordId)> RightsReads { get; } = new();

        /// <summary>Adds an active (or inactive) entry from its parts and returns its id.</summary>
        /// <remarks><paramref name="objectRecordIdText"/> (task 154) stores the record id exactly as given (e.g. with
        /// braces), as a Web API writer or an import could; otherwise the canonical form is stored.</remarks>
        public Guid AddEntry(
            Guid? subjectUser = null, Guid? subjectContact = null, Guid? subjectOrganization = null,
            Guid? objectOrganization = null, (string LogicalName, Guid Id)? objectRecord = null,
            Guid? modifiedBy = null, int stateCode = 0, string? objectRecordIdText = null)
        {
            var id = Guid.NewGuid();
            Guid? typeRef = null;
            if (objectRecord is { } r)
            {
                typeRef = Guid.NewGuid();
                RecordTypeNames[typeRef.Value] = r.LogicalName;
            }

            Entries[id] = new NoAccessEntrySnapshot(
                new NoAccessEntryRow
                {
                    sprk_noaccessentryid = id,
                    _sprk_subjectsystemuser_value = subjectUser,
                    _sprk_subjectcontact_value = subjectContact,
                    _sprk_subjectorganization_value = subjectOrganization,
                    _sprk_objectorganization_value = objectOrganization,
                    _sprk_objectrecordtype_value = typeRef,
                    sprk_objectrecordid = objectRecordIdText ?? objectRecord?.Id.ToString(),
                },
                stateCode,
                modifiedBy);
            return id;
        }

        /// <summary>Seeds an enabled person.</summary>
        public void Person(Guid id, bool disabled = false, Guid? applicationId = null)
            => People[id] = new EnforcementSystemUser(id, disabled, applicationId);

        /// <summary>Task 143 r1: every entry id the enforcer read, in order — which entries a run enforced.</summary>
        public ConcurrentQueue<Guid> EntryReads { get; } = new();

        /// <summary>Task 143 r1: the record-owner read throws (residual access through team ownership unverifiable).</summary>
        public bool FailOwnerRead { get; set; }

        internal override Task<NoAccessEntrySnapshot?> ReadEntryAsync(Guid entryId, CancellationToken ct)
        {
            EntryReads.Enqueue(entryId);
            return FailEntryRead
                ? throw new HttpRequestException("Simulated entry read failure.")
                : Task.FromResult(Entries.TryGetValue(entryId, out var e) ? e : null);
        }

        internal override Task<(IReadOnlyList<Guid> Ids, bool Truncated)> ReadActiveEntryIdsAsync(int max, CancellationToken ct)
        {
            if (FailEntryScan)
                throw new HttpRequestException("Simulated entry scan failure.");

            var limit = ScanCeilingOverride ?? max;
            var ids = Entries.Values.Where(e => e.IsActive).Select(e => e.Row.sprk_noaccessentryid!.Value).ToList();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(
                ids.Count > limit ? (ids.Take(limit).ToList(), true) : (ids, false));
        }

        /// <summary>Task 064: runs as the covering-entry query answers — throw to fault it, or change rows after it.</summary>
        public Action? CoveringHook { get; set; }

        internal override Task<(IReadOnlyList<Guid> Ids, bool Truncated)> ReadActiveEntryIdsCoveringAsync(
            Guid recordId, IReadOnlyCollection<Guid> organizationIds, int max, CancellationToken ct)
        {
            CoveringHook?.Invoke();
            var ids = Entries.Values
                .Where(e => e.IsActive
                            && (e.Row.sprk_objectrecordid == recordId.ToString()
                                || (e.Row._sprk_objectorganization_value is { } o && organizationIds.Contains(o))))
                .Select(e => e.Row.sprk_noaccessentryid!.Value)
                .ToList();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(ids.Count > max ? (ids.Take(max).ToList(), true) : (ids, false));
        }

        internal override Task<string?> ReadRecordTypeLogicalNameAsync(Guid typeRefId, CancellationToken ct)
            => Task.FromResult(RecordTypeNames.TryGetValue(typeRefId, out var n) ? n : null);

        internal override Task<IReadOnlyList<Guid>> FindSystemUsersByOidsAsync(IReadOnlyCollection<Guid> oids, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Guid>>(
                oids.Where(SystemUsersByOid.ContainsKey).Select(o => SystemUsersByOid[o]).Distinct().ToList());

        internal override Task<IReadOnlyDictionary<Guid, EnforcementSystemUser>> ReadSystemUsersAsync(
            IReadOnlyCollection<Guid> systemUserIds, CancellationToken ct)
            => FailPeopleRead
                ? throw new HttpRequestException("Simulated systemuser read failure.")
                : Task.FromResult<IReadOnlyDictionary<Guid, EnforcementSystemUser>>(
                    systemUserIds.Where(People.ContainsKey).Distinct().ToDictionary(id => id, id => People[id]));

        internal override Task<IReadOnlySet<Guid>> ReadTeamMembershipsAsync(
            Guid systemUserId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
            => Task.FromResult<IReadOnlySet<Guid>>(
                (TeamMembers.TryGetValue(systemUserId, out var teams) ? teams : new HashSet<Guid>())
                .Where(teamIds.Contains).ToHashSet());

        internal override Task<Guid?> ReadOwningTeamAsync(string entitySet, string idColumn, Guid recordId, CancellationToken ct)
            => FailOwnerRead
                ? throw new HttpRequestException("Simulated owner read failure.")
                : Task.FromResult(OwningTeams.TryGetValue(recordId, out var t) ? t : (Guid?)null);

        internal override Task<AccessRights> GetPrincipalRightsAsync(
            Guid systemUserId, string entitySet, Guid recordId, CancellationToken ct)
        {
            RightsReads.Add((systemUserId, recordId));
            if (FailRightsRead)
                throw new HttpRequestException("Simulated RetrievePrincipalAccess failure.");

            return Task.FromResult(Rights.TryGetValue((systemUserId, recordId), out var r) ? r : AccessRights.None);
        }
    }

    /// <summary>A recording tenant cache: every removal's (tenant, resource, id).</summary>
    internal sealed class RecordingCache
    {
        public RecordingCache()
        {
            Mock
                .Setup(c => c.RemoveAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, int, string, CancellationToken>(
                    (tenant, resource, id, _, _, _) => Removed.Add((tenant, resource, id)))
                .Returns(Task.CompletedTask);
        }

        public Mock<ITenantCache> Mock { get; } = new();

        public ConcurrentBag<(string Tenant, string Resource, string Id)> Removed { get; } = new();
    }

    /// <summary>Everything one enforcer needs, wired, with the production enforcer between the doubles.</summary>
    internal sealed class Harness
    {
        public FakeEnforcementStore Store { get; } = new();

        public GrantPolicyTestDoubles.FlagStubParticipationService Participations { get; } =
            new(defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false));

        public InMemoryContactIdentityStore Identities { get; } = new();

        public FakeRecordShareTable Shares { get; } = new();

        public RecordingCache Cache { get; } = new();

        /// <summary>The per-record removal lease (task 143 r1, S5). Shared by every enforcer this harness builds, as the
        /// scheduler's singleton lease store is shared by every enforcement in a process.</summary>
        public IScheduledJobLease Lease { get; set; } = new ProcessLocalScheduledJobLease();

        /// <summary>
        /// Task 149 (merged after 143): the Dataverse the secure-child synchronizer reads after a removal. Default: an
        /// environment with no Secure Record BU, where that fan-out reads the root, finds nothing secure and writes nothing.
        /// </summary>
        public DataMutation.ExternalAccess.SecureChildShareWorld ChildWorld { get; set; } =
            DataMutation.ExternalAccess.SecureChildShareWorld.WithoutSecureBusinessUnit();

        /// <summary>The REAL synchronizer over <see cref="ChildWorld"/> and the given share seam (default: <see cref="Shares"/>).</summary>
        public Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer ChildShares(
            Sprk.Bff.Api.Services.Access.IDataverseRecordShareService? shares = null)
            => DataMutation.ExternalAccess.SecureChildShareWorld.SynchronizerOver(() => ChildWorld, shares ?? Shares);

        /// <summary>
        /// Task 158 final round (round 58 item 1): the enforcer's reads of what is filed under a covered matter or project —
        /// the same Dataverse the synchronizer reads (<see cref="ChildWorld"/>), as in production.
        /// </summary>
        public IGenericEntityService Entities() =>
            DataMutation.ExternalAccess.SecureChildShareWorld.EntitiesOver(() => ChildWorld).Object;

        public NoAccessShareEnforcer Enforcer => new(
            Store, Participations, Identities, Shares, Cache.Mock.Object, Lease, ChildShares(), Entities(),
            NullLogger<NoAccessShareEnforcer>.Instance);
    }
}
