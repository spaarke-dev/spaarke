using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using ProcessorTests = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 105: Override and Return of a blocked internal submission (<see cref="SubmissionOverrideService"/>), security-sensitive.
/// The blocked outcome comes from the real processor and the stored findings from the real persister builder; the Decision Record is
/// written by the REAL <see cref="BillingDecisionRecorder"/> over the writer's test harness, so "exactly one Decision Record" is the
/// writer's own exactly-once. Every refusal asserts that nothing changed (no record, no submission update, no invoice move, no notice).
/// </summary>
public class SubmissionOverrideServiceTests
{
    private const string Reason = "Negotiated rate is not yet in the rate card.";

    /// <summary>One call of the app-identity close.</summary>
    internal sealed record CloseCall(Guid SignalId, Guid RecordId, DateTime SetReadAtUtc, string PolicyCode, Guid? DecidedBy);

    // ── The world ──

    internal sealed class World
    {
        public static readonly Guid SubmissionId = Guid.Parse("10500000-0000-4000-8000-0000000000b1");
        public static readonly Guid Matter = Guid.Parse("10500000-0000-4000-8000-0000000000a1");
        public static readonly Guid Caller = Guid.Parse("10500000-0000-4000-8000-0000000000c1");
        public static readonly Guid CallerContact = Guid.Parse("10500000-0000-4000-8000-0000000000c2");
        public static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
        public const string Token = "caller-token";
        public static readonly DateTime CreatedOn = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

        public Entity Submission { get; }
        public Entity? Invoice { get; set; }

        /// <summary>What happened, in order: lock, record, transition, update, notify, unlock.</summary>
        public List<string> Order { get; } = [];

        public List<BillingDecision> Recorded { get; } = [];
        public List<Dictionary<string, object>> Updates { get; } = [];
        public List<InvoiceLifecycleStatus> Transitions { get; } = [];
        public List<SubmissionDecidedNotification> Notified { get; } = [];
        public List<string> LockKeys { get; } = [];
        public HashSet<string> HeldKeys { get; } = new(StringComparer.Ordinal);

        public Exception? ReadFault { get; set; }
        public Exception? InvoiceReadFault { get; set; }
        public Exception? LockFault { get; set; }
        public Exception? ReleaseFault { get; set; }
        public Exception? RecordFault { get; set; }
        public Exception? UpdateFault { get; set; }
        public Exception? SinkFault { get; set; }
        public Exception? TransitionFault { get; set; }
        public string? TransitionRefusal { get; set; }
        public bool SubmissionAbsent { get; set; }

        /// <summary>Called inside the conditional submission write, before the row version is compared (a concurrent writer).</summary>
        public Action? OnUpdate { get; set; }

        /// <summary>Called when the invoice's decision lock is taken: the moment between the first read and the re-read under the lock.</summary>
        public Action? OnLock { get; set; }

        // ── D-122 / FR-51: the invoice's lines and Signals, the app-identity close ──
        public List<Guid> LineIds { get; } = [];
        public List<Entity> Signals { get; } = [];
        public List<CloseCall> Closes { get; } = [];
        public Dictionary<Guid, Guid> ClosedBy { get; } = new();
        public Func<Guid, Exception?> CloseFault { get; set; } = _ => null;

        /// <summary>A forced non-throwing outcome of the close for a Signal (ChangedSinceRead, LeftAsIs), as the writer returns them.</summary>
        public Func<Guid, SignalAutomatedCloseOutcome?> CloseOutcome { get; set; } = _ => null;
        public Exception? SignalReadFault { get; set; }

        /// <summary>Thrown by the lookup of a decision's writer-created Decision Record (the stored-set read), as the writer throws it.</summary>
        public Exception? FindFault { get; set; }
        public List<string> SignalQueries { get; } = [];
        public int FindCalls { get; set; }

        /// <summary>The store write's own hook and fault (<see cref="OnUpdate"/> and <see cref="UpdateFault"/> are the decision write's).</summary>
        public Action? OnStore { get; set; }
        public Exception? StoreFault { get; set; }

        /// <summary>The writes that stored the decision's Signal set in the findings (before the record); <see cref="Updates"/> holds the rest.</summary>
        public List<Dictionary<string, object>> SetStores { get; } = [];

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceLifecycleService> Lifecycle { get; } = new(MockBehavior.Strict);
        public Mock<IIdempotencyService> Idempotency { get; } = new(MockBehavior.Strict);
        public Mock<IEbillingNotificationSink> Sink { get; } = new(MockBehavior.Strict);
        public ScriptedProbe Probe { get; } = new();
        public DecisionRecordWriterTests.Harness Records { get; }
        public BillingDecisionRecorder Recorder { get; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        public CapturedLogger Log { get; } = new();
        public SubmissionOverrideService Service { get; }

        // D-176 (task 119 round 2): the CALLER's Dataverse, the evaluating double of InvoiceReviewReadServiceTests (and ontology 157). The
        // invoice read returns the In-listed ids the caller reads; any other read fails the test (#2294: no tracking number is looked up).
        public HashSet<Guid> ReadableInvoices { get; } = [];
        public List<string> CallerPaths { get; } = [];
        public Func<string, Task<DataverseUserResponse>>? CallerFault { get; set; }
        public Mock<IDataverseUserClient> CallerClient { get; } = new(MockBehavior.Strict);

        public int CallerReads(string set) => CallerPaths.Count(p => p.StartsWith(set + "?", StringComparison.Ordinal));

        private readonly string _initialJson;

        public static async Task<World> CreateAsync(Func<JsonObject, JsonObject>? editFindings = null)
        {
            var harness = new ProcessorHarness { Rules = [ProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
            harness.Lines = [ProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
            var outcome = await harness.RunAsync();
            outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked, "the fixture must be a real blocked outcome");
            return new World(outcome, editFindings);
        }

        private World(SubmissionEvaluationOutcome outcome, Func<JsonObject, JsonObject>? editFindings)
        {
            var json = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!.AsObject();
            json = editFindings?.Invoke(json) ?? json;
            _initialJson = json.ToJsonString();

            // The held state task 049 leaves (D-101): the submission Rejected with the blocked code, its invoice In Review.
            Submission = new Entity(FinanceBaseValues.SubmissionEntity, SubmissionId)
            {
                ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
                [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected),
                [InvoiceSubmissionPersister.SubmissionReasonCode] = SubmissionReasonCodes.Blocked,
                [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", InvoiceId),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = _initialJson,
                ["sprk_trackingnumber"] = "INV-100.1",
                ["sprk_firminvoicenumber"] = "INV-100",
                ["versionnumber"] = 100L,
                // When the BFF created it (Dataverse-set, never updatable): the lower bound of a stored Signal set's read time (F1-1).
                ["createdon"] = CreatedOn,
            };
            SetInvoiceStatus(InvoiceLifecycleStatus.InReview);

            Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, SubmissionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string[] columns, CancellationToken _) =>
                {
                    if (ReadFault is { } fault)
                    {
                        return Task.FromException<Entity>(fault);
                    }

                    return SubmissionAbsent ? Task.FromException<Entity>(NotFound()) : Task.FromResult(Copy(Submission, columns));
                });
            Entities.Setup(e => e.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string[] columns, CancellationToken _) =>
                {
                    if (InvoiceReadFault is { } fault)
                    {
                        return Task.FromException<Entity>(fault);
                    }

                    return Invoice is null ? Task.FromException<Entity>(NotFound()) : Task.FromResult(Copy(Invoice, columns));
                });
            Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(FinanceBaseValues.SubmissionEntity,
                    It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken _) =>
                {
                    // D-122: the write that only adds the decision's Signal set to the findings ("store", before the record) is told
                    // apart from the decision's own write ("update": the reason code, the state).
                    var isStore = updates.Count == 1 && updates[0].fields.Keys.SequenceEqual([InvoiceSubmissionPersister.SubmissionFindingsJson]);
                    Order.Add(isStore ? "store" : "update");
                    (isStore ? OnStore : OnUpdate)?.Invoke();
                    if ((isStore ? StoreFault : UpdateFault) is { } fault)
                    {
                        return Task.FromException(fault);
                    }

                    var (id, fields, rowVersion) = updates.Single();
                    id.Should().Be(SubmissionId);
                    if (rowVersion != Submission.GetAttributeValue<long>("versionnumber"))
                    {
                        throw new System.Data.DBConcurrencyException("row version mismatch");
                    }

                    (isStore ? SetStores : Updates).Add(fields);
                    foreach (var (key, value) in fields)
                    {
                        Submission[key] = value;
                    }

                    Submission["versionnumber"] = Submission.GetAttributeValue<long>("versionnumber") + 1;
                    return Task.CompletedTask;
                });

            Lifecycle.Setup(l => l.TransitionAsync(InvoiceId, It.IsAny<InvoiceLifecycleStatus>(), It.IsAny<CancellationToken>()))
                .Returns((Guid _, InvoiceLifecycleStatus target, CancellationToken _) =>
                {
                    Order.Add("transition");
                    Transitions.Add(target);
                    if (TransitionFault is { } fault)
                    {
                        return Task.FromException<InvoiceTransitionResult>(fault);
                    }

                    if (TransitionRefusal is { } reason)
                    {
                        return Task.FromResult(InvoiceTransitionResult.Refuse(target, InvoiceLifecycleStatus.InReview, reason));
                    }

                    SetInvoiceStatus(target);
                    return Task.FromResult(InvoiceTransitionResult.Moved(InvoiceLifecycleStatus.InReview, target));
                });

            Idempotency.Setup(i => i.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Returns((string key, string _, TimeSpan? _, CancellationToken _) =>
                {
                    Order.Add("lock");
                    LockKeys.Add(key);
                    OnLock?.Invoke();
                    if (LockFault is { } fault)
                    {
                        return Task.FromException<bool>(fault);
                    }

                    return Task.FromResult(HeldKeys.Add(key));
                });
            Idempotency.Setup(i => i.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, string _, CancellationToken _) =>
                {
                    Order.Add("unlock");
                    HeldKeys.Remove(key);
                    return ReleaseFault is { } fault ? Task.FromException(fault) : Task.CompletedTask;
                });

            Sink.Setup(s => s.SubmissionDecidedAsync(It.IsAny<SubmissionDecidedNotification>(), It.IsAny<CancellationToken>()))
                .Returns((SubmissionDecidedNotification n, CancellationToken _) =>
                {
                    Order.Add("notify");
                    if (SinkFault is { } fault)
                    {
                        return Task.FromException(fault);
                    }

                    Notified.Add(n);
                    return Task.CompletedTask;
                });

            Records = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
            Records.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
            foreach (var version in (outcome.Evaluation?.Findings ?? []).Select(f => f.PolicyVersionId).Distinct())
            {
                Records.Rows[("sprk_policyversion", version)] = new Entity("sprk_policyversion", version);
            }

            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(Caller, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PersonIdentity(Caller, ContactId: CallerContact));
            Recorder = new BillingDecisionRecorder(Records.Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance);

            Probe.CallerSystemUserId = Caller;
            Probe.Hold(EbillingOverridePrivilege.PrivilegeName);

            CallerClient.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken _) => AnswerCaller(path));

            Service = new SubmissionOverrideService(
                Entities.Object, Lifecycle.Object, Idempotency.Object, Sink.Object, Probe, CallerClient.Object,
                new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance), Log,
                (decision, ct) =>
                {
                    Order.Add("record");
                    Recorded.Add(decision);
                    return RecordFault is { } fault ? Task.FromException<BillingDecisionRecordResult>(fault) : Recorder.RecordAsync(decision, ct);
                },
                Recorder.Preflight,
                CloseAsync,
                (code, invoice, decisionId, ct) =>
                {
                    FindCalls++;
                    return FindFault is { } fault
                        ? Task.FromException<DecisionRecordWriter.BillingStoredResolvedSet?>(fault)
                        : Recorder.FindStoredResolvedSetAsync(code, invoice, decisionId, ct);
                },
                Clock);

            // The open-Signal read (049's ReadOpenSignalSetAsync): the invoice's lines, then the Signals on the invoice or a line.
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) =>
                {
                    // A stored set's check reads its Signals by id (StoredSignalSetCheck): told apart from the open-set read.
                    SignalQueries.Add(query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_signalid") ? query.EntityName + ":byid" : query.EntityName);
                    if (SignalReadFault is { } fault)
                    {
                        return Task.FromException<EntityCollection>(fault);
                    }

                    if (query.EntityName == "sprk_signal"
                        && query.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == "sprk_signalid" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.In) is { } byId)
                    {
                        // A stored set's Signals read by id (StoredSignalSetCheck), app-only.
                        return Task.FromResult(new EntityCollection(Signals.Where(s => byId.Values.Contains(s.Id)).Select(s => Copy(s, [.. s.Attributes.Keys])).ToList()));
                    }

                    return Task.FromResult(query.EntityName switch
                    {
                        "sprk_billingevent" => new EntityCollection(LineIds.Select(id => new Entity("sprk_billingevent", id)).ToList()),
                        "sprk_signal" => new EntityCollection(Signals
                            .Where(s => query.Criteria.Conditions.Any(c => s.GetAttributeValue<EntityReference>(c.AttributeName) is { } subject
                                && c.Values.Any(v => Equals(v, subject.Id))))
                            .Select(s => Copy(s, [.. s.Attributes.Keys]))
                            .ToList()),
                        _ => throw new InvalidOperationException("unexpected query of " + query.EntityName),
                    });
                });
            Entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, CancellationToken _) => table + "s");
        }

        private Task<DataverseUserResponse> AnswerCaller(string path)
        {
            CallerPaths.Add(path);
            if (CallerFault is not null)
            {
                return CallerFault(path);
            }

            var set = path[..path.IndexOf('?')];
            var filter = Uri.UnescapeDataString(path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..]);
            object rows = set switch
            {
                "sprk_invoices" => Regex.Matches(filter, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value))
                    .Where(ReadableInvoices.Contains)
                    .Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id })
                    .ToList(),
                _ => throw new InvalidOperationException($"unexpected caller read {path}"),
            };
            return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));
        }

        /// <summary>The app-identity close as the Signal writer behaves: Open/Acknowledged → Resolved Acted by the record; a repeat by the
        /// same record → AlreadyClosedByThisRecord; resolved otherwise → LeftAsIs. Every call is recorded.</summary>
        public Task<SignalAutomatedCloseOutcome> CloseAsync(
            Guid signalId, Guid recordId, DateTime setReadAtUtc, string policyCode, Guid? decidedBy, CancellationToken ct)
        {
            Order.Add("close");
            Closes.Add(new CloseCall(signalId, recordId, setReadAtUtc, policyCode, decidedBy));
            if (CloseFault(signalId) is { } fault)
            {
                return Task.FromException<SignalAutomatedCloseOutcome>(fault);
            }

            if (CloseOutcome(signalId) is { } forced)
            {
                return Task.FromResult(forced);
            }

            if (ClosedBy.TryGetValue(signalId, out var by))
            {
                return Task.FromResult(by == recordId ? SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord : SignalAutomatedCloseOutcome.LeftAsIs);
            }

            ClosedBy[signalId] = recordId;
            Signals.Single(s => s.Id == signalId)["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            return Task.FromResult(SignalAutomatedCloseOutcome.Closed);
        }

        /// <summary>A line of the invoice, filed under the invoice's matter (the writer's harness holds it too).</summary>
        public Guid AddLine()
        {
            var id = Guid.NewGuid();
            LineIds.Add(id);
            Records.Rows[("sprk_billingevent", id)] = new Entity("sprk_billingevent", id)
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            };
            return id;
        }

        /// <summary>An Open (or <paramref name="status"/>) Signal on <paramref name="subjectTable"/> <paramref name="subjectId"/>, filed as
        /// the Signal writer files one; the writer's harness holds it too, so the record's resolved-Signal checks find it.</summary>
        public Guid AddSignal(string subjectTable, Guid subjectId, string policyCode = "EB-AGE", int status = SignalWriter.SignalStatusOpen)
        {
            var id = Guid.NewGuid();
            var lookup = SignalWriter.RegardingLookupByEntity[subjectTable];
            Signals.Add(new Entity("sprk_signal", id)
            {
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, subjectTable, subjectId.ToString("D"), 1),
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["sprk_regardingrecordid"] = subjectId.ToString("D"),
                [lookup] = new EntityReference(subjectTable, subjectId),
            });
            Records.Rows[("sprk_signal", id)] = new Entity("sprk_signal", id)
            {
                ["sprk_regardingrecordid"] = subjectId.ToString("D"),
                [lookup] = new EntityReference(subjectTable, subjectId),
            };
            return id;
        }

        public int RecordCreates => Records.Created.Count(e => e.LogicalName == "sprk_decisionrecord");

        public InvoiceSubmissionStatus Status =>
            (InvoiceSubmissionStatus)Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value;

        public string? Code => Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);

        public string FindingsJson => Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);

        public InvoiceLifecycleStatus InvoiceStatus
        {
            get
            {
                InvoiceStatusCodes.TryFromStatusCode(Invoice!.GetAttributeValue<OptionSetValue>("statuscode").Value, out var status).Should().BeTrue();
                return status;
            }
        }

        public void SetInvoiceStatus(InvoiceLifecycleStatus status) =>
            Invoice = new Entity("sprk_invoice", InvoiceId)
            {
                ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(status)),
                ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
            };

        public void SetSubmission(InvoiceSubmissionStatus status, string? code)
        {
            Submission[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)status);
            Submission[InvoiceSubmissionPersister.SubmissionReasonCode] = code;
        }

        public Task<BlockedDecisionResult> OverrideAsync(string? reason = Reason, string? token = Token) =>
            Service.OverrideAsync(SubmissionId, reason, token, CancellationToken.None);

        public Task<BlockedDecisionResult> ReturnAsync(string? reason = Reason, string? token = Token) =>
            Service.ReturnAsync(SubmissionId, reason, token, CancellationToken.None);

        /// <summary>Nothing was recorded, moved, written or announced, and the submission is exactly as it was, apart from the decision's
        /// Signal set when it was stored before the record (D-122: bookkeeping a retry reuses; the submission is still held, its findings
        /// otherwise untouched).</summary>
        public void AssertNothingChanged()
        {
            RecordCreates.Should().Be(0, "no Decision Record");
            Recorded.Should().BeEmpty("the recorder was never reached");
            Updates.Should().BeEmpty("no submission write");
            Transitions.Should().BeEmpty("no invoice move");
            Notified.Should().BeEmpty("no notice");
            Closes.Should().BeEmpty("no Signal closed");
            var findings = JsonNode.Parse(FindingsJson)!.AsObject();
            findings.Remove(SubmissionOverrideService.HumanDecisionSetMember);
            findings.ToJsonString().Should().Be(_initialJson);
        }

        /// <summary><see cref="AssertNothingChanged"/>, and not even the Signal set was stored: the refusal came before every write.</summary>
        public void AssertNothingWritten()
        {
            AssertNothingChanged();
            SetStores.Should().BeEmpty("refused before any write, the Signal set included");
            FindingsJson.Should().Be(_initialJson);
        }

        public static Entity Copy(Entity source, string[] columns)
        {
            var copy = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
            foreach (var (k, v) in source.Attributes)
            {
                if (columns.Contains(k, StringComparer.Ordinal))
                {
                    copy[k] = v;
                }
            }

            return copy;
        }

        public static FaultException<OrganizationServiceFault> NotFound() =>
            new(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"));
    }

    /// <summary>
    /// The caller-rights probe at its virtual seams: which privileges the caller holds, who the caller is, and a fault for the privilege
    /// question ALONE (<see cref="PrivilegeFault"/>) or for every question (<see cref="IdentityFault"/>), so each fail-closed path is
    /// reached on its own. Like the real probe, a missing token answers "not held" and "unknown caller".
    /// </summary>
    internal sealed class ScriptedProbe : Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe
    {
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);

        public ScriptedProbe()
            : base(new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance)
        {
        }

        public List<FinanceEndpointsAuthorizationContractTests.PrivilegeCall> Calls { get; } = [];
        public Exception? PrivilegeFault { get; set; }
        public Exception? IdentityFault { get; set; }
        public Guid? CallerSystemUserId { get; set; }

        /// <summary>The caller's rights on a record (entity set, id). By default the REAL roles' (F1-2): Core User and Basic User hold only
        /// Read on <c>sprk_billingevent</c> (D-90), and Write on the invoice and AppendTo on the core. A missing token holds none.</summary>
        public Func<string, Guid, Spaarke.Dataverse.AccessRights> Rights { get; set; } = RealReviewerRights;

        public static AccessRights RealReviewerRights(string set, Guid id) =>
            set == "sprk_billingevents" ? AccessRights.Read : AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;

        public List<(string? Token, string Set, Guid Id)> RightsAsked { get; } = [];

        public void Hold(string privilege) => _held.Add(privilege);

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            RightsAsked.Add((callerBearerToken, entitySet, recordId));
            return Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? AccessRights.None : Rights(entitySet, recordId));
        }

        public void Release(string privilege) => _held.Remove(privilege);

        public override Task<bool> CallerHoldsPrivilegeAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            Calls.Add(new FinanceEndpointsAuthorizationContractTests.PrivilegeCall(privilegeName, !string.IsNullOrEmpty(callerBearerToken)));
            return PrivilegeFault is { } fault
                ? Task.FromException<bool>(fault)
                : Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && _held.Contains(privilegeName));
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            IdentityFault is { } fault
                ? Task.FromException<Guid?>(fault)
                : Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? null : CallerSystemUserId);
    }

    internal sealed class CapturedLogger : ILogger<SubmissionOverrideService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " | " + exception.Message)));
    }

    private static bool IsHeld(Entity submission)
    {
        var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        var code = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);
        var conditions = FinanceBaseValues.HeldSubmissionConditions;
        return conditions.Single(c => c.Column == FinanceBaseValues.SubmissionStatusColumn).Values.Contains(status!)
            && conditions.Single(c => c.Column == FinanceBaseValues.SubmissionReasonColumn).Values.Contains(code!);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Override: the happy path
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Override_WithThePrivilegeAndAReason_AcceptsTheSubmission_RecordsOnce_AndReleasesTheHold()
    {
        var w = await World.CreateAsync();
        IsHeld(w.Submission).Should().BeTrue("the fixture starts in the held set");

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        result.Outcome.Should().Be("Overridden");
        result.InvoiceId.Should().Be(World.InvoiceId);
        result.RecordId.Should().NotBeNull();
        w.RecordCreates.Should().Be(1, "exactly one Decision Record (D-100)");
        w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        w.Code.Should().Be(SubmissionReasonCodes.Overridden);
        IsHeld(w.Submission).Should().BeFalse("Accepted leaves the held set, which releases the invoice (D-101)");
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview, "an override releases the hold; the invoice stays In Review for approval");
        w.Transitions.Should().BeEmpty();
        w.Notified.Should().ContainSingle().Which.Should().Be(
            new SubmissionDecidedNotification(World.SubmissionId, SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Internal, World.InvoiceId, null));
    }

    [Fact]
    public async Task Override_RecordsBeforeAnythingChanges_AndHoldsThePerInvoiceLockAroundTheWholeDecision()
    {
        var w = await World.CreateAsync();

        await w.OverrideAsync();

        w.Order.Should().Equal("lock", "store", "record", "update", "notify", "unlock");
        w.LockKeys.Should().Equal(SubmissionOverrideService.LockKey(World.InvoiceId));
        w.HeldKeys.Should().BeEmpty("the lock is released");
    }

    [Fact]
    public async Task Override_StoresWhoWhenAndWhyOnTheSubmission_AndKeepsEveryFindingUntouched()
    {
        var w = await World.CreateAsync();
        var before = JsonNode.Parse(w.FindingsJson)!.AsObject();

        var result = await w.OverrideAsync();

        var after = JsonNode.Parse(w.FindingsJson)!.AsObject();
        after["override"]!["recordId"]!.GetValue<string>().Should().Be(result.RecordId!.Value.ToString("D"));
        after["override"]!["bySystemUserId"]!.GetValue<string>().Should().Be(World.Caller.ToString("D"));
        after["override"]!["reason"]!.GetValue<string>().Should().Be(Reason);
        after["override"]!["on"]!.GetValue<string>().Should().StartWith("2026-10-20T09:00:00");
        foreach (var (name, value) in before)
        {
            after[name]!.ToJsonString().Should().Be(value!.ToJsonString(), $"the stored member '{name}' is unchanged: an override never rewrites a finding");
        }
    }

    [Fact]
    public async Task Override_TheDecisionRecordIsBuiltOnlyFromTheCallerAndTheStoredFindings()
    {
        var w = await World.CreateAsync();

        await w.OverrideAsync("  " + Reason + "  ");

        var decision = w.Recorded.Should().ContainSingle().Which;
        decision.ActionCode.Should().Be("override-submission");
        decision.DecisionId.Should().Be(SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), "derived server-side from the submission");
        decision.SubjectId.Should().Be(World.InvoiceId, "an override is recorded under the invoice (D-101)");
        decision.Decider.SystemUserId.Should().Be(World.Caller, "the decider is the authenticated caller");
        decision.Decider.Role.Should().Contain("eBilling Override");
        decision.Outcome.Should().Be("Overridden");
        decision.Reason.Should().Be(Reason, "trimmed");
        decision.ResolvedSignalIds.Should().BeNull("no Signal is open on this invoice, so the override resolves none (D-122 lists them when there are)");
        decision.PolicyVersionId.Should().NotBeNull().And.NotBe(Guid.Empty, "the first deciding finding's policy version, from the stored findings");
        decision.Values!.Values.Should().OnlyContain(v => v is string, "Values are strings only (074)");
        decision.Written.Should().ContainSingle().Which.Id.Should().Be(World.InvoiceId);
    }

    [Fact]
    public async Task Override_TheSameCallTwice_IsOneDecision_AndTheRepeatWritesNothing()
    {
        var w = await World.CreateAsync();
        var first = await w.OverrideAsync();
        var updates = w.Updates.Count;

        var second = await w.OverrideAsync();

        second.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        second.Outcome.Should().Be("Overridden");
        w.RecordCreates.Should().Be(1, "a repeat writes no second record");
        w.Updates.Should().HaveCount(updates);
        w.Notified.Should().HaveCount(1, "a repeat announces nothing");
        first.Kind.Should().Be(BlockedDecisionKind.Decided);
    }

    [Fact]
    public async Task Override_WhenTheNoticeFails_TheOverrideStands()
    {
        var w = await World.CreateAsync();
        w.SinkFault = new InvalidOperationException("sink down");

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Override: the privilege (D-76), fail closed
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Override_WithoutThePrivilege_Is403NotPermitted_AndNothingIsReadOrWritten()
    {
        var w = await World.CreateAsync();
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.override.not_permitted");
        w.AssertNothingChanged();
        w.Order.Should().BeEmpty("refused before the lock, before any read");
        w.Entities.Invocations.Should().BeEmpty("a refusal says nothing about the submission");
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Fact]
    public async Task Override_WhenThePrivilegeCheckFaults_RefusesLikeNoPrivilege()
    {
        var w = await World.CreateAsync();
        w.Probe.PrivilegeFault = new HttpRequestException("dataverse down");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.override.not_permitted");
        w.AssertNothingChanged();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Override_WithoutACallerToken_Is403_AndNothingChanges(string? token)
    {
        var w = await World.CreateAsync();

        var result = await w.OverrideAsync(token: token);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.override.not_permitted");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WhenTheCallerCannotBeIdentified_Is403_EvenWithThePrivilege()
    {
        var w = await World.CreateAsync();
        w.Probe.CallerSystemUserId = null;

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.override.not_permitted");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_AsksTheOverridePrivilege_OfDataverse_AsTheCaller_OnEveryCall()
    {
        var w = await World.CreateAsync();

        await w.OverrideAsync();
        await w.OverrideAsync();

        w.Probe.Calls.Should().HaveCount(2).And.OnlyContain(c => c.Privilege == "prvWritesprk_ebillingoverride" && c.HasToken);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // The reason
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public async Task Override_WithoutAReason_Is400ReasonRequired_AndNothingChanges(string? reason)
    {
        var w = await World.CreateAsync();

        var result = await w.OverrideAsync(reason);

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ebilling.reason_required");
        w.AssertNothingChanged();
        w.Order.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Return_WithoutAReason_Is400ReasonRequired_AndNothingChanges(string? reason)
    {
        var w = await World.CreateAsync();

        var result = await w.ReturnAsync(reason);

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ebilling.reason_required");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WithAReasonLongerThanTheColumn_Is400_AndNothingChanges()
    {
        var w = await World.CreateAsync();

        var result = await w.OverrideAsync(new string('x', 2001));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ebilling.reason_too_long");
        w.AssertNothingChanged();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Override: what may be overridden
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Override_OfAMissingSubmission_Is404()
    {
        var w = await World.CreateAsync();
        w.SubmissionAbsent = true;

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("ebilling.blocked.not_found");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WhenTheSubmissionCannotBeRead_Is503_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.ReadFault = new TimeoutException("dataverse");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be("ebilling.blocked.unavailable");
        w.AssertNothingChanged();
    }

    [Theory]
    [InlineData("spa", "ebilling.blocked.not_internal")]
    [InlineData("failed", "ebilling.blocked.submission_failed")]
    [InlineData("accepted-by-processing", "ebilling.blocked.already_accepted")]
    [InlineData("still-processing", "ebilling.blocked.still_processing")]
    [InlineData("rejected-other-reason", "ebilling.blocked.not_blocked")]
    [InlineData("received", "ebilling.blocked.not_blocked")]
    [InlineData("returned", "ebilling.blocked.already_returned")]
    public async Task Override_OfASubmissionThatIsNotBlocked_Is409_AndNothingChanges(string shape, string expected)
    {
        var w = await World.CreateAsync();
        Shape(w, shape);
        w.Updates.Clear();

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(expected);
        w.RecordCreates.Should().Be(0);
        w.Updates.Should().BeEmpty();
        w.Transitions.Should().BeEmpty();
        w.Notified.Should().BeEmpty();
    }

    [Theory]
    [InlineData("spa", "ebilling.blocked.not_internal")]
    [InlineData("failed", "ebilling.blocked.submission_failed")]
    [InlineData("accepted-by-processing", "ebilling.blocked.already_accepted")]
    [InlineData("still-processing", "ebilling.blocked.still_processing")]
    [InlineData("rejected-other-reason", "ebilling.blocked.not_blocked")]
    [InlineData("overridden", "ebilling.blocked.already_overridden")]
    public async Task Return_OfASubmissionThatIsNotBlocked_Is409_AndNothingChanges(string shape, string expected)
    {
        var w = await World.CreateAsync();
        Shape(w, shape);

        var result = await w.ReturnAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(expected);
        w.RecordCreates.Should().Be(0);
        w.Updates.Should().BeEmpty();
        w.Transitions.Should().BeEmpty();
        w.Notified.Should().BeEmpty();
    }

    private static void Shape(World w, string shape)
    {
        switch (shape)
        {
            case "spa":
                w.Submission["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa);
                break;
            case "failed":
                w.SetSubmission(InvoiceSubmissionStatus.Failed, SubmissionReasonCodes.ProcessingFailed);
                break;
            case "accepted-by-processing":
                w.SetSubmission(InvoiceSubmissionStatus.Accepted, null);
                break;
            case "still-processing":
                w.SetSubmission(InvoiceSubmissionStatus.Processing, SubmissionReasonCodes.Blocked);
                break;
            case "rejected-other-reason":
                w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.ReadNoInvoice);
                break;
            case "received":
                w.SetSubmission(InvoiceSubmissionStatus.Received, null);
                break;
            case "returned":
                w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Returned);
                break;
            case "overridden":
                w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(InvoiceLifecycleStatus.Approved)]
    [InlineData(InvoiceLifecycleStatus.SentToAp)]
    [InlineData(InvoiceLifecycleStatus.Paid)]
    [InlineData(InvoiceLifecycleStatus.Rejected)]
    [InlineData(InvoiceLifecycleStatus.Cancelled)]
    public async Task Override_WhenTheInvoiceIsNoLongerInReview_Is409InvoiceNotHeld_AndNothingChanges(InvoiceLifecycleStatus status)
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(status);

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.invoice_not_held");
        w.AssertNothingChanged();
        w.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
    }

    [Fact]
    public async Task Override_WhenTheInvoiceWasReturnedToTheFirm_Is409AlreadyReturned()
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(InvoiceLifecycleStatus.ReturnedToFirm);

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.already_returned");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WhenTheInvoiceIsGone_Is409InvoiceNotHeld()
    {
        var w = await World.CreateAsync();
        w.Invoice = null;

        var result = await w.OverrideAsync();

        result.ErrorCode.Should().Be("ebilling.blocked.invoice_not_held");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WhenTheSubmissionLinksNoInvoice_Is409_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.Submission[InvoiceSubmissionPersister.SubmissionInvoice] = null;

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.invoice_not_held");
        w.Order.Should().BeEmpty("no lock is taken for a submission with no invoice");
    }

    [Fact]
    public async Task Override_WhenTheInvoiceCannotBeRead_Is503_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.InvoiceReadFault = new TimeoutException("dataverse");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be("ebilling.blocked.unavailable");
        w.AssertNothingChanged();
        w.HeldKeys.Should().BeEmpty("the lock taken for the decision is released");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Override: the stored findings fail closed
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("not-an-object")]
    [InlineData("other-invoice")]
    [InlineData("not-blocked-kind")]
    [InlineData("no-deciding-finding")]
    public async Task Override_WhenTheStoredFindingsCannotBuildTheDecision_Is409_AndNothingChanges(string defect)
    {
        var w = await World.CreateAsync();
        switch (defect)
        {
            case "missing":
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = null;
                break;
            case "garbage":
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = "{not json";
                break;
            case "not-an-object":
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = "[1,2]";
                break;
            case "other-invoice":
                var json = JsonNode.Parse(w.FindingsJson)!.AsObject();
                json["invoiceId"] = Guid.NewGuid().ToString("D");
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = json.ToJsonString();
                break;
            case "not-blocked-kind":
                var kind = JsonNode.Parse(w.FindingsJson)!.AsObject();
                kind["kind"] = "Accepted";
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = kind.ToJsonString();
                break;
            case "no-deciding-finding":
                var none = JsonNode.Parse(w.FindingsJson)!.AsObject();
                none["decision"]!["deciding"] = new JsonArray();
                w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = none.ToJsonString();
                break;
        }

        var json0 = w.FindingsJson;
        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.findings_unreadable");
        w.RecordCreates.Should().Be(0);
        w.Updates.Should().BeEmpty();
        w.Notified.Should().BeEmpty();
        w.FindingsJson.Should().Be(json0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Serialisation, record-before-mutate, retries (074 caller obligations)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Override_WhenAnotherDecisionHoldsTheInvoiceLock_Is409InProgress_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.HeldKeys.Add(SubmissionOverrideService.LockKey(World.InvoiceId));

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.in_progress");
        w.AssertNothingChanged();
        w.HeldKeys.Should().Contain(SubmissionOverrideService.LockKey(World.InvoiceId), "a lock this call did not take is not released");
    }

    [Fact]
    public async Task Override_WhenTheLockCannotBeTaken_Is503_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.LockFault = new TimeoutException("redis");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be("ebilling.blocked.unavailable");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WhenTheLockCannotBeReleased_TheFinishedDecisionStillSucceeds()
    {
        var w = await World.CreateAsync();
        w.ReleaseFault = new TimeoutException("redis");

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact]
    public async Task Override_WhenTheDecisionRecordCannotBeWritten_Is503_AndTheSubmissionIsStillHeld()
    {
        var w = await World.CreateAsync();
        w.RecordFault = new TimeoutException("dataverse");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be("ebilling.blocked.record_unavailable");
        w.Updates.Should().BeEmpty("record before mutate: a failed record changes nothing");
        w.Notified.Should().BeEmpty();
        IsHeld(w.Submission).Should().BeTrue();
        w.HeldKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ReviewInvalid", "ebilling.blocked.decision_conflict", 409)]
    [InlineData("SubjectUnfileable", "ebilling.blocked.record_refused", 409)]
    [InlineData("OwnerRefused", "ebilling.blocked.record_refused", 409)]
    [InlineData("BillingRecordLookupFailed", "ebilling.blocked.record_unavailable", 503)]
    public async Task Override_WhenTheRecordWriterRefuses_TheRefusalIsMapped_AndNothingChanges(string reasonName, string code, int status)
    {
        var w = await World.CreateAsync();
        var reason = (string)typeof(DecisionRecordRefusalReason).GetField(reasonName)!.GetRawConstantValue()!;
        w.RecordFault = new DecisionRecordRefusedException(reason, "refused");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(status);
        result.ErrorCode.Should().Be(code);
        w.Updates.Should().BeEmpty();
        w.Notified.Should().BeEmpty();
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Fact]
    public async Task Override_ARetryAfterTheSubmissionWriteFailed_FindsTheSameRecordAndFinishes()
    {
        var w = await World.CreateAsync();
        w.UpdateFault = new TimeoutException("dataverse");
        var failed = await w.OverrideAsync();
        failed.StatusCode.Should().Be(503);
        w.RecordCreates.Should().Be(1, "the record was written before the write that failed");
        IsHeld(w.Submission).Should().BeTrue("the submission is unchanged, so the retry is allowed");

        w.UpdateFault = null;
        var retried = await w.OverrideAsync();

        retried.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1, "the retry found the first attempt's record instead of writing another");
        w.Recorded.Should().HaveCount(2);
        w.Recorded.Select(d => d.DecisionId).Distinct().Should().ContainSingle();
        w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
    }

    [Fact]
    public async Task Override_WhenAConcurrentWriterOverrodeItFirst_ThisCallIsAnAlreadyDecidedNoOp()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            // Another writer finishes the same override between this call's read and its conditional write.
            w.OnUpdate = null;
            w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1; // the other writer moved the row
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.RecordCreates.Should().Be(1);
        w.Notified.Should().BeEmpty("the call that wrote the state announces it, not the one that lost the race");
    }

    [Fact]
    public async Task Override_WhenTheSubmissionChangedUnderIt_Is409ConcurrentChange_AndOverwritesNothing()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            // A reprocess rewrites the submission (a different state, not an override).
            w.OnUpdate = null;
            w.SetSubmission(InvoiceSubmissionStatus.Processing, SubmissionReasonCodes.Blocked);
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1; // the other writer moved the row
        };

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.concurrent_change");
        w.Updates.Should().BeEmpty("the conditional write did not land");
        w.Status.Should().Be(InvoiceSubmissionStatus.Processing, "the other writer's state is not overwritten");
        w.Notified.Should().BeEmpty();
    }

    [Fact]
    public async Task Override_WhenTheDecisionCannotBeAddedToTheFindings_Is409_AndTheSubmissionIsNotAccepted()
    {
        var w = await World.CreateAsync(json =>
        {
            json["padding"] = new string('p', InvoiceSubmissionPersister.FindingsJsonMaxLength);
            return json;
        });

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.findings_too_large");
        w.Updates.Should().BeEmpty();
        IsHeld(w.Submission).Should().BeTrue("the submission is not released without its decision stored");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Return (D-73: no privilege)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Return_NeedsNoPrivilege_RecordsOnce_ReturnsTheInvoice_AndLeavesTheHeldSet()
    {
        var w = await World.CreateAsync();
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var result = await w.ReturnAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        result.Outcome.Should().Be("Returned");
        w.Probe.Calls.Should().BeEmpty("Return never asks the override privilege (D-73)");
        w.RecordCreates.Should().Be(1);
        w.Recorded.Single().ActionCode.Should().Be("return-to-firm");
        w.Recorded.Single().DecisionId.Should().Be(SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId));
        w.Recorded.Single().Decider.SystemUserId.Should().Be(World.Caller);
        w.Transitions.Should().Equal(InvoiceLifecycleStatus.ReturnedToFirm);
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.ReturnedToFirm);
        w.Status.Should().Be(InvoiceSubmissionStatus.Rejected, "a returned submission stays Rejected");
        w.Code.Should().Be(SubmissionReasonCodes.Returned);
        IsHeld(w.Submission).Should().BeFalse();
        w.Notified.Should().ContainSingle("the firm is notified of a return (D-143)").Which.Should().Be(
            new SubmissionDecidedNotification(World.SubmissionId, SubmissionDecisionOutcome.Returned, InvoiceSubmissionChannel.Internal, World.InvoiceId, SubmissionReasonCodes.Returned));
        w.Order.Should().Equal("lock", "store", "record", "transition", "update", "notify", "unlock");
    }

    [Fact]
    public async Task Return_StoresWhoWhenAndWhy_AndTheOverrideDecisionIdIsNotTheReturnDecisionId()
    {
        var w = await World.CreateAsync();

        var result = await w.ReturnAsync();

        var after = JsonNode.Parse(w.FindingsJson)!.AsObject();
        after["return"]!["recordId"]!.GetValue<string>().Should().Be(result.RecordId!.Value.ToString("D"));
        after["return"]!["bySystemUserId"]!.GetValue<string>().Should().Be(World.Caller.ToString("D"));
        after["return"]!["reason"]!.GetValue<string>().Should().Be(Reason);
        after.ContainsKey("override").Should().BeFalse();
        SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId).Should().NotBe(SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Return_WhenTheCallerCannotBeIdentified_Is403_AndNothingChanges(string? token)
    {
        var w = await World.CreateAsync();

        var result = await w.ReturnAsync(token: token);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.blocked.decider_unverified");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Return_WhenTheCallerIdentityIsUnknownOrFaults_Is403_AndNothingChanges()
    {
        var unknown = await World.CreateAsync();
        unknown.Probe.CallerSystemUserId = null;
        var faulted = await World.CreateAsync();
        faulted.Probe.IdentityFault = new HttpRequestException("dataverse down");

        foreach (var w in new[] { unknown, faulted })
        {
            var result = await w.ReturnAsync();

            result.StatusCode.Should().Be(403);
            result.ErrorCode.Should().Be("ebilling.blocked.decider_unverified");
            w.AssertNothingChanged();
        }
    }

    [Fact]
    public async Task Override_WhenTheCallerIdentityFaults_Is403NotPermitted_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.Probe.IdentityFault = new HttpRequestException("dataverse down");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ebilling.override.not_permitted");
        w.AssertNothingChanged();
    }

    [Fact]
    public async Task Return_RecordsBeforeItMovesTheInvoice_AndAFailedRecordMovesNothing()
    {
        var w = await World.CreateAsync();
        w.RecordFault = new TimeoutException("dataverse");

        var result = await w.ReturnAsync();

        result.StatusCode.Should().Be(503);
        w.Transitions.Should().BeEmpty("the invoice is not moved without its record");
        w.Updates.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview);
    }

    [Theory]
    [InlineData(InvoiceTransitionReasons.ConcurrentChange, "ebilling.blocked.concurrent_change")]
    [InlineData(InvoiceTransitionReasons.NotAllowed, "ebilling.blocked.invoice_not_held")]
    [InlineData(InvoiceTransitionReasons.NotFound, "ebilling.blocked.invoice_not_held")]
    public async Task Return_WhenTheLifecycleRefusesTheMove_IsRefused_AndTheSubmissionStaysHeld(string reason, string code)
    {
        var w = await World.CreateAsync();
        w.TransitionRefusal = reason;

        var result = await w.ReturnAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(code);
        w.Updates.Should().BeEmpty("the submission is not marked returned while its invoice was not");
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Fact]
    public async Task Return_WhenTheLifecycleFaults_Is503_AndARetryFinishesWithOneRecord()
    {
        var w = await World.CreateAsync();
        w.TransitionFault = new TimeoutException("dataverse");
        (await w.ReturnAsync()).StatusCode.Should().Be(503);

        w.TransitionFault = null;
        var retried = await w.ReturnAsync();

        retried.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.ReturnedToFirm);
        w.Code.Should().Be(SubmissionReasonCodes.Returned);
    }

    [Fact]
    public async Task Return_AnInterruptedReturn_WhereTheInvoiceWasAlreadyMoved_FinishesWithoutMovingItAgain()
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(InvoiceLifecycleStatus.ReturnedToFirm);

        var result = await w.ReturnAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Transitions.Should().BeEmpty("the invoice is already Returned to Firm");
        w.Code.Should().Be(SubmissionReasonCodes.Returned);
        w.RecordCreates.Should().Be(1);
    }

    [Fact]
    public async Task Return_TheSameCallTwice_IsOneDecision()
    {
        var w = await World.CreateAsync();
        await w.ReturnAsync();

        var second = await w.ReturnAsync();

        second.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.RecordCreates.Should().Be(1);
        w.Transitions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Return_ThenOverride_IsRefused_AndOverrideThenReturn_IsRefused()
    {
        var returned = await World.CreateAsync();
        await returned.ReturnAsync();
        var overrideAfterReturn = await returned.OverrideAsync();
        overrideAfterReturn.ErrorCode.Should().Be("ebilling.blocked.already_returned");
        returned.RecordCreates.Should().Be(1);

        var overridden = await World.CreateAsync();
        await overridden.OverrideAsync();
        var returnAfterOverride = await overridden.ReturnAsync();
        returnAfterOverride.ErrorCode.Should().Be("ebilling.blocked.already_overridden");
        overridden.RecordCreates.Should().Be(1);
        overridden.Transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Return_WhenTheInvoiceIsAlreadyApprovedOrGone_Is409_AndNothingChanges()
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(InvoiceLifecycleStatus.Approved);

        var result = await w.ReturnAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.blocked.invoice_not_held");
        w.AssertNothingChanged();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Read
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_OfABlockedSubmission_ShowsTheBlockingFindings_AndCanOverrideForAPrivilegedCaller()
    {
        var w = await World.CreateAsync();

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view.Should().NotBeNull();
        view!.State.Should().Be("Blocked");
        view.InvoiceId.Should().Be(World.InvoiceId);
        view.CanOverride.Should().BeTrue();
        view.CanReturn.Should().BeTrue();
        view.Findings.Should().NotBeEmpty();
        view.Findings.Should().Contain(f => f.Blocking && !f.Overridden && !string.IsNullOrEmpty(f.ReasonCode));
        view.Override.Should().BeNull();
        view.Return.Should().BeNull();
        w.Updates.Should().BeEmpty("a read writes nothing");
    }

    [Fact]
    public async Task Get_ForACallerWithoutThePrivilege_CannotOverride_ButCanReturn()
    {
        var w = await World.CreateAsync();
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.CanOverride.Should().BeFalse();
        view.CanReturn.Should().BeTrue("Return needs no privilege (D-73)");
    }

    [Fact]
    public async Task Get_WhenThePrivilegeCheckFaults_CanOverrideIsFalse()
    {
        var w = await World.CreateAsync();
        w.Probe.PrivilegeFault = new HttpRequestException("down");

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.CanOverride.Should().BeFalse();
        view.State.Should().Be("Blocked");
    }

    [Fact]
    public async Task Get_WithoutAToken_CanOverrideIsFalse()
    {
        var w = await World.CreateAsync();

        var view = await w.Service.GetAsync(World.SubmissionId, null, CancellationToken.None);

        view!.CanOverride.Should().BeFalse();
    }

    [Theory]
    [InlineData(InvoiceLifecycleStatus.Approved)]
    [InlineData(InvoiceLifecycleStatus.Paid)]
    public async Task Get_WhenTheInvoiceIsNoLongerInReview_CannotOverrideOrReturn(InvoiceLifecycleStatus status)
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(status);

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.CanOverride.Should().BeFalse();
        view.CanReturn.Should().BeFalse();
    }

    [Fact]
    public async Task Get_OfASubmissionLinkingAnInvoiceItDidNotCreate_CannotOverrideOrReturn_AndNeverAsksThePrivilege()
    {
        var w = await World.CreateAsync();
        PointAtPlantedInvoice(w);

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.State.Should().Be("Blocked");
        view.CanOverride.Should().BeFalse("the POST refuses a planted link with InvoiceNotHeld");
        view.CanReturn.Should().BeFalse();
        w.Probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_AfterAnOverride_MarksTheBlockingFindingsOverridden_WithoutRemovingThem()
    {
        var w = await World.CreateAsync();
        var before = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);
        await w.OverrideAsync();

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.State.Should().Be("Overridden");
        view.CanOverride.Should().BeFalse();
        view.CanReturn.Should().BeFalse();
        view.Findings.Should().HaveCount(before!.Findings.Count, "an override never deletes a finding");
        view.Findings.Where(f => f.Blocking).Should().NotBeEmpty().And.OnlyContain(f => f.Overridden);
        view.Override.Should().NotBeNull();
        view.Override!.BySystemUserId.Should().Be(World.Caller);
        view.Override.Reason.Should().Be(Reason);
    }

    [Fact]
    public async Task Get_AfterAReturn_ShowsReturned()
    {
        var w = await World.CreateAsync();
        await w.ReturnAsync();

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.State.Should().Be("Returned");
        view.Return.Should().NotBeNull();
        view.Findings.Should().OnlyContain(f => !f.Overridden);
    }

    [Fact]
    public async Task Get_OfAnSpaSubmission_IsNotBlocked_AndNeverAsksThePrivilege()
    {
        var w = await World.CreateAsync();
        Shape(w, "spa");

        var view = await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        view!.State.Should().Be("NotBlocked");
        view.CanOverride.Should().BeFalse();
        view.CanReturn.Should().BeFalse();
        w.Probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_OfAMissingSubmission_IsNull_AndOfAnEmptyId_IsNull()
    {
        var w = await World.CreateAsync();
        w.SubmissionAbsent = true;

        (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None)).Should().BeNull();
        (await w.Service.GetAsync(Guid.Empty, World.Token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Get_WhenTheReadFaults_Throws503_NeverNotFound()
    {
        var w = await World.CreateAsync();
        w.ReadFault = new TimeoutException("dataverse");

        var act = () => w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Read: D-176 (task 119 round 2), a Duplicate's earlier invoice judged as the caller (DuplicateDetailGate, shared with /review)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid FirmId = Guid.Parse("10500000-0000-4000-8000-0000000000f1");
    private static readonly Guid EarlierInvoice = Guid.Parse("10500000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtherEarlierInvoice = Guid.Parse("10500000-0000-4000-8000-0000000000e2");
    private const string EarlierNumber = "F-090.1";
    private const string OtherEarlierNumber = "F-080.1";

    private static JsonObject ValueRead(string name, string? value, int? line, string? field = null) => new()
    {
        ["name"] = name, ["value"] = value, ["line"] = line, ["field"] = field, ["confidence"] = field is null ? null : 0.97,
        ["belowReliabilityThreshold"] = false,
    };

    /// <summary>A stored finding as 049 writes DuplicateHandler's: the line's values, the current invoice number, then (when visible at
    /// write time) <c>duplicateOf.trackingNumber</c>, <c>duplicateOf.lineIndex</c> and <c>duplicateOf.invoiceId</c>; a group scrubbed at
    /// write time (D-112) has no values at all, as the persister writes it.</summary>
    private static JsonObject StoredFinding(
        string policy, string reason, int? line, string? number = null, Guid? earlierInvoice = null, bool withheld = false, string ruleType = "Duplicate")
    {
        var finding = new JsonObject
        {
            ["ruleType"] = ruleType, ["policyCode"] = policy, ["policyVersionId"] = Guid.NewGuid().ToString("D"), ["clause"] = "Guideline 9",
            ["clauseLocation"] = null, ["subject"] = line is null ? "Invoice" : "Line", ["line"] = line, ["reasonCode"] = reason,
            ["detailWithheld"] = withheld, ["consequence"] = "Flag", ["rejects"] = false, ["heldBecause"] = null,
        };
        if (withheld)
        {
            return finding;
        }

        var values = new JsonArray(
            ValueRead("line.timekeeperName", "J. Smith", line, "Timekeeper"), ValueRead("line.date", "2026-09-01", line, "Date"),
            ValueRead("invoice.firmInvoiceNumber", "INV-100", null));
        if (number is not null)
        {
            values.Add(ValueRead("duplicateOf.trackingNumber", number, null));
            values.Add(ValueRead("duplicateOf.lineIndex", "3", null));
        }

        if (earlierInvoice is { } id)
        {
            values.Add(ValueRead("duplicateOf.invoiceId", id.ToString("D"), null));
        }

        finding["valuesRead"] = values;
        return finding;
    }

    /// <summary>The blocked fixture (its real InvoiceAge finding kept) plus a RateCard finding on line 0 and the given findings.</summary>
    private static async Task<World> DuplicateWorldAsync(params JsonObject[] added)
    {
        var w = await World.CreateAsync(json =>
        {
            var findings = (JsonArray)json["findings"]!;
            foreach (var finding in added.Prepend(StoredFinding("BILL-RATE", "rate.above_card", 0, ruleType: "RateCard")))
            {
                finding["index"] = findings.Count;
                findings.Add(finding);
            }

            return json;
        });
        return w;
    }

    private static Task<BlockedSubmissionView?> GetBlockedAsync(World w) => w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None);

    private static BlockedFindingView Of(BlockedSubmissionView view, string policy, int? line = 0) =>
        view.Findings.Single(f => f.PolicyCode == policy && f.Line == line);

    private static void ShouldBeShown(BlockedFindingView finding)
    {
        finding.DetailWithheld.Should().BeFalse();
        finding.ValuesRead.Should().Contain(v => v.Name == "line.timekeeperName");
    }

    private static void ShouldBeWithheld(BlockedFindingView finding)
    {
        finding.DetailWithheld.Should().BeTrue();
        finding.ValuesRead.Should().BeEmpty("no duplicateOf.* entry and no line values (the write-time withheld shape)");
    }

    /// <summary>The whole response, as serialized, names neither the earlier tracking number nor the earlier invoice id (K3).</summary>
    private static void ShouldNotLeak(BlockedSubmissionView view, string number, Guid invoice)
    {
        var json = JsonSerializer.Serialize(view);
        json.Should().NotContain(number);
        json.Should().NotContain(invoice.ToString("D"));
    }

    /// <summary>The non-Duplicate findings come back as stored: the RateCard one with its values, the fixture's own blocking finding.</summary>
    private static void OthersUntouched(BlockedSubmissionView view)
    {
        var rate = Of(view, "BILL-RATE");
        rate.DetailWithheld.Should().BeFalse();
        rate.ValuesRead.Should().HaveCount(3);
        view.Findings.Where(f => f.PolicyCode is not ("BILL-DUP" or "BILL-DUPINV")).Should().OnlyContain(f => !f.DetailWithheld);
        view.Findings.Should().Contain(f => f.Blocking, "the fixture's blocking finding is untouched");
    }

    [Fact(DisplayName = "119 r2: blocked view, a Duplicate whose earlier invoice (duplicateOf.invoiceId) the caller reads is returned unchanged, read as the caller")]
    public async Task Get_D176_ReadableEarlierInvoice_IsUnchanged()
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice));
        w.ReadableInvoices.Add(EarlierInvoice);

        var view = (await GetBlockedAsync(w))!;

        var duplicate = Of(view, "BILL-DUP");
        ShouldBeShown(duplicate);
        duplicate.ValuesRead.Should().Contain(v => v.Name == "duplicateOf.trackingNumber" && v.Value == EarlierNumber);
        OthersUntouched(view);
        w.CallerReads("sprk_invoices").Should().Be(1, "the earlier invoice is read as the caller");
        w.CallerPaths.Should().ContainSingle("an id needs only the invoice read");
    }

    [Theory(DisplayName = "#2294: blocked view, a cross-invoice Duplicate identified only by its tracking number is withheld, even when the number would resolve to an invoice the caller reads; no number is looked up")]
    [InlineData("DuplicateOfPriorSubmissionLine", 0)]
    [InlineData("duplicate.line_prior_submission", 0)]
    [InlineData("duplicate.invoice_number", null)]
    public async Task Get_D176_NumberOnlyDuplicate_IsWithheld_WhateverTheNumberWouldResolveTo(string reason, int? line)
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", reason, line, EarlierNumber));
        w.ReadableInvoices.UnionWith([EarlierInvoice, World.InvoiceId]);

        var view = (await GetBlockedAsync(w))!;

        ShouldBeWithheld(Of(view, "BILL-DUP", line));
        OthersUntouched(view);
        ShouldNotLeak(view, EarlierNumber, EarlierInvoice);
        w.CallerPaths.Should().BeEmpty("a number-only Duplicate is judged by nothing: no read, no lookup");
    }

    [Theory(DisplayName = "#2294: blocked view, a same-invoice repeat (duplicate.line_same_invoice) keeps its detail when the caller reads the submission's own invoice, and is withheld when not")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_D176_SameInvoiceRepeat_IsJudgedByTheCallersReadOfTheSubmissionsInvoice(bool ownInvoiceReadable)
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "duplicate.line_same_invoice", 0, "INV-100.1"));
        w.ReadableInvoices.Add(EarlierInvoice);
        if (ownInvoiceReadable)
        {
            w.ReadableInvoices.Add(World.InvoiceId);
        }

        var view = (await GetBlockedAsync(w))!;

        if (ownInvoiceReadable)
        {
            ShouldBeShown(Of(view, "BILL-DUP"));
        }
        else
        {
            ShouldBeWithheld(Of(view, "BILL-DUP"));
        }

        OthersUntouched(view);
        Uri.UnescapeDataString(w.CallerPaths.Single()).Should().StartWith("sprk_invoices?").And.Contain(World.InvoiceId.ToString("D"));
    }

    [Theory(DisplayName = "119 r2: blocked view, an earlier invoice the caller cannot read withholds the Duplicate group; nothing in the response names it")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_D176_UnreadableEarlierInvoice_IsWithheld(bool byId)
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, byId ? EarlierInvoice : null));

        var view = (await GetBlockedAsync(w))!;

        ShouldBeWithheld(Of(view, "BILL-DUP"));
        OthersUntouched(view);
        ShouldNotLeak(view, EarlierNumber, EarlierInvoice);
    }

    [Theory(DisplayName = "119 r5: blocked view, a Duplicate whose stored values give no usable reference to the earlier invoice (a non-GUID id, duplicateOf.* values that are blank, a number only) is withheld, and the caller is asked nothing")]
    [InlineData("not-a-guid")]
    [InlineData("blank-values")]
    [InlineData("number-only")]
    public async Task Get_D176_UnusableReference_IsWithheld_AndAsksTheCallerNothing(string shape)
    {
        var duplicate = StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0);
        var values = (JsonArray)duplicate["valuesRead"]!;
        switch (shape)
        {
            case "not-a-guid":
                values.Add(ValueRead("duplicateOf.invoiceId", "not-a-guid", null));
                break;
            case "blank-values":
                // The Duplicate names its prior but the values are empty. (A finding with NO duplicateOf.* name at all is not a Duplicate to
                // the gate or to the Signal read: it quotes no earlier invoice and is returned as stored.)
                values.Add(ValueRead("duplicateOf.invoiceId", "", null));
                values.Add(ValueRead("duplicateOf.trackingNumber", "  ", null));
                break;
            case "number-only":
                values.Add(ValueRead("duplicateOf.trackingNumber", EarlierNumber, null));
                break;
        }

        var w = await DuplicateWorldAsync(duplicate);

        // Everything the caller could read is readable: only the missing reference withholds.
        w.ReadableInvoices.UnionWith([EarlierInvoice, World.InvoiceId]);

        var view = (await GetBlockedAsync(w))!;

        ShouldBeWithheld(Of(view, "BILL-DUP"));
        OthersUntouched(view);
        ShouldNotLeak(view, EarlierNumber, EarlierInvoice);
        JsonSerializer.Serialize(view).Should().NotContain("not-a-guid");
        w.CallerPaths.Should().BeEmpty("no usable reference: no caller read is made for the earlier-invoice check");
    }

    [Theory(DisplayName = "119 r2: blocked view, a 500 or a throw in the caller's invoice read withholds; the view still loads")]
    [InlineData("500")]
    [InlineData("throw")]
    public async Task Get_D176_ReadFaults_Withhold(string failure)
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice));
        w.ReadableInvoices.Add(EarlierInvoice);
        switch (failure)
        {
            case "500":
                w.CallerFault = _ => Task.FromResult(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom"));
                break;
            case "throw":
                w.CallerFault = _ => Task.FromException<DataverseUserResponse>(new TimeoutException("read timed out"));
                break;
        }

        var view = await GetBlockedAsync(w);

        view.Should().NotBeNull("the check never fails the view (200)");
        ShouldBeWithheld(Of(view!, "BILL-DUP"));
        OthersUntouched(view!);
        ShouldNotLeak(view!, EarlierNumber, EarlierInvoice);
    }

    [Fact(DisplayName = "119 r2: blocked view, a finding withheld at write time (no duplicateOf.*) stays withheld and asks the caller nothing")]
    public async Task Get_D176_WriteTimeWithheld_StaysWithheld()
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, withheld: true));

        var view = (await GetBlockedAsync(w))!;

        ShouldBeWithheld(Of(view, "BILL-DUP"));
        OthersUntouched(view);
        w.CallerPaths.Should().BeEmpty("no Duplicate quoting an earlier invoice: nothing to judge");
    }

    [Fact(DisplayName = "119 r2: blocked view, with no Duplicate every finding is returned as stored and the caller is asked nothing")]
    public async Task Get_D176_NonDuplicateFindings_AreUntouched()
    {
        var w = await DuplicateWorldAsync(StoredFinding("BILL-STAFF", "staffing.too_many", null, ruleType: "Staffing"));

        var view = (await GetBlockedAsync(w))!;

        OthersUntouched(view);
        Of(view, "BILL-STAFF", null).ValuesRead.Should().HaveCount(3);
        w.CallerPaths.Should().BeEmpty();
    }

    [Fact(DisplayName = "119 r2: blocked view, two Duplicate groups, one readable and one not: only the unreadable one is withheld")]
    public async Task Get_D176_MixedReadability_WithholdsOnlyTheUnreadableGroup()
    {
        var w = await DuplicateWorldAsync(
            StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
            StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 1, OtherEarlierNumber, OtherEarlierInvoice));
        w.ReadableInvoices.Add(EarlierInvoice);

        var view = (await GetBlockedAsync(w))!;

        ShouldBeShown(Of(view, "BILL-DUP", 0));
        ShouldBeWithheld(Of(view, "BILL-DUP", 1));
        OthersUntouched(view);
        JsonSerializer.Serialize(view).Should().Contain(EarlierNumber, "the readable group is returned whole");
        ShouldNotLeak(view, OtherEarlierNumber, OtherEarlierInvoice);
    }

    [Fact(DisplayName = "119 r2: blocked view, one check per response: one caller invoice read whatever the number of Duplicates")]
    public async Task Get_D176_OneCheckPerResponse()
    {
        var w = await DuplicateWorldAsync(
            StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
            StoredFinding("BILL-DUP", "duplicate.line_same_invoice", 1, "INV-100.1"),
            StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 2, OtherEarlierNumber, OtherEarlierInvoice),
            StoredFinding("BILL-DUPINV", "DuplicateInvoiceNumber", null, OtherEarlierNumber, OtherEarlierInvoice));
        w.ReadableInvoices.UnionWith([EarlierInvoice, OtherEarlierInvoice, World.InvoiceId]);

        var view = (await GetBlockedAsync(w))!;

        view.Findings.Where(f => f.PolicyCode is "BILL-DUP" or "BILL-DUPINV").Should().HaveCount(4).And.OnlyContain(f => !f.DetailWithheld);
        w.CallerReads("sprk_invoices").Should().Be(1);
        w.CallerPaths.Should().ContainSingle("ids and the own invoice of a same-invoice repeat share one read");
    }

    [Fact(DisplayName = "119 r2: blocked view, an exception escaping the check withholds every Duplicate group, warns without detail, and the view still loads")]
    public async Task Get_D176_CheckThatThrows_WithholdsEveryDuplicate()
    {
        var w = await DuplicateWorldAsync(
            StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
            StoredFinding("BILL-DUPINV", "DuplicateInvoiceNumber", null, EarlierNumber, EarlierInvoice));
        w.ReadableInvoices.Add(EarlierInvoice);
        // A null response makes EarlierInvoiceReadability throw past its own guard (a NullReferenceException after the read): only the
        // gate's wrap stands between it and the route.
        w.CallerFault = _ => Task.FromResult<DataverseUserResponse>(null!);

        var view = await GetBlockedAsync(w);

        view.Should().NotBeNull();
        ShouldBeWithheld(Of(view!, "BILL-DUP"));
        ShouldBeWithheld(Of(view!, "BILL-DUPINV", null));
        OthersUntouched(view!);
        ShouldNotLeak(view!, EarlierNumber, EarlierInvoice);
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("Blocked submission: the earlier-invoice check faulted (NullReferenceException)", StringComparison.Ordinal));
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(EarlierNumber, StringComparison.Ordinal)
            || e.Message.Contains(EarlierInvoice.ToString("D"), StringComparison.Ordinal), "no earlier-invoice detail in the log");
    }

    [Fact(DisplayName = "119 r2: blocked view, the request's cancellation during the check propagates")]
    public async Task Get_D176_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var w = await DuplicateWorldAsync(StoredFinding("BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice));
        w.CallerFault = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var act = () => w.Service.GetAsync(World.SubmissionId, World.Token, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Pins
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ThePrivilegeName_IsWriteOnTheCarrierTable_TheDeployScriptCreates()
    {
        EbillingOverridePrivilege.CarrierTable.Should().Be("sprk_ebillingoverride");
        EbillingOverridePrivilege.PrivilegeName.Should().Be("prvWritesprk_ebillingoverride");
        EbillingOverridePrivilege.RoleName.Should().Be("eBilling Override");

        var script = File.ReadAllText(FindFromRepoRoot("scripts", "ebilling", "Deploy-EbillingOverridePrivilegeSchema.ps1"));
        script.Should().Contain("sprk_ebillingoverride", "the privilege's table is the one the script creates");
        script.Should().Contain("prvWritesprk_ebillingoverride", "the script verifies the exact privilege the service asks for");
        script.Should().Contain("eBilling Override", "the script creates the role the service documents");
    }

    [Fact]
    public void TheOverrideReasonCodes_AreThoseTheHeldPredicateAndTheSpendPathsExpect()
    {
        SubmissionReasonCodes.Overridden.Should().Be("ebilling.submission.overridden");
        SubmissionReasonCodes.Returned.Should().Be("ebilling.submission.returned");
        SubmissionReasonCodes.Overridden.Should().NotBe(FinanceBaseValues.BlockedReasonCode);
        SubmissionReasonCodes.Returned.Should().NotBe(FinanceBaseValues.BlockedReasonCode);
        SubmissionOverrideService.LockKey(World.InvoiceId).Should().Be($"ebilling-invoice-decision:{World.InvoiceId:N}");
    }

    [Fact]
    public void TheErrorCodes_AreAStableContract()
    {
        BlockedSubmissionErrors.NotPermitted.Should().Be("ebilling.override.not_permitted");
        BlockedSubmissionErrors.ReasonRequired.Should().Be("ebilling.reason_required");
        BlockedSubmissionErrors.AlreadyOverridden.Should().Be("ebilling.blocked.already_overridden");
        BlockedSubmissionErrors.AlreadyReturned.Should().Be("ebilling.blocked.already_returned");
        BlockedSubmissionErrors.NotInternal.Should().Be("ebilling.blocked.not_internal");
    }

    [Fact]
    public async Task Logs_CarryIdsAndCodesOnly_NeverTheReason()
    {
        var w = await World.CreateAsync();
        const string secret = "CONFIDENTIAL-negotiation-note-7731";
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
        await w.OverrideAsync(secret);
        w.Probe.Hold(EbillingOverridePrivilege.PrivilegeName);
        w.RecordFault = new TimeoutException("dataverse " + secret);
        await w.OverrideAsync(secret);
        w.RecordFault = null;
        await w.OverrideAsync(secret);
        await w.ReturnAsync(secret);

        w.Log.Entries.Should().NotBeEmpty();
        w.Log.Entries.Where(e => e.Message.Contains("CONFIDENTIAL", StringComparison.Ordinal))
            .Where(e => !e.Message.Contains("dataverse CONFIDENTIAL", StringComparison.Ordinal))
            .Should().BeEmpty("the reason text is never logged by this service");
    }

    [Fact]
    public async Task BuildDecision_IsDeterministic_SoEveryAttemptBuildsTheSameDecision()
    {
        var w = await World.CreateAsync();
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(w.FindingsJson)!;

        var set = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), []);
        var a = SubmissionOverrideService.BuildDecision(SubmissionOverrideService.Verb.Override, stored, World.SubmissionId, World.InvoiceId, World.Caller, Reason, set);
        var b = SubmissionOverrideService.BuildDecision(SubmissionOverrideService.Verb.Override, stored, World.SubmissionId, World.InvoiceId, World.Caller, Reason, set);

        a.DecisionId.Should().Be(b.DecisionId);
        a.Facts.Keys.Should().BeEquivalentTo(b.Facts.Keys);
        a.Values.Should().BeEquivalentTo(b.Values);
        a.Facts.Keys.Should().NotContain("findings", "the facts carry the scrubbed plan, never the raw findings (D-112)");
    }

    [Theory(DisplayName = "122 (#2235): the override and return records store the withheld sentence for a Duplicate reason, never the earlier invoice; a non-Duplicate sentence is unchanged")]
    [InlineData(true, "duplicate.invoice_number")]
    [InlineData(true, "duplicate.line_prior_submission")]
    [InlineData(true, "duplicate.line_same_invoice")]
    [InlineData(false, "duplicate.invoice_number")]
    [InlineData(false, "duplicate.line_prior_submission")]
    [InlineData(false, "duplicate.line_same_invoice")]
    public async Task BuildDecision_StoresTheWithheldSentence_ForADuplicateReason(bool isOverride, string duplicateCode)
    {
        var verb = isOverride ? SubmissionOverrideService.Verb.Override : SubmissionOverrideService.Verb.Return;
        const string otherSentence = "Line 2 bills 0.3 hours, not a multiple of 0.1.";
        var w = await World.CreateAsync();
        var parsed = InvoiceSubmissionAfterSteps.StoredFindings.Parse(w.FindingsJson)!;
        parsed.Plan.Should().NotBeEmpty("the fixture's blocked submission has a Signal plan to copy the entry shape from");
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
                basis with { ReasonCodes = [duplicateCode], Sentence = $"Invoice INV-100 duplicates {EarlierNumber} ({EarlierInvoice:D})." },
                basis with { ReasonCodes = ["time.increment"], Sentence = otherSentence },
            ],
        };
        var set = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), []);

        var decision = SubmissionOverrideService.BuildDecision(verb, stored, World.SubmissionId, World.InvoiceId, World.Caller, Reason, set);

        var reasons = decision.Facts["reasons"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object?>>>().Subject.ToList();
        reasons.Should().HaveCount(2);
        reasons[0]["sentence"].Should().Be(Sprk.Bff.Api.Services.Signals.Billing.BillingRuleCatalogue.WithheldDuplicateTemplate);
        reasons[1]["sentence"].Should().Be(otherSentence);
        var snapshot = JsonSerializer.Serialize(decision.Facts);
        snapshot.Should().NotContain(EarlierNumber).And.NotContain(EarlierInvoice.ToString("D"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // D-122 / FR-51: the human-decision Signal close (the shape agreed with ontology-r1)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly DateTime FirstReadAt = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A world whose invoice has a line, with an Open Signal on the invoice, an Open and an Acknowledged one on the line, a
    /// resolved one on the line (not in the set) and an Open one on a line of ANOTHER invoice (not in the set).</summary>
    private static async Task<(World W, Guid Line, Guid OnInvoice, Guid OnLine, Guid Acknowledged, Guid Resolved, Guid Elsewhere)> SignalWorldAsync()
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var onInvoice = w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
        var onLine = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        var acknowledged = w.AddSignal("sprk_billingevent", line, "EB-TASK", SignalWriter.SignalStatusAcknowledged);
        var resolved = w.AddSignal("sprk_billingevent", line, "EB-OLD", SignalWriter.SignalStatusResolved);
        var elsewhere = w.AddSignal("sprk_billingevent", Guid.NewGuid(), "EB-RATE");
        return (w, line, onInvoice, onLine, acknowledged, resolved, elsewhere);
    }

    private static Task<BlockedDecisionResult> Decide(World w, string verb) => verb == "override" ? w.OverrideAsync() : w.ReturnAsync();

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_ClosesEveryOpenSignalOnTheInvoiceAndItsLines_AsActed_ByItsRecord_NamingTheDecider(string verb)
    {
        var (w, _, onInvoice, onLine, acknowledged, resolved, elsewhere) = await SignalWorldAsync();

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Closes.Select(c => c.SignalId).Should().BeEquivalentTo([onInvoice, onLine, acknowledged], "D-122: Open and Acknowledged, invoice and lines, every policy");
        w.Closes.Should().OnlyContain(c => c.RecordId == result.RecordId && c.DecidedBy == World.Caller && c.SetReadAtUtc == FirstReadAt,
            "closed by this decision's record, naming the person (from the on-behalf-of identity), with the set's read time");
        w.Closes.Single(c => c.SignalId == onInvoice).PolicyCode.Should().Be("EB-AGE", "each Signal's own policy code, from its dedupe key");
        w.Closes.Single(c => c.SignalId == acknowledged).PolicyCode.Should().Be("EB-TASK");
        w.ClosedBy.Keys.Should().NotContain([resolved, elsewhere], "a resolved Signal and another invoice's are not in the set");
        w.Order.Should().Equal(verb == "override"
            ? ["lock", "store", "record", "close", "close", "close", "update", "notify", "unlock"]
            : ["lock", "store", "record", "close", "close", "close", "transition", "update", "notify", "unlock"],
            "the set is stored, then recorded, then closed, then the state changes");
    }

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_ListsTheResolvedSignals_InItsRecord_AndStoresTheSetBeforeIt(string verb)
    {
        var (w, line, onInvoice, onLine, acknowledged, _, _) = await SignalWorldAsync();

        var result = await Decide(w, verb);

        var decision = w.Recorded.Should().ContainSingle().Which;
        decision.ResolvedSignalIds.Should().BeEquivalentTo([onInvoice, onLine, acknowledged]);
        decision.Facts[DecisionRecordWriter.BillingResolvedSetFact].Should().NotBeNull("the read time and the policy codes ride in the facts");
        var record = w.Records.Created.Should().ContainSingle(e => e.LogicalName == "sprk_decisionrecord").Which;
        record.GetAttributeValue<string>("sprk_steps").Should().Contain(onInvoice.ToString("D")).And.Contain(onLine.ToString("D")).And.Contain(acknowledged.ToString("D"));

        var stored = JsonNode.Parse(w.FindingsJson)![SubmissionOverrideService.HumanDecisionSetMember]!.AsObject();
        stored["decisionId"]!.GetValue<string>().Should().Be((verb == "override"
            ? SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId)
            : SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId)).ToString("D"));
        stored["signals"]!.AsArray().Select(s => Guid.Parse(s!["id"]!.GetValue<string>())).Should().BeEquivalentTo([onInvoice, onLine, acknowledged]);
        stored["subjects"]!.AsArray().Select(s => s!["entity"]!.GetValue<string>()).Should().BeEquivalentTo(["sprk_invoice", "sprk_billingevent"]);
        w.SetStores.Should().ContainSingle("stored once, before the record");
        JsonNode.Parse(w.FindingsJson)!["resolvedSignalSet"].Should().BeNull("049's member is left alone");
        result.RecordId.Should().NotBeNull();
    }

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_AsksTheCallersRights_AsTheCaller_AppendToOnTheMatter_AndWriteOnEachSubject(string verb)
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Decided, "a reviewer with the real roles' rights (Read only on the lines) may decide");
        w.Probe.RightsAsked.Should().BeEquivalentTo(
            [(World.Token, "sprk_matters", World.Matter), (World.Token, "sprk_invoices", World.InvoiceId)],
            "the caller's own token, the core once, each distinct subject once; a line subject is asked on its parent invoice (ontology-r1 2026-10-20, D-90)");
    }

    [Theory(DisplayName = "105 F1-2: a reviewer with exactly the real roles' rights (Read only on sprk_billingevent, Write on the invoice) decides with an open LINE Signal, and it is closed")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WithAnOpenLineSignal_AndTheRealRolesRights_Decides(string verb)
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var onLine = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        w.Probe.Rights = (set, _) => set switch
        {
            "sprk_billingevents" => AccessRights.Read,
            "sprk_invoices" => AccessRights.Read | AccessRights.Write,
            "sprk_matters" => AccessRights.Read | AccessRights.AppendTo,
            _ => AccessRights.None,
        };

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.ClosedBy.Should().ContainKey(onLine);
        w.Probe.RightsAsked.Should().NotContain(r => r.Set == "sprk_billingevents", "Write on a line is never asked");
    }

    [Theory(DisplayName = "105 F1-2: with an open LINE Signal, a caller without Write on the parent invoice is 403 and nothing is written")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WithAnOpenLineSignal_WithoutWriteOnTheInvoice_Is403_AndNothingIsWritten(string verb)
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        w.AddSignal("sprk_billingevent", line, "EB-RATE");
        w.Probe.Rights = (set, _) => set == "sprk_matters" ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read;

        var result = await Decide(w, verb);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        w.AssertNothingWritten();
    }

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WithoutAppendToOnTheMatter_Is403_AndNothingIsWritten(string verb)
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.Probe.Rights = (set, _) => set == "sprk_matters" ? AccessRights.Read | AccessRights.Write : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await Decide(w, verb);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        w.AssertNothingWritten();
        w.Order.Should().Equal("lock", "unlock");
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Theory]
    [InlineData("override", "sprk_invoices")]
    [InlineData("return", "sprk_invoices")]
    public async Task Decision_WithoutWriteOnASignalsSubject_Is403_AndNothingIsWritten(string verb, string deniedSet)
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.Probe.Rights = (set, _) => set == deniedSet ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await Decide(w, verb);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        w.AssertNothingWritten();
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Fact]
    public async Task Decision_WhenTheRightsCannotBeAnswered_IsRefused_AndNothingIsWritten()
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.Probe.Rights = (_, _) => throw new HttpRequestException("dataverse down");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403, "a rights question that cannot be answered is a no");
        w.AssertNothingWritten();
    }

    [Fact]
    public async Task Decision_WithNoOpenSignal_AsksNoRecordRights_AndClosesNothing()
    {
        var w = await World.CreateAsync();
        w.AddLine();

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Probe.RightsAsked.Should().BeEmpty("nothing to close, nothing beyond the route's own authorization");
        w.Closes.Should().BeEmpty();
    }

    [Fact]
    public async Task Decision_WhenTheOpenSignalsCannotBeRead_Is503_AndNothingIsWritten()
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.SignalReadFault = new TimeoutException("dataverse");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        w.AssertNothingWritten();
    }

    /// <summary>The Decision Record every close of the decision carried (a failed decision answers no record id, so the closes name it).</summary>
    private static Guid RecordIdOf(World w) => w.Closes.Select(c => c.RecordId).Distinct().Single();

    /// <summary>F2: a failed close is neither Decided nor AlreadyDecided: the retryable 503 <see cref="BlockedSubmissionErrors.Unavailable"/>.</summary>
    private static void ShouldBeUnavailable(BlockedDecisionResult result)
    {
        result.Kind.Should().Be(BlockedDecisionKind.Refused, "a failed close is never reported as Decided or AlreadyDecided");
        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.Unavailable);
        result.Outcome.Should().BeNull();
    }

    [Theory(DisplayName = "105 F2: a close that fails (timeout) answers 503 unavailable, not Decided: the decision stands (record, state, notification), the other Signals are closed, the failure is warned")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WhenAClosesFails_IsUnavailable_NotDecided_TheDecisionStands_AndTheOtherSignalsAreClosed(string verb)
    {
        var (w, _, onInvoice, onLine, acknowledged, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;

        var result = await Decide(w, verb);

        ShouldBeUnavailable(result);
        w.RecordCreates.Should().Be(1, "the Decision Record was written before the close");
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned, "the decision stands");
        w.Notified.Should().ContainSingle("the decision committed, so it is notified once");
        w.ClosedBy.Keys.Should().BeEquivalentTo([onLine, acknowledged]);
        w.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(onInvoice.ToString("D"), StringComparison.Ordinal));
        w.Order.Should().Equal(verb == "override"
            ? ["lock", "store", "record", "close", "close", "close", "update", "notify", "unlock"]
            : ["lock", "store", "record", "close", "close", "close", "transition", "update", "notify", "unlock"],
            "the order is unchanged: record, close, state change; only the answer differs");
    }

    [Theory(DisplayName = "105 F2: a Signal that kept changing (ChangedSinceRead) is a failed close too: 503 unavailable")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WhenASignalKeepsChanging_IsUnavailable(string verb)
    {
        var (w, _, onInvoice, onLine, acknowledged, _, _) = await SignalWorldAsync();
        w.CloseOutcome = id => id == onInvoice ? SignalAutomatedCloseOutcome.ChangedSinceRead : null;

        var result = await Decide(w, verb);

        ShouldBeUnavailable(result);
        w.RecordCreates.Should().Be(1);
        w.ClosedBy.Keys.Should().BeEquivalentTo([onLine, acknowledged]);
    }

    [Fact(DisplayName = "105 F2: a Signal resolved otherwise (LeftAsIs) is not a failed close: the decision is Decided")]
    public async Task Decision_WhenASignalWasResolvedOtherwise_LeftAsIs_IsStillDecided()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseOutcome = id => id == onInvoice ? SignalAutomatedCloseOutcome.LeftAsIs : null;

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Theory(DisplayName = "105 F2: the repeat of a decision whose close failed completes the close with the writer-created record and its decider, answers AlreadyDecided, and writes NO second Decision Record")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_ARepeatAfterAFailedClose_ReclosesTheStoredSet_WithTheRecordAndDecider_AndNothingNew(string verb)
    {
        var (w, _, onInvoice, onLine, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await Decide(w, verb));
        var recordId = RecordIdOf(w);
        w.CloseFault = _ => null;
        var raisedLater = w.AddSignal("sprk_invoice", World.InvoiceId, "EB-LATE");
        var closedBefore = w.ClosedBy.Count;

        var repeat = await Decide(w, verb);

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided, "AlreadyDecided only once every close is done");
        w.ClosedBy[onInvoice].Should().Be(recordId, "the repeat finished the close with the writer-created record");
        w.Closes.Last(c => c.SignalId == onInvoice).DecidedBy.Should().Be(World.Caller);
        w.Closes.Last(c => c.SignalId == onInvoice).SetReadAtUtc.Should().Be(FirstReadAt, "the stored read time, not now");
        w.ClosedBy.Should().HaveCount(closedBefore + 1, "only the Signal the first attempt failed to close; the others were AlreadyClosedByThisRecord");
        w.ClosedBy.Should().NotContainKey(raisedLater, "a Signal raised after the set was read stays open");
        w.ClosedBy[onLine].Should().Be(recordId);
        w.RecordCreates.Should().Be(1, "exactly one Decision Record");
        w.Notified.Should().ContainSingle("the repeat does not notify again");

        var third = await Decide(w, verb);
        third.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy.Should().HaveCount(closedBefore + 1);
        w.RecordCreates.Should().Be(1);
    }

    [Theory(DisplayName = "105 F2: a repeat whose close still fails (timeout, or a Signal that keeps changing) is 503 unavailable, never AlreadyDecided; a later repeat completes it with one Decision Record")]
    [InlineData("override", false)]
    [InlineData("override", true)]
    [InlineData("return", false)]
    [InlineData("return", true)]
    public async Task Repeat_WhoseCloseStillFails_IsUnavailable_AndALaterRepeatCompletesIt(string verb, bool changedSinceRead)
    {
        var (w, _, onInvoice, onLine, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await Decide(w, verb));
        var recordId = RecordIdOf(w);
        if (changedSinceRead)
        {
            w.CloseFault = _ => null;
            w.CloseOutcome = id => id == onInvoice ? SignalAutomatedCloseOutcome.ChangedSinceRead : null;
        }

        var stillFailing = await Decide(w, verb);

        ShouldBeUnavailable(stillFailing);
        w.ClosedBy.Should().NotContainKey(onInvoice);
        w.Closes.Count(c => c.SignalId == onInvoice).Should().Be(2, "the repeat attempted the close again");

        w.CloseFault = _ => null;
        w.CloseOutcome = _ => null;
        var done = await Decide(w, verb);

        done.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy[onInvoice].Should().Be(recordId);
        w.ClosedBy[onLine].Should().Be(recordId);
        w.RecordCreates.Should().Be(1, "exactly one Decision Record across all attempts");
    }

    [Fact(DisplayName = "105 F2: a repeat that finds the decision finished only UNDER the lock (a concurrent run) also answers 503 unavailable while its close fails, and AlreadyDecided once it completes")]
    public async Task Repeat_FoundDecidedUnderTheLock_WithAFailingClose_IsUnavailable_ThenAlreadyDecided()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        var recordId = RecordIdOf(w);

        // The first read (before the lock) sees the submission still held; the lock is taken, and then the decided state is what the re-read sees.
        w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked);
        w.OnLock = () => w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);
        var underTheLock = await w.OverrideAsync();

        ShouldBeUnavailable(underTheLock);
        w.Order.Count(o => o == "lock").Should().Be(2, "the second call took the lock: its reclose is the under-the-lock site, not the early one");
        w.Closes.Count(c => c.SignalId == onInvoice).Should().Be(2, "the reclose under the lock attempted the close again");

        w.CloseFault = _ => null;
        w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked);
        var done = await w.OverrideAsync();

        done.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy[onInvoice].Should().Be(recordId);
        w.RecordCreates.Should().Be(1);
    }

    // ── F2 follow-up: a transient read fault on a repeat is never an "already decided" answer ──

    /// <summary>Makes the repeat's reads fault, as each read can: the writer-created record's lookup (the writer's own refusal, and a raw
    /// fault) and the stored Signals and the invoice (read by the repeat's checks).</summary>
    private static void InjectReadFault(World w, string kind)
    {
        switch (kind)
        {
            case "lookup-refused":
                w.FindFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.BillingRecordLookupFailed, "the lookup faulted", innerException: new TimeoutException("dataverse"));
                break;
            case "lookup-timeout":
                w.FindFault = new TimeoutException("dataverse");
                break;
            case "signals":
                w.SignalReadFault = new TimeoutException("dataverse");
                break;
            case "invoice":
                w.InvoiceReadFault = new TimeoutException("dataverse");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void HealReadFaults(World w)
    {
        w.FindFault = null;
        w.SignalReadFault = null;
        w.InvoiceReadFault = null;
    }

    [Theory(DisplayName = "105 F2: a read fault on a repeat (the record lookup, the stored Signals, the invoice) is 503 unavailable, never AlreadyDecided; a later repeat with the reads healthy completes the closes, answers AlreadyDecided and leaves one Decision Record")]
    [InlineData("override", "lookup-refused")]
    [InlineData("override", "lookup-timeout")]
    [InlineData("override", "signals")]
    [InlineData("override", "invoice")]
    [InlineData("return", "lookup-refused")]
    [InlineData("return", "lookup-timeout")]
    [InlineData("return", "signals")]
    [InlineData("return", "invoice")]
    public async Task Repeat_WithAReadFault_IsUnavailable_AndALaterRepeatCompletesIt(string verb, string fault)
    {
        var (w, _, onInvoice, onLine, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await Decide(w, verb));
        var recordId = RecordIdOf(w);
        w.CloseFault = _ => null;
        InjectReadFault(w, fault);
        var closes = w.Closes.Count;

        var faulted = await Decide(w, verb);

        ShouldBeUnavailable(faulted);
        w.Closes.Should().HaveCount(closes, "nothing could be read, so no close was attempted and the Signal is still Open");
        w.ClosedBy.Should().NotContainKey(onInvoice);

        HealReadFaults(w);
        var done = await Decide(w, verb);

        done.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy[onInvoice].Should().Be(recordId, "the healthy repeat completed the close with the writer-created record");
        w.ClosedBy[onLine].Should().Be(recordId);
        w.RecordCreates.Should().Be(1, "exactly one Decision Record across all attempts");
    }

    [Theory(DisplayName = "105 F2: the same read fault found only UNDER the lock (the second reclose site) is 503 unavailable, and a later repeat completes it with one Decision Record")]
    // No "invoice" row: under the lock the decision reads the invoice itself before it classifies the submission, so that fault is
    // 503 before the reclose is reached and would not exercise it.
    [InlineData("override", "lookup-refused")]
    [InlineData("override", "lookup-timeout")]
    [InlineData("override", "signals")]
    [InlineData("return", "lookup-refused")]
    [InlineData("return", "lookup-timeout")]
    [InlineData("return", "signals")]
    public async Task Repeat_WithAReadFault_UnderTheLock_IsUnavailable_AndALaterRepeatCompletesIt(string verb, string fault)
    {
        var (w, _, onInvoice, onLine, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await Decide(w, verb));
        var recordId = RecordIdOf(w);
        w.CloseFault = _ => null;
        var (decidedStatus, decidedCode) = (w.Status, w.Code);

        // The first read (before the lock) sees the submission still held; the decided state is what the re-read under the lock sees.
        w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked);
        w.OnLock = () => w.SetSubmission(decidedStatus, decidedCode);
        InjectReadFault(w, fault);
        var locksBefore = w.Order.Count(o => o == "lock");
        var closes = w.Closes.Count;

        var faulted = await Decide(w, verb);

        ShouldBeUnavailable(faulted);
        w.Order.Count(o => o == "lock").Should().Be(locksBefore + 1, "the reclose that faulted is the one under the lock");
        w.Closes.Should().HaveCount(closes);

        HealReadFaults(w);
        w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked);
        var done = await Decide(w, verb);

        done.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy[onInvoice].Should().Be(recordId);
        w.ClosedBy[onLine].Should().Be(recordId);
        w.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "105 F2: the permanent cases stay AlreadyDecided with a warning: the writer refusing to read the stored set (ReviewInvalid) re-closes nothing")]
    public async Task Repeat_WhenTheWriterRefusesTheStoredSet_StaysAlreadyDecided_AndWarns()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        w.CloseFault = _ => null;
        w.FindFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.ReviewInvalid, "more than one record matches the key");
        var closes = w.Closes.Count;

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided, "a repeat does not change a refusal; it is a known limit, not a transient fault");
        w.Closes.Should().HaveCount(closes);
        w.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("nothing is re-closed", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ontology-r1 ask 2: a repeat the writer refuses to read logs the refusal warning exactly once")]
    public async Task Repeat_WhenTheWriterRefusesTheStoredSet_LogsTheRefusalWarningOnce()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        w.CloseFault = _ => null;
        w.FindFault = new DecisionRecordRefusedException(DecisionRecordRefusalReason.ReviewInvalid, "more than one record matches the key");
        w.Log.Entries.Clear();

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.Log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("decision_review_invalid", StringComparison.Ordinal))
            .Should().Be(1, "the refusal reason is logged once, not by the filter and again by the handler");
    }

    [Theory(DisplayName = "105 F2: a clean close is unchanged: Decided, then a repeat is AlreadyDecided with every Signal closed once and no second record")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task CleanClose_IsUnchanged_DecidedThenAlreadyDecided(string verb)
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();

        var first = await Decide(w, verb);
        var closed = w.ClosedBy.Count;
        var repeat = await Decide(w, verb);

        first.Kind.Should().Be(BlockedDecisionKind.Decided);
        first.StatusCode.Should().Be(200);
        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.ClosedBy.Should().HaveCount(closed, "AlreadyClosedByThisRecord: nothing closed twice");
        w.RecordCreates.Should().Be(1);
        w.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "105 F2: losing the state write to a concurrent run that finished the decision, with this run's close failed, is 503 unavailable, not AlreadyDecided")]
    public async Task Decision_LosingTheStateWriteToAConcurrentRun_WithAFailedClose_IsUnavailable()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        w.OnUpdate = () =>
        {
            // A concurrent run finished this same decision between our read and our write.
            w.OnUpdate = null;
            w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1;
        };

        var result = await w.OverrideAsync();

        ShouldBeUnavailable(result);
    }

    [Fact]
    public async Task Decision_ARetryAfterTheStateWriteFailed_ClosesExactlyTheStoredSet_AndFindsItsRecord()
    {
        var (w, _, onInvoice, onLine, acknowledged, _, _) = await SignalWorldAsync();
        w.UpdateFault = new TimeoutException("dataverse");
        var failed = await w.OverrideAsync();
        failed.StatusCode.Should().Be(503);
        w.UpdateFault = null;
        var raisedLater = w.AddSignal("sprk_billingevent", w.LineIds[0], "EB-LATE");
        w.Clock.Advance(TimeSpan.FromMinutes(5));
        var signalReads = w.SignalQueries.Count(q => q == "sprk_signal");

        var retried = await w.OverrideAsync();

        retried.Kind.Should().Be(BlockedDecisionKind.Decided, "the stored set rebuilds the same decision, so the record answers it (not review_invalid)");
        w.RecordCreates.Should().Be(1);
        w.SignalQueries.Count(q => q == "sprk_signal").Should().Be(signalReads, "a stored set is never re-read");
        w.Recorded.Last().ResolvedSignalIds.Should().BeEquivalentTo([onInvoice, onLine, acknowledged]);
        w.ClosedBy.Should().NotContainKey(raisedLater, "never widened");
        w.Closes.Should().OnlyContain(c => c.SetReadAtUtc == FirstReadAt, "the stored read time, not the retry's clock");
        w.SetStores.Should().ContainSingle("the retry reuses the stored set; it does not store another");
    }

    [Fact]
    public async Task Decision_LosingTheRaceAtTheSetStore_ToTheSameDecision_IsAnAlreadyDecidedNoOp()
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.OnStore = () =>
        {
            w.OnStore = null;
            w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1;
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.RecordCreates.Should().Be(0, "this call recorded nothing");
        w.Closes.Should().BeEmpty();
    }

    [Fact]
    public async Task Decision_WhenTheInvoiceNamesNoCoreRecord_AndThereAreSignals_IsRefused_AndNothingIsWritten()
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.Invoice!.Attributes.Remove("sprk_matter");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.RecordRefused);
        w.AssertNothingWritten();
    }

    [Theory(DisplayName = "#2455: an invoice naming two core records (never the first found) is refused with record_refused, and nothing is written")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_WhenTheInvoiceNamesTwoCoreRecords_AndThereAreSignals_IsRefused_AndNothingIsWritten(string verb)
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.Invoice!["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());

        var result = await Decide(w, verb);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.RecordRefused);
        w.AssertNothingWritten();
    }

    [Fact(DisplayName = "#2455: control: an invoice naming exactly one core record is decided and its Signals closed as before")]
    public async Task Decision_WhenTheInvoiceNamesOneCoreRecord_AndThereAreSignals_IsDecided()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.ClosedBy.Should().ContainKey(onInvoice);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // F1-1 (ontology-r1 review): sprk_findingsjson is user-writable, so nothing read back from it is trusted
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Plants a <c>humanDecisionSignalSet</c> as a user with Write on the submission could: any ids, any read time, no subjects.</summary>
    private static void PlantSet(World w, Guid decisionId, DateTime readAtUtc, params Guid[] signalIds) =>
        PlantSetClaiming(w, decisionId, readAtUtc, "EB-RATE", signalIds);

    /// <summary><see cref="PlantSet"/>, every Signal claimed under <paramref name="policyCode"/>.</summary>
    private static void PlantSetClaiming(World w, Guid decisionId, DateTime readAtUtc, string policyCode, params Guid[] signalIds)
    {
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root[SubmissionOverrideService.HumanDecisionSetMember] = new JsonObject
        {
            ["decisionId"] = decisionId.ToString("D"),
            ["setReadAtUtc"] = readAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["signals"] = new JsonArray(signalIds.Select(id => (JsonNode)new JsonObject { ["id"] = id.ToString("D"), ["policyCode"] = policyCode }).ToArray()),
            ["subjects"] = new JsonArray(),
        };
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
    }

    [Theory(DisplayName = "105 F1-1 (c): a planted set naming a Signal of ANOTHER invoice (or its line) is refused 409 findings_unreadable; nothing is recorded or closed")]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_invoice")]
    public async Task Return_WithAPlantedSetNamingAForeignSignal_IsRefused_AndClosesNothing(string foreignTable)
    {
        var w = await World.CreateAsync();
        var foreign = w.AddSignal(foreignTable, Guid.NewGuid(), "EB-RATE");
        PlantSet(w, SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId), World.CreatedOn.AddMinutes(5), foreign);

        var result = await w.ReturnAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsUnreadable);
        w.ClosedBy.Should().NotContainKey(foreign);
        w.Closes.Should().BeEmpty();
        w.RecordCreates.Should().Be(0);
        w.Updates.Should().BeEmpty();
        w.Transitions.Should().BeEmpty();
        w.SetStores.Should().BeEmpty("a refused set is not stored again");
        w.Probe.RightsAsked.Should().BeEmpty("refused before any rights are asked");
    }

    [Fact(DisplayName = "105 F1-1: a planted set naming the invoice's OWN Signal is trusted (subjects from the row), its rights asked, and closed")]
    public async Task Override_WithAStoredSetOfTheInvoicesOwnSignal_UsesTheRowsSubjects()
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var own = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        PlantSet(w, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), World.CreatedOn.AddMinutes(5), own);

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.ClosedBy.Should().ContainKey(own);
        w.Probe.RightsAsked.Should().Contain((World.Token, "sprk_invoices", World.InvoiceId), "the line's subject, from its ROW, asked on its parent invoice");
    }

    [Theory(DisplayName = "105 F1-1 (3): a stored read time before the submission was created (beyond the skew), or in the future, is refused 409 findings_unreadable")]
    [InlineData(-2)]
    [InlineData(+10)]
    public async Task Override_WithAStoredReadTimeOutOfBounds_IsRefused(int minutes)
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var own = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        var readAt = minutes < 0 ? World.CreatedOn.AddMinutes(minutes) : w.Clock.GetUtcNow().UtcDateTime.AddMinutes(minutes);
        PlantSet(w, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), readAt, own);

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsUnreadable);
        w.Closes.Should().BeEmpty();
        w.RecordCreates.Should().Be(0);
    }

    [Fact(DisplayName = "105 F1-1 (2): a repeat takes the record id and the decider from the WRITER-CREATED record, never from an edited stamp")]
    public async Task Repeat_WithAnEditedStamp_ClosesWithTheWriterCreatedRecordAndItsDecider()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        var recordId = RecordIdOf(w);
        w.CloseFault = _ => null;
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root[SubmissionOverrideService.OverrideMember]!["recordId"] = Guid.NewGuid().ToString("D");
        root[SubmissionOverrideService.OverrideMember]!["bySystemUserId"] = Guid.NewGuid().ToString("D");
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        var reclose = w.Closes.Last(c => c.SignalId == onInvoice);
        reclose.RecordId.Should().Be(recordId, "the writer-created record, not the stamp");
        reclose.DecidedBy.Should().Be(World.Caller, "the record's sprk_confirmedby, not the stamp");
    }

    [Fact(DisplayName = "105 F1-1 (2): a decided submission with a planted stamp and set but NO writer-created record re-closes nothing")]
    public async Task Repeat_WithNoWriterCreatedRecord_ClosesNothing()
    {
        var w = await World.CreateAsync();
        var other = w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
        PlantSet(w, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), World.CreatedOn.AddMinutes(5), other);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = SubmissionOverrideService.WithStamp(
            w.FindingsJson, SubmissionOverrideService.OverrideMember, new BlockedDecisionStamp(Guid.NewGuid(), Guid.NewGuid(), w.Clock.GetUtcNow(), Reason));
        w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.Closes.Should().BeEmpty("with no writer-created record the stamp is not trusted");
    }

    [Fact(DisplayName = "105 F3 (a): a repeat whose caller lacks the rights closes nothing")]
    public async Task Repeat_WhoseCallerLacksTheRights_ClosesNothing()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        w.CloseFault = _ => null;
        var closes = w.Closes.Count;
        w.Probe.Rights = (set, _) => set == "sprk_invoices" ? AccessRights.Read : ScriptedProbe.RealReviewerRights(set, Guid.Empty);

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided, "no close was attempted, so none failed: the repeat's answer is AlreadyDecided as before");
        w.Closes.Should().HaveCount(closes, "no close is attempted for a caller without Write on the invoice");
        w.ClosedBy.Should().NotContainKey(onInvoice);
    }

    [Fact(DisplayName = "105 F3 (b): losing the set-store race to a RETURN is 409 concurrent_change, never AlreadyDecided")]
    public async Task Override_LosingTheSetStoreRace_ToAReturn_Is409()
    {
        var (w, _, _, _, _, _, _) = await SignalWorldAsync();
        w.OnStore = () =>
        {
            w.OnStore = null;
            w.SetSubmission(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Returned);
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1;
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.ConcurrentChange);
        w.RecordCreates.Should().Be(0);
        w.Closes.Should().BeEmpty();
    }

    [Fact(DisplayName = "105 K1: a retry whose record exists uses the record's stored set, even when the findings hold none (a decision in flight across a deploy)")]
    public async Task Retry_WhenTheRecordExists_UsesTheRecordsSet_NotAFreshRead()
    {
        var (w, _, onInvoice, onLine, acknowledged, _, _) = await SignalWorldAsync();
        w.UpdateFault = new TimeoutException("dataverse");
        (await w.OverrideAsync()).StatusCode.Should().Be(503);
        w.UpdateFault = null;
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root.Remove(SubmissionOverrideService.HumanDecisionSetMember);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        var raisedLater = w.AddSignal("sprk_invoice", World.InvoiceId, "EB-LATE");

        var retried = await w.OverrideAsync();

        retried.Kind.Should().Be(BlockedDecisionKind.Decided, "the record's set rebuilds the same decision (never DecisionConflict)");
        w.RecordCreates.Should().Be(1);
        w.Recorded.Last().ResolvedSignalIds.Should().BeEquivalentTo([onInvoice, onLine, acknowledged]);
        w.ClosedBy.Should().NotContainKey(raisedLater);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // ontology-r1 round 2: F1-a (a planted sprk_invoice), F1-b (a planted policy code), K1 (createdon skew), K3 (no decider)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid PlantedInvoice = Guid.Parse("10500000-0000-4000-8000-0000000000f1");

    /// <summary>
    /// F1-a: what a Core User with Write on the submission could do: point its user-writable <c>sprk_invoice</c> at ANOTHER invoice B (In
    /// Review, filed under the same matter, readable and movable) and make the findings name B too. B is not the invoice the persister
    /// created for the submission (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>).
    /// </summary>
    private static void PointAtPlantedInvoice(World w)
    {
        var invoiceB = new Entity("sprk_invoice", PlantedInvoice)
        {
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(InvoiceLifecycleStatus.InReview)),
            ["sprk_matter"] = new EntityReference("sprk_matter", World.Matter),
        };
        w.Entities.Setup(e => e.RetrieveAsync("sprk_invoice", PlantedInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, string[] columns, CancellationToken _) => Task.FromResult(World.Copy(invoiceB, columns)));
        w.Lifecycle.Setup(l => l.TransitionAsync(PlantedInvoice, It.IsAny<InvoiceLifecycleStatus>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, InvoiceLifecycleStatus target, CancellationToken _) =>
            {
                w.Order.Add("transition");
                w.Transitions.Add(target);
                return Task.FromResult(InvoiceTransitionResult.Moved(InvoiceLifecycleStatus.InReview, target));
            });
        w.Records.Rows[("sprk_invoice", PlantedInvoice)] = new Entity("sprk_invoice", PlantedInvoice) { ["sprk_matter"] = new EntityReference("sprk_matter", World.Matter) };

        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root["invoiceId"] = PlantedInvoice.ToString("D");
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        w.Submission[InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", PlantedInvoice);
    }

    [Theory(DisplayName = "105 F1-a: a submission whose sprk_invoice was pointed at ANOTHER invoice (no open Signal there) is 409 invoice_not_held; nothing is recorded under it or moved")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Decision_OfASubmissionLinkingAnInvoiceItDidNotCreate_IsRefused_AndNothingIsWritten(string verb)
    {
        var w = await World.CreateAsync();
        PointAtPlantedInvoice(w);

        var result = await Decide(w, verb);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.InvoiceNotHeld);
        w.RecordCreates.Should().Be(0, "no Decision Record under the planted invoice");
        w.Recorded.Should().BeEmpty();
        w.Transitions.Should().BeEmpty("the planted invoice is not moved to Returned to Firm");
        w.Updates.Should().BeEmpty();
        w.SetStores.Should().BeEmpty();
        w.Order.Should().BeEmpty("refused before the lock, any read of the planted invoice or any write");
        IsHeld(w.Submission).Should().BeTrue();
    }

    [Fact(DisplayName = "105 F1-a: a repeat of a decided submission whose sprk_invoice was pointed at ANOTHER invoice re-closes nothing, even when a record under that invoice lists its Signal")]
    public async Task Repeat_OfASubmissionLinkingAnInvoiceItDidNotCreate_ClosesNothing()
    {
        var w = await World.CreateAsync();
        PointAtPlantedInvoice(w);
        var onB = w.AddSignal("sprk_invoice", PlantedInvoice, "EB-AGE");

        // A writer-created record of this submission's override under B, listing B's Signal (what the decide path wrote before F1-a).
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(w.FindingsJson)!;
        var set = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(FirstReadAt, [new InvoiceSubmissionAfterSteps.ResolvedSignal(onB, "EB-AGE")]);
        await w.Recorder.RecordAsync(SubmissionOverrideService.BuildDecision(
            SubmissionOverrideService.Verb.Override, stored, World.SubmissionId, PlantedInvoice, World.Caller, Reason, set), CancellationToken.None);
        w.RecordCreates.Should().Be(1, "the fixture: a writer-created record under B exists");
        w.SetSubmission(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden);

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided, "the repeat's answer is unchanged");
        w.Closes.Should().BeEmpty("only the invoice the submission created is ever re-closed");
        w.ClosedBy.Should().NotContainKey(onB);
        w.FindCalls.Should().Be(0, "the record is not even looked up under a planted invoice");
    }

    [Fact(DisplayName = "105 F1-b: a stored set naming the invoice's own Signal under ANOTHER policy code (claimed EB-AGE, the row EB-RATE) is refused 409 findings_unreadable; nothing is recorded or closed")]
    public async Task Override_WithAStoredSetClaimingAnotherPolicyCode_IsRefused()
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var own = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        PlantSetClaiming(w, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), World.CreatedOn.AddMinutes(5), "EB-AGE", own);

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsUnreadable);
        w.Closes.Should().BeEmpty();
        w.Recorded.Should().BeEmpty("the claimed code never reaches the record's resolvedSignalSet fact");
        w.RecordCreates.Should().Be(0);
        w.Probe.RightsAsked.Should().BeEmpty("refused before any rights are asked");
    }

    [Theory(DisplayName = "105 K1: a stored read time up to the skew BEFORE createdon (Dataverse's clock) is trusted; past the skew it is refused")]
    [InlineData(-30, true)]
    [InlineData(-60, true)]
    [InlineData(-61, false)]
    public async Task Override_WithAStoredReadTimeJustBeforeCreatedOn_IsTrustedWithinTheSkew(int seconds, bool trusted)
    {
        var w = await World.CreateAsync();
        var line = w.AddLine();
        var own = w.AddSignal("sprk_billingevent", line, "EB-RATE");
        PlantSet(w, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId), World.CreatedOn.AddSeconds(seconds), own);

        var result = await w.OverrideAsync();

        if (trusted)
        {
            result.Kind.Should().Be(BlockedDecisionKind.Decided, "the BFF's clock may run behind Dataverse's by up to the skew");
            w.ClosedBy.Should().ContainKey(own);
        }
        else
        {
            result.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsUnreadable);
            w.Closes.Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "105 K3: a repeat whose writer-created record names no decider (sprk_confirmedby) re-closes nothing and warns")]
    public async Task Repeat_WhoseRecordNamesNoDecider_ClosesNothing_AndWarns()
    {
        var (w, _, onInvoice, _, _, _, _) = await SignalWorldAsync();
        w.CloseFault = id => id == onInvoice ? new TimeoutException("dataverse") : null;
        ShouldBeUnavailable(await w.OverrideAsync());
        var recordId = RecordIdOf(w);
        w.CloseFault = _ => null;
        w.Records.Created.Single(e => e.LogicalName == "sprk_decisionrecord").Attributes.Remove("sprk_confirmedby");
        var closes = w.Closes.Count;

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.Closes.Should().HaveCount(closes, "no close in no one's name");
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("names no decider", StringComparison.Ordinal)
            && e.Message.Contains(recordId.ToString("D"), StringComparison.Ordinal));
    }

    private static string FindFromRepoRoot(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
