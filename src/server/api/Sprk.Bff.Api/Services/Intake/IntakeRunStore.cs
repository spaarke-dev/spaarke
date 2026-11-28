using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Intake.Harnesses;

namespace Sprk.Bff.Api.Services.Intake;

/// <summary>
/// The intake run's reads and conditional writes (spaarke-intake-pipeline-r1 task 013, FR-07): the run
/// (<c>sprk_processingjob</c>, job type 7) and the request stage on <c>sprk_servicerequest</c>. The one writer of the run
/// state, the request's <c>sprk_intakestage</c> and <c>sprk_currentrun</c> (ADR-002, with the approval endpoint, task 017).
/// </summary>
/// <remarks>
/// <para><b>Atomic claim (ADR-004 A1 §4).</b> A delivery may write a run only while it holds the run's claim
/// (<see cref="IntakeRunClaim"/> in the stored state). The claim is taken by a write conditional on the row version the
/// delivery read (<c>If-Match</c>, <see cref="IGenericEntityService.UpdateIfRowVersionMatchesAsync"/>), so of two
/// deliveries that read the same version exactly one claims it; the other's write is refused by Dataverse. It is not
/// <c>IIdempotencyService</c> (check-then-set, fails open, #984). The owner is one delivery (<see cref="ClaimOwner"/>: the
/// job id plus a per-delivery nonce), so two deliveries of the same message never share a fence. A claim last refreshed
/// less than <see cref="ClaimLease"/> ago by another job is refused; a later delivery of the SAME job takes it over once it
/// is <see cref="OwnerTakeoverAge"/> old (the invoice lock's rule, task 068 #1086: a duplicate send arrives within seconds, a
/// genuine redelivery only after the message lock expired). Every fenced write refreshes the claim's time, and while a delivery
/// waits on a long step it refreshes it every <see cref="ClaimHeartbeat"/> (<see cref="RefreshClaimAsync"/>, review F1), so a
/// same-job takeover happens only once the holder stopped heartbeating.</para>
/// <para><b>Every later write is fenced.</b> <see cref="WriteRunAsync"/> re-reads the row, refuses
/// (<see cref="IntakeClaimLostException"/>) when the stored claim is no longer this delivery's, applies the change to the
/// state AS STORED (so a step result the engine completion recorded meanwhile is kept) and writes conditionally on the
/// version it just read. A takeover between the read and the write fails the write; a write by the engine completion
/// (which does not claim) is retried on the new version.</para>
/// <para><b>Lookups are <see cref="EntityReference"/>s</b> (<c>sprk_servicerequest</c>, <c>sprk_outputdocument</c>,
/// <c>sprk_currentrun</c>); a bare Guid is refused by Dataverse (task 011 finding 1).</para>
/// <para><b>Intake rows only.</b> A request whose <c>sprk_intakestage</c> is null is not an intake request (ontology's
/// outbound inquiry rows) and is never processed or written (notes/decisions.md 2026-11-27).</para>
/// <para>§11: <i>Existing</i> - <c>IProcessingJobService</c> (reflection mapper, no conditional write) and
/// <c>OfficeJobStatusService</c> (Office rows). <i>Extension</i> - neither can write conditionally on a row version, and
/// the Office service must never see intake rows (FR-06b). <i>Cost of doing nothing</i> - no atomic claim, so two
/// deliveries of one run could both run its AI and create its outputs twice (FR-07b).</para>
/// </remarks>
public class IntakeRunStore
{
    internal const string RunEntity = "sprk_processingjob";
    internal const string RequestEntity = "sprk_servicerequest";
    internal const string DocumentEntity = "sprk_document";

    internal const string RunRequest = "sprk_servicerequest";
    internal const string RunJobType = "sprk_jobtype";
    internal const string RunStatus = "sprk_status";
    internal const string RunCurrentStage = "sprk_currentstage";
    internal const string RunStages = "sprk_stages";
    internal const string RunState = "sprk_stagestatus";
    internal const string RunStarted = "sprk_starteddate";
    internal const string RunCompleted = "sprk_completeddate";
    internal const string RunErrorCode = "sprk_errorcode";
    internal const string RunErrorMessage = "sprk_errormessage";
    internal const string RunCorrelation = "sprk_correlationid";
    internal const string RunHarnessKey = "sprk_harnesskey";
    internal const string RunEngineExecution = "sprk_engineexecutionid";
    internal const string RunOutputDocument = "sprk_outputdocument";
    internal const string RunIdempotencyKey = "sprk_idempotencykey";
    internal const string RunName = "sprk_name";
    internal const string RunInitiatedBy = "sprk_initiatedby";
    internal const string RowVersionColumn = "versionnumber";

    internal const string RequestIntakeStage = "sprk_intakestage";
    internal const string RequestType = "sprk_requesttype";
    internal const string RequestChannel = "sprk_intakechannel";
    internal const string RequestCurrentRun = "sprk_currentrun";

    internal const string DocumentRequest = "sprk_relatedservicerequest";

    /// <summary><c>sprk_stagestatus</c> is Memo(10 000).</summary>
    internal const int MaxStateLength = 10_000;

    /// <summary>Longer than one delivery's worst case (two AI calls and the writes), shorter than the processor's retry window.</summary>
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(15);

    /// <summary>How old a claim of the same job must be before a later delivery of that job takes it over (the invoice lock's value, #1086).</summary>
    internal static readonly TimeSpan OwnerTakeoverAge = TimeSpan.FromMinutes(1);

    /// <summary>The shortest claim heartbeat (<see cref="ClaimHeartbeat"/>): one fenced write per beat, so never a tight loop.</summary>
    internal static readonly TimeSpan MinClaimHeartbeat = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest claim heartbeat: a third of <see cref="OwnerTakeoverAge"/>, so a live holder's claim is never older than the
    /// takeover age even when two refreshes in a row fail (review F1).
    /// </summary>
    internal static readonly TimeSpan MaxClaimHeartbeat = OwnerTakeoverAge / 3;

    /// <summary>
    /// The heartbeat interval for <c>IntakePipelineOptions.ClaimHeartbeatSeconds</c>: not positive → <see cref="MaxClaimHeartbeat"/>
    /// (the default, 20 s); otherwise clamped to [<see cref="MinClaimHeartbeat"/>, <see cref="MaxClaimHeartbeat"/>].
    /// </summary>
    internal static TimeSpan ClaimHeartbeat(int seconds)
    {
        if (seconds <= 0)
        {
            return MaxClaimHeartbeat;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        return interval < MinClaimHeartbeat ? MinClaimHeartbeat : interval > MaxClaimHeartbeat ? MaxClaimHeartbeat : interval;
    }

    /// <summary>The claim owner of one delivery of <paramref name="jobId"/>: <c>{jobId:N}.{nonce:N}</c>.</summary>
    public static string ClaimOwner(Guid jobId) => $"{jobId:N}.{Guid.NewGuid():N}";

    private const int MaxConcurrencyRetries = 3;

    private static readonly string[] RunColumns =
    [
        RunRequest, RunJobType, RunStatus, RunCurrentStage, RunState, RunCorrelation, RunHarnessKey, RunEngineExecution, RowVersionColumn,
    ];

    private static readonly string[] RequestColumns = [RequestIntakeStage, RequestType, RequestChannel, RequestCurrentRun, RowVersionColumn];

    private readonly IGenericEntityService _entities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly TimeProvider _time;
    private readonly ILogger<IntakeRunStore> _logger;

    public IntakeRunStore(IGenericEntityService entities, IRecordOwnershipResolver ownership, TimeProvider time, ILogger<IntakeRunStore> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The deterministic run id of one pass over a request: the same door key always names the same run (FR-07b).</summary>
    public static Guid RunIdFor(Guid requestId, string passKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"intake-run|{requestId:N}|{passKey}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (name-based) in the Guid byte layout
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }

    /// <summary>
    /// Creates the run row for a door (Front Door 021, Console 022, email 040, Copilot 044): job type Intake run, status
    /// Pending, the request as an <see cref="EntityReference"/>, and the content document ids in the state. Idempotent:
    /// the id is <see cref="RunIdFor"/>, and a row that already exists for the same request is returned, not duplicated.
    /// <para><b>Owner (unified-access-control-r2 task 146, write-path invariant I-2).</b> The run is a child of its request:
    /// its owner team comes from <see cref="IRecordOwnershipResolver"/> over the row's lookups (secure-if-any, so a request
    /// filed under a secure record gives its run the named Secure team), and a refusal creates nothing.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A row with that id exists but belongs to another request or is not an intake run.</exception>
    /// <exception cref="IntakeRunOwnerRefusedException">No owner team resolves for the request; no row was written.</exception>
    public virtual async Task<Guid> CreateRunAsync(
        Guid requestId,
        string passKey,
        IReadOnlyList<Guid> contentDocumentIds,
        string correlationId,
        Guid? initiatedBy,
        CancellationToken ct)
    {
        var runId = RunIdFor(requestId, passKey);
        var state = new IntakeRunState { ContentDocumentIds = contentDocumentIds.Distinct().ToList() };
        var entity = new Entity(RunEntity, runId)
        {
            [RunName] = $"Intake run {runId:N}",
            [RunJobType] = new OptionSetValue((int)ProcessingJobType.IntakeRun),
            [RunStatus] = new OptionSetValue((int)ProcessingJobStatus.Pending),
            [RunRequest] = new EntityReference(RequestEntity, requestId),
            [RunCorrelation] = correlationId,
            [RunIdempotencyKey] = $"intake-run-{runId:N}",
            [RunState] = Serialize(state),
        };
        if (initiatedBy is { } user && user != Guid.Empty)
        {
            entity[RunInitiatedBy] = new EntityReference("systemuser", user);
        }

        var owner = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForChild(entity), ct).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            _logger.LogWarning("Intake run {RunId} not created: no owner resolved for request {RequestId} ({ReasonCode})",
                runId, requestId, owner.RefusalCode);
            throw new IntakeRunOwnerRefusedException(runId, owner.RefusalCode ?? "record_owner_unresolved");
        }

        entity["ownerid"] = new EntityReference("team", owner.OwningTeamId!.Value);
        owner.StampCreatorOn(entity);

        try
        {
            await _entities.CreateAsync(entity, ct).ConfigureAwait(false);
            return runId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A duplicate create (the door retried) fails on the primary key: the run already exists. Anything else rethrows.
            var existing = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            if (existing.RequestId != requestId || existing.JobType != (int)ProcessingJobType.IntakeRun)
            {
                throw new InvalidOperationException($"Run {runId} exists but is not this request's intake run.");
            }

            return runId;
        }
    }

    /// <summary>Reads the run; null when it does not exist.</summary>
    public virtual async Task<IntakeRunSnapshot?> ReadRunAsync(Guid runId, CancellationToken ct)
    {
        var row = await RetrieveOrNullAsync(RunEntity, runId, RunColumns, ct).ConfigureAwait(false);
        return row is null ? null : ToRunSnapshot(row);
    }

    /// <summary>Reads the request; null when it does not exist.</summary>
    public virtual async Task<IntakeRequestSnapshot?> ReadRequestAsync(Guid requestId, CancellationToken ct)
    {
        var row = await RetrieveOrNullAsync(RequestEntity, requestId, RequestColumns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new IntakeRequestSnapshot
        {
            RequestId = requestId,
            IntakeStage = row.GetAttributeValue<OptionSetValue>(RequestIntakeStage)?.Value,
            RequestType = row.GetAttributeValue<OptionSetValue>(RequestType)?.Value,
            Channel = row.GetAttributeValue<OptionSetValue>(RequestChannel)?.Value,
            CurrentRunId = row.GetAttributeValue<EntityReference>(RequestCurrentRun)?.Id,
            RowVersion = row.RowVersion
                ?? (row.Contains(RowVersionColumn) ? Convert.ToString(row[RowVersionColumn], CultureInfo.InvariantCulture) : null),
        };
    }

    /// <summary>True when the document exists, is active and is filed on the request (<c>sprk_relatedservicerequest</c>).</summary>
    public virtual async Task<bool> IsRequestContentAsync(Guid requestId, Guid documentId, CancellationToken ct)
    {
        var row = await RetrieveOrNullAsync(DocumentEntity, documentId, ["statecode", DocumentRequest], ct).ConfigureAwait(false);
        return row is not null
            && (row.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0
            && row.GetAttributeValue<EntityReference>(DocumentRequest)?.Id == requestId;
    }

    /// <summary>
    /// Takes the run's claim for <paramref name="owner"/> (step 0). Returns the stored state with the claim, or null when
    /// another live delivery holds it or wrote the row since <paramref name="run"/> was read (nothing written).
    /// </summary>
    public virtual async Task<IntakeRunState?> TryClaimAsync(IntakeRunSnapshot run, string owner, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var now = _time.GetUtcNow();
        if (run.State.Claim is { } held && !CanTakeOver(held, owner, now))
        {
            _logger.LogInformation("Intake run {RunId}: claimed by another delivery; not taken", run.RunId);
            return null;
        }

        if (run.RowVersion is null)
        {
            // A conditional write needs the version it is conditional on: never claim unconditionally (fail closed).
            _logger.LogWarning("Intake run {RunId}: no row version was read; the claim cannot be taken atomically", run.RunId);
            return null;
        }

        var state = run.State;
        state.Claim = new IntakeRunClaim(owner, now);
        var write = new Dictionary<string, object>();
        if (state.Held is not null)
        {
            // A held run that is delivered again (replay, re-drive) is being worked on: its attention text is cleared. If
            // the cause is still there, the delivery holds it again with fresh text.
            state.Held = null;
            write[RunErrorCode] = DBNull.Value;
            write[RunErrorMessage] = DBNull.Value;
        }

        write[RunState] = Serialize(state);
        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, run.RunId, run.RowVersion, write, ct).ConfigureAwait(false);
            return state;
        }
        catch (System.Data.DBConcurrencyException)
        {
            _logger.LogInformation("Intake run {RunId}: changed since it was read; another delivery claimed it", run.RunId);
            return null;
        }
    }

    /// <summary>
    /// One fenced run write: re-read, refuse unless <paramref name="owner"/> still holds the claim, apply
    /// <paramref name="mutate"/> to the stored state, write the state and <paramref name="fields"/> conditionally.
    /// <paramref name="release"/> clears the claim in the same write (the last write of a delivery).
    /// </summary>
    /// <returns>The state as written.</returns>
    /// <exception cref="IntakeClaimLostException">The run is gone or another delivery holds it; nothing was written.</exception>
    public virtual async Task<IntakeRunState> WriteRunAsync(
        Guid runId,
        string owner,
        Action<IntakeRunState>? mutate,
        IReadOnlyDictionary<string, object>? fields,
        bool release,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null || run.State.Claim?.Owner != owner || run.RowVersion is null)
            {
                throw new IntakeClaimLostException(runId);
            }

            var state = run.State;
            mutate?.Invoke(state);

            // Released in the last write; otherwise refreshed, so a takeover only happens after the holder fell silent.
            state.Claim = release ? null : new IntakeRunClaim(owner, _time.GetUtcNow());

            var write = new Dictionary<string, object> { [RunState] = Serialize(state) };
            if (fields is not null)
            {
                foreach (var (column, value) in fields)
                {
                    write[column] = value;
                }
            }

            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, runId, run.RowVersion, write, ct).ConfigureAwait(false);
                return state;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Another writer (the engine completion) wrote in between; a takeover is caught by the claim check on the re-read.
            }
            catch (System.Data.DBConcurrencyException)
            {
                throw new IntakeClaimLostException(runId);
            }
        }
    }

    /// <summary>
    /// The heartbeat (review F1): refreshes <paramref name="owner"/>'s claim time and nothing else, through the same fenced,
    /// row-version-conditional write as every other run write (<see cref="WriteRunAsync"/>).
    /// </summary>
    /// <exception cref="IntakeClaimLostException">Another delivery took the run over; nothing was written.</exception>
    public virtual Task RefreshClaimAsync(Guid runId, string owner, CancellationToken ct) =>
        WriteRunAsync(runId, owner, mutate: null, fields: null, release: false, ct);

    /// <summary>Clears this owner's claim, if it still holds it. Best effort: an unreleased claim expires after <see cref="ClaimLease"/>.</summary>
    public virtual async Task ReleaseClaimAsync(Guid runId, string owner, CancellationToken ct)
    {
        var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
        if (run?.State.Claim?.Owner != owner || run.RowVersion is null)
        {
            return;
        }

        run.State.Claim = null;
        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, runId, run.RowVersion,
                new Dictionary<string, object> { [RunState] = Serialize(run.State) }, ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            // Someone wrote since the read; the claim expires on its own.
        }
    }

    /// <summary>
    /// The engine completion's write (task 014): records the result of the external step the run is awaiting and clears
    /// the wait, conditionally on the version read. The caller then enqueues an <c>IntakeRun</c> job to continue the run.
    /// </summary>
    /// <param name="expectedExecutionId">
    /// When set, the run's <see cref="IntakeRunSnapshot.EngineExecutionId"/> must equal it, checked on the same read the write is
    /// conditional on: a completion of an older dispatch attempt never records its result against a newer one (task 014).
    /// </param>
    /// <returns>
    /// False (nothing written) when the run is gone, terminal, or not awaiting <paramref name="step"/> as a DISPATCHED step
    /// (<see cref="IntakeAwaitingStep.Dispatched"/>), the step already has a result, or the execution id differs.
    /// </returns>
    /// <remarks>
    /// The orchestrator writes <c>Awaiting = {step, Dispatched: false}</c> before it sends, and the dispatched flag and the execution
    /// id in ONE write after it (review F1). Until then the execution id still names the previous dispatch, so a redelivered
    /// completion of that previous dispatch must not be recorded against the step now being sent: only a run awaiting a dispatched
    /// <paramref name="step"/> accepts a result, and the execution id then belongs to that dispatch.
    /// </remarks>
    public virtual async Task<bool> RecordExternalStepResultAsync(
        Guid runId, string step, IntakeHarnessResultReference result, CancellationToken ct, string? expectedExecutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        ArgumentNullException.ThrowIfNull(result);
        for (var attempt = 1; ; attempt++)
        {
            var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null || run.RowVersion is null || IsTerminalStatus(run.Status)
                || run.State.Awaiting is not { Dispatched: true } awaiting || awaiting.Step != step || run.State.Steps.ContainsKey(step)
                || (expectedExecutionId is not null && run.EngineExecutionId != expectedExecutionId))
            {
                return false;
            }

            run.State.Steps[step] = result;
            run.State.Awaiting = null;
            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, runId, run.RowVersion,
                    new Dictionary<string, object> { [RunState] = Serialize(run.State) }, ct).ConfigureAwait(false);
                return true;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
            }
        }
    }

    /// <summary>
    /// Stops the run's wait on an external step without a result (task 014): <see cref="IntakeExternalStopKind.Hold"/> (a failed
    /// <c>check_outputs</c>, a plan that does not fit, the hard timeout: status stays In Progress, admin-attention text, nothing
    /// continues, an administrator re-drives it) or <see cref="IntakeExternalStopKind.Fail"/> (an unreadable draft: terminal Failed
    /// with the code, the request moved to Needs triage so a person sees it).
    /// </summary>
    /// <remarks>
    /// <para><b>Bound to its dispatch (review F1).</b> Applies only while the run awaits <paramref name="step"/> as a DISPATCHED step
    /// whose execution id is <paramref name="executionId"/> (the engine request's key), and is not already held. A completion or
    /// timeout of an older attempt, of a step the run has left, of a step whose dispatch is not recorded yet (the orchestrator
    /// marks it awaiting before the send and records the dispatch after), or of a run an administrator is looking at, changes
    /// nothing. A run a live delivery holds is <see cref="IntakeExternalWriteResult.Busy"/> (the caller retries), so this never
    /// writes under the orchestrator's claim. The step stays unrecorded and the wait is cleared: a re-driven run dispatches the
    /// step again (under a new attempt).</para>
    /// <para><b>Order.</b> The first write is conditional on the version read and is the fence: it clears the wait and puts the run
    /// on hold, so a result recorded since the read makes it fail and the decision is taken again on the new row. Only after it
    /// succeeded is a <see cref="IntakeExternalStopKind.Fail"/> carried on: the request moves to Needs triage, then the terminal
    /// status is written LAST. A crash in between leaves the run held (visible to an administrator) and the request in triage,
    /// never a request in triage whose run is still waiting.</para>
    /// </remarks>
    /// <param name="step">The awaited step the dispatch belongs to (an engine step is named by its operation).</param>
    /// <param name="acceptUnrecordedExecution">
    /// True for the timeout: a dispatch whose execution id was never recorded still times out, but only while the run's execution
    /// column is EMPTY; a column naming another dispatch is that dispatch's wait, not this one's.
    /// </param>
    public virtual async Task<IntakeExternalWriteResult> StopAwaitedStepAsync(
        Guid runId, string step, string executionId, bool acceptUnrecordedExecution, IntakeExternalStop stop, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(stop);
        for (var attempt = 1; ; attempt++)
        {
            var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null || run.RowVersion is null || IsTerminalStatus(run.Status) || run.State.Held is not null
                || !AwaitsDispatch(run, step, executionId, acceptUnrecordedExecution))
            {
                return IntakeExternalWriteResult.NotApplicable;
            }

            var now = _time.GetUtcNow();
            if (run.State.Claim is { } claim && now - claim.At < ClaimLease)
            {
                return IntakeExternalWriteResult.Busy;
            }

            run.State.Awaiting = null;
            run.State.Claim = null;
            run.State.Held = new IntakeRunHold(stop.Stage.ToString(), stop.Code, now);
            var fields = new Dictionary<string, object>
            {
                [RunCurrentStage] = stop.Stage.ToString(),
                [RunErrorCode] = stop.Code,
                [RunErrorMessage] = stop.Message,
                [RunState] = Serialize(run.State),
            };
            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, runId, run.RowVersion, fields, ct).ConfigureAwait(false);
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Changed since the read (a completion recorded, a claim taken): decide again on the new row. Nothing else was written.
                continue;
            }

            if (stop.Kind == IntakeExternalStopKind.Fail)
            {
                await FailHeldRunAsync(run, stop, ct).ConfigureAwait(false);
            }

            return IntakeExternalWriteResult.Written;
        }
    }

    /// <summary>
    /// The run awaits <paramref name="step"/> as a dispatched step whose execution id is <paramref name="executionId"/>; or, when
    /// <paramref name="acceptUnrecordedExecution"/>, awaits <paramref name="step"/> with no execution id recorded at all.
    /// </summary>
    private static bool AwaitsDispatch(IntakeRunSnapshot run, string step, string executionId, bool acceptUnrecordedExecution)
    {
        if (run.State.Awaiting is not { } awaiting || awaiting.Step != step || run.State.Steps.ContainsKey(step))
        {
            return false;
        }

        return (awaiting.Dispatched && run.EngineExecutionId == executionId)
            || (acceptUnrecordedExecution && string.IsNullOrEmpty(run.EngineExecutionId));
    }

    /// <summary>
    /// The second half of a <see cref="IntakeExternalStopKind.Fail"/>, after the fence write held the run: the request moves to Needs
    /// triage, then the run's terminal status is written last. Skipped (the hold stands) when the held run was touched since.
    /// </summary>
    private async Task FailHeldRunAsync(IntakeRunSnapshot fenced, IntakeExternalStop stop, CancellationToken ct)
    {
        if (fenced.RequestId is { } requestId)
        {
            await WriteRequestStageAsync(requestId, fenced.RunId, IntakeRequestStages.NeedsTriage, makeCurrentRun: false, ct).ConfigureAwait(false);
        }

        for (var attempt = 1; ; attempt++)
        {
            var held = await ReadRunAsync(fenced.RunId, ct).ConfigureAwait(false);
            if (held is null || held.RowVersion is null || IsTerminalStatus(held.Status) || held.State.Claim is not null
                || held.State.Awaiting is not null || held.State.Held?.Code != stop.Code)
            {
                // A re-drive claimed the run (or a result was recorded) since the fence: the hold stands, the run is not failed over it.
                _logger.LogInformation("Intake run {RunId}: held for {Code}, then changed before it could be failed; the hold stands", fenced.RunId, stop.Code);
                return;
            }

            held.State.Held = null;
            var terminal = new Dictionary<string, object>
            {
                [RunStatus] = new OptionSetValue((int)ProcessingJobStatus.Failed),
                [RunCompleted] = _time.GetUtcNow().UtcDateTime,
                [RunState] = Serialize(held.State),
            };
            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, fenced.RunId, held.RowVersion, terminal, ct).ConfigureAwait(false);
                return;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
            }
        }
    }

    /// <summary>
    /// Records that the continuation of an engine completion was enqueued (<paramref name="marker"/> → <paramref name="key"/>), so a
    /// duplicate of that completion enqueues nothing. Written after the send: a failure here costs one extra, harmless
    /// <c>IntakeRun</c> job on the duplicate, never a lost one.
    /// </summary>
    public virtual async Task MarkContinuationEnqueuedAsync(Guid runId, string marker, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        for (var attempt = 1; ; attempt++)
        {
            var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null || run.RowVersion is null || IsTerminalStatus(run.Status) || run.State.Markers.ContainsKey(marker))
            {
                return;
            }

            run.State.Markers[marker] = key;
            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RunEntity, runId, run.RowVersion,
                    new Dictionary<string, object> { [RunState] = Serialize(run.State) }, ct).ConfigureAwait(false);
                return;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
            }
        }
    }

    /// <summary>
    /// Writes the request's intake stage (and, for Receive, its current run). Only the request's current run writes its
    /// stage, so an older run never overwrites a newer one's; a run becomes current only when the request has none, or its
    /// current run is gone or terminal (a re-driven older run never takes the request from a live newer one); a request
    /// that is not an intake request is never written. The write is conditional on the request's row version, so a
    /// concurrent change by another run is re-read, never overwritten.
    /// </summary>
    /// <returns>False when nothing was written (not the current run, or not an intake request).</returns>
    public virtual async Task<bool> WriteRequestStageAsync(
        Guid requestId, Guid runId, int stage, bool makeCurrentRun, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var request = await ReadRequestAsync(requestId, ct).ConfigureAwait(false);
            if (request?.IntakeStage is null)
            {
                return false;
            }

            var isCurrent = request.CurrentRunId == runId;
            if (!isCurrent && !(makeCurrentRun && await CanBecomeCurrentAsync(request.CurrentRunId, ct).ConfigureAwait(false)))
            {
                _logger.LogInformation("Intake run {RunId}: not the current run of request {RequestId}; its stage is left as it is", runId, requestId);
                return false;
            }

            if (request.IntakeStage == stage && isCurrent)
            {
                return true;
            }

            if (request.RowVersion is null)
            {
                throw new InvalidOperationException($"Request {requestId}: no row version was read; its stage is not written unconditionally.");
            }

            var fields = new Dictionary<string, object> { [RequestIntakeStage] = new OptionSetValue(stage) };
            if (!isCurrent)
            {
                fields[RequestCurrentRun] = new EntityReference(RunEntity, runId);
            }

            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(RequestEntity, requestId, request.RowVersion, fields, ct).ConfigureAwait(false);
                return true;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Changed since the read (a user edit, another run): decide again on the new row.
            }
        }
    }

    private async Task<bool> CanBecomeCurrentAsync(Guid? currentRunId, CancellationToken ct)
    {
        if (currentRunId is not { } current)
        {
            return true;
        }

        var run = await ReadRunAsync(current, ct).ConfigureAwait(false);
        return run is null || IsTerminalStatus(run.Status);
    }

    internal static bool IsTerminalStatus(int? status) =>
        status is (int)ProcessingJobStatus.Completed or (int)ProcessingJobStatus.Failed or (int)ProcessingJobStatus.Cancelled;

    private bool CanTakeOver(IntakeRunClaim held, string owner, DateTimeOffset now)
    {
        var age = now - held.At;
        return string.Equals(JobOf(held.Owner), JobOf(owner), StringComparison.Ordinal) ? age >= OwnerTakeoverAge : age >= ClaimLease;
    }

    private static string JobOf(string owner) => owner.Split('.')[0];

    private static string Serialize(IntakeRunState state)
    {
        var json = state.Serialize();
        if (json.Length > MaxStateLength)
        {
            throw new InvalidOperationException($"The run state is {json.Length} characters; the column holds {MaxStateLength}.");
        }

        return json;
    }

    private static IntakeRunSnapshot ToRunSnapshot(Entity row) => new()
    {
        RunId = row.Id,
        RequestId = row.GetAttributeValue<EntityReference>(RunRequest)?.Id,
        JobType = row.GetAttributeValue<OptionSetValue>(RunJobType)?.Value,
        Status = row.GetAttributeValue<OptionSetValue>(RunStatus)?.Value,
        CurrentStage = row.GetAttributeValue<string>(RunCurrentStage),
        CorrelationId = row.GetAttributeValue<string>(RunCorrelation),
        HarnessKey = row.GetAttributeValue<string>(RunHarnessKey),
        EngineExecutionId = row.GetAttributeValue<string>(RunEngineExecution),
        State = IntakeRunState.Parse(row.GetAttributeValue<string>(RunState)),
        RowVersion = row.RowVersion
            ?? (row.Contains(RowVersionColumn) ? Convert.ToString(row[RowVersionColumn], CultureInfo.InvariantCulture) : null),
    };

    private async Task<Entity?> RetrieveOrNullAsync(string entity, Guid id, string[] columns, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(entity, id, columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFoundInChain(ex))
        {
            return null;
        }
    }
}

/// <summary>The delivery no longer holds the run's claim (another delivery took it over, or the run is gone). Nothing was written.</summary>
public sealed class IntakeClaimLostException(Guid runId)
    : Exception($"Intake run {runId}: this delivery no longer holds the run.")
{
    public Guid RunId { get; } = runId;
}

/// <summary>The run's owner team could not be resolved (<see cref="IRecordOwnershipResolver"/> refused); no run was created.</summary>
public sealed class IntakeRunOwnerRefusedException(Guid runId, string refusalCode)
    : InvalidOperationException($"Intake run {runId}: no owner team resolved ({refusalCode}); the run was not created.")
{
    public Guid RunId { get; } = runId;

    /// <summary>The resolver's stable refusal code (<see cref="RecordOwnerRefusal"/>).</summary>
    public string RefusalCode { get; } = refusalCode;
}
