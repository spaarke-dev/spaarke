using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
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

    private const string PathBPlan = "{\"actions\":[\"send-budget-inquiry\",\"revise-budget\",\"approve-variance\"],\"nextSteps\":[\"add-todo\",\"send-email\"]}";
    private const string DoPlan = "{\"actions\":[\"mark-complete\",\"reschedule\"],\"nextSteps\":[\"add-todo\"]}";

    private const int Open = SignalWriter.SignalStatusOpen;
    private const int Resolved = SignalWriter.SignalStatusResolved;

    // ── The rig ──────────────────────────────────────────────────────────────────────────────────────────────────

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
        public DecisionPlanReadStamp PlanReads { get; }
        public RecordOwnerResolution Owner { get; set; } = RecordOwnerResolution.Unchanged("not a secure matter");
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
                    new Entity("sprk_policyversion", id) { ["sprk_decisionplan"] = id == DoVersion ? DoPlan : PathBPlan });
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EntityCollection(ExistingRecords.ToList()));
            Entities.Setup(e => e.RetrieveAsync("sprk_todo", ItemTodo, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_todo", ItemTodo) { ["ownerid"] = new EntityReference("systemuser", ItemOwner) });

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
            Org.Setup(o => o.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
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
            var suppression = new SignalSuppressionService(Entities.Object, WriterClient(), Clock, NullLogger<SignalSuppressionService>.Instance);
            FollowUps = new Mock<DecisionCommitFollowUps>(trigger, suppression, Entities.Object, NullLogger<DecisionCommitFollowUps>.Instance)
            { CallBase = false };
            FollowUps.Setup(f => f.AfterRecordedAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(),
                    It.IsAny<IReadOnlyList<DecisionCommitService.SignalRow>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid? _, IReadOnlyList<DecisionCommitStep> _, IReadOnlyList<DecisionCommitService.SignalRow> dismissed, CancellationToken _) =>
                    DismissedHandedOn.Add(dismissed))
                .ReturnsAsync(Array.Empty<string>());
            FollowUps.Setup(f => f.AfterFailedCommitAsync(It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<DecisionCommitStep>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid? _, IReadOnlyList<DecisionCommitStep> executed, CancellationToken _) => FailedHandedOn.Add(executed))
                .Returns(Task.CompletedTask);
        }

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
            if (subjectEntity is not null) e["sprk_regardingrecordtype"] = subjectEntity;
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
            if (path == "WhoAmI")
                return Ok(new Dictionary<string, object?> { ["UserId"] = Caller.ToString("D") });
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

                return Ok(body);
            }

            if (path.StartsWith("sprk_matters(", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path["sprk_matters(".Length..path.IndexOf(')')]);
                return CallerCannotReadCore.Contains(id)
                    ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no")
                    : Ok(new Dictionary<string, object?> { ["createdon"] = "2026-01-01T00:00:00Z" });
            }

            return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

        public DecisionCommitService Service()
        {
            var access = new SignalCoreRecordAccess(User.Object, Entities.Object, NullLogger<SignalCoreRecordAccess>.Instance);
            var plans = new DecisionPlanService(Entities.Object, NullLogger<DecisionPlanService>.Instance);
            var ownership = new Mock<IRecordOwnershipResolver>();
            ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Log.Add("owner");
                    return Owner;
                });
            var records = new DecisionRecordWriter(WriterClient(), ownership.Object, Clock, NullLogger<DecisionRecordWriter>.Instance);
            return new DecisionCommitService(access, plans, new DecisionActionExecutors(Executors.Values), records, WriterClient(),
                Entities.Object, Probe.Object, Ledger, FollowUps.Object, Lease, PlanReads, Clock, NullLogger<DecisionCommitService>.Instance);
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
    public async Task WhenTheLedgerHasForgotten_TheRecordItselfAnswersTheRepeat()
    {
        var rig = new Rig();
        rig.ExistingRecords.Add(new Entity("sprk_decisionrecord", RecordId));

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        result.Replayed.Should().BeTrue();
        result.DecisionRecordId.Should().Be(RecordId);
        rig.Log.Should().NotContain(l => l.StartsWith("execute:", StringComparison.Ordinal) || l.StartsWith("record:", StringComparison.Ordinal),
            "nothing is run or recorded again");
        rig.LinkedRecord(Primary).Should().Be(RecordId, "a Signal the first run never closed is closed with the record found");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
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
        rig.Record.Contains("ownerid").Should().BeFalse("a core-record item's record is owned by the resolver's answer, never the client");
    }

    [Fact]
    public async Task ANoCoreDoItem_IsOwnedByTheItemsOwner_ReadAppOnly()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, null, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Signals[DoSignal]["ownerid"] = new EntityReference("systemuser", Caller); // D-35: the caller owns it

        var result = await rig.CommitAsync(new DecisionReview
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("mark-complete"), Skip("reschedule")],
        }, DoSignal);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(ItemOwner, "read from the item row, not the Signal or the body");
        rig.Executors["mark-complete"].Requests.Single().Subject.Should().Be(new DecisionRecordRef("sprk_todo", ItemTodo));
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
    public async Task ASubjectWhoseTableCannotBeLookedUp_IsUnavailable_BeforeAnyWrite()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_unknown", subjectId: ItemTodo);
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_unknown", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no metadata"));
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
