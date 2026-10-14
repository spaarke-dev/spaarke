using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 042 (FR-23): <see cref="RuleGatedAssessedConsumer"/> persists the communications gate's decision on BOTH paths from one
/// call site before the branch, through <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/>, and a failed write can never change
/// what the consumer does. The authorize path (which needs the real RI action chain) is in
/// <c>RiActionsViaSeamSeamTests</c>; every case here is a deny or a write failure, so the RI action service is never reached.
/// </summary>
/// <remarks>The writer's columns and option values are paired with the live schema in <c>DecisionRecordWriterTests.GateDecision.cs</c>.</remarks>
public sealed class RuleGatedAssessedConsumerDecisionRecordTests
{
    private static readonly Guid CommunicationId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RuleId = Guid.NewGuid();

    private static readonly AsyncLocal<Guid> MetricScope = new();

    private sealed class Rig
    {
        public DecisionRecordWriterTests.Harness Writer { get; } = new();
        public CapturingLogger<RuleGatedAssessedConsumer> Log { get; } = new();
        public RuleGatedAssessedConsumer Consumer { get; }

        /// <param name="rulePriority">The matched rule's priority; null: no rule matches the communication.</param>
        /// <param name="withWriter">False builds the consumer as the existing tests do (no Decision Record writer).</param>
        public Rig(Guid? matter = null, Guid? project = null, Guid? directPair = null, bool withRule = false, bool withWriter = true)
        {
            var communication = new Entity("sprk_communication", CommunicationId);
            if (matter is { } m) communication["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (project is { } p) communication["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (directPair is { } d) communication["sprk_regardingrecordid"] = d.ToString("D");
            Writer.Rows[("sprk_communication", CommunicationId)] = communication;

            var rules = new EntityCollection();
            if (withRule)
            {
                rules.Entities.Add(new Entity("sprk_communicationrule", RuleId)
                {
                    ["sprk_enabled"] = true,
                    ["sprk_priority"] = 100,
                    ["sprk_confidencethreshold"] = 0.9m,
                });
                Writer.Rows[("sprk_communicationrule", RuleId)] = new Entity("sprk_communicationrule", RuleId) { ["sprk_priority"] = 100 };
            }

            Writer.Entities
                .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationrule"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rules);

            var gate = new CommunicationRuleGate(Writer.Entities.Object, Options.Create(new CommsPolicyOptions()), NullLogger<CommunicationRuleGate>.Instance);
            // A deny never reaches the RI action service (invoked only inside `if (decision.Authorize)`).
            var ri = (CommunicationRiActionService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CommunicationRiActionService));
            Consumer = new RuleGatedAssessedConsumer(
                Writer.Entities.Object, gate, ri, Log, signalTrigger: null, decisionRecords: withWriter ? Writer.Writer : null);
        }

        public Task PublishAsync(double confidence = 0.5, CancellationToken ct = default) => Consumer.PublishAsync(
            new CommunicationAssessedSignal(CommunicationId, CommunicationDirection.Incoming, "Fee schedule change", "partner@firm.example", 1, confidence), ct);
    }

    private static JsonElement Snapshot(Entity record) => JsonDocument.Parse(record.GetAttributeValue<string>("sprk_factsnapshot")).RootElement.GetProperty("facts");

    [Fact]
    public async Task Deny_WithNoMatchingRule_WritesADeniedRecord_FiledUnderTheCommunication_AndTheMatterComesFromTheResolver()
    {
        var rig = new Rig(matter: MatterId);

        await rig.PublishAsync();

        var record = rig.Writer.Row;
        record.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000001, "Denied");
        record.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000000, "Judgement on both paths");
        record.GetAttributeValue<string>("sprk_reason").Should().Be("no-matching-rule");
        record.Contains("sprk_actioncode").Should().BeFalse();
        record.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        record.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        var facts = Snapshot(record);
        facts.GetProperty("communicationId").GetGuid().Should().Be(CommunicationId);
        facts.GetProperty("gateOutcome").GetString().Should().Be("Denied");
        facts.GetProperty("matchedRuleId").ValueKind.Should().Be(JsonValueKind.Null, "no rule matched");
    }

    [Fact]
    public async Task Deny_BelowTheMatchedRulesThreshold_RecordsTheRuleIdAndPriority_AndNoMessageContent()
    {
        var rig = new Rig(matter: MatterId, withRule: true);

        await rig.PublishAsync(confidence: 0.5);

        var record = rig.Writer.Row;
        record.GetAttributeValue<string>("sprk_reason").Should().Be("rule-matched-confidence-below-threshold");
        var facts = Snapshot(record);
        facts.GetProperty("matchedRuleId").GetGuid().Should().Be(RuleId);
        facts.GetProperty("matchedRulePriority").GetInt32().Should().Be(100);
        facts.GetProperty("confidence").GetDouble().Should().Be(0.5);
        facts.GetProperty("threshold").GetDouble().Should().Be(0.9);
        var all = record.GetAttributeValue<string>("sprk_factsnapshot") + record.GetAttributeValue<string>("sprk_steps");
        all.Should().NotContain("Fee schedule change").And.NotContain("partner@firm.example", "ids and numbers only, never the subject or sender");
    }

    [Fact]
    public async Task Deny_OfACommunicationDirectlyFiledUnderAProject_AlsoFiledUnderAMatter_TakesTheProjectAsCore_D37()
    {
        var rig = new Rig(matter: MatterId, project: ProjectId, directPair: ProjectId);

        await rig.PublishAsync();

        rig.Writer.Row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(ProjectId, "the pair id the consumer read decides D-37's first tie-break");
        rig.Writer.Row.Contains("sprk_matter").Should().BeFalse();
    }

    [Fact]
    public async Task APriorityThatCannotBeRead_StillRecordsTheDecision_WithoutIt()
    {
        var rig = new Rig(matter: MatterId, withRule: true);
        rig.Writer.Entities
            .Setup(e => e.RetrieveAsync("sprk_communicationrule", RuleId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));

        await rig.PublishAsync();

        var facts = Snapshot(rig.Writer.Row);
        facts.GetProperty("matchedRuleId").GetGuid().Should().Be(RuleId);
        facts.GetProperty("matchedRulePriority").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AWriterRefusal_NeverChangesTheConsumer_ItIsLoggedAtError()
    {
        var rig = new Rig(matter: MatterId);
        rig.Writer.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "no business unit"));

        var act = () => rig.PublishAsync();

        await act.Should().NotThrowAsync("a lost record must never fail the enrichment that triggered the assessment");
        rig.Writer.Created.Should().BeEmpty();
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionNotRecorded).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain(CommunicationId.ToString()).And.Contain(DecisionRecordRefusalReason.OwnerRefused);
        rig.Writer.Log.Entries.Should().Contain(e => e.EventId == DecisionRecordEvents.WriteRefused, "the writer logged and metered the refusal itself");
    }

    [Fact]
    public async Task AnUnclassifiedFailure_IsLoggedWithTheException_AndMetered_NotSwallowed()
    {
        // A Dataverse client timeout can surface as a TaskCanceledException the writer does not wrap (it only wraps non-cancellation
        // faults). The caller's token is NOT cancelled, so this is a failure, not a shutdown.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig(matter: MatterId);
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("timeout"));

        var act = () => rig.PublishAsync();

        await act.Should().NotThrowAsync();
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.GateDecisionNotRecorded).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeOfType<TaskCanceledException>();
        reasons.Should().Contain(DecisionRecordRefusalReason.GateRecordFailed, "App Insights sees the lost record (customMetrics ontology.writer.failures)");
    }

    [Fact]
    public async Task ACancelledCaller_IsNotSwallowed()
    {
        var rig = new Rig(matter: MatterId);
        using var cts = new CancellationTokenSource();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => rig.PublishAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.Log.Entries.Should().NotContain(e => e.EventId == DecisionRecordEvents.GateDecisionNotRecorded);
    }

    [Fact]
    public async Task WithoutAWriter_TheConsumerBehavesAsBefore_NoReadForTheRulePriority_NoRecord()
    {
        var rig = new Rig(matter: MatterId, withRule: true, withWriter: false);

        await rig.PublishAsync();

        rig.Writer.Created.Should().BeEmpty();
        rig.Writer.Entities.Verify(
            e => e.RetrieveAsync("sprk_communicationrule", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheRecordIsWrittenBeforeTheGateOutcomeIsActedOn_OneCallSiteForBothPaths()
    {
        // Structural pin: the consumer has ONE call to the gate-decision writer, and it precedes the authorize branch (FR-23: "a line
        // before the branch"). The behaviour is proved by the deny tests here and the authorize seam test.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Communication", "CommunicationRuleGate.cs"));
        var consumer = source[source.IndexOf("class RuleGatedAssessedConsumer", StringComparison.Ordinal)..];
        var publish = consumer[consumer.IndexOf("public async Task PublishAsync", StringComparison.Ordinal)..consumer.IndexOf("private async Task RecordDecisionAsync", StringComparison.Ordinal)];

        System.Text.RegularExpressions.Regex.Matches(consumer, @"WriteGateDecisionAsync\(").Count.Should().Be(1, "one call site");
        publish.IndexOf("RecordDecisionAsync(", StringComparison.Ordinal).Should().BePositive()
            .And.BeLessThan(publish.IndexOf("if (decision.Authorize)", StringComparison.Ordinal), "the write precedes the branch");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static (MeterListener Listener, List<string?> Reasons) ListenReasons(Guid scope)
    {
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
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
