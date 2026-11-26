/**
 * The Decisions widget's fetch shim (spaarkeai-mcp-bff-api-r1 task 030, FR-12, FR-09): the shared widget shim
 * (`src/client/widgets/shared/mcp/mcpFetch.ts`, spaarkeai-mcp-bff-api-r2 task 021, FR-16) with the Decisions routes' own titles.
 *
 * The Decide lane (task 031) and the wizard (task 032) pass their route tables to `createMcpFetch`; the shim turns each request into a
 * tool call through the host and wraps the result in the Response REST would have sent (see the shared file). The one Decisions-specific
 * rule is the `title` of a failed commit: REST's commit route answers a failed commit 500, or 409 when it is a stored failure returned
 * again, and titles both "The decision was not recorded" (`DecisionResultMapper.CommitProblem` / `ToHttp`; only a failed commit carries
 * the D-17 `partial` facts). Never a copy of the message: the wizard's `failureMessage` shows `detail` only when it differs from `title`.
 */
import type { ResponseFetchFn } from '@spaarke/ui-components/utils/fetchTypes';
import {
  createMcpFetch as createSharedMcpFetch,
  type CallServerTool,
  type McpRoute,
} from '../../../shared/mcp/mcpFetch';

export {
  ShimReason,
  DEFAULT_TOOL_ERROR_STATUS,
  NON_DOMAIN_ERROR_STATUS,
  type CallServerTool,
  type McpRequest,
  type McpRoute,
  type McpToolResult,
} from '../../../shared/mcp/mcpFetch';

const DECISION_NOT_RECORDED = 'The decision was not recorded';

/** The Decisions routes' titles: a failed commit (500, or 409 with the D-17 facts); the standard phrase otherwise. */
function decisionTitle(status: number, hasPartial: boolean): string | undefined {
  return status === 500 || (hasPartial && status === 409) ? DECISION_NOT_RECORDED : undefined;
}

/**
 * Builds the injected fetch for the shared services. Every outcome is a `Response` (a returned `!res.ok` keeps `reasonCode` and the
 * D-17 facts intact) EXCEPT a rejected host call, which is rethrown: the consumer then sees "no answer at all" (status 0), the honest
 * state for a commit whose outcome is unknown.
 */
export function createMcpFetch(callTool: CallServerTool, routes: readonly McpRoute[]): ResponseFetchFn {
  return createSharedMcpFetch(callTool, routes, { title: decisionTitle });
}
