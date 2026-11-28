using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake;

// =====================================================================================================================
// spaarke-intake-pipeline-r1 task 017 (spec FR-11, NFR-01/02/08; owner decisions 2026-11-28): the intake approval's claim, approve and
// reject transitions, and the ONE authorization chain they share.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: the Decide lane (DecisionCommitService, the catalog, the review-kind Decision Record) records the decision; IntakeRunStore owns
//     every run and request write; CallerRecordAccessProbe, IIdentityNormalizationService and ContactUserRepresentation answer the access
//     questions. Nothing moves an intake run out of Awaiting approval, and nothing claims a request.
//   Extension: the approve/reject executors and the claim route all need the same three checks and the same conditional transitions; this
//     class holds them once and writes only through IntakeRunStore (one owner per invariant). It adds no Dataverse column and no package.
//   Cost of doing nothing: a run that needs approval stays in Awaiting approval for ever; FR-11, FR-13(b) and the door's Return never happen.
// =====================================================================================================================

/// <summary>The three intake approval actions.</summary>
public enum IntakeApprovalAction
{
    Claim,
    Approve,
    Reject,
}

/// <summary>How an approval check or transition ended. Every value but <see cref="Done"/> wrote nothing.</summary>
public enum IntakeApprovalOutcome
{
    /// <summary>Allowed (a check), or written (a transition), or already done by this same action (a retry: nothing new written).</summary>
    Done,

    /// <summary>The record check failed: no Read or Write on the request, a fault, an unknown id or a row that is not an intake request.
    /// One answer for all, so the route is not an existence oracle.</summary>
    NotFound,

    /// <summary>Not a member of the request's owning team (or it could not be told), no single linked contact, or not the claimer.</summary>
    Forbidden,

    /// <summary>The run's harness switch is off (ADR-032). Checked after authorization.</summary>
    Disabled,

    /// <summary>The request or its run is not waiting for approval (or was decided the other way, or is not claimed yet).</summary>
    WrongState,

    /// <summary>Someone else claimed it, or the claim lost a race.</summary>
    AlreadyClaimed,

    /// <summary>A read or write the transition needs failed (the run, an output's SPE version, the conditional write); nothing was written,
    /// or (after a timeout) the outcome is unknown and a retry converges.</summary>
    Unavailable,
}

/// <summary>What the authorization chain established: the request row it read, the caller's contact and the run.</summary>
public sealed record IntakeApprovalContext(IntakeApprovalRequestSnapshot Request, Guid CallerContactId, IntakeRunSnapshot? Run);

/// <summary>An answer of <see cref="IntakeApprovalService"/>: the outcome, the context when the checks passed, and a fixed detail.</summary>
public sealed record IntakeApprovalResult(IntakeApprovalOutcome Outcome, IntakeApprovalContext? Context = null, string? Detail = null)
{
    public bool IsDone => Outcome == IntakeApprovalOutcome.Done;
}

/// <summary>
/// The intake approval transitions (claim, approve, reject) and their authorization, in the agreed order (uac-r2 (a), owner "Team, then
/// claimer"), each step as the caller and failing closed:
/// <list type="number">
/// <item>Record check: the caller's own rights on the request through the equalised gate question
/// (<see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>), Read AND Write; then the request read app-only. A
/// denial, a fault, an unknown id and a row with no intake stage (an outbound inquiry) are all <see cref="IntakeApprovalOutcome.NotFound"/>.</item>
/// <item>Membership of the request's owning team (<see cref="IIdentityNormalizationService.ResolveAsync"/> → <c>TeamIds</c>). Not a member,
/// a user-owned row, an unresolved caller or a faulted team read are all <see cref="IntakeApprovalOutcome.Forbidden"/>. The identity is cached
/// for 2 minutes (ADR-009): a membership removed in that window is still honoured (a known limit, named in the PR).</item>
/// <item>The caller's OWN linked contact, resolved on the server (<see cref="ContactUserRepresentation"/>); none, several or unreadable is
/// Forbidden. For approve and reject the request's claimer must be that contact (Forbidden otherwise).</item>
/// </list>
/// Then the run's harness switch (<see cref="IntakePipelineOptions.IsHarnessEnabled"/>; off is <see cref="IntakeApprovalOutcome.Disabled"/>,
/// nothing written), then the state. The checks run again at approve and reject, not only at claim. Self-approval is allowed (owner
/// decision 2026-11-28, ontology D-237): being the requester neither bars nor grants anything, so no check reads the requester.
/// </summary>
/// <remarks>
/// <para><b>Writes.</b> Only through <see cref="IntakeRunStore"/>, each conditional on the row version read, so a race refuses instead of
/// acting twice. The claim sets only the claimer column, as the app (G5: checked as the user, written as the app); it grants nothing and
/// creates no Assigned-To access row (a service request is never grantable to a contact, uac-r2 2026-11-28). Approve and reject write the
/// run and the request in ONE transaction conditional on both rows as the checks read them, so a request whose stage or claimer changed
/// in between refuses (ontology-r1 (b)). A write fault is <see cref="IntakeApprovalOutcome.Unavailable"/>.</para>
/// <para><b>Idempotent on retry.</b> An approval already recorded is finished (its Return enqueued once, marker after send) and answered
/// Done; a rejection already recorded is answered Done. The continuation's key is <c>intake-return:{runId:N}</c>.</para>
/// <para><b>ADR-015.</b> Logs carry ids, outcomes and codes only; never document text or a reject reason (this class never sees one).</para>
/// </remarks>
public class IntakeApprovalService
{
    /// <summary>The request's entity SET (the probe's question is by set).</summary>
    internal const string RequestEntitySet = "sprk_servicerequests";

    /// <summary>The run state marker written after the continuation of an approval was enqueued.</summary>
    internal const string ApprovalContinuedMarker = "approve-continued";

    private readonly IntakeRunStore _store;
    private readonly CallerRecordAccessProbe _probe;
    private readonly IIdentityNormalizationService _identity;
    private readonly ContactUserRepresentation _people;
    private readonly ISpeFileOperations _files;
    private readonly JobSubmissionService _jobs;
    private readonly IOptionsMonitor<IntakePipelineOptions> _options;
    private readonly ILogger<IntakeApprovalService> _logger;

    public IntakeApprovalService(
        IntakeRunStore store,
        CallerRecordAccessProbe probe,
        IIdentityNormalizationService identity,
        ContactUserRepresentation people,
        ISpeFileOperations files,
        JobSubmissionService jobs,
        IOptionsMonitor<IntakePipelineOptions> options,
        ILogger<IntakeApprovalService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The continuation key of an approved run: the door's own return key (task 013's <c>MarkerKey("return")</c>).</summary>
    public static string ReturnKey(Guid runId) => $"intake-return:{runId:N}";

    // ── The checks ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The authorization chain (record check, team, contact and, for approve and reject, the claimer) followed by the harness switch and
    /// the state check for <paramref name="action"/>. Writes nothing. <see cref="IntakeApprovalOutcome.Done"/> carries the context the
    /// transition acts on.
    /// </summary>
    public virtual async Task<IntakeApprovalResult> CheckAsync(HttpContext http, Guid requestId, IntakeApprovalAction action, CancellationToken ct)
    {
        var authorized = await AuthorizeAsync(http, requestId, action, ct).ConfigureAwait(false);
        if (!authorized.IsDone)
        {
            return authorized;
        }

        var context = authorized.Context!;
        IntakeRunSnapshot? run;
        try
        {
            run = context.Request.CurrentRunId is { } runId ? await _store.ReadRunAsync(runId, ct).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // After authorization, so the caller may learn that the read failed.
            _logger.LogWarning("Intake approval: the run of request {RequestId} could not be read ({ExceptionType})", requestId, ex.GetType().Name);
            return Refuse(IntakeApprovalOutcome.Unavailable, "The request could not be read right now; nothing was written.");
        }

        if (run is null || run.RequestId != requestId || run.JobType != (int)ProcessingJobType.IntakeRun || string.IsNullOrEmpty(run.HarnessKey))
        {
            return Refuse(IntakeApprovalOutcome.WrongState, "The request has no intake run waiting for approval.");
        }

        // ADR-032: the run's own harness switch, AFTER authorization, so an unauthorised caller still gets 404/403.
        if (!_options.CurrentValue.IsHarnessEnabled(run.HarnessKey))
        {
            _logger.LogInformation("Intake approval: {Action} on request {RequestId} refused, harness {HarnessKey} is switched off", action, requestId, run.HarnessKey);
            return Refuse(IntakeApprovalOutcome.Disabled, "Approval of this request is switched off right now.");
        }

        context = context with { Run = run };
        return StateOf(action, context) is { } refusal ? refusal : new IntakeApprovalResult(IntakeApprovalOutcome.Done, context);
    }

    /// <summary>The three authorization steps alone (see the class summary). Writes nothing.</summary>
    public virtual async Task<IntakeApprovalResult> AuthorizeAsync(HttpContext http, Guid requestId, IntakeApprovalAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var token = TokenHelper.ExtractBearerTokenOrNull(http);

        // (1) Record check: Read and Write, as the caller, through the equalised question; then the row, app-only.
        IntakeApprovalRequestSnapshot? request;
        try
        {
            var rights = requestId == Guid.Empty
                ? AccessRights.None
                : await _probe.GetCallerRightsForAuthorizationGateAsync(token, RequestEntitySet, requestId, ct).ConfigureAwait(false);
            if (!rights.HasFlag(AccessRights.Read) || !rights.HasFlag(AccessRights.Write))
            {
                return NotFound();
            }

            request = await _store.ReadApprovalRequestAsync(requestId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Intake approval: the record check of request {RequestId} faulted ({ExceptionType}); denying", requestId, ex.GetType().Name);
            return NotFound();
        }

        if (request?.IntakeStage is null)
        {
            // Gone, or not an intake request (an outbound inquiry shares the table): the same answer as no access.
            return NotFound();
        }

        // (2) Owning-team membership.
        Guid? caller;
        try
        {
            caller = await _probe.GetCallerSystemUserIdAsync(token, ct).ConfigureAwait(false);
            if (caller is not { } userId || userId == Guid.Empty || request.OwningTeamId is not { } team)
            {
                return Forbid(requestId, action, "no-team-or-caller");
            }

            var identity = await _identity.ResolveAsync(userId, ct).ConfigureAwait(false);
            if ((identity.Faults & IdentityReadFaults.Teams) != 0 || !identity.TeamIds.Contains(team))
            {
                return Forbid(requestId, action, "not-a-member");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Intake approval: the membership check of request {RequestId} faulted ({ExceptionType}); denying", requestId, ex.GetType().Name);
            return Forbid(requestId, action, "membership-fault");
        }

        // (3) The caller's own contact; for approve and reject, the claimer.
        UserContactResolution contact;
        try
        {
            contact = await ResolveCallerContactAsync(caller!.Value, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Intake approval: the caller's contact for request {RequestId} could not be read ({ExceptionType}); denying", requestId, ex.GetType().Name);
            return Forbid(requestId, action, "contact-fault");
        }

        if (contact.Status != UserContactStatus.One || contact.ContactId is not { } contactId || contactId == Guid.Empty)
        {
            return Forbid(requestId, action, "no-single-contact");
        }

        if (action != IntakeApprovalAction.Claim && request.ClaimedByContactId is { } claimer && claimer != contactId)
        {
            return Forbid(requestId, action, "not-the-claimer");
        }

        return new IntakeApprovalResult(IntakeApprovalOutcome.Done, new IntakeApprovalContext(request, contactId, Run: null));
    }

    /// <summary>
    /// The caller's ONE linked contact, resolved on the server (uac-r2 (1)b): never from the request or the body. A seam only so the
    /// transition tests can name the answer; production always asks <see cref="ContactUserRepresentation"/>.
    /// </summary>
    internal virtual Task<UserContactResolution> ResolveCallerContactAsync(Guid systemUserId, CancellationToken ct) =>
        _people.ResolveContactForUserAsync(systemUserId, ct);

    /// <summary>The state refusal for <paramref name="action"/>, or null when the request and run allow it (or it is a retry of it).</summary>
    private static IntakeApprovalResult? StateOf(IntakeApprovalAction action, IntakeApprovalContext context)
    {
        var run = context.Run!;
        var state = run.State;

        // A retry of a decision already recorded by THIS action is let through, so the transition can finish it; the other action is not.
        if (action == IntakeApprovalAction.Approve && state.ApprovedOn is not null && state.RejectedOn is null && context.Request.ClaimedByContactId == context.CallerContactId)
        {
            return null;
        }

        if (action == IntakeApprovalAction.Reject && state.RejectedOn is not null && state.ApprovedOn is null && context.Request.ClaimedByContactId == context.CallerContactId)
        {
            return null;
        }

        var waiting = context.Request.IntakeStage == IntakeRequestStages.AwaitingApproval
            && run.Status == (int)ProcessingJobStatus.InProgress
            && state.Markers.ContainsKey("approve")
            && !state.IsCompleted(IntakeRunStage.Approve)
            && state.ApprovedOn is null
            && state.RejectedOn is null
            && state.Outputs is not null;
        if (!waiting)
        {
            return Refuse(IntakeApprovalOutcome.WrongState, "The request is not waiting for approval.");
        }

        if (action == IntakeApprovalAction.Claim)
        {
            return context.Request.ClaimedByContactId is { } claimer && claimer != context.CallerContactId
                ? Refuse(IntakeApprovalOutcome.AlreadyClaimed, "Someone else has claimed this request.")
                : null;
        }

        // Approve and reject need a claim (the decision is the claimer's): an unclaimed request is claimed first.
        return context.Request.ClaimedByContactId is null
            ? Refuse(IntakeApprovalOutcome.WrongState, "Claim the request before approving or rejecting it.")
            : null;
    }

    // ── The transitions ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Claims the request for the caller (self-assignment only). A claim the caller already holds is Done with no write.</summary>
    public virtual async Task<IntakeApprovalResult> ClaimAsync(HttpContext http, Guid requestId, CancellationToken ct)
    {
        var check = await CheckAsync(http, requestId, IntakeApprovalAction.Claim, ct).ConfigureAwait(false);
        if (!check.IsDone)
        {
            return check;
        }

        var context = check.Context!;
        if (context.Request.ClaimedByContactId == context.CallerContactId)
        {
            return check;
        }

        IntakeApprovalWriteResult written;
        try
        {
            written = await _store.ClaimRequestAsync(requestId, context.Request.RowVersion, context.CallerContactId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Intake approval: claim of request {RequestId} faulted ({ExceptionType})", requestId, ex.GetType().Name);
            return Refuse(IntakeApprovalOutcome.Unavailable, "The request could not be claimed right now; try again.");
        }

        _logger.LogInformation("Intake approval: claim of request {RequestId}: {Result}", requestId, written);
        return written switch
        {
            IntakeApprovalWriteResult.Written => check,
            // uac-r2 (1)c: a row changed since the read (412) is exactly one 409; nothing was written.
            IntakeApprovalWriteResult.Changed => Refuse(IntakeApprovalOutcome.AlreadyClaimed, "The request changed while it was being claimed; someone else may have claimed it."),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// Approves (the executor's write, after its preflight ran the same checks): records each output's current SPE version and
    /// <see cref="IntakeRunState.ApprovedOn"/> in one transaction conditional on the run and the request as read, then enqueues the run's continuation once. The checks run again
    /// first (uac-r2 (a): at approve, not only at claim).
    /// </summary>
    public virtual async Task<IntakeApprovalResult> ApproveAsync(HttpContext http, Guid requestId, CancellationToken ct)
    {
        var check = await CheckAsync(http, requestId, IntakeApprovalAction.Approve, ct).ConfigureAwait(false);
        if (!check.IsDone)
        {
            return check;
        }

        var run = check.Context!.Run!;
        if (run.State.ApprovedOn is null)
        {
            var versions = await CaptureVersionsAsync(run, ct).ConfigureAwait(false);
            if (versions is null)
            {
                return Refuse(IntakeApprovalOutcome.Unavailable, "The approved version of an output document could not be read; nothing was approved.");
            }

            IntakeApprovalWriteResult written;
            try
            {
                written = await _store.RecordApprovalAsync(run, check.Context.Request, versions, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // All or nothing; after a timeout the outcome may be unknown: a retry re-reads and finds the approval, or records it.
                _logger.LogWarning("Intake approval: approval of run {RunId} faulted ({ExceptionType})", run.RunId, ex.GetType().Name);
                return Refuse(IntakeApprovalOutcome.Unavailable, "The approval could not be saved right now; try again.");
            }

            if (written != IntakeApprovalWriteResult.Written)
            {
                _logger.LogInformation("Intake approval: approval of run {RunId} not written ({Result})", run.RunId, written);
                return Refuse(IntakeApprovalOutcome.WrongState, "The request changed while it was being approved; nothing was approved.");
            }

            _logger.LogInformation("Intake approval: run {RunId} approved with {VersionCount} output version(s)", run.RunId, versions.Count);
        }

        await EnqueueReturnOnceAsync(run, ct).ConfigureAwait(false);
        return check;
    }

    /// <summary>
    /// Rejects: the run ends Cancelled (never Failed) and the request goes back to In review, in one transaction conditional on both rows.
    /// The checks run again first. A rejection already recorded is Done with no write.
    /// </summary>
    public virtual async Task<IntakeApprovalResult> RejectAsync(HttpContext http, Guid requestId, CancellationToken ct)
    {
        var check = await CheckAsync(http, requestId, IntakeApprovalAction.Reject, ct).ConfigureAwait(false);
        if (!check.IsDone)
        {
            return check;
        }

        var run = check.Context!.Run!;
        if (run.State.RejectedOn is null)
        {
            IntakeApprovalWriteResult written;
            try
            {
                written = await _store.RecordRejectionAsync(run, check.Context.Request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Intake approval: rejection of run {RunId} faulted ({ExceptionType})", run.RunId, ex.GetType().Name);
                return Refuse(IntakeApprovalOutcome.Unavailable, "The rejection could not be saved right now; try again.");
            }

            if (written != IntakeApprovalWriteResult.Written)
            {
                _logger.LogInformation("Intake approval: rejection of run {RunId} not written ({Result})", run.RunId, written);
                return Refuse(IntakeApprovalOutcome.WrongState, "The request changed while it was being rejected; nothing was rejected.");
            }

            _logger.LogInformation("Intake approval: run {RunId} rejected", run.RunId);
        }

        return check;
    }

    /// <summary>The current SPE version id of every output document (ADR-007, through the SpeFileStore facade); null when any cannot be read.</summary>
    private async Task<Dictionary<Guid, string>?> CaptureVersionsAsync(IntakeRunSnapshot run, CancellationToken ct)
    {
        var outputs = run.State.Outputs!;
        var documents = outputs.DocumentIdsByRole.Values
            .Concat(outputs.OutputDocumentId is { } single ? [single] : [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var versions = new Dictionary<Guid, string>();
        foreach (var documentId in documents)
        {
            try
            {
                var location = await _store.ReadDocumentLocationAsync(documentId, ct).ConfigureAwait(false);
                var version = location is { } at ? await _files.GetCurrentVersionIdAsync(at.DriveId, at.ItemId, ct).ConfigureAwait(false) : null;
                if (string.IsNullOrWhiteSpace(version))
                {
                    _logger.LogWarning("Intake approval: run {RunId} output document {DocumentId} has no readable version", run.RunId, documentId);
                    return null;
                }

                versions[documentId] = version;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Intake approval: run {RunId} output document {DocumentId} version read faulted ({ExceptionType})", run.RunId, documentId, ex.GetType().Name);
                return null;
            }
        }

        return versions;
    }

    /// <summary>
    /// Sends the run's continuation unless the marker says it was sent; the marker is written after the send (task 014's pattern), so a
    /// failure in between costs one harmless extra <c>IntakeRun</c> job on the retry, never a lost one.
    /// </summary>
    private async Task EnqueueReturnOnceAsync(IntakeRunSnapshot read, CancellationToken ct)
    {
        var run = await _store.ReadRunAsync(read.RunId, ct).ConfigureAwait(false) ?? read;
        if (run.State.Markers.ContainsKey(ApprovalContinuedMarker) || IntakeRunStore.IsTerminalStatus(run.Status))
        {
            return;
        }

        var job = IntakeRunJobHandler.CreateJob(run.RequestId!.Value, run.RunId, run.CorrelationId ?? run.RunId.ToString("D"));
        job.IdempotencyKey = ReturnKey(run.RunId);
        await _jobs.SubmitJobAsync(job, ct).ConfigureAwait(false);
        await _store.MarkContinuationEnqueuedAsync(run.RunId, ApprovalContinuedMarker, job.IdempotencyKey, ct).ConfigureAwait(false);
        _logger.LogInformation("Intake approval: run {RunId} continuation enqueued", run.RunId);
    }

    private IntakeApprovalResult Forbid(Guid requestId, IntakeApprovalAction action, string why)
    {
        _logger.LogInformation("Intake approval: {Action} on request {RequestId} forbidden ({Why})", action, requestId, why);
        return Refuse(IntakeApprovalOutcome.Forbidden, "You cannot decide this request.");
    }

    private static IntakeApprovalResult NotFound() => new(IntakeApprovalOutcome.NotFound, Detail: "The requested record was not found.");

    private static IntakeApprovalResult Refuse(IntakeApprovalOutcome outcome, string detail) => new(outcome, Detail: detail);
}
