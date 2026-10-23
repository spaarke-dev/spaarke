using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> / LogEntry (internal, same assembly)
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 149 (D-139, D-144): <see cref="GateDecisionRecordJobHandler"/>, the ADR-004 job that writes the communications gate's
/// Decision Record after the gate has acted. The real writer, the real idempotency service (over an in-memory cache) and the real ownership
/// resolver (over an in-memory directory, for D-144) are used; only Dataverse is a double. Writer-level idempotency is in
/// <c>DecisionRecordWriterTests.GateIdempotency.cs</c>; the enqueue is in <c>RuleGatedAssessedConsumerDecisionRecordTests</c>.
/// </summary>
public sealed class GateDecisionRecordJobTests
{
    private static readonly Guid CommunicationId = Guid.NewGuid();
    private static readonly Guid CommunicationOwner = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid RuleId = Guid.NewGuid();
    private static readonly DateTimeOffset GateTime = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly AsyncLocal<Guid> MetricScope = new();

    private sealed class Rig
    {
        public DecisionRecordWriterTests.Harness Writer { get; }
        public IIdempotencyService Idempotency { get; }
        public CapturingLogger<GateDecisionRecordJobHandler> Log { get; } = new();
        public GateDecisionRecordJobHandler Handler { get; }

        /// <param name="filedUnderMatter">True: the communication is filed under a matter (mocked resolver, Owned). False: it stands alone.</param>
        /// <param name="realResolver">The REAL ownership resolver over an in-memory directory (null: the mocked resolver).</param>
        public Rig(bool filedUnderMatter = true, IRecordOwnershipResolver? realResolver = null)
        {
            Writer = new DecisionRecordWriterTests.Harness(realResolver: realResolver);
            if (filedUnderMatter)
            {
                Writer.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                };
            }

            Writer.Rows[("sprk_communicationrule", RuleId)] = new Entity("sprk_communicationrule", RuleId) { ["sprk_priority"] = 100 };
            Idempotency = new IdempotencyService(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance);
            Handler = new GateDecisionRecordJobHandler(Writer.Writer, Writer.Entities.Object, Idempotency, Log);
        }

        public Task<JobOutcome> RunAsync(GateDecisionRecordPayload payload, Guid? jobId = null, CancellationToken ct = default) =>
            Handler.ProcessAsync(Job(payload, jobId), ct);
    }

    private static GateDecisionRecordPayload Payload(
        bool authorize = true, Guid? evaluation = null, Guid? rule = null, string? reason = null) => new(
        CommunicationId, evaluation ?? Guid.NewGuid(), authorize,
        reason ?? (authorize ? "rule-matched-confidence-met" : "rule-matched-confidence-below-threshold"),
        Confidence: 0.9, Threshold: 0.7, MatchedRuleId: rule ?? RuleId, PrivilegeFlagged: false, DirectFiledUnderId: null, DecidedOn: GateTime);

    private static JobContract Job(GateDecisionRecordPayload payload, Guid? jobId = null, int attempt = 1) => new()
    {
        JobId = jobId ?? Guid.NewGuid(),
        JobType = GateDecisionRecordEnqueuer.JobTypeName,
        SubjectId = payload.CommunicationId.ToString("D"),
        CorrelationId = payload.EvaluationId.ToString("D"),
        IdempotencyKey = GateDecisionKey.Of(payload.CommunicationId, payload.Authorize),
        Attempt = attempt,
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    private static int Option(Entity e, string column) => e.GetAttributeValue<OptionSetValue>(column).Value;

    [Fact]
    public void TheHandlerRoutesOnTheEnqueuersJobType()
    {
        new Rig().Handler.JobType.Should().Be(GateDecisionRecordEnqueuer.JobTypeName).And.Be("GateDecisionRecord");
    }

    [Fact]
    public async Task AnAuthorizeJob_WritesOneJudgementRecord_WithTheGatesTimeAndNumbers_AndReadsThePriorityAtJobTime()
    {
        var rig = new Rig();
        var evaluation = Guid.NewGuid();

        var outcome = await rig.RunAsync(Payload(authorize: true, evaluation: evaluation));

        outcome.Status.Should().Be(JobStatus.Completed);
        var record = rig.Writer.Row;
        Option(record, "sprk_decisionoutcome").Should().Be(100000000, "Authorized");
        Option(record, "sprk_recordclass").Should().Be(100000000, "Judgement");
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(GateDecisionRecordJobHandler.ProposedActionCode);
        record.GetAttributeValue<string>("sprk_reason").Should().Be("rule-matched-confidence-met");
        record.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(GateTime.UtcDateTime, "the record states when the gate decided");
        record.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        record.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        record.Contains("sprk_confirmedby").Should().BeFalse();
        var facts = JsonDocument.Parse(record.GetAttributeValue<string>("sprk_factsnapshot")).RootElement.GetProperty("facts");
        facts.GetProperty("correlationId").GetGuid().Should().Be(evaluation);
        facts.GetProperty("communicationId").GetGuid().Should().Be(CommunicationId);
        facts.GetProperty("gateOutcome").GetString().Should().Be("Authorized");
        facts.GetProperty("matchedRuleId").GetGuid().Should().Be(RuleId);
        facts.GetProperty("matchedRulePriority").GetInt32().Should().Be(100, "read when the job runs: the gate's path no longer pays that request");
        facts.GetProperty("confidence").GetDouble().Should().Be(0.9);
        facts.GetProperty("threshold").GetDouble().Should().Be(0.7);
    }

    [Fact]
    public async Task ADenyJob_WritesADeniedRecord_NoActionCode()
    {
        var rig = new Rig();

        await rig.RunAsync(Payload(authorize: false));

        Option(rig.Writer.Row, "sprk_decisionoutcome").Should().Be(100000001, "Denied");
        rig.Writer.Row.Contains("sprk_actioncode").Should().BeFalse();
    }

    [Fact]
    public async Task TheSameJobProcessedTwice_WritesExactlyOneRecord()
    {
        // A redelivery (the broker delivers a message at least once) is the same job id.
        var rig = new Rig();
        var payload = Payload();
        var job = Job(payload);

        var first = await rig.Handler.ProcessAsync(job, CancellationToken.None);
        var second = await rig.Handler.ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Completed);
        second.Status.Should().Be(JobStatus.Completed, "an already-recorded decision is a success, not a failure to retry");
        rig.Writer.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task TwoJobsForTheSameCommunicationAndOutcome_WriteExactlyOneRecord()
    {
        // An enrichment retry queues the decision again: another job id, another run id, the same (communication, outcome).
        var rig = new Rig();

        var first = await rig.RunAsync(Payload(authorize: true, evaluation: Guid.NewGuid()));
        var second = await rig.RunAsync(Payload(authorize: true, evaluation: Guid.NewGuid()));

        first.Status.Should().Be(JobStatus.Completed);
        second.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
        rig.Log.Entries.Should().Contain(e => e.EventId == GateDecisionRecordEvents.HandlerCompleted && e.Message.Contains("already recorded"));
    }

    [Fact]
    public async Task ADenyThenAnAuthorizeAfterARuleChange_AreTwoDecisions_TwoRecords()
    {
        var rig = new Rig();

        await rig.RunAsync(Payload(authorize: false));
        await rig.RunAsync(Payload(authorize: true));
        await rig.RunAsync(Payload(authorize: true));  // a retry of the authorize
        await rig.RunAsync(Payload(authorize: false)); // a retry of the deny

        rig.Writer.Created.Should().HaveCount(2);
        rig.Writer.Created.Select(r => Option(r, "sprk_decisionoutcome")).Should().BeEquivalentTo(new[] { 100000001, 100000000 });
    }

    [Fact]
    public async Task AJobWhileAnotherJobHoldsTheSameDecision_WritesNothing_AndCompletes_TheLockNarrowsTheRace()
    {
        // The writer's lookup is durable but cannot see a create still in flight; the lock is what keeps a concurrent copy out.
        var rig = new Rig();
        var createStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>(async (entity, _) =>
            {
                createStarted.SetResult();
                await release.Task;
                rig.Writer.Created.Add(entity);
                return rig.Writer.NewId;
            });

        var first = rig.RunAsync(Payload(authorize: true));
        await createStarted.Task;
        var second = await rig.RunAsync(Payload(authorize: true));

        second.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().BeEmpty("the second job did not create: the first still holds the decision");
        rig.Log.Entries.Should().Contain(e => e.EventId == GateDecisionRecordEvents.HandlerCompleted && e.Message.Contains("another job holds"));

        release.SetResult();
        (await first).Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task TheLock_IsReleasedWhenTheJobEnds_SoARedeliveryIsNeverBlockedByIt()
    {
        var rig = new Rig();
        var payload = Payload();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse"));

        var failed = await rig.RunAsync(payload);

        failed.Status.Should().Be(JobStatus.Failed);
        (await rig.Idempotency.TryAcquireProcessingLockAsync(GateDecisionKey.Of(CommunicationId, true), "another-owner", TimeSpan.FromMinutes(1)))
            .Should().BeTrue("a job that ended, however it ended, left no lock behind");
    }

    [Theory]
    [InlineData("nullPayload")]
    [InlineData("emptyCommunication")]
    [InlineData("emptyEvaluation")]
    [InlineData("blankReason")]
    [InlineData("notAnObject")]
    public async Task AnUnreadablePayload_IsPoisoned_NothingIsWritten_AndNoLockIsTaken(string flaw)
    {
        var rig = new Rig();
        var good = Payload();
        var job = Job(good);
        job.Payload = flaw switch
        {
            "nullPayload" => null,
            "emptyCommunication" => JsonSerializer.SerializeToDocument(good with { CommunicationId = Guid.Empty }),
            "emptyEvaluation" => JsonSerializer.SerializeToDocument(good with { EvaluationId = Guid.Empty }),
            "blankReason" => JsonSerializer.SerializeToDocument(good with { Reason = " " }),
            _ => JsonDocument.Parse("[1,2,3]"),
        };

        var outcome = await rig.Handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "a retry cannot fix a payload");
        rig.Writer.Created.Should().BeEmpty();
        rig.Writer.Lookups.Should().BeEmpty();
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == GateDecisionRecordEvents.HandlerFailed).Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task ARefusalARetryCannotChange_IsPoisoned_NotRetried()
    {
        var rig = new Rig();

        var outcome = await rig.RunAsync(Payload(reason: new string('x', 2001)));

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        rig.Writer.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnfiledCommunication_GetsOneRecord_OwnedByTheCommunicationsOwner_D144Amended()
    {
        // D-144 as amended (owner ruling on uac-r2 F1). REAL RecordOwnershipResolver over an in-memory directory.
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var rig = new Rig(filedUnderMatter: false, realResolver: directory.Resolver());
        rig.Writer.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["ownerid"] = new EntityReference("systemuser", CommunicationOwner),
            ["owner.isdisabled"] = new AliasedValue("systemuser", "isdisabled", false),
        };

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Completed);
        var record = rig.Writer.Row;
        record.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("systemuser", CommunicationOwner),
            "the communication's own owner, never the business unit's default team");
        record.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        // Idempotent for the unfiled path too.
        (await rig.RunAsync(Payload())).Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
    }

    [Theory]
    [InlineData("noBusinessUnit")]
    [InlineData("noOwner")]
    public async Task AnUnfiledCommunicationWithAMissingOrUnreadableOwner_WritesNothing_TheJobCompletes_CountedAsASkipNeverAFailure_D144Amended(string shape)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (failureListener, failures) = ListenReasons(scope);
        var (skipListener, skips) = ListenReasons(scope, "ontology.writer.skips");
        using var _ = failureListener;
        using var __ = skipListener;
        var directory = shape == "noOwner"
            ? Standard().WithRecord("sprk_communication", CommunicationId, ChildBu)
            : Standard().WithRecord("sprk_communication", CommunicationId, owningBusinessUnit: null);
        var rig = new Rig(filedUnderMatter: false, realResolver: directory.Resolver());
        rig.Writer.Rows[("sprk_communication", CommunicationId)] = shape == "noOwner"
            ? new Entity("sprk_communication", CommunicationId)
            : new Entity("sprk_communication", CommunicationId) { ["ownerid"] = new EntityReference("systemuser", CommunicationOwner) };

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Completed, "a retry meets the same condition: dead-lettering it would only bury a by-design skip");
        rig.Writer.Created.Should().BeEmpty();
        failures.Should().BeEmpty();
        skips.Should().Equal(DecisionRecordRefusalReason.GateSkippedNoUnitTeam);
        rig.Writer.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionSkippedNoUnitTeam).Which.Level.Should().Be(LogLevel.Warning);
        rig.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task AnUnfiledCommunicationWhoseOwnerIsDisabled_WritesNothing_TheJobCompletes_CountedAsASkipNeverAFailure_D144Amended()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (failureListener, failures) = ListenReasons(scope);
        var (skipListener, skips) = ListenReasons(scope, "ontology.writer.skips");
        using var _ = failureListener;
        using var __ = skipListener;
        var directory = Standard().WithRecord("sprk_communication", CommunicationId, ChildBu);
        var rig = new Rig(filedUnderMatter: false, realResolver: directory.Resolver());
        rig.Writer.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["ownerid"] = new EntityReference("systemuser", CommunicationOwner),
            ["owner.isdisabled"] = new AliasedValue("systemuser", "isdisabled", true),
        };

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Completed, "a retry meets the same condition: dead-lettering it would only page the failures alert");
        rig.Writer.Created.Should().BeEmpty();
        failures.Should().BeEmpty();
        skips.Should().Equal(DecisionRecordRefusalReason.GateSkippedOwnerDisabled);
    }

    [Fact]
    public async Task ATransientFault_IsAFailure_ForRedelivery_NotPoisoned_AndTheWriterMetersIt()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse"));

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Failed, "abandoned and redelivered by the processor; dead-lettered after the last delivery");
        reasons.Should().Equal(DecisionRecordRefusalReason.CreateFailed);
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == GateDecisionRecordEvents.HandlerFailed).Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task ACreateThatLandedButTimedOut_IsNotWrittenAgainOnRedelivery()
    {
        var rig = new Rig();
        var attempt = 0;
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((entity, _) =>
            {
                rig.Writer.Created.Add(entity); // the row lands...
                return ++attempt == 1 ? Task.FromException<Guid>(new TimeoutException("response lost")) : Task.FromResult(rig.Writer.NewId);
            });
        var job = Job(Payload());

        var first = await rig.Handler.ProcessAsync(job, CancellationToken.None);
        var redelivered = await rig.Handler.ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        redelivered.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle("the redelivery found the landed row by its key and wrote nothing");
    }

    [Fact]
    public async Task ALookupThatFaults_IsAFailure_AndNothingIsWritten()
    {
        var rig = new Rig();
        rig.Writer.LookupFault = new TimeoutException("dataverse");

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Writer.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnclassifiedException_IsAFailure_LoggedWithTheException_AndMetered()
    {
        // A Dataverse client timeout can surface as a TaskCanceledException the writer does not wrap. The host's token is NOT cancelled.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("timeout"));

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Failed);
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == GateDecisionRecordEvents.HandlerFailed).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeOfType<TaskCanceledException>();
        reasons.Should().Equal(DecisionRecordRefusalReason.GateRecordFailed);
    }

    [Fact]
    public async Task APriorityThatCannotBeRead_StillRecordsTheDecision_WithoutIt()
    {
        var rig = new Rig();
        rig.Writer.Entities
            .Setup(e => e.RetrieveAsync("sprk_communicationrule", RuleId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));

        var outcome = await rig.RunAsync(Payload());

        outcome.Status.Should().Be(JobStatus.Completed);
        var facts = JsonDocument.Parse(rig.Writer.Row.GetAttributeValue<string>("sprk_factsnapshot")).RootElement.GetProperty("facts");
        facts.GetProperty("matchedRuleId").GetGuid().Should().Be(RuleId);
        facts.GetProperty("matchedRulePriority").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ANoRuleDecision_ReadsNoPriority_AndRecordsNone()
    {
        var rig = new Rig();

        await rig.RunAsync(Payload(authorize: false, reason: "no-matching-rule") with { MatchedRuleId = null });

        rig.Writer.Entities.Verify(
            e => e.RetrieveAsync("sprk_communicationrule", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        JsonDocument.Parse(rig.Writer.Row.GetAttributeValue<string>("sprk_factsnapshot")).RootElement.GetProperty("facts")
            .GetProperty("matchedRuleId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ACancelledHost_IsNotSwallowed_AndTheLockIsStillReleased()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => rig.RunAsync(Payload(), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await rig.Idempotency.TryAcquireProcessingLockAsync(GateDecisionKey.Of(CommunicationId, true), "another-owner", TimeSpan.FromMinutes(1)))
            .Should().BeTrue("the release does not honour the cancelled token (task 068, #1086)");
    }

    [Fact]
    public async Task TheJobLogsAtInformationOnly_NamingTheCommunicationById_WhenItWritesOrFindsTheRecord()
    {
        var rig = new Rig();

        await rig.RunAsync(Payload());
        await rig.RunAsync(Payload()); // already recorded

        string.Join("\n", rig.Log.Entries.Select(e => e.Message)).Should().Contain(CommunicationId.ToString("D"));
        // The job never sees the subject or the sender (the payload test is in the consumer tests), so its lines cannot carry them (D-123).
        rig.Log.Entries.Should().OnlyContain(e => e.Level == LogLevel.Information);
    }

    private static (MeterListener Listener, List<string?> Reasons) ListenReasons(Guid scope, string instrumentName = "ontology.writer.failures")
    {
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == instrumentName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason") lock (reasons) reasons.Add(tag.Value as string);
        });
        listener.Start();
        return (listener, reasons);
    }
}
