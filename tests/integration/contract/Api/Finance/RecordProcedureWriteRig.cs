using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084d-3 (design notes/084d-dry-run-design.md §5-§6): <see cref="RecordProcedureRig"/> in a WRITABLE mode, on its own instance.
/// The rig's strict entity service gains a transaction interpreter over the same in-memory world (create, update by row version, delete; each
/// created row stamped with <c>createdon</c> from <see cref="Clock"/> and a row version), and two REAL <see cref="ProcedureAssignmentService"/>s
/// over it: <see cref="Writer"/> (the write, with a stub ownership answer and a mocked close) and <see cref="Planner"/> (the dry run's planner, as
/// 084d-2's host builds it: a STRICT ownership resolver and a STRICT closure with only its two selections set up).
/// </summary>
/// <remarks>
/// <para><b>Rig page size (parity condition (a)).</b> The resolver pages every read at 5000 rows. <see cref="PageSizes"/> shrinks that page for
/// the four override tables, so a modest seed spans several pages and the overlay's last-page append really runs after earlier pages.
/// Only a query that pages at 5000 is shrunk (the readers that loop on <c>MoreRecords</c>); a query without paging is left alone.</para>
/// <para><b>Thread safety.</b> The world is not thread-safe, so every read and every transaction runs under one lock;
/// <see cref="BeforeQuery"/> runs before the lock, so concurrent flows interleave between queries.</para>
/// </remarks>
internal sealed class RecordProcedureWriteRig
{
    /// <summary>The one instant of the parity tests: the read service's clock (<see cref="RecordProcedureRig"/>), the dry run's stamp, the write's.</summary>
    public static readonly DateTimeOffset Instant = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>When seeded overrides are written through the REAL writer (earlier than <see cref="Instant"/>: parity condition (c)).</summary>
    public static readonly DateTimeOffset SeedInstant = new(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The writer's caller (Entra object id) and the creator person its stub ownership answer stamps.</summary>
    public static readonly Guid CallerObjectId = Guid.Parse("084d3000-0000-4000-8000-0000000000ca");

    public static readonly Guid OwnerTeam = Guid.Parse("084d3000-0000-4000-8000-0000000000e7");

    /// <summary>The four tables the overlay covers.</summary>
    public static readonly string[] OverrideTables = { ProcedureAssignmentEntity.Entity, RuleSet, Policy, PolicyVersion };

    public static readonly string[] WriteMembers =
    {
        nameof(IGenericEntityService.CreateAsync), nameof(IGenericEntityService.UpsertAsync), nameof(IGenericEntityService.UpdateAsync),
        nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync), nameof(IGenericEntityService.BulkUpdateAsync),
        nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync), nameof(IGenericEntityService.ExecuteTransactionAsync),
        nameof(IGenericEntityService.DeleteAsync), nameof(IGenericEntityService.AssociateAsync),
    };

    private readonly object _gate = new();
    private long _rowVersion = 1000;

    public RecordProcedureRig Rig { get; } = new();

    /// <summary>The write's clock: the transaction interpreter's <c>createdon</c> and the writer's in-force instant.</summary>
    public SettableClock Clock { get; } = new(Instant);

    /// <summary>The page size per override table for queries the resolver pages at 5000 (absent: unchanged).</summary>
    public Dictionary<string, int> PageSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every page the world answered on the four tables: (table, page number, more records, rows, the query's In values per column).</summary>
    public List<(string Table, int Page, bool More, int Rows, IReadOnlyDictionary<string, HashSet<Guid>> Lists)> Pages { get; } = new();

    /// <summary>Runs before each read, outside the lock (the concurrency tests interleave and hook here).</summary>
    public Func<QueryExpression, Task>? BeforeQuery { get; set; }

    /// <summary>Every transaction sent, committed or not.</summary>
    public List<IReadOnlyList<OrganizationRequest>> Transactions { get; } = new();

    /// <summary>False: the rig is STRICT — every write member of the seam throws (and is recorded as an invocation of the mock).</summary>
    public bool Writable { get; private set; } = true;

    public Mock<IRecordOwnershipResolver> Ownership { get; } = new(MockBehavior.Strict);

    public Mock<SignalClosureService> WriterClosure { get; }

    public Mock<IRecordOwnershipResolver> PlannerOwnership { get; } = new(MockBehavior.Strict);

    public Mock<SignalClosureService> PlannerClosure { get; }

    public ProcedureAssignmentService Writer { get; }

    public ProcedureAssignmentService Planner { get; }

    public RecordProcedureWriteRig()
    {
        var entities = Rig.Entities;
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns(async (QueryExpression query, CancellationToken _) =>
            {
                if (BeforeQuery is { } before)
                {
                    await before(query);
                }

                return Answer(query);
            });
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                lock (_gate)
                {
                    return Task.FromResult(Rig.World.Retrieve(table, id, columns));
                }
            });
        entities
            .Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
            {
                Transact(requests);
                return Task.CompletedTask;
            });

        Ownership
            .Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Owned(OwnerTeam) with { CreatedByPerson = Reviewer });

        var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter));
        WriterClosure = new Mock<SignalClosureService>(MockBehavior.Strict, writer, entities.Object, TimeProvider.System, NullLogger<SignalClosureService>.Instance);
        PlannerClosure = new Mock<SignalClosureService>(MockBehavior.Strict, writer, entities.Object, TimeProvider.System, NullLogger<SignalClosureService>.Instance);
        foreach (var closure in new[] { WriterClosure, PlannerClosure })
        {
            closure.Setup(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<OpenPolicySignal>());
            closure.Setup(c => c.SelectOpenSignalsOfPolicyOnSubjectsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<OpenPolicySignal>());
        }

        // The write's close of a removed changed rule (no findings are seeded, so it closes nothing).
        WriterClosure.Setup(c => c.ClosePolicySignalsRetiredAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicySignalClosure(SignalClosureOutcome.None, Array.Empty<OpenPolicySignal>()));

        var counter = new AffectedInvoiceCounter(entities.Object, Rig.Probe, NullLogger<AffectedInvoiceCounter>.Instance);
        Writer = new ProcedureAssignmentService(
            entities.Object, Ownership.Object, Rig.RuleSets, WriterClosure.Object, counter, NullLogger<ProcedureAssignmentService>.Instance)
        { Clock = Clock };
        Planner = new ProcedureAssignmentService(
            entities.Object, PlannerOwnership.Object, Rig.RuleSets, PlannerClosure.Object, counter, NullLogger<ProcedureAssignmentService>.Instance);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────────────────

    private EntityCollection Answer(QueryExpression query)
    {
        lock (_gate)
        {
            var overrideTable = OverrideTables.Contains(query.EntityName, StringComparer.OrdinalIgnoreCase);
            if (overrideTable && query.PageInfo is { Count: 5000 } paging && PageSizes.TryGetValue(query.EntityName, out var size))
            {
                paging.Count = size;
            }

            var answer = Rig.World.Answer(query);
            if (overrideTable)
            {
                var lists = query.Criteria.Conditions
                    .Where(c => c.Operator is ConditionOperator.In or ConditionOperator.Equal)
                    .GroupBy(c => c.AttributeName)
                    .ToDictionary(g => g.Key, g => g.SelectMany(c => c.Values).OfType<Guid>().ToHashSet());
                Pages.Add((query.EntityName, Math.Max(query.PageInfo?.PageNumber ?? 1, 1), answer.MoreRecords, answer.Entities.Count, lists));
            }

            return answer;
        }
    }

    /// <summary>Every row of a table, all columns (for the world's before/after comparison and the created-on condition).</summary>
    public IReadOnlyList<Entity> RowsOf(string table)
    {
        lock (_gate)
        {
            return Rig.World.Answer(new QueryExpression(table) { ColumnSet = new ColumnSet(true) }).Entities.ToList();
        }
    }

    /// <summary>The four override tables as text, row by row (ids and values), for "the world is unchanged".</summary>
    public string Snapshot() => string.Join("\n", OverrideTables.SelectMany(t => RowsOf(t).OrderBy(r => r.Id).Select(r =>
        $"{t} {r.Id} " + string.Join(";", r.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={Text(a.Value)}")))));

    private static string Text(object? value) => value switch
    {
        EntityReference r => $"{r.LogicalName}:{r.Id}",
        OptionSetValue o => o.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    // ── The transaction interpreter ─────────────────────────────────────────────────────────────────────────

    private const int DuplicateKey = unchecked((int)0x80040237);
    private const int DoesNotExist = unchecked((int)0x80040217);
    private const int RowVersionMismatch = unchecked((int)0x80060882);

    /// <summary>
    /// ONE transaction, as Dataverse's ExecuteTransaction runs it: every request checked in order against the world as the earlier requests leave
    /// it (a create of an existing id is a duplicate key, an update or delete of a missing row does not exist, a conditional update needs the row
    /// version as it stands at its turn), and only then applied, all or nothing. A created row gets <c>createdon</c> from <see cref="Clock"/>
    /// (UTC) and a row version; an update advances the row version.
    /// </summary>
    private void Transact(IReadOnlyList<OrganizationRequest> requests)
    {
        lock (_gate)
        {
            Transactions.Add(requests.ToList());
            if (!Writable)
            {
                throw new InvalidOperationException("The rig is strict: a write was attempted.");
            }

            var created = new HashSet<(string, Guid)>();
            var deleted = new HashSet<(string, Guid)>();
            var versions = new Dictionary<(string, Guid), long>();
            bool Exists(string table, Guid id) => created.Contains((table, id)) || (Rig.World.Has(table, id) && !deleted.Contains((table, id)));
            for (var i = 0; i < requests.Count; i++)
            {
                var fault = requests[i] switch
                {
                    CreateRequest c when Exists(c.Target.LogicalName, c.Target.Id) => (DuplicateKey, "Cannot insert duplicate key."),
                    CreateRequest c => Mark(created, c.Target.LogicalName, c.Target.Id),
                    UpdateRequest u when !Exists(u.Target.LogicalName, u.Target.Id) => (DoesNotExist, "Does Not Exist"),
                    UpdateRequest u => CheckVersion(u, versions),
                    DeleteRequest d when !Exists(d.Target.LogicalName, d.Target.Id) => (DoesNotExist, "Does Not Exist"),
                    DeleteRequest d => Mark(deleted, d.Target.LogicalName, d.Target.Id),
                    var other => throw new NotSupportedException(other.GetType().Name),
                };
                if (fault is { } f)
                {
                    throw new DataverseTransactionException(
                        $"rig: request {i} failed; nothing was applied", i, false,
                        new System.ServiceModel.FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = f.Item1, Message = f.Item2 }, new System.ServiceModel.FaultReason(f.Item2)));
                }
            }

            var stamp = Clock.GetUtcNow().UtcDateTime;
            foreach (var request in requests)
            {
                switch (request)
                {
                    case CreateRequest c:
                        var columns = c.Target.Attributes.Select(a => (a.Key, a.Value)).ToList();
                        // Server-filled on create, as Dataverse fills them: createdon, and the default state and status.
                        if (!c.Target.Contains("createdon")) columns.Add(("createdon", stamp));
                        if (!c.Target.Contains("statecode")) columns.Add(("statecode", new OptionSetValue(0)));
                        if (!c.Target.Contains("statuscode")) columns.Add(("statuscode", new OptionSetValue(1)));
                        columns.RemoveAll(a => a.Key == "versionnumber");
                        columns.Add(("versionnumber", ++_rowVersion));
                        Rig.World.Remove(c.Target.LogicalName, c.Target.Id); // a create after a delete of the same id in one transaction
                        Rig.World.Add(c.Target.LogicalName, c.Target.Id, columns.ToArray());
                        break;
                    case UpdateRequest u:
                        foreach (var (column, value) in u.Target.Attributes)
                        {
                            Rig.World.Set(u.Target.LogicalName, u.Target.Id, column, value);
                        }

                        Rig.World.Set(u.Target.LogicalName, u.Target.Id, "versionnumber", ++_rowVersion);
                        break;
                    case DeleteRequest d:
                        Rig.World.Delete(d.Target.LogicalName, d.Target.Id);
                        break;
                }
            }
        }

        static (int, string)? Mark(HashSet<(string, Guid)> set, string table, Guid id)
        {
            set.Add((table, id));
            return null;
        }
    }

    private (int, string)? CheckVersion(UpdateRequest update, Dictionary<(string, Guid), long> versions)
    {
        var key = (update.Target.LogicalName, update.Target.Id);
        var current = versions.TryGetValue(key, out var moved) ? moved : Rig.World.ValueOf<long>(key.LogicalName, key.Id, "versionnumber");
        if (update.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches
            && current.ToString(System.Globalization.CultureInfo.InvariantCulture) != update.Target.RowVersion)
        {
            return (RowVersionMismatch, "The version of the existing record doesn't match the RowVersion property provided.");
        }

        versions[key] = current + 1;
        return null;
    }

    /// <summary>
    /// Makes the rig STRICT: every write member of the seam throws, the transaction included, so a dry run that wrote anything would fail (and be
    /// recorded as an invocation). Reads are unchanged.
    /// </summary>
    public void Strict()
    {
        Writable = false;
        var refused = new InvalidOperationException("The rig is strict: a write was attempted.");
        Rig.Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(refused);
        Rig.Entities.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(refused);
        Rig.Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(refused);
    }

    /// <summary>The write members of the seam the rig's entity service was called with (any, whether it threw or not).</summary>
    public IReadOnlyList<string> WriteInvocations() =>
        Rig.Entities.Invocations.Select(i => i.Method.Name).Where(WriteMembers.Contains).ToList();
}

/// <summary>084d-3: a clock a test sets forwards or back (the writer seeds at <see cref="RecordProcedureWriteRig.SeedInstant"/>, then writes at the parity instant).</summary>
internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset value) => _now = value;
}
