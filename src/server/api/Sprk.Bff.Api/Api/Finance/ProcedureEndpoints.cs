using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>Create a version. Both dates are optional on a Draft (half-open: [from, to)).</summary>
public sealed record ProcedureVersionCreateBody(DateOnly? EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>Change a version's dates. A null member is left as it is; <see cref="ClearEffectiveTo"/> makes the version open-ended.</summary>
public sealed record ProcedureVersionDatesBody(DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool ClearEffectiveTo = false);

/// <summary>Publish a version from this date (else the version's own effective-from).</summary>
public sealed record ProcedureVersionPublishBody(DateOnly? EffectiveFrom);

/// <summary>
/// The drafting job of a Procedure Version was queued (task 116). <see cref="StatusUrl"/> is the Procedure's rule set read: a finished run shows
/// the version's not-converted list there (an empty list is <c>[]</c>).
/// </summary>
public sealed record ProcedureVersionDraftAccepted(Guid ProcedureVersionId, Guid RuleSetId, Guid JobId, string StatusUrl);

/// <summary>A Procedure Version's source file was stored (task 116): <see cref="FileId"/> is the new Stored File, now the version's one Servable file.</summary>
public sealed record ProcedureVersionFileStored(Guid ProcedureVersionId, Guid FileId);

/// <summary>
/// The Procedure and Procedure Version API (ebilling-r1 task 081, EB-D-148; procedure model 3.1, 5.1, 7.2, 8.2 and 15 answers 1-2), ADR-001
/// (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable <c>errorCode</c>).
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order.</b> Step 0 on the group (task 099): a flagged-external caller, a caller with no systemuser row and a
/// flag-read fault are refused before any read, reads included (Procedures are internal-only). Then each route's own
/// <c>AddFinanceAuthorizationFilter</c> (<see cref="BillingAdminAuthorization"/>, as the caller, fail closed): Create needs Create on
/// <c>sprk_procedure</c>; a read needs the caller's Read on the Procedure; every other route needs the caller's Write on the Procedure AND
/// the billing-admin marker (I-P5), plus the privilege of the policy table it writes. Then the eBilling flag (<c>RequireEbillingEnabled</c>,
/// ADR-018): the flag never replaces authorization. The writes then run as the BFF application user (I-P2 field security).</para>
/// <para><b>Refusal shapes.</b> 401 unauthenticated; 403 step 0 or <c>sdap.access.deny.*</c>; 503 <c>ebilling.disabled</c>; then, from the
/// service: 400 <c>procedure.invalid</c> / <c>procedure.firm_required</c> / <c>procedure.publish_invalid</c> with <c>errors</c>; 404
/// <c>procedure.not_found</c> / <c>procedure.version_not_found</c>; 409 <c>procedure.scope_locked</c>, <c>procedure.version_overlap</c>,
/// <c>procedure.retired</c>, <c>procedure.draft_exists</c>, <c>procedure.version_not_draft</c>, <c>procedure.version_not_editable</c>,
/// <c>procedure.concurrent_edit</c>, <c>procedure.version_conflict</c>, <c>procedure.ruleset_missing</c>, <c>ruleset.no_draft_version</c> or
/// the owner reason code; 422 <c>procedure.too_many_rules</c>; 500 <c>procedure.write_failed</c>.</para>
/// <para><b>Task 116 (model 5.3, 7.2).</b> POST <c>…/versions/{versionId}/draft</c> queues the drafting job (202) and answers 400
/// <c>procedure.not_billing</c>, 409 <c>procedure_version.not_draft</c> / <c>ruleset.already_drafted</c> / <c>procedure.retired</c> /
/// <c>procedure.ruleset_missing</c> / <c>ruleset.retired</c>, 422 <c>ruleset.source_missing</c>, 503 <c>ruleset.draft_not_queued</c>; a redraft
/// also 404 <c>ruleset.not_found</c> (no Read on the rule set), 409 <c>ruleset.drafting_in_progress</c> / <c>ruleset.redraft_scope_mismatch</c> /
/// <c>ruleset.draft_changed</c>, 422 <c>ruleset.redraft_too_large</c>, 503 <c>ruleset.redraft_outcome_unknown</c> / <c>ruleset.redraft_not_queued</c>. PUT
/// <c>…/versions/{versionId}/file</c> stores the version's source file (multipart, replacing the earlier one) and answers 400
/// <c>procedure_version.file_missing</c>, 409 <c>procedure_version.not_draft</c> / <c>procedure.retired</c> / the owner code, 413
/// <c>procedure_version.file_too_large</c>, 500 <c>procedure_version.file_write_failed</c>, 503 <c>stored_file.store_not_configured</c>. On both,
/// a version of another Procedure is the unknown-id 404 <c>procedure.version_not_found</c>.</para>
/// </remarks>
public static class ProcedureEndpoints
{
    internal const string Route = "/api/finance/procedures";

    internal const string CodeDraftNotQueued = "ruleset.draft_not_queued";
    internal const string CodeFileTooLarge = "procedure_version.file_too_large";
    internal const string CodeFileMissing = "procedure_version.file_missing";
    internal const string CodeFileWriteFailed = "procedure_version.file_write_failed";

    public static IEndpointRouteBuilder MapProcedureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099), reads included
            .WithTags("Finance");

        group.MapPost("/", CreateProcedure)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.CreateProcedure())
            .RequireEbillingEnabled()
            .WithName("CreateProcedure")
            .WithSummary("Create a Procedure (Billing area, Active) with its one Draft rule set")
            .Produces<ProcedureView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{procedureId:guid}", GetProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.ReadProcedure)
            .RequireEbillingEnabled()
            .WithName("GetProcedure")
            .WithSummary("A Procedure with its versions")
            .Produces<ProcedureView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/{procedureId:guid}", UpdateProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("UpdateProcedure")
            .WithSummary("Change a Procedure (Applies to and Firm lock once a version has been in force)")
            .Produces<ProcedureView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions", CreateVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.CreateProcedureVersion)
            .RequireEbillingEnabled()
            .RequireRateCardCreateOnARateCardProcedure() // a Rate card Procedure's copy-forward also creates cards
            .WithName("CreateProcedureVersion")
            .WithSummary("Create the next version as a Draft, copying the rules of the version before it")
            .Produces<ProcedureVersionCreated>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/{procedureId:guid}/versions/{versionId:guid}", ChangeVersionDates)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("ChangeProcedureVersionDates")
            .WithSummary("Change a Draft or Active version's dates (versions in force never overlap)")
            .Produces<ProcedureVersionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions/{versionId:guid}/publish", PublishVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.PublishProcedureVersion)
            .RequireEbillingEnabled()
            .WithName("PublishProcedureVersion")
            .WithSummary("Publish a Draft version, superseding the previous one, in one transaction")
            .Produces<ProcedureVersionPublished>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Task 116 (model 5.3): start drafting a Draft version's rules from its source file (ADR-004 job). DraftRules() plus Write on the Procedure.
        group.MapPost("/{procedureId:guid}/versions/{versionId:guid}/draft", DraftVersionRules)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.DraftProcedureVersion)
            .RequireEbillingEnabled()
            .WithName("DraftProcedureVersionRules")
            .WithSummary("Start drafting a Draft version's rules from its source file (a background job)")
            .WithDescription(
                "Returns 202 with a Location (the Procedure's rule set read) once the job is queued. The job proposes rules from the closed list of " +
                "eleven billing rule types; each is validated before it is stored as a Proposed rule under this version, and text that cannot be " +
                "converted is listed on the version. 409 ruleset.already_drafted once a run has finished; 422 ruleset.source_missing unless the " +
                "version has exactly one servable source file.")
            .Produces<ProcedureVersionDraftAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Task 116 (model 3.2, 4.2, 4.3): store a Draft version's source file through the file store, replacing the earlier one. 081's write check.
        group.MapPut("/{procedureId:guid}/versions/{versionId:guid}/file", PutVersionFile)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's rights, not a form token
            .Accepts<IFormFile>("multipart/form-data")
            // Fix round 1: the body cap of the submission routes (the configurable ceiling plus the envelope), above Kestrel's 30 MB default.
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("PutProcedureVersionFile")
            .WithSummary("Store a Draft version's source file (replaces the earlier one)")
            .Produces<ProcedureVersionFileStored>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions/{versionId:guid}/retire", RetireVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("RetireProcedureVersion")
            .WithSummary("Retire one version (its rules stay enabled; nothing is closed or re-checked)")
            .Produces<ProcedureVersionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{procedureId:guid}/retire-preview", PreviewRetire)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("PreviewRetireProcedure")
            .WithSummary("The retire confirmation: the warning and how many open invoices the caller can see are affected (writes nothing)")
            .Produces<ProcedureRetirePreview>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/retire", RetireProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.RetireProcedure)
            .RequireEbillingEnabled()
            .WithName("RetireProcedure")
            .WithSummary("Retire the Procedure: every rule Off and their open findings closed PolicyRetired")
            .Produces<ProcedureRetired>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CreateProcedure(ProcedureCreateRequest body, ProcedureService service, HttpContext http, CancellationToken ct)
    {
        var outcome = await service.CreateAsync(body, CallerOid(http), ct);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{Route}/{outcome.Value!.Id}", outcome.Value)
            : Map(outcome, http);
    }

    private static async Task<IResult> GetProcedure(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.GetAsync(procedureId, ct), http);

    private static async Task<IResult> UpdateProcedure(
        Guid procedureId, ProcedureUpdateRequest body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.UpdateAsync(procedureId, body, ct), http);

    internal static async Task<IResult> CreateVersion(
        Guid procedureId, ProcedureVersionCreateBody? body, ProcedureService service, HttpContext http, CancellationToken ct)
    {
        // The Procedure type the privilege filter checked; the service refuses 409 if the Procedure reads as a Rate card now and the check
        // covered another type. No record of the check is a fault.
        if (!http.Items.TryGetValue(BillingAdminAuthorization.CheckedProcedureTypeItem, out var checkedItem) || checkedItem is not ProcedureType checkedType)
        {
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        var outcome = await service.CreateVersionAsync(procedureId, body?.EffectiveFrom, body?.EffectiveTo, CallerOid(http), checkedType, ct);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{Route}/{procedureId}", outcome.Value)
            : Map(outcome, http);
    }

    private static async Task<IResult> ChangeVersionDates(
        Guid procedureId, Guid versionId, ProcedureVersionDatesBody body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.ChangeDatesAsync(procedureId, versionId, body.EffectiveFrom, body.EffectiveTo, body.ClearEffectiveTo, ct), http);

    private static async Task<IResult> PublishVersion(
        Guid procedureId, Guid versionId, ProcedureVersionPublishBody? body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.PublishVersionAsync(procedureId, versionId, body?.EffectiveFrom, ct), http);

    private static async Task<IResult> RetireVersion(Guid procedureId, Guid versionId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.RetireVersionAsync(procedureId, versionId, ct), http);

    internal static async Task<IResult> DraftVersionRules(
        Guid procedureId, Guid versionId, RuleSetService ruleSets, JobSubmissionService jobs, StoredFileService files,
        AuthorizationService authorization, IIdempotencyService locks, HttpContext http, ILogger<RuleSetService> logger, CancellationToken ct)
    {
        // The source, the version's state and a finished run are decided here, so the admin hears 404 / 409 / 422 now, not from a dead-lettered job.
        // Fix round 1: the route checks the caller's rights on the version before its other answers (an unknown version first, then those rights).
        // Fix round 2: the rights are asked for every id, an unknown one too, before the version is read, so both answers take the same path.
        // Fix round 3: the probe answers no rights for an unknown id too, so its no-Read 404 is returned before the version is read: an
        // unknown id and an unreadable version make the same reads. Only Read without Write (403) waits for the version read below, so a
        // version under another Procedure still answers 404.
        // Fix round 4 (K1): a probe FAULT is returned now too (403 system_failure), before the version is read, so during a fault every id,
        // known or not, gets the same answer from the same reads. Only Read without Write (a real answer) waits for the version read.
        var (denied, answerNow) = await CallerVersionRightsAsync(files, versionId, VersionNotFoundForDraft, http, logger, ct);
        if (answerNow)
        {
            return denied!;
        }

        // Fix round 5 (EB-D-216a): a redraft takes the drafts it supersedes out of review, so the caller must hold what a REJECT of them needs,
        // asked as the caller and before anything about the version's drafts is read (rights, then reads, then the transaction). The
        // privileges are DecideRule()'s, on the route's declaration; here, through the reject route's own calls, Read on the Procedure's rule
        // set (the version's uniform 404, its record too when it is record-scoped) and Write on that set's Procedure. Row-level rights on
        // draft rows are not the gate; the redraft asks the reject rights as the caller (EB-D-216a).
        var checkedRuleSet = await CallerRejectRightsRefusalAsync(procedureId, ruleSets, authorization, http, logger, ct);
        if (checkedRuleSet.Refusal is { } refused)
        {
            return refused;
        }

        var found = await ruleSets.GetDraftSourceAsync(procedureId, versionId, ct);
        if (found.Kind != RuleSetOutcomeKind.Done && found.StatusCode == StatusCodes.Status404NotFound)
        {
            return Map(found, http);
        }

        if (denied is not null)
        {
            return denied;
        }

        if (found.Kind != RuleSetOutcomeKind.Done)
        {
            return Map(found, http);
        }

        var source = found.Value!;
        if (source.AlreadyDrafted)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict, detail: "Rules have already been drafted for this Procedure Version.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = RuleSetService.CodeAlreadyDrafted });
        }

        // Fix round 5 (EB-D-216a, item 3): the rights above were asked on the Procedure's rule set; the version must draft into that same set.
        if (checkedRuleSet.RuleSetId != source.RuleSetId)
        {
            return ScopeMismatch(versionId, source.RuleSetId, "the version's rule set is not the one the rights were checked on", http, logger);
        }

        // Fix round 5 (F2, fix b): the plan, the deactivation and the queueing run under the drafting job's own per-version lock (the same key
        // and store), so a run still storing its rules (its marker not yet written) cannot have part of its output deactivated under it.
        var lockKey = BillingRuleDraftJobHandler.LockKeyFor(versionId);
        var lockOwner = $"redraft:{Guid.NewGuid():N}";
        if (!await locks.TryAcquireProcessingLockAsync(lockKey, lockOwner, RedraftLockDuration, ct))
        {
            logger.LogWarning("Procedure Version {ProcedureVersionId}: a drafting run holds the version's lock; the redraft was refused", versionId);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: "Rules are being drafted for this Procedure Version right now. Nothing was changed; try again when that run has finished.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = CodeDraftingInProgress });
        }

        try
        {
            return await PlanDeactivateAndQueueAsync(procedureId, versionId, source, checkedRuleSet.RuleSetId!.Value, ruleSets, jobs, http, logger, ct);
        }
        finally
        {
            await locks.ReleaseProcessingLockAsync(lockKey, lockOwner, CancellationToken.None);
        }
    }

    /// <summary>How long the redraft holds the drafting job's per-version lock at most: a few reads and one transaction, far below the job's own.</summary>
    internal static readonly TimeSpan RedraftLockDuration = TimeSpan.FromMinutes(5);

    /// <summary>Fix round 5 (F2): a drafting run holds the version's lock (409); nothing was read about its drafts or changed.</summary>
    internal const string CodeDraftingInProgress = "ruleset.drafting_in_progress";

    /// <summary>Fix round 5 (EB-D-216a): a draft the redraft would supersede is outside the rule set or Procedure Version it checked (409).</summary>
    internal const string CodeRedraftScopeMismatch = "ruleset.redraft_scope_mismatch";

    /// <summary>Fix round 5 (uac-r2 addition 3): the earlier drafts were deactivated, but the drafting job could not be queued (503).</summary>
    internal const string CodeRedraftNotQueued = "ruleset.redraft_not_queued";

    /// <summary>
    /// The redraft's writes, under the job's lock (fix rounds 4 and 5): plan the superseded drafts (only those made before the current source
    /// file); refuse the whole call if any of them lies outside the checked rule set or this Procedure Version; deactivate them all or none, as
    /// the application; then queue the run. Once the deactivation has been sent, the queueing runs on <see cref="CancellationToken.None"/>, so a
    /// client that disconnects never leaves inactive drafts with no run queued; a queue failure after it answers 503
    /// <see cref="CodeRedraftNotQueued"/> (a retry plans nothing and queues the run).
    /// </summary>
    private static async Task<IResult> PlanDeactivateAndQueueAsync(
        Guid procedureId, Guid versionId, DraftSource source, Guid checkedRuleSetId, RuleSetService ruleSets, JobSubmissionService jobs,
        HttpContext http, ILogger logger, CancellationToken ct)
    {
        var plan = await ruleSets.PlanRedraftAsync(versionId, source.FileId, ct);
        if (plan.Kind != RuleSetOutcomeKind.Done)
        {
            return Map(plan, http);
        }

        // Every superseded draft is the checked rule set's, under this version (so under the route's Procedure, which GetDraftSourceAsync
        // confirmed). By construction they all are; a draft that is not refuses the whole call before anything is written.
        if (plan.Value!.Drafts.FirstOrDefault(d => d.RuleSetId != checkedRuleSetId || d.ProcedureVersionIds.Any(v => v != versionId)) is { } outside)
        {
            return ScopeMismatch(versionId, outside.RuleSetId,
                $"superseded draft {outside.PolicyId} is outside rule set {checkedRuleSetId} or Procedure Version {versionId} (Procedure {procedureId})", http, logger);
        }

        var deactivatedAny = false;
        if (plan.Value.Drafts.Count > 0)
        {
            var deactivated = await ruleSets.DeactivateSupersededDraftsAsync(versionId, plan.Value, ct);
            if (deactivated.Kind != RuleSetOutcomeKind.Done)
            {
                return Map(deactivated, http);
            }

            deactivatedAny = true;
        }

        var jobId = Guid.NewGuid();
        var statusUrl = $"{RuleSetEndpoints.Route}/{source.RuleSetId}";
        try
        {
            await jobs.SubmitJobAsync(
                BillingRuleDraftJobHandler.BuildJob(jobId, versionId, CallerOid(http), http.TraceIdentifier), deactivatedAny ? CancellationToken.None : ct);
        }
        catch (Exception ex) when (deactivatedAny || ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the drafting job could not be queued ({ExceptionType}); earlier drafts deactivated: {Deactivated}",
                versionId, ex.GetType().Name, deactivatedAny);
            return deactivatedAny
                ? Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    detail: "The earlier drafts of this version were already replaced, but the drafting run could not be queued. Start drafting again to queue it.",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = CodeRedraftNotQueued })
                : Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The drafting job could not be queued. Try again shortly.",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = CodeDraftNotQueued });
        }

        logger.LogInformation("Procedure Version {ProcedureVersionId}: drafting job {JobId} queued", versionId, jobId);
        return Results.Accepted(statusUrl, new ProcedureVersionDraftAccepted(versionId, source.RuleSetId, jobId, statusUrl));
    }

    private static IResult ScopeMismatch(Guid versionId, Guid ruleSetId, string why, HttpContext http, ILogger logger)
    {
        logger.LogError("Procedure Version {ProcedureVersionId}: {ReasonCode}: {Why} (rule set {RuleSetId}); nothing was changed",
            versionId, CodeRedraftScopeMismatch, why, ruleSetId);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            detail: "An earlier draft of this version does not belong to its Procedure's rule set. Nothing was changed; contact support.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeRedraftScopeMismatch });
    }

    /// <summary>
    /// Fix round 5 (EB-D-216a): the reject route's rights on the route Procedure's rule set, as the caller, before any draft state is read:
    /// <see cref="RuleSetEndpoints.CallerAccessToTheRuleSetRefusalAsync"/> (Read on the set, the uniform 404; its record too when record-scoped)
    /// and <see cref="BillingAdminAuthorization.CallerRightsOnTheRuleSetsProcedureRefusalAsync"/> (Write on the set's Procedure). The set is
    /// found app-only from the Procedure; a Procedure with no rule set has nothing to supersede and is answered by the source read (409
    /// <c>procedure.ruleset_missing</c>), so its rule set id is null. A fault finding the set is 403 <c>system_failure</c>.
    /// </summary>
    private static async Task<(Guid? RuleSetId, IResult? Refusal)> CallerRejectRightsRefusalAsync(
        Guid procedureId, RuleSetService ruleSets, AuthorizationService authorization, HttpContext http, ILogger logger, CancellationToken ct)
    {
        Guid? ruleSetId;
        try
        {
            ruleSetId = await ruleSets.ProcedureRuleSetIdAsync(procedureId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Procedure {ProcedureId}: its rule set could not be read for the redraft's rights; denying (fail closed)", procedureId);
            return (null, ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier));
        }

        if (ruleSetId is not { } id)
        {
            return (null, null);
        }

        // Fix round 6 (NFR-01): the id in this URL is the VERSION's, so the set's uniform 404 is answered as the missing-version body (the
        // same bytes as an unknown version id); the set check logs its own line as before.
        var access = await RuleSetEndpoints.CallerAccessToTheRuleSetRefusalAsync(http, id, ruleSets, authorization, logger, ct);
        if (access is IStatusCodeHttpResult { StatusCode: StatusCodes.Status404NotFound })
        {
            return (id, VersionNotFound(VersionNotFoundForDraft, http));
        }

        var refusal = access
            ?? await BillingAdminAuthorization.CallerRightsOnTheRuleSetsProcedureRefusalAsync(http, id, "write", ruleSets, authorization, logger, ct);
        return (id, refusal);
    }

    /// <summary>
    /// In this order (fix round 1): the version under this Procedure (the unknown-id 404), the caller's rights on the version (no Read is that
    /// same 404, no Write 403), the version's state (409), then the size (Content-Length before the body is buffered, the server's cap while it
    /// is read, the file's own length after). A drafted version's drafted state is reset before anything is staged (owner ruling 2026-11-10:
    /// a new file makes the version draftable again); if that fails, nothing is stored. The bytes go through
    /// <see cref="StoredFileService.ReplaceForCallerAsync"/> (step 0, the caller's Write on the version again, owner, stage, row, flip, the
    /// version re-read, then the earlier file Removed).
    /// </summary>
    internal static async Task<IResult> PutVersionFile(
        Guid procedureId, Guid versionId, ProcedureService procedures, StoredFileService files, IOptionsMonitor<EbillingOptions> ebilling,
        HttpContext http, ILogger<StoredFileService> logger, CancellationToken ct)
    {
        // Fix round 2: the caller's rights on the version are asked for every id (an unknown one too) before the version is read.
        var denied = await CallerVersionRefusalAsync(files, versionId, VersionNotFoundForFile, http, logger, ct);
        var target = await procedures.GetDraftVersionForFileAsync(procedureId, versionId, ct);
        if (target.Kind != RuleSetOutcomeKind.Done && target.StatusCode == StatusCodes.Status404NotFound)
        {
            return Map(target, http);
        }

        if (denied is not null)
        {
            return denied;
        }

        if (target.Kind != RuleSetOutcomeKind.Done)
        {
            return Map(target, http);
        }

        var request = http.Request;
        var maxMb = ebilling.CurrentValue.MaxSubmissionFileSizeMb;
        var maxBytes = (long)maxMb * 1024 * 1024;
        if (request.ContentLength > maxBytes + (1024 * 1024))
        {
            return FileTooLarge(maxMb);
        }

        // A chunked body has no Content-Length: the server stops reading it at the configured limit (the 413 below).
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
        {
            cap.MaxRequestBodySize = maxBytes + (1024 * 1024);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return FileTooLarge(maxMb);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return FileMissing("The request body is not valid multipart/form-data.");
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return FileMissing("The source file is missing or empty.");
        }

        if (file.Length > maxBytes)
        {
            return FileTooLarge(maxMb);
        }

        // The stored media type is derived from the name, never the client's claimed Content-Type (RuleSetService.MimeTypeOf).
        var fileName = Path.GetFileName(file.FileName);
        var mediaType = RuleSetService.MimeTypeOf(fileName);
        var parent = new StoredFileParent(StoredFileParentKind.ProcedureVersion, versionId);

        // Fix round 2: what the store would refuse about the request itself (name, type, size, an unconfigured store) is answered before the
        // reset below, so such a request changes nothing. Fix round 3: a store that cannot stage now (the stage's own check) is refused here
        // too; a fault of that check stores nothing and resets nothing.
        string? early;
        try
        {
            early = await files.PrecheckUploadAsync(StoredFilePurpose.ProcedureSource, parent, fileName, mediaType, file.Length, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the file store check failed; nothing changed ({ExceptionType})", versionId, ex.GetType().Name);
            return FileWriteFailed();
        }

        if (early is not null)
        {
            logger.LogWarning("Procedure Version {ProcedureVersionId}: the source file was refused before anything changed ({Code})", versionId, early);
            return FileRefused(StoredFileCreateResult.Refused(early), maxMb, http);
        }

        // Owner ruling 2026-11-10 (reset and allow redraft): a drafted version is made draftable again BEFORE the new file is staged, so a
        // later fault leaves at worst an undrafted version that still has its old file, never a new file on a version marked drafted.
        bool stillDraft;
        try
        {
            stillDraft = await procedures.ResetDraftedStateIfDraftAsync(versionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the drafted state could not be reset; no file stored ({ExceptionType})", versionId, ex.GetType().Name);
            return FileWriteFailed();
        }

        if (!stillDraft)
        {
            return NotDraft();
        }

        StoredFileCreateResult stored;
        try
        {
            await using var content = file.OpenReadStream();
            stored = await files.ReplaceForCallerAsync(
                http.User, TokenHelper.ExtractBearerTokenOrNull(http), StoredFilePurpose.ProcedureSource, parent, fileName, mediaType, content, ct,
                parentStillAccepts: token => procedures.RecheckDraftAfterNewFileAsync(versionId, token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the source file could not be stored ({ExceptionType})", versionId, ex.GetType().Name);
            return FileWriteFailed();
        }

        if (stored.IsCreated)
        {
            logger.LogInformation("Procedure Version {ProcedureVersionId}: source file {FileId} stored", versionId, stored.FileId);
            return Results.Ok(new ProcedureVersionFileStored(versionId, stored.FileId!.Value));
        }

        logger.LogWarning("Procedure Version {ProcedureVersionId}: the source file was refused ({Code})", versionId, stored.RefusalCode);
        return FileRefused(stored, maxMb, http);
    }

    /// <summary>A refused caller write of the version's source file, as the route answers it (also the early refusals of fix round 2).</summary>
    private static IResult FileRefused(StoredFileCreateResult stored, int maxMb, HttpContext http) =>
        stored.RefusalCode switch
        {
            StoredFileRefusal.CallerNotInternal => ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier),
            StoredFileRefusal.NoWriteOnParent => ProblemDetailsHelper.Forbidden(
                FinanceAuthorizationFilter.InsufficientRightsReasonCode, detail: "You need Write on this Procedure Version.", traceId: http.TraceIdentifier),
            StoredFileRefusal.OwnerUnresolved => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, detail: "The file could not be given an owner.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = stored.OwnerRefusalCode ?? StoredFileRefusal.OwnerUnresolved }),
            StoredFileRefusal.NotConfigured => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The file store is not configured in this environment.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = StoredFileRefusal.NotConfigured }),
            StoredFileRefusal.InvalidRequest => FileMissing("The file name or type is not accepted."),
            StoredFileRefusal.ParentNoLongerAccepts => NotDraft(),
            StoredFileRefusal.SizeOutOfRange => FileTooLarge(maxMb),
            _ => FileWriteFailed(),
        };

    internal const string VersionNotFoundForDraft = "The Procedure Version was not found.";
    private const string VersionNotFoundForFile = "The version was not found in this Procedure.";

    /// <summary>
    /// Fix round 1: the caller's own rights on the version, asked as the user (the file store's check, <see
    /// cref="StoredFileService.GetCallerRightsOnParentAsync"/>). No Read answers exactly as an unknown version does (<paramref
    /// name="notFoundDetail"/> is the route's own unknown-id text); Read without Write is 403; a fault is 403 <c>system_failure</c>. Null: go on.
    /// </summary>
    internal static async Task<IResult?> CallerVersionRefusalAsync(
        StoredFileService files, Guid versionId, string notFoundDetail, HttpContext http, ILogger logger, CancellationToken ct) =>
        (await CallerVersionRightsAsync(files, versionId, notFoundDetail, http, logger, ct)).Refusal;

    /// <summary>
    /// <see cref="CallerVersionRefusalAsync"/>, and whether the refusal answers for every id alike (fix round 4, K1): no Read (the unknown-id 404)
    /// and a probe fault (403 <c>system_failure</c>) do; Read without Write (403) depends on the version, so a caller may still read it first.
    /// </summary>
    internal static async Task<(IResult? Refusal, bool AnswerNow)> CallerVersionRightsAsync(
        StoredFileService files, Guid versionId, string notFoundDetail, HttpContext http, ILogger logger, CancellationToken ct)
    {
        AccessRights rights;
        try
        {
            rights = await files.GetCallerRightsOnParentAsync(
                TokenHelper.ExtractBearerTokenOrNull(http), new StoredFileParent(StoredFileParentKind.ProcedureVersion, versionId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Procedure Version route: the caller's rights could not be read ({ExceptionType})", ex.GetType().Name);
            return (ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier), true);
        }

        if (!rights.HasFlag(AccessRights.Read))
        {
            return (VersionNotFound(notFoundDetail, http), true);
        }

        return rights.HasFlag(AccessRights.Write)
            ? (null, false)
            : (ProblemDetailsHelper.Forbidden(
                FinanceAuthorizationFilter.InsufficientRightsReasonCode, detail: "You need Write on this Procedure Version.", traceId: http.TraceIdentifier), false);
    }

    /// <summary>The unknown-version 404 (<c>procedure.version_not_found</c>) with the route's own text; the same body as no Read on it.</summary>
    internal static IResult VersionNotFound(string detail, HttpContext http) =>
        Map(RuleSetOutcome<ProcedureVersionView>.Refused(StatusCodes.Status404NotFound, ProcedureService.CodeVersionNotFound, detail), http);

    private static IResult NotDraft() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: "A file can be stored only on a Draft version.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = RuleSetService.CodeVersionNotDraft });

    private static async Task<IResult> PreviewRetire(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.PreviewRetireAsync(procedureId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    private static async Task<IResult> RetireProcedure(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.RetireAsync(procedureId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    // ═══════════════════════════════════════════════════════════════════════════
    // Responses (ADR-019: ProblemDetails with a stable errorCode)
    // ═══════════════════════════════════════════════════════════════════════════

    private static IResult Map<T>(RuleSetOutcome<T> outcome, HttpContext http) where T : class =>
        outcome.Kind switch
        {
            RuleSetOutcomeKind.Done => Results.Ok(outcome.Value),
            RuleSetOutcomeKind.Invalid => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: outcome.ErrorCode == ProcedureService.CodePublishInvalid ? "Procedure version cannot be published" : "Procedure request is not valid",
                detail: $"{outcome.Errors.Count} problem(s) found; nothing was written.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = outcome.ErrorCode,
                    ["errors"] = outcome.Errors
                        .Select(e => (object)new { policyId = e.PolicyId, rule = e.RuleName, field = e.Field, message = e.Message, reason = e.Reason })
                        .ToArray(),
                }),
            RuleSetOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "procedure", http.TraceIdentifier),
            _ => Results.Problem(
                statusCode: outcome.StatusCode, detail: outcome.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = outcome.ErrorCode }),
        };

    private static IResult FileTooLarge(int maxMb) =>
        Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large", detail: $"The source file may be at most {maxMb} MB.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeFileTooLarge });

    private static IResult FileMissing(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeFileMissing });

    private static IResult FileWriteFailed() =>
        Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            detail: "The source file could not be stored; the earlier file, if any, is unchanged. Try again; if it keeps failing, contact support.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeFileWriteFailed });

    private static Guid? CallerOid(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
}
