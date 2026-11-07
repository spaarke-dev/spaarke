using Microsoft.AspNetCore.Mvc;
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
    internal const string PartitionUndeterminedReasonCode = "ontology.signal.partition_undetermined";
    internal const string AccessUnavailableReasonCode = "ontology.signal.access_unavailable";

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
                + "lane's closed dismissal reasons. 404 when the Signal or its core record is not readable by the caller, or a Do item "
                + "is not the caller's own work (D-141); 503 when that could not be checked; "
                + "422 when the plan does not resolve against the closed catalog.")
            .Produces<DecisionPlanResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Task 034 (FR-17, reconciliation B.2 row 034): the suppression count the wizard's dismissal step and admin show.
        group.MapGet("/{signalId:guid}/suppression", GetSuppressionAsync)
            .WithName("GetSignalSuppression")
            .WithSummary("Get the (policy, core record) dismissal count and suppression date for a Signal")
            .WithDescription("Returns the counted and recorded dismissals in the trailing 30 days for the Signal's policy on the Signal's "
                + "core record (or on the item itself when it has none) and the current sprk_suppresseduntil; for a Do item, only the dismissals "
                + "of that item's person's own Do items, reported only to that person (D-145a). 404 when the Signal or its "
                + "core record is not readable by the caller, or a Do item is not the caller's own work (D-141); 403 when the caller cannot be "
                + "resolved; 503 when access could not be checked or an authorized caller's count cannot be read.")
            .Produces<SuppressionResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // spaarkeai-mcp-bff-api-r1 task 016 (B-3, FR-07): the wizard's Person and Lookup search, scoped by the plan, read as the caller.
        // Inherits this group's RequireAuthorization (the default workforce policy); never the ExternalCollaboration policy.
        group.MapGet("/{signalId:guid}/lookup", SearchDecisionLookupAsync)
            .WithName("SearchSignalDecisionLookup")
            .WithSummary("Search the candidates for a person or lookup parameter of a Signal's decision plan")
            .WithDescription("The table and its scope come from the Signal's decision plan for parameterCode, never from the caller: "
                + "active contacts for a person, the active budgets of the Signal's matter for a budget (D-18), active organisations for a "
                + "firm. Read as the caller, at most 25 by name. 404 when the Signal or its core record is not readable by the caller "
                + "(as the decision-plan route); 403 when the caller cannot be resolved; 422 when parameterCode is not a person or lookup "
                + "parameter of the plan or the plan does not resolve; 503 when the search could not be read.")
            .Produces<DecisionLookupResponse>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>The lookup route over <see cref="IDecisionLookupSearch.SearchAsync"/> (task 016, B-3): HTTP shaping only.</summary>
    /// <remarks>
    /// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> The search needs the decision
    /// plan and the caller's OBO Dataverse client, both BFF-only (spec "Placement Justification"). In this existing group so it inherits the
    /// group's authorization and EndpointMappingExtensions is unchanged. The search service is <c>[FromServices]</c> so a host that maps
    /// this group without registering it (the task 011 golden-baseline host) still starts; production registers it unconditionally
    /// (SignalsModule).</para>
    /// </remarks>
    internal static async Task<IResult> SearchDecisionLookupAsync(
        Guid signalId,
        [FromQuery] string? parameterCode,
        [FromQuery] string? query,
        HttpContext httpContext,
        [FromServices] IDecisionLookupSearch search,
        CancellationToken ct) =>
        DecisionResultMapper.ToHttp(await search.SearchAsync(httpContext, signalId, parameterCode, query, ct), httpContext);

    /// <summary>The suppression route over <see cref="IDecisionCapability.GetSuppressionAsync"/> (task 013, B-1): HTTP shaping only.</summary>
    internal static async Task<IResult> GetSuppressionAsync(
        Guid signalId,
        HttpContext httpContext,
        IDecisionCapability decisions,
        CancellationToken ct) =>
        DecisionResultMapper.ToHttp(await decisions.GetSuppressionAsync(httpContext, signalId, ct), httpContext);

    /// <summary>The decision-plan route over <see cref="IDecisionCapability.GetPlanAsync"/> (task 013, B-1): HTTP shaping only. The
    /// capability writes the plan-read stamp (task 043, K4) exactly as this route did.</summary>
    internal static async Task<IResult> GetDecisionPlanAsync(
        Guid signalId,
        HttpContext httpContext,
        IDecisionCapability decisions,
        CancellationToken ct) =>
        DecisionResultMapper.ToHttp(await decisions.GetPlanAsync(httpContext, signalId, ct), httpContext);
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
    /// <param name="inquiryRecipient">Task 160 (D-179): the To pre-fill of <c>send-budget-inquiry</c> only; null leaves To empty.</param>
    /// <param name="parameterFacts">Task 167 (D-193): the resolved option lists and starting values of the parameters that name a source; null adds none.</param>
    internal static DecisionPlanResponse From(
        Guid signalId, DecisionLane lane, Guid policyVersionId, ResolvedDecisionPlan plan, RuleDescription? ruleDescription = null,
        OutsideFirmContact? inquiryRecipient = null, DecisionParameterFacts? parameterFacts = null) =>
        new(signalId,
            lane.ToString(),
            policyVersionId,
            plan.Actions.Select((a, i) => DecisionActionDto.From(a, isRecommended: i == 0,
                prefillTo: a.Code == BudgetInquiryExecutor.ActionCode && inquiryRecipient is { } r
                    ? [new DecisionRecipientDto(r.ContactId, r.Name, r.Email)]
                    : null,
                parameterFacts)).ToList(),
            plan.NextSteps.Select(a => DecisionActionDto.From(a, isRecommended: false, parameterFacts: parameterFacts)).ToList(),
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
    IReadOnlyList<string> Excludes,
    string? Composer = null,
    IReadOnlyList<DecisionRecipientDto>? PrefillTo = null)
{
    /// <param name="prefillTo">Task 160 (D-179): the To starting value (the outside firm's contact), or null.</param>
    internal static DecisionActionDto From(
        DecisionActionDefinition a, bool isRecommended, IReadOnlyList<DecisionRecipientDto>? prefillTo = null,
        DecisionParameterFacts? parameterFacts = null) =>
        new(a.Code, a.Label, a.WorkType, a.RecordClass.ToString(), isRecommended,
            a.Parameters.Select(p => new DecisionParameterDto(p.Code, p.Label, p.Kind.ToString(), p.Required, p.Hint, p.LookupEntity,
                OptionsOf(p, parameterFacts), p.OptionsSource,
                p.DefaultSource is not null && parameterFacts is not null && parameterFacts.DefaultsBySource.TryGetValue(p.DefaultSource, out var start) ? start : p.DefaultValue,
                p.NotBefore, p.MaxLength)).ToList(),
            a.EffectLines,
            a.Excludes,
            a.Composer,
            prefillTo);

    /// <summary>A fixed list as declared; a named source's list as resolved (task 167); null when the parameter has neither or the plan
    /// route resolved none (a plan read without the resolver).</summary>
    private static List<DecisionOptionDto>? OptionsOf(DecisionActionParameter p, DecisionParameterFacts? facts) =>
        p.Options is not null
            ? p.Options.Select(o => new DecisionOptionDto(o.Value, o.Label)).ToList()
            : p.OptionsSource is not null && facts is not null && facts.OptionsBySource.TryGetValue(p.OptionsSource, out var resolved)
                ? resolved.Select(o => new DecisionOptionDto(o.Value, o.Label)).ToList()
                : null;
}

/// <summary>A recipient the wizard starts To with (task 160, D-179): a contact the caller can read, with an email address.</summary>
public sealed record DecisionRecipientDto(Guid ContactId, string? Name, string Email);

public sealed record DecisionParameterDto(
    string Code,
    string Label,
    string Kind,
    bool Required,
    string? Hint,
    string? LookupEntity,
    IReadOnlyList<DecisionOptionDto>? Options,
    string? OptionsSource,
    string? DefaultValue = null,
    string? NotBefore = null,
    int? MaxLength = null);

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
