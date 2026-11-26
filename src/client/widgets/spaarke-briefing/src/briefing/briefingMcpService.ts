/**
 * The widget's `BriefingDataService`: the Console's `DailyBriefingApp` reads its briefing through this seam (spaarkeai-mcp-bff-api-r2
 * task 010), and here the briefing comes from the MCP tool `spaarke_get_briefing` (task 034), not from the BFF route. The widget has no
 * network and no token: every read is a tool call through the host (`../../../shared/mcp`, task 021).
 *
 * Two sources, one mapping:
 *  - The FIRST read waits for the result that opened the widget (the host delivers the tool result that rendered it), so opening the
 *    briefing costs one generation, not two. If none arrives within `OPENING_RESULT_WAIT_MS`, the widget asks for the briefing itself.
 *  - Every later read (the Refresh button, a changed window) calls the tool.
 * Both go through `mapBriefingResult`, which turns what the tool answered into the `NarrationResult` the component draws:
 *  - the briefing (`DailyBriefingNarrateResponse`, the route's body) -> `success`, normalised the way the Console's `fetchBriefingLive` does;
 *  - a tool failure -> `unavailable` for the statuses the Console treats so (503, 429; 401, 403), `error` otherwise, with fixed user-safe text.
 */
// Deep paths, not the package barrel: the barrel also registers the Console's workspace widget, which is not part of this bundle.
import type { BriefingDataService } from '@spaarke/daily-briefing-components/services/briefingDataService';
import type {
  BriefingWindowParams,
  NarrateResponse,
  NarrationResult,
} from '@spaarke/daily-briefing-components/services/briefingService';
import type { WidgetMcpTransport } from '../../../shared/mcp/widgetMcpTransport';

/** The tool this widget reads the briefing through. */
export const GET_BRIEFING_TOOL = 'spaarke_get_briefing';

/**
 * How long the first read waits for the result that opened the widget before it asks for the briefing itself. The tool answers within 30 s
 * (FR-14); this leaves room for the host to deliver the result after it, and bounds a widget opened with no result at all.
 */
export const OPENING_RESULT_WAIT_MS = 45_000;

const COULD_NOT_LOAD = 'Could not load the daily briefing.';
const NOT_AVAILABLE = 'The AI daily briefing is temporarily unavailable.';
const SIGN_IN_REQUIRED = 'Sign-in required to view your daily briefing.';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * Turns what the tool answered into the result the component draws. `structured` is the tool result's `structuredContent`; `isError` and
 * `reasonCode` come from the transport (`WidgetToolResult`); the opening result carries only the structured body, so a failure there is
 * recognised by its shape (`reasonCode` and no `channelNarratives`).
 */
export function mapBriefingResult(
  structured: unknown,
  failure?: { isError?: boolean; message?: string }
): NarrationResult {
  if (isRecord(structured) && Array.isArray(structured.channelNarratives) && failure?.isError !== true) {
    return { status: 'success', data: normalise(structured as unknown as NarrateResponse) };
  }

  const status = isRecord(structured) && typeof structured.status === 'number' ? structured.status : undefined;
  const message = isRecord(structured) && typeof structured.message === 'string' ? structured.message : undefined;
  if (status === 503 || status === 429) return { status: 'unavailable', reason: NOT_AVAILABLE };
  if (status === 401 || status === 403) return { status: 'unavailable', reason: SIGN_IN_REQUIRED };

  // A failure the tool described (its message says how to fix the call, never record text), or the transport's fixed text.
  const text = message ?? failure?.message;
  return { status: 'error', message: text && text.trim() ? text : COULD_NOT_LOAD };
}

/** The Console's `fetchBriefingLive` defaults for the TL;DR fields, so an omitted field never reaches the render code undefined. */
function normalise(data: NarrateResponse): NarrateResponse {
  const tldr = data.tldr;
  if (!tldr) return data;
  return {
    ...data,
    tldr: {
      ...tldr,
      keyTakeaways: Array.isArray(tldr.keyTakeaways) ? tldr.keyTakeaways : [],
      summary: tldr.summary ?? '',
      itemRefs: Array.isArray(tldr.itemRefs) ? tldr.itemRefs : [],
    },
  };
}

/**
 * Holds the result that opened the widget until the first read takes it. `offer` is called whenever the host delivers a result (the host
 * connection's `toolResult`); `take` resolves with the first one, once, or `undefined` after `waitMs` with none.
 */
export class OpeningResult {
  private value: unknown;
  private offered = false;
  private taken = false;
  private waiter: { resolve: (value: unknown) => void; timer: ReturnType<typeof setTimeout> } | undefined;

  /** The host delivered a result. Only the first one counts, and only until a read has taken it (a later result is not the opening one). */
  offer(result: unknown): void {
    if (this.offered || result === null || result === undefined) return;
    this.value = result;
    this.offered = true;
    if (this.waiter) {
      const { resolve, timer } = this.waiter;
      this.waiter = undefined;
      clearTimeout(timer);
      resolve(this.value);
    }
  }

  /** The opening result (once: a second call, or a call after the wait ran out, gets `undefined`), waiting up to `waitMs` for it. */
  take(waitMs: number): Promise<unknown> {
    if (this.taken) return Promise.resolve(undefined);
    this.taken = true;
    if (this.offered) return Promise.resolve(this.value);
    return new Promise(resolve => {
      const timer = setTimeout(() => {
        this.waiter = undefined;
        resolve(undefined);
      }, waitMs);
      this.waiter = { resolve, timer };
    });
  }
}

export interface BriefingMcpServiceOptions {
  transport: Pick<WidgetMcpTransport, 'callTool'>;
  opening: OpeningResult;
  /** Override for tests. */
  openingWaitMs?: number;
}

/** The seam over the tool: `renderBriefing` only. No preferences, so the component hides its Preferences menu; nothing else is offered. */
export function createBriefingMcpService({
  transport,
  opening,
  openingWaitMs = OPENING_RESULT_WAIT_MS,
}: BriefingMcpServiceOptions): BriefingDataService {
  return {
    async renderBriefing(windows?: BriefingWindowParams): Promise<NarrationResult> {
      const openedWith = await opening.take(openingWaitMs);
      if (openedWith !== undefined) return mapBriefingResult(openedWith);

      const result = await transport.callTool(
        GET_BRIEFING_TOOL,
        windows ? { dueWithinDays: windows.dueWithinDays, recencyHours: windows.recencyHours } : {}
      );
      const text = result.content?.find(block => block.type === 'text')?.text;
      return mapBriefingResult(result.structuredContent, { isError: result.isError, message: text });
    },
  };
}
