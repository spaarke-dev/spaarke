using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// ADR-004 job that drafts the billing rules of a Procedure Version from its source file (ebilling-r1 task 075, FR-27; keyed per Procedure
/// Version by task 116, model 7.2 "Drafting"): it reads the version's one Servable Procedure-source Stored File through the Finance file
/// store (<see cref="StoredFileService.OpenForProcessingAsync"/>: Servable, its own path, its hash and a clean scan verdict), extracts its
/// text, asks <see cref="IInvoiceAi.DraftRulesAsync"/> (the PublicContracts facade, ADR-013) for proposals from the closed list of eleven
/// rule types, and hands them to <see cref="RuleSetService.AddDraftsAsync"/>, which validates every one with
/// <c>PolicyVersionValidator.ValidateForSave</c> (Billing domain) before it stores it in the Procedure's rule set as a Proposed, Off rule
/// whose version belongs to this Procedure Version (publish state Draft). Text that did not become a rule, and every proposal the validator
/// refused, goes to the version's not-converted list.
/// </summary>
/// <remarks>
/// <para><b>Idempotency (ADR-004; delivery is at least once).</b> The Procedure Version's Yes/No <c>sprk_notconverted</c> ("drafted",
/// EB-D-211) is written Yes LAST, after the rules and the not-converted list (<c>sprk_notconvertedlist</c>), and is the completion marker: a
/// redelivery after it finds <see cref="DraftSource.AlreadyDrafted"/> and completes without calling the model. A
/// redelivery after a crash BEFORE the marker first removes the earlier run's untouched drafts of THIS version
/// (<see cref="RuleSetService.RemoveUntouchedDraftsAsync"/>: created by this identity, Proposed, Off, never modified, never published, every
/// version a Draft under this Procedure Version), because the model is asked again and its output is not deterministic, so a second set would
/// otherwise sit beside the first. A draft an administrator worked on is kept, and the store does not store a second rule for a clause such a
/// rule already covers. A compensation fault fails the run before the model is asked. The store also skips every rule whose type, body and
/// clause it already holds under this version. No row is written until every model call has returned. Another version of the same Procedure
/// has its own marker, so version 2 is drafted although version 1 was.</para>
/// <para><b>One run at a time.</b> The queue does not de-duplicate and a run can outlast the message lock, so the run holds a processing
/// lock keyed on the Procedure Version (ADR-004); a delivery that cannot take it fails and is retried, finds the marker, and ends as a no-op.
/// The model's clause quotes are checked against the document text before anything is stored. A payload without <c>procedureVersionId</c>
/// (a message queued before task 116, keyed on a rule set) is Poisoned.</para>
/// <para><b>Failures.</b> Transient ones (the model or a write failed, the Binding read failed, a store fault, the file's malware scan has
/// no verdict yet, another delivery holds the lock) return <see cref="JobOutcome.Failure"/> until <see cref="JobContract.IsFinalDelivery"/>,
/// then <see cref="JobOutcome.Poisoned"/>; permanent ones (no such version, not a Draft, not Billing, no single source file, a Malicious
/// file, AI switched off, no text, no owner for the rules) are Poisoned at once so they reach the dead-letter queue instead of looping. There
/// is no per-job status row (ADR-017 is Proposed and a status store needs a schema or cache-key review, see the task notes): the admin
/// watches the rule set (a finished run lists its rules and its not-converted list), and an operator reads the log and the DLQ.</para>
/// <para><b>Logging (ADR-015).</b> Ids, counts and reason codes only; never the document text, a clause or a model reply.</para>
/// <para>§11: <i>Existing</i> — <c>InvoiceSubmissionProcessingJobHandler</c> (store read, extract, facade) and <c>RuleSetService</c> (row
/// shape, validation). <i>Extension</i> — neither is a rule-drafting job; the row writes were added to <c>RuleSetService</c>, not here.
/// <i>Cost of doing nothing</i> — an administrator would author every rule by hand and FR-27 (draft rules from a guideline) would not exist.</para>
/// </remarks>
public class BillingRuleDraftJobHandler : IJobHandler
{
    /// <summary>Job type constant — must match the JobType the trigger route enqueues.</summary>
    public const string JobTypeName = "BillingRuleDraft";

    private readonly RuleSetService _ruleSets;
    private readonly IInvoiceAi _ai;
    private readonly StoredFileService _files;
    private readonly ITextExtractor _text;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<BillingRuleDraftJobHandler> _logger;

    /// <summary>Processing-lock key prefix; one drafting run per Procedure Version at a time (task 116).</summary>
    internal const string LockKeyPrefix = "billing-rule-draft:";

    /// <summary>Longer than the worst run (eight Standard-tier calls plus the writes), so a live run is never overtaken; a dead run's lock expires.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(20);

    public BillingRuleDraftJobHandler(
        RuleSetService ruleSets,
        IInvoiceAi ai,
        StoredFileService files,
        ITextExtractor text,
        IIdempotencyService idempotency,
        ILogger<BillingRuleDraftJobHandler> logger)
    {
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    /// <summary>
    /// Builds the job the trigger route enqueues (task 116): payload <c>{ procedureVersionId, callerObjectId }</c>; the subject and the
    /// idempotency key are the Procedure Version id. The job type is unchanged.
    /// </summary>
    internal static JobContract BuildJob(Guid jobId, Guid procedureVersionId, Guid? callerObjectId, string? correlationId) => new()
    {
        JobId = jobId,
        JobType = JobTypeName,
        SubjectId = procedureVersionId.ToString(),
        CorrelationId = correlationId ?? string.Empty,
        IdempotencyKey = procedureVersionId.ToString(),
        Payload = JsonSerializer.SerializeToDocument(
            new BillingRuleDraftPayload { ProcedureVersionId = procedureVersionId, CallerObjectId = callerObjectId },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = ParsePayload(job.Payload);
        if (payload is null || payload.ProcedureVersionId == Guid.Empty)
        {
            _logger.LogError("Billing rule draft job {JobId}: the payload is missing or has no procedureVersionId", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: missing or invalid procedureVersionId", job.Attempt, stopwatch.Elapsed);
        }

        var versionId = payload.ProcedureVersionId;
        var lockKey = LockKeyPrefix + versionId.ToString("N");
        var lockHeld = false;
        try
        {
            _logger.LogInformation(
                "Billing rule draft job {JobId} for Procedure Version {ProcedureVersionId}, attempt {Attempt}, correlation {CorrelationId}",
                job.JobId, versionId, job.Attempt, job.CorrelationId);

            // 0. One run per Procedure Version at a time. The queue does not de-duplicate, and a run can outlast the message lock, so a second
            //    delivery (a double click, two admins, a redelivery) would otherwise store every rule twice. Not acquired: try again later;
            //    by then the first run has finished and the marker makes this one a no-op.
            lockHeld = await _idempotency.TryAcquireProcessingLockAsync(lockKey, LockDuration, ct);
            if (!lockHeld)
            {
                _logger.LogWarning("Billing rule draft job {JobId}: another delivery is drafting Procedure Version {ProcedureVersionId}", job.JobId, versionId);
                return Transient(job, "another delivery is drafting this Procedure Version", stopwatch);
            }

            // 1. The source, found server-side. A finished run is a no-op (idempotent redelivery).
            var found = await _ruleSets.GetDraftSourceAsync(versionId, ct);
            if (found.Kind != RuleSetOutcomeKind.Done)
            {
                return Refused(job, found.ErrorCode, found.StatusCode, stopwatch);
            }

            var source = found.Value!;
            if (source.AlreadyDrafted)
            {
                _logger.LogInformation("Billing rule draft job {JobId}: Procedure Version {ProcedureVersionId} is already drafted; nothing to do", job.JobId, versionId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // 2. Retry compensation. No marker means no finished run, but an earlier run may have died midway and left drafts. The model is
            //    asked again below and its output is not deterministic, so the earlier run's UNTOUCHED drafts of this version are removed
            //    first (the store decides what untouched means; a draft an administrator worked on is kept). A fault fails the run before the
            //    model is asked, and the next delivery compensates again.
            var compensated = await _ruleSets.RemoveUntouchedDraftsAsync(versionId, ct);
            if (compensated.Kind != RuleSetOutcomeKind.Done)
            {
                if (compensated.ErrorCode == RuleSetService.CodeAlreadyDrafted)
                {
                    // A concurrent delivery finished between the check above and here.
                    _logger.LogInformation("Billing rule draft job {JobId}: Procedure Version {ProcedureVersionId} was drafted by another delivery; nothing to do", job.JobId, versionId);
                    return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
                }

                return Refused(job, compensated.ErrorCode, compensated.StatusCode, stopwatch);
            }

            if (compensated.Value!.Removed > 0)
            {
                _logger.LogInformation(
                    "Billing rule draft job {JobId}: removed {Removed} untouched draft(s) of an earlier run of Procedure Version {ProcedureVersionId}; {Kept} other rule(s) kept",
                    job.JobId, compensated.Value.Removed, versionId, compensated.Value.Kept);
            }

            // 3. The text, from the version's Stored File.
            var extracted = await ReadTextAsync(job, source, stopwatch, ct);
            if (extracted.Outcome is not null)
            {
                return extracted.Outcome;
            }

            // 4. The model, piece by piece. Nothing is written until every piece has returned.
            var pieces = BillingRuleDraftTranslator.Split(
                extracted.Text!, BillingRuleDraftTranslator.ChunkChars, BillingRuleDraftTranslator.MaxChunks, out var unreadFrom);
            var options = BillingRuleDraftTranslator.RuleTypeOptions();
            var proposals = new List<BillingRuleProposal>();
            var notConverted = new List<NotConvertedClause>();
            foreach (var piece in pieces)
            {
                ct.ThrowIfCancellationRequested();
                BillingRuleDraftResult reply;
                try
                {
                    // DocumentId is logged only (never sent): the Stored File id.
                    reply = await _ai.DraftRulesAsync(
                        new BillingRuleDraftRequest { DocumentText = piece, DocumentId = source.FileId, RuleTypes = options }, ct);
                }
                catch (FeatureDisabledException ex)
                {
                    _logger.LogWarning("Billing rule draft job {JobId}: the AI feature is off ({ErrorCode}); nothing was drafted", job.JobId, ex.ErrorCode);
                    return JobOutcome.Poisoned(job.JobId, JobType, $"AI is switched off ({ex.ErrorCode})", job.Attempt, stopwatch.Elapsed);
                }
                catch (InvoiceAiException ex)
                {
                    return AiFailed(job, ex, stopwatch);
                }

                proposals.AddRange(reply.Proposals);
                notConverted.AddRange(reply.NotConverted.Select(n => new NotConvertedClause(n.Text, n.Location, n.Reason)));
            }

            if (unreadFrom >= 0)
            {
                notConverted.Add(new NotConvertedClause(
                    string.Empty, null,
                    $"The document is longer than one drafting run reads: the text after its first {unreadFrom} characters was not read. Split the document and add the rest as rules by hand or in a later version."));
            }

            // 5. The proposals become drafted rules; the store validates every one and writes them, then the marker.
            var translated = BillingRuleDraftTranslator.Translate(proposals, extracted.Text);
            notConverted.AddRange(translated.Refused);
            var added = await _ruleSets.AddDraftsAsync(versionId, translated.Rules, notConverted, payload.CallerObjectId, ct);
            if (added.Kind == RuleSetOutcomeKind.Done)
            {
                _logger.LogInformation(
                    "Billing rule draft job {JobId}: Procedure Version {ProcedureVersionId} drafted: {Stored} stored, {Present} already present, {NotConverted} not converted, in {Elapsed} ms",
                    job.JobId, versionId, added.Value!.Stored, added.Value.AlreadyPresent, added.Value.NotConverted.Count, stopwatch.ElapsedMilliseconds);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            if (added.ErrorCode == RuleSetService.CodeAlreadyDrafted)
            {
                // A concurrent delivery finished first.
                _logger.LogInformation("Billing rule draft job {JobId}: Procedure Version {ProcedureVersionId} was drafted by another delivery; nothing to do", job.JobId, versionId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            if (added.Kind == RuleSetOutcomeKind.OwnerRefused)
            {
                // Nobody can own the new rules (no owner team for the caller): permanent until an administrator fixes the setup.
                var reason = added.OwnerRefusal?.RefusalCode ?? "owner_refused";
                _logger.LogWarning("Billing rule draft job {JobId}: the new rules could not be given an owner ({Reason})", job.JobId, reason);
                return JobOutcome.Poisoned(job.JobId, JobType, $"The drafted rules have no owner ({reason})", job.Attempt, stopwatch.Elapsed);
            }

            return Refused(job, added.ErrorCode, added.StatusCode, stopwatch);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Billing rule draft job {JobId} for Procedure Version {ProcedureVersionId} failed ({ExceptionType})", job.JobId, versionId, ex.GetType().Name);
            return Transient(job, ex.GetType().Name, stopwatch);
        }
        finally
        {
            if (lockHeld)
            {
                try
                {
                    await _idempotency.ReleaseProcessingLockAsync(lockKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // The lock expires on its own; a failed release must not turn a finished run into a failure.
                    _logger.LogWarning(ex, "Billing rule draft job {JobId}: the processing lock for Procedure Version {ProcedureVersionId} could not be released", job.JobId, versionId);
                }
            }
        }
    }

    /// <summary>
    /// The source file's text (task 116): its bytes through the file store's job read, which re-checks the row (Servable, its own path, its
    /// SHA-256) and the scan verdict. Pending (no verdict yet) is transient; Malicious is Poisoned and never read; Missing (the row was
    /// removed or replaced since the source was found, or its blob is gone) is the 422 <c>ruleset.source_missing</c> refusal, Poisoned. A
    /// store fault throws (transient, by the caller's catch).
    /// </summary>
    private async Task<(string? Text, JobOutcome? Outcome)> ReadTextAsync(JobContract job, DraftSource source, Stopwatch stopwatch, CancellationToken ct)
    {
        var read = await _files.OpenForProcessingAsync(source.FileId, ct);
        switch (read.Outcome)
        {
            case StoredFileProcessingOutcome.Pending:
                _logger.LogWarning("Billing rule draft job {JobId}: source file {FileId} has no malware-scan verdict yet", job.JobId, source.FileId);
                return (null, Transient(job, "the source file's malware scan has no verdict yet", stopwatch));
            case StoredFileProcessingOutcome.Malicious:
                _logger.LogError("Billing rule draft job {JobId}: source file {FileId} is Malicious; it is never read", job.JobId, source.FileId);
                return (null, JobOutcome.Poisoned(job.JobId, JobType, "The source file failed its malware scan", job.Attempt, stopwatch.Elapsed));
            case StoredFileProcessingOutcome.Ok when read.Content is not null:
                break;
            default:
                _logger.LogError("Billing rule draft job {JobId}: source file {FileId} is no longer servable", job.JobId, source.FileId);
                return (null, Refused(job, RuleSetService.CodeSourceMissing, StatusCodes.Status422UnprocessableEntity, stopwatch));
        }

        try
        {
            using var stream = read.Content!.ToStream();
            var result = await _text.ExtractAsync(stream, source.FileName, ct);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            {
                _logger.LogError(
                    "Billing rule draft job {JobId}: no text could be read from source file {FileId} ({Method})",
                    job.JobId, source.FileId, result.Method);
                return (null, JobOutcome.Poisoned(job.JobId, JobType, "No text could be read from the source file", job.Attempt, stopwatch.Elapsed));
            }

            return (result.Text, null);
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning("Billing rule draft job {JobId}: text extraction is off ({ErrorCode}); nothing was drafted", job.JobId, ex.ErrorCode);
            return (null, JobOutcome.Poisoned(job.JobId, JobType, $"Text extraction is switched off ({ex.ErrorCode})", job.Attempt, stopwatch.Elapsed));
        }
    }

    private JobOutcome AiFailed(JobContract job, InvoiceAiException ex, Stopwatch stopwatch)
    {
        _logger.LogWarning("Billing rule draft job {JobId}: the AI step failed ({ReasonCode})", job.JobId, ex.ReasonCode);
        // A missing Binding can be a transient routing-table read (the facade documents it so; platform issue #1936) as well as missing
        // configuration, so it is retried like a failed completion or an output the parser refused. On the last delivery it is Poisoned
        // with its reason, which is where a genuinely missing Binding ends up.
        return Transient(job, ex.ReasonCode == InvoiceAiException.BindingMissing
            ? $"The {ConsumerTypes.BillingRuleDraft} Binding is missing ({ex.ReasonCode})"
            : ex.ReasonCode, stopwatch);
    }

    private JobOutcome Refused(JobContract job, string? errorCode, int statusCode, Stopwatch stopwatch)
    {
        _logger.LogWarning("Billing rule draft job {JobId}: refused with {ErrorCode} ({StatusCode})", job.JobId, errorCode, statusCode);
        // A write that failed (5xx) or a read that was cut short may work again; not found, not a Draft, not Billing, no source and the like cannot.
        return statusCode >= 500
            ? Transient(job, errorCode ?? "write failed", stopwatch)
            : JobOutcome.Poisoned(job.JobId, JobType, errorCode ?? $"refused ({statusCode})", job.Attempt, stopwatch.Elapsed);
    }

    private static JobOutcome Transient(JobContract job, string reason, Stopwatch stopwatch) =>
        // IsFinalDelivery, not Attempt: a redelivery is the same message, so Attempt never advances; the broker's delivery count does.
        !job.IsFinalDelivery
            ? JobOutcome.Failure(job.JobId, JobTypeName, reason, job.Attempt, stopwatch.Elapsed)
            : JobOutcome.Poisoned(job.JobId, JobTypeName, reason, job.Attempt, stopwatch.Elapsed);

    private BillingRuleDraftPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            return payload.Deserialize<BillingRuleDraftPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Payload of a <see cref="BillingRuleDraftJobHandler"/> job (task 116: keyed per Procedure Version). The caller's object id is server-derived
/// (the owner team of the stored rules).
/// </summary>
public sealed class BillingRuleDraftPayload
{
    public Guid ProcedureVersionId { get; set; }

    public Guid? CallerObjectId { get; set; }
}
