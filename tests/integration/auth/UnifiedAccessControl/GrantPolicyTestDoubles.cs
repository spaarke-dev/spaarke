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

        /// <param name="defaultFlags">What an unseeded id answers.</param>
        /// <param name="filing">Task 174: the filing world the effective-flag read walks; by default nothing is filed under
        /// anything (every record's own flags are its effective flags).</param>
        public FlagStubParticipationService(RootRecordFlags defaultFlags, Spaarke.Dataverse.IGenericEntityService? filing = null)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance,
                   filing: filing ?? Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
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

        /// <summary>
        /// Task 139 r1: when set, the referenced-organization read THROWS this exception — e.g. a
        /// <see cref="TaskCanceledException"/> with no caller cancellation, the shape of an HttpClient timeout, which
        /// the deny-veto code rethrows rather than absorbing.
        /// </summary>
        public Exception? ReferencedOrganizationsThrow { get; set; }

        /// <summary>
        /// Task 143 r1: records whose referenced-organization read comes back <see cref="ReferencedOrganizations.Unresolved"/>
        /// — the PRODUCTION fault shape (the real read reports the fault per record and never throws for it).
        /// </summary>
        public ConcurrentDictionary<Guid, bool> UnreadableReferencedOrganizations { get; } = new();

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

        /// <summary>
        /// Task 140: a contact's raw <c>sprk_contactorganization</c> rows, projected through the PRODUCTION
        /// <see cref="ExternalParticipationService.ProjectOrganizationMemberships"/> (today's UTC date, as the production read
        /// uses) — so an inactive or date-ended membership is judged by the real rule, not by the double.
        /// </summary>
        public ConcurrentDictionary<Guid, ContactOrgRow[]> MembershipRows { get; } = new();

        /// <summary>Task 140: contacts whose membership read faults (one subject, not every read).</summary>
        public ConcurrentDictionary<Guid, bool> UnreadableMembershipContacts { get; } = new();

        /// <summary>Task 140: contacts whose membership read THROWS.</summary>
        public ConcurrentDictionary<Guid, bool> ThrowingMembershipContacts { get; } = new();

        /// <summary>Task 140 r-final: every contact whose membership read ran, in order.</summary>
        public ConcurrentQueue<Guid> MembershipReads { get; } = new();

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(
            Guid contactId, CancellationToken ct = default)
        {
            MembershipReads.Enqueue(contactId);
            if (MembershipsUnreadable || UnreadableMembershipContacts.ContainsKey(contactId))
                return Task.FromResult(ActiveOrgMemberships.Failed);

            if (ThrowingMembershipContacts.ContainsKey(contactId))
                throw new HttpRequestException("Simulated membership read failure.");

            if (MembershipRows.TryGetValue(contactId, out var rows))
                return Task.FromResult(ProjectOrganizationMemberships(rows, DateOnly.FromDateTime(DateTime.UtcNow)));

            var orgs = ContactOrganizations.TryGetValue(contactId, out var ids) ? ids : Array.Empty<Guid>();
            return Task.FromResult(new ActiveOrgMemberships(orgs, orgs, Unreadable: false));
        }

        /// <summary>
        /// Task 140 r2: the contacts the colleague-by-email read sees — id → (email, statecode), e.g. the test's identity
        /// store. A contact not answered here has no expanded contact on its junction rows (names nobody).
        /// </summary>
        public Func<Guid, (string? Email, int StateCode)?>? ContactDirectory { get; set; }

        /// <summary>Task 140 r2: the colleague-by-email read THROWS (the production fault shape of a failed page).</summary>
        public bool ColleagueByEmailFaults { get; set; }

        /// <summary>Task 140 r2: every colleague-by-email read — the organizations it named and the email.</summary>
        public ConcurrentBag<(Guid[] Organizations, string Email)> ColleagueByEmailReads { get; } = new();

        /// <summary>
        /// Task 140 r2: the junction rows a colleague-by-email chunk returns, as Dataverse would for the production
        /// <c>$filter</c>'s ORGANIZATION and junction-state clauses — every active membership row of the named organizations,
        /// built from <see cref="MembershipRows"/> / <see cref="ContactOrganizations"/>, each with its contact expanded from
        /// <see cref="ContactDirectory"/>. The contact's email and state are deliberately NOT filtered here: the PRODUCTION
        /// projection (<see cref="ExternalParticipationService.ProjectColleaguesByEmail"/>) must decide them, so a test
        /// proves the code — not the double — keeps an outsider or an inactive contact out.
        /// </summary>
        internal override Task<IReadOnlyList<ColleagueMembershipRow>> ReadColleagueMembershipRowsAsync(
            IReadOnlyCollection<Guid> organizationIds, string email, CancellationToken ct)
        {
            ColleagueByEmailReads.Add((organizationIds.ToArray(), email));
            if (ColleagueByEmailFaults)
                throw new HttpRequestException("Simulated colleague-by-email read failure.");

            var rows = new List<ColleagueMembershipRow>();
            foreach (var contactId in ContactOrganizations.Keys.Union(MembershipRows.Keys).Distinct())
            {
                var memberships = MembershipRows.TryGetValue(contactId, out var seeded)
                    ? seeded
                    : ContactOrganizations[contactId].Select(org => new ContactOrgRow
                    {
                        OrganizationId = org,
                        StateCode = 0,
                        Organization = new OrganizationStateRow { StateCode = 0 },
                    }).ToArray();

                var contact = ContactDirectory?.Invoke(contactId) is { } info
                    ? new ColleagueContactRow { Email = info.Email, StateCode = info.StateCode }
                    : null;

                rows.AddRange(memberships
                    .Where(m => m.OrganizationId is { } org && organizationIds.Contains(org) && IsActiveState(m.StateCode))
                    .Select(m => new ColleagueMembershipRow
                    {
                        ContactId = contactId,
                        OrganizationId = m.OrganizationId,
                        StartDate = m.StartDate,
                        EndDate = m.EndDate,
                        StateCode = m.StateCode,
                        Organization = m.Organization,
                        Contact = contact,
                    }));
            }

            return Task.FromResult<IReadOnlyList<ColleagueMembershipRow>>(rows);
        }

        /// <summary>
        /// Task 140: a contact's grant set, for the real CIAM composition. Only a SEEDED contact is answered here; any
        /// other falls through to the production read, exactly as before this seam existed.
        /// </summary>
        public ConcurrentDictionary<Guid, ExternalGrantSet> GrantSets { get; } = new();

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => GrantSets.TryGetValue(contactId, out var set)
                ? Task.FromResult(set)
                : base.GetGrantSetAsync(contactId, ct);

        /// <summary>Task 143: the table each record belongs to (unlisted = <c>sprk_project</c>), for the reverse reads.</summary>
        public ConcurrentDictionary<Guid, string> RecordTables { get; } = new();

        /// <summary>Task 143: an organization's ACTIVE member contacts (the wall set), for the enforcer's reverse read.</summary>
        public ConcurrentDictionary<Guid, Guid[]> OrganizationMembers { get; } = new();

        /// <summary>Task 143: when set, the enforcer's reverse reads throw.</summary>
        public bool ReverseReadsThrow { get; set; }

        /// <summary>
        /// Task 143: the SECURE records of a table that reference the organization — derived from
        /// <see cref="RecordOrganizations"/>, <see cref="RecordTables"/> and the seeded flags, so the reverse read and the
        /// forward one cannot disagree in a test.
        /// </summary>
        public override Task<(IReadOnlyList<Guid> RecordIds, bool Truncated)> FindSecureRootsReferencingOrganizationAsync(
            string entityType, Guid organizationId, int maxRows, CancellationToken ct = default)
        {
            if (ReverseReadsThrow)
                throw new InvalidOperationException("Simulated reverse-read failure.");

            var ids = RecordOrganizations
                .Where(kv => kv.Value.Contains(organizationId)
                             && (RecordTables.TryGetValue(kv.Key, out var t) ? t : "sprk_project") == entityType
                             && StoredSecureFlag(kv.Key) == true)
                .Select(kv => kv.Key)
                .ToList();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(
                ids.Count > maxRows ? (ids.Take(maxRows).ToList(), true) : (ids, false));
        }

        /// <summary>
        /// Task 174: the NOT-flagged records of a table that reference the organization — the same derivation as the secure
        /// read above, with the flag inverted.
        /// </summary>
        public override Task<(IReadOnlyList<Guid> RecordIds, bool Truncated)> FindUnflaggedRootsReferencingOrganizationAsync(
            string entityType, Guid organizationId, int maxRows, CancellationToken ct = default)
        {
            if (ReverseReadsThrow)
                throw new InvalidOperationException("Simulated reverse-read failure.");

            var ids = RecordOrganizations
                .Where(kv => kv.Value.Contains(organizationId)
                             && (RecordTables.TryGetValue(kv.Key, out var t) ? t : "sprk_project") == entityType
                             && MatchesFlagFilter(UnflaggedRootFilter, StoredSecureFlag(kv.Key)))
                .Select(kv => kv.Key)
                .ToList();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(
                ids.Count > maxRows ? (ids.Take(maxRows).ToList(), true) : (ids, false));
        }

        /// <summary>Task 174: records whose STORED <c>sprk_issecure</c> is blank (null) — what the reverse reads filter on.</summary>
        public ConcurrentDictionary<Guid, bool> BlankSecureFlags { get; } = new();

        private bool? StoredSecureFlag(Guid id) =>
            BlankSecureFlags.ContainsKey(id) ? null : (Flags.TryGetValue(id, out var f) ? f : _defaultFlags).IsSecure;

        /// <summary>
        /// Evaluates a <c>sprk_issecure</c> OData filter of the shape production sends (<c>eq true</c>, <c>ne true</c>,
        /// <c>eq null</c>, joined by <c>or</c>) with Dataverse's null semantics: <c>ne</c> never matches a null. So a double
        /// cannot hide a filter that drops blank flags.
        /// </summary>
        internal static bool MatchesFlagFilter(string filter, bool? value)
            => filter.Trim('(', ')').Split(" or ").Any(clause => clause.Trim() switch
            {
                "sprk_issecure eq true" => value == true,
                "sprk_issecure ne true" => value == false,
                "sprk_issecure eq null" => value is null,
                var other => throw new InvalidOperationException($"Unmodelled flag filter clause: {other}"),
            });

        /// <summary>Task 143: an organization's active member contacts.</summary>
        public override Task<(IReadOnlyList<Guid> ContactIds, bool Truncated)> FindWallMemberContactsAsync(
            Guid organizationId, int maxRows, CancellationToken ct = default)
        {
            if (ReverseReadsThrow)
                throw new InvalidOperationException("Simulated reverse-read failure.");

            var members = OrganizationMembers.TryGetValue(organizationId, out var m) ? m : Array.Empty<Guid>();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(
                members.Length > maxRows ? (members.Take(maxRows).ToList(), true) : (members, false));
        }

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            if (ReferencedOrganizationsThrow is { } ex)
                return Task.FromException<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(ex);

            return Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
                recordIds.Distinct().ToDictionary(
                    id => id,
                    id => UnreadableReferencedOrganizations.ContainsKey(id)
                        ? ReferencedOrganizations.Unresolved
                        : RecordOrganizations.TryGetValue(id, out var orgs)
                            ? new ReferencedOrganizations(orgs, Unreadable: false)
                            : ReferencedOrganizations.None));
        }

        /// <summary>Task 137: a contact's live state. Unseeded, every contact is Active (answered, never a fault).</summary>
        public ConcurrentDictionary<Guid, ContactRecordState> ContactStates { get; } = new();

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactStates.TryGetValue(contactId, out var state) ? state : ContactRecordState.Active);

        /// <summary>
        /// Task 137: every call of the ONE grant-cache invalidation routine, as (contacts, organizations). The routine
        /// itself — tenants, organization paging, failure handling — is pinned over a real cache by
        /// <c>ExternalParticipationServiceInvalidationTests</c> and the task-137 section of
        /// <c>GrantLifecycleCharacterizationTests</c>, and its production organization-member page read over the
        /// transport by <c>OrganizationMembershipReadTests</c> (task 137 r2); here a test proves WHICH grantees a write
        /// path asked it to invalidate.
        /// </summary>
        public ConcurrentQueue<(Guid[] Contacts, Guid[] Organizations)> Invalidations { get; } = new();

        public override Task<GrantCacheInvalidation> InvalidateGrantSetsAsync(
            IEnumerable<Guid> contactIds, IEnumerable<Guid> organizationIds, CancellationToken ct = default,
            string? explicitTenantId = null)
        {
            var contacts = contactIds.ToArray();
            Invalidations.Enqueue((contacts, organizationIds.ToArray()));
            return Task.FromResult(new GrantCacheInvalidation(contacts.Length, 0, 0, Array.Empty<Guid>()));
        }
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

        /// <summary>
        /// Task 139 r1: when set, every query THROWS this exception instead of answering — e.g. a
        /// <see cref="TaskCanceledException"/> with no caller cancellation (an HttpClient timeout), which the reader
        /// rethrows rather than turning into its own fail-closed <c>null</c>.
        /// </summary>
        public Exception? Throws { get; set; }

        /// <summary>
        /// Task 142 round 18: when set, only the query whose SUBJECT fragment names this id faults (<c>null</c>) — one
        /// faulted subject chunk among several, the others answering.
        /// </summary>
        public Guid? FaultsWhenSubjectNames { get; set; }

        /// <summary>How many chunk queries ran — proves the shared reader was consulted.</summary>
        public int Queries { get; private set; }

        /// <summary>A CONTACT subject denied on one specific record.</summary>
        public void DenyContactOnRecord(Guid contactId, Guid recordId)
            => _entries.Add(($"_sprk_subjectcontact_value eq {contactId}", WithContact(RecordRow(recordId), contactId)));

        /// <summary>An ORGANIZATION subject (every member, or an org grantee) denied on one specific record.</summary>
        public void DenyOrganizationOnRecord(Guid organizationId, Guid recordId)
            => _entries.Add(($"_sprk_subjectorganization_value eq {organizationId}", WithOrganization(RecordRow(recordId), organizationId)));

        /// <summary>Task 143: a SYSTEMUSER subject (<c>sprk_subjectsystemuser</c>) denied on one specific record.</summary>
        public Guid DenySystemUserOnRecord(Guid systemUserId, Guid recordId)
        {
            var row = RecordRow(recordId);
            row._sprk_subjectsystemuser_value = systemUserId;
            _entries.Add(($"_sprk_subjectsystemuser_value eq {systemUserId}", row));
            return row.sprk_noaccessentryid!.Value;
        }

        /// <summary>Task 158 r1c-v1: an operator lifts one entry (removes it from the No Access list).</summary>
        public void Lift(Guid entryId) => _entries.RemoveAll(e => e.Row.sprk_noaccessentryid == entryId);

        /// <summary>Task 143: a CONTACT subject denied on every record referencing an organization (ethical wall).</summary>
        public void DenyContactOnOrganization(Guid contactId, Guid objectOrganizationId)
            => _entries.Add(($"_sprk_subjectcontact_value eq {contactId}", WithContact(OrganizationRow(objectOrganizationId), contactId)));

        /// <summary>Task 143: a SYSTEMUSER subject denied on every record referencing an organization.</summary>
        public void DenySystemUserOnOrganization(Guid systemUserId, Guid objectOrganizationId)
        {
            var row = OrganizationRow(objectOrganizationId);
            row._sprk_subjectsystemuser_value = systemUserId;
            _entries.Add(($"_sprk_subjectsystemuser_value eq {systemUserId}", row));
        }

        private static NoAccessEntryRow WithContact(NoAccessEntryRow row, Guid contactId)
        {
            row._sprk_subjectcontact_value = contactId;
            return row;
        }

        private static NoAccessEntryRow WithOrganization(NoAccessEntryRow row, Guid organizationId)
        {
            row._sprk_subjectorganization_value = organizationId;
            return row;
        }

        private static NoAccessEntryRow OrganizationRow(Guid objectOrganizationId) => new()
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_objectorganization_value = objectOrganizationId,
        };

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(
            string subjectFilter, string objectFilter, CancellationToken ct)
        {
            Queries++;
            if (Throws is { } ex)
                return Task.FromException<List<NoAccessEntryRow>?>(ex);
            if (Faults)
                return Task.FromResult<List<NoAccessEntryRow>?>(null);
            if (FaultsWhenSubjectNames is { } faulting && subjectFilter.Contains(faulting.ToString(), StringComparison.Ordinal))
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
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
            NullLogger<AccessibleRecordSetService>.Instance);

    /// <summary>
    /// Task 137: the PRODUCTION <see cref="ExternalParticipationService"/> over <paramref name="cache"/>, with the
    /// request's <paramref name="context"/> (its <c>tid</c>) and optional <paramref name="configuration"/> (the CIAM and
    /// workforce tenant ids), and ONLY the organization-member page read substituted — answered from
    /// <paramref name="members"/>, one page per <paramref name="pageSize"/> rows with a synthetic next link. The ONE
    /// invalidation routine runs unmodified: tenant enumeration, paging to completion, per-removal failure handling.
    /// </summary>
    internal static MemberPagingParticipationService RealInvalidationOver(
        Sprk.Bff.Api.Infrastructure.Cache.ITenantCache cache,
        Microsoft.AspNetCore.Http.HttpContext? context,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
        => new(cache, context, configuration);

    /// <summary>See <see cref="RealInvalidationOver"/>.</summary>
    internal sealed class MemberPagingParticipationService : ExternalParticipationService
    {
        public MemberPagingParticipationService(
            Sprk.Bff.Api.Infrastructure.Cache.ITenantCache cache,
            Microsoft.AspNetCore.Http.HttpContext? context,
            Microsoft.Extensions.Configuration.IConfiguration? configuration)
            : base(new HttpClient(), cache,
                   configuration ?? new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                   credential: null!, AccessorFor(context), NullLogger<ExternalParticipationService>.Instance,
                   filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
        {
        }

        /// <summary>Each organization's ACTIVE members, in page order.</summary>
        public ConcurrentDictionary<Guid, Guid[]> Members { get; } = new();

        /// <summary>
        /// When set, answers an organization's members instead of <see cref="Members"/> — e.g. a fake table that
        /// interprets the production <c>ExternalOrganizationMembership.ActiveMembersFilter</c>.
        /// </summary>
        public Func<Guid, Guid[]>? MemberSource { get; set; }

        /// <summary>Rows per page the double serves (the production page size by default).</summary>
        public int PageSize { get; set; } = OrganizationMemberPageSize;

        /// <summary>When set, the page with this zero-based index faults (for every organization).</summary>
        public int? FailPage { get; set; }

        /// <summary>Every (organization, page index) read, in order.</summary>
        public ConcurrentQueue<(Guid OrganizationId, int Page)> PageReads { get; } = new();

        /// <summary>
        /// When set, the root-flag read answers these flags for every id instead of reading Dataverse — so a WRITE
        /// path whose policy check reads the flags (<c>/grant</c>) can run end to end over the real invalidation
        /// routine. Unset, the production read runs (and the write paths that never read flags are unaffected).
        /// </summary>
        public RootRecordFlags? RootFlags { get; set; }

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => RootFlags is { } flags
                ? Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(
                    recordIds.Distinct().ToDictionary(id => id, _ => flags))
                : base.GetRootRecordFlagsAsync(entityType, recordIds, ct);

        internal override Task<OrganizationMemberPage> ReadOrganizationMemberPageAsync(
            Guid organizationId, string? nextLink, CancellationToken ct)
        {
            var page = nextLink is null ? 0 : int.Parse(nextLink.Split('=')[1]);
            PageReads.Enqueue((organizationId, page));
            if (FailPage == page)
                throw new HttpRequestException("Simulated member-page read failure.");

            var all = MemberSource?.Invoke(organizationId)
                      ?? (Members.TryGetValue(organizationId, out var ids) ? ids : Array.Empty<Guid>());
            var slice = all.Skip(page * PageSize).Take(PageSize).ToList();
            var more = (page + 1) * PageSize < all.Length;
            return Task.FromResult(new OrganizationMemberPage(slice, more ? $"next?page={page + 1}" : null));
        }

        private static Microsoft.AspNetCore.Http.IHttpContextAccessor AccessorFor(Microsoft.AspNetCore.Http.HttpContext? context)
            => new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context };
    }

    /// <summary>
    /// A write-time No Access check that answers <paramref name="denied"/> for every grantee — for tests that are not
    /// ABOUT the deny list (the deny decision itself is pinned through <see cref="RealDenyList"/>). Task 142 r4: the
    /// check is a tri-state; <c>false</c> answers <see cref="NoAccessCheckAnswer.Allowed"/>, <c>true</c>
    /// <see cref="NoAccessCheckAnswer.Denied"/>.
    /// </summary>
    internal static IAccessibleRecordSetService DenyListAnswering(bool denied)
        => DenyListAnswering(denied ? NoAccessCheckAnswer.Denied : NoAccessCheckAnswer.Allowed);

    /// <summary>
    /// A write-time No Access check that answers <paramref name="answer"/> for every grantee — including a value outside
    /// the enum, which no production check returns, to pin that a consumer's defensive branch refuses it (task 142 r5,
    /// r4 verifier finding 6). Only for tests ABOUT a consumer's handling of the answer; the answer itself is pinned
    /// through <see cref="RealDenyList"/>.
    /// </summary>
    internal static IAccessibleRecordSetService DenyListAnswering(NoAccessCheckAnswer answer)
    {
        var mock = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        mock.Setup(s => s.CheckGranteeNoAccessAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);
        return mock.Object;
    }
}
