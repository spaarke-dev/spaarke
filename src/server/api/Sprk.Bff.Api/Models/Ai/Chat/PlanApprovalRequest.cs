namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Request body for POST /api/ai/chat/sessions/{sessionId}/plan/approve.
///
/// The frontend echoes back the <see cref="PlanId"/> it received in the <c>plan_preview</c>
/// SSE event. The BFF validates this ID against the stored pending plan before executing,
/// which prevents double-execution if the user clicks "Proceed" twice.
///
/// Shape (matches task 070 design doc, Section 4):
/// <code>
/// {
///   "planId": "a1b2c3d4e5f6..."
/// }
/// </code>
/// </summary>
/// <param name="PlanId">
/// The plan ID echoed back from the <c>plan_preview</c> SSE event.
/// Must match the <see cref="PendingPlan.PlanId"/> stored in Redis.
/// Used to prevent double-execution: if the plan was already approved (Redis key deleted),
/// the endpoint returns 409 Conflict.
/// </param>
public record PlanApprovalRequest(string PlanId);

/// <summary>
/// Immediate acknowledgment response for POST /api/ai/chat/sessions/{sessionId}/plan/approve.
///
/// Note: The actual plan execution result is delivered via SSE stream — this record represents
/// only the initial JSON acknowledgment returned before the SSE stream begins, used when the
/// endpoint returns a non-streaming error response (400/404/409).
///
/// For the SSE streaming success case, the response Content-Type is "text/event-stream"
/// and the body contains SSE events (<c>plan_step_start</c>, <c>token</c>,
/// <c>plan_step_complete</c>, <c>done</c>).
/// </summary>
/// <param name="PlanId">The plan ID that was approved.</param>
/// <param name="Status">
/// Execution status. Always "executing" when included in a 200-level response before the SSE
/// stream begins.
/// </param>
public record PlanApprovalResponse(string PlanId, string Status);
