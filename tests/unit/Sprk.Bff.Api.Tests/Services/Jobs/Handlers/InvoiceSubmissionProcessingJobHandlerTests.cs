using System.ServiceModel;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// ebilling-r1 task 049 part E: <see cref="InvoiceSubmissionProcessingJobHandler"/>, the ADR-004 shell around the pipeline. The processor,
/// the persister and the after-steps are STRICT scripted doubles at their interfaces (an unscripted call fails the test), so this file proves
/// the handler's own decisions: step 0, the status writes it owns, and the outcome each component result maps to. The same decisions over the
/// REAL components are <see cref="InvoiceSubmissionProcessingFlowTests"/>.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the at-least-once and dead-letter guarantees a redelivered job and an operator rely on.</remarks>
public sealed class InvoiceSubmissionProcessingJobHandlerTests
{
    internal static readonly Guid SubmissionId = Guid.Parse("04900000-0000-4000-8000-0000000000b1");
    internal static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
    internal static readonly Guid DocumentId = Guid.Parse("04900000-0000-4000-8000-0000000000d1");
    private static readonly Guid MatterId = Guid.Parse("04900000-0000-4000-8000-0000000000a1");
    private static readonly Guid FirmId = Guid.Parse("04900000-0000-4000-8000-0000000000f1");

    private const int Received = (int)InvoiceSubmissionStatus.Received;
    private const int Processing = (int)InvoiceSubmissionStatus.Processing;
    private const int Accepted = (int)InvoiceSubmissionStatus.Accepted;
    private const int Rejected = (int)InvoiceSubmissionStatus.Rejected;
    private const int Failed = (int)InvoiceSubmissionStatus.Failed;
    private const string AttentionColumn = InvoiceSubmissionPersister.SubmissionAdminAttention;

    // ── builders ──

    internal static JobContract Job(int attempt = 1, int deliveryCount = 0, Guid? submissionId = null, string? correlation = "corr-1")
    {
        var job = InvoiceSubmissionService.BuildJob(Guid.NewGuid(), submissionId ?? SubmissionId, correlation);
        job.Attempt = attempt;
        job.DeliveryCount = deliveryCount;
        return job;
    }

    internal static JobContract FinalJob() => Job(attempt: 3, deliveryCount: 1);

    internal static Entity SubmissionRow(int status = Received, Guid? invoice = null, bool trackingMissing = false)
    {
        var row = new Entity(FinanceBaseValues.SubmissionEntity, SubmissionId)
        {
            ["sprk_trackingnumber"] = "INV-100.2",
            ["sprk_submissionsequence"] = 2,
            ["sprk_firminvoicenumber"] = "INV-100",
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["createdon"] = new DateTime(2026, 6, 5, 23, 30, 0, DateTimeKind.Utc),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa),
            ["sprk_processingstatus"] = new OptionSetValue(status),
        };
        if (invoice is { } linked)
        {
            row["sprk_invoice"] = new EntityReference("sprk_invoice", linked);
        }

        if (trackingMissing)
        {
            row.Attributes.Remove("sprk_submissionsequence");
            row.Attributes.Remove("sprk_trackingnumber");
        }

        return row;
    }

    /// <summary>
    /// The snapshot of an existing-document submission (050a) over <paramref name="row"/>: its document is read from SharePoint Embedded.
    /// Task 092: the default row is an upload (no document).
    /// </summary>
    internal static SubmissionSnapshot DocumentSnapshot(Entity row) => SubmissionSnapshot.Read(row).Snapshot! with { DocumentId = DocumentId };

    internal static SubmissionEvaluationOutcome Outcome(SubmissionEvaluationKind kind, string? reason = null, Entity? row = null) => new()
    {
        Kind = kind,
        SubmissionId = SubmissionId,
        ReasonCode = reason,
        Submission = SubmissionSnapshot.Read(row ?? SubmissionRow()).Snapshot,
    };

    internal static SubmissionPersistResult Persist(SubmissionPersistStatus status, Guid? invoice = null, string? reason = null, Entity? current = null) => new()
    {
        Status = status,
        SubmissionId = SubmissionId,
        InvoiceId = invoice,
        ReasonCode = reason,
        CurrentSubmission = current,
        TerminalStatus = invoice is null ? null : InvoiceSubmissionStatus.Accepted,
    };

    internal static SubmissionAfterStepsResult AfterResult(SubmissionAfterStepsStatus status, string? reason = null) => new()
    {
        Status = status,
        SubmissionId = SubmissionId,
        InvoiceId = InvoiceId,
        ReasonCode = reason,
        TerminalStatus = status == SubmissionAfterStepsStatus.Completed ? InvoiceSubmissionStatus.Accepted : null,
    };

    // ── the strict doubles ──

    internal sealed class Processor : IInvoiceSubmissionProcessor
    {
        public Func<Entity, Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>>, Task<SubmissionEvaluationOutcome>>? Script { get; set; }
        public List<Entity> Calls { get; } = [];

        public Task<SubmissionEvaluationOutcome> EvaluateAsync(
            Entity submission, Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>> loadDocument, CancellationToken ct = default)
        {
            Calls.Add(submission);
            return (Script ?? throw new InvalidOperationException("EvaluateAsync was not scripted")).Invoke(submission, loadDocument);
        }
    }

    internal sealed class Persister : IInvoiceSubmissionPersister
    {
        public List<Func<SubmissionEvaluationOutcome, Task<SubmissionPersistResult>>> Script { get; } = [];
        public List<(SubmissionEvaluationOutcome Outcome, string? Correlation)> Calls { get; } = [];

        public Task<SubmissionPersistResult> PersistAsync(SubmissionEvaluationOutcome outcome, string? correlationId, CancellationToken ct = default)
        {
            Calls.Add((outcome, correlationId));
            if (Script.Count < Calls.Count)
            {
                throw new InvalidOperationException("PersistAsync was not scripted for this call");
            }

            return Script[Calls.Count - 1](outcome);
        }
    }

    internal sealed class AfterSteps : IInvoiceSubmissionAfterSteps
    {
        public Func<SubmissionPersistResult, Task<SubmissionAfterStepsResult>>? Run { get; set; }
        public Func<Task<SubmissionAfterStepsResult>>? Resume { get; set; }
        public Action<SubmissionEvaluationOutcome>? Preflight { get; set; }
        public List<(SubmissionPersistResult Persisted, string? Correlation)> RunCalls { get; } = [];
        public List<string?> ResumeCalls { get; } = [];
        public List<SubmissionEvaluationOutcome> PreflightCalls { get; } = [];

        public Task<SubmissionAfterStepsResult> RunAsync(SubmissionPersistResult persisted, string? correlationId, CancellationToken ct = default)
        {
            RunCalls.Add((persisted, correlationId));
            return (Run ?? throw new InvalidOperationException("RunAsync was not scripted"))(persisted);
        }

        public Task<SubmissionAfterStepsResult> ResumeAsync(Guid submissionId, string? correlationId, CancellationToken ct = default)
        {
            submissionId.Should().Be(SubmissionId);
            ResumeCalls.Add(correlationId);
            return (Resume ?? throw new InvalidOperationException("ResumeAsync was not scripted"))();
        }

        public void PreflightRejection(SubmissionEvaluationOutcome outcome)
        {
            PreflightCalls.Add(outcome);
            Preflight?.Invoke(outcome);
        }
    }

    /// <summary>The idempotency service's processing lock with its real owner semantics: a different owner cannot take a held lock.</summary>
    internal sealed class Locks : IIdempotencyService
    {
        private readonly object _gate = new();
        public Dictionary<string, string> Held { get; } = new(StringComparer.Ordinal);
        public List<(string Key, string Owner)> Acquired { get; } = [];
        public List<(string Key, string Owner)> Released { get; } = [];
        public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
        public bool RefuseAll { get; set; }

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(Markers.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            Markers.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the handler must take the lock with its job id as the owner");

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (RefuseAll || Held.ContainsKey(eventId))
                {
                    return Task.FromResult(false);
                }

                Held[eventId] = ownerId;
                Acquired.Add((eventId, ownerId));
                return Task.FromResult(true);
            }
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the handler must release the lock only as its owner");

        public Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Held.TryGetValue(eventId, out var owner) && owner == ownerId)
                {
                    Held.Remove(eventId);
                }

                Released.Add((eventId, ownerId));
                return Task.CompletedTask;
            }
        }
    }

    internal sealed class RecordingLog : ILogger<InvoiceSubmissionProcessingJobHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
                if (exception is not null)
                {
                    Entries.Add((logLevel, exception.ToString()));
                }
            }
        }
    }

    internal sealed class Rig
    {
        public Entity Row { get; set; } = SubmissionRow();
        public bool RowMissing { get; set; }
        public bool Enabled { get; set; } = true;
        public Exception? RetrieveFault { get; set; }

        public Processor Processor { get; } = new();
        public Persister Persister { get; } = new();
        public AfterSteps After { get; } = new();
        public Locks Locks { get; } = new();
        public RecordingLog Log { get; } = new();

        /// <summary>Every submission update, in order.</summary>
        public List<Dictionary<string, object>> Writes { get; } = [];
        public List<string> Events { get; } = [];
        public List<SubmissionDecidedNotification> Notified { get; } = [];
        public Exception? SinkFault { get; set; }
        public Exception? UpdateFault { get; set; }

        public bool DocumentMissing { get; set; }
        public bool DownloadReturnsNothing { get; set; }
        public string PointerContainer { get; set; } = "drive-1";
        public string AllowedContainer { get; set; } = "drive-1";
        public string? MimeType { get; set; } = "application/pdf";
        public byte[] Bytes { get; set; } = [1, 2, 3];
        public List<string> Downloads { get; } = [];

        /// <summary>Task 092: the Finance file store an upload's bytes are read from (real service, in-memory blob gateway).</summary>
        public StoredFileRig Files { get; } = new();

        /// <summary>Task 092: stages the upload of <see cref="SubmissionId"/> as the intake does; scanned clean unless tags are given.</summary>
        public StagedBlob StageUpload(byte[] bytes, IDictionary<string, string>? tags = null, bool scanned = true)
        {
            var spec = InvoiceSubmissionService.UploadFileSpec(SubmissionId, "invoice.pdf", InvoiceSubmissionChannel.Spa);
            var staged = Files.Service.StageAsync(spec, new MemoryStream(bytes), CancellationToken.None).GetAwaiter().GetResult().Blob!;
            if (scanned)
            {
                Files.Gateway.Get(staged.Container, staged.BlobPath)!.Tags =
                    new Dictionary<string, string>(tags ?? StoredFileRig.CleanTags(Files.Gateway.Now.AddMinutes(1)), StringComparer.Ordinal);
            }

            return staged;
        }

        public IEnumerable<(int Status, string? Reason)> StatusWrites => Writes
            .Where(w => w.ContainsKey("sprk_processingstatus"))
            .Select(w => (((OptionSetValue)w["sprk_processingstatus"]).Value, w.TryGetValue("sprk_outcomereasoncode", out var r) ? (string?)r : null));

        public InvoiceSubmissionProcessingJobHandler Build()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, SubmissionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string[] _, CancellationToken _) =>
                {
                    Events.Add("retrieve");
                    if (RetrieveFault is { } fault)
                    {
                        return Task.FromException<Entity>(fault);
                    }

                    if (RowMissing)
                    {
                        return Task.FromException<Entity>(new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"));
                    }

                    return Task.FromResult(Copy(Row));
                });
            entities.Setup(e => e.UpdateAsync(FinanceBaseValues.SubmissionEntity, SubmissionId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    var status = fields.TryGetValue("sprk_processingstatus", out var s) ? ((OptionSetValue)s).Value : (int?)null;
                    status.Should().NotBe(Accepted, "the handler never writes Accepted; the after-steps write the terminal status, last");
                    if (UpdateFault is { } fault)
                    {
                        return Task.FromException(fault);
                    }

                    Events.Add($"update:{status}");
                    Writes.Add(new Dictionary<string, object>(fields));
                    foreach (var (k, v) in fields)
                    {
                        Row[k] = v;
                    }

                    return Task.CompletedTask;
                });

            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
            documents.Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => DocumentMissing
                    ? null
                    : new DocumentEntity
                    {
                        Id = DocumentId.ToString(),
                        Name = "invoice",
                        GraphDriveId = PointerContainer,
                        GraphItemId = "item-1",
                        FileName = "invoice.pdf",
                        MimeType = MimeType,
                    });

            var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string drive, string item, CancellationToken _) =>
                {
                    Downloads.Add($"{drive}/{item}");
                    return Task.FromResult<Stream?>(DownloadReturnsNothing ? null : new MemoryStream(Bytes));
                });

            var sink = new Mock<IEbillingNotificationSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmissionDecidedAsync(It.IsAny<SubmissionDecidedNotification>(), It.IsAny<CancellationToken>()))
                .Returns((SubmissionDecidedNotification n, CancellationToken _) =>
                {
                    Events.Add("notify");
                    Notified.Add(n);
                    return SinkFault is { } fault ? Task.FromException(fault) : Task.CompletedTask;
                });

            return new InvoiceSubmissionProcessingJobHandler(
                entities.Object,
                documents.Object,
                spe.Object,
                TestRecordContainerResolver.ForBusinessUnitContainers(c => c == AllowedContainer),
                Processor,
                Persister,
                After,
                sink.Object,
                Locks,
                Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == new EbillingOptions { Enabled = Enabled }),
                Files.Service,
                Log);
        }

        /// <summary>Scripts the happy path of every component: Accepted outcome, Committed, Completed.</summary>
        public Rig HappyPath()
        {
            Processor.Script = (_, _) =>
            {
                Events.Add("evaluate");
                return Task.FromResult(Outcome(SubmissionEvaluationKind.Accepted));
            };
            Persister.Script.Add(_ =>
            {
                Events.Add("persist");
                return Task.FromResult(Persist(SubmissionPersistStatus.Committed, InvoiceId));
            });
            After.Preflight = _ => Events.Add("preflight");
            After.Run = _ =>
            {
                Events.Add("run");
                return Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Completed));
            };
            After.Resume = () =>
            {
                Events.Add("resume");
                return Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Completed));
            };
            return this;
        }

        public bool NothingRan => Processor.Calls.Count == 0 && Persister.Calls.Count == 0 && After.RunCalls.Count == 0 && After.ResumeCalls.Count == 0;

        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var (k, v) in row.Attributes)
            {
                copy[k] = v;
            }

            return copy;
        }
    }

    // ── 0a: the payload ──

    [Theory(DisplayName = "049 E 0a: a missing, empty or malformed payload is Poisoned; nothing is locked, read or written")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"submissionId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"submissionId\":\"not-a-guid\"}")]
    [InlineData("[1]")]
    public async Task InvalidPayload_IsPoisoned(string payload)
    {
        var rig = new Rig().HappyPath();
        var job = Job();
        job.Payload = payload == "null" ? null : JsonDocument.Parse(payload);

        var outcome = await rig.Build().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Locks.Acquired.Should().BeEmpty();
        rig.Events.Should().BeEmpty();
        rig.NothingRan.Should().BeTrue();
    }

    [Fact(DisplayName = "049 E 0a: the job type is the one 044 enqueues")]
    public void JobType_IsTheOne044Enqueues()
    {
        new Rig().Build().JobType.Should().Be(InvoiceSubmissionService.JobType).And.Be("InvoiceSubmissionProcessing");
    }

    // ── 0b: one run per submission ──

    [Fact(DisplayName = "049 E 0b: the run holds invoice-submission:{id:N} owned by the job id for the whole run and releases it")]
    public async Task Lock_IsHeldForTheWholeRun_OwnedByTheJob_AndReleased()
    {
        var rig = new Rig().HappyPath();
        var job = Job();
        rig.After.Run = _ =>
        {
            rig.Locks.Held.Should().ContainKey("invoice-submission:" + SubmissionId.ToString("N")).WhoseValue.Should().Be(job.JobId.ToString("N"));
            return Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Completed));
        };

        var outcome = await rig.Build().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Locks.Acquired.Should().Equal(("invoice-submission:" + SubmissionId.ToString("N"), job.JobId.ToString("N")));
        rig.Locks.Released.Should().Equal(rig.Locks.Acquired);
        rig.Locks.Held.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 0b: the lock is released when the run faults")]
    public async Task Lock_IsReleased_WhenTheRunFaults()
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) => throw new InvalidOperationException("boom");

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Locks.Held.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 0b: a delivery that cannot take the lock returns Failure and touches nothing (not even a Failed status)")]
    public async Task LockBusy_IsFailure_AndTouchesNothing()
    {
        var rig = new Rig().HappyPath();
        rig.Locks.RefuseAll = true;

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Events.Should().BeEmpty();
        rig.NothingRan.Should().BeTrue();
        rig.Locks.Released.Should().BeEmpty("it never held the lock");
    }

    [Fact(DisplayName = "049 E 0b: on the final delivery a busy lock is Poisoned, and the submission is still untouched (the other delivery owns it)")]
    public async Task LockBusy_OnTheFinalDelivery_IsPoisoned_WithoutWriting()
    {
        var rig = new Rig().HappyPath();
        rig.Locks.RefuseAll = true;

        var outcome = await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Writes.Should().BeEmpty();
        rig.Events.Should().BeEmpty();
    }

    // ── 0c: the re-read ──

    [Fact(DisplayName = "049 E 0c: a submission that no longer exists is Success (044 K4), logged with the id")]
    public async Task MissingSubmission_IsSuccess()
    {
        var rig = new Rig { RowMissing = true }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Writes.Should().BeEmpty();
        rig.NothingRan.Should().BeTrue();
        rig.Log.Entries.Should().Contain(e => e.Message.Contains(SubmissionId.ToString()) && e.Message.Contains("no longer exists"));
    }

    [Theory(DisplayName = "049 E 0c: a terminal status is a no-op Success and comes FIRST, even with the invoice linked or the flag off")]
    [InlineData(Accepted, false, true)]
    [InlineData(Rejected, false, true)]
    [InlineData(Failed, false, true)]
    [InlineData(Accepted, true, true)]
    [InlineData(Rejected, true, true)]
    [InlineData(Failed, true, true)]
    [InlineData(Accepted, true, false)]
    [InlineData(Rejected, false, false)]
    [InlineData(Failed, false, false)]
    public async Task TerminalStatus_IsANoOp(int status, bool linked, bool enabled)
    {
        var rig = new Rig { Row = SubmissionRow(status, linked ? InvoiceId : null), Enabled = enabled }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Writes.Should().BeEmpty("a finished submission is never rewritten, whatever the flag says");
        rig.NothingRan.Should().BeTrue();
    }

    [Fact(DisplayName = "049 E 0c: Processing with its invoice linked RESUMES through the after-steps; it never evaluates, persists or re-runs AI")]
    public async Task ProcessingWithLink_Resumes_WithoutEvaluatingOrPersisting()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.After.ResumeCalls.Should().Equal("corr-1");
        rig.Processor.Calls.Should().BeEmpty();
        rig.Persister.Calls.Should().BeEmpty();
        rig.After.RunCalls.Should().BeEmpty();
        rig.After.PreflightCalls.Should().BeEmpty();
        rig.Downloads.Should().BeEmpty("no document is downloaded for a resume");
        rig.Writes.Should().BeEmpty("the handler writes no status on a resume; the after-steps write the terminal");
    }

    [Theory(DisplayName = "049 E 0c: the resume result maps like RunAsync's: Completed/Missing Success, Retry Failure; none writes a status (Refused is its own theory)")]
    [InlineData(SubmissionAfterStepsStatus.Completed, JobStatus.Completed)]
    [InlineData(SubmissionAfterStepsStatus.SubmissionMissing, JobStatus.Completed)]
    [InlineData(SubmissionAfterStepsStatus.Retry, JobStatus.Failed)]
    public async Task ResumeResults_MapToOutcomes(SubmissionAfterStepsStatus status, JobStatus expected)
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(status, status == SubmissionAfterStepsStatus.Completed ? null : "code.x"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        rig.Writes.Should().BeEmpty("the handler writes no status on a resume; Retry stays Processing with its link");
    }

    // -- F4-1 (ontology review of part E): a submission that links its invoice is never marked Failed --

    [Theory(DisplayName = "049 E F4-1: a Processing submission that links its invoice and whose ResumeAsync is Refused stays Processing, is Poisoned, and has NO status write")]
    [InlineData("ebilling.submission.stored_state_mismatch")]
    [InlineData("ebilling.submission.signal_plan_missing")]
    [InlineData("ebilling.submission.invoice_link_mismatch")]
    [InlineData("ebilling.submission.not_processing")]
    public async Task ResumeRefused_WithALink_LeavesProcessing_AndWritesNothing(string code)
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, code));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(code);
        rig.StatusWrites.Should().BeEmpty("a linked submission is never marked Failed: no status write at all (Failed is terminal and the write is unconditional)");
        rig.Writes.Should().ContainSingle().Which.Keys.Should().Equal([AttentionColumn], "R3: only the administrator's attention text is written");
        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote(code));
        rig.After.ResumeCalls.Should().HaveCount(1);
        var errors = rig.Log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).ToList();
        errors.Should().ContainSingle(m => m.Contains(SubmissionId.ToString()) && m.Contains(code), "the id and the code, at Error, so the operator sees the bug");
        errors.Should().OnlyContain(m => !m.Contains("INV-100"));
    }

    [Theory(DisplayName = "049 E F4-1: the same through RunAsync after Committed: Refused leaves the linked submission Processing, Poisoned, no Failed write")]
    [InlineData("ebilling.submission.stored_state_mismatch")]
    [InlineData("ebilling.submission.signal_plan_missing")]
    [InlineData("ebilling.submission.invoice_link_mismatch")]
    [InlineData("ebilling.submission.not_processing")]
    public async Task RunRefused_AfterCommitted_LeavesProcessing_AndWritesNoFailed(string code)
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, code));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(code);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)], "only the Processing write before the AI call; never Failed");
        rig.Log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).Should()
            .ContainSingle(m => m.Contains(SubmissionId.ToString()) && m.Contains(code));
    }

    [Fact(DisplayName = "049 E F4-1: a Resumed persist whose resume is Refused also leaves the linked submission Processing (no Failed write)")]
    public async Task PersistResumed_ThenRefused_LeavesProcessing()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Resumed, InvoiceId)));
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, "ebilling.submission.not_processing"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
    }

    // -- R3 (D-133): whatever leaves a linked submission Processing for review reaches the administrator through sprk_adminattention --

    [Theory(DisplayName = "049 R3: a permanent Decision Record refusal (owner / subject / core) leaves the submission Processing, sets the attention text (codes only) and writes NO status")]
    [InlineData(DecisionRecordRefusalReason.OwnerRefused)]
    [InlineData(DecisionRecordRefusalReason.SubjectUnfileable)]
    [InlineData(DecisionRecordRefusalReason.CoreUnsupported)]
    public async Task PermanentRecordRefusal_SetsAdminAttention_AndKeepsProcessing(string code)
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, code));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().BeEmpty();
        rig.Row.GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value.Should().Be(Processing);
        var text = rig.Row.GetAttributeValue<string>(AttentionColumn);
        text.Should().Be(AdminAttentionText.StoppedNote(code)).And.StartWith("Processing stopped: " + code);
        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.AdminAttentionMaxLength);
        text.Should().NotContain("INV-100", "codes only (ADR-015)");
    }

    [Fact(DisplayName = "049 R3: LeaveLinkedSubmission after a Committed run (RunAsync Refused) sets the attention text too")]
    public async Task RunRefused_AfterCommitted_SetsAdminAttention()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote(DecisionRecordRefusalReason.OwnerRefused));
    }

    [Fact(DisplayName = "049 R3: a linked submission failed by FailAsync (a Received row that carries a link and fails an evaluation) is flagged, still not Failed")]
    public async Task LinkedFailAsync_SetsAdminAttention_NotFailed()
    {
        var rig = new Rig { Row = SubmissionRow(Received, InvoiceId) }.HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.Failed, "invoice.read.failed"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote("invoice.read.failed"));
    }

    [Fact(DisplayName = "049 R3: the final delivery of a retry with the invoice linked is Poisoned, stays Processing and sets the attention text; an earlier delivery sets nothing")]
    public async Task LinkedFinalDelivery_SetsAdminAttention_EarlierDeliveryDoesNot()
    {
        var early = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        early.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.signal_write_skipped"));
        var last = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        last.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.signal_write_skipped"));

        (await early.Build().ProcessAsync(Job(), CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        (await last.Build().ProcessAsync(FinalJob(), CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);

        early.Writes.Should().BeEmpty("a delivery that will be retried says nothing");
        last.StatusWrites.Should().BeEmpty();
        last.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote("ebilling.submission.signal_write_skipped"));
    }

    [Theory(DisplayName = "050a review round 3 (uac-r2 F2): a re-file Retry (isolation unreadable, or the reparent faulted) on every delivery: the final delivery tells the administrator the invoice is committed and its document is NOT filed")]
    [InlineData("ebilling.submission.document_isolation_unreadable")]
    [InlineData("ebilling.submission.document_refile_failed")]
    public async Task RefileRetry_OnEveryDelivery_FinalNoteSaysTheDocumentIsNotFiled(string code)
    {
        var deliveries = new[] { Job(), Job(), FinalJob() };
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, code) with { DocumentNotFiled = true });

        var outcomes = new List<JobStatus>();
        foreach (var delivery in deliveries)
        {
            outcomes.Add((await rig.Build().ProcessAsync(delivery, CancellationToken.None)).Status);
        }

        outcomes.Should().Equal(JobStatus.Failed, JobStatus.Failed, JobStatus.Poisoned);
        rig.StatusWrites.Should().BeEmpty("never Failed: the invoice is committed");
        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote(code, documentNotFiled: true))
            .And.Contain("the invoice is committed but its document is NOT filed under it");
    }

    [Fact(DisplayName = "049 R3: the final delivery with NO link is Failed as before and sets no attention text (the Failed status is the signal)")]
    public async Task UnlinkedFinalDelivery_IsFailed_WithoutAttention()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.signal_write_skipped"));

        await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        rig.StatusWrites.Should().Contain((Failed, "ebilling.submission.signal_write_skipped"));
        rig.Row.Contains(AttentionColumn).Should().BeFalse();
    }

    [Fact(DisplayName = "049 R3: the stopped note MERGES with a Signal-refusal attention already present; the refusals are rebuilt from the stored findings, not lost")]
    public async Task StoppedNote_MergesWithTheSignalRefusalAttention()
    {
        var signalText = "1 Signal could not be created: dataverse_access_denied (BILL-W on line aaaaaaaa)";
        var row = SubmissionRow(Processing, InvoiceId);
        row[AttentionColumn] = signalText;
        row["sprk_findingsjson"] = new System.Text.Json.Nodes.JsonObject
        {
            [InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["policyCode"] = "BILL-W", ["code"] = "dataverse_access_denied",
                ["subject"] = new System.Text.Json.Nodes.JsonObject { ["entity"] = "sprk_billingevent", ["id"] = "aaaaaaaa-0000-0000-0000-000000000000" },
            }),
        }.ToJsonString();
        var rig = new Rig { Row = row }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(
            signalText + AdminAttentionText.Separator + AdminAttentionText.StoppedNote(DecisionRecordRefusalReason.OwnerRefused));
    }

    [Fact(DisplayName = "049 R3: an administrator's text and a policy note already in the column are kept")]
    public async Task StoppedNote_KeepsOtherSegments()
    {
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "missing")!;
        var row = SubmissionRow(Processing, InvoiceId);
        row[AttentionColumn] = policy;
        var rig = new Rig { Row = row }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(policy + AdminAttentionText.Separator + AdminAttentionText.StoppedNote(DecisionRecordRefusalReason.OwnerRefused));
    }

    [Theory(DisplayName = "050a review round 2, 2(b): a confirmed document's re-file refused after the commit: the submission stays Processing (no status write), the job is Poisoned, the administrator is told the invoice has NO document, and a replay after the cause is resolved completes it")]
    [InlineData("ebilling.submission.document_already_invoiced")]
    [InlineData("ebilling.submission.document_not_found")]
    [InlineData("ebilling.submission.document_rejected")]
    [InlineData("record_owner_secure_parent_not_isolated")]
    public async Task RefileRefusedAfterTheCommit_StaysProcessing_TellsTheAdminTheInvoiceHasNoDocument_AndAReplayCompletes(string code)
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, code) with { DocumentNotFiled = true });

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "no redelivery can change a refused re-file; the message is dead-lettered for a replay");
        outcome.ErrorMessage.Should().Be(code);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)], "never Failed: the invoice is committed");
        var text = rig.Row.GetAttributeValue<string>(AttentionColumn);
        text.Should().Be(AdminAttentionText.StoppedNote(code, documentNotFiled: true))
            .And.StartWith("Processing stopped: " + code)
            .And.Contain("its document is NOT filed under it", "an invoice is never left without its document silently");
        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.AdminAttentionMaxLength);

        // The administrator resolves the document (e.g. unlinks it from the other invoice) and replays the message: the resume re-runs the
        // re-file first and completes (the after-steps then withdraw the stopped segment, R7).
        // The replay finds what the commit left: Processing with the invoice linked (and the attention text).
        var replay = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        replay.Row[AttentionColumn] = text;
        replay.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Completed));
        (await replay.Build().ProcessAsync(Job(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        replay.After.ResumeCalls.Should().HaveCount(1, "the replay resumes the after-steps, whose first step is the re-file");
        replay.StatusWrites.Should().BeEmpty("the after-steps write the terminal status, not the handler");
    }

    [Fact(DisplayName = "050a review round 2, 2(b) (must not fire): an after-steps refusal that is NOT a refused re-file keeps the ordinary stopped note")]
    public async Task OtherRefusal_KeepsTheOrdinaryStoppedNote()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, "ebilling.submission.signal_plan_missing"));

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote("ebilling.submission.signal_plan_missing"))
            .And.NotContain("NOT filed");
    }

    [Fact(DisplayName = "049 R3: a redelivery that stops for the same reason writes nothing new (idempotent); a different reason replaces only the stopped segment")]
    public async Task StoppedNote_IsIdempotentAcrossARedelivery()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);
        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Writes.Should().ContainSingle("the second delivery found the same text and wrote nothing");
        rig.After.ResumeCalls.Should().HaveCount(2);

        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.SubjectUnfileable));
        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Writes.Should().HaveCount(2);
        rig.Row.GetAttributeValue<string>(AttentionColumn).Should().Be(AdminAttentionText.StoppedNote(DecisionRecordRefusalReason.SubjectUnfileable));
    }

    [Fact(DisplayName = "049 R3: a fault writing the attention text is logged and swallowed - the outcome is still Poisoned with the same code, and no status is written")]
    public async Task AttentionWriteFault_DoesNotChangeTheOutcome()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId), UpdateFault = new TimeoutException("dataverse") }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        rig.Writes.Should().BeEmpty();
        rig.Row.Contains(AttentionColumn).Should().BeFalse();
        rig.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Needs Admin Attention") && e.Message.Contains("TimeoutException"));
    }

    [Fact(DisplayName = "049 R3: a read fault while composing the attention text is swallowed too")]
    public async Task AttentionReadFault_DoesNotChangeTheOutcome()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () =>
        {
            rig.RetrieveFault = new TimeoutException("dataverse"); // every read from now on: the attention read
            return Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, DecisionRecordRefusalReason.OwnerRefused));
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 R3 (must not fire): a flag-off linked submission (the administrator's own switch) is left alone with no attention text")]
    public async Task FlagOff_WithALink_SetsNoAttention()
    {
        var rig = new Rig { Enabled = false, Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E F4-1: a Received row that already carries a link and then fails an evaluation is NOT marked Failed (only the Processing write)")]
    public async Task ReceivedWithLink_ThenFailedOutcome_IsNotMarkedFailed()
    {
        var rig = new Rig { Row = SubmissionRow(Received, InvoiceId) }.HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.Failed, "invoice.read.failed"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E F4-1: a permanent step failure (document unavailable) on a row that carries a link is NOT marked Failed")]
    public async Task PermanentStepFailure_WithALink_IsNotMarkedFailed()
    {
        var rig = new Rig { Row = SubmissionRow(Received, InvoiceId), DocumentMissing = true }.HappyPath();
        rig.Processor.Script = async (row, load) =>
        {
            await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
    }

    [Fact(DisplayName = "049 E 0c: a resume that throws is transient: Failure, and the submission is left Processing")]
    public async Task ResumeException_IsFailure()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => throw new TimeoutException("dataverse");

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 0c: Received with a link (an anomaly) takes the normal path; the persister then answers Resumed and the stored state resumes")]
    public async Task ReceivedWithLink_TakesTheNormalPath()
    {
        var rig = new Rig { Row = SubmissionRow(Received, InvoiceId) }.HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ =>
        {
            rig.Events.Add("persist");
            return Task.FromResult(Persist(SubmissionPersistStatus.Resumed, InvoiceId));
        });

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Events.Should().ContainInOrder("update:" + Processing, "evaluate", "persist", "resume");
        rig.After.RunCalls.Should().BeEmpty("a terminal derived from a re-run outcome could contradict the committed one");
    }

    // ── ADR-018: the flag ──

    [Fact(DisplayName = "049 E ADR-018: Ebilling:Enabled false marks the submission Failed ebilling.disabled and Poisons the job; no pipeline step runs")]
    public async Task FlagOff_IsFailedAndPoisoned()
    {
        var rig = new Rig { Enabled = false }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Failed, "ebilling.disabled")]);
        rig.NothingRan.Should().BeTrue();
        rig.Downloads.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E ADR-018: with the flag off a submission that already links its invoice is left alone (Poisoned, replayable)")]
    public async Task FlagOff_WithALink_LeavesTheCommittedSubmissionAlone()
    {
        var rig = new Rig { Enabled = false, Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Writes.Should().BeEmpty();
        rig.NothingRan.Should().BeTrue();
    }

    // ── 0d: the tracking number ──

    [Fact(DisplayName = "049 E 0d (D-87): a submission without its tracking number is Failed tracking_missing and Poisoned, before Processing is written; never allocated")]
    public async Task MissingTrackingNumber_IsFailedBeforeProcessing()
    {
        var rig = new Rig { Row = SubmissionRow(trackingMissing: true) }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Failed, "ebilling.submission.tracking_missing")]);
        rig.Processor.Calls.Should().BeEmpty();
    }

    // ── 1: Processing, then the pipeline ──

    [Fact(DisplayName = "049 E 1: Processing is written before the processor is asked; a Received submission goes Received -> Processing -> (the after-steps' terminal)")]
    public async Task Processing_IsWrittenBeforeAnyAiCall()
    {
        var rig = new Rig().HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Events.Should().ContainInOrder("retrieve", "update:" + Processing, "evaluate", "preflight", "persist", "run");
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)], "the handler writes only Processing here; Accepted is the after-steps' LAST step");
        rig.After.RunCalls.Single().Correlation.Should().Be("corr-1");
        rig.Persister.Calls.Single().Correlation.Should().Be("corr-1");
    }

    [Fact(DisplayName = "049 E 1: a submission already Processing (a crashed attempt, no link) restarts from the read without a second Processing write")]
    public async Task ProcessingWithoutLink_RestartsFromTheRead()
    {
        var rig = new Rig { Row = SubmissionRow(Processing) }.HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Writes.Should().BeEmpty();
        rig.Processor.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 E 1: an empty job correlation id is passed on as null, so the submission's own is kept")]
    public async Task EmptyCorrelation_IsNull()
    {
        var rig = new Rig().HappyPath();

        await rig.Build().ProcessAsync(Job(correlation: null), CancellationToken.None);

        rig.Persister.Calls.Single().Correlation.Should().BeNull();
        rig.After.RunCalls.Single().Correlation.Should().BeNull();
    }

    // ── 1a: the document ──

    [Fact(DisplayName = "049 E 1a / 092: an existing-document submission's bytes and MIME type reach the read request; the pointer's container is verified first")]
    public async Task Document_IsDownloadedAfterTheContainerCheck_AndHandedToTheRead()
    {
        var rig = new Rig { Bytes = [9, 8, 7], MimeType = "image/png" }.HappyPath();
        InvoiceReadRequest? request = null;
        rig.Processor.Script = async (row, load) =>
        {
            request = await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Downloads.Should().Equal("drive-1/item-1");
        request!.Content.ToArray().Should().Equal(9, 8, 7);
        request.ContentType.Should().Be("image/png");
        request.DocumentId.Should().Be(DocumentId);
    }

    [Fact(DisplayName = "049 E 1a: no MIME type on the document keeps the read request's default (application/pdf)")]
    public async Task Document_WithoutMimeType_KeepsTheDefault()
    {
        var rig = new Rig { MimeType = null }.HappyPath();
        InvoiceReadRequest? request = null;
        rig.Processor.Script = async (row, load) =>
        {
            request = await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        request!.ContentType.Should().Be("application/pdf");
    }

    [Fact(DisplayName = "049 E 1a: an unverifiable pointer container is Failed document_storage_unverified, Poisoned, and nothing is downloaded")]
    public async Task UnverifiableContainer_IsFailed_AndNothingIsDownloaded()
    {
        var rig = new Rig { PointerContainer = "drive-evil" }.HappyPath();
        rig.Processor.Script = async (row, load) =>
        {
            await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Downloads.Should().BeEmpty("the check comes before the application-identity download");
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, "ebilling.submission.document_storage_unverified")]);
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 1a: a missing document row is Failed document_unavailable and Poisoned")]
    public async Task MissingDocumentRow_IsFailed()
    {
        var rig = new Rig { DocumentMissing = true }.HappyPath();
        rig.Processor.Script = async (row, load) =>
        {
            await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Contain((Failed, "ebilling.submission.document_unavailable"));
    }

    [Fact(DisplayName = "049 E 1a: a download that returns nothing is transient: Failure, no Failed status")]
    public async Task DownloadReturningNothing_IsTransient()
    {
        var rig = new Rig { DownloadReturnsNothing = true }.HappyPath();
        rig.Processor.Script = async (row, load) =>
        {
            await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
    }

    // ── 1a (task 092): an upload's bytes come from the Finance file store ──

    /// <summary>Scripts the processor to load the default (upload) row's PDF and keep the request it got.</summary>
    private static Func<InvoiceReadRequest?> LoadsTheUpload(Rig rig)
    {
        InvoiceReadRequest? request = null;
        rig.Processor.Script = async (row, load) =>
        {
            request = await load(SubmissionSnapshot.Read(row).Snapshot!, CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted);
        };
        return () => request;
    }

    [Fact(DisplayName = "092: an upload's staged bytes (scanned clean, hash matching) reach the read request; nothing is read from SPE")]
    public async Task Upload_IsReadFromTheFileStore()
    {
        var rig = new Rig().HappyPath();
        rig.StageUpload([0x25, 0x50, 0x44, 0x46, 7]);
        var request = LoadsTheUpload(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        request()!.Content.ToArray().Should().Equal(0x25, 0x50, 0x44, 0x46, 7);
        request()!.ContentType.Should().Be("application/pdf");
        request()!.DocumentId.Should().Be(StoredFileIds.ForUpload(SubmissionId), "the reader's id is the Stored File's");
        rig.Downloads.Should().BeEmpty("no SharePoint Embedded read for an upload");
    }

    [Fact(DisplayName = "092: an upload with no scan verdict yet is transient (Failure), never Failed")]
    public async Task Upload_PendingScan_IsTransient()
    {
        var rig = new Rig().HappyPath();
        rig.StageUpload([1, 2, 3], scanned: false);
        LoadsTheUpload(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "092 must-fire: an upload scanned Malicious is Failed file_malicious and Poisoned; nothing persisted")]
    public async Task Upload_Malicious_IsFailedFileMalicious()
    {
        var rig = new Rig().HappyPath();
        rig.StageUpload([1, 2, 3], new Dictionary<string, string> { [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanMaliciousValue });
        LoadsTheUpload(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, SubmissionReasonCodes.FileMalicious)]);
        SubmissionReasonCodes.FileMalicious.Should().Be("ebilling.submission.file_malicious");
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Theory(DisplayName = "092 must-fire: an upload with no staged blob, or bytes that no longer match the staged hash, is Failed document_unavailable and Poisoned")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_MissingOrChanged_IsFailedDocumentUnavailable(bool staged)
    {
        var rig = new Rig().HappyPath();
        if (staged)
        {
            var blob = rig.StageUpload([1, 2, 3]);
            rig.Files.Gateway.Get(blob.Container, blob.BlobPath)!.Content = [9, 9, 9];
        }

        LoadsTheUpload(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, SubmissionReasonCodes.DocumentUnavailable)]);
        rig.Persister.Calls.Should().BeEmpty();
    }

    // ── 092: file_unservable is the one refusal that fails a submission linked to its invoice ──

    [Fact(DisplayName = "092 (model 9.1 step 6): the persister's FileUnservable WITH the invoice marks the submission Failed, Poisons the job and runs no after-step")]
    public async Task PersistUnservable_WithTheInvoice_IsFailed_NoAfterStep()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Refused, InvoiceId, SubmissionReasonCodes.FileUnservable)));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(SubmissionReasonCodes.FileUnservable);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, SubmissionReasonCodes.FileUnservable)]);
        rig.After.RunCalls.Should().BeEmpty("no after-step runs after a definitive flip failure");
        rig.After.ResumeCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "092: a resume whose flip gate answers FileUnservable marks the linked submission Failed and Poisons the job")]
    public async Task ResumeUnservable_IsFailed()
    {
        var rig = new Rig { Row = SubmissionRow(Processing, InvoiceId) }.HappyPath();
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, SubmissionReasonCodes.FileUnservable));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Failed, SubmissionReasonCodes.FileUnservable)]);
        rig.Writes.Should().NotContain(w => w.ContainsKey(AttentionColumn), "the Failed status is the signal; no stopped note");
    }

    [Fact(DisplayName = "092 must-not-fire: a refusal WITHOUT an invoice keeps the ordinary FailAsync path (same Failed write, no FileUnservable special case)")]
    public async Task PersistRefused_WithoutTheInvoice_IsTheOrdinaryFailure()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Refused, null, SubmissionReasonCodes.FileUnservable)));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, SubmissionReasonCodes.FileUnservable)]);
        rig.After.RunCalls.Should().BeEmpty();
    }

    // ── 2: the processor's outcome kinds ──

    [Theory(DisplayName = "049 E 2 (FR-21, D-101): a document that could not be read is Rejected with that code; no invoice, no persister call; the firm is told")]
    [InlineData("invoice.read.unreadable")]
    [InlineData("invoice.read.page_limit")]
    [InlineData("invoice.read.no_invoice")]
    public async Task RejectedUnreadable_IsRejectedWithTheCode_AndNoInvoice(string code)
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.RejectedUnreadable, code));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Rejected, code)]);
        rig.Persister.Calls.Should().BeEmpty("no invoice, no line, no approval for a document that was not read");
        rig.After.PreflightCalls.Should().BeEmpty();
        rig.After.RunCalls.Should().BeEmpty();
        rig.Notified.Should().ContainSingle().Which.Should().Be(
            new SubmissionDecidedNotification(SubmissionId, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Spa, null, code));
    }

    [Fact(DisplayName = "049 E 2: a notification fault after a document-level rejection never undoes it or retries the job")]
    public async Task RejectedUnreadable_SinkFault_DoesNotRetry()
    {
        var rig = new Rig { SinkFault = new InvalidOperationException("sink down") }.HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.RejectedUnreadable, "invoice.read.unreadable"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.StatusWrites.Should().Contain((Rejected, "invoice.read.unreadable"));
    }

    [Theory(DisplayName = "049 E 2: a Failed outcome writes Failed with the processor's code and Poisons the job; no persister call")]
    [InlineData("ai.invoice.disabled")]
    [InlineData("ai.invoice.read.not_configured")]
    [InlineData("ebilling.submission.core_record_missing")]
    public async Task FailedOutcome_IsFailedWithItsCode(string code)
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.Failed, code));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(code);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, code)]);
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 2: invoice.read.service_failed is retried (Failure) until the final delivery, then the submission is Failed with that code and the job Poisoned")]
    public async Task ServiceFailed_RetriesThenFails()
    {
        var failure = new InvoiceAiException(InvoiceAiException.ReadServiceFailed, InvoiceAiException.ReadCapability, "down");
        var early = new Rig().HappyPath();
        early.Processor.Script = (_, _) => throw failure;
        var last = new Rig().HappyPath();
        last.Processor.Script = (_, _) => throw failure;

        var first = await early.Build().ProcessAsync(Job(), CancellationToken.None);
        var final = await last.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        early.StatusWrites.Should().NotContain(w => w.Status == Failed);
        final.Status.Should().Be(JobStatus.Poisoned);
        last.StatusWrites.Should().Contain((Failed, "invoice.read.service_failed"));
    }

    [Fact(DisplayName = "049 E 2: the final delivery is the broker's delivery count too, not only the attempt number")]
    public async Task FinalDelivery_IsTheDeliveryCountToo()
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) => throw new TimeoutException();

        var outcome = await rig.Build().ProcessAsync(Job(attempt: 1, deliveryCount: JobContract.MaxDeliveryCount), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Contain((Failed, "ebilling.submission.processing_failed"));
    }

    [Fact(DisplayName = "049 E 2: an unexpected fault on the final delivery with the invoice already linked leaves the submission Processing (a replay resumes it)")]
    public async Task FinalDelivery_WithALink_DoesNotMarkFailed()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ =>
        {
            rig.Row["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId);
            throw new TimeoutException("dataverse");
        };

        var outcome = await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
    }

    [Fact(DisplayName = "049 E 2: a failure that cannot even be recorded returns Failure (the submission is never left claiming a status it does not have)")]
    public async Task UnrecordableFailure_IsFailure()
    {
        var rig = new Rig { Enabled = false }.HappyPath();
        rig.UpdateFault = new TimeoutException("dataverse");

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
    }

    [Fact(DisplayName = "049 E: cancellation propagates; it is neither a Failure nor a Failed submission")]
    public async Task Cancellation_Propagates()
    {
        var rig = new Rig().HappyPath();
        using var cts = new CancellationTokenSource();
        rig.Processor.Script = (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var act = () => rig.Build().ProcessAsync(Job(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
        rig.Locks.Held.Should().BeEmpty();
    }

    // ── 3: the preflight ──

    [Fact(DisplayName = "049 E 3 (074 K-3): an AutoRejected outcome is preflighted BEFORE the persister creates anything")]
    public async Task AutoRejected_IsPreflighted_BeforePersist()
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) =>
        {
            rig.Events.Add("evaluate");
            return Task.FromResult(Outcome(SubmissionEvaluationKind.AutoRejected, "invoice.age"));
        };

        await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        rig.Events.Should().ContainInOrder("evaluate", "preflight", "persist", "run");
        rig.After.PreflightCalls.Should().ContainSingle().Which.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
    }

    [Fact(DisplayName = "049 E 3: a refused preflight is Failed with the refusal's code, Poisoned, and nothing is persisted")]
    public async Task PreflightRefusal_IsFailed_AndNothingIsPersisted()
    {
        var rig = new Rig().HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.AutoRejected, "invoice.age"));
        rig.After.Preflight = _ => throw new DecisionRecordRefusedException(DecisionRecordRefusalReason.RecordClassUnderivable, "refused");

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Persister.Calls.Should().BeEmpty();
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, DecisionRecordRefusalReason.RecordClassUnderivable)]);
    }

    // ── 4: the persister's results ──

    [Fact(DisplayName = "049 E 4: Committed runs the after-steps with that result and maps Completed to Success")]
    public async Task Committed_RunsTheAfterSteps()
    {
        var rig = new Rig().HappyPath();

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.After.RunCalls.Single().Persisted.InvoiceId.Should().Be(InvoiceId);
        rig.After.ResumeCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 4 (R2): Retry that carries an invoice id (the submission is linked) resumes from the STORED state - ResumeAsync, never RunAsync - and the resume's Retry stays Processing")]
    public async Task RetryWithInvoice_ResumesFromTheStoredState()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Retry, InvoiceId, "ebilling.submission.totals_failed")));
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.totals_failed"));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.After.RunCalls.Should().BeEmpty("RunAsync is only for a run that committed in this delivery (Committed)");
        rig.After.ResumeCalls.Should().ContainSingle();
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)], "it stays Processing with its link");
        rig.Writes.Should().OnlyContain(w => w.ContainsKey("sprk_processingstatus"), "a retryable Retry is not 'stopped': no attention text");
    }

    [Fact(DisplayName = "049 E 4 (R2): a Resumed persist whose totals faulted (the persister's old Retry-with-invoice shape) is answered from the stored state: ResumeAsync completes it, no stored_state_mismatch, nothing flagged")]
    public async Task ResumedWithATotalsFault_ResumesFromStoredState_NoStoredStateMismatch()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Retry, InvoiceId, "ebilling.submission.totals_failed")));
        // What the real after-steps do with this run's re-evaluated terminal when the stored one differs (13a A).
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Refused, SubmissionReasonCodes.StoredStateMismatch));
        rig.After.Resume = () => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Completed));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.After.ResumeCalls.Should().ContainSingle();
        rig.After.RunCalls.Should().BeEmpty();
        rig.Writes.Should().OnlyContain(w => w.ContainsKey("sprk_processingstatus"), "no stored_state_mismatch, so no 'processing stopped' text");
        rig.Row.Contains(AttentionColumn).Should().BeFalse();
    }

    [Fact(DisplayName = "049 E 4: Retry without an invoice is Failure (nothing persisted); on the final delivery the submission is Failed with the code")]
    public async Task RetryWithoutInvoice_IsFailure_ThenFailed()
    {
        var early = new Rig().HappyPath();
        early.Persister.Script.Clear();
        early.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Retry, null, "ebilling.submission.transaction_failed")));
        var last = new Rig().HappyPath();
        last.Persister.Script.Clear();
        last.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Retry, null, "ebilling.submission.transaction_failed")));

        var first = await early.Build().ProcessAsync(Job(), CancellationToken.None);
        var final = await last.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        early.After.RunCalls.Should().BeEmpty();
        early.StatusWrites.Should().NotContain(w => w.Status == Failed);
        final.Status.Should().Be(JobStatus.Poisoned);
        last.StatusWrites.Should().Contain((Failed, "ebilling.submission.transaction_failed"));
    }

    [Fact(DisplayName = "049 E 4: Refused is Failed with the persister's code and Poisoned")]
    public async Task PersistRefused_IsFailedWithTheCode()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Refused, null, "ebilling.submission.submitter_missing")));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, "ebilling.submission.submitter_missing")]);
        rig.After.RunCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 4: SubmissionMissing from the persister is Success")]
    public async Task PersistSubmissionMissing_IsSuccess()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.SubmissionMissing)));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.After.RunCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 4: Resumed from the persister resumes from the STORED state (ResumeAsync), never RunAsync with this run's outcome")]
    public async Task PersistResumed_ResumesFromTheStoredState()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Resumed, InvoiceId)));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.After.ResumeCalls.Should().ContainSingle();
        rig.After.RunCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 E 4 (072 L): ReEvaluate re-runs the processor ONCE on the current submission, then persists that outcome")]
    public async Task ReEvaluate_RerunsTheProcessorOnce()
    {
        var rig = new Rig().HappyPath();
        var current = SubmissionRow(Processing);
        current["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        var first = Outcome(SubmissionEvaluationKind.Accepted);
        var second = Outcome(SubmissionEvaluationKind.Blocked, "ebilling.submission.blocked");
        var calls = 0;
        rig.Processor.Script = (_, _) => Task.FromResult(++calls == 1 ? first : second);
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.ReEvaluate, null, null, current)));
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.Committed, InvoiceId)));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Processor.Calls.Should().HaveCount(2);
        rig.Processor.Calls[1].Should().BeSameAs(current, "the second pass judges the submission as the persister re-read it");
        rig.Persister.Calls.Select(c => c.Outcome).Should().Equal(first, second);
        rig.After.PreflightCalls.Should().HaveCount(2);
    }

    [Fact(DisplayName = "049 E 4: a second ReEvaluate is Failure (retry), not a loop; two evaluations and two persist calls at most")]
    public async Task SecondReEvaluate_IsFailure_NotALoop()
    {
        var rig = new Rig().HappyPath();
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.ReEvaluate, null, null, SubmissionRow(Processing))));
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.ReEvaluate, null, null, SubmissionRow(Processing))));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Processor.Calls.Should().HaveCount(2);
        rig.Persister.Calls.Should().HaveCount(2);
        rig.After.RunCalls.Should().BeEmpty();
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
    }

    [Fact(DisplayName = "049 E 4: a ReEvaluate whose second pass is a document-level rejection ends as that rejection")]
    public async Task ReEvaluate_ThenUnreadable_IsRejected()
    {
        var rig = new Rig().HappyPath();
        var calls = 0;
        rig.Processor.Script = (_, _) => Task.FromResult(++calls == 1
            ? Outcome(SubmissionEvaluationKind.Accepted)
            : Outcome(SubmissionEvaluationKind.RejectedUnreadable, "invoice.read.unreadable"));
        rig.Persister.Script.Clear();
        rig.Persister.Script.Add(_ => Task.FromResult(Persist(SubmissionPersistStatus.ReEvaluate, null, null, SubmissionRow(Processing))));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.StatusWrites.Should().Contain((Rejected, "invoice.read.unreadable"));
    }

    // ── 5: the after-steps' results ──

    [Theory(DisplayName = "049 E 5: the after-steps' result maps Completed/Missing Success, Retry Failure (Processing); none writes Failed (Refused: see F4-1)")]
    [InlineData(SubmissionAfterStepsStatus.Completed, JobStatus.Completed)]
    [InlineData(SubmissionAfterStepsStatus.SubmissionMissing, JobStatus.Completed)]
    [InlineData(SubmissionAfterStepsStatus.Retry, JobStatus.Failed)]
    public async Task AfterStepsResults_MapToOutcomes(SubmissionAfterStepsStatus status, JobStatus expected)
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(status, status == SubmissionAfterStepsStatus.Retry ? "ebilling.submission.signal_write_skipped" : null));

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
    }

    [Fact(DisplayName = "049 E 5: an after-steps exception is transient: Failure, no Failed status before the final delivery")]
    public async Task AfterStepsException_IsFailure()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => throw new TimeoutException("signal write");

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null)]);
    }

    [Fact(DisplayName = "049 E 5: Retry on the final delivery with the invoice linked is Poisoned and leaves the submission Processing (resumable)")]
    public async Task AfterStepsRetry_OnTheFinalDelivery_KeepsProcessing()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ =>
        {
            rig.Row["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId);
            return Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.signal_write_skipped"));
        };

        var outcome = await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().NotContain(w => w.Status == Failed);
    }

    [Fact(DisplayName = "049 E 5: after-steps Retry on the final delivery with no invoice link marks Failed with the after-steps' own reason code")]
    public async Task AfterStepsRetry_OnTheFinalDelivery_NoLink_FailsWithTheResultCode()
    {
        var rig = new Rig().HappyPath();
        rig.After.Run = _ => Task.FromResult(AfterResult(SubmissionAfterStepsStatus.Retry, "ebilling.submission.signal_write_skipped"));

        var outcome = await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, "ebilling.submission.signal_write_skipped")]);
    }

    // ── invariants ──

    [Fact(DisplayName = "049 E invariant: across every branch the handler writes only Processing, Failed and the unreadable Rejected; never Accepted")]
    public async Task NeverWritesAccepted()
    {
        // The rig's UpdateAsync double fails the test on an Accepted write; drive the branches that write.
        var rigs = new List<(Rig Rig, JobContract Job)>();
        var flagOff = new Rig { Enabled = false }.HappyPath();
        rigs.Add((flagOff, Job()));
        var unreadable = new Rig().HappyPath();
        unreadable.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.RejectedUnreadable, "invoice.read.unreadable"));
        rigs.Add((unreadable, Job()));
        var happy = new Rig().HappyPath();
        rigs.Add((happy, Job()));

        foreach (var (rig, job) in rigs)
        {
            (await rig.Build().ProcessAsync(job, CancellationToken.None)).Status.Should().NotBe(JobStatus.Failed);
            rig.StatusWrites.Select(w => w.Status).Should().OnlyContain(s => s == Processing || s == Failed || s == Rejected);
        }
    }

    [Fact(DisplayName = "049 E ADR-015: logs carry ids and codes only; never the tracking number or the typed invoice number")]
    public async Task Logs_CarryIdsAndCodesOnly()
    {
        var rig = new Rig { Enabled = true }.HappyPath();
        rig.Processor.Script = (_, _) => Task.FromResult(Outcome(SubmissionEvaluationKind.RejectedUnreadable, "invoice.read.unreadable"));
        await rig.Build().ProcessAsync(Job(), CancellationToken.None);
        var failing = new Rig { Row = SubmissionRow(trackingMissing: true) }.HappyPath();
        await failing.Build().ProcessAsync(Job(), CancellationToken.None);
        var faulting = new Rig().HappyPath();
        faulting.Processor.Script = (_, _) => throw new InvalidOperationException("INV-100.2 leaked in a message");
        await faulting.Build().ProcessAsync(Job(), CancellationToken.None);

        foreach (var entry in rig.Log.Entries.Concat(failing.Log.Entries))
        {
            entry.Message.Should().NotContain("INV-100");
        }

        faulting.Log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).Should()
            .OnlyContain(m => m.Contains(SubmissionId.ToString()) && !m.Contains("INV-100"), "a fault is logged by its type and our code, not its message");
    }
}
