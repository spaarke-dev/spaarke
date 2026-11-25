using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// The Office add-in's Generate Profile request (FR-08): one <c>AppOnlyDocumentAnalysis</c> job on the job queue, the
/// ADR-004 path every automatic profile already takes.
/// </summary>
/// <remarks>
/// <para>
/// Task 068 (#1086) replaces <c>OfficeProfileDispatcher</c>, which ran the profile in <c>Task.Run</c> behind the 202, so a
/// restart lost it. The 202 now means the request is on Service Bus and survives the process.
/// </para>
/// <para>
/// <b>The key</b> carries this request's own id, task 029's discriminator
/// (<see cref="AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey"/>). A redelivery of the request repeats it and
/// still skips, every click has its own and runs, and a document a save already profiled is profiled again. That was
/// the collision the dispatcher was built to avoid.
/// </para>
/// <para>
/// <b>App-only, after the caller was authorized.</b> The route's <c>DocumentAuthorizationFilter("write")</c> has
/// authorized the caller on this document before anything is queued. The job then runs the same ACT-011 Action and the
/// same field mapper as the OBO path (<c>DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md</c>, #919 Fix 2), and it records
/// <c>sprk_filesummarystatus</c> Pending → Completed or Failed, which the OBO path never wrote. A user's token cannot be
/// queued: it expires, and it must not be stored in a message. Who asked is kept in the payload and the log instead.
/// </para>
/// <para>
/// <b>Availability.</b> <see cref="IDocumentProfileAi"/> is registered exactly when the compound AI gate is on, the
/// same gate that registers the handler's analysis service (<c>AnalysisServicesModule.AddAnalysisOrchestrationServices</c>).
/// Without it a queued job could never run, so the request is refused (503) rather than accepted.
/// </para>
/// </remarks>
public class OfficeProfileQueue
{
    // sprk_filesummarystatus "None" (the pane reads a never-set column the same way).
    private const int SummaryStatusNone = 100000000;

    private readonly JobSubmissionService _jobs;
    private readonly IDocumentDataverseService _documents;
    private readonly IDocumentProfileAi? _profiling;
    private readonly ILogger<OfficeProfileQueue> _logger;

    public OfficeProfileQueue(
        JobSubmissionService jobs,
        IDocumentDataverseService documents,
        ILogger<OfficeProfileQueue> logger,
        IDocumentProfileAi? profiling = null)
    {
        _jobs = jobs;
        _documents = documents;
        _logger = logger;
        _profiling = profiling;
    }

    /// <summary>
    /// Queues a fresh profile of <paramref name="documentId"/>, returning only once the job is on Service Bus.
    /// </summary>
    /// <remarks>
    /// The submit is not cancelled with the request: a send that has started may land even if the caller leaves, and
    /// the caller is then told nothing either way, so there is nothing to gain from abandoning it half-way. A Service
    /// Bus failure is <see cref="GenerateProfileDispatchOutcome.QueueUnavailable"/>, a retryable 503; anything else
    /// throws to the global handler. Neither may produce a 202.
    /// </remarks>
    public async Task<GenerateProfileResult> QueueAsync(
        Guid documentId,
        string correlationId,
        string? requestedBy)
    {
        if (_profiling is null)
        {
            _logger.LogWarning(
                "Office Generate Profile: document profiling is unavailable (compound AI gate off) — document {DocumentId} not queued.",
                documentId);
            return new GenerateProfileResult(GenerateProfileDispatchOutcome.FacadeUnavailable);
        }

        // Pending is written BEFORE the submit and the 202 (task 137, #1090). The pane re-reads the document as soon as it
        // has the 202, and the job writes its own first status only once it starts, so a read in between would return
        // the status from before the click (Failed, say) as if it were this request's result. Before the submit, never
        // after: a job that fails fast writes its terminal status within a second of starting, and a Pending written
        // after it would overwrite that and stay.
        var pending = await MarkPendingAsync(documentId);

        var requestId = Guid.NewGuid();
        var job = new JobContract
        {
            JobId = requestId,
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
            SubjectId = documentId.ToString(),
            CorrelationId = correlationId,
            IdempotencyKey = AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId, requestId),
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                DocumentId = documentId,
                Source = "OfficeGenerateProfile",
                RequestedBy = requestedBy,
                EnqueuedAt = DateTimeOffset.UtcNow,
            })),
        };

        try
        {
            await _jobs.SubmitJobAsync(job, CancellationToken.None);
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Office Generate Profile: the job queue refused document {DocumentId} (reason {Reason}, transient {Transient}, correlation {CorrelationId}).",
                documentId, ex.Reason, ex.IsTransient, correlationId);

            // Nothing was queued, so nothing will ever move the status on from Pending: put back what was there.
            await RestoreStatusAsync(documentId, pending);
            return new GenerateProfileResult(GenerateProfileDispatchOutcome.QueueUnavailable);
        }
        catch
        {
            // Any other submit failure (a missing Service Bus role is an UnauthorizedAccessException, not a
            // ServiceBusException) is still a request that was not queued: same restore, and the failure goes on to the
            // global handler as before.
            await RestoreStatusAsync(documentId, pending);
            throw;
        }

        _logger.LogInformation(
            "Office Generate Profile: document {DocumentId} queued as job {JobId} for {RequestedBy} (key {IdempotencyKey}, correlation {CorrelationId}).",
            documentId, requestId, requestedBy ?? "(unknown)", job.IdempotencyKey, correlationId);
        return new GenerateProfileResult(GenerateProfileDispatchOutcome.Dispatched, requestId);
    }

    /// <summary>
    /// Records <c>sprk_filesummarystatus</c> = Pending on the document, and the status that was there. Best effort: a
    /// failed read or write is logged and the request is still queued, because the profile is still wanted, and the
    /// pane's own cap ends its wait. The job writes Pending again when it starts (<c>AppOnlyAnalysisService</c>), so the
    /// two writes agree.
    /// </summary>
    private async Task<PendingMark> MarkPendingAsync(Guid documentId)
    {
        int? previous = null;
        try
        {
            var document = await _documents.GetDocumentAsync(documentId.ToString(), CancellationToken.None);
            if (document is null)
            {
                _logger.LogWarning(
                    "Office Generate Profile: document {DocumentId} was not found when marking it Pending; the job is queued anyway.",
                    documentId);
                return new PendingMark(Written: false, Previous: null);
            }

            previous = document.SummaryStatus;
            await _documents.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest { SummaryStatus = AppOnlyAnalysisService.SummaryStatusPending },
                CancellationToken.None);
            return new PendingMark(Written: true, Previous: previous);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Office Generate Profile: could not mark document {DocumentId} Pending; the job is queued anyway and the pane's wait is capped.",
                documentId);
            return new PendingMark(Written: false, Previous: previous);
        }
    }

    /// <summary>
    /// Undoes <see cref="MarkPendingAsync"/> after the queue refused the request. A row that never had a status goes back
    /// to None, which the pane reads exactly as it reads a missing one. Best effort: a failure here is logged.
    /// </summary>
    /// <remarks>
    /// A send that Service Bus retries can take a minute, and a job from an earlier click may finish in that time. So the
    /// row is read again and put back only while it still says Pending: a status a job wrote since is newer than the one
    /// this request saw, and is left alone. A row that was Pending before needs no restore at all.
    /// </remarks>
    private async Task RestoreStatusAsync(Guid documentId, PendingMark pending)
    {
        if (!pending.Written || pending.Previous == AppOnlyAnalysisService.SummaryStatusPending)
        {
            return;
        }

        try
        {
            var current = (await _documents.GetDocumentAsync(documentId.ToString(), CancellationToken.None))?.SummaryStatus;
            if (current != AppOnlyAnalysisService.SummaryStatusPending)
            {
                return;
            }

            await _documents.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest { SummaryStatus = pending.Previous ?? SummaryStatusNone },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Office Generate Profile: could not restore the profile status of document {DocumentId} after the queue refused it; it stays Pending until the next profile.",
                documentId);
        }
    }

    private readonly record struct PendingMark(bool Written, int? Previous);
}

/// <summary>
/// Outcome of the FR-08 Generate Profile request. Public: it crosses <see cref="IOfficeService"/> to
/// <c>OfficeEndpoints.GenerateProfileAsync</c>, which maps every member to a distinct HTTP response.
/// </summary>
/// <remarks>
/// The member names predate task 068. <c>NoBearer</c> is gone with the OBO call that needed the caller's token; the route's
/// filter refuses a token-less caller anyway, and the queued job needs none.
/// </remarks>
public enum GenerateProfileDispatchOutcome
{
    /// <summary>The request is on the job queue. The endpoint returns 202 Accepted: the ONLY outcome that may claim
    /// success.</summary>
    Dispatched,

    /// <summary>Document profiling is unavailable: the compound AI gate is off, so no <c>IDocumentProfileAi</c> is
    /// registered and the job could never run. The endpoint MUST return 503, never 202 (root CLAUDE.md §10
    /// asymmetric-registration rule).</summary>
    FacadeUnavailable,

    /// <summary>The job queue refused the request (a Service Bus failure). The endpoint returns 503, retryable: nothing
    /// was queued.</summary>
    QueueUnavailable,
}

/// <summary>The Generate Profile outcome, and the queued job's id when there is one (ADR-017: the 202 carries it).</summary>
public sealed record GenerateProfileResult(GenerateProfileDispatchOutcome Outcome, Guid? JobId = null);
