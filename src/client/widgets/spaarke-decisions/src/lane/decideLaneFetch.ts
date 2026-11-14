/**
 * The Decide lane's read, over MCP (spaarkeai-mcp-bff-api-r1 task 031, FR-05, FR-12).
 *
 * The shared `createWorklistService` reads ONE address, `GET /api/v1/signals/worklist?set=lanes`, and parses the Signal read route's
 * body (`{ lanes: [{ lane, groups: [{ core, items }] }] }`). Here that address is answered by the app-only paging tool
 * `spaarke_list_decision_page`, one page of at most 50 slim rows per call (`{ items, cores, nextCursor }`, each row carrying its
 * `coreRecordId`). This module follows the cursor chain to the end, then builds the route's body from the pages, so the real service
 * parses it and the lane's counts describe the WHOLE lane, as the Console's cards do (Console-equal counts). One page at a time goes
 * through `createMcpFetch`, so every failure keeps the REST shape (`reasonCode`, `detail`) the service reads; a host call that rejects
 * is rethrown by the shim and reaches the service as "no answer".
 *
 * Why the first page is read through the paging tool and not taken from `spaarke_show_decisions`: that result is reduced for the model
 * (privileged items are only counted, FR-04), so it cannot show the full lane. The paging tool is app-only and carries full row detail.
 *
 * What the slim page does not carry: a core record's type (it names each core once, by name and number). The lane's headings need only
 * the name and number; the type is the placeholder `CORE_RECORD_TYPE`, so `groupKey` stays unique per core and no heading says a type.
 */
import type { ResponseFetchFn } from '@spaarke/ui-components/utils/fetchTypes';
import { createMcpFetch, ShimReason, type CallServerTool, type McpRoute } from '../mcp/mcpFetch';

/** The app-only paging tool (task 022). */
export const LIST_DECISION_PAGE_TOOL = 'spaarke_list_decision_page';

/** The address the shared worklist service reads for the lanes. */
export const WORKLIST_ROUTE = '/api/v1/signals/worklist';

/** An internal route (never sent anywhere): one page of the lane, `?cursor=` the previous page's `nextCursor`. */
const PAGE_ROUTE = '/api/v1/decision-lane/page';

/** The record type used for every core record: the slim page names no type (see the file header). */
export const CORE_RECORD_TYPE = 'core';

/** A lane longer than this is refused rather than followed: 100 pages is 5,000 Work Items, far past any real lane. */
export const MAX_PAGES = 100;

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** One page as `spaarke_list_decision_page` returns it. */
interface DecisionPage {
  items: Record<string, unknown>[];
  cores: Record<string, { name?: string | null; number?: string | null }>;
  nextCursor: string | null;
}

/** A page is readable when it has an `items` array; `cores` and `nextCursor` may be absent (no cores / last page). */
function readPage(structured: unknown): DecisionPage | undefined {
  if (!isRecord(structured) || !Array.isArray(structured.items)) return undefined;
  const cursor = structured.nextCursor;
  return {
    items: structured.items.filter(isRecord),
    cores: isRecord(structured.cores) ? (structured.cores as DecisionPage['cores']) : {},
    nextCursor: typeof cursor === 'string' && cursor !== '' ? cursor : null,
  };
}

const PAGE_ROUTES: readonly McpRoute[] = [
  {
    method: 'GET',
    path: PAGE_ROUTE,
    tool: LIST_DECISION_PAGE_TOOL,
    args: request => (request.query.cursor ? { cursor: request.query.cursor } : {}),
    // A body with no `items` array is an unreadable result (502), not an empty lane (a false all-clear).
    result: readPage,
  },
];

function failure(status: number, reasonCode: string, detail: string): Response {
  return new Response(JSON.stringify({ title: 'Bad Gateway', status, detail, reasonCode }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/**
 * The route's body for the pages read: the Decide lane's rows grouped by core record in the order they arrived (the Console's order:
 * one group's rows are contiguous within and across pages), a row seen twice kept once, and an empty Do lane (the widget shows Decide only).
 */
export function lanesBodyFromPages(pages: readonly DecisionPage[]): unknown {
  const groups = new Map<string, { core: unknown; items: Record<string, unknown>[] }>();
  const seen = new Set<string>();
  for (const page of pages) {
    for (const item of page.items) {
      const signalId = typeof item.signalId === 'string' ? item.signalId : '';
      if (!signalId || seen.has(signalId)) continue;
      seen.add(signalId);
      const coreId = typeof item.coreRecordId === 'string' && item.coreRecordId ? item.coreRecordId : null;
      const key = coreId ?? 'not-filed';
      let group = groups.get(key);
      if (!group) {
        const named = coreId ? page.cores[coreId] : undefined;
        group = {
          core: coreId
            ? {
                recordType: CORE_RECORD_TYPE,
                recordId: coreId,
                name: named?.name ?? null,
                number: named?.number ?? null,
              }
            : null,
          items: [],
        };
        groups.set(key, group);
      }
      group.items.push(item);
    }
  }
  return {
    lanes: [
      {
        lane: 'Decide',
        groups: [...groups.values()].map(g => ({ core: g.core, notFiled: g.core === null, items: g.items })),
      },
      { lane: 'Do', groups: [] },
    ],
  };
}

/**
 * The injected fetch for `createWorklistService` on the Decide lane. `GET /api/v1/signals/worklist?set=lanes` reads every page; any
 * other request is not mapped (404, `widget.route_unmapped`), since nothing else is read here.
 */
export function createDecideLaneFetch(callTool: CallServerTool): ResponseFetchFn {
  const pageFetch = createMcpFetch(callTool, PAGE_ROUTES);

  return async (address, init) => {
    const method = (init?.method ?? 'GET').toUpperCase();
    let isLanesRead = false;
    try {
      const url = new URL(address, 'http://widget.invalid');
      isLanesRead = method === 'GET' && url.pathname === WORKLIST_ROUTE && url.searchParams.get('set') === 'lanes';
    } catch {
      isLanesRead = false;
    }
    if (!isLanesRead) return pageFetch(address, init);

    const pages: DecisionPage[] = [];
    const cursors = new Set<string>();
    let cursor: string | null = null;
    for (let count = 0; count < MAX_PAGES; count += 1) {
      const response: Response = await pageFetch(
        cursor === null ? PAGE_ROUTE : `${PAGE_ROUTE}?cursor=${encodeURIComponent(cursor)}`,
        { method: 'GET' }
      );
      // A failure on any page fails the read: a partial lane would show counts the Console would not.
      if (!response.ok) return response;
      const page = (await response.json()) as DecisionPage;
      pages.push(page);
      if (page.nextCursor === null) {
        return new Response(JSON.stringify(lanesBodyFromPages(pages)), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      // A cursor seen before would loop for ever.
      if (cursors.has(page.nextCursor)) {
        return failure(
          502,
          ShimReason.UnreadableResult,
          'The decision lane repeated a page, so it could not be read to the end.'
        );
      }
      cursors.add(page.nextCursor);
      cursor = page.nextCursor;
    }
    return failure(502, ShimReason.UnreadableResult, 'The decision lane is too long to read here. Open it in Spaarke.');
  };
}
