using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-13) for the comms-RI action
/// convergence — the point where an AUTHORIZED assessed communication becomes an observable end-user outcome. It
/// drives the REAL composed chain from the public seam entry point
/// (<see cref="RuleGatedAssessedConsumer.PublishAsync"/>, the <see cref="ICommunicationAssessedProducer"/> seam):
/// real <see cref="CommunicationRuleGate"/> (task 041) → real <see cref="CommunicationRiActionService"/> (task 042)
/// → real <see cref="ActionSeam"/> (task 031 Layer-A facade, ADR-013) → real <see cref="OutboxService"/> (task 012)
/// → real <see cref="NotificationService"/> (the appnotification mirror), doubling ONLY the two true external
/// boundaries: the Dataverse <see cref="IGenericEntityService"/> and the Azure-SignalR delivery leg
/// (<see cref="SignalRDeliveryService.PingUserAsync"/>, overridden to record).
/// </summary>
/// <remarks>
/// Proves the task-042 DoD both ways:
/// <list type="bullet">
///   <item><b>Authorize</b> — the domain record is created via the Layer-A seam (a <c>task</c>, NOT a direct
///     Dataverse write), a <c>kind=communication-assessed</c> outbox row is written BEFORE the best-effort ping,
///     the outbox envelope carries IDs + minimal display metadata + <c>regardingRecordId</c> only, and an
///     appnotification is created via an explicit <see cref="NotificationService.CreateNotificationAsync"/> call
///     (visible via the Daily-Briefing read path) — in that exact order.</item>
///   <item><b>Deny</b> — a below-threshold confidence yields NO seam call, NO outbox row, NO ping, and NO
///     appnotification (the short-circuit is structural: the RI service is only reached inside the gate's
///     authorize branch).</item>
/// </list>
/// </remarks>
public sealed class RiActionsViaSeamSeamTests
{
    /// <summary>The ontology writer's own application user (WhoAmI on its connection): the <c>createdby</c> of the records it creates.</summary>
    private static readonly Guid WriterUser = Guid.Parse("3121bf1b-9fbf-f111-aaaf-0022482913fc");

    /// <summary>Records the real ping calls + appends "ping:{recipient}" to the shared ordering log.</summary>
    private sealed class RecordingDelivery : SignalRDeliveryService
    {
        public readonly List<(Guid OutboxRowId, Guid Recipient, NotificationKind Kind)> Pings = new();
        private readonly List<string> _events;

        public RecordingDelivery(List<string> events)
            : base((ISystemUserIdentityResolver?)null, NullLogger<SignalRDeliveryService>.Instance)
            => _events = events;

        public override Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
        {
            Pings.Add((outboxRowId, recipientSystemUserId, kind));
            _events.Add($"ping:{recipientSystemUserId}");
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public required RuleGatedAssessedConsumer Consumer { get; init; }
        public required RecordingDelivery Delivery { get; init; }
        public required List<DataverseEntity> Creates { get; init; }
        public required List<string> Events { get; init; }
        public required Guid CommunicationId { get; init; }
        public required Guid ThreadId { get; init; }
        public required Guid MatterId { get; init; }
        public required Guid OwnerId { get; init; }

        /// <summary>Task 042: every Decision Record the gate's writer created, in order (empty unless the harness was built with one).</summary>
        public required List<DataverseEntity> DecisionRecords { get; init; }

        /// <summary>Task 042: the writer's Dataverse boundary, so a test can make the record's create fail.</summary>
        public required Mock<IOrganizationServiceAsync2> WriterOrg { get; init; }

        public required Guid RuleId { get; init; }

        /// <summary>Task 149 (D-139): every Decision Record job the consumer queued, in order. The gate does not write the record; this job does.</summary>
        public required List<JobContract> QueuedJobs { get; init; }

        /// <summary>Task 149: the queue's send, so a test can make the enqueue fail.</summary>
        public required Mock<JobSubmissionService> Queue { get; init; }

        /// <summary>Task 149: runs the REAL <see cref="GateDecisionRecordJobHandler"/> over every job queued so far and not yet run, as the
        /// job processor would, and returns what each answered.</summary>
        public required Func<Task<List<JobOutcome>>> RunQueuedJobsAsync { get; init; }
    }

    /// <summary>
    /// Composes the REAL chain over a doubled Dataverse boundary. The assessed communication is owned by
    /// <c>OwnerId</c> (the RI recipient), regards <c>MatterId</c>, and a single enabled <c>sprk_communicationrule</c>
    /// matches that matter at threshold <paramref name="ruleThreshold"/>. All three write entity types
    /// (<c>task</c> / <c>sprk_notificationoutbox</c> / <c>appnotification</c>) record their creation into the shared
    /// ordering log so the seam→outbox→ping→mirror ordering is assertable.
    /// </summary>
    private static Harness BuildHarness(
        decimal ruleThreshold, TimeProvider? clock = null, int recipientTimeZoneCode = 92, string recipientZoneName = "UTC",
        bool withDecisionRecords = false)
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var creates = new List<DataverseEntity>();
        var events = new List<string>();

        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);

        // Re-read of the assessed communication — serves BOTH the consumer's matter read (sprk_regardingmatter)
        // AND the RI service's owner/thread/channel/regarding read. One rich row satisfies each caller's projection.
        var communication = new DataverseEntity("sprk_communication", communicationId)
        {
            ["ownerid"] = new EntityReference("systemuser", ownerId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["sprk_regardingrecordid"] = matterId.ToString(),
            ["sprk_regardingrecordtype"] = "sprk_matter",
        };
        entity
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);

        // Task 098: the RI task's due dates count from the RECIPIENT's today, in their Dataverse time zone.
        entity
            .Setup(s => s.RetrieveAsync("usersettings", ownerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("usersettings", ownerId) { ["timezonecode"] = recipientTimeZoneCode });
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "timezonedefinition"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("timezonedefinition") { ["standardname"] = recipientZoneName },
            }));

        // Rule store: one enabled rule matching the matter at the given threshold.
        var rule = new DataverseEntity("sprk_communicationrule", ruleId)
        {
            ["sprk_enabled"] = true,
            ["sprk_flagprivilege"] = false,
            ["sprk_priority"] = 100,
            ["sprk_matter"] = new EntityReference("sprk_matter", matterId),
            ["sprk_confidencethreshold"] = ruleThreshold,
        };
        // Task 042 / 149: the Decision Record job reads the matched rule's priority for the fact snapshot.
        entity
            .Setup(s => s.RetrieveAsync("sprk_communicationrule", ruleId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationrule"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { rule }));

        // Three distinct create paths, each recording into the shared ordering log. With MockBehavior.Strict a
        // create that the code does NOT make (e.g. on the deny path) simply never fires — proving "no side effect".
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_event"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("seam-task-create"); return Guid.NewGuid(); });
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_notificationoutbox"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("outbox-write"); return Guid.NewGuid(); });
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("appnotification-create"); return Guid.NewGuid(); });

        var logger = NullLoggerFactory.Instance;
        var gate = new CommunicationRuleGate(
            entity.Object,
            Options.Create(new CommsPolicyOptions { DefaultConfidenceThreshold = 0.8 }),
            NullLogger<CommunicationRuleGate>.Instance);

        // Real Layer-A facade (ADR-013). Only CreateTaskAsync is exercised → the field-mapping/scope deps are
        // never touched; loose doubles satisfy the ctor.
        var seam = new ActionSeam(
            entity.Object,
            new Mock<IFieldMappingDataverseService>().Object,
            new Mock<IServiceScopeFactory>().Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
            Moq.Mock.Of<Spaarke.Dataverse.ICommunicationDataverseService>(),
            NullLogger<ActionSeam>.Instance);

        var outbox = new OutboxService(entity.Object, NullLogger<OutboxService>.Instance);       // REAL outbox
        var delivery = new RecordingDelivery(events);                                             // SignalR boundary
        var notifications = new NotificationService(entity.Object, NullLogger<NotificationService>.Instance); // REAL mirror

        var riAction = new CommunicationRiActionService(
            seam, outbox, delivery, notifications, entity.Object, NullLogger<CommunicationRiActionService>.Instance, clock);

        // Task 042: the REAL Decision Record writer over a doubled Dataverse boundary (the SDK's own IOrganizationServiceAsync2 behind the
        // writer client; the stamp columns exist on sprk_decisionrecord live, task 140), uac-r2's resolver double, and the spaarkedev1 catalog.
        var decisionRecords = new List<DataverseEntity>();
        var writerOrg = new Mock<IOrganizationServiceAsync2>();
        writerOrg.Setup(o => o.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) =>
            {
                // Dataverse stamps the creator: the writer's own application user, with no impersonation (uac-r2 re-check, F3-1).
                e["createdby"] = new EntityReference("systemuser", WriterUser);
                decisionRecords.Add(e);
                events.Add("decision-record-create");
                return Guid.NewGuid();
            });
        // WhoAmI on the writer's own connection: the createdby the gate's "already recorded" lookup narrows on (without it the lookup fails closed).
        writerOrg.Setup(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.WhoAmIRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<OrganizationResponse>(new Microsoft.Crm.Sdk.Messages.WhoAmIResponse { Results = { ["UserId"] = WriterUser } }));
        // Task 149 (D-139): the gate only QUEUES the decision (the queue's send is the doubled boundary; it logs "decision-job-enqueue" in the
        // ordering log), and a REAL GateDecisionRecordJobHandler writes the record when a test runs the queued jobs, with the real writer, the
        // real idempotency service over an in-memory cache, and the doubled Dataverse boundary above. The writer's "already recorded" lookup
        // is answered from the records the writer has created (equality on the lookup, LIKE on sprk_steps), as Dataverse would.
        var queuedJobs = new List<JobContract>();
        var queue = new Mock<JobSubmissionService>(
            MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }), NullLogger<JobSubmissionService>.Instance, new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, CancellationToken _) => { queuedJobs.Add(job); events.Add("decision-job-enqueue"); return Task.CompletedTask; });

        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_decisionrecord"),
                It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                var communicationCondition = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_regardingcommunication");
                var likeCondition = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_steps");
                var needle = ((string)likeCondition.Values.Single()).Trim('%');
                // Like Dataverse, answer only the rows whose createdby equals the query's createdby condition (the writer's own records).
                var createdBy = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal);
                var found = decisionRecords
                    .Where(r => createdBy is null || Equals(r.GetAttributeValue<EntityReference>("createdby")?.Id, createdBy.Values.Single()))
                    .Where(r => r.GetAttributeValue<EntityReference>("sprk_regardingcommunication")?.Id.Equals(communicationCondition.Values.Single()) == true
                                && (r.GetAttributeValue<string>("sprk_steps") ?? string.Empty).Contains(needle, StringComparison.Ordinal))
                    .Select(r => new DataverseEntity("sprk_decisionrecord", Guid.NewGuid()))
                    .Take(1)
                    .ToList();
                return Task.FromResult(new EntityCollection(found));
            });

        DecisionRecordWriter? writer = null;
        GateDecisionRecordJobHandler? handler = null;
        if (withDecisionRecords)
        {
            writer = new DecisionRecordWriter(
                new OntologyWriterDataverseClient(() => writerOrg.Object, NullLogger<OntologyWriterDataverseClient>.Instance),
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
                new CoreAncestorResolver(
                    entity.Object,
                    (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                        CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
                    NullLogger<CoreAncestorResolver>.Instance),
                entity.Object,
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
                TimeProvider.System,
                NullLogger<DecisionRecordWriter>.Instance,
                new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
            handler = new GateDecisionRecordJobHandler(
                writer,
                entity.Object,
                new IdempotencyService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance),
                NullLogger<GateDecisionRecordJobHandler>.Instance);
        }

        var ranJobs = 0;
        async Task<List<JobOutcome>> RunQueuedJobs()
        {
            var outcomes = new List<JobOutcome>();
            while (handler is not null && ranJobs < queuedJobs.Count)
                outcomes.Add(await handler.ProcessAsync(queuedJobs[ranJobs++], CancellationToken.None));
            return outcomes;
        }

        var consumer = new RuleGatedAssessedConsumer(
            entity.Object, gate, riAction, NullLogger<RuleGatedAssessedConsumer>.Instance, signalTrigger: null,
            decisionRecords: withDecisionRecords ? new GateDecisionRecordEnqueuer(queue.Object, NullLogger<GateDecisionRecordEnqueuer>.Instance) : null);

        return new Harness
        {
            QueuedJobs = queuedJobs,
            Queue = queue,
            RunQueuedJobsAsync = RunQueuedJobs,
            Consumer = consumer,
            Delivery = delivery,
            Creates = creates,
            Events = events,
            CommunicationId = communicationId,
            ThreadId = threadId,
            MatterId = matterId,
            OwnerId = ownerId,
            DecisionRecords = decisionRecords,
            WriterOrg = writerOrg,
            RuleId = ruleId,
        };
    }

    private static CommunicationAssessedSignal Signal(Guid communicationId, double confidence) =>
        new(communicationId, CommunicationDirection.Incoming, Subject: "Settlement terms", From: "counsel@firm.example", RecipientCount: 2, Confidence: confidence);

    // ── Authorize: seam record → outbox (kind=communication-assessed) → ping → appnotification, IN THAT ORDER. ──
    [Fact]
    public async Task PublishAsync_WhenGateAuthorizes_CreatesSeamTaskThenOutboxThenPingsThenMirrorsAppnotification()
    {
        var h = BuildHarness(ruleThreshold: 0.7m);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        // (1) The domain record was created via the Layer-A seam — a sprk_event (event type = Task) regarding the
        //     communication via its typed sprk_regardingcommunication lookup, owned by the recipient — NOT a direct
        //     Dataverse write of some other shape.
        var task = h.Creates.SingleOrDefault(e => e.LogicalName == "sprk_event");
        task.Should().NotBeNull("the RI action is created via the Layer-A seam (ADR-013), not a direct write");
        task!.GetAttributeValue<string>("sprk_eventname").Should().Contain("Settlement terms");
        task.GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(h.CommunicationId);
        // unified-access-control-r2 task 146: a task filed to a record is owned by that record's TEAM (the resolver's
        // answer over its parents — the communication here), not the recipient; the recipient is still notified
        // (the ping and the appnotification below) and belongs in Assigned To (task 152; owner B2).
        task.GetAttributeValue<EntityReference>("ownerid")!.LogicalName.Should().Be("team");
        task.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(
            Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId);

        // (2) One outbox row, kind=communication-assessed, IDs + minimal display metadata + regardingRecordId only.
        var outboxRow = h.Creates.SingleOrDefault(e => e.LogicalName == "sprk_notificationoutbox");
        outboxRow.Should().NotBeNull();
        outboxRow!.GetAttributeValue<string>("sprk_kind").Should().Be("communication-assessed");
        var envelope = JsonSerializer.Deserialize<CommunicationEnvelope>(outboxRow.GetAttributeValue<string>("sprk_envelope"));
        envelope.Should().NotBeNull();
        envelope!.Kind.Should().Be(NotificationKind.CommunicationAssessed);
        envelope.CommunicationId.Should().Be(h.CommunicationId);
        envelope.ThreadId.Should().Be(h.ThreadId);
        envelope.Channel.Should().Be("email");
        envelope.Direction.Should().Be("inbound");
        envelope.RegardingRecordId.Should().Be(h.MatterId.ToString());
        envelope.Snippet.Should().BeNull("NFR-02/03: content is never placed on the spine");

        // (3) One best-effort ping to the recipient, backed by the outbox row id.
        h.Delivery.Pings.Should().ContainSingle();
        h.Delivery.Pings[0].Recipient.Should().Be(h.OwnerId);
        h.Delivery.Pings[0].Kind.Should().Be(NotificationKind.CommunicationAssessed);
        h.Delivery.Pings[0].OutboxRowId.Should().NotBe(Guid.Empty);

        // (4) The appnotification mirror was created via the explicit NotificationService call, owned by the recipient.
        var appnotification = h.Creates.SingleOrDefault(e => e.LogicalName == "appnotification");
        appnotification.Should().NotBeNull("the RI action mirrors to appnotification for Daily-Briefing visibility");
        appnotification!.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(h.OwnerId);

        // Ordering: seam record → outbox BEFORE ping → appnotification mirror (ADR-041/043 outbox-before-ping;
        // seam-before-outbox per the task-042 escalation guard).
        h.Events.Should().Equal("seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create");
    }

    // ── Task 098: the RI task's due dates are the RECIPIENT's calendar dates, not UTC dates. ──
    // Reproduces the two live "ONTOLOGY DEV SEED 005" tasks hand-corrected in the Date Only conversion: raised at
    // 2026-10-04T01:04:37Z — 21:04 on Oct 3 for an Eastern recipient — with the default +1 / +3 days. The former
    // createdAtUtc.AddDays(n) wrote Oct 5 / Oct 7 (the UTC date); the recipient's dates are Oct 4 / Oct 6.
    [Fact]
    public async Task PublishAsync_TaskDueDates_CountFromTheRecipientsLocalToday()
    {
        var h = BuildHarness(
            ruleThreshold: 0.7m,
            clock: new FakeTimeProvider(DateTimeOffset.Parse("2026-10-04T01:04:37Z", System.Globalization.CultureInfo.InvariantCulture)),
            recipientTimeZoneCode: 35,
            recipientZoneName: "Eastern Standard Time");

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        var task = h.Creates.Single(e => e.LogicalName == "sprk_event");
        task.GetAttributeValue<DateTime>("sprk_duedate").Should().Be(new DateTime(2026, 10, 4));
        task.GetAttributeValue<DateTime>("sprk_finalduedate").Should().Be(new DateTime(2026, 10, 6));
        task.GetAttributeValue<DateTime>("sprk_duedate").Kind.Should().Be(DateTimeKind.Unspecified,
            "a Date Only column stores the date as written; a Local kind would be converted to UTC first");
    }

    // ── Deny: below-threshold confidence → NO seam call, NO outbox, NO ping, NO appnotification. ──
    [Fact]
    public async Task PublishAsync_WhenGateDenies_ProducesNoSeamCallNoOutboxNoPingNoAppnotification()
    {
        var h = BuildHarness(ruleThreshold: 0.9m);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.5)); // 0.5 < 0.9 → deny

        h.Creates.Should().BeEmpty("a denied assessment creates no task, no outbox row, and no appnotification");
        h.Delivery.Pings.Should().BeEmpty("a denied assessment fires no Layer-C ping");
        h.Events.Should().BeEmpty("the RI action path is never entered on deny (structural short-circuit)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════
    // Ontology task 042 (FR-23): the Decision Record on BOTH gate paths, through the REAL chain.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly System.Xml.Linq.XNamespace NoNs = System.Xml.Linq.XNamespace.None;

    /// <summary>The per-matter query the spec names (success criterion 3): every outcome for one matter, newest first.</summary>
    private static string PerMatterFetchXml(Guid matterId) => $@"
<fetch>
  <entity name='sprk_decisionrecord'>
    <attribute name='sprk_decisionoutcome' />
    <attribute name='sprk_reason' />
    <filter><condition attribute='sprk_regardingmatter' operator='eq' value='{matterId}' /></filter>
    <order attribute='sprk_decidedon' descending='true' />
  </entity>
</fetch>";

    /// <summary>Runs the FetchXML's single <c>eq</c> condition over the created rows: what the column filter would return.</summary>
    private static List<DataverseEntity> Run(string fetchXml, IEnumerable<DataverseEntity> rows)
    {
        var doc = System.Xml.Linq.XDocument.Parse(fetchXml);
        var entityName = doc.Root!.Element(NoNs + "entity")!.Attribute("name")!.Value;
        var condition = doc.Descendants(NoNs + "condition").Single();
        var attribute = condition.Attribute("attribute")!.Value;
        condition.Attribute("operator")!.Value.Should().Be("eq");
        var value = Guid.Parse(condition.Attribute("value")!.Value);
        return rows.Where(r => r.LogicalName == entityName
                               && r.GetAttributeValue<EntityReference>(attribute)?.Id == value).ToList();
    }

    [Fact]
    public async Task PublishAsync_WhenGateAuthorizes_QueuesTheDecisionBeforeTheRiAction_TheRiActionRunsWithoutTheRecord_AndTheJobWritesAnAuthorizedRecord()
    {
        var h = BuildHarness(ruleThreshold: 0.7m, withDecisionRecords: true);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        // Task 149 (D-139): one job is queued BEFORE the branch, and the RI action ran to its end without waiting for (or having) the record.
        h.QueuedJobs.Should().ContainSingle();
        h.DecisionRecords.Should().BeEmpty("the gate writes nothing: the job does");
        h.Events.Should().Equal("decision-job-enqueue", "seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create");

        var outcomes = await h.RunQueuedJobsAsync();

        outcomes.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Completed);
        var record = h.DecisionRecords.Should().ContainSingle("criterion 1: an authorize writes a Decision Record").Subject;
        record.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000000, "Authorized");
        record.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000000, "Judgement");
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(GateDecisionRecordJobHandler.ProposedActionCode);
        record.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(h.CommunicationId);
        record.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(h.MatterId);
        record.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(h.MatterId);
        record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId);
        record.Contains("sprk_gatesessionid").Should().BeFalse("criterion 4 as amended: null for enrichment-gate records");
        record.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain(h.RuleId.ToString("D")).And.Contain(h.CommunicationId.ToString("D"));

        // The record is written AFTER the RI action, by the job: the RI action did not wait for it.
        h.Events.Should().Equal(
            "decision-job-enqueue", "seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create", "decision-record-create");
    }

    [Fact]
    public async Task PublishAsync_WhenGateDenies_QueuesTheDecision_TheJobWritesADeniedRecord_AndTheGateStillProducesNoSideEffect()
    {
        var h = BuildHarness(ruleThreshold: 0.9m, withDecisionRecords: true);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.5));

        h.QueuedJobs.Should().ContainSingle("a deny queues its decision too");
        h.DecisionRecords.Should().BeEmpty();
        h.Events.Should().Equal("decision-job-enqueue");

        await h.RunQueuedJobsAsync();

        var record = h.DecisionRecords.Should().ContainSingle("criterion 2: a deny writes a Decision Record").Subject;
        record.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000001, "Denied");
        record.Contains("sprk_actioncode").Should().BeFalse();
        record.Contains("sprk_action").Should().BeFalse();
        record.GetAttributeValue<string>("sprk_reason").Should().Be("rule-matched-confidence-below-threshold");
        h.Creates.Should().BeEmpty("the record is written by the ontology writer identity; a deny still creates no task, outbox row or appnotification");
        h.Delivery.Pings.Should().BeEmpty();
        h.Events.Should().Equal("decision-job-enqueue", "decision-record-create");
    }

    [Fact]
    public async Task PublishAsync_ForOneMatter_BothOutcomesAreQueryablePerMatter_Criterion3()
    {
        var h = BuildHarness(ruleThreshold: 0.7m, withDecisionRecords: true);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9)); // authorize
        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.4)); // deny: below the rule's 0.7
        await h.RunQueuedJobsAsync();

        var found = Run(PerMatterFetchXml(h.MatterId), h.DecisionRecords);
        found.Select(r => r.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value)
            .Should().BeEquivalentTo(new[] { 100000000, 100000001 }, "the per-matter query returns the authorize AND the deny");
        Run(PerMatterFetchXml(Guid.NewGuid()), h.DecisionRecords).Should().BeEmpty("and only that matter's");
    }

    [Fact]
    public async Task PublishAsync_ARetryOfTheSameEnrichment_QueuesTheDecisionAgain_ButOnlyOneRecordIsWritten_D139()
    {
        // Enrichment is retried: the gate evaluates again (with a new run id) and queues again. Two jobs, the same idempotency key, ONE record.
        var h = BuildHarness(ruleThreshold: 0.7m, withDecisionRecords: true);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));
        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));
        var outcomes = await h.RunQueuedJobsAsync();

        h.QueuedJobs.Should().HaveCount(2);
        h.QueuedJobs.Select(j => j.IdempotencyKey).Distinct().Should().ContainSingle()
            .Which.Should().Be(GateDecisionKey.Of(h.CommunicationId, authorize: true));
        h.QueuedJobs.Select(j => j.JobId).Distinct().Should().HaveCount(2);
        outcomes.Should().HaveCount(2).And.OnlyContain(o => o.Status == JobStatus.Completed);
        h.DecisionRecords.Should().ContainSingle("the second job found the first job's record and wrote nothing");
    }

    [Fact]
    public async Task PublishAsync_WhenTheJobCannotWriteTheRecord_TheRiActionHasAlreadyRun_AndTheJobFailsVisibly()
    {
        var h = BuildHarness(ruleThreshold: 0.7m, withDecisionRecords: true);
        h.WriterOrg.Setup(o => o.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse"));

        var act = () => h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        await act.Should().NotThrowAsync("the gate's outcome and the enrichment that triggered it are unaffected by a lost record");
        h.Events.Should().Equal("decision-job-enqueue", "seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create");

        var outcomes = await h.RunQueuedJobsAsync();

        outcomes.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Failed, "abandoned and redelivered by the processor, dead-lettered after the last delivery");
        h.DecisionRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenTheDecisionCannotBeQueued_TheGateOutcomeAndTheRiActionAreUnchanged()
    {
        var h = BuildHarness(ruleThreshold: 0.7m, withDecisionRecords: true);
        h.Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("service bus down"));

        var act = () => h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        await act.Should().NotThrowAsync("a degraded queue never fails the enrichment that triggered the assessment");
        h.QueuedJobs.Should().BeEmpty();
        h.Events.Should().Equal("seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create");
    }
}
