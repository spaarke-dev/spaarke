/**
 * Test data for the Decide lane (task 031): rows shaped as `spaarke_list_decision_page` returns them (the slim row of
 * `WorklistItemSummary` plus `cores`, `nextCursor`), and a double for the HOST (`callServerTool`), which is the module boundary the lane
 * tests mock. Everything between the host and the DOM is the real code: the shim, the paged read, the shared worklist service and the
 * shared lane panel.
 */
import type { CallServerTool, McpToolResult } from '../mcp/mcpFetch';

export const PAGE_TOOL = 'spaarke_list_decision_page';

export const guid = (prefix: string, n: number): string =>
  `${prefix}${n.toString(16).padStart(8, '0')}-0000-4000-8000-000000000000`;

export interface SlimRow {
  signalId: string;
  coreRecordId?: string;
  lane: 'Decide';
  status: string;
  title?: string;
  subjectName?: string;
  ruleShortName?: string;
  workType?: string;
  pastDueWording: string;
  raisedOn?: string;
  invoiceReview?: { invoiceId: string; lineId?: string };
  privilegeFlagged: boolean;
}

export interface RowOptions {
  core?: string;
  workType?: string;
  invoiceReview?: { invoiceId: string; lineId?: string };
  raisedOn?: string;
  privileged?: boolean;
  subjectName?: string;
}

export function row(n: number, options: RowOptions = {}): SlimRow {
  return {
    signalId: guid('5', n),
    ...(options.core ? { coreRecordId: options.core } : {}),
    lane: 'Decide',
    status: 'Open',
    subjectName: options.subjectName ?? `Subject ${n}`,
    ruleShortName: `Rule ${n % 7}`,
    ...(options.workType ? { workType: options.workType } : {}),
    pastDueWording: 'overdue',
    raisedOn: options.raisedOn ?? '2026-10-06T12:00:00Z',
    ...(options.invoiceReview ? { invoiceReview: options.invoiceReview } : {}),
    privilegeFlagged: options.privileged ?? false,
  };
}

export const MATTERS = {
  a: { id: guid('a', 1), name: 'Acme v. Beta', number: 'MTR-00001' },
  b: { id: guid('a', 2), name: 'Northwind licence', number: 'MTR-00002' },
  c: { id: guid('a', 3), name: 'Globex audit', number: 'MTR-00003' },
  d: { id: guid('a', 4), name: 'Initech dispute', number: 'MTR-00004' },
};
export const CORES: Record<string, { name: string; number: string }> = Object.fromEntries(
  Object.values(MATTERS).map(m => [m.id, { name: m.name, number: m.number }])
);

export const INVOICE = '11111111-2222-3333-4444-555555555555';
export const LINE = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
export const CONSOLE_BASE = 'https://org.crm.dynamics.com';

/** A `callServerTool` double answering the paging tool from `rows`, `pageSize` at a time, with opaque cursors; records every call. */
export function pagingHost(rows: readonly SlimRow[], pageSize = 50) {
  const calls: Array<{ name: string; args: Record<string, unknown> }> = [];
  const callTool: CallServerTool = async (name, args) => {
    calls.push({ name, args });
    if (name !== PAGE_TOOL) throw new Error(`unexpected tool ${name}`);
    const start = typeof args.cursor === 'string' ? Number(args.cursor.replace('cursor-', '')) : 0;
    const items = rows.slice(start, start + pageSize);
    const cores: Record<string, { name: string; number: string }> = {};
    for (const item of items)
      if (item.coreRecordId && CORES[item.coreRecordId]) cores[item.coreRecordId] = CORES[item.coreRecordId];
    const next = start + pageSize < rows.length ? `cursor-${start + pageSize}` : undefined;
    return { structuredContent: { items, cores, ...(next ? { nextCursor: next } : {}) } };
  };
  return { callTool, calls };
}

/** An `isError` tool result as the BFF sends it (McpToolResults.FromError). */
export function toolError(reasonCode: string, message: string, status: number): McpToolResult {
  return {
    isError: true,
    content: [{ type: 'text', text: message }],
    structuredContent: { reasonCode, message, status, correlationId: 'corr-1' },
  };
}

/**
 * jsdom has no `Response`; the shim and the shared service use one. A minimal stand-in is installed only when it is missing (the node
 * environment has the real one). It is the transport object, not the code under test.
 */
export function installResponseStandIn(): void {
  if (typeof globalThis.Response !== 'undefined') return;
  class ResponseStandIn {
    readonly status: number;
    readonly ok: boolean;
    readonly headers: Map<string, string>;
    private readonly text: string;
    constructor(body?: string | null, init?: { status?: number; headers?: Record<string, string> }) {
      this.text = body ?? '';
      this.status = init?.status ?? 200;
      this.ok = this.status >= 200 && this.status <= 299;
      this.headers = new Map(Object.entries(init?.headers ?? {}));
    }
    json(): Promise<unknown> {
      return Promise.resolve(JSON.parse(this.text));
    }
  }
  (globalThis as unknown as { Response: unknown }).Response = ResponseStandIn;
}
