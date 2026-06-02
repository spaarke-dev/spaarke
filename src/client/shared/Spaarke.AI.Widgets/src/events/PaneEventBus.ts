/**
 * @spaarke/ai-widgets — PaneEventBus
 *
 * Typed, multi-subscriber, DOM-free event bus for cross-pane communication.
 *
 * Design goals:
 *  1. Multi-subscriber — every subscriber on a channel receives every event;
 *     no last-write-wins drop (fixes R1's single-listener limitation).
 *  2. Typed channels — the channel name determines the event payload type at
 *     compile time; no `any` casts in call sites.
 *  3. No DOM dependency — pure TypeScript; works in Node (tests) and browser.
 *  4. Stable API — subscribe() returns a cleanup function; React hooks wrap
 *     this directly in useEffect.
 *
 * Usage:
 *  const bus = new PaneEventBus();
 *
 *  const unsubscribe = bus.subscribe('context', (event) => {
 *    if (event.type === 'context_highlight') { ... }
 *  });
 *
 *  bus.dispatch('context', { type: 'context_highlight', citationId: 'ref-1' });
 *
 *  unsubscribe(); // removes this specific handler, leaves others intact
 *
 * @see PaneEventTypes — channel and payload type definitions
 * @see PaneEventBusProvider — React context that holds a shared bus instance
 * @see usePaneEvent — React hook for subscribing
 * @see useDispatchPaneEvent — React hook for dispatching
 */

import type { PaneChannel, PaneChannelEventMap, PaneEventHandler } from './PaneEventTypes';

// ---------------------------------------------------------------------------
// Internal channel store type
// ---------------------------------------------------------------------------

/**
 * Internal subscriber registry: one Set of handlers per channel.
 *
 * Using a Set (not an array) gives O(1) has/delete and automatic deduplication
 * if the same function reference is registered twice.
 */
type ChannelStore = {
  [C in PaneChannel]: Set<PaneEventHandler<C>>;
};

// ---------------------------------------------------------------------------
// PaneEventBus
// ---------------------------------------------------------------------------

/**
 * Multi-subscriber typed event bus for the four cross-pane channels.
 *
 * Create a single shared instance and provide it to the React tree via
 * PaneEventBusProvider. React components interact through usePaneEvent and
 * useDispatchPaneEvent rather than accessing the bus instance directly.
 */
export class PaneEventBus {
  /**
   * Per-channel subscriber sets.
   *
   * Typed as `ChannelStore` so TypeScript knows each channel holds a Set of
   * the correct handler type. Initialised lazily to keep construction cheap.
   */
  private readonly _channels: ChannelStore;

  constructor() {
    this._channels = {
      workspace: new Set(),
      context: new Set(),
      conversation: new Set(),
      safety: new Set(),
    };
  }

  // -------------------------------------------------------------------------
  // subscribe
  // -------------------------------------------------------------------------

  /**
   * Subscribe a handler to the given channel.
   *
   * The handler is added to the channel's Set and will receive every future
   * event dispatched on that channel until it is unsubscribed.
   *
   * @param channel - One of `'workspace'`, `'context'`, `'conversation'`, `'safety'`.
   * @param handler - Callback invoked with the typed event payload.
   * @returns An unsubscribe function. Call it to remove this specific handler
   *          without affecting other subscribers on the same channel.
   *
   * @example
   * const unsubscribe = bus.subscribe('workspace', (event) => {
   *   if (event.type === 'tab_change') { setActiveTab(event.tabId!); }
   * });
   * // On cleanup:
   * unsubscribe();
   */
  subscribe<C extends PaneChannel>(channel: C, handler: PaneEventHandler<C>): () => void {
    // Cast needed because TypeScript cannot narrow the Set<PaneEventHandler<C>>
    // through the mapped type index. The generic constraint guarantees correctness.
    const set = this._channels[channel] as Set<PaneEventHandler<C>>;
    set.add(handler);

    return (): void => {
      set.delete(handler);
    };
  }

  // -------------------------------------------------------------------------
  // dispatch
  // -------------------------------------------------------------------------

  /**
   * Dispatch a typed event to all subscribers on the given channel.
   *
   * Iterates the channel's Set and calls each handler synchronously in
   * insertion order. Subscribers added during dispatch are NOT called for the
   * current event (standard Set iteration behaviour).
   *
   * Dispatching to a channel with no subscribers is safe — no error is thrown
   * and no work is done.
   *
   * @param channel - The target channel.
   * @param event   - The typed event payload for that channel.
   *
   * @example
   * bus.dispatch('safety', {
   *   type: 'safety_annotation',
   *   confidence: 'high',
   *   groundedness: { score: 0.97 },
   * });
   */
  dispatch<C extends PaneChannel>(channel: C, event: PaneChannelEventMap[C]): void {
    const set = this._channels[channel] as Set<PaneEventHandler<C>>;

    // Iterate a snapshot so that handlers that unsubscribe during dispatch
    // do not mutate the Set mid-iteration.
    for (const handler of Array.from(set)) {
      handler(event);
    }
  }

  // -------------------------------------------------------------------------
  // subscriberCount (testing / diagnostics)
  // -------------------------------------------------------------------------

  /**
   * Returns the number of active subscribers on the given channel.
   *
   * Intended for unit tests and diagnostic logging — not for application logic.
   *
   * @param channel - The channel to inspect.
   */
  subscriberCount(channel: PaneChannel): number {
    return this._channels[channel].size;
  }
}
