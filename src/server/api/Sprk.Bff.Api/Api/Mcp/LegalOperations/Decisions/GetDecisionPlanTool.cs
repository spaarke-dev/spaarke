using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

/// <summary>
/// <c>spaarke_get_decision_plan</c> (spaarkeai-mcp-bff-api-r1 task 023, FR-06; design §7.1): the app-only, read-only call the decision
/// wizard makes when it opens an item. It returns the Signal's decision plan, its dismissal-suppression state and its full Work Item
/// row, each resolved as the caller, and the capability writes the plan-read stamp a later commit requires.
/// </summary>
/// <remarks>
/// <para><b>One caller (FR-10, option C).</b> The tool reads the request's <see cref="HttpContext"/> from <see cref="IHttpContextAccessor"/>
/// and passes exactly that instance to <see cref="IDecisionCapability"/> (and its <c>User</c> to the list read), never a context it built
/// or cached. A null context or an unauthenticated caller is <c>UserContextRequired</c> before any capability call.</para>
/// <para><b>The plan is the REST plan.</b> <see cref="IDecisionCapability.GetPlanAsync"/> is the call
/// <c>GET /api/v1/signals/{id}/decision-plan</c> makes, so a refusal carries the REST route's <c>reasonCode</c> and writes no stamp
/// (ADR-019: <see cref="DecisionResultMapper.ToToolError(DecisionPlanReadResult)"/> through <see cref="McpToolResults.FromError"/>).
/// Suppression and the row are read after the plan, and a failure of either leaves the plan and its stamp standing: the wizard already
/// opens without the dismissal count (<c>suppressionFailed</c>).</para>
/// <para><b>The full row.</b> No route returns one Work Item's full row, so it is found in the caller's own worklist read
/// (<see cref="IDecisionCapability.ListDecideAsync"/>, the read the Console's lane makes), not by a new read path. See
/// <c>notes/task-023-notes.md</c> for the cost and the follow-up.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> A thin adapter inside the BFF over
/// the decision capability the REST route also calls (spec "Placement Justification"); no package, no AI-internal type, no background
/// work, no DI registration (the catalog finds the class by attribute). Component justification (section 11): existing = the REST
/// decision-plan and suppression routes; extension = this is a second host over their capability; cost of doing nothing = the wizard
/// cannot open an item from Copilot, so no commit can be recorded.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetDecisionPlanTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_decision_plan";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            HandleAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Get a decision plan",
                ReadOnly = true,
                Meta = PlanAndLookupToolSupport.AppOnlyMeta(),
                Description =
                    "Use for: opening one Decide or Do item in the Spaarke decision wizard. Returns the item's decision plan (the actions in plan "
                    + "order, the next steps and the closed dismissal reasons), its dismissal suppression state and its full Work Item row, all "
                    + "resolved as the signed-in user, and records that the user has read the plan, which a later completion requires. Not for: "
                    + "listing items (use spaarke_show_decisions), searching for a person, budget or firm (use spaarke_search_decision_lookup) "
                    + "or recording a decision (use spaarke_complete_decision). Called by the Spaarke decisions widget, not by the model.",
            });

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows: 1 when the plan was returned, else 0).</summary>
    internal static Task<CallToolResult> HandleAsync(
        [Description("The id (GUID) of the Work Item to open, from the Decide lane.")] Guid signalId,
        IHttpContextAccessor accessor,
        IDecisionCapability decisions,
        TimeProvider time,
        ILogger<GetDecisionPlanTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(signalId, accessor, decisions, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        Guid signalId,
        IHttpContextAccessor accessor,
        IDecisionCapability decisions,
        CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return McpToolResults.FromError(DecisionResultMapper.ToToolError(DecisionPlanReadResult.UserContextRequired)!, http?.TraceIdentifier);
        }

        // The plan first: a refusal (not readable by the caller, plan does not resolve, access unavailable) is the REST route's answer and
        // nothing else is read or stamped.
        var plan = await decisions.GetPlanAsync(http, signalId, cancellationToken);
        var planError = DecisionResultMapper.ToToolError(plan);
        if (planError != null)
        {
            return McpToolResults.FromError(planError, http!.TraceIdentifier);
        }

        var suppression = await decisions.GetSuppressionAsync(http, signalId, cancellationToken);
        var suppressionError = DecisionResultMapper.ToToolError(suppression);

        var lanes = await decisions.ListDecideAsync(http!.User, cancellationToken);
        var rowError = DecisionResultMapper.ToToolError(lanes);

        var body = new PlanAndLookupToolSupport.PlanBody(
            plan.Plan!,
            suppressionError == null ? suppression.Suppression : null,
            rowError == null ? FindRow(lanes, signalId) : null,
            suppressionError == null ? null : PlanAndLookupToolSupport.PartError.From(suppressionError),
            rowError == null ? null : PlanAndLookupToolSupport.PartError.From(rowError));

        // Counts only (ADR-015): no title, subject, matter or party name.
        return PlanAndLookupToolSupport.Success(
            $"Decision plan loaded: {plan.Plan!.Actions.Count} action(s), {plan.Plan.NextSteps.Count} next step(s).", body);
    }

    private static WorklistItemRow? FindRow(SignalReadResult lanes, Guid signalId) =>
        lanes.Response?.Lanes
            .SelectMany(lane => lane.Groups)
            .SelectMany(group => group.Items)
            .FirstOrDefault(item => item.SignalId == signalId);
}
