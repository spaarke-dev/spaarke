using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 043: the decision commit route's write path (D-17) through the REAL <see cref="DecisionCommitService"/>, the REAL
/// <see cref="SignalCoreRecordAccess"/> (as the caller, over a scripted caller-identity client), the REAL
/// <see cref="DecisionPlanService"/>, the REAL <see cref="DecisionRecordWriter"/> over the SDK seam of
/// <see cref="OntologyWriterDataverseClient"/>, and the REAL tenant-cache ledger. The executors are scripted at their interface
/// (<see cref="IDecisionActionExecutor"/>): each executor's own writes are pinned in <c>DecisionActionExecutorsTests</c>, and the
/// typed-pair create runs through uac-r2's real core in <c>SecureChildOwnershipAiToolTests.DecisionCommit.cs</c>.
/// </summary>
/// <remarks>
/// What this file pins (ADR-038 data-mutation): the fixed order; record last; on a failure at each step boundary NO record and
/// NO Signal change with exactly the landed writes listed; one review id, one outcome; Also resolve; the authorization cases;
/// dismissal reasons; D-119; the gate tier; the server-derived core and owner; step 6 against a system closure.
/// Option values are the live ones (SignalWriter constants; sprk_decisionrecord read 2026-10-10).
/// </remarks>
public sealed class DecisionCommitServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Caller = Guid.Parse("c0430000-0000-4000-8000-000000000001");
    private static readonly Guid CallerOid = Guid.Parse("c0430000-0000-4000-8000-000000000002");
    private const string Tenant = "c0430000-0000-4000-8000-0000000000aa";
    private static readonly Guid TypeMatter = Guid.Parse("c0430000-0000-4000-8000-000000000010");
    private static readonly Guid Matter = Guid.Parse("c0430000-0000-4000-8000-000000000011");
    private static readonly Guid OtherMatter = Guid.Parse("c0430000-0000-4000-8000-000000000012");
    private static readonly Guid Policy = Guid.Parse("c0430000-0000-4000-8000-000000000020");
    private static readonly Guid PathB = Guid.Parse("c0430000-0000-4000-8000-000000000021");
    private static readonly Guid DoVersion = Guid.Parse("c0430000-0000-4000-8000-000000000022");
    private static readonly Guid Primary = Guid.Parse("c0430000-0000-4000-8000-000000000031");
    private static readonly Guid Second = Guid.Parse("c0430000-0000-4000-8000-000000000032");
    private static readonly Guid DoSignal = Guid.Parse("c0430000-0000-4000-8000-000000000033");
    private static readonly Guid Budget = Guid.Parse("c0430000-0000-4000-8000-000000000040");
    private static readonly Guid Assignee = Guid.Parse("c0430000-0000-4000-8000-000000000041");
    private static readonly Guid TodoId = Guid.Parse("c0430000-0000-4000-8000-000000000051");
    private static readonly Guid RevisionId = Guid.Parse("c0430000-0000-4000-8000-000000000052");
    private static readonly Guid ServiceRequestId = Guid.Parse("c0430000-0000-4000-8000-000000000053");
    private static readonly Guid CommunicationId = Guid.Parse("c0430000-0000-4000-8000-000000000054");
    private static readonly Guid RecordId = Guid.Parse("c0430000-0000-4000-8000-000000000060");
    private static readonly Guid ItemTodo = Guid.Parse("c0430000-0000-4000-8000-000000000070");
    private static readonly Guid ItemOwner = Guid.Parse("c0430000-0000-4000-8000-000000000071");
    private static readonly Guid CallerContactId = Guid.Parse("c0430000-0000-4000-8000-000000000072");
    private static readonly Guid ItemTeam = Guid.Parse("c0430000-0000-4000-8000-000000000073");
    private static readonly Guid SecureTeam = Guid.Parse("c0430000-0000-4000-8000-000000000074");
    private static readonly Guid OrdinaryTeam = Guid.Parse("c0430000-0000-4000-8000-000000000076");
    private static readonly Guid ReminderVersion = Guid.Parse("c0430000-0000-4000-8000-000000000075");
    private static readonly Guid ReassignVersion = Guid.Parse("c0430000-0000-4000-8000-000000000077");

    private const string PathBPlan = "{\"actions\":[\"send-budget-inquiry\",\"revise-budget\",\"approve-variance\"],\"nextSteps\":[\"add-todo\",\"send-email\"]}";
    private const string DoPlan = "{\"actions\":[\"mark-complete\",\"reschedule\"],\"nextSteps\":[\"add-todo\"]}";
    private const string ReminderPlan = "{\"actions\":[\"send-reminder\",\"mark-complete\"]}";
    private const string ReassignPlan = "{\"actions\":[\"reassign\",\"mark-complete\"],\"nextSteps\":[\"add-todo\"]}";

    private const int Open = SignalWriter.SignalStatusOpen;
    private const int Resolved = SignalWriter.SignalStatusResolved;

    // ── The rig ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Uac-r2 re-check F1: the ontology writer's own application user (WhoAmI on its connection), the <c>createdby</c> of its records.</summary>
    private static readonly Guid WriterUser = Guid.Parse("3121bf1b-9fbf-f111-aaaf-0022482913fc");

    /// <summary>Uac-r2 re-check F1: a Console user, who has Deep Create on <c>sprk_decisionrecord</c>.</summary>
    private static readonly Guid ConsoleCreator = Guid.Parse("c0430000-0000-4000-8000-0000000000cc");

    private sealed class Rig
    {
        public Dictionary<Guid, Entity> Signals { get; } = new();
        public Dictionary<Guid, int> Versions { get; } = new();
        public HashSet<Guid> CallerCannotRead { get; } = new();
        public HashSet<Guid> CallerCannotReadCore { get; } = new();
        public bool CallerUnresolved { get; set; }
        public List<string> Log { get; } = new();
        public List<Entity> Records { get; } = new();
        public List<(Guid Id, Dictionary<string, object> Fields)> SignalUpdates { get; } = new();
        public Dictionary<string, FakeExecutor> Executors { get; } = new(StringComparer.Ordinal);
        public Exception? FailRecordWith { get; set; }
        public Action<Guid>? BeforeSignalUpdate { get; set; }
        public List<Entity> ExistingRecords { get; } = new();

        /// <summary>Task 161 (D-180): how many ACTIVE budgets the matter has, as the caller reads them; null: the read is not scripted
        /// (404), which the routes must treat as "cannot be counted". <see cref="BudgetsReadFails"/> answers instead when set.</summary>
        public int? ActiveBudgets { get; set; }

        public DataverseUserResponse? BudgetsReadFails { get; set; }

        public List<string> BudgetReads { get; } = new();

        /// <summary>Uac-r2 re-check F1: every query the commit service's reads asked (the stored-record lookup included).</summary>
        public List<QueryExpression> QueriesAsked { get; } = new();

        /// <summary>Uac-r2 re-check F1: when set, WhoAmI on the writer's connection throws it; and how often it was asked.</summary>
        public Exception? WhoAmIFault { get; set; }

        public int WhoAmICalls;
        public Mock<IOrganizationServiceAsync2> Org { get; } = new();
        public Mock<IGenericEntityService> Entities { get; } = new();
        public Mock<IDataverseUserClient> User { get; } = new();
        public Mock<CallerRecordAccessProbe> Probe { get; }
        public InMemoryTenantCache Ledger { get; } = new();
        public Mock<DecisionCommitFollowUps> FollowUps { get; }
        public FakeTimeProvider Clock { get; } = new(Now);
        public List<IReadOnlyList<DecisionCommitService.SignalRow>> DismissedHandedOn { get; } = new();
        public List<IReadOnlyList<DecisionCommitStep>> FailedHandedOn { get; } = new();
        public Dictionary<(string Set, Guid Id), AccessRights> Rights { get; } = new();
        public IScheduledJobLease Lease { get; set; } = new ProcessLocalScheduledJobLease();

        /// <summary>The commit service's review ledger when a test wraps <see cref="Ledger"/>; the plan-read stamp keeps the plain one.</summary>
        public ITenantCache? LedgerStore { get; set; }
        public DecisionPlanReadStamp PlanReads { get; }
        public RecordOwnerResolution Owner { get; set; } = RecordOwnerResolution.Owned(OrdinaryTeam);

        /// <summary>When set, the REAL uac-r2 resolver (over an <c>OwnershipDirectory</c>) answers instead of <see cref="Owner"/>.</summary>
        public IRecordOwnershipResolver? RealResolver { get; set; }

        /// <summary>A fault of uac-r2's resolver (not an answer): thrown instead of <see cref="Owner"/>.</summary>
        public Exception? OwnerFault { get; set; }

        /// <summary>Every ownership question the resolver was asked, in order.</summary>
        public List<RecordOwnershipContext> OwnerAsks { get; } = new();

        /// <summary>The caller's linked internal contact (task 141 link); null: the caller has none.</summary>
        public Guid? CallerContact { get; set; } = CallerContactId;

        /// <summary>What the real identity service does on a read fault: it never throws, it returns the fail-soft identity
        /// (<c>ContactId</c> null) with these <see cref="IdentityReadFaults"/> set (<see cref="PersonIdentity.ContactUnreadable"/>).</summary>
        public IdentityReadFaults IdentityFaults { get; set; }

        /// <summary>A defensive case the real service does not produce: the identity service itself throws.</summary>
        public Exception? IdentityFault { get; set; }

        public Mock<IIdentityNormalizationService> Identity { get; } = new();

        /// <summary>D-134: the systemusers each contact is the primary contact of (uac-r2's link-candidate read, in memory).</summary>
        public AssignedAccessTestDoubles.FakeAssignedAccessStore Links { get; } = new();

        /// <summary>D-134: the To Do rows the app-only Assigned To read answers (an unscripted id reads as a To Do with no Assigned To).</summary>
        public Dictionary<Guid, Entity> TodoRows { get; } = new();

        /// <summary>D-134: a fault of the app-only To Do read that names Assigned To (the assignee read). Task 142: the Decision Record's
        /// filing reads the To Do's root columns separately (<see cref="TodoFilingReadFault"/>).</summary>
        public Exception? TodoReadFault { get; set; }

        /// <summary>Task 142: a fault of the core-ancestor read of the To Do subject (its root columns, no Assigned To).</summary>
        public Exception? TodoFilingReadFault { get; set; }

        /// <summary>Subject rows the CALLER cannot read (the sprk_todos entity set), and a fault answer for any subject read.</summary>
        public HashSet<Guid> CallerCannotReadSubject { get; } = new();

        public DataverseUserResponse? SubjectReadFails { get; set; }

        /// <summary>Task 154 (D-111): a To Do's <c>sprk_notes</c> as the caller reads it (the inquiry hint), returned when selected.</summary>
        public Dictionary<Guid, string> TodoNotes { get; } = new();

        /// <summary>Task 154: a fault answer for the caller's read of a To Do's notes only.</summary>
        public DataverseUserResponse? TodoNotesReadFails { get; set; }

        /// <summary>Task 154 (adversarial pass 1): a To Do's matter (<c>_sprk_regardingmatter_value</c>), returned with its notes.</summary>
        public Dictionary<Guid, Guid> TodoMatter { get; } = new();

        /// <summary>Task 154 (adversarial pass 1): service requests as the caller reads them: their matter, direction and statecode.
        /// One that is absent is 404.</summary>
        public Dictionary<Guid, (Guid Matter, int Direction, int State)> Inquiries { get; } = new();

        /// <summary>The caller's reads of service requests (task 154).</summary>
        public List<string> InquiryReads { get; } = new();

        /// <summary>Task 154 pass 2 F3: a fault answer for the caller's GET of a hinted inquiry.</summary>
        public DataverseUserResponse? InquiryReadFails { get; set; }

        /// <summary>Task 154 pass 2 F3: the answer to the caller's list of the matter's open inquiries, instead of the list (a fault,
        /// or a page with an <c>@odata.nextLink</c>).</summary>
        public DataverseUserResponse? InquiryListFails { get; set; }

        /// <summary>Task 154 pass 2 F3: thrown by the caller's list of the matter's open inquiries.</summary>
        public Exception? InquiryListThrows { get; set; }

        /// <summary>Task 150 (D-141): subjects the caller can read but that are not their own work (the "mine" read omits them).</summary>
        public HashSet<Guid> CallerNotMine { get; } = new();

        /// <summary>Task 150: a fault answer for the Do-lane "mine" read, and every such read asked.</summary>
        public DataverseUserResponse? MineReadFails { get; set; }

        public List<string> MineAsked { get; } = new();
        public Dictionary<Guid, Guid> CallerSeesCore { get; } = new();
        public List<(string Set, Guid Id)> RightsAsked { get; } = new();

        public const AccessRights AllRights =
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share | AccessRights.Delete;

        public Rig()
        {
            AddSignal(Primary, DecisionLane.Decide, Matter, PathB);

            foreach (var code in DecisionActionCatalog.All.Select(a => a.Code))
                Executors[code] = new FakeExecutor(code, this);

            Entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", TypeMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", TypeMatter) { ["sprk_recordlogicalname"] = "sprk_matter" });
            Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
            Entities.Setup(e => e.GetEntitySetNameAsync("sprk_todo", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_todos");
            Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                    new Entity("sprk_policyversion", id) { ["sprk_decisionplan"] = id == DoVersion ? DoPlan : id == ReminderVersion ? ReminderPlan : id == ReassignVersion ? ReassignPlan : PathBPlan });
            // The stored-record lookup is narrowed by createdby (uac-r2 re-check F1): like Dataverse, answer only the rows whose createdby
            // equals the query's createdby condition, when it has one.
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    QueriesAsked.Add(query);
                    var createdBy = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal);
                    return new EntityCollection(ExistingRecords
                        .Where(r => createdBy is null || Equals(r.GetAttributeValue<EntityReference>("createdby")?.Id, createdBy.Values.Single()))
                        .ToList());
                });
            Entities.Setup(e => e.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, string[] columns, CancellationToken _) =>
                    (columns.Contains("sprk_assignedto") ? TodoReadFault : TodoFilingReadFault) is { } fault
                        ? Task.FromException<Entity>(fault)
                        : Task.FromResult(TodoRows.TryGetValue(id, out var row) ? row : new Entity("sprk_todo", id)));

            Identity.Setup(i => i.ResolveAsync(Caller, It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken _) =>
                    IdentityFault is not null
                        ? throw IdentityFault
                        : Task.FromResult(new PersonIdentity(id, (IdentityFaults & (IdentityReadFaults.SystemUser | IdentityReadFaults.ContactBinding)) != 0 ? null : CallerContact) { Faults = IdentityFaults }));

            User.Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) => AnswerAsCaller(path));

            Org.Setup(o => o.RetrieveAsync("sprk_signal", It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, ColumnSet _, CancellationToken _) => Snapshot(id));
            Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns((Entity e, CancellationToken _) =>
                {
                    Log.Add($"record:{e.LogicalName}");
                    if (FailRecordWith is not null) throw FailRecordWith;
                    Records.Add(e);
                    return Task.FromResult(RecordId);
                });
            // Uac-r2 re-check F1: WhoAmI on the writer's own connection (the createdby of every row it creates).
            Org.Setup(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.WhoAmIRequest>(), It.IsAny<CancellationToken>()))
                .Returns((OrganizationRequest _, CancellationToken _) =>
                {
                    WhoAmICalls++;
                    return WhoAmIFault is { } fault
                        ? Task.FromException<OrganizationResponse>(fault)
                        : Task.FromResult<OrganizationResponse>(new Microsoft.Crm.Sdk.Messages.WhoAmIResponse { Results = { ["UserId"] = WriterUser } });
                });
            Org.Setup(o => o.ExecuteAsync(It.IsAny<UpdateRequest>(), It.IsAny<CancellationToken>()))
                .Returns((OrganizationRequest request, CancellationToken _) =>
                {
                    var target = ((UpdateRequest)request).Target;
                    BeforeSignalUpdate?.Invoke(target.Id);
                    if (target.RowVersion != Versions[target.Id].ToString())
                    {
                        throw new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060882), Message = "ConcurrencyVersionMismatch" });
                    }

                    Log.Add($"close:{target.Id}");
                    var fields = target.Attributes.ToDictionary(a => a.Key, a => a.Value);
                    SignalUpdates.Add((target.Id, fields));
                    foreach (var (k, v) in fields) Signals[target.Id][k] = v;
                    Versions[target.Id]++;
                    return Task.FromResult(new OrganizationResponse());
                });

            PlanReads = new DecisionPlanReadStamp(Ledger, Clock, NullLogger<DecisionPlanReadStamp>.Instance);

            Probe = new Mock<CallerRecordAccessProbe>(
                new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!)
            { CallBase = false };
            Probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => CallerUnresolved ? null : Caller);
            // FR-51: the caller's rights on the subject and the core record; every record is granted unless a test takes it away.
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, string set, Guid id, CancellationToken _) =>
                {
                    token.Should().Be("caller-token", "the rights are asked AS THE CALLER");
                    RightsAsked.Add((set, id));
                    return Rights.TryGetValue((set, id), out var granted) ? granted : AllRights;
                });

            var jobs = new Mock<JobSubmissionService>(MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }),
                NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
            var trigger = new SignalEvaluationTrigger(jobs.Object, NullLogger<SignalEvaluationTrigger>.Instance);
            var suppression = new SignalSuppressionService(Entities.Object, WriterClient(), Clock, new ConfigurationBuilder().Build(),
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<SignalSuppressionService>.Instance);
            FollowUps = new Mock<DecisionCommitFollowUps>(trigger, suppression, Entities.Object, NullLogger<DecisionCommitFollowUps>.Instance)
            { CallBase = false };
            FollowUps.Setup(f => f.AfterRecordedAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(),
                    It.IsAny<IReadOnlyList<DecisionCommitService.SignalRow>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid? _, IReadOnlyList<DecisionCommitStep> _, IReadOnlyList<DecisionCommitService.SignalRow> dismissed, CancellationToken _) =>
                    DismissedHandedOn.Add(dismissed))
                .ReturnsAsync(Array.Empty<string>());
            FollowUps.Setup(f => f.SecureFamiliesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<Guid> ids, CancellationToken _) =>
                {
                    FamiliesAsked.Add(ids);
                    return FamiliesFault is { } fault
                        ? Task.FromException<IReadOnlyList<SuppressionPartition>>(fault)
                        : Task.FromResult<IReadOnlyList<SuppressionPartition>>(
                            ids.Select(id => Families.TryGetValue(id, out var f) ? f : SuppressionPartition.Ordinary).ToList());
                });
            FollowUps.Setup(f => f.AfterFailedCommitAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid? _, IReadOnlyList<DecisionCommitStep> executed, CancellationToken _) => FailedHandedOn.Add(executed))
                .Returns(Task.CompletedTask);
        }

        /// <summary>Task 142: the <c>sprk_recordtype_ref</c> catalog the shared regarding builder types the Decision Record's pair from
        /// (spaarkedev1's rows: a communication subject is untyped, as live).</summary>
        public Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub RecordTypes => _recordTypes ??= new((name, _) =>
            CatalogFault is { } fault && _catalogReads++ >= CatalogFaultFromRead
                ? Task.FromException<Entity?>(fault)
                : Task.FromResult(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1.TryGetValue(name, out var id)
                    ? new Entity("sprk_recordtype_ref", id) { ["sprk_recorddisplayname"] = name }
                    : null));

        private Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub? _recordTypes;
        private int _catalogReads;

        /// <summary>Task 142: a fault of the record-type catalog read, from the <see cref="CatalogFaultFromRead"/>-th read (0: every read).</summary>
        public Exception? CatalogFault { get; set; }

        public int CatalogFaultFromRead { get; set; }

        /// <summary>Task 142: uac-r2's REAL core-ancestor resolver over <see cref="Entities"/>; the probe says every stamp column exists
        /// (they do on sprk_decisionrecord after task 140).</summary>
        public CoreAncestorResolver CoreAncestors() => new(
            Entities.Object,
            (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);

        /// <summary>Task 142: the secure family of each Signal the commit resolves (null: all ordinary). A fault, when set, is thrown.</summary>
        public Dictionary<Guid, SuppressionPartition> Families { get; } = new();

        public Exception? FamiliesFault { get; set; }

        public List<IReadOnlyList<Guid>> FamiliesAsked { get; } = new();

        public OntologyWriterDataverseClient WriterClient() =>
            new(() => Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);

        public void AddSignal(Guid id, DecisionLane lane, Guid? core, Guid version, int status = Open, int? resolution = null,
            DateTime? resolvedOn = null, string? subjectEntity = null, Guid? subjectId = null)
        {
            var e = new Entity("sprk_signal", id)
            {
                ["sprk_lane"] = new OptionSetValue((int)lane),
                ["sprk_policy"] = new EntityReference("sprk_policy", Policy),
                ["sprk_policyversion"] = new EntityReference("sprk_policyversion", version),
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["sprk_factsnapshot"] = "{\"variancePct\":12.5}",
                ["sprk_privilegeflagged"] = false,
            };
            if (core is { } c)
            {
                e["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", TypeMatter);
                e["sprk_corerecordid"] = c.ToString("D");
                e["sprk_matter"] = new EntityReference("sprk_matter", c);
            }

            if (resolution is { } r) e["sprk_resolutiontype"] = new OptionSetValue(r);
            if (resolvedOn is { } on) e["sprk_resolvedon"] = on;
            // Task 141: the subject is the typed subject lookup that carries the pair id (the pair type is a lookup to
            // sprk_recordtype_ref since task 140 and is not read).
            if (subjectEntity is not null && subjectId is { } subject
                && SignalWriter.RegardingLookupByEntity.TryGetValue(subjectEntity, out var subjectLookup))
                e[subjectLookup] = new EntityReference(subjectEntity, subject);
            if (subjectId is { } s) e["sprk_regardingrecordid"] = s.ToString("D");
            e["ownerid"] = new EntityReference("systemuser", ItemOwner);
            Signals[id] = e;
            Versions[id] = 1;
        }

        private Entity Snapshot(Guid id)
        {
            if (!Signals.TryGetValue(id, out var row)) throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), "missing");
            var copy = new Entity("sprk_signal", id) { RowVersion = Versions[id].ToString() };
            foreach (var (k, v) in row.Attributes) copy[k] = v;
            return copy;
        }

        private DataverseUserResponse AnswerAsCaller(string path)
        {
            if (CallerUnresolved)
                return DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.UserContextRequired, "no user");
            if (path is "WhoAmI" or "WhoAmI()")
                return Ok(new Dictionary<string, object?> { ["UserId"] = Caller.ToString("D") });
            if (path.Contains("&$filter=Microsoft.Dynamics.CRM.In(PropertyName='", StringComparison.Ordinal))
            {
                // Task 150 (D-141): the worklist's Do-lane "mine" read of a subject, AS THE CALLER. The subject is the caller's own
                // work unless a test says otherwise.
                var pk = System.Text.RegularExpressions.Regex.Match(path, "PropertyName='(\\w+)'").Groups[1].Value;
                var id = Guid.Parse(System.Text.RegularExpressions.Regex.Match(path, "PropertyValues=\\['([0-9a-f-]{36})'").Groups[1].Value);
                MineAsked.Add(path);
                if (MineReadFails is { } mineFault)
                    return mineFault;
                return Ok(new Dictionary<string, object?>
                {
                    ["value"] = CallerNotMine.Contains(id) || CallerCannotReadSubject.Contains(id)
                        ? Array.Empty<object>()
                        : new object[] { new Dictionary<string, object?> { [pk] = id.ToString("D") } },
                });
            }
            if (path.StartsWith("sprk_signals(", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path["sprk_signals(".Length..path.IndexOf(')')]);
                if (CallerCannotRead.Contains(id) || !Signals.TryGetValue(id, out var row))
                    return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
                var body = new Dictionary<string, object?>
                {
                    ["sprk_lane"] = row.GetAttributeValue<OptionSetValue>("sprk_lane").Value,
                    ["_sprk_policyversion_value"] = row.GetAttributeValue<EntityReference>("sprk_policyversion").Id.ToString("D"),
                    ["_ownerid_value"] = row.GetAttributeValue<EntityReference>("ownerid")?.Id.ToString("D"),
                };
                if (row.GetAttributeValue<EntityReference>("sprk_corerecordtype") is { } t)
                {
                    body["_sprk_corerecordtype_value"] = t.Id.ToString("D");
                    body["sprk_corerecordid"] = CallerSeesCore.TryGetValue(id, out var seen) ? seen.ToString("D") : row.GetAttributeValue<string>("sprk_corerecordid");
                }

                foreach (var lookup in SignalWriter.RegardingLookupByEntity.Values)
                    if (row.GetAttributeValue<EntityReference>(lookup) is { } subjectRef) body[$"_{lookup}_value"] = subjectRef.Id.ToString("D");
                if (row.Contains("sprk_regardingrecordid")) body["sprk_regardingrecordid"] = row.GetAttributeValue<string>("sprk_regardingrecordid");

                return Ok(body);
            }

            if (path.StartsWith("sprk_todos(", StringComparison.Ordinal))
            {
                // A no-core Signal's SUBJECT row, read AS THE CALLER (D-35 as amended by D-129).
                var id = Guid.Parse(path["sprk_todos(".Length..path.IndexOf(')')]);
                if (SubjectReadFails is { } fault)
                    return fault;
                var selectsNotes = path.Contains("$select=sprk_notes", StringComparison.Ordinal);
                if (selectsNotes && TodoNotesReadFails is { } notesFault)
                    return notesFault;
                if (CallerCannotReadSubject.Contains(id))
                    return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
                var todo = new Dictionary<string, object?> { ["createdon"] = "2026-01-01T00:00:00Z" };
                if (selectsNotes)
                {
                    todo["sprk_notes"] = TodoNotes.TryGetValue(id, out var notes) ? notes : null;
                    todo["_sprk_regardingmatter_value"] = TodoMatter.TryGetValue(id, out var todoMatter) ? todoMatter.ToString("D") : null;
                }
                return Ok(todo);
            }

            if (path.StartsWith("sprk_servicerequests(", StringComparison.Ordinal))
            {
                InquiryReads.Add(path);
                if (InquiryReadFails is { } inquiryFault)
                    return inquiryFault;
                var id = Guid.Parse(path["sprk_servicerequests(".Length..path.IndexOf(')')]);
                return Inquiries.TryGetValue(id, out var inquiry)
                    ? Ok(new Dictionary<string, object?> { ["statecode"] = inquiry.State, ["sprk_direction"] = inquiry.Direction })
                    : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
            }

            if (path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal))
            {
                // The matter's open outbound inquiries (the fallback), evaluated like Dataverse.
                InquiryReads.Add(path);
                if (InquiryListThrows is { } thrown)
                    throw thrown;
                if (InquiryListFails is { } listFault)
                    return listFault;
                var matter = Guid.Parse(System.Text.RegularExpressions.Regex.Match(path, "_sprk_regardingmatter_value eq ([0-9a-f-]{36})").Groups[1].Value);
                var direction = int.Parse(System.Text.RegularExpressions.Regex.Match(path, "sprk_direction eq (\\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var state = int.Parse(System.Text.RegularExpressions.Regex.Match(path, "statecode eq (\\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return Ok(new Dictionary<string, object?>
                {
                    ["value"] = Inquiries.Where(kv => kv.Value.Matter == matter && kv.Value.Direction == direction && kv.Value.State == state)
                        .Select(kv => new Dictionary<string, object?> { ["sprk_servicerequestid"] = kv.Key.ToString("D") }).ToList(),
                });
            }

            if (path.StartsWith("sprk_budgets?", StringComparison.Ordinal))
            {
                // Task 161: the matter's active budgets, as the caller reads them.
                BudgetReads.Add(path);
                if (BudgetsReadFails is { } budgetFault)
                    return budgetFault;
                return ActiveBudgets is { } count
                    ? Ok(new Dictionary<string, object?> { ["value"] = Enumerable.Range(0, Math.Min(count, 1)).Select(_ => new Dictionary<string, object?> { ["sprk_budgetid"] = Budget.ToString("D") }).ToList() })
                    : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
            }

            if (path.StartsWith("sprk_matters(", StringComparison.Ordinal) || path.StartsWith("sprk_projects(", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
                return CallerCannotReadCore.Contains(id)
                    ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no")
                    : Ok(new Dictionary<string, object?> { ["createdon"] = "2026-01-01T00:00:00Z" });
            }

            return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

        public DecisionCommitService Service()
        {
            var access = new SignalCoreRecordAccess(User.Object, Entities.Object, Identity.Object, NullLogger<SignalCoreRecordAccess>.Instance);
            var plans = new DecisionPlanService(Entities.Object, NullLogger<DecisionPlanService>.Instance);
            var ownership = new Mock<IRecordOwnershipResolver>();
            ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .Returns((RecordOwnershipContext context, CancellationToken _) =>
                {
                    Log.Add("owner");
                    OwnerAsks.Add(context);
                    return OwnerFault is not null ? throw OwnerFault : Task.FromResult(Owner);
                });
            IRecordOwnershipResolver resolver = ownership.Object;
            if (RealResolver is { } real)
            {
                var recording = new Mock<IRecordOwnershipResolver>();
                recording.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                    .Returns((RecordOwnershipContext context, CancellationToken token) =>
                    {
                        Log.Add("owner");
                        OwnerAsks.Add(context);
                        return real.ResolveOwnerAsync(context, token);
                    });
                resolver = recording.Object;
            }

            var records = new DecisionRecordWriter(WriterClient(), resolver, CoreAncestors(), Entities.Object, RecordTypes, Clock, NullLogger<DecisionRecordWriter>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
            return new DecisionCommitService(access, plans, new DecisionActionExecutors(Executors.Values), records, WriterClient(),
                Entities.Object, Probe.Object, Identity.Object, Links, LedgerStore ?? Ledger, FollowUps.Object, Lease, PlanReads, Clock, NullLogger<DecisionCommitService>.Instance);
        }

        public Task StampPlanRead(Guid signal, DateTimeOffset at) => PlanReads.StampAtAsync(Tenant, CallerOid.ToString("D"), signal, at, CancellationToken.None);

        public Task<DecisionCommitResult> CommitAsync(DecisionReview review, Guid? signal = null, HttpContext? http = null, CancellationToken ct = default) =>
            Service().CommitAsync(signal ?? Primary, review, http ?? Http(), ct);

        public Entity Record => Records.Single();

        public int Status(Guid id) => Signals[id].GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value;

        public int? Resolution(Guid id) => Signals[id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value;

        public Guid? LinkedRecord(Guid id) => Signals[id].GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id;

        public IEnumerable<string> Writes => Log.Where(l => l.StartsWith("execute:", StringComparison.Ordinal) || l.StartsWith("record:", StringComparison.Ordinal) || l.StartsWith("close:", StringComparison.Ordinal));
    }

    /// <summary>An executor scripted at the interface; it logs the order it is called in.</summary>
    private sealed class FakeExecutor(string code, Rig rig) : IDecisionActionExecutor
    {
        public string Code => code;

        public DecisionActionOutcome? Refusal { get; set; }

        public DecisionActionOutcome? Result { get; set; }

        public bool Throws { get; set; }

        public Action? OnPreflight { get; set; }

        public Action? OnExecute { get; set; }

        public List<CancellationToken> ExecuteTokens { get; } = new();

        public List<DecisionActionRequest> Requests { get; } = new();

        public Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
        {
            rig.Log.Add($"preflight:{code}");
            OnPreflight?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Refusal);
        }

        public Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
        {
            rig.Log.Add($"execute:{code}");
            Requests.Add(request);
            ExecuteTokens.Add(ct);
            OnExecute?.Invoke();
            if (Throws) throw new TimeoutException("no answer");
            return Task.FromResult(Result ?? DecisionActionOutcome.Done(code, DefaultWritten(code)));
        }

        private static DecisionRecordRef[] DefaultWritten(string code) => code switch
        {
            "revise-budget" => [new("sprk_budgetrevision", RevisionId), new("sprk_budget", Budget)],
            "create-budget" => [new("sprk_budget", Budget)],
            "add-todo" => [new("sprk_todo", TodoId)],
            "send-budget-inquiry" => [new("sprk_servicerequest", ServiceRequestId), new("sprk_communication", CommunicationId)],
            "send-email" => [new("sprk_communication", Guid.Parse("c0430000-0000-4000-8000-000000000055"))],
            _ => [],
        };
    }

    private static HttpContext Http(bool withClaims = true)
    {
        var http = new DefaultHttpContext();
        if (withClaims)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", Tenant), new Claim("oid", CallerOid.ToString("D"))], "test"));
        http.Request.Headers.Authorization = "Bearer caller-token";
        return http;
    }

    private static IReadOnlyDictionary<string, JsonElement> Values(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value), StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, JsonElement> InquiryValues =
        Values(("to", "billing@firm.example"), ("subject", "Budget"), ("body", "Please confirm the revised budget"));

    private static readonly IReadOnlyDictionary<string, JsonElement> ReviseValues =
        Values(("budget", Budget.ToString("D")), ("amount", 125000.5m), ("reason", "Scope grew"));

    private static readonly IReadOnlyDictionary<string, JsonElement> ApproveValues = Values(("amount", 5000), ("note", "ok"));

    private static readonly IReadOnlyDictionary<string, JsonElement> CreateBudgetValues =
        Values(("amount", 90000m), ("reason", "No budget was ever set"));

    private static readonly IReadOnlyDictionary<string, JsonElement> TodoValues =
        Values(("title", "Chase the revision"), ("assignee", Assignee.ToString("D")), ("dueDate", "2026-10-20"));

    private static DecisionReviewAction Take(string code, IReadOnlyDictionary<string, JsonElement>? values = null) =>
        new() { Code = code, Taken = true, Values = values };

    private static DecisionReviewAction Skip(string code) => new() { Code = code, Taken = false };

    private static DecisionReview PathBReview(
        bool inquiry = true, bool revise = true, bool approve = false, bool todo = true, Guid? reviewId = null, IReadOnlyList<Guid>? also = null) =>
        new()
        {
            ReviewId = reviewId ?? Guid.NewGuid(),
            Confirmed = true,
            AlsoResolve = also,
            Actions =
            [
                inquiry ? Take("send-budget-inquiry", InquiryValues) : Skip("send-budget-inquiry"),
                revise ? Take("revise-budget", ReviseValues) : Skip("revise-budget"),
                approve ? Take("approve-variance", ApproveValues) : Skip("approve-variance"),
            ],
            NextSteps = todo ? [new DecisionReviewNextStep { Code = "add-todo", Values = TodoValues }] : null,
            Because = "The firm agreed the scope change",
        };

    /// <summary>Task 161 (D-180): the Path B review as the wizard builds it for a matter with no active budget: Create budget in the
    /// place Revise budget had.</summary>
    private static DecisionReview CreateBudgetReview(bool create = true, bool inquiry = false, Guid? reviewId = null) =>
        new()
        {
            ReviewId = reviewId ?? Guid.NewGuid(),
            Confirmed = true,
            Actions =
            [
                inquiry ? Take("send-budget-inquiry", InquiryValues) : Skip("send-budget-inquiry"),
                create ? Take("create-budget", CreateBudgetValues) : Skip("create-budget"),
                Skip("approve-variance"),
            ],
            Because = "The matter never had a budget",
        };

    private static DecisionReview Dismissal(string? code, string? detail = null) =>
        new()
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Skip("send-budget-inquiry"), Skip("revise-budget"), Skip("approve-variance")],
            Dismissal = code is null ? null : new DecisionReviewDismissal { Code = code, Detail = detail },
        };

    private static JsonElement Steps(Entity record) => JsonDocument.Parse(record.GetAttributeValue<string>("sprk_steps")).RootElement;

    // ── AC1: the Path B review ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PathB_InquiryAndRevisionPlusATodo_RunsInD17Order_WritesOneRecordLast_AndClosesTheSignalActed()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Writes.Should().Equal(
            "execute:revise-budget", "execute:add-todo", "execute:send-budget-inquiry", "record:sprk_decisionrecord", $"close:{Primary}");
        rig.Log.IndexOf("preflight:send-budget-inquiry").Should().BeLessThan(rig.Log.IndexOf("execute:revise-budget"),
            "every preflight runs before the first write");

        var record = rig.Record;
        var steps = Steps(record);
        steps.GetProperty("steps").EnumerateArray().Select(s => (s.GetProperty("code").GetString(), s.GetProperty("taken").GetBoolean()))
            .Should().Equal(("send-budget-inquiry", true), ("revise-budget", true), ("approve-variance", false));
        steps.GetProperty("steps")[2].GetProperty("outcome").GetString().Should().Be("Skipped");
        JsonDocument.Parse(record.GetAttributeValue<string>("sprk_followons")).RootElement[0].GetProperty("id").GetGuid().Should().Be(TodoId);
        record.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier4", "the inquiry is an external send (the pure function, D-52)");
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Caller);

        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        rig.Signals[Primary].GetAttributeValue<EntityReference>("sprk_resolvedby").Id.Should().Be(Caller);
        result.FollowOns.Should().Equal(new DecisionCommitRef("sprk_todo", TodoId));
        result.GateTier.Should().Be("Tier4");
    }

    [Fact]
    public async Task TheExecutors_GetTheServerDerivedCore_WithItsTypedPair_AndTheSignalsMatter()
    {
        var rig = new Rig();

        await rig.CommitAsync(PathBReview());

        var request = rig.Executors["add-todo"].Requests.Single();
        request.Core.Should().Be(new DecisionCoreRecord("sprk_matter", Matter, TypeMatter),
            "the #1634 review: the child create sends the TYPED pair from sprk_corerecordtype");
        request.MatterId.Should().Be(Matter);
        request.SignalId.Should().Be(Primary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T145_TheDecisionsMatter_IsTheCorePairsMatter_NeverTheTypedSprkMatter(bool typedNamesAnotherMatter)
    {
        // D-34 (task 145): the matter the executors act on (and the budget-revision trigger re-evaluates) is the decision CONTEXT, so it
        // comes from sprk_corerecordtype + sprk_corerecordid. The typed sprk_matter lookup is not read: whether it names another matter
        // or is empty, the answer is the core's.
        var rig = new Rig();
        if (typedNamesAnotherMatter) rig.Signals[Primary]["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        else rig.Signals[Primary].Attributes.Remove("sprk_matter");

        await rig.CommitAsync(PathBReview());

        rig.Executors["add-todo"].Requests.Single().MatterId.Should().Be(Matter);
    }

    [Fact]
    public async Task T145_AProjectCoresDecision_HasNoMatter_EvenWhenATypedSprkMatterIsSet()
    {
        // Review F3: only a MATTER core pair gives the decision a matter; a project core gives none, whatever sprk_matter says.
        var rig = new Rig();
        var typeProject = Guid.Parse("c0430000-0000-4000-8000-000000000011");
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", typeProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", typeProject) { ["sprk_recordlogicalname"] = "sprk_project" });
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_project", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_projects");
        rig.Signals[Primary]["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", typeProject);
        rig.Signals[Primary]["sprk_matter"] = new EntityReference("sprk_matter", Matter);

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.ReasonCode);
        rig.Executors["add-todo"].Requests.Single().MatterId.Should().BeNull();
    }

    // ── AC2: the dismissal ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TakingNothingWithNotMaterial_WritesOneDismissalRecord_AndDismissesTheSignal()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Log.Should().NotContain(l => l.StartsWith("execute:", StringComparison.Ordinal));
        rig.Record.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be((int)RecordedDecisionClass.Dismissal);
        rig.Record.GetAttributeValue<string>("sprk_reason").Should().Be("not-material - Not material");
        rig.Record.Contains("sprk_gatetier").Should().BeFalse("nothing was taken, so there is no gate tier");
        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionDismissed);
        rig.Signals[Primary].GetAttributeValue<string>("sprk_resolutionnotes").Should().Be("not-material - Not material");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        rig.DismissedHandedOn.Single().Select(r => r.Id).Should().Equal(Primary);
    }

    [Theory]
    [InlineData(null, null, DecisionCommitReasons.DismissalReasonRequired)]
    [InlineData("other", null, DecisionCommitReasons.DismissalDetailRequired)]
    [InlineData("other", "   ", DecisionCommitReasons.DismissalDetailRequired)]
    [InlineData("not-mine", null, DecisionCommitReasons.DismissalReasonUnknown)] // a Do-lane reason on a Decide item
    public async Task ADismissalWithoutAReasonFromTheLanesList_OrOtherWithoutDetail_IsRefusedBeforeAnyWrite(string? code, string? detail, string reason)
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(Dismissal(code, detail));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(reason);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWithDetail_IsStoredAsCodeDashDetail()
    {
        var rig = new Rig();

        (await rig.CommitAsync(Dismissal("other", "Handled by the GC directly"))).Status.Should().Be(DecisionCommitStatus.Recorded);

        rig.Record.GetAttributeValue<string>("sprk_reason").Should().Be("other - Handled by the GC directly");
    }

    // ── AC3: a failure at each step boundary ─────────────────────────────────────────────────────────────────────

    public static TheoryData<string, int, string[]> FailingSteps => new()
    {
        // failing executor, D-17 step, writes confirmed landed before it
        { "revise-budget", 2, [] },
        { "add-todo", 3, ["sprk_budgetrevision", "sprk_budget"] },
        { "send-budget-inquiry", 4, ["sprk_budgetrevision", "sprk_budget", "sprk_todo"] },
    };

    [Theory]
    [MemberData(nameof(FailingSteps))]
    public async Task AFailureAtEachStepBeforeTheRecord_WritesNoRecord_ChangesNoSignal_AndListsExactlyWhatLanded(
        string failing, int step, string[] landed)
    {
        var rig = new Rig();
        rig.Executors[failing].Result = DecisionActionOutcome.Failed(failing, DecisionActionReasons.WriteFailed, "Dataverse said no");

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Failed);
        result.FailedStep.Should().Be(step);
        result.ActionCode.Should().Be(failing);
        result.Landed.Select(l => l.Entity).Should().Equal(landed);
        rig.Records.Should().BeEmpty("record last: no record after a failed step");
        rig.SignalUpdates.Should().BeEmpty("no Signal changes after a failed step");
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task AFailureWritingTheRecord_AfterTheInquiryWentOut_ChangesNoSignal_ListsEveryWrite_AndIsTheEscalationCase()
    {
        var rig = new Rig { FailRecordWith = new TimeoutException("no answer") };

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Failed);
        result.FailedStep.Should().Be(5);
        result.SentButNotRecorded.Should().BeTrue("escalation trigger 1: a send landed and the record did not");
        result.RecordMayExist.Should().BeTrue("a timed-out create may have committed");
        result.Resumable.Should().BeFalse("after a send the owner decides whether a retry may write the record");
        result.Landed.Select(l => l.Entity).Should().Equal("sprk_budgetrevision", "sprk_budget", "sprk_todo", "sprk_servicerequest", "sprk_communication");
        rig.SignalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task AnExecutorThatThrows_IsAFailedStep_WithNothingClaimedForIt()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Throws = true;

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Failed);
        result.FailedStep.Should().Be(3);
        result.Landed.Select(l => l.Entity).Should().Equal("sprk_budgetrevision", "sprk_budget");
        result.Resumable.Should().BeFalse("a step that threw may have landed; a retry could write it twice");
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyARefusedStep_IsResumable_AFailedOneMayHaveLanded()
    {
        var failedRig = new Rig();
        failedRig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");
        var refusedRig = new Rig();
        refusedRig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");

        (await failedRig.CommitAsync(PathBReview(inquiry: false))).Resumable.Should().BeFalse("a Failed create may have committed");
        (await refusedRig.CommitAsync(PathBReview(inquiry: false))).Resumable.Should().BeTrue("a Refused step wrote nothing");
    }

    [Fact]
    public async Task ARevisionThatLanded_BeforeAFailure_IsHandedToTheBudgetRevisionTrigger()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");

        await rig.CommitAsync(PathBReview());

        rig.FailedHandedOn.Single().Should().Contain(s => s.Code == "revise-budget" && s.Written.Any(w => w.Entity == "sprk_budgetrevision"),
            "032 review: the condition the revision cleared is re-evaluated when the commit fails before closing the Signal");
    }

    // ── AC4: one review id, one outcome ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSameReviewIdTwice_TheSecondWritesNothing_AndReturnsTheFirstOutcome()
    {
        var rig = new Rig();
        var review = PathBReview();

        var first = await rig.CommitAsync(review);
        var writes = rig.Writes.Count();
        var second = await rig.CommitAsync(review);

        second.Replayed.Should().BeTrue();
        second.DecisionRecordId.Should().Be(first.DecisionRecordId);
        second.Status.Should().Be(first.Status);
        rig.Writes.Should().HaveCount(writes, "a repeated review id performs no write");
        rig.Log.Count(l => l == "execute:send-budget-inquiry").Should().Be(1);
    }

    [Fact]
    public async Task TheSameReviewIdAfterAFailure_ReturnsTheFailure_AndWritesNothing()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview();

        var first = await rig.CommitAsync(review);
        rig.Executors["add-todo"].Result = null;
        var second = await rig.CommitAsync(review);

        second.Status.Should().Be(DecisionCommitStatus.Failed);
        second.Replayed.Should().BeTrue();
        second.Landed.Should().Equal(first.Landed);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task TheRecordLookup_IsByThisCallerAndThisReviewIdInSprkSteps()
    {
        var rig = new Rig();
        QueryExpression? asked = null;
        rig.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => asked = q)
            .ReturnsAsync(new EntityCollection());
        var review = PathBReview();

        await rig.CommitAsync(review);

        asked!.EntityName.Should().Be("sprk_decisionrecord");
        asked.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_confirmedby" && (Guid)c.Values[0] == Caller);
        asked.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_steps" && c.Operator == ConditionOperator.Like
                                                       && (string)c.Values[0] == $"%\"reviewId\":\"{review.ReviewId:D}\"%");
        Steps(rig.Record).GetProperty("reviewId").GetString().Should().Be(review.ReviewId.ToString("D"),
            "the lookup pattern matches what the writer stores");
    }

    [Fact]
    public async Task AReviewInProgress_IsRefusedWithoutAWrite()
    {
        var rig = new Rig();
        var review = PathBReview();
        await rig.Ledger.SetAsync(Tenant, DecisionCommitService.LedgerResource, $"{CallerOid:D}:{review.ReviewId:D}", DecisionCommitService.LedgerVersion,
            new DecisionCommitService.LedgerEntry(true, Now, "x", null));

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.InProgress);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusedReview_CanBeCorrectedAndSentAgainUnderTheSameId()
    {
        var rig = new Rig();
        var id = Guid.NewGuid();

        var refused = await rig.CommitAsync(Dismissal(null) with { ReviewId = id });
        var recorded = await rig.CommitAsync(Dismissal("duplicate") with { ReviewId = id });

        refused.Status.Should().Be(DecisionCommitStatus.Refused);
        recorded.Status.Should().Be(DecisionCommitStatus.Recorded, "a refusal wrote nothing, so it is not the review's outcome");
    }

    // ── AC5: Also resolve ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlsoResolve_ADecideSignalOnTheSameMatter_BothReferenceTheOneRecord()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Records.Should().ContainSingle();
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        rig.LinkedRecord(Second).Should().Be(RecordId);
        rig.Resolution(Second).Should().Be(SignalWriter.ResolutionActed);
        Steps(rig.Record).GetProperty("resolvedSignalIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(Primary, Second);
    }

    // ── Task 142 (contract C9, #1778): one decision never spans two secure walls ─────────────────────────────────────────

    private static readonly Guid SecureMatterX = Guid.Parse("c0430000-0000-4000-8000-0000000000e1");
    private static readonly Guid SecureProjectY = Guid.Parse("c0430000-0000-4000-8000-0000000000e2");

    private static void AssertNothingWritten(Rig rig, string because)
    {
        rig.Writes.Should().BeEmpty(because);
        rig.Records.Should().BeEmpty(because);
        rig.SignalUpdates.Should().BeEmpty(because);
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal), because);
        rig.Status(Primary).Should().Be(Open);
        rig.Status(Second).Should().Be(Open);
    }

    [Fact]
    public async Task A142_AlsoResolve_AcrossTwoSecureWalls_IsRefused_AndNothingIsWritten()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.Families[Primary] = SuppressionPartition.Secure([("sprk_matter", SecureMatterX)]);
        rig.Families[Second] = SuppressionPartition.Secure([("sprk_project", SecureProjectY)]);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        result.Detail.Should().NotContain(SecureMatterX.ToString("D")).And.NotContain(SecureProjectY.ToString("D"), "the refusal names no record");
        rig.FamiliesAsked.Should().ContainSingle().Which.Should().Equal(Primary, Second);
        rig.OwnerAsks.Should().BeEmpty("refused before the owner is resolved");
        AssertNothingWritten(rig, "a two-wall decision is refused before its first write");
    }

    [Fact]
    public async Task A142_TheWallCheckRunsAfterFR51_ACallerWhoMayNotDecide_GetsForbidden_NotTheWallRefusal()
    {
        // Adversarial pass 1 K4: the family answer (which tells "these items sit under different secure records") is given only to a
        // caller FR-51 lets act on every item.
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.Families[Primary] = SuppressionPartition.Secure([("sprk_matter", SecureMatterX)]);
        rig.Families[Second] = SuppressionPartition.Secure([("sprk_project", SecureProjectY)]);
        rig.Rights[("sprk_matters", Matter)] = AccessRights.Read | AccessRights.Write;

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.FamiliesAsked.Should().BeEmpty("the families are never read for a caller who may not decide");
        rig.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ordinary-beside-secure")]
    [InlineData("undetermined")]
    public async Task A142_AlsoResolve_WithAnItemOutsideTheSecureFamily_OrOneWhoseFamilyIsUnknown_IsRefused_AndNothingIsWritten(string second)
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.Families[Primary] = SuppressionPartition.Secure([("sprk_matter", SecureMatterX)]);
        rig.Families[Second] = second == "undetermined" ? SuppressionPartition.Undetermined : SuppressionPartition.Ordinary;

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused, second);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        AssertNothingWritten(rig, "fail closed: " + second);
    }

    [Fact]
    public async Task A142_AlsoResolve_WhenTheSecureFamiliesCannotBeRead_IsUnavailable_AndNothingIsWritten()
    {
        var rig = new Rig { FamiliesFault = new TimeoutException("no answer") };
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SecureFamilyUnavailable);
        AssertNothingWritten(rig, "a fault is not an answer");
    }

    [Fact]
    public async Task A142_AlsoResolve_WithinOneSecureFamily_IsRecorded_AndASingleItemDecisionReadsNoFamilies()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.Families[Primary] = SuppressionPartition.Secure([("sprk_matter", SecureMatterX)]);
        rig.Families[Second] = SuppressionPartition.Secure([("sprk_matter", SecureMatterX)]);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Records.Should().ContainSingle();

        var single = new Rig();
        (await single.CommitAsync(PathBReview())).Status.Should().Be(DecisionCommitStatus.Recorded);
        single.FamiliesAsked.Should().BeEmpty("one item is always one family");
    }

    [Fact]
    public async Task A142_TheRecordIsFiledUnderTheSubject_StampedFromIt_AndOwnedForEveryParent_ThroughTheRoute()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.TodoRows[ItemTodo] = new Entity("sprk_todo", ItemTodo) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter) };

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("sprk_regardingtodo").Id.Should().Be(ItemTodo, "filed under the Signal's subject");
        rig.Record.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(Matter, "stamped from the subject");
        rig.Record.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id
            .Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_todo"]);
        rig.Record.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(Matter, "the typed lineage column stays populated");
        rig.OwnerAsks.Should().ContainSingle("the 1f answer is reused at the write: same parents");
        rig.OwnerAsks[0].Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", Matter), new RecordOwnershipParent("sprk_todo", ItemTodo));
        rig.OwnerAsks[0].RequestedBy!.SystemUserId.Should().Be(Caller);
    }

    [Fact]
    public async Task A142_ACatalogFault_RefusesAtStep1f_BeforeTheInquiryIsSent_NeverSentButNotRecorded()
    {
        // Adversarial pass 2 F1: every filing read happens before the first write. A catalog fault refuses the whole decision at 1f.
        var rig = new Rig { CatalogFault = new TimeoutException("429") };

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        rig.Writes.Should().BeEmpty("refused before the inquiry is sent");
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task A142_TheWriteAtStep5_MakesNoFilingRead_AFaultThereCannotStrandASentDecision()
    {
        // The catalog answers once (step 1f) and faults on every later read: the write after the sends must not read it again.
        var rig = new Rig { CatalogFault = new TimeoutException("429"), CatalogFaultFromRead = 1 };

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.RecordTypes.Queried.Should().ContainSingle("the filing is computed once, at 1f, and applied at step 5 with no read");
        rig.Record.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(Matter);
        rig.Record.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id
            .Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_matter"]);
    }

    [Fact]
    public async Task A142_ASubjectThatNoLongerExists_IsRefused_NotUnavailable_AtStep1f_AndNothingIsWritten()
    {
        // Adversarial pass 1 F1: a permanent filing condition is a refusal (a retry never changes it), not a 503.
        var rig = new Rig
        {
            TodoFilingReadFault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "sprk_todo With Id = ... Does Not Exist"),
        };
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Refused, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.RecordRefused);
        result.Detail.Should().Contain(DecisionRecordRefusalReason.SubjectUnfileable).And.NotContain(ItemTodo.ToString("D"));
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task A142_WhenTheSubjectsFilingCannotBeRead_TheCommitIsUnavailable_AndNothingIsWritten()
    {
        var rig = new Rig { TodoFilingReadFault = new TimeoutException("no answer") };
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task A142_AnAccessDeniedFaultOnTheSubjectsCoreRead_IsUnavailable_NotARefusal_AndNothingIsWritten()
    {
        // Fix round 2 (F3, uac-r2): only "does not exist" (0x80040217) is permanent (422). An access-denied fault (0x80040220) on the
        // subject's core read is a fault a retry may clear: Unavailable (503), with nothing written.
        var rig = new Rig
        {
            TodoFilingReadFault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220) }, "SecLib::AccessCheckEx failed"),
        };
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        result.Detail.Should().NotContain(DecisionRecordRefusalReason.SubjectUnfileable);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task AlsoResolve_ADoSignal_OrASignalOnAnotherMatter_OrANoCoreSignal_IsRefusedBeforeAnyWrite()
    {
        foreach (var (lane, core) in new (DecisionLane, Guid?)[] { (DecisionLane.Do, Matter), (DecisionLane.Decide, OtherMatter), (DecisionLane.Do, null) })
        {
            var rig = new Rig();
            rig.AddSignal(Second, lane, core, lane == DecisionLane.Do ? DoVersion : PathB, subjectEntity: "sprk_todo", subjectId: ItemTodo);
            if (core is null) rig.Signals[Second]["ownerid"] = new EntityReference("systemuser", Caller); // readable to the caller (D-35)

            var result = await rig.CommitAsync(PathBReview(also: [Second]));

            result.Status.Should().Be(DecisionCommitStatus.Refused, $"{lane}/{core}");
            result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
            rig.Writes.Should().BeEmpty();
            rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ADoItem_NeverResolvesAnother()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.AddSignal(Second, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(new DecisionReview
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            AlsoResolve = [Second],
            Actions = [Take("mark-complete"), Skip("reschedule")],
        }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        rig.Writes.Should().BeEmpty();
    }

    // ── AC6: authorization ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACallerWhoCannotReadTheMatter_Gets404_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.CallerCannotReadCore.Add(Matter);

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.Log.Should().BeEmpty("not even a preflight runs");
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAlsoResolveSignalTheCallerCannotRead_Is404_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.CallerCannotRead.Add(Second);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerWhoCannotWriteAnActionsTarget_IsRefusedBeforeAnyWrite()
    {
        var rig = new Rig();
        // The inquiry runs LAST, so this proves the refusal comes from the preflight, not from the inquiry's own turn.
        rig.Executors["send-budget-inquiry"].Refusal =
            DecisionActionOutcome.Refused("send-budget-inquiry", DecisionActionReasons.NotAuthorized, "You cannot file under this matter.");

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ActionCode.Should().Be("send-budget-inquiry");
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnresolvedCaller_GetsTheSingle403()
    {
        var unresolved = new Rig { CallerUnresolved = true };
        (await unresolved.CommitAsync(PathBReview())).Status.Should().Be(DecisionCommitStatus.CallerUnresolved);
        unresolved.Log.Should().BeEmpty();

        var anonymous = new Rig();
        (await anonymous.CommitAsync(PathBReview(), http: Http(withClaims: false))).Status.Should().Be(DecisionCommitStatus.CallerUnresolved);
        anonymous.Log.Should().BeEmpty();
    }

    // ── AC7: unresolved codes, plan mismatch, conflicts ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnActionTheCatalogDoesNotKnow_IsRefused_NotDefaulted()
    {
        var rig = new Rig();
        var review = PathBReview();
        review = review with { Actions = [.. review.Actions!, Take("escalate")] };

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.UnresolvedAction);
        rig.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("notInPlan")]
    public async Task AReviewThatDoesNotListThePlanExactly_IsRefused(string shape)
    {
        var rig = new Rig();
        DecisionReviewAction[] actions = shape switch
        {
            "missing" => [Take("send-budget-inquiry", InquiryValues), Skip("revise-budget")],
            "duplicate" => [Take("send-budget-inquiry", InquiryValues), Skip("revise-budget"), Skip("revise-budget")],
            _ => [Take("send-budget-inquiry", InquiryValues), Skip("revise-budget"), Take("mark-complete")],
        };

        var result = await rig.CommitAsync(new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = actions });

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ActionsDoNotMatchPlan);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoActionsThatExcludeEachOther_AreRefused()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview(revise: true, approve: true));

        result.ReasonCode.Should().Be(DecisionCommitReasons.ActionsExclude);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingRequiredValue_OrAnInputTheActionDoesNotHave_IsRefused()
    {
        var rig = new Rig();
        var missing = PathBReview() with { Actions = [Take("send-budget-inquiry", Values(("to", "a@x.example"))), Skip("revise-budget"), Skip("approve-variance")] };
        var unknown = PathBReview() with
        {
            Actions = [Take("send-budget-inquiry", Values(("to", "a@x.example"), ("subject", "s"), ("body", "b"), ("bcc", "x"))), Skip("revise-budget"), Skip("approve-variance")],
        };

        (await rig.CommitAsync(missing)).ReasonCode.Should().Be(DecisionCommitReasons.InvalidParameter);
        (await rig.CommitAsync(unknown)).ReasonCode.Should().Be(DecisionCommitReasons.InvalidParameter);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnconfirmedReview_DoesNothing()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview() with { Confirmed = false });

        result.ReasonCode.Should().Be(DecisionCommitReasons.NotConfirmed);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASignalAPersonAlreadyResolved_IsAConflict_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionActed, Now.UtcDateTime.AddMinutes(-1));

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        rig.Writes.Should().BeEmpty();
    }

    // ── D-119 through the commit route ───────────────────────────────────────────────────────────────────────────

    private static async Task<DecisionCommitResult> ThroughTheRoute(Rig rig, DecisionReview review)
    {
        var http = Http();
        var reply = await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review, http, rig.Service(), CancellationToken.None);
        reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<DecisionCommitResult>>();
        return ((Microsoft.AspNetCore.Http.HttpResults.Ok<DecisionCommitResult>)reply).Value!;
    }

    [Fact]
    public async Task D119_TheInquiryAlone_WritesItsRecord_AndLeavesTheItemOpen_MarkedAndLinked()
    {
        var rig = new Rig();

        var result = await ThroughTheRoute(rig, PathBReview(inquiry: true, revise: false, todo: false));

        rig.Records.Should().ContainSingle("the inquiry's Decision Record is still written, record last");
        rig.Status(Primary).Should().Be(SignalWriter.SignalStatusAcknowledged, "the Work Item stays open (D-119)");
        rig.Resolution(Primary).Should().BeNull();
        rig.LinkedRecord(Primary).Should().Be(RecordId, "it shows that an inquiry was sent");
        result.Signals.Single().State.Should().Be("InquirySent");
    }

    [Fact]
    public async Task D119_ApproveVariance_ResolvesTheItemActed()
    {
        var rig = new Rig();

        await ThroughTheRoute(rig, PathBReview(inquiry: false, revise: false, approve: true, todo: false));

        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.Record.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier2b");
    }

    [Fact]
    public async Task D119_ReviseBudget_ResolvesTheItemActed_AndQueuesTheRevisionTriggerAfterTheClose()
    {
        var rig = new Rig();
        var order = new List<string>();
        rig.FollowUps.Setup(f => f.AfterRecordedAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(),
                It.IsAny<IReadOnlyList<DecisionCommitService.SignalRow>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? matter, IReadOnlyList<DecisionCommitStep> executed, IReadOnlyList<DecisionCommitService.SignalRow> _, CancellationToken _) =>
            {
                order.Add($"after-record:{rig.Status(Primary)}");
                matter.Should().Be(Matter);
                executed.Should().Contain(s => s.Code == "revise-budget");
            })
            .ReturnsAsync(Array.Empty<string>());

        await ThroughTheRoute(rig, PathBReview(inquiry: false, revise: true, todo: false));

        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        order.Should().Equal(new[] { $"after-record:{Resolved}" }, "the trigger is queued only after step 6 closed the Signal (032 review)");
    }

    // ── uac-r2 binding: core and owner are the server's ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AClientSuppliedCoreOrOwner_IsIgnored_TheRecordCarriesTheSignalsOwn()
    {
        var rig = new Rig();
        var forged = Guid.NewGuid();
        var json = $$"""
            {
              "reviewId": "{{Guid.NewGuid():D}}",
              "confirmed": true,
              "core": { "entityLogicalName": "sprk_matter", "recordId": "{{forged:D}}", "recordTypeRefId": "{{forged:D}}" },
              "itemOwnerUserId": "{{forged:D}}",
              "corerecordid": "{{forged:D}}",
              "actions": [
                { "code": "send-budget-inquiry", "taken": false },
                { "code": "revise-budget", "taken": false },
                { "code": "approve-variance", "taken": true, "values": { "amount": 10 } }
              ]
            }
            """;
        var review = JsonSerializer.Deserialize<DecisionReview>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Matter.ToString("D"));
        rig.Record.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(TypeMatter);
        rig.Record.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(Matter);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OrdinaryTeam, "the record is owned by the resolver's answer, never the client's");
    }

    private static Rig NoCoreRig()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        return rig;
    }

    private static DecisionReview MarkComplete() =>
        new() { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("mark-complete"), Skip("reschedule")] };

    // ── Task 154 (D-111, 059 review): an inquiry's "Record the outcome" To Do is never completed by Mark complete ──────────

    private static readonly Guid InquiryTodo = InquiryDispositionService.TodoIdFor(ServiceRequestId);

    /// <summary>A Do Signal on <paramref name="todoId"/>, a To Do on <see cref="Matter"/>; the inquiry <see cref="ServiceRequestId"/> is an
    /// outbound inquiry on that matter, open unless <paramref name="inquiryState"/> says otherwise.</summary>
    private static Rig InquiryTodoRig(Guid todoId, string? notes, int inquiryState = 0)
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: todoId);
        if (notes is not null)
            rig.TodoNotes[todoId] = notes;
        rig.TodoMatter[todoId] = Matter;
        rig.Inquiries[ServiceRequestId] = (Matter, BudgetInquiryExecutor.DirectionOutbound, inquiryState);
        return rig;
    }

    private static string InquiryHint(Guid serviceRequestId) => $"{InquiryReplyTodoCreator.NotesKey}{serviceRequestId:D}";

    private static readonly IReadOnlyDictionary<string, JsonElement> RescheduleValues =
        Values(("dueDate", "2026-10-30"), ("reason", "Waiting for the client's reply"));

    [Fact]
    public async Task AnInquiryOutcomeTodo_MarkCompleteTaken_Is409DispositionRequired_AndNothingIsWritten()
    {
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Conflict, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        result.ReasonCode.Should().Be("inquiry.disposition_required");
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnInquiryOutcomeTodo_RescheduleUnderTheReducedPlan_IsRecorded()
    {
        // The plan route offers [reschedule] (Mark complete removed); a review built from it lists only that action.
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));

        var result = await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("reschedule", RescheduleValues)] }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["reschedule"].Requests.Should().ContainSingle();
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnInquiryOutcomeTodo_AReviewThatSkipsMarkComplete_IsRecorded_TheSkipIsNotAnActionOfThePlan()
    {
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));

        var result = await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Skip("mark-complete"), Take("reschedule", RescheduleValues)] }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]                                   // no notes: an ordinary To Do
    [InlineData("Call the client back")]                 // notes without the hint
    [InlineData("serviceRequestId=other")]               // a hint that is not a GUID
    public async Task AnOrdinaryTodo_StillExecutesMarkComplete(string? notes)
    {
        var rig = InquiryTodoRig(ItemTodo, notes);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["mark-complete"].Requests.Single().Subject.Should().Be(new DecisionRecordRef("sprk_todo", ItemTodo));
    }

    [Fact]
    public async Task ATodoWhoseHintNamesAnInquiryThatDoesNotDeriveItsId_IsOrdinary_MarkCompleteExecutes()
    {
        // The hint is only a hint: a To Do that copies another inquiry's hint is not that inquiry's outcome To Do.
        var rig = InquiryTodoRig(ItemTodo, InquiryHint(ServiceRequestId));

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["mark-complete"].Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AClosedInquirysOutcomeTodo_IsOrdinary_MarkCompleteExecutes()
    {
        // Adversarial pass 1 F1: the disposition was recorded but the To Do's completion failed (logged only); the To Do went overdue.
        // It must not be stuck: the disposition route answers already_resolved, so Mark complete is the way out.
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId), inquiryState: 1);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["mark-complete"].Requests.Single().Subject.Should().Be(new DecisionRecordRef("sprk_todo", InquiryTodo));
        rig.InquiryReads.Should().Equal($"sprk_servicerequests({ServiceRequestId:D})?$select=statecode,sprk_direction");
    }

    [Fact]
    public async Task AnInquiryTheCallerCannotRead_IsOrdinary_MarkCompleteExecutes()
    {
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
        rig.Inquiries.Clear();

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
    }

    [Fact]
    public async Task AnOpenInquirysOutcomeTodo_WithAValidHint_CostsOneGetOfTheInquiry_AndNoList()
    {
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));

        (await rig.CommitAsync(MarkComplete(), DoSignal)).ReasonCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);

        rig.InquiryReads.Should().Equal($"sprk_servicerequests({ServiceRequestId:D})?$select=statecode,sprk_direction");
    }

    [Fact]
    public async Task AHintOnTheSecondLine_IsGuarded_409()
    {
        // Adversarial pass 1 F2: the assignee typed a note above the hint in TodoDetail.
        // No matter on the To Do, so the matter fallback cannot find it: the hint alone must.
        var rig = InquiryTodoRig(InquiryTodo, $"Phoned the firm on Tuesday\n{InquiryHint(ServiceRequestId)}");
        rig.TodoMatter.Remove(InquiryTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Conflict, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
        rig.InquiryReads.Should().Equal($"sprk_servicerequests({ServiceRequestId:D})?$select=statecode,sprk_direction");
    }

    [Theory]
    [InlineData(null)]                       // the notes were cleared
    [InlineData("Waiting on the firm")]      // the hint line was deleted
    public async Task AHintlessOpenInquirysOutcomeTodo_IsFoundByTheMatterFallback_AndRefused_409(string? notes)
    {
        // Adversarial pass 1 F2: the commit agrees with for-todo, which finds the inquiry by the derived id.
        var rig = InquiryTodoRig(InquiryTodo, notes);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Conflict, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
        rig.InquiryReads.Should().ContainSingle().Which.Should().StartWith("sprk_servicerequests?$select=sprk_servicerequestid&$filter=");
    }

    [Fact]
    public async Task AnOrdinaryTodoOnAMatterWithAnOpenInquiry_IsUnaffected_MarkCompleteExecutes_AfterOneList()
    {
        // The fallback finds the matter's open inquiry, but its derived To Do id is not this To Do's.
        var rig = InquiryTodoRig(ItemTodo, "Call the client back");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Executors["mark-complete"].Requests.Single().Subject.Should().Be(new DecisionRecordRef("sprk_todo", ItemTodo));
        rig.InquiryReads.Should().ContainSingle("the matter fallback: one list as the caller");
    }

    [Theory]
    [InlineData(404, DataverseUserClientErrorCodes.NotFound)]
    [InlineData(403, DataverseUserClientErrorCodes.AccessDenied)]
    public async Task WhenTheCallerCannotReadTheTodosNotes_TheCommitIsNotFound_NotUnavailable(int status, string code)
    {
        // Adversarial pass 1 K2: "cannot read" is the item's not-readable answer, never "try again".
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
        rig.TodoNotesReadFails = DataverseUserResponse.Fail(status, code, "no");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.NotFound, result.Detail);
        rig.Writes.Should().BeEmpty();
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheTodosNotesCannotBeRead_TheCommitIsUnavailable_NeverMarkedComplete()
    {
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
        rig.TodoNotesReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalUnreadable);
        rig.Writes.Should().BeEmpty();
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    // ── Task 154 pass 2 F3: the commit fails closed when the inquiry read faults ─────────────────────────────────────────

    public enum InquiryFault { HintedGet503, MatterList503, MatterListHasMorePages, MatterListThrows }

    [Theory]
    [InlineData(InquiryFault.HintedGet503)]
    [InlineData(InquiryFault.MatterList503)]
    [InlineData(InquiryFault.MatterListHasMorePages)]
    [InlineData(InquiryFault.MatterListThrows)]
    public async Task WhenTheInquiryCannotBeRead_TheCommitIsUnavailable_NothingRunsOrIsWritten(InquiryFault fault)
    {
        // The hinted fault keeps the hint; the matter-list faults have none, so the fallback is the only way to know.
        var rig = InquiryTodoRig(InquiryTodo, fault == InquiryFault.HintedGet503 ? InquiryHint(ServiceRequestId) : null);
        switch (fault)
        {
            case InquiryFault.HintedGet503:
                rig.InquiryReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
                break;
            case InquiryFault.MatterList503:
                rig.InquiryListFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
                break;
            case InquiryFault.MatterListHasMorePages:
                rig.InquiryListFails = DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["value"] = Array.Empty<object>(),
                    ["@odata.nextLink"] = "https://org.example/api/data/v9.2/sprk_servicerequests?$skiptoken=2",
                }));
                break;
            case InquiryFault.MatterListThrows:
                rig.InquiryListThrows = new TaskCanceledException("Dataverse timed out");
                break;
        }

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalUnreadable);
        rig.InquiryReads.Should().ContainSingle("the fault came from the inquiry read, not before it");
        rig.Executors.Values.SelectMany(e => e.Requests).Should().BeEmpty("no executor runs");
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal));
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
    }

    // ── Task 154 pass 2 K4: the finishing path (a writer-created record of this review exists) runs no inquiry check ──────

    [Fact]
    public async Task K4_FinishingAMarkCompleteRecordedWhileTheInquiryWasClosed_ClosesTheSignal_EvenThoughTheInquiryIsNowOpen()
    {
        // Mark complete ran while the inquiry was closed (an ordinary To Do); the close failed; the ledger forgot. The inquiry is
        // open again by the retry: before, the guard ran on the finishing path and answered 409, leaving the Signal open forever.
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId), inquiryState: 1);
        FirstCloseFails(rig);
        var review = MarkComplete();
        (await rig.CommitAsync(review, DoSignal)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(DoSignal).Should().Be(Open);
        await LedgerForgetsButTheRecordStays(rig, review);
        rig.Inquiries[ServiceRequestId] = (Matter, BudgetInquiryExecutor.DirectionOutbound, 0);
        var reads = rig.InquiryReads.Count;
        var executes = rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal));

        var result = await rig.CommitAsync(review, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.ReasonCode.Should().NotBe(DecisionCommitReasons.InquiryDispositionRequired);
        result.Replayed.Should().BeTrue();
        rig.LinkedRecord(DoSignal).Should().Be(RecordId, "the Signal is closed with the record found");
        rig.Status(DoSignal).Should().NotBe(Open);
        rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal)).Should().Be(executes, "nothing is run again");
        rig.InquiryReads.Should().HaveCount(reads, "the finishing path reads no inquiry");
    }

    public enum InquiryAtFinish { StillOpen, NowClosed, ReadFaults }

    [Theory]
    [InlineData(InquiryAtFinish.StillOpen)]
    [InlineData(InquiryAtFinish.NowClosed)]
    [InlineData(InquiryAtFinish.ReadFaults)]
    public async Task K4_FinishingAReviewRecordedUnderTheReducedPlan_ClosesTheSignal_WhateverTheInquiryIsNow(InquiryAtFinish now)
    {
        // The review was matched against the plan without Mark complete (the inquiry was open) and lists only Reschedule. Its stored
        // record carries the review's hash, so the finishing path matches it against that plan without reading the inquiry again.
        var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
        FirstCloseFails(rig);
        var review = new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("reschedule", RescheduleValues)] };
        (await rig.CommitAsync(review, DoSignal)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(DoSignal).Should().Be(Open);
        await LedgerForgetsButTheRecordStays(rig, review);
        if (now == InquiryAtFinish.NowClosed)
            rig.Inquiries[ServiceRequestId] = (Matter, BudgetInquiryExecutor.DirectionOutbound, 1);
        if (now == InquiryAtFinish.ReadFaults)
            rig.InquiryReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");

        var result = await rig.CommitAsync(review, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeTrue();
        rig.LinkedRecord(DoSignal).Should().Be(RecordId);
        rig.Executors["reschedule"].Requests.Should().ContainSingle("nothing is run again");
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATeamOwnedNoCoreItem_NowRecords_OwnedByTheTeamUac2sResolverNames_D129()
    {
        // Before D-129 the route read the item's ownerid and refused anything but a systemuser (item_owner_unsupported).
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Owned(ItemTeam);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var owner = rig.Record.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(ItemTeam, "the resolver's answer exactly as returned");
        rig.Record.Contains("owningbusinessunit").Should().BeFalse("it derives from ownerid; the route never sets it");
        rig.Executors["mark-complete"].Requests.Single().Subject.Should().Be(new DecisionRecordRef("sprk_todo", ItemTodo));
        rig.Resolution(DoSignal).Should().Be(SignalWriter.ResolutionActed);
        rig.Entities.Verify(e => e.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()), Times.Never,
            "the item's ownerid is never read: nothing uses an owner as a person (D-134 reads its Assigned To, not its owner)");
    }

    [Fact]
    public async Task TheResolverIsAskedWithTheSubjectAsTheParent_AndNeverOptsIntoKeepCreator()
    {
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Owned(ItemTeam);

        await rig.CommitAsync(MarkComplete(), DoSignal);

        var asked = rig.OwnerAsks.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("sprk_todo");
        asked.TargetRecordId.Should().Be(ItemTodo);
        asked.Parents.Should().Equal(new RecordOwnershipParent("sprk_todo", ItemTodo));
        asked.WhenUnfiled.Should().Be(UnfiledOwnership.ActingUserTeam);
        asked.KeepCreatorUnlessTargetIsTeamOwned.Should().BeFalse();
    }

    [Fact]
    public async Task ANoCoreItemWithASecureSubject_IsOwnedByTheSecureRecordOwnersTeam_D123()
    {
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true };

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam);
    }

    [Fact]
    public async Task ANoCoreItemWithAUserOwnedSubject_GetsTheResolversAnswer_NeverAnOwnerTheRouteChooses()
    {
        // A personal To Do: the resolver answers a team (the BU's team) or refuses; there is no third answer.
        var answered = NoCoreRig();
        answered.Owner = RecordOwnerResolution.Owned(ItemTeam);
        var refused = NoCoreRig();
        refused.Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "the personal To Do has no owning business unit");

        var ok = await answered.CommitAsync(MarkComplete(), DoSignal);
        var no = await refused.CommitAsync(MarkComplete(), DoSignal);

        ok.Status.Should().Be(DecisionCommitStatus.Recorded, ok.Detail);
        answered.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(ItemTeam);
        no.Status.Should().Be(DecisionCommitStatus.Refused);
        refused.Records.Should().BeEmpty("the route does not fall back to the user, the creator or the writer");
    }

    [Fact]
    public async Task AResolverRefusal_Is422ItemOwnerUnsupported_WithFixedText_AndNothingIsWritten()
    {
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "secret detail about the parent");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ItemOwnerUnsupported);
        result.Detail.Should().NotContain("secret").And.NotContain(RecordOwnerRefusal.SecureParentNotIsolated, "the resolver's reason is not echoed");
        rig.Writes.Should().BeEmpty("the refusal comes before the first write");
        rig.Executors["mark-complete"].Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AResolverFault_Is503_NotA422_AndNothingIsWritten()
    {
        var rig = NoCoreRig();
        rig.OwnerFault = new TimeoutException("no answer");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ItemOwnerUnsupported);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ANoCoreSignalThatNamesNoSubject_IsTheUniform404_WithoutAskingTheResolver()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        // The caller-scoped access check already refuses a no-core Signal with no subject (uniform 404), so the route never gets here.
        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.OwnerAsks.Should().BeEmpty();
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ANoCoreItem_WhoseSubjectTheCallerCannotRead_Is404_AndNeverReachesTheResolver()
    {
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Owned(ItemTeam);
        rig.CallerCannotReadSubject.Add(ItemTodo);   // e.g. a subject in a secure family, for a non-member (D-123)

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.OwnerAsks.Should().BeEmpty();
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ANoCoreItem_WhoseSubjectReadFaults_Is503_NotThe404()
    {
        var rig = NoCoreRig();
        rig.Owner = RecordOwnerResolution.Owned(ItemTeam);
        rig.SubjectReadFails = DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalUnreadable, "a read fault on the item is not an idempotency-store fault");
        result.Detail.Should().Be("This item could not be checked right now; nothing was done.");
        rig.OwnerAsks.Should().BeEmpty();
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ANoCoreItem_WhoseSignalTheCallerCannotRead_Is404_AndTheSubjectIsNeverAsked()
    {
        var rig = NoCoreRig();
        rig.CallerCannotRead.Add(DoSignal);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.User.Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── uac-r2 F1: the record's owner is asked about EVERY Signal's subject, not only the core ───────────────────────────────

    private static readonly Guid SecureProject = Guid.Parse("c0430000-0000-4000-8000-000000000080");
    private static readonly Guid SubjectUnderSecure = Guid.Parse("c0430000-0000-4000-8000-000000000081");
    private static readonly Guid SubjectOrdinary = Guid.Parse("c0430000-0000-4000-8000-000000000082");
    private static readonly Guid SubjectOrdinary2 = Guid.Parse("c0430000-0000-4000-8000-000000000083");

    /// <summary>The scenario: an ORDINARY matter (the core), a SECURE project, a To Do filed under both (its Signal is secure through
    /// its subject, its core is the ordinary matter), and two To Dos filed under the matter only.</summary>
    private static Tests.TestInfrastructure.OwnershipDirectory F1World()
    {
        var matter = new EntityReference("sprk_matter", Matter);
        return Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithOrdinaryRoot("sprk_matter", Matter)
            .WithSecureRoot("sprk_project", SecureProject)
            .WithRecord("sprk_todo", SubjectUnderSecure, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingmatter"] = matter, ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject) })
            .WithRecord("sprk_todo", SubjectOrdinary, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu, extra: new() { ["sprk_regardingmatter"] = matter })
            .WithRecord("sprk_todo", SubjectOrdinary2, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu, extra: new() { ["sprk_regardingmatter"] = matter });
    }

    private static Rig F1Rig(Guid primarySubject, Guid? secondSubject = null)
    {
        var rig = new Rig { RealResolver = F1World().Resolver() };
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: primarySubject);
        if (secondSubject is { } subject)
            rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: subject);
        return rig;
    }

    [Fact]
    public async Task F1_ASignalWhoseSubjectIsUnderASecureRecord_MakesTheRecordSecureOwned_EvenUnderAnOrdinaryCore()
    {
        var rig = F1Rig(SubjectUnderSecure);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var owner = rig.Record.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam,
            "the core is an ordinary matter, but the Signal's subject sits under a secure project: secure-if-any");
        rig.Record.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(Matter, "the core pair is unchanged");
        var asked = rig.OwnerAsks.Should().ContainSingle().Subject;
        asked.Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", Matter), new RecordOwnershipParent("sprk_todo", SubjectUnderSecure));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task F1_AnAlsoResolveSetMixingASecureAndAnOrdinarySubject_IsSecureOwned_WhicheverIsThePrimary(bool secureIsPrimary)
    {
        // The writer's secure-if-any answer over every parent, as defence in depth: the route's family check (task 142) is stubbed at
        // ONE family here (the Rig's default). With the families the walk really gives, the route refuses this mix before the owner is
        // asked (A142_AnAlsoResolveMixingASecureAndAnOrdinarySubject_IsRefusedByTheFamilyCheck_BeforeTheOwnerIsAsked).
        var rig = secureIsPrimary ? F1Rig(SubjectUnderSecure, SubjectOrdinary) : F1Rig(SubjectOrdinary, SubjectUnderSecure);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
        rig.OwnerAsks.Single().Parents.Select(p => p.RecordId).Should().Contain([Matter, SubjectUnderSecure, SubjectOrdinary],
            "the primary's and the also-resolved Signal's subjects are both asked about");
    }

    [Fact]
    public async Task A142_AnAlsoResolveMixingASecureAndAnOrdinarySubject_IsRefusedByTheFamilyCheck_BeforeTheOwnerIsAsked()
    {
        var rig = F1Rig(SubjectUnderSecure, SubjectOrdinary);
        rig.Families[Primary] = SuppressionPartition.Secure([("sprk_project", SecureProjectY)]);
        rig.Families[Second] = SuppressionPartition.Ordinary;

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        rig.OwnerAsks.Should().BeEmpty();
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task F1_AllOrdinarySubjects_GetTheResolversOrdinaryAnswer_TheCoresBusinessUnitTeam()
    {
        var rig = F1Rig(SubjectOrdinary, SubjectOrdinary2);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var owner = rig.Record.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.ChildTeam, "the ordinary matter's business-unit default team, exactly as returned");
        rig.Record.Contains("owningbusinessunit").Should().BeFalse("it derives from the owner");
    }

    [Fact]
    public async Task F1_ASubjectTheResolverCannotRead_RefusesTheWholeDecision_NotDroppedAsAParent()
    {
        var rig = F1Rig(Guid.NewGuid(), SubjectOrdinary);   // the first To Do is in no directory: unresolved, so it might be the secure one

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.RecordRefused);
        rig.Writes.Should().BeEmpty("nothing is sent, recorded or closed");
        rig.SignalUpdates.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
        rig.Status(Second).Should().Be(Open);
    }

    [Fact]
    public async Task F1_AResolverRefusal_OnACoreItem_Is422_WithNoWritesAndNoCloses()
    {
        var rig = new Rig { Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "secret detail") };
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: SubjectOrdinary);
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: SubjectUnderSecure);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.Detail.Should().NotContain("secret");
        rig.Writes.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.OwnerAsks.Single().Parents.Select(p => p.RecordId).Should().Contain([Matter, SubjectOrdinary, SubjectUnderSecure]);
    }

    // ── D-134: a personal To Do's record is owned by its Assigned To user's business-unit team ────────────────────────────────

    private static readonly Guid AssigneeContact = Guid.Parse("c0430000-0000-4000-8000-000000000090");
    private static readonly Guid AssigneeUser = Guid.Parse("c0430000-0000-4000-8000-000000000091");
    private static readonly Guid OtherUser = Guid.Parse("c0430000-0000-4000-8000-000000000092");
    private static readonly Guid FilingMatter = Guid.Parse("c0430000-0000-4000-8000-000000000093");
    private static readonly Guid FilingDocument = Guid.Parse("c0430000-0000-4000-8000-000000000096");
    private static readonly Guid FilingServiceRequest = Guid.Parse("c0430000-0000-4000-8000-000000000095");

    /// <summary>The REAL resolver over: a personal To Do (user-owned, filed under nothing unless <paramref name="filed"/>) in the
    /// CHILD unit, so its own answer is ChildTeam; two users in the GENERAL unit (GeneralTeam). A no-core Signal about the To Do, whose
    /// Assigned To is <see cref="AssigneeContact"/> when <paramref name="assigned"/>. Which users link to that contact is per test.</summary>
    private static Rig PersonalToDoRig(bool assigned = true, bool filed = false, bool filedUnderServiceRequest = false, bool filedUnderDocument = false, bool assigneeHasBusinessUnit = true,
        Dictionary<string, object>? regardingOverride = null)
    {
        var regarding = filed ? new Dictionary<string, object> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", FilingMatter) }
            : filedUnderServiceRequest ? new Dictionary<string, object> { ["sprk_regardingservicerequest"] = new EntityReference("sprk_servicerequest", FilingServiceRequest) }
            : filedUnderDocument ? new Dictionary<string, object> { ["sprk_regardingdocument"] = new EntityReference("sprk_document", FilingDocument) }
            : regardingOverride;
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithUser(AssigneeUser, Guid.NewGuid(), assigneeHasBusinessUnit ? Tests.TestInfrastructure.OwnershipDirectory.GeneralBu : Guid.Empty)
            .WithUser(OtherUser, Guid.NewGuid(), Tests.TestInfrastructure.OwnershipDirectory.GeneralBu)
            .WithOrdinaryRoot("sprk_matter", FilingMatter)
            .WithOrdinaryRoot("sprk_servicerequest", FilingServiceRequest)
            .WithRecord("sprk_document", FilingDocument, Tests.TestInfrastructure.OwnershipDirectory.ChildBu)
            .WithRecord("sprk_todo", ItemTodo, Tests.TestInfrastructure.OwnershipDirectory.ChildBu, extra: regarding);
        var rig = new Rig { RealResolver = directory.Resolver() };
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var todo = new Entity("sprk_todo", ItemTodo);
        if (assigned)
            todo["sprk_assignedto"] = new EntityReference("contact", AssigneeContact);
        foreach (var (column, value) in regarding ?? new())
            todo[column] = value;
        rig.TodoRows[ItemTodo] = todo;
        return rig;
    }

    private static AssignedLinkCandidate LinkedTo(
        Guid user, bool disabled = false, Guid? applicationId = null, int accessMode = 0, bool external = false) =>
        new(user, AssigneeContact, Guid.NewGuid(), IsDisabled: disabled, AccessMode: accessMode, ApplicationId: applicationId, IsExternal: external);

    [Fact]
    public async Task D134_APersonalToDo_WhoseAssignedToIsOneEnabledUserInAnotherUnit_IsOwnedByThatUnitsTeam()
    {
        var rig = PersonalToDoRig();
        rig.Links.UsersByLink[AssigneeContact] = [LinkedTo(AssigneeUser)];

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var owner = rig.Record.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.GeneralTeam, "the Assigned To user's business-unit team, not the To Do's own (ChildTeam)");
        rig.OwnerAsks.Should().HaveCount(2);
        rig.OwnerAsks[0].Parents.Should().Equal([new RecordOwnershipParent("sprk_todo", ItemTodo)], "the parents are resolved first (item 7)");
        rig.OwnerAsks[1].Parents.Should().BeEmpty();
        rig.OwnerAsks[1].CallerSystemUserId.Should().Be(AssigneeUser, "then the assignee's team, through ForUserTeam");
        rig.Record.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Id.Should().Be(CallerContactId, "the decider is unchanged");
    }

    [Theory]
    [InlineData("no-assigned-to")]
    [InlineData("no-systemuser")]
    [InlineData("two-systemusers")]
    [InlineData("disabled-only")]
    [InlineData("todo-filed")]
    [InlineData("todo-filed-under-service-request")]
    [InlineData("todo-filed-under-non-core-parent")]
    [InlineData("todo-regarding-organization")]
    [InlineData("todo-regarding-contact")]
    [InlineData("todo-pair-only")]
    [InlineData("application-user-only")]
    [InlineData("non-interactive-only")]
    [InlineData("external-flagged-only")]
    public async Task D134_NoSingleEnabledAssignee_FallsBackToTheTodosOwnTeam(string fallback)
    {
        var rig = PersonalToDoRig(assigned: fallback != "no-assigned-to", filed: fallback == "todo-filed",
            filedUnderServiceRequest: fallback == "todo-filed-under-service-request",
            filedUnderDocument: fallback == "todo-filed-under-non-core-parent",
            regardingOverride: fallback switch
            {
                // identity parents (not lineage) and the ADR-024 pair alone: all FILED, so the assignee never wins
                "todo-regarding-organization" => new() { ["sprk_regardingorganization"] = new EntityReference("sprk_organization", Guid.NewGuid()) },
                "todo-regarding-contact" => new() { ["sprk_regardingcontact"] = new EntityReference("contact", Guid.NewGuid()) },
                "todo-pair-only" => new() { ["sprk_regardingrecordtype"] = "sprk_document", ["sprk_regardingrecordid"] = FilingDocument.ToString() },
                _ => null,
            });   // owner-confirmed K2: only a To Do filed under NOTHING is personal
        rig.Links.UsersByLink[AssigneeContact] = fallback switch
        {
            "no-systemuser" => [],
            "two-systemusers" => [LinkedTo(AssigneeUser), LinkedTo(OtherUser)],
            "disabled-only" => [LinkedTo(AssigneeUser, disabled: true)],
            "application-user-only" => [LinkedTo(AssigneeUser, applicationId: Guid.NewGuid())],
            "non-interactive-only" => [LinkedTo(AssigneeUser, accessMode: 4)],
            "external-flagged-only" => [LinkedTo(AssigneeUser, external: true)],
            _ => [LinkedTo(AssigneeUser)],   // a To Do with no Assigned To, or one filed under a matter or a service request, never reaches its user
        };

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.ChildTeam,
            "no single enabled user: the parents' answer (the To Do's own unit's team, or for a filed To Do its matter's) stands");
        rig.OwnerAsks.Should().ContainSingle("the assignee's team is never asked for");
    }

    [Fact]
    public async Task D134_APersonAndAnApplicationUserLinkedToTheSameContact_IsThePerson()
    {
        var rig = PersonalToDoRig();
        rig.Links.UsersByLink[AssigneeContact] = [LinkedTo(AssigneeUser), LinkedTo(OtherUser, applicationId: Guid.NewGuid())];

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.GeneralTeam,
            "an application user never counts toward \"exactly one\": the person is the assignee");
        rig.OwnerAsks[1].CallerSystemUserId.Should().Be(AssigneeUser);
    }

    [Fact]
    public async Task D134_TheAssigneesTeamRefused_AssigneeWithNoBusinessUnit_Is422_AndNothingIsWritten()
    {
        var rig = PersonalToDoRig(assigneeHasBusinessUnit: false);
        rig.Links.UsersByLink[AssigneeContact] = [LinkedTo(AssigneeUser)];

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Refused, result.Detail);
        rig.OwnerAsks.Should().HaveCount(2, "the parents' answer was ordinary; the ForUserTeam question is the one refused");
        rig.Writes.Should().BeEmpty("refused before the first write");
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.Status(DoSignal).Should().Be(Open);
    }

    [Fact]
    public async Task D134_AResolverFaultOnTheAssigneesTeam_Is503_AndNothingIsWritten()
    {
        var rig = PersonalToDoRig();
        rig.Links.UsersByLink[AssigneeContact] = [LinkedTo(AssigneeUser)];
        rig.RealResolver = new FaultsOnUserTeamQuestion(rig.RealResolver!);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, "a resolver fault is not a refusal");
        result.ReasonCode.Should().Be(DecisionCommitReasons.ItemOwnerUnsupported);
        rig.OwnerAsks.Should().HaveCount(2);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.Status(DoSignal).Should().Be(Open);
    }

    /// <summary>The real resolver, except that the ForUserTeam question (no parents) throws instead of answering.</summary>
    private sealed class FaultsOnUserTeamQuestion(IRecordOwnershipResolver inner) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) =>
            context.Parents.Count == 0 ? throw new TimeoutException("no answer") : inner.ResolveOwningTeamAsync(context, ct);

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct) =>
            context.Parents.Count == 0 ? throw new TimeoutException("no answer") : inner.ResolveOwnerAsync(context, ct);

        public Task<RecordOwnerResolution> ReparentAsync(RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(request, applyChange, ct);
    }

    [Theory]
    [InlineData("todo")]
    [InlineData("systemuser")]
    public async Task D134_AFaultReadingTheAssignee_Is503_AndNothingIsWritten(string read)
    {
        var rig = PersonalToDoRig();
        rig.Links.UsersByLink[AssigneeContact] = [LinkedTo(AssigneeUser)];
        if (read == "todo")
            rig.TodoReadFault = new TimeoutException("no answer");
        else
            rig.Links.FailLinkRead = true;

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, "a fault is not \"no single user\": the owner is unknown");
        result.ReasonCode.Should().Be(DecisionCommitReasons.ItemOwnerUnsupported);
        rig.Writes.Should().BeEmpty("refused before the first write");
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.Status(DoSignal).Should().Be(Open);
    }

    [Fact]
    public async Task D134_AnAssigneeReadFault_DecidesNothingWhenAParentIsSecure_TheSecureAnswerStands_Item7()
    {
        var project = Guid.Parse("c0430000-0000-4000-8000-000000000094");
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithSecureRoot("sprk_project", project)
            .WithRecord("sprk_todo", ItemTodo, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", project) });
        var rig = new Rig { RealResolver = directory.Resolver() };
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.TodoReadFault = new TimeoutException("no answer");

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    // ── D-129: the deciding individual is the caller's linked contact, never an owner ────────────────────────────────────

    [Fact]
    public async Task TheDecidersLinkedContact_IsAssignedToInternal_AndConfirmedByStaysTheCallersSystemUser()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var assigned = rig.Record.GetAttributeValue<EntityReference>("sprk_assignedtointernal");
        assigned.LogicalName.Should().Be("contact");
        assigned.Id.Should().Be(CallerContactId);
        rig.Record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Caller);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").LogicalName.Should().Be("team", "no person is an owner; the decider is Assigned To Internal");
    }

    [Fact]
    public async Task ACallerWithNoLinkedContact_CommitsWithAssignedToInternalEmpty()
    {
        var rig = new Rig { CallerContact = null };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.Contains("sprk_assignedtointernal").Should().BeFalse();
        rig.Record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Caller);
    }

    [Fact]
    public async Task AContactReadThatFaults_Is503_AndNothingIsWritten()
    {
        // The real IdentityNormalizationService never throws: a failed systemuser / contact-binding read comes back as
        // ContactId null + Faults. The Decision Record is append-only, so it must not be written without its decider.
        var rig = new Rig { IdentityFaults = IdentityReadFaults.SystemUser };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Writes.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        rig.Log.Should().NotContain(l => l.StartsWith("record:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AContactBindingReadThatFaults_Is503_AndNothingIsWritten()
    {
        var rig = new Rig { IdentityFaults = IdentityReadFaults.ContactBinding };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnIdentityServiceThatThrows_Is503_AndNothingIsWritten()
    {
        var rig = new Rig { IdentityFault = new TimeoutException("no answer") };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnrelatedIdentityFault_WithAContactResolved_StillCommitsWithTheDecider()
    {
        // A Teams / Account fault leaves the contact answer intact: only an UNREADABLE contact refuses.
        var rig = new Rig { IdentityFaults = IdentityReadFaults.Teams };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Id.Should().Be(CallerContactId);
    }

    // ── D-128 Q18: send-reminder does not resolve the item ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, JsonElement> ReminderValues =
        Values(("to", "client@example.test"), ("subject", "Reminder"), ("body", "Please send the response"));

    private static Rig ReminderRig()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, ReminderVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        return rig;
    }

    [Fact]
    public async Task Q18_TheReminderAlone_LeavesTheItemOpen_AcknowledgedAndLinked_NotResolved()
    {
        var rig = ReminderRig();

        var result = await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("send-reminder", ReminderValues), Skip("mark-complete")] }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Records.Should().ContainSingle("the reminder's Decision Record is still written");
        rig.Status(DoSignal).Should().Be(SignalWriter.SignalStatusAcknowledged, "the item stays open until the work is done or its due date moves");
        rig.Resolution(DoSignal).Should().BeNull();
        rig.LinkedRecord(DoSignal).Should().Be(RecordId);
        result.Signals.Single().State.Should().Be("InquirySent");
    }

    [Fact]
    public async Task Q18_ALaterActedDecision_ClosesTheAcknowledgedItem()
    {
        var rig = ReminderRig();
        await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("send-reminder", ReminderValues), Skip("mark-complete")] }, DoSignal);
        rig.Status(DoSignal).Should().Be(SignalWriter.SignalStatusAcknowledged);
        rig.Records.Clear();

        var later = await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Skip("send-reminder"), Take("mark-complete")] }, DoSignal);

        later.Status.Should().Be(DecisionCommitStatus.Recorded, later.Detail);
        rig.Status(DoSignal).Should().Be(Resolved);
        rig.Resolution(DoSignal).Should().Be(SignalWriter.ResolutionActed);
    }

    [Fact]
    public async Task Q18_AReminderTakenWithAResolvingAction_StillResolvesTheItem()
    {
        var rig = ReminderRig();

        await rig.CommitAsync(
            new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("send-reminder", ReminderValues), Take("mark-complete")] }, DoSignal);

        rig.Status(DoSignal).Should().Be(Resolved);
        rig.Resolution(DoSignal).Should().Be(SignalWriter.ResolutionActed);
    }

    // ── Step 6 against a closure during the review (032 review) ──────────────────────────────────────────────────

    [Fact]
    public async Task ASignalTheSystemClosedDuringTheReview_IsReclosedWithThisDecision_SoSignalAndRecordAgree()
    {
        var rig = new Rig();
        rig.BeforeSignalUpdate = id =>
        {
            if (rig.Status(id) != Open) return;
            rig.BeforeSignalUpdate = null;
            // A Classification trigger closes it ConditionCleared between the read and the update.
            rig.Signals[id]["sprk_signalstatus"] = new OptionSetValue(Resolved);
            rig.Signals[id]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
            rig.Signals[id]["sprk_resolvedon"] = Now.UtcDateTime;
            rig.Versions[id]++;
        };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed, "re-closed as the commit's resolution");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        result.Signals.Single().Warning.Should().Be("reclosed_after_system_resolution");
    }

    [Fact]
    public async Task ASignalAPersonResolvedDuringTheReview_IsLeftAlone_AndReported()
    {
        var rig = new Rig();
        var other = Guid.NewGuid();
        rig.BeforeSignalUpdate = id =>
        {
            rig.BeforeSignalUpdate = null;
            rig.Signals[id]["sprk_signalstatus"] = new OptionSetValue(Resolved);
            rig.Signals[id]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionDismissed);
            rig.Signals[id]["sprk_resolvedon"] = Now.UtcDateTime;
            rig.Signals[id]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", other);
            rig.Versions[id]++;
        };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionDismissed, "another human decision is never overwritten");
        rig.LinkedRecord(Primary).Should().Be(other);
        result.Signals.Single().Warning.Should().Be("resolved_by_another_decision");
        result.Warnings.Should().Contain("resolved_by_another_decision");
    }

    [Fact]
    public async Task ASignalTheSystemClosedBeforeTheReviewStarted_IsAConflict()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddHours(-2));

        await rig.StampPlanRead(Primary, Now.AddMinutes(-5));

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ARetryOfAFailedReview_AfterTheSystemClosedTheSignal_SkipsWhatLanded_AndRecloses()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);

        var failed = await rig.CommitAsync(review);
        failed.Resumable.Should().BeTrue();

        // The revision landed; its trigger (or the nightly pass) closes the Signal ConditionCleared before the retry.
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Signals[Primary]["sprk_signalstatus"] = new OptionSetValue(Resolved);
        rig.Signals[Primary]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
        rig.Signals[Primary]["sprk_resolvedon"] = rig.Clock.GetUtcNow().UtcDateTime;
        rig.Versions[Primary]++;
        rig.Executors["add-todo"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1, "a landed step is not run again");
        rig.Log.Count(l => l == "execute:add-todo").Should().Be(2);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        Steps(rig.Record).GetProperty("steps")[1].GetProperty("written").EnumerateArray()
            .Select(w => w.GetProperty("entity").GetString()).Should().Equal("sprk_budgetrevision", "sprk_budget");
    }

    [Fact]
    public async Task AStepThatFailedAfterWritingPartOfItsWork_IsNotResumable_SoARetryCannotWriteItTwice()
    {
        var rig = new Rig();
        rig.Executors["revise-budget"].Result = DecisionActionOutcome.Failed("revise-budget", DecisionActionReasons.BudgetAmountNotWritten,
            "The amount was not written", written: [new DecisionRecordRef("sprk_budgetrevision", RevisionId)]);
        var review = PathBReview(inquiry: false);

        var failed = await rig.CommitAsync(review);
        rig.Executors["revise-budget"].Result = null;
        var retried = await rig.CommitAsync(review with { Retry = true });

        failed.Resumable.Should().BeFalse();
        failed.Landed.Should().Equal(new DecisionCommitRef("sprk_budgetrevision", RevisionId));
        retried.ReasonCode.Should().Be(DecisionCommitReasons.NotResumable);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1, "the revision is never written twice");
    }

    [Fact]
    public async Task ARetryThatChangesTheReview_IsRefused()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        await rig.CommitAsync(review);

        var changed = await rig.CommitAsync(review with { Retry = true, Because = "something else" });

        changed.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1);
    }

    // ── Review fixes: a resume never forgets what landed; one review start; no cancellation after the first write ──────

    [Fact]
    public async Task ARetryThatIsRefused_KeepsTheStoredFailure_SoTheNextRetryStillSkipsWhatLanded()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        (await rig.CommitAsync(review)).Resumable.Should().BeTrue();

        // The retry is refused before any write (the To Do's target is not writable right now).
        rig.Executors["add-todo"].Result = null;
        rig.Executors["add-todo"].Refusal = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.NotAuthorized, "no");
        var refused = await rig.CommitAsync(review with { Retry = true });
        refused.Status.Should().Be(DecisionCommitStatus.Forbidden);

        // The cause is fixed; the next retry resumes the SAME failure.
        rig.Executors["add-todo"].Refusal = null;
        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1, "the revision that landed on the first attempt is never written again");
    }

    [Fact]
    public async Task ARetry_JudgesASystemClosure_AgainstTheReviewsOwnStart_NotTheFirstAttempt()
    {
        var rig = new Rig();
        // The wizard read the plan ten minutes ago; the system closed the Signal five minutes ago (after the read).
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        await rig.StampPlanRead(Primary, Now.AddMinutes(-10)); // the server served the plan ten minutes ago
        var review = PathBReview(inquiry: false);

        var failed = await rig.CommitAsync(review);
        failed.Resumable.Should().BeTrue(failed.Detail);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Executors["add-todo"].Result = null;
        // The plan is read again now: only the STORED review start can still place the closure after it.
        await rig.StampPlanRead(Primary, rig.Clock.GetUtcNow());
        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, "the closure came after the review started, so it is re-closed, not a conflict");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task ACancelledRequest_AfterTheFirstWrite_StillFinishesTheCommit_AndWritesTheRecord()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Executors["revise-budget"].OnExecute = cts.Cancel; // the client disconnects once a write has landed

        var result = await rig.CommitAsync(PathBReview(), ct: cts.Token);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Records.Should().ContainSingle();
        rig.Status(Primary).Should().Be(Resolved);
        rig.Executors.Values.SelectMany(e => e.ExecuteTokens).Should().OnlyContain(t => !t.CanBeCanceled, "the writes never run on the request's token");
    }

    [Fact]
    public async Task ACancelledRequest_BeforeAnyWrite_ReleasesTheReviewId()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Executors["add-todo"].OnPreflight = cts.Cancel;
        var review = PathBReview();

        var cancelled = () => rig.CommitAsync(review, ct: cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        rig.Writes.Should().BeEmpty();

        rig.Executors["add-todo"].OnPreflight = null;
        var again = await rig.CommitAsync(review);
        again.Status.Should().Be(DecisionCommitStatus.Recorded, "the review id is not left marked in progress");
    }

    [Fact]
    public async Task ANotificationThatNamedNoRow_IsStillRecorded_AsAnUnnamedNextStep_NotRefusedAsADismissal()
    {
        var rig = new Rig();
        rig.Executors["send-email"].Result = DecisionActionOutcome.Done("send-email");
        var review = PathBReview(inquiry: false, revise: false, todo: false) with
        {
            NextSteps = [new DecisionReviewNextStep { Code = "send-email", Values = InquiryValues }],
        };

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.SentButNotRecorded.Should().BeFalse();
        result.RecordClass.Should().Be("Judgement");
        var followOn = JsonDocument.Parse(rig.Record.GetAttributeValue<string>("sprk_followons")).RootElement.EnumerateArray().Single();
        followOn.GetProperty("actionCode").GetString().Should().Be("send-email");
        followOn.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null, "a follow-on without an id, never dropped");
    }

    // ── FR-51: the caller writes the subject and appends to the matter ───────────────────────────────────────────

    [Fact]
    public async Task ACallerWhoCannotAppendToTheMatter_IsForbidden_EvenForADismissal_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.Rights[("sprk_matters", Matter)] = AccessRights.Read | AccessRights.Write;

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.Writes.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task ACallerWhoCannotWriteTheSubject_IsForbidden_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Rights[("sprk_todos", ItemTodo)] = AccessRights.Read | AccessRights.AppendTo;
        var review = new DecisionReview
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("mark-complete"), Skip("reschedule")],
        };

        var result = await rig.CommitAsync(review, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.RightsAsked.Should().Contain(("sprk_todos", ItemTodo), "asked on the subject's own table, as the caller");
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASignalWhoseSubjectTableCannotBeDerived_IsForbidden_LikeAnUnreadableSubject_BeforeAnyWrite()
    {
        // Task 141 review F3: the pair id is set but no subject lookup carries it (the sprk_signal_reg* relationships cascade a
        // subject delete as RemoveLink, which clears the lookup and leaves the pair id). An unknown subject is never "no subject".
        // Task 150 (D-141): on a Decide Signal. A Do Signal whose subject cannot be derived has no "mine" and is now the uniform 404 at
        // the access check (ADoSignalWhoseSubjectCannotBeDerived_IsTheUniform404_D141), before this refusal is reached.
        var rig = new Rig();
        rig.Signals[Primary]["sprk_regardingrecordid"] = ItemTodo.ToString("D"); // the pair id, with no subject lookup carrying it
        var review = PathBReview(inquiry: false, todo: false);

        var result = await rig.CommitAsync(review);

        // Task 141 fix round 2 (K2): the same refusal (Forbidden, fail closed), with its own code and message.
        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalSubjectMissing);
        result.ReasonCode.Should().Be("ontology.decision.signal_subject_missing");
        result.Detail.Should().Be("This Signal's subject no longer exists.");
        rig.Writes.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    // ── Task 150 (D-141): Do items are private to their assignee ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task D141_ASameBuColleagueWhoIsNotTheDoItemsAssignee_GetsExactlyTheUnreadableSignalAnswer_AndNothingIsWritten(bool hasCore)
    {
        var notMine = new Rig();
        notMine.AddSignal(DoSignal, DecisionLane.Do, hasCore ? Matter : null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        notMine.CallerNotMine.Add(ItemTodo);   // the caller reads the Signal, the matter and the To Do, but it is not their work
        var unreadable = new Rig();
        unreadable.AddSignal(DoSignal, DecisionLane.Do, hasCore ? Matter : null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        unreadable.CallerCannotRead.Add(DoSignal);
        var review = MarkComplete();

        var denied = await notMine.CommitAsync(review, DoSignal);
        var baseline = await unreadable.CommitAsync(review, DoSignal);

        denied.Status.Should().Be(DecisionCommitStatus.NotFound);
        denied.Should().BeEquivalentTo(baseline);
        notMine.MineAsked.Should().ContainSingle("the worklist's own mine read, as the caller");
        notMine.Writes.Should().BeEmpty();
        notMine.Status(DoSignal).Should().Be(Open);
    }

    [Fact]
    public async Task D141_ANotMineDoItemInAlsoResolve_RefusesTheWholeReview_AsAnUnreadableOneDoes()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.CallerNotMine.Add(ItemTodo);
        var review = PathBReview(inquiry: false, todo: false) with { AlsoResolve = [Second] };

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.Writes.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task D141_TheAssignee_StillCommits()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.MineAsked.Should().ContainSingle();
    }

    [Fact]
    public async Task D141_AFaultInTheMineRead_IsUnavailable_AndNothingIsWritten()
    {
        var rig = new Rig { MineReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom") };
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task D141_ARepeatOfARecordedReview_AfterTheDecisionReassignedTheItem_ReturnsTheFirstOutcome()
    {
        // Adversarial review F1: Reassign moves the To Do to someone else; the wizard resends the same review id after a lost
        // response. The caller passed the "mine" check on the first attempt; the repeat returns that outcome, never a 404.
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        var review = MarkComplete();
        (await rig.CommitAsync(review, DoSignal)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.CallerNotMine.Add(ItemTodo);   // the item is no longer the caller's work

        var repeat = await rig.CommitAsync(review, DoSignal);

        repeat.Status.Should().Be(DecisionCommitStatus.Recorded, repeat.Detail);
        repeat.Replayed.Should().BeTrue();
    }

    /// <summary>A Do review whose own <c>reassign</c> moves the To Do away from the caller (the fake executor makes it not-mine as
    /// it lands) and then adds a To Do, which a test makes fail so the review is stored Failed and resumable.</summary>
    private static DecisionReview ReassignThenAddTodo() =>
        new()
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("reassign", Values(("assignee", Assignee.ToString("D")))), Skip("mark-complete")],
            NextSteps = [new DecisionReviewNextStep { Code = "add-todo", Values = TodoValues }],
        };

    private static Rig ReassignRig()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, ReassignVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Executors["reassign"].OnExecute = () => rig.CallerNotMine.Add(ItemTodo);   // the review's own reassign moves the item away
        return rig;
    }

    [Fact]
    public async Task D141_AResumeOfTheCallersFailedReview_WhoseOwnReassignLanded_StillRuns_WithoutAskingMineAgain()
    {
        // Adversarial review F1 (pass 1): the review's own landed reassign is what made the item not-mine; its resume is not judged by it.
        var rig = ReassignRig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = ReassignThenAddTodo();
        var failed = await rig.CommitAsync(review, DoSignal);
        failed.Status.Should().Be(DecisionCommitStatus.Failed, failed.Detail);
        failed.Resumable.Should().BeTrue();
        failed.Steps.Should().Contain(s => s.Code == "reassign" && s.Status == "Done");
        rig.CallerNotMine.Should().Contain(ItemTodo, "the reassign landed");
        rig.Executors["add-todo"].Result = null;

        var resumed = await rig.CommitAsync(review with { Retry = true }, DoSignal);

        resumed.Status.Should().Be(DecisionCommitStatus.Recorded, resumed.Detail);
        rig.Log.Count(l => l == "execute:reassign").Should().Be(1, "a landed step is not run again");
        rig.MineAsked.Should().ContainSingle("the check ran on the first attempt only");
    }

    [Fact]
    public async Task D141_ARepairReplayOfARecordedReview_WhoseOwnReassignLanded_StillCloses()
    {
        var rig = ReassignRig();
        FirstCloseFails(rig);
        var review = ReassignThenAddTodo();
        (await rig.CommitAsync(review, DoSignal)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(DoSignal).Should().Be(Open, "the close failed after the record");

        var replay = await rig.CommitAsync(review, DoSignal);

        replay.Status.Should().Be(DecisionCommitStatus.Recorded, replay.Detail);
        replay.Replayed.Should().BeTrue();
        rig.Status(DoSignal).Should().Be(Resolved, "the repair finished the close the first run did not");
        rig.MineAsked.Should().ContainSingle("the check ran on the first attempt only");
    }

    [Fact]
    public async Task D141_ARetryOfTheCallersFailedReview_AfterAnOutsideReassign_IsTheUniform404_AndWritesNothing()
    {
        // Adversarial review F (pass 2): Bob's mark-complete failed transiently and is stored resumable; a manager then reassigns the
        // To Do to Alice, outside the decision. Bob's Retry must not complete Alice's item as Bob.
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Executors["mark-complete"].Result = DecisionActionOutcome.Refused("mark-complete", DecisionActionReasons.WriteFailed, "no");
        var review = MarkComplete();
        var failed = await rig.CommitAsync(review, DoSignal);
        failed.Status.Should().Be(DecisionCommitStatus.Failed, failed.Detail);
        failed.Resumable.Should().BeTrue();
        failed.Steps.Should().NotContain(s => s.Code == "reassign", "no reassign of this review landed: the item moved outside the decision");
        var writes = rig.Writes.ToList();
        rig.CallerNotMine.Add(ItemTodo);
        rig.Executors["mark-complete"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true }, DoSignal);

        retried.Status.Should().Be(DecisionCommitStatus.NotFound);
        retried.Should().BeEquivalentTo(new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = review.ReviewId });
        rig.Writes.Should().Equal(writes, "nothing is written for an item that is no longer the caller's");
        rig.Status(DoSignal).Should().Be(Open);

        // The refusal wrote no marker: if the item comes back to the caller, the same failure still resumes.
        rig.CallerNotMine.Remove(ItemTodo);
        var back = await rig.CommitAsync(review with { Retry = true }, DoSignal);
        back.Status.Should().Be(DecisionCommitStatus.Recorded, back.Detail);
    }

    [Fact]
    public async Task D141_ARepairReplayOfTheCallersRecordedReview_AfterAnOutsideReassign_IsTheUniform404_AndWritesNothing()
    {
        // The same hole through the repair: the record landed, the close did not; the item then moves outside the decision.
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        FirstCloseFails(rig);
        var review = MarkComplete();
        var first = await rig.CommitAsync(review, DoSignal);
        first.Status.Should().Be(DecisionCommitStatus.Recorded, first.Detail);
        rig.Status(DoSignal).Should().Be(Open, "the close failed after the record, so the repair has a close to finish");
        var writes = rig.Writes.ToList();
        var closes = rig.SignalUpdates.Count;
        rig.CallerNotMine.Add(ItemTodo);

        var replay = await rig.CommitAsync(review, DoSignal);

        replay.Status.Should().Be(DecisionCommitStatus.NotFound);
        replay.Should().BeEquivalentTo(new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = review.ReviewId });
        rig.Writes.Should().Equal(writes, "the repair closes nothing for an item that is no longer the caller's");
        rig.SignalUpdates.Should().HaveCount(closes);
        rig.Status(DoSignal).Should().Be(Open);

        // And it stays a repair for the caller who still owns the item.
        rig.CallerNotMine.Remove(ItemTodo);
        var repaired = await rig.CommitAsync(review, DoSignal);
        repaired.Status.Should().Be(DecisionCommitStatus.Recorded, repaired.Detail);
        repaired.Replayed.Should().BeTrue();
        rig.Status(DoSignal).Should().Be(Resolved);
    }

    [Fact]
    public async Task D141_AReviewIdTheCallerUsedBefore_OnANotMineItem_IsTheUniform404_NotReviewChanged()
    {
        // Same review id, other content: never the same review, so the "mine" check runs before the ledger can answer.
        var rig = new Rig();
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.CallerNotMine.Add(ItemTodo);

        var reused = await rig.CommitAsync(MarkComplete() with { ReviewId = review.ReviewId }, DoSignal);

        reused.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.Status(DoSignal).Should().Be(Open);
    }

    [Fact]
    public async Task ADoSignalWhoseSubjectCannotBeDerived_IsTheUniform404_D141()
    {
        // Task 141 review F3's case on a Do Signal: no subject, so no "mine"; the worklist shows it to nobody.
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: null, subjectId: ItemTodo);

        var result = await rig.CommitAsync(MarkComplete(), DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.Writes.Should().BeEmpty();
        rig.Status(DoSignal).Should().Be(Open);
    }

    [Fact]
    public async Task K2_ARecordedReplay_WhoseSignalSubjectNoLongerExists_SaysSo_AndStillClosesNothing()
    {
        // The replay-repair asks FR-51 again; a Signal whose subject was deleted since the first run (its lookup cleared, the pair
        // id left) is refused as before, now with the subject-missing code instead of signal_close_not_authorized.
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Signals[Primary]["sprk_regardingrecordid"] = ItemTodo.ToString("D"); // the subject lookup is not set: it was cleared

        var replay = await rig.CommitAsync(review);

        replay.Status.Should().Be(DecisionCommitStatus.Recorded);
        replay.Replayed.Should().BeTrue();
        replay.Signals.Single().Should().Be(new DecisionCommitSignal(Primary, "Unchanged", "signal_subject_missing"));
        replay.Warnings.Should().Contain("signal_subject_missing").And.NotContain("signal_close_not_authorized");
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public void TheDecisionRecordOwnerParents_RefuseASignalWhoseSubjectTableCannotBeDerived_NeverDropIt()
    {
        // Task 141 review F3 (uac-r2 F1): the record's owner is secure-if-any over every resolved Signal's subject. An unknown
        // subject dropped from that set could hide a secure parent, so it refuses, as an unreadable parent does.
        var known = DecisionCommitService.ToRow(Guid.NewGuid(), new Entity("sprk_signal")
        {
            ["sprk_regardingrecordid"] = ItemTodo.ToString("D"),
            ["sprk_regardingtodo"] = new EntityReference("sprk_todo", ItemTodo),
        });
        var noSubject = DecisionCommitService.ToRow(Guid.NewGuid(), new Entity("sprk_signal"));
        var unknown = DecisionCommitService.ToRow(Guid.NewGuid(), new Entity("sprk_signal")
        {
            ["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D"), // its lookup was cleared
        });

        DecisionCommitService.SignalSubjectsOf([known, noSubject, known]).Should().Equal(new DecisionRecordRef("sprk_todo", ItemTodo));

        var act = () => DecisionCommitService.SignalSubjectsOf([known, unknown]);

        var refused = act.Should().Throw<DecisionRecordRefusedException>().Which;
        refused.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused, "the commit route maps it to Refused");
        refused.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Fact]
    public async Task ASubjectWhoseTableCannotBeLookedUp_IsUnavailable_BeforeAnyWrite()
    {
        var rig = new Rig();
        // Task 141: the subject table now comes from the Signal's subject lookup, so the table is one a Signal can carry.
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_event", subjectId: ItemTodo);
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no metadata"));
        var review = new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("mark-complete"), Skip("reschedule")] };

        var result = await rig.CommitAsync(review, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, "a metadata fault is not an answer about the caller; it still fails closed");
        rig.Writes.Should().BeEmpty();
        rig.RightsAsked.Should().NotContain(r => r.Id == ItemTodo);
    }

    [Fact]
    public async Task AReadFaultBeforeAnyWrite_IsUnavailable_AndFreesTheReviewId()
    {
        var rig = new Rig();
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));
        var review = PathBReview();

        var faulted = await rig.CommitAsync(review);

        faulted.Status.Should().Be(DecisionCommitStatus.Unavailable, "nothing was written, so it is not a stored failure");
        rig.Writes.Should().BeEmpty();
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity("sprk_policyversion", id) { ["sprk_decisionplan"] = PathBPlan });
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded, "the same review id runs once the read recovers");
    }

    [Fact]
    public async Task AReadFaultDuringAResume_PutsTheResumedFailureBack()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        (await rig.CommitAsync(review)).Resumable.Should().BeTrue();
        rig.Executors["add-todo"].Result = null;

        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));
        (await rig.CommitAsync(review with { Retry = true })).Status.Should().Be(DecisionCommitStatus.Unavailable);

        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity("sprk_policyversion", id) { ["sprk_decisionplan"] = PathBPlan });
        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task AlsoResolve_IsCapped()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview(also: Enumerable.Range(0, 26).Select(_ => Guid.NewGuid()).ToList()));

        result.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInvalid);
        rig.Writes.Should().BeEmpty();
    }

    // ── Coordinator round (review at e48399548) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASecondConcurrentCommitOfTheSameReview_IsInProgress_AndWritesNothing()
    {
        var rig = new Rig();
        var review = PathBReview();
        // Another instance holds the review's lock (taken before the ledger read).
        var held = await rig.Lease.TryAcquireAsync(DecisionCommitService.LeaseId(Tenant, CallerOid.ToString("D"), review.ReviewId), null, TimeSpan.FromMinutes(1), CancellationToken.None);
        held.Status.Should().Be(ScheduledJobLeaseStatus.Granted);

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.InProgress);
        rig.Writes.Should().BeEmpty();
        await rig.Lease.ReleaseAsync(DecisionCommitService.LeaseId(Tenant, CallerOid.ToString("D"), review.ReviewId), held.Token!, CancellationToken.None);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded, "the lock is released, not leaked");
    }

    [Fact]
    public async Task ALockStoreThatCannotBeReached_FailsClosed()
    {
        var rig = new Rig();
        var lease = new Mock<IScheduledJobLease>();
        lease.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScheduledJobLeaseUnavailableException("redis down"));
        rig.Lease = lease.Object;

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRecordOwner_IsResolvedBeforeTheFirstWrite_AndARefusalWritesNothing()
    {
        var ok = new Rig();
        await ok.CommitAsync(PathBReview());
        ok.Log.IndexOf("owner").Should().BeLessThan(ok.Log.FindIndex(l => l.StartsWith("execute:", StringComparison.Ordinal)), "step 1, before any write");
        ok.Log.Count(l => l == "owner").Should().Be(1, "resolved once and handed to the write");

        var refused = new Rig { Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };
        var result = await refused.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.RecordRefused);
        refused.Writes.Should().BeEmpty("the inquiry never went out");
    }

    [Fact]
    public async Task ARecordedReplay_ClosesASignalTheFirstRunCouldNotClose()
    {
        var rig = new Rig();
        rig.BeforeSignalUpdate = _ =>
        {
            rig.BeforeSignalUpdate = null;
            throw new TimeoutException("no answer"); // the close fails once, after the record
        };
        var review = PathBReview(inquiry: false, todo: false);

        var first = await rig.CommitAsync(review);
        first.Status.Should().Be(DecisionCommitStatus.Recorded);
        first.Signals.Single().Warning.Should().Be("signal_close_failed");
        rig.Status(Primary).Should().Be(Open);

        var replay = await rig.CommitAsync(review);

        replay.Replayed.Should().BeTrue();
        rig.Records.Should().ContainSingle("the record is not written again");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        replay.Signals.Single().Warning.Should().BeNull();
        (await rig.CommitAsync(review)).Signals.Single().Warning.Should().BeNull("the stored outcome was updated");
    }

    [Fact]
    public async Task TheSameReviewIdForADifferentDecision_OrADifferentSignal_IsReviewChanged()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);

        var changed = await rig.CommitAsync(review with { Because = "something else" });
        var otherSignal = await rig.CommitAsync(review, Second);

        changed.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged);
        otherSignal.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged);
        rig.Status(Second).Should().Be(Open);
    }

    [Fact]
    public async Task ACorePairThatChangedBetweenTheCallersReadAndTheAppOnlyRead_IsRefused()
    {
        var rig = new Rig();
        rig.CallerSeesCore[Primary] = OtherMatter; // the caller was authorized on another matter

        var result = await rig.CommitAsync(PathBReview());

        result.ReasonCode.Should().Be(DecisionCommitReasons.CoreChanged);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAServerPlanReadStamp_ASystemClosureBeforeTheCommit_IsAConflict()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Conflict, "no server stamp: the review started now");
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerPlanReadStamp_BeforeTheSystemClosure_LetsTheDecisionReclose()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));
        await rig.StampPlanRead(Primary, Now.AddMinutes(-10));

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        result.Signals.Single().Warning.Should().Be("reclosed_after_system_resolution");
    }

    [Fact]
    public async Task APlanReadStamp_IsTheServersOwn_AndExpires()
    {
        var rig = new Rig();
        var http = Http();

        await rig.PlanReads.StampAsync(http, Primary, CancellationToken.None);
        (await rig.PlanReads.ReadAsync(Tenant, CallerOid.ToString("D"), Primary, CancellationToken.None)).Should().Be(Now);
        (await rig.PlanReads.ReadAsync(Tenant, Guid.NewGuid().ToString("D"), Primary, CancellationToken.None)).Should().BeNull("per caller");

        rig.Clock.Advance(DecisionPlanReadStamp.Ttl + TimeSpan.FromMinutes(1));
        (await rig.PlanReads.ReadAsync(Tenant, CallerOid.ToString("D"), Primary, CancellationToken.None)).Should().BeNull("an old read is not a review start");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AClosedSignal_HasLastEvaluatedEqualToResolvedOn_D120(bool reclose)
    {
        var rig = new Rig();
        if (reclose)
        {
            rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));
            rig.Signals[Primary]["sprk_lastevaluated"] = Now.UtcDateTime.AddMinutes(-5);
            await rig.StampPlanRead(Primary, Now.AddMinutes(-10));
        }

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var update = rig.SignalUpdates.Single(u => u.Id == Primary).Fields;
        update["sprk_lastevaluated"].Should().Be(update["sprk_resolvedon"], "D-120: never a lastevaluated later than resolvedon");
        rig.Signals[Primary].GetAttributeValue<DateTime>("sprk_lastevaluated").Should().Be(rig.Signals[Primary].GetAttributeValue<DateTime>("sprk_resolvedon"));
    }

    // ── Second review (deb4bbbc6) and uac-r2's review of #1665 ─────────────────────────────────────────────────

    private static readonly Guid Third = Guid.Parse("c0430000-0000-4000-8000-000000000034");

    private static Task ForgetLedger(Rig rig, DecisionReview review) =>
        rig.Ledger.RemoveAsync(Tenant, DecisionCommitService.LedgerResource, $"{CallerOid:D}:{review.ReviewId:D}", DecisionCommitService.LedgerVersion);

    /// <summary>The ledger forgets the outcome (7-day TTL or eviction); the record written by the first run is what the lookup finds.</summary>
    private static async Task LedgerForgetsButTheRecordStays(Rig rig, DecisionReview review)
    {
        var written = rig.Records.Single();
        written.Id = RecordId;
        written["createdby"] = new EntityReference("systemuser", WriterUser); // Dataverse stamps the creator: the writer, with no impersonation
        rig.ExistingRecords.Add(written);
        await ForgetLedger(rig, review);
    }

    /// <summary>The first run's close fails once (after the record), so the Signal is still open and a repeat has a close to finish.</summary>
    private static void FirstCloseFails(Rig rig) =>
        rig.BeforeSignalUpdate = _ =>
        {
            rig.BeforeSignalUpdate = null;
            throw new TimeoutException("no answer");
        };

    [Fact]
    public async Task F1_AStoredRecordFoundByItsReviewId_NeverClosesOtherSignals_UnderIt()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.AddSignal(Third, DecisionLane.Decide, Matter, PathB);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);
        var closes = rig.SignalUpdates.Count;

        // Days later: the same review id on ANOTHER item, also resolving a third, all items the caller can only read.
        var reused = await rig.CommitAsync(review with { AlsoResolve = [Third] }, Second);

        reused.Status.Should().Be(DecisionCommitStatus.Refused);
        reused.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged);
        rig.SignalUpdates.Should().HaveCount(closes, "nothing is closed under another review's record");
        rig.Status(Second).Should().Be(Open);
        rig.Status(Third).Should().Be(Open);
    }

    [Fact]
    public async Task F1_AStoredRecordForTheSameSignals_ButADifferentReview_OrWithoutAStoredHash_ClosesNothing()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);
        Steps(rig.ExistingRecords.Single()).GetProperty("reviewHash").GetString().Should().Be(DecisionCommitService.ContentHash(Primary, review),
            "the record stores the review's content hash");

        var changed = await rig.CommitAsync(review with { Because = "something else" });
        changed.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged);

        var steps = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rig.ExistingRecords.Single().GetAttributeValue<string>("sprk_steps"))!;
        steps.Remove("reviewHash");
        rig.ExistingRecords.Single()["sprk_steps"] = JsonSerializer.Serialize(steps);
        var unproven = await rig.CommitAsync(review);
        unproven.ReasonCode.Should().Be(DecisionCommitReasons.ReviewChanged, "a record that cannot prove it is this review answers nothing");

        rig.Status(Primary).Should().Be(Open);
    }

    // ── Task 161 (D-180): Create budget in place of Revise budget on a matter with no active budget ─────────────────────────

    [Fact]
    public async Task CreateBudget_OnAMatterWithNoActiveBudget_RunsTheCreate_RecordsIt_AndClosesTheSignalActed_D180()
    {
        var rig = new Rig { ActiveBudgets = 0 };

        var result = await rig.CommitAsync(CreateBudgetReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Writes.Should().Equal("execute:create-budget", "record:sprk_decisionrecord", $"close:{Primary}");
        rig.Executors["create-budget"].Requests.Single().Parameters["amount"].Should().Be("90000");
        rig.Executors["create-budget"].Requests.Single().Core.Should().NotBeNull();
        rig.Executors["revise-budget"].Requests.Should().BeEmpty();
        var steps = Steps(rig.Record).GetProperty("steps").EnumerateArray().ToList();
        steps.Select(x => (x.GetProperty("code").GetString(), x.GetProperty("taken").GetBoolean()))
            .Should().Equal(("send-budget-inquiry", false), ("create-budget", true), ("approve-variance", false));
        steps[1].GetProperty("label").GetString().Should().Be("Create budget");
        steps[1].GetProperty("written").EnumerateArray().Select(w => (w.GetProperty("entity").GetString(), w.GetProperty("id").GetGuid()))
            .Should().Equal(("sprk_budget", Budget));
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.BudgetReads.Should().ContainSingle("one count read, as the caller");
    }

    [Fact]
    public async Task CreateBudget_ReviewedOnZeroBudgets_ButTheMatterNowHasOne_IsRefusedAsTheActionsMismatch_NothingWritten_D17()
    {
        var rig = new Rig { ActiveBudgets = 1 };

        var result = await rig.CommitAsync(CreateBudgetReview());

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ActionsDoNotMatchPlan);
        rig.Writes.Should().BeEmpty("a create-budget is never silently turned into a revise-budget");
        rig.Executors["create-budget"].Requests.Should().BeEmpty();
        rig.Executors["revise-budget"].Requests.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task ReviseBudget_ReviewedOnABudget_ButTheMatterNowHasNone_IsRefusedAsTheActionsMismatch_NothingWritten_D17()
    {
        var rig = new Rig { ActiveBudgets = 0 };

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ActionsDoNotMatchPlan);
        rig.Writes.Should().BeEmpty("a revise-budget is never silently turned into a create-budget");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ReviseBudget_OnAMatterWithBudgets_IsUnchanged_MustNotFire_D180(int budgets)
    {
        var rig = new Rig { ActiveBudgets = budgets };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Writes.Should().Equal("execute:revise-budget", "record:sprk_decisionrecord", $"close:{Primary}");
        rig.Executors["create-budget"].Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBudgetsCannotBeCounted_ReviseBudgetStays_AndACreateBudgetReviewIsTheMismatch_NeverAFailure_D180()
    {
        foreach (var fails in new DataverseUserResponse?[] { null, DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "down") })
        {
            var rig = new Rig { ActiveBudgets = null, BudgetsReadFails = fails };

            var revise = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));
            revise.Status.Should().Be(DecisionCommitStatus.Recorded, revise.Detail);
            rig.Executors["revise-budget"].Requests.Should().ContainSingle();

            var other = new Rig { ActiveBudgets = null, BudgetsReadFails = fails };
            var create = await other.CommitAsync(CreateBudgetReview());
            create.Status.Should().Be(DecisionCommitStatus.Refused);
            create.ReasonCode.Should().Be(DecisionCommitReasons.ActionsDoNotMatchPlan);
            other.Writes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ARetryOfAFailedReview_WhoseCreateBudgetAlreadyLanded_KeepsThePlanItWasTakenUnder_AndNeverCreatesTheBudgetTwice_D180()
    {
        var rig = new Rig { ActiveBudgets = 0 };
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = CreateBudgetReview() with { NextSteps = [new DecisionReviewNextStep { Code = "add-todo", Values = TodoValues }] };

        var failed = await rig.CommitAsync(review);
        failed.Resumable.Should().BeTrue(failed.Detail);

        // The first attempt's create-budget landed, so the matter now HAS a budget: the plan the review was taken under is kept.
        rig.ActiveBudgets = 1;
        rig.Executors["add-todo"].Result = null;
        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Log.Count(l => l == "execute:create-budget").Should().Be(1, "a landed step is not run again");
        rig.Executors["revise-budget"].Requests.Should().BeEmpty();
        Steps(rig.Record).GetProperty("steps")[1].GetProperty("code").GetString().Should().Be("create-budget");
    }

    [Fact]
    public async Task AFinishedReview_WhoseCreateBudgetAlreadyLanded_IsAnsweredByItsRecord_AndNeverRefusedForTheNewBudget_D180()
    {
        var rig = new Rig { ActiveBudgets = 0 };
        FirstCloseFails(rig);
        var review = CreateBudgetReview();
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);

        rig.ActiveBudgets = 1; // the budget the first run created
        var finished = await rig.CommitAsync(review);

        finished.Status.Should().Be(DecisionCommitStatus.Recorded, finished.Detail);
        finished.Replayed.Should().BeTrue();
        rig.Log.Count(l => l == "execute:create-budget").Should().Be(1);
    }

    // ── uac-r2 re-check F1 (PR #1953): the stored-record lookup matches only records the ontology writer created ───────────────────────

    [Fact]
    public async Task F1_AUserCreatedRowWithTheReviewsIdAndHash_IsIgnored_TheFullPathRuns_AndTheGenuineRecordIsWrittenAndLinked()
    {
        // A Console user (Deep create on sprk_decisionrecord; sprk_steps and sprk_confirmedby are not field-secured) pre-creates a row with
        // their own confirmedby, the review id, the predictable review hash and the Signals.
        var rig = new Rig();
        var review = PathBReview(inquiry: false, todo: false);
        var forged = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_confirmedby"] = new EntityReference("systemuser", Caller),
            ["createdby"] = new EntityReference("systemuser", ConsoleCreator),
            ["sprk_steps"] = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["reviewId"] = review.ReviewId.ToString("D"),
                ["reviewHash"] = DecisionCommitService.ContentHash(Primary, review),
                ["resolvedSignalIds"] = new[] { Primary.ToString("D") },
                ["steps"] = new[] { new { code = "approve-variance", taken = true } },
            }),
            ["sprk_followons"] = "[]",
        };
        rig.ExistingRecords.Add(forged);

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeFalse("the forged row is not an earlier run of this review");
        result.DecisionRecordId.Should().Be(RecordId).And.NotBe(forged.Id);
        rig.Log.Should().Contain(l => l.StartsWith("execute:", StringComparison.Ordinal), "the executors ran");
        rig.Records.Should().ContainSingle("the genuine record is written");
        rig.LinkedRecord(Primary).Should().Be(RecordId, "the Signal links the genuine record, not the forged row");
    }

    [Fact]
    public async Task F1_AWriterCreatedRecordWithTheSameKey_StillTakesTheFinishingPath_NoRegression()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);
        var executes = rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal));

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeTrue();
        rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal)).Should().Be(executes, "nothing is run again");
        rig.Records.Should().ContainSingle("nothing is recorded again");
        rig.QueriesAsked.Last(q => q.EntityName == "sprk_decisionrecord").Criteria.Conditions.Should().Contain(
            c => c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(WriterUser));
    }

    [Fact]
    public async Task F1_WhenTheWritersIdentityCannotBeRead_TheCommitIsUnavailable_AndNothingIsDoneOrClosed()
    {
        var rig = new Rig { WhoAmIFault = new TimeoutException("whoami") };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, "the lookup cannot be narrowed, so it fails closed, never 'no record found'");
        rig.WhoAmICalls.Should().BeGreaterThan(0);
        rig.Writes.Should().BeEmpty("nothing is executed, recorded or closed");
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task WhenTheLedgerHasForgotten_TheRecordItselfAnswersTheRepeat()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(Primary).Should().Be(Open);
        await LedgerForgetsButTheRecordStays(rig, review);
        var executes = rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal));

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeTrue();
        result.DecisionRecordId.Should().Be(RecordId);
        rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal)).Should().Be(executes, "nothing is run again");
        rig.Records.Should().ContainSingle("nothing is recorded again");
        rig.LinkedRecord(Primary).Should().Be(RecordId, "a Signal the first run never closed is closed with the record found");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed, "the record's resolution: it recorded a revision");
    }

    [Fact]
    public async Task F1_AStoredRecordRepeat_AsksFr51Again_AndARevokedRightClosesNothing()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);
        rig.Rights[("sprk_matters", Matter)] = AccessRights.Read;

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task K6_AStoredRecordRepeatThatDismisses_AppliesSuppression()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = Dismissal("not-material");
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        await LedgerForgetsButTheRecordStays(rig, review);
        var handed = rig.DismissedHandedOn.Count;

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionDismissed, "the record's resolution: it recorded a dismissal");
        rig.Signals[Primary].GetAttributeValue<string>("sprk_resolutionnotes").Should().StartWith("not-material", "the record's own reason");
        rig.DismissedHandedOn.Should().HaveCount(handed + 1);
        rig.DismissedHandedOn[^1].Select(r => r.Id).Should().Equal(Primary);
    }

    [Fact]
    public async Task K6_ARecordedReplayThatDismisses_AppliesSuppression()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = Dismissal("not-material");
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        var handed = rig.DismissedHandedOn.Count;

        var replay = await rig.CommitAsync(review);

        replay.Replayed.Should().BeTrue();
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionDismissed);
        rig.DismissedHandedOn.Should().HaveCount(handed + 1);
        rig.DismissedHandedOn[^1].Select(r => r.Id).Should().Equal(Primary);
    }

    [Fact]
    public async Task F4_ARevisionThatLanded_BeforeTheRecordCreateFaulted_StillQueuesItsTrigger()
    {
        var rig = new Rig { FailRecordWith = new TimeoutException("no answer") };

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Failed);
        result.FailedStep.Should().Be(5);
        rig.FailedHandedOn.Should().ContainSingle()
            .Which.Should().Contain(s => s.Code == "revise-budget" && s.Written.Any(w => w.Entity == "sprk_budgetrevision" && w.Id == RevisionId));
    }

    [Fact]
    public async Task F4_AFaultAfterTheFirstWrite_StillQueuesTheTriggerForALandedRevision()
    {
        var rig = new Rig();
        rig.FollowUps.Setup(f => f.AfterRecordedAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(),
                It.IsAny<IReadOnlyList<DecisionCommitService.SignalRow>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Failed);
        rig.FailedHandedOn.Should().ContainSingle()
            .Which.Should().Contain(s => s.Code == "revise-budget" && s.Written.Any(w => w.Entity == "sprk_budgetrevision"));
    }

    [Fact]
    public async Task K1_ASecondReviewIdOnTheSameSignal_WhileTheFirstHoldsIt_IsInProgress_AndSendsNothing()
    {
        var rig = new Rig();
        var first = PathBReview();
        var second = PathBReview();
        // The first review is committing Primary on another instance: it holds the Signal's lock.
        var held = await rig.Lease.TryAcquireAsync(DecisionCommitService.SignalLeaseId(Tenant, Primary), null, TimeSpan.FromMinutes(1), CancellationToken.None);
        held.Status.Should().Be(ScheduledJobLeaseStatus.Granted);

        var result = await rig.CommitAsync(second);

        result.Status.Should().Be(DecisionCommitStatus.InProgress);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInProgress);
        rig.Log.Should().NotContain("execute:send-budget-inquiry", "no second inquiry goes out");
        rig.Writes.Should().BeEmpty();

        await rig.Lease.ReleaseAsync(DecisionCommitService.SignalLeaseId(Tenant, Primary), held.Token!, CancellationToken.None);
        (await rig.CommitAsync(first)).Status.Should().Be(DecisionCommitStatus.Recorded, "the review lock of the refused one was released");
        (await rig.CommitAsync(second)).Status.Should().Be(DecisionCommitStatus.Conflict, "once the first is recorded the item is closed");
    }

    [Fact]
    public async Task K3_AResumeAfterTheReviewWindow_DoesNotRecloseASystemResolutionOlderThanTheWindow()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        (await rig.CommitAsync(review)).Resumable.Should().BeTrue(); // the review started at Now

        // The system closed the Signal 30 minutes after the review started; the resume comes five hours later.
        rig.Signals[Primary]["sprk_signalstatus"] = new OptionSetValue(Resolved);
        rig.Signals[Primary]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
        rig.Signals[Primary]["sprk_resolvedon"] = Now.UtcDateTime.AddMinutes(30);
        rig.Versions[Primary]++;
        rig.Clock.Advance(TimeSpan.FromHours(5));
        rig.Executors["add-todo"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Conflict, "the window is the plan-read window (4 h), not the ledger's 7 days");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionConditionCleared);
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task K5_ARecordedReplay_AsksFr51Again_ARevokedRightLeavesTheSignalOpen_AndSaysSo()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Rights[("sprk_matters", Matter)] = AccessRights.Read;

        var replay = await rig.CommitAsync(review);

        // Stated behaviour: the stored outcome comes back (200, Replayed) with the open Signal listed Unchanged and the warning.
        replay.Status.Should().Be(DecisionCommitStatus.Recorded);
        replay.Replayed.Should().BeTrue();
        replay.Signals.Single().Should().Be(new DecisionCommitSignal(Primary, "Unchanged", "signal_close_not_authorized"));
        replay.Warnings.Should().Contain("signal_close_not_authorized");
        rig.Status(Primary).Should().Be(Open);

        rig.Rights.Remove(("sprk_matters", Matter));
        var restored = await rig.CommitAsync(review);
        restored.Signals.Single().Warning.Should().BeNull("the stored outcome was not overwritten, so a later repeat still finishes the close");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
    }

    /// <summary>A lock store whose take lands and THEN observes the request's cancellation (a Redis SET that committed before the
    /// await saw the cancel).</summary>
    private sealed class CancelDuringTakeLease(IScheduledJobLease inner, CancellationTokenSource cts) : IScheduledJobLease
    {
        public bool Armed { get; set; } = true;

        public bool IsDistributed => inner.IsDistributed;

        public async Task<ScheduledJobLeaseGrant> TryAcquireAsync(string jobId, DateTimeOffset? occurrenceUtc, TimeSpan duration, CancellationToken cancellationToken)
        {
            var grant = await inner.TryAcquireAsync(jobId, occurrenceUtc, duration, CancellationToken.None);
            if (Armed)
            {
                Armed = false;
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return grant;
        }

        public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken) =>
            inner.RenewAsync(jobId, token, duration, cancellationToken);

        public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken) => inner.ReleaseAsync(jobId, token, cancellationToken);
    }

    [Fact]
    public async Task K7_ACancellationWhileTheLockIsTaken_ReleasesIt_SoTheSameReviewRunsAtOnce()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Lease = new CancelDuringTakeLease(new ProcessLocalScheduledJobLease(), cts);
        var review = PathBReview();

        var cancelled = () => rig.CommitAsync(review, ct: cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        rig.Writes.Should().BeEmpty();

        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded, "the lock was released, not held for its 10 minutes");
    }

    /// <summary>Cancels the request while the commit service writes its in-progress marker (the write itself lands).</summary>
    private sealed class CancelDuringMarkerLedger(ITenantCache inner, CancellationTokenSource cts) : ITenantCache
    {
        public bool Armed { get; set; } = true;

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

        public async Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            await inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, CancellationToken.None);
            if (Armed && resource == DecisionCommitService.LedgerResource)
            {
                Armed = false;
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
            inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);
    }

    [Fact]
    public async Task K2_ACancellationWhileTheMarkerIsWritten_LeavesNoMarker_SoTheSameReviewIsNotBlocked()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.LedgerStore = new CancelDuringMarkerLedger(rig.Ledger, cts);
        var review = PathBReview();

        try
        {
            await rig.CommitAsync(review, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Either outcome is fine for the cancelled attempt; what matters is the repeat below.
        }

        var again = await rig.CommitAsync(review);
        again.Status.Should().NotBe(DecisionCommitStatus.InProgress, "the marker was released or replaced by the outcome, not held for its 10 minutes");
        again.Status.Should().Be(DecisionCommitStatus.Recorded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UacF4a_ACallerWhoCannotWriteANoCoreSubject_GetsTheSame403_WhetherOrNotItsOwnerResolves(bool ownerResolves)
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Rights[("sprk_todos", ItemTodo)] = AccessRights.Read;
        // Whether uac-r2's resolver would answer a team, refuse, or fault must not change what this caller is told.
        if (ownerResolves)
            rig.Owner = RecordOwnerResolution.Owned(ItemTeam);
        else
            rig.OwnerFault = new TimeoutException("no answer");

        var result = await rig.CommitAsync(new DecisionReview { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("mark-complete"), Skip("reschedule")] }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        result.Detail.Should().Be("You do not have permission to change an item in this decision.");
        rig.OwnerAsks.Should().BeEmpty("the resolver (and so any read of the subject row app-only) runs only after FR-51");
        rig.Entities.Verify(e => e.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task UacF4b_ACoreTypeReadFault_Is503_AMissingCoreType_Is422()
    {
        // The caller's access check reads the record type first (task 036); the commit's own read is the second.
        var matterType = new Entity("sprk_recordtype_ref", TypeMatter) { ["sprk_recordlogicalname"] = "sprk_matter" };
        var faulted = new Rig();
        faulted.Entities.SetupSequence(e => e.RetrieveAsync("sprk_recordtype_ref", TypeMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterType)
            .ThrowsAsync(new TimeoutException("no answer"));
        var missing = new Rig();
        missing.Entities.SetupSequence(e => e.RetrieveAsync("sprk_recordtype_ref", TypeMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterType)
            .ThrowsAsync(new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "missing"));

        var fault = await faulted.CommitAsync(Dismissal("not-material"));
        var gone = await missing.CommitAsync(Dismissal("not-material"));

        fault.Status.Should().Be(DecisionCommitStatus.Unavailable, "a fault after authorization is 503");
        gone.Status.Should().Be(DecisionCommitStatus.Refused, "a catalog row that does not exist is 422");
        gone.ReasonCode.Should().Be(DecisionCommitReasons.CoreUnsupported);
        faulted.Writes.Should().BeEmpty();
        missing.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UacF4b_ANoCoreOwnerResolverFault_Is503_ARefusal_Is422()
    {
        Rig NoCore()
        {
            var rig = new Rig();
            rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
            return rig;
        }

        var faulted = NoCore();
        faulted.OwnerFault = new TimeoutException("no answer");
        var refused = NoCore();
        refused.Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "the To Do has no owning business unit");
        DecisionReview Review() => new() { ReviewId = Guid.NewGuid(), Confirmed = true, Actions = [Take("mark-complete"), Skip("reschedule")] };

        var fault = await faulted.CommitAsync(Review(), DoSignal);
        var unsupported = await refused.CommitAsync(Review(), DoSignal);

        fault.Status.Should().Be(DecisionCommitStatus.Unavailable);
        unsupported.Status.Should().Be(DecisionCommitStatus.Refused);
        unsupported.ReasonCode.Should().Be(DecisionCommitReasons.ItemOwnerUnsupported);
        faulted.Writes.Should().BeEmpty();
        refused.Writes.Should().BeEmpty();
    }

    // ── The HTTP mapping ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DecisionCommitStatus.Recorded, 200)]
    [InlineData(DecisionCommitStatus.NotFound, 404)]
    [InlineData(DecisionCommitStatus.CallerUnresolved, 403)]
    [InlineData(DecisionCommitStatus.Forbidden, 403)]
    [InlineData(DecisionCommitStatus.Conflict, 409)]
    [InlineData(DecisionCommitStatus.InProgress, 409)]
    [InlineData(DecisionCommitStatus.Refused, 422)]
    [InlineData(DecisionCommitStatus.Unavailable, 503)]
    [InlineData(DecisionCommitStatus.Failed, 500)]
    public void EachOutcome_IsOneHttpStatus(DecisionCommitStatus status, int http)
    {
        var reply = DecisionCommitEndpoints.ToHttp(new DecisionCommitResult { Status = status, ReviewId = Guid.NewGuid() }, Http());

        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)reply).StatusCode.Should().Be(http);
    }

    [Fact]
    public void AFailure_TellsTheUserWhichWritesLanded()
    {
        var landed = new DecisionCommitRef("sprk_budgetrevision", RevisionId);
        var reply = DecisionCommitEndpoints.ToHttp(new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Failed,
            ReviewId = Guid.NewGuid(),
            ReasonCode = DecisionActionReasons.WriteFailed,
            FailedStep = 3,
            Landed = [landed],
            Resumable = true,
        }, Http());

        var problem = reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject.ProblemDetails;
        problem.Extensions["failedStep"].Should().Be(3);
        problem.Extensions["landed"].Should().BeEquivalentTo(new[] { landed });
        problem.Extensions["resumable"].Should().Be(true);
        problem.Extensions["reasonCode"].Should().Be(DecisionActionReasons.WriteFailed);
    }

    [Fact]
    public void ANotFound_IsTheUniform404_WithNothingAboutTheReview()
    {
        var reply = DecisionCommitEndpoints.ToHttp(new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = Guid.NewGuid(), Detail = "secret" }, Http());

        var problem = reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject.ProblemDetails;
        problem.Detail.Should().NotContain("secret");
        problem.Extensions.Should().NotContainKey("reviewId");
    }
}
