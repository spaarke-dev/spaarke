using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Api.Signals;

/// <summary>
/// <c>GET /api/v1/signals/{signalId}/decision-plan</c> (task 036, spec FR-49/FR-50): a Signal's decision plan resolved
/// against the closed action catalog, so the decision wizard builds its steps from one call. Read-only; the record
/// class of every action comes from the catalog, never from the caller.
/// </summary>
/// <remarks>
/// <para><b>Authorization (NFR-10, D-29).</b> <c>RequireAuthorization</c> on the group; the record-level decision is
/// <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> (as the caller, through the caller-identity Dataverse client):
/// a Signal or core record the caller cannot read is the uniform 404, a caller who cannot be resolved is the single 403.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> The wizard's data
/// owner is the BFF: the catalog is closed code (spec A-2, no Action Engine in R1) and the Signal is readable only through
/// BFF routes (FR-24). No AI-internal type is used; the route is mapped unconditionally and its services are registered
/// unconditionally (SignalsModule), so there is no asymmetric registration.</para>
/// </remarks>
public static class DecisionPlanEndpoints
{
    internal const string CallerUnresolvedReasonCode = "sdap.access.deny.caller_unresolved";
    internal const string PlanRefusedReasonCode = "ontology.decisionplan.refused";
    internal const string SuppressionUnavailableReasonCode = "ontology.suppression.unavailable";

    public static void MapDecisionPlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/signals")
            .WithTags("Signals")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet("/{signalId:guid}/decision-plan", GetDecisionPlanAsync)
            .WithName("GetSignalDecisionPlan")
            .WithSummary("Get a Signal's decision plan resolved against the action catalog")
            .WithDescription("Returns the plan's actions in plan order (the first is the recommendation), the Next-steps set and the "
                + "lane's closed dismissal reasons. 404 when the Signal or its core record is not readable by the caller; "
                + "422 when the plan does not resolve against the closed catalog.")
            .Produces<DecisionPlanResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Task 034 (FR-17, reconciliation B.2 row 034): the suppression count the wizard's dismissal step and admin show.
        group.MapGet("/{signalId:guid}/suppression", GetSuppressionAsync)
            .WithName("GetSignalSuppression")
            .WithSummary("Get the (policy, core record) dismissal count and suppression date for a Signal")
            .WithDescription("Returns the counted and recorded dismissals in the trailing 30 days for the Signal's policy on the Signal's "
                + "core record (or on the item itself when it has none) and the current sprk_suppresseduntil. 404 when the Signal or its "
                + "core record is not readable by the caller; 403 when the caller cannot be resolved; 503 when an authorized caller's count cannot be read.")
            .Produces<SuppressionResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> GetSuppressionAsync(
        Guid signalId,
        HttpContext httpContext,
        SignalCoreRecordAccess signalAccess,
        SignalSuppressionService suppression,
        IDataverseUserClient caller,
        ILogger<SignalSuppressionService> logger,
        CancellationToken ct)
    {
        var decision = await signalAccess.AuthorizeAsync(signalId, ct);
        if (decision.Outcome == SignalAccessOutcome.CallerUnresolved)
        {
            return CallerUnresolved(httpContext);
        }

        if (decision.Outcome != SignalAccessOutcome.Allowed)
        {
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        // The caller may read this Signal and its core record, so the count is theirs to see. The count itself is read with the BFF's
        // own reader (Decision Records are not readable to Console users), trimmed to the Signals the caller's own read returns, and only
        // the numbers and the date are returned.
        SuppressionStatus? status;
        try
        {
            status = await suppression.GetStatusForSignalAsync(signalId, caller, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // AFTER Allowed: the caller is authorized, so this is a fault of ours, not a denial. A timeout (a cancellation the caller did
            // not ask for) is the same. 503 with the trace id, never a guessed count and never the uniform 404 (that answer is for "not yours").
            logger.LogError(ex, "Suppression count failed for an authorized caller (Signal {SignalId}); answering 503.", signalId);
            return Results.Problem(
                title: "Service Unavailable",
                detail: "The suppression count could not be read. Try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = SuppressionUnavailableReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }

        return status is null
            ? ProblemDetailsHelper.UniformRecordNotFound(httpContext)
            : Results.Ok(SuppressionResponse.From(signalId, status));
    }

    private static IResult CallerUnresolved(HttpContext httpContext) =>
        Results.Problem(
            title: "Forbidden",
            detail: "The caller could not be resolved to a Dataverse user.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = CallerUnresolvedReasonCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    internal static async Task<IResult> GetDecisionPlanAsync(
        Guid signalId,
        HttpContext httpContext,
        SignalCoreRecordAccess signalAccess,
        DecisionPlanService planService,
        RuleBodyDescriber describer,
        CancellationToken ct)
    {
        var decision = await signalAccess.AuthorizeAsync(signalId, ct);
        if (decision.Outcome == SignalAccessOutcome.CallerUnresolved)
        {
            return CallerUnresolved(httpContext);
        }

        if (decision.Outcome != SignalAccessOutcome.Allowed || decision.Lane is not { } lane || decision.PolicyVersionId is not { } versionId)
        {
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        var resolution = await planService.GetAsync(versionId, lane, ct);
        if (resolution.Plan is not { } plan)
        {
            return Results.Problem(
                title: "Unprocessable Entity",
                detail: "This Signal's decision plan does not resolve against the action catalog, so no actions can be offered.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = PlanRefusedReasonCode,
                    ["reason"] = resolution.RefusalReason,
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }

        // "How this was determined" (task 026). A body that cannot be described is shown without the description, never
        // as a guess; it does not stop the decision from being taken.
        var described = await describer.DescribeAsync(resolution.RuleBodyJson, ct);
        return Results.Ok(DecisionPlanResponse.From(signalId, lane, versionId, plan, described.Description));
    }
}

/// <summary>The plan as the wizard consumes it.</summary>
public sealed record DecisionPlanResponse(
    Guid SignalId,
    string Lane,
    Guid PolicyVersionId,
    IReadOnlyList<DecisionActionDto> Actions,
    IReadOnlyList<DecisionActionDto> NextSteps,
    IReadOnlyList<DismissalReasonDto> DismissalReasons,
    RuleDescription? RuleDescription)
{
    internal static DecisionPlanResponse From(
        Guid signalId, DecisionLane lane, Guid policyVersionId, ResolvedDecisionPlan plan, RuleDescription? ruleDescription = null) =>
        new(signalId,
            lane.ToString(),
            policyVersionId,
            plan.Actions.Select((a, i) => DecisionActionDto.From(a, isRecommended: i == 0)).ToList(),
            plan.NextSteps.Select(a => DecisionActionDto.From(a, isRecommended: false)).ToList(),
            plan.DismissalReasons.Select(r => new DismissalReasonDto(r.Code, r.Label, r.CountsTowardSuppression, r.RequiresDetail)).ToList(),
            ruleDescription);
}

public sealed record DecisionActionDto(
    string Code,
    string Label,
    string? WorkType,
    string RecordClass,
    bool IsRecommended,
    IReadOnlyList<DecisionParameterDto> Parameters,
    IReadOnlyList<string> EffectLines,
    IReadOnlyList<string> Excludes)
{
    internal static DecisionActionDto From(DecisionActionDefinition a, bool isRecommended) =>
        new(a.Code, a.Label, a.WorkType, a.RecordClass.ToString(), isRecommended,
            a.Parameters.Select(p => new DecisionParameterDto(p.Code, p.Label, p.Kind.ToString(), p.Required, p.Hint, p.LookupEntity,
                p.Options?.Select(o => new DecisionOptionDto(o.Value, o.Label)).ToList(), p.OptionsSource)).ToList(),
            a.EffectLines,
            a.Excludes);
}

public sealed record DecisionParameterDto(
    string Code,
    string Label,
    string Kind,
    bool Required,
    string? Hint,
    string? LookupEntity,
    IReadOnlyList<DecisionOptionDto>? Options,
    string? OptionsSource);

public sealed record DecisionOptionDto(string Value, string Label);

public sealed record DismissalReasonDto(string Code, string Label, bool CountsTowardSuppression, bool RequiresDetail);

/// <summary>The suppression state of a Signal's (policy, core record) grain (task 034).</summary>
/// <param name="DismissalCount">Dismissals that count toward the threshold, in the trailing window.</param>
/// <param name="DismissalsRecorded">Every dismissal in the window, counted or not.</param>
/// <param name="Threshold">Counted dismissals that suppress the pairing.</param>
/// <param name="WindowDays">The counting window and the length of the suppression.</param>
/// <param name="SuppressedUntil">The current <c>sprk_suppresseduntil</c> (UTC), null when the pairing is not suppressed.</param>
/// <param name="CountsTowardSuppression">False when the policy's dismissals never count (<c>sprk_countstowardsuppression</c> No).</param>
public sealed record SuppressionResponse(
    Guid SignalId, int DismissalCount, int DismissalsRecorded, int Threshold, int WindowDays, DateTime? SuppressedUntil,
    bool CountsTowardSuppression)
{
    public static SuppressionResponse From(Guid signalId, SuppressionStatus s) =>
        new(signalId, s.DismissalsCounted, s.DismissalsRecorded, s.Threshold, s.WindowDays, s.SuppressedUntil, s.PolicyCountsTowardSuppression);
}
