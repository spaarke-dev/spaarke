/**
 * @spaarke/ai-widgets — usePaneEvent
 *
 * React hook for subscribing to a PaneEventBus channel.
 *
 * Subscribes on mount, unsubscribes on unmount. The handler reference is
 * stabilised internally via a ref so that callers can pass inline arrow
 * functions without causing the subscription to tear down and re-establish
 * on every render.
 *
 * @example
 * // In a context-pane component:
 * usePaneEvent('context', (event) => {
 *   if (event.type === 'context_highlight') {
 *     scrollToCitation(event.citationId!);
 *   }
 * });
 *
 * @see useDispatchPaneEvent — companion hook for dispatching events
 * @see PaneEventBusProvider — must wrap the component tree
 */

import { useEffect, useRef } from 'react';
import type { PaneChannel, PaneChannelEventMap } from './PaneEventTypes';
import { usePaneEventBus } from './PaneEventBusContext';

/**
 * Subscribe to a typed PaneEventBus channel for the lifetime of the component.
 *
 * The subscription is established in a useEffect (runs after mount) and torn
 * down in the effect's cleanup function (runs on unmount or when the channel
 * changes). The `handler` argument may be an unstable reference (new function
 * each render) — the hook always calls the latest version without
 * re-subscribing.
 *
 * @param channel - The channel to subscribe to.
 * @param handler - Callback invoked with the typed event payload. The reference
 *                  is allowed to change each render; the hook adapts via a ref.
 *
 * @example
 * function WorkspacePaneTabBar() {
 *   const [activeTab, setActiveTab] = useState('summary');
 *
 *   usePaneEvent('workspace', (event) => {
 *     if (event.type === 'tab_change' && event.tabId) {
 *       setActiveTab(event.tabId);
 *     }
 *   });
 *
 *   return <TabBar activeTab={activeTab} />;
 * }
 */
export function usePaneEvent<C extends PaneChannel>(
  channel: C,
  handler: (event: PaneChannelEventMap[C]) => void
): void {
  const bus = usePaneEventBus();

  // Store the latest handler in a ref so the effect closure never becomes
  // stale. This pattern avoids tearing down the subscription when the handler
  // function identity changes (e.g. on each parent re-render).
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect((): (() => void) => {
    // The stable wrapper delegates to whatever is currently in handlerRef.
    const stableHandler = (event: PaneChannelEventMap[C]): void => {
      handlerRef.current(event);
    };

    const unsubscribe = bus.subscribe(channel, stableHandler);

    // Cleanup: remove this handler when the component unmounts or when the
    // channel prop changes (channel change is unusual but handled correctly).
    return unsubscribe;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bus, channel]);
  // handler is intentionally omitted from deps — changes are handled via ref.
}
