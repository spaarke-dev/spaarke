using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>The edit of a rule's working version. Every member is optional; a rule body is a JSON object or a JSON string.</summary>
public sealed record RuleEditBody(
    string? RuleType, JsonElement? RuleBody, string? MessageTemplate, string? ShortHeadline, string? Consequence,
    string? ClauseReference, string? ClauseLocation);

/// <summary>
/// Accept or reject a rule. <see cref="RowVersion"/> is the token the Rule Review page was shown for the rule (<c>rowVersion</c> on the
/// proposal the rule set read returned); the decision is refused 409 when the rule has changed since (task 077, 076 re-check K3).
/// <see cref="Reason"/> is required by a reject (trimmed, 1 to 2,000 characters; D-113) and ignored by an accept.
/// </summary>
public sealed record RuleDecisionBody(string? RowVersion, string? Reason = null);

/// <summary>Publish a rule set from this date.</summary>
public sealed record RuleSetPublishBody(DateOnly? EffectiveDate);

/// <summary>Assign a rule set to the client default or to one firm.</summary>
public sealed record RuleSetAssignmentBody(string? Scope, Guid? FirmId);

/// <summary>Turn "visible to firms" on or off.</summary>
public sealed record RuleSetVisibilityBody(bool? VisibleToFirms);

/// <summary>The rule drafting job was queued. <see cref="StatusUrl"/> is the rule set read: a finished run has its not-converted list.</summary>
public sealed record RuleDraftAccepted(Guid RuleSetId, Guid JobId, string StatusUrl);

/// <summary>
/// The rule set API behind the Rule Review page (ebilling-r1 task 076, FR-28), ADR-001 (Minimal API), ADR-008 (endpoint filters) and
/// ADR-019 (ProblemDetails with a stable <c>errorCode</c>).
/// </summary>
/// <remarks>
/// <para><b>Admin-only, by Dataverse table privilege (D-76).</b> Each route carries its OWN <c>AddFinanceAuthorizationFilter</c> declared by
/// <see cref="BillingAdminAuthorization"/> (the caller's own privileges, fail closed), then the eBilling flag filter
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization. Reads of the proposals are admin-only too.</para>
/// <para><b>Refusal shapes.</b> 401 unauthenticated; 403 <c>reasonCode</c> <c>sdap.access.deny.insufficient_privilege</c> for a caller without
/// the billing-admin privileges; 503 <c>ebilling.disabled</c> with the flag off; then, from the service: 400 <c>ruleset.invalid</c> /
/// <c>ruleset.publish_invalid</c> with <c>errors</c> (policyId, rule, field, message, reason); 404 <c>ruleset.not_found</c> /
/// <c>ruleset.proposal_not_found</c> (an Ontology policy is a 404 here); 409 <c>ruleset.retired</c> / <c>ruleset.proposal_in_force</c> or the
/// owner reason code; 413 <c>ruleset.file_too_large</c>; 422 <c>ruleset.result_truncated</c>; 500 <c>ruleset.write_failed</c>; 503
/// <c>ruleset.container_not_configured</c>.</para>
/// </remarks>
public static class RuleSetEndpoints
{
    internal const string Route = "/api/finance/rulesets";
    internal const string CodeFileTooLarge = "ruleset.file_too_large";
    internal const string CodeDraftNotQueued = "ruleset.draft_not_queued";

    /// <summary>
    /// Task 089e (owner 2026-11-04): a record-scoped rule set asked for on a by-id route here, answered only to a caller who can Read the set
    /// AND its record (<see cref="RequireAccessToTheRuleSet"/>). Logged under this code.
    /// </summary>
    internal const string CodeRecordScoped = "ruleset.record_scoped";

    public static IEndpointRouteBuilder MapRuleSetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/rulesets?name=&sourceKind=&scope=&firmId= (multipart file) — create a Draft rule set from a source document
        group.MapPost("/", CreateRuleSet)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.CreateRuleSet())
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's table privileges, not a form token
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("CreateRuleSet")
            .WithSummary("Create a Draft rule set from an uploaded source document")
            .Produces<RuleSetCreated>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/rulesets/{id}/draft — start the rule drafting job (ADR-004) for a rule set's source document (task 075, FR-27)
        group.MapPost("/{ruleSetId:guid}/draft", DraftRules)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.DraftRules())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .RequireWriteOnTheRuleSetsProcedure() // uac-r2 round 2 (I-P5): and Write on the set's Procedure, when it has one
            .WithName("DraftRuleSetRules")
            .WithSummary("Start drafting the rule set's rules from its source document (a background job)")
            .WithDescription(
                "Returns 202 with a Location (the rule set read) once the job is queued. The job proposes rules from the closed list of eleven " +
                "billing rule types; each is validated before it is stored as a Proposed rule, and text that cannot be converted is listed on the " +
                "rule set. Poll the rule set read: a finished run has its not-converted list (an empty list is []). 409 ruleset.already_drafted " +
                "when a run has finished.")
            .Produces<RuleDraftAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", ListRuleSets)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin())
            .RequireEbillingEnabled()
            .WithName("ListRuleSets")
            .WithSummary("List the billing rule sets")
            .Produces<IReadOnlyList<RuleSetSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{ruleSetId:guid}", GetRuleSet)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .RequireReadOnTheRuleSetsProcedure() // uac-r2 round 2 (I-P5): and Read on the set's Procedure, when it has one
            .WithName("GetRuleSet")
            .WithSummary("A rule set with its rules beside their clause quotes")
            .Produces<RuleSetDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // PATCH — a draft is edited in place; a version that is or was in force is left untouched and a new Draft version is created
        group.MapPatch("/{ruleSetId:guid}/proposals/{policyId:guid}", EditProposal)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.EditRule())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .RequireWriteOnTheRuleSetsProcedure() // uac-r2 D (I-P5): and Write on the set's Procedure, when it has one
            .WithName("EditRuleSetProposal")
            .WithSummary("Edit a rule (a published version is never changed: the edit creates a new Draft version)")
            .Produces<ProposalEdited>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{ruleSetId:guid}/proposals/{policyId:guid}/accept", AcceptProposal)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.DecideRule())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .RequireWriteOnTheRuleSetsProcedure() // uac-r2 D (I-P5): and Write on the set's Procedure, when it has one
            .WithName("AcceptRuleSetProposal")
            .WithSummary("Accept a rule (validated; the body names the rowVersion the admin was shown, 409 when it changed since)")
            .Produces<ProposalView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{ruleSetId:guid}/proposals/{policyId:guid}/reject", RejectProposal)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.DecideRule())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .RequireWriteOnTheRuleSetsProcedure() // uac-r2 D (I-P5): and Write on the set's Procedure, when it has one
            .WithName("RejectRuleSetProposal")
            .WithSummary("Reject a rule (the body names the rowVersion the admin was shown and a required reason, 1 to 2,000 characters)")
            .Produces<ProposalView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{ruleSetId:guid}/publish", PublishRuleSet)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Publish())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .WithName("PublishRuleSet")
            .WithSummary("Publish the accepted rules from an effective date, all or nothing")
            .WithDescription(
                "Validates every accepted rule that is not yet in force with PolicyVersionValidator; one invalid rule refuses the whole publish " +
                "with per-rule errors and writes nothing. The response states whether clause quotes will be visible to firms.")
            .Produces<RuleSetPublished>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{ruleSetId:guid}/assignment", AssignRuleSet)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .WithName("AssignRuleSet")
            .WithSummary("Assign the rule set to the client default or a firm")
            .Produces<RuleSetSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{ruleSetId:guid}/visibility", SetVisibility)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin())
            .RequireEbillingEnabled()
            .RequireAccessToTheRuleSet() // 089e: Read on the set, and on its record when it is record-scoped (uniform 404)
            .WithName("SetRuleSetVisibility")
            .WithSummary("Turn \"visible to firms\" on or off")
            .Produces<RuleSetSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CreateRuleSet(
        HttpContext http, RuleSetService service, IOptionsMonitor<EbillingOptions> ebilling, CancellationToken ct)
    {
        var request = http.Request;
        var query = request.Query;
        var problems = new List<RuleSetError>();

        var name = query["name"].ToString().Trim();
        if (name.Length == 0 || name.Length > 200)
        {
            problems.Add(new RuleSetError(null, null, "name", "name is required and may be at most 200 characters.", null));
        }

        if (!TryParseEnum<RuleSetSourceKind>(query["sourceKind"].ToString(), out var sourceKind))
        {
            problems.Add(new RuleSetError(null, null, "sourceKind", "sourceKind must be Guideline, RateCard or InternalProcedure.", null));
        }

        var scope = RuleSetScope.ClientDefault;
        if (query.ContainsKey("scope") && !TryParseScope(query["scope"].ToString(), out scope))
        {
            problems.Add(new RuleSetError(null, null, "scope", "scope must be ClientDefault or Firm.", null));
        }

        Guid? firmId = null;
        var firmRaw = query["firmId"].ToString();
        if (firmRaw.Length > 0)
        {
            if (Guid.TryParse(firmRaw, out var parsed) && parsed != Guid.Empty)
            {
                firmId = parsed;
            }
            else
            {
                problems.Add(new RuleSetError(null, null, "firmId", "firmId must be a GUID.", null));
            }
        }

        if (problems.Count > 0)
        {
            return InvalidProblem(RuleSetService.CodeInvalid, problems);
        }

        var maxBytes = (long)ebilling.CurrentValue.MaxSubmissionFileSizeMb * 1024 * 1024;
        if (request.ContentLength > maxBytes + (1024 * 1024))
        {
            return FileTooLarge(ebilling.CurrentValue.MaxSubmissionFileSizeMb);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[] { new RuleSetError(null, null, "file", "The request body is not valid multipart/form-data.", null) });
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[] { new RuleSetError(null, null, "file", "The source document is missing or empty.", null) });
        }

        if (file.Length > maxBytes)
        {
            return FileTooLarge(ebilling.CurrentValue.MaxSubmissionFileSizeMb);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var outcome = await service.CreateAsync(
            new RuleSetCreateRequest(name, sourceKind, scope, firmId, file.FileName, CallerOid(http)), bytes, ct);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{Route}/{outcome.Value!.RuleSetId}", outcome.Value)
            : Map(outcome, http);
    }

    private static async Task<IResult> DraftRules(
        Guid ruleSetId, RuleSetService service, JobSubmissionService jobs, HttpContext http, ILogger<RuleSetService> logger, CancellationToken ct)
    {
        // The source, the rule set's state and a finished run are decided here, so the admin hears 404 / 409 / 422 now, not from a dead-lettered job.
        var found = await service.GetDraftSourceAsync(ruleSetId, ct);
        if (found.Kind != RuleSetOutcomeKind.Done)
        {
            return Map(found, http);
        }

        if (found.Value!.AlreadyDrafted)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict, detail: "Rules have already been drafted for this rule set.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = RuleSetService.CodeAlreadyDrafted });
        }

        var jobId = Guid.NewGuid();
        var statusUrl = $"{Route}/{ruleSetId}";
        try
        {
            await jobs.SubmitJobAsync(BillingRuleDraftJobHandler.BuildJob(jobId, ruleSetId, CallerOid(http), http.TraceIdentifier), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rule set {RuleSetId}: the drafting job could not be queued ({ExceptionType})", ruleSetId, ex.GetType().Name);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The drafting job could not be queued. Try again shortly.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = CodeDraftNotQueued });
        }

        logger.LogInformation("Rule set {RuleSetId}: drafting job {JobId} queued", ruleSetId, jobId);
        return Results.Accepted(statusUrl, new RuleDraftAccepted(ruleSetId, jobId, statusUrl));
    }

    /// <summary>The most distinct Procedures the list asks the caller's rights on (the same ceiling as the invoice sibling scan, <see cref="InvoiceContextService.MaxSiblingAccessChecks"/>).</summary>
    internal const int MaxProcedureRightsChecks = InvoiceContextService.MaxSiblingAccessChecks;

    /// <summary>
    /// The list leaves out each Procedure rule set whose Procedure the caller cannot Read (uac-r2 re-check round 2, I-P5): a Procedure's rules are
    /// no more visible here than on the Procedure's own read. Legacy sets (no Procedure) are unchanged. One rights question per distinct Procedure,
    /// asked as the caller. A known deny leaves the set out; but an answer that is missing for ANY Procedure (a fault, a throttle), or more distinct
    /// Procedures than <see cref="MaxProcedureRightsChecks"/>, makes the list itself 403 <c>system_failure</c> (round 3, F2 / K2) - the same shape the
    /// single-set read gives an unknown answer - rather than a list that is silently incomplete.
    /// </summary>
    private static async Task<IResult> ListRuleSets(
        RuleSetService service, AuthorizationService authorization, ILogger<RuleSetService> logger, HttpContext http, CancellationToken ct)
    {
        var outcome = await service.ListAsync(ct);
        if (outcome.Kind != RuleSetOutcomeKind.Done || outcome.Value is not { } sets)
        {
            return Map(outcome, http);
        }

        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
        if (!sets.Any(s => s.ProcedureId is not null))
        {
            return await ReadableSetsOnlyAsync(sets, authorization, caller, logger, http, ct);
        }

        var procedures = sets.Where(s => s.ProcedureId is not null).Select(s => s.ProcedureId!.Value).Distinct().ToList();
        if (procedures.Count > MaxProcedureRightsChecks)
        {
            logger.LogWarning(
                "Rule set list: {Procedures} distinct Procedures exceed the {Cap} rights checks the list may ask; the list is refused (fail closed)",
                procedures.Count, MaxProcedureRightsChecks);
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        using var gate = new SemaphoreSlim(CallerReadCheck.Parallelism);
        var answers = await Task.WhenAll(procedures.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (Id: id, Outcome: await CallerReadCheck.CheckAsync(
                    authorization, caller, BillingAdminAuthorization.ProcedureEntitySet, "read", id, logger, "Rule set list: caller read on a Procedure", ct));
            }
            finally
            {
                gate.Release();
            }
        }));

        var unknown = answers.Count(a => a.Outcome == CallerReadOutcome.Failed);
        if (unknown > 0)
        {
            logger.LogWarning(
                "Rule set list: the caller's rights could not be determined for {Unknown} of {Procedures} Procedures; the list is refused (fail closed)",
                unknown, procedures.Count);
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        var readable = answers.ToDictionary(a => a.Id, a => a.Outcome == CallerReadOutcome.Readable); // a known deny leaves the set out
        IReadOnlyList<RuleSetSummary> kept = sets.Where(s => s.ProcedureId is not { } id || readable[id]).ToList();
        return await ReadableSetsOnlyAsync(kept, authorization, caller, logger, http, ct);
    }

    /// <summary>The most rule sets the list asks the caller's Read on (the same ceiling as the Procedure checks above).</summary>
    internal const int MaxRuleSetReadChecks = InvoiceContextService.MaxSiblingAccessChecks;

    /// <summary>
    /// 089e fix round 2 (F1 ii): the list keeps only the sets the caller can Read, asked as the caller on each <c>sprk_ruleset</c> row
    /// (<see cref="CallerReadCheck.ScanAsync{T}"/>) - the same question (A) of <see cref="RequireAccessToTheRuleSet"/> asks on every by-id route,
    /// so a set the list shows is one its own routes answer, and a set its routes hide is not listed. A known deny leaves the set out; an answer
    /// missing for ANY set, or more sets than <see cref="MaxRuleSetReadChecks"/>, makes the list 403 <c>system_failure</c>, as the Procedure checks do.
    /// </summary>
    private static async Task<IResult> ReadableSetsOnlyAsync(
        IReadOnlyList<RuleSetSummary> sets, AuthorizationService authorization, InvoiceContextService.InvoiceCaller caller,
        ILogger logger, HttpContext http, CancellationToken ct)
    {
        if (sets.Count > MaxRuleSetReadChecks)
        {
            logger.LogWarning(
                "Rule set list: {Sets} sets exceed the {Cap} rights checks the list may ask; the list is refused (fail closed)",
                sets.Count, MaxRuleSetReadChecks);
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        var scan = await CallerReadCheck.ScanAsync(
            authorization, caller, "sprk_rulesets", "read", sets, s => s.Id, int.MaxValue, MaxRuleSetReadChecks, logger,
            "Rule set list: caller read on a set", ct);
        if (scan.Failed || !scan.AllExamined)
        {
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        return Map(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(scan.Readable), http);
    }

    private static async Task<IResult> GetRuleSet(Guid ruleSetId, RuleSetService service, HttpContext http, CancellationToken ct) =>
        Map(await service.GetAsync(ruleSetId, ct), http);

    private static async Task<IResult> EditProposal(
        Guid ruleSetId, Guid policyId, RuleEditBody body, RuleSetService service, HttpContext http, CancellationToken ct)
    {
        string? ruleBody = null;
        if (body.RuleBody is { } element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    ruleBody = element.GetRawText();
                    break;
                case JsonValueKind.String:
                    ruleBody = element.GetString();
                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    break;
                default:
                    return InvalidProblem(RuleSetService.CodeInvalid, new[]
                    {
                        new RuleSetError(policyId, null, "ruleBody", "ruleBody must be a JSON object (or a string holding one).", null),
                    });
            }
        }

        var edit = new ProposalEdit(
            body.RuleType, ruleBody, body.MessageTemplate, body.ShortHeadline, body.Consequence,
            body.ClauseReference, body.ClauseLocation, CallerOid(http));
        return Map(await service.EditAsync(ruleSetId, policyId, edit, ct), http);
    }

    private static Task<IResult> AcceptProposal(
        Guid ruleSetId, Guid policyId, RuleDecisionBody? body, RuleSetService service, HttpContext http, CancellationToken ct) =>
        Decide(ruleSetId, policyId, accept: true, body, service, http, ct);

    private static Task<IResult> RejectProposal(
        Guid ruleSetId, Guid policyId, RuleDecisionBody? body, RuleSetService service, HttpContext http, CancellationToken ct) =>
        Decide(ruleSetId, policyId, accept: false, body, service, http, ct);

    /// <summary>
    /// The version the admin was shown is required: a decision without it could be taken on content that was never displayed. A missing,
    /// blank or non-numeric token is a 400 and never reaches the service; a stale one is the service's 409.
    /// </summary>
    private static async Task<IResult> Decide(
        Guid ruleSetId, Guid policyId, bool accept, RuleDecisionBody? body, RuleSetService service, HttpContext http, CancellationToken ct)
    {
        if (!long.TryParse(body?.RowVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expected))
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(policyId, null, "rowVersion",
                    "rowVersion is required: send the rowVersion of the rule as the rule set read returned it.", null),
            });
        }

        // A reject needs a reason (D-113): a missing, blank or over-long one is a 400 that never reaches the service. An accept ignores it.
        string? reason = null;
        if (!accept)
        {
            if (!RuleSetService.TryNormalizeRejectionReason(body?.Reason, out var normalized))
            {
                return InvalidProblem(RuleSetService.CodeInvalid, new[] { RuleSetService.RejectionReasonError(policyId) });
            }

            reason = normalized;
        }

        return Map(await service.DecideAsync(ruleSetId, policyId, accept, expected, ct, reason), http);
    }

    private static async Task<IResult> PublishRuleSet(Guid ruleSetId, RuleSetPublishBody body, RuleSetService service, HttpContext http, CancellationToken ct)
    {
        if (body.EffectiveDate is not { } effective)
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(null, null, "effectiveDate", "effectiveDate is required (yyyy-MM-dd).", null),
            });
        }

        return Map(await service.PublishAsync(ruleSetId, effective, ct), http);
    }

    private static async Task<IResult> AssignRuleSet(Guid ruleSetId, RuleSetAssignmentBody body, RuleSetService service, HttpContext http, CancellationToken ct)
    {
        if (!TryParseScope(body.Scope, out var scope))
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(null, null, "scope", "scope must be ClientDefault or Firm.", null),
            });
        }

        return Map(await service.AssignAsync(ruleSetId, scope, body.FirmId, ct), http);
    }

    private static async Task<IResult> SetVisibility(Guid ruleSetId, RuleSetVisibilityBody body, RuleSetService service, HttpContext http, CancellationToken ct)
    {
        if (body.VisibleToFirms is not { } visible)
        {
            return InvalidProblem(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(null, null, "visibleToFirms", "visibleToFirms is required (true or false).", null),
            });
        }

        return Map(await service.SetVisibilityAsync(ruleSetId, visible, ct), http);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089e: the rule-set by-id routes authorise the set and its record
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The override routes of each core table, as the 409 names them (the record routes, <c>ProcedureAssignmentEndpoints</c>).</summary>
    private static readonly IReadOnlyDictionary<string, (string EntitySet, string Segment, string Kind)> RecordHomes =
        new Dictionary<string, (string EntitySet, string Segment, string Kind)>(StringComparer.Ordinal)
        {
            [ProcedureAssignmentEndpoints.MatterTable] = (FinanceAuthorizationFilter.MatterEntitySet, "matters", "matter"),
            [ProcedureAssignmentEndpoints.ProjectTable] = (FinanceAuthorizationFilter.ProjectEntitySet, "projects", "project"),
            [ProcedureAssignmentEndpoints.WorkAssignmentTable] = (FinanceAuthorizationFilter.WorkAssignmentEntitySet, "workassignments", "work assignment"),
        };

    /// <summary>
    /// Task 089e (owner 2026-11-04, "Approve (A)+(B) on all 9 routes"; the uac-r2 design): every route here that takes a rule set id
    /// authorises the set, and a record-scoped set's record, before anything that differs between "exists" and "does not exist" is read.
    /// <list type="number">
    /// <item>(A) The caller's own Read on the <c>sprk_ruleset</c> row (<see cref="CallerReadCheck"/>, as the caller), for every set.</item>
    /// <item>Only then the set's home, read app-only (<see cref="RuleSetService.RecordHomeOfRuleSetAsync"/>). Not record-scoped, or no set:
    /// the route continues unchanged (the Procedure filter where the route has one, then the service and its own 404).</item>
    /// <item>(B) A record-scoped set: the caller's access to its core record through the evaluator the record's own routes use
    /// (<see cref="FinanceAuthorizationFilter.CallerHoldsRightsAsync"/>, as the caller), asking Read. A Billing Administrator holds Read at
    /// Deep on <c>sprk_ruleset</c>, so (A) alone would pass a set whose record (a Restricted or Secure one) the caller cannot see.</item>
    /// <item>Both passed: 409 <see cref="CodeRecordScoped"/>, naming the record kind and its override routes, logged under the code.</item>
    /// </list>
    /// Any refusal, no answer, or a set with no single core record is the route's uniform 404 (<see cref="RuleSetService.RuleSetNotFound{T}"/>),
    /// the same bytes a missing set gets, so neither the set's existence nor the record's changed rule is shown to a caller who cannot see
    /// them. A fault reading the set's home (after (A) passed) is 403 <c>system_failure</c>, as in the Procedure filter.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <c>RequireRead/WriteOnTheRuleSetsProcedure</c> (the set's Procedure only, on five routes) and
    /// <c>RequireReadOnTheOverridesSubject</c> (the uniform-404 shape). <i>Extension</i> — the Procedure filter reads through
    /// <c>LoadRuleSetAsync</c>, which hides a record-scoped set by design, and runs after this one; a separate filter keeps its 403 answers
    /// unchanged. No new evaluator: (A) is <see cref="CallerReadCheck"/>, (B) the record routes' own check. <i>Cost of doing nothing</i> —
    /// the owner's descriptive, tracked error for a record-scoped set cannot be given without telling every billing admin, including one
    /// who cannot see the record, that the record has a changed rule.</remarks>
    internal static TBuilder RequireAccessToTheRuleSet<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices;
            var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
            if (!http.Request.RouteValues.TryGetValue("ruleSetId", out var raw) || !Guid.TryParse(raw?.ToString(), out var ruleSetId))
            {
                return Map(RuleSetService.RuleSetNotFound<object>(), http); // the route constraint guarantees a guid
            }

            var userId = CallerResolution.ResolveObjectId(http.User) ?? string.Empty;
            var token = TokenHelper.ExtractBearerTokenOrNull(http);
            var authorization = services.GetRequiredService<AuthorizationService>();

            // (A) The set itself, as the caller. Refused and no answer are both the uniform 404: a set id that does not exist may answer either.
            var setRead = await CallerReadCheck.CheckAsync(
                authorization, new InvoiceContextService.InvoiceCaller(userId, token), "sprk_rulesets", "read", ruleSetId, logger,
                $"Rule set {ruleSetId}: caller read on the set", http.RequestAborted);
            if (setRead != CallerReadOutcome.Readable)
            {
                return Map(RuleSetService.RuleSetNotFound<object>(), http);
            }

            RecordRuleSetHome? home;
            try
            {
                home = await services.GetRequiredService<RuleSetService>().RecordHomeOfRuleSetAsync(ruleSetId, http.RequestAborted);
            }
            catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
            {
                logger.LogError(ex, "Rule set {RuleSetId}: its record could not be read for the record check; denying (fail closed)", ruleSetId);
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (home is null)
            {
                return await next(context); // not record-scoped, or no set: unchanged
            }

            // (B) The record, through the record routes' own evaluator, as the caller. Any refusal or fault is the uniform 404.
            if (home.CoreTable is not { } coreTable || !RecordHomes.TryGetValue(coreTable, out var record) || home.CoreId == Guid.Empty)
            {
                logger.LogWarning("Rule set {RuleSetId}: record-scoped with no single core record; denying (fail closed)", ruleSetId);
                return Map(RuleSetService.RuleSetNotFound<object>(), http);
            }

            bool recordReadable;
            try
            {
                recordReadable = userId.Length > 0 && await FinanceAuthorizationFilter.CallerHoldsRightsAsync(
                    authorization, userId, record.EntitySet, home.CoreId, "read", token, http.RequestAborted);
            }
            catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Rule set {RuleSetId}: the record check on {EntitySet} faulted; denying (fail closed)", ruleSetId, record.EntitySet);
                recordReadable = false;
            }

            if (!recordReadable)
            {
                return Map(RuleSetService.RuleSetNotFound<object>(), http);
            }

            var routes = $"{ProcedureAssignmentEndpoints.Route}/{record.Segment}/{home.CoreId}";
            logger.LogWarning(
                "Rule set {RuleSetId}: {ReasonCode}: a record-scoped set ({RecordKind} {RecordId}) was asked for on {Method} {Path}",
                ruleSetId, CodeRecordScoped, record.Kind, home.CoreId, http.Request.Method, http.Request.Path.Value);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This rule set belongs to one record",
                detail: $"This rule set holds the changed rules of one {record.Kind}. It is not read or changed here: use that {record.Kind}'s "
                    + $"override routes, {routes}/rule-overrides/change or {routes}/rule-overrides/exclude to change a rule, and "
                    + $"{routes}/overrides/{{overrideId}} to remove one.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = CodeRecordScoped,
                    ["recordKind"] = record.Kind,
                    ["recordId"] = home.CoreId,
                    ["overridesRoute"] = routes,
                });
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // Responses (ADR-019: ProblemDetails with a stable errorCode)
    // ═══════════════════════════════════════════════════════════════════════════

    private static IResult Map<T>(RuleSetOutcome<T> outcome, HttpContext http) where T : class =>
        outcome.Kind switch
        {
            RuleSetOutcomeKind.Done => Results.Ok(outcome.Value),
            RuleSetOutcomeKind.Invalid => InvalidProblem(outcome.ErrorCode!, outcome.Errors),
            RuleSetOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "rule set", http.TraceIdentifier),
            _ => Results.Problem(
                statusCode: outcome.StatusCode, detail: outcome.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = outcome.ErrorCode }),
        };

    private static IResult InvalidProblem(string errorCode, IReadOnlyList<RuleSetError> errors) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: errorCode == RuleSetService.CodePublishInvalid ? "Rule set cannot be published" : "Rule set request is not valid",
            detail: $"{errors.Count} problem(s) found; nothing was written.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["errors"] = errors.Select(e => (object)new { policyId = e.PolicyId, rule = e.RuleName, field = e.Field, message = e.Message, reason = e.Reason }).ToArray(),
            });

    private static IResult FileTooLarge(int maxMb) =>
        Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large",
            detail: $"The source document may be at most {maxMb} MB.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeFileTooLarge });

    private static Guid? CallerOid(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;

    private static bool TryParseScope(string? text, out RuleSetScope scope) => TryParseEnum(text, out scope);

    /// <summary>Exact member name, case-insensitive; never a number or a comma list (Enum.TryParse would accept both).</summary>
    private static bool TryParseEnum<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }
}
