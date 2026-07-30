/**
 * @spaarke/ai-widgets — useDispatchPaneEvent
 *
 * React hook that returns a stable dispatch function for the PaneEventBus.
 *
 * The returned function is memoised with useCallback and will not change
 * across re-renders (the bus instance is stable for the provider's lifetime),
 * so components can pass it to child props or event handlers without causing
 * unnecessary re-renders.
 *
 * @example
 * // In an output pane citation link:
 * const dispatch = useDispatchPaneEvent();
 *
 * function handleCitationClick(citationId: string, selectionRef: string) {
 *   dispatch('context', {
 *     type: 'context_highlight',
 *     citationId,
 *     selectionRef,
 *   });
 * }
 *
 * @see usePaneEvent — companion hook for subscribing to events
 * @see PaneEventBusProvider — must wrap the component tree
 */

import { useCallback } from 'react';
import type { PaneChannel, PaneChannelEventMap } from './PaneEventTypes';
import { usePaneEventBus, useOptionalPaneEventBus } from './PaneEventBusContext';

// ---------------------------------------------------------------------------
// Return type
// ---------------------------------------------------------------------------

/**
 * The stable dispatch function returned by useDispatchPaneEvent.
 *
 * Typed as a generic function so callers get full inference at the call site:
 *
 * @example
 * const dispatch = useDispatchPaneEvent();
 *
 * // TypeScript infers 'workspace' → WorkspacePaneEvent payload:
 * dispatch('workspace', { type: 'tab_change', tabId: 'analysis' });
 *
 * // Type error — 'citationId' is not on WorkspacePaneEvent:
 * dispatch('workspace', { type: 'tab_change', citationId: 'ref-1' });
 */
export type DispatchPaneEvent = <C extends PaneChannel>(channel: C, event: PaneChannelEventMap[C]) => void;

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Returns a stable, typed dispatch function for the PaneEventBus.
 *
 * The function reference is stable across re-renders — wrap it in useCallback
 * at the call site is unnecessary. It is safe to pass directly to React.memo
 * child components or dependency arrays.
 *
 * @returns A dispatch function `<C extends PaneChannel>(channel, event) => void`.
 *
 * @example
 * function ConversationPlaybookPicker() {
 *   const dispatch = useDispatchPaneEvent();
 *
 *   const handlePlaybookSelect = (playbookId: string, playbookName: string) => {
 *     dispatch('conversation', { type: 'playbook_change', playbookId, playbookName });
 *   };
 *
 *   return <PlaybookList onSelect={handlePlaybookSelect} />;
 * }
 */
export function useDispatchPaneEvent(): DispatchPaneEvent {
  const bus = usePaneEventBus();

  // useCallback with [bus] dep — bus is stable for the provider's lifetime, so
  // this callback is effectively stable. The dep is explicit to satisfy
  // react-hooks/exhaustive-deps and correctly handle the (rare) case where the
  // provider is remounted with a different bus instance.
  return useCallback(
    <C extends PaneChannel>(channel: C, event: PaneChannelEventMap[C]): void => {
      bus.dispatch(channel, event);
    },
    [bus]
  );
}

/**
 * Non-throwing counterpart to {@link useDispatchPaneEvent}: returns a stable
 * dispatch function that forwards to the PaneEventBus when a
 * `<PaneEventBusProvider>` is present, and **no-ops** when it is not — instead
 * of throwing at render.
 *
 * For **dual-use widgets** (Pattern D) that render in BOTH the SpaarkeAi shell
 * (bus present → dispatch works) AND a bus-less host (LegalWorkspace section /
 * MDA / dev → dispatch silently no-ops so user interactions degrade gracefully
 * instead of crashing). The returned function is always defined, so callers do
 * not need a null check at the call site. Companion to `useOptionalPaneEventBus`.
 */
export function useOptionalDispatchPaneEvent(): DispatchPaneEvent {
  const bus = useOptionalPaneEventBus();

  return useCallback(
    <C extends PaneChannel>(channel: C, event: PaneChannelEventMap[C]): void => {
      // No provider in this host (e.g. standalone LegalWorkspace section / dev):
      // silently drop the dispatch. The widget still renders; the interaction is
      // simply inert where there is no bus to route it.
      bus?.dispatch(channel, event);
    },
    [bus]
  );
}
