/**
 * The decision wizard's reads and its one write, over MCP (spaarkeai-mcp-bff-api-r1 task 032, FR-13, FR-15, NFR-06).
 *
 * The shared wizard service (`createDecisionWizardService`) takes an injected `fetch` and asks it for three addresses:
 *   GET  /api/v1/signals/{id}/decision-plan   -> `spaarke_get_decision_plan`     (the plan member of the tool's result)
 *   GET  /api/v1/signals/{id}/suppression     -> `spaarke_get_decision_plan`     (the suppression member of the same result)
 *   POST /api/v1/signals/{id}/decision        -> `spaarke_complete_decision`     (the DecisionCommitResult, unchanged)
 * and the widget adds one internal address the type-ahead reads:
 *   GET  /api/v1/decision-lookup?signalId=&parameterCode=&query=  -> `spaarke_search_decision_lookup`
 * `createMcpFetch` (task 030) turns each into a tool call and each tool answer into the `Response` REST would have sent, so a refusal
 * keeps its `reasonCode`, `detail` and the D-17 facts (landed, possiblyLanded, resumable, ...) and `decisionDisplay` words it exactly as
 * the Console does. A host call that rejects is rethrown by the shim: the wizard then shows "may not have been recorded" for a commit.
 *
 * One plan call per item open. The plan tool reads the plan, the suppression and the full row in ONE call (and writes the plan-read
 * stamp a later commit requires), and it runs a whole-lane read on the server, so the three consumers of one open (the item loader, the
 * plan route and the suppression route) share that call: `readItemRow` makes it, and the next two plan or suppression reads of the same
 * item take its answer instead of calling again. Opening the same item again always makes a new call, so the stamp is fresh.
 *
 * Nothing here writes except the commit route, and that is reached only by the wizard's Confirm (`service.commit`): no read in this file
 * calls `spaarke_complete_decision`.
 */
import type { ResponseFetchFn } from '@spaarke/ui-components/utils/fetchTypes';
import { createMcpFetch, type CallServerTool, type McpRoute, type McpToolResult } from '../mcp/mcpFetch';

export const GET_DECISION_PLAN_TOOL = 'spaarke_get_decision_plan';
export const SEARCH_DECISION_LOOKUP_TOOL = 'spaarke_search_decision_lookup';
export const COMPLETE_DECISION_TOOL = 'spaarke_complete_decision';

/** The internal address of the type-ahead's read (never sent anywhere; mapped to the lookup tool). */
export const LOOKUP_ROUTE = '/api/v1/decision-lookup';

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const WIZARD_ROUTES: readonly McpRoute[] = [
  {
    method: 'GET',
    path: '/api/v1/signals/:signalId/decision-plan',
    tool: GET_DECISION_PLAN_TOOL,
    args: request => ({ signalId: request.params.signalId }),
    // The REST plan route returns the plan alone; the tool wraps it with the suppression and the row.
    result: structured => (isRecord(structured) && isRecord(structured.plan) ? structured.plan : undefined),
  },
  {
    method: 'GET',
    path: '/api/v1/signals/:signalId/suppression',
    tool: GET_DECISION_PLAN_TOOL,
    args: request => ({ signalId: request.params.signalId }),
    // A suppression the tool could not read is null: unreadable here, so the wizard says the dismissal history is unavailable
    // (`suppressionFailed`) rather than showing a count of nothing.
    result: structured =>
      isRecord(structured) && isRecord(structured.suppression) ? structured.suppression : undefined,
  },
  {
    method: 'POST',
    path: '/api/v1/signals/:signalId/decision',
    tool: COMPLETE_DECISION_TOOL,
    // The path identifier always wins: a body can never name another Work Item than the one the wizard is deciding.
    args: request => ({ signalId: request.params.signalId, review: request.body }),
  },
  {
    method: 'GET',
    path: LOOKUP_ROUTE,
    tool: SEARCH_DECISION_LOOKUP_TOOL,
    args: request => ({
      signalId: request.query.signalId,
      parameterCode: request.query.parameterCode,
      query: request.query.query ?? '',
    }),
  },
];

/** The plan tool's `structuredContent`: the plan, the suppression and the full row, each readable or null with a reason. */
export interface PlanToolBody {
  plan: unknown;
  suppression: unknown;
  row: unknown;
  suppressionError?: unknown;
  rowError?: unknown;
}

export interface WizardBridge {
  /** The injected fetch for `createDecisionWizardService` and the type-ahead. */
  fetch: ResponseFetchFn;
  /**
   * Starts one open of an item: calls the plan tool, and returns its body. The plan and suppression reads that follow for the same
   * item take this answer instead of calling the tool again. Rejects with an `Error` whose message is fit to show (the tool's own
   * message for a refusal; a plain sentence when the host did not answer).
   */
  readItemRow(signalId: string): Promise<PlanToolBody>;
}

const NOT_ANSWERED = 'The decision could not be read. Try again.';

function messageOf(result: McpToolResult): string {
  const structured = result.structuredContent;
  if (isRecord(structured) && typeof structured.message === 'string' && structured.message) return structured.message;
  const text = result.content?.find(c => c.type === 'text' && typeof c.text === 'string' && c.text)?.text;
  return text ?? NOT_ANSWERED;
}

/** Builds the wizard's fetch and the item-open call over one `callServerTool`. */
export function createWizardBridge(callTool: CallServerTool): WizardBridge {
  // One open of one item: the pending plan-tool answer and how many reads (plan, suppression) still expect it.
  const pending = new Map<string, { result: Promise<McpToolResult>; remaining: number }>();

  const sharedCall: CallServerTool = (name, args) => {
    if (name !== GET_DECISION_PLAN_TOOL) return callTool(name, args);
    const id = String(args.signalId);
    const open = pending.get(id);
    if (!open) return callTool(name, args); // no loader opened this item: an ordinary call
    open.remaining -= 1;
    if (open.remaining <= 0) pending.delete(id);
    return open.result;
  };

  return {
    fetch: createMcpFetch(sharedCall, WIZARD_ROUTES),

    async readItemRow(signalId) {
      const result = callTool(GET_DECISION_PLAN_TOOL, { signalId });
      // Registered before the first await so the plan and suppression reads that follow take this call.
      pending.set(signalId, { result, remaining: 2 });
      let answer: McpToolResult;
      try {
        answer = await result;
      } catch {
        pending.delete(signalId);
        throw new Error(NOT_ANSWERED);
      }
      if (answer.isError === true) {
        pending.delete(signalId);
        throw new Error(messageOf(answer));
      }
      if (!isRecord(answer.structuredContent)) {
        pending.delete(signalId);
        throw new Error(NOT_ANSWERED);
      }
      return answer.structuredContent as unknown as PlanToolBody;
    },
  };
}
