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
import { ArrowExportRegular } from '@fluentui/react-icons';
import { ISprkChatMessageProps, ICitation, IDocumentStatusChatMessage } from './types';
import { CitationMarker } from './SprkChatCitationPopover';
import { SprkChatMessageRenderer } from './SprkChatMessageRenderer';
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
}) => {
  const styles = useStyles();
  const isUser = message.role === 'User';
  const isAssistant = message.role === 'Assistant';

  // ── Structured response rendering ──────────────────────────────────────────

  const responseType = message.metadata?.responseType;
  const isStructured = isAssistant && responseType != null && responseType !== '';

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
      <div
        className={styles.structuredContainer}
        role="listitem"
        aria-label="AI plan preview"
      >
        <PlanPreviewCard
          planTitle={message.metadata?.planTitle ?? 'Proposed Plan'}
          steps={planSteps}
          isExecuting={isPlanExecuting ?? false}
          onProceed={onProceed ?? (() => {
            console.log('[SprkChatMessage] onProceed stub');
          })}
          onCancel={onCancel ?? (() => {
            console.log('[SprkChatMessage] onCancel stub');
          })}
          onEditPlan={onEditPlan ?? ((editMessage) => {
            console.log('[SprkChatMessage] onEditPlan stub — edit message:', editMessage);
          })}
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
      (structuredData as { text?: string }).text ??
      (structuredData as { summary?: string }).summary ??
      message.content;

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
          onOpenDiff={(proposedText) => {
            // Dispatch open_diff event so host layer opens DiffReviewPanel
            dispatchOpenDiff(proposedText);
          }}
        />
        {onInsert && structuredInsertContent && (
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

  // Inject markdown CSS once on first mount (idempotent — shared with SprkChatMessageRenderer)
  React.useEffect(() => {
    ensureMarkdownCssInjected();
  }, []);

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

  // Render assistant messages as markdown HTML (headings, bold, code blocks, etc.)
  // User messages are rendered as plain text to avoid unexpected formatting.
  // During streaming, render as plain text to avoid re-parsing on every token.
  const markdownHtml = React.useMemo(() => {
    if (isAssistant && !isStreaming && message.content && !(citations && citations.length > 0)) {
      return renderMarkdownHtml(message.content);
    }
    return null;
  }, [message.content, isAssistant, isStreaming, citations]);

  // Insert button: only for completed (non-streaming) assistant messages with content.
  // The button is NOT rendered for user messages (spec-2D: "Insert button MUST only
  // appear on AI response messages, not user messages").
  const showInsertButton = isAssistant && !isStreaming && !!message.content && !!onInsert;

  return (
    <div className={containerClass} role="listitem" aria-label={`${message.role} message`}>
      {markdownHtml ? (
        <div
          className={styles.markdownContent}
          dangerouslySetInnerHTML={{ __html: markdownHtml }}
        />
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
