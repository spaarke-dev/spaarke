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
import { makeStyles, shorthands, tokens, mergeClasses, Text, Spinner, Button } from '@fluentui/react-components';
import {
  ArrowExportRegular,
  CheckmarkRegular,
  DismissRegular,
  ArrowSyncRegular,
  BookmarkRegular,
} from '@fluentui/react-icons';
import { ISprkChatMessageProps, ICitation, IDocumentStatusChatMessage } from './types';
import { CitationMarker } from './SprkChatCitationPopover';
import { SprkChatMessageRenderer } from './SprkChatMessageRenderer';
import type { INextStepChip } from './OutcomeCard';
import { SprkChatDocumentStatus } from './SprkChatDocumentStatus';
import { PlanPreviewCard } from './PlanPreviewCard';
import type { PlanStep } from './PlanPreviewCard';
import { renderMarkdown as renderMarkdownHtml, SPRK_MARKDOWN_CSS } from '../../services/renderMarkdown';

// ─────────────────────────────────────────────────────────────────────────────
// Markdown CSS injection (shared with SprkChatMessageRenderer, idempotent)
// ─────────────────────────────────────────────────────────────────────────────

const SPRK_MARKDOWN_STYLE_ID = 'sprk-markdown-styles';

function ensureMarkdownCssInjected(): void {
  if (typeof document === 'undefined') return;
  if (document.getElementById(SPRK_MARKDOWN_STYLE_ID)) return;

  const style = document.createElement('style');
  style.id = SPRK_MARKDOWN_STYLE_ID;
  style.textContent = SPRK_MARKDOWN_CSS;
  document.head.appendChild(style);
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    maxWidth: '80%',
    ...shorthands.padding('8px', '12px'),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    wordBreak: 'break-word',
    whiteSpace: 'pre-wrap',
  },
  userContainer: {
    alignSelf: 'flex-end',
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  assistantContainer: {
    alignSelf: 'flex-start',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
  /** Structured cards use the full available width within the message list. */
  structuredContainer: {
    alignSelf: 'stretch',
    maxWidth: '100%',
    backgroundColor: 'transparent',
    ...shorthands.padding('0'),
  },
  messageContent: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },
  /** Container for markdown-rendered assistant message content. */
  markdownContent: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    wordBreak: 'break-word',
  },
  timestamp: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    marginTop: '4px',
    alignSelf: 'flex-end',
  },
  userTimestamp: {
    color: tokens.colorNeutralForegroundOnBrand,
    opacity: 0.7,
  },
  streamingIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginTop: '4px',
  },
  /**
   * Action row below AI message content (Insert button + future Copy, etc.).
   * Only rendered on completed (non-streaming) assistant messages.
   */
  messageActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    marginTop: tokens.spacingVerticalXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format a timestamp string for display.
 * Shows time in the user's local timezone.
 */
function formatTimestamp(timestamp: string): string {
  try {
    const date = new Date(timestamp);
    return date.toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return '';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Citation Rendering
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Regex to match citation markers like [1], [2], [12], etc. in message text.
 * Captures the numeric ID inside the brackets.
 */
const CITATION_MARKER_REGEX = /\[(\d+)\]/g;

/**
 * Builds a lookup map from citation ID to ICitation for O(1) access.
 */
function buildCitationMap(citations: ICitation[]): Map<number, ICitation> {
  const map = new Map<number, ICitation>();
  for (const c of citations) {
    map.set(c.id, c);
  }
  return map;
}

/**
 * Parses message text and replaces [N] markers with CitationMarker components
 * when a matching citation exists.
 *
 * Returns an array of React nodes: plain text strings interspersed with
 * CitationMarker elements. If no citations are provided or no markers match,
 * returns the original text as a single-element array.
 */
function renderContentWithCitations(text: string, citations: ICitation[] | undefined): React.ReactNode[] {
  if (!citations || citations.length === 0) {
    return [text];
  }

  const citationMap = buildCitationMap(citations);
  const nodes: React.ReactNode[] = [];
  let lastIndex = 0;

  // Reset regex state (global regex retains lastIndex between calls)
  CITATION_MARKER_REGEX.lastIndex = 0;

  let match: RegExpExecArray | null;
  while ((match = CITATION_MARKER_REGEX.exec(text)) !== null) {
    const citationId = parseInt(match[1], 10);
    const citation = citationMap.get(citationId);

    if (!citation) {
      // No matching citation metadata — leave the [N] marker as plain text
      continue;
    }

    // Add text before this marker
    if (match.index > lastIndex) {
      nodes.push(text.slice(lastIndex, match.index));
    }

    // Add the CitationMarker component
    nodes.push(
      React.createElement(CitationMarker, {
        key: `citation-${citationId}-${match.index}`,
        citation,
      })
    );

    lastIndex = match.index + match[0].length;
  }

  // Add remaining text after the last marker
  if (lastIndex < text.length) {
    nodes.push(text.slice(lastIndex));
  }

  // If no markers were replaced, return original text
  if (nodes.length === 0) {
    return [text];
  }

  return nodes;
}

// ─────────────────────────────────────────────────────────────────────────────
// BroadcastChannel helpers (ADR-012: shared lib MUST NOT call Xrm directly)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel name used for navigation events from SprkChat to host layer.
 * AnalysisWorkspace / SprkChatPane listen on this channel and call
 * Xrm.Navigation on behalf of the shared library.
 */
const SPRKCHAT_NAVIGATION_CHANNEL = 'sprkchat-navigation';

/**
 * Dispatch a navigate_entity event via BroadcastChannel so the host layer
 * (AnalysisWorkspace or SprkChatPane Code Page) can call Xrm.Navigation.
 *
 * Falls back to console.warn when BroadcastChannel is unavailable (e.g. unit tests).
 */
function dispatchNavigateEntity(entityType: string, entityId: string): void {
  try {
    const channel = new BroadcastChannel(SPRKCHAT_NAVIGATION_CHANNEL);
    channel.postMessage({ type: 'navigate_entity', entityType, entityId });
    channel.close();
  } catch (err) {
    console.warn('[SprkChatMessage] BroadcastChannel unavailable for navigate_entity:', err);
  }
}

/**
 * Dispatch an open_diff event via BroadcastChannel so the host layer can open
 * DiffReviewPanel with the proposed text.
 *
 * Falls back to console.warn when BroadcastChannel is unavailable.
 */
function dispatchOpenDiff(proposedText: string): void {
  try {
    const channel = new BroadcastChannel(SPRKCHAT_NAVIGATION_CHANNEL);
    channel.postMessage({ type: 'open_diff', proposedText });
    channel.close();
  } catch (err) {
    console.warn('[SprkChatMessage] BroadcastChannel unavailable for open_diff:', err);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Extended props for plan_preview integration
// ─────────────────────────────────────────────────────────────────────────────

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
   * @deprecated spaarkeai-compose-r2 FIX #10a: the generic per-message "Open in Compose" affordance was
   * REMOVED (it did not reliably work and was not always appropriate). This prop is retained for prop
   * compatibility with SprkChat's conditional forwarding but NO button is rendered from it any longer.
   * Intentional mounting now happens via the "revise this document" flow / server-driven
   * `workspace_open_tab` seed, not an auto-appended per-message link.
   */
  onOpenInCompose?: (content: string) => void;
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

  /**
   * chat-routing-redesign-r1 task 117b (FR-50). Called when the user clicks an
   * inline candidate-playbook link button on a `playbook_options` card.
   * Receives the playbook GUID + session attachment IDs so the host can POST
   * to the dispatcher endpoint.
   *
   * Only meaningful on messages with `metadata.responseType === 'playbook_options'`.
   * Buttons render disabled when this prop is absent.
   */
  onSelectPlaybook?: (playbookId: string, sessionAttachmentIds: string[]) => void;

  /**
   * chat-routing-redesign-r1 task 117b (FR-51). Called when the user clicks the
   * "Open Library" link on a `playbook_options` card.
   * Receives the session attachment IDs so the host can pre-filter the modal
   * by attachment classification when available.
   *
   * Only meaningful on messages with `metadata.responseType === 'playbook_options'`.
   * The link renders disabled when this prop is absent.
   */
  onOpenLibraryModal?: (sessionAttachmentIds: string[]) => void;

  /**
   * spaarke-ai-architecture-redesign-r2 task 062 (FR-A1-06 / FR-B-13 workspace-intelligence
   * precursor). Called when the user clicks a next-step chip on an `outcome_card` card. Threaded
   * straight through to `SprkChatMessageRenderer` (only meaningful when
   * `metadata.responseType === 'outcome_card'`); chips render disabled when omitted.
   */
  onNextStep?: (chip: INextStepChip) => void;

  /**
   * spaarkeai-compose-r2 DEF-12 — per-message Compose-edit controls. Rendered ONLY on an Assistant
   * message carrying `metadata.composeEdit` while {@link composeEditActive} is true. Each callback
   * receives the message's `ledgerRef` + `bindingId`; the host wires them to the existing redline
   * handlers (Accept → usePendingRedline.accept, Reject → useEditSupersession.undo, Try-another →
   * useEditSupersession.tryAnother). Omitting a callback renders that control disabled.
   */
  onComposeEditAccept?: (ledgerRef: string, bindingId: string) => void;
  onComposeEditReject?: (ledgerRef: string, bindingId: string) => void;
  onComposeEditTryAnother?: (ledgerRef: string, bindingId: string) => void;
  /**
   * spaarkeai-compose-r2 FIX #3 — "Keep redline". Dismisses the action prompt but LEAVES the pending
   * redline marks in place so the user keeps editing (the host clears only the tracked edit; it does
   * NOT accept, undo, or reject). Omitting this callback renders the control disabled.
   */
  onComposeEditKeep?: (ledgerRef: string, bindingId: string) => void;
  /**
   * DEF-12 — whether this message's compose edit is still the live pending one. False (stale /
   * superseded / accepted) suppresses the controls so old confirmations don't show dead buttons.
   */
  composeEditActive?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

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
export const SprkChatMessage: React.FC<ISprkChatMessageExtendedProps> = ({
  message,
  isStreaming = false,
  citations,
  onProceed,
  onCancel,
  onEditPlan,
  isPlanExecuting,
  onCancelExecution,
  onInsert,
  onSaveToMatterFiles,
  hasContainerId,
  // chat-routing-redesign-r1 task 117b
  onSelectPlaybook,
  onOpenLibraryModal,
  // spaarke-ai-architecture-redesign-r2 task 062
  onNextStep,
  // spaarkeai-compose-r2 DEF-12
  onComposeEditAccept,
  onComposeEditReject,
  onComposeEditTryAnother,
  onComposeEditKeep,
  composeEditActive,
}) => {
  const styles = useStyles();
  const isUser = message.role === 'User';
  const isAssistant = message.role === 'Assistant';

  // ── Hooks (MUST be called unconditionally — before any early returns) ──────

  const containerClass = mergeClasses(styles.container, isUser ? styles.userContainer : styles.assistantContainer);
  const timestampClass = mergeClasses(styles.timestamp, isUser ? styles.userTimestamp : undefined);

  // For assistant messages with citations, parse [N] markers and render
  // interactive CitationMarker components. User messages are always plain text.
  const renderedContent = React.useMemo(() => {
    if (isAssistant && citations && citations.length > 0 && !isStreaming) {
      return renderContentWithCitations(message.content, citations);
    }
    return message.content;
  }, [message.content, citations, isAssistant, isStreaming]);

  // Inject markdown CSS once on first mount (idempotent — shared with SprkChatMessageRenderer)
  React.useEffect(() => {
    ensureMarkdownCssInjected();
  }, []);

  // Render assistant messages as markdown HTML (headings, bold, code blocks, etc.)
  // User messages are rendered as plain text to avoid unexpected formatting.
  // During streaming, render as plain text to avoid re-parsing on every token.
  const markdownHtml = React.useMemo(() => {
    if (isAssistant && !isStreaming && message.content && !(citations && citations.length > 0)) {
      return renderMarkdownHtml(message.content);
    }
    return null;
  }, [message.content, isAssistant, isStreaming, citations]);

  // ── Structured response rendering ──────────────────────────────────────────

  const responseType = message.metadata?.responseType;
  const isStructured = isAssistant && responseType != null && responseType !== '';

  // ── DEF-12: per-message Compose-edit controls (Accept / Reject / Try-another) ──────────────
  // Attached to the CONFIRMATION message for an applied Compose AI edit (the Assistant is the AI↔user
  // interaction surface — Word Copilot parity; the cramped in-editor bar is gone). Rendered ONLY while
  // this edit is the live pending one (`composeEditActive`). Because the confirmation carries
  // `responseType: 'markdown'` it renders through the structured branch, so the controls node is shared
  // into BOTH the structured-markdown branch and the plain-text branch below.
  const composeEdit = isAssistant && !isStreaming ? message.metadata?.composeEdit : undefined;
  const showComposeEditControls = !!composeEdit && composeEditActive === true;
  const composeEditControlsNode =
    showComposeEditControls && composeEdit ? (
      <div className={styles.messageActions} role="group" aria-label="Accept, reject, or replace the AI edit">
        <Button
          appearance="primary"
          size="small"
          icon={React.createElement(CheckmarkRegular)}
          disabled={!onComposeEditAccept}
          onClick={() => onComposeEditAccept?.(composeEdit.ledgerRef, composeEdit.bindingId)}
          data-testid="compose-edit-accept"
          title="Accept the tracked change in the document"
        >
          Accept
        </Button>
        <Button
          appearance="subtle"
          size="small"
          icon={React.createElement(DismissRegular)}
          disabled={!onComposeEditReject}
          onClick={() => onComposeEditReject?.(composeEdit.ledgerRef, composeEdit.bindingId)}
          data-testid="compose-edit-reject"
          title="Reject the change (removes the redline)"
        >
          Reject
        </Button>
        <Button
          appearance="subtle"
          size="small"
          icon={React.createElement(ArrowSyncRegular)}
          disabled={!onComposeEditTryAnother}
          onClick={() => onComposeEditTryAnother?.(composeEdit.ledgerRef, composeEdit.bindingId)}
          data-testid="compose-edit-try-another"
          title="Discard this and draft a different alternative"
        >
          Try another
        </Button>
        {/* FIX #3 — "Keep redline": dismiss this action prompt but LEAVE the pending redline marks in
            place so the user keeps editing. The per-change on-click Accept/Reject popover remains. */}
        <Button
          appearance="subtle"
          size="small"
          icon={React.createElement(BookmarkRegular)}
          disabled={!onComposeEditKeep}
          onClick={() => onComposeEditKeep?.(composeEdit.ledgerRef, composeEdit.bindingId)}
          data-testid="compose-edit-keep"
          title="Keep the redline in the document and keep editing"
        >
          Keep redline
        </Button>
      </div>
    ) : null;

  // ── Document status rendering (FR-14: Save to matter files) ────────────────
  // When the message carries document_status metadata, render SprkChatDocumentStatus
  // with the save-to-matter-files action button (only when containerId is available).
  if (responseType === 'document_status') {
    const docMsg = message as IDocumentStatusChatMessage;
    if (docMsg.documentStatus) {
      return (
        <div
          className={styles.structuredContainer}
          role="listitem"
          aria-label={`Document status: ${docMsg.documentStatus.fileName}`}
        >
          <SprkChatDocumentStatus
            status={docMsg.documentStatus}
            onSaveToMatterFiles={onSaveToMatterFiles}
            hasContainerId={hasContainerId}
          />
        </div>
      );
    }
  }

  // PlanPreviewCard gate — only when not currently streaming the plan
  if (isStructured && responseType === 'plan_preview' && !isStreaming) {
    const planSteps: PlanStep[] = (message.metadata?.plan ?? []).map(s => ({
      id: s.id,
      description: s.description,
      status: s.status,
      result: s.result,
    }));

    return (
      <div className={styles.structuredContainer} role="listitem" aria-label="AI plan preview">
        <PlanPreviewCard
          planTitle={message.metadata?.planTitle ?? 'Proposed Plan'}
          steps={planSteps}
          isExecuting={isPlanExecuting ?? false}
          onProceed={
            onProceed ??
            (() => {
              console.log('[SprkChatMessage] onProceed stub');
            })
          }
          onCancel={
            onCancel ??
            (() => {
              console.log('[SprkChatMessage] onCancel stub');
            })
          }
          onEditPlan={
            onEditPlan ??
            (editMessage => {
              console.log('[SprkChatMessage] onEditPlan stub — edit message:', editMessage);
            })
          }
          onCancelExecution={onCancelExecution}
        />
        {onInsert && message.content && (
          <div className={styles.messageActions}>
            <Button
              appearance="subtle"
              size="small"
              icon={React.createElement(ArrowExportRegular)}
              onClick={() => onInsert(message.content)}
              title="Insert into editor"
            >
              Insert
            </Button>
          </div>
        )}
      </div>
    );
  }

  // SprkChatMessageRenderer for all other structured types (including 'markdown')
  if (isStructured && responseType !== 'plan_preview' && !isStreaming) {
    const structuredData = message.metadata?.data ?? { text: message.content };

    // Derive insertable content from structured data.
    // For text-based types (markdown, citations, diff summary) extract the 'text'
    // or 'summary' field. For entity_card and action_confirmation, use message.content
    // as the fallback — these card types rarely carry a free-text body to insert.
    const structuredInsertContent =
      (structuredData as { text?: string }).text ?? (structuredData as { summary?: string }).summary ?? message.content;

    return (
      <div
        className={styles.structuredContainer}
        role="listitem"
        aria-label={`Assistant structured response: ${responseType}`}
      >
        <SprkChatMessageRenderer
          responseType={responseType}
          data={structuredData as Parameters<typeof SprkChatMessageRenderer>[0]['data']}
          onNavigate={(entityType, entityId) => {
            // ADR-012: MUST NOT call Xrm directly — dispatch BroadcastChannel event
            dispatchNavigateEntity(entityType, entityId);
          }}
          onOpenDiff={proposedText => {
            // Dispatch open_diff event so host layer opens DiffReviewPanel
            dispatchOpenDiff(proposedText);
          }}
          // chat-routing-redesign-r1 task 117b: thread playbook-options handlers
          // down to the renderer's card. SprkChatMessageRenderer only uses these
          // when responseType === 'playbook_options'.
          onSelectPlaybook={onSelectPlaybook}
          onOpenLibraryModal={onOpenLibraryModal}
          // spaarke-ai-architecture-redesign-r2 task 062 (FR-B-13): thread the
          // next-step chip handler down to the outcome_card renderer. Only used
          // when responseType === 'outcome_card'.
          onNextStep={onNextStep}
        />
        {/* DEF-12: compose-edit confirmation controls (responseType 'markdown' renders here). */}
        {composeEditControlsNode}
        {/*
         * chat-routing-redesign-r1 task 117b: suppress the Insert button on
         * `playbook_options` cards — they have no free-text body to insert into
         * an editor. The card's link buttons + library link are the only
         * affordances for this response type.
         */}
        {onInsert && structuredInsertContent && responseType !== 'playbook_options' && (
          <div className={styles.messageActions}>
            <Button
              appearance="subtle"
              size="small"
              icon={React.createElement(ArrowExportRegular)}
              onClick={() => onInsert(structuredInsertContent)}
              title="Insert into editor"
            >
              Insert
            </Button>
          </div>
        )}
      </div>
    );
  }

  // ── Plain text rendering (with markdown for assistant messages) ──────────────

  // Insert button: only for completed (non-streaming) assistant messages with content.
  // The button is NOT rendered for user messages (spec-2D: "Insert button MUST only
  // appear on AI response messages, not user messages").
  const showInsertButton = isAssistant && !isStreaming && !!message.content && !!onInsert;

  return (
    <div className={containerClass} role="listitem" aria-label={`${message.role} message`}>
      {markdownHtml ? (
        <div className={styles.markdownContent} dangerouslySetInnerHTML={{ __html: markdownHtml }} />
      ) : (
        <Text className={styles.messageContent}>{renderedContent}</Text>
      )}

      {isStreaming && !message.content && (
        <div className={styles.streamingIndicator}>
          <Spinner size="tiny" />
          <Text size={200}>Thinking...</Text>
        </div>
      )}

      {message.timestamp && !isStreaming && (
        <span className={timestampClass}>{formatTimestamp(message.timestamp)}</span>
      )}

      {/* DEF-12: compose-edit confirmation controls (plain-text branch — when responseType is absent). */}
      {composeEditControlsNode}

      {showInsertButton && (
        <div className={styles.messageActions}>
          <Button
            appearance="subtle"
            size="small"
            icon={React.createElement(ArrowExportRegular)}
            onClick={() => onInsert!(message.content)}
            title="Insert into editor"
          >
            Insert
          </Button>
        </div>
      )}
    </div>
  );
};

export default SprkChatMessage;
