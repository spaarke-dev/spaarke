/**
 * @spaarke/ai-widgets — useCitationLink
 *
 * React hook that returns a stable `handleCitationClick` callback for wiring
 * up citation link clicks in workspace widgets.
 *
 * When a user clicks a bracketed citation reference (e.g. "[1]") inside a
 * workspace widget, the widget calls the returned handler. The handler
 * dispatches a `context_highlight` event to the `context` PaneEventBus
 * channel, which the ContextPaneController receives and forwards to the active
 * context widget's `onHighlight()` method — scrolling to and highlighting the
 * cited source passage.
 *
 * Design notes:
 * - Built on `useDispatchPaneEvent` (stable across renders) + `useCallback`,
 *   so the returned function reference is stable and safe for `React.memo`
 *   child props or dependency arrays.
 * - Does not require the PaneEventBusProvider to be at any specific level in
 *   the tree — it must simply be an ancestor of the calling component.
 * - The hook is a thin React adapter over `handleCitationClick`; all logic
 *   lives in the utility function for testability.
 *
 * @see handleCitationClick — underlying utility function (testable without React)
 * @see useDispatchPaneEvent — provides the typed dispatch function
 * @see ContextPaneController — handles context_highlight and routes to widgets
 *
 * Task: AIPU2-100
 *
 * @example
 * // In a workspace widget component:
 * function SummaryWidget({ data }: { data: SummaryData }) {
 *   const handleCitationClick = useCitationLink();
 *
 *   return (
 *     <p>
 *       The contract terminates on notice
 *       <button onClick={() => handleCitationClick('1')}>
 *         [1]
 *       </button>
 *       .
 *     </p>
 *   );
 * }
 *
 * @example
 * // With a sub-range selectionRef pointing to a specific character range:
 * function ClauseWidget({ data }: { data: ClauseData }) {
 *   const handleCitationClick = useCitationLink();
 *
 *   return (
 *     <span
 *       role="link"
 *       tabIndex={0}
 *       onClick={() => handleCitationClick('2', 'char:512-640')}
 *       onKeyDown={(e) => e.key === 'Enter' && handleCitationClick('2', 'char:512-640')}
 *     >
 *       [2]
 *     </span>
 *   );
 * }
 */

import { useCallback } from 'react';
import { useDispatchPaneEvent } from '../events/useDispatchPaneEvent';
import { handleCitationClick } from './CitationLinkHandler';

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

/**
 * Stable callback returned by `useCitationLink`.
 *
 * @param citationId   - Bracketed citation index or named slug (e.g. `"1"`).
 * @param selectionRef - Optional sub-range within the source (source-widget-
 *                       specific format, e.g. `"char:1024-1200"`).
 */
export type CitationClickHandler = (citationId: string, selectionRef?: string) => void;

// ---------------------------------------------------------------------------
// useCitationLink
// ---------------------------------------------------------------------------

/**
 * Returns a stable `handleCitationClick(citationId, selectionRef?)` function
 * for workspace widgets.
 *
 * The returned callback dispatches a `context_highlight` event via
 * PaneEventBus within the same event-loop tick as the click — satisfying the
 * <50 ms dispatch acceptance criterion (AIPU2-100 AC-1).
 *
 * @returns A stable callback: `(citationId: string, selectionRef?: string) => void`.
 *
 * @throws If called outside a `PaneEventBusProvider` tree (same constraint as
 *         `useDispatchPaneEvent`).
 */
export function useCitationLink(): CitationClickHandler {
  const dispatch = useDispatchPaneEvent();

  // useCallback depends only on dispatch, which is stable for the provider's
  // lifetime — so this callback is effectively stable across renders.
  return useCallback(
    (citationId: string, selectionRef?: string): void => {
      handleCitationClick(dispatch, { citationId, selectionRef });
    },
    [dispatch],
  );
}
