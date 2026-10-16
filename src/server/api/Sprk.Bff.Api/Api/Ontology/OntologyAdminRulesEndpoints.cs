using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Ontology;

namespace Sprk.Bff.Api.Api.Ontology;

/// <summary>
/// The Rules area of the Ontology admin route group (ontology platform R1 task 100; spec FR-57; D-22): the rule list, the read-only
/// rule page, On/Off and Retire. Mapped onto the <c>/api/v1/ontology/admin</c> group by <c>OntologyAdminEndpoints</c> (one line), so the
/// group's <c>RequireAuthorization</c> and rate limit apply to every route here.
/// </summary>
/// <remarks>
/// <para><b>Authorization (NFR-10, D-29).</b> No token: 401, from the group. Every Dataverse read and write is made AS THE CALLER by
/// <see cref="OntologyAdminRulesService"/> through the on-behalf-of client, so Dataverse enforces the Spaarke Ontology Administrator role
/// on each write and the audit names the human: a Console User's PUT or POST is refused by Dataverse and answers 403. A caller who cannot
/// be resolved (no user context, on-behalf-of not configured or refused) gets #1312's single 403. A rule that is missing, not visible to
/// the caller, or not an Ontology rule (Billing or Docketing) is the same 404 with no write. A fault is 503, never a shorter list. No route
/// here reads the capability answer the tab uses (rule A of <c>OntologyAdminCallerBoundaryGuardTests</c>).</para>
/// <para><b>Placement (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> In the BFF, as the other admin routes: the writes
/// must run as the caller through the on-behalf-of client the BFF owns, and Retire must close the rule's Signals through the Signal writer
/// (task 033). It adds one scoped service and one delegate registration to <c>SignalsModule</c> (unconditional, so there is no asymmetric
/// registration) and no package.</para>
/// </remarks>
public static class OntologyAdminRulesEndpoints
{
    internal const string ForbiddenReasonCode = "ontology.admin.forbidden";
    internal const string NotFoundReasonCode = "ontology.rule.not_found";
    internal const string UnavailableReasonCode = "ontology.admin.rules_unavailable";

    public static void MapOntologyAdminRulesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/rules", ListRulesAsync)
            .WithName("ListOntologyAdminRules")
            .WithSummary("List the Ontology rules with lane, On/Off, version in force and the numbers the Needs a look filter reads")
            .WithDescription("Every rule whose domain is empty or Ontology (Billing and Docketing rules are never listed), read as the caller. "
                + "Each rule carries its lane, state (On, Off, Retired), version in force, open items, last fired and action rate (acted / "
                + "(acted + dismissed) over 90 days; no rate below 5 judged). The three numbers are read independently: one that cannot be read is null "
                + "(metricsAvailability says which) and the rest still show; last fired looks back 90 days. 403 when the caller cannot be resolved; 503 when the list cannot be read.")
            .Produces<RuleListResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/rules/{policyId:guid}", GetRuleAsync)
            .WithName("GetOntologyAdminRule")
            .WithSummary("Get one Ontology rule: condition in words and formal form, wording, plan, scope, noise, behaviour, versions, history")
            .WithDescription("Read-only. The condition text comes from the rule describer, the wording, plan and versions from the stored rows, "
                + "and the change history from the immutable version rows plus the rule record's last change. 404 when the rule does not "
                + "exist, is not visible to the caller, or is not an Ontology rule; 403 when the caller cannot be resolved.")
            .Produces<RuleDetailResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/rules/{policyId:guid}/enabled", SetRuleEnabledAsync)
            .WithName("SetOntologyAdminRuleEnabled")
            .WithSummary("Switch an Ontology rule On or Off")
            .WithDescription("Sets sprk_enabled and nothing else, as the caller; open items close at the next pass. An inactive rule (retired or "
                + "otherwise) cannot be switched (409). 403 when Dataverse refuses the caller (not an Ontology Administrator) or the caller cannot be resolved; "
                + "404 for a missing, invisible or non-Ontology rule.")
            .Produces<RuleEnabledResult>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/rules/{policyId:guid}/retire", RetireRuleAsync)
            .WithName("RetireOntologyAdminRule")
            .WithSummary("Retire an Ontology rule with a reason and close its open items now")
            .WithDescription("A reason is required (400 without one, nothing read or written). Sets the rule Retired with the reason as the caller, "
                + "then closes its open Signals as rule retired at once; no Decision Record is written. The reply reports whether every open "
                + "item closed; an incomplete close is finished by the nightly pass. 403 when Dataverse refuses the caller or the caller cannot be "
                + "resolved; 404 for a missing, invisible or non-Ontology rule; 409 when already retired or changed since it was opened.")
            .Produces<RuleRetiredResult>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> ListRulesAsync(HttpContext httpContext, OntologyAdminRulesService rules, CancellationToken ct)
    {
        var result = await rules.ListAsync(ct).ConfigureAwait(false);
        return Respond(httpContext, result);
    }

    internal static async Task<IResult> GetRuleAsync(HttpContext httpContext, Guid policyId, OntologyAdminRulesService rules, CancellationToken ct)
    {
        var result = await rules.GetAsync(policyId, ct).ConfigureAwait(false);
        return Respond(httpContext, result);
    }

    internal static async Task<IResult> SetRuleEnabledAsync(
        HttpContext httpContext, Guid policyId, RuleEnabledRequest request, OntologyAdminRulesService rules, CancellationToken ct)
    {
        var result = await rules.SetEnabledAsync(policyId, request.Enabled, ct).ConfigureAwait(false);
        return Respond(httpContext, result);
    }

    internal static async Task<IResult> RetireRuleAsync(
        HttpContext httpContext, Guid policyId, RuleRetireRequest request, OntologyAdminRulesService rules, CancellationToken ct)
    {
        var result = await rules.RetireAsync(policyId, request.Reason, ct).ConfigureAwait(false);
        return Respond(httpContext, result);
    }

    /// <summary>One mapping for every route: the outcome decides the status; a fault never reads as an empty answer.</summary>
    internal static IResult Respond<T>(HttpContext httpContext, OntologyAdminRulesResult<T> result)
    {
        IDictionary<string, object?> With(string reasonCode) =>
            new Dictionary<string, object?> { ["reasonCode"] = reasonCode, ["correlationId"] = httpContext.TraceIdentifier };

        switch (result.Outcome)
        {
            case OntologyAdminRulesOutcome.Ok:
                return Results.Ok(result.Value);

            case OntologyAdminRulesOutcome.CallerUnresolved:
                return Results.Problem(
                    title: "Forbidden",
                    detail: "The caller could not be resolved to a Dataverse user.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: With(DecisionPlanEndpoints.CallerUnresolvedReasonCode));

            case OntologyAdminRulesOutcome.Forbidden:
                return Results.Problem(
                    title: "Forbidden",
                    detail: "Dataverse refused this request: it needs the Spaarke Ontology Administrator role.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: With(ForbiddenReasonCode));

            case OntologyAdminRulesOutcome.NotFound:
                return Results.Problem(
                    title: "Not Found",
                    detail: "No such rule.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: With(NotFoundReasonCode));

            case OntologyAdminRulesOutcome.Invalid:
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [FieldFor(result.ReasonCode)] = [result.Detail ?? "The request is not valid."] },
                    extensions: With(result.ReasonCode ?? "ontology.rule.invalid"));

            case OntologyAdminRulesOutcome.Conflict:
                return Results.Problem(
                    title: "Conflict",
                    detail: result.Detail ?? "The rule is in a state that refuses this change.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: With(result.ReasonCode ?? "ontology.rule.conflict"));

            default:
                return Results.Problem(
                    title: "Service Unavailable",
                    detail: "The rules could not be read or changed. Try again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: With(UnavailableReasonCode));
        }
    }

    private static string FieldFor(string? reasonCode) => reasonCode switch
    {
        OntologyAdminRulesService.EnabledRequired => "enabled",
        _ => "reason",
    };
}
