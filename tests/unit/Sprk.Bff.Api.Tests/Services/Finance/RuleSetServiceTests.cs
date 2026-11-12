using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="RuleSetService"/> (ebilling-r1 task 076, FR-28): create from a source document, the rules beside their clause quotes, edit /
/// accept / reject, publish (all or nothing, immutable versions), assignment, visibility, and the single clause-visibility decision.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees the rules engine and the firms rely on — nothing is in force before a publish, a published
/// version is never updated, one invalid rule publishes nothing, an Ontology policy is invisible to and unchanged by this service, and a
/// flag-off rule set never yields clause text. Dataverse and SPE are substituted at their narrow seams; validation, the describer and the
/// orchestration are real. The fake store evaluates the real query (nested filters, In, Null), so a lost domain filter is observable.
/// </remarks>
public sealed class RuleSetServiceTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid OtherFirmId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Drive = "b!procedurelibrary";
    private static readonly DateOnly Effective = new(2026, 11, 1);

    private const string CapBody = """{"type":"DailyHoursCap","maxHoursPerDay":10}""";
    private const string CapBody8 = """{"type":"DailyHoursCap","maxHoursPerDay":8}""";
    private const string IncrementBody = """{"type":"TimeIncrement","incrementHours":0.1}""";
    private const string Template = "A timekeeper billed over {{rule.maxHoursPerDay}} hours.";

    internal enum Write { Create, Update, BulkUpdate, Delete }

    private sealed class ListLogger(List<(LogLevel Level, string Message, Exception? Exception)> sink) : ILogger<RuleSetService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception), exception));
    }

    internal sealed class Harness
    {
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();

        public readonly Dictionary<string, List<Entity>> Rows = new();
        public readonly List<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> Writes = new();
        public readonly List<QueryExpression> Queries = new();
        public readonly List<string> Order = new();
        public readonly List<(string Entity, int Size)> BulkCalls = new();
        public Func<string, bool>? FailBulkFor;
        public Func<string, bool>? FailUpdateFor;

        /// <summary>The publish claim store the service takes (round 3): the real process-local lease, so a test can hold a claim or watch one released.</summary>
        public Spaarke.Scheduling.IScheduledJobLease Lease = new Spaarke.Scheduling.ProcessLocalScheduledJobLease();

        /// <summary>Runs at the start of every bulk update (after the cancellation check): a test uses it to disconnect the caller mid-write.</summary>
        public Action<string>? OnBulk;
        public readonly List<(LogLevel Level, string Message, Exception? Exception)> Logs = new();
        public Func<Entity, bool>? FailCreateWhen;

        /// <summary>Runs at the start of every create, before the duplicate-key check: a test uses it to run a concurrent edit.</summary>
        public Func<Entity, Task>? BeforeCreate;

        /// <summary>
        /// Runs after every read has produced its result (the rows the service got are already copied): a test uses it to land another
        /// admin's write between the service's read and its own write, whichever write method the service uses.
        /// </summary>
        public Action<QueryExpression>? AfterQuery;

        /// <summary>Every conditional bulk update as sent: table, ids and the row versions they were conditional on.</summary>
        public readonly List<(string Entity, IReadOnlyList<(Guid Id, long RowVersion)> Rows)> ConditionalCalls = new();

        /// <summary>Conditional bulk updates the fake refused as a row version mismatch (nothing applied).</summary>
        public readonly List<(string Entity, IReadOnlyList<Guid> Ids)> Refusals = new();

        /// <summary>The row version (Dataverse <c>versionnumber</c>) of every row: set at seed, advanced by every write.</summary>
        public readonly Dictionary<Guid, long> RowVersions = new();
        private readonly Dictionary<Guid, DateTime> _createdOn = new();
        private readonly Dictionary<Guid, DateTime> _modifiedOn = new();
        private readonly Dictionary<Guid, Guid> _createdBy = new();
        private long _clock;

        /// <summary>The application identity the fake writes as: <c>GetCurrentIdentityIdAsync</c> answers it and every row a service creates carries it as <c>createdby</c> (task 075).</summary>
        public Guid Identity = Guid.NewGuid();

        /// <summary>Makes <c>GetCurrentIdentityIdAsync</c> fault (a WhoAmI that failed).</summary>
        public bool FailIdentity { get; set; }

        /// <summary>Runs at the start of every delete: a returned exception is thrown and the row stays (a delete fault, or a row that is already gone).</summary>
        public Func<string, Guid, Exception?>? OnDelete;

        /// <summary>Every delete the service attempted, whether or not it succeeded.</summary>
        public readonly List<(string Entity, Guid Id)> DeleteAttempts = new();
        public bool IgnoreQueryFilters;
        public RecordOwnerResolution OwnerAnswer = RecordOwnerResolution.Owned(TeamId);

        public Harness()
        {
            Seed(new Entity("sprk_organization", FirmId));

            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OwnerAnswer);

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken token) =>
                {
                    Queries.Add(q);
                    QueryTokens.Add(token);
                    var matched = (Rows.GetValueOrDefault(q.EntityName) ?? new List<Entity>())
                        .Where(r => IgnoreQueryFilters && q.EntityName != "sprk_organization" ? true : Matches(r, q.Criteria))
                        .Select(Clone);
                    IOrderedEnumerable<Entity>? ordered = null;
                    foreach (var order in q.Orders) // the real query's order, as Dataverse applies it
                    {
                        Func<Entity, IComparable?> key = r => OrderKey(r, order.AttributeName);
                        ordered = ordered is null
                            ? order.OrderType == OrderType.Ascending ? matched.OrderBy(key) : matched.OrderByDescending(key)
                            : order.OrderType == OrderType.Ascending ? ordered.ThenBy(key) : ordered.ThenByDescending(key);
                    }

                    var result = new EntityCollection((ordered ?? matched).Take(q.TopCount ?? int.MaxValue).ToList());
                    AfterQuery?.Invoke(q);
                    return result;
                });
            Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns(async (Entity entity, CancellationToken _) =>
                {
                    if (BeforeCreate is not null)
                    {
                        await BeforeCreate(entity);
                    }

                    if (FailCreateWhen?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated Dataverse failure");
                    }

                    // A caller-supplied id is the primary key, as in Dataverse: a second create with it is a duplicate-key fault.
                    if (entity.Id != Guid.Empty && Rows.GetValueOrDefault(entity.LogicalName)?.Any(r => r.Id == entity.Id) == true)
                    {
                        throw new InvalidOperationException($"Failed to create {entity.LogicalName} record: Cannot insert duplicate key (0x80040237).");
                    }

                    entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
                    Writes.Add((Write.Create, entity.LogicalName, entity.Id, entity.Attributes.ToDictionary(a => a.Key, a => a.Value)));
                    Seed(entity);
                    _createdBy[entity.Id] = Identity;
                    return entity.Id;
                });
            Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested(); // the real seam honours the caller's token
                    if (FailUpdateFor?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated update failure");
                    }

                    Order.Add("update:" + entity);
                    Writes.Add((Write.Update, entity, id, fields));
                    Apply(entity, id, fields);
                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested(); // the real seam honours the caller's token
                    OnBulk?.Invoke(entity);
                    if (FailBulkFor?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated transaction failure");
                    }

                    Order.Add("bulk:" + entity);
                    BulkCalls.Add((entity, updates.Count));
                    foreach (var (id, fields) in updates)
                    {
                        Writes.Add((Write.BulkUpdate, entity, id, fields));
                        Apply(entity, id, fields);
                    }

                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                    It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken ct) =>
                {
                    // The same transaction as BulkUpdateAsync (one table, all or nothing), each update applied only at its row version.
                    ct.ThrowIfCancellationRequested();
                    OnBulk?.Invoke(entity);
                    ConditionalCalls.Add((entity, updates.Select(u => (u.id, u.rowVersion)).ToList()));
                    if (FailBulkFor?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated transaction failure");
                    }

                    if (updates.Any(u => RowVersions.GetValueOrDefault(u.id) != u.rowVersion))
                    {
                        Refusals.Add((entity, updates.Select(u => u.id).ToList()));
                        throw new System.Data.DBConcurrencyException("simulated row version mismatch: nothing applied");
                    }

                    Order.Add("bulk:" + entity);
                    BulkCalls.Add((entity, updates.Count));
                    foreach (var (id, fields, _) in updates)
                    {
                        Writes.Add((Write.BulkUpdate, entity, id, fields));
                        Apply(entity, id, fields);
                    }

                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.GetCurrentIdentityIdAsync(It.IsAny<CancellationToken>()))
                .Returns(() => FailIdentity ? throw new InvalidOperationException("simulated WhoAmI failure") : Task.FromResult(Identity));
            Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, CancellationToken _) =>
                {
                    DeleteAttempts.Add((entity, id));
                    if (OnDelete?.Invoke(entity, id) is { } fault)
                    {
                        throw fault;
                    }

                    Writes.Add((Write.Delete, entity, id, new Dictionary<string, object>()));
                    Rows.GetValueOrDefault(entity)?.RemoveAll(r => r.Id == id);
                    return Task.CompletedTask;
                });

            // Task 081: ONE transaction across tables, as Dataverse's ExecuteTransaction runs it — requests applied in order, a conditional
            // update checked at ITS turn (so a row updated earlier in the same transaction has moved), and any fault rolls every earlier
            // request back. The exception is the real seam's: DataverseTransactionException over the Dataverse fault.
            Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    Transactions.Add(requests.ToList());
                    if (TransactionOutcomeUnknown || TransactionGivesNoAnswer?.Invoke(requests) == true)
                    {
                        throw new DataverseTransactionException("simulated timeout: no fault came back", null, true, new TimeoutException());
                    }

                    if (TransactionThrows is { } thrown)
                    {
                        throw thrown;
                    }

                    var snapshot = TakeSnapshot();
                    for (var i = 0; i < requests.Count; i++)
                    {
                        var fault = FailTransactionRequest?.Invoke(requests[i]) == true
                            ? (unchecked((int)0x80040265), "simulated request failure")
                            : ApplyInTransaction(requests[i]);
                        if (fault is { } f)
                        {
                            RestoreSnapshot(snapshot);
                            TransactionFaults.Add((i, f.Item1));
                            throw new DataverseTransactionException(
                                $"simulated: request {i} failed; nothing was applied", i, false,
                                new System.ServiceModel.FaultException<OrganizationServiceFault>(
                                    new OrganizationServiceFault { ErrorCode = f.Item1, Message = f.Item2 },
                                    new System.ServiceModel.FaultReason(f.Item2)));
                        }
                    }

                    if (TransactionCommitsButAnswersUnknown?.Invoke(requests) == true)
                    {
                        throw new DataverseTransactionException("simulated timeout after the commit: no answer came back", null, true, new TimeoutException());
                    }

                    return Task.CompletedTask;
                });
        }

        /// <summary>Task 089e fix round 2: the token each query in <c>Queries</c> was sent with, in the same order.</summary>
        public readonly List<CancellationToken> QueryTokens = new();

        /// <summary>Every transaction as sent (task 081), whether or not it committed.</summary>
        public readonly List<IReadOnlyList<OrganizationRequest>> Transactions = new();

        /// <summary>Faulted transactions: the index of the request that faulted and its Dataverse error code.</summary>
        public readonly List<(int Index, int ErrorCode)> TransactionFaults = new();

        /// <summary>Faults the first request it answers true for (nothing in the transaction is applied).</summary>
        public Func<OrganizationRequest, bool>? FailTransactionRequest;

        /// <summary>The transaction returns no fault at all (a timeout): <see cref="DataverseTransactionException.OutcomeUnknown"/>, nothing applied.</summary>
        public bool TransactionOutcomeUnknown;

        /// <summary>Task 089e (K2): a transaction this answers true for is APPLIED, then answers no fault at all (<see cref="DataverseTransactionException.OutcomeUnknown"/>).</summary>
        public Func<IReadOnlyList<OrganizationRequest>, bool>? TransactionCommitsButAnswersUnknown;

        /// <summary>Task 089e (K2): a transaction this answers true for gives no answer and applies nothing (as <see cref="TransactionOutcomeUnknown"/>, for one transaction).</summary>
        public Func<IReadOnlyList<OrganizationRequest>, bool>? TransactionGivesNoAnswer;

        /// <summary>The transaction throws this exception as is, nothing applied (e.g. an HttpClient timeout's TaskCanceledException).</summary>
        public Exception? TransactionThrows;

        private (int Code, string Message)? ApplyInTransaction(OrganizationRequest request)
        {
            switch (request)
            {
                case Microsoft.Xrm.Sdk.Messages.CreateRequest create:
                    {
                        var target = create.Target;
                        if (target.Id != Guid.Empty && Rows.GetValueOrDefault(target.LogicalName)?.Any(r => r.Id == target.Id) == true)
                        {
                            return (unchecked((int)0x80040237), "Cannot insert duplicate key.");
                        }

                        // The (Procedure, version number) alternate key of sprk_procedureversion (task 080).
                        if (target.LogicalName == "sprk_procedureversion"
                            && Rows.GetValueOrDefault("sprk_procedureversion")?.Any(r =>
                                r.GetAttributeValue<EntityReference>("sprk_procedure")?.Id == target.GetAttributeValue<EntityReference>("sprk_procedure")?.Id
                                && r.GetAttributeValue<int?>("sprk_versionnumber") == target.GetAttributeValue<int?>("sprk_versionnumber")) == true)
                        {
                            return (unchecked((int)0x80060892), "A record with matching key values already exists.");
                        }

                        // Task 089c: sprk_policy's code key (sprk_policycodekey).
                        if (target.LogicalName == "sprk_policy" && target.GetAttributeValue<string>("sprk_policycode") is { } code
                            && Rows.GetValueOrDefault("sprk_policy")?.Any(r => r.GetAttributeValue<string>("sprk_policycode") == code) == true)
                        {
                            return (unchecked((int)0x80060892), "A record with matching key values already exists.");
                        }

                        var row = new Entity(target.LogicalName, target.Id == Guid.Empty ? Guid.NewGuid() : target.Id);
                        foreach (var a in target.Attributes) row[a.Key] = a.Value;
                        Writes.Add((Write.Create, row.LogicalName, row.Id, row.Attributes.ToDictionary(a => a.Key, a => a.Value)));
                        Order.Add("txn-create:" + row.LogicalName);
                        Seed(row);
                        _createdBy[row.Id] = Identity;
                        return null;
                    }

                case Microsoft.Xrm.Sdk.Messages.UpdateRequest update:
                    {
                        var target = update.Target;
                        if (Rows.GetValueOrDefault(target.LogicalName)?.Any(r => r.Id == target.Id) != true)
                        {
                            return (unchecked((int)0x80040217), $"{target.LogicalName} With Id = {target.Id} Does Not Exist");
                        }

                        if (update.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches
                            && RowVersions.GetValueOrDefault(target.Id).ToString(System.Globalization.CultureInfo.InvariantCulture) != target.RowVersion)
                        {
                            Refusals.Add((target.LogicalName, new[] { target.Id }));
                            return (unchecked((int)0x80060882), "The version of the existing record doesn't match the RowVersion property provided.");
                        }

                        var fields = target.Attributes.ToDictionary(a => a.Key, a => a.Value ?? DBNull.Value);
                        Writes.Add((Write.Update, target.LogicalName, target.Id, fields));
                        Order.Add("txn-update:" + target.LogicalName);
                        Apply(target.LogicalName, target.Id, fields);
                        return null;
                    }

                // Task 089c: a delete inside the transaction (rolled back with it); a row that is gone is Dataverse's "Does Not Exist".
                case Microsoft.Xrm.Sdk.Messages.DeleteRequest delete:
                    {
                        var target = delete.Target;
                        if (Rows.GetValueOrDefault(target.LogicalName)?.Any(r => r.Id == target.Id) != true)
                        {
                            return (unchecked((int)0x80040217), $"{target.LogicalName} With Id = {target.Id} Does Not Exist");
                        }

                        Writes.Add((Write.Delete, target.LogicalName, target.Id, new Dictionary<string, object>()));
                        Order.Add("txn-delete:" + target.LogicalName);
                        Rows[target.LogicalName].RemoveAll(r => r.Id == target.Id);
                        return null;
                    }

                default:
                    throw new NotSupportedException(request.GetType().Name);
            }
        }

        private sealed record Snapshot(
            Dictionary<string, List<(Entity Row, Dictionary<string, object> Attributes)>> Rows, Dictionary<Guid, long> RowVersions,
            int Writes, int Order, Dictionary<Guid, Guid> CreatedBy);

        private Snapshot TakeSnapshot() => new(
            Rows.ToDictionary(t => t.Key, t => t.Value.Select(r => (r, r.Attributes.ToDictionary(a => a.Key, a => a.Value))).ToList()),
            new Dictionary<Guid, long>(RowVersions), Writes.Count, Order.Count, new Dictionary<Guid, Guid>(_createdBy));

        private void RestoreSnapshot(Snapshot s)
        {
            foreach (var table in Rows.Keys.ToList())
            {
                if (!s.Rows.TryGetValue(table, out var rows))
                {
                    Rows.Remove(table);
                    continue;
                }

                Rows[table].Clear();
                foreach (var (row, attributes) in rows)
                {
                    row.Attributes.Clear();
                    foreach (var (key, value) in attributes) row[key] = value;
                    Rows[table].Add(row);
                }
            }

            RowVersions.Clear();
            foreach (var (id, version) in s.RowVersions) RowVersions[id] = version;
            Writes.RemoveRange(s.Writes, Writes.Count - s.Writes);
            Order.RemoveRange(s.Order, Order.Count - s.Order);
            _createdBy.Clear();
            foreach (var (id, user) in s.CreatedBy) _createdBy[id] = user;
        }

        public RuleSetService Service()
        {
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
            return new RuleSetService(
                Entities.Object, Ownership.Object, validator,
                new RuleBodyDescriber(validator, Entities.Object, NullLogger<RuleBodyDescriber>.Instance),
                Lease,
                new ListLogger(Logs));
        }

        public IEnumerable<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> WritesOf(params string[] entities) =>
            Writes.Where(w => entities.Contains(w.Entity));

        public void Seed(params Entity[] rows)
        {
            foreach (var row in rows)
            {
                if (!Rows.TryGetValue(row.LogicalName, out var list))
                {
                    Rows[row.LogicalName] = list = new List<Entity>();
                }

                list.Add(row);
                RowVersions[row.Id] = 1000 + ++_clock;
                _createdOn[row.Id] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_clock);
            }
        }

        /// <summary>Another admin's write to a row (a concurrent edit, decision or publish): applies it and advances its row version.</summary>
        public void ConcurrentWrite(string entity, Guid id, Dictionary<string, object> fields) => Apply(entity, id, fields);

        public Entity Row(string entity, Guid id) => Rows[entity].Single(r => r.Id == id);

        /// <summary>The version token (row version) of a rule's working (highest-numbered) version, as a page that has just read the rule set holds it.</summary>
        public long WorkingToken(Guid policyId) => RowVersions[
            Rows["sprk_policyversion"].Where(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == policyId)
                .OrderBy(v => v.GetAttributeValue<int>("sprk_versionnumber")).Last().Id];

        /// <summary>Records who created a seeded row (rows a service creates already carry <see cref="Identity"/>).</summary>
        public void SetCreatedBy(Guid rowId, Guid user) => _createdBy[rowId] = user;

        public Guid RuleSet(
            int status = RuleSetService.RuleSetDraft, bool visible = true, int? scope = (int)RuleSetScope.ClientDefault, Guid? firm = null,
            int? domain = RuleDomains.PolicyDomainBilling, DateTime? effectiveFrom = null, Guid? procedure = null)
        {
            var e = new Entity("sprk_ruleset", Guid.NewGuid());
            e["sprk_name"] = "Acme guidelines";
            if (procedure is { } p) e["sprk_procedure"] = new EntityReference("sprk_procedure", p);
            e["statuscode"] = new OptionSetValue(status);
            e["sprk_visibletofirms"] = visible;
            if (scope is { } s) e["sprk_scope"] = new OptionSetValue(s);
            if (firm is { } f) e["sprk_firm"] = new EntityReference("sprk_organization", f);
            if (domain is { } d) e["sprk_domain"] = new OptionSetValue(d);
            if (effectiveFrom is { } from) e["sprk_effectivefrom"] = from;
            Seed(e);
            return e.Id;
        }

        /// <summary>Task 116: a drafting target — the version, its Procedure, the Procedure's rule set (empty when none) and its source file(s).</summary>
        public sealed record Drafting(Guid Version, Guid Procedure, Guid RuleSet, IReadOnlyList<Guid> Files)
        {
            public Guid File => Files.Single();
        }

        /// <summary>
        /// Task 116: a version (Draft unless <paramref name="versionStatus"/>; null = no status) of a Billing Procedure (or <paramref name="procedure"/>,
        /// whose rule set is reused), the Procedure's rule set, and <paramref name="servableFiles"/> Servable Procedure-source Stored Files.
        /// <paramref name="drafted"/> is the version's Yes/No marker (null = never written); <paramref name="notConvertedList"/> its memo.
        /// </summary>
        public Drafting DraftVersion(
            int? versionStatus = ProcedureEntity.VersionStatusDraft, bool? drafted = null, int servableFiles = 1, int setStatus = RuleSetService.RuleSetDraft,
            int? area = RuleDomains.PolicyDomainBilling, int procedureStatus = ProcedureEntity.StatusActive, int number = 1, Guid? procedure = null,
            string? notConvertedList = null, bool withRuleSet = true)
        {
            Guid procedureId, ruleSetId = Guid.Empty;
            if (procedure is { } existing)
            {
                procedureId = existing;
                ruleSetId = Rows.GetValueOrDefault("sprk_ruleset")?
                    .FirstOrDefault(r => r.GetAttributeValue<EntityReference>("sprk_procedure")?.Id == existing)?.Id ?? Guid.Empty;
            }
            else
            {
                var p = new Entity(ProcedureEntity.Entity, Guid.NewGuid());
                p[ProcedureEntity.NameColumn] = "Acme outside counsel guidelines";
                if (area is { } a) p[ProcedureEntity.AreaColumn] = new OptionSetValue(a);
                p[ProcedureEntity.StatusColumn] = new OptionSetValue(procedureStatus);
                Seed(p);
                procedureId = p.Id;
                if (withRuleSet)
                {
                    ruleSetId = RuleSet(status: setStatus, procedure: procedureId);
                }
            }

            var v = new Entity(ProcedureEntity.VersionEntity, Guid.NewGuid());
            v[ProcedureEntity.VersionProcedureColumn] = new EntityReference(ProcedureEntity.Entity, procedureId);
            v[ProcedureEntity.VersionNumberColumn] = number;
            if (versionStatus is { } vs) v[ProcedureEntity.VersionStatusColumn] = new OptionSetValue(vs);
            if (drafted is { } d) v[RuleSetService.VersionDraftedColumn] = d;
            if (notConvertedList is not null) v[RuleSetService.VersionNotConvertedListColumn] = notConvertedList;
            Seed(v);

            var files = new List<Guid>();
            for (var i = 0; i < servableFiles; i++)
            {
                files.Add(SourceFile(v.Id, StoredFileStatus.Servable, i == 0 ? "guidelines.pdf" : $"guidelines-{i}.pdf"));
            }

            return new Drafting(v.Id, procedureId, ruleSetId, files);
        }

        /// <summary>Task 116: a Procedure-source Stored File row of <paramref name="versionId"/> in <paramref name="status"/>.</summary>
        public Guid SourceFile(Guid versionId, StoredFileStatus status, string name = "guidelines.pdf", StoredFilePurpose purpose = StoredFilePurpose.ProcedureSource)
        {
            var f = new Entity(StoredFileSchema.Entity, Guid.NewGuid());
            f[StoredFileSchema.Name] = name;
            f[StoredFileSchema.ProcedureVersionTable] = new EntityReference(ProcedureEntity.VersionEntity, versionId);
            f[StoredFileSchema.Purpose] = new OptionSetValue((int)purpose);
            f[StoredFileSchema.Status] = new OptionSetValue((int)status);
            Seed(f);
            return f.Id;
        }

        /// <summary>A policy (domain per <paramref name="domain"/>; null = empty = Ontology) and its first version.</summary>
        public (Guid Policy, Guid Version) Rule(
            Guid ruleSetId, string code = "BILL-A", ProposalState state = ProposalState.Proposed, bool current = false,
            string type = "DailyHoursCap", string? body = CapBody, int? consequence = (int)BillingConsequence.Flag,
            int? domain = RuleDomains.PolicyDomainBilling, DateTime? from = null, DateTime? to = null,
            string? quote = "No timekeeper may bill more than 10 hours a day.", string? location = "Section 4.2, p. 7")
        {
            var policy = new Entity("sprk_policy", Guid.NewGuid());
            policy["sprk_name"] = "Rule " + code;
            policy["sprk_policycode"] = code;
            policy["sprk_ruleset"] = new EntityReference("sprk_ruleset", ruleSetId);
            policy["sprk_enabled"] = false;
            if (domain is { } d) policy["sprk_domain"] = new OptionSetValue(d);
            Seed(policy);

            var version = AddVersion(policy.Id, 1, state, type, body, consequence, from, to, quote, location);
            if (current)
            {
                policy["sprk_currentversion"] = new EntityReference("sprk_policyversion", version);
                policy["sprk_enabled"] = true;
            }

            return (policy.Id, version);
        }

        public Guid AddVersion(
            Guid policyId, int number, ProposalState state, string type = "DailyHoursCap", string? body = CapBody,
            int? consequence = (int)BillingConsequence.Flag, DateTime? from = null, DateTime? to = null,
            string? quote = "No timekeeper may bill more than 10 hours a day.", string? location = "Section 4.2, p. 7")
        {
            var v = new Entity("sprk_policyversion", Guid.NewGuid());
            v["sprk_policy"] = new EntityReference("sprk_policy", policyId);
            v["sprk_versionnumber"] = number;
            v["sprk_ruletype"] = new OptionSetValue((int)Enum.Parse<RuleType>(type));
            if (body is not null) v["sprk_rulebody"] = body;
            v["sprk_messagetemplate"] = Template;
            v["sprk_shortheadline"] = "Daily hours cap";
            v["sprk_proposalstatus"] = new OptionSetValue((int)state);
            v["sprk_inforcefrom"] = from ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (consequence is { } c) v["sprk_consequence"] = new OptionSetValue(c);
            if (to is { } t) v["sprk_inforceto"] = t;
            if (quote is not null) v["sprk_clausereference"] = quote;
            if (location is not null) v["sprk_clauselocation"] = location;
            Seed(v);
            return v.Id;
        }

        /// <summary>A Procedure (task 083): its internal name and, when given, its firm-facing name.</summary>
        public Guid Procedure(string name, string? firmFacingName = null)
        {
            var e = new Entity("sprk_procedure", Guid.NewGuid());
            e["sprk_name"] = name;
            if (firmFacingName is not null) e["sprk_firmfacingname"] = firmFacingName;
            Seed(e);
            return e.Id;
        }

        /// <summary>
        /// An invoice of <paramref name="firm"/> (the test firm when null; none when <paramref name="noFirm"/>) with its OWN submission (the id the
        /// invoice's id derives from) storing <paramref name="findingsJson"/> (no findings column when null). Returns the invoice id.
        /// </summary>
        public Guid Invoice(string? findingsJson, Guid? firm = null, bool noFirm = false)
        {
            var submissionId = Guid.NewGuid();
            var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
            var invoice = new Entity("sprk_invoice", invoiceId);
            if (!noFirm) invoice["sprk_vendororg"] = new EntityReference("sprk_organization", firm ?? FirmId);
            Seed(invoice);
            AddSubmission(invoiceId, submissionId, findingsJson);
            return invoiceId;
        }

        /// <summary>A submission linked to <paramref name="invoiceId"/>; its id is the invoice's own only when it is the one the invoice id derives from.</summary>
        public void AddSubmission(Guid invoiceId, Guid submissionId, string? findingsJson)
        {
            var submission = new Entity("sprk_invoicesubmission", submissionId);
            submission["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId);
            if (findingsJson is not null) submission["sprk_findingsjson"] = findingsJson;
            Seed(submission);
        }

        private void Apply(string entity, Guid id, Dictionary<string, object> fields)
        {
            var row = Row(entity, id);
            foreach (var (key, value) in fields)
            {
                if (value is DBNull) row.Attributes.Remove(key);
                else row[key] = value;
            }

            RowVersions[id] = 1000 + ++_clock; // every write advances the row version, as Dataverse's versionnumber does
            if (_createdOn.TryGetValue(id, out var createdOn))
            {
                _modifiedOn[id] = createdOn.AddSeconds(_clock); // and moves modifiedon off createdon
            }
        }

        /// <summary>A row as a read returns it: its columns plus the system columns the fake keeps aside (versionnumber, createdon).</summary>
        private Entity Clone(Entity e)
        {
            var c = new Entity(e.LogicalName, e.Id);
            foreach (var a in e.Attributes) c[a.Key] = a.Value;
            if (RowVersions.TryGetValue(e.Id, out var version)) c["versionnumber"] = version;
            if (_createdOn.TryGetValue(e.Id, out var createdOn))
            {
                c["createdon"] = createdOn;
                c["modifiedon"] = _modifiedOn.GetValueOrDefault(e.Id, createdOn); // equal to createdon until the row is written again
            }

            if (_createdBy.TryGetValue(e.Id, out var createdBy)) c["createdby"] = new EntityReference("systemuser", createdBy);
            return c;
        }

        private static IComparable? OrderKey(Entity row, string attribute)
        {
            var value = attribute == row.LogicalName + "id" ? row.Id : row.Contains(attribute) ? row[attribute] : null;
            return value switch { OptionSetValue o => o.Value, EntityReference r => r.Id, IComparable c => c, _ => null };
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            var results = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
            return results.Count == 0 || (filter.FilterOperator == LogicalOperator.And ? results.All(x => x) : results.Any(x => x));
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            object? actual = c.AttributeName == row.LogicalName + "id"
                ? row.Id
                : row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == "statecode" ? new OptionSetValue(0) : null;
            actual = actual switch { EntityReference r => r.Id, OptionSetValue o => o.Value, _ => actual };
            var wanted = c.Values.Select(v => v is OptionSetValue o ? o.Value : v).ToList();
            return c.Operator switch
            {
                ConditionOperator.Equal => actual is not null && Equals(actual, wanted[0]),
                ConditionOperator.In => actual is not null && wanted.Any(w => Equals(actual, w)),
                ConditionOperator.Null => actual is null,
                // 082c C19: Dataverse LIKE (% any run, _ one character, case-insensitive), for the reverse chain read's pair candidates.
                ConditionOperator.Like => actual is string text && wanted[0] is string pattern && System.Text.RegularExpressions.Regex.IsMatch(text,
                    "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline),
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }
    }

    private static ProposalEdit Edit(
        string? type = null, string? body = null, string? template = null, string? headline = null, string? consequence = null,
        string? quote = null, string? location = null) =>
        new(type, body, template, headline, consequence, quote, location, CallerOid);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] DomainIsolated = { "sprk_policy", "sprk_ruleset" };

    // Task 083: the invoice's snapshot, built by the real builder (082d) so the reader the firm clause uses is checked against what is stored.
    private static BillingRuleApplication AppliedOn(Guid version) =>
        new(version, BillingRuleSource.Procedure, [BillingSubject.ForInvoice()], [], []);

    private static BillingRuleApplication DisplacedOn(Guid version) =>
        new(version, BillingRuleSource.Procedure, [], [BillingSubject.ForInvoice()], []);

    private static JsonObject SnapshotOf(params BillingRuleApplication[] applications) =>
        ProcedureSnapshotPatchKit.SnapshotOf(new BillingProcedureBasis([], [], applications));

    private static string FindingsWith(JsonNode? snapshot, bool member = true)
    {
        var root = new JsonObject { ["schema"] = 1, ["kind"] = "Accepted" };
        if (member) root[ProcedureSnapshot.Member] = snapshot;
        return root.ToJsonString();
    }

    /// <summary>An invoice of the test firm whose snapshot applied exactly <paramref name="versions"/>.</summary>
    private static Guid JudgedInvoice(Harness h, params Guid[] versions) =>
        h.Invoice(FindingsWith(SnapshotOf(versions.Select(AppliedOn).ToArray())));

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads and domain isolation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_ReturnsOnlyBillingRuleSets_WithTheirRuleCounts_NeverAnOntologyOrEmptyDomainRow()
    {
        var h = new Harness();
        var billing = h.RuleSet();
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var empty = h.RuleSet(domain: null);
        h.Rule(billing, "BILL-A");
        h.Rule(billing, "BILL-B");
        h.Rule(billing, "ONT-X", domain: RuleDomains.PolicyDomainOntology);

        var outcome = await h.Service().ListAsync(default);

        outcome.Value!.Select(s => s.Id).Should().Equal(billing);
        outcome.Value!.Single().RuleCount.Should().Be(2, "an Ontology policy pointing at a billing rule set is not a rule of it");
        outcome.Value!.Select(s => s.Id).Should().NotContain(new[] { ontology, empty });
    }

    [Fact]
    public async Task Get_ShowsEachRuleBesideItsClause_WithDescriptionStateAndConsequence()
    {
        var h = new Harness();
        var set = h.DraftVersion(drafted: true, notConvertedList: """[{"text":"Travel","reason":"unsupported"}]""").RuleSet;
        var (policy, version) = h.Rule(set, "BILL-A", ProposalState.Accepted);

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.NotConverted!.AsArray().Should().HaveCount(1);
        var p = outcome.Value.Proposals.Single();
        p.PolicyId.Should().Be(policy);
        p.VersionId.Should().Be(version);
        p.State.Should().Be("Accepted");
        p.RuleType.Should().Be("DailyHoursCap");
        p.Consequence.Should().Be("Flag");
        p.ClauseQuote.Should().Be("No timekeeper may bill more than 10 hours a day.");
        p.ClauseLocation.Should().Be("Section 4.2, p. 7");
        p.DescriptionText.Should().Contain("at most 10 hours per day");
        p.InForce.Should().BeFalse();
        p.ValidationErrors.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 116 (EB-D-211): the detail's not-converted list is the HIGHEST-numbered version's memo, not an earlier version's, and not a set column")]
    public async Task Get_NotConverted_IsTheHighestNumberedVersionsList()
    {
        var h = new Harness();
        var v1 = h.DraftVersion(versionStatus: ProcedureEntity.VersionStatusActive, drafted: true, number: 1,
            notConvertedList: """[{"text":"v1 clause","reason":"r"}]""");
        h.DraftVersion(procedure: v1.Procedure, number: 2, drafted: true, notConvertedList: """[{"text":"v2 a","reason":"r"},{"text":"v2 b","reason":"r"}]""");
        // Another Procedure's version 3 never answers for this set.
        h.DraftVersion(number: 3, drafted: true, notConvertedList: """[{"text":"other","reason":"r"}]""");

        var outcome = await h.Service().GetAsync(v1.RuleSet, default);

        outcome.Value!.NotConverted!.AsArray().Select(n => n!["text"]!.GetValue<string>()).Should().Equal("v2 a", "v2 b");

        // The highest version not drafted yet: no list, although version 1 has one.
        var h2 = new Harness();
        var w1 = h2.DraftVersion(versionStatus: ProcedureEntity.VersionStatusActive, drafted: true, notConvertedList: "[]");
        h2.DraftVersion(procedure: w1.Procedure, number: 2);
        (await h2.Service().GetAsync(w1.RuleSet, default)).Value!.NotConverted.Should().BeNull();
    }

    [Fact(DisplayName = "089e: RecordHomeOfRuleSetAsync names a record-scoped set's one record (the by-id routes' check), and the set still reads as no set here")]
    public async Task RecordHome_NamesTheRecordOfARecordScopedSet_NullOtherwise_AndTheReadsStillHideIt()
    {
        var h = new Harness();
        var matterId = Guid.NewGuid();
        var recordSet = h.RuleSet(RuleSetService.RuleSetPublished, scope: RecordRuleSet.ScopeRecord);
        h.Row("sprk_ruleset", recordSet)["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        var lookupOnly = h.RuleSet(RuleSetService.RuleSetPublished); // a core lookup without a record scope still counts (fail closed)
        h.Row("sprk_ruleset", lookupOnly)["sprk_project"] = new EntityReference("sprk_project", matterId);
        var noCore = h.RuleSet(RuleSetService.RuleSetPublished, scope: RecordRuleSet.ScopeRecord);
        var twoCores = h.RuleSet(RuleSetService.RuleSetPublished, scope: RecordRuleSet.ScopeRecord);
        h.Row("sprk_ruleset", twoCores)["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        h.Row("sprk_ruleset", twoCores)["sprk_workassignment"] = new EntityReference("sprk_workassignment", Guid.NewGuid());
        var legacy = h.RuleSet(RuleSetService.RuleSetPublished);
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology, scope: RecordRuleSet.ScopeRecord);

        var service = h.Service();
        (await service.RecordHomeOfRuleSetAsync(recordSet, default)).Should().Be(new RecordRuleSetHome("sprk_matter", matterId));
        (await service.RecordHomeOfRuleSetAsync(lookupOnly, default)).Should().Be(new RecordRuleSetHome("sprk_project", matterId));
        (await service.RecordHomeOfRuleSetAsync(noCore, default)).Should().Be(new RecordRuleSetHome(null, Guid.Empty), "no core lookup: the route denies");
        (await service.RecordHomeOfRuleSetAsync(twoCores, default)).Should().Be(new RecordRuleSetHome(null, Guid.Empty), "two core lookups: the route denies");
        (await service.RecordHomeOfRuleSetAsync(legacy, default)).Should().BeNull();
        (await service.RecordHomeOfRuleSetAsync(ontology, default)).Should().BeNull("not a billing set");
        (await service.RecordHomeOfRuleSetAsync(Guid.NewGuid(), default)).Should().BeNull();
        (await service.RecordHomeOfRuleSetAsync(Guid.Empty, default)).Should().BeNull();

        foreach (var id in new[] { recordSet, lookupOnly, noCore, twoCores })
        {
            var outcome = await service.GetAsync(id, default);
            outcome.StatusCode.Should().Be(404, "089c: every read here still answers a record-scoped set as no set");
            (await service.ProcedureOfRuleSetAsync(id, default)).Should().BeNull();
        }

        (await service.GetAsync(Guid.NewGuid(), default)).Should().BeEquivalentTo(RuleSetService.RuleSetNotFound<RuleSetDetail>(),
            "the routes' uniform 404 is the service's own not found");
    }

    [Fact]
    public async Task Get_ARuleSetOfAnotherDomain_IsNotFound()
    {
        var h = new Harness();
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var empty = h.RuleSet(domain: null);
        var docketing = h.RuleSet(domain: 100000002);

        foreach (var id in new[] { ontology, empty, docketing })
        {
            var outcome = await h.Service().GetAsync(id, default);
            outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
            outcome.StatusCode.Should().Be(404);
        }
    }

    [Fact]
    public async Task Get_ListsOnlyBillingPolicies_AnOntologyPolicyLinkedToTheSetIsNeverShown()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-A");
        h.Rule(set, "ONT-NULL", domain: null);
        h.Rule(set, "ONT-1", domain: RuleDomains.PolicyDomainOntology);
        h.Rule(set, "DOCK", domain: 100000002);

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Value!.Proposals.Select(p => p.PolicyCode).Should().Equal("BILL-A");
    }

    [Fact]
    public async Task EveryPolicyAndRuleSetQuery_CarriesATopLevelBillingDomainCondition()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: false);
        var service = h.Service();

        await service.ListAsync(default);
        await service.GetAsync(set, default);
        await service.EditAsync(set, policy, Edit(headline: "x"), default);
        await service.DecideAsync(set, policy, true, h.WorkingToken(policy), default);
        await service.PublishAsync(set, Effective, default);
        await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);
        await service.SetVisibilityAsync(set, false, default);
        await service.GetFirmClauseAsync(version, JudgedInvoice(h, version), default);

        var reads = h.Queries.Where(q => DomainIsolated.Contains(q.EntityName)).ToList();
        reads.Should().NotBeEmpty();
        reads.Should().OnlyContain(q => q.Criteria.Conditions.Any(c =>
            c.AttributeName == "sprk_domain" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(RuleDomains.PolicyDomainBilling)));
    }

    [Fact]
    public async Task EvenWhenAQueryLosesItsDomainFilter_AnOntologyPolicyIsNeverActedOn_ByAnyOperation()
    {
        var h = new Harness { IgnoreQueryFilters = true };
        var set = h.RuleSet();
        var (nullDomain, v1) = h.Rule(set, "ONT-NULL", ProposalState.Accepted, domain: null);
        var (ontology, v2) = h.Rule(set, "ONT-1", ProposalState.Accepted, domain: RuleDomains.PolicyDomainOntology);
        var service = h.Service();

        foreach (var policy in new[] { nullDomain, ontology })
        {
            (await service.EditAsync(set, policy, Edit(headline: "x"), default)).StatusCode.Should().Be(404);
            (await service.DecideAsync(set, policy, true, h.WorkingToken(policy), default)).StatusCode.Should().Be(404);
            (await service.DecideAsync(set, policy, false, h.WorkingToken(policy), default, rejectionReason: Reason)).StatusCode.Should().Be(404);
        }

        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.nothing_to_publish");
        (await service.GetAsync(set, default)).Value!.Proposals.Should().BeEmpty();
        var invoice = JudgedInvoice(h, v1, v2);
        (await service.GetFirmClauseAsync(v1, invoice, default)).Should().Be(FirmClauseText.None);
        (await service.GetFirmClauseAsync(v2, invoice, default)).Should().Be(FirmClauseText.None);
        h.Writes.Should().BeEmpty("nothing about an Ontology policy is written");
    }

    [Fact]
    public async Task BillingOperations_NeverWriteTheDomainColumn_AndTheirRowsAreInvisibleToTheOntologyReaders()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var draft = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        await service.EditAsync(set, policy, Edit(headline: "Edited"), default); // edits the draft in place
        await service.PublishAsync(set, Effective, default);
        await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);

        h.Writes.Should().NotBeEmpty();
        h.Writes.Should().OnlyContain(w => !w.Fields.ContainsKey("sprk_domain"));

        // The other direction: the Ontology allow-list refuses every row the billing path reads or writes.
        h.Row("sprk_policy", policy).Should().Match<Entity>(p => !RuleDomains.IsOntologyPolicy(p));
        var ontologyQuery = new QueryExpression("sprk_policy");
        RuleDomains.AddOntologyPolicyAllowlist(ontologyQuery);
        ontologyQuery.Criteria.Filters.Single().Conditions.Select(c => c.AttributeName).Should().OnlyContain(a => a == "sprk_domain");
        ontologyQuery.Criteria.Filters.Single().Conditions.Should().NotContain(c => c.Values.Contains(RuleDomains.PolicyDomainBilling));
        draft.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edit
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edit_OfADraft_UpdatesItInPlace_AndCreatesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        // Task 073 (FR-26): a DailyHoursCap rule may only Flag, so the consequence edit goes from a stored Reject (a row that predates the
        // check, or was written around it) to Flag.
        var (policy, version) = h.Rule(set, consequence: (int)BillingConsequence.Reject);

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8, consequence: "flag", location: "Section 9"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CreatedNewVersion.Should().BeFalse();
        outcome.Value.Proposal.VersionId.Should().Be(version);
        outcome.Value.Proposal.Consequence.Should().Be("Flag");
        h.Writes.Should().ContainSingle().Which.Should().Match<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)>(
            w => w.Kind == Write.BulkUpdate && w.Entity == "sprk_policyversion" && w.Id == version);
        h.ConditionalCalls.Should().ContainSingle("the in-place edit is conditional on the row it read (review round 2)");
        var row = h.Row("sprk_policyversion", version);
        row.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8);
        row.GetAttributeValue<string>("sprk_clauselocation").Should().Be("Section 9");
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("billing-flag");
    }

    [Theory]
    [InlineData(ProposalState.Accepted, "body")]
    [InlineData(ProposalState.Accepted, "template")]
    [InlineData(ProposalState.Accepted, "consequence")]
    [InlineData(ProposalState.Accepted, "type")]
    [InlineData(ProposalState.Rejected, "body")]
    [InlineData(ProposalState.Rejected, "template")]
    [InlineData(ProposalState.Rejected, "consequence")]
    [InlineData(ProposalState.Rejected, "type")]
    [InlineData(ProposalState.Accepted, "clauseReference")] // D-109: the clause text and its location are content too
    [InlineData(ProposalState.Accepted, "clauseLocation")]
    [InlineData(ProposalState.Rejected, "clauseReference")]
    [InlineData(ProposalState.Rejected, "clauseLocation")]
    [InlineData(ProposalState.Proposed, "body")] // review pass 2: written even when the read saw Proposed (an Accept may land in between)
    [InlineData(ProposalState.Proposed, "clauseReference")]
    public async Task Edit_OfADraft_ThatChangesItsContent_WritesProposed_WhateverStateItWasReadIn(ProposalState decided, string member)
    {
        var h = new Harness();
        var set = h.RuleSet();
        // Task 073 (FR-26): a DailyHoursCap rule may only Flag, so the "consequence" edit goes from a stored Reject to Flag.
        var (policy, version) = h.Rule(set, state: decided, consequence: member == "consequence" ? (int)BillingConsequence.Reject : (int)BillingConsequence.Flag);
        var edit = member switch
        {
            "body" => Edit(body: CapBody8),
            "template" => Edit(template: "Over {{rule.maxHoursPerDay}} hours."),
            "consequence" => Edit(consequence: "Flag"),
            "clauseReference" => Edit(quote: "A different quote."),
            "clauseLocation" => Edit(location: "Section 9"),
            _ => Edit(type: "TimeIncrement", body: IncrementBody, template: "Hours must be a multiple of {{rule.incrementHours}}."),
        };

        var outcome = await h.Service().EditAsync(set, policy, edit, default);

        outcome.Value!.CreatedNewVersion.Should().BeFalse();
        outcome.Value.Proposal.State.Should().Be("Proposed");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed,
            "an edit nobody has re-accepted is not the version that was accepted");
        h.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("sprk_proposalstatus",
            "the content write carries the status itself, so a decision that read the old content cannot outlive it");
    }

    [Theory]
    [InlineData("headline")]
    [InlineData("sameBody")]
    [InlineData("sameConsequence")]
    [InlineData("sameClause")]
    public async Task Edit_OfAnAcceptedDraft_ThatChangesNothingTheRuleDoes_KeepsItAccepted(string member)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted);
        var edit = member switch
        {
            "headline" => Edit(headline: "A new headline"),
            "sameBody" => Edit(body: CapBody, headline: "A new headline"),
            "sameClause" => Edit(quote: "No timekeeper may bill more than 10 hours a day.", location: "Section 4.2, p. 7", headline: "A new headline"),
            _ => Edit(consequence: "Flag", headline: "A new headline"),
        };

        var outcome = await h.Service().EditAsync(set, policy, edit, default);

        outcome.Value!.Proposal.State.Should().Be("Accepted");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);
        h.Writes.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_proposalstatus");
    }

    [Fact]
    public async Task EditThenPublish_WithoutAcceptingAgain_DoesNotPutTheEditInForce()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();

        await service.EditAsync(set, policy, Edit(body: CapBody), default); // 8 -> 10 hours: a different rule from the one that was accepted
        var refused = await service.PublishAsync(set, Effective, default);

        refused.ErrorCode.Should().Be("ruleset.nothing_to_publish");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        h.WritesOf("sprk_policy", "sprk_ruleset").Should().BeEmpty("nothing was published");
        h.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_inforcefrom") || w.Fields.ContainsKey("sprk_inforceto"), "nothing was published");

        await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);
        var published = await service.PublishAsync(set, Effective, default);

        published.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(v2);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
    }

    [Fact]
    public async Task Edit_OfAVersionInForce_CreatesANewDraftVersion_AndLeavesThePublishedRowUnchanged()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var before = h.Row("sprk_policyversion", v1).Attributes.ToDictionary(a => a.Key, a => a.Value);

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        outcome.Value.Proposal.VersionNumber.Should().Be(2);
        outcome.Value.Proposal.State.Should().Be("Proposed");
        outcome.Value.Proposal.InForce.Should().BeFalse();
        outcome.Value.Proposal.HasPublishedVersion.Should().BeTrue();
        h.Writes.Should().ContainSingle(w => w.Kind == Write.Create && w.Entity == "sprk_policyversion");
        h.Writes.Should().NotContain(w => w.Kind != Write.Create, "no update touches the published version, the policy pointer or the rule set");
        h.Row("sprk_policyversion", v1).Attributes.Should().BeEquivalentTo(before);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        var created = h.Rows["sprk_policyversion"].Single(v => v.Id != v1);
        created.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8);
        created.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
        created.GetAttributeValue<EntityReference>("sprk_policy").Id.Should().Be(policy);
        created.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
    }

    [Fact]
    public async Task Edit_OfAVersionThatWasInForceAndEnded_AlsoCreatesANewVersion()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, to: Utc(2026, 6, 1)); // ended, no longer current

        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "Changed"), default);

        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        h.Writes.Should().NotContain(w => w.Id == v1);
    }

    [Fact]
    public async Task Edit_TwoConcurrentEditsOfAVersionInForce_OneCreatesTheNextVersion_TheOtherIs409_NothingIsLostSilently()
    {
        // Review pass 2 item 1 (F2): admins A and B both read v1 (in force) and both create "v2". The new version's id is derived from
        // (policy, number), so the second create collides on the primary key and is answered 409, not 200 with an edit nobody sees.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var service = h.Service();
        Task<RuleSetOutcome<ProposalEdited>>? edited = null;
        h.BeforeCreate = async entity =>
        {
            if (entity.LogicalName != "sprk_policyversion") return;
            h.BeforeCreate = null;
            edited = service.EditAsync(set, policy, Edit(body: CapBody8), default); // B: reads v1, creates v2 before A's create lands
            await edited;
        };

        var a = await service.EditAsync(set, policy, Edit(template: "Over {{rule.maxHoursPerDay}} hours a day."), default);
        var b = await edited!;

        b.Kind.Should().Be(RuleSetOutcomeKind.Done);
        b.Value!.CreatedNewVersion.Should().BeTrue();
        a.Kind.Should().Be(RuleSetOutcomeKind.Refused, "A's edit was not saved and must not be reported as saved");
        a.StatusCode.Should().Be(409);
        a.ErrorCode.Should().Be("ruleset.concurrent_edit");
        var versions = h.Rows["sprk_policyversion"].Where(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == policy).ToList();
        versions.Should().HaveCount(2, "exactly one version 2 exists");
        var v2 = versions.Single(v => v.Id != v1);
        v2.Id.Should().Be(RuleSetService.VersionIdFor(policy, 2));
        v2.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8, "B's edit is the one that was saved");
        v2.GetAttributeValue<string>("sprk_messagetemplate").Should().Be(Template, "A's edit is not merged in silently either");
    }

    [Fact]
    public async Task Edit_WhenTheNewVersionCannotBeCreated_AndNoOtherEditCreatedIt_Is500()
    {
        var h = new Harness { FailCreateWhen = e => e.LogicalName == "sprk_policyversion" };
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8), default);

        outcome.StatusCode.Should().Be(500, "a create that failed for another reason is not a conflict");
        outcome.ErrorCode.Should().Be("ruleset.write_failed");
    }

    [Fact]
    public void VersionIdFor_IsAVersion5IdOfThePolicyAndTheNumber()
    {
        var policy = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var id = RuleSetService.VersionIdFor(policy, 2);

        RuleSetService.VersionIdFor(policy, 2).Should().Be(id, "the same policy and number always give the same id");
        RuleSetService.VersionIdFor(policy, 3).Should().NotBe(id);
        RuleSetService.VersionIdFor(Guid.NewGuid(), 2).Should().NotBe(id);
        id.ToString("D")[14].Should().Be('5', "an RFC 4122 version 5 (name-based, SHA-1) id");
        "89ab".Should().Contain(id.ToString("D")[19].ToString(), "the RFC 4122 variant");
    }

    [Fact]
    public async Task TheVersionsOfARuleAreReadInAStableOrder_SoTheWorkingVersionIsTheSameOnEveryRead()
    {
        // Review pass 2 item 1: two rows with one number (created before edits had derived ids) must not swap as the working version.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var first = h.AddVersion(policy, 2, ProposalState.Proposed, body: CapBody8);
        var second = h.AddVersion(policy, 2, ProposalState.Proposed, body: IncrementBody, type: "TimeIncrement");
        var rows = h.Rows["sprk_policyversion"];
        var firstRow = rows.Single(r => r.Id == first);
        rows.Remove(firstRow);
        rows.Add(firstRow); // stored after the later one: a read in storage order would see the later one last

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Value!.Proposals.Single().VersionId.Should().Be(first, "the earliest created of the highest number is the working version");
        h.Queries.Where(q => q.EntityName == "sprk_policyversion").Should().OnlyContain(q =>
            q.Orders.Select(o => o.AttributeName + " " + o.OrderType).SequenceEqual(new[]
            {
                "sprk_versionnumber Ascending", "createdon Ascending", "sprk_policyversionid Ascending",
            }));
        second.Should().NotBe(first);
    }

    [Fact]
    public async Task Edit_WithAnInvalidResult_WritesNothing_AndNamesTheField()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set);

        var badBody = await h.Service().EditAsync(set, policy, Edit(body: """{"type":"DailyHoursCap","maxHoursPerDay":99}"""), default);
        var badTemplate = await h.Service().EditAsync(set, policy, Edit(template: "{{line.nope}}"), default);
        var badConsequence = await h.Service().EditAsync(set, policy, Edit(consequence: "Explode"), default);
        var nothing = await h.Service().EditAsync(set, policy, Edit(), default);

        badBody.Errors.Should().Contain(e => e.Field == "ruleBody");
        badTemplate.Errors.Should().Contain(e => e.Field == "messageTemplate");
        badConsequence.Errors.Should().Contain(e => e.Field == "consequence");
        nothing.Errors.Should().Contain(e => e.Field == "edit");
        new[] { badBody, badTemplate, badConsequence, nothing }.Should().OnlyContain(o => o.Kind == RuleSetOutcomeKind.Invalid);
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Existence")]
    [InlineData("Threshold")]
    [InlineData("100000002")]
    public async Task Edit_ToAnOntologyRuleType_IsRefusedAsWrongDomain_AndWritesNothing(string ruleType)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set);

        var outcome = await h.Service().EditAsync(set, policy, Edit(type: ruleType), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "ruleType" && e.Reason == "rule_type_wrong_domain");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_ToAnotherBillingType_NeedsAMatchingBody()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);

        var mismatch = await h.Service().EditAsync(set, policy, Edit(type: "TimeIncrement"), default);
        var ok = await h.Service().EditAsync(set, policy, Edit(type: "TimeIncrement", body: IncrementBody, template: "Hours must be a multiple of {{rule.incrementHours}}."), default);

        mismatch.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        ok.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_ruletype").Value.Should().Be((int)RuleType.TimeIncrement);
    }

    [Fact]
    public async Task Edit_Decide_Publish_OfAMissingOrForeignRule_Is404_WithZeroWrites()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var other = h.RuleSet();
        var (foreign, _) = h.Rule(other, "BILL-OTHER", ProposalState.Accepted);
        var service = h.Service();

        var missing = await service.EditAsync(set, Guid.NewGuid(), Edit(headline: "x"), default);
        var inAnotherSet = await service.EditAsync(set, foreign, Edit(headline: "x"), default);
        var unknownSet = await service.DecideAsync(Guid.NewGuid(), foreign, true, h.WorkingToken(foreign), default);

        missing.ErrorCode.Should().Be("ruleset.proposal_not_found");
        inAnotherSet.ErrorCode.Should().Be("ruleset.proposal_not_found");
        unknownSet.ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_OnARetiredRuleSet_Is409()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetRetired);
        var (policy, _) = h.Rule(set);

        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "x"), default);

        outcome.ErrorCode.Should().Be("ruleset.retired");
        outcome.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Accept / reject
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Accept_And_Reject_SetTheProposalState_OnTheDraftVersion_AndNothingElse()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();

        var accepted = await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);
        var rejected = await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason);

        accepted.Value!.State.Should().Be("Accepted");
        rejected.Value!.State.Should().Be("Rejected");
        h.Writes.Should().OnlyContain(w => w.Kind == Write.BulkUpdate && w.Entity == "sprk_policyversion" && w.Id == version);
        h.Writes.Should().OnlyContain(w => w.Fields.Keys.All(k => k == "sprk_proposalstatus" || k == "sprk_rejectionreason"),
            "a decision writes the status and, for a reject, its reason");
        h.Writes.First().Fields.Keys.Should().Equal(new[] { "sprk_proposalstatus" }, "an accept of a draft that was never rejected writes the status alone");
        h.ConditionalCalls.Should().HaveCount(2, "each decision is written conditional on the row version it read");
        h.Row("sprk_policy", policy).Contains("sprk_currentversion").Should().BeFalse("deciding never puts anything in force");
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    // The rejection reason (task 077 follow-up, D-113): required on a reject, stored with the status, admin-only
    // ───────────────────────────────────────────────────────────────────────────────────────────

    private const string Reason = "The guideline allows 12 hours a day, not 10.";

    private static string ReasonOf(Harness h, Guid version) =>
        h.Row("sprk_policyversion", version).GetAttributeValue<string>("sprk_rejectionreason");

    [Fact]
    public async Task Reject_WithAReason_StoresItInTheSameConditionalWriteAsTheStatus_AndTheViewCarriesIt()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);

        var outcome = await service.DecideAsync(set, policy, accept: false, shown, default, rejectionReason: "  " + Reason + "\n ");

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.State.Should().Be("Rejected");
        outcome.Value.RejectionReason.Should().Be(Reason, "the reason is trimmed");
        var write = h.Writes.Single();
        write.Id.Should().Be(version);
        write.Fields["sprk_proposalstatus"].Should().BeEquivalentTo(new OptionSetValue((int)ProposalState.Rejected));
        write.Fields["sprk_rejectionreason"].Should().Be(Reason, "the reason and the status are one write");
        h.ConditionalCalls.Single().Rows.Single().RowVersion.Should().Be(shown, "and that write is conditional on the version the admin saw");
        ReasonOf(h, version).Should().Be(Reason);

        var read = await service.GetAsync(set, default);
        read.Value!.Proposals.Single().RejectionReason.Should().Be(Reason, "the Rule Review read shows it");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n ")]
    public async Task Reject_WithNoReason_Is400OnTheReasonField_AndNothingIsReadOrWritten(string? reason)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);
        var queriesBefore = h.Queries.Count;

        var outcome = await service.DecideAsync(set, policy, accept: false, shown, default, rejectionReason: reason);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("ruleset.invalid");
        outcome.Errors.Should().ContainSingle().Which.Field.Should().Be("reason");
        h.Writes.Should().BeEmpty("nothing is written");
        h.Queries.Should().HaveCount(queriesBefore, "the request is refused before the rule is read");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
        h.Row("sprk_policyversion", version).Contains("sprk_rejectionreason").Should().BeFalse();
    }

    [Fact]
    public async Task Reject_ReasonOfExactly2000Characters_IsStored_AndOneMoreIs400()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var longest = new string('r', RuleSetService.MaxRejectionReasonChars);

        var tooLong = await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: longest + "x");
        h.Writes.Should().BeEmpty();
        var exact = await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: " " + longest + " ");

        tooLong.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        exact.Kind.Should().Be(RuleSetOutcomeKind.Done);
        ReasonOf(h, version).Should().Be(longest, "the limit is applied after trimming");
    }

    [Fact]
    public async Task Reject_WithAStaleRowVersion_StillIs409_AndStoresNoReason()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);
        await service.EditAsync(set, policy, Edit(body: CapBody8), default); // another admin changes the rule
        var writesAfterTheEdit = h.Writes.Count;

        var outcome = await service.DecideAsync(set, policy, accept: false, shown, default, rejectionReason: Reason);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Writes.Should().HaveCount(writesAfterTheEdit, "the refused reject wrote nothing");
        h.Row("sprk_policyversion", version).Contains("sprk_rejectionreason").Should().BeFalse("no reason is stored for a decision that was refused");
    }

    [Fact]
    public async Task Reject_ARejectedDraftAgain_ReplacesTheReason()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        (await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var again = await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: "A better reason.");

        again.Value!.RejectionReason.Should().Be("A better reason.");
        ReasonOf(h, version).Should().Be("A better reason.");
    }

    [Fact]
    public async Task Accept_AfterAReject_ClearsTheReason_InTheSameWriteAsTheStatus_AndTheViewCarriesNone()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        (await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Writes.Clear();

        var accepted = await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);

        accepted.Kind.Should().Be(RuleSetOutcomeKind.Done);
        accepted.Value!.State.Should().Be("Accepted");
        accepted.Value.RejectionReason.Should().BeNull();
        var write = h.Writes.Single();
        write.Fields["sprk_proposalstatus"].Should().BeEquivalentTo(new OptionSetValue((int)ProposalState.Accepted));
        write.Fields["sprk_rejectionreason"].Should().Be(DBNull.Value, "the old reason is cleared in the accepting write");
        h.Row("sprk_policyversion", version).Contains("sprk_rejectionreason").Should().BeFalse();
        (await service.GetAsync(set, default)).Value!.Proposals.Single().RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task Accept_IgnoresAReasonInTheRequest_AndStoresNone()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);

        var accepted = await h.Service().DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default, rejectionReason: Reason);

        accepted.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policyversion", version).Contains("sprk_rejectionreason").Should().BeFalse("only a reject stores a reason");
    }

    [Theory]
    [InlineData("body")]
    [InlineData("quote")]
    public async Task Edit_ThatReturnsARejectedDraftToProposed_ClearsTheReason(string what)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        (await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Writes.Clear();

        var edited = await service.EditAsync(set, policy, what == "body" ? Edit(body: CapBody8) : Edit(quote: "A different clause."), default);

        edited.Kind.Should().Be(RuleSetOutcomeKind.Done);
        edited.Value!.Proposal.State.Should().Be("Proposed");
        edited.Value.Proposal.RejectionReason.Should().BeNull();
        var write = h.Writes.Single();
        write.Fields["sprk_proposalstatus"].Should().BeEquivalentTo(new OptionSetValue((int)ProposalState.Proposed));
        write.Fields["sprk_rejectionreason"].Should().Be(DBNull.Value, "the decision is void and its reason goes with it, in the same write");
        h.Row("sprk_policyversion", version).Contains("sprk_rejectionreason").Should().BeFalse();
        (await service.GetAsync(set, default)).Value!.Proposals.Single().RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task Edit_OfARejectedDraft_ThatChangesOnlyTheHeadline_KeepsTheDecisionAndItsReason()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        (await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var edited = await service.EditAsync(set, policy, Edit(headline: "A different headline"), default);

        edited.Value!.Proposal.State.Should().Be("Rejected");
        edited.Value.Proposal.RejectionReason.Should().Be(Reason);
        ReasonOf(h, version).Should().Be(Reason);
    }

    [Fact]
    public async Task Read_ShowsAReasonOnlyForARejectedDraft_EvenIfAStaleOneIsStoredOnAnotherState()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted);
        h.Row("sprk_policyversion", version)["sprk_rejectionreason"] = Reason; // data written outside this service

        var read = await h.Service().GetAsync(set, default);

        read.Value!.Proposals.Single().RejectionReason.Should().BeNull("a reason belongs to a Rejected draft only");
    }

    // The reason is for the admins who review rules. A firm is shown a rule's clause (FR-28, D-66) and nothing about how the rule was
    // reviewed: not that it was rejected, and not why.
    [Fact]
    public async Task TheFirmProjection_NeverCarriesTheRejectionReason()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        h.Row("sprk_policyversion", version)["sprk_rejectionreason"] = "INTERNAL: " + Reason; // even a stray value on a version in force

        var shown = await h.Service().GetFirmClauseAsync(version, JudgedInvoice(h, version), default);

        shown.Should().Be(new FirmClauseText("No timekeeper may bill more than 10 hours a day.", "Section 4.2, p. 7"));
        System.Text.Json.JsonSerializer.Serialize(shown).Should().NotContain("INTERNAL").And.NotContain(Reason).And.NotContainEquivalentOf("reason");
        typeof(FirmClauseText).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Should().BeEquivalentTo(
            new[] { "Quote", "SectionReference", "FirmFacingName" }, "the firm projection has exactly the clause, its location and its label");
        h.Queries.Where(q => q.EntityName == "sprk_policyversion").Should().OnlyContain(
            q => !q.ColumnSet.AllColumns && !q.ColumnSet.Columns.Contains(RuleSetService.RejectionReasonColumn),
            "the firm read never selects the reason column, nor all columns");
        policy.Should().NotBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    // The decision names the version the admin was shown (task 077, 076 re-check K3)
    // ───────────────────────────────────────────────────────────────────────────────────────────

    private static long TokenShown(RuleSetOutcome<RuleSetDetail> read, Guid policy) =>
        long.Parse(read.Value!.Proposals.Single(p => p.PolicyId == policy).RowVersion!);

    [Fact]
    public async Task Read_CarriesTheVersionTokenOfEachRule_AndAWriteResponseCarriesNone()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();

        var storedBeforeTheRead = h.RowVersions[version].ToString(System.Globalization.CultureInfo.InvariantCulture);
        var read = await service.GetAsync(set, default);
        var edited = await service.EditAsync(set, policy, Edit(body: CapBody8), default);
        var decided = await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);

        read.Value!.Proposals.Single().RowVersion.Should().Be(storedBeforeTheRead, "the token is the row's version number as it was read");
        edited.Value!.Proposal.RowVersion.Should().BeNull("the token after a write is not read back; a stale one would let the next decision pass");
        decided.Value!.RowVersion.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decide_AdminALoads_AdminBEdits_AdminADecides_Is409_AndNothingIsDecided(bool accept)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shownToA = TokenShown(await service.GetAsync(set, default), policy);

        var bEdit = await service.EditAsync(set, policy, Edit(body: CapBody8), default); // admin B changes what the rule does
        bEdit.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var writesAfterB = h.Writes.Count;

        var outcome = await service.DecideAsync(set, policy, accept, shownToA, default, rejectionReason: accept ? null : Reason);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Writes.Should().HaveCount(writesAfterB, "the refused decision wrote nothing");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value
            .Should().Be((int)ProposalState.Proposed, "nothing was accepted or rejected");
        h.Row("sprk_policyversion", version).GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8, "B's edit stands");
    }

    [Theory]
    [InlineData(true, ProposalState.Accepted)]
    [InlineData(false, ProposalState.Rejected)]
    public async Task Decide_OnAnUnchangedDraft_WithTheTokenTheAdminWasShown_IsDone(bool accept, ProposalState expected)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);

        var outcome = await service.DecideAsync(set, policy, accept, shown, default, rejectionReason: accept ? null : Reason);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.State.Should().Be(expected.ToString());
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)expected);
        h.ConditionalCalls.Single().Rows.Single().RowVersion.Should().Be(shown, "the write is conditional on the version the admin saw");
    }

    [Fact]
    public async Task Decide_AfterAnyChangeToTheRow_EvenAHeadlineOnlyEdit_IsRefused409_BecauseTheAdminSawTheOldHeadline()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);
        await service.EditAsync(set, policy, Edit(headline: "A different headline"), default); // keeps the decision, but changes what was shown

        var outcome = await service.DecideAsync(set, policy, accept: true, shown, default);

        outcome.StatusCode.Should().Be(409);
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
    }

    [Fact]
    public async Task Decide_AfterAnotherAdminDecided_IsRefused409_SoTheSecondDecisionNeverOverwritesTheFirst()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shownToBoth = TokenShown(await service.GetAsync(set, default), policy);
        (await service.DecideAsync(set, policy, accept: false, shownToBoth, default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var second = await service.DecideAsync(set, policy, accept: true, shownToBoth, default);

        second.StatusCode.Should().Be(409);
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Rejected);
    }

    [Fact]
    public async Task Decide_WithAStaleToken_IsRefusedBeforeValidation_SoTheAdminIsNotToldAboutContentTheyNeverSaw()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();
        var shown = TokenShown(await service.GetAsync(set, default), policy);
        h.ConcurrentWrite("sprk_policyversion", version,
            new Dictionary<string, object> { ["sprk_rulebody"] = """{"type":"DailyHoursCap","maxHoursPerDay":0}""" }); // B saved an invalid body

        var outcome = await service.DecideAsync(set, policy, accept: true, shown, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused, "a 409, not the 400 validation errors of content the admin was never shown");
        outcome.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Decide_WithATokenOfAnotherRow_Or_ATokenThatMatchesNothing_IsRefused409()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var (other, _) = h.Rule(set, "BILL-B");
        var service = h.Service();

        var otherRows = await service.DecideAsync(set, policy, accept: true, h.WorkingToken(other), default);
        var invented = await service.DecideAsync(set, policy, accept: true, 1L, default);

        otherRows.StatusCode.Should().Be(409);
        invented.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty();
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
    }

    [Fact]
    public async Task Accept_ThatReadTheOldContent_IsRefused409_WhenAnEditLandedFirst()
    {
        // Review pass 2 item 3, one order: the Accept reads and validates v1; an edit then changes its content (and writes Proposed);
        // the Accept's write must not put "Accepted" on content nobody reviewed.
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        h.AfterQuery = q =>
        {
            if (q.EntityName != "sprk_policyversion") return;
            h.AfterQuery = null;
            h.ConcurrentWrite("sprk_policyversion", version, new Dictionary<string, object>
            {
                ["sprk_rulebody"] = CapBody8,
                ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed),
            });
        };

        var outcome = await h.Service().DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed,
            "the edited content was never reviewed");
    }

    [Fact]
    public async Task Edit_ThatReadProposed_IsRefused409_WhenAnAcceptLandedBeforeItsWrite_SoTheEditIsNeverAccepted()
    {
        // Review pass 2 item 3, the other order: the edit reads v1 Proposed; an Accept of the old content lands; then the edit writes.
        // The edit's write is conditional on the row it read, so it is refused: the Accepted content is the content that was reviewed.
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        h.AfterQuery = q =>
        {
            if (q.EntityName != "sprk_policyversion") return;
            h.AfterQuery = null;
            h.ConcurrentWrite("sprk_policyversion", version, new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted) });
        };

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8), default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        var row = h.Row("sprk_policyversion", version);
        row.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody, "the edit was not applied");
        row.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted,
            "what is Accepted is exactly what the reviewer saw");
    }

    [Fact]
    public async Task Edit_OfADraftAPublishHasStartedToPutInForce_IsRefused409_SoUnreviewedContentIsNeverPublished()
    {
        // Review round 2, Step 9.5: a publish has written this Accepted draft's start date (its step 1) after the edit read it, and is
        // about to move the pointer to it. The in-place edit must not land on the row going into force.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        h.AfterQuery = q =>
        {
            if (q.EntityName != "sprk_policyversion") return;
            h.AfterQuery = null;
            h.ConcurrentWrite("sprk_policyversion", v2, new Dictionary<string, object> { ["sprk_inforcefrom"] = Utc(2026, 11, 1) });
        };

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody), default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Row("sprk_policyversion", v2).GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8, "the accepted content is what goes into force");
        v1.Should().NotBe(v2);
    }

    [Fact]
    public async Task Accept_OfAnInvalidRule_IsRefusedWithItsErrors_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, body: """{"type":"DailyHoursCap","maxHoursPerDay":0}""", consequence: null);

        var outcome = await h.Service().DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Select(e => e.Field).Should().Contain(new[] { "ruleBody", "consequence" });
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Decide_OnAVersionInForce_Is409_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);

        var accept = await h.Service().DecideAsync(set, policy, true, h.WorkingToken(policy), default);
        var reject = await h.Service().DecideAsync(set, policy, false, h.WorkingToken(policy), default, rejectionReason: Reason);

        accept.ErrorCode.Should().Be("ruleset.proposal_in_force");
        reject.ErrorCode.Should().Be("ruleset.proposal_in_force");
        accept.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Publish_PutsEveryAcceptedRuleInForce_ThroughVersionsThenPoliciesThenTheRuleSet()
    {
        var h = new Harness();
        var set = h.RuleSet(visible: true);
        var (p1, v1) = h.Rule(set, "BILL-A", ProposalState.Accepted);
        var (p2, v2) = h.Rule(set, "BILL-B", ProposalState.Accepted, type: "TimeIncrement", body: IncrementBody,
            quote: "Bill in tenths.");
        h.Row("sprk_policyversion", v2)["sprk_messagetemplate"] = "Hours are not a multiple of {{rule.incrementHours}}.";

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Status.Should().Be("Published");
        outcome.Value.Published.Select(p => p.VersionId).Should().BeEquivalentTo(new[] { v1, v2 });
        h.Order.Should().Equal("bulk:sprk_policyversion", "bulk:sprk_policy", "update:sprk_ruleset");
        foreach (var (policy, version) in new[] { (p1, v1), (p2, v2) })
        {
            var row = h.Row("sprk_policy", policy);
            row.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(version);
            row.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
            h.Row("sprk_policyversion", version).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
        }

        var set1 = h.Row("sprk_ruleset", set);
        set1.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        set1.GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(Utc(2026, 11, 1));
    }

    [Theory]
    [InlineData(true, "Clause quotes will be visible to firms.")]
    [InlineData(false, "Clause quotes will not be visible to firms.")]
    public async Task Publish_StatesWhetherClauseQuotesWillBeVisibleToFirms(bool visible, string notice)
    {
        var h = new Harness();
        var set = h.RuleSet(visible: visible);
        h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.ClauseQuotesVisibleToFirms.Should().Be(visible);
        outcome.Value.ClauseVisibilityNotice.Should().Be(notice);
    }

    [Fact]
    public async Task Publish_WithOneInvalidAcceptedRule_RefusesTheWholePublishWithPerRuleErrors_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-GOOD", ProposalState.Accepted);
        var (bad, _) = h.Rule(set, "BILL-BAD", ProposalState.Accepted, body: """{"type":"DailyHoursCap","maxHoursPerDay":0}""");

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("ruleset.publish_invalid");
        outcome.Errors.Should().OnlyContain(e => e.PolicyId == bad);
        outcome.Errors.Should().Contain(e => e.Field == "ruleBody" && e.Reason == "schema_invalid");
        h.Writes.Should().BeEmpty("nothing is published when one rule is invalid");
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
    }

    [Fact]
    public async Task Publish_RefusesAnAcceptedRuleOfAnotherDomainsType_BecauseSaveValidationIsBillingOnly()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-ONT", ProposalState.Accepted, type: "Existence", body: """{"type":"Existence"}""");

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "ruleType" && e.Reason == "rule_type_wrong_domain");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ARuleWithNoConsequence_IsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, state: ProposalState.Accepted, consequence: null);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Errors.Should().ContainSingle(e => e.Field == "consequence");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_NeverPutsProposedOrRejectedRulesInForce_AndCountsThem()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (accepted, vAccepted) = h.Rule(set, "BILL-A", ProposalState.Accepted);
        var (proposed, _) = h.Rule(set, "BILL-P", ProposalState.Proposed);
        var (rejected, _) = h.Rule(set, "BILL-R", ProposalState.Rejected);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(vAccepted);
        outcome.Value.NotPublishedProposed.Should().Be(1);
        outcome.Value.NotPublishedRejected.Should().Be(1);
        foreach (var policy in new[] { proposed, rejected })
        {
            h.Row("sprk_policy", policy).Contains("sprk_currentversion").Should().BeFalse();
            h.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        }

        h.Writes.Should().NotContain(w => w.Entity == "sprk_policy" && w.Id != accepted);
    }

    [Fact]
    public async Task Publish_WithNothingAccepted_IsRefused_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-P", ProposalState.Proposed);
        h.Rule(set, "BILL-R", ProposalState.Rejected);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.ErrorCode.Should().Be("ruleset.nothing_to_publish");
        outcome.StatusCode.Should().Be(400);
        h.Writes.Should().BeEmpty();
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
    }

    [Fact]
    public async Task EditThenPublish_StampsThePredecessorsInForceTo_MovesThePointer_AndLeavesThePredecessorOtherwiseUnchanged()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished, effectiveFrom: Utc(2026, 1, 1));
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var service = h.Service();
        var edited = await service.EditAsync(set, policy, Edit(body: CapBody8, template: "Over {{rule.maxHoursPerDay}} hours."), default);
        var v2 = edited.Value!.Proposal.VersionId;
        await service.DecideAsync(set, policy, accept: true, h.WorkingToken(policy), default);
        var v1Before = h.Row("sprk_policyversion", v1).Attributes.ToDictionary(a => a.Key, a => a.Value);

        var outcome = await service.PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.Should().Be(new PublishedRuleVersion(policy, v2, 2, v1));
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        var predecessor = h.Row("sprk_policyversion", v1);
        predecessor.GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        predecessor.Attributes.Where(a => a.Key != "sprk_inforceto").Should().BeEquivalentTo(v1Before, "only the end stamp is written to a published version");
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_ruleset", set).GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(Utc(2026, 1, 1), "an already effective set keeps its start");
    }

    [Fact]
    public async Task Publish_OfMoreThan1000Rules_WritesInTransactionsOfAtMost1000_VersionsThenPolicies_RuleSetLast()
    {
        var h = new Harness();
        var set = h.RuleSet();
        for (var i = 0; i < 1001; i++)
        {
            h.Rule(set, "BILL-" + i, ProposalState.Accepted);
        }

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Published.Should().HaveCount(1001);
        h.BulkCalls.Should().Equal(
            ("sprk_policyversion", 1000), ("sprk_policyversion", 1), ("sprk_policy", 1000), ("sprk_policy", 1));
        h.Order.Last().Should().Be("update:sprk_ruleset");
    }

    [Fact]
    public async Task Get_ReadsTheVersionsOfManyRulesInBatchesOfAtMost500InValues()
    {
        var h = new Harness();
        var set = h.RuleSet();
        for (var i = 0; i < 501; i++)
        {
            h.Rule(set, "BILL-" + i);
        }

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Value!.Proposals.Should().HaveCount(501);
        var inLists = h.Queries
            .SelectMany(q => q.Criteria.Conditions.Concat(q.Criteria.Filters.SelectMany(f => f.Conditions)))
            .Where(c => c.Operator == ConditionOperator.In)
            .ToList();
        inLists.Should().NotBeEmpty();
        inLists.Should().OnlyContain(c => c.Values.Count <= 500, "SQL Server refuses about 2,100 parameters in one statement");
        inLists.Max(c => c.Values.Count).Should().Be(500);
    }

    [Fact]
    public async Task Publish_DoesNotTouchRulesAlreadyInForce()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (inForce, vInForce) = h.Rule(set, "BILL-LIVE", ProposalState.Accepted, current: true);
        var (draft, vDraft) = h.Rule(set, "BILL-NEW", ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(vDraft);
        h.Writes.Should().NotContain(w => w.Id == vInForce || w.Id == inForce);
    }

    [Fact]
    public async Task Publish_WithAnEffectiveDateBeforeThePredecessorStarted_IsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 12, 1));
        h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Errors.Should().ContainSingle(e => e.Field == "effectiveDate");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenThePolicyTransactionFails_LeavesTheOldRulesInForce_NotPublished_AndARetryCompletes()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        h.FailBulkFor = entity => entity == "sprk_policy";

        var failed = await service.PublishAsync(set, Effective, default);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.ErrorCode.Should().Be("ruleset.write_failed");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1, "nothing is in force before the pointer moves");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "the end date and the pointer move together (D-14): the pointer did not move, so the end date the first write made is taken back");

        h.FailBulkFor = null;
        var retried = await service.PublishAsync(set, Effective, default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        retried.Value!.Published.Single().VersionId.Should().Be(v2);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
    }

    /// <summary>A published rule with a drafted, accepted successor, and a pointer move that fails (review probe 2, 3).</summary>
    private static async Task<(Harness H, RuleSetService Service, Guid Set, Guid Policy, Guid V1, Guid V2)> FailedPointerMoveAsync(Func<string, bool>? failBulk = null)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        h.FailBulkFor = failBulk ?? (entity => entity == "sprk_policy");

        var failed = await service.PublishAsync(set, Effective, default);

        failed.ErrorCode.Should().Be("ruleset.write_failed");
        return (h, service, set, policy, v1, v2);
    }

    [Fact]
    public async Task Publish_AfterAFailedPointerMove_ARetryWithAnotherDate_PutsTheNewDateOnBothVersions_LeavingNoGap()
    {
        var (h, service, set, policy, v1, v2) = await FailedPointerMoveAsync();
        h.FailBulkFor = null;

        var retried = await service.PublishAsync(set, new DateOnly(2026, 12, 1), default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 12, 1));
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 12, 1),
            "the old version ends on the day the new one starts");
    }

    [Fact]
    public async Task Publish_WhenTheEndDateCannotBeTakenBack_TheFailureIsLoggedAsAnError_AndARetryWithAnotherDateRestampsIt()
    {
        var versionWrites = 0;
        // The first version write (the stamps) lands; the pointer move fails; the take-back (a second version write) fails too.
        var (h, service, set, policy, v1, v2) = await FailedPointerMoveAsync(e => e == "sprk_policy" || (e == "sprk_policyversion" && ++versionWrites > 1));

        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1), "the take-back failed");
        h.Logs.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains(v1.ToString()),
            "an end date that could not be taken back is named in an Error");
        h.FailBulkFor = null;

        var retried = await service.PublishAsync(set, new DateOnly(2026, 12, 1), default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 12, 1),
            "the old version is re-stamped when its end date differs from the new effective date");
    }

    [Fact]
    public async Task Publish_AfterAFailedPointerMove_RejectingTheDraftLeavesNoEndDateOnTheVersionInForce()
    {
        var (h, service, set, policy, v1, _) = await FailedPointerMoveAsync();
        h.FailBulkFor = null;

        (await service.DecideAsync(set, policy, accept: false, h.WorkingToken(policy), default, rejectionReason: Reason)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.nothing_to_publish");

        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "a rule that stays in force must not carry a permanent end date");
    }

    [Fact]
    public async Task Publish_WhenTheVersionWritesFailPartWay_TakesBackTheEndDatesTheEarlierChunkWrote()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        for (var i = 0; i < 501; i++) // 2 version updates each = 1,002: one chunk of 1,000 and one of 2
        {
            var (policy, _) = h.Rule(set, "BILL-" + i, ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        }

        var versionWrites = 0;
        h.FailBulkFor = e => e == "sprk_policyversion" && ++versionWrites == 2;

        var failed = await h.Service().PublishAsync(set, Effective, default);

        failed.ErrorCode.Should().Be("ruleset.write_failed");
        var numberOf = h.Rows["sprk_policyversion"].ToDictionary(v => v.Id, v => v.GetAttributeValue<int>("sprk_versionnumber"));
        h.Rows["sprk_policy"].Should().OnlyContain(p => numberOf[((EntityReference)p["sprk_currentversion"]).Id] == 1, "no pointer moved");
        h.Rows["sprk_policyversion"].Where(v => numberOf[v.Id] == 1).Should()
            .OnlyContain(v => !v.Contains("sprk_inforceto"), "no version in force keeps an end date after a failed publish");
    }

    [Fact]
    public async Task Publish_WhenOnlySomePolicyChunksMove_KeepsTheEndDatesOfTheMovedRules_AndTakesBackTheRest()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        for (var i = 0; i < 1001; i++) // 1,001 pointer moves: one chunk of 1,000 and one of 1
        {
            var (policy, _) = h.Rule(set, "BILL-" + i, ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        }

        var policyWrites = 0;
        h.FailBulkFor = e => e == "sprk_policy" && ++policyWrites == 2;
        var service = h.Service();

        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.write_failed");

        var numberOf = h.Rows["sprk_policyversion"].ToDictionary(v => v.Id, v => v.GetAttributeValue<int>("sprk_versionnumber"));
        var oldVersions = h.Rows["sprk_policyversion"].Where(v => numberOf[v.Id] == 1).ToList();
        var moved = h.Rows["sprk_policy"].Where(p => numberOf[((EntityReference)p["sprk_currentversion"]).Id] == 2).Select(p => p.Id).ToHashSet();
        moved.Should().HaveCount(1000);
        foreach (var old in oldVersions)
        {
            var policyMoved = moved.Contains(((EntityReference)old["sprk_policy"]).Id);
            old.GetAttributeValue<DateTime?>("sprk_inforceto").Should().Be(policyMoved ? Utc(2026, 11, 1) : null,
                "a version that was replaced keeps its end date; one that is still in force gets it taken back");
        }

        h.FailBulkFor = null;
        var retried = await service.PublishAsync(set, Effective, default);
        retried.Value!.Published.Should().HaveCount(1, "the 1,000 that moved are already in force; the retry finishes the last one");
    }

    // ── Review pass 2 item 2 (F1): the publish writes are conditional on the rows as read ──

    [Fact]
    public async Task Publish_WhenAnOldVersionWasStampedByAConcurrentPublish_TheVersionWriteIs409_AndThatEndDateIsNotTakenBack()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var theirs = Utc(2026, 10, 1);
        h.AfterQuery = q =>
        {
            if (q.EntityName != "sprk_policyversion") return;
            h.AfterQuery = null; // another admin's publish stamps v1 right after this publish read it
            h.ConcurrentWrite("sprk_policyversion", v1, new Dictionary<string, object> { ["sprk_inforceto"] = theirs });
        };

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Refusals.Should().ContainSingle().Which.Entity.Should().Be("sprk_policyversion");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(theirs,
            "the refused chunk wrote nothing, so the end date v1 carries is the other publish's and is not taken back");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        h.Writes.Should().NotContain(w => w.Kind == Write.BulkUpdate, "nothing of this publish landed");
    }

    [Fact]
    public async Task Publish_WhenThePointerWasMovedByAConcurrentPublish_ThePolicyWriteIs409_AndTheStampsOfThatChunkStand()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policy") return;
            h.OnBulk = null; // the other publish moved the pointer to v2 between this publish's version write and its pointer move
            h.ConcurrentWrite("sprk_policy", policy, new Dictionary<string, object> { ["sprk_currentversion"] = new EntityReference("sprk_policyversion", v2) });
        };

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Refusals.Should().ContainSingle().Which.Entity.Should().Be("sprk_policy");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1),
            "v1 really was superseded: its end date stands");
        h.ConditionalCalls.Should().HaveCount(2, "the version write and the refused pointer move; the take-back reads, sees the pointer on v2 and sends nothing");
    }

    // ── Review round 3 item 3: one publish of a rule set at a time (the take-back race) ──

    [Fact]
    public async Task Publish_WhileAnotherPublishOfTheSetHoldsTheClaim_Is409PublishInProgress_AndReadsAndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var held = await h.Lease.TryAcquireAsync(RuleSetService.PublishClaimId(set), null, TimeSpan.FromMinutes(10), default);
        held.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.publish_in_progress");
        h.Queries.Should().BeEmpty("a refused publish reads nothing");
        h.Writes.Should().BeEmpty();

        // The claim is per rule set: another set publishes while this one is held.
        var other = h.RuleSet();
        h.Rule(other, state: ProposalState.Accepted);
        (await h.Service().PublishAsync(other, Effective, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("refused")]
    [InlineData("failed")]
    [InlineData("throws")]
    public async Task Publish_ReleasesItsClaim_HoweverItEnds_SoTheNextPublishIsNotRefused(string ending)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        if (ending != "refused")
        {
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        }

        if (ending == "failed") h.FailBulkFor = entity => entity == "sprk_policy";
        if (ending == "throws") h.AfterQuery = _ => throw new InvalidOperationException("simulated read failure");

        var act = () => h.Service().PublishAsync(set, Effective, default);
        if (ending == "throws")
        {
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await act();
        }

        var next = await h.Lease.TryAcquireAsync(RuleSetService.PublishClaimId(set), null, TimeSpan.FromMinutes(10), default);
        next.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted, "the claim was released when the publish ended: " + ending);
    }

    [Fact]
    public async Task Publish_WhenTheClaimStoreIsUnreachable_FailsClosed_500_AndReadsAndWritesNothing()
    {
        var lease = new Mock<Spaarke.Scheduling.IScheduledJobLease>();
        lease.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Spaarke.Scheduling.ScheduledJobLeaseUnavailableException("redis down"));
        var h = new Harness { Lease = lease.Object };
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(500);
        outcome.ErrorCode.Should().Be("ruleset.write_failed");
        h.Queries.Should().BeEmpty();
        h.Writes.Should().BeEmpty();
        lease.Verify(l => l.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "there was no claim to release");
    }

    [Fact]
    public async Task Publish_WhenASecondPublishStartsWhileTheFirstFailsAtItsPointerMove_NoSupersededVersionIsLeftWithoutAnEndDate()
    {
        // The interleaving: P1 stamps v1 and its pointer move fails; P2 (another date) starts, reads v1 stamped, stamps it again and is
        // about to move the pointer; P1's take-back reads v1 stamped with the pointer unmoved and clears it; P2 moves the pointer.
        // Unclaimed, v1 ends superseded with no end date, which the in-force rule (072) never evaluates. Claimed, P2 is refused at once.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        var second = new DateOnly(2026, 12, 1);

        var parked = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        Task<RuleSetOutcome<RuleSetPublished>>? p2 = null;
        var policyCalls = 0;
        var firstFails = 0;
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policy") return;
            switch (Interlocked.Increment(ref policyCalls))
            {
                case 1: // P1's pointer move is about to fail: P2 starts now and runs until it finishes or is about to move its pointer
                    p2 = Task.Run(() => service.PublishAsync(set, second, default));
                    WaitHandle.WaitAny(new[] { parked.WaitHandle, ((IAsyncResult)p2).AsyncWaitHandle }, TimeSpan.FromSeconds(10));
                    Interlocked.Exchange(ref firstFails, 1);
                    break;
                default: // P2's pointer move: wait until P1 has failed and taken back
                    parked.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                    break;
            }
        };
        h.FailBulkFor = entity => entity == "sprk_policy" && Interlocked.Exchange(ref firstFails, 0) == 1;

        var first = await service.PublishAsync(set, Effective, default);
        release.Set();
        var secondOutcome = await p2!;

        first.ErrorCode.Should().Be("ruleset.write_failed");
        var pointer = h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id;
        var v1End = h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto");
        (pointer != v1).Should().Be(v1End is not null,
            "a version the pointer left carries an end date and a version still current carries none (pointer " + (pointer == v2 ? "v2" : "v1") + ", end " + v1End + ")");
        secondOutcome.StatusCode.Should().Be(409);
        secondOutcome.ErrorCode.Should().Be("ruleset.publish_in_progress");

        // The failed publish released its claim: the second publish, run again, completes and ends v1 on its date.
        var retried = await service.PublishAsync(set, second, default);
        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 12, 1));
    }

    // ── Review round 3 item 1 (F3): a refused pointer move that did not move the pointer ──

    [Theory]
    [InlineData("sprk_name", "Renamed by another admin")]
    [InlineData("sprk_enabled", false)]
    public async Task Publish_WhenThePolicyChangedWithoutItsPointerMoving_ThePolicyWriteIs409_AndTheVersionStillInForceKeepsNoEndDate(string column, object value)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policy") return;
            h.OnBulk = null; // another admin renames the policy (or toggles it) between this publish's version write and its pointer move
            h.ConcurrentWrite("sprk_policy", policy, new Dictionary<string, object> { [column] = value });
        };
        var service = h.Service();

        var outcome = await service.PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.concurrent_edit");
        h.Refusals.Should().ContainSingle().Which.Entity.Should().Be("sprk_policy");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1, "the refused pointer move wrote nothing");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "v1 is still the policy's current version, so it must not keep the end date this publish stamped: the stamp is taken back");

        var retried = await service.PublishAsync(set, Effective, default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1), "a retry after the reload completes the publish");
    }

    [Fact]
    public async Task Publish_WhenOneChunkOfPolicyWritesIsRefusedWithoutAPointerMove_TakesBackThatChunk_AndKeepsTheStampsOfThePointersThatMoved()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var olds = new Dictionary<Guid, Guid>(); // old version -> policy
        for (var i = 0; i < 1001; i++) // 1,001 pointer moves: a chunk of 1,000 that lands and a chunk of 1 that is refused
        {
            var (policy, v1) = h.Rule(set, "BILL-" + i.ToString("D4"), ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
            olds[v1] = policy;
        }

        var policyWrites = 0;
        Guid? renamed = null;
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policy" || ++policyWrites != 2) return;
            // The second chunk holds the one policy whose pointer move has not been written; another admin renames it first.
            var moved = h.Rows["sprk_policy"].Where(p => ((EntityReference)p["sprk_currentversion"]).Id is var cur
                && h.Row("sprk_policyversion", cur).GetAttributeValue<int>("sprk_versionnumber") == 2).Select(p => p.Id).ToHashSet();
            renamed = h.Rows["sprk_policy"].Single(p => !moved.Contains(p.Id)).Id;
            h.ConcurrentWrite("sprk_policy", renamed.Value, new Dictionary<string, object> { ["sprk_name"] = "Renamed" });
        };

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        renamed.Should().NotBeNull();
        foreach (var (old, policy) in olds)
        {
            var row = h.Row("sprk_policyversion", old);
            var pointerMoved = h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id != old;
            row.GetAttributeValue<DateTime?>("sprk_inforceto").Should().Be(pointerMoved ? Utc(2026, 11, 1) : null,
                "a version whose pointer moved keeps its end date; one that is still the current version has none");
        }
    }

    [Fact]
    public async Task Publish_WhenALaterVersionChunkIsRefused_TakesBackOnlyTheChunkThatLanded_Conditionally()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var olds = new List<Guid>();
        for (var i = 0; i < 501; i++) // 2 version updates each = 1,002: one chunk of 1,000 and one of 2 (the last rule's)
        {
            var (policy, v1) = h.Rule(set, "BILL-" + i.ToString("D3"), ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
            olds.Add(v1);
        }

        var theirs = Utc(2026, 10, 1);
        var versionWrites = 0;
        Guid? refusedOld = null;
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policyversion" || ++versionWrites != 2) return;
            // Before the second chunk lands, another publish stamps the old version that chunk holds.
            var chunk2 = h.Rows["sprk_policyversion"].Where(v => olds.Contains(v.Id)).Where(v => v.Contains("sprk_inforceto") == false).ToList();
            refusedOld = chunk2.Single().Id;
            h.ConcurrentWrite("sprk_policyversion", refusedOld.Value, new Dictionary<string, object> { ["sprk_inforceto"] = theirs });
        };

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.StatusCode.Should().Be(409);
        h.Row("sprk_policyversion", refusedOld!.Value).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(theirs, "the refused chunk is not taken back");
        h.Rows["sprk_policyversion"].Where(v => olds.Contains(v.Id) && v.Id != refusedOld).Should()
            .OnlyContain(v => !v.Contains("sprk_inforceto"), "the chunk that landed is taken back: those versions are still in force");
        var takeBack = h.ConditionalCalls.Last();
        takeBack.Entity.Should().Be("sprk_policyversion");
        takeBack.Rows.Should().HaveCount(500).And.NotContain(r => r.Id == refusedOld);
        h.Entities.Verify(e => e.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()),
            Times.Never, "every publish write, the take-back included, is conditional");
    }

    [Fact]
    public async Task Publish_TheTakeBackIsConditionalOnItsFreshRead_SoALateStampIsNotOverwritten()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        h.FailBulkFor = entity => entity == "sprk_policy"; // the pointer move fails, so the end date is taken back
        var late = Utc(2026, 10, 1);
        var versionWrites = 0;
        h.OnBulk = entity =>
        {
            if (entity != "sprk_policyversion" || ++versionWrites != 2) return;
            // Between the take-back's read and its write, another publish's stamp of v1 commits late.
            h.ConcurrentWrite("sprk_policyversion", v1, new Dictionary<string, object> { ["sprk_inforceto"] = late });
        };

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.ErrorCode.Should().Be("ruleset.write_failed");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(late,
            "the take-back read v1 before the late write; its clear is refused rather than wiping the other publish's end date");
        h.Logs.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains(v1.ToString()), "a version not taken back is named in an Error");
    }

    [Fact]
    public async Task Publish_TheTakeBackLeavesNoEndDate_EvenWhenTheOldVersionCarriedAStaleOneBeforeThePublish()
    {
        // Review pass 2 item 4: v1 carries E1 from an earlier failed take-back; a retry with E2 stamps E2, its pointer move fails, and the
        // take-back must clear v1 (it is in force), not put E1 back.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1), to: Utc(2026, 11, 1));
        h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        h.FailBulkFor = entity => entity == "sprk_policy";

        var outcome = await h.Service().PublishAsync(set, new DateOnly(2026, 12, 1), default);

        outcome.ErrorCode.Should().Be("ruleset.write_failed");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "v1 is in force; the stale E1 it carried before this publish is not restored");
    }

    [Fact]
    public async Task Publish_FinishingASet_ReportsTheVersionThatEnded_NeverARejectedDraftThatWasNeverInForce()
    {
        // Review pass 2 item 6: the retry branch reports the superseded version. A lower-numbered version with no end date was never in
        // force (a rejected draft) and superseded nothing.
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetDraft);
        var (neverInForce, rejected) = h.Rule(set, "BILL-A", ProposalState.Rejected);
        var current = h.AddVersion(neverInForce, 2, ProposalState.Accepted, body: CapBody8, from: Utc(2026, 11, 1));
        h.Row("sprk_policy", neverInForce)["sprk_currentversion"] = new EntityReference("sprk_policyversion", current);
        var (replaced, ended) = h.Rule(set, "BILL-B", ProposalState.Accepted, from: Utc(2026, 1, 1), to: Utc(2026, 11, 1));
        var successor = h.AddVersion(replaced, 2, ProposalState.Accepted, body: CapBody8, from: Utc(2026, 11, 1));
        h.Row("sprk_policy", replaced)["sprk_currentversion"] = new EntityReference("sprk_policyversion", successor);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Published.Single(p => p.PolicyId == neverInForce).SupersededVersionId.Should().BeNull(
            $"version 1 ({rejected}) was rejected and never in force");
        outcome.Value.Published.Single(p => p.PolicyId == replaced).SupersededVersionId.Should().Be(ended);
    }

    [Fact]
    public async Task Publish_WhenTheCallerDisconnectsDuringTheWrites_StillFinishesAllThreeWrites()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness { OnBulk = _ => cts.Cancel() }; // the client goes away as the first write starts
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);

        var outcome = await h.Service().PublishAsync(set, Effective, cts.Token);

        cts.IsCancellationRequested.Should().BeTrue("the test disconnected the caller during the writes");
        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
    }

    [Theory]
    [InlineData(11, 1)] // the same date
    [InlineData(12, 1)] // another date: the rules were put in force on the first, which the set then shows
    public async Task Publish_WhenTheLastWriteFails_ARetryMarksTheSetPublished_AndWritesNothingElse(int month, int day)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetDraft);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted);
        var service = h.Service();
        h.FailUpdateFor = entity => entity == "sprk_ruleset";

        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.write_failed");

        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1, "the rules are in force");
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
        h.FailUpdateFor = null;
        var writesBefore = h.Writes.Count;

        var retried = await service.PublishAsync(set, new DateOnly(2026, month, day), default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done, "the set is finished, not refused as having nothing to publish");
        retried.Value!.Status.Should().Be("Published");
        retried.Value.EffectiveDate.Should().Be(new DateOnly(2026, 11, 1));
        retried.Value.Published.Should().ContainSingle().Which.VersionId.Should().Be(v1);
        var row = h.Row("sprk_ruleset", set);
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        row.GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(Utc(2026, 11, 1));
        h.Writes.Skip(writesBefore).Should().ContainSingle().Which.Entity.Should().Be("sprk_ruleset", "only the rule set is written");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
    }

    [Fact]
    public async Task Publish_OfADraftSetWithARuleInForceAndAProposedOne_FinishesTheSet_AndCountsTheProposedOne()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetDraft);
        h.Rule(set, "BILL-LIVE", ProposalState.Accepted, current: true, from: Utc(2026, 11, 1));
        var (proposed, _) = h.Rule(set, "BILL-NEXT", ProposalState.Proposed);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.NotPublishedProposed.Should().Be(1);
        h.Row("sprk_policy", proposed).Contains("sprk_currentversion").Should().BeFalse("a Proposed rule is never put in force");
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
    }

    [Fact]
    public async Task Publish_OfAPublishedSetWithEverythingInForce_HasNothingToPublish_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        h.Rule(set, state: ProposalState.Accepted, current: true);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.ErrorCode.Should().Be("ruleset.nothing_to_publish");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_CancelledBeforeAnyWrite_WritesNothing_AndRethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, state: ProposalState.Accepted);

        var act = () => h.Service().PublishAsync(set, Effective, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("firmNoFirm")]
    [InlineData("noScope")]
    [InlineData("defaultWithFirm")]
    public async Task Publish_OfARuleSetWhoseAssignmentIsInconsistent_IsRefused_AndWritesNothing(string scenario)
    {
        var h = new Harness();
        var set = scenario switch
        {
            "firmNoFirm" => h.RuleSet(scope: (int)RuleSetScope.Firm),
            "noScope" => h.RuleSet(scope: null),
            _ => h.RuleSet(scope: (int)RuleSetScope.ClientDefault, firm: FirmId),
        };
        h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "scope");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_OfARetiredRuleSet_Is409_AndAnUnknownOrForeignDomainOneIs404()
    {
        var h = new Harness();
        var retired = h.RuleSet(RuleSetService.RuleSetRetired);
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        h.Rule(retired, state: ProposalState.Accepted);
        h.Rule(ontology, state: ProposalState.Accepted);
        var service = h.Service();

        (await service.PublishAsync(retired, Effective, default)).ErrorCode.Should().Be("ruleset.retired");
        (await service.PublishAsync(ontology, Effective, default)).ErrorCode.Should().Be("ruleset.not_found");
        (await service.PublishAsync(Guid.NewGuid(), Effective, default)).ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Assignment and visibility
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_MovesTheSetBetweenTheClientDefaultAndAFirm()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var service = h.Service();

        var toFirm = await service.AssignAsync(set, RuleSetScope.Firm, FirmId, default);
        var back = await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);

        toFirm.Value!.Scope.Should().Be("Firm");
        toFirm.Value.FirmId.Should().Be(FirmId);
        back.Value!.Scope.Should().Be("ClientDefault");
        back.Value.FirmId.Should().BeNull();
        h.Row("sprk_ruleset", set).Contains("sprk_firm").Should().BeFalse("moving to the client default clears the firm");
    }

    [Theory]
    [InlineData("firmNoFirm")]
    [InlineData("defaultWithFirm")]
    [InlineData("unknownFirm")]
    public async Task Assign_InvalidCombinations_AreRefused_AndWriteNothing(string scenario)
    {
        var h = new Harness();
        var set = h.RuleSet();

        var outcome = scenario switch
        {
            "firmNoFirm" => await h.Service().AssignAsync(set, RuleSetScope.Firm, null, default),
            "defaultWithFirm" => await h.Service().AssignAsync(set, RuleSetScope.ClientDefault, FirmId, default),
            _ => await h.Service().AssignAsync(set, RuleSetScope.Firm, Guid.NewGuid(), default),
        };

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SetVisibility_TogglesTheFlag_AndARetiredOrForeignRuleSetIsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet(visible: true);
        var retired = h.RuleSet(RuleSetService.RuleSetRetired);
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var service = h.Service();

        var off = await service.SetVisibilityAsync(set, false, default);

        off.Value!.VisibleToFirms.Should().BeFalse();
        h.Row("sprk_ruleset", set).GetAttributeValue<bool>("sprk_visibletofirms").Should().BeFalse();
        (await service.SetVisibilityAsync(retired, true, default)).ErrorCode.Should().Be("ruleset.retired");
        (await service.SetVisibilityAsync(ontology, true, default)).ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Clause visibility (the one decision 130 / 134 / 135 call)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ForFirm_WithTheFlagOffOrUnknown_ReturnsNoClauseText(bool? flag)
    {
        var text = RuleSetClauseVisibility.ForFirm(flag, "No timekeeper may bill more than 10 hours.", "Section 4.2");

        text.Quote.Should().BeNull();
        text.SectionReference.Should().BeNull();
        text.Should().Be(FirmClauseText.None);
    }

    [Fact]
    public void ForFirm_WithTheFlagOn_ReturnsQuoteAndSectionReference_AndNeverADocumentLink()
    {
        var text = RuleSetClauseVisibility.ForFirm(true, "No timekeeper may bill more than 10 hours.", "Section 4.2");

        text.Quote.Should().Be("No timekeeper may bill more than 10 hours.");
        text.SectionReference.Should().Be("Section 4.2");
        typeof(FirmClauseText).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Where(n => n != "EqualityContract")
            .Should().BeEquivalentTo(new[] { "Quote", "SectionReference", "FirmFacingName" }, "the result has no member that could carry the source document or a link to it");
        RuleSetClauseVisibility.ForFirm(true, " ", null).Should().Be(FirmClauseText.None);
        RuleSetClauseVisibility.ForFirm(true, " ", null, "A label alone").Should().Be(FirmClauseText.None, "a label without a clause is no text");
        RuleSetClauseVisibility.ForFirm(false, "Quote", "Section 1", "Label").Should().Be(FirmClauseText.None, "the flag off hides the label too");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 083: a firm sees a clause only for a policy version its invoice's snapshot applied
    // ═══════════════════════════════════════════════════════════════════════════

    private const string Quote = "No timekeeper may bill more than 10 hours a day.";
    private const string Section = "Section 4.2, p. 7";

    /// <summary>A Published, visible, Procedure-owned rule set with one rule in force; the Procedure and its version row.</summary>
    private static (Guid Procedure, Guid Set, Guid Policy, Guid Version) ProcedureRule(
        Harness h, string name = "INTERNAL Acme OCG v3", string? firmFacing = "Acme billing guidelines", bool visible = true, int status = RuleSetService.RuleSetPublished)
    {
        var procedure = h.Procedure(name, firmFacing);
        var set = h.RuleSet(status, visible, procedure: procedure);
        var (policy, version) = h.Rule(set, "BILL-P", ProposalState.Accepted, current: true);
        return (procedure, set, policy, version);
    }

    [Fact(DisplayName = "083: a version the invoice's snapshot applied shows its quote, section and the Procedure's firm-facing name")]
    public async Task GetFirmClause_ForAnAppliedVersion_ShowsTheClauseWithTheFirmFacingName()
    {
        var h = new Harness();
        var (_, _, _, version) = ProcedureRule(h);

        var shown = await h.Service().GetFirmClauseAsync(version, JudgedInvoice(h, version), default);

        shown.Should().Be(new FirmClauseText(Quote, Section, "Acme billing guidelines"));
    }

    [Fact(DisplayName = "083: the label is the Procedure's name when its firm-facing name is blank")]
    public async Task GetFirmClause_WithABlankFirmFacingName_LabelsWithTheName()
    {
        var h = new Harness();
        var (_, _, _, blank) = ProcedureRule(h, name: "Acme OCG", firmFacing: "  ");
        var (_, _, _, absent) = ProcedureRule(h, name: "Beta OCG", firmFacing: null);
        var invoice = JudgedInvoice(h, blank, absent);

        (await h.Service().GetFirmClauseAsync(blank, invoice, default)).FirmFacingName.Should().Be("Acme OCG");
        (await h.Service().GetFirmClauseAsync(absent, invoice, default)).FirmFacingName.Should().Be("Beta OCG");
    }

    [Fact(DisplayName = "083 (a): a version the snapshot lists as displaced, as excluded or not at all shows no text; the applied one beside it does")]
    public async Task GetFirmClause_ForADisplacedExcludedOrUnlistedVersion_ShowsNothing()
    {
        var h = new Harness();
        var (_, _, _, applied) = ProcedureRule(h);
        var (_, _, _, displaced) = ProcedureRule(h);
        var (_, _, _, excluded) = ProcedureRule(h);
        var (_, _, _, unlisted) = ProcedureRule(h);
        var (_, _, _, mixed) = ProcedureRule(h);
        var snapshot = SnapshotOf(
            AppliedOn(applied), DisplacedOn(displaced),
            new BillingRuleApplication(mixed, BillingRuleSource.Procedure, [BillingSubject.ForLine(0)], [BillingSubject.ForLine(1)], []));
        snapshot["resolutions"] = new JsonArray(new JsonObject
        {
            ["dates"] = new JsonArray("2026-11-01"),
            ["procedures"] = new JsonArray(),
            ["rules"] = new JsonArray(),
            ["excludedRules"] = new JsonArray(new JsonObject { ["policyVersionId"] = excluded.ToString("D") }),
            ["excludedProcedures"] = new JsonArray(),
            ["gaps"] = new JsonArray(),
        });
        var invoice = h.Invoice(FindingsWith(snapshot));
        var service = h.Service();

        (await service.GetFirmClauseAsync(applied, invoice, default)).Quote.Should().Be(Quote, "the control: the applied version shows");
        (await service.GetFirmClauseAsync(mixed, invoice, default)).Quote.Should().Be(Quote, "applied on one subject and displaced on another is applied");
        (await service.GetFirmClauseAsync(displaced, invoice, default)).Should().Be(FirmClauseText.None, "displaced: it never judged this invoice");
        (await service.GetFirmClauseAsync(excluded, invoice, default)).Should().Be(FirmClauseText.None, "excluded: it never judged this invoice");
        (await service.GetFirmClauseAsync(unlisted, invoice, default)).Should().Be(FirmClauseText.None, "not in the snapshot");
    }

    [Fact(DisplayName = "083 (b): with the visible-to-firms flag off an applied version shows nothing, label included")]
    public async Task GetFirmClause_WithTheFlagOff_ShowsNothingForAnAppliedVersion()
    {
        var h = new Harness();
        var (_, _, _, off) = ProcedureRule(h, visible: false);
        var (_, _, _, on) = ProcedureRule(h);
        var legacyOff = h.Rule(h.RuleSet(RuleSetService.RuleSetPublished, visible: false), "BILL-L", ProposalState.Accepted, current: true).Version;
        var invoice = JudgedInvoice(h, off, on, legacyOff);
        var service = h.Service();

        (await service.GetFirmClauseAsync(on, invoice, default)).Quote.Should().Be(Quote, "the control: the same world with the flag on shows");
        (await service.GetFirmClauseAsync(off, invoice, default)).Should().Be(FirmClauseText.None, "D-79: the flag is off");
        (await service.GetFirmClauseAsync(legacyOff, invoice, default)).Should().Be(FirmClauseText.None, "D-79: the flag is off");
    }

    [Theory(DisplayName = "083 (c): an invoice with no usable snapshot shows no text, however the snapshot is missing")]
    [InlineData("absentMember")]
    [InlineData("nullMember")]
    [InlineData("unknownVersion")]
    [InlineData("notAnObject")]
    [InlineData("noApplications")]
    [InlineData("noFindingsColumn")]
    [InlineData("blankFindings")]
    [InlineData("malformedFindings")]
    [InlineData("arrayFindings")]
    public async Task GetFirmClause_WithoutAUsableSnapshot_ShowsNothing(string shape)
    {
        var h = new Harness();
        var (_, _, _, version) = ProcedureRule(h);
        var good = SnapshotOf(AppliedOn(version));

        var invoice = shape switch
        {
            "absentMember" => h.Invoice(FindingsWith(null, member: false)),
            "nullMember" => h.Invoice(FindingsWith(null)),
            "unknownVersion" => h.Invoice(FindingsWith(WithMember(good, "version", 2))),
            "notAnObject" => h.Invoice(FindingsWith(JsonValue.Create("applied"))),
            "noApplications" => h.Invoice(FindingsWith(WithMember(good, "applications", null))),
            "noFindingsColumn" => h.Invoice(null),
            "blankFindings" => h.Invoice("  "),
            "malformedFindings" => h.Invoice("{\"procedureSnapshot\":"),
            _ => h.Invoice("[]"),
        };
        var control = JudgedInvoice(h, version);
        var service = h.Service();

        (await service.GetFirmClauseAsync(version, control, default)).Quote.Should().Be(Quote, "the control: a usable snapshot shows");
        (await service.GetFirmClauseAsync(version, invoice, default)).Should().Be(FirmClauseText.None);
    }

    private static JsonObject WithMember(JsonObject snapshot, string name, JsonNode? value)
    {
        var copy = (JsonObject)snapshot.DeepClone();
        copy[name] = value;
        return copy;
    }

    [Fact(DisplayName = "083: no submission, or only a submission that is not the invoice's own, shows no text")]
    public async Task GetFirmClause_WithoutTheInvoicesOwnSubmission_ShowsNothing()
    {
        var h = new Harness();
        var (_, _, _, version) = ProcedureRule(h);
        var snapshot = FindingsWith(SnapshotOf(AppliedOn(version)));

        var noSubmission = Guid.NewGuid();
        h.Seed(new Entity("sprk_invoice", noSubmission) { ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId) });
        // A foreign submission (it applied the version) is seeded FIRST, so a reader that takes "the first submission linked to the invoice"
        // would return text; only the own submission (its id derives the invoice's id; it displaced the version) is the invoice's snapshot.
        var ownSubmission = Guid.NewGuid();
        var foreign = InvoiceSubmissionPersister.InvoiceIdFor(ownSubmission);
        h.Seed(new Entity("sprk_invoice", foreign) { ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId) });
        h.AddSubmission(foreign, Guid.NewGuid(), snapshot);
        h.AddSubmission(foreign, ownSubmission, FindingsWith(SnapshotOf(DisplacedOn(version))));
        // A second foreign submission that applied the version is seeded AFTER the own one, so a reader that takes "the last submission linked"
        // would also return text. Only the own submission, in the middle, is read.
        h.AddSubmission(foreign, Guid.NewGuid(), snapshot);
        var service = h.Service();

        (await service.GetFirmClauseAsync(version, noSubmission, default)).Should().Be(FirmClauseText.None);
        (await service.GetFirmClauseAsync(version, foreign, default)).Should().Be(FirmClauseText.None,
            "the own submission displaced it; a foreign linked submission that applied it is not read");
    }

    [Fact(DisplayName = "083: an invoice with no firm, an unknown invoice, an empty id or a read fault shows no text")]
    public async Task GetFirmClause_FailsClosedWithoutAFirmOrAnInvoice()
    {
        var h = new Harness();
        var (_, _, _, version) = ProcedureRule(h);
        var findings = FindingsWith(SnapshotOf(AppliedOn(version)));
        var firmless = h.Invoice(findings, noFirm: true);
        var service = h.Service();

        (await service.GetFirmClauseAsync(version, h.Invoice(findings), default)).Quote.Should().Be(Quote, "the control: with a firm it shows");
        (await service.GetFirmClauseAsync(version, firmless, default)).Should().Be(FirmClauseText.None, "the invoice has no firm");
        (await service.GetFirmClauseAsync(version, Guid.NewGuid(), default)).Should().Be(FirmClauseText.None, "no such invoice");
        (await service.GetFirmClauseAsync(version, Guid.Empty, default)).Should().Be(FirmClauseText.None);
        (await service.GetFirmClauseAsync(Guid.Empty, h.Invoice(findings), default)).Should().Be(FirmClauseText.None);
        (await service.GetFirmClauseAsync(Guid.NewGuid(), h.Invoice(findings), default)).Should().Be(FirmClauseText.None, "the snapshot does not list it");
    }

    [Theory(DisplayName = "083: a fault on any read shows no text and throws nothing; the log carries no id or name")]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_invoicesubmission")]
    [InlineData("sprk_policyversion")]
    [InlineData("sprk_policy")]
    [InlineData("sprk_ruleset")]
    [InlineData("sprk_procedure")]
    public async Task GetFirmClause_OnAReadFault_ShowsNothing_AndLogsNoIdOrName(string faultingTable)
    {
        var h = new Harness();
        var (procedure, set, policy, version) = ProcedureRule(h);
        var invoice = JudgedInvoice(h, version);
        var service = h.Service();
        (await service.GetFirmClauseAsync(version, invoice, default)).Quote.Should().Be(Quote, "the control: without the fault it shows");
        h.Logs.Clear();
        var secrets = new[] { invoice, version, procedure, set, policy, FirmId }.Select(id => id.ToString("D")).Append("INTERNAL Acme OCG v3").ToArray();
        h.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == faultingTable), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse fault on " + string.Join(" ", secrets)));

        var shown = await service.GetFirmClauseAsync(version, invoice, default);

        shown.Should().Be(FirmClauseText.None);
        h.Logs.Should().Contain(l => l.Level == LogLevel.Warning, "a fault is logged");
        h.Logs.Where(l => l.Level >= LogLevel.Information).Should().OnlyContain(l => l.Exception == null,
            "an exception object carries the fault text (ids and names) to the log sink; only its type name may be logged");
        // The Debug detail entry carries the exception by design (the service documents it); it is below the Information threshold.
        // The Warning alone names exactly two ids, as structured parameters, so a fault can be traced to its read: the invoice and the policy
        // version the caller passed. Every other id and name stays banned at every level, and those two stay banned at every other level.
        var warning = h.Logs.Single(l => l.Level == LogLevel.Warning);
        warning.Message.Should().Contain(invoice.ToString("D")).And.Contain(version.ToString("D"), "the Warning names the invoice and the version");
        h.Logs.Should().OnlyContain(
            l => secrets.All(s => !l.Message.Contains(s) || (l.Level == LogLevel.Warning && (s == invoice.ToString("D") || s == version.ToString("D")))),
            "no id or name appears in any entry's message, except the invoice and policy version ids on the Warning");
        h.Logs.Where(l => l.Level >= LogLevel.Information).Should().OnlyContain(l => secrets.All(s => l.Exception == null || !l.Exception.ToString().Contains(s)),
            "no id or name appears in the exception text of an entry at Information or above");
    }

    [Fact(DisplayName = "083: a caller's own cancellation is not turned into a clause answer")]
    public async Task GetFirmClause_WhenTheCallerCancels_Propagates()
    {
        var h = new Harness();
        var (_, _, _, version) = ProcedureRule(h);
        var invoice = JudgedInvoice(h, version);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        h.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoice"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => h.Service().GetFirmClauseAsync(version, invoice, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(DisplayName = "083: a Procedure published, changed or retired after the invoice was judged does not change the answer for that invoice")]
    public async Task GetFirmClause_DoesNotReResolve_ALaterProcedureChangeLeavesTheAnswerAlone()
    {
        var h = new Harness();
        var (_, set, policy, v1) = ProcedureRule(h);
        var invoice = JudgedInvoice(h, v1);
        var service = h.Service();
        var before = await service.GetFirmClauseAsync(v1, invoice, default);

        // Later: version 2 of the rule goes live (v1 ends, the pointer moves), another Procedure and its rule are published, and the Procedure is renamed.
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, quote: "A newer clause.", location: "Section 9", from: Utc(2026, 11, 1));
        h.Row("sprk_policyversion", v1)["sprk_inforceto"] = Utc(2026, 11, 1);
        h.Row("sprk_policy", policy)["sprk_currentversion"] = new EntityReference("sprk_policyversion", v2);
        var (_, _, _, newcomer) = ProcedureRule(h, name: "Published later");
        h.Row("sprk_ruleset", set)["statuscode"] = new OptionSetValue(RuleSetService.RuleSetRetired);
        h.Queries.Clear();

        var after = await service.GetFirmClauseAsync(v1, invoice, default);

        after.Should().Be(before, "the invoice was judged on v1; nothing published or retired since changes what its firm sees");
        (await service.GetFirmClauseAsync(v2, invoice, default)).Should().Be(FirmClauseText.None, "v2 did not judge this invoice, though it is in force now");
        (await service.GetFirmClauseAsync(newcomer, invoice, default)).Should().Be(FirmClauseText.None, "a Procedure published later did not judge it");
        h.Queries.Select(q => q.EntityName).Distinct().Should().BeSubsetOf(
            new[] { "sprk_invoice", "sprk_invoicesubmission", "sprk_policyversion", "sprk_policy", "sprk_ruleset", "sprk_procedure" },
            "no Procedure assignment, override or Procedure Version is read: the snapshot is not re-resolved");
    }

    [Fact(DisplayName = "083: a record rule's clause carries the neutral label, never the Procedure's names, and reads no Procedure")]
    public async Task GetFirmClause_ForARecordRule_CarriesTheNeutralLabel()
    {
        var h = new Harness();
        var procedure = h.Procedure("BASE-INTERNAL-NAME", "BASE-FIRM-NAME");
        var recordSet = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, scope: RecordRuleSet.ScopeRecord, procedure: procedure);
        var firmRecordSet = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, scope: RecordRuleSet.ScopeFirmRecord, firm: FirmId, procedure: procedure);
        var (_, record) = h.Rule(recordSet, "BRR-1", ProposalState.Accepted, current: true);
        var (_, firmRecord) = h.Rule(firmRecordSet, "BRR-2", ProposalState.Accepted, current: true);
        var invoice = JudgedInvoice(h, record, firmRecord);

        var shown = await h.Service().GetFirmClauseAsync(record, invoice, default);
        var shownForFirm = await h.Service().GetFirmClauseAsync(firmRecord, invoice, default);

        shown.Should().Be(new FirmClauseText(Quote, Section, "Billing instructions for this record"));
        shownForFirm.FirmFacingName.Should().Be("Billing instructions for this record");
        System.Text.Json.JsonSerializer.Serialize(shown).Should().NotContain("BASE-");
        h.Queries.Should().NotContain(q => q.EntityName == "sprk_procedure", "the neutral label needs no Procedure");
    }

    [Fact(DisplayName = "083: a legacy rule set (no Procedure) shows its clause with no label")]
    public async Task GetFirmClause_ForALegacyRuleSet_HasNoLabel()
    {
        var h = new Harness();
        var legacy = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var (_, version) = h.Rule(legacy, "BILL-L", ProposalState.Accepted, current: true);

        var shown = await h.Service().GetFirmClauseAsync(version, JudgedInvoice(h, version), default);

        shown.Should().Be(new FirmClauseText(Quote, Section, null));
    }

    [Fact(DisplayName = "083: the snapshot is the authority, not the rule set's scope: another firm's set that judged this invoice shows")]
    public async Task GetFirmClause_HasNoScopeSwitchOfItsOwn()
    {
        var h = new Harness();
        var procedure = h.Procedure("Other firm OCG", "Other firm guidelines");
        var otherFirmsSet = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, scope: (int)RuleSetScope.Firm, firm: OtherFirmId, procedure: procedure);
        var (_, version) = h.Rule(otherFirmsSet, "BILL-O", ProposalState.Accepted, current: true);

        var shown = await h.Service().GetFirmClauseAsync(version, JudgedInvoice(h, version), default);

        shown.FirmFacingName.Should().Be("Other firm guidelines");
        shown.Quote.Should().Be(Quote);
    }

    [Fact(DisplayName = "083: a rule set that is not Published or Retired, a non-Billing policy or rule set, and an unnameable Procedure show no text")]
    public async Task GetFirmClause_FailsClosedOnTheRuleSide()
    {
        var h = new Harness();
        var draftSet = h.RuleSet(RuleSetService.RuleSetDraft, visible: true);
        var ontologySet = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, domain: RuleDomains.PolicyDomainOntology);
        var published = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var (_, vDraftSet) = h.Rule(draftSet, "BILL-DS", ProposalState.Accepted, current: true);
        var (_, vOntologyPolicy) = h.Rule(published, "ONT-P", ProposalState.Accepted, current: true, domain: RuleDomains.PolicyDomainOntology);
        var (_, vOntologySet) = h.Rule(ontologySet, "BILL-OS", ProposalState.Accepted, current: true);
        var gone = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, procedure: Guid.NewGuid());
        var (_, vGone) = h.Rule(gone, "BILL-G", ProposalState.Accepted, current: true);
        var nameless = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, procedure: h.Procedure("  ", null));
        var (_, vNameless) = h.Rule(nameless, "BILL-N", ProposalState.Accepted, current: true);
        var retiredVersion = ProcedureRule(h, status: RuleSetService.RuleSetRetired).Version;
        var (_, vPublished) = h.Rule(published, "BILL-OK", ProposalState.Accepted, current: true);
        var orphanVersion = Guid.NewGuid();
        var invoice = JudgedInvoice(h, vDraftSet, vOntologyPolicy, vOntologySet, vGone, vNameless, retiredVersion, vPublished, orphanVersion);
        var service = h.Service();

        (await service.GetFirmClauseAsync(vPublished, invoice, default)).Quote.Should().Be(Quote, "the control: a Published billing set shows");
        (await service.GetFirmClauseAsync(retiredVersion, invoice, default)).Quote.Should().Be(Quote, "a retired set still holds the versions that judged earlier invoices");
        (await service.GetFirmClauseAsync(vDraftSet, invoice, default)).Should().Be(FirmClauseText.None, "the rule set is a draft");
        (await service.GetFirmClauseAsync(vOntologyPolicy, invoice, default)).Should().Be(FirmClauseText.None, "an Ontology policy has no firm clause");
        (await service.GetFirmClauseAsync(vOntologySet, invoice, default)).Should().Be(FirmClauseText.None, "an Ontology rule set is not a billing rule set");
        (await service.GetFirmClauseAsync(vGone, invoice, default)).Should().Be(FirmClauseText.None, "the Procedure cannot be read, so the clause cannot be labelled");
        (await service.GetFirmClauseAsync(vNameless, invoice, default)).Should().Be(FirmClauseText.None, "the Procedure has no usable name");
        (await service.GetFirmClauseAsync(orphanVersion, invoice, default)).Should().Be(FirmClauseText.None, "the snapshot lists a version that does not exist");
    }

    [Fact(DisplayName = "083: the result and the logs never carry the Procedure name (when a firm-facing name is set), an id or an override reason")]
    public async Task GetFirmClause_NeverCarriesTheBasis()
    {
        var h = new Harness();
        var (procedure, set, policy, version) = ProcedureRule(h);
        var snapshot = SnapshotOf(AppliedOn(version));
        snapshot["overrides"] = new JsonArray(new JsonObject
        {
            ["rowId"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "Exclude",
            ["procedureId"] = procedure.ToString("D"),
            ["reason"] = "OVERRIDE-REASON-TEXT",
            ["pinnedVersionId"] = Guid.NewGuid().ToString("D"),
        });
        snapshot["chain"] = new JsonArray(new JsonObject { ["table"] = "sprk_matter", ["id"] = Guid.NewGuid().ToString("D"), ["distance"] = 0 });
        var invoice = h.Invoice(FindingsWith(snapshot));
        var procedureVersion = Guid.NewGuid();
        h.Row("sprk_policyversion", version)["sprk_procedureversion"] = new EntityReference("sprk_procedureversion", procedureVersion);

        var shown = await h.Service().GetFirmClauseAsync(version, invoice, default);

        shown.FirmFacingName.Should().Be("Acme billing guidelines");
        var forbidden = new[] { "INTERNAL Acme OCG v3", "OVERRIDE-REASON-TEXT", procedure.ToString("D"), procedureVersion.ToString("D"), set.ToString("D"), policy.ToString("D"), version.ToString("D"), invoice.ToString("D") };
        var surfaces = new[] { System.Text.Json.JsonSerializer.Serialize(shown), shown.ToString(), $"{shown}" };
        foreach (var surface in surfaces)
        {
            forbidden.Should().OnlyContain(f => !surface.Contains(f));
        }

        h.Logs.Where(l => l.Level >= LogLevel.Information && forbidden.Any(f => l.Message.Contains(f))).Should().BeEmpty("no log line at Information or above names one");
        h.Queries.Where(q => q.EntityName == "sprk_policyversion").Should().OnlyContain(q => !q.ColumnSet.AllColumns && !q.ColumnSet.Columns.Contains("sprk_procedureversion"),
            "the Procedure Version link is not even selected");
    }

    [Fact]
    public async Task TurningTheFlagOff_StopsTheClauseImmediately()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var (_, version) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var invoice = JudgedInvoice(h, version);
        var service = h.Service();

        (await service.GetFirmClauseAsync(version, invoice, default)).Quote.Should().NotBeNull();
        await service.SetVisibilityAsync(set, false, default);

        (await service.GetFirmClauseAsync(version, invoice, default)).Should().Be(FirmClauseText.None);
    }
}
