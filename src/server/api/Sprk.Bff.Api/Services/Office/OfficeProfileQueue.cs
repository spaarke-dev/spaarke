using System.Text.Json;
using Azure.Messaging.ServiceBus;
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
    private readonly JobSubmissionService _jobs;
    private readonly IDocumentProfileAi? _profiling;
    private readonly ILogger<OfficeProfileQueue> _logger;

    public OfficeProfileQueue(
        JobSubmissionService jobs,
        ILogger<OfficeProfileQueue> logger,
        IDocumentProfileAi? profiling = null)
    {
        _jobs = jobs;
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
            return new GenerateProfileResult(GenerateProfileDispatchOutcome.QueueUnavailable);
        }

        _logger.LogInformation(
            "Office Generate Profile: document {DocumentId} queued as job {JobId} for {RequestedBy} (key {IdempotencyKey}, correlation {CorrelationId}).",
            documentId, requestId, requestedBy ?? "(unknown)", job.IdempotencyKey, correlationId);
        return new GenerateProfileResult(GenerateProfileDispatchOutcome.Dispatched, requestId);
    }
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
