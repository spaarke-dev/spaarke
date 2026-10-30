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
using Sprk.Bff.Api.Services.Dataverse;
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

    internal enum PolicyState { Present, Missing, Disabled, NoVersionInForce, WrongType, Ambiguous }

    private sealed class World
    {
        public Entity Row { get; }
        public Dictionary<string, Entity> Signals { get; } = new(StringComparer.Ordinal);
        public List<SignalWriteRequest> Writes { get; } = [];
        public List<Guid> Superseded { get; } = [];
        public List<SubmissionDecidedNotification> Notified { get; } = [];
        /// <summary>Task 152: every invoice-index job the acceptance step queued: (invoice, correlation id).</summary>
        public List<(Guid InvoiceId, string? CorrelationId)> IndexEnqueues { get; } = [];
        /// <summary>Task 152: the queue refuses the index job with this.</summary>
        public Exception? IndexFault { get; set; }
        public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
        /// <summary>Every write of <c>sprk_processingstatus</c>, conditional or not (an unconditional one is also in <see cref="UnconditionalStatusWrites"/>).</summary>
        public List<Dictionary<string, object>> TerminalWrites { get; } = [];
        public List<Dictionary<string, object>> UnconditionalStatusWrites { get; } = [];
        public List<string> FindingsWrites { get; } = [];
        /// <summary>Every unconditional submission update (D-133: the Needs Admin Attention column is told from these).</summary>
        public List<Dictionary<string, object>> SubmissionUpdates { get; } = [];
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
        /// <summary>F1-3: the writer throws this for the policy's Signal (after recording the attempt in <see cref="Writes"/>).</summary>
        public Dictionary<string, Exception> WriteFaults { get; } = new(StringComparer.Ordinal);
        /// <summary>F1-3: the writer skips the policy's Signal with this <see cref="SignalWriteResult.SkippedRefusalCode"/>.</summary>
        public Dictionary<string, string> SkipCodes { get; } = new(StringComparer.Ordinal);
        /// <summary>F1-3: these policies' Signals go to the REAL <see cref="SignalWriter"/> (<see cref="RealWriter"/>).</summary>
        public HashSet<string> RealWriterPolicies { get; } = new(StringComparer.Ordinal);
        public SignalWriter? RealWriter { get; set; }
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
        /// <summary>Called at the start of every record call, after the decision is noted (another writer acting between two record calls).</summary>
        public Action<BillingDecision>? OnRecordCall { get; set; }
        public bool CrashAtRecord { get; set; }
        public bool CrashAtNotifyRead { get; set; }
        public Exception? SinkFault { get; set; }
        public bool CrashAtTerminal { get; set; }

        /// <summary>R5: a write of the attention column alone (the policy note) fails with this.</summary>
        public Exception? AttentionFault { get; set; }

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
                // Dataverse-set when the submission was created: a stored set's read time may not precede it (F1-1).
                ["createdon"] = CreatedOn,
            };

            // R7: a read returns only the columns it asks for (as Dataverse does), so a column the code needs must be in its column list.
            Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Submission, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) => Copy(Row, columns));
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
                    if (AttentionFault is { } attentionFault && fields.Count == 1 && fields.ContainsKey(InvoiceSubmissionPersister.SubmissionAdminAttention))
                    {
                        return Task.FromException(attentionFault);
                    }

                    SubmissionUpdates.Add(new Dictionary<string, object>(fields));
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
                Row[k] = v is DBNull ? null : v; // DBNull.Value is the explicit clear
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
                    OnRecordCall?.Invoke(decision);
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
                time: Clock,
                commitGate: CommitGate!,
                refreshApprovers: RefreshApprovers,
                enqueueIndex: (invoice, correlation, _) =>
                {
                    if (IndexFault is { } fault)
                    {
                        throw fault;
                    }

                    IndexEnqueues.Add((invoice, correlation));
                    return Task.CompletedTask;
                });
        }

        /// <summary>Task 094: the resume's commit gate (model 9.1 steps 5-6). Passed unless a test sets it (the constructor requires one, fix round K6).</summary>
        public Func<Guid, CancellationToken, Task<SubmissionCommitGateResult>>? CommitGate { get; set; } = (_, _) => Task.FromResult(SubmissionCommitGateResult.Passed);

        /// <summary>Task 103: the eligible-approver step at acceptance (null: the test-seam default, "nothing to resolve").</summary>
        public Func<Guid, CancellationToken, Task<bool>>? RefreshApprovers { get; set; }

        /// <summary>The writer's rule (SignalWriter.CloseActedByDecisionAsync), over the in-memory table.</summary>
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
            // LineQueries are the open-set reads (all the invoice's lines); a stored set's check (StoredSignalSetCheck, by id) is not one.
            if (!q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_billingeventid"))
            {
                LineQueries.Add(q);
            }

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
            if (Policy == PolicyState.Ambiguous)
            {
                // Two policies with the system code (the query asks for TopCount 2, which is how the reader tells "more than one").
                var twin = new Entity("sprk_policy", Guid.NewGuid());
                foreach (var (k, v) in policy.Attributes)
                {
                    twin[k] = v;
                }

                return new EntityCollection([policy, twin]);
            }

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

        private Task<SignalWriteResult> WriteAsync(SignalWriteRequest request, CancellationToken ct)
        {
            if (Writes.Count == CrashAtWrite)
            {
                CrashAtWrite = -1;
                throw new Crash();
            }

            Writes.Add(request);
            if (RealWriterPolicies.Contains(request.PolicyCode))
            {
                return RealWriter!.WriteAsync(request, ct);
            }

            if (WriteFaults.TryGetValue(request.PolicyCode, out var fault))
            {
                return Task.FromException<SignalWriteResult>(fault);
            }

            if (SkipCodes.TryGetValue(request.PolicyCode, out var skipCode))
            {
                return Task.FromResult(new SignalWriteResult(Guid.Empty, false, Guid.Empty, Guid.Empty) { SkippedRefusalCode = skipCode });
            }

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

        public static Entity Copy(Entity source) => Copy(source, null);

        public static Entity Copy(Entity source, string[]? columns)
        {
            var copy = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
            foreach (var (k, v) in source.Attributes)
            {
                if (columns is null || columns.Contains(k, StringComparer.Ordinal))
                {
                    copy[k] = v;
                }
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

    [Fact(DisplayName = "049 D / 152: Accepted - no Signals, no record, the invoice-index job queued (094: never from the PDF), notify Accepted, terminal Accepted")]
    public async Task Accepted_Notifies()
    {
        var outcome = await Accepted();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.Notified.Should().ContainSingle().Which.Should().Be(new SubmissionDecidedNotification(
            Submission, SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Spa, InvoiceId, null));
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionStatus]
            .Should().Be(new OptionSetValue((int)InvoiceSubmissionStatus.Accepted));
    }

    [Fact(DisplayName = "103: an accepted invoice's eligible approvers are resolved once, before the notification; an automatic rejection resolves none")]
    public async Task Accepted_ResolvesTheEligibleApprovers_AutoRejectedDoesNot()
    {
        var accepted = await Accepted();
        var refreshed = new List<Guid>();
        var world = new World(accepted)
        {
            RefreshApprovers = (invoice, _) =>
            {
                refreshed.Add(invoice);
                return Task.FromResult(true);
            },
        };

        var result = await world.Build().RunAsync(Persisted(accepted), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        refreshed.Should().Equal(InvoiceId);

        var rejected = await AutoRejected();
        var rejectedWorld = new World(rejected)
        {
            RefreshApprovers = (invoice, _) =>
            {
                refreshed.Add(invoice);
                return Task.FromResult(true);
            },
        };
        (await rejectedWorld.Build().RunAsync(Persisted(rejected), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        refreshed.Should().ContainSingle("an automatically rejected invoice has no one to approve it");
    }

    [Fact(DisplayName = "103: a failure resolving the eligible approvers only warns; the submission still completes (the refresh job retries)")]
    public async Task EligibleApproverFailure_OnlyWarns()
    {
        var accepted = await Accepted();
        var world = new World(accepted) { RefreshApprovers = (_, _) => throw new InvalidOperationException("dataverse down") };

        var result = await world.Build().RunAsync(Persisted(accepted), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Notified.Should().ContainSingle();
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 D: AcceptedUncoded - the hold system Signal (policy present), notify Accepted")]
    public async Task AcceptedUncoded_WritesTheHoldSignal()
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
        world.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Accepted);
        world.RecordCreates.Should().Be(0);
    }

    [Fact(DisplayName = "049 D: Blocked - the rule Signal as planned (presented as held for review: the stored plan is written as it is), no record, notified Rejected + blocked code")]
    public async Task Blocked_HeldSignal_Indexed_NotifiedRejectedBlocked()
    {
        var outcome = await Blocked();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        var signal = world.Writes.Should().ContainSingle().Subject;
        signal.PolicyCode.Should().Be("BILL-AGE");
        // Held presentation (049): the mapper's output, stored in the plan and written as it is; the after-steps add nothing of their own.
        signal.ShortHeadline.Should().Be(BillingFindingSignalMapper.HeldHeadline);
        signal.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Which.Text.Should().Be(SubmissionReasonCodes.Blocked);
        world.RecordCreates.Should().Be(0, "a held invoice is not decided; 105's Override / Return records it");
        world.Notified.Should().ContainSingle().Which.Should().Be(new SubmissionDecidedNotification(
            Submission, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Internal, InvoiceId, SubmissionReasonCodes.Blocked));
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionReasonCode].Should().Be(SubmissionReasonCodes.Blocked);
    }

    [Fact(DisplayName = "049 D: a held group's stored billing.decision Held line is written with the Signal, text included")]
    public async Task AHeldPlanEntry_IsWrittenWithItsHeldLine()
    {
        var outcome = await AutoRejected();
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

    [Theory(DisplayName = "122 (#2235): the rejection record stores the withheld sentence for a Duplicate reason, never the earlier invoice; a non-Duplicate sentence is unchanged")]
    [InlineData(BillingReasonCodes.DuplicateInvoiceNumber)]
    [InlineData(BillingReasonCodes.DuplicateOfPriorSubmissionLine)]
    [InlineData(BillingReasonCodes.DuplicateOfEarlierLine)]
    public async Task TheRejectionDecision_StoresTheWithheldSentence_ForADuplicateReason(string duplicateCode)
    {
        const string earlierNumber = "F-090.1";
        var earlierInvoice = new Guid("9f0e2c51-7a3b-4c8e-b1d2-0a9e8f7c6b5d");
        const string otherSentence = "Line 2 bills 0.3 hours, not a multiple of 0.1.";
        var parsed = InvoiceSubmissionAfterSteps.StoredFindings.Parse(InvoiceSubmissionPersister.BuildFindingsJson(await AutoRejected(), InvoiceId))!;
        parsed.Plan.Should().NotBeEmpty("the fixture's rejection has a Signal plan to copy the entry shape from");
        var basis = parsed.Plan[0];
        var stored = new InvoiceSubmissionAfterSteps.StoredFindings
        {
            Kind = parsed.Kind,
            ReasonCode = parsed.ReasonCode,
            InvoiceId = parsed.InvoiceId,
            TrackingNumber = parsed.TrackingNumber,
            ReliabilityThreshold = parsed.ReliabilityThreshold,
            DecidingIndexes = parsed.DecidingIndexes,
            Findings = parsed.Findings,
            Plan =
            [
                basis with { ReasonCodes = [duplicateCode], Sentence = $"Invoice INV-100 duplicates {earlierNumber} ({earlierInvoice:D})." },
                basis with { ReasonCodes = ["time.increment"], Sentence = otherSentence },
            ],
        };

        var decision = InvoiceSubmissionAfterSteps.BuildRejectionDecision(stored, Submission, InvoiceId)!;

        var reasons = decision.Facts["reasons"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object?>>>().Subject.ToList();
        reasons.Should().HaveCount(2);
        reasons[0]["sentence"].Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
        reasons[1]["sentence"].Should().Be(otherSentence);
        var snapshot = JsonSerializer.Serialize(decision.Facts);
        snapshot.Should().NotContain(earlierNumber).And.NotContain(earlierInvoice.ToString("D"));
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

    // ── Resume after a crash at each step ──

    public enum CrashPoint { MidSignals, Record, BeforeNotify, Terminal }

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

    [Theory(DisplayName = "049 D resume (Accepted): a crash before notify or at the terminal write - one notify, one terminal write")]
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

        world.Notified.Should().ContainSingle();
        world.TerminalWrites.Should().ContainSingle();
    }

    private static void Arm(World world, CrashPoint point)
    {
        switch (point)
        {
            case CrashPoint.MidSignals: world.CrashAtWrite = 1; break;
            case CrashPoint.Record: world.CrashAtRecord = true; break;
            case CrashPoint.BeforeNotify: world.CrashAtNotifyRead = true; break;
            case CrashPoint.Terminal: world.CrashAtTerminal = true; break;
        }
    }

    private static void Disarm(World world)
    {
        world.CrashAtWrite = -1;
        world.CrashAtRecord = false;
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

    [Fact(DisplayName = "049 D: a totals Retry writes the Signals and the record, then returns Retry with the persister's code; no notify or terminal")]
    public async Task TotalsRetry_RunsSignalsAndRecordOnly()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome, SubmissionPersistStatus.Retry), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TotalsFailedCode);
        world.Writes.Should().ContainSingle();
        world.RecordCreates.Should().Be(1);
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
    }

    [Theory(DisplayName = "049 13a A resume (Accepted): a crash before notify or at the terminal write, then ResumeAsync completes Accepted")]
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

    // ── ebilling-r1 task 094 (model 9.1 step 6; 092 review items 1-2): every resume runs the commit gate before anything else ──

    /// <summary>A world whose resume gate answers <paramref name="gate"/>; the gate and the totals are recorded in <paramref name="order"/>.</summary>
    private static World GateWorld(SubmissionEvaluationOutcome outcome, List<string> order, Func<Task<SubmissionCommitGateResult>> gate, bool withDocument = true)
    {
        var world = new World(outcome);
        if (!withDocument)
        {
            world.Row.Attributes.Remove("sprk_document");
        }

        world.CommitGate = (id, _) =>
        {
            id.Should().Be(Submission);
            order.Add("gate");
            return gate();
        };
        world.Totals.Setup(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                order.Add("totals");
                return Task.FromResult(world.TotalsResult);
            });
        return world;
    }

    [Theory(DisplayName = "094 (model 9.1 step 6): a resume runs the commit gate FIRST on every channel (source document or upload), before the totals and every after-step, then completes")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumeAsync_RunsTheCommitGateFirst_OnEveryChannel(bool withDocument)
    {
        var order = new List<string>();
        var world = GateWorld(await Accepted(), order, () => Task.FromResult(SubmissionCommitGateResult.Passed), withDocument);

        var resumed = await world.Build().ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        order.Should().StartWith(["gate", "totals"]);
        order.Count(o => o == "gate").Should().Be(1);
        world.TerminalWrites.Should().ContainSingle();
    }

    [Theory(DisplayName = "094 must-fire: a resume whose gate undid the commit (complete or held) stops Undone: no totals, Signals, record, notification or terminal write")]
    [InlineData(SubmissionCommitGateStatus.Undone, SubmissionReasonCodes.SourceCheckFailed, false)]
    [InlineData(SubmissionCommitGateStatus.UndoHeld, SubmissionReasonCodes.FileUnservable, true)]
    public async Task ResumeAsync_GateUndone_RunsNothing(SubmissionCommitGateStatus status, string code, bool held)
    {
        var order = new List<string>();
        var world = GateWorld(await AutoRejected(), order, () => Task.FromResult(new SubmissionCommitGateResult(status, code)));

        var resumed = await world.Build().ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Undone);
        resumed.ReasonCode.Should().Be(code);
        resumed.UndoHeld.Should().Be(held);
        resumed.InvoiceId.Should().Be(InvoiceId);
        order.Should().Equal("gate");
        world.Writes.Should().BeEmpty();
        world.RecordCreates.Should().Be(0);
        world.Notified.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 fix round K6 (must fire): the after-steps cannot be built without a commit gate - no seam defaults the gate to passed")]
    public async Task Constructor_WithoutACommitGate_Throws()
    {
        var world = new World(await Accepted()) { CommitGate = null };

        var build = () => world.Build();

        build.Should().Throw<ArgumentNullException>().WithParameterName("commitGate");
    }

    [Fact(DisplayName = "094: a gate fault on resume throws (the job retries) and nothing after it runs")]
    public async Task ResumeAsync_GateFault_Throws()
    {
        var order = new List<string>();
        var world = GateWorld(await Accepted(), order, () => Task.FromException<SubmissionCommitGateResult>(new TimeoutException("storage")));

        await FluentActions.Invoking(() => world.Build().ResumeAsync(Submission, null)).Should().ThrowAsync<TimeoutException>();

        order.Should().Equal("gate");
        world.TerminalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 must-not-fire: RunAsync after a commit runs no gate (the persister ran it before handing over)")]
    public async Task RunAsync_RunsNoGate()
    {
        var outcome = await Accepted();
        var order = new List<string>();
        var world = GateWorld(outcome, order, () => throw new InvalidOperationException("RunAsync must not run the gate"));

        (await world.Build().RunAsync(Persisted(outcome), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        order.Should().NotContain("gate");
    }

    // Task 152 replaces 094's "the after-steps take no job submitter" guard. 094's rule was that a Submit Invoice COPY stays out of indexing,
    // search and chat grounding because the invoice index was document-keyed (DEF-047). The index is now built from the invoice's structured
    // records and the job carries no document, so the rule holds by the job's shape, which these tests pin: the payload names the invoice and
    // the change only, and the handler (InvoiceIndexingJobHandlerTests) reads no document and no submission.
    [Fact(DisplayName = "152 (supersedes 094's constructor guard): the acceptance's index job is keyed to the acceptance and carries the invoice only, no document")]
    public async Task AcceptanceIndexJob_CarriesTheInvoiceOnly()
    {
        var sent = new List<JobContract>();
        var jobs = new Mock<JobSubmissionService>(
            Microsoft.Extensions.Options.Options.Create(new Sprk.Bff.Api.Configuration.ServiceBusOptions { QueueName = "q", CommunicationQueueName = "c" }),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => sent.Add(job))
            .Returns(Task.CompletedTask);

        await InvoiceSubmissionAfterSteps.EnqueueAcceptedIndex(jobs.Object)(InvoiceId, "corr-9", CancellationToken.None);

        var job = sent.Should().ContainSingle().Subject;
        job.JobType.Should().Be(InvoiceIndexingJobHandler.JobTypeName);
        job.SubjectId.Should().Be(InvoiceId.ToString());
        job.CorrelationId.Should().Be("corr-9");
        job.IdempotencyKey.Should().Be($"invoice-index-{InvoiceId:N}-accepted");
        job.Payload!.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("InvoiceId", "Change");
        job.Payload.RootElement.GetProperty("InvoiceId").GetGuid().Should().Be(InvoiceId);
    }

    [Theory(DisplayName = "152: an accepted, uncoded or blocked invoice is queued for the index once, with the job's correlation id")]
    [InlineData("accepted")]
    [InlineData("uncoded")]
    [InlineData("blocked")]
    public async Task AcceptedKinds_QueueTheIndexJob(string kind)
    {
        var outcome = kind switch { "accepted" => await Accepted(), "uncoded" => await AcceptedUncoded(), _ => await Blocked() };
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-7");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.IndexEnqueues.Should().ContainSingle().Which.Should().Be((InvoiceId, "corr-7"));
    }

    [Fact(DisplayName = "152: a redelivery (resume) queues the same index job again; it is idempotent by its key, not skipped")]
    public async Task Resume_QueuesTheIndexJobAgain()
    {
        var outcome = await Accepted();
        var world = new World(outcome);

        await world.Build().RunAsync(Persisted(outcome), "corr-1");
        world.Row[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing);
        await world.Build().ResumeAsync(Submission, "corr-1");

        world.IndexEnqueues.Should().HaveCount(2).And.OnlyContain(e => e.InvoiceId == InvoiceId);
    }

    [Fact(DisplayName = "152 (FR-43): an automatic rejection is NOT queued for the invoice index")]
    public async Task AutoRejected_IsNotIndexed()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.IndexEnqueues.Should().BeEmpty();
    }

    [Fact(DisplayName = "152: a queue fault leaves the submission Processing (Retry, no notification, no terminal write); the redelivery queues again")]
    public async Task IndexEnqueueFault_StaysProcessing_ThenRetrySucceeds()
    {
        var outcome = await Accepted();
        var world = new World(outcome) { IndexFault = new InvalidOperationException("service bus down") };

        var first = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.IndexEnqueueFailedCode);
        world.TerminalWrites.Should().BeEmpty();
        world.Notified.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);

        world.IndexFault = null;
        var second = await world.Build().ResumeAsync(Submission, "corr-1");

        second.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.IndexEnqueues.Should().ContainSingle();
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

    /// <summary>The submission's <c>createdon</c>: an hour before the world's clock.</summary>
    private static readonly DateTime CreatedOn = new(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>A <c>resolvedSignalSet</c> planted in the findings, as a user with Write on the submission could (F1-1).</summary>
    private static Func<JsonObject, JsonObject> PlantedSet(DateTime readAtUtc, params Guid[] signalIds) => json =>
    {
        json[InvoiceSubmissionAfterSteps.ResolvedSetMember] = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
            readAtUtc, signalIds.Select(id => new InvoiceSubmissionAfterSteps.ResolvedSignal(id, "BILL-THRESHOLD")).ToList()).ToJson();
        return json;
    };

    [Fact(DisplayName = "049 F1-1: a planted resolvedSignalSet naming ANOTHER invoice's Signal is refused (findings_unreadable): nothing recorded, nothing closed")]
    public async Task AutoRejected_APlantedSetNamingAForeignSignal_IsRefused()
    {
        var outcome = await AutoRejected();
        var foreignId = Guid.NewGuid();
        var world = new World(outcome, PlantedSet(CreatedOn.AddMinutes(5), foreignId));
        var foreign = world.Seed(SignalWriter.BuildDedupeKey("BILL-THRESHOLD", "sprk_invoice", OtherInvoice.ToString("D"), 1), SignalWriter.SignalStatusOpen, "[]");
        world.Signals.Remove(world.Signals.Single(s => s.Value.Id == foreign.Id).Key);
        foreign.Id = foreignId;
        world.Signals["foreign"] = foreign;

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.FindingsUnreadableCode);
        world.RecordCreates.Should().Be(0);
        world.Closes.Should().BeEmpty();
        StatusOf(foreign).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "049 F1-1: a planted read time before the submission was created (beyond the skew), or in the future, is refused (findings_unreadable)")]
    [InlineData(-2)]
    [InlineData(+10)]
    public async Task AutoRejected_APlantedReadTimeOutOfBounds_IsRefused(int minutes)
    {
        var outcome = await AutoRejected();
        var readAt = minutes < 0 ? CreatedOn.AddMinutes(minutes) : new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
        var world = new World(outcome, PlantedSet(readAt));

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.FindingsUnreadableCode);
        world.RecordCreates.Should().Be(0);
        world.Closes.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F1-1: a stored set of the invoice's own Signal, read inside the submission's life, is trusted and closed")]
    public async Task AutoRejected_AStoredSetOfItsOwnSignal_IsTrusted()
    {
        var outcome = await AutoRejected();
        var ownId = Guid.NewGuid();
        var world = new World(outcome, PlantedSet(CreatedOn.AddMinutes(5), ownId));
        var own = world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusOpen, "[]");
        world.Signals.Remove(world.Signals.Single(s => s.Value.Id == own.Id).Key);
        own.Id = ownId;
        world.Signals[InvoiceKey("BILL-THRESHOLD", 1)] = own;

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Closes.Select(c => c.Signal).Should().Equal(ownId);
    }

    /// <summary>A Signal of the invoice's own, seeded under <paramref name="rowPolicyCode"/> and given <paramref name="id"/> (the id a planted
    /// set names).</summary>
    private static Entity SeedOwnInvoiceSignal(World world, Guid id, string rowPolicyCode)
    {
        var own = world.Seed(InvoiceKey(rowPolicyCode, 1), SignalWriter.SignalStatusOpen, "[]");
        world.Signals.Remove(world.Signals.Single(s => s.Value.Id == own.Id).Key);
        own.Id = id;
        world.Signals[InvoiceKey(rowPolicyCode, 1)] = own;
        return own;
    }

    [Fact(DisplayName = "049 F1-b (ontology-r1 round 2): a stored set naming the invoice's own Signal under ANOTHER policy code than its row's is refused (findings_unreadable): nothing recorded, nothing closed")]
    public async Task AutoRejected_AStoredSetClaimingAnotherPolicyCode_IsRefused()
    {
        var outcome = await AutoRejected();
        var ownId = Guid.NewGuid();
        var world = new World(outcome, PlantedSet(CreatedOn.AddMinutes(5), ownId)); // claims BILL-THRESHOLD
        var own = SeedOwnInvoiceSignal(world, ownId, "BILL-RATE");

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.FindingsUnreadableCode);
        world.RecordCreates.Should().Be(0, "the claimed code never reaches the record's resolvedSignalSet fact");
        world.Closes.Should().BeEmpty();
        StatusOf(own).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "049 K1 (ontology-r1 round 2): a stored read time up to the skew BEFORE createdon (Dataverse's clock) is trusted on resume; past it, refused")]
    [InlineData(-30, true)]
    [InlineData(-60, true)]
    [InlineData(-61, false)]
    public async Task AutoRejected_AStoredReadTimeJustBeforeCreatedOn_IsTrustedWithinTheSkew(int seconds, bool trusted)
    {
        var outcome = await AutoRejected();
        var ownId = Guid.NewGuid();
        var world = new World(outcome, PlantedSet(CreatedOn.AddSeconds(seconds), ownId));
        SeedOwnInvoiceSignal(world, ownId, "BILL-THRESHOLD");

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        if (trusted)
        {
            result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
            world.Closes.Select(c => c.Signal).Should().Equal(ownId);
        }
        else
        {
            result.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
            result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.FindingsUnreadableCode);
            world.Closes.Should().BeEmpty();
        }
    }

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

    [Fact(DisplayName = "049 13c: review_invalid (a record under other content exists) is a permanent Refused after exactly ONE record call; no fallback record; the submission stays Processing")]
    public async Task ReviewInvalid_IsRefused_WithExactlyOneRecordCall()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!;
        // A record under this decision id whose hash is over another list (here none) - the writer refuses the run's set as review_invalid.
        await world.Recorder.RecordAsync(InvoiceSubmissionAfterSteps.BuildRejectionDecision(stored, Submission, InvoiceId)!);

        var result = await world.Build().ResumeAsync(Submission, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused, result.ReasonCode);
        result.ReasonCode.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        StoredSet(world)!.Signals.Should().NotBeEmpty("the stored set is untouched");
        world.RecordedDecisions.Should().ContainSingle("no second record call re-records with an empty list");
        world.RecordCreates.Should().Be(1, "only the record that was there");
        world.Closes.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);

        var faulted = new World(outcome) { RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.ReviewInvalid, "x") };
        var again = await faulted.Build().RunAsync(Persisted(outcome), null);
        again.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        again.ReasonCode.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        faulted.RecordedDecisions.Should().ContainSingle();
        faulted.Closes.Should().BeEmpty();
    }

    /// <summary>A run that stored a set of two Signals (the rule Signal on a line, an open invoice Signal) and stopped at the record.</summary>
    private static async Task<(World World, InvoiceSubmissionAfterSteps Sut, SubmissionEvaluationOutcome Outcome, Entity Gone, Guid Kept, DateTime ReadAt)> StoredPairAsync()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome) { CrashAtRecord = true };
        var gone = world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusOpen, "[]");
        var sut = world.Build();
        (await Run(sut, Persisted(outcome))).Should().BeNull("the crash at the record");
        var set = StoredSet(world)!;
        set.Signals.Should().HaveCount(2);
        world.CrashAtRecord = false;
        return (world, sut, outcome, gone, world.Signals[LineKey("BILL-AGE", 1)].Id, set.SetReadAtUtc);
    }

    [Fact(DisplayName = "049 13c: a listed Signal missing before the record - ONLY that id is removed, the read time is kept, the shrunk set is stored, the record is tried once more and the run completes")]
    public async Task MissingListedSignal_ShrinksTheSet_AndRetriesOnce()
    {
        var (world, sut, _, gone, kept, readAt) = await StoredPairAsync();
        world.Signals.Remove(InvoiceKey("BILL-THRESHOLD", 1)); // deleted after the set was stored
        world.Clock.Advance(TimeSpan.FromMinutes(5));

        var result = await sut.ResumeAsync(Submission, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.RecordedDecisions.Should().HaveCount(2);
        world.RecordedDecisions[0].ResolvedSignalIds.Should().Contain([gone.Id, kept]);
        world.RecordedDecisions[1].ResolvedSignalIds.Should().Equal(kept);
        var set = StoredSet(world)!;
        set.Signals.Select(s => s.Id).Should().Equal(new[] { kept }, "the stored set is the shrunk one");
        set.SetReadAtUtc.Should().Be(readAt, "the read time does not move");
        world.RecordCreates.Should().Be(1);
        world.Closes.Select(c => c.Signal).Should().Equal(kept);
        world.Closes.Should().OnlyContain(c => c.Record == result.DecisionRecordId && c.SetReadAt == readAt);
        world.FindingsWrites.Should().HaveCount(2, "the first store and the shrunk store");
        world.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("no longer exist", StringComparison.Ordinal));
        world.TerminalWrites.Should().ContainSingle();

        // A later resume uses the shrunk set directly: one record call, AlreadyRecorded, no new store.
        world.Row[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing);
        world.RecordedDecisions.Clear();
        (await sut.ResumeAsync(Submission, null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.RecordedDecisions.Should().ContainSingle().Which.ResolvedSignalIds.Should().Equal(kept);
        world.RecordCreates.Should().Be(1);
        world.FindingsWrites.Should().HaveCount(2);
    }

    [Fact(DisplayName = "049 13c: owner_refused with every listed Signal present keeps its code after one record call and does not shrink; a retry that is refused again is classified as any refusal (no third call)")]
    public async Task OwnerRefused_NothingMissing_Stands_AndASecondRefusalIsFinal()
    {
        var outcome = await AutoRejected();
        var owner = new World(outcome) { RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.OwnerRefused, "x") };

        var refused = await owner.Build().RunAsync(Persisted(outcome), null);

        refused.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        refused.ReasonCode.Should().Be(DecisionRecordRefusalReason.OwnerRefused, "every listed Signal exists");
        owner.RecordedDecisions.Should().ContainSingle("nothing is missing, so there is no retry");
        owner.FindingsWrites.Should().ContainSingle("the set was stored once");

        var (world, sut, _, gone, kept, _) = await StoredPairAsync();
        world.Signals.Remove(InvoiceKey("BILL-THRESHOLD", 1));
        world.RecordFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.OwnerRefused, "x");

        var second = await sut.ResumeAsync(Submission, null);

        second.Status.Should().Be(SubmissionAfterStepsStatus.Refused);
        second.ReasonCode.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        world.RecordedDecisions.Should().HaveCount(2, "one retry with the shrunk set, then the refusal stands");
        world.RecordedDecisions[1].ResolvedSignalIds.Should().Equal(kept);
        StoredSet(world)!.Signals.Select(s => s.Id).Should().Equal(kept);
        gone.Id.Should().NotBe(Guid.Empty);
        world.Closes.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 13c: a record already exists under other content - the shrunk call gets review_invalid and the run is a permanent Refused; no wrong record is written")]
    public async Task MissingListedSignal_RecordAlreadyExists_ShrunkCallIsReviewInvalid()
    {
        var (world, sut, _, _, _, _) = await StoredPairAsync();
        world.Signals.Remove(InvoiceKey("BILL-THRESHOLD", 1));
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!;
        // Another writer records the decision (under other content) between the refused first call and the shrunk call.
        world.OnRecordCall = _ =>
        {
            if (world.RecordedDecisions.Count == 2)
            {
                world.Recorder.RecordAsync(InvoiceSubmissionAfterSteps.BuildRejectionDecision(stored, Submission, InvoiceId, [])!).GetAwaiter().GetResult();
            }
        };

        var result = await sut.ResumeAsync(Submission, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Refused, result.ReasonCode);
        result.ReasonCode.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        world.RecordedDecisions.Should().HaveCount(2);
        world.RecordCreates.Should().Be(1, "only the other writer's record; the shrunk call wrote none");
        world.Closes.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
    }

    [Fact(DisplayName = "049 13c K1: EVERY open or acknowledged Signal of the invoice and its lines is listed and closed - two open episodes of one (policy, subject), an open earlier episode under a resolved later one")]
    public async Task EveryOpenSignal_IsListed_NotTheLatestEpisodePerSubject()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        var first = world.Seed(InvoiceKey("BILL-THRESHOLD", 1), SignalWriter.SignalStatusOpen, "[]");
        var second = world.Seed(InvoiceKey("BILL-THRESHOLD", 2), SignalWriter.SignalStatusOpen, "[]");
        var earlierOpen = world.Seed(LineKey("BILL-RATE", 1), SignalWriter.SignalStatusAcknowledged, "[]");
        var laterResolved = world.Seed(LineKey("BILL-RATE", 2), SignalWriter.SignalStatusResolved, "[]");

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var written = new[] { world.Signals[LineKey("BILL-AGE", 1)].Id, world.Signals[InvoiceKey(BillingRuleCatalogue.SystemPolicyCode, 1)].Id };
        var expected = written.Concat([first.Id, second.Id, earlierOpen.Id]).ToList();
        var set = StoredSet(world)!;
        set.Signals.Select(s => s.Id).Should().BeEquivalentTo(expected);
        set.Signals.Select(s => s.Id).Should().BeInAscendingOrder("ordered by id so the hash is stable");
        set.Signals.Where(s => s.PolicyCode == "BILL-THRESHOLD").Should().HaveCount(2);
        world.Closes.Select(c => c.Signal).Should().BeEquivalentTo(expected);
        StatusOf(first).Should().Be(SignalWriter.SignalStatusResolved);
        StatusOf(second).Should().Be(SignalWriter.SignalStatusResolved);
        StatusOf(earlierOpen).Should().Be(SignalWriter.SignalStatusResolved);
        RecordOf(laterResolved).Should().BeNull("a resolved Signal is not in the set");
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

    // ── F1-3: a permanent Signal-write refusal never strands the automatic rejection ──

    private static System.ServiceModel.FaultException<OrganizationServiceFault> AccessDenied() =>
        new(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220) }, "Principal user is missing prvCreatesprk_signal privilege");

    /// <summary>Each permanent class (F1-3) and the code it is recorded under.</summary>
    public static TheoryData<string, string> PermanentRefusals => new()
    {
        { "argument", InvoiceSubmissionAfterSteps.RequestRefusedCode },
        { "template", SignalWriteRefusal.SentenceTemplateInvalid },
        { "escalation", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved },
        { "escalation-argument", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.SubjectPairUnresolved },
        { "access-denied", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.DataverseAccessDenied },
        { "skip-core-changed", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordChanged },
        { "skip-owner", Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.NoDefaultOwnerTeam },
    };

    /// <summary>Makes the BILL-AGE Signal fail as <paramref name="kind"/>. "argument": an argument refusal of a request the writer's preflight
    /// passes (the code is then <see cref="InvoiceSubmissionAfterSteps.RequestRefusedCode"/>; the real-writer test below covers the preflight's
    /// own code).</summary>
    private static void FailBillAge(World world, string kind)
    {
        switch (kind)
        {
            case "argument":
                world.WriteFaults["BILL-AGE"] = new ArgumentException("The rendered sentence is too long.", "request");
                break;
            case "template":
                world.WriteFaults["BILL-AGE"] = new SignalSentenceTemplateException("token missing");
                break;
            case "escalation":
                world.WriteFaults["BILL-AGE"] = new SignalWriterEscalationException("no core", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved);
                break;
            case "escalation-argument":
                world.WriteFaults["BILL-AGE"] = new SignalWriterEscalationException("no pair", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.SubjectPairUnresolved,
                    new ArgumentException("Regarding entity name is required."));
                break;
            case "access-denied":
                world.WriteFaults["BILL-AGE"] = AccessDenied();
                break;
            case "skip-core-changed":
                world.SkipCodes["BILL-AGE"] = Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordChanged;
                break;
            case "skip-owner":
                world.SkipCodes["BILL-AGE"] = Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.NoDefaultOwnerTeam;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static JsonArray StoredWriteRefusals(World world) =>
        JsonNode.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))![InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] as JsonArray ?? [];

    [Theory(DisplayName = "049 F1-3: a permanent Signal refusal is recorded and the run continues - the other Signals, the record, the terminal (AutoRejected)")]
    [MemberData(nameof(PermanentRefusals))]
    public async Task PermanentRefusal_IsRecorded_TheRejectionCompletesWithItsRecord(string kind, string code)
    {
        var outcome = await AutoRejected(readNumber: "INV-999"); // the rule Signal (BILL-AGE, written first) and the D-91 system Signal
        var world = new World(outcome);
        FailBillAge(world, kind);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        result.SignalsRefused.Should().Be(1);
        result.SignalsWritten.Should().Be(1, "the system Signal after the refused one is still written");
        world.Writes.Select(w => w.PolicyCode).Should().Equal("BILL-AGE", BillingRuleCatalogue.SystemPolicyCode);
        world.RecordCreates.Should().Be(1, "074 K-3: the automatic rejection ends with its record");
        result.DecisionRecordId.Should().NotBeNull();
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionStatus]
            .Should().Be(new OptionSetValue((int)InvoiceSubmissionStatus.Rejected));
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
        world.KeysFor("BILL-AGE").Should().BeEmpty("the refused Signal was not written, so the record cannot list it");

        var refusal = StoredWriteRefusals(world).Should().ContainSingle().Subject!.AsObject();
        refusal["policyCode"]!.GetValue<string>().Should().Be("BILL-AGE");
        refusal["code"]!.GetValue<string>().Should().Be(code);
        refusal["subject"]!["entity"]!.GetValue<string>().Should().Be("sprk_billingevent");
        refusal["subject"]!["id"]!.GetValue<string>().Should().Be(LineId.ToString("D"));
        var error = world.Log.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(Submission.ToString()).And.Contain(LineId.ToString()).And.Contain(code).And.Contain("BILL-AGE");
    }

    public static TheoryData<string> TransientFaults => new() { "read-back", "catalog-fault", "dataverse-fault" };

    [Theory(DisplayName = "049 F1-3: a transient Signal fault still propagates (retry): no record, no terminal, nothing recorded")]
    [MemberData(nameof(TransientFaults))]
    public async Task TransientFault_StillRetries(string kind)
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        world.WriteFaults["BILL-AGE"] = kind switch
        {
            "read-back" => new SignalWriterEscalationException("read back failed", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.OwnerReadBackFailed,
                new HttpRequestException("503")),
            "catalog-fault" => new SignalWriterEscalationException("no pair", Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.SubjectPairUnresolved,
                new InvalidOperationException("Dataverse request failed")),
            _ => new TimeoutException("Dataverse timed out"),
        };

        var act = () => world.Build().RunAsync(Persisted(outcome), "corr-1");

        await act.Should().ThrowAsync<Exception>();
        world.RecordCreates.Should().Be(0);
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
        StoredWriteRefusals(world).Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F1-3: a skip code not known to be permanent (parent undetermined) still retries (must-not-fire)")]
    public async Task UndeterminedParentSkip_StillRetries()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        world.SkipCodes["BILL-AGE"] = Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.ParentUndetermined;

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalWriteSkippedCode);
        world.RecordCreates.Should().Be(0);
        StoredWriteRefusals(world).Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F1-3: a permanent refusal is recorded once across a resume (idempotent); the resume completes")]
    public async Task PermanentRefusal_RecordedOnce_AcrossAResume()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome) { CrashAtTerminal = true };
        FailBillAge(world, "escalation");

        var first = () => world.Build().RunAsync(Persisted(outcome), "corr-1");
        await first.Should().ThrowAsync<Crash>();
        var writesAfterFirst = world.FindingsWrites.Count;
        StoredWriteRefusals(world).Should().ContainSingle();

        world.CrashAtTerminal = false;
        var resumed = await world.Build().ResumeAsync(Submission, "corr-2");

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        resumed.SignalsRefused.Should().Be(1, "refused again on the resume, and counted");
        world.FindingsWrites.Count.Should().Be(writesAfterFirst, "the same refusal is already stored: no second write");
        StoredWriteRefusals(world).Should().ContainSingle();
        world.RecordCreates.Should().Be(1);
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
    }

    // ── D-133: the submission is marked "Needs Admin Attention" with the refusal ──

    private const string AdminAttention = InvoiceSubmissionPersister.SubmissionAdminAttention;

    private static string? StoredAttention(World world) => world.Row.Contains(AdminAttention) ? world.Row.GetAttributeValue<string>(AdminAttention) : null;

    [Fact(DisplayName = "049 D-133: a permanent Signal refusal sets sprk_adminattention in the SAME update that records it, codes only")]
    public async Task PermanentRefusal_SetsAdminAttention_InTheSameUpdate()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var world = new World(outcome);
        FailBillAge(world, "skip-owner");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var expected = $"1 Signal could not be created: {Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.NoDefaultOwnerTeam} (BILL-AGE on line {LineId.ToString("D")[..8]})";
        var update = world.SubmissionUpdates.Where(u => u.ContainsKey(AdminAttention)).Should().ContainSingle().Subject;
        update[AdminAttention].Should().Be(expected, "the marker is part of the write that records the refusal");
        ((string)update[InvoiceSubmissionPersister.SubmissionFindingsJson]).Should().Contain(InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember);
        world.SubmissionUpdates.TakeWhile(u => !u.ContainsKey(AdminAttention)).Should().NotContain(
            u => u.ContainsKey(InvoiceSubmissionPersister.SubmissionFindingsJson) && ((string)u[InvoiceSubmissionPersister.SubmissionFindingsJson]).Contains(InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember),
            "no earlier write recorded the refusal without the marker");
        StoredAttention(world).Should().Be(expected);
        StoredAttention(world).Should().NotContain("INV-999", "codes and ids only, no extracted value");
    }

    [Fact(DisplayName = "049 D-133: a resume that finds the refusal stored writes nothing new and leaves the marker as it is (idempotent; an admin's clear stands)")]
    public async Task PermanentRefusal_AdminAttention_IsIdempotentAcrossAResume()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome) { CrashAtTerminal = true };
        FailBillAge(world, "escalation");

        var first = () => world.Build().RunAsync(Persisted(outcome), "corr-1");
        await first.Should().ThrowAsync<Crash>();
        var text = StoredAttention(world);
        text.Should().NotBeNull();

        world.Row[AdminAttention] = null; // the administrator reviewed and cleared it
        world.CrashAtTerminal = false;
        var resumed = await world.Build().ResumeAsync(Submission, "corr-2");

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.SubmissionUpdates.Count(u => u.ContainsKey(AdminAttention)).Should().Be(1, "written once, with the refusal; the resume wrote nothing new");
        StoredAttention(world).Should().BeNull("code never writes the column again after the administrator cleared it");
    }

    [Fact(DisplayName = "049 D-133: the text covers the plan refusals stored at persist and the write refusals, each once, in one update")]
    public async Task PermanentRefusal_AdminAttention_CoversPlanAndWriteRefusals()
    {
        var outcome = await AutoRejected();
        var planRefusalId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var world = new World(outcome, json =>
        {
            json[InvoiceSubmissionPersister.SignalPlanRefusalsMember] = new JsonArray(new JsonObject
            {
                ["policyCode"] = "BILL-LONG",
                ["subject"] = new JsonObject { ["entity"] = "sprk_invoice", ["id"] = planRefusalId.ToString("D") },
                ["code"] = SignalWriteRefusal.SentenceTooLong,
            });
            return json;
        });
        FailBillAge(world, "escalation");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        StoredAttention(world).Should().Be(
            $"2 Signals could not be created: {SignalWriteRefusal.SentenceTooLong} (BILL-LONG on invoice 11111111), "
            + $"{Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved} (BILL-AGE on line {LineId.ToString("D")[..8]})");
    }

    [Fact(DisplayName = "049 D-133: many refusals are capped at 500 characters and end with the count that did not fit")]
    public async Task PermanentRefusal_AdminAttention_OverflowIsCappedWithTheCount()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome, json =>
        {
            json[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = new JsonArray(Enumerable.Range(0, 40).Select(i => (JsonNode)new JsonObject
            {
                ["policyCode"] = $"BILL-OLD-{i}",
                ["subject"] = new JsonObject { ["entity"] = "sprk_billingevent", ["id"] = Guid.NewGuid().ToString("D") },
                ["code"] = "core_record_changed",
            }).ToArray());
            return json;
        });
        FailBillAge(world, "escalation");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var text = StoredAttention(world)!;
        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.AdminAttentionMaxLength);
        text.Should().StartWith("41 Signals could not be created: ").And.MatchRegex(@"… \(\+\d+ more\)$");
        var kept = text.Split("core_record_changed").Length - 1;
        text.Should().EndWith($"… (+{41 - kept} more)");
    }

    [Fact(DisplayName = "049 D-133: a run with no refusal never touches sprk_adminattention (not null, not empty) (must-not-fire)")]
    public async Task NoRefusal_LeavesAdminAttentionUntouched()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.SubmissionUpdates.Should().NotContain(u => u.ContainsKey(AdminAttention));
        world.TerminalWrites.Should().NotContain(u => u.ContainsKey(AdminAttention));
        world.Row.Contains(AdminAttention).Should().BeFalse();
    }

    // ── R5 (D-133): a misconfigured system policy reaches the administrator; a disabled one does not ──

    private static string PolicyAttention(string state) => $"System billing policy {BillingRuleCatalogue.SystemPolicyCode} is {state}: system Signals were not created";

    [Theory(DisplayName = "049 R5: a system policy that is missing, ambiguous, without a version in force or of the wrong type sets sprk_adminattention (codes only); the run still completes")]
    [InlineData(PolicyState.Missing, "missing")]
    [InlineData(PolicyState.Ambiguous, "ambiguous (more than one)")]
    [InlineData(PolicyState.NoVersionInForce, "without a version in force")]
    [InlineData(PolicyState.WrongType, "of the wrong rule type")]
    internal async Task SystemPolicyMisconfigured_SetsAdminAttention(PolicyState state, string described)
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome) { Policy = state };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Writes.Should().BeEmpty("no system Signal is written");
        StoredAttention(world).Should().Be(PolicyAttention(described));
        StoredAttention(world).Should().NotContain("INV-999").And.NotContain("INV-100");
        world.TerminalWrites.Should().ContainSingle("the attention write moved the row version; the terminal write re-reads it and still lands");
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact(DisplayName = "049 R5 (must not fire): a DISABLED system policy (D-125, the administrator's choice) stays a Warning only - no attention")]
    public async Task SystemPolicyDisabled_SetsNoAdminAttention()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome) { Policy = PolicyState.Disabled };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Row.Contains(AdminAttention).Should().BeFalse();
        world.SubmissionUpdates.Should().NotContain(u => u.ContainsKey(AdminAttention));
        world.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact(DisplayName = "049 R5 (must not fire): a usable system policy, or a run with no system finding, sets no attention")]
    public async Task SystemPolicyUsable_OrNoSystemFinding_SetsNoAdminAttention()
    {
        var withFinding = await Accepted(readNumber: "INV-999");
        var usable = new World(withFinding);
        var noFinding = await Accepted();
        var missingButNothingToSay = new World(noFinding) { Policy = PolicyState.Missing };

        (await usable.Build().RunAsync(Persisted(withFinding), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        (await missingButNothingToSay.Build().RunAsync(Persisted(noFinding), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        usable.Row.Contains(AdminAttention).Should().BeFalse();
        missingButNothingToSay.Row.Contains(AdminAttention).Should().BeFalse("the policy is read only when there is a system finding");
    }

    [Fact(DisplayName = "049 R5: the policy note is idempotent - a redelivery that finds the same misconfiguration writes the attention column once")]
    public async Task SystemPolicyMisconfigured_AdminAttention_IsIdempotentAcrossARedelivery()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome) { Policy = PolicyState.Missing, CrashAtTerminal = true };

        var first = () => world.Build().RunAsync(Persisted(outcome), null);
        await first.Should().ThrowAsync<Crash>();
        world.CrashAtTerminal = false;
        var resumed = await world.Build().ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.SubmissionUpdates.Count(u => u.ContainsKey(AdminAttention)).Should().Be(1, "the same text is already stored: nothing is written again");
        StoredAttention(world).Should().Be(PolicyAttention("missing"));
    }

    [Fact(DisplayName = "049 R5: a policy note MERGES with a Signal-refusal attention - whichever is recorded first, both stay, Signals first")]
    public async Task SystemPolicyMisconfigured_MergesWithSignalRefusalAttention()
    {
        var outcome = await AutoRejected(readNumber: "INV-999");
        var signalText = $"1 Signal could not be created: {Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved} (BILL-AGE on line {LineId.ToString("D")[..8]})";
        var expected = signalText + AdminAttentionText.Separator + PolicyAttention("missing");

        // (1) the policy is bad from the start: the policy note is written first, then the refusal is merged in.
        var policyFirst = new World(outcome) { Policy = PolicyState.Missing };
        FailBillAge(policyFirst, "escalation");
        (await policyFirst.Build().RunAsync(Persisted(outcome), null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        StoredAttention(policyFirst).Should().Be(expected);

        // (2) the refusal is recorded first (policy fine), then the policy goes bad before the redelivery.
        var refusalFirst = new World(outcome) { CrashAtTerminal = true };
        FailBillAge(refusalFirst, "escalation");
        var crashed = () => refusalFirst.Build().RunAsync(Persisted(outcome), null);
        await crashed.Should().ThrowAsync<Crash>();
        StoredAttention(refusalFirst).Should().Be(signalText);
        refusalFirst.CrashAtTerminal = false;
        refusalFirst.Policy = PolicyState.Missing;
        (await refusalFirst.Build().ResumeAsync(Submission, null)).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        StoredAttention(refusalFirst).Should().Be(expected);
    }

    [Fact(DisplayName = "049 R5: a fault writing the attention text is logged and swallowed - the run completes as it would have")]
    public async Task SystemPolicyMisconfigured_AttentionWriteFault_DoesNotChangeTheOutcome()
    {
        var outcome = await Accepted(readNumber: "INV-999");
        var world = new World(outcome) { Policy = PolicyState.Missing, AttentionFault = new TimeoutException("dataverse") };

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        world.Row.Contains(AdminAttention).Should().BeFalse();
        world.Log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("Needs Admin Attention") && e.Message.Contains("TimeoutException"));
    }

    // ── R7 (D-133): completing the submission withdraws ONLY the "Processing stopped" segment, in the terminal update ──

    private static string StoppedAttention => AdminAttentionText.StoppedNote("decision_owner_refused");

    private static string SignalAttention => $"1 Signal could not be created: {Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved} (BILL-AGE on line {LineId.ToString("D")[..8]})";

    private static string Join(params string[] parts) => string.Join(AdminAttentionText.Separator, parts);

    private static void AssertTerminalWasConditional(World world) =>
        world.Entities.Invocations.Last().Method.Name.Should().Be(nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync),
            "the terminal status, with the attention change, is one conditional update");

    [Fact(DisplayName = "049 R7: a stalled submission that a replay completes loses ONLY its Processing stopped segment; the Signal and policy segments stay, in the terminal update")]
    public async Task Completion_WithdrawsOnlyTheStoppedSegment_InTheTerminalUpdate()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.Row[AdminAttention] = Join(SignalAttention, PolicyAttention("missing"), StoppedAttention);

        var result = await world.Build().ResumeAsync(Submission, "corr-2");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        StoredAttention(world).Should().Be(Join(SignalAttention, PolicyAttention("missing")));
        world.TerminalWrites.Should().ContainSingle().Which[AdminAttention].Should().Be(Join(SignalAttention, PolicyAttention("missing")),
            "the same update as the terminal status, so it rests on the row version the status does");
        world.SubmissionUpdates.Should().NotContain(u => u.ContainsKey(AdminAttention), "no follow-on write");
        AssertTerminalWasConditional(world);
    }

    [Fact(DisplayName = "049 R7: when the stopped segment is all there is, the column becomes null (code withdrawing its own now-false statement)")]
    public async Task Completion_WithOnlyAStoppedSegment_NullsTheColumn()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.Row[AdminAttention] = StoppedAttention;

        var result = await world.Build().ResumeAsync(Submission, "corr-2");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        StoredAttention(world).Should().BeNull();
        world.TerminalWrites.Should().ContainSingle().Which.Should().ContainKey(AdminAttention).WhoseValue.Should().Be(DBNull.Value, "the explicit clear");
        AssertTerminalWasConditional(world);
    }

    [Theory(DisplayName = "049 R7 (must not fire): a stored text with no stopped segment is never written - not null, not rewritten")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Checked with the vendor; credit note pending")]
    public async Task Completion_WithNoStoppedSegment_DoesNotTouchTheColumn(string? stored)
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        if (stored is not null)
        {
            world.Row[AdminAttention] = stored;
        }

        var withSegments = new World(outcome);
        withSegments.Row[AdminAttention] = Join(SignalAttention, PolicyAttention("missing"));

        (await world.Build().ResumeAsync(Submission, "corr-2")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        (await withSegments.Build().ResumeAsync(Submission, "corr-2")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        world.TerminalWrites.Should().ContainSingle().Which.Should().NotContainKey(AdminAttention);
        withSegments.TerminalWrites.Should().ContainSingle().Which.Should().NotContainKey(AdminAttention);
        world.SubmissionUpdates.Should().NotContain(u => u.ContainsKey(AdminAttention));
        withSegments.SubmissionUpdates.Should().NotContain(u => u.ContainsKey(AdminAttention));
        world.Row.Contains(AdminAttention).Should().Be(stored is not null);
        if (stored is not null)
        {
            world.Row.GetAttributeValue<string>(AdminAttention).Should().Be(stored);
        }

        StoredAttention(withSegments).Should().Be(Join(SignalAttention, PolicyAttention("missing")));
    }

    [Fact(DisplayName = "049 R7: text an administrator typed is kept, before or after the stopped segment")]
    public async Task Completion_KeepsAdministratorText()
    {
        var outcome = await Accepted();
        var before = new World(outcome);
        before.Row[AdminAttention] = Join("Vendor called, hold for review", StoppedAttention);
        var after = new World(outcome);
        after.Row[AdminAttention] = Join(SignalAttention, StoppedAttention, "Vendor called, hold for review");

        (await before.Build().ResumeAsync(Submission, "corr-2")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        (await after.Build().ResumeAsync(Submission, "corr-2")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        StoredAttention(before).Should().Be("Vendor called, hold for review");
        StoredAttention(after).Should().Be(Join(SignalAttention, "Vendor called, hold for review"));
    }

    [Fact(DisplayName = "049 R7: a redelivery after completion writes nothing more (idempotent)")]
    public async Task Completion_Withdrawal_IsIdempotentAcrossARedelivery()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.Row[AdminAttention] = Join(SignalAttention, StoppedAttention);

        (await world.Build().ResumeAsync(Submission, "corr-2")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        var writes = world.TerminalWrites.Count;
        var version = world.Row.GetAttributeValue<long>("versionnumber");
        var again = await world.Build().ResumeAsync(Submission, "corr-3");

        again.Status.Should().Be(SubmissionAfterStepsStatus.Completed);
        again.AlreadyTerminal.Should().BeTrue();
        world.TerminalWrites.Count.Should().Be(writes, "terminal already: no second write");
        world.SubmissionUpdates.Should().BeEmpty();
        world.Row.GetAttributeValue<long>("versionnumber").Should().Be(version);
        StoredAttention(world).Should().Be(SignalAttention);
    }

    [Fact(DisplayName = "049 R7 (must not fire): the first run whose refusal wrote the Signal segment keeps it - the terminal update does not touch the column")]
    public async Task Completion_AfterARefusalOnTheSameRun_KeepsTheSignalSegment()
    {
        var outcome = await AutoRejected();
        var world = new World(outcome);
        FailBillAge(world, "escalation");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        StoredAttention(world).Should().Be(SignalAttention);
        world.TerminalWrites.Should().ContainSingle().Which.Should().NotContainKey(AdminAttention);
    }

    [Fact(DisplayName = "049 R7: a concurrent change to the attention text moves the row version; the terminal update is refused and re-computed from the NEW text (the condition is not weakened)")]
    public async Task Completion_ConcurrentAttentionChange_IsRecomputedOnTheNewVersion()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.Row[AdminAttention] = Join(SignalAttention, StoppedAttention);
        world.OnTerminalWrite = () =>
        {
            world.OnTerminalWrite = null;
            world.Apply(new Dictionary<string, object> { [AdminAttention] = Join(SignalAttention, StoppedAttention, "Reviewing now") });
        };

        var result = await world.Build().ResumeAsync(Submission, "corr-2");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        StoredAttention(world).Should().Be(Join(SignalAttention, "Reviewing now"), "the text the administrator added meanwhile survives; the stale copy was not written back");
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 R7 (must not fire): a submission already terminal (a reviewer decided) is not written, so its attention text is untouched")]
    public async Task NoTerminalWrite_LeavesTheStoppedSegment()
    {
        var outcome = await Accepted();
        var world = new World(outcome);
        world.Row[AdminAttention] = Join(SignalAttention, StoppedAttention);
        world.Row[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected);

        var result = await world.Build().ResumeAsync(Submission, "corr-2");

        result.AlreadyTerminal.Should().BeTrue();
        world.TerminalWrites.Should().BeEmpty();
        StoredAttention(world).Should().Be(Join(SignalAttention, StoppedAttention));
    }

    [Fact(DisplayName = "049 F1-3: a permanent refusal on an Accepted-path Signal does not stop the run either (Blocked: indexed, terminal)")]
    public async Task PermanentRefusal_Blocked_Completes()
    {
        var outcome = await Blocked();
        var world = new World(outcome);
        FailBillAge(world, "access-denied");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.SignalsRefused.Should().Be(1);
        world.TerminalWrites.Should().ContainSingle();
    }

    /// <summary>A REAL <see cref="SignalWriter"/> whose every Dataverse dependency throws: only its I/O-free request checks can run.</summary>
    private static SignalWriter RealWriterWithoutDataverse()
    {
        var noIo = new Mock<IGenericEntityService>(MockBehavior.Strict).Object;
        return new SignalWriter(
            new OntologyWriterDataverseClient(() => throw new InvalidOperationException("no Dataverse in this test"), NullLogger<OntologyWriterDataverseClient>.Instance),
            noIo,
            new Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver(noIo, (_, _) => throw new InvalidOperationException("no probe"),
                NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
            new Mock<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(MockBehavior.Strict).Object,
            new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(MockBehavior.Strict).Object,
            TimeProvider.System,
            NullLogger<SignalWriter>.Instance);
    }

    [Fact(DisplayName = "049 F1-3 (real SignalWriter): an over-long sentence is caught at persist; a plan stored before the fix is refused by the real writer, recorded, and the record still happens")]
    public async Task OverLongSentence_RealWriter_CaughtAtPersist_AndAtWriteTime_TheRecordStillHappens()
    {
        var outcome = await InvoiceSubmissionPersisterTests.OutcomeWithAnOverLongPlanEntryAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);

        // (a) The persister's plan no longer holds the entry: the real writer is never asked, the record happens.
        var world = new World(outcome) { RealWriter = RealWriterWithoutDataverse() };
        world.RealWriterPolicies.Add("BILL-LONG");
        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Writes.Select(w => w.PolicyCode).Should().Equal("BILL-AGE");
        world.RecordCreates.Should().Be(1);
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
        JsonNode.Parse(world.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))![InvoiceSubmissionPersister.SignalPlanRefusalsMember]!
            .AsArray().Should().ContainSingle();

        // (b) A plan stored before the preflight (the entry is in it): the REAL writer refuses it before any I/O (ArgumentException,
        //     sentence_too_long); it is recorded and the record and the terminal still happen.
        var stale = new World(outcome, json =>
        {
            var plan = json["signalPlan"]!.AsArray();
            var copy = plan[0]!.DeepClone().AsObject();
            copy["policyCode"] = "BILL-LONG";
            copy["policyId"] = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit.Id("policy:BILL-LONG").ToString("D");
            copy["policyVersionId"] = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit.Id("version:BILL-LONG:1").ToString("D");
            copy["template"] = new string('x', 2100);
            plan.Insert(0, copy);
            json.Remove(InvoiceSubmissionPersister.SignalPlanRefusalsMember);
            return json;
        })
        { RealWriter = RealWriterWithoutDataverse() };
        stale.RealWriterPolicies.Add("BILL-LONG");

        var staleResult = await stale.Build().RunAsync(Persisted(outcome), "corr-1");

        staleResult.Status.Should().Be(SubmissionAfterStepsStatus.Completed, staleResult.ReasonCode);
        staleResult.SignalsRefused.Should().Be(1);
        stale.Writes.Select(w => w.PolicyCode).Should().Equal("BILL-LONG", "BILL-AGE");
        stale.RecordCreates.Should().Be(1);
        stale.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
        StoredWriteRefusals(stale).Should().ContainSingle().Which!["code"]!.GetValue<string>().Should().Be(SignalWriteRefusal.SentenceTooLong);
    }

    /// <summary>A REAL <see cref="SignalWriter"/> over the writer test harness's Dataverse doubles (task 026's invoice-of-a-matter path), so the
    /// Signal row it creates can be read back.</summary>
    private static (SignalWriter Writer, Sprk.Bff.Api.Tests.Domain.Signals.SignalWriterTests.FakeOrganizationService Org) RealWriterOverFakeDataverse()
    {
        var sysadmin = new Sprk.Bff.Api.Tests.Domain.Signals.SignalWriterTests.FakeSysadminClient()
            .WithMatterBusinessUnit(Matter, Sprk.Bff.Api.Tests.Domain.Signals.SignalWriterTests.BusinessUnitId)
            .Stub(new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) })
            .Stub(new Entity("sprk_billingevent", LineId) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) });
        var org = new Sprk.Bff.Api.Tests.Domain.Signals.SignalWriterTests.FakeOrganizationService();
        return (Sprk.Bff.Api.Tests.Domain.Signals.SignalWriterTests.Build(org, sysadmin, new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero))), org);
    }

    [Fact(DisplayName = "049 Held (real SignalWriter): a Blocked submission writes the Held Signal: the held headline, the rejection rule's deciding reason in the sentence, a Held line with the blocked code")]
    public async Task Blocked_RealWriter_WritesTheHeldSignal()
    {
        var outcome = await Blocked();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        var (writer, org) = RealWriterOverFakeDataverse();
        var world = new World(outcome) { RealWriter = writer };
        world.RealWriterPolicies.Add("BILL-AGE");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.SignalsRefused.Should().Be(0, string.Join(" | ", world.Log.Entries.Select(e => e.Message)));
        var signal = org.Rows.Values.Should().ContainSingle(r => r.LogicalName == "sprk_signal").Subject;
        signal.GetAttributeValue<string>("sprk_name").Should().Be("Invoice held for review");
        var sentence = signal.GetAttributeValue<string>("sprk_sentence");
        sentence.Should().StartWith("The rejection rule would reject this invoice: ");
        var reason = sentence["The rejection rule would reject this invoice: ".Length..];
        reason.Should().NotBeNullOrWhiteSpace().And.NotContain("ebilling.submission.blocked").And.NotContain(BillingReasonCodes.InvoiceTooOld, "never a raw code");
        var evidence = JsonNode.Parse(signal.GetAttributeValue<string>("sprk_evidencerefs"))!.AsArray().OfType<JsonObject>().ToList();
        var held = evidence.Should().ContainSingle(e => e["kind"]!.GetValue<string>() == "billing.decision").Subject;
        held["ref"]!.GetValue<string>().Should().Be("Held");
        held["text"]!.GetValue<string>().Should().Be("ebilling.submission.blocked");
        evidence.Should().Contain(e => e["kind"]!.GetValue<string>() == "billing.reason" && e["text"]!.GetValue<string>() == reason,
            "the sentence's reason is the deciding finding's own rendered reason");

        // The Blocked routing is unchanged: no record, no closes, notified, terminal Rejected.
        world.RecordCreates.Should().Be(0);
        world.Closes.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
    }

    // -- F2 (ontology F1-3 re-check): a transient core-ancestor fault is not a permanent refusal --

    /// <summary>A REAL <see cref="SignalWriter"/> over a REAL <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"/> whose column probe and
    /// row read are the test's: the resolver's own Failed() (a fault) and Refused() (the row is gone) reach the writer's Error case.</summary>
    private static SignalWriter RealWriterOverResolver(Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.EntityColumnProbe probe, IGenericEntityService entities) =>
        new(new OntologyWriterDataverseClient(() => throw new InvalidOperationException("no Dataverse in this test"), NullLogger<OntologyWriterDataverseClient>.Instance),
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            new Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver(entities, probe, NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
            new Mock<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(MockBehavior.Strict).Object,
            new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(MockBehavior.Strict).Object,
            TimeProvider.System,
            NullLogger<SignalWriter>.Instance);

    private static readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.EntityColumnProbe LineColumns = (_, _) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(["sprk_project", "sprk_matter", "sprk_regardingworkassignment"], StringComparer.OrdinalIgnoreCase));

    private static IGenericEntityService RowReadFails(Exception fault)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(fault);
        return entities.Object;
    }

    [Theory(DisplayName = "049 F2 (real SignalWriter): a transient core-ancestor fault is NOT recorded as permanent - it propagates (retry): no refusal, no record, no terminal")]
    [InlineData("column-probe")]
    [InlineData("row-read")]
    public async Task TransientCoreAncestorFault_RealWriter_IsRetried_NotRecordedAsPermanent(string where)
    {
        var outcome = await AutoRejected();
        var writer = where == "column-probe"
            ? RealWriterOverResolver((_, _) => throw new TimeoutException("metadata unavailable"), new Mock<IGenericEntityService>(MockBehavior.Strict).Object)
            : RealWriterOverResolver(LineColumns, RowReadFails(new HttpRequestException("503 Service Unavailable")));
        var world = new World(outcome) { RealWriter = writer };
        world.RealWriterPolicies.Add("BILL-AGE");

        var act = () => world.Build().RunAsync(Persisted(outcome), "corr-1");

        var thrown = (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which;
        thrown.Reason.Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved);
        thrown.InnerException.Should().NotBeNull();
        InvoiceSubmissionAfterSteps.PermanentWriteRefusal(world.Writes[0], thrown).Should().BeNull("the classifier reads the inner exception");
        StoredWriteRefusals(world).Should().BeEmpty("nothing is recorded as permanent: the Signal is written on the retry");
        world.RecordCreates.Should().Be(0);
        world.TerminalWrites.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
    }

    [Fact(DisplayName = "049 F2 (real SignalWriter): a permanent core-ancestor Error (the line is gone) is recorded as core_record_unresolved and the run continues to the record")]
    public async Task PermanentCoreAncestorError_RealWriter_IsRecorded_TheRecordStillHappens()
    {
        var outcome = await AutoRejected();
        var gone = new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist");
        var world = new World(outcome) { RealWriter = RealWriterOverResolver(LineColumns, RowReadFails(gone)) };
        world.RealWriterPolicies.Add("BILL-AGE");

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.SignalsRefused.Should().Be(1);
        StoredWriteRefusals(world).Should().ContainSingle().Which!["code"]!.GetValue<string>()
            .Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.CoreRecordUnresolved);
        world.RecordCreates.Should().Be(1);
        world.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
    }
}
