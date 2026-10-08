using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Thin orchestrator for Office add-in operations.
/// Delegates to focused services: OfficeEmailEnricher, OfficeDocumentPersistence,
/// OfficeJobQueue, and OfficeStorageUploader.
/// </summary>
/// <remarks>
/// <para>
/// This service orchestrates the Office add-in backend workflows:
/// - Save: enriches content, uploads to SPE, persists to Dataverse, queues finalization
/// - Job status: queries Dataverse/in-memory store for job progress
/// - SSE streaming: real-time job status updates via Redis pub/sub
/// - Quick-create and To Do: record creation
/// - Search and matter types: delegated to <see cref="OfficeSearchService"/>, which owns every Dataverse
///   read this add-in makes (task 059)
/// </para>
/// <para>
/// Per ADR-001, heavy processing (SPE upload, AI processing) is delegated to background workers.
/// This service focuses on fast job creation (target: &lt;3 seconds response time).
/// </para>
/// </remarks>
public class OfficeService : IOfficeService
{
    // Task 060 (#1084): the save's job record (create, transitions, the idempotency lookup, the status read and the SSE
    // stream), stored on the Dataverse row instead of a static in-memory dictionary.
    private readonly OfficeJobStatusService _jobs;
    private readonly OfficeEmailEnricher _emailEnricher;
    private readonly OfficeDocumentPersistence _documentPersistence;
    private readonly OfficeJobQueue _jobQueue;
    private readonly OfficeStorageUploader _storageUploader;
    private readonly EmailProcessingOptions _emailProcessingOptions;
    private readonly RecordContainerResolver _containerResolver;

    /// <summary>FR-26 core-ancestor derivation for the To Do write path (task 052).</summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver _coreAncestors;

    /// <summary>
    /// Write-path invariant I-6 (task 080): the BU default owner team every record created here is owned by.
    /// Required, not optional (ADR-032) — it is registered unconditionally, and a missing one must fail at
    /// startup rather than quietly re-open app-user ownership, the defect it exists to remove.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownershipResolver;
    private readonly IMembershipEventPublisher _membershipEventPublisher;
    // FR-B3 (task 043): routes a user-saved EMAIL through the SAME Association Engine as mailbox capture so a
    // hand-filed email is associated + triaged (an intelligence-bearing sprk_communication), not merely a
    // sprk_document archive. Required (task 059): CommunicationModule registers it unconditionally. CaptureAsync
    // is itself best-effort (NFR-04) and never throws out of the save.
    private readonly EmailUploadCaptureService _emailUploadCapture;
    // Task 059: the Office add-in's Dataverse READS — the "File to" entity search (impersonated, task 062), the
    // matter-type list, and the sprk_recordtype_ref lookup the To Do writer below stamps. Extracted so this
    // class issues no Dataverse read of its own; the IOfficeService search members delegate to it.
    private readonly OfficeSearchService _search;
    // Slice 3 (#10): generic Dataverse create for the add-in inline "New record" (Invoice) and the To Do writer.
    // Required (task 059): registered unconditionally as a singleton (→ IDataverseService, GraphModule.cs).
    private readonly IGenericEntityService _genericEntityService;
    // FR-13 (spaarkeai-word-add-in-r1 task 030): the Matter quick-create path. Required (ADR-032): its registration
    // is unconditional, so an absent one is a startup fault, never a per-request 403.
    private readonly RecordCreationService _recordCreation;
    // Task 067 (finding F5): resolves the calling user's Dataverse systemuserid so the job row can record
    // WHO asked for it (sprk_initiatedby). Reused, not reinvented (CLAUDE.md §11) — this is the same
    // ICallerSystemUserResolver the Communication read path and task 062's picker already depend on,
    // TryAdd-registered inline by CommunicationModule, which Program.cs composes unconditionally. Required
    // (task 059). A caller it cannot resolve still records no creator, and an unrecorded creator is REFUSED by
    // the ownership checks, so the degradation direction is closed, never open.
    private readonly ICallerSystemUserResolver _callerSystemUserResolver;
    private readonly ILogger<OfficeService> _logger;

    // FR-08's Generate Profile request, on the job queue (task 068, #1086). It replaced OfficeProfileDispatcher, whose
    // Task.Run behind the 202 lost the profile on a restart, and the three optional parameters that built it.
    private readonly OfficeProfileQueue _profileQueue;

    // Task 083 (#1044 writer half): the caller's linked CONTACT (task 141's link,
    // systemuser.sprk_primarycontact / contact.sprk_externalobjectid), used to default CreateTodoAsync's
    // sprk_assignedto when the request names no assignee. Required, not optional (ADR-032): registered
    // unconditionally as a singleton by MembershipModule, so an absent one is a startup fault.
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService _identity;

    public OfficeService(
        OfficeJobStatusService jobs,
        OfficeEmailEnricher emailEnricher,
        OfficeDocumentPersistence documentPersistence,
        OfficeJobQueue jobQueue,
        OfficeStorageUploader storageUploader,
        IOptions<EmailProcessingOptions> emailProcessingOptions,
        IMembershipEventPublisher membershipEventPublisher,
        RecordContainerResolver containerResolver,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        RecordCreationService recordCreation,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownershipResolver,
        OfficeSearchService search,
        EmailUploadCaptureService emailUploadCapture,
        IGenericEntityService genericEntityService,
        ICallerSystemUserResolver callerSystemUserResolver,
        OfficeProfileQueue profileQueue,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        ILogger<OfficeService> logger)
    {
        _containerResolver = containerResolver
            ?? throw new ArgumentNullException(nameof(containerResolver));
        // Required, not optional (ADR-032): an absent registration must fail at startup, not surface as a 403
        // "permission" message on every Matter quick-create.
        _recordCreation = recordCreation
            ?? throw new ArgumentNullException(nameof(recordCreation));
        _coreAncestors = coreAncestors
            ?? throw new ArgumentNullException(nameof(coreAncestors));
        _ownershipResolver = ownershipResolver
            ?? throw new ArgumentNullException(nameof(ownershipResolver));
        _identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
        _jobs = jobs;
        _emailEnricher = emailEnricher;
        _documentPersistence = documentPersistence;
        _jobQueue = jobQueue;
        _storageUploader = storageUploader;
        _emailProcessingOptions = emailProcessingOptions.Value;
        _membershipEventPublisher = membershipEventPublisher;
        _emailUploadCapture = emailUploadCapture;
        _search = search;
        _genericEntityService = genericEntityService;
        _callerSystemUserResolver = callerSystemUserResolver;
        _profileQueue = profileQueue;
        _logger = logger;
    }

    /// <summary>
    /// The Office save's owner-event publish (POST /office/save; R3 task 082, UAC-r2 task 152 / ADR-034 A3): the saved
    /// <c>sprk_document</c> is owned by the business-unit default owner TEAM the save resolved (task 080), so the event
    /// names that team — <c>PersonIdType = Team</c>, <c>PersonId = owningTeamId</c>, the key
    /// <c>MembershipReconciliationJob</c> builds for the same row — never the caller's AAD oid. Should the save ever
    /// hold no team, the owner is read back from the row. Fire-and-forget (ADR-034 Q2): never throws.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="SaveAsync"/> (task 152 verifier round 1, item 7) so the site's own decisions — which
    /// table, which owner, the read-back fallback — run in a test. <see cref="SaveAsync"/> depends on a dozen concrete
    /// collaborators (SPE upload, job store, persistence) and has no unit harness; it passes exactly
    /// (<c>documentId</c>, <c>owningTeamId</c>) here.
    /// </remarks>
    internal static Task<MembershipChangedEvent?> PublishSavedDocumentOwnerAsync(
        IMembershipEventPublisher publisher,
        IGenericEntityService dataverse,
        Guid documentId,
        Guid? owningTeamId,
        string correlationId,
        ILogger logger,
        CancellationToken cancellationToken)
        => MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher,
            dataverse,
            "sprk_document",
            documentId,
            // The team this save resolved; should it ever be absent, the owner is read back from the row.
            owningTeamId is { } savedTeamId ? new Microsoft.Xrm.Sdk.EntityReference("team", savedTeamId) : null,
            correlationId,
            logger,
            cancellationToken);

    /// <inheritdoc />
    public Task<GenerateProfileResult> GenerateProfileAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return _profileQueue.QueueAsync(
            documentId,
            httpContext.TraceIdentifier,
            CallerResolution.ResolveObjectId(httpContext.User));
    }

    /// <summary>
    /// Decides which SPE container this save writes into. Server-side, always — task 085.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a <c>TargetEntity</c>, the container is derived from that record through task 076's
    /// <see cref="RecordContainerResolver"/>. That record is the one
    /// <c>AddEntityAccessFilter</c> already authorized the caller against, so the authorization key
    /// and the write destination become a single value. The resolver refuses (rather than falling
    /// back) when a secure record has no container of its own, which is why this method does not catch
    /// <see cref="SdapProblemException"/>: swallowing it would turn a correct fail-closed refusal into
    /// what reads like a misconfiguration.
    /// </para>
    /// <para>
    /// <b>Without a <c>TargetEntity</c> there is no record</b>, so the container is derived from the
    /// ACTING USER's business unit — task 076's
    /// <see cref="RecordContainerResolver.ResolveForActingUserAsync"/>, the owner-sanctioned answer for
    /// content that exists before its owning record does. The configured
    /// <c>EmailProcessing:DefaultContainerId</c> is the LAST resort, and is still fail-closed when unset.
    /// </para>
    ///
    /// <para><b>⚠️ CORRECTED 2026-09-21 (spaarkeai-word-add-in-r1 task 065).</b> This paragraph used to
    /// read <i>"Every shipped add-in path sends a <c>TargetEntity</c>, so this branch exists for the
    /// contract rather than for traffic."</i> <b>That was false, and had been since task 037.</b> Three
    /// shipped paths send no target, and together they are the save spine's mainline rather than an edge:
    /// <list type="number">
    /// <item>the <b>Word ribbon quick-save</b> (FR-17, task 037) — <c>buildDocumentSaveRequest</c> in
    /// <c>quickSaveHelpers.ts</c> sends no <c>targetEntity</c> at all, because a Word ribbon click carries
    /// no Spaarke context and there is no document-side association prediction to stand in for one;</item>
    /// <item>every <b>FR-11 version save</b> (task 023) — <c>useSaveFlow.ts</c> sets
    /// <c>sentEntity = versionTarget ? null : selectedEntity</c> BY OWNER DECISION (D-4/D-5): a version
    /// save re-associates nothing, and sending a target would add an unrelated
    /// <see cref="Api.Filters.EntityAccessFilter"/> check that could refuse a legitimate version save.
    /// That one is authorized instead by <c>OfficeVersionSaveAuthorizationFilter</c>, on the document;</item>
    /// <item>every <b>pane save with no "Related to" selected</b> — <c>useSaveFlow.ts</c> attaches
    /// <c>targetEntity</c> only <c>if (sentEntity?.id)</c>.</item>
    /// </list>
    /// Roughly 80 of the ~85 <c>POST /office/save</c> bodies in the test corpus are in this shape too.
    /// <b>So this branch is the traffic, not the contract</b>, and anything that makes it fail closed is a
    /// save outage rather than a tightening.
    /// </para>
    ///
    /// <para><b>What this does NOT do.</b> Deriving a container is a PLACEMENT decision, not an
    /// authorization one. A save with no target still passes <see cref="Api.Filters.EntityAccessFilter"/>
    /// untouched and no per-record check runs anywhere in the path (finding F4). Narrowing the destination
    /// from one tenant-wide container to one per business unit reduces the blast radius; it does not close
    /// F4. Closing F4 means requiring <c>TargetEntity</c>, which task 065 ESCALATED rather than shipped —
    /// see <c>projects/spaarkeai-word-add-in-r1/notes/065-require-target-entity.md</c>.</para>
    ///
    /// <para><b>Why a fallback and not a refusal when the acting user cannot be resolved.</b>
    /// <see cref="RecordContainerResolver.ResolveForActingUserAsync"/> throws
    /// <see cref="SdapProblemException"/> for a caller who maps to no Dataverse user, or to more than one,
    /// or to one with no business unit. Those are CALLER-IDENTITY failures, not isolation failures: this
    /// branch is only reached when there is no record, and <c>ResolveForActingUserAsync</c>'s own contract
    /// states that <c>FailClosed</c> is unreachable here by construction because nothing on this path
    /// CAN be secure. Letting such a caller fall through to the configured default is therefore exactly
    /// today's behaviour for that caller — strictly non-regressive — whereas refusing would convert an
    /// unprovisioned or duplicated user record into a total quick-save outage. The RECORD branch above
    /// keeps its no-catch posture, for the opposite reason: there a refusal IS the isolation guarantee.</para>
    /// </remarks>
    /// <param name="request">The save being placed.</param>
    /// <param name="actingUserObjectId">
    /// The caller's Entra <c>oid</c>, as <c>OfficeAuthFilter</c> resolved it. Used only on the no-record
    /// branch, and only as a LOOKUP KEY into <c>systemuser.azureactivedirectoryobjectid</c> — never
    /// compared against a <c>systemuserid</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<string> ResolveContainerAsync(
        SaveRequest request,
        string? actingUserObjectId,
        CancellationToken ct)
    {
        if (request.TargetEntity is { } target && target.EntityId != Guid.Empty)
        {
            // The LOGICAL name, not the wire spelling. The save endpoint accepts only the friendly types
            // ("matter", "project", …), while RecordContainerResolver and ISecurableEntityRegistry are keyed on
            // logical names. Passed through as-is, "project" was never securable, so a save filed to a SECURE
            // project skipped the secure branch, resolved Unresolved, and landed in the tenant-wide default
            // container — irreversibly, because SPE permissions are additive-only. Found by task 080's review;
            // mapped through the same alias table the ownership resolver uses, so the two cannot disagree.
            var targetLogicalName = DocumentAssociationMap.ToLogicalName(target.EntityType) ?? target.EntityType;

            var decision = await _containerResolver
                .ResolveForRecordAsync(targetLogicalName, target.EntityId, ct)
                .ConfigureAwait(false);

            // FailClosed means the record is SECURE and has no container of its own. Falling through to
            // the configured default here would put a secure record's content in the shared container —
            // the precise failure this project exists to prevent, and irreversible in SPE because
            // permissions there are additive-only. Refuse instead.
            if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
            {
                throw new InvalidOperationException(
                    $"The storage container for secure record {target.EntityType} {target.EntityId} could "
                    + "not be determined. Refusing rather than writing its content into a shared "
                    + "container, which SPE cannot subsequently un-share.");
            }

            if (!string.IsNullOrWhiteSpace(decision.ContainerId))
            {
                _logger.LogDebug(
                    "Office save container derived from {EntityType} {EntityId} (outcome: {Outcome})",
                    target.EntityType, target.EntityId, decision.Outcome);

                return decision.ContainerId!;
            }

            // Unresolved: a non-secure record with no container and no business-unit default. Falling
            // through to the configured default is safe here precisely BECAUSE the record is not
            // secure — the outcome enum guarantees Unresolved is unreachable for a secure record.
            _logger.LogDebug(
                "No container resolved for {EntityType} {EntityId} (outcome: {Outcome}); using the "
                + "configured default.",
                target.EntityType, target.EntityId, decision.Outcome);
        }
        else
        {
            // ══ NO RECORD (task 065) ══════════════════════════════════════════════════════════════
            // The Word ribbon quick-save and every pane save with no "Related to" selected arrive here.
            // (An FR-11 VERSION save does not: it writes to the target document's own drive and never
            // calls this method at all — see the call site.) Ask for the acting user's business-unit
            // container BEFORE reaching for the tenant-wide default. Deliberately NOT reached for a
            // target-bearing save: see the remarks — a record's container follows the RECORD, and letting
            // the uploader's business unit win there is the isolation failure task 076 closed.
            try
            {
                var actingUserDecision = await _containerResolver
                    .ResolveForActingUserAsync(actingUserObjectId, ct)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(actingUserDecision.ContainerId))
                {
                    _logger.LogDebug(
                        "Office save has no target entity; container derived from the acting user's "
                        + "business unit (outcome: {Outcome}).",
                        actingUserDecision.Outcome);

                    return actingUserDecision.ContainerId!;
                }

                // A business unit with no sprk_containerid stamped is a legitimate configuration state
                // (3 of 6 live units, verified 2026-08-27), not an error. Fall through.
                _logger.LogInformation(
                    "Office save has no target entity and the acting user's business unit has no "
                    + "container stamped; using the configured default.");
            }
            catch (SdapProblemException ex)
            {
                // Caller identity, not isolation — see the remarks. Logged at Warning rather than
                // swallowed silently, because a tenant where this fires for everyone has an unprovisioned
                // -user problem worth seeing, and the fallback hides it from the user by design.
                _logger.LogWarning(
                    "Office save has no target entity and the acting user's container could not be "
                    + "derived ({Code}); using the configured default. This is not an isolation decision "
                    + "— nothing on the no-record branch can be secure.",
                    ex.Code);
            }
        }

        var configured = _emailProcessingOptions.DefaultContainerId;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "No storage container could be determined for this save. Neither the target record (if "
                + "one was named) nor the acting user's business unit yielded one, and "
                + "EmailProcessing:DefaultContainerId is not configured. Refusing rather than guessing "
                + "a container.");
        }

        return configured;
    }

    /// <inheritdoc />
    public async Task<SaveResponse> SaveAsync(
        SaveRequest request,
        string userId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Save requested for {ContentType} by user {UserId}",
            request.ContentType,
            userId);

        // DEBUG: Log email body info
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            _logger.LogInformation(
                "[EMAIL BODY DEBUG] Subject={Subject}, HasBody={HasBody}, BodyLength={BodyLength}",
                request.Email.Subject,
                !string.IsNullOrEmpty(request.Email.Body),
                request.Email.Body?.Length ?? 0);
        }

        // Fetch email body and attachments from Graph API if missing
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            request = await _emailEnricher.EnrichEmailFromGraphAsync(request, httpContext, cancellationToken);
        }

        // Fetch attachment content from Graph API if missing (single attachment save)
        if (request.ContentType == SaveContentType.Attachment && request.Attachment != null)
        {
            request = await _emailEnricher.EnrichAttachmentFromGraphAsync(request, httpContext, cancellationToken);
        }

        // Declared outside the try (task 039, finding 2) so the outer catch can mark THIS save's ProcessingJob
        // Failed. A job left Queued/Running by a save that threw was answered as the "duplicate" of every retry.
        // jobRecord (task 060) is the save's view of that job, which the catch records as Failed.
        var jobId = Guid.Empty;
        JobStatusResponse? jobRecord = null;

        try
        {
            // Step 1: The save's idempotency key — the ONE source that decides whether this save runs (task 039,
            // finding 1): the BODY key when the client sends one, else the server's own key (content-aware for
            // every Document save). The X-Idempotency-Key header never reaches here — it only names the response
            // cache in IdempotencyFilter, which is bound to the body for Document saves.
            var idempotencyKey = ResolveIdempotencyKey(request);

            // Step 2: Check for existing job with this idempotency key. Task 060 (#1084): this read never worked in
            // production (`dynamic` over another assembly's anonymous type threw, and was swallowed into "no duplicate").
            var existingJob = await _jobs.FindExistingAsync(idempotencyKey, cancellationToken);

            // Task 047: a key names CONTENT, not a moment. For a version save, a Completed job under this key is the
            // duplicate only while the document still holds exactly this content. Otherwise B, then A, then B again
            // was answered with the first B save's job and never written.
            if (existingJob is not null && !await IsStillTheSameOperationAsync(request, existingJob, cancellationToken))
            {
                existingJob = null;
            }

            if (existingJob is not null)
            {
                _logger.LogInformation(
                    "Duplicate save detected, returning existing job {JobId}",
                    existingJob.JobId);

                return new SaveResponse
                {
                    Success = true,
                    Duplicate = true,
                    JobId = existingJob.JobId,
                    StatusUrl = $"/api/office/jobs/{existingJob.JobId}",
                    StreamUrl = $"/api/office/jobs/{existingJob.JobId}/stream"
                };
            }

            // ══ RECORD OWNERSHIP (task 080, write-path invariant I-6) ═════════════════════════════════
            // The sprk_document this save creates is owned by a business unit's DEFAULT OWNER TEAM — the team of
            // the record it is filed against, or the acting user's when it is filed against nothing (owner
            // decisions 2026-09-22 / 2026-09-25). Left unset, Dataverse makes the app user the owner, which sits
            // in the ROOT business unit, so no child-BU user could ever read their own saved document.
            //
            // Resolved HERE — before the email capture, the job row, the upload and the document row — so a
            // refusal writes nothing at all. A version save is skipped: it creates no document, and its row keeps
            // the owner it already has. `IsVersionSave` is the cheap intent predicate; the target is read below.
            Guid? owningTeamId = null;
            Guid? createdByPersonId = null;
            if (!IsVersionSave(request))
            {
                var documentOwner = await _ownershipResolver.ResolveOwnerAsync(
                    new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
                    {
                        TargetEntityLogicalName = request.TargetEntity?.EntityType,
                        TargetRecordId = request.TargetEntity?.EntityId,
                        // The oid is enough: the resolver keys systemuser on azureactivedirectoryobjectid, the same
                        // key OfficeAuthFilter resolved and RecordContainerResolver uses for this save's container.
                        CallerObjectId = Guid.TryParse(userId, out var callerObjectId) ? callerObjectId : null,
                        // Task 146 c1-r1 (owner round 13 item 9): the saving user asked for every document this save
                        // creates app-only — recorded as their creator person (carried to the worker with the team).
                        RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfObjectId(userId),
                    },
                    cancellationToken);
                owningTeamId = documentOwner.IsOwned ? documentOwner.OwningTeamId : null;
                createdByPersonId = documentOwner.CreatedByPerson;

                if (owningTeamId is null)
                {
                    // Fail-closed, never app-owned: an app-owned row is invisible to its own author, which is the
                    // defect this exists to remove. The resolver has already logged which link in the chain broke.
                    return new SaveResponse
                    {
                        Success = false,
                        Error = new SaveError
                        {
                            Code = OfficeErrorCodes.RecordOwnerUnresolved,
                            Message = request.TargetEntity is not null
                                ? "This item could not be assigned an owner from the record it is filed to, so it "
                                  + "was not saved. Check that the record still exists and that you can open it."
                                : "This item could not be assigned an owner from your business unit, so it was not "
                                  + "saved. Ask an administrator to check your user record's business unit.",
                            Retryable = false
                        }
                    };
                }
            }

            // FR-B3 (task 043): route a user-saved EMAIL through the SAME capture engine as mailbox intake —
            // create/reconcile the canonical sprk_communication and run association + triage + provenance — so a
            // hand-filed email is intelligence-bearing, not merely a sprk_document archive. Runs AFTER the
            // idempotency early-return (a genuine replay already captured) and BEFORE document creation so
            // OfficeDocumentPersistence's cross-path link (FR-C4) resolves the canonical this produces. Message-
            // level dedup is structural (FR-C1 alternate key) inside CaptureAsync — no second dedup mechanism.
            // CaptureAsync is internally best-effort/non-fatal (NFR-04): it never throws out of the save.
            if (request.ContentType == SaveContentType.Email)
            {
                await _emailUploadCapture.CaptureAsync(request, userId, cancellationToken);
            }

            // Step 3: Determine job type based on content type
            var jobType = request.ContentType switch
            {
                SaveContentType.Email => JobType.EmailSave,
                SaveContentType.Attachment => JobType.AttachmentSave,
                SaveContentType.Document => JobType.DocumentSave,
                _ => throw new ArgumentOutOfRangeException(nameof(request.ContentType))
            };

            // ══ FR-11 VERSION SAVE (spaarkeai-word-add-in-r1 task 023) ════════════════════════════════
            // A Document save naming an existing sprk_document writes a NEW SPE VERSION of that document's
            // own drive item and refreshes that row — it never creates a second row. Scoped by IsVersionSave
            // on the CONTENT TYPE explicitly, so Email and Attachment saves never read the field, whatever
            // their body carries. The endpoint filter has already required "write" on the target (ADR-008);
            // this is the existence + pointer read that follows it, and every refusal returns HERE — before a
            // ProcessingJob row, an SPE write, or a document row can exist.
            OfficeDocumentPersistence.VersionTarget? versionTarget = null;
            if (IsVersionSave(request))
            {
                var (target, refusal) = await ResolveVersionTargetAsync(request.Document!, cancellationToken);
                if (refusal is not null)
                {
                    return new SaveResponse { Success = false, Error = refusal };
                }

                versionTarget = target;
            }

            // Step 4: Create a new ProcessingJob record in Dataverse (jobId is declared above the try — task 039)
            _logger.LogInformation(
                "Creating ProcessingJob for {ContentType} save with association {AssociationType}:{AssociationId}",
                request.ContentType,
                request.TargetEntity?.EntityType,
                request.TargetEntity?.EntityId);

            // ══ SERVER-DERIVED CONTAINER (task 085) ═══════════════════════════════════════════════
            // Derived HERE, before the payload is built, so the job record and the upload below cannot
            // disagree. Previously the payload carried request.ContainerId — a client-chosen container
            // that outlived the request inside the ProcessingJob row.
            //
            // With a TargetEntity, the container comes from the SAME record the caller was authorized
            // against (AddEntityAccessFilter), via task 076's resolver: the authorization key and the
            // write destination are now one value, so no code path can let them disagree. The resolver
            // is also secure-aware — a secure record's own container wins over any business-unit
            // default, which is the isolation guarantee a client-supplied id could always defeat.
            //
            // Without a TargetEntity there is no record to derive from, so the ACTING USER's
            // business-unit container applies (RecordContainerResolver.ResolveForActingUserAsync),
            // with EmailProcessing:DefaultContainerId as the last resort — server-side either way, and
            // still fail-closed when both are unavailable. See ResolveContainerAsync's remarks.
            //
            // ⚠️ CORRECTED 2026-09-21 (task 065). This comment used to end: "Office save always carries
            // a TargetEntity from the shipped add-in, so its no-record branch is contract-only". THAT
            // WAS FALSE, and had been since task 037 — the Word ribbon quick-save, every FR-11 version
            // save's sibling create, and every pane save with no "Related to" selected all send no
            // target. The no-record branch is this route's MAINLINE traffic. The corrected claim, and
            // the enumeration behind it, are in ResolveContainerAsync's remarks.
            //
            // What did NOT change: acting-user resolution is still deliberately kept AWAY from a save
            // that names a record. RecordContainerResolver §"Why the RECORD's business unit and not the
            // ACTING USER's" is right about that case — users sit in the Operations subtree while secure
            // records are owned in Secure Record, so acting-user resolution applied to a RECORD writes
            // a secure record's content into the general Operations container. Task 076's owner sanction
            // covers exactly the no-record shape, which is the one the branch below now uses, and it is
            // the same call OBOEndpoints and ComposeService already make — no new component (CLAUDE.md §11).
            //
            // ⚠️ And it is a PLACEMENT change, not an authorization one: a no-target save still passes
            // EntityAccessFilter untouched (finding F4). See notes/065-require-target-entity.md.
            //
            // FR-11 (task 023): a VERSION save writes to the target document's OWN drive — the destination is
            // an item that already exists, so a container derived from TargetEntity could only disagree with it.
            var derivedContainerId = versionTarget is not null
                ? versionTarget.DriveId!
                : await ResolveContainerAsync(request, userId, cancellationToken);

            // The request's METADATA for sprk_payload, never its content (task 060, #1084): the content's base64 made
            // Dataverse refuse the create for 27 of 40 saves in 60 days, and those saves ran with no job row.
            var payload = OfficeJobStatusService.BuildPayload(request, derivedContainerId);

            // Task 067 (finding F5): record WHO asked for this job, at CREATE time, in sprk_initiatedby.
            //
            // Task 060: the save's own view (written with the row) also records the creator OID the save
            // authenticated, and ownership reads that first; sprk_initiatedby is the creator for rows written
            // before it. An unresolved caller yields null here, the property is skipped by the create mapper, and
            // the row records no systemuser. Resolved BEFORE the create, so a resolver hiccup cannot be mistaken
            // for a failed create.
            Guid? initiatedBySystemUserId = null;
            var callerResolution = await _callerSystemUserResolver
                .ResolveAsync(httpContext.User, cancellationToken);

            if (callerResolution.IsResolved
                && Guid.TryParse(callerResolution.SystemUserId, out var resolvedSystemUserId)
                && resolvedSystemUserId != Guid.Empty)
            {
                initiatedBySystemUserId = resolvedSystemUserId;
            }
            else
            {
                _logger.LogWarning(
                    "Could not resolve a Dataverse systemuser for the caller ({Reason}); this job's row will record " +
                    "no sprk_initiatedby. Ownership still reads the creator OID the save's view records (task 060).",
                    callerResolution.UnresolvedReason ?? "unknown");
            }

            // The save's view of its job: what the pane polls. Persisted with the row and with every transition below
            // (task 060), so any instance, before or after a restart, answers the same.
            var correlationId = Guid.NewGuid().ToString();
            jobRecord = new JobStatusResponse
            {
                JobId = Guid.Empty, // the row's id, set once it exists
                Status = JobStatus.Queued,
                JobType = jobType,
                Progress = 0,
                CurrentPhase = "Queued",
                CompletedPhases = new List<CompletedPhase>(),
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };

            try
            {
                // FR-11 (task 023): a version save is named as one, so each revision's job row — which also carries
                // Document.IsNewVersion/VersionComment in its payload — is identifiable. Every other save keeps the
                // identical "{ContentType} Save - …" name.
                jobId = await _jobs.CreateAsync(
                    jobRecord,
                    $"{(versionTarget is not null ? "Document Version" : request.ContentType.ToString())} Save - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    idempotencyKey,
                    payload,
                    initiatedBySystemUserId,
                    cancellationToken);

                _logger.LogInformation(
                    "ProcessingJob {JobId} created in Dataverse for {ContentType}",
                    jobId,
                    request.ContentType);
            }
            catch (Exception ex)
            {
                // Task 060: refused, no longer downgraded to an in-memory job. A job with no row has no durable status
                // and no idempotency (ADR-017: no orphaned jobs), and nothing has been written yet: no SPE upload, no
                // document row. The only production cause, the oversized payload, is gone (BuildPayload above).
                _logger.LogError(ex, "Failed to create ProcessingJob in Dataverse; the save is refused before any write");
                jobRecord = null; // no row to record against
                return new SaveResponse
                {
                    Success = false,
                    Error = new SaveError
                    {
                        Code = OfficeErrorCodes.DataverseError,
                        Message = "The save could not be started, so nothing was saved. Try again.",
                        Retryable = true
                    }
                };
            }

            jobRecord = jobRecord with { JobId = jobId };

            // Step 5: Upload content to SPE and queue finalization job
            // Following existing document flow per architecture docs:
            // 1. Create Document → 2. Upload to SPE → 3. Associate Document to SPE → 4. Trigger AI
            Stream? contentStream = null;
            string fileName;
            // Task 046 (b) / 020: the name the document is SHOWN under (sprk_documentname). Null means "the
            // stored file name", which is the case only for Attachment — Email (task 046 (b)) and Document
            // (task 020, FR-06) both set it explicitly below.
            string? documentName = null;
            long fileSize = 0;
            // FR-02 (task 014): on a Document CREATE the sprk_document id must be known BEFORE the bytes are
            // uploaded, because the stamp goes INTO those bytes. Minted in the Document arm below and handed to
            // the row create, so the stored file and its record cannot disagree about which document this is.
            // Null for Email/Attachment and for a version save (which already knows its target row).
            Guid? preAssignedDocumentId = null;

            try
            {
                // Build file content based on content type
                switch (request.ContentType)
                {
                    case SaveContentType.Email when request.Email != null:
                        // Build .eml file from email metadata using MimeKit
                        contentStream = OfficeEmailEnricher.BuildEmlFromMetadata(request.Email);
                        // Task 046 (b): a SYSTEM-DERIVED name is stored with a short unique suffix, because the
                        // path-keyed Replace upload below would otherwise let a second same-subject, same-date
                        // email overwrite the first. A name the user typed is stored exactly as today.
                        // sprk_documentname keeps the readable name either way.
                        (documentName, fileName) = OfficeEmailEnricher.GenerateEmlNames(request.Email);
                        fileSize = contentStream.Length;
                        break;

                    case SaveContentType.Attachment when request.Attachment != null:
                        // Decode base64 attachment content
                        if (!string.IsNullOrEmpty(request.Attachment.ContentBase64))
                        {
                            var bytes = Convert.FromBase64String(request.Attachment.ContentBase64);
                            contentStream = new MemoryStream(bytes);
                            fileSize = bytes.Length;
                        }
                        else
                        {
                            throw new InvalidOperationException("Attachment content is required for attachment saves");
                        }
                        // SANITIZED — see the note on the Document branch below. The client-supplied
                        // attachment name becomes the SPE upload path verbatim, and any '/' in it makes
                        // Graph create a folder.
                        fileName = SpeUploadPath.SanitizeFileName(request.Attachment.FileName);
                        break;

                    case SaveContentType.Document when request.Document != null:
                        // Decode base64 document content
                        if (!string.IsNullOrEmpty(request.Document.ContentBase64))
                        {
                            var bytes = Convert.FromBase64String(request.Document.ContentBase64);

                            // ══ FR-02 (task 014) — STAMP THE IDENTITY INTO THE UPLOADED BYTES ════════════════
                            // The user's OPEN document in Word is never touched: the stamp goes into the copy
                            // already in flight to the server, so Word never reports unsaved changes and never
                            // prompts. Forward-only BY CONSTRUCTION (owner decision 2026-09-04) — this is the
                            // only write, and it acts on bytes the client just sent; nothing already stored is
                            // ever rewritten, so there is no backfill, migration or stamp-on-read to find.
                            Guid stampTarget;
                            if (versionTarget is not null)
                            {
                                stampTarget = versionTarget.DocumentId;
                            }
                            else
                            {
                                preAssignedDocumentId = Guid.NewGuid();
                                stampTarget = preAssignedDocumentId.Value;
                            }

                            var stamp = OfficeDocumentStamp.Stamp(bytes, stampTarget);

                            if (stamp.Outcome == OfficeDocumentStamp.StampOutcome.Corrupt)
                            {
                                // Refused BEFORE any SPE write, so nothing partial is stored. Note what does NOT
                                // land here: a PDF, an EML, an arbitrary binary and a readable non-Word zip are
                                // all pass-throughs, not errors. Only "carries a zip signature but is not a
                                // readable package" is refused — bytes no reader could open.
                                _logger.LogWarning(
                                    "Document save refused: the uploaded bytes carry a zip signature but could not be "
                                    + "read as an Office package (job {JobId}). Nothing was written to storage.",
                                    jobId);

                                jobRecord = jobRecord with
                                {
                                    Status = JobStatus.Failed,
                                    CurrentPhase = "CorruptDocument",
                                    CompletedAt = DateTimeOffset.UtcNow
                                };
                                await _jobs.RecordAsync(jobRecord, stamp.Reason, cancellationToken);

                                return new SaveResponse
                                {
                                    Success = false,
                                    Error = new SaveError
                                    {
                                        Code = OfficeErrorCodes.CorruptDocumentPackage,
                                        Message = "This file could not be read as a Word document, so nothing was saved.",
                                        Details = stamp.Reason,
                                        Retryable = false
                                    }
                                };
                            }

                            if (stamp.Outcome == OfficeDocumentStamp.StampOutcome.Unsupported)
                            {
                                // A MISSING stamp is a normal, recoverable state (task 019 condition 3), never
                                // corruption: the save proceeds unstamped and identity falls back to task 012's
                                // Graph path. Logged so an unhandled package shape is discoverable rather than silent.
                                _logger.LogWarning(
                                    "Document stored WITHOUT an identity stamp: {Reason} (job {JobId}). Identity for "
                                    + "this document falls back to the URL/Graph path.",
                                    stamp.Reason, jobId);
                            }

                            bytes = stamp.Bytes;
                            contentStream = new MemoryStream(bytes);
                            // The STAMPED length: sprk_filesize must describe the bytes actually stored, and the
                            // finalization payload carries this same value downstream.
                            fileSize = bytes.Length;
                        }
                        else
                        {
                            throw new InvalidOperationException("Document content is required for document saves");
                        }
                        // ══ SANITIZED 2026-08-28 — THIS IS THE FOLDER-MINTING DEFECT, ROOT CAUSE ══════
                        // The add-in's "Document Name" box is free text (SaveFlow.tsx) and its value
                        // arrives here as request.Document.FileName with NO client-side cleaning. It then
                        // becomes the SPE upload path verbatim (OfficeStorageUploader → UploadSmallAsync →
                        // Drives[id].Root.ItemWithPath(path)), and Graph creates EVERY '/'-delimited
                        // segment of an upload path as a folder.
                        //
                        // So a user typing a date — "New Word Document from Word Web Add In 8/24/2026" —
                        // produced a folder "New Word Document from Word Web Add In 8", containing a
                        // folder "24", containing an extension-less file "2026". That is the origin of the
                        // mystery folders in SPE Admin: not Word Online writing directly to the container,
                        // and not a folder prefix in our code, but OUR OWN app-only upload of a filename
                        // with slashes in it. Confirmed against production sprk_document rows (created by
                        // the BFF service identities, in the reported container) — the app-only upload is
                        // also why SPE Admin showed no human creator, which is what made it look external.
                        //
                        // The EMAIL branch above never had this bug because GenerateEmlFileName sanitizes.
                        // The asymmetry was the defect; the document and attachment branches now use the
                        // same sanitizer. Removing the hardcoded folder prefixes elsewhere in this change
                        // does NOT subsume this — a filename is a path, so it needs its own guard.
                        fileName = SpeUploadPath.SanitizeFileName(request.Document.FileName);

                        // Task 020 (FR-06): the readable name (sprk_documentname) is the user-facing
                        // Document Name the pane sends as document.title — independent of the sanitized SPE
                        // upload path above (the inverted-mapping UAT defect this task closes: the record list
                        // showed .docx filenames instead of the names users typed). Falls back to the sanitized
                        // file name only when Title is absent/blank, so sprk_documentname is never empty.
                        documentName = !string.IsNullOrWhiteSpace(request.Document.Title)
                            ? request.Document.Title
                            : fileName;
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported content type: {request.ContentType}");
                }

                // Task 085: the container was derived from the AUTHORIZED RECORD above, before the job
                // payload was built. This site used to read request.ContainerId and only fall back to
                // config — which is how a caller chose the destination of an app-only MI write.
                var containerId = derivedContainerId;

                // FR-11 (task 023): the version path leaves HERE — after the Document branch above has decoded
                // the bytes and sanitized the name (the folder-minting fix stays on this path too) — and never
                // reaches the path-keyed upload or CreateDocumentWithSpePointersAsync below.
                if (versionTarget is not null)
                {
                    return await CompleteVersionSaveAsync(
                        versionTarget, request, userId, httpContext, jobId, jobRecord, idempotencyKey,
                        correlationId, contentStream, fileName, fileSize, cancellationToken);
                }

                // ══ TASK 025 (spaarkeai-word-add-in-r1) — REFUSE-BEFORE-UPLOAD, DOCUMENT CREATES ══════
                // D1: a same-name Document create used to land on the SAME SPE item as an existing
                // document (ConflictBehavior.Replace, path-keyed), overwriting its bytes, and then fail
                // creating a SECOND row on sprk_graphitemid_uk — so the FIRST document's content was
                // silently replaced by a save that itself errored. ConflictBehavior.Fail makes Graph
                // refuse the PUT atomically on a collision: no bytes move, the existing item is provably
                // untouched. This is a FIXED value chosen from exactly two states — Fail (the default:
                // refuse and ask) or Rename (the pane's explicit "Keep both" retry, AllowRename) — never a
                // resolver. Mirrors OBOEndpoints' own default-to-Fail posture and the shipped
                // external-upload precedent (ExternalProjectDataEndpoints.UploadDocument) — no new
                // collision-detection mechanism, the same Graph-native conflictBehavior two other callers
                // already use.
                //
                // ══ TASK 054 — THE SAME REFUSAL NOW COVERS EMAIL AND ATTACHMENT ══════════════════
                // Task 025 scoped Fail to Document because a BLANKET Fail regressed
                // OfficeImmutableSaveFileSafetyTests: an immutable capture's safety net is content-hash
                // dedup running AFTER the upload, and that suite pins a same-name, byte-identical
                // Attachment save as a SUCCESSFUL suppressed duplicate, which a bare refusal turns into a
                // 409. So a typed-name Email/Attachment collision kept overwriting the first document's
                // file (the 2026-09-15 owner decision's unimplemented half).
                //
                // The conflict is one of ORDERING, and it is resolved in ResolveNameCollisionAsync, not
                // here: a name alone cannot separate "the same capture saved twice" (a duplicate suppress
                // is right to collapse) from "two different emails that share a typed name" (two documents
                // that must become two files) — only the CONTENT can, and it is compared there against the
                // already-stored file BEFORE anything is written. Every content type therefore asks SPE to
                // refuse first; what a refusal MEANS is decided below.
                var conflictBehavior = request.ContentType == SaveContentType.Document
                    && request.Document?.AllowRename == true
                        ? ConflictBehavior.Rename
                        : ConflictBehavior.Fail;

                var upload = await _storageUploader.UploadToSpeAsync(
                    containerId,
                    fileName,
                    contentStream,
                    cancellationToken,
                    conflictBehavior);

                if (upload.IsNameCollision)
                {
                    // Reachable for EVERY content type since task 054 (Document, Email and Attachment all
                    // ask for Fail above). Nothing was written: SPE refused the PUT before any bytes
                    // moved, so the existing item (and its owning sprk_document row, if any) is provably
                    // untouched. Extracted to its own method (task 025 code-review, root CLAUDE.md §11.5):
                    // the ENTIRE collision-resolution contract — reclaim-if-orphaned, continue-if-the-same
                    // -immutable-content, refuse-otherwise — is auditable in one place, mirroring the
                    // existing ResolveVersionTargetAsync (Target, Refusal) shape.
                    var collisionUploadError = upload.Error;
                    SaveError? refusal;
                    (refusal, upload) = await ResolveNameCollisionAsync(
                        upload, containerId, fileName, contentStream, request.ContentType,
                        request.TargetEntity, cancellationToken);

                    if (refusal is not null)
                    {
                        jobRecord = jobRecord with
                        {
                            Status = JobStatus.Failed,
                            CurrentPhase = "NameCollision",
                            CompletedAt = DateTimeOffset.UtcNow
                        };
                        await _jobs.RecordAsync(jobRecord, collisionUploadError, cancellationToken);

                        return new SaveResponse { Success = false, Error = refusal };
                    }
                }

                if (!upload.Success || string.IsNullOrEmpty(upload.DriveId) || string.IsNullOrEmpty(upload.ItemId))
                {
                    jobRecord = jobRecord with
                    {
                        Status = JobStatus.Failed,
                        CurrentPhase = "UploadFailed",
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                    await _jobs.RecordAsync(jobRecord, upload.Error, cancellationToken);

                    return new SaveResponse
                    {
                        Success = false,
                        Error = new SaveError
                        {
                            Code = "OFFICE_012",
                            Message = "Failed to upload file to storage",
                            Details = upload.Error,
                            Retryable = true
                        }
                    };
                }

                var driveId = upload.DriveId;
                var itemId = upload.ItemId;
                var webUrl = upload.WebUrl;
                // Task 025: the name the item ACTUALLY holds in SPE — equal to `fileName` under Fail/Replace
                // (the only paths that ran before this task), but DIFFERENT under Rename, where Graph chose
                // a non-colliding name. The Dataverse row's stored file name must track SPE, never the name
                // that was merely requested.
                var storedFileName = upload.FileName ?? fileName;

                // Update job status to uploading complete
                jobRecord = jobRecord with
                {
                    Status = JobStatus.Running,
                    Progress = 30,
                    CurrentPhase = "FileUploaded"
                };
                await _jobs.RecordAsync(jobRecord, null, cancellationToken);

                // Create Document record with SPE pointers
                var (documentId, wasContentDuplicate) = await _documentPersistence.CreateDocumentWithSpePointersAsync(
                    request,
                    driveId,
                    itemId,
                    webUrl,
                    storedFileName,
                    documentName ?? fileName,
                    fileSize,
                    userId,
                    cancellationToken,
                    // FR-02 (task 014): the id already stamped into the uploaded bytes becomes this row's
                    // primary key, so the stored file self-identifies as the record that owns it.
                    preAssignedDocumentId,
                    // Task 080: resolved (or refused) before anything was written — see RECORD OWNERSHIP above.
                    owningTeamId,
                    createdByPersonId);

                // FR-C3 (email-communication-intelligence-r2, R-3): the content is byte-identical to an existing
                // canonical document (returned as `documentId`). No second document was created — and there is
                // nothing new to finalize: the canonical already carries its Email/Attachment artifacts + AI, so
                // re-running finalization would only duplicate them and re-spend AI on identical bytes. Skip the
                // whole downstream pipeline, clean up the upload (gate-after-write), and complete the job. The
                // detector already NOTIFIED the user of the canonical. Cleanup is best-effort (never fails the save).
                // The `finally` below still disposes the content stream.
                //
                // Task 046: the upload is deleted ONLY when no sprk_document points at it. The create upload above is
                // path-keyed under ConflictBehavior.Replace, so a name that already exists in the container lands on
                // THAT existing item — the canonical's own file (the detector's canonical lookup does not exclude the
                // probed item's row) or another document's. "The item this request just uploaded" is then not a
                // transient blob, and deleting it destroyed that document's file while this save reported success.
                // The guard sits here, in the caller that deletes, not in the shared ContentDedupDetector: the
                // detector's answer (the canonical for this content) is right; the delete assumed the rest.
                if (wasContentDuplicate)
                {
                    var uploadIsTransient = await _documentPersistence.IsUploadUnreferencedAsync(
                        itemId, documentId, cancellationToken);
                    if (uploadIsTransient)
                    {
                        await _storageUploader.DeleteFromSpeAsync(driveId, itemId, cancellationToken);
                    }

                    jobRecord = jobRecord with
                    {
                        Status = JobStatus.Completed,
                        Progress = 100,
                        CurrentPhase = "DeduplicatedToExisting",
                        CompletedAt = DateTimeOffset.UtcNow,
                        // Task 039 (finding 3): names the canonical this save resolved to. No file id: the upload was
                        // either deleted or is some document's own file, and the canonical's file is not re-read here.
                        Result = DocumentResult(documentId, speFileId: null, driveId: null, webUrl: null)
                    };
                    await _jobs.RecordAsync(jobRecord, null, cancellationToken);

                    _logger.LogInformation(
                        "ProcessingJob {JobId} completed: content duplicate of canonical document {DocumentId}; finalization skipped, upload cleanup attempted: {CleanupAttempted}.",
                        jobId, documentId, uploadIsTransient);

                    return new SaveResponse
                    {
                        Success = true,
                        Duplicate = false, // NOT an idempotent job replay — this is a content dedup (user notified via the canonical notification)
                        JobId = jobId,
                        StatusUrl = $"/api/office/jobs/{jobId}",
                        StreamUrl = $"/api/office/jobs/{jobId}/stream"
                    };
                }

                // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event.
                // Per event-source-inventory §3B (line 64), POST /office/save creates a sprk_document owned by the
                // business-unit default owner TEAM resolved above (RECORD OWNERSHIP). UAC-r2 task 152 (ADR-034 A3):
                // the event states that real owner — PersonIdType=Team, PersonId=owningTeamId — the same key
                // MembershipReconciliationJob builds for this row, instead of the caller's AAD oid as a User.
                // When MembershipEventPublisherOptions.Enabled=false (default), the Null peer logs + returns
                // (ADR-032 P2). The Task is discarded explicitly: fire-and-forget, never throws.
                _ = PublishSavedDocumentOwnerAsync(
                    _membershipEventPublisher,
                    _genericEntityService,
                    documentId,
                    owningTeamId,
                    correlationId,
                    _logger,
                    cancellationToken);

                // Update job status to records created
                jobRecord = jobRecord with
                {
                    Progress = 50,
                    CurrentPhase = "RecordsCreated"
                };
                await _jobs.RecordAsync(jobRecord, null, cancellationToken);

                // ALWAYS queue finalization job - it creates EmailArtifact/AttachmentArtifact records
                // and optionally triggers AI processing based on TriggerAiProcessing flag in payload
                await _jobQueue.QueueUploadFinalizationAsync(
                    jobId,
                    idempotencyKey,
                    correlationId,
                    userId,
                    request,
                    driveId,
                    itemId,
                    fileName,
                    fileSize,
                    documentId,
                    isVersionSave: false,
                    owningTeamId,
                    cancellationToken,
                    createdByPersonId);

                // Mark job as complete - background workers will process asynchronously
                // User sees immediate success while AI processing continues in background. The save's view (task 060)
                // keeps that answer even while the finalization workers move the row's pipeline columns again.
                jobRecord = jobRecord with
                {
                    Status = JobStatus.Completed,
                    Progress = 100,
                    CurrentPhase = "Complete",
                    CompletedAt = DateTimeOffset.UtcNow,
                    Result = DocumentResult(documentId, itemId, driveId, webUrl) // task 039 (finding 3)
                };
                await _jobs.RecordAsync(jobRecord, null, cancellationToken);

                _logger.LogInformation(
                    "ProcessingJob {JobId} completed, file uploaded to SPE, document {DocumentId} created. Background workers queued for finalization.",
                    jobId,
                    documentId);
            }
            finally
            {
                contentStream?.Dispose();
            }

            // Step 6: Return success response with job tracking URLs
            return new SaveResponse
            {
                Success = true,
                Duplicate = false,
                JobId = jobId,
                StatusUrl = $"/api/office/jobs/{jobId}",
                StreamUrl = $"/api/office/jobs/{jobId}/stream"
            };
        }
        catch (SdapProblemException refusal)
        {
            // A DELIBERATE refusal, not a server fault (unified-access-control-r2 task 155, merged with task 075):
            // RecordContainerResolver refuses with a stable reason code when it cannot show where this save's bytes may
            // go — container_record_not_found, container_ancestor_unverifiable, secure_record_container_missing, … —
            // and nothing has been uploaded. Task 075's catch-all below turns UNEXPECTED exceptions into a generic 500;
            // a refusal must instead reach the pane with its own code and its client-facing text (SdapProblemException
            // carries RFC 7807 Title/Detail, never server internals), in the save's pre-existing refusal shape.
            _logger.LogWarning(
                "Save for {ContentType} by user {UserId} was refused ({Code}, HTTP {Status}); nothing was uploaded",
                request.ContentType, userId, refusal.Code, refusal.StatusCode);

            if (jobId != Guid.Empty && jobRecord is not null)
            {
                await _jobs.RecordAsync(
                    jobRecord with
                    {
                        Status = JobStatus.Failed,
                        CurrentPhase = "Failed",
                        CompletedAt = DateTimeOffset.UtcNow
                    },
                    refusal.Code,
                    CancellationToken.None);
            }

            return new SaveResponse
            {
                Success = false,
                Error = new SaveError
                {
                    Code = refusal.Code,
                    Message = refusal.Detail ?? refusal.Title,
                    // A 5xx refusal is transient. So is container_ancestor_stale (task 156), although it is a 409: the
                    // refusal itself enqueued the re-stamp that makes the same save succeed, and its text tells the user
                    // to try again in a minute. Every other 4xx refusal is permanent until the data changes.
                    Retryable = refusal.StatusCode >= 500
                        || string.Equals(refusal.Code, RecordContainerResolver.AncestorStaleCode, StringComparison.Ordinal)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create save job for {ContentType} by user {UserId}: {ErrorMessage}",
                request.ContentType,
                userId,
                ex.Message);

            // Task 039 (finding 2): a save that throws AFTER its ProcessingJob exists must not leave that job
            // Queued/Running. The idempotency lookup answers any retry with a job that did not fail, so a job
            // stranded mid-flight would make every retry a "duplicate" of a save that never finished. Recorded with
            // CancellationToken.None so a client disconnect cannot skip it; the update itself is best-effort.
            if (jobId != Guid.Empty && jobRecord is not null)
            {
                await _jobs.RecordAsync(
                    jobRecord with
                    {
                        Status = JobStatus.Failed,
                        CurrentPhase = "Failed",
                        CompletedAt = DateTimeOffset.UtcNow
                    },
                    ex.Message,
                    CancellationToken.None);
            }

            // Task 075: a server fault, so the caller gets a generic message and the endpoint renders a 500. The
            // exception (logged above, with the request's correlation id) never goes on the wire: its message can
            // carry server internals, and the full ex.ToString() this used to attach carried the stack trace.
            return new SaveResponse
            {
                Success = false,
                Error = new SaveError
                {
                    Code = OfficeErrorCodes.InternalError,
                    Message = "The save could not be completed. Try again; if it keeps failing, contact support with the correlation id.",
                    Retryable = true
                }
            };
        }
    }

    /// <summary>
    /// Generates an idempotency key based on the request content.
    /// Uses SHA256 hash of the canonical payload.
    /// </summary>
    private static string GenerateIdempotencyKey(SaveRequest request)
    {
        // Create a canonical representation of the request for hashing
        var canonical = $"{request.ContentType}|" +
                       $"{request.TargetEntity?.EntityType}|" +
                       $"{request.TargetEntity?.EntityId}|" +
                       $"{request.Email?.InternetMessageId ?? request.Email?.Subject}|" +
                       $"{request.Attachment?.AttachmentId}|" +
                       $"{request.Document?.FileName}|" +
                       $"{request.Document?.ExistingDocumentId}";

        // FR-11 (task 023): a VERSION save also keys on its CONTENT. Without this, every revision of the same
        // document (same file name, same target, same existing id) produced the SAME key, and the persistent
        // ProcessingJob lookup in SaveAsync answered the SECOND revision "Duplicate" with the first save's job —
        // the new bytes were never written. With it, two different revisions are two operations while a retried
        // identical request still de-duplicates. The version string is unchanged by task 039.
        //
        // Task 039 (finding 4): a Document CREATE keys on its content too, with the SAME hashing. The create
        // string used to be content-free, so a user who saved a new document to a record, edited it, and saved
        // it again to the same record under the same name got the second save answered "Duplicate" from the
        // first save's job — the edits were silently never written. Identical bytes still hash identically, so
        // a true retry is still de-duplicated here (and a byte-identical but deliberate re-save is content
        // dedup's job — link/graduate, task 028 — not this cache's). Email and Attachment keep their string
        // byte-for-byte: they name an immutable message or attachment, never editable content.
        if (request.ContentType == SaveContentType.Document)
        {
            var contentHash = HashContent(request.Document?.ContentBase64);
            canonical += IsVersionSave(request)
                ? $"|version-content:{contentHash}"
                : $"|create-content:{contentHash}";
        }

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Task 039 (finding 3): what a COMPLETED save's job carries — the <c>sprk_document</c> the save landed on.
    /// The created document on the create path, the EXISTING document on the version path, and the canonical on
    /// the immutable duplicate path. The task pane completes on <c>result.artifact.id</c>; without it the job
    /// reported Completed with nothing to open and the pane stalled on its job card.
    /// </summary>
    private static JobResult DocumentResult(Guid documentId, string? speFileId, string? driveId, string? webUrl) => new()
    {
        Artifact = new CreatedArtifact
        {
            Type = ArtifactType.Document,
            Id = documentId,
            SpeFileId = speFileId,
            ContainerId = driveId,
            WebUrl = webUrl
        }
    };

    /// <summary>
    /// FR-11 (task 023): is this save a VERSION of an existing document? True only for
    /// <see cref="SaveContentType.Document"/> carrying a non-empty <c>Document.ExistingDocumentId</c>.
    /// </summary>
    /// <remarks>
    /// The ONE predicate for the version path: <see cref="SaveAsync"/> branches on it and
    /// <c>OfficeVersionSaveAuthorizationFilter</c> gates on it, so the gate and the write cannot disagree about
    /// what a version save is. Keyed on the content type EXPLICITLY — never on <c>request.Document</c> being
    /// null — so an Email or Attachment body that happens to carry the field is never acted on.
    /// </remarks>
    public static bool IsVersionSave(SaveRequest request) =>
        request.ContentType == SaveContentType.Document
        && request.Document?.ExistingDocumentId is { } existingDocumentId
        && existingDocumentId != Guid.Empty;

    /// <summary>
    /// Task 039 (finding 1): the save's AUTHORITATIVE idempotency key — the request BODY's
    /// <c>idempotencyKey</c> when present, else the server's own (<see cref="GenerateIdempotencyKey"/>, content-aware
    /// for every Document save). The ONE definition: <see cref="SaveAsync"/> de-duplicates on it, and the save
    /// route binds its <c>X-Idempotency-Key</c> response cache to it for Document saves
    /// (<c>OfficeEndpoints.DocumentSaveIdempotencyBinding</c>), so the two layers cannot disagree about which saves
    /// are the same operation. The header itself is never this key.
    /// </summary>
    public static string ResolveIdempotencyKey(SaveRequest request) =>
        request.IdempotencyKey ?? GenerateIdempotencyKey(request);

    /// <summary>
    /// Task 047: may the job found under this save's key still answer it as a duplicate? For a VERSION save, a
    /// Completed job does so only while the target document still holds exactly this save's content.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> A version key (the pane's, or <see cref="GenerateIdempotencyKey"/>'s
    /// <c>version-content</c> one) names a document and its content, not a moment. Neither layer used to tell a retry
    /// of a save from a later save of the same content. B, then A, then B again, found the first B save's Completed job,
    /// so the third save was never written: SPE kept A while the pane reported success. Checking what the document
    /// holds is key-agnostic, so it holds for every client and for the server's own key.</para>
    /// <para><b>The rule.</b> A duplicate is answered only when the document is proven to hold these bytes. Then a write
    /// would only add an identical version, so skipping it loses nothing. When the content differs (a later save wrote
    /// another version), or cannot be read, the save is a new operation. The worst case of an unreadable document is
    /// one more identical version, never a lost one.</para>
    /// <para><b>What is unchanged.</b> Email, Attachment and Document-create saves, and a version job still in flight
    /// (Queued/Running: its write may not have landed, and a concurrent double submit must not write twice), are decided
    /// by the key alone, as before. Failed and Cancelled jobs never reach here (task 039,
    /// <see cref="OfficeDocumentPersistence.CheckForExistingJobAsync"/>).</para>
    /// </remarks>
    private async Task<bool> IsStillTheSameOperationAsync(
        SaveRequest request,
        JobStatusResponse existingJob,
        CancellationToken cancellationToken)
    {
        if (!IsVersionSave(request) || existingJob.Status != JobStatus.Completed)
        {
            return true;
        }

        try
        {
            var requested = Convert.FromBase64String(request.Document!.ContentBase64 ?? string.Empty);
            var existingDocumentId = request.Document.ExistingDocumentId!.Value;

            // FR-02 (task 014): the document's STORED bytes are stamped; the request's are not. Comparing them
            // raw would therefore never match, and this check would answer "not the same operation" for every
            // retry — writing a redundant SPE version each time and silently undoing task 047's guarantee. So
            // compare like with like: stamp the request exactly as the save would. This is sound because
            // stamping is byte-DETERMINISTIC and a re-stamp with the same id is a true no-op, which also makes
            // it correct for a round-tripped document whose bytes already carry this id.
            var content = OfficeDocumentStamp.Stamp(requested, existingDocumentId).Bytes;

            var target = await _documentPersistence.ResolveVersionTargetAsync(
                existingDocumentId, cancellationToken);
            var holdsContent = content.Length > 0 && target is { HasSpePointers: true }
                ? await _storageUploader.ItemHoldsContentAsync(target.DriveId!, target.ItemId!, content, cancellationToken)
                : null;

            if (holdsContent == true)
            {
                return true;
            }

            _logger.LogInformation(
                "Completed job {JobId} carries this version save's key, but document {DocumentId} {State}; " +
                "treating the save as a new operation (task 047).",
                existingJob.JobId,
                request.Document.ExistingDocumentId,
                holdsContent == false
                    ? "now holds different content (a later save wrote another version)"
                    : "could not be read to confirm it still holds this content");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not provably the same operation. The save proceeds, and its own validation refuses or writes.
            _logger.LogWarning(ex,
                "Could not confirm that completed job {JobId} still describes its document; treating the save as a new " +
                "operation (task 047).",
                existingJob.JobId);
            return false;
        }
    }

    private static string HashContent(string? contentBase64) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(contentBase64 ?? string.Empty)));

    /// <summary>
    /// Task 025 (spaarkeai-word-add-in-r1), extended to immutable captures by task 054: decides what a
    /// create's name collision means and what to do about it — the ENTIRE collision-resolution contract in
    /// one auditable place, mirroring the shape of <see cref="ResolveVersionTargetAsync"/> (a
    /// decision-or-refusal tuple, no job/response mechanics inside it). Returns EITHER a refusal
    /// (<see cref="SaveError"/>; <c>Upload</c> is the original, still-collided result and MUST NOT be used
    /// further) OR the upload to continue processing with (<c>Refusal</c> is <c>null</c>). Never both;
    /// never neither.
    /// </summary>
    /// <remarks>
    /// <para><b>Three outcomes, in the order they are decided.</b></para>
    /// <list type="number">
    /// <item><b>Nothing owns the name</b> — an orphan left by an earlier attempt that uploaded but failed
    /// before its Dataverse row was written. Reclaimed via <see cref="ConflictBehavior.Replace"/> rather
    /// than refused, or a transient failure would become a permanent lockout (task 025).</item>
    /// <item><b>The name is owned, the content is IMMUTABLE, and the stored file already holds exactly
    /// these bytes</b> (task 054) — that is the content duplicate the suppress path exists for, so the
    /// upload proceeds under <see cref="ConflictBehavior.Replace"/> exactly as it did before this task and
    /// the caller's dedup branch answers it. Unreachable for Document.</item>
    /// <item><b>Otherwise the name is owned</b> — refused with <see cref="OfficeErrorCodes.NameCollision"/>,
    /// having written nothing.</item>
    /// </list>
    /// <para><b>Task 054 — why the CONTENT is compared, and why before the upload.</b> An immutable
    /// capture's safety net is content-hash dedup, which runs AFTER the upload because the hash is SPE's; a
    /// name refusal must decide BEFORE it. A name alone cannot separate the two cases that share it: two
    /// saves of the SAME capture are a duplicate suppress is right to collapse, while two DIFFERENT emails
    /// that happen to share a user-typed name are two documents that must become two files. What separates
    /// them is the content, and <see cref="OfficeStorageUploader.ItemHoldsContentAsync"/> (task 047) answers
    /// exactly that against the already-stored item before anything is written — a byte comparison, never a
    /// re-implemented hash, so it cannot disagree with what SPE holds.
    /// <c>ContentDedupDetector</c> is deliberately NOT touched: its answer is correct for its other callers
    /// (email-r2, Compose), and the ordering problem belongs to this caller.</para>
    /// <para><b>Fail-safe.</b> A collision target that cannot be resolved or read answers "not provably
    /// identical" and is REFUSED. A refusal is recoverable — the user renames and retries; overwriting
    /// another document's file is not. Same direction as task 046's cleanup guard.</para>
    /// <para><b>Document saves are unchanged by task 054.</b> Two Word drafts that are byte-identical right
    /// now are still two distinct drafts (NFR-08), so an editable collision is refused on the NAME alone,
    /// exactly as task 025 left it. Only an editable refusal can carry <c>CanSaveAsVersion = true</c>: FR-11's
    /// version-save retry is Document-only — an Email/Attachment save carrying <c>ExistingDocumentId</c>
    /// ignores it and creates its own document (pinned by
    /// <c>OfficeVersionSaveContractTests.Post_OfficeSave_EmailOrAttachmentCarryingExistingDocumentId_IgnoresIt_AndBehavesAsBefore</c>),
    /// so advertising that retry for an immutable capture would offer the pane a choice the server does not
    /// honour.</para>
    /// <para><b>Task 088 (UAT-5).</b> Every OWNED collision now carries the owning document's id and display
    /// name, whatever its content type and wherever it is filed, so the pane can offer "Open". The version
    /// retry is the separate <c>CanSaveAsVersion</c> flag (editable AND filed to the target record). The
    /// endpoint strips id, name and flag when the caller cannot read that document.</para>
    /// </remarks>
    private async Task<(SaveError? Refusal, OfficeStorageUploader.UploadResult Upload)> ResolveNameCollisionAsync(
        OfficeStorageUploader.UploadResult collidedUpload,
        string containerId,
        string fileName,
        Stream contentStream,
        SaveContentType contentType,
        // Task 055 (#1005): the record this save is filing to. Used ONLY to compare against the colliding
        // document's own associations — a pure comparison, never an authorization decision (that is the
        // endpoint's, per ADR-008 and notes/055-collision-names-its-target.md §2).
        SaveEntityReference? targetEntity,
        CancellationToken cancellationToken)
    {
        // Resolve which row already holds this name in this drive — read-only, best-effort — so the pane
        // can offer "Save as new version" as a direct retry through the ALREADY-SHIPPED version-save path
        // (Document.ExistingDocumentId + IsNewVersion), never a second write mechanism.
        //
        // Task 055: this lookup now also returns the row's display name and direct associations, from the
        // SAME single query — an id alone cannot answer "which document is this" or "is it filed where the
        // caller is filing", and both are required before offering to write into it.
        var collisionTarget = collidedUpload.DriveId is { } collisionDriveId
            ? await _documentPersistence.FindCollisionTargetByLocationAsync(collisionDriveId, fileName, cancellationToken)
            : null;
        var collidingDocumentId = collisionTarget?.DocumentId;

        if (collidingDocumentId is null)
        {
            // The colliding item is UNREFERENCED — no sprk_document points at it. Without this reclaim, a
            // create that uploads successfully but then fails BEFORE the Dataverse row is written (a
            // transient Dataverse error, task 039's FailNextDocumentCreate scenario) would leave an
            // orphaned item that PERMANENTLY refuses every retry under the same name — turning a transient
            // failure into a lockout, which is a worse outcome than the D1 defect this task removes.
            // Reclaiming is safe here specifically because nothing owns the name: no existing document's
            // bytes are at risk, so Replace is the ORIGINAL, still-correct semantics for "this name is mine
            // to use." Never reached for an owned collision — that always returns the refusal below.
            _logger.LogInformation(
                "Name collision on '{FileName}' resolved to an unreferenced item (no owning document); " +
                "reclaiming it rather than refusing.",
                fileName);

            // The stream was already read once by the collided attempt (Fail still sends the PUT body;
            // Graph rejects only after receiving it) — rewind before reusing it, or the reclaim would
            // upload zero/partial bytes. MemoryStream (every Document save's content stream) is always
            // seekable; the CanSeek guard is defensive for any future non-seekable source, which would
            // instead surface as an ordinary upload failure below rather than silently corrupting content.
            if (contentStream.CanSeek)
            {
                contentStream.Position = 0;
            }

            var reclaimed = await _storageUploader.UploadToSpeAsync(
                containerId, fileName, contentStream, cancellationToken, ConflictBehavior.Replace);
            return (null, reclaimed);
        }

        var isEditable = OfficeDocumentPersistence.IsEditableContent(contentType);

        if (!isEditable)
        {
            // Task 054: the name is owned AND this is an immutable capture, so the question the suppress
            // path would have answered after the upload has to be answered now, from the content.
            var holdsSameContent = await CollisionTargetHoldsRequestContentAsync(
                collidingDocumentId.Value, contentStream, cancellationToken);

            if (holdsSameContent == true)
            {
                // The stored file already holds exactly these bytes, so this save IS the duplicate the
                // immutable suppress path is for. Continue under Replace — byte-for-byte the behaviour
                // before task 054 — and let ContentDedupDetector reconcile it as it always has. The
                // rewind matters: Fail still sends the PUT body, so the stream was consumed, and reading
                // it for the comparison above consumed it again.
                _logger.LogInformation(
                    "Name collision on '{FileName}' resolved to document {ExistingDocumentId}, whose file already "
                    + "holds exactly these bytes; continuing as a content duplicate rather than refusing.",
                    fileName, collidingDocumentId);

                if (contentStream.CanSeek)
                {
                    contentStream.Position = 0;
                }

                var deduplicated = await _storageUploader.UploadToSpeAsync(
                    containerId, fileName, contentStream, cancellationToken, ConflictBehavior.Replace);
                return (null, deduplicated);
            }

            _logger.LogWarning(
                "Name collision refused: '{FileName}' is already held by document {ExistingDocumentId}, whose file "
                + "{State}. Nothing was uploaded, so that document's file is untouched.",
                fileName, collidingDocumentId,
                holdsSameContent == false
                    ? "holds different content — this is a different capture that happens to share the name"
                    : "could not be read to confirm its content (fail-safe: an unknown is never 'identical')");
        }
        else
        {
            _logger.LogInformation(
                "Name collision refused: a file named '{FileName}' already exists (existing document {ExistingDocumentId}).",
                fileName, collidingDocumentId);
        }

        // ══ TASK 055 (#1005 / ISS-006) — the version retry is offered ONLY when the colliding document is
        // ══ already filed where this save is filing. (Task 088: that is now the CanSaveAsVersion flag; the
        // ══ document's identity travels regardless, for the pane's "Open".)
        //
        // The defect this closes: Word's default upload name is "Untitled Document.docx" (getSubject() falls
        // back to it whenever the Title property is blank, which is the norm) and containers are
        // BUSINESS-UNIT scoped with a flat root — so that one name is a single shared slot for an entire
        // business unit. A collision therefore routinely resolves an UNRELATED document. Offering "Save as
        // new version" against it wrote a patent report as a version of a stranger's row, profiled and
        // RAG-indexed under it, while the matter the user selected received nothing: the version path sends
        // no TargetEntity (task 023 D-4/D-5, deliberately and correctly), so the caller's chosen record is
        // silently discarded and the bytes land wherever the COLLIDING document happens to be filed.
        //
        // The fix is to withhold the offer, NOT to re-associate. Re-associating would re-file another user's
        // document onto this caller's record — worse than the defect, and across the authorization boundary
        // task 023 drew. An empty association set (the orphan actually observed on 2026-09-18) is a
        // non-match by construction, which is why this is phrased as "matches", never "does not conflict".
        var associationMatches = targetEntity is { EntityId: var targetId }
            && collisionTarget is not null
            && collisionTarget.DirectAssociationIds.Contains(targetId);

        // Immutable captures get no version-save retry either — FR-11's version path is Document-only.
        var canSaveAsVersion = isEditable && associationMatches;

        if (isEditable && !associationMatches)
        {
            _logger.LogInformation(
                "Collision refusal for '{FileName}': the owning document {ExistingDocumentId} is not filed to "
                + "the record this save targets, so no version retry is offered.",
                fileName, collidingDocumentId);
        }

        // ══ TASK 088 (UAT-5) — the identity travels on EVERY owned collision; the version retry is a FLAG.
        //
        // Until task 088 the id's PRESENCE was the version-retry signal, so the #1005 rule above had to
        // withhold the identity to withhold the offer — which also took away any way to reach the other file
        // ("Name Already Exists" offered Keep both + Dismiss only, UAT-5). The two meanings are now separate:
        // CanSaveAsVersion carries the #1005 rule, and the id + name let the pane offer "Open". Nothing here is
        // an authorization decision: the endpoint (WithholdCollisionIdentityIfUnauthorizedAsync, ADR-008)
        // strips the id, the name AND the flag unless the caller holds Read on that document, and Open itself
        // goes through GET /api/documents/{id}/open-links, whose filter re-checks Read.
        return (new SaveError
        {
            Code = OfficeErrorCodes.NameCollision,
            Message = $"A file named \"{fileName}\" already exists here. Nothing was uploaded or changed.",
            Retryable = false,
            FileName = fileName,
            ExistingDocumentId = collidingDocumentId,
            // The name travels WITH the id, never without it.
            ExistingDocumentName = collisionTarget?.DocumentName,
            CanSaveAsVersion = canSaveAsVersion
        }, collidedUpload);
    }

    /// <summary>
    /// Task 054 (spaarkeai-word-add-in-r1): does the document that already owns a collided name hold EXACTLY
    /// the bytes this save is trying to upload? <c>true</c> / <c>false</c> when the comparison was actually
    /// made; <c>null</c> when it could not be (the row carries no SPE pointers, its file could not be read,
    /// or the row itself could not be resolved). An unknown is never "identical" — the caller refuses.
    /// </summary>
    /// <remarks>
    /// Reuses the two seams that already exist for this exact question rather than adding a third:
    /// <see cref="OfficeDocumentPersistence.ResolveVersionTargetAsync"/> for the row's SPE pointers, and
    /// <see cref="OfficeStorageUploader.ItemHoldsContentAsync"/> (task 047) for the byte comparison — which
    /// stops at the first differing byte and never reads more than the request's own length.
    /// </remarks>
    private async Task<bool?> CollisionTargetHoldsRequestContentAsync(
        Guid collidingDocumentId,
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        OfficeDocumentPersistence.VersionTarget? target;
        try
        {
            target = await _documentPersistence.ResolveVersionTargetAsync(collidingDocumentId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not resolve sprk_document {DocumentId}, which owns a collided name; its content is unknown.",
                collidingDocumentId);
            return null;
        }

        // No pointers means there is no stored file to compare against — not "the same", just unknown.
        if (target is not { HasSpePointers: true })
        {
            return null;
        }

        var content = ReadAllBytes(contentStream);
        if (content.Length == 0)
        {
            return null;
        }

        return await _storageUploader.ItemHoldsContentAsync(
            target.DriveId!, target.ItemId!, content, cancellationToken);
    }

    /// <summary>
    /// Task 054: the full content of a save's content stream, rewound first so the stream can still be
    /// uploaded afterwards. Every save's stream is an in-memory, seekable one built in <c>SaveAsync</c>.
    /// </summary>
    private static byte[] ReadAllBytes(Stream content)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// FR-11 (task 023): validates a version save's intent and resolves its target row. Returns the target, or
    /// a refusal whose code is distinct per cause. Runs BEFORE any ProcessingJob, SPE write or document row.
    /// </summary>
    private async Task<(OfficeDocumentPersistence.VersionTarget? Target, SaveError? Refusal)> ResolveVersionTargetAsync(
        DocumentMetadata document,
        CancellationToken cancellationToken)
    {
        // IsNewVersion is the explicit-intent half of the contract. A request naming an existing document while
        // declaring "not a new version" is ambiguous, and both guesses are data-integrity faults: a second row
        // (the defect this path removes) or an overwrite nobody asked for. So it is refused, not interpreted.
        if (!document.IsNewVersion)
        {
            return (null, new SaveError
            {
                Code = "OFFICE_018",
                Message = "A save that names an existing document must set isNewVersion to true. Nothing was saved.",
                Retryable = false
            });
        }

        var existingDocumentId = document.ExistingDocumentId!.Value;
        var target = await _documentPersistence.ResolveVersionTargetAsync(existingDocumentId, cancellationToken);

        if (target is null)
        {
            _logger.LogWarning(
                "Version save refused: existing document {ExistingDocumentId} was not found.", existingDocumentId);
            return (null, new SaveError
            {
                Code = "OFFICE_016",
                Message = "The document this save was meant to add a version to could not be found. Nothing was saved.",
                Retryable = false
            });
        }

        if (!target.HasSpePointers)
        {
            // Never fall back to uploading a fresh item: that is a second document, not a version.
            _logger.LogWarning(
                "Version save refused: existing document {ExistingDocumentId} has no SPE pointers.", existingDocumentId);
            return (null, new SaveError
            {
                Code = "OFFICE_017",
                Message = "The document this save was meant to add a version to has no file in storage. Nothing was saved.",
                Retryable = false
            });
        }

        // uac-r2 task 171: the version is written AS THE APPLICATION (the user may hold no role on the container — every
        // secure container, and any BU container they were not added to), so the row's pointer must name a container and
        // item this document may use. Under OBO, SPE's own ACL did that; under app-only a re-pointed row would otherwise
        // let a Write holder overwrite another container's file. Reuses OFFICE_017 ("no usable file in storage") rather
        // than minting a code the task pane has no message for — the file this row names is not one it may write.
        if (!await _containerResolver.IsDocumentPointerContainerAllowedAsync(
                existingDocumentId, target.DriveId, target.ItemId, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "Version save refused: existing document {ExistingDocumentId}'s storage pointer could not be verified.",
                existingDocumentId);
            return (null, new SaveError
            {
                Code = "OFFICE_017",
                Message = "The document's file in storage could not be verified, so a new version could not be written. "
                          + "Nothing was saved. An administrator must repair the document's storage location.",
                Retryable = false
            });
        }

        return (target, null);
    }

    /// <summary>
    /// FR-11 (task 023): the version path's write half — a new SPE version of the target's own drive item, the
    /// target row's file metadata refreshed, and finalization queued against the EXISTING document id.
    /// </summary>
    /// <remarks>
    /// <para>No row is created (the one-row invariant) and no membership <c>Added</c> event is published (no
    /// new row, so no ownership change). The job phases reuse the names the task pane keys its progress list
    /// on (<c>FileUploaded</c>, <c>RecordsCreated</c>, <c>Complete</c> — <c>useSaveFlow.ts</c>); on this path
    /// <c>RecordsCreated</c> means "the existing record was updated".</para>
    /// </remarks>
    private async Task<SaveResponse> CompleteVersionSaveAsync(
        OfficeDocumentPersistence.VersionTarget target,
        SaveRequest request,
        string userId,
        HttpContext httpContext,
        Guid jobId,
        JobStatusResponse jobRecord,
        string idempotencyKey,
        string correlationId,
        Stream content,
        string sanitizedFileName,
        long fileSize,
        CancellationToken cancellationToken)
    {
        var driveId = target.DriveId!;
        var itemId = target.ItemId!;

        var write = await _storageUploader.WriteNewVersionAsync(driveId, itemId, content, cancellationToken);
        if (!write.Success)
        {
            jobRecord = jobRecord with
            {
                Status = JobStatus.Failed,
                CurrentPhase = "UploadFailed",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _jobs.RecordAsync(jobRecord, write.Error, cancellationToken);

            var code = write.ErrorCode ?? "OFFICE_012";
            return new SaveResponse
            {
                Success = false,
                Error = new SaveError
                {
                    Code = code,
                    Message = code switch
                    {
                        "OFFICE_017" => "The document's file was not found in storage, so a new version could not be written. Nothing was saved.",
                        "OFFICE_019" => "The document is locked for editing, so a new version could not be written. Nothing was saved; try again when it is released.",
                        "OFFICE_009" => "You do not have permission to write this document's file. Nothing was saved.",
                        _ => "Failed to write the new version to storage"
                    },
                    Details = write.Error,
                    Retryable = code is "OFFICE_012" or "OFFICE_019"
                }
            };
        }

        jobRecord = jobRecord with { Status = JobStatus.Running, Progress = 30, CurrentPhase = "FileUploaded" };
        await _jobs.RecordAsync(jobRecord, null, cancellationToken);

        var metadataRefreshed = await _documentPersistence.RecordNewVersionAsync(
            target, write.ItemName, write.WebUrl, fileSize, cancellationToken);

        jobRecord = jobRecord with { Progress = 50, CurrentPhase = "RecordsCreated" };
        await _jobs.RecordAsync(jobRecord, null, cancellationToken);

        // The EXISTING document id and the SAME drive item: downstream artifacts and AI attach to this record.
        // The file name is SPE's (a PUT by item id does not rename the item), falling back to the row's.
        // Task 029: isVersionSave stamps THIS save's job id on the payload, so the profile and index refresh for the
        // version just written instead of being answered "already processed" by the first save's keys.
        await _jobQueue.QueueUploadFinalizationAsync(
            jobId,
            idempotencyKey,
            correlationId,
            userId,
            request,
            driveId,
            itemId,
            write.ItemName ?? target.FileName ?? sanitizedFileName,
            fileSize,
            target.DocumentId,
            isVersionSave: true,
            owningTeamId: null, // a version save creates no document; its row keeps its existing owner
            cancellationToken);

        jobRecord = jobRecord with
        {
            Status = JobStatus.Completed,
            Progress = 100,
            CurrentPhase = "Complete",
            CompletedAt = DateTimeOffset.UtcNow,
            // Task 039 (finding 3): the EXISTING document — a version never creates one.
            Result = DocumentResult(target.DocumentId, itemId, driveId, write.WebUrl)
        };
        await _jobs.RecordAsync(jobRecord, null, cancellationToken);

        _logger.LogInformation(
            "ProcessingJob {JobId} completed: new SPE version of item {ItemId} saved to existing document {DocumentId} " +
            "(metadata refreshed: {MetadataRefreshed}, comment supplied: {HasComment}). Finalization queued.",
            jobId, itemId, target.DocumentId, metadataRefreshed, !string.IsNullOrWhiteSpace(request.Document?.VersionComment));

        return new SaveResponse
        {
            Success = true,
            Duplicate = false,
            JobId = jobId,
            StatusUrl = $"/api/office/jobs/{jobId}",
            StreamUrl = $"/api/office/jobs/{jobId}/stream"
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Task 060 (#1084): read from the job's Dataverse row through <see cref="OfficeJobStatusService"/>. It used to be a
    /// process-wide static dictionary, with a Dataverse fallback that read an anonymous type through <c>dynamic</c> and
    /// therefore threw in production, so a restart or a second instance answered 404.
    /// </remarks>
    public Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string? userId,
        CancellationToken cancellationToken = default)
        => _jobs.GetAsync(jobId, userId, cancellationToken);

    /// <inheritdoc />
    public Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Without ownership validation: authorization filters use this overload to verify the job exists and read its
        // recorded creator.
        return _jobs.GetAsync(jobId, userId: null, cancellationToken);
    }
    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        // Basic health check - always returns true for now
        // Will be expanded to check dependencies (Dataverse, SPE, etc.)
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    /// <remarks>Delegates to <see cref="OfficeSearchService"/> (task 059). The search code moved there verbatim.</remarks>
    public Task<EntitySearchResponse> SearchEntitiesAsync(
        EntitySearchRequest request,
        string userId,
        Guid callerSystemUserId,
        CancellationToken cancellationToken = default)
        => _search.SearchEntitiesAsync(request, userId, callerSystemUserId, cancellationToken);

    /// <inheritdoc />
    /// <remarks>Delegates to <see cref="OfficeSearchService"/> (task 059).</remarks>
    public Task<ReferenceListResponse> GetReferenceListAsync(
        OfficeReferenceList list,
        CancellationToken cancellationToken = default)
        => _search.GetReferenceListAsync(list, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>Slice 3 (#10, email-communication-intelligence-r2 2026-09-02): implements the inline
    /// "New record" for the add-in "Related to" picker. Scope = <b>Matter, Project, Invoice</b>; other types
    /// return null (endpoint 403s) until built out.</para>
    /// <para><b>Matter</b> (task 030, FR-13) and <b>Project</b> (task 031, FR-13) are created complete by
    /// <see cref="RecordCreationService"/>: the caller as a <b>load-bearing</b> owner, business-unit defaults, the
    /// Field Mapping Framework, the matter-type and practice-area lookups (Matter) or the project-type lookup
    /// (Project) when supplied, and the Assigned To contact (task 100). Neither writes its entity's number — the
    /// platform's autonumber assigns <c>sprk_matternumber</c> / <c>sprk_projectnumber</c> (task 076, interim).
    /// A refusal surfaces as <see cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException"/> (no row
    /// written); see <see cref="QuickCreateViaCreationServiceAsync"/>.</para>
    /// <para><b>Invoice</b> keeps the minimal path: the generic Dataverse create
    /// (<see cref="IGenericEntityService.CreateAsync"/>) with the name, the description and the Assigned To contact
    /// (task 100). It is owned by the caller's business-unit
    /// default owner team (task 080, invariant I-6), and REFUSED with <see cref="OfficeErrorCodes.RecordOwnerUnresolved"/>
    /// when no team resolves — no longer best-effort, which left an unresolved caller's invoice app-owned in ROOT.
    /// There is no impersonated-create helper in the BFF, so ownership is set via the <c>ownerid</c> lookup rather
    /// than MSCRMCallerID.</para>
    /// </remarks>
    public async Task<QuickCreateResponse?> QuickCreateAsync(
        QuickCreateEntityType entityType,
        QuickCreateRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Quick create requested for {EntityType} by user {UserId}",
            entityType,
            userId);

        // Scope: Matter + Project + Invoice (UI feedback 2026-09-02). Others not yet supported.
        if (entityType is not (QuickCreateEntityType.Matter
            or QuickCreateEntityType.Project
            or QuickCreateEntityType.Invoice))
        {
            _logger.LogInformation(
                "Quick create for {EntityType} is not yet supported (Matter/Project/Invoice only).",
                entityType);
            return null;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null; // endpoint validates Name; guard defensively
        }

        // FR-13: Matter (task 030) and Project (task 031) both go through the shared server-side creation service —
        // load-bearing owner, BU defaults, Field Mapping Framework, plus the matter-type lookup for Matter. Neither
        // writes its entity's number; a separate on-create numbering component owns both. Invoice stays on the
        // minimal path below.
        if (entityType is QuickCreateEntityType.Matter or QuickCreateEntityType.Project)
        {
            return await QuickCreateViaCreationServiceAsync(
                    entityType, name, request, userId, ownerSystemUserId, cancellationToken)
                .ConfigureAwait(false);
        }

        // Invoice only: name, description and Assigned To (task 100, owner UAT round 5 item 3).
        var logicalName = QuickCreateFieldRequirements.GetLogicalName(entityType);

        var entity = new Microsoft.Xrm.Sdk.Entity(logicalName);
        // sprk_name is sprk_invoice's primary name attribute (live metadata, 2026-10-01). This wrote
        // "sprk_invoicename", which sprk_invoice does not have, so Dataverse refused every invoice quick-create
        // (#1079, task 085). sprk_billingevent is the entity that has a sprk_invoicename column.
        entity["sprk_name"] = name;

        // Task 100 — both columns verified against live metadata (spaarkedev1, 2026-10-05): sprk_description (Memo,
        // "Description") and sprk_assignedto1 (contact lookup, "Assigned To 1" — the invoice's first Assigned To slot;
        // sprk_invoice has no sprk_assignedtointernal). The contact id was authorized (caller holds Read) by
        // QuickCreateSourceAccessFilter before this runs. An invoice has NO server default assignee: absent means
        // unassigned (unlike Matter/Project, which name the maker — unified-access-control-r2 task 152).
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            entity[InvoiceDescriptionAttribute] = request.Description.Trim();
        }

        if (request.AssignedToContactId is { } invoiceAssignee && invoiceAssignee != Guid.Empty)
        {
            entity[InvoiceAssignedToAttribute] = new Microsoft.Xrm.Sdk.EntityReference("contact", invoiceAssignee);
        }

        // Ownership (ADR-034 — ownership is what confers access; NOT ADR-024, which is the polymorphic RESOLVER
        // pattern and says nothing about ownerid, a miscitation corrected 2026-09-22). Task 080 (invariant I-6): the
        // caller's business-unit DEFAULT OWNER TEAM. A new invoice is filed against nothing here, so the acting user's
        // business unit decides. This used to be best-effort — an unresolved caller left the invoice app-owned in the
        // ROOT business unit, invisible to its own creator — and is now a refusal, like Matter and Project.
        var invoiceOwner = await _ownershipResolver.ResolveOwnerAsync(
            new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
            {
                CallerSystemUserId = Guid.TryParse(ownerSystemUserId, out var callerSystemUserId)
                    ? callerSystemUserId
                    : null,
                CallerObjectId = Guid.TryParse(userId, out var callerObjectId) ? callerObjectId : null,
                // Task 146 c1-r1 (owner round 13 item 9): the Office user asked for the invoice the app creates.
                RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.Of(
                    Guid.TryParse(ownerSystemUserId, out var requesterId) ? requesterId : null,
                    Guid.TryParse(userId, out var requesterOid) ? requesterOid : null),
            },
            cancellationToken).ConfigureAwait(false);
        var invoiceOwnerTeamId = invoiceOwner.IsOwned ? invoiceOwner.OwningTeamId : null;

        if (invoiceOwnerTeamId is null)
        {
            // Rendered by the quick-create endpoint's SdapProblemException catch; no row was written.
            throw new SdapProblemException(
                OfficeErrorCodes.RecordOwnerUnresolved,
                $"{QuickCreateFieldRequirements.GetDisplayName(entityType)} Not Created",
                "The new invoice could not be assigned to your business unit's team, so it was not created. Ask an "
                + "administrator to check that your user has a business unit and that the unit has its default team.",
                OfficeErrorCodes.GetStatusCode(OfficeErrorCodes.RecordOwnerUnresolved));
        }

        entity["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", invoiceOwnerTeamId.Value);
        invoiceOwner.StampCreatorOn(entity); // task 146 c1-r1 — the Office user, on the app-created invoice

        var createdId = await _genericEntityService.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Quick create completed: EntityType={EntityType}, Id={Id}, Name={Name}",
            entityType,
            createdId,
            name);

        return new QuickCreateResponse
        {
            Id = createdId,
            EntityType = entityType,
            LogicalName = logicalName,
            Name = name,
            // Org URL isn't known server-side (the add-in must not be org-pinned); the add-in uses
            // Id + Name to select the new record as the regarding, not the Url.
            Url = null
        };
    }

    /// <summary>
    /// The Matter (task 030) and Project (task 031) leg of <see cref="QuickCreateAsync"/>, FR-13: delegates to
    /// <see cref="RecordCreationService"/> and adapts its outcome to this method's contract.
    /// </summary>
    /// <remarks>
    /// <para>A structured <see cref="RecordCreationFailure"/> is surfaced as the codebase's typed-problem exception
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException"/>) because
    /// <see cref="IOfficeService.QuickCreateAsync"/> returns <c>QuickCreateResponse?</c> and its signature cannot
    /// change here: the shared <c>Phase2EndToEndFixture</c> mocks it. The endpoint renders that exception in the
    /// Office ProblemDetails shape. <see cref="RecordCreationService"/> itself returns the failure as data — that is
    /// the contract the wizard-migration evaluation consumes.</para>
    /// <para>Owner attribution is LOAD-BEARING for both (an unresolved caller is refused, 403), unlike the
    /// best-effort posture the Invoice leg keeps. Every optional field is passed through for both; each path reads
    /// only its own (<c>MatterTypeId</c> / <c>PracticeAreaId</c> on a Matter, <c>ProjectTypeId</c> on a Project).</para>
    /// </remarks>
    private async Task<QuickCreateResponse?> QuickCreateViaCreationServiceAsync(
        QuickCreateEntityType entityType,
        string name,
        QuickCreateRequest request,
        string userId,
        string? ownerSystemUserId,
        CancellationToken cancellationToken)
    {
        var result = await _recordCreation.CreateAsync(
            new RecordCreationRequest
            {
                EntityType = entityType,
                Name = name,
                Description = request.Description,
                CallerUserId = userId,
                OwnerSystemUserId = ownerSystemUserId,
                MatterTypeId = request.MatterTypeId,
                PracticeAreaId = request.PracticeAreaId,
                ProjectTypeId = request.ProjectTypeId,
                AssignedToContactId = request.AssignedToContactId,
                SourceEntityLogicalName = request.SourceEntityType,
                SourceRecordId = request.SourceRecordId,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Failure is { } failure)
        {
            throw new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                failure.Code,
                $"{QuickCreateFieldRequirements.GetDisplayName(entityType)} Not Created",
                failure.Detail,
                MapCreationFailureStatus(failure.Kind));
        }

        return new QuickCreateResponse
        {
            Id = result.RecordId,
            EntityType = entityType,
            LogicalName = result.LogicalName,
            Name = result.Name,
            Warnings = result.Warnings.Count > 0 ? result.Warnings : null,
            // Org URL isn't known server-side (the add-in must not be org-pinned).
            Url = null
        };
    }

    /// <summary><c>sprk_invoice</c> description column (Memo, "Description"; live metadata 2026-10-05, task 100).</summary>
    internal const string InvoiceDescriptionAttribute = "sprk_description";

    /// <summary><c>sprk_invoice</c> "Assigned To 1" contact lookup — the invoice's Assigned To (live metadata 2026-10-05, task 100).</summary>
    internal const string InvoiceAssignedToAttribute = "sprk_assignedto1";

    /// <summary>HTTP status for each creation refusal. Every refusal means no row was written.</summary>
    internal static int MapCreationFailureStatus(RecordCreationFailureKind kind) => kind switch
    {
        RecordCreationFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
        RecordCreationFailureKind.OwnerUnresolved => StatusCodes.Status403Forbidden,
        // Task 076: the platform's next numbers were all held by rows; a retry later (or a re-seed) succeeds.
        RecordCreationFailureKind.NumberUnavailable => StatusCodes.Status409Conflict,
        // Task 158 r1: a caller walled off (or without the rights to create under) a secure record — 403; a secure create that
        // could not be checked or completed (and was removed again) — 500.
        RecordCreationFailureKind.SecureFilingRefused => StatusCodes.Status403Forbidden,
        RecordCreationFailureKind.SecureFilingFailed => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>Friendly regarding type → (entity-specific <c>sprk_todo</c> lookup attribute, target logical name).
    /// Mirrors <c>TodoRegardingUpdateBuilder.TODO_REGARDING_CATALOG</c>. The first three entries are the types
    /// the add-in "Related to" picker offers (Matter/Project/Invoice) and are looked up via
    /// <see cref="CreateTodoRequest.RegardingEntityType"/>. The <c>Document</c> / <c>Communication</c> entries
    /// (FR-14, task 035) are the pane's independent "carrying" regarding — looked up via
    /// <see cref="CreateTodoRequest.DocumentId"/> / <see cref="CreateTodoRequest.CommunicationId"/>, NOT via
    /// <c>RegardingEntityType</c> — because both a carrier and a record may be set on the same call. See
    /// <c>notes/035-todo-regarding-decision.md</c>.
    ///
    /// <para><b>Internal, not private (task 064).</b> <see cref="Sprk.Bff.Api.Api.Filters.TodoSourceAccessFilter"/>
    /// read-gates exactly the ids this table causes to be WRITTEN, and it drives off this table rather than
    /// declaring its own copy. That is the forcing function: adding a row here automatically gates the new
    /// type, and a row that disappears un-gates a write that also stops happening. Two tables would have
    /// drifted, and the drift's failure direction is an ungated write.</para></summary>
    internal static readonly IReadOnlyDictionary<string, (string LookupAttribute, string LogicalName)> TodoRegardingMap =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Matter"] = ("sprk_regardingmatter", "sprk_matter"),
            ["Project"] = ("sprk_regardingproject", "sprk_project"),
            ["Invoice"] = ("sprk_regardinginvoice", "sprk_invoice"),
            ["Document"] = ("sprk_regardingdocument", "sprk_document"),
            ["Communication"] = ("sprk_regardingcommunication", "sprk_communication"),
        };

    /// <inheritdoc />
    public async Task<CreateTodoResponse?> CreateTodoAsync(
        CreateTodoRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Create To Do requested by user {UserId}", userId);

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null; // endpoint validates Name; guard defensively.
        }

        // Core sprk_todo body (mirrors CreateTodoWizard's todoService.createTodo — statecode/statuscode Open+Active).
        var entity = new Microsoft.Xrm.Sdk.Entity("sprk_todo");
        entity["sprk_name"] = name;
        entity["statecode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0);   // Active
        entity["statuscode"] = new Microsoft.Xrm.Sdk.OptionSetValue(1);  // Open

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            entity["sprk_description"] = request.Description!.Trim();
        }

        // sprk_duedate is Date-Only — write UTC-midnight of the supplied yyyy-MM-dd (best-effort parse).
        if (!string.IsNullOrWhiteSpace(request.DueDate)
            && DateOnly.TryParse(request.DueDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var due))
        {
            entity["sprk_duedate"] = due.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        // Priority/Effort scores (client resolved the choice → 0-100 score, mirroring the wizard).
        entity["sprk_priorityscore"] = request.PriorityScore;
        entity["sprk_effortscore"] = request.EffortScore;

        // Assignee → CONTACT (sprk_assignedto migrated systemuser → contact, 2026-06-21).
        if (request.AssignedToContactId is { } contactId && contactId != Guid.Empty)
        {
            entity["sprk_assignedto"] = new Microsoft.Xrm.Sdk.EntityReference("contact", contactId);
        }
        else
        {
            // Task 083 (#1044 writer half): task 080 made the owner a BU default team (above), so nothing on
            // the row names the person it is for, and the Daily Briefing (UAC-r2 task 152's people-targeting
            // surface, which matches sprk_assignedto through the SAME link) could not find it. Default to the
            // CALLER's linked contact — task 141's link, read through IIdentityNormalizationService exactly as
            // 141's contract directs (141-link-contract.md §6): resolve the caller's systemuserid with the
            // existing task-067 resolver (already done by the endpoint into ownerSystemUserId), then
            // ResolveAsync(...).ContactId. Two contacts on one oid, or an inactive one, resolve to null (141's
            // ambiguity rule) — never refused: a To Do that is hard to find beats a refused one. Do NOT call
            // ContactIdentityBinder here; linking is the reconciliation job's and the sign-in resolver's job.
            Guid? callerContactId = null;
            if (Guid.TryParse(ownerSystemUserId, out var callerSystemUserId))
            {
                try
                {
                    callerContactId = (await _identity.ResolveAsync(callerSystemUserId, cancellationToken)
                        .ConfigureAwait(false)).ContactId;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "The caller's linked contact could not be resolved for {CallerId}", callerSystemUserId);
                }
            }

            if (callerContactId is { } resolvedContactId && resolvedContactId != Guid.Empty)
            {
                entity["sprk_assignedto"] = new Microsoft.Xrm.Sdk.EntityReference("contact", resolvedContactId);
            }
            else
            {
                _logger.LogWarning(
                    "todo_assignee_unset: caller={CallerId} reason={Reason} — no assignee was chosen and the "
                    + "caller has no linked contact, so sprk_assignedto is left blank; the To Do is still "
                    + "created (never refused) but will not surface in the caller's Daily Briefing until the "
                    + "link exists",
                    ownerSystemUserId ?? "(unresolved)",
                    "caller_has_no_linked_contact");
            }
        }

        // Regarding (the filed record) — entity-specific lookup + ADR-024 resolver fields.
        if (!string.IsNullOrWhiteSpace(request.RegardingEntityType)
            && request.RegardingRecordId is { } regardingId && regardingId != Guid.Empty
            && TodoRegardingMap.TryGetValue(request.RegardingEntityType!, out var reg))
        {
            entity[reg.LookupAttribute] = new Microsoft.Xrm.Sdk.EntityReference(reg.LogicalName, regardingId);
            entity["sprk_regardingrecordid"] = regardingId.ToString();
            if (!string.IsNullOrWhiteSpace(request.RegardingRecordName))
            {
                entity["sprk_regardingrecordname"] = request.RegardingRecordName!.Trim();
            }

            // Best-effort record-type ref (denormalized ADR-024 resolver lookup). Non-fatal — the typed lookup
            // above is the load-bearing relationship; a missing record-type ref only affects cross-entity display.
            var recordTypeId = await _search.ResolveRegardingRecordTypeIdAsync(reg.LogicalName, cancellationToken)
                .ConfigureAwait(false);
            if (recordTypeId is { } rtId && rtId != Guid.Empty)
            {
                entity["sprk_regardingrecordtype"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_recordtype_ref", rtId);
            }

            // FR-26 core-ancestor stamp (task 052) — applied AFTER the typed lookup so it cannot be
            // overwritten. Two of the three types this picker offers are CORE (Matter, Project) and stamp
            // only themselves; INVOICE is child-class, so the add-in can already file a To Do under an
            // invoice today and that To Do would otherwise carry no matter/project stamp — invisible to
            // everyone whose access comes from the invoice's matter.
            var stamp = await _coreAncestors
                .StampAsync(entity, reg.LogicalName, regardingId, cancellationToken)
                .ConfigureAwait(false);

            if (!stamp.Succeeded)
            {
                // NFR-01 fail-closed, expressed in THIS method's existing error contract: a null return
                // is what every other guard here uses, and the endpoint maps it to a failure. Creating
                // the To Do without the stamp would be a silent inheritance hole, which is worse.
                _logger.LogError(
                    "Create To Do aborted: core-ancestor derivation failed for {RegardingType} {RegardingId}. {Error}",
                    reg.LogicalName, regardingId, stamp.Error);
                return null;
            }
        }

        // Carrying regarding (FR-14, task 035) — the open Word document or the Outlook email being filed.
        // Independent of the record regarding above: both may be written to the same sprk_todo (constraint:
        // "Setting only one when both are available is a defect, not a graceful degradation"). Does NOT touch
        // sprk_regardingrecordid/-name/-type (those describe the RECORD, per notes/035-todo-regarding-decision.md).
        // When a record regarding was chosen above, the stamp is that record's and a carrier adds none (the pair names
        // the record: CoreAncestorResolver.ClassifyStampSource reads every carrier as a carrier). When NO record was
        // chosen, a single carrier IS what the To Do is filed under (ClassifyStampSource rule 5) — so it is stamped
        // from that carrier below (unified-access-control-r2 task 156, verifier round 1 item 8).
        if (request.DocumentId is { } documentId
            && documentId != Guid.Empty
            && TodoRegardingMap.TryGetValue("Document", out var docCarrier))
        {
            entity[docCarrier.LookupAttribute] = new Microsoft.Xrm.Sdk.EntityReference(docCarrier.LogicalName, documentId);
        }

        if (request.CommunicationId is { } communicationId
            && communicationId != Guid.Empty
            && TodoRegardingMap.TryGetValue("Communication", out var commCarrier))
        {
            entity[commCarrier.LookupAttribute] = new Microsoft.Xrm.Sdk.EntityReference(commCarrier.LogicalName, communicationId);
        }

        // Carrier-only To Do (no record regarding): stamp it from what it is filed under, so it is BORN with the copy the
        // storage resolver compares and the reconciliation job keeps fresh. Before this, the carrier-only To Do was
        // created with no copy, so its first upload answered 409 container_ancestor_stale until the re-stamp landed, and
        // until then it inherited none of the carrier's project / matter / work assignment (C10 part 2: a child of a
        // secure record is secure). The ONE classification rule decides: exactly one carrier → that record; a document
        // AND an email with no record → AmbiguousSource → nothing is stamped (the storage resolver refuses it as
        // ambiguous, and it inherits nothing — fail closed). Same fail-closed contract as the record regarding above.
        if (!entity.Contains("sprk_regardingrecordid")
            && Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.ClassifyStampSource(
                    "sprk_todo", entity,
                    Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.PartyRegardingColumnNames("sprk_todo"))
                is { Kind: Sprk.Bff.Api.Services.Dataverse.StampSourceKind.Source, Source: { } carrierSource })
        {
            var carrierStamp = await _coreAncestors
                .StampAsync(entity, carrierSource.Intermediate, carrierSource.Id, cancellationToken)
                .ConfigureAwait(false);

            if (!carrierStamp.Succeeded)
            {
                _logger.LogError(
                    "Create To Do aborted: core-ancestor derivation failed for its {CarrierType} {CarrierId}. {Error}",
                    carrierSource.Intermediate, carrierSource.Id, carrierStamp.Error);
                return null;
            }
        }

        // Task 173 (owner round 81): the record regarding was stamped (and its Access Permission set) BEFORE the carriers were
        // added, so with a record AND a carrier the value is computed again over every parent the To Do now names — the
        // carrier's filing may be the more restrictive. A carrier-only To Do was already decided by its own stamp above.
        if (entity.Contains("sprk_regardingrecordid")
            && (entity.Contains("sprk_regardingdocument") || entity.Contains("sprk_regardingcommunication")))
        {
            await _coreAncestors.ApplyInheritedAccessPermissionAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        // Owner (task 080, write-path invariant I-6): a business unit's DEFAULT OWNER TEAM, never the app user and
        // never an individual. RECORD-FIRST — the To Do belongs with what it is filed against: the record regarding
        // first, then the document or email it was created from, and only when there is neither, the acting user.
        // Before this, a resolved caller owned the To Do and an unresolved one silently left it app-owned in ROOT.
        var (todoTeamId, todoCreatorPerson) = await ResolveTodoOwnerTeamAsync(
            entity, request, userId, ownerSystemUserId, cancellationToken).ConfigureAwait(false);
        entity["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", todoTeamId);
        // Task 146 c1-r1 (owner round 13 item 9): the Office user asked for the To Do the app creates.
        Spaarke.Dataverse.RecordCreatorPersonColumn.StampIfKnown(entity, todoCreatorPerson);

        var todoId = await _genericEntityService.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Create To Do completed: Id={TodoId}, Name={Name}, Regarding={RegardingType}",
            todoId, name, request.RegardingEntityType ?? "(none)");

        return new CreateTodoResponse { TodoId = todoId, Name = name };
    }

    /// <summary>
    /// The owner team for a new To Do (task 080): the default owner team of the business unit of whatever the To Do
    /// is filed against — the regarding record, else its document carrier, else its communication carrier — or the
    /// acting user's when it is filed against nothing.
    /// </summary>
    /// <remarks>
    /// Task 146 (secure-if-any): EVERY parent the To Do carries is passed — the record regarding, its core-ancestor
    /// stamps, and the document / email carriers — with the first named as the primary (its business unit decides an
    /// ordinary To Do). Any secure parent makes the To Do the named Secure team's; any named-but-unreadable parent
    /// refuses rather than being skipped (a carrier could be the secure one). Before 146 only the first target was
    /// passed, so a To Do on an ordinary matter carrying a SECURE document was owned in the ordinary unit.
    /// </remarks>
    /// <exception cref="SdapProblemException"><see cref="OfficeErrorCodes.RecordOwnerUnresolved"/> (403) when no team
    /// resolves. Thrown rather than returned as null so the endpoint can tell this refusal from its generic one.</exception>
    private async Task<(Guid Team, Guid? CreatorPerson)> ResolveTodoOwnerTeamAsync(
        Microsoft.Xrm.Sdk.Entity todo,
        CreateTodoRequest request,
        string userId,
        string? ownerSystemUserId,
        CancellationToken cancellationToken)
    {
        (string? LogicalName, Guid? Id) target =
            !string.IsNullOrWhiteSpace(request.RegardingEntityType)
            && request.RegardingRecordId is { } regardingId && regardingId != Guid.Empty
            && TodoRegardingMap.TryGetValue(request.RegardingEntityType!, out var regarding)
                ? (regarding.LogicalName, regardingId)
                : request.DocumentId is { } documentId && documentId != Guid.Empty
                    ? (TodoRegardingMap["Document"].LogicalName, documentId)
                    : request.CommunicationId is { } communicationId && communicationId != Guid.Empty
                        ? (TodoRegardingMap["Communication"].LogicalName, communicationId)
                        : (null, null);

        var primary = target.LogicalName is not null && target.Id is { } targetId
            ? new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(target.LogicalName, targetId)
            : null;
        var todoOwner = await _ownershipResolver.ResolveOwnerAsync(
            Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForChild(todo, primary) with
            {
                CallerSystemUserId = Guid.TryParse(ownerSystemUserId, out var callerSystemUserId)
                    ? callerSystemUserId
                    : null,
                CallerObjectId = Guid.TryParse(userId, out var callerObjectId) ? callerObjectId : null,
                // Task 146 c1-r1 (owner round 13 item 9): the Office user is the person who asked.
                RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.Of(
                    Guid.TryParse(ownerSystemUserId, out var requesterId) ? requesterId : null,
                    Guid.TryParse(userId, out var requesterOid) ? requesterOid : null),
            },
            cancellationToken).ConfigureAwait(false);
        var teamId = todoOwner.IsOwned ? todoOwner.OwningTeamId : null;

        return teamId is { } resolvedTeam ? (resolvedTeam, todoOwner.CreatedByPerson) : throw new SdapProblemException(
            OfficeErrorCodes.RecordOwnerUnresolved,
            OfficeErrorCodes.GetTitle(OfficeErrorCodes.RecordOwnerUnresolved),
            target.LogicalName is not null
                ? "The To Do could not be assigned an owner from the item it is filed to, so it was not created. "
                  + "Check that the item still exists and that you can open it."
                : "The To Do could not be assigned an owner from your business unit, so it was not created. Ask an "
                  + "administrator to check your user record's business unit.",
            OfficeErrorCodes.GetStatusCode(OfficeErrorCodes.RecordOwnerUnresolved));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<byte[]> StreamJobStatusAsync(
        Guid jobId,
        string? lastEventId,
        CancellationToken cancellationToken = default)
        => _jobs.StreamAsync(jobId, lastEventId, cancellationToken);
}
