using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        /// <summary>Every write of <c>sprk_processingstatus</c>, conditional or not (an unconditional one is also in <see cref="UnconditionalStatusWrites"/>).</summary>
        public List<Dictionary<string, object>> TerminalWrites { get; } = [];
        public List<Dictionary<string, object>> UnconditionalStatusWrites { get; } = [];
        public List<string> FindingsWrites { get; } = [];
        public List<QueryExpression> PolicyQueries { get; } = [];
        public List<QueryExpression> VersionQueries { get; } = [];

        /// <summary>The system policy's versions (D-125); null = one current version built from <see cref="Policy"/>.</summary>
        public List<Entity>? PolicyVersions { get; set; }

        /// <summary>The policy's <c>sprk_currentversion</c>.</summary>
        public Guid PolicyRowsCurrentVersion { get; set; } = SystemVersionId;

        /// <summary>Called inside the conditional terminal write, before the row version is compared (a concurrent writer).</summary>
        public Action? OnTerminalWrite { get; set; }

        public Mock<IInvoiceTotalsService> Totals { get; } = new(MockBehavior.Strict);
        public InvoiceTotalsCalculation TotalsResult { get; set; } = new(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), null);
        public List<QueryExpression> SignalQueries { get; } = [];
        public List<BillingDecision> Preflighted { get; } = [];
        public PolicyState Policy { get; set; } = PolicyState.Present;
        public HashSet<string> SkipPolicyCodes { get; } = new(StringComparer.Ordinal);
        public bool CloseChanges { get; set; }

        // ── D-122 (notes 13c) ──
        /// <summary>The invoice's line ids, as the line query returns them (the outcome's lines by default).</summary>
        public List<Guid> Lines { get; }
        /// <summary>Every automated close: (signal, record, setReadAt, policy code).</summary>
        public List<(Guid Signal, Guid Record, DateTime SetReadAt, string PolicyCode)> Closes { get; } = [];
        /// <summary>A forced close answer per Signal (instead of the writer's read-then-decide rule).</summary>
        public Dictionary<Guid, SignalAutomatedCloseOutcome> CloseAnswers { get; } = [];
        /// <summary>A close that throws, per Signal.</summary>
        public Dictionary<Guid, Exception> CloseFaults { get; } = [];
        public List<QueryExpression> LineQueries { get; } = [];
        /// <summary>Every decision handed to the recorder (after a record crash point).</summary>
        public List<BillingDecision> RecordedDecisions { get; } = [];
        /// <summary>Automated closes that WROTE (Closed), as opposed to answered.</summary>
        public int ClosedWrites { get; private set; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        /// <summary>Called inside the open-set read, after the rows are copied (a writer acting after the read).</summary>
        public Action? OnSetRead { get; set; }

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
            Lines = Enumerable.Range(0, outcome.Read?.Lines.Count ?? 0).Select(i => InvoiceSubmissionPersister.LineIdFor(Submission, i)).ToList();
            Row = new Entity(FinanceBaseValues.SubmissionEntity, Submission)
            {
                [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", InvoiceId),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = json.ToJsonString(),
                ["sprk_channel"] = new OptionSetValue((int)outcome.Submission!.Channel),
                ["sprk_document"] = new EntityReference("sprk_document", Document),
                [InvoiceSubmissionPersister.SubmissionCorrelationId] = "corr-stored",
                [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing),
                ["versionnumber"] = 100L,
            };

            Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Copy(Row));
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
                {
                    "sprk_signal" => SignalRows(q),
                    "sprk_policy" => PolicyRows(q),
                    "sprk_policyversion" => VersionRows(q),
                    "sprk_billingevent" => LineRows(q),
                    _ => throw new InvalidOperationException($"unexpected query on {q.EntityName}"),
                });

            // E: a findings-JSON write is told from a status write; only a status write counts as a terminal write.
            Entities.Setup(e => e.UpdateAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    if (fields.ContainsKey(InvoiceSubmissionPersister.SubmissionStatus))
                    {
                        if (CrashAtTerminal)
                        {
                            throw new Crash();
                        }

                        UnconditionalStatusWrites.Add(fields);
                        TerminalWrites.Add(fields);
                    }

                    if (fields.TryGetValue(InvoiceSubmissionPersister.SubmissionFindingsJson, out var findings))
                    {
                        FindingsWrites.Add((string)findings);
                    }

                    Apply(fields);
                    return Task.CompletedTask;
                });

            // B: the terminal write is conditional on the row version read.
            Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(FinanceBaseValues.SubmissionEntity,
                    It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken _) =>
                {
                    if (CrashAtTerminal)
                    {
                        throw new Crash();
                    }

                    OnTerminalWrite?.Invoke();
                    var (id, fields, rowVersion) = updates.Single();
                    id.Should().Be(Submission);
                    if (rowVersion != Row.GetAttributeValue<long>("versionnumber"))
                    {
                        throw new System.Data.DBConcurrencyException("row version mismatch");
                    }

                    TerminalWrites.Add(fields);
                    Apply(fields);
                    return Task.CompletedTask;
                });

            Totals.Setup(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => TotalsResult);

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

        /// <summary>A write by anyone: the fields land and the row version moves (Dataverse's versionnumber).</summary>
        public void Apply(Dictionary<string, object> fields)
        {
            foreach (var (k, v) in fields)
            {
                Row[k] = v;
            }

            Row["versionnumber"] = Row.GetAttributeValue<long>("versionnumber") + 1;
        }

        public InvoiceSubmissionStatus Status => (InvoiceSubmissionStatus)Row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value;

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
                Totals.Object,
                sink.Object,
                Log,
                WriteAsync,
                SupersedeAsync,
                CloseActedAsync,
                (decision, ct) =>
                {
                    if (CrashAtRecord)
                    {
                        throw new Crash();
                    }

                    RecordedDecisions.Add(decision);
                    if (RecordFault is { } fault)
                    {
                        return Task.FromException<BillingDecisionRecordResult>(fault);
                    }

                    // The writer reads the resolved Signals through its own reader (the Signal table as it is now, filed as the writer files them).
                    foreach (var key in Records.Rows.Keys.Where(k => k.Item1 == "sprk_signal").ToList())
                    {
                        Records.Rows.Remove(key);
                    }

                    foreach (var signal in Signals.Values)
                    {
                        Records.Rows[("sprk_signal", signal.Id)] = Copy(signal);
                    }

                    return Recorder.RecordAsync(decision, ct);
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
                },
                Clock);
        }

        /// <summary>The writer's rule (SignalWriter.CloseActedByAutomatedDecisionAsync), over the in-memory table.</summary>
        private Task<SignalAutomatedCloseOutcome> CloseActedAsync(Guid id, Guid recordId, DateTime setReadAt, string policyCode, CancellationToken _)
        {
            setReadAt.Kind.Should().Be(DateTimeKind.Utc);
            Closes.Add((id, recordId, setReadAt, policyCode));
            if (CloseFaults.TryGetValue(id, out var fault))
            {
                return Task.FromException<SignalAutomatedCloseOutcome>(fault);
            }

            if (CloseAnswers.TryGetValue(id, out var answer))
            {
                return Task.FromResult(answer);
            }

            var row = Signals.Values.Single(s => s.Id == id);
            var status = row.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value;
            if (status == SignalWriter.SignalStatusResolved && row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id == recordId)
            {
                return Task.FromResult(SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);
            }

            if (status is not (SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged))
            {
                return Task.FromResult(SignalAutomatedCloseOutcome.LeftAsIs);
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionActed);
            row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId);
            row.RowVersion = (long.Parse(row.RowVersion!) + 1).ToString();
            ClosedWrites++;
            return Task.FromResult(SignalAutomatedCloseOutcome.Closed);
        }

        private EntityCollection LineRows(QueryExpression q)
        {
            LineQueries.Add(q);
            q.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_invoice" && (Guid)c.Values[0] == InvoiceId);
            return new EntityCollection(Lines.Select(id => new Entity("sprk_billingevent", id)).ToList());
        }

        private EntityCollection SignalRows(QueryExpression q)
        {
            SignalQueries.Add(q);
            if (q.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == "sprk_signalid" && c.Operator == ConditionOperator.In) is { } byId)
            {
                return new EntityCollection(Signals.Values.Where(s => byId.Values.Contains(s.Id)).Select(Copy).ToList());
            }

            if (!q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_dedupekey"))
            {
                // The D-122 open-set read: by subject lookup (invoice OR its lines).
                q.Criteria.FilterOperator.Should().Be(LogicalOperator.Or);
                var set = new EntityCollection(Signals.Values.Where(s => q.Criteria.Conditions.Any(c => c.Operator switch
                {
                    ConditionOperator.Equal or ConditionOperator.In => s.GetAttributeValue<EntityReference>(c.AttributeName)?.Id is { } id && c.Values.Contains(id),
                    _ => throw new NotSupportedException(c.Operator.ToString()),
                })).Select(Copy).ToList());
                OnSetRead?.Invoke();
                return set;
            }

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
                ["sprk_currentversion"] = new EntityReference("sprk_policyversion", PolicyRowsCurrentVersion),
                [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_lane"] = new OptionSetValue(SignalWriter.LaneDecide),
            };
            return new EntityCollection([policy]);
        }

        private EntityCollection VersionRows(QueryExpression q)
        {
            VersionQueries.Add(q);
            var policyId = (Guid)q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_policy").Values[0];
            var versions = PolicyVersions ??
            [
                SystemVersion(SystemVersionId, 1,
                    Policy == PolicyState.NoVersionInForce ? new DateOnly(2027, 1, 1) : new DateOnly(2026, 1, 1), null,
                    Policy == PolicyState.WrongType ? RuleType.InvoiceAge : RuleType.SubmissionIntegrity),
            ];
            return new EntityCollection(versions.Where(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == policyId).ToList());
        }

        /// <summary>A system policy version row (the 076 publish shape: dates at UTC midnight; a superseded one carries its end date).</summary>
        public static Entity SystemVersion(Guid id, int number, DateOnly from, DateOnly? to, RuleType type = RuleType.SubmissionIntegrity)
        {
            var row = new Entity("sprk_policyversion", id)
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", SystemPolicyId),
                ["sprk_ruletype"] = new OptionSetValue((int)type),
                ["sprk_consequence"] = new OptionSetValue((int)BillingConsequence.Flag),
                ["sprk_inforcefrom"] = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                ["sprk_versionnumber"] = number,
            };
            if (to is { } end)
            {
                row["sprk_inforceto"] = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            }

            return row;
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
            SignalWriter.TryParseDedupeKey(key, out _, out var subjectEntity, out var subjectId, out _).Should().BeTrue();
            var row = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_dedupekey"] = key,
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["sprk_evidencerefs"] = evidence,
                // Filed as the writer files a Signal: the pair id and the subject lookup that repeats it.
                ["sprk_regardingrecordid"] = subjectId.ToString("D"),
                [SignalWriter.RegardingLookupByEntity[subjectEntity]] = new EntityReference(subjectEntity, subjectId),
                RowVersion = "1",
            };
            Signals[key] = row;
            return row;
        }

        public static Entity Copy(Entity source)
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
        world.Entities.Invocations.Last().Method.Name.Should().Be(nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync),
            "the terminal status is the last write, conditional on the row version");
        world.UnconditionalStatusWrites.Should().BeEmpty();
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
        // E: the id is the deterministic derivation from its key (the persister's IdOf), not merely equal to itself.
        decision.DecisionId.Should().Be(InvoiceSubmissionPersister.IdOf($"ebilling-auto-reject-{Submission:D}"))
            .And.NotBe(InvoiceSubmissionPersister.InvoiceIdFor(Submission))
            .And.NotBe(InvoiceSubmissionAfterSteps.RejectionDecisionIdFor(Matter));
        decision.ActionCode.Should().Be("reject-invoice");
        decision.SubjectId.Should().Be(InvoiceId);
        decision.Decider.SystemUserId.Should().BeNull();
        decision.Decider.Role.Should().Be("automated: submission rejection rule");
        decision.Outcome.Should().Be("Rejected");
        var deciding = outcome.Decision!.DecidingFindings.Select(d => d.Finding).ToList();
        decision.Reason.Should().Be(string.Join(", ", deciding.Select(f => f.ReasonCode).Distinct()));
        decision.PolicyVersionId.Should().Be(deciding[0].PolicyVersionId);
        decision.Values!.Values.Should().OnlyContain(v => v is string);
        decision.ResolvedSignalIds.Should().BeNull("without a set (the I/O-free preflight) the decision lists none; RunAsync passes the stored set (13c)");
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

    // ── 13a A: the terminal comes from the STORED state (F4-1) ──

    [Theory(DisplayName = "049 13a A: a persisted result that disagrees with the stored kind / reason is Refused (stored_state_mismatch) and writes nothing")]
    [InlineData(InvoiceSubmissionStatus.Accepted, null)]
    [InlineData(InvoiceSubmissionStatus.Rejected, "ebilling.other.code")]
    public async Task PersistedDisagreeingWithTheStoredState_IsRefused(InvoiceSubmissionStatus claimed, string? claimedCode)
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome) with { TerminalStatus = claimed, TerminalReasonCode = claimedCode }, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused, result.ReasonCode);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.StoredStateMismatch);
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
    }

    [Theory(DisplayName = "049 13a A resume: a crash at each step, then ResumeAsync (no outcome, no evaluation) completes with the STORED terminal: one record, one notify, one terminal write")]
    [InlineData(CrashPoint.MidSignals)]
    [InlineData(CrashPoint.Record)]
    [InlineData(CrashPoint.BeforeNotify)]
    [InlineData(CrashPoint.Terminal)]
    public async Task ResumeAsync_AfterACrash_AutoRejected_CompletesFromTheStoredState(CrashPoint point)
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        Arm(world, point);
        var sut = world.Build();

        (await Run(sut, Persisted(outcome))).Should().NotBe(SubmissionAfterStepsStatus.Completed);
        world.TerminalWrites.Should().BeEmpty();
        Disarm(world);
        var resumed = await sut.ResumeAsync(Submission, "corr-2");

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        resumed.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        resumed.AlreadyTerminal.Should().BeFalse();
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionReasonCode].Should().Be(outcome.ReasonCode);
        world.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Rejected);
        world.RecordCreates.Should().Be(1);
        world.Writes.Should().OnlyContain(w => w.Episode == 1);
        world.Totals.Verify(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()), Times.Once, "the resume re-runs the totals once; the first run took the persister's");
        world.Jobs.Should().BeEmpty();
    }

    [Theory(DisplayName = "049 13a A resume (Accepted): a crash at indexing, before notify or at the terminal write, then ResumeAsync completes Accepted with the same indexing job")]
    [InlineData(CrashPoint.Indexing)]
    [InlineData(CrashPoint.BeforeNotify)]
    [InlineData(CrashPoint.Terminal)]
    public async Task ResumeAsync_AfterACrash_Accepted_CompletesFromTheStoredState(CrashPoint point)
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        Arm(world, point);
        var sut = world.Build();

        (await Run(sut, Persisted(outcome))).Should().NotBe(SubmissionAfterStepsStatus.Completed);
        Disarm(world);
        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        world.TerminalWrites.Should().ContainSingle();
        world.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Accepted);
        world.Jobs.Select(j => (j.JobId, j.IdempotencyKey)).Distinct().Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13a A resume: a totals fault on resume writes the Signals and the record, returns Retry (totals_failed); the next resume completes")]
    public async Task ResumeAsync_TotalsFault_RetriesThenCompletes()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        var calls = 0;
        world.Totals.Setup(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1 ? Task.FromException<InvoiceTotalsCalculation>(new System.Data.DBConcurrencyException("busy")) : Task.FromResult(world.TotalsResult));
        var sut = world.Build();

        var first = await sut.ResumeAsync(Submission, null);
        var second = await sut.ResumeAsync(Submission, null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionPersister.TotalsFailedCode);
        first.DecisionRecordId.Should().NotBeNull("the Signals and the record run before the totals Retry stops the run");
        second.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.RecordCreates.Should().Be(1);
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13a A: a totals refusal is recorded in the findings JSON once (idempotent across a resume) and the invoice stands; the run completes")]
    public async Task TotalsRefusal_IsRecordedInTheFindings_Once()
    {
        var outcome = await Accepted();
        var world = new World(outcome) { CrashAtTerminal = true };
        var refused = world.TotalsResult with { Refusal = new InvoiceTotalsRefusal(InvoiceTotalsRefusalCodes.ApprovedTotalNegative, "below zero by 12.00") };
        world.TotalsResult = refused;
        var sut = world.Build();

        (await Run(sut, Persisted(outcome) with { Totals = refused })).Should().BeNull("crashed at the terminal write");
        world.CrashAtTerminal = false;
        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        var findings = world.FindingsWrites.Should().ContainSingle("the second run finds the same code stored and does not write again").Subject;
        var recorded = JsonNode.Parse(findings)![InvoiceSubmissionAfterSteps.TotalsRefusalMember]!;
        recorded["code"]!.GetValue<string>().Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
        findings.Should().NotContain("below zero", "the code only, never the message");
        InvoiceSubmissionAfterSteps.StoredFindings.Parse(findings)!.HasSignalPlan.Should().BeTrue("the rest of the stored JSON is kept");
        world.TerminalWrites.Should().ContainSingle();
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact(DisplayName = "049 13a A resume: a submission that links no invoice is Refused; a missing one is SubmissionMissing")]
    public async Task ResumeAsync_NoLinkOrMissing()
    {
        var outcome = await Accepted();
        var unlinked = new World(outcome);
        unlinked.Row.Attributes.Remove(InvoiceSubmissionPersister.SubmissionInvoice);
        var missing = new World(outcome);
        missing.Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

        var refused = await unlinked.Build().ResumeAsync(Submission, null);

        refused.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        refused.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.InvoiceLinkMismatchCode);
        (await missing.Build().ResumeAsync(Submission, null)).Status.Should().Be(SubmissionAfterStepsStatus.SubmissionMissing);
        unlinked.Totals.Verify(t => t.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 13a B: the terminal is written only while Processing, on the row version (F4-2) ──

    [Fact(DisplayName = "049 13a B scenario: Blocked completes, a reviewer sets Accepted, a re-delivery (RunAsync or ResumeAsync) writes nothing over it and does not notify again")]
    public async Task Blocked_ThenReviewerAccepts_RedeliveryDoesNotOverwrite()
    {
        var outcome = await Blocked();
        var world = new World(outcome);
        var sut = world.Build();

        (await sut.RunAsync(Persisted(outcome), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
        world.Apply(new Dictionary<string, object> // the reviewer's Override (105)
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = null!,
        });
        var writesBefore = world.Writes.Count;

        var viaRun = await sut.RunAsync(Persisted(outcome, SubmissionPersistStatus.Resumed), null);
        var viaResume = await sut.ResumeAsync(Submission, null);

        foreach (var redelivery in new[] { viaRun, viaResume })
        {
            redelivery.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
            redelivery.AlreadyTerminal.Should().BeTrue();
            redelivery.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Accepted);
        }

        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted, "the reviewer's decision stands");
        world.TerminalWrites.Should().ContainSingle("only the first run's Rejected");
        world.Notified.Should().ContainSingle();
        world.Writes.Count.Should().Be(writesBefore, "no step runs on a terminal submission");
        world.Jobs.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13a B: the row changes (still Processing) between the read and the terminal write -> re-read once, the write is retried on the new version")]
    public async Task VersionConflict_StillProcessing_RetriesTheWriteOnce()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.OnTerminalWrite = () =>
        {
            world.OnTerminalWrite = null;
            world.Apply(new Dictionary<string, object> { [InvoiceSubmissionPersister.SubmissionCorrelationId] = "touched" });
        };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        result.AlreadyTerminal.Should().BeFalse();
        world.TerminalWrites.Should().ContainSingle();
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact(DisplayName = "049 13a B: a reviewer decides between the read and the terminal write -> re-read finds it terminal, Completed, nothing written over it")]
    public async Task VersionConflict_NowTerminal_CompletesWithoutWriting()
    {
        var outcome = await Blocked();
        var world = new World(outcome);
        world.OnTerminalWrite = () =>
        {
            world.OnTerminalWrite = null;
            world.Apply(new Dictionary<string, object> { [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted) });
        };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        result.AlreadyTerminal.Should().BeTrue();
        result.InvoiceId.Should().Be(InvoiceId);
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact(DisplayName = "049 13a B: the terminal write loses to a concurrent change twice -> Retry (terminal_write_conflict), still Processing")]
    public async Task VersionConflict_Twice_Retries()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.OnTerminalWrite = () => world.Apply(new Dictionary<string, object> { [InvoiceSubmissionPersister.SubmissionCorrelationId] = "busy" });

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.TerminalWriteConflictCode);
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
    }

    [Theory(DisplayName = "049 13a B: a submission already Accepted / Rejected / Failed runs no step; Received is Refused (not_processing)")]
    [InlineData(InvoiceSubmissionStatus.Accepted, SubmissionAfterStepsStatus.Completed)]
    [InlineData(InvoiceSubmissionStatus.Rejected, SubmissionAfterStepsStatus.Completed)]
    [InlineData(InvoiceSubmissionStatus.Failed, SubmissionAfterStepsStatus.Completed)]
    [InlineData(InvoiceSubmissionStatus.Received, SubmissionAfterStepsStatus.Refused)]
    public async Task NotProcessing_RunsNoStep(InvoiceSubmissionStatus status, SubmissionAfterStepsStatus expected)
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        world.Row[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)status);

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(expected);
        if (expected == SubmissionAfterStepsStatus.Refused)
        {
            result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.NotProcessingCode);
        }

        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(status);
    }

    // ── 13a F: the system policy's version is the one in force on the received date (K1) ──

    [Fact(DisplayName = "049 13a F: a re-seed after receivedOn - the superseded version in force on receivedOn is used, so the system Signal is still written")]
    public async Task SystemPolicy_SupersededInWindow_IsUsed()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        outcome.ReceivedOn.Should().Be(new DateOnly(2026, 6, 5));
        var current = Guid.Parse("04900000-0000-4000-8000-0000000d0126");
        var world = new World(outcome)
        {
            PolicyVersions =
            [
                World.SystemVersion(SystemVersionId, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1)),
                World.SystemVersion(current, 2, new DateOnly(2026, 7, 1), null),
            ],
        };
        world.PolicyRowsCurrentVersion = current;

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().ContainSingle().Which.PolicyVersionId.Should().Be(SystemVersionId, "the version in force on 2026-06-05");
        world.VersionQueries.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13a F (must not fire): no version in force on receivedOn (window ended before it, current starts after; a draft in the window) -> no system Signal")]
    public async Task SystemPolicy_NoVersionInForceOnReceivedOn_NoSignal()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var current = Guid.Parse("04900000-0000-4000-8000-0000000d0126");
        var world = new World(outcome)
        {
            PolicyVersions =
            [
                World.SystemVersion(SystemVersionId, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
                World.SystemVersion(Guid.Parse("04900000-0000-4000-8000-0000000d0127"), 3, new DateOnly(2026, 6, 1), null), // a draft: not current, no end
                World.SystemVersion(current, 2, new DateOnly(2026, 7, 1), null),
            ],
        };
        world.PolicyRowsCurrentVersion = current;

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().BeEmpty();
        world.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    // ── 13a G: the Signal plan is required ──

    [Fact(DisplayName = "049 13a G: stored findings without a signalPlan are Refused (signal_plan_missing), not read as an empty plan; nothing is written")]
    public async Task MissingSignalPlan_IsRefused()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome, json =>
        {
            json.Remove("signalPlan");
            return json;
        });

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused, result.ReasonCode);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalPlanMissingCode);
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
    }

    // ── 13c: D-122 - the automatic rejection resolves every open Signal on the invoice and its lines ──

    private static readonly Guid OtherInvoice = Guid.Parse("04900000-0000-4000-8000-0000000000e9");

    private static InvoiceSubmissionAfterSteps.ResolvedSignalSet? StoredSet(World world) =>
        InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!.ResolvedSet;

    private static int StatusOf(Entity signal) => signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value;

    private static Guid? RecordOf(Entity signal) => signal.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id;

    [Fact(DisplayName = "049 13c: AutoRejected - the record resolves and closes every open Signal on the invoice and its lines (written now, an earlier episode's, Acknowledged); resolved and other-invoice Signals are untouched")]
    public async Task AutoRejected_ResolvesEveryOpenSignal_OnTheInvoiceAndItsLines()
    {
        var outcome = await AutoRejected(readNumber: "INV-999"); // step 1 writes the rule Signal (line) and the system mismatch Signal (invoice)
        var world = new World(outcome);
        world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusResolved, "[]");
        var earlier = world.Seed(InvoiceKey("BILL-THRESHOLD", 2), SignalWriter.SignalStatusOpen, "[]");
        var acknowledged = world.Seed(LineKey("BILL-RATE", 1), SignalWriter.SignalStatusAcknowledged, "[]");
        var resolvedByPerson = world.Seed(LineKey("BILL-NOTE", 1), SignalWriter.SignalStatusResolved, "[]");
        var elsewhere = world.Seed(SignalWriter.BuildDedupeKey("BILL-THRESHOLD", "sprk_invoice", OtherInvoice.ToString("D"), 1), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var written = new[] { world.Signals[LineKey("BILL-AGE", 1)].Id, world.Signals[InvoiceKey(BillingRuleCatalogue.SystemPolicyCode, 1)].Id };
        var expected = written.Concat([earlier.Id, acknowledged.Id]).ToList();
        var set = StoredSet(world)!;
        set.Signals.Select(s => s.Id).Should().BeEquivalentTo(expected);
        set.Signals.Single(s => s.Id == earlier.Id).PolicyCode.Should().Be("BILL-THRESHOLD");
        set.Signals.Single(s => s.Id == written[0]).PolicyCode.Should().Be("BILL-AGE");
        set.SetReadAtUtc.Should().Be(world.Clock.GetUtcNow().UtcDateTime);
        set.SetReadAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        set.RecordPredatesSet.Should().BeFalse();
        world.RecordedDecisions.Should().ContainSingle().Which.ResolvedSignalIds.Should().BeEquivalentTo(expected);
        world.RecordCreates.Should().Be(1);
        world.Closes.Select(c => c.Signal).Should().BeEquivalentTo(expected);
        world.Closes.Should().OnlyContain(c => c.Record == result.DecisionRecordId && c.SetReadAt == set.SetReadAtUtc);
        world.Closes.Single(c => c.Signal == acknowledged.Id).PolicyCode.Should().Be("BILL-RATE");
        foreach (var id in expected)
        {
            var row = world.Signals.Values.Single(s => s.Id == id);
            StatusOf(row).Should().Be(SignalWriter.SignalStatusResolved);
            RecordOf(row).Should().Be(result.DecisionRecordId!.Value);
        }

        RecordOf(resolvedByPerson).Should().BeNull("a Signal resolved before the read is not in the set");
        StatusOf(elsewhere).Should().Be(SignalWriter.SignalStatusOpen, "another invoice's Signal is not on this invoice");
        world.LineQueries.Should().ContainSingle("the set is read once");
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13c: Accepted and Blocked close nothing, store no set and read no set")]
    public async Task AcceptedAndBlocked_CloseNothing()
    {
        foreach (var outcome in new[] { await Accepted(readNumber: "INV-999"), await Blocked() })
        {
            var world = new World(outcome);
            var openElsewhere = world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusOpen, "[]");

            var result = await world.Build().RunAsync(Persisted(outcome), null);

            result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
            world.Writes.Should().NotBeEmpty();
            world.Closes.Should().BeEmpty(outcome.Kind.ToString());
            world.LineQueries.Should().BeEmpty();
            StoredSet(world).Should().BeNull();
            world.Signals.Values.Should().OnlyContain(s => StatusOf(s) == SignalWriter.SignalStatusOpen);
            StatusOf(openElsewhere).Should().Be(SignalWriter.SignalStatusOpen);
        }
    }

    [Fact(DisplayName = "049 13c: the set is stored BEFORE the record; a crash between them resumes (ResumeAsync) from the stored set with no fresh read; a Signal opened after the read stays open")]
    public async Task StoredSet_IsWrittenBeforeTheRecord_ResumeUsesIt()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome) { CrashAtRecord = true };
        var sut = world.Build();

        (await Run(sut, Persisted(outcome))).Should().BeNull("the crash at the record");
        var set = StoredSet(world)!;
        set.Signals.Select(s => s.Id).Should().Equal(world.Signals[LineKey("BILL-AGE", 1)].Id);
        world.RecordCreates.Should().Be(0);
        world.Closes.Should().BeEmpty();

        world.CrashAtRecord = false;
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        var late = world.Seed(InvoiceKey("BILL-LATE", 1), SignalWriter.SignalStatusOpen, "[]");
        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.LineQueries.Should().ContainSingle("a stored set is never read again");
        StoredSet(world).Should().BeEquivalentTo(set, "the stored set is frozen");
        world.RecordedDecisions.Should().ContainSingle().Which.ResolvedSignalIds.Should().Equal(set.Signals.Select(s => s.Id));
        world.Closes.Should().ContainSingle().Which.SetReadAt.Should().Be(set.SetReadAtUtc, "the close uses the stored read time, not the resume's clock");
        StatusOf(late).Should().Be(SignalWriter.SignalStatusOpen);
        world.Closes.Should().NotContain(c => c.Signal == late.Id);
    }

    [Fact(DisplayName = "049 13c: a Signal opened just after the set was read is not in the record and stays open")]
    public async Task ASignalOpenedAfterTheRead_StaysOpen()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        Entity? late = null;
        world.OnSetRead = () =>
        {
            late = world.Seed(InvoiceKey("BILL-LATE", 1), SignalWriter.SignalStatusOpen, "[]");
            world.Clock.Advance(TimeSpan.FromSeconds(3));
        };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        StoredSet(world)!.Signals.Should().NotContain(s => s.Id == late!.Id);
        StoredSet(world)!.SetReadAtUtc.Should().BeBefore(world.Clock.GetUtcNow().UtcDateTime, "the read time is taken before the read");
        world.RecordedDecisions.Single().ResolvedSignalIds.Should().NotContain(late!.Id);
        StatusOf(late).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "049 13c: a resume after a partial close set re-closes from the stored set only; each Signal is written once")]
    public async Task ResumeAfterAPartialClose_ReclosesFromTheStoredSetOnly()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusOpen, "[]");
        // The set is closed in id order: the crash hits the LAST Signal, after the others were closed.
        world.OnSetRead = () => world.CloseFaults[world.Signals.Values.Max(s => s.Id)] = new Crash();
        var sut = world.Build();

        (await Run(sut, Persisted(outcome))).Should().BeNull("the crash in the close step");
        var set = StoredSet(world)!;
        set.Signals.Should().HaveCount(3);
        world.ClosedWrites.Should().Be(2, "the other two were closed before the crash");
        world.TerminalWrites.Should().BeEmpty();

        world.CloseFaults.Clear();
        var late = world.Seed(LineKey("BILL-LATE", 1), SignalWriter.SignalStatusOpen, "[]");
        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.LineQueries.Should().ContainSingle();
        world.RecordCreates.Should().Be(1, "AlreadyRecorded on the resume");
        world.ClosedWrites.Should().Be(3, "no double write: the two closed ones answer AlreadyClosedByThisRecord");
        world.Closes.Select(c => c.Signal).Distinct().Should().BeEquivalentTo(set.Signals.Select(s => s.Id));
        StatusOf(late).Should().Be(SignalWriter.SignalStatusOpen);
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13c: ChangedSinceRead on a close is a Retry (still Processing, every Signal attempted, nothing notified); the redelivery completes")]
    public async Task ChangedSinceRead_Retries()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        var sut = world.Build();
        var ruleSignal = () => world.Signals[LineKey("BILL-AGE", 1)].Id;
        world.OnSetRead = () => world.CloseAnswers[ruleSignal()] = SignalAutomatedCloseOutcome.ChangedSinceRead;

        var first = await sut.RunAsync(Persisted(outcome), null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalChangedCode);
        first.DecisionRecordId.Should().NotBeNull();
        world.Closes.Should().HaveCount(2, "every Signal is attempted");
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
        world.TerminalWrites.Should().BeEmpty();
        world.Notified.Should().BeEmpty();

        world.CloseAnswers.Clear();
        var second = await sut.ResumeAsync(Submission, null);

        second.Status.Should().Be(SubmissionAfterStepsStatus.Completed, second.ReasonCode);
        StatusOf(world.Signals[LineKey("BILL-AGE", 1)]).Should().Be(SignalWriter.SignalStatusResolved);
        world.RecordCreates.Should().Be(1);
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 13c: LeftAsIs stays listed in the record; one Information line with ids and the policy code; the run completes")]
    public async Task LeftAsIs_StaysListed()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        world.OnSetRead = () => world.CloseAnswers[world.Signals[LineKey("BILL-AGE", 1)].Id] = SignalAutomatedCloseOutcome.LeftAsIs;

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var id = world.Signals[LineKey("BILL-AGE", 1)].Id;
        world.RecordedDecisions.Single().ResolvedSignalIds.Should().Equal(id);
        StoredSet(world)!.Signals.Should().ContainSingle(s => s.Id == id);
        world.Log.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information && e.Message.Contains("resolved otherwise", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Message.Should().Contain(id.ToString()).And.Contain("BILL-AGE").And.Contain(result.DecisionRecordId!.Value.ToString());
    }

    [Fact(DisplayName = "049 13c: a record written before D-122 (no stored set, hash over no Signals) - the resume does not fail; the stored set becomes empty; nothing is closed")]
    public async Task PreD122Record_ResumeDoesNotFail()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!;
        stored.ResolvedSet.Should().BeNull("written before this change");
        // The pre-D-122 run recorded the rejection listing no Signal (its hash is over the empty list), then stopped before the terminal status.
        var prior = await world.Recorder.RecordAsync(InvoiceSubmissionAfterSteps.BuildRejectionDecision(stored, Submission, InvoiceId)!);
        var sut = world.Build();

        var result = await sut.ResumeAsync(Submission, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.DecisionRecordId.Should().Be(prior.RecordId);
        world.RecordCreates.Should().Be(1, "the pre-D-122 record answers; no second record");
        var set = StoredSet(world)!;
        set.Signals.Should().BeEmpty();
        set.RecordPredatesSet.Should().BeTrue();
        world.Closes.Should().BeEmpty("the record lists no Signal, so it closes none");
        world.Signals.Should().HaveCount(2);
        world.Signals.Values.Should().OnlyContain(s => StatusOf(s) == SignalWriter.SignalStatusOpen);
        world.TerminalWrites.Should().ContainSingle();

        // A later resume uses the stored empty set directly (no read, one recorder call).
        world.Row[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing);
        world.RecordedDecisions.Clear();
        (await sut.ResumeAsync(Submission, null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.RecordedDecisions.Should().ContainSingle().Which.ResolvedSignalIds.Should().BeEmpty();
        world.LineQueries.Should().ContainSingle();
        world.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "049 13c: a Signal of the stored set that no longer exists - the writer refuses (owner_refused) and the run is Refused with resolved_signal_missing; another owner refusal keeps its code")]
    public async Task MissingListedSignal_IsRefusedWithItsOwnCode()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome) { CrashAtRecord = true };
        var sut = world.Build();
        (await Run(sut, Persisted(outcome))).Should().BeNull();
        var listed = StoredSet(world)!.Signals.Single().Id;

        world.CrashAtRecord = false;
        world.Signals.Remove(LineKey("BILL-AGE", 1)); // deleted after the set was stored
        var result = await sut.ResumeAsync(Submission, null);

        world.Signals[LineKey("BILL-AGE", 1)].Id.Should().NotBe(listed, "step 1 wrote the plan's Signal again as a new row");
        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused, result.ReasonCode);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.ResolvedSignalMissingCode);
        StoredSet(world)!.Signals.Single().Id.Should().Be(listed, "the stored set is frozen");
        world.RecordCreates.Should().Be(0);
        world.Closes.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();

        var owner = new World(outcome) { RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.OwnerRefused, "x") };
        var refused = await owner.Build().RunAsync(Persisted(outcome), null);
        refused.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        refused.ReasonCode.Should().Be(DecisionRecordRefusalReason.OwnerRefused, "every listed Signal exists");
    }

    [Fact(DisplayName = "049 13c: a listed Signal deleted after the record has nothing to close (one Warning); a transient close fault propagates and leaves the submission Processing")]
    public async Task CloseFaults_NotFoundIsSkipped_OtherFaultsPropagate()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var notFound = new World(outcome);
        notFound.OnSetRead = () => notFound.CloseFaults[notFound.Signals[LineKey("BILL-AGE", 1)].Id] =
            new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }); // ObjectDoesNotExist

        var result = await notFound.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        notFound.ClosedWrites.Should().Be(1, "the other Signal is closed");
        notFound.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("no longer exists", StringComparison.Ordinal));

        var transient = new World(outcome);
        transient.OnSetRead = () => transient.CloseFaults[transient.Signals[LineKey("BILL-AGE", 1)].Id] = new TimeoutException("dataverse");
        var act = () => transient.Build().RunAsync(Persisted(outcome), null);

        await act.Should().ThrowAsync<TimeoutException>();
        transient.Status.Should().Be(InvoiceSubmissionStatus.Processing);
        transient.TerminalWrites.Should().BeEmpty();
    }
}
