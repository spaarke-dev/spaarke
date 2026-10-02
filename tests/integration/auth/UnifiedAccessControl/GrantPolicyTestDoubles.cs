using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test doubles for the write-time grant policy (unified-access-control-r2 task 138) and the grant core's task-139
/// checks (grantor ceiling, never-lower, the No Access list at write time).
/// </summary>
internal static class GrantPolicyTestDoubles
{
    /// <summary>
    /// The production <see cref="ExternalParticipationService"/> with ONLY its data reads overridden — the same
    /// subclass-and-override seam every evaluator test uses (ADR-038: no <c>Mock&lt;HttpMessageHandler&gt;</c>).
    /// </summary>
    /// <remarks>
    /// <para>Records the entity type it was asked about, so a test can prove the policy addressed the flag
    /// reader by the root's LOGICAL name (an entity-set name would return an empty map).</para>
    /// <para>An id with no seeded flags answers <c>defaultFlags</c>. <see cref="Absent"/> drops an id from the
    /// returned map altogether — the shape the real reader produces for a non-flag-bearing entity type — and
    /// <see cref="ThrowOnRead"/> makes the read throw.</para>
    /// <para>Task 139: the two reads the write-time No Access check makes are overridden too — a contact's
    /// organization memberships (<see cref="ContactOrganizations"/>, <see cref="MembershipsUnreadable"/>) and a
    /// record's referenced organizations (<see cref="RecordOrganizations"/>). Unseeded, a contact belongs to no
    /// organization and a record references none — answered, never a fault.</para>
    /// </remarks>
    internal sealed class FlagStubParticipationService : ExternalParticipationService
    {
        private readonly RootRecordFlags _defaultFlags;

        public FlagStubParticipationService(RootRecordFlags defaultFlags)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
            _defaultFlags = defaultFlags;
        }

        /// <summary>Per-record flags; anything not listed answers the default.</summary>
        public ConcurrentDictionary<Guid, RootRecordFlags> Flags { get; } = new();

        /// <summary>Ids the read leaves OUT of its answer (the absent-key case).</summary>
        public ConcurrentDictionary<Guid, bool> Absent { get; } = new();

        /// <summary>When set, the read throws instead of answering.</summary>
        public bool ThrowOnRead { get; set; }

        /// <summary>Every (entityType, recordId) the read was asked about.</summary>
        public ConcurrentBag<(string EntityType, Guid RecordId)> Reads { get; } = new();

        /// <summary>Task 139: a contact's ACTIVE organization memberships (both named sets).</summary>
        public ConcurrentDictionary<Guid, Guid[]> ContactOrganizations { get; } = new();

        /// <summary>Task 139: the membership read faults (<see cref="ActiveOrgMemberships.Failed"/>).</summary>
        public bool MembershipsUnreadable { get; set; }

        /// <summary>Task 139: the organizations a record references (the ethical-wall object side).</summary>
        public ConcurrentDictionary<Guid, Guid[]> RecordOrganizations { get; } = new();

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds)
            {
                Reads.Add((entityType, id));
            }

            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Simulated root-flag read failure.");
            }

            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds
                .Distinct()
                .Where(id => !Absent.ContainsKey(id))
                .ToDictionary(id => id, id => Flags.TryGetValue(id, out var f) ? f : _defaultFlags);
            return Task.FromResult(result);
        }

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(
            Guid contactId, CancellationToken ct = default)
        {
            if (MembershipsUnreadable)
                return Task.FromResult(ActiveOrgMemberships.Failed);

            var orgs = ContactOrganizations.TryGetValue(contactId, out var ids) ? ids : Array.Empty<Guid>();
            return Task.FromResult(new ActiveOrgMemberships(orgs, orgs, Unreadable: false));
        }

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
                recordIds.Distinct().ToDictionary(
                    id => id,
                    id => RecordOrganizations.TryGetValue(id, out var orgs)
                        ? new ReferencedOrganizations(orgs, Unreadable: false)
                        : ReferencedOrganizations.None));
    }

    /// <summary>
    /// The PRODUCTION <see cref="NoAccessListReader"/> with only its wire seam (<see cref="NoAccessListReader.QueryChunkAsync"/>)
    /// substituted — the same seam <c>NoAccessListReaderTests</c> and <c>UnifiedEvaluatorSeamTests</c> use. The real
    /// subject/object filters, chunking, row-shape matching and fail-closed handling all run.
    /// </summary>
    /// <remarks>
    /// An entry is returned only when the query's SUBJECT filter names its subject (a real Dataverse query would have
    /// filtered on it), and only on the loop whose OBJECT shape matches it — so the double never decides who is denied.
    /// </remarks>
    internal sealed class SeamNoAccessListReader : NoAccessListReader
    {
        private readonly List<(string Subject, NoAccessEntryRow Row)> _entries = new();

        public SeamNoAccessListReader()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
        {
        }

        /// <summary>When set, every query faults (<c>null</c>, the reader's own fail-closed signal).</summary>
        public bool Faults { get; set; }

        /// <summary>How many chunk queries ran — proves the shared reader was consulted.</summary>
        public int Queries { get; private set; }

        /// <summary>A CONTACT subject denied on one specific record.</summary>
        public void DenyContactOnRecord(Guid contactId, Guid recordId)
            => _entries.Add(($"sprk_subjectcontact eq {contactId}", RecordRow(recordId)));

        /// <summary>An ORGANIZATION subject (every member, or an org grantee) denied on one specific record.</summary>
        public void DenyOrganizationOnRecord(Guid organizationId, Guid recordId)
            => _entries.Add(($"sprk_subjectorganization eq {organizationId}", RecordRow(recordId)));

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(
            string subjectFilter, string objectFilter, CancellationToken ct)
        {
            Queries++;
            if (Faults)
                return Task.FromResult<List<NoAccessEntryRow>?>(null);

            var recordLoop = objectFilter.Contains("sprk_objectrecordid", StringComparison.Ordinal);
            var rows = _entries
                .Where(e => subjectFilter.Contains(e.Subject, StringComparison.Ordinal))
                .Select(e => e.Row)
                .Where(r => recordLoop
                    ? r.sprk_objectrecordid is { } id && objectFilter.Contains($"'{id}'", StringComparison.Ordinal)
                    : r._sprk_objectorganization_value is { } org && objectFilter.Contains(org.ToString(), StringComparison.Ordinal))
                .ToList();
            return Task.FromResult<List<NoAccessEntryRow>?>(rows);
        }

        private static NoAccessEntryRow RecordRow(Guid recordId) => new()
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_objectrecordtype_value = Guid.NewGuid(), // populated — only its presence matters to the shape check
            sprk_objectrecordid = recordId.ToString(),
        };
    }

    /// <summary>
    /// The PRODUCTION <see cref="AccessibleRecordSetService"/> — whose write-time No Access entry point shares the
    /// read-path veto code — over the given participation reads and the real deny-list reader.
    /// </summary>
    internal static AccessibleRecordSetService RealDenyList(
        FlagStubParticipationService participations, SeamNoAccessListReader reader)
        => new(
            Mock.Of<IMembershipResolverService>(),
            participations,
            Mock.Of<ISubjectStandingGrantReader>(),
            reader,
            NullLogger<AccessibleRecordSetService>.Instance);

    /// <summary>
    /// A write-time No Access check that answers <paramref name="denied"/> for every grantee — for tests that are not
    /// ABOUT the deny list (the deny decision itself is pinned through <see cref="RealDenyList"/>).
    /// </summary>
    internal static IAccessibleRecordSetService DenyListAnswering(bool denied)
    {
        var mock = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        mock.Setup(s => s.IsGranteeDeniedOnRecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(denied);
        return mock.Object;
    }
}
