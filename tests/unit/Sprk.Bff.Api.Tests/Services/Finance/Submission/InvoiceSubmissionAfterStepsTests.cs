using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 049 part D: the after-commit steps (<see cref="InvoiceSubmissionAfterSteps"/>). The outcomes come from the real processor
/// (part B harness) and the stored findings JSON from the real persister builder (part C); the Signal table is an in-memory fake keyed by
/// the writer's dedupe key; the automatic rejection is recorded by the REAL <see cref="BillingDecisionRecorder"/> over the writer's test
/// harness, so "one Decision Record" is the writer's own exactly-once, not a counter in this file.
/// </summary>
public class InvoiceSubmissionAfterStepsTests
{
    private static readonly Guid Submission = Guid.Parse("04900000-0000-4000-8000-0000000000b1");
    private static readonly Guid Matter = Guid.Parse("04900000-0000-4000-8000-0000000000a1");
    private static readonly Guid Document = Guid.Parse("04900000-0000-4000-8000-0000000000d1");
    private static readonly Guid SystemPolicyId = Guid.Parse("04900000-0000-4000-8000-0000000d0124");
    private static readonly Guid SystemVersionId = Guid.Parse("04900000-0000-4000-8000-0000000d0125");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(Submission);
    private static readonly Guid LineId = InvoiceSubmissionPersister.LineIdFor(Submission, 0);

    // ── Outcomes ──

    private static Task<SubmissionEvaluationOutcome> AutoRejected(string? readNumber = "INV-100")
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], ReadInvoiceNumber = readNumber };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        return harness.RunAsync();
    }

    private static Task<SubmissionEvaluationOutcome> Blocked()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        return harness.RunAsync();
    }

    private static Task<SubmissionEvaluationOutcome> Accepted(string? readNumber = "INV-100") => new ProcessorHarness { ReadInvoiceNumber = readNumber }.RunAsync();

    private static Task<SubmissionEvaluationOutcome> AcceptedUncoded()
    {
        var harness = new ProcessorHarness();
        harness.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");
        return harness.RunAsync();
    }

    private static SubmissionPersistResult Persisted(SubmissionEvaluationOutcome outcome, SubmissionPersistStatus status = SubmissionPersistStatus.Committed)
    {
        var (terminal, code) = InvoiceSubmissionPersister.TerminalFor(outcome);
        return new SubmissionPersistResult
        {
            Status = status,
            SubmissionId = Submission,
            InvoiceId = InvoiceId,
            TerminalStatus = terminal,
            TerminalReasonCode = code,
            ReasonCode = status == SubmissionPersistStatus.Retry ? InvoiceSubmissionPersister.TotalsFailedCode : null,
        };
    }

    // ── The world: submission row, Signal table, system policy, recorder, queue, cache, sink ──

    internal sealed class Logger : Microsoft.Extensions.Logging.ILogger<InvoiceSubmissionAfterSteps>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Crash : Exception
    {
        public Crash() : base("simulated process crash") { }
    }

    internal enum PolicyState { Present, Missing, Disabled, NoVersionInForce, WrongType }

    private sealed class World
    {
        public Entity Row { get; }
        public Dictionary<string, Entity> Signals { get; } = new(StringComparer.Ordinal);
        public List<SignalWriteRequest> Writes { get; } = [];
        public List<Guid> Superseded { get; } = [];
        public List<JobContract> Jobs { get; } = [];
        public List<SubmissionDecidedNotification> Notified { get; } = [];
        public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
        public List<Dictionary<string, object>> TerminalWrites { get; } = [];
        public List<QueryExpression> PolicyQueries { get; } = [];
        public List<QueryExpression> SignalQueries { get; } = [];
        public List<BillingDecision> Preflighted { get; } = [];
        public PolicyState Policy { get; set; } = PolicyState.Present;
        public HashSet<string> SkipPolicyCodes { get; } = new(StringComparer.Ordinal);
        public bool CloseChanges { get; set; }

        // Crash points (a fault at that call; the next run is the redelivery)
        public int CrashAtWrite { get; set; } = -1;
        public Exception? RecordFault { get; set; }
        public bool CrashAtRecord { get; set; }
        public Exception? JobFault { get; set; }
        public bool CrashAtNotifyRead { get; set; }
        public Exception? SinkFault { get; set; }
        public bool CrashAtTerminal { get; set; }

        /// <summary>Called inside the Signal read, before the rows are returned (a concurrent writer acting between read and write).</summary>
        public Action<string>? OnSignalRead { get; set; }

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public DecisionRecordWriterTests.Harness Records { get; }
        public BillingDecisionRecorder Recorder { get; }
        public Logger Log { get; } = new();

        public World(SubmissionEvaluationOutcome outcome, Func<JsonObject, JsonObject>? editFindings = null)
        {
            var json = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!.AsObject();
            json = editFindings?.Invoke(json) ?? json;
            Row = new Entity(FinanceBaseValues.SubmissionEntity, Submission)
            {
                [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", InvoiceId),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = json.ToJsonString(),
                ["sprk_channel"] = new OptionSetValue((int)outcome.Submission!.Channel),
                ["sprk_document"] = new EntityReference("sprk_document", Document),
                [InvoiceSubmissionPersister.SubmissionCorrelationId] = "corr-stored",
                [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing),
            };

            Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Row);
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
                {
                    "sprk_signal" => SignalRows(q),
                    "sprk_policy" => PolicyRows(q),
                    _ => throw new InvalidOperationException($"unexpected query on {q.EntityName}"),
                });
            Entities.Setup(e => e.UpdateAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    if (CrashAtTerminal)
                    {
                        throw new Crash();
                    }

                    TerminalWrites.Add(fields);
                    foreach (var (k, v) in fields)
                    {
                        Row[k] = v;
                    }

                    return Task.CompletedTask;
                });

            Records = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
            Records.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
            foreach (var version in (outcome.Evaluation?.Findings ?? []).Select(f => f.PolicyVersionId).Distinct())
            {
                Records.Rows[("sprk_policyversion", version)] = new Entity("sprk_policyversion", version); // F4-1: the record's version exists
            }
            var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
            Recorder = new BillingDecisionRecorder(Records.Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance);
        }

        public int RecordCreates => Records.Created.Count(e => e.LogicalName == "sprk_decisionrecord");

        public InvoiceSubmissionAfterSteps Build()
        {
            var idempotency = new Mock<IIdempotencyService>(MockBehavior.Strict);
            idempotency.Setup(i => i.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken _) => CrashAtNotifyRead ? throw new Crash() : Task.FromResult(Markers.Contains(key)));
            idempotency.Setup(i => i.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Returns((string key, TimeSpan? _, CancellationToken _) =>
                {
                    Markers.Add(key);
                    return Task.CompletedTask;
                });

            var sink = new Mock<IEbillingNotificationSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmissionDecidedAsync(It.IsAny<SubmissionDecidedNotification>(), It.IsAny<CancellationToken>()))
                .Returns((SubmissionDecidedNotification n, CancellationToken _) =>
                {
                    if (SinkFault is { } fault)
                    {
                        throw fault;
                    }

                    Notified.Add(n);
                    return Task.CompletedTask;
                });

            return new InvoiceSubmissionAfterSteps(
                Entities.Object,
                idempotency.Object,
                sink.Object,
                Log,
                WriteAsync,
                SupersedeAsync,
                (decision, ct) =>
                {
                    if (CrashAtRecord)
                    {
                        throw new Crash();
                    }

                    return RecordFault is { } fault ? Task.FromException<BillingDecisionRecordResult>(fault) : Recorder.RecordAsync(decision, ct);
                },
                decision =>
                {
                    Preflighted.Add(decision);
                    Recorder.Preflight(decision);
                },
                (job, _) =>
                {
                    if (JobFault is { } fault)
                    {
                        throw fault;
                    }

                    Jobs.Add(job);
                    return Task.CompletedTask;
                });
        }

        private EntityCollection SignalRows(QueryExpression q)
        {
            SignalQueries.Add(q);
            var prefix = (string)q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_dedupekey" && c.Operator == ConditionOperator.BeginsWith).Values[0];
            var rows = new EntityCollection(Signals.Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(s => Copy(s.Value)).ToList());
            OnSignalRead?.Invoke(prefix); // after the copy: what happens now is after this read
            return rows;
        }

        private EntityCollection PolicyRows(QueryExpression q)
        {
            PolicyQueries.Add(q);
            if (Policy == PolicyState.Missing)
            {
                return new EntityCollection();
            }

            var policy = new Entity("sprk_policy", SystemPolicyId)
            {
                ["sprk_policycode"] = BillingRuleCatalogue.SystemPolicyCode,
                ["sprk_enabled"] = Policy != PolicyState.Disabled,
                ["statecode"] = new OptionSetValue(0),
                ["sprk_currentversion"] = new EntityReference("sprk_policyversion", SystemVersionId),
                [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_lane"] = new OptionSetValue(SignalWriter.LaneDecide),
                ["v.statecode"] = new AliasedValue("sprk_policyversion", "statecode", new OptionSetValue(0)),
                ["v.sprk_ruletype"] = new AliasedValue("sprk_policyversion", "sprk_ruletype",
                    new OptionSetValue(Policy == PolicyState.WrongType ? (int)RuleType.InvoiceAge : (int)RuleType.SubmissionIntegrity)),
                ["v.sprk_consequence"] = new AliasedValue("sprk_policyversion", "sprk_consequence", new OptionSetValue((int)BillingConsequence.Flag)),
                ["v.sprk_inforcefrom"] = new AliasedValue("sprk_policyversion", "sprk_inforcefrom",
                    Policy == PolicyState.NoVersionInForce ? new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                ["v.sprk_versionnumber"] = new AliasedValue("sprk_policyversion", "sprk_versionnumber", 1),
            };
            return new EntityCollection([policy]);
        }

        private Task<SignalWriteResult> WriteAsync(SignalWriteRequest request, CancellationToken _)
        {
            if (Writes.Count == CrashAtWrite)
            {
                CrashAtWrite = -1;
                throw new Crash();
            }

            Writes.Add(request);
            if (SkipPolicyCodes.Contains(request.PolicyCode))
            {
                return Task.FromResult(new SignalWriteResult(Guid.Empty, false, Guid.Empty, Guid.Empty) { SkippedRefusalCode = "signal_core_unresolved" });
            }

            var key = SignalWriter.BuildDedupeKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D"), request.Episode);
            var evidence = JsonSerializer.Serialize(request.EvidenceRefs ?? [], new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (Signals.TryGetValue(key, out var existing))
            {
                existing["sprk_evidencerefs"] = evidence;
                existing.RowVersion = (long.Parse(existing.RowVersion!) + 1).ToString();
                return Task.FromResult(new SignalWriteResult(existing.Id, false, Guid.Empty, Guid.Empty));
            }

            var row = Seed(key, SignalWriter.SignalStatusOpen, evidence);
            return Task.FromResult(new SignalWriteResult(row.Id, true, Guid.Empty, Guid.Empty));
        }

        private Task<SignalCloseOutcome> SupersedeAsync(Guid id, string rowVersion, string policyCode, CancellationToken _)
        {
            var row = Signals.Values.Single(s => s.Id == id);
            if (CloseChanges || row.RowVersion != rowVersion)
            {
                return Task.FromResult(SignalCloseOutcome.ChangedSinceRead);
            }

            Superseded.Add(id);
            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row.RowVersion = (long.Parse(row.RowVersion!) + 1).ToString();
            return Task.FromResult(SignalCloseOutcome.Closed);
        }

        public Entity Seed(string key, int status, string evidence)
        {
            var row = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_dedupekey"] = key,
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["sprk_evidencerefs"] = evidence,
                RowVersion = "1",
            };
            Signals[key] = row;
            return row;
        }

        private static Entity Copy(Entity source)
        {
            var copy = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
            foreach (var (k, v) in source.Attributes)
            {
                copy[k] = v;
            }

            return copy;
        }

        public IEnumerable<string> KeysFor(string policyCode) => Signals.Keys.Where(k => k.StartsWith(policyCode + "|", StringComparison.Ordinal));
    }

    private static string EvidenceWithFingerprint(string fingerprint) =>
        JsonSerializer.Serialize(new[] { new { kind = BillingFindingSignalMapper.FingerprintEvidenceKind, @ref = fingerprint, tier = "Fact" } });

    private static string LineKey(string policyCode, int episode) =>
        SignalWriter.BuildDedupeKey(policyCode, "sprk_billingevent", LineId.ToString("D"), episode);

    private static string InvoiceKey(string policyCode, int episode) =>
        SignalWriter.BuildDedupeKey(policyCode, "sprk_invoice", InvoiceId.ToString("D"), episode);

    // ── Each kind ──

    [Fact(DisplayName = "049 D: AutoRejected - rule Signal from the stored plan, ONE Decision Record, no indexing, notify Rejected, terminal LAST")]
    public async Task AutoRejected_RunsEveryStep_InOrder()
    {
        var outcome = await AutoRejected();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        var world = new World(outcome);
        world.Entities.Invocations.Clear();

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        result.SignalsWritten.Should().Be(1);
        world.Writes.Should().ContainSingle().Which.Should().Match<SignalWriteRequest>(w =>
            w.PolicyCode == "BILL-AGE" && w.SubjectId == LineId && w.SubjectEntityLogicalName == "sprk_billingevent" && w.Episode == 1);
        world.Writes[0].SubjectDisplayName.Should().Be("INV-100.2 #1");
        world.KeysFor("BILL-AGE").Should().Equal(LineKey("BILL-AGE", 1));
        world.RecordCreates.Should().Be(1);
        result.DecisionRecordId.Should().NotBeNull();
        world.Jobs.Should().BeEmpty("a rejected invoice is not indexed");
        world.Notified.Should().ContainSingle().Which.Should().Be(new SubmissionDecidedNotification(
            Submission, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Spa, InvoiceId, outcome.ReasonCode));
        world.TerminalWrites.Should().ContainSingle();
        world.TerminalWrites[0][InvoiceSubmissionPersister.SubmissionStatus].Should().Be(new OptionSetValue((int)InvoiceSubmissionStatus.Rejected));
        world.TerminalWrites[0][InvoiceSubmissionPersister.SubmissionReasonCode].Should().Be(outcome.ReasonCode);
        world.Entities.Invocations.Last().Method.Name.Should().Be(nameof(IGenericEntityService.UpdateAsync), "the terminal status is the last write");
        world.PolicyQueries.Should().BeEmpty("no system finding: the system policy is not read");
    }

    [Fact(DisplayName = "049 D: Accepted - no Signals, no record, indexing enqueued with the deterministic id and key, notify Accepted, terminal Accepted")]
    public async Task Accepted_IndexesAndNotifies()
    {
        var outcome = await Accepted();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        var job = world.Jobs.Should().ContainSingle().Subject;
        job.JobType.Should().Be(InvoiceIndexingJobHandler.JobTypeName);
        job.JobId.Should().Be(InvoiceSubmissionAfterSteps.IndexingJobIdFor(InvoiceId));
        job.IdempotencyKey.Should().Be($"invoice-index-{InvoiceId}");
        job.CorrelationId.Should().Be("corr-1");
        job.Payload!.RootElement.GetProperty("InvoiceId").GetGuid().Should().Be(InvoiceId);
        job.Payload!.RootElement.GetProperty("DocumentId").GetGuid().Should().Be(Document);
        world.Notified.Should().ContainSingle().Which.Should().Be(new SubmissionDecidedNotification(
            Submission, SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Spa, InvoiceId, null));
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionStatus]
            .Should().Be(new OptionSetValue((int)InvoiceSubmissionStatus.Accepted));
    }

    [Fact(DisplayName = "049 D: AcceptedUncoded - the hold system Signal (policy present), indexing, notify Accepted")]
    public async Task AcceptedUncoded_WritesTheHoldSignal_AndIndexes()
    {
        var outcome = await AcceptedUncoded();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        var system = world.Writes.Should().ContainSingle().Subject;
        system.PolicyCode.Should().Be(BillingRuleCatalogue.SystemPolicyCode);
        system.PolicyId.Should().Be(SystemPolicyId);
        system.PolicyVersionId.Should().Be(SystemVersionId);
        system.SubjectId.Should().Be(InvoiceId);
        world.PolicyQueries.Should().ContainSingle("one app-only read per run");
        world.Jobs.Should().ContainSingle().Which.CorrelationId.Should().Be("corr-stored", "null correlation id uses the submission's");
        world.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Accepted);
        world.RecordCreates.Should().Be(0);
    }

    [Fact(DisplayName = "049 D: Blocked - the rule Signal as planned (073 decided it rejects; the channel blocked), no record, indexed, notified Rejected + blocked code")]
    public async Task Blocked_HeldSignal_Indexed_NotifiedRejectedBlocked()
    {
        var outcome = await Blocked();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        var signal = world.Writes.Should().ContainSingle().Subject;
        signal.PolicyCode.Should().Be("BILL-AGE");
        signal.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind, "not held: the finding decided");
        world.RecordCreates.Should().Be(0, "a held invoice is not decided; 105's Override / Return records it");
        world.Jobs.Should().ContainSingle();
        world.Notified.Should().ContainSingle().Which.Should().Be(new SubmissionDecidedNotification(
            Submission, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Internal, InvoiceId, SubmissionReasonCodes.Blocked));
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionReasonCode].Should().Be(SubmissionReasonCodes.Blocked);
    }

    [Fact(DisplayName = "049 D: a held group's stored billing.decision Held line is written with the Signal, text included")]
    public async Task AHeldPlanEntry_IsWrittenWithItsHeldLine()
    {
        var outcome = await Blocked();
        var world = new World(outcome, json =>
        {
            var entry = json["signalPlan"]!.AsArray()[0]!.AsObject();
            entry["decision"] = nameof(BillingGroupDecision.Held);
            entry["evidence"]!.AsArray().Add(new JsonObject
            {
                ["kind"] = BillingFindingSignalMapper.DecisionEvidenceKind,
                ["ref"] = BillingFindingSignalMapper.HeldDecisionRef,
                ["tier"] = "Fact",
                ["text"] = "read_field_below_threshold",
            });
            return json;
        });

        await world.Build().RunAsync(Persisted(outcome), null);

        var held = world.Writes.Single().EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Subject;
        held.Ref.Should().Be(BillingFindingSignalMapper.HeldDecisionRef);
        held.Text.Should().Be("read_field_below_threshold");
        held.Tier.Should().Be("Fact");
    }

    // ── Decision Record content and preflight ──

    [Fact(DisplayName = "049 D (notes s7): the automatic rejection's record - reject-invoice, deterministic id, automated decider, deciding codes, first deciding version, string Values")]
    public async Task TheRejectionDecision_HasTheSection7Shape()
    {
        var outcome = await AutoRejected();
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!;

        var decision = InvoiceSubmissionAfterSteps.BuildRejectionDecision(stored, Submission, InvoiceId)!;

        decision.DecisionId.Should().Be(InvoiceSubmissionAfterSteps.RejectionDecisionIdFor(Submission));
        InvoiceSubmissionAfterSteps.RejectionDecisionIdFor(Submission).Should().Be(InvoiceSubmissionAfterSteps.RejectionDecisionIdFor(Submission));
        decision.ActionCode.Should().Be("reject-invoice");
        decision.SubjectId.Should().Be(InvoiceId);
        decision.Decider.SystemUserId.Should().BeNull();
        decision.Decider.Role.Should().Be("automated: submission rejection rule");
        decision.Outcome.Should().Be("Rejected");
        var deciding = outcome.Decision!.DecidingFindings.Select(d => d.Finding).ToList();
        decision.Reason.Should().Be(string.Join(", ", deciding.Select(f => f.ReasonCode).Distinct()));
        decision.PolicyVersionId.Should().Be(deciding[0].PolicyVersionId);
        decision.Values!.Values.Should().OnlyContain(v => v is string);
        decision.ResolvedSignalIds.Should().BeNull("D-122 is blocked: no path closes a Signal as resolved by a billing decision (notes s13)");
        decision.Facts.Should().ContainKeys("reasons", "decidingReasonCodes", "reliabilityThreshold", "trackingNumber");
    }

    [Fact(DisplayName = "049 D: PreflightRejection runs the recorder's I/O-free checks on the same decision RunAsync records; nothing for other kinds")]
    public async Task PreflightRejection_ChecksTheSameDecision()
    {
        var rejected = await AutoRejected();
        var world = new World(rejected);
        var sut = world.Build();

        sut.PreflightRejection(rejected);
        sut.PreflightRejection(await Accepted());
        sut.PreflightRejection(await Blocked());

        world.Preflighted.Should().ContainSingle();
        world.Records.Created.Should().BeEmpty("preflight writes nothing");
        world.Entities.Invocations.Should().BeEmpty("preflight reads nothing");
        var recorded = world.Preflighted[0];
        (await sut.RunAsync(Persisted(rejected), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Records.Created.Single(e => e.LogicalName == "sprk_decisionrecord").GetAttributeValue<string>("sprk_actioncode").Should().Be(recorded.ActionCode);
    }

    [Fact(DisplayName = "049 D: a permanent record refusal is Refused, a transient one is Retry; neither writes the terminal status")]
    public async Task RecordRefusals_AreClassified()
    {
        var outcome = await AutoRejected();
        var permanent = new World(outcome) { RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.ReviewInvalid, "x") };
        var transient = new World(outcome) { RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.CreateFailed, "x") };

        var refused = await permanent.Build().RunAsync(Persisted(outcome), null);
        var retry = await transient.Build().RunAsync(Persisted(outcome), null);

        refused.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        refused.ReasonCode.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        retry.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        retry.ReasonCode.Should().Be(DecisionRecordRefusalReason.CreateFailed);
        permanent.TerminalWrites.Should().BeEmpty();
        transient.TerminalWrites.Should().BeEmpty();
        transient.Notified.Should().BeEmpty();
    }

    // ── Signal lifecycle (notes s6, option a) ──

    private static async Task<(World World, string Fingerprint)> RejectedWorldWithStoredFingerprint()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!;
        return (world, stored.Plan.Single().Fingerprint);
    }

    [Fact(DisplayName = "049 D lifecycle: an open Signal with the same fingerprint is refreshed in the same episode")]
    public async Task OpenSame_RefreshesTheSameEpisode()
    {
        var (world, fingerprint) = await RejectedWorldWithStoredFingerprint();
        world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusOpen, EvidenceWithFingerprint(fingerprint));

        var result = await world.Build().RunAsync(Persisted(await AutoRejected()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().ContainSingle().Which.Episode.Should().Be(1);
        world.Superseded.Should().BeEmpty();
        world.KeysFor("BILL-AGE").Should().Equal(LineKey("BILL-AGE", 1));
    }

    [Fact(DisplayName = "049 D lifecycle: an open Signal with a different fingerprint is closed Superseded, then episode n+1")]
    public async Task OpenDifferent_SupersedesThenNextEpisode()
    {
        var (world, _) = await RejectedWorldWithStoredFingerprint();
        world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusResolved, EvidenceWithFingerprint("old-a"));
        var open = world.Seed(LineKey("BILL-AGE", 2), SignalWriter.SignalStatusOpen, EvidenceWithFingerprint("old-b"));

        var result = await world.Build().RunAsync(Persisted(await AutoRejected()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Superseded.Should().Equal(open.Id);
        world.Writes.Should().ContainSingle().Which.Episode.Should().Be(3, "the latest episode is 2");
    }

    [Fact(DisplayName = "049 D lifecycle: an open Signal that changes while being superseded -> Retry, no new episode, no record, no terminal")]
    public async Task OpenDifferent_ChangedSinceRead_Retries()
    {
        var (world, _) = await RejectedWorldWithStoredFingerprint();
        world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusOpen, EvidenceWithFingerprint("old"));
        world.CloseChanges = true;

        var result = await world.Build().RunAsync(Persisted(await AutoRejected()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalChangedCode);
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.TerminalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D lifecycle (concurrent): a Signal another writer refreshes between the read and the close is not closed over; the redelivery completes")]
    public async Task ConcurrentRefresh_BetweenReadAndClose_RetriesThenCompletes()
    {
        var (world, _) = await RejectedWorldWithStoredFingerprint();
        var open = world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusOpen, EvidenceWithFingerprint("old"));
        world.OnSignalRead = _ =>
        {
            // The re-evaluation job (no lock; row-version conditional) refreshes the row right after this read.
            world.OnSignalRead = null;
            world.Signals[LineKey("BILL-AGE", 1)].RowVersion = "7";
        };
        var sut = world.Build();

        var first = await sut.RunAsync(Persisted(await AutoRejected()), null);
        var second = await sut.RunAsync(Persisted(await AutoRejected()), null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        second.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Superseded.Should().Equal(open.Id);
        world.KeysFor("BILL-AGE").Should().BeEquivalentTo(LineKey("BILL-AGE", 1), LineKey("BILL-AGE", 2));
        world.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "049 D lifecycle: a resolved Signal with the same fingerprint is skipped (a resume never re-raises it)")]
    public async Task ResolvedSame_IsSkipped()
    {
        var (world, fingerprint) = await RejectedWorldWithStoredFingerprint();
        world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusResolved, EvidenceWithFingerprint(fingerprint));

        var result = await world.Build().RunAsync(Persisted(await AutoRejected()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        result.SignalsWritten.Should().Be(0);
        world.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D lifecycle: a resolved Signal with a different fingerprint gets episode n+1 (no close)")]
    public async Task ResolvedDifferent_NextEpisode()
    {
        var (world, _) = await RejectedWorldWithStoredFingerprint();
        world.Seed(LineKey("BILL-AGE", 1), SignalWriter.SignalStatusResolved, EvidenceWithFingerprint("old"));

        await world.Build().RunAsync(Persisted(await AutoRejected()), null);

        world.Superseded.Should().BeEmpty();
        world.Writes.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Fact(DisplayName = "049 D: a write the writer skipped (SkippedRefusalCode) -> Retry; the other Signals are still written; no record, no terminal")]
    public async Task SkippedWrite_Retries_AfterWritingTheRest()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        world.SkipPolicyCodes.Add("BILL-AGE");

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalWriteSkippedCode);
        world.Writes.Select(w => w.PolicyCode).Should().BeEquivalentTo("BILL-AGE", BillingRuleCatalogue.SystemPolicyCode);
        result.SignalsWritten.Should().Be(1);
        world.RecordCreates.Should().Be(0);
        world.TerminalWrites.Should().BeEmpty();
    }

    // ── System policy (D-125) ──

    [Fact(DisplayName = "049 D (D-125): policy present - the mismatch is a system Signal on the invoice with the typed and read numbers")]
    public async Task SystemPolicyPresent_WritesTheMismatchSignal()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome);

        await world.Build().RunAsync(Persisted(outcome), null);

        var system = world.Writes.Should().ContainSingle().Subject;
        system.PolicyCode.Should().Be(BillingRuleCatalogue.SystemPolicyCode);
        system.SubjectEntityLogicalName.Should().Be("sprk_invoice");
        system.SubjectDisplayName.Should().Be(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson) is { } json
            ? JsonNode.Parse(json)!["invoiceName"]!.GetValue<string>() : null);
        SignalWriter.RenderSentence(system.MessageTemplate, system.FactValues).Should().Contain("INV-100").And.Contain("INV-999");
        world.KeysFor(BillingRuleCatalogue.SystemPolicyCode).Should().Equal(InvoiceKey(BillingRuleCatalogue.SystemPolicyCode, 1));
    }

    [Theory(DisplayName = "049 D (D-125): policy missing, disabled, without a version in force, or of the wrong type - no system Signal, one Warning with the code only, and the run completes")]
    [InlineData(PolicyState.Missing)]
    [InlineData(PolicyState.Disabled)]
    [InlineData(PolicyState.NoVersionInForce)]
    [InlineData(PolicyState.WrongType)]
    internal async Task SystemPolicyUnusable_NoSystemSignal_OneWarning_Continues(PolicyState state)
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome) { Policy = state };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().BeEmpty();
        world.PolicyQueries.Should().ContainSingle();
        var warning = world.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Subject;
        warning.Message.Should().Contain(BillingRuleCatalogue.SystemPolicyCode).And.NotContain("INV-999").And.NotContain("INV-100");
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 D (D-125, 130 b): stored refused codes and firm-code mismatches map to system Signals on the line")]
    public async Task StoredCodingFindings_MapToLineSignals()
    {
        var outcome = await Accepted();
        var world = new World(outcome, json =>
        {
            json["codingFindings"] = new JsonArray(new JsonObject
            {
                ["line"] = 0,
                ["refused"] = new JsonArray(new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = nameof(CodeRefusalReason.OutsideCodeSet) }),
                ["task"] = new JsonObject { ["code"] = "L110", ["firmCode"] = "L120", ["mismatch"] = true },
                ["activity"] = new JsonObject { ["code"] = "A103", ["firmCode"] = "A103", ["mismatch"] = false },
                ["expense"] = new JsonObject { ["code"] = null, ["firmCode"] = null, ["mismatch"] = false },
            });
            return json;
        });

        await world.Build().RunAsync(Persisted(outcome), null);

        var line = world.Writes.Should().ContainSingle().Subject;
        line.PolicyCode.Should().Be(BillingRuleCatalogue.SystemPolicyCode);
        line.SubjectId.Should().Be(LineId);
        line.SubjectDisplayName.Should().Be("INV-100.2 #1");
        line.EvidenceRefs!.Single(e => e.Kind == BillingFindingSignalMapper.FingerprintEvidenceKind).Ref
            .Should().Contain(BillingReasonCodes.CodingRefusedCode).And.Contain(BillingReasonCodes.CodingFirmCodeMismatch);
    }

    // ── Notification ──

    [Fact(DisplayName = "049 D: the notification is sent once per submission - a second run with the marker set does not notify again")]
    public async Task Notify_IsOnce_ByTheMarker()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        var sut = world.Build();

        await sut.RunAsync(Persisted(outcome), null);
        await sut.RunAsync(Persisted(outcome, SubmissionPersistStatus.Resumed), null);

        world.Notified.Should().ContainSingle();
        world.Markers.Should().Contain(InvoiceSubmissionAfterSteps.NotifyMarkerFor(Submission));
    }

    [Fact(DisplayName = "049 D: a sink fault is logged and does not retry the pipeline (the sink's contract); the terminal status is written")]
    public async Task SinkFault_IsLogged_AndTheRunCompletes()
    {
        var outcome = await Accepted();
        var world = new World(outcome) { SinkFault = new InvalidOperationException("sink down") };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Markers.Should().BeEmpty("not notified, so not marked");
        world.TerminalWrites.Should().ContainSingle();
        world.Log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("InvalidOperationException"))
            .And.NotContain(e => e.Message.Contains("sink down"));
    }

    // ── Indexing ──

    [Fact(DisplayName = "049 D: an indexing enqueue fault -> Retry before the notification and the terminal status")]
    public async Task IndexingFault_Retries()
    {
        var outcome = await Accepted();
        var world = new World(outcome) { JobFault = new InvalidOperationException("bus down") };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.IndexingEnqueueFailedCode);
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
    }

    // ── Resume after a crash at each step ──

    public enum CrashPoint { MidSignals, Record, Indexing, BeforeNotify, Terminal }

    [Theory(DisplayName = "049 D resume: a crash at each step leaves the submission Processing; the redelivery completes with no duplicate episode, one record, one notify, one terminal write")]
    [InlineData(CrashPoint.MidSignals)]
    [InlineData(CrashPoint.Record)]
    [InlineData(CrashPoint.BeforeNotify)]
    [InlineData(CrashPoint.Terminal)]
    public async Task Resume_AfterACrash_AutoRejected(CrashPoint point)
    {
        var outcome = await AutoRejected(readNumber: "INV-999"); // two Signals: the rule's and the system mismatch
        var world = new World(outcome);
        Arm(world, point);
        var sut = world.Build();

        var first = await Run(sut, Persisted(outcome));
        first.Should().NotBe(SubmissionAfterStepsStatus.Completed);
        world.TerminalWrites.Should().BeEmpty("any failure before step 5 leaves the submission Processing");
        world.Row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)InvoiceSubmissionStatus.Processing);

        Disarm(world);
        var second = await sut.RunAsync(Persisted(outcome, SubmissionPersistStatus.Resumed), null);

        second.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Signals.Keys.Should().BeEquivalentTo(LineKey("BILL-AGE", 1), InvoiceKey(BillingRuleCatalogue.SystemPolicyCode, 1));
        world.Writes.Should().OnlyContain(w => w.Episode == 1, "no duplicate episode");
        world.Superseded.Should().BeEmpty();
        world.RecordCreates.Should().Be(1, "one Decision Record");
        world.Notified.Should().ContainSingle();
        world.TerminalWrites.Should().ContainSingle();
    }

    [Theory(DisplayName = "049 D resume (Accepted): a crash at indexing, before notify or at the terminal write - re-enqueued with the same id and key, one notify, one terminal write")]
    [InlineData(CrashPoint.Indexing)]
    [InlineData(CrashPoint.BeforeNotify)]
    [InlineData(CrashPoint.Terminal)]
    public async Task Resume_AfterACrash_Accepted(CrashPoint point)
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        Arm(world, point);
        var sut = world.Build();

        (await Run(sut, Persisted(outcome))).Should().NotBe(SubmissionAfterStepsStatus.Completed);
        world.TerminalWrites.Should().BeEmpty();
        Disarm(world);
        (await sut.RunAsync(Persisted(outcome, SubmissionPersistStatus.Resumed), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        world.Jobs.Select(j => (j.JobId, j.IdempotencyKey)).Distinct().Should().ContainSingle("a resend is the same message (Service Bus duplicate detection)");
        world.Notified.Should().ContainSingle();
        world.TerminalWrites.Should().ContainSingle();
    }

    private static void Arm(World world, CrashPoint point)
    {
        switch (point)
        {
            case CrashPoint.MidSignals: world.CrashAtWrite = 1; break;
            case CrashPoint.Record: world.CrashAtRecord = true; break;
            case CrashPoint.Indexing: world.JobFault = new Crash(); break;
            case CrashPoint.BeforeNotify: world.CrashAtNotifyRead = true; break;
            case CrashPoint.Terminal: world.CrashAtTerminal = true; break;
        }
    }

    private static void Disarm(World world)
    {
        world.CrashAtWrite = -1;
        world.CrashAtRecord = false;
        world.JobFault = null;
        world.CrashAtNotifyRead = false;
        world.CrashAtTerminal = false;
    }

    private static async Task<SubmissionAfterStepsStatus?> Run(InvoiceSubmissionAfterSteps sut, SubmissionPersistResult persisted)
    {
        try
        {
            return (await sut.RunAsync(persisted, null)).Status;
        }
        catch (Crash)
        {
            return null;
        }
    }

    // ── Totals Retry, missing, mismatched, unreadable ──

    [Fact(DisplayName = "049 D: a totals Retry writes the Signals and the record, then returns Retry with the persister's code; no indexing, notify or terminal")]
    public async Task TotalsRetry_RunsSignalsAndRecordOnly()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome, SubmissionPersistStatus.Retry), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TotalsFailedCode);
        world.Writes.Should().ContainSingle();
        world.RecordCreates.Should().Be(1);
        world.Jobs.Should().BeEmpty();
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D: a missing submission, a link to another invoice, or unreadable findings write nothing")]
    public async Task MissingMismatchedOrUnreadable_WritesNothing()
    {
        var outcome = await Accepted();

        var missing = new World(outcome);
        missing.Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        (await missing.Build().RunAsync(Persisted(outcome), null)).Status.Should().Be(SubmissionAfterStepsStatus.SubmissionMissing);

        var mismatched = new World(outcome);
        mismatched.Row[InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", Guid.NewGuid());
        var refused = await mismatched.Build().RunAsync(Persisted(outcome), null);
        refused.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        refused.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.InvoiceLinkMismatchCode);

        var unreadable = new World(outcome);
        unreadable.Row[InvoiceSubmissionPersister.SubmissionFindingsJson] = "{ not json";
        (await unreadable.Build().RunAsync(Persisted(outcome), null)).ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.FindingsUnreadableCode);

        foreach (var world in new[] { missing, mismatched, unreadable })
        {
            world.Writes.Should().BeEmpty();
            world.Jobs.Should().BeEmpty();
            world.Notified.Should().BeEmpty();
            world.TerminalWrites.Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "049 D: a result with no committed invoice is refused as an argument")]
    public async Task NoInvoice_Throws()
    {
        var outcome = await Accepted();
        var sut = new World(outcome).Build();

        var act = () => sut.RunAsync(new SubmissionPersistResult { Status = SubmissionPersistStatus.Refused, SubmissionId = Submission }, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
