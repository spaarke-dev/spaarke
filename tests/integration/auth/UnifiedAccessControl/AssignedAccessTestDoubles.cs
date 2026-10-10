using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test doubles for the Assigned-To materializer (unified-access-control-r2 task 142). Every double sits at a module
/// boundary (ADR-038: no HTTP doubles): the ledger store's internal-virtual reads/writes, an in-memory grant table behind
/// <see cref="DataverseWebApiClient"/>'s virtual methods that INTERPRETS the production <c>$filter</c>, the participation
/// service's virtual reads, the identity row store, the POA share table, the tenant cache and the standing-grant reader.
/// The PRODUCTION materializer, grant core (<c>CreateGrantAsync</c>), No Access guard and deny-list evaluator run between
/// them.
/// </summary>
internal static class AssignedAccessTestDoubles
{
    internal const string TestTenant = "00000000-0000-0000-0000-0000000000aa";
    internal static readonly DateOnly Today = new(2026, 10, 3);

    /// <summary>A fixed clock (UTC noon on <see cref="Today"/>).</summary>
    internal sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The ledger, the roots' registry columns, the link candidates and organization state, in memory.</summary>
    internal sealed class FakeAssignedAccessStore : AssignedAccessStore
    {
        public FakeAssignedAccessStore() : base(null!, NullLogger<AssignedAccessStore>.Instance)
        {
        }

        public ConcurrentDictionary<(ExternalGrantRootType, Guid), ConcurrentDictionary<string, Guid?>> Roots { get; } = new();
        public List<AssignedAccessLedgerRow> Ledger { get; } = new();
        public ConcurrentDictionary<Guid, List<AssignedLinkCandidate>> UsersByLink { get; } = new();
        public ConcurrentDictionary<Guid, List<AssignedLinkCandidate>> UsersByOid { get; } = new();
        public ConcurrentDictionary<Guid, int?> OrganizationStates { get; } = new();
        public ConcurrentDictionary<Guid, string> Names { get; } = new();

        public bool FailRootRead { get; set; }
        public bool FailLedgerRead { get; set; }
        public bool FailLinkRead { get; set; }
        public bool FailLedgerWrites { get; set; }

        /// <summary>Every ledger write that landed: ("create"|"update", key-or-id, state).</summary>
        public List<(string Op, string Target, AssignedAccessState State)> Writes { get; } = new();

        private readonly object _gate = new();

        public void Assign(ExternalGrantRootType type, Guid rootId, string field, Guid? subjectId)
        {
            var values = Roots.GetOrAdd((type, rootId), _ => new ConcurrentDictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase));
            values[field] = subjectId;
        }

        public void Root(ExternalGrantRootType type, Guid rootId)
            => Roots.GetOrAdd((type, rootId), _ => new ConcurrentDictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase));

        /// <summary>The ledger rows of one subject on one root (any state).</summary>
        public IReadOnlyList<AssignedAccessLedgerRow> RowsOf(Guid rootId, Guid subjectId)
        {
            lock (_gate)
                return Ledger.Where(r => RootIdOf(r) == rootId && (r.SubjectContactId == subjectId || r.SubjectOrganizationId == subjectId)).ToList();
        }

        public AssignedAccessLedgerRow SeedRow(ExternalGrantRootType type, Guid rootId, string field, AssignedSubject subject,
            AssignedAccessState state, string? reason = null, Guid? grantId = null, Guid? systemUserId = null,
            int? level = null, DateOnly? expiry = null)
        {
            var row = NewRow(type, rootId, field, subject);
            Apply(row, new AssignedAccessLedgerWrite(state, reason, grantId, systemUserId, level, expiry));
            lock (_gate)
            {
                Bump(row);
                Ledger.Add(row);
            }

            return row;
        }

        internal override Task<IReadOnlyList<AssignedAccessLedgerRow>> ReadLedgerAsync(
            ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
        {
            if (FailLedgerRead)
                throw new HttpRequestException("Simulated ledger read failure.");
            lock (_gate)
                return Task.FromResult<IReadOnlyList<AssignedAccessLedgerRow>>(
                    Ledger.Where(r => RootIdOf(r) == rootId).Select(Clone).ToList());
        }

        internal override Task<Guid> CreateLedgerAsync(
            ExternalGrantRootType rootType, Guid rootId, string sourceField, AssignedSubject subject,
            AssignedAccessLedgerWrite write, CancellationToken ct)
        {
            if (FailLedgerWrites)
                throw new HttpRequestException("Simulated ledger write failure.");

            var key = LedgerKey(rootType, rootId, sourceField, subject);
            lock (_gate)
            {
                if (Ledger.Any(r => r.LedgerKey == key))
                    throw new InvalidOperationException($"Duplicate ledger key {key} — the materializer created a row that exists.");

                var row = NewRow(rootType, rootId, sourceField, subject);
                Apply(row, write);
                Bump(row);
                Ledger.Add(row);
                Writes.Add(("create", key, write.State));
                return Task.FromResult(row.Id);
            }
        }

        /// <summary>
        /// Task 158 r1c-v1: only UPDATES fail (a create still lands) — e.g. a write-ahead row created, its share written, and
        /// the confirmation of that row failing.
        /// </summary>
        public bool FailLedgerUpdates { get; set; }

        internal override Task UpdateLedgerAsync(Guid rowId, AssignedAccessLedgerWrite write, CancellationToken ct)
        {
            if (FailLedgerWrites || FailLedgerUpdates)
                throw new HttpRequestException("Simulated ledger write failure.");

            lock (_gate)
            {
                var row = Ledger.Single(r => r.Id == rowId);
                Apply(row, write);
                Bump(row);
                Writes.Add(("update", row.LedgerKey!, write.State));
            }

            return Task.CompletedTask;
        }

        // ── Batch-4 integration (round 47 item 2): the conditional update the inheritance pass sends ──────────────────

        /// <summary>
        /// Runs just before a conditional (If-Match) update is evaluated, with the row id. This is a write landing between
        /// a pass's read and its write, such as an operator's Declined marker or another pass's end.
        /// </summary>
        public Action<Guid>? BeforeConditionalUpdate { get; set; }

        /// <summary>The conditional updates refused because the row's version had moved on (the 412s): (row id, version sent).</summary>
        public List<(Guid RowId, string ETag)> PreconditionFailures { get; } = new();

        /// <summary>
        /// The production contract of <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>, behind the store's seam. A
        /// version other than the row's is refused (412) and nothing is written. A row that is gone is refused (404).
        /// </summary>
        internal override Task<bool> TryUpdateLedgerIfMatchAsync(
            Guid rowId, AssignedAccessLedgerWrite write, string etag, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(etag))
                throw new ArgumentException("A conditional update needs the row's ETag as it was read; nothing was sent.", nameof(etag));

            BeforeConditionalUpdate?.Invoke(rowId);
            if (FailLedgerWrites || FailLedgerUpdates)
                throw new HttpRequestException("Simulated ledger write failure.");

            lock (_gate)
            {
                var row = Ledger.SingleOrDefault(r => r.Id == rowId);
                if (row is null)
                    return Task.FromResult(false);
                if (!string.Equals(row.ETag, etag, StringComparison.Ordinal))
                {
                    PreconditionFailures.Add((rowId, etag));
                    return Task.FromResult(false);
                }

                Apply(row, write);
                Bump(row);
                Writes.Add(("update", row.LedgerKey!, write.State));
                return Task.FromResult(true);
            }
        }

        internal override Task<AssignedAccessLedgerRow?> ReadLedgerRowAsync(Guid rowId, CancellationToken ct)
        {
            if (FailLedgerRead)
                throw new HttpRequestException("Simulated ledger read failure.");
            lock (_gate)
                return Task.FromResult(Ledger.Where(r => r.Id == rowId).Select(Clone).FirstOrDefault());
        }

        /// <summary>
        /// Writes <paramref name="write"/> to a row as another writer would (an operator's marker, another pass), with no
        /// fault and no hook, moving its version on. Used by the race tests.
        /// </summary>
        public void WriteConcurrently(Guid rowId, AssignedAccessLedgerWrite write)
        {
            lock (_gate)
            {
                var row = Ledger.Single(r => r.Id == rowId);
                Apply(row, write);
                Bump(row);
            }
        }

        private long _version;

        /// <summary>Moves a row's version on (<c>@odata.etag</c>, <c>W/"versionnumber"</c>), as every Dataverse write does.</summary>
        private void Bump(AssignedAccessLedgerRow row) => row.ETag = $"W/\"{++_version}\"";

        // ── Task 158 r1 (owner round 30): the inherited-share provenance rows, in the same in-memory ledger ──────────────

        /// <summary>The inherited-share rows held on one filed root (any state).</summary>
        public IReadOnlyList<AssignedAccessLedgerRow> InheritedRowsOf(Guid rootId)
        {
            lock (_gate)
                return Ledger.Where(r => RootIdOf(r) == rootId && InheritedSourceOf(r.SourceField) is not null).Select(Clone).ToList();
        }

        /// <summary>
        /// Task 158 r1c-v2: runs at the start of every read of a filed record's inherited rows — a concurrent change landing
        /// between two of a pass's reads.
        /// </summary>
        public Action? BeforeInheritedRead { get; set; }

        internal override Task<IReadOnlyList<AssignedAccessLedgerRow>> ReadInheritedLedgerAsync(
            ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
        {
            BeforeInheritedRead?.Invoke();
            if (FailLedgerRead)
                throw new HttpRequestException("Simulated ledger read failure.");
            return Task.FromResult(InheritedRowsOf(rootId));
        }

        internal override Task<(IReadOnlyList<AssignedAccessLedgerRow> Rows, bool Truncated)> ReadInheritedLedgerByParentAsync(
            string parentTable, Guid parentId, CancellationToken ct)
        {
            if (FailLedgerRead)
                throw new HttpRequestException("Simulated ledger read failure.");
            var source = InheritedSourceField(parentTable, parentId);
            lock (_gate)
            {
                // Task 158 final round (round 58 item 2): only rows still in force — the production query leaves Revoked out.
                return Task.FromResult<(IReadOnlyList<AssignedAccessLedgerRow>, bool)>(
                    (Ledger.Where(r => string.Equals(r.SourceField, source, StringComparison.OrdinalIgnoreCase)
                                       && r.State != AssignedAccessState.Revoked).Select(Clone).ToList(),
                        InheritedByParentTruncated));
            }
        }

        /// <summary>Task 158 r1c-v2: the by-parent read reports more rows than one pass reads (its <c>Truncated</c>).</summary>
        public bool InheritedByParentTruncated { get; set; }

        /// <summary>
        /// Task 158 r1c-v1: runs just before an inherited-share create lands — a CONCURRENT pass creating the same row in the
        /// window between this pass's read and its create (the race the alternate key settles).
        /// </summary>
        public Action<string>? BeforeInheritedCreate { get; set; }

        /// <summary>Task 158 r1c-v1: inherited-share creates that lost the race to the alternate key (answered <c>null</c>).</summary>
        public List<string> InheritedCreateConflicts { get; } = new();

        internal override Task<Guid?> CreateInheritedLedgerAsync(
            ExternalGrantRootType rootType, Guid rootId, string parentTable, Guid parentId, DataversePrincipalRef principal,
            AssignedAccessLedgerWrite write, CancellationToken ct)
        {
            if (FailLedgerWrites)
                throw new HttpRequestException("Simulated ledger write failure.");

            var key = InheritedLedgerKey(rootType, rootId, parentTable, parentId, principal);
            BeforeInheritedCreate?.Invoke(key);
            lock (_gate)
            {
                // The production store's conflict path (the alternate key answered 412 and the row reads back): nothing is
                // written over the row a concurrent pass created; the caller is told it did not record.
                if (Ledger.Any(r => r.LedgerKey == key))
                {
                    InheritedCreateConflicts.Add(key);
                    return Task.FromResult<Guid?>(null);
                }

                var row = new AssignedAccessLedgerRow
                {
                    Id = Guid.NewGuid(),
                    LedgerKey = key,
                    SourceField = InheritedSourceField(parentTable, parentId),
                    ProjectId = rootType == ExternalGrantRootType.Project ? rootId : null,
                    MatterId = rootType == ExternalGrantRootType.Matter ? rootId : null,
                    WorkAssignmentId = rootType == ExternalGrantRootType.WorkAssignment ? rootId : null,
                    SystemUserId = principal.Kind == DataversePrincipalKind.SystemUser ? principal.Id : null,
                    SubjectTeamId = principal.Kind == DataversePrincipalKind.Team ? principal.Id : null,
                };
                Apply(row, write with { SystemUserId = null });
                Bump(row);
                Ledger.Add(row);
                Writes.Add(("create", key, write.State));
                return Task.FromResult<Guid?>(row.Id);
            }
        }

        /// <summary>
        /// Task 158 r1c-v1: inserts an inherited-share row as a concurrent pass would have written it (no fault, no hook) —
        /// the race seeds.
        /// </summary>
        public AssignedAccessLedgerRow SeedInheritedRow(
            ExternalGrantRootType rootType, Guid rootId, string parentTable, Guid parentId, DataversePrincipalRef principal,
            AssignedAccessLedgerWrite write)
        {
            var row = new AssignedAccessLedgerRow
            {
                Id = Guid.NewGuid(),
                LedgerKey = InheritedLedgerKey(rootType, rootId, parentTable, parentId, principal),
                SourceField = InheritedSourceField(parentTable, parentId),
                ProjectId = rootType == ExternalGrantRootType.Project ? rootId : null,
                MatterId = rootType == ExternalGrantRootType.Matter ? rootId : null,
                WorkAssignmentId = rootType == ExternalGrantRootType.WorkAssignment ? rootId : null,
                SystemUserId = principal.Kind == DataversePrincipalKind.SystemUser ? principal.Id : null,
                SubjectTeamId = principal.Kind == DataversePrincipalKind.Team ? principal.Id : null,
            };
            Apply(row, write with { SystemUserId = null });
            lock (_gate)
            {
                Bump(row);
                Ledger.Add(row);
            }

            return row;
        }

        internal override Task<AssignedRootSnapshot?> ReadRootAsync(
            ExternalGrantRootType rootType, Guid rootId, IReadOnlyCollection<string> fields, CancellationToken ct)
        {
            if (FailRootRead)
                throw new HttpRequestException("Simulated root read failure.");
            if (!Roots.TryGetValue((rootType, rootId), out var values))
                return Task.FromResult<AssignedRootSnapshot?>(null);

            return Task.FromResult<AssignedRootSnapshot?>(new AssignedRootSnapshot(
                fields.ToDictionary(f => f, f => values.TryGetValue(f, out var v) ? v : null, StringComparer.OrdinalIgnoreCase)));
        }

        internal override Task<IReadOnlyList<AssignedLinkCandidate>> ReadLinkCandidatesAsync(
            Guid contactId, Guid? boundOid, CancellationToken ct)
        {
            if (FailLinkRead)
                throw new HttpRequestException("Simulated systemuser read failure.");

            var result = new List<AssignedLinkCandidate>();
            if (UsersByLink.TryGetValue(contactId, out var linked))
                result.AddRange(linked);
            if (boundOid is { } oid && UsersByOid.TryGetValue(oid, out var byOid))
                result.AddRange(byOid);
            return Task.FromResult<IReadOnlyList<AssignedLinkCandidate>>(result);
        }

        internal override Task<int?> ReadOrganizationStateAsync(Guid organizationId, CancellationToken ct)
            => Task.FromResult(OrganizationStates.TryGetValue(organizationId, out var s) ? s : 0);

        internal override Task<IReadOnlyDictionary<Guid, string>> ReadSubjectNamesAsync(
            IReadOnlyCollection<AssignedSubject> subjects, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                subjects.Where(s => Names.ContainsKey(s.Id)).ToDictionary(s => s.Id, s => Names[s.Id]));

        internal override Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanAssignedRootsAsync(
            ExternalGrantRootType rootType, IReadOnlyCollection<string> fields, CancellationToken ct)
        {
            if (FailScan)
                throw new HttpRequestException("Simulated scan failure.");
            var roots = Roots
                .Where(kv => kv.Key.Item1 == rootType && kv.Value.Any(v => v.Value is not null && fields.Contains(v.Key, StringComparer.OrdinalIgnoreCase)))
                .Select(kv => new AssignedRootRef(rootType, kv.Key.Item2, Modified.TryGetValue(kv.Key.Item2, out var m) ? m : null))
                .ToList();
            var ceiling = ScanCeiling ?? MaxScanRows;
            return Task.FromResult<(IReadOnlyList<AssignedRootRef>, bool)>(
                roots.Count > ceiling ? (roots.Take(ceiling).ToList(), true) : (roots, false));
        }

        /// <summary>Task 114: the roots whose Access Permission is Restricted (what the production scan's filter selects).</summary>
        public ConcurrentDictionary<(ExternalGrantRootType Type, Guid Id), bool> RestrictedRoots { get; } = new();

        internal override Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanRestrictedRootsAsync(
            ExternalGrantRootType rootType, CancellationToken ct)
        {
            if (FailScan)
                throw new HttpRequestException("Simulated scan failure.");
            var roots = RestrictedRoots.Keys
                .Where(k => k.Type == rootType)
                .Select(k => new AssignedRootRef(rootType, k.Id, Modified.TryGetValue(k.Id, out var m) ? m : null))
                .ToList();
            return Task.FromResult<(IReadOnlyList<AssignedRootRef>, bool)>((roots, false));
        }

        internal override Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanLedgerRootsAsync(CancellationToken ct)
        {
            if (FailScan)
                throw new HttpRequestException("Simulated scan failure.");
            lock (_gate)
            {
                // The production OData filter's predicate (task 158 r1): live rows, inherited-share provenance rows left out.
                // AssignedAccessStoreODataTests pins the production filter itself over an evaluating Web API double.
                var roots = Ledger.Where(r => r.State != AssignedAccessState.Revoked && InheritedSourceOf(r.SourceField) is null)
                    .Select(RootOf).Where(r => r is not null).Select(r => r!.Value).Distinct().ToList();
                return Task.FromResult<(IReadOnlyList<AssignedRootRef>, bool)>((roots, false));
            }
        }

        /// <summary>
        /// Owner round 71: live Assigned-To rows whose every root lookup is empty — the production OData predicate, pinned
        /// itself by <c>AssignedAccessStoreODataTests</c>.
        /// </summary>
        internal override Task<(IReadOnlyList<AssignedAccessLedgerRow> Rows, bool Truncated)> ScanRootlessLedgerRowsAsync(CancellationToken ct)
        {
            if (FailRootlessScan)
                throw new HttpRequestException("Simulated rootless-ledger scan failure.");
            lock (_gate)
            {
                var rows = Ledger.Where(r => r.State != AssignedAccessState.Revoked && RootIdOf(r) is null
                                             && InheritedSourceOf(r.SourceField) is null)
                    .Select(Clone).ToList();
                return Task.FromResult<(IReadOnlyList<AssignedAccessLedgerRow>, bool)>((rows, false));
            }
        }

        /// <summary>
        /// Models the record's DELETION as Dataverse performs it for a matter or work assignment: the record is gone and the
        /// RemoveLink cascade empties the root lookup of every ledger row on it (the key, text, keeps the id).
        /// </summary>
        public void DeleteRoot(ExternalGrantRootType type, Guid rootId)
        {
            Roots.TryRemove((type, rootId), out _);
            ClearRootLookup(rootId);
        }

        /// <summary>Empties the root lookup of every ledger row on <paramref name="rootId"/>, leaving the record itself in place.</summary>
        public void ClearRootLookup(Guid rootId)
        {
            lock (_gate)
            {
                foreach (var row in Ledger.Where(r => RootIdOf(r) == rootId))
                {
                    row.ProjectId = row.MatterId = row.WorkAssignmentId = null;
                    Bump(row);
                }
            }
        }

        public bool FailRootlessScan { get; set; }
        public bool FailScan { get; set; }
        public int? ScanCeiling { get; set; }
        public ConcurrentDictionary<Guid, DateTimeOffset> Modified { get; } = new();

        private static Guid? RootIdOf(AssignedAccessLedgerRow r) => r.ProjectId ?? r.MatterId ?? r.WorkAssignmentId;

        private static AssignedAccessLedgerRow NewRow(ExternalGrantRootType type, Guid rootId, string field, AssignedSubject subject)
            => new()
            {
                Id = Guid.NewGuid(),
                LedgerKey = LedgerKey(type, rootId, field, subject),
                SourceField = field,
                ProjectId = type == ExternalGrantRootType.Project ? rootId : null,
                MatterId = type == ExternalGrantRootType.Matter ? rootId : null,
                WorkAssignmentId = type == ExternalGrantRootType.WorkAssignment ? rootId : null,
                SubjectContactId = subject.Kind == AssignedSubjectKind.Contact ? subject.Id : null,
                SubjectOrganizationId = subject.Kind == AssignedSubjectKind.Organization ? subject.Id : null,
            };

        /// <summary>Applies a write exactly as the production payload does: state and reason always, the rest when set.</summary>
        private static void Apply(AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write)
        {
            row.StateValue = (int)write.State;
            row.Reason = write.Reason;
            if (write.GrantId is { } g && g != Guid.Empty) row.GrantId = g;
            if (write.SystemUserId is { } u && u != Guid.Empty) row.SystemUserId = u;
            if (write.GrantedLevel is { } l) row.GrantedLevel = l;
            if (write.GrantedExpiry is { } e) row.GrantedExpiry = e;
        }

        private static AssignedAccessLedgerRow Clone(AssignedAccessLedgerRow r)
            => JsonSerializer.Deserialize<AssignedAccessLedgerRow>(JsonSerializer.Serialize(r))!;
    }

    /// <summary>
    /// An in-memory <c>sprk_externalrecordaccess</c> table behind <see cref="DataverseWebApiClient"/>'s virtual methods. It
    /// INTERPRETS the production filter (<c>ExternalGrantKey.ToActiveRowsFilter</c>) for all three root types, so the grant
    /// core and the materializer read exactly the rows a real query would return.
    /// </summary>
    internal sealed class GrantTable : DataverseWebApiClient
    {
        private const string GrantSet = "sprk_externalrecordaccesses";
        private readonly List<ExternalGrantRow> _rows = new();
        private readonly object _gate = new();

        public GrantTable()
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
        {
        }

        /// <summary>
        /// Task 114: the OWNING user of a project / matter / work assignment, answered to a root read by id
        /// (<c>_owninguser_value</c>) — the Restricted remover's external-owner check. A root not listed comes back team-owned.
        /// </summary>
        public ConcurrentDictionary<Guid, Guid> RootOwners { get; } = new();

        /// <summary>Task 114 verifier V3: a root-owner read fails (every other query still answers).</summary>
        public bool FailRootOwnerQueries { get; set; }

        /// <summary>System users the share routes can read (eligible internal people unless seeded otherwise).</summary>
        public ConcurrentDictionary<Guid, Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.SystemUserRow> SystemUsers { get; } = new();

        public void Person(Guid id) => SystemUsers[id] = new Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.SystemUserRow
        {
            Id = id,
            FullName = "Person",
            IsDisabled = false,
            AccessMode = 0,
            ApplicationId = null,
            IsExternal = false,
        };

        /// <summary>
        /// Task 181: the caller's systemuser by Entra oid — what <c>ResolveGrantedBySystemUserIdAsync</c> reads to tell the
        /// granter apart. Empty by default (no caller resolves, as before).
        /// </summary>
        public ConcurrentDictionary<Guid, Guid> SystemUserIdsByOid { get; } = new();

        /// <summary>Task 181: a root record's display name, answered to the grant notification's app-only read by id.</summary>
        public ConcurrentDictionary<Guid, string> RootNames { get; } = new();

        public List<(string Set, string Payload)> Creates { get; } = new();
        public List<(string Set, Guid Id, string Payload)> Updates { get; } = new();
        public bool FailQueries { get; set; }

        /// <summary>
        /// Runs before every grant-table query with its 1-based ordinal — lets a test change a row BETWEEN two reads
        /// (the materializer's, then the grant core's), as a concurrent writer or a midnight would.
        /// </summary>
        public Action<int>? BeforeGrantQuery { get; set; }

        private int _grantQueries;

        /// <summary>Every Dataverse write this table took (grant creates and updates).</summary>
        public int WriteCount => Creates.Count + Updates.Count;

        public IReadOnlyList<ExternalGrantRow> Rows
        {
            get { lock (_gate) return _rows.ToList(); }
        }

        public IReadOnlyList<ExternalGrantRow> ActiveRowsOf(Guid rootId, Guid? contactId = null, Guid? organizationId = null)
        {
            lock (_gate)
            {
                return _rows.Where(r => r.IsActive && RootOf(r) == rootId
                                        && (contactId is null || r.ContactId == contactId)
                                        && (organizationId is null || (r.OrganizationId == organizationId && r.ContactId is null)))
                    .ToList();
            }
        }

        public ExternalGrantRow Seed(ExternalGrantRootType type, Guid rootId, Guid? contactId, Guid? organizationId, int level,
            DateOnly? expiry)
        {
            var row = new ExternalGrantRow
            {
                Id = Guid.NewGuid(),
                ContactId = contactId,
                OrganizationId = organizationId,
                ProjectId = type == ExternalGrantRootType.Project ? rootId : null,
                MatterId = type == ExternalGrantRootType.Matter ? rootId : null,
                WorkAssignmentId = type == ExternalGrantRootType.WorkAssignment ? rootId : null,
                AccessLevel = level,
                ExpiresDate = expiry,
                StateCode = 0,
            };
            lock (_gate)
                _rows.Add(row);
            return row;
        }

        /// <summary>Deactivates a row outside the BFF (an MDA form or grid edit).</summary>
        public void DeactivateOutOfBand(Guid id)
        {
            lock (_gate)
                _rows.Single(r => r.Id == id).StateCode = 1;
        }

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
            int? top = null, int? skip = null, CancellationToken cancellationToken = default)
        {
            if (FailQueries)
                throw new HttpRequestException("Simulated Dataverse query failure.");

            if (entitySetName == GrantSet)
                BeforeGrantQuery?.Invoke(Interlocked.Increment(ref _grantQueries));

            if (FailRootOwnerQueries && entitySetName is "sprk_projects" or "sprk_matters" or "sprk_workassignments")
                throw new HttpRequestException("Simulated root-owner read failure.");

            object rows = entitySetName switch
            {
                GrantSet => MatchGrants(filter),
                // The share routes read a user by id; sprk_grantedby resolution reads by oid (task 181: SystemUserIdsByOid).
                "systemusers" when filter is not null && filter.StartsWith("azureactivedirectoryobjectid eq ", StringComparison.Ordinal) =>
                    SystemUserIdsByOid
                        .Where(kv => filter.Contains(kv.Key.ToString(), StringComparison.OrdinalIgnoreCase))
                        .Select(kv => new Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.SystemUserRow { Id = kv.Value })
                        .ToList(),
                "systemusers" => SystemUsers.Values
                    .Where(u => filter is not null && filter.Contains($"systemuserid eq {u.Id}", StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                "sprk_projects" or "sprk_matters" or "sprk_workassignments" => RootOwners
                    .Where(kv => filter is not null && filter.Contains(kv.Key.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(kv => new Dictionary<string, object?> { ["_owninguser_value"] = kv.Value })
                    .ToList<object>(),
                _ => throw new InvalidOperationException($"Unexpected query of {entitySetName} through the grant table."),
            };

            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        public override Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null,
            CancellationToken cancellationToken = default) where T : default
        {
            // Task 181: the grant notification reads a root's display name by id, selecting that one column.
            if (entitySetName is "sprk_projects" or "sprk_matters" or "sprk_workassignments")
            {
                return Task.FromResult(RootNames.TryGetValue(id, out var name) && select is not null
                    ? JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(new Dictionary<string, object?> { [select] = name }))
                    : default);
            }

            lock (_gate)
            {
                var row = _rows.FirstOrDefault(r => r.Id == id);
                return Task.FromResult(row is null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row)));
            }
        }

        public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
        {
            if (entitySetName != GrantSet)
                throw new InvalidOperationException($"Unexpected create in {entitySetName}.");

            var payload = (IDictionary<string, object?>)entity;
            var row = new ExternalGrantRow
            {
                Id = Guid.NewGuid(),
                ContactId = BoundId(payload, "sprk_Contact@odata.bind"),
                OrganizationId = BoundId(payload, "sprk_Organization@odata.bind"),
                ProjectId = BoundId(payload, "sprk_Project@odata.bind"),
                MatterId = BoundId(payload, "sprk_Matter@odata.bind"),
                WorkAssignmentId = BoundId(payload, "sprk_WorkAssignment@odata.bind"),
                AccessLevel = (int?)payload["sprk_accesslevel"],
                ExpiresDate = payload.TryGetValue("sprk_expiresdate", out var e) && e is string s
                    ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null,
                StateCode = 0,
            };
            lock (_gate)
            {
                _rows.Add(row);
                Creates.Add((entitySetName, JsonSerializer.Serialize(payload)));
            }

            return Task.FromResult(row.Id);
        }

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(entity);
            lock (_gate)
            {
                Updates.Add((entitySetName, id, json));
                var row = _rows.FirstOrDefault(r => r.Id == id);
                if (row is null)
                    return Task.CompletedTask;

                if (json.Contains("\"statecode\":1", StringComparison.Ordinal))
                    row.StateCode = 1;
                var level = Regex.Match(json, @"""sprk_accesslevel"":(\d+)");
                if (level.Success)
                    row.AccessLevel = int.Parse(level.Groups[1].Value, CultureInfo.InvariantCulture);
                var expiry = Regex.Match(json, @"""sprk_expiresdate"":""(\d{4}-\d{2}-\d{2})""");
                if (expiry.Success)
                    row.ExpiresDate = DateOnly.ParseExact(expiry.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return Task.CompletedTask;
        }

        private IEnumerable<ExternalGrantRow> MatchGrants(string? filter)
        {
            if (filter is null)
                return Enumerable.Empty<ExternalGrantRow>();

            var root = Regex.Match(filter, @"_sprk_(project|matter|workassignment)_value eq ([0-9a-fA-F-]{36})");
            var rootId = Guid.Parse(root.Groups[2].Value);
            var activeOnly = filter.Contains("statecode eq 0", StringComparison.Ordinal);

            lock (_gate)
            {
                var candidates = _rows.Where(r => RootOf(r) == rootId && (!activeOnly || r.IsActive));
                if (filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal))
                {
                    var orgId = Guid.Parse(Regex.Match(filter, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                    return candidates.Where(r => r.OrganizationId == orgId && r.ContactId is null).ToList();
                }

                var contact = Regex.Match(filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})");
                return contact.Success
                    ? candidates.Where(r => r.ContactId == Guid.Parse(contact.Groups[1].Value)).ToList()
                    : candidates.ToList();
            }
        }

        private static Guid? RootOf(ExternalGrantRow r) => r.ProjectId ?? r.MatterId ?? r.WorkAssignmentId;

        private static Guid? BoundId(IDictionary<string, object?> payload, string key) =>
            payload.TryGetValue(key, out var value) && value is string s
                ? Guid.Parse(Regex.Match(s, @"\(([0-9a-fA-F-]{36})\)").Groups[1].Value)
                : null;

    }

    /// <summary>A credential for client doubles that issue no HTTP.</summary>
    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }

    /// <summary>
    /// A singleton-safe <see cref="DataverseWebApiClient"/> for a test host: forwards every call to the CURRENT
    /// <see cref="GrantTable"/> (a fixture resets its harness per test), so the host can keep the client a singleton as
    /// production registers it.
    /// </summary>
    internal sealed class ForwardingClient : DataverseWebApiClient
    {
        private readonly Func<DataverseWebApiClient> _target;

        public ForwardingClient(Func<DataverseWebApiClient> target)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
            => _target = target;

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
            int? top = null, int? skip = null, CancellationToken cancellationToken = default)
            => _target().QueryAsync<T>(entitySetName, filter, select, top, skip, cancellationToken);

        public override Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null,
            CancellationToken cancellationToken = default) where T : default
            => _target().RetrieveAsync<T>(entitySetName, id, select, cancellationToken);

        public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
            => _target().CreateAsync(entitySetName, entity, cancellationToken);

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
            => _target().UpdateAsync(entitySetName, id, entity, cancellationToken);
    }

    /// <summary>A standing-grant reader answered from memory (unseeded = not held).</summary>
    internal sealed class InMemoryStandingGrants : ISubjectStandingGrantReader
    {
        public ConcurrentDictionary<Guid, ExternalAccessLevel> Contacts { get; } = new();
        public ConcurrentDictionary<Guid, ExternalAccessLevel> Organizations { get; } = new();

        public Task<StandingGrantState> ReadForContactAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(Contacts.TryGetValue(contactId, out var l) ? new StandingGrantState(true, l) : StandingGrantState.NotHeld);

        public Task<StandingGrantState> ReadForOrganizationAsync(Guid organizationId, CancellationToken ct)
            => Task.FromResult(Organizations.TryGetValue(organizationId, out var l) ? new StandingGrantState(true, l) : StandingGrantState.NotHeld);
    }

    /// <summary>The canonical access-conferring registry, bound as production binds it.</summary>
    internal static IOptions<MembershipOptions> CanonicalRegistry()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(null, options);
        return Options.Create(options);
    }

    /// <summary>Everything one materializer needs, wired, with the production materializer between the doubles.</summary>
    internal sealed class Harness
    {
        /// <param name="store">Task 158 r1: a ledger shared with another component under test (the secure-root inheritance).</param>
        public Harness(FakeAssignedAccessStore? store = null) => Store = store ?? new FakeAssignedAccessStore();

        public FakeAssignedAccessStore Store { get; }
        public GrantTable Grants { get; } = new();
        /// <summary>The flag reads. Settable (task 174) so a test can give it a filing world to walk; by default nothing is
        /// filed under anything.</summary>
        public GrantPolicyTestDoubles.FlagStubParticipationService Participations { get; set; } = new(RootRecordFlags.None);
        /// <summary>The deny list; replace it to model an entry being deactivated (the wall lifted).</summary>
        public GrantPolicyTestDoubles.SeamNoAccessListReader DenyList { get; set; } = new();
        public InMemoryContactIdentityStore Identities { get; } = new();
        public FakeRecordShareTable Shares { get; } = new();
        public NoAccessEnforcementTestDoubles.RecordingCache Cache { get; } = new();
        public InMemoryStandingGrants Standing { get; } = new();
        public FixedTimeProvider Time { get; } = new();
        public IOptions<MembershipOptions> Registry { get; set; } = CanonicalRegistry();

        public IConfiguration Configuration { get; set; } = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = TestTenant })
            .Build();

        /// <summary>The materializer's logger; set a capturing logger to assert what it reports (task 142 r3).</summary>
        public ILogger<AssignedAccessMaterializer> Logger { get; set; } = NullLogger<AssignedAccessMaterializer>.Instance;

        public AccessibleRecordSetService AccessibleRecords => GrantPolicyTestDoubles.RealDenyList(Participations, DenyList);

        /// <summary>
        /// Replaces the write-time No Access check the materializer (and, through it, the grant core) consults — the real
        /// one over <see cref="DenyList"/> when null. Task 142 r5: only to hand it an answer no production check returns.
        /// </summary>
        public IAccessibleRecordSetService? NoAccessCheckOverride { get; set; }

        /// <summary>
        /// Task 158 r1c-v2 (round 39 item 2): what the guard's filing walk reads — by default no row (a record filed under
        /// nothing, so only its own No Access list applies); a host fixture passes its world.
        /// </summary>
        public Spaarke.Dataverse.IGenericEntityService Entities { get; set; } = NoFilingRows();

        public SecureShareNoAccessGuard Guard =>
            GuardOverride ?? new(Participations, DenyList, Identities, Entities, NullLogger<SecureShareNoAccessGuard>.Instance);

        /// <summary>Task 158 r1c-v2: a host fixture's own guard (its world, its deny list), used instead of <see cref="Guard"/>.</summary>
        public SecureShareNoAccessGuard? GuardOverride { get; set; }

        /// <summary>Task 158 r1c-v2: a host fixture's own share seam, used instead of <see cref="Shares"/>.</summary>
        public Sprk.Bff.Api.Services.Access.IDataverseRecordShareService? SharesOverride { get; set; }

        /// <summary>
        /// Task 158 r1c-v2 (round 47 item 1 (3)): the host's scopes, through which the materializer reaches the secure-root
        /// inheritance's sharee-only pass after an assignment ended; <c>null</c> (no host) by default.
        /// </summary>
        public Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? Scopes { get; set; }

        /// <summary>
        /// Task 149 (batch 4 integration, the 142 x 149 merge-order obligation): the Dataverse rows the REAL secure-child
        /// share synchronizer reads — a world with no secure record by default, so it answers "not applicable"; seed a
        /// Secure-team-owned root and children to watch a root share write reach them.
        /// </summary>
        public Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld ChildWorld { get; set; } =
            Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard();

        /// <summary>The REAL synchronizer over <see cref="ChildWorld"/>, this harness's share table and its No Access guard.</summary>
        public Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer Children =>
            Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.SynchronizerOver(() => ChildWorld, Shares, Guard);

        public AssignedAccessMaterializer Materializer => new(
            Store, Grants, Participations, NoAccessCheckOverride ?? AccessibleRecords, Identities, Guard, SharesOverride ?? Shares,
            Children, Cache.Mock.Object, Standing, Registry, Configuration, Time, Logger, Scopes);

        /// <summary>Task 181: every <c>appnotification</c> the grant notifier wrote through task 100's NotificationService.</summary>
        public ConcurrentQueue<Microsoft.Xrm.Sdk.Entity> SentNotifications { get; } = new();

        /// <summary>Task 181: when set, every notification write throws this (Dataverse refusing the create).</summary>
        public Exception? NotificationWriteFailure { get; set; }

        /// <summary>
        /// Task 181: the PRODUCTION grant notifier over this harness's grant table (names, the caller's oid), identity
        /// store and ledger store (who a contact represents) and No Access guard; the PRODUCTION NotificationService over
        /// an entity seam that records each write.
        /// </summary>
        public Sprk.Bff.Api.Api.ExternalAccess.GrantAccessNotifier Notifier
        {
            get
            {
                var entities = new Moq.Mock<Spaarke.Dataverse.IGenericEntityService>();
                entities
                    .Setup(e => e.CreateAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Entity>(), Moq.It.IsAny<CancellationToken>()))
                    .Returns((Microsoft.Xrm.Sdk.Entity entity, CancellationToken _) =>
                    {
                        if (NotificationWriteFailure is { } failure)
                            throw failure;
                        SentNotifications.Enqueue(entity);
                        return Task.FromResult(Guid.NewGuid());
                    });
                return new Sprk.Bff.Api.Api.ExternalAccess.GrantAccessNotifier(
                    new Sprk.Bff.Api.Services.NotificationService(entities.Object, NullLogger<Sprk.Bff.Api.Services.NotificationService>.Instance),
                    Grants, Identities, Store, Guard, Participations, NullLogger<Sprk.Bff.Api.Api.ExternalAccess.GrantAccessNotifier>.Instance);
            }
        }

        /// <summary>
        /// Task 114: the PRODUCTION Restricted remover over this harness's flags, share table, system users
        /// (<see cref="GrantTable.SystemUsers"/>), cache and child synchronizer.
        /// </summary>
        public RestrictedExternalShareRemover RestrictedRemover => new(
            Participations, SharesOverride ?? Shares, Grants, Cache.Mock.Object, Children, Lease,
            NullLogger<RestrictedExternalShareRemover>.Instance);

        /// <summary>Task 114: the per-record removal lease the remover shares with task 143's enforcer and /unshare-user.</summary>
        public Spaarke.Scheduling.IScheduledJobLease Lease { get; set; } = new Spaarke.Scheduling.ProcessLocalScheduledJobLease();

        /// <summary>
        /// Task 114: a system user the share routes and the Restricted remover read (an enabled person; the flag as given —
        /// <c>null</c> is a blank <c>sprk_isexternal</c>).
        /// </summary>
        public Guid SystemUser(bool? isExternal, bool disabled = false, Guid? id = null)
        {
            var userId = id ?? Guid.NewGuid();
            Grants.SystemUsers[userId] = new Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.SystemUserRow
            {
                Id = userId,
                FullName = $"User {userId:N}",
                IsDisabled = disabled,
                AccessMode = 0,
                ApplicationId = null,
                IsExternal = isExternal,
            };
            return userId;
        }

        /// <summary>An active, UNLINKED contact (no systemuser represents it).</summary>
        public Guid Contact(Guid? id = null, int stateCode = 0, string? oid = null)
        {
            var contactId = id ?? Guid.NewGuid();
            Identities.AddContact(contactId, oid: oid, stateCode: stateCode);
            return contactId;
        }

        /// <summary>A contact linked (141, <c>sprk_primarycontact</c>) to a systemuser with the given eligibility.</summary>
        public (Guid ContactId, Guid SystemUserId) LinkedContact(
            bool disabled = false, int accessMode = 0, Guid? applicationId = null, bool? isExternal = false)
        {
            var contactId = Contact();
            var userId = Guid.NewGuid();
            var oid = Guid.NewGuid();
            Store.UsersByLink.GetOrAdd(contactId, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(userId, contactId, oid, disabled, accessMode, applicationId, isExternal));
            // The same user in the identity store task 143's guard reads (its link, status-first).
            Identities.AddSystemUser(userId, oid, $"{userId:N}@firm.example", primaryContactId: contactId,
                disabled: disabled, accessMode: accessMode, applicationId: applicationId);
            return (contactId, userId);
        }

        /// <summary>Links an existing contact to a NEW eligible systemuser (task 141's job linking it later).</summary>
        public Guid LinkLater(Guid contactId)
        {
            var userId = Guid.NewGuid();
            var oid = Guid.NewGuid();
            Store.UsersByLink.GetOrAdd(contactId, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(userId, contactId, oid, false, 0, null, false));
            Identities.AddSystemUser(userId, oid, $"{userId:N}@firm.example", primaryContactId: contactId);
            return userId;
        }

        public Task<AssignedAccessOutcome> SyncAsync(ExternalGrantRootType type, Guid rootId, bool revokeOnChange = true,
            AssignedAccessTrigger trigger = AssignedAccessTrigger.Sync, string? grantorOid = null)
            => Materializer.MaterializeAsync(
                new AssignedAccessRequest(type, rootId, trigger, grantorOid, revokeOnChange, new[] { TestTenant }),
                CancellationToken.None);

        /// <summary>Every Dataverse write any boundary took: grants, shares and ledger rows.</summary>
        public int TotalWrites => Grants.WriteCount + Shares.Writes.Count + Store.Writes.Count;
    }

    /// <summary>
    /// A materializer over an EMPTY ledger, for handler tests that are not about task 142: its operator markers find
    /// nothing to mark, so they are no-ops (and never throw).
    /// </summary>
    internal static AssignedAccessMaterializer InertMaterializer() => new Harness().Materializer;

    /// <summary>
    /// Task 181: a grant notifier over an EMPTY world, for handler tests that are not about the notification: no contact
    /// represents anyone, and a share's notification is written to an entity seam nobody reads. It never throws.
    /// </summary>
    internal static Sprk.Bff.Api.Api.ExternalAccess.GrantAccessNotifier InertNotifier() => new Harness().Notifier;

    /// <summary>
    /// Task 158 r1c-v2 (round 39 item 2): an <see cref="Spaarke.Dataverse.IGenericEntityService"/> that finds no row — the No
    /// Access guard's filing walk then finds no parent, so only the record's own list applies (tests not about filing).
    /// </summary>
    internal static Spaarke.Dataverse.IGenericEntityService NoFilingRows()
    {
        var entities = new Moq.Mock<Spaarke.Dataverse.IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveMultipleAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Microsoft.Xrm.Sdk.EntityCollection()));
        return entities.Object;
    }
}
