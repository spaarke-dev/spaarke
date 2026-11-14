/**
 * MCP fetch shims (spaarkeai-mcp-bff-api-r1 task 030, FR-12, FR-09).
 *
 * The shared Worklist and Decision wizard services take an injected `fetch` that returns a real `Response`
 * (`createWorklistService({ fetch, baseUrl })`, a `ResponseFetchFn`). In the Console that fetch talks to the BFF REST routes. Here the
 * widget has no network origin and no token, so the same services get a fetch that turns each request into an MCP tool call through the
 * host (`callServerTool`) and wraps the tool result in a `Response`:
 *
 *  - success: HTTP 200 whose JSON body is the tool's `structuredContent`;
 *  - `isError: true`: a non-2xx `Response` whose JSON body carries `reasonCode` and `message` (the REST problem-details vocabulary,
 *    FR-09), plus `detail` and `title` so a service that reads ProblemDetails keeps working. `decisionDisplay.ts` reads `reasonCode`
 *    (for example "Decide this in Invoice Review"), so it is passed through untouched.
 *
 * THIS FILE IS THE ONLY PLACE the widget makes a "fetch": it never touches the network. Nothing here reads or stores a token; the host
 * owns the connection and the caller's identity (ADR-028 A10). The route table (which request maps to which tool) is data the Decide
 * lane (task 031) and the wizard (task 032) pass in, so adding a tool never edits this file.
 */
import type { ResponseFetchFn } from '@spaarke/ui-components/utils/fetchTypes';

/** The part of an MCP `CallToolResult` the shim reads (structural, so tests need no MCP runtime). */
export interface McpToolResult {
  isError?: boolean;
  content?: ReadonlyArray<{ type: string; text?: string }>;
  structuredContent?: unknown;
}

/** Calls an MCP tool through the host: `app.callServerTool({ name, arguments })` in production. */
export type CallServerTool = (name: string, args: Record<string, unknown>) => Promise<McpToolResult>;

/** What a route's argument builder is given: the request as the shared services made it. */
export interface McpRequest {
  method: string;
  /** Path only, no origin or query, for example `/api/v1/signals/abc/decision-plan`. */
  path: string;
  /** The `:name` segments of the route's path pattern, URL-decoded. */
  params: Readonly<Record<string, string>>;
  query: Readonly<Record<string, string>>;
  /** The parsed JSON body; `undefined` when the request has none. */
  body: unknown;
}

/** One request shape mapped to one tool. */
export interface McpRoute {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  /** A path pattern whose `:name` segments bind to `McpRequest.params`, for example `/api/v1/signals/:signalId/decision-plan`. */
  path: string;
  tool: string;
  /** Builds the tool arguments. Default: the body object, then the query, then the path params, merged in that order (the path identifier always wins). */
  args?: (request: McpRequest) => Record<string, unknown>;
}

/** `reasonCode` values the shim itself produces (a domain failure keeps the tool's own `reasonCode`). */
export const ShimReason = {
  RouteUnmapped: 'widget.route_unmapped',
  BadRequest: 'widget.bad_request',
  HostCallFailed: 'widget.host_call_failed',
  UnreadableResult: 'widget.unreadable_result',
  ToolError: 'widget.tool_error',
} as const;

/** Status of an `isError` result when the tool does not name one: the request was understood and refused. */
export const DEFAULT_TOOL_ERROR_STATUS = 422;

// The origin is a placeholder used only to parse a relative address; no request is ever sent to it.
const PARSE_BASE = 'http://widget.invalid';

function compile(pattern: string): { regex: RegExp; names: string[] } {
  const names: string[] = [];
  const source = pattern
    .split('/')
    .map(segment => {
      if (segment.startsWith(':')) {
        names.push(segment.slice(1));
        return '([^/]+)';
      }
      return segment.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    })
    .join('/');
  return { regex: new RegExp(`^${source}/?$`), names };
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function problem(status: number, reasonCode: string, message: string): Response {
  return jsonResponse(status, { reasonCode, message, detail: message, title: message, status });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function firstText(result: McpToolResult): string | null {
  const block = result.content?.find(c => c.type === 'text' && typeof c.text === 'string' && c.text.length > 0);
  return block?.text ?? null;
}

/** The success body: `structuredContent`, else the first text block when it is JSON, else nothing. */
function successBody(result: McpToolResult): { ok: true; body: unknown } | { ok: false } {
  if (result.structuredContent !== undefined && result.structuredContent !== null) {
    return { ok: true, body: result.structuredContent };
  }
  const text = firstText(result);
  if (text !== null) {
    try {
      return { ok: true, body: JSON.parse(text) as unknown };
    } catch {
      return { ok: false };
    }
  }
  return { ok: false };
}

function errorResponse(result: McpToolResult): Response {
  const structured = isRecord(result.structuredContent) ? result.structuredContent : {};
  const message =
    (typeof structured.message === 'string' && structured.message) ||
    firstText(result) ||
    'The request was not completed.';
  const reasonCode =
    typeof structured.reasonCode === 'string' && structured.reasonCode ? structured.reasonCode : ShimReason.ToolError;
  const named = structured.status;
  const status =
    typeof named === 'number' && Number.isInteger(named) && named >= 400 && named <= 599
      ? named
      : DEFAULT_TOOL_ERROR_STATUS;

  // The tool's own members (reasonCode, message, and `partial` for a failed commit) are passed through; detail/title/status let a
  // service that reads RFC 7807 problem details work unchanged.
  return jsonResponse(status, { ...structured, reasonCode, message, detail: message, title: message, status });
}

async function readBody(init: RequestInit | undefined): Promise<{ ok: true; body: unknown } | { ok: false }> {
  const raw = init?.body;
  if (raw === undefined || raw === null || raw === '') return { ok: true, body: undefined };
  if (typeof raw !== 'string') return { ok: false };
  try {
    return { ok: true, body: JSON.parse(raw) as unknown };
  } catch {
    return { ok: false };
  }
}

/**
 * Builds the injected fetch for the shared services. Never throws: every outcome, including a rejected host call, is a `Response`
 * (the shared services handle a returned `!res.ok` and a thrown error, but a Response keeps `reasonCode` intact).
 */
export function createMcpFetch(callTool: CallServerTool, routes: readonly McpRoute[]): ResponseFetchFn {
  const compiled = routes.map(route => ({ route, ...compile(route.path) }));

  return async (address, init) => {
    const method = (init?.method ?? 'GET').toUpperCase();

    // An address that cannot be parsed, or a path segment with a malformed percent escape, is a bad request, not an exception.
    let url: URL;
    let matched: { route: McpRoute; params: Record<string, string> } | null = null;
    try {
      url = new URL(address, PARSE_BASE);
      for (const entry of compiled) {
        if (entry.route.method !== method) continue;
        const m = entry.regex.exec(url.pathname);
        if (!m) continue;
        const params: Record<string, string> = {};
        entry.names.forEach((name, i) => {
          params[name] = decodeURIComponent(m[i + 1]);
        });
        matched = { route: entry.route, params };
        break;
      }
    } catch {
      return problem(400, ShimReason.BadRequest, 'The request address is not valid.');
    }
    if (!matched) {
      return problem(404, ShimReason.RouteUnmapped, `No tool is mapped to ${method} ${url.pathname}.`);
    }

    const body = await readBody(init);
    if (!body.ok) {
      return problem(400, ShimReason.BadRequest, 'The request body is not valid JSON.');
    }

    const query: Record<string, string> = {};
    url.searchParams.forEach((value, key) => {
      query[key] = value;
    });
    const request: McpRequest = { method, path: url.pathname, params: matched.params, query, body: body.body };
    let args: Record<string, unknown>;
    try {
      // Default: body, then query, then the path params LAST, so a body member can never override the identifier in the path.
      args = matched.route.args
        ? matched.route.args(request)
        : { ...(isRecord(request.body) ? request.body : {}), ...query, ...request.params };
    } catch {
      return problem(400, ShimReason.BadRequest, 'The request could not be turned into a tool call.');
    }

    let result: McpToolResult;
    try {
      result = await callTool(matched.route.tool, args);
    } catch (err) {
      const reason = err instanceof Error && err.message ? err.message : 'The host could not complete the call.';
      return problem(502, ShimReason.HostCallFailed, reason);
    }

    if (!isRecord(result)) {
      return problem(502, ShimReason.UnreadableResult, 'The tool answered without a readable result.');
    }
    if (result.isError === true) return errorResponse(result);

    const success = successBody(result);
    if (!success.ok) {
      return problem(502, ShimReason.UnreadableResult, 'The tool answered without a readable result.');
    }
    return jsonResponse(200, success.body);
  };
}
