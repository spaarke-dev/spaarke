using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Jobs.Handlers.InvoiceSubmissionProcessingJobHandlerTests;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// ebilling-r1 task 049 part E, end to end: <see cref="InvoiceSubmissionProcessingJobHandler"/> over the REAL processor (part B harness: real
/// 072 evaluation and 073 decision), the REAL persister (part C) and the REAL after-steps (part D), all over ONE in-memory Dataverse double, so
/// what is asserted is what the pipeline actually wrote: one transaction, one invoice, the Signals, the Decision Record, the job, the
/// notification and the order of the submission's statuses. Only the leaves are doubles: Dataverse, the Signal writer's call, the queue, the cache.
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038): the all-or-nothing and idempotent-redelivery guarantees of FR-20 / NFR-08.</para>
/// <para><b>ResumeAsync.</b> <see cref="IInvoiceSubmissionAfterSteps.ResumeAsync"/> is implemented by a parallel change (D-122). Until it lands the
/// real class throws, so the redelivery tests here wrap the real after-steps in <see cref="ResumingAfterSteps"/>, a test-only stand-in that
/// resumes by running the real <c>RunAsync</c> from the stored state. The handler's own contract (a Processing submission with its link is
/// resumed through <c>ResumeAsync</c>; the processor and the persister are never called) is proved against the strict fake in
/// <see cref="InvoiceSubmissionProcessingJobHandlerTests"/>; the stand-in is replaced by the real method when it merges.</para>
/// </remarks>
public sealed class InvoiceSubmissionProcessingFlowTests
{
    private static readonly Guid Matter = Guid.Parse("04900000-0000-4000-8000-0000000000a1");
    private static readonly Guid TeamId = Guid.Parse("04900000-0000-4000-8000-0000000000e7");
    private static readonly Guid RecordTypeId = Guid.Parse("04900000-0000-4000-8000-0000000000e8");
    private static readonly Guid Submitter = Guid.Parse("04900000-0000-4000-8000-0000000000c9");

    private sealed class Crash : Exception
    {
        public Crash() : base("simulated process crash") { }
    }

    /// <summary>Test-only stand-in for the D-122 <c>ResumeAsync</c>: resumes from the stored link and findings, re-running the real RunAsync.</summary>
    private sealed class ResumingAfterSteps(IInvoiceSubmissionAfterSteps real, Flow flow) : IInvoiceSubmissionAfterSteps
    {
        public int ResumeCalls { get; private set; }

        public Task<SubmissionAfterStepsResult> RunAsync(SubmissionPersistResult persisted, string? correlationId, CancellationToken ct = default) =>
            real.RunAsync(persisted, correlationId, ct);

        public void PreflightRejection(SubmissionEvaluationOutcome outcome) => real.PreflightRejection(outcome);

        public Task<SubmissionAfterStepsResult> ResumeAsync(Guid submissionId, string? correlationId, CancellationToken ct = default)
        {
            ResumeCalls++;
            var kind = Enum.Parse<SubmissionEvaluationKind>(
                JsonNode.Parse(flow.Row.GetAttributeValue<string>("sprk_findingsjson"))!["kind"]!.GetValue<string>());
            var terminal = kind is SubmissionEvaluationKind.AutoRejected or SubmissionEvaluationKind.Blocked
                ? InvoiceSubmissionStatus.Rejected
                : InvoiceSubmissionStatus.Accepted;
            return real.RunAsync(new SubmissionPersistResult
            {
                Status = SubmissionPersistStatus.Resumed,
                SubmissionId = submissionId,
                InvoiceId = flow.Row.GetAttributeValue<EntityReference>("sprk_invoice").Id,
                TerminalStatus = terminal,
                TerminalReasonCode = flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode"),
            }, correlationId, ct);
        }
    }

    /// <summary>Wraps the processor to count and, for the concurrency test, hold the first evaluation open.</summary>
    private sealed class SpyProcessor(IInvoiceSubmissionProcessor inner, Flow flow) : IInvoiceSubmissionProcessor
    {
        public int Calls { get; private set; }

        public async Task<SubmissionEvaluationOutcome> EvaluateAsync(
            Entity submission, Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>> loadDocument, CancellationToken ct = default)
        {
            Calls++;
            flow.Entered.TrySetResult();
            if (flow.Gate is { } gate)
            {
                await gate.Task;
            }

            return flow.LastOutcome = await inner.EvaluateAsync(submission, loadDocument, ct);
        }
    }

    private sealed class Flow
    {
        public Entity Row { get; } = SubmissionRow((int)InvoiceSubmissionStatus.Received);
        public ProcessorHarness Harness { get; }
        public bool Enabled { get; set; } = true;
        public SubmissionEvaluationOutcome? LastOutcome { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<IReadOnlyList<OrganizationRequest>> Transactions { get; } = [];
        public List<Entity> Created { get; } = [];
        public List<Guid> TotalsCalls { get; } = [];
        public List<SignalWriteRequest> SignalWrites { get; } = [];
        public Dictionary<string, Entity> Signals { get; } = new(StringComparer.Ordinal);
        public List<JobContract> Jobs { get; } = [];
        public List<SubmissionDecidedNotification> Notified { get; } = [];
        public List<int> StatusHistory { get; } = [];
        public Locks Locks { get; } = new();

        public Exception? TransactionFault { get; set; }
        public int CrashAtSignalWrite { get; set; } = -1;
        public DecisionRecordWriterTests.Harness Records { get; }
        public BillingDecisionRecorder Recorder { get; }
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public int RecordCreates => Records.Created.Count(e => e.LogicalName == "sprk_decisionrecord");

        public Flow(ProcessorHarness? harness = null)
        {
            Harness = harness ?? new ProcessorHarness();
            Row["sprk_createdbyperson"] = new EntityReference("systemuser", Submitter);

            Records = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
            Records.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
            Recorder = new BillingDecisionRecorder(Records.Writer, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, NullLogger<BillingDecisionRecorder>.Instance);

            Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => entity == FinanceBaseValues.SubmissionEntity
                    ? Copy(Row)
                    : new Entity(entity, id) { [RegardingRecordType.GetPrimaryNameField(entity)!] = "Acme v. Roe", ["sprk_matternumber"] = "M-1" });
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
                {
                    "sprk_signal" => SignalRows(q),
                    "sprk_policy" => new EntityCollection(),
                    "sprk_billingevent" => new EntityCollection(Created.Where(c => c.LogicalName == "sprk_billingevent").ToList()),
                    _ => throw new InvalidOperationException($"unexpected query on {q.EntityName}"),
                });
            Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    Transactions.Add(requests);
                    if (TransactionFault is { } fault)
                    {
                        return Task.FromException(fault); // all or nothing: nothing below is applied
                    }

                    foreach (var request in requests)
                    {
                        switch (request)
                        {
                            case CreateRequest create:
                                Created.Add(create.Target);
                                break;
                            case UpdateRequest update when update.Target.LogicalName == FinanceBaseValues.SubmissionEntity:
                                Apply(update.Target.Attributes);
                                break;
                        }
                    }

                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.UpdateAsync(FinanceBaseValues.SubmissionEntity, SubmissionId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    if (fields.TryGetValue("sprk_processingstatus", out var status))
                    {
                        StatusHistory.Add(((OptionSetValue)status).Value);
                    }

                    Apply(fields);
                    return Task.CompletedTask;
                });
        }

        public Guid? LinkedInvoice => Row.GetAttributeValue<EntityReference>("sprk_invoice")?.Id;

        public int Status => Row.GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value;

        public IEnumerable<Entity> CreatedOf(string table) => Created.Where(c => c.LogicalName == table);

        public InvoiceSubmissionProcessingJobHandler BuildHandler(bool resumeStandIn = false)
        {
            var processor = new SpyProcessor(Harness.Build(), this);
            Spy = processor;
            var persister = BuildPersister();
            var after = BuildAfterSteps();
            ResumingAfterSteps? resuming = resumeStandIn ? new ResumingAfterSteps(after, this) : null;
            Resuming = resuming;

            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
            documents.Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity { Id = DocumentId.ToString(), Name = "invoice", GraphDriveId = "drive-1", GraphItemId = "item-1", FileName = "invoice.pdf" });
            var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
            spe.Setup(s => s.DownloadFileAsync("drive-1", "item-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([1, 2, 3]));

            return new InvoiceSubmissionProcessingJobHandler(
                Entities.Object,
                documents.Object,
                spe.Object,
                TestRecordContainerResolver.ForBusinessUnitContainers("drive-1"),
                processor,
                persister,
                (IInvoiceSubmissionAfterSteps?)resuming ?? after,
                BuildSink(),
                Locks,
                Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == new EbillingOptions { Enabled = Enabled }),
                new QuietLog());
        }

        public SpyProcessor? Spy { get; private set; }

        public ResumingAfterSteps? Resuming { get; private set; }

        private IEbillingNotificationSink? _sink;

        private IEbillingNotificationSink BuildSink()
        {
            if (_sink is not null)
            {
                return _sink;
            }

            var sink = new Mock<IEbillingNotificationSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmissionDecidedAsync(It.IsAny<SubmissionDecidedNotification>(), It.IsAny<CancellationToken>()))
                .Returns((SubmissionDecidedNotification n, CancellationToken _) =>
                {
                    Notified.Add(n);
                    return Task.CompletedTask;
                });
            return _sink = sink.Object;
        }

        private InvoiceSubmissionPersister BuildPersister()
        {
            var recordTypes = new Mock<ICommunicationDataverseService>();
            recordTypes.Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", RecordTypeId) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" });
            var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
            ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecordOwnerResolution.Owned(TeamId));
            var totals = BuildTotals();
            return new InvoiceSubmissionPersister(
                Entities.Object,
                recordTypes.Object,
                ownership.Object,
                new InvoiceLifecycleService(new Mock<IFieldMappingDataverseService>().Object, NullLogger<InvoiceLifecycleService>.Instance),
                totals.Object,
                NullLogger<InvoiceSubmissionPersister>.Instance);
        }

        // One totals double for the persister and the after-steps (the after-steps re-run the totals on resume, 13a A).
        private Mock<IInvoiceTotalsService> BuildTotals()
        {
            var totals = new Mock<IInvoiceTotalsService>(MockBehavior.Strict);
            totals.Setup(t => t.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken _) =>
                {
                    TotalsCalls.Add(id);
                    return Task.FromResult(new InvoiceTotalsCalculation(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), null));
                });
            return totals;
        }

        private InvoiceSubmissionAfterSteps BuildAfterSteps() => new(
            Entities.Object,
            Locks,
            BuildTotals().Object,
            BuildSink(),
            NullLogger<InvoiceSubmissionAfterSteps>.Instance,
            WriteSignalAsync,
            (_, _, _, _) => Task.FromResult(SignalCloseOutcome.Closed),
            (decision, ct) =>
            {
                // F4-1: the record's policy versions exist (the evaluation named them).
                foreach (var version in (LastOutcome?.Evaluation?.Findings ?? []).Select(f => f.PolicyVersionId).Distinct())
                {
                    Records.Rows[("sprk_policyversion", version)] = new Entity("sprk_policyversion", version);
                }

                return Recorder.RecordAsync(decision, ct);
            },
            Recorder.Preflight,
            (job, _) =>
            {
                Jobs.Add(job);
                return Task.CompletedTask;
            });

        private Task<SignalWriteResult> WriteSignalAsync(SignalWriteRequest request, CancellationToken _)
        {
            if (SignalWrites.Count == CrashAtSignalWrite)
            {
                CrashAtSignalWrite = -1;
                throw new Crash();
            }

            SignalWrites.Add(request);
            var key = SignalWriter.BuildDedupeKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D"), request.Episode);
            var evidence = JsonSerializer.Serialize(request.EvidenceRefs ?? [], new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (Signals.TryGetValue(key, out var existing))
            {
                existing["sprk_evidencerefs"] = evidence;
                existing.RowVersion = (long.Parse(existing.RowVersion!) + 1).ToString();
                return Task.FromResult(new SignalWriteResult(existing.Id, false, Guid.Empty, Guid.Empty));
            }

            var row = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_dedupekey"] = key,
                ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
                ["sprk_evidencerefs"] = evidence,
                RowVersion = "1",
            };
            Signals[key] = row;
            return Task.FromResult(new SignalWriteResult(row.Id, true, Guid.Empty, Guid.Empty));
        }

        private EntityCollection SignalRows(QueryExpression q)
        {
            var prefix = (string)q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_dedupekey" && c.Operator == ConditionOperator.BeginsWith).Values[0];
            return new EntityCollection(Signals.Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(s => Copy(s.Value)).ToList());
        }

        private void Apply(IEnumerable<KeyValuePair<string, object>> attributes)
        {
            foreach (var (column, value) in attributes)
            {
                Row[column] = value;
            }
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
    }

    private sealed class QuietLog : Microsoft.Extensions.Logging.ILogger<InvoiceSubmissionProcessingJobHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private static Entity Target(OrganizationRequest request) => request switch
    {
        CreateRequest c => c.Target,
        UpdateRequest u => u.Target,
        _ => throw new InvalidOperationException(request.GetType().Name),
    };

    private static ProcessorHarness Aged(int consequence)
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject() with { Consequence = consequence }] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        return harness;
    }

    /// <summary>One old line and a Reject rule: SPA is AutoRejected, Internal is Blocked. The channel is read from the row the handler re-reads.</summary>
    private static Flow AgedLineRejecting(InvoiceSubmissionChannel channel = InvoiceSubmissionChannel.Spa)
    {
        var flow = new Flow(Aged((int)BillingConsequence.Reject));
        flow.Row["sprk_channel"] = new OptionSetValue((int)channel);
        return flow;
    }

    // ── Accepted ──

    [Fact(DisplayName = "049 E criterion 1: Accepted - exactly one invoice In Review, its lines, one approval, the document link, totals, Signals, Accepted with sprk_invoice, indexing once, notify once")]
    public async Task Accepted_WritesEverythingOnce()
    {
        // A flag-only rule that fires: the invoice is Accepted WITH a finding, and the finding becomes a Signal.
        var flow = new Flow(Aged((int)BillingConsequence.Flag));

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        flow.LastOutcome!.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        flow.Transactions.Should().ContainSingle("FR-20: one transaction");
        flow.CreatedOf("sprk_invoice").Should().ContainSingle().Which.Should().Match<Entity>(i =>
            i.Id == InvoiceId && i.GetAttributeValue<OptionSetValue>("statuscode").Value == InvoiceStatusCodes.InReview);
        flow.CreatedOf("sprk_billingevent").Should().HaveCount(1);
        flow.CreatedOf("sprk_invoiceapproval").Should().ContainSingle();
        flow.Transactions[0].Select(Target).Should().Contain(t => t.LogicalName == "sprk_document" && t.GetAttributeValue<EntityReference>("sprk_invoice").Id == InvoiceId);
        flow.TotalsCalls.Should().Equal(InvoiceId);
        flow.SignalWrites.Should().NotBeEmpty("the finding is a Signal");
        flow.LinkedInvoice.Should().Be(InvoiceId);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Accepted);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().BeNull();
        flow.Jobs.Should().ContainSingle(j => j.JobType == "InvoiceIndexing").Which.SubjectId.Should().Be(InvoiceId.ToString());
        flow.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Accepted);
        flow.RecordCreates.Should().Be(0, "an Accepted submission has no Decision Record");
        flow.StatusHistory.Should().Equal([(int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Accepted], "Received -> Processing -> one terminal, in order");
    }

    [Fact(DisplayName = "049 E (I-5, reused from part B 'AiObservationAlone_NeverRejects'): no AI output alone rejects - a failing AiObservation with reliable reads is Accepted end to end")]
    public async Task NoAiOutputAloneRejects_EndToEnd()
    {
        var harness = new ProcessorHarness
        {
            Rules = [BillingEvaluationKit.Version("BILL-AI", RuleType.AiObservation) with { Consequence = (int)BillingConsequence.Reject }],
            StoredRejectBypassesTheGate = true,
        };
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];
        var flow = new Flow(harness);

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Accepted);
        flow.CreatedOf("sprk_invoice").Single().GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        flow.RecordCreates.Should().Be(0);
    }

    // ── Rejected (D-101) ──

    [Fact(DisplayName = "049 E criterion 2 (D-101): a Rejected SPA invoice is created Rejected; no approval; the submission is Rejected with the reason code and the findings JSON; one Decision Record")]
    public async Task RejectedSpa_InvoiceCreatedRejected_NoApproval()
    {
        var flow = AgedLineRejecting();

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        flow.LastOutcome!.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        flow.CreatedOf("sprk_invoice").Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Rejected);
        flow.CreatedOf("sprk_invoiceapproval").Should().BeEmpty("a rejected invoice has no approval");
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Rejected);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(flow.LastOutcome.ReasonCode).And.NotBeNullOrEmpty();
        using (var findings = JsonDocument.Parse(flow.Row.GetAttributeValue<string>("sprk_findingsjson")))
        {
            findings.RootElement.GetProperty("findings").GetArrayLength().Should().BeGreaterThan(0);
        }

        flow.RecordCreates.Should().Be(1, "the automatic rejection's Decision Record, filed under the invoice");
        flow.Jobs.Should().BeEmpty("a rejected invoice is not indexed");
        flow.Notified.Should().ContainSingle().Which.Should().Match<SubmissionDecidedNotification>(n =>
            n.Outcome == SubmissionDecisionOutcome.Rejected && n.InvoiceId == InvoiceId && n.ReasonCode == flow.LastOutcome.ReasonCode);
        flow.SignalWrites.Should().NotBeEmpty();
        flow.StatusHistory.Should().Equal([(int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Rejected]);
    }

    [Fact(DisplayName = "049 E (D-101): a blocked internal submission is created In Review with its approval and ends Rejected + ebilling.submission.blocked; no Decision Record")]
    public async Task BlockedInternal_InvoiceInReview_SubmissionRejectedBlocked()
    {
        var flow = AgedLineRejecting(InvoiceSubmissionChannel.Internal);

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        flow.LastOutcome!.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        flow.CreatedOf("sprk_invoice").Single().GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        flow.CreatedOf("sprk_invoiceapproval").Should().ContainSingle();
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Rejected);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(SubmissionReasonCodes.Blocked);
        flow.RecordCreates.Should().Be(0);
    }

    // ── reason-code mapping ──

    [Theory(DisplayName = "049 E criterion (reason codes): a document-level read failure is Rejected with that code, and creates no invoice")]
    [InlineData("invoice.read.unreadable")]
    [InlineData("invoice.read.page_limit")]
    [InlineData("invoice.read.no_invoice")]
    public async Task DocumentLevelReadFailure_IsRejected_NoInvoice(string code)
    {
        var flow = new Flow(new ProcessorHarness { ReadFailure = new InvoiceAiException(code, InvoiceAiException.ReadCapability, "read failed") });

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Rejected);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(code);
        flow.Transactions.Should().BeEmpty();
        flow.Created.Should().BeEmpty();
        flow.LinkedInvoice.Should().BeNull();
        flow.Notified.Should().ContainSingle().Which.InvoiceId.Should().BeNull();
        flow.StatusHistory.Should().Equal([(int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Rejected]);
    }

    [Fact(DisplayName = "049 E criterion (reason codes): invoice.read.service_failed retries, then the submission is Failed with that code; no invoice")]
    public async Task ServiceFailed_RetriesThenFailed_NoInvoice()
    {
        var flow = new Flow(new ProcessorHarness { ReadFailure = new InvoiceAiException(InvoiceAiException.ReadServiceFailed, InvoiceAiException.ReadCapability, "down") });

        var first = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);
        var second = await flow.BuildHandler().ProcessAsync(Job(attempt: 2), CancellationToken.None);
        var final = await flow.BuildHandler().ProcessAsync(FinalJob(), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        second.Status.Should().Be(JobStatus.Failed);
        final.Status.Should().Be(JobStatus.Poisoned);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Failed);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be("invoice.read.service_failed");
        flow.Created.Should().BeEmpty();
        flow.StatusHistory.Should().Equal([(int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Failed]);
    }

    [Fact(DisplayName = "049 E criterion (reason codes): Ebilling:Enabled false is Failed ebilling.disabled and Poisoned; nothing is read")]
    public async Task FlagOff_IsFailed_EbillingDisabled()
    {
        var flow = new Flow(new ProcessorHarness { Enabled = false }) { Enabled = false };

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Failed);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be("ebilling.disabled");
        flow.Spy!.Calls.Should().Be(0);
        flow.Harness.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "049 E criterion (reason codes): the Null AI facade is Failed ai.invoice.disabled and Poisoned")]
    public async Task NullAiFacade_IsFailed_AiInvoiceDisabled()
    {
        var flow = new Flow(new ProcessorHarness { Ai = new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance) });

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Failed);
        flow.Row.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be("ai.invoice.disabled");
        flow.Created.Should().BeEmpty();
    }

    // ── FR-20: all or nothing ──

    [Fact(DisplayName = "049 E criterion 3: a fault after the transaction is built commits nothing; the submission is not Accepted and no later step ran")]
    public async Task TransactionFault_CommitsNothing_NeverAccepted()
    {
        var flow = new Flow
        {
            TransactionFault = new DataverseTransactionException("line refused", failedRequestIndex: 1, outcomeUnknown: false, null),
        };

        var outcome = await flow.BuildHandler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "retry");
        flow.Transactions.Should().ContainSingle();
        flow.Created.Should().BeEmpty("nothing of the transaction was committed");
        flow.LinkedInvoice.Should().BeNull();
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Processing).And.NotBe((int)InvoiceSubmissionStatus.Accepted);
        flow.SignalWrites.Should().BeEmpty();
        flow.Jobs.Should().BeEmpty();
        flow.Notified.Should().BeEmpty();
        flow.TotalsCalls.Should().BeEmpty();
        flow.RecordCreates.Should().Be(0);
    }

    // ── NFR-08: redelivery ──

    [Fact(DisplayName = "049 E criterion 4: re-delivery of a completed job creates nothing new (no second transaction, job, Signal or notification)")]
    public async Task RedeliveryOfACompletedJob_IsANoOp()
    {
        var flow = AgedLineRejecting();
        var job = Job();
        await flow.BuildHandler().ProcessAsync(job, CancellationToken.None);
        var before = (flow.Transactions.Count, flow.Jobs.Count, flow.SignalWrites.Count, flow.Notified.Count, flow.RecordCreates, flow.Created.Count);
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Rejected);

        var handler = flow.BuildHandler();
        var again = await handler.ProcessAsync(job, CancellationToken.None);

        again.Status.Should().Be(JobStatus.Completed);
        (flow.Transactions.Count, flow.Jobs.Count, flow.SignalWrites.Count, flow.Notified.Count, flow.RecordCreates, flow.Created.Count).Should().Be(before);
        flow.Spy!.Calls.Should().Be(0, "the second delivery never reached the processor");
    }

    [Fact(DisplayName = "049 E criterion 5: re-delivery after a Signal-write failure completes the Signals and the terminal status; there is never a second invoice")]
    public async Task RedeliveryAfterASignalWriteFailure_CompletesWithoutASecondInvoice()
    {
        var flow = AgedLineRejecting();
        flow.CrashAtSignalWrite = 0;
        var job = Job();

        var first = await flow.BuildHandler(resumeStandIn: true).ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed, "a Signal write fault is transient");
        flow.Transactions.Should().ContainSingle();
        flow.LinkedInvoice.Should().Be(InvoiceId, "the invoice is committed");
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Processing, "not terminal until the after-steps finish");
        flow.SignalWrites.Should().BeEmpty();
        flow.Jobs.Should().BeEmpty();
        flow.Notified.Should().BeEmpty();
        flow.RecordCreates.Should().Be(0, "the Decision Record comes after the Signals");

        var redelivered = flow.BuildHandler(resumeStandIn: true);
        var second = await redelivered.ProcessAsync(job, CancellationToken.None);

        second.Status.Should().Be(JobStatus.Completed);
        flow.Resuming!.ResumeCalls.Should().Be(1, "a Processing submission that links its invoice is resumed");
        flow.Spy!.Calls.Should().Be(0, "a resume never re-runs the read, the coding or the evaluation");
        flow.Transactions.Should().ContainSingle("no second invoice, ever");
        flow.CreatedOf("sprk_invoice").Should().ContainSingle();
        flow.SignalWrites.Should().NotBeEmpty("the missing Signals are completed");
        flow.RecordCreates.Should().Be(1);
        flow.Notified.Should().ContainSingle();
        flow.Status.Should().Be((int)InvoiceSubmissionStatus.Rejected);
        flow.StatusHistory.Should().Equal([(int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Rejected]);
    }

    // ── 074 K1 / K-4: serialisation ──

    [Fact(DisplayName = "049 E criterion (074 K1): two concurrent deliveries of one submission - one runs, the other returns Failure; exactly one invoice create and one Decision Record")]
    public async Task TwoConcurrentDeliveries_OneRuns_TheOtherFails()
    {
        var flow = AgedLineRejecting();
        flow.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = flow.BuildHandler();
        var second = flow.BuildHandler();

        var running = first.ProcessAsync(Job(), CancellationToken.None);
        await flow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var rejected = await second.ProcessAsync(Job(), CancellationToken.None);
        flow.Gate.SetResult();
        var finished = await running;

        rejected.Status.Should().Be(JobStatus.Failed, "the lock is held by the first delivery");
        finished.Status.Should().Be(JobStatus.Completed);
        flow.CreatedOf("sprk_invoice").Should().ContainSingle("exactly one invoice create");
        flow.Transactions.Should().ContainSingle();
        flow.RecordCreates.Should().Be(1, "exactly one record call");
        flow.Notified.Should().ContainSingle();
        flow.Locks.Held.Should().BeEmpty("the owner released the lock");
    }
}
