using System.Diagnostics.Metrics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 042 (FR-23) / task 149 (D-139): <see cref="RuleGatedAssessedConsumer"/> hands the communications gate's decision to the
/// queue on BOTH paths from one call site before the branch, and never writes the record or waits for it. A failed enqueue can never change
/// what the consumer does. The authorize path (which needs the real RI action chain) is in <c>RiActionsViaSeamSeamTests</c>, together with the
/// job that writes the record; every case here is a deny or an enqueue failure, so the RI action service is never reached. What the JOB does
/// is in <c>GateDecisionRecordJobTests</c>.
/// </summary>
public sealed class RuleGatedAssessedConsumerDecisionRecordTests
{
    private static readonly Guid CommunicationId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RuleId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly AsyncLocal<Guid> MetricScope = new();

    private sealed class Rig
    {
        public Mock<IGenericEntityService> Entities { get; } = new();
        public List<JobContract> Jobs { get; } = [];
        public Mock<JobSubmissionService> Queue { get; }
        public CapturingLogger<GateDecisionRecordEnqueuer> EnqueuerLog { get; } = new();
        public CapturingLogger<RuleGatedAssessedConsumer> Log { get; } = new();
        public RuleGatedAssessedConsumer Consumer { get; }

        /// <param name="withRule">A matching rule whose confidence threshold is 0.9 (and priority 100); false: no rule matches the communication.</param>
        /// <param name="withEnqueuer">False builds the consumer as the existing tests do (no Decision Record queueing).</param>
        /// <param name="enqueuer">A replacement enqueuer (a test double), instead of the real one over <see cref="Queue"/>.</param>
        public Rig(Guid? matter = null, Guid? project = null, Guid? directPair = null, bool withRule = false, bool withEnqueuer = true,
            Func<Rig, GateDecisionRecordEnqueuer>? enqueuer = null)
        {
            var communication = new Entity("sprk_communication", CommunicationId);
            if (matter is { } m) communication["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (project is { } p) communication["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (directPair is { } d) communication["sprk_regardingrecordid"] = d.ToString("D");
            Entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(communication);

            var rules = new EntityCollection();
            if (withRule)
            {
                rules.Entities.Add(new Entity("sprk_communicationrule", RuleId)
                {
                    ["sprk_enabled"] = true,
                    ["sprk_priority"] = 100,
                    ["sprk_confidencethreshold"] = 0.9m,
                });
            }

            Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationrule"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rules);

            Queue = new Mock<JobSubmissionService>(
                MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }), NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
            Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) => { Jobs.Add(job); return Task.CompletedTask; });

            var gate = new CommunicationRuleGate(Entities.Object, Options.Create(new CommsPolicyOptions()), NullLogger<CommunicationRuleGate>.Instance);
            // A deny never reaches the RI action service (invoked only inside `if (decision.Authorize)`).
            var ri = (CommunicationRiActionService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CommunicationRiActionService));
            var decisionRecords = !withEnqueuer ? null : enqueuer?.Invoke(this) ?? new GateDecisionRecordEnqueuer(Queue.Object, EnqueuerLog);
            Consumer = new RuleGatedAssessedConsumer(
                Entities.Object, gate, ri, Log, signalTrigger: null, decisionRecords: decisionRecords, timeProvider: new FakeTimeProvider(Now));
        }

        public Task PublishAsync(double confidence = 0.5, CancellationToken ct = default) => Consumer.PublishAsync(
            new CommunicationAssessedSignal(CommunicationId, CommunicationDirection.Incoming, "Fee schedule change", "partner@firm.example", 1, confidence), ct);

        public GateDecisionRecordPayload Payload(JobContract job) =>
            job.Payload!.Deserialize<GateDecisionRecordPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task Deny_WithNoMatchingRule_QueuesOneJobCarryingTheDecision_AndWritesNoRecordItself()
    {
        var rig = new Rig(matter: MatterId);

        await rig.PublishAsync(confidence: 0.5);

        var job = rig.Jobs.Should().ContainSingle("a deny queues its decision too (D-139: both paths)").Subject;
        job.JobType.Should().Be(GateDecisionRecordEnqueuer.JobTypeName);
        job.SubjectId.Should().Be(CommunicationId.ToString("D"));
        job.IdempotencyKey.Should().Be(GateDecisionKey.Of(CommunicationId, authorize: false));
        var payload = rig.Payload(job);
        payload.CommunicationId.Should().Be(CommunicationId);
        payload.Authorize.Should().BeFalse();
        payload.Reason.Should().Be("no-matching-rule");
        payload.Confidence.Should().Be(0.5);
        payload.Threshold.Should().Be(new CommsPolicyOptions().DefaultConfidenceThreshold);
        payload.MatchedRuleId.Should().BeNull();
        payload.PrivilegeFlagged.Should().BeFalse();
        payload.DecidedOn.Should().Be(Now, "the record states when the gate DECIDED, not when a (possibly retried) job runs");
        job.CorrelationId.Should().Be(payload.EvaluationId.ToString("D"));
        rig.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deny_BelowTheMatchedRulesThreshold_QueuesTheRuleIdAndTheNumbers_AndTheConsumerNoLongerReadsThePriority()
    {
        var rig = new Rig(matter: MatterId, withRule: true);

        await rig.PublishAsync(confidence: 0.5);

        var payload = rig.Payload(rig.Jobs.Single());
        payload.Reason.Should().Be("rule-matched-confidence-below-threshold");
        payload.MatchedRuleId.Should().Be(RuleId);
        payload.Confidence.Should().Be(0.5);
        payload.Threshold.Should().Be(0.9);
        rig.Entities.Verify(
            e => e.RetrieveAsync("sprk_communicationrule", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "D-139: the request that read the rule's priority left the gate's path; the job reads it");
    }

    [Fact]
    public async Task TheJobPayload_HoldsIdsAndTheGatesNumbersOnly_NeverTheSubjectOrTheSender_D123()
    {
        var rig = new Rig(matter: MatterId, withRule: true);

        await rig.PublishAsync(confidence: 0.5);

        var job = rig.Jobs.Single();
        var text = job.Payload!.RootElement.GetRawText();
        text.Should().NotContain("Fee schedule change").And.NotContain("partner@firm.example", "ids and numbers only, never the subject or sender");
        job.SubjectId.Should().NotContain("Fee").And.NotContain("partner");
        job.Payload.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            new[]
            {
                "CommunicationId", "EvaluationId", "Authorize", "Reason", "Confidence", "Threshold", "MatchedRuleId", "PrivilegeFlagged",
                "DirectFiledUnderId", "DecidedOn",
            },
            "a closed set: a new field is a conscious change to this contract and to D-123's reading of it");
    }

    [Fact]
    public async Task Deny_OfACommunicationDirectlyFiledUnderAProject_QueuesThePairIdForD37()
    {
        var rig = new Rig(matter: MatterId, project: ProjectId, directPair: ProjectId);

        await rig.PublishAsync();

        rig.Payload(rig.Jobs.Single()).DirectFiledUnderId.Should().Be(ProjectId, "the pair id the consumer read decides D-37's first tie-break in the job");
    }

    [Fact]
    public async Task ARepeatedEnrichment_QueuesAgain_WithANewRunId_ButTheSameIdempotencyKey()
    {
        var rig = new Rig(matter: MatterId);

        await rig.PublishAsync();
        await rig.PublishAsync();

        rig.Jobs.Should().HaveCount(2);
        rig.Jobs.Select(j => j.IdempotencyKey).Distinct().Should().ContainSingle().Which.Should().Be(GateDecisionKey.Of(CommunicationId, authorize: false));
        rig.Jobs.Select(j => rig.Payload(j).EvaluationId).Distinct().Should().HaveCount(2, "each run has its own id; it is not the record's key");
    }

    [Fact]
    public async Task AFailedEnqueue_NeverChangesTheConsumer_ItIsLoggedWithTheException_AndMetered()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig(matter: MatterId);
        rig.Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("service bus down"));

        var act = () => rig.PublishAsync();

        await act.Should().NotThrowAsync("a lost record must never fail the enrichment that triggered the assessment");
        rig.Jobs.Should().BeEmpty();
        var entry = rig.EnqueuerLog.Entries.Should().ContainSingle(e => e.EventId == GateDecisionRecordEvents.EnqueueFailed).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeOfType<InvalidOperationException>();
        entry.Message.Should().Contain(CommunicationId.ToString("D"));
        entry.Message.Should().NotContain("Fee schedule change").And.NotContain("partner@firm.example");
        reasons.Should().Equal(DecisionRecordRefusalReason.GateEnqueueFailed);
    }

    [Fact]
    public async Task AnEnqueueThatTimesOut_IsAbandoned_LoggedAtWarning_AndMetered_TheGateGoesOn()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig(matter: MatterId);
        rig.Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContract _, CancellationToken token) => await Task.Delay(Timeout.Infinite, token));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var act = () => rig.PublishAsync();

        await act.Should().NotThrowAsync();
        started.Elapsed.Should().BeLessThan(GateDecisionRecordEnqueuer.EnqueueTimeout + TimeSpan.FromSeconds(5));
        rig.EnqueuerLog.Entries.Should().ContainSingle(e => e.EventId == GateDecisionRecordEvents.EnqueueTimedOut).Which.Level.Should().Be(LogLevel.Warning);
        reasons.Should().Equal(DecisionRecordRefusalReason.GateEnqueueFailed);
    }

    [Fact]
    public async Task AnEnqueuerThatThrows_IsLoggedAtError_AndMetered_NotSwallowedQuietly()
    {
        // The real enqueuer never throws; this is the consumer's own belt and braces.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig(matter: MatterId, enqueuer: r =>
        {
            var throwing = new Mock<GateDecisionRecordEnqueuer>(r.Queue.Object, NullLogger<GateDecisionRecordEnqueuer>.Instance);
            throwing.Setup(e => e.EnqueueAsync(It.IsAny<GateDecisionRecordPayload>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
            return throwing.Object;
        });

        var act = () => rig.PublishAsync();

        await act.Should().NotThrowAsync();
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionNotRecorded).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeOfType<InvalidOperationException>();
        entry.Message.Should().Contain(CommunicationId.ToString());
        reasons.Should().Equal(DecisionRecordRefusalReason.GateEnqueueFailed);
    }

    [Fact]
    public async Task ACancelledCaller_IsNotSwallowed()
    {
        var rig = new Rig(matter: MatterId);
        using var cts = new CancellationTokenSource();
        rig.Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => rig.PublishAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.EnqueuerLog.Entries.Should().NotContain(e => e.EventId == GateDecisionRecordEvents.EnqueueFailed);
        rig.Log.Entries.Should().NotContain(e => e.EventId == DecisionRecordEvents.GateDecisionNotRecorded);
    }

    [Fact]
    public async Task WithoutAnEnqueuer_TheConsumerBehavesAsBefore_NothingIsQueued()
    {
        var rig = new Rig(matter: MatterId, withRule: true, withEnqueuer: false);

        await rig.PublishAsync();

        rig.Jobs.Should().BeEmpty();
        rig.Entities.Verify(
            e => e.RetrieveAsync("sprk_communicationrule", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheDecisionIsQueuedBeforeTheGateOutcomeIsActedOn_OneCallSiteForBothPaths_AndTheConsumerNeverWritesTheRecord()
    {
        // Structural pin: the consumer has ONE enqueue of the gate decision, it precedes the authorize branch (FR-23: "a line before the
        // branch"), and the consumer no longer holds the writer (the record's one create site stays in the writer, ADR-004 job above it).
        // The behaviour is proved by the deny tests here and the authorize seam test.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Communication", "CommunicationRuleGate.cs"));
        var consumer = source[source.IndexOf("class RuleGatedAssessedConsumer", StringComparison.Ordinal)..];
        var publish = consumer[consumer.IndexOf("public async Task PublishAsync", StringComparison.Ordinal)..consumer.IndexOf("private async Task EnqueueDecisionRecordAsync", StringComparison.Ordinal)];

        System.Text.RegularExpressions.Regex.Matches(consumer, @"EnqueueAsync\(\s*new GateDecisionRecordPayload").Count.Should().Be(1, "one call site");
        publish.IndexOf("EnqueueDecisionRecordAsync(", StringComparison.Ordinal).Should().BePositive()
            .And.BeLessThan(publish.IndexOf("if (decision.Authorize)", StringComparison.Ordinal), "the enqueue precedes the branch");
        consumer.Should().NotContain("WriteGateDecisionAsync", "the consumer does not write the record");
        consumer.Should().NotContain("DecisionRecordWriter ", "nor does it hold the writer");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
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
