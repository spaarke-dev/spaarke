namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// A single step within a pending plan awaiting user approval.
///
/// Stored as part of <see cref="PendingPlan"/> in Redis.
/// Serialized to JSON; the <c>ParametersJson</c> field holds tool-specific parameters
/// as a JSON string so the step can be re-executed on approval without re-running the LLM.
/// </summary>
/// <param name="Id">Step identifier (e.g., "step-1"). Used to track status updates during execution.</param>
/// <param name="Description">Human-readable description of this step shown in the PlanPreviewCard UI.</param>
/// <param name="ToolName">Name of the AI tool to call for this step (e.g., "EditWorkingDocument", "RunAnalysis").</param>
/// <param name="ParametersJson">
/// Tool-specific parameters encoded as a JSON string.
/// Preserved so the step can be re-executed on POST /plan/approve without re-querying the LLM.
/// </param>
public record PendingPlanStep(
    string Id,
    string Description,
    string ToolName,
    string ParametersJson);

/// <summary>
/// Represents a plan pending user approval before the BFF executes any tools.
///
/// Stored in Redis at key <c>"plan:pending:{tenantId}:{sessionId}"</c> with a 30-minute absolute TTL.
/// The plan is created when compound intent is detected (2+ tools, any Dataverse write, any external
/// action) and is deleted atomically when the user approves via POST /plan/approve.
///
/// Design decisions (task 070):
/// - Stored as a separate Redis key, NOT embedded in <see cref="ChatSession"/>, to avoid
///   inflating every session cache read with plan payload (can be 10-50 KB).
/// - 30-minute absolute TTL: the plan preview window is an interactive gate, not long-lived.
///   If the user walks away, the pending plan expires cleanly.
/// - <c>PlanId</c> is a generated GUID echoed back by the frontend on approval to prevent
///   double-execution (the endpoint validates it against the stored plan before deleting).
/// </summary>
/// <param name="PlanId">
/// Unique identifier for this pending plan (GUID string, no hyphens).
/// Sent to the frontend in the <c>plan_preview</c> SSE event; echoed back on POST /plan/approve.
/// Validated atomically on approval to prevent double-execution.
/// </param>
/// <param name="SessionId">Session ID this plan belongs to (for logging and validation).</param>
/// <param name="TenantId">Tenant ID — used as part of the Redis cache key (ADR-014).</param>
/// <param name="PlanTitle">
/// Display title shown at the top of the PlanPreviewCard (e.g., "Analyze contract risk and summarize findings").
/// </param>
/// <param name="Steps">Ordered list of steps to be executed on approval.</param>
/// <param name="AnalysisId">
/// Optional GUID of the <c>sprk_analysisoutput</c> record.
/// Present when the plan involves analysis write-back; null otherwise.
/// </param>
/// <param name="WriteBackTarget">
/// Optional canonical field path for the write-back step.
/// When present, always <c>"sprk_analysisoutput.sprk_workingdocument"</c>.
/// Null when the plan does not involve a Dataverse write.
/// </param>
/// <param name="CreatedAt">UTC creation timestamp (used for TTL validation).</param>
public record PendingPlan(
    string PlanId,
    string SessionId,
    string TenantId,
    string PlanTitle,
    PendingPlanStep[] Steps,
    string? AnalysisId,
    string? WriteBackTarget,
    DateTimeOffset CreatedAt);
