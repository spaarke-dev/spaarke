using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using Ownership = Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 032: the three event triggers (classification, budget-revision create, association confirmed; D-12, D-48; spec FR-13,
/// FR-14a, FR-16). One handler, the nightly job's own scoped path, no second evaluator.
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). A partial of <see cref="SignalReevaluationJobTests"/> on purpose:
/// the trigger runs the SAME job, evaluator, writer and validator, so these tests reuse that class's in-memory Dataverse
/// seams (its <c>Harness</c>) instead of growing a second copy that could drift from the real wiring. Column names, entity
/// names and option values are the ones that class pins to the live read of spaarkedev1 (notes/031-evaluator-progress.md); this
/// task adds no schema.</para>
/// </remarks>
public partial class SignalReevaluationJobTests
{
    private static readonly Guid RevisionId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000d1");

    private static JobContract TriggerJob(SignalTriggerKind kind, string entity, Guid subject, Guid source) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = SignalEvaluationTrigger.JobTypeName,
        SubjectId = subject.ToString("D"),
        CorrelationId = "corr-032",
        IdempotencyKey = $"signal-eval-{SignalEvaluationTrigger.KindName(kind)}-{subject:D}-{source:D}",
        Payload = JsonSerializer.SerializeToDocument(new { kind = SignalEvaluationTrigger.KindName(kind), subjectEntity = entity, subjectId = subject, sourceId = source }),
    };

    private static SignalEvaluationTriggerHandler TriggerHandler(Harness h) =>
        new(h.Job, new SinkLogger<SignalEvaluationTriggerHandler>(h.Log));

    private static Task<JobOutcome> RunTriggerAsync(Harness h, SignalTriggerKind kind, Guid matter, Guid source) =>
        TriggerHandler(h).ProcessAsync(TriggerJob(kind, "sprk_matter", matter, source), default);

    // =====================================================================================
    // AC1 / AC9: classification and association-confirmed raise the Work Item through the same handler
    // =====================================================================================

    [Theory]
    [InlineData(SignalTriggerKind.Classification)]
    [InlineData(SignalTriggerKind.AssociationConfirmed)]
    public async Task Trigger_ForAMatterWhosePredicateHolds_CreatesTheSignal_ScopedToThatOneMatter(SignalTriggerKind kind)
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        var outcome = await RunTriggerAsync(h, kind, MatterId, CommunicationId);

        outcome.Status.Should().Be(JobStatus.Completed);
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|1");
        h.Sysadmin.Fetches.First(f => f.Contains("name=\"sprk_matter\"")).Should().Contain(MatterId.ToString("D"),
            "the subject query is compiled for the ONE matter, not every matter");
        h.Sysadmin.WriteCalls.Should().Be(0, "the evaluation core never writes through the shared reader");
    }

    // =====================================================================================
    // AC2: a budget revision clears the open Signal at once
    // =====================================================================================

    [Fact]
    public async Task BudgetRevisionTrigger_OpenPathBSignal_IsClosedConditionCleared()
    {
        var h = new Harness().WithPathBPolicy(); // the revision exists: the notExists half no longer holds, so no hit
        var signalId = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var outcome = await RunTriggerAsync(h, SignalTriggerKind.BudgetRevision, MatterId, RevisionId);

        outcome.Status.Should().Be(JobStatus.Completed);
        var update = h.Org.ConditionalUpdates.Should().ContainSingle().Subject;
        update.Id.Should().Be(signalId);
        update.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        h.Org.CreateCalls.Should().Be(0, "no Decision Record and no new Signal");
    }

    // =====================================================================================
    // AC6: a Signal a decision already closed as Acted stays Acted
    // =====================================================================================

    [Fact]
    public async Task BudgetRevisionTrigger_SignalAlreadyActed_IsNotClosedAgain()
    {
        var h = new Harness().WithPathBPolicy();
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddSeconds(-30), coreId: MatterId);

        var outcome = await RunTriggerAsync(h, SignalTriggerKind.BudgetRevision, MatterId, RevisionId);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.WriteCalls.Should().Be(0, "FR-16: a Resolved Signal is never closed again, so it stays Acted");
    }

    [Fact]
    public async Task BudgetRevisionTrigger_AcknowledgedSignal_IsStillClosed()
    {
        var h = new Harness().WithPathBPolicy();
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusAcknowledged, coreId: MatterId);

        await RunTriggerAsync(h, SignalTriggerKind.BudgetRevision, MatterId, RevisionId);

        h.Org.ConditionalUpdates.Should().ContainSingle()
            .Which.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
    }

    // =====================================================================================
    // Scope: the trigger touches the named subject only
    // =====================================================================================

    [Fact]
    public async Task Trigger_DoesNotCloseAnotherSubjectsOpenSignal()
    {
        var h = new Harness().WithPathBPolicy();
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusOpen, coreId: OtherMatterId);

        var outcome = await RunTriggerAsync(h, SignalTriggerKind.BudgetRevision, MatterId, RevisionId);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.WriteCalls.Should().Be(0, "the other matter's Signal belongs to its own evaluation; only the nightly pass sweeps all");
    }

    [Fact]
    public async Task Trigger_ForAnEntityNoPolicyIsAbout_EvaluatesNothing()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        var outcome = await TriggerHandler(h).ProcessAsync(TriggerJob(SignalTriggerKind.AssociationConfirmed, "sprk_project", ProjectId, CommunicationId), default);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Sysadmin.Fetches.Should().BeEmpty("a matter policy cannot hold or clear for a project");
        h.Org.WriteCalls.Should().Be(0);
    }

    // =====================================================================================
    // AC3: Do-lane policies are nightly only
    // =====================================================================================

    [Fact]
    public async Task Trigger_DoesNotEvaluateADoLanePolicy()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));

        var outcome = await TriggerHandler(h).ProcessAsync(TriggerJob(SignalTriggerKind.Classification, "sprk_todo", TodoId, CommunicationId), default);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Sysadmin.Fetches.Should().BeEmpty("the Do lane runs nightly only (cadence from sprk_lane)");
        h.Org.WriteCalls.Should().Be(0);

        // Control: the same To Do IS written by the nightly job, so the empty result above is the lane filter, not missing data.
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle();
    }

    // =====================================================================================
    // AC7: secure-child ownership is the writer's, exactly as nightly
    // =====================================================================================

    [Fact]
    public async Task Trigger_OnASecureMatter_WritesTheSignalOwnedByTheSecureRecordOwnersTeam()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Ownership.AnswerFor[MatterId] = Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

        var outcome = await RunTriggerAsync(h, SignalTriggerKind.Classification, MatterId, CommunicationId);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId));
    }

    [Fact]
    public async Task Trigger_OnAnOrdinaryMatter_IsWrittenNormally_NotOwnedBySecureTeam_NoSkip()
    {
        // Restricted and Limited exclude external contacts only (ADR-003); they are written like any matter (D-33, D-38).
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        await RunTriggerAsync(h, SignalTriggerKind.Classification, MatterId, CommunicationId);

        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("ownerid").Should().BeNull("an ordinary matter's Signal keeps the writer's default owner; only a Secure matter names the owner team");
    }

    // =====================================================================================
    // AC4: a failure is observable, never silent
    // =====================================================================================

    [Fact]
    public async Task Trigger_WhenTheSignalWriteFails_ReturnsFailure_AndLogsAnError()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.FailCreates = true;

        var outcome = await RunTriggerAsync(h, SignalTriggerKind.Classification, MatterId, CommunicationId);

        outcome.Status.Should().Be(JobStatus.Failed, "the message is abandoned and redelivered, then dead-lettered; never dropped");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId == SignalTriggerEvents.HandlerFailed);
    }

    [Fact]
    public async Task Trigger_WhenThePoliciesCannotBeRead_ReturnsFailure_AndLogsTheException()
    {
        var h = new Harness();
        var handler = new SignalEvaluationTriggerHandler(
            new SignalReevaluationJob(h.Evaluator, h.Writer, new ThrowingReads(), h.Cache, h.Clock, new SinkLogger<SignalReevaluationJob>(h.Log)),
            new SinkLogger<SignalEvaluationTriggerHandler>(h.Log));

        var outcome = await handler.ProcessAsync(TriggerJob(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId), default);

        outcome.Status.Should().Be(JobStatus.Failed);
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId == SignalTriggerEvents.HandlerFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"nightly\",\"subjectEntity\":\"sprk_matter\",\"subjectId\":\"2444af6d-e1f2-f011-8406-7ced8d1dc988\"}")]
    [InlineData("{\"kind\":\"classification\",\"subjectEntity\":\"sprk_matter\",\"subjectId\":\"not-a-guid\"}")]
    [InlineData("{\"kind\":\"classification\",\"subjectEntity\":\"sprk_matter\",\"subjectId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task Trigger_WithAnUnreadablePayload_IsPoisoned_AndLogged(string payload)
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var job = TriggerJob(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId);
        job.Payload = payload.Length == 0 ? null : JsonDocument.Parse(payload);

        var outcome = await TriggerHandler(h).ProcessAsync(job, default);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId == SignalTriggerEvents.HandlerFailed);
        h.Org.WriteCalls.Should().Be(0);
    }

    // =====================================================================================
    // AC5 / AC10: one evaluation path
    // =====================================================================================

    [Fact]
    public void TheHandler_HasNoEvaluatorOrWriterOfItsOwn_OnlyTheNightlyJobsScopedPath()
    {
        var parameters = typeof(SignalEvaluationTriggerHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        parameters.Should().BeEquivalentTo(new[] { typeof(SignalReevaluationJob), typeof(ILogger<SignalEvaluationTriggerHandler>) },
            "the triggers call the nightly job's own evaluate-plan-write path; a SignalEvaluator or SignalWriter here would be a second one");
        typeof(SignalEvaluationTriggerHandler).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(f => f.FieldType).Should().NotContain(new[] { typeof(SignalEvaluator), typeof(SignalWriter) });
    }

    [Fact]
    public async Task TheTrigger_AndTheNightlyJob_AgreeOnTheSameSubject()
    {
        var viaTrigger = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var nightly = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        await RunTriggerAsync(viaTrigger, SignalTriggerKind.Classification, MatterId, CommunicationId);
        await nightly.RunAsync();

        // The two harnesses mint their own policy and version row ids; everything else must match.
        static IEnumerable<(string Key, string Value)> Comparable(Entity e) => e.Attributes
            .Where(a => a.Key is not ("sprk_policy" or "sprk_policyversion"))
            .Select(a => (a.Key, Render(a.Value)));

        Comparable(viaTrigger.Org.CreatedPayloads.Should().ContainSingle().Subject).Should()
            .BeEquivalentTo(Comparable(nightly.Org.CreatedPayloads.Single()),
                "the same evaluator writes the same Signal whichever pass reached the matter");
    }

    private static string Render(object? value) => value switch
    {
        EntityReference r => $"{r.LogicalName}:{r.Id}",
        OptionSetValue o => o.Value.ToString(),
        DateTime => "<time>",
        null => "<null>",
        _ => value.ToString() ?? string.Empty,
    };

    // =====================================================================================
    // The enqueue side
    // =====================================================================================

    private sealed class Queue
    {
        public Mock<JobSubmissionService> Jobs { get; } = new(
            MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test-jobs" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);

        public List<JobContract> Plain { get; } = new();
        public List<LogEntry> Log { get; } = new();
        public FakeTimeProvider Clock { get; } = new(RunAt);

        public Queue()
        {
            Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback<JobContract, CancellationToken>((job, _) => Plain.Add(job)).Returns(Task.CompletedTask);
        }

        public SignalEvaluationTrigger Trigger() => new(Jobs.Object, new SinkLogger<SignalEvaluationTrigger>(Log));
    }

    [Fact]
    public async Task Enqueue_Classification_QueuesTheMainQueueJob_WithADeterministicKey()
    {
        var q = new Queue();

        (await q.Trigger().EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId)).Should().BeTrue();

        var job = q.Plain.Should().ContainSingle().Subject;
        job.JobType.Should().Be(SignalEvaluationTrigger.JobTypeName);
        job.IdempotencyKey.Should().Be($"signal-eval-classification-{MatterId:D}-{CommunicationId:D}");
        job.SubjectId.Should().Be(MatterId.ToString("D"));
    }

    [Fact]
    public async Task Enqueue_AssociationConfirmed_QueuesTheSameJobType()
    {
        var q = new Queue();

        await q.Trigger().EnqueueAsync(SignalTriggerKind.AssociationConfirmed, "sprk_matter", MatterId, CommunicationId);

        q.Plain.Should().ContainSingle().Which.JobType.Should().Be(SignalEvaluationTrigger.JobTypeName,
            "D-48: the same handler, a third enqueue site");
    }

    [Fact]
    public async Task Enqueue_BudgetRevision_QueuesImmediately_ForTheCommitRouteToCall()
    {
        var q = new Queue();

        await q.Trigger().EnqueueAsync(SignalTriggerKind.BudgetRevision, "sprk_matter", MatterId, RevisionId);

        q.Plain.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"signal-eval-budget-revision-{MatterId:D}-{RevisionId:D}");
    }

    [Fact]
    public async Task Enqueue_WhenTheBusStalls_GivesUpAfterTheTimeout_WithAWarning_AndNeverThrows()
    {
        var q = new Queue();
        q.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>(async (_, token) => await Task.Delay(Timeout.Infinite, token));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var queued = await q.Trigger().EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId);

        queued.Should().BeFalse();
        started.Elapsed.Should().BeLessThan(SignalEvaluationTrigger.EnqueueTimeout + TimeSpan.FromSeconds(5));
        q.Log.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.EventId == SignalTriggerEvents.EnqueueTimedOut);
        q.Log.Should().NotContain(e => e.EventId == SignalTriggerEvents.EnqueueFailed);
    }

    [Fact]
    public async Task Enqueue_WhenTheCallerCancels_IsNotReportedAsATimeout()
    {
        var q = new Queue();
        q.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>(async (_, token) => await Task.Delay(Timeout.Infinite, token));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var act = () => q.Trigger().EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        q.Log.Should().NotContain(e => e.EventId == SignalTriggerEvents.EnqueueTimedOut);
    }

    // The message the producer really builds, serialised exactly as JobSubmissionService does and read back as
    // ServiceBusJobProcessor does, must evaluate. Renaming a member of the payload record breaks this (the other handler tests
    // build their payload by hand and would not notice).
    [Theory]
    [InlineData(SignalTriggerKind.Classification)]
    [InlineData(SignalTriggerKind.BudgetRevision)]
    [InlineData(SignalTriggerKind.AssociationConfirmed)]
    public async Task TheMessageTheProducerBuilds_RoundTripsThroughTheWire_AndTheHandlerEvaluatesIt(SignalTriggerKind kind)
    {
        var q = new Queue();
        await q.Trigger().EnqueueAsync(kind, "sprk_matter", MatterId, CommunicationId);
        var produced = q.Plain.Should().ContainSingle().Subject;

        var wire = JsonSerializer.Serialize(produced, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false });
        var received = JsonSerializer.Deserialize<JobContract>(wire, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var outcome = await TriggerHandler(h).ProcessAsync(received, default);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.CreatedPayloads.Should().ContainSingle("the handler read the producer's payload and evaluated the matter");
    }

    [Fact]
    public async Task Enqueue_WhenTheQueueFails_ReturnsFalse_NeverThrows_AndLogsTheExceptionAtError()
    {
        var q = new Queue();
        q.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("bus down"));

        var queued = await q.Trigger().EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId);

        queued.Should().BeFalse();
        q.Log.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId == SignalTriggerEvents.EnqueueFailed)
            .Which.Message.Should().Contain("nightly pass");
    }

    [Fact]
    public async Task Enqueue_WithNoSubject_IsLoggedNotQueued()
    {
        var q = new Queue();

        (await q.Trigger().EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", Guid.Empty, CommunicationId)).Should().BeFalse();

        q.Plain.Should().BeEmpty();
        q.Log.Should().Contain(e => e.EventId == SignalTriggerEvents.EnqueueFailed);
    }

    // =====================================================================================
    // The classification call site (RuleGatedAssessedConsumer)
    // =====================================================================================

    private sealed class ConsumerHarness
    {
        public Mock<IGenericEntityService> Entities { get; } = new();
        public Mock<SignalEvaluationTrigger> Trigger { get; }
        public RuleGatedAssessedConsumer Consumer { get; }

        public ConsumerHarness(Guid? matter, bool classified, bool trigger = true)
        {
            var queue = new Queue();
            Trigger = new Mock<SignalEvaluationTrigger>(queue.Jobs.Object, NullLogger<SignalEvaluationTrigger>.Instance);
            Trigger.Setup(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var communication = new Entity("sprk_communication", CommunicationId);
            if (matter is { } m) communication["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (classified) communication["sprk_triagecategory"] = new EntityReference("sprk_triagecategory", Guid.NewGuid());
            Entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(communication);
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection()); // no comms rule: the RI gate denies, which never reaches the RI action service

            var gate = new CommunicationRuleGate(Entities.Object, Options.Create(new CommsPolicyOptions()), NullLogger<CommunicationRuleGate>.Instance);
            // The deny path never touches the RI action service (RuleGatedAssessedConsumer invokes it only inside `if (Authorize)`).
            var ri = (CommunicationRiActionService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CommunicationRiActionService));
            Consumer = new RuleGatedAssessedConsumer(Entities.Object, gate, ri, NullLogger<RuleGatedAssessedConsumer>.Instance,
                trigger ? Trigger.Object : null);
        }

        public Task PublishAsync() => Consumer.PublishAsync(
            new CommunicationAssessedSignal(CommunicationId, CommunicationDirection.Incoming, "Fee schedule change", "partner@firm.example", 1, 0.9));
    }

    [Fact]
    public async Task Classification_OfAFiledCommunication_EnqueuesTheMatterTrigger_Once()
    {
        var c = new ConsumerHarness(MatterId, classified: true);

        await c.PublishAsync();

        c.Trigger.Verify(t => t.EnqueueAsync(SignalTriggerKind.Classification, "sprk_matter", MatterId, CommunicationId,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        c.Trigger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true)]   // classified but not yet filed: the association-confirmed trigger covers it later (D-48)
    [InlineData(true, false)]   // filed but carries no classification: nothing the predicate reads has changed
    public async Task Classification_WithoutAMatterOrWithoutACategory_EnqueuesNothing(bool hasMatter, bool classified)
    {
        var c = new ConsumerHarness(hasMatter ? MatterId : null, classified);

        await c.PublishAsync();

        c.Trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Classification_StillRunsTheRiGate_WhenNoTriggerIsRegistered()
    {
        var c = new ConsumerHarness(MatterId, classified: true, trigger: false);

        var act = () => c.PublishAsync();

        await act.Should().NotThrowAsync();
        c.Entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A reader whose every read throws: the policy read fails, which a trigger must surface rather than swallow.</summary>
    private sealed class ThrowingReads : IGenericEntityService
    {
        private static Exception Boom() => new InvalidOperationException("Dataverse is unavailable.");
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) => throw Boom();
        public Task<EntityCollection> RetrieveMultipleAsync(Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken ct = default) => throw Boom();
        public Task<EntityCollection> RetrieveMultipleAsync(Microsoft.Xrm.Sdk.Query.FetchExpression fetch, CancellationToken ct = default) => throw Boom();
        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw Boom();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw Boom();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw Boom();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw Boom();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw Boom();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw Boom();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw Boom();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw Boom();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw Boom();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw Boom();
    }
}
