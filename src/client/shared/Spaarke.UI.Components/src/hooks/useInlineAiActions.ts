/**
 * useInlineAiActions - BroadcastChannel dispatcher for InlineAiToolbar actions
 *
 * Dispatches `InlineActionBroadcastEvent` messages via a BroadcastChannel when
 * the user triggers an inline AI action from the toolbar. The receiving end
 * (SprkChat side pane or AnalysisWorkspace bridge) subscribes to the same
 * channel and routes the event to the appropriate handler based on actionType:
 *
 *   - 'chat'  → stream a response into the active SprkChat session
 *   - 'diff'  → open DiffReviewPanel with before/after comparison
 *
 * Channel name is passed in as a parameter to keep this hook context-agnostic.
 * The standard channel for inline actions is 'sprk-inline-action'.
 *
 * Usage:
 * ```tsx
 * const { handleAction } = useInlineAiActions({
 *   channelName: 'sprk-inline-action',
 * });
 *
 * <InlineAiToolbar
 *   ...
 *   onAction={(action, text) => handleAction(action, text)}
 * />
 * ```
 *
 * Constraints:
 * - MUST NOT import Xrm or ComponentFramework (ADR-012)
 * - BroadcastChannel name MUST match SprkChatPane subscriber (spec-2B, spec-FR-04)
 * - Auth tokens MUST NEVER be included in BroadcastChannel messages (ADR-015)
 *
 * @see InlineActionBroadcastEvent - the wire format dispatched
 * @see useInlineAiToolbar — companion hook for selection tracking
 * @see ADR-012 - Shared Component Library
 */

import { useCallback, useRef, useEffect } from 'react';
import type { InlineAiAction, InlineActionBroadcastEvent } from '../components/InlineAiToolbar/inlineAiToolbar.types';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseInlineAiActionsOptions {
  /**
   * The BroadcastChannel name to post messages on.
   * MUST match the channel name subscribed to by SprkChatPane.
   * Standard value: 'sprk-inline-action'
   */
  channelName: string;
}

export interface UseInlineAiActionsResult {
  /**
   * Dispatch an inline AI action via BroadcastChannel.
   *
   * Posts an `InlineActionBroadcastEvent` message to the configured channel.
   * The receiving pane routes the event based on `actionType`:
   *   - 'chat' → SprkChat session receives the selected text as context
   *   - 'diff' → DiffReviewPanel opens with the selection for AI rewrite
   *
   * @param action - The InlineAiAction that was triggered
   * @param selectedText - The text that was selected when the action was triggered
   */
  handleAction: (action: InlineAiAction, selectedText: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Returns a stable `handleAction` callback that broadcasts an
 * `InlineActionBroadcastEvent` to the configured BroadcastChannel.
 *
 * The BroadcastChannel instance is created lazily on first action dispatch
 * and reused for subsequent dispatches to avoid unnecessary channel churn.
 * It is closed on component unmount.
 *
 * @param options - Configuration options
 * @returns Object containing the `handleAction` callback
 */
export function useInlineAiActions(options: UseInlineAiActionsOptions): UseInlineAiActionsResult {
  const { channelName } = options;

  // Lazily-initialized BroadcastChannel reference.
  // Created on first dispatch; closed on unmount.
  const channelRef = useRef<BroadcastChannel | null>(null);

  // ───────────────────────────────────────────────────────────────────────────
  // BroadcastChannel lifecycle
  // ───────────────────────────────────────────────────────────────────────────

  useEffect(() => {
    // Nothing to initialize eagerly — channel is created lazily on first dispatch.
    // Return cleanup to close any open channel when channelName changes or on unmount.
    return () => {
      if (channelRef.current) {
        channelRef.current.close();
        channelRef.current = null;
      }
    };
  }, [channelName]);

  // ───────────────────────────────────────────────────────────────────────────
  // Action dispatch
  // ───────────────────────────────────────────────────────────────────────────

  /**
   * Obtain (or create) the BroadcastChannel and post an InlineActionBroadcastEvent.
   */
  const handleAction = useCallback(
    (action: InlineAiAction, selectedText: string): void => {
      // Lazily create the channel on first use.
      // Re-create if the ref was cleared (e.g., after a channelName change).
      if (!channelRef.current) {
        try {
          channelRef.current = new BroadcastChannel(channelName);
        } catch (error) {
          // BroadcastChannel may be unavailable in some environments (e.g., IE, Node).
          // Silently drop the message — toolbar actions are non-critical when bridge
          // is unavailable.
          console.warn(
            `useInlineAiActions: Failed to create BroadcastChannel "${channelName}". ` +
              'Inline action was not dispatched.',
            error
          );
          return;
        }
      }

      const event: InlineActionBroadcastEvent = {
        type: 'inline_action',
        actionId: action.id,
        actionType: action.actionType,
        label: action.label,
        selectedText,
      };

      try {
        channelRef.current.postMessage(event);
      } catch (error) {
        // Channel may have been closed externally or entered an invalid state.
        // Reset the ref so that the next dispatch creates a fresh channel.
        console.warn(
          `useInlineAiActions: Failed to post message to channel "${channelName}". ` +
            'The channel may have been closed. It will be recreated on next dispatch.',
          error
        );
        channelRef.current = null;
      }
    },
    [channelName]
  );

  return { handleAction };
}
