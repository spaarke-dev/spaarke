using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// ADR-004 job that drafts the billing rules of a rule set from its source document (ebilling-r1 task 075, FR-27): it reads the document
/// the rule set links in the Procedure Library, extracts its text, asks <see cref="IInvoiceAi.DraftRulesAsync"/> (the PublicContracts
/// facade, ADR-013) for proposals from the closed list of eleven rule types, and hands them to <see cref="RuleSetService.AddDraftsAsync"/>,
/// which validates every one with <c>PolicyVersionValidator.ValidateForSave</c> (Billing domain) before it stores it as a Proposed, Off
/// rule. Text that did not become a rule, and every proposal the validator refused, goes to the rule set's not-converted list.
/// </summary>
/// <remarks>
/// <para><b>Idempotency (ADR-004; delivery is at least once).</b> The rule set's not-converted list is written LAST and is the completion
/// marker: a redelivery after it finds <see cref="DraftSource.AlreadyDrafted"/> and completes without calling the model. A redelivery
/// after a crash BEFORE the marker first removes the earlier run's untouched drafts (<see cref="RuleSetService.RemoveUntouchedDraftsAsync"/>:
/// created by this identity, Proposed, Off, never modified, never published), because the model is asked again and its output is not
/// deterministic, so a second set would otherwise sit beside the first. A draft an administrator worked on is kept, and the store does not
/// store a second rule for a clause such a rule already covers. A compensation fault fails the run before the model is asked. The store
/// also skips every rule whose type, body and clause it already holds. No row is written until every model call has returned.</para>
/// <para><b>One run at a time.</b> The queue does not de-duplicate and a run can outlast the message lock, so the run holds a processing
/// lock keyed on the rule set (ADR-004); a delivery that cannot take it fails and is retried, finds the marker, and ends as a no-op. The
/// model's clause quotes are checked against the document text before anything is stored.</para>
/// <para><b>Failures.</b> Transient ones (the model or a write failed, the Binding read failed, the download returned nothing, another
/// delivery holds the lock) return <see cref="JobOutcome.Failure"/> until <see cref="JobContract.IsFinalDelivery"/>, then
/// <see cref="JobOutcome.Poisoned"/>; permanent ones (no such rule set, AI switched off, no text, no owner for the rules) are Poisoned at
/// once so they reach the dead-letter queue instead of looping. There is no per-job status row (ADR-017 is Proposed
/// and a status store needs a schema or cache-key review, see the task notes): the admin watches the rule set (a finished run lists its
/// rules and its not-converted list), and an operator reads the log and the DLQ.</para>
/// <para><b>Logging (ADR-015).</b> Ids, counts and reason codes only; never the document text, a clause or a model reply.</para>
/// <para>§11: <i>Existing</i> — <c>InvoiceExtractionJobHandler</c> (download, extract, facade) and <c>RuleSetService</c> (row shape, validation).
/// <i>Extension</i> — neither is a rule-drafting job; the row writes were added to <c>RuleSetService</c>, not here. <i>Cost of doing
/// nothing</i> — an administrator would author every rule by hand and FR-27 (draft rules from a guideline) would not exist.</para>
/// </remarks>
public class BillingRuleDraftJobHandler : IJobHandler
{
    /// <summary>Job type constant — must match the JobType the trigger route enqueues.</summary>
    public const string JobTypeName = "BillingRuleDraft";

    private readonly RuleSetService _ruleSets;
    private readonly IInvoiceAi _ai;
    private readonly ISpeFileOperations _spe;
    private readonly ITextExtractor _text;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<BillingRuleDraftJobHandler> _logger;

    /// <summary>Processing-lock key prefix; one drafting run per rule set at a time.</summary>
    internal const string LockKeyPrefix = "billing-rule-draft:";

    /// <summary>Longer than the worst run (eight Standard-tier calls plus the writes), so a live run is never overtaken; a dead run's lock expires.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(20);

    public BillingRuleDraftJobHandler(
        RuleSetService ruleSets,
        IInvoiceAi ai,
        ISpeFileOperations spe,
        ITextExtractor text,
        IIdempotencyService idempotency,
        ILogger<BillingRuleDraftJobHandler> logger)
    {
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    /// <summary>Builds the job the trigger route enqueues: payload <c>{ ruleSetId, callerObjectId }</c>, idempotency key = the rule set id.</summary>
    internal static JobContract BuildJob(Guid jobId, Guid ruleSetId, Guid? callerObjectId, string? correlationId) => new()
    {
        JobId = jobId,
        JobType = JobTypeName,
        SubjectId = ruleSetId.ToString(),
        CorrelationId = correlationId ?? string.Empty,
        IdempotencyKey = ruleSetId.ToString(),
        Payload = JsonSerializer.SerializeToDocument(
            new BillingRuleDraftPayload { RuleSetId = ruleSetId, CallerObjectId = callerObjectId },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = ParsePayload(job.Payload);
        if (payload is null || payload.RuleSetId == Guid.Empty)
        {
            _logger.LogError("Billing rule draft job {JobId}: the payload is missing or has no ruleSetId", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: missing or invalid ruleSetId", job.Attempt, stopwatch.Elapsed);
        }

        var ruleSetId = payload.RuleSetId;
        var lockKey = LockKeyPrefix + ruleSetId.ToString("N");
        var lockHeld = false;
        try
        {
            _logger.LogInformation(
                "Billing rule draft job {JobId} for rule set {RuleSetId}, attempt {Attempt}, correlation {CorrelationId}",
                job.JobId, ruleSetId, job.Attempt, job.CorrelationId);

            // 0. One run per rule set at a time. The queue does not de-duplicate, and a run can outlast the message lock, so a second
            //    delivery (a double click, two admins, a redelivery) would otherwise store every rule twice. Not acquired: try again
            //    later; by then the first run has finished and the marker makes this one a no-op.
            lockHeld = await _idempotency.TryAcquireProcessingLockAsync(lockKey, LockDuration, ct);
            if (!lockHeld)
            {
                _logger.LogWarning("Billing rule draft job {JobId}: another delivery is drafting rule set {RuleSetId}", job.JobId, ruleSetId);
                return Transient(job, "another delivery is drafting this rule set", stopwatch);
            }

            // 1. The source, found server-side. A finished run is a no-op (idempotent redelivery).
            var found = await _ruleSets.GetDraftSourceAsync(ruleSetId, ct);
            if (found.Kind != RuleSetOutcomeKind.Done)
            {
                return Refused(job, found.ErrorCode, found.StatusCode, stopwatch);
            }

            var source = found.Value!;
            if (source.AlreadyDrafted)
            {
                _logger.LogInformation("Billing rule draft job {JobId}: rule set {RuleSetId} is already drafted; nothing to do", job.JobId, ruleSetId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // 2. Retry compensation. No marker means no finished run, but an earlier run may have died midway and left drafts. The model is
            //    asked again below and its output is not deterministic, so the earlier run's UNTOUCHED drafts are removed first (the store
            //    decides what untouched means; a draft an administrator worked on is kept). A fault fails the run before the model is
            //    asked, and the next delivery compensates again.
            var compensated = await _ruleSets.RemoveUntouchedDraftsAsync(ruleSetId, ct);
            if (compensated.Kind != RuleSetOutcomeKind.Done)
            {
                if (compensated.ErrorCode == RuleSetService.CodeAlreadyDrafted)
                {
                    // A concurrent delivery finished between the check above and here.
                    _logger.LogInformation("Billing rule draft job {JobId}: rule set {RuleSetId} was drafted by another delivery; nothing to do", job.JobId, ruleSetId);
                    return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
                }

                return Refused(job, compensated.ErrorCode, compensated.StatusCode, stopwatch);
            }

            if (compensated.Value!.Removed > 0)
            {
                _logger.LogInformation(
                    "Billing rule draft job {JobId}: removed {Removed} untouched draft(s) of an earlier run of rule set {RuleSetId}; {Kept} other rule(s) kept",
                    job.JobId, compensated.Value.Removed, ruleSetId, compensated.Value.Kept);
            }

            // 3. The text.
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
                    reply = await _ai.DraftRulesAsync(
                        new BillingRuleDraftRequest { DocumentText = piece, DocumentId = source.DocumentId, RuleTypes = options }, ct);
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
                    $"The document is longer than one drafting run reads: the text after its first {unreadFrom} characters was not read. Split the document and create a rule set for the rest."));
            }

            // 5. The proposals become drafted rules; the store validates every one and writes them, then the marker.
            var translated = BillingRuleDraftTranslator.Translate(proposals, extracted.Text);
            notConverted.AddRange(translated.Refused);
            var added = await _ruleSets.AddDraftsAsync(ruleSetId, translated.Rules, notConverted, payload.CallerObjectId, ct);
            if (added.Kind == RuleSetOutcomeKind.Done)
            {
                _logger.LogInformation(
                    "Billing rule draft job {JobId}: rule set {RuleSetId} drafted: {Stored} stored, {Present} already present, {NotConverted} not converted, in {Elapsed} ms",
                    job.JobId, ruleSetId, added.Value!.Stored, added.Value.AlreadyPresent, added.Value.NotConverted.Count, stopwatch.ElapsedMilliseconds);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            if (added.ErrorCode == RuleSetService.CodeAlreadyDrafted)
            {
                // A concurrent delivery finished first.
                _logger.LogInformation("Billing rule draft job {JobId}: rule set {RuleSetId} was drafted by another delivery; nothing to do", job.JobId, ruleSetId);
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
            _logger.LogError(ex, "Billing rule draft job {JobId} for rule set {RuleSetId} failed ({ExceptionType})", job.JobId, ruleSetId, ex.GetType().Name);
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
                    _logger.LogWarning(ex, "Billing rule draft job {JobId}: the processing lock for rule set {RuleSetId} could not be released", job.JobId, ruleSetId);
                }
            }
        }
    }

    private async Task<(string? Text, JobOutcome? Outcome)> ReadTextAsync(JobContract job, DraftSource source, Stopwatch stopwatch, CancellationToken ct)
    {
        var stream = await _spe.DownloadFileAsync(source.DriveId, source.ItemId, ct);
        if (stream is null)
        {
            _logger.LogError("Billing rule draft job {JobId}: the source document {DocumentId} could not be downloaded", job.JobId, source.DocumentId);
            return (null, Transient(job, "source download returned no file", stopwatch));
        }

        try
        {
            using (stream)
            {
                var result = await _text.ExtractAsync(stream, source.FileName, ct);
                if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
                {
                    _logger.LogError(
                        "Billing rule draft job {JobId}: no text could be read from source document {DocumentId} ({Method})",
                        job.JobId, source.DocumentId, result.Method);
                    return (null, JobOutcome.Poisoned(job.JobId, JobType, "No text could be read from the source document", job.Attempt, stopwatch.Elapsed));
                }

                return (result.Text, null);
            }
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
        // A write that failed (5xx) or a read that was cut short may work again; not found, retired, no source and the like cannot.
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

/// <summary>Payload of a <see cref="BillingRuleDraftJobHandler"/> job. The caller's object id is server-derived (the owner team of the stored rules).</summary>
public sealed class BillingRuleDraftPayload
{
    public Guid RuleSetId { get; set; }

    public Guid? CallerObjectId { get; set; }
}
