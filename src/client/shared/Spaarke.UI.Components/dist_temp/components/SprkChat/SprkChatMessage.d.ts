/**
 * SprkChatMessage - Individual chat message bubble
 *
 * Renders user messages (right-aligned, accent) and assistant messages (left-aligned, subtle).
 * Shows a typing indicator during streaming.
 *
 * For assistant messages that carry structured metadata, delegates to:
 *   - SprkChatMessageRenderer for responseType: markdown, citations, diff, entity_card, action_confirmation
 *   - PlanPreviewCard for responseType: plan_preview
 *
 * BroadcastChannel events dispatched by callbacks (ADR-012 — shared library MUST NOT call Xrm):
 *   - onNavigate  → broadcasts 'navigate_entity' on channel 'sprkchat-navigation'
 *   - onOpenDiff  → broadcasts 'open_diff'      on channel 'sprkchat-navigation'
 *   - onInsert    → broadcasts 'document_insert' on channel 'sprk-document-insert'
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 */
import * as React from 'react';
import { ISprkChatMessageProps } from './types';
/**
 * Extended props for SprkChatMessage.
 *
 * The optional `onProceed`, `onCancel`, and `onEditPlan` callbacks are only
 * used when the message carries `metadata.responseType === 'plan_preview'`.
 *
 * `onProceed` is wired to the BFF plan approval endpoint in task 072.
 * For now (task 062) a stub is passed from SprkChat.tsx.
 *
 * `onInsert` (Phase 2D) is called when the user clicks the Insert button on an
 * AI response message. SprkChat.tsx wires this to a BroadcastChannel dispatch
 * that sends a `document_insert` event to the AnalysisWorkspace editor.
 */
export interface ISprkChatMessageExtendedProps extends ISprkChatMessageProps {
    /**
     * Called when the user clicks Proceed on a PlanPreviewCard.
     * MUST be implemented in SprkChat.tsx (task 072 wires the BFF endpoint).
     */
    onProceed?: () => void;
    /**
     * Called when the user clicks Cancel on a PlanPreviewCard.
     * Typically removes or dismisses the plan message from the list.
     */
    onCancel?: () => void;
    /**
     * Called when the user submits an edit message from within a PlanPreviewCard.
     * SprkChat routes this to handleSend() so the BFF receives it as a new message
     * and can regenerate the plan.
     * @param editMessage - Free-text modification request from the user.
     */
    onEditPlan?: (editMessage: string) => void;
    /**
     * Whether the plan is currently being executed (SSE stream active).
     * When true, PlanPreviewCard shows step execution icons and the Cancel Execution button.
     */
    isPlanExecuting?: boolean;
    /**
     * Called when the user clicks Cancel Execution during plan execution.
     * MUST abort the SSE stream via AbortController (spec MUST rule).
     */
    onCancelExecution?: () => void;
    /**
     * Called when the user clicks the "Insert" button on an AI response message.
     * Receives the text content to insert. SprkChat.tsx dispatches this as a
     * `document_insert` BroadcastChannel event for the AnalysisWorkspace editor
     * (task 051 adds the Lexical handler on the receiving end).
     *
     * Only rendered on completed (non-streaming) assistant messages.
     *
     * @param content - The message text content to insert into the editor.
     * @see IDocumentInsertEvent in types.ts
     */
    onInsert?: (content: string) => void;
    /**
     * Called when the user clicks "Save to matter files" on a completed document
     * status message. SprkChat.tsx calls the BFF persist endpoint and updates the
     * message's persistenceState accordingly.
     *
     * Only passed for document_status messages when ChatHostContext.containerId is truthy.
     *
     * @param documentId - The session document ID to persist to SPE.
     * @see spec-FR-14 — Optional SPE persistence for uploaded documents
     */
    onSaveToMatterFiles?: (documentId: string) => void;
    /**
     * Whether the host context has a containerId (SPE container available).
     * When false/undefined, the "Save to matter files" button is hidden on
     * document_status messages.
     */
    hasContainerId?: boolean;
}
/**
 * SprkChatMessage - Renders a single chat message with role-appropriate styling.
 *
 * For plain assistant messages (no metadata.responseType) the existing
 * text bubble is rendered unchanged — no regression.
 *
 * For structured assistant messages, delegates to SprkChatMessageRenderer
 * (citations, diff, entity_card, action_confirmation) or PlanPreviewCard
 * (plan_preview).
 *
 * @example
 * ```tsx
 * // Plain text message (unchanged behaviour)
 * <SprkChatMessage
 *   message={{ role: "Assistant", content: "Hello!", timestamp: "..." }}
 * />
 *
 * // Structured citations card
 * <SprkChatMessage
 *   message={{
 *     role: "Assistant",
 *     content: "",
 *     timestamp: "...",
 *     metadata: {
 *       responseType: "citations",
 *       data: { text: "See [1]...", citations: [...] }
 *     }
 *   }}
 * />
 *
 * // Plan preview gate
 * <SprkChatMessage
 *   message={{
 *     role: "Assistant",
 *     content: "",
 *     timestamp: "...",
 *     metadata: {
 *       responseType: "plan_preview",
 *       planTitle: "Analyze Contract Risk",
 *       plan: [{ id: "s1", description: "...", status: "pending" }]
 *     }
 *   }}
 *   onProceed={() => triggerPlanApproval()}
 *   onCancel={() => dismissPlanMessage()}
 * />
 * ```
 */
export declare const SprkChatMessage: React.FC<ISprkChatMessageExtendedProps>;
export default SprkChatMessage;
//# sourceMappingURL=SprkChatMessage.d.ts.map