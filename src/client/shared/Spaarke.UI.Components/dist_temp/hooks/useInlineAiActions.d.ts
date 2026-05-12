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
import type { InlineAiAction } from '../components/InlineAiToolbar/inlineAiToolbar.types';
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
export declare function useInlineAiActions(options: UseInlineAiActionsOptions): UseInlineAiActionsResult;
//# sourceMappingURL=useInlineAiActions.d.ts.map