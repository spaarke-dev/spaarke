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
/// after a crash BEFORE the marker re-asks the model and the store skips every rule whose type, body and clause it already holds, so
/// no rule is stored twice. No row is written until every model call has returned.</para>
/// <para><b>Failures.</b> Transient ones (the model or a write failed, the download returned nothing) return <see cref="JobOutcome.Failure"/>
/// until the last attempt, then <see cref="JobOutcome.Poisoned"/>; permanent ones (no such rule set, no Binding, AI switched off, no
/// text) are Poisoned at once so they reach the dead-letter queue instead of looping. There is no per-job status row (ADR-017 is Proposed
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
    private readonly ILogger<BillingRuleDraftJobHandler> _logger;

    public BillingRuleDraftJobHandler(
        RuleSetService ruleSets,
        IInvoiceAi ai,
        ISpeFileOperations spe,
        ITextExtractor text,
        ILogger<BillingRuleDraftJobHandler> logger)
    {
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _text = text ?? throw new ArgumentNullException(nameof(text));
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
        try
        {
            _logger.LogInformation(
                "Billing rule draft job {JobId} for rule set {RuleSetId}, attempt {Attempt}, correlation {CorrelationId}",
                job.JobId, ruleSetId, job.Attempt, job.CorrelationId);

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

            // 2. The text.
            var extracted = await ReadTextAsync(job, source, stopwatch, ct);
            if (extracted.Outcome is not null)
            {
                return extracted.Outcome;
            }

            // 3. The model, piece by piece. Nothing is written until every piece has returned.
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

            // 4. The proposals become drafted rules; the store validates every one and writes them, then the marker.
            var translated = BillingRuleDraftTranslator.Translate(proposals);
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
        // No Binding is configuration, not chance: retrying cannot help. A failed completion or an output the parser refused may succeed again.
        return ex.ReasonCode == InvoiceAiException.BindingMissing
            ? JobOutcome.Poisoned(job.JobId, JobType, $"The {ConsumerTypes.BillingRuleDraft} Binding is missing ({ex.ReasonCode})", job.Attempt, stopwatch.Elapsed)
            : Transient(job, ex.ReasonCode, stopwatch);
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
        job.Attempt < job.MaxAttempts
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
