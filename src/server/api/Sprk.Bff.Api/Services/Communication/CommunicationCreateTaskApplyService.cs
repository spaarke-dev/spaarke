using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Job C — APPLY (FR-D5, task 034; backs FR-E5). Applies a <b>confirmed</b> pending create-task proposal (produced by
/// Job C / task 040 as an OPEN deadline-bearing <c>sprk_emailreviewlog</c> <c>Proposed</c> row, keyed by the
/// <c>__create_task__:</c> sentinel <c>sprk_targetfield</c>) by creating the <c>sprk_event</c> (type=task) it
/// describes and writing the append-only human-decision audit row.
/// </summary>
/// <remarks>
/// <para><b>Sibling of <see cref="CommunicationProposalApplyService"/> (Job B).</b> The 9-step apply contract is
/// mirrored exactly: caller resolved server-side (403 fail-closed — never app-only, never client-supplied), the
/// proposal must be an OPEN <c>Proposed</c> row (409 otherwise), it must still be open (no later terminal row — 409),
/// the cited source text must still exist against the live message (422, NFR-06), and exactly one append-only
/// <c>Applied</c> audit row is written (a mutate-without-audit path does not exist). Nothing deadline-bearing
/// auto-finalizes (ADR-015): the task is created only on this explicit human confirm.</para>
///
/// <para><b>Path B — create + PATCH under ONE audit row (owner decision 2026-08-05; ADR-013 reconciliation, task
/// 034).</b> <see cref="CreateTaskRequest"/> carries no impersonation field and ADR-013 / the task POML forbid
/// widening the <see cref="IActionSeam"/> facade, so the two writes are split by capability:
/// <list type="number">
///   <item>the <c>sprk_event</c> is CREATED via the blessed <see cref="IActionSeam.CreateTaskAsync"/> write core
///   (Subject / Description / DueDate / Regarding / Owner=assigned-to) — app-only, the only create the facade
///   exposes;</item>
///   <item>the remaining FR-E5 fields the human supplied at reconcile time
///   (<c>sprk_eventstatus</c> / <c>sprk_completeddate</c> / <c>sprk_basedate</c> / <c>sprk_finalduedate</c>) are
///   PATCHed on the created task via the caller-impersonated <see cref="IActionSeam.UpdateRecordAsync"/> — so those
///   deadline-bearing / status fields carry the confirming user's <c>modifiedby</c> and privilege intersection.</item>
/// </list>
/// BOTH writes are recorded under the SAME single append-only <c>Applied</c> audit row (actor = the confirming
/// human). No deadline-bearing field is dropped — <c>due</c> is set at create; base/final-due at PATCH; a PATCH
/// coercion failure is surfaced loudly (422) after the audit row is written, never silently. The confirming human is
/// on the record via <c>ownerid</c> (assigned-to), <c>modifiedby</c> (the impersonated PATCH), and the audit row;
/// only <c>createdby</c> is the app user (the consequence of the facade not exposing an impersonated create — see the
/// task-034 completion note).</para>
///
/// <para><b>Association gate (NFR-10 / ADR-024).</b> The task's regarding is the proposal's stored
/// <c>sprk_targetentity</c> + <c>sprk_targetrecordid</c> — the core-record association Job C resolved for the
/// communication. As with the Job B sibling, association-confirmation is enforced upstream (the queue-feed / reconcile
/// tab only offers a create-task apply for a communication whose association is resolved); this service does not add a
/// second association re-check.</para>
///
/// <para><b>Component Justification (CLAUDE.md §11):</b> Existing — no service applies a create-task proposal today
/// (<see cref="CommunicationProposalApplyService"/> applies Job B field-updates only; Job C's
/// <c>RunEmailCreateTaskAsync</c> auto-creates only NON-deadline candidates and never applies a confirmed one).
/// Extension — reuses the blessed <see cref="IActionSeam"/> write core, the shipped <see cref="CitationVerifier"/>,
/// and the same caller-resolve / open-walk / append-only-audit shape as the Job B sibling (a NEW sibling because the
/// Job B apply is field-update-specific: allow-list re-validation + a single UpdateRecord). Cost-of-doing-nothing —
/// without it a confirmed create-task proposal (the Tasks reconcile tab, FR-E5) has no endpoint to land on; Job C
/// deadline-bearing extraction is stored-but-inert.</para>
/// </remarks>
public interface ICommunicationCreateTaskApplyService
{
    /// <summary>
    /// Applies the confirmed create-task proposal identified by <paramref name="reviewLogId"/> (the open
    /// <c>Proposed</c> <c>sprk_emailreviewlog</c> row id from the queue feed) under the confirming caller's
    /// impersonation, and writes the <c>Applied</c> audit row. <paramref name="request"/> carries the human-supplied
    /// FR-E5 fields (assigned-to, status, completed-date, base-date, final-due-date, and an optional due-date
    /// override) collected by the reconcile tab; null = create from the extracted fields only. Throws
    /// <see cref="SdapProblemException"/> (RFC 7807) for an unresolved caller (403), a missing proposal (404), a
    /// non-create-task / malformed / unverifiable-citation proposal (422), an already-resolved proposal (409), a
    /// failed create (422), or a failed field PATCH after create (422).
    /// </summary>
    Task<ApplyCreateTaskResult> ApplyAsync(
        Guid reviewLogId, ApplyCreateTaskRequest? request, ClaimsPrincipal? caller, CancellationToken ct);

    /// <summary>
    /// Creates an AD-HOC task (FR-E5 "+ New task", task 056b) regarding the confirmed record — a task the reviewer
    /// AUTHORED, not proposed by the engine — through the SAME create-task path as an applied proposal:
    /// <see cref="IActionSeam.CreateTaskAsync"/> + the caller-impersonated FR-E5 PATCH + EXACTLY ONE append-only
    /// <c>Applied</c> audit row (actor = the confirming human). There is NO proposal row, NO citation (nothing was
    /// extracted, so no NFR-06 re-verify), and NO open-walk. Association gating is the UI's (NFR-10): the caller
    /// supplies the confirmed record as the regarding, matching the applied-proposal sibling's documented posture (it
    /// also does not add a second server-side association re-check — see the class remarks). Throws
    /// <see cref="SdapProblemException"/> (RFC 7807) for an unresolved caller (403), a blank subject or missing/blank
    /// regarding (422), a failed create (422), or a failed FR-E5 PATCH after create (422).
    /// </summary>
    Task<CreateAdHocTaskResult> CreateAdHocAsync(
        Guid communicationId, CreateAdHocTaskRequest request, ClaimsPrincipal? caller, CancellationToken ct);

    /// <summary>
    /// UNDO a just-created task (email-communication-intelligence-r2 B2.2): soft-cancels the <c>sprk_event</c>
    /// identified by <paramref name="taskId"/> (the <c>CreatedTaskId</c> the apply/ad-hoc create returned) by setting
    /// <c>sprk_eventstatus</c> = Cancelled UNDER THE CALLER'S IMPERSONATION, then writes ONE append-only compensating
    /// audit row for <paramref name="communicationId"/> (provenance + append-only-audit symmetry with the field-undo).
    /// Soft-cancel (not hard delete) so the write is gated by the caller's Dataverse access (a caller who cannot write
    /// the event cannot cancel it — no app-only "delete any event by id" hole), it is reversible, and it preserves the
    /// event's own audit trail (<c>modifiedby</c> = the caller). Throws <see cref="SdapProblemException"/> for an
    /// unresolved caller (403), a failed cancel write (422 — caller lacks access, or the event no longer exists), or a
    /// failed audit write after the cancel (500).
    /// </summary>
    Task<UndoCreateTaskResult> UndoCreateTaskAsync(
        Guid communicationId, Guid taskId, ClaimsPrincipal? caller, CancellationToken ct);
}

/// <summary>Human-supplied FR-E5 fields for a create-task apply (task 056 reconcile tab). All optional — an omitted
/// field is simply not set on the created task. Dates are <see cref="DateOnly"/> (the <c>sprk_event</c> date columns
/// are Date-Only).</summary>
public sealed record ApplyCreateTaskRequest
{
    /// <summary>Overrides the extracted deadline; falls back to the proposal's <c>dueDate</c> when null.</summary>
    public DateOnly? DueDate { get; init; }
    public DateOnly? BaseDate { get; init; }
    public DateOnly? FinalDueDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary><c>sprk_eventstatus</c> Choice value (e.g. Completed=2 for the create-and-complete case).</summary>
    public int? Status { get; init; }

    /// <summary>The task Owner (<c>ownerid</c> systemuser) — set at create via
    /// <see cref="CreateTaskRequest.OwnerId"/>.</summary>
    public Guid? AssignedTo { get; init; }
}

/// <summary>Result of a successful <see cref="ICommunicationCreateTaskApplyService.ApplyAsync"/>.</summary>
public sealed record ApplyCreateTaskResult(
    Guid ReviewLogId,
    Guid CreatedTaskId,
    Guid AuditLogId,
    string RegardingEntity,
    Guid RegardingRecordId,
    IReadOnlyList<string> FieldsPatched);

/// <summary>Human-authored fields for an AD-HOC create-task (FR-E5 "+ New task", task 056b). Unlike
/// <see cref="ApplyCreateTaskRequest"/> there is no backing proposal, so <see cref="Subject"/> + the regarding
/// (<see cref="RegardingEntity"/> / <see cref="RegardingRecordId"/> — the confirmed record, NFR-10) are REQUIRED; the
/// rest mirror the applied-proposal FR-E5 field set. Dates are <see cref="DateOnly"/> (the <c>sprk_event</c> date
/// columns are Date-Only).</summary>
public sealed record CreateAdHocTaskRequest
{
    /// <summary>The task name (<c>sprk_event</c> Subject) — REQUIRED for an ad-hoc task.</summary>
    public string? Subject { get; init; }
    public string? Description { get; init; }

    /// <summary>The confirmed record's entity logical name (the task's regarding) — REQUIRED (NFR-10).</summary>
    public string? RegardingEntity { get; init; }
    /// <summary>The confirmed record's id (the task's regarding) — REQUIRED (NFR-10).</summary>
    public Guid? RegardingRecordId { get; init; }

    public DateOnly? DueDate { get; init; }
    public DateOnly? BaseDate { get; init; }
    public DateOnly? FinalDueDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary><c>sprk_eventstatus</c> Choice value (e.g. Completed=2 for the create-and-complete case).</summary>
    public int? Status { get; init; }
    /// <summary>The task Owner (<c>ownerid</c> systemuser).</summary>
    public Guid? AssignedTo { get; init; }
}

/// <summary>Result of a successful <see cref="ICommunicationCreateTaskApplyService.CreateAdHocAsync"/> (task 056b).</summary>
public sealed record CreateAdHocTaskResult(
    Guid CreatedTaskId,
    Guid AuditLogId,
    string RegardingEntity,
    Guid RegardingRecordId,
    IReadOnlyList<string> FieldsPatched);

/// <summary>Result of a successful <see cref="ICommunicationCreateTaskApplyService.UndoCreateTaskAsync"/> (B2.2). The
/// task was soft-cancelled (<c>sprk_eventstatus</c> = Cancelled) — carries the task id + the new status value.</summary>
public sealed record UndoCreateTaskResult(Guid TaskId, int NewStatus);

/// <inheritdoc />
public sealed class CommunicationCreateTaskApplyService : ICommunicationCreateTaskApplyService
{
    // As-built sprk_emailreviewlog option-set integers (task 012 verification; mirrors the production constants in
    // CommunicationProposalApplyService / CommunicationQueueFeedService / CommunicationEnrichmentService).
    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApproved = 100000002;
    private const int ReviewActionOverriden = 100000003;
    private const int ReviewActionDismissed = 100000004;
    private const int ReviewActionApplied = 100000005;
    private const int ReviewActorTypeHuman = 100000001; // as-built: Machine=100000000, Human=100000001

    private const string ReviewLogEntity = "sprk_emailreviewlog";
    private const string EventEntity = "sprk_event";

    // sprk_event FR-E5 fields PATCHed under impersonation (dataverse-describe-verified 2026-08-06): base/final-due/
    // completed are Date-Only; eventstatus is a Choice (Completed=2). due + owner are set at create via the facade.
    private const string EventBaseDateField = "sprk_basedate";
    private const string EventFinalDueDateField = "sprk_finalduedate";
    private const string EventCompletedDateField = "sprk_completeddate";
    private const string EventStatusField = "sprk_eventstatus";

    // sprk_eventstatus Choice value for a soft-cancel undo (canonical Events status set: Draft=0, Open=1,
    // Completed=2, Closed=3, On Hold=4, Cancelled=5, Reassigned=6, Archived=7 — mirrors the client EVENT_STATUS map).
    private const int EventStatusCancelled = 5;

    /// <summary>The <c>sprk_targetfield</c> sentinel PREFIX Job C writes for a create-task proposal — mirrored from
    /// <c>CommunicationEnrichmentService.CreateTaskSentinelFieldPrefix</c> (duplicated with a "mirrored from" note the
    /// same way the option-set integers are, rather than coupling this apply path to the enrichment writer).</summary>
    private const string CreateTaskSentinelFieldPrefix = "__create_task__:";

    private static readonly JsonSerializerOptions SuggestionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IActionSeam _actionSeam;
    private readonly ICommunicationEnvelopeReader _envelopeReader;
    private readonly ILogger<CommunicationCreateTaskApplyService> _logger;

    public CommunicationCreateTaskApplyService(
        ICallerSystemUserResolver callerResolver,
        IGenericEntityService genericEntityService,
        IActionSeam actionSeam,
        ICommunicationEnvelopeReader envelopeReader,
        ILogger<CommunicationCreateTaskApplyService> logger)
    {
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _actionSeam = actionSeam ?? throw new ArgumentNullException(nameof(actionSeam));
        _envelopeReader = envelopeReader ?? throw new ArgumentNullException(nameof(envelopeReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApplyCreateTaskResult> ApplyAsync(
        Guid reviewLogId, ApplyCreateTaskRequest? request, ClaimsPrincipal? caller, CancellationToken ct)
    {
        // (1) Resolve the confirming caller server-side; fail closed (403) — NEVER app-only, never client-supplied.
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "CALLER_NOT_RESOLVED",
                title: "Caller Not Resolved",
                detail: "The caller could not be resolved to a Dataverse systemuser; the apply is refused (fail closed — the write must run under the confirming user's identity, never app-only).",
                statusCode: 403);
        }

        // (2) Load the proposal row.
        var row = await LoadReviewLogRowAsync(reviewLogId, ct).ConfigureAwait(false);
        if (row is null)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_NOT_FOUND",
                title: "Proposal Not Found",
                detail: $"No sprk_emailreviewlog proposal was found for id {reviewLogId}.",
                statusCode: 404);
        }

        var communicationRef = row.GetAttributeValue<EntityReference>("sprk_communication");
        var targetEntity = row.GetAttributeValue<string>("sprk_targetentity")?.Trim();
        var sentinelField = row.GetAttributeValue<string>("sprk_targetfield")?.Trim();
        var targetRecordIdRaw = row.GetAttributeValue<string>("sprk_targetrecordid")?.Trim();
        var action = row.GetAttributeValue<OptionSetValue>("sprk_action")?.Value;

        // (2a) It MUST be a create-task proposal — refuse a Job B field-update reviewLogId POSTed here (a caller must
        //      not cross the two apply endpoints).
        if (string.IsNullOrEmpty(sentinelField)
            || !sentinelField.StartsWith(CreateTaskSentinelFieldPrefix, StringComparison.Ordinal))
        {
            throw new SdapProblemException(
                code: "PROPOSAL_NOT_CREATE_TASK",
                title: "Proposal Not A Create-Task Proposal",
                detail: "The referenced review-log row is not a Job C create-task proposal (wrong apply endpoint).",
                statusCode: 422);
        }

        if (communicationRef is null
            || string.IsNullOrWhiteSpace(targetEntity)
            || !Guid.TryParse(targetRecordIdRaw, out var regardingRecordId)
            || regardingRecordId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_MALFORMED",
                title: "Proposal Malformed",
                detail: "The proposal row is missing a communication, regarding entity, or a parseable regarding record id.",
                statusCode: 422);
        }

        if (action != ReviewActionProposed)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_NOT_PENDING",
                title: "Proposal Not Pending",
                detail: "The referenced review-log row is not a pending Proposed row.",
                statusCode: 409);
        }

        // (3) Confirm the proposal is still OPEN (no later terminal row for the same (communication, entity, sentinel)).
        await EnsureStillOpenAsync(reviewLogId, communicationRef.Id, targetEntity!, sentinelField, ct).ConfigureAwait(false);

        // (4) Parse the machine create-task suggestion (subject/description/dueDate + citation).
        var suggestionJson = row.GetAttributeValue<string>("sprk_aisuggestion");
        var suggestion = ParseSuggestion(suggestionJson);
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.Subject))
        {
            throw new SdapProblemException(
                code: "PROPOSAL_MALFORMED",
                title: "Proposal Malformed",
                detail: "The proposal's sprk_aisuggestion payload is missing or has no task subject.",
                statusCode: 422);
        }

        // (5) Re-verify the cited source text still exists against the live message (NFR-06 / ADR-015 — nothing
        //     deadline-bearing is finalized without a still-verifiable citation).
        var (message, _) = await _envelopeReader.ReconstructEnvelopeAsync(communicationRef.Id, ct).ConfigureAwait(false);
        var sourceText = CitationVerifier.BuildSourceText(message.Subject, message.BodyText, message.AttachmentText);
        if (!CitationVerifier.IsCitedTextPresent(sourceText, suggestion.Citation?.QuotedText))
        {
            throw new SdapProblemException(
                code: "CITATION_NOT_FOUND",
                title: "Citation Not Verifiable",
                detail: "The proposal's cited source text no longer exists in the communication; the apply is refused (NFR-06).",
                statusCode: 422);
        }

        // (6) CREATE the sprk_event via the blessed write core (Subject/Description/DueDate/Regarding/Owner). App-only:
        //     the facade exposes no impersonated create and ADR-013 forbids widening it (see class remarks); the
        //     confirming user is attributed via ownerid + the impersonated PATCH below + the audit row.
        var dueDate = request?.DueDate ?? TryParseDateOnly(suggestion.DueDate);
        var createResult = await _actionSeam.CreateTaskAsync(
            new CreateTaskRequest
            {
                Subject = Truncate(suggestion.Subject!, 200),
                Description = suggestion.Description,
                DueDate = dueDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                RegardingObjectId = regardingRecordId,
                RegardingObjectType = targetEntity,
                OwnerId = request?.AssignedTo,
            },
            ct).ConfigureAwait(false);

        if (!createResult.Success || createResult.TaskId == Guid.Empty)
        {
            // No mutation occurred (or a degraded create) — refuse without an audit row (nothing to audit).
            throw new SdapProblemException(
                code: "CREATE_TASK_FAILED",
                title: "Create Task Failed",
                detail: createResult.Error ?? "The task (sprk_event) could not be created.",
                statusCode: 422);
        }

        var createdTaskId = createResult.TaskId;

        // (7) PATCH the remaining FR-E5 fields on the created task UNDER THE CONFIRMING USER'S IMPERSONATION. String
        //     mappings let UpdateRecordActionCore's metadata-driven coercion resolve the Choice status fail-loud and
        //     pass the Date-Only columns verbatim (the same pattern the Job B apply uses).
        var mappings = BuildPatchMappings(request);
        UpdateRecordResult? patchResult = null;
        if (mappings.Count > 0)
        {
            patchResult = await _actionSeam.UpdateRecordAsync(
                new UpdateRecordRequest
                {
                    EntityLogicalName = EventEntity,
                    RecordId = createdTaskId,
                    FieldMappings = mappings,
                    ImpersonateSystemUserId = callerSystemUserId,
                },
                ct).ConfigureAwait(false);
        }

        var patchFailed = patchResult is { Success: false };
        var fieldsPatched = patchResult is { Success: true }
            ? patchResult.FieldsUpdated
            : (IReadOnlyList<string>)Array.Empty<string>();

        // (8) Write EXACTLY ONE append-only Applied audit row (actor = the confirming human) covering BOTH writes. The
        //     task WAS created, so this always runs — a mutate-without-audit path does not exist. If the audit write
        //     itself fails we surface it loudly (the mutation details are logged) rather than swallowing it.
        Guid auditLogId;
        try
        {
            auditLogId = await WriteAppliedAuditRowAsync(
                communicationRef.Id, targetEntity!, regardingRecordId, sentinelField, createdTaskId,
                suggestion, fieldsPatched, patchResult?.Error, callerSystemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Job C apply CREATED sprk_event {CreatedTaskId} (regarding {Entity}({RecordId})) for caller {Caller} from proposal {ReviewLogId} but the Applied audit row write FAILED. Manual audit reconciliation required.",
                createdTaskId, targetEntity, regardingRecordId, callerSystemUserId, reviewLogId);
            throw new SdapProblemException(
                code: "AUDIT_WRITE_FAILED",
                title: "Audit Write Failed",
                detail: "The task was created but the audit row could not be written; this has been logged for reconciliation.",
                statusCode: 500);
        }

        // (9) If the FR-E5 PATCH failed, the task exists (and is audited) but its status/deadline fields did not
        //     apply — surface loudly (422), NEVER silently drop a deadline-bearing field (ADR-015).
        if (patchFailed)
        {
            _logger.LogWarning(
                "Job C apply CREATED sprk_event {CreatedTaskId} + audit row {AuditLogId}, but the FR-E5 field PATCH failed ({Error}) for caller {Caller} from proposal {ReviewLogId}.",
                createdTaskId, auditLogId, patchResult?.Error, callerSystemUserId, reviewLogId);
            throw new SdapProblemException(
                code: "FIELD_PATCH_FAILED",
                title: "Field Update Failed",
                detail: $"The task was created and audited, but its status/deadline fields could not be applied: {patchResult?.Error}",
                statusCode: 422);
        }

        _logger.LogInformation(
            "Job C apply: created sprk_event {CreatedTaskId} (regarding {Entity}({RecordId})) under caller {Caller} from proposal {ReviewLogId}; {PatchedCount} FR-E5 field(s) patched under impersonation; audit row {AuditLogId} written.",
            createdTaskId, targetEntity, regardingRecordId, callerSystemUserId, reviewLogId, fieldsPatched.Count, auditLogId);

        return new ApplyCreateTaskResult(
            reviewLogId, createdTaskId, auditLogId, targetEntity!, regardingRecordId, fieldsPatched);
    }

    /// <inheritdoc />
    public async Task<CreateAdHocTaskResult> CreateAdHocAsync(
        Guid communicationId, CreateAdHocTaskRequest request, ClaimsPrincipal? caller, CancellationToken ct)
    {
        // (1) Resolve the confirming caller server-side; fail closed (403) — NEVER app-only, never client-supplied.
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "CALLER_NOT_RESOLVED",
                title: "Caller Not Resolved",
                detail: "The caller could not be resolved to a Dataverse systemuser; the ad-hoc create is refused (fail closed — the write must be attributed to the confirming user, never app-only).",
                statusCode: 403);
        }

        // (2) Validate the request shape. There is no proposal to derive from, so subject + the regarding (the
        //     confirmed record, NFR-10) are REQUIRED. Association gating is the UI's — matching the applied-proposal
        //     sibling's posture (no second server-side association re-check; see class remarks).
        var subject = request.Subject?.Trim();
        var regardingEntity = request.RegardingEntity?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new SdapProblemException(
                code: "TASK_SUBJECT_REQUIRED",
                title: "Task Subject Required",
                detail: "An ad-hoc task requires a non-empty subject.",
                statusCode: 422);
        }
        if (string.IsNullOrWhiteSpace(regardingEntity)
            || request.RegardingRecordId is not { } regardingRecordId
            || regardingRecordId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "REGARDING_REQUIRED",
                title: "Regarding Record Required",
                detail: "An ad-hoc task must attach to the confirmed record (NFR-10): a regarding entity + record id are required.",
                statusCode: 422);
        }
        if (communicationId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "COMMUNICATION_REQUIRED",
                title: "Communication Required",
                detail: "A communication id is required to link the ad-hoc task's audit row.",
                statusCode: 422);
        }

        // (3) CREATE the sprk_event via the blessed write core (app-only create, same as the applied-proposal path —
        //     the confirming user is attributed via ownerid + the impersonated PATCH + the audit row).
        var createResult = await _actionSeam.CreateTaskAsync(
            new CreateTaskRequest
            {
                Subject = Truncate(subject, 200),
                Description = request.Description,
                DueDate = request.DueDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                RegardingObjectId = regardingRecordId,
                RegardingObjectType = regardingEntity,
                OwnerId = request.AssignedTo,
            },
            ct).ConfigureAwait(false);

        if (!createResult.Success || createResult.TaskId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "CREATE_TASK_FAILED",
                title: "Create Task Failed",
                detail: createResult.Error ?? "The ad-hoc task (sprk_event) could not be created.",
                statusCode: 422);
        }

        var createdTaskId = createResult.TaskId;

        // (4) PATCH the remaining FR-E5 fields under the confirming user's impersonation (same as the applied path).
        var mappings = BuildPatchMappings(request.BaseDate, request.FinalDueDate, request.CompletedDate, request.Status);
        UpdateRecordResult? patchResult = null;
        if (mappings.Count > 0)
        {
            patchResult = await _actionSeam.UpdateRecordAsync(
                new UpdateRecordRequest
                {
                    EntityLogicalName = EventEntity,
                    RecordId = createdTaskId,
                    FieldMappings = mappings,
                    ImpersonateSystemUserId = callerSystemUserId,
                },
                ct).ConfigureAwait(false);
        }

        var patchFailed = patchResult is { Success: false };
        var fieldsPatched = patchResult is { Success: true }
            ? patchResult.FieldsUpdated
            : (IReadOnlyList<string>)Array.Empty<string>();

        // (5) Write EXACTLY ONE append-only Applied audit row (actor = the confirming human). The task WAS created, so
        //     this always runs — a mutate-without-audit path does not exist.
        Guid auditLogId;
        try
        {
            auditLogId = await WriteAdHocAppliedAuditRowAsync(
                communicationId, regardingEntity, regardingRecordId, subject, request.Description, createdTaskId,
                fieldsPatched, patchResult?.Error, callerSystemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Job C ad-hoc create CREATED sprk_event {CreatedTaskId} (regarding {Entity}({RecordId})) for caller {Caller} on communication {CommunicationId} but the Applied audit row write FAILED. Manual audit reconciliation required.",
                createdTaskId, regardingEntity, regardingRecordId, callerSystemUserId, communicationId);
            throw new SdapProblemException(
                code: "AUDIT_WRITE_FAILED",
                title: "Audit Write Failed",
                detail: "The ad-hoc task was created but the audit row could not be written; this has been logged for reconciliation.",
                statusCode: 500);
        }

        // (6) If the FR-E5 PATCH failed, the task exists (and is audited) but its status/deadline fields did not
        //     apply — surface loudly (422), NEVER silently drop a deadline-bearing field (ADR-015).
        if (patchFailed)
        {
            _logger.LogWarning(
                "Job C ad-hoc create CREATED sprk_event {CreatedTaskId} + audit row {AuditLogId}, but the FR-E5 field PATCH failed ({Error}) for caller {Caller}.",
                createdTaskId, auditLogId, patchResult?.Error, callerSystemUserId);
            throw new SdapProblemException(
                code: "FIELD_PATCH_FAILED",
                title: "Field Update Failed",
                detail: $"The ad-hoc task was created and audited, but its status/deadline fields could not be applied: {patchResult?.Error}",
                statusCode: 422);
        }

        _logger.LogInformation(
            "Job C ad-hoc create: created sprk_event {CreatedTaskId} (regarding {Entity}({RecordId})) under caller {Caller} on communication {CommunicationId}; {PatchedCount} FR-E5 field(s) patched; audit row {AuditLogId} written.",
            createdTaskId, regardingEntity, regardingRecordId, callerSystemUserId, communicationId, fieldsPatched.Count, auditLogId);

        return new CreateAdHocTaskResult(createdTaskId, auditLogId, regardingEntity, regardingRecordId, fieldsPatched);
    }

    /// <summary>The <c>sprk_targetfield</c> marker written on the compensating task-undo audit row — a distinct
    /// sentinel so it never collides with a create-task proposal's <c>(communication, targetEntity, targetField)</c>
    /// open-walk key.</summary>
    private const string UndoTaskSentinelField = "__undo_task__";

    /// <inheritdoc />
    public async Task<UndoCreateTaskResult> UndoCreateTaskAsync(
        Guid communicationId, Guid taskId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        // (1) Resolve the caller server-side; fail closed (403). The cancel runs under the caller's identity so
        //     Dataverse gates it (a caller who cannot write the event cannot cancel it) — never app-only.
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "CALLER_NOT_RESOLVED",
                title: "Caller Not Resolved",
                detail: "The caller could not be resolved to a Dataverse systemuser; the task undo is refused (fail closed — the cancel must run under the caller's identity).",
                statusCode: 403);
        }

        // (2) Soft-cancel via the SAME blessed impersonated write core the create PATCH uses (String mapping →
        //     metadata-driven Choice coercion). Preserves the event + its audit trail; reversible. Impersonation is
        //     the authorization gate — a caller who cannot write the event gets 422 (no app-only "cancel any event").
        var updateResult = await _actionSeam.UpdateRecordAsync(
            new UpdateRecordRequest
            {
                EntityLogicalName = EventEntity,
                RecordId = taskId,
                FieldMappings = new[]
                {
                    new ActionFieldMapping(EventStatusField, ActionFieldType.String,
                        EventStatusCancelled.ToString(CultureInfo.InvariantCulture)),
                },
                ImpersonateSystemUserId = callerSystemUserId,
            },
            ct).ConfigureAwait(false);

        if (!updateResult.Success)
        {
            throw new SdapProblemException(
                code: "TASK_UNDO_FAILED",
                title: "Task Undo Failed",
                detail: updateResult.Error ?? "The created task could not be cancelled (it may no longer exist or the caller lacks access).",
                statusCode: 422);
        }

        // (3) Write EXACTLY ONE append-only compensating audit row (Dismissed, actor = the caller) tying the cancel
        //     to the communication + the cancelled task — symmetric with the Job B field-undo (W1) and the append-only
        //     -audit culture; also the provenance record for the cancel (W2). Mutate-without-audit does not exist:
        //     the event was cancelled, so a failed audit write surfaces loudly (500) for reconciliation.
        Guid auditLogId;
        try
        {
            var entity = new Entity(ReviewLogEntity)
            {
                ["sprk_name"] = Truncate($"Cancelled task: {taskId}", 850),
                ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeHuman),
                ["sprk_action"] = new OptionSetValue(ReviewActionDismissed),
                ["sprk_actor"] = Truncate(callerSystemUserId.ToString(), 200),
                ["sprk_targetentity"] = Truncate(EventEntity, 100),
                ["sprk_targetrecordid"] = Truncate(taskId.ToString(), 100),
                ["sprk_targetfield"] = Truncate(UndoTaskSentinelField, 100),
            };
            auditLogId = await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Job C undo CANCELLED sprk_event {TaskId} (communication {CommunicationId}) for caller {Caller} but the compensating audit row write FAILED. Manual audit reconciliation required.",
                taskId, communicationId, callerSystemUserId);
            throw new SdapProblemException(
                code: "AUDIT_WRITE_FAILED",
                title: "Audit Write Failed",
                detail: "The task was cancelled but the audit row could not be written; this has been logged for reconciliation.",
                statusCode: 500);
        }

        _logger.LogInformation(
            "Job C undo: sprk_event {TaskId} soft-cancelled (sprk_eventstatus=Cancelled) under caller {Caller} impersonation; audit row {AuditLogId} written.",
            taskId, callerSystemUserId, auditLogId);

        return new UndoCreateTaskResult(taskId, EventStatusCancelled);
    }

    private static List<ActionFieldMapping> BuildPatchMappings(ApplyCreateTaskRequest? request) =>
        request is null
            ? new List<ActionFieldMapping>()
            : BuildPatchMappings(request.BaseDate, request.FinalDueDate, request.CompletedDate, request.Status);

    /// <summary>Builds the FR-E5 impersonated-PATCH field mappings (base/final-due/completed dates + status Choice).
    /// Shared by the applied-proposal path and the ad-hoc path so both write the identical field set the same way.</summary>
    private static List<ActionFieldMapping> BuildPatchMappings(
        DateOnly? baseDate, DateOnly? finalDueDate, DateOnly? completedDate, int? status)
    {
        var mappings = new List<ActionFieldMapping>();
        if (baseDate is { } b)
            mappings.Add(new ActionFieldMapping(EventBaseDateField, ActionFieldType.String, FormatDate(b)));
        if (finalDueDate is { } f)
            mappings.Add(new ActionFieldMapping(EventFinalDueDateField, ActionFieldType.String, FormatDate(f)));
        if (completedDate is { } c)
            mappings.Add(new ActionFieldMapping(EventCompletedDateField, ActionFieldType.String, FormatDate(c)));
        if (status is { } s)
            mappings.Add(new ActionFieldMapping(EventStatusField, ActionFieldType.String, s.ToString(CultureInfo.InvariantCulture)));

        return mappings;
    }

    private async Task<Entity?> LoadReviewLogRowAsync(Guid reviewLogId, CancellationToken ct)
    {
        try
        {
            return await _genericEntityService.RetrieveAsync(
                ReviewLogEntity,
                reviewLogId,
                new[]
                {
                    "sprk_communication", "sprk_action", "sprk_targetentity", "sprk_targetrecordid",
                    "sprk_targetfield", "sprk_confidence", "sprk_sourceref", "sprk_aisuggestion",
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "sprk_emailreviewlog {ReviewLogId} could not be retrieved (treated as not-found).", reviewLogId);
            return null;
        }
    }

    /// <summary>
    /// Walks all review-log rows for the proposal's <c>(communication, targetEntity, sentinelField)</c> key
    /// chronologically; the currently-open Proposed row MUST be <paramref name="reviewLogId"/>. A later terminal
    /// (Approved/Applied/Dismissed/Overriden) means it was already resolved (409) — the SAME idempotency + double-apply
    /// guard as the Job B sibling, generalized over the create-task sentinel field.
    /// <para>
    /// <b>Concurrency (accepted, narrowed limitation — same posture as the Job B sibling):</b> this is a read-then-act
    /// check with no Dataverse transaction spanning walk → create → PATCH → audit, so two TRULY-CONCURRENT applies of
    /// the same proposal could both pass here. SEQUENTIAL re-apply IS blocked (the first Applied row closes the
    /// proposal). The blast radius of the race is one duplicate task + one extra audit row (both under the confirming
    /// user's identity) — not a privilege or authorization bypass.
    /// </para>
    /// </summary>
    private async Task EnsureStillOpenAsync(
        Guid reviewLogId, Guid communicationId, string targetEntity, string sentinelField, CancellationToken ct)
    {
        var query = new QueryExpression(ReviewLogEntity)
        {
            ColumnSet = new ColumnSet("sprk_action", "sprk_targetentity", "sprk_targetfield", "createdon"),
        };
        query.Criteria.AddCondition("sprk_communication", ConditionOperator.Equal, communicationId);
        query.Criteria.AddCondition("sprk_targetentity", ConditionOperator.Equal, targetEntity);
        query.Criteria.AddCondition("sprk_targetfield", ConditionOperator.Equal, sentinelField);
        query.AddOrder("createdon", OrderType.Ascending);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        Guid openProposedId = Guid.Empty;
        foreach (var r in result.Entities)
        {
            switch (r.GetAttributeValue<OptionSetValue>("sprk_action")?.Value)
            {
                case ReviewActionProposed:
                    openProposedId = r.Id;
                    break;
                case ReviewActionApproved:
                case ReviewActionApplied:
                case ReviewActionDismissed:
                case ReviewActionOverriden:
                    openProposedId = Guid.Empty;
                    break;
            }
        }

        if (openProposedId != reviewLogId)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_ALREADY_RESOLVED",
                title: "Proposal Already Resolved",
                detail: "This create-task proposal is no longer the open pending proposal (it was already applied, dismissed, or superseded).",
                statusCode: 409);
        }
    }

    private async Task<Guid> WriteAppliedAuditRowAsync(
        Guid communicationId, string targetEntity, Guid regardingRecordId, string sentinelField, Guid createdTaskId,
        CreateTaskSuggestion suggestion, IReadOnlyList<string> fieldsPatched, string? patchError,
        Guid callerSystemUserId, CancellationToken ct)
    {
        // The Applied audit row carries the SAME (communication, targetEntity, sentinelField) key as the Proposed row
        // so the open-walk closes the proposal; the created task id + patched fields ride in the suggestion JSON so the
        // row is self-contained.
        var applied = new
        {
            kind = "create-task",
            subject = suggestion.Subject,
            createdTaskId,
            regardingObjectType = targetEntity,
            regardingObjectId = regardingRecordId,
            fieldsPatched,
            patchError,
            citation = new
            {
                source = suggestion.Citation?.Source,
                locator = suggestion.Citation?.Locator,
                quotedText = suggestion.Citation?.QuotedText,
            },
            confidence = suggestion.Confidence,
        };
        var appliedJson = JsonSerializer.Serialize(applied);

        var name = $"Created task: {suggestion.Subject}";
        var entity = new Entity(ReviewLogEntity)
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeHuman),
            ["sprk_action"] = new OptionSetValue(ReviewActionApplied),
            ["sprk_actor"] = Truncate(callerSystemUserId.ToString(), 200),
            ["sprk_targetentity"] = Truncate(targetEntity, 100),
            ["sprk_targetrecordid"] = Truncate(regardingRecordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_sourceref"] = Truncate(suggestion.Citation?.Locator ?? suggestion.Citation?.Source ?? string.Empty, 1000),
            ["sprk_aisuggestion"] = appliedJson,
        };

        if (suggestion.Confidence.HasValue)
            entity["sprk_confidence"] = (decimal)suggestion.Confidence.Value;

        return await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the single append-only <c>Applied</c> audit row for an AD-HOC create (task 056b). Mirrors
    /// <see cref="WriteAppliedAuditRowAsync"/> but there is no AI proposal — no citation, no confidence — so the row
    /// records the human-authored task and an <c>adhoc:true</c> marker in the self-contained JSON. The
    /// <c>sprk_targetfield</c> carries the create-task sentinel + <c>adhoc</c> so the row is categorized with the
    /// applied-proposal rows without colliding with any proposal's <c>(communication, entity, sentinel)</c> key.
    /// </summary>
    private async Task<Guid> WriteAdHocAppliedAuditRowAsync(
        Guid communicationId, string regardingEntity, Guid regardingRecordId, string subject, string? description,
        Guid createdTaskId, IReadOnlyList<string> fieldsPatched, string? patchError, Guid callerSystemUserId,
        CancellationToken ct)
    {
        var applied = new
        {
            kind = "create-task-adhoc",
            adhoc = true,
            subject,
            description,
            createdTaskId,
            regardingObjectType = regardingEntity,
            regardingObjectId = regardingRecordId,
            fieldsPatched,
            patchError,
        };
        var appliedJson = JsonSerializer.Serialize(applied);

        var entity = new Entity(ReviewLogEntity)
        {
            ["sprk_name"] = Truncate($"Created ad-hoc task: {subject}", 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeHuman),
            ["sprk_action"] = new OptionSetValue(ReviewActionApplied),
            ["sprk_actor"] = Truncate(callerSystemUserId.ToString(), 200),
            ["sprk_targetentity"] = Truncate(regardingEntity, 100),
            ["sprk_targetrecordid"] = Truncate(regardingRecordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(CreateTaskSentinelFieldPrefix + "adhoc", 100),
            ["sprk_aisuggestion"] = appliedJson,
        };

        return await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);
    }

    private static CreateTaskSuggestion? ParseSuggestion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CreateTaskSuggestion>(json, SuggestionJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateOnly? TryParseDateOnly(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record CreateTaskSuggestion
    {
        [JsonPropertyName("kind")] public string? Kind { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("dueDate")] public string? DueDate { get; init; }
        [JsonPropertyName("regardingObjectType")] public string? RegardingObjectType { get; init; }
        [JsonPropertyName("regardingObjectId")] public Guid? RegardingObjectId { get; init; }
        [JsonPropertyName("citation")] public CreateTaskCitation? Citation { get; init; }
        [JsonPropertyName("reason")] public string? Reason { get; init; }
        [JsonPropertyName("confidence")] public double? Confidence { get; init; }
        [JsonPropertyName("requireConfirm")] public bool? RequireConfirm { get; init; }
    }

    private sealed record CreateTaskCitation
    {
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("locator")] public string? Locator { get; init; }
        [JsonPropertyName("quotedText")] public string? QuotedText { get; init; }
    }
}
