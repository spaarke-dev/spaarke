using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>A decision plan resolved against the closed catalog: actions in plan order (the first is the recommendation),
/// the Next-steps set, and the lane's closed dismissal reasons.</summary>
public sealed record ResolvedDecisionPlan(
    IReadOnlyList<DecisionActionDefinition> Actions,
    IReadOnlyList<DecisionActionDefinition> NextSteps,
    IReadOnlyList<DecisionDismissalReason> DismissalReasons)
{
    /// <summary>The catalog code of Mark complete.</summary>
    public const string MarkCompleteCode = "mark-complete";

    /// <summary>
    /// Task 154 (D-111, 059 review): the plan for an inquiry's "Record the outcome" To Do. Mark complete is removed, because completing
    /// that To Do without the inquiry's disposition leaves the inquiry open with nobody prompted again; it is completed by 071's
    /// disposition route. Every other action keeps its place. The ONE reduction: the plan route and the commit route both apply it.
    /// </summary>
    public ResolvedDecisionPlan WithoutMarkComplete() =>
        this with { Actions = Actions.Where(a => a.Code != MarkCompleteCode).ToList() };

    /// <summary>The catalog code of Revise budget.</summary>
    public const string ReviseBudgetCode = "revise-budget";

    /// <summary>The catalog code of Create budget, the substitution-only action (task 161, D-180).</summary>
    public const string CreateBudgetCode = "create-budget";

    /// <summary>
    /// Task 161 (D-180): the plan for a matter with NO active budget. Revise budget has nothing to revise, so Create budget takes its
    /// place: same position, hence the same recommended flag. Every other action is untouched, and a plan without Revise budget is
    /// returned as it is. The ONE substitution: the plan route and the commit route both apply it
    /// (<see cref="SignalCoreRecordAccess.WithCreateBudgetWhenNoneAsync"/>), so the client and the server agree on the offered actions.
    /// </summary>
    public ResolvedDecisionPlan WithCreateBudgetInsteadOfRevise() =>
        Actions.Any(a => a.Code == ReviseBudgetCode) && DecisionActionCatalog.TryGet(CreateBudgetCode, out var create)
            ? this with { Actions = Actions.Select(a => a.Code == ReviseBudgetCode ? create : a).ToList() }
            : this;
}

/// <summary>Exactly one of <see cref="Plan"/> / <see cref="RefusalReason"/> is set.</summary>
public sealed record DecisionPlanResolution(ResolvedDecisionPlan? Plan, string? RefusalReason)
{
    public bool IsRefused => Plan is null;

    /// <summary>The version's <c>sprk_rulebody</c>, read with the plan so <see cref="RuleBodyDescriber"/> describes the same version.</summary>
    public string? RuleBodyJson { get; init; }
}

/// <summary>
/// Resolves a policy version's <c>sprk_decisionplan</c> against <see cref="DecisionActionCatalog"/> (spec FR-49/FR-50).
/// </summary>
/// <remarks>
/// <para><b>Fail closed (task 036).</b> A plan naming a code that is not in the catalog, a code in the wrong position
/// for the Signal's lane, a duplicate, or a plan that is missing or malformed is REFUSED as a whole: logged once with
/// <see cref="OntologyWriterEvents.DecisionPlanRefused"/> and counted on <c>ontology.decisionplan.refused</c>. The wizard
/// never receives a partial plan, because a silently dropped action would let a decision be taken without the step the
/// rule author intended.</para>
/// <para><b>Plan shape</b> (written by task 009): <c>{"actions":["code",...],"nextSteps":["code",...]}</c>.
/// <c>nextSteps</c> may be absent. Neither the plan JSON nor any rejected code is logged — only the policy version id
/// and the bounded reason.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the catalog is a static table; nothing resolves
/// a plan against it. Extension: the catalog stays a pure table and the resolver is its only consumer that does I/O.
/// Cost of doing nothing: the wizard and the commit route would each parse the plan and disagree on what an unknown
/// code means.</para>
/// </remarks>
public sealed class DecisionPlanService
{
    private const string PlanColumn = "sprk_decisionplan";
    private const string RuleBodyColumn = "sprk_rulebody";

    private readonly IGenericEntityService _entities;
    private readonly ILogger<DecisionPlanService> _logger;

    public DecisionPlanService(IGenericEntityService entities, ILogger<DecisionPlanService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the policy version's plan (configuration, not matter data, so the read is the BFF's own) and resolves it for
    /// <paramref name="lane"/>. A refusal is logged and metered here; a failed read throws.
    /// </summary>
    public async Task<DecisionPlanResolution> GetAsync(Guid policyVersionId, DecisionLane lane, CancellationToken ct)
    {
        var version = await _entities
            .RetrieveAsync("sprk_policyversion", policyVersionId, [PlanColumn, RuleBodyColumn], ct)
            .ConfigureAwait(false);

        var resolution = Resolve(version.GetAttributeValue<string>(PlanColumn), lane) with
        {
            RuleBodyJson = version.GetAttributeValue<string>(RuleBodyColumn),
        };
        if (resolution.IsRefused)
        {
            _logger.LogWarning(
                OntologyWriterEvents.DecisionPlanRefused,
                "Decision plan refused (policyVersionId={PolicyVersionId}, reason={Reason}).",
                policyVersionId, resolution.RefusalReason);
            OntologyWriterTelemetry.RecordDecisionPlanRefused(resolution.RefusalReason!);
        }

        return resolution;
    }

    /// <summary>Pure resolution of a plan JSON string. Never throws for any input.</summary>
    public static DecisionPlanResolution Resolve(string? planJson, DecisionLane lane)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return Refused(DecisionPlanRefusalReason.PlanMissing);
        }

        List<string> actionCodes;
        List<string> nextStepCodes;
        try
        {
            using var doc = JsonDocument.Parse(planJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !TryReadCodes(doc.RootElement, "actions", required: true, out actionCodes)
                || !TryReadCodes(doc.RootElement, "nextSteps", required: false, out nextStepCodes))
            {
                return Refused(DecisionPlanRefusalReason.PlanMalformed);
            }
        }
        catch (JsonException)
        {
            return Refused(DecisionPlanRefusalReason.PlanMalformed);
        }

        if (actionCodes.Count == 0)
        {
            return Refused(DecisionPlanRefusalReason.PlanMissing);
        }

        // A code in both lists would be offered as a step and again as a Next step.
        if (HasDuplicate(actionCodes) || HasDuplicate(nextStepCodes) || actionCodes.Intersect(nextStepCodes, StringComparer.Ordinal).Any())
        {
            return Refused(DecisionPlanRefusalReason.DuplicateAction);
        }

        var actions = new List<DecisionActionDefinition>(actionCodes.Count);
        foreach (var code in actionCodes)
        {
            if (!DecisionActionCatalog.TryGet(code, out var action))
            {
                return Refused(DecisionPlanRefusalReason.UnknownAction);
            }

            // Task 161 (D-180): a substitution-only action is never authored; the routes swap it in. A stored plan naming it is refused.
            if (action.PlanLane != lane || action.SubstitutionOnly)
            {
                return Refused(DecisionPlanRefusalReason.ActionNotAllowed);
            }

            actions.Add(action);
        }

        var nextSteps = new List<DecisionActionDefinition>(nextStepCodes.Count);
        foreach (var code in nextStepCodes)
        {
            if (!DecisionActionCatalog.TryGet(code, out var step))
            {
                return Refused(DecisionPlanRefusalReason.UnknownAction);
            }

            if (!step.IsNextStep)
            {
                return Refused(DecisionPlanRefusalReason.ActionNotAllowed);
            }

            nextSteps.Add(step);
        }

        return new DecisionPlanResolution(
            new ResolvedDecisionPlan(actions, nextSteps, DecisionActionCatalog.DismissalReasons(lane)), null);
    }

    private static DecisionPlanResolution Refused(string reason) => new(null, reason);

    private static bool HasDuplicate(List<string> codes) =>
        codes.Distinct(StringComparer.Ordinal).Count() != codes.Count;

    private static bool TryReadCodes(JsonElement root, string name, bool required, out List<string> codes)
    {
        codes = [];
        if (!root.TryGetProperty(name, out var array))
        {
            return !required;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return false;
            }

            codes.Add(item.GetString()!);
        }

        return true;
    }
}
