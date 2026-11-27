using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
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
/// genuine redelivery only after the message lock expired). Every fenced write refreshes the claim's time.</para>
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

    /// <summary>The claim owner of one delivery of <paramref name="jobId"/>: <c>{jobId:N}.{nonce:N}</c>.</summary>
    public static string ClaimOwner(Guid jobId) => $"{jobId:N}.{Guid.NewGuid():N}";

    private const int MaxConcurrencyRetries = 3;

    private static readonly string[] RunColumns =
    [
        RunRequest, RunJobType, RunStatus, RunCurrentStage, RunState, RunCorrelation, RunHarnessKey, RowVersionColumn,
    ];

    private static readonly string[] RequestColumns = [RequestIntakeStage, RequestType, RequestChannel, RequestCurrentRun];

    private readonly IGenericEntityService _entities;
    private readonly TimeProvider _time;
    private readonly ILogger<IntakeRunStore> _logger;

    public IntakeRunStore(IGenericEntityService entities, TimeProvider time, ILogger<IntakeRunStore> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
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
    /// </summary>
    /// <exception cref="InvalidOperationException">A row with that id exists but belongs to another request or is not an intake run.</exception>
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
    /// <returns>False (nothing written) when the run is gone, terminal, or not awaiting <paramref name="step"/>, or the step already has a result.</returns>
    public virtual async Task<bool> RecordExternalStepResultAsync(
        Guid runId, string step, IntakeHarnessResultReference result, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        ArgumentNullException.ThrowIfNull(result);
        for (var attempt = 1; ; attempt++)
        {
            var run = await ReadRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null || run.RowVersion is null || IsTerminalStatus(run.Status)
                || run.State.Awaiting?.Step != step || run.State.Steps.ContainsKey(step))
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
    /// Writes the request's intake stage (and, for Receive, its current run). Only the request's current run writes its
    /// stage, so an older run never overwrites a newer one's; a request that is not an intake request is never written.
    /// </summary>
    /// <returns>False when nothing was written (not the current run, or not an intake request).</returns>
    public virtual async Task<bool> WriteRequestStageAsync(
        Guid requestId, Guid runId, int stage, bool makeCurrentRun, CancellationToken ct)
    {
        var request = await ReadRequestAsync(requestId, ct).ConfigureAwait(false);
        if (request?.IntakeStage is null)
        {
            return false;
        }

        if (!makeCurrentRun && request.CurrentRunId != runId)
        {
            _logger.LogInformation("Intake run {RunId}: not the current run of request {RequestId}; its stage is left as it is", runId, requestId);
            return false;
        }

        if (request.IntakeStage == stage && request.CurrentRunId == runId)
        {
            return true;
        }

        var fields = new Dictionary<string, object> { [RequestIntakeStage] = new OptionSetValue(stage) };
        if (makeCurrentRun)
        {
            fields[RequestCurrentRun] = new EntityReference(RunEntity, runId);
        }

        await _entities.UpdateAsync(RequestEntity, requestId, fields, ct).ConfigureAwait(false);
        return true;
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
