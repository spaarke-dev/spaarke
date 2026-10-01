// spaarkeai-word-add-in-r1 task 060 (GitHub #1084): the Office save's job record, stored durably and read typed.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the job record's members lived in two classes that each had a different main job: the in-memory
//       store, the status read and the SSE stream in OfficeService (the save), and the row writes plus the idempotency
//       lookup in OfficeDocumentPersistence (the sprk_document writer). The Dataverse row (IProcessingJobService) is
//       the store; this class is its one reader and writer for the Office save.
//   (2) Extension — No. The goal is to REMOVE a responsibility from OfficeService: the job-status/SSE cluster is the one
//       cut the architecture review found genuinely cohesive (task 060 POML). It is a peer of the existing
//       OfficeDocumentPersistence / OfficeStorageUploader / OfficeSearchService collaborators.
//   (3) Cost-of-doing-nothing — after a restart, or on a second instance, the pane cannot learn which document its save
//       produced. Both job reads threw in production (RuntimeBinderException, 2026-08-25), so a repeated save was never
//       recognised as a duplicate, and 27 of 40 saves in 60 days had no job row at all (#1084).
//
// Placement Justification (bff-extensions.md): in the BFF, beside the Office save it serves. No new endpoint, package
// or background work. One concrete registration in the existing AddOfficeModule (ADR-010), unconditional (ADR-032).

using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// The Office save's job record: it creates the <c>sprk_processingjob</c> row, records each transition the save makes,
/// answers the idempotency check and the status read from that row, and streams the status over SSE.
/// </summary>
/// <remarks>
/// <para>
/// <b>The store is the row (task 060 decision, notes §2).</b> Nothing is held in process memory, so a restart or a second
/// instance reads the same answer. ADR-017: job status and outcome are persisted.
/// </para>
/// <para>
/// <b>The save's own view lives in <c>sprk_result</c>.</b> The save is synchronous: when <c>POST /api/office/save</c>
/// returns, its job is already Completed with its document, or Failed. Finalization then runs on the queue, and its
/// workers move <c>sprk_status</c>, <c>sprk_currentstage</c> and <c>sprk_progress</c> again (Running, then Completed or
/// Failed). Those columns therefore describe the PIPELINE. The pane needs the SAVE: the exact
/// <see cref="JobStatusResponse"/> the save produced, which the workers never write. Reading the pipeline columns instead
/// would turn the pane's immediate success into a wait on AI processing, and would show a finalization failure as a
/// failed save.
/// </para>
/// <para>
/// <b>One rule, two readers.</b> <see cref="ToEffectiveView"/> turns a row into the job's effective state, and both the
/// status read and the idempotency check (task 039) use it. A save whose request died (a restart mid-save) leaves its row
/// non-terminal forever. Past <see cref="MaxSaveDuration"/> it reads as Failed, and the idempotency check treats it as a
/// new attempt, so a retry is never answered with a job that will never finish.
/// </para>
/// </remarks>
public class OfficeJobStatusService
{
    /// <summary>
    /// The longest a save can still be running. The App Service front end ends a request at 230 seconds, so a
    /// non-terminal job older than this belongs to a request that no longer exists.
    /// </summary>
    internal static readonly TimeSpan MaxSaveDuration = TimeSpan.FromMinutes(5);

    /// <summary><c>sprk_payload</c>'s maximum length. A longer value makes Dataverse refuse the create.</summary>
    internal const int PayloadMaxLength = 50_000;

    /// <summary>The phase an abandoned save reads as (see <see cref="ToEffectiveView"/>).</summary>
    internal const string AbandonedPhase = "Abandoned";

    /// <summary>How the save's view is stored in <c>sprk_result</c>: enums as names, so the row is readable in Dataverse.</summary>
    private static readonly JsonSerializerOptions ViewJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IProcessingJobService _jobService;
    private readonly IJobStatusService _jobStatusService;
    private readonly TimeProvider _time;
    private readonly ILogger<OfficeJobStatusService> _logger;

    public OfficeJobStatusService(
        IProcessingJobService jobService,
        IJobStatusService jobStatusService,
        TimeProvider timeProvider,
        ILogger<OfficeJobStatusService> logger)
    {
        _jobService = jobService;
        _jobStatusService = jobStatusService;
        _time = timeProvider;
        _logger = logger;
    }

    // ══ WRITE SIDE: the save path ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates the job's row, holding <paramref name="view"/> as the save's view. Throws when the row cannot be created.
    /// The caller refuses the save then, because a job with no row has no durable status and no idempotency
    /// (ADR-017: no orphaned jobs).
    /// </summary>
    /// <param name="view">The save's initial view. Its <c>JobId</c> is ignored: the row's id is the job id.</param>
    /// <param name="name">The row's <c>sprk_name</c>.</param>
    /// <param name="idempotencyKey">The save's key (task 039). Not derived here.</param>
    /// <param name="payload">The request metadata, from <see cref="BuildPayload"/>.</param>
    /// <param name="initiatedBy">The creator's <c>systemuserid</c> for <c>sprk_initiatedby</c> (task 067), when resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<Guid> CreateAsync(
        JobStatusResponse view,
        string name,
        string idempotencyKey,
        string payload,
        Guid? initiatedBy,
        CancellationToken ct)
    {
        return _jobService.CreateProcessingJobAsync(new
        {
            Name = name,
            JobType = (int)view.JobType,
            Status = ToDataverseStatus(view.Status),
            Progress = view.Progress,
            IdempotencyKey = idempotencyKey,
            CorrelationId = Guid.NewGuid().ToString(),
            Payload = payload,
            // → sprk_initiatedby (systemuser lookup). Null is SKIPPED by the create mapper, so an unresolved caller
            // leaves the field unset rather than failing the save (task 067).
            InitiatedBy = initiatedBy,
            Result = SerializeView(view),
        }, ct);
    }

    /// <summary>
    /// Records a transition the save made: the pipeline columns, as before, and the save's view, in ONE update.
    /// Best-effort, like the write it replaces: a failed status write must not fail a save whose document already
    /// exists. It is logged as an error, because the persisted view is now the only copy.
    /// </summary>
    public async Task RecordAsync(JobStatusResponse view, string? errorMessage, CancellationToken ct)
    {
        try
        {
            await _jobService.UpdateProcessingJobAsync(view.JobId, new
            {
                Status = ToDataverseStatus(view.Status),
                Progress = view.Progress,
                CurrentStage = view.CurrentPhase,
                ErrorMessage = errorMessage,
                CompletedDate = view.Status is JobStatus.Completed or JobStatus.Failed
                    ? _time.GetUtcNow().UtcDateTime
                    : (DateTime?)null,
                Result = SerializeView(view),
            }, ct);

            _logger.LogDebug(
                "ProcessingJob {JobId} recorded: {Status}, {Phase}, {Progress}%",
                view.JobId, view.Status, view.CurrentPhase, view.Progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record ProcessingJob {JobId} as {Status}/{Phase}; its status read will not reflect this transition",
                view.JobId, view.Status, view.CurrentPhase);
        }
    }

    /// <summary>
    /// What the job row's <c>sprk_payload</c> holds: the request's METADATA, never its content.
    /// </summary>
    /// <remarks>
    /// The whole request used to be serialized here, including the document's and attachments' base64 and the email
    /// body. <c>sprk_payload</c> holds at most 50,000 characters, so Dataverse refused the create for 27 of 40 saves in
    /// 60 days, and those saves ran with no job row (#1084). ADR-004 and ADR-015 forbid content in it anyway. Nothing
    /// reads <c>sprk_payload</c>; it is diagnostics. A request whose metadata alone is still too long (thousands of
    /// recipients) is recorded as its identifying fields only.
    /// </remarks>
    internal static string BuildPayload(SaveRequest request, string containerId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ContentType = request.ContentType.ToString(),
            TargetEntity = request.TargetEntity,
            ContainerId = containerId,
            Email = request.Email is null
                ? null
                : request.Email with
                {
                    Body = null,
                    Attachments = request.Email.Attachments?.Select(a => a with { ContentBase64 = null }).ToList(),
                },
            Attachment = request.Attachment is null ? null : request.Attachment with { ContentBase64 = null },
            Document = request.Document is null ? null : request.Document with { ContentBase64 = null },
            TriggerAiProcessing = request.TriggerAiProcessing
        });

        return payload.Length <= PayloadMaxLength
            ? payload
            : JsonSerializer.Serialize(new
            {
                ContentType = request.ContentType.ToString(),
                TargetEntity = request.TargetEntity,
                ContainerId = containerId,
                TriggerAiProcessing = request.TriggerAiProcessing,
                MetadataOmitted = "longer than sprk_payload holds",
            });
    }

    // ══ READ SIDE ═══════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The task 039 idempotency check: the job a repeated save is the duplicate of, or <c>null</c> when this key has no
    /// performed or in-flight operation.
    /// </summary>
    /// <remarks>
    /// The NEWEST row with the key decides (the query orders by <c>createdon</c>). A Failed, Cancelled or abandoned
    /// attempt is not a performed operation, so it cannot make a retry a duplicate (039 finding 2). A read that faults
    /// is treated as "no duplicate", as before: the save then runs, and the name-collision and content-dedup checks
    /// downstream still protect the stored file.
    /// </remarks>
    public async Task<JobStatusResponse?> FindExistingAsync(string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var row = await _jobService.GetProcessingJobByIdempotencyKeyAsync(idempotencyKey, ct);
            if (row is null)
            {
                return null;
            }

            var view = ToEffectiveView(row, _time.GetUtcNow());
            if (view.Status is JobStatus.Failed or JobStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Existing job {JobId} with this idempotency key is {Status} ({Phase}); treating the request as a new attempt",
                    view.JobId, view.Status, view.CurrentPhase);
                return null;
            }

            _logger.LogInformation(
                "Found existing job {JobId} with idempotency key, status: {Status}", view.JobId, view.Status);
            return view;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for existing job by idempotency key, treating as no duplicate");
            return null;
        }
    }

    /// <summary>
    /// The job's status, read from its row. With a <paramref name="userId"/>, ownership must be proven: a job whose
    /// creator is not that caller, or records no creator, is answered as not found (task 067, fail-closed). Without
    /// one, the raw record is returned, for <c>JobOwnershipFilter</c> and the SSE producer, which check ownership
    /// themselves or have already.
    /// </summary>
    /// <remarks>
    /// A Dataverse fault propagates, so the caller gets a 5xx, which is retryable. It used to be swallowed into "not
    /// found", which is a different answer.
    /// </remarks>
    public async Task<JobStatusResponse?> GetAsync(Guid jobId, string? userId, CancellationToken ct)
    {
        var row = await _jobService.GetProcessingJobAsync(jobId, ct);
        if (row is null)
        {
            _logger.LogDebug("Job {JobId} not found", jobId);
            return null;
        }

        var job = ToEffectiveView(row, _time.GetUtcNow());

        if (userId is not null && !string.Equals(job.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Job {JobId} ownership not proven for caller {ActualUser} (recorded creator: {ExpectedUser}); treating as not found.",
                jobId, userId, string.IsNullOrWhiteSpace(job.CreatedBy) ? "<none recorded>" : job.CreatedBy);
            return null; // Treat as not found for security
        }

        return job;
    }

    /// <summary>
    /// The job's effective state, from its row. ONE rule for both readers (notes §3).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>The save's view</b> (<c>sprk_result</c>) is authoritative when present. The workers never write it.</item>
    /// <item><b>A row written before task 060</b> has no view. It is read from the pipeline columns, and it carries no
    /// result artifact, because none was ever stored.</item>
    /// <item><b>The creator</b> is the OID the save authenticated (in the view). A row without one falls back to task
    /// 067's <c>sprk_initiatedby</c> join. A row with neither has no creator, and ownership checks refuse it.</item>
    /// <item><b>Abandoned</b>: a non-terminal job older than <see cref="MaxSaveDuration"/> belongs to a request that no
    /// longer exists. It reads as Failed and retryable, so the pane reaches a definite outcome and a retry runs.</item>
    /// </list>
    /// </remarks>
    internal static JobStatusResponse ToEffectiveView(ProcessingJobRecord row, DateTimeOffset now)
    {
        var saved = TryReadView(row.Result);
        var createdAt = saved?.CreatedAt
            ?? (row.CreatedOn is { } createdOn
                ? new DateTimeOffset(DateTime.SpecifyKind(createdOn, DateTimeKind.Utc))
                : now);

        var view = saved is not null
            ? saved with
            {
                JobId = row.Id,
                CreatedBy = !string.IsNullOrWhiteSpace(saved.CreatedBy) ? saved.CreatedBy : row.InitiatedByOid,
            }
            : FromPipelineColumns(row, createdAt);

        if (view.Status is JobStatus.Queued or JobStatus.Running && now - createdAt > MaxSaveDuration)
        {
            return view with
            {
                Status = JobStatus.Failed,
                CurrentPhase = AbandonedPhase,
                Error = new JobError
                {
                    Code = "OFFICE_INTERNAL",
                    Message = "This save did not finish. Check whether the document was saved, then try again.",
                    Retryable = true,
                },
            };
        }

        return view;
    }

    /// <summary>A row written before task 060: no save view, so the pipeline columns are all there is.</summary>
    private static JobStatusResponse FromPipelineColumns(ProcessingJobRecord row, DateTimeOffset createdAt)
    {
        var status = MapDataverseStatusToJobStatus(row.Status);
        var isCompleted = status == JobStatus.Completed;
        return new JobStatusResponse
        {
            JobId = row.Id,
            Status = status,
            JobType = MapDataverseJobTypeToJobType(row.JobType),
            Progress = isCompleted ? 100 : row.Progress ?? 0,
            CurrentPhase = isCompleted ? "Complete" : row.CurrentStage,
            CompletedPhases = new List<CompletedPhase>(),
            CreatedAt = createdAt,
            CompletedAt = row.CompletedDate is { } completed
                ? new DateTimeOffset(DateTime.SpecifyKind(completed, DateTimeKind.Utc))
                : null,
            CreatedBy = row.InitiatedByOid,
            Error = status is JobStatus.Failed && (row.ErrorCode is not null || row.ErrorMessage is not null)
                ? new JobError { Code = row.ErrorCode ?? "OFFICE_INTERNAL", Message = row.ErrorMessage ?? "Job failed" }
                : null,
        };
    }

    /// <summary>The save's view as stored in <c>sprk_result</c>. Internal so tests write exactly what production writes.</summary>
    internal static string SerializeView(JobStatusResponse view) => JsonSerializer.Serialize(view, ViewJson);

    private static JobStatusResponse? TryReadView(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JobStatusResponse>(json, ViewJson);
        }
        catch (JsonException)
        {
            // Not this writer's schema: read the row as one written before task 060.
            return null;
        }
    }

    /// <summary>The <c>sprk_status</c> option value for a job status.</summary>
    internal static int ToDataverseStatus(JobStatus status) => status switch
    {
        JobStatus.Queued => 0,
        JobStatus.Running => 1,
        JobStatus.Completed => 2,
        JobStatus.Failed => 3,
        JobStatus.Cancelled => 4,
        _ => 1
    };

    /// <summary>Maps a <c>sprk_status</c> option value to a job status.</summary>
    internal static JobStatus MapDataverseStatusToJobStatus(int? statusValue) => statusValue switch
    {
        0 => JobStatus.Queued,
        1 => JobStatus.Running,
        2 => JobStatus.Completed,
        3 => JobStatus.Failed,
        4 => JobStatus.Cancelled,
        _ => JobStatus.Queued
    };

    /// <summary>
    /// Maps a <c>sprk_jobtype</c> option value to a job type. Used only for rows written before task 060. A row written
    /// since carries its job type in the save's view, because the create writes <c>(int)JobType</c>, which does not
    /// match the option set (notes §1.5).
    /// </summary>
    internal static JobType MapDataverseJobTypeToJobType(int? jobTypeValue) => jobTypeValue switch
    {
        0 => JobType.DocumentSave,
        1 => JobType.EmailSave,
        2 => JobType.AttachmentSave,
        3 => JobType.AiProcessing,
        4 => JobType.Indexing,
        _ => JobType.DocumentSave
    };

    // ══ SSE STREAM ══════════════════════════════════════════════════════════════════════════════════════════════════
    // Moved from OfficeService by task 060; numbering and ordering rewritten by task 068 (#1086).

    /// <summary>
    /// Streams the job's status as SSE events (the <c>/api/office/jobs/{jobId}/stream</c> route), resuming after
    /// <paramref name="lastEventId"/> on reconnect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One number line.</b> Only events a worker published carry an id, and the id is that event's publisher
    /// sequence, which every instance takes from one Redis counter. The connection, the snapshot, heartbeats and the
    /// polling fallback carry no id, so they never move the client's <c>Last-Event-ID</c>. The stream used to number
    /// those with a counter of its own and mix in the publisher's numbers, so ids repeated and a reconnect could drop
    /// live events.
    /// </para>
    /// <para>
    /// <b>Reconnect.</b> Every published event after <paramref name="lastEventId"/> is sent once; none at or before it is
    /// sent again. Redis pub/sub keeps no history, so the state between them arrives as the unnumbered snapshot.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<byte[]> StreamAsync(
        Guid jobId,
        string? lastEventId,
        CancellationToken cancellationToken = default)
    {
        // Use Channel to produce events - avoids yield-inside-try-catch limitation
        var channel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        // Start the producer task
        _ = ProduceJobStatusEventsAsync(jobId, lastEventId, channel.Writer, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Produces SSE events for job status streaming and writes them to the channel.
    /// </summary>
    private async Task ProduceJobStatusEventsAsync(
        Guid jobId,
        string? lastEventId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        CancellationToken cancellationToken)
    {
        using var subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? subscription = null;

        try
        {
            _logger.LogInformation(
                "SSE stream started for job {JobId}, LastEventId={LastEventId}",
                jobId,
                lastEventId ?? "none");

            // The last publisher event the client has (0 on a first connection).
            long lastSeen = 0;
            if (SseHelper.TryParseLastEventId(lastEventId, out var parsedJobId, out var parsedSequence) && parsedJobId == jobId)
            {
                lastSeen = parsedSequence;
                _logger.LogInformation(
                    "SSE reconnection detected for job {JobId}, resuming after sequence {Sequence}",
                    jobId,
                    lastSeen);
            }

            var heartbeatInterval = TimeSpan.FromSeconds(15); // Per spec.md

            // Subscribe BEFORE reading the snapshot: pub/sub keeps no history, so an event published between the read
            // and the subscription would never be sent, and if it were the terminal one the stream would wait forever.
            System.Threading.Channels.Channel<JobStatusUpdate>? live = null;
            if (await _jobStatusService.IsHealthyAsync(cancellationToken))
            {
                live = System.Threading.Channels.Channel.CreateUnbounded<JobStatusUpdate>(
                    new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
                var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                subscription = ReceiveLiveUpdatesAsync(jobId, live.Writer, subscribed, subscriptionCts.Token);
                await subscribed.Task.WaitAsync(cancellationToken);
            }

            await writer.WriteAsync(SseHelper.FormatConnected(jobId), cancellationToken);

            // The snapshot: the state pub/sub cannot replay. Unnumbered.
            var currentStatus = await GetAsync(jobId, userId: null, cancellationToken);
            if (currentStatus is null)
            {
                // Job not found - send error and close
                _logger.LogWarning("SSE stream: Job {JobId} not found", jobId);
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_008",
                    "Job not found or has expired",
                    jobId.ToString()), cancellationToken);
                return;
            }

            await writer.WriteAsync(SseHelper.FormatProgress(
                currentStatus.Progress,
                currentStatus.CurrentPhase,
                eventId: null), cancellationToken);

            foreach (var phase in currentStatus.CompletedPhases ?? [])
            {
                await writer.WriteAsync(SseHelper.FormatStageUpdate(
                    phase.Name,
                    "Completed",
                    phase.CompletedAt,
                    eventId: null), cancellationToken);
            }

            if (currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            {
                _logger.LogInformation("SSE stream: Job {JobId} already {Status}", jobId, currentStatus.Status);
                await writer.WriteAsync(FormatTerminal(jobId, currentStatus, eventId: null), cancellationToken);
                return;
            }

            if (live is not null)
            {
                _logger.LogInformation(
                    "SSE stream using Redis pub/sub for job {JobId}",
                    jobId);

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = SendHeartbeatsAsync(jobId, writer, heartbeatInterval, heartbeatCts.Token);

                try
                {
                    await foreach (var update in live.Reader.ReadAllAsync(cancellationToken))
                    {
                        // The client already has every event up to its Last-Event-ID. If that includes the outcome,
                        // there is nothing left to send.
                        if (update.Sequence <= lastSeen)
                        {
                            _logger.LogDebug(
                                "SSE stream: Skipping update with sequence {Sequence} (already sent) for job {JobId}",
                                update.Sequence,
                                jobId);
                            if (IsTerminal(update.UpdateType))
                            {
                                return;
                            }

                            continue;
                        }

                        var eventId = SseHelper.GenerateEventId(jobId, update.Sequence);

                        // Format and send the SSE event based on update type
                        var sseEvent = update.UpdateType switch
                        {
                            JobStatusUpdateType.Progress => SseHelper.FormatProgress(
                                update.Progress,
                                update.CurrentPhase,
                                eventId),

                            JobStatusUpdateType.StageComplete when update.CompletedPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CompletedPhase.Name,
                                    "Completed",
                                    update.CompletedPhase.CompletedAt,
                                    eventId),

                            JobStatusUpdateType.StageStarted when update.CurrentPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CurrentPhase,
                                    "Running",
                                    update.Timestamp,
                                    eventId),

                            JobStatusUpdateType.JobCompleted => SseHelper.FormatJobComplete(
                                jobId,
                                update.Result?.Artifact?.Id,
                                update.Result?.Artifact?.WebUrl,
                                eventId),

                            JobStatusUpdateType.JobFailed or JobStatusUpdateType.JobCancelled =>
                                SseHelper.FormatJobFailed(
                                    jobId,
                                    update.Error?.Code ?? "OFFICE_INTERNAL",
                                    update.Error?.Message ?? "Job failed",
                                    update.Error?.Retryable ?? false,
                                    eventId),

                            _ => SseHelper.FormatProgress(update.Progress, update.CurrentPhase, eventId)
                        };

                        await writer.WriteAsync(sseEvent, cancellationToken);

                        _logger.LogDebug(
                            "SSE event sent for job {JobId}: Type={UpdateType}, Progress={Progress}",
                            jobId,
                            update.UpdateType,
                            update.Progress);

                        // Terminal states end the stream
                        if (IsTerminal(update.UpdateType))
                        {
                            _logger.LogInformation(
                                "SSE stream ending for job {JobId} due to terminal state {State}",
                                jobId,
                                update.UpdateType);
                            return;
                        }
                    }
                }
                finally
                {
                    // Cancel heartbeat task
                    heartbeatCts.Cancel();
                    try { await heartbeatTask; } catch (OperationCanceledException) { }
                }

                // The subscription ended without an outcome (Redis dropped it, or it failed). Poll the job's row rather
                // than closing the stream with no outcome; first check whether the outcome arrived in the meantime.
                _logger.LogWarning(
                    "SSE stream: the subscription for job {JobId} ended without an outcome; polling instead",
                    jobId);
                currentStatus = await GetAsync(jobId, userId: null, cancellationToken);
                if (currentStatus is null)
                {
                    _logger.LogWarning("SSE stream: Job {JobId} was deleted during streaming", jobId);
                    await writer.WriteAsync(SseHelper.FormatError(
                        "OFFICE_008",
                        "Job no longer exists",
                        jobId.ToString()), cancellationToken);
                    return;
                }

                if (currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                {
                    await writer.WriteAsync(FormatTerminal(jobId, currentStatus, eventId: null), cancellationToken);
                    return;
                }
            }
            else
            {
                _logger.LogWarning(
                    "SSE stream falling back to polling for job {JobId} (Redis unavailable)",
                    jobId);
            }

            // Polling: Redis is unavailable, or the subscription ended early. No publisher sequence exists here, so
            // nothing is numbered.
            {
                var fallbackPollInterval = TimeSpan.FromMilliseconds(500);
                var previousStatus = currentStatus.Status;
                var previousProgress = currentStatus.Progress;
                var previousPhase = currentStatus.CurrentPhase;
                var previousCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                var fallbackLastHeartbeat = DateTimeOffset.UtcNow;

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check if heartbeat is needed
                    var now = DateTimeOffset.UtcNow;
                    if (now - fallbackLastHeartbeat >= heartbeatInterval)
                    {
                        await writer.WriteAsync(SseHelper.FormatHeartbeat(now), cancellationToken);
                        fallbackLastHeartbeat = now;
                        _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
                    }

                    await Task.Delay(fallbackPollInterval, cancellationToken);

                    currentStatus = await GetAsync(jobId, userId: null, cancellationToken);
                    if (currentStatus is null)
                    {
                        _logger.LogWarning("SSE stream: Job {JobId} was deleted during streaming", jobId);
                        await writer.WriteAsync(SseHelper.FormatError(
                            "OFFICE_008",
                            "Job no longer exists",
                            jobId.ToString()), cancellationToken);
                        return;
                    }

                    // Send progress updates
                    if (currentStatus.Progress != previousProgress)
                    {
                        await writer.WriteAsync(SseHelper.FormatProgress(
                            currentStatus.Progress,
                            currentStatus.CurrentPhase,
                            eventId: null), cancellationToken);
                        previousProgress = currentStatus.Progress;
                    }

                    // Send completed phase updates
                    var currentCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                    if (currentCompletedPhaseCount > previousCompletedPhaseCount)
                    {
                        for (var i = previousCompletedPhaseCount; i < currentCompletedPhaseCount; i++)
                        {
                            var phase = currentStatus.CompletedPhases![i];
                            await writer.WriteAsync(SseHelper.FormatStageUpdate(
                                phase.Name,
                                "Completed",
                                phase.CompletedAt,
                                eventId: null), cancellationToken);
                        }
                        previousCompletedPhaseCount = currentCompletedPhaseCount;
                    }

                    // Send current phase change
                    if (currentStatus.CurrentPhase != previousPhase && !string.IsNullOrEmpty(currentStatus.CurrentPhase))
                    {
                        await writer.WriteAsync(SseHelper.FormatStageUpdate(
                            currentStatus.CurrentPhase,
                            "Running",
                            DateTimeOffset.UtcNow,
                            eventId: null), cancellationToken);
                        previousPhase = currentStatus.CurrentPhase;
                    }

                    // Check for terminal state
                    if (currentStatus.Status != previousStatus &&
                        currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    {
                        await writer.WriteAsync(FormatTerminal(jobId, currentStatus, eventId: null), cancellationToken);
                        return;
                    }
                    previousStatus = currentStatus.Status;
                }
            }

            _logger.LogInformation(
                "SSE stream ended for job {JobId} (cancellation requested)",
                jobId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "SSE stream cancelled for job {JobId} (client disconnected)",
                jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SSE stream error for job {JobId}",
                jobId);

            // Send terminal error event per ADR-019
            try
            {
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_INTERNAL",
                    "Internal server error during job status streaming",
                    jobId.ToString()), CancellationToken.None);
            }
            catch
            {
                // Ignore errors when writing final error event
            }
        }
        finally
        {
            subscriptionCts.Cancel();
            if (subscription is not null)
            {
                await subscription;
            }

            writer.Complete();
        }
    }

    /// <summary>
    /// Receives the job's published updates into <paramref name="live"/>, signalling <paramref name="subscribed"/> once
    /// the subscription is live (or has ended without becoming live, so the stream never waits on it forever).
    /// </summary>
    private async Task ReceiveLiveUpdatesAsync(
        Guid jobId,
        System.Threading.Channels.ChannelWriter<JobStatusUpdate> live,
        TaskCompletionSource subscribed,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _jobStatusService.SubscribeToJobAsync(
                jobId, onSubscribed: () => subscribed.TrySetResult(), cancellationToken))
            {
                await live.WriteAsync(update, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The stream ended.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSE stream: the subscription for job {JobId} failed", jobId);
        }
        finally
        {
            subscribed.TrySetResult();
            live.TryComplete();
        }
    }

    private static bool IsTerminal(JobStatusUpdateType type) =>
        type is JobStatusUpdateType.JobCompleted or JobStatusUpdateType.JobFailed or JobStatusUpdateType.JobCancelled;

    private static byte[] FormatTerminal(Guid jobId, JobStatusResponse status, string? eventId) =>
        status.Status == JobStatus.Completed
            ? SseHelper.FormatJobComplete(
                jobId,
                status.Result?.Artifact?.Id,
                status.Result?.Artifact?.WebUrl,
                eventId)
            : SseHelper.FormatJobFailed(
                jobId,
                status.Error?.Code ?? "OFFICE_INTERNAL",
                status.Error?.Message ?? $"Job {status.Status.ToString().ToLowerInvariant()}",
                status.Error?.Retryable ?? false,
                eventId);

    /// <summary>
    /// Sends heartbeat events at regular intervals to keep the SSE connection alive. Unnumbered: a heartbeat is not a
    /// published event and must not move the client's <c>Last-Event-ID</c>.
    /// </summary>
    private async Task SendHeartbeatsAsync(
        Guid jobId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);
                await writer.WriteAsync(SseHelper.FormatHeartbeat(DateTimeOffset.UtcNow), cancellationToken);
                _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error sending heartbeat for job {JobId}",
                jobId);
        }
    }
}
