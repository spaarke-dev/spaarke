using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>The edit of a rule's working version. Every member is optional; a rule body is a JSON object or a JSON string.</summary>
public sealed record RuleEditBody(
    string? RuleType, JsonElement? RuleBody, string? MessageTemplate, string? ShortHeadline, string? Consequence,
    string? ClauseReference, string? ClauseLocation);

/// <summary>Publish a rule set from this date.</summary>
public sealed record RuleSetPublishBody(DateOnly? EffectiveDate);

/// <summary>Assign a rule set to the client default or to one firm.</summary>
public sealed record RuleSetAssignmentBody(string? Scope, Guid? FirmId);

/// <summary>Turn "visible to firms" on or off.</summary>
public sealed record RuleSetVisibilityBody(bool? VisibleToFirms);

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

    public static IEndpointRouteBuilder MapRuleSetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
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
            .WithName("AcceptRuleSetProposal")
            .WithSummary("Accept a rule (validated)")
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
            .WithName("RejectRuleSetProposal")
            .WithSummary("Reject a rule")
            .Produces<ProposalView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{ruleSetId:guid}/publish", PublishRuleSet)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Publish())
            .RequireEbillingEnabled()
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

    private static async Task<IResult> ListRuleSets(RuleSetService service, HttpContext http, CancellationToken ct) =>
        Map(await service.ListAsync(ct), http);

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

    private static async Task<IResult> AcceptProposal(Guid ruleSetId, Guid policyId, RuleSetService service, HttpContext http, CancellationToken ct) =>
        Map(await service.DecideAsync(ruleSetId, policyId, accept: true, ct), http);

    private static async Task<IResult> RejectProposal(Guid ruleSetId, Guid policyId, RuleSetService service, HttpContext http, CancellationToken ct) =>
        Map(await service.DecideAsync(ruleSetId, policyId, accept: false, ct), http);

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
