/**
 * SidePaneFilterChannel — generalized cross-iframe transport for side-pane →
 * DataGrid filter messages.
 *
 * Designed as a first-class framework feature in task 035 UAT iteration 5
 * after the operator directive: "we need to include the side pane context
 * injection for the calendar type interaction — generalized so we can use
 * this in other use cases".
 *
 * **Use cases**:
 *  - Date / range filters (the canonical Calendar pane)
 *  - Saved-filter sets (pick a named filter combination)
 *  - AI-assistant filters (natural language → conditions)
 *  - Map-based geographic filters
 *  - Multi-record parent-context pickers (filter by selected related records)
 *  - Advanced query builders
 *
 * Each side-pane web resource declares a unique `paneId` and sends filter
 * payloads with that id. Hosting Custom Pages subscribe by `paneId` and
 * translate the payload into `HostFilterCondition[]`.
 *
 * **Transport stack** (all four fired on send; subscribers listen on all four):
 *  1. `BroadcastChannel('spaarke-datagrid-sidepane-channel')` — primary
 *     cross-iframe transport. Works between sibling iframes in same-origin
 *     Dataverse contexts.
 *  2. `window.parent.postMessage` — fallback when BroadcastChannel is
 *     unavailable (some sandboxed iframes).
 *  3. `window.top.postMessage` — fallback for deeply nested iframe layouts.
 *  4. `CustomEvent('spaarke-sidepane-filter')` on the local window — same-
 *     window scenarios + unit tests.
 *
 * Subscribers filter messages by `paneId` so multiple panes can share the
 * channel without cross-talk.
 *
 * @see useSidePaneFilter (React host hook)
 * @see DataGridSidePaneOrchestrator (pane lifecycle)
 * @see docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §5
 */

const CHANNEL_NAME = 'spaarke-datagrid-sidepane-channel';
const MESSAGE_TYPE = 'SIDE_PANE_FILTER_CHANGED';
const CUSTOM_EVENT_TYPE = 'spaarke-sidepane-filter';

/**
 * The wire-format envelope for every side-pane filter message. The `payload`
 * shape is pane-specific — translators on the host side narrow the type.
 */
export interface SidePaneFilterMessage<TPayload = unknown> {
  readonly type: typeof MESSAGE_TYPE;
  /** Unique identifier of the source pane. Hosts filter on this. */
  readonly paneId: string;
  /** Pane-specific filter payload. Opaque to the channel. */
  readonly payload: TPayload;
}

function isMatchingMessage<TPayload>(data: unknown, paneId: string): data is SidePaneFilterMessage<TPayload> {
  return (
    typeof data === 'object' &&
    data !== null &&
    (data as SidePaneFilterMessage).type === MESSAGE_TYPE &&
    (data as SidePaneFilterMessage).paneId === paneId
  );
}

/**
 * Send a filter payload from a side-pane web resource. Call this whenever
 * the pane's UI produces a new filter selection (e.g. user clicks Apply on
 * a date range).
 *
 * Idempotent — sending the same payload twice produces two messages; hosts
 * dedupe via state. Safe to call from any same-origin web resource.
 *
 * @param args.paneId Unique pane identifier. Must match what the host subscribes to.
 * @param args.payload Pane-specific filter shape (opaque to the channel).
 */
export function sendSidePaneFilter<TPayload>(args: { paneId: string; payload: TPayload }): void {
  const message: SidePaneFilterMessage<TPayload> = {
    type: MESSAGE_TYPE,
    paneId: args.paneId,
    payload: args.payload,
  };

  // 1) BroadcastChannel — primary cross-iframe transport.
  try {
    if (typeof BroadcastChannel !== 'undefined') {
      const ch = new BroadcastChannel(CHANNEL_NAME);
      ch.postMessage(message);
      ch.close();
    }
  } catch {
    /* BroadcastChannel unavailable; fall through to postMessage */
  }

  // 2) Bubble to window.parent (covers form-tab iframe → form layout).
  try {
    if (typeof window !== 'undefined' && window.parent !== window) {
      window.parent.postMessage(message, window.location.origin);
    }
  } catch {
    /* cross-origin */
  }

  // 3) Bubble to window.top (covers deeply nested iframe layouts).
  try {
    if (typeof window !== 'undefined' && window.top && window.top !== window) {
      window.top.postMessage(message, window.location.origin);
    }
  } catch {
    /* cross-origin */
  }

  // 4) Local CustomEvent — useful for same-window scenarios + tests.
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent !== 'undefined') {
      window.dispatchEvent(new CustomEvent(CUSTOM_EVENT_TYPE, { detail: message }));
    }
  } catch {
    /* CustomEvent unsupported */
  }
}

/**
 * Subscribe to filter messages for a specific pane.
 *
 * Returns a cleanup function that tears down ALL listeners (BroadcastChannel,
 * window 'message', custom event). Call it when the host unmounts.
 *
 * @param paneId Unique pane identifier to listen for.
 * @param onPayload Callback fired with the payload portion of every matching message.
 * @returns Cleanup function.
 */
export function subscribeSidePaneFilter<TPayload>(paneId: string, onPayload: (payload: TPayload) => void): () => void {
  const handle = (data: unknown) => {
    if (isMatchingMessage<TPayload>(data, paneId)) {
      onPayload(data.payload);
    }
  };

  let bc: BroadcastChannel | null = null;
  try {
    if (typeof BroadcastChannel !== 'undefined') {
      bc = new BroadcastChannel(CHANNEL_NAME);
      bc.onmessage = e => handle(e.data);
    }
  } catch {
    /* BroadcastChannel unavailable */
  }

  const onMessage = (e: MessageEvent) => handle(e.data);
  if (typeof window !== 'undefined') {
    window.addEventListener('message', onMessage);
  }

  const onCustomEvent = (e: Event) => handle((e as CustomEvent).detail);
  if (typeof window !== 'undefined') {
    window.addEventListener(CUSTOM_EVENT_TYPE as keyof WindowEventMap, onCustomEvent as EventListener);
  }

  return () => {
    try {
      bc?.close();
    } catch {
      /* ignore */
    }
    if (typeof window !== 'undefined') {
      window.removeEventListener('message', onMessage);
      window.removeEventListener(CUSTOM_EVENT_TYPE as keyof WindowEventMap, onCustomEvent as EventListener);
    }
  };
}
