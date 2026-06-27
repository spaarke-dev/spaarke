/**
 * SprkChatMessageRenderer - Structured response card renderer for SprkChat
 *
 * Selects the appropriate card renderer based on `responseType` and renders
 * structured response data. Supports five card types:
 * - markdown (default): rendered as formatted text
 * - citations: text with clickable citation references + source list
 * - diff: summary with "Open in Diff Viewer" action button
 * - entity_card: Dataverse record link with key fields and navigation
 * - action_confirmation: completed action summary with status badge
 *
 * Navigation for entity cards is handled via the onNavigate callback —
 * this component MUST NOT call Xrm.Navigation directly (ADR-012).
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Button,
  Badge,
  Card,
  CardHeader,
  CardPreview,
  Link,
} from '@fluentui/react-components';
import {
  DocumentRegular,
  GlobeRegular,
  OpenRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  RecordRegular,
} from '@fluentui/react-icons';
import type { CitationSourceType, IPlaybookOptionCandidate } from './types';
import { renderMarkdown as renderMarkdownHtml, SPRK_MARKDOWN_CSS } from '../../services/renderMarkdown';

// ─────────────────────────────────────────────────────────────────────────────
// Markdown CSS injection (one-time, idempotent)
// ─────────────────────────────────────────────────────────────────────────────

const SPRK_MARKDOWN_STYLE_ID = 'sprk-markdown-styles';

/**
 * Injects the SPRK_MARKDOWN_CSS stylesheet into the document <head> once.
 * Uses an idempotent check via a data attribute to avoid duplicate injection
 * when multiple instances of the component mount.
 */
function ensureMarkdownCssInjected(): void {
  if (typeof document === 'undefined') return;
  if (document.getElementById(SPRK_MARKDOWN_STYLE_ID)) return;

  const style = document.createElement('style');
  style.id = SPRK_MARKDOWN_STYLE_ID;
  style.textContent = SPRK_MARKDOWN_CSS;
  document.head.appendChild(style);
}

// ─────────────────────────────────────────────────────────────────────────────
// Structured Response Data Types
// ─────────────────────────────────────────────────────────────────────────────

/** A single citation reference within a response. */
export interface ICitationRef {
  /** 1-based index matching [N] markers in response text */
  index: number;
  /** Display title of the source document or web page */
  title: string;
  /** Dataverse document GUID for linking (optional for web citations) */
  documentId?: string;
  /**
   * Citation source type discriminator.
   * - 'document' (default) — internal SPE document reference
   * - 'web' — external web search result
   * When absent, defaults to 'document' for backward compatibility.
   */
  sourceType?: CitationSourceType;
  /** Full URL of the web search result. Present when sourceType is 'web'. */
  url?: string;
}

/** Response data for markdown card type. */
export interface IMarkdownResponse {
  /** The text content (plain text or simple markdown) */
  text: string;
}

/** Response data for citations card type. */
export interface ICitationsResponse {
  /** The AI response text with [N] citation markers */
  text: string;
  /** Ordered list of citation references */
  citations: ICitationRef[];
}

/** Response data for diff card type. */
export interface IDiffResponse {
  /** Human-readable summary of proposed changes */
  summary: string;
  /** The proposed revised text passed to onOpenDiff */
  proposedText: string;
}

/** A key-value field on an entity card. */
export interface IEntityCardField {
  /** Field label for display */
  label: string;
  /** Field value string */
  value: string;
}

/** Response data for entity_card card type. */
export interface IEntityCardResponse {
  /** Display name of the entity record */
  entityName: string;
  /** Entity logical name (e.g., "matter", "contact") */
  entityType: string;
  /** GUID of the entity record */
  entityId: string;
  /** Optional ordered list of key fields to display */
  fields?: IEntityCardField[];
}

/** Response data for action_confirmation card type. */
export interface IActionConfirmationResponse {
  /** Name/label of the action that was completed */
  actionName: string;
  /** Whether the action succeeded or failed */
  status: 'success' | 'failure';
  /** Human-readable summary of what was done */
  summary: string;
}

/**
 * Response data for playbook_options card type (chat-routing-redesign-r1
 * task 117b — FR-50 + FR-51). Mirrors the BFF SSE payload shape verbatim.
 *
 * Click handlers are passed as separate props on the renderer (not embedded in
 * data) so the data shape remains JSON-serializable + maps 1:1 to the SSE wire
 * format.
 */
export interface IPlaybookOptionsResponse {
  /** Top-N candidates (highest confidence first). May be empty (no-match case). */
  candidates: IPlaybookOptionCandidate[];
  /** When true, render the "Open Library" link alongside (or in place of) candidates. */
  libraryModalCta: boolean;
  /** Opaque session attachment IDs forwarded to the click handlers. */
  sessionAttachmentIds: string[];
  /** Whether the upstream reranker ran. Surfaced only for diagnostics; not displayed. */
  rerankInvoked?: boolean;
  /** Controlled-vocabulary tag (`top-confidence`, `llm-rerank-from-5`, etc.). Not displayed. */
  rerankReason?: string | null;
}

/** Union of all structured response data types. */
export type StructuredResponseData =
  | IMarkdownResponse
  | ICitationsResponse
  | IDiffResponse
  | IEntityCardResponse
  | IActionConfirmationResponse
  | IPlaybookOptionsResponse;

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

/** Props for the SprkChatMessageRenderer component. */
export interface ISprkChatMessageRendererProps {
  /** Discriminates which card renderer to use */
  responseType: 'markdown' | 'citations' | 'diff' | 'entity_card' | 'action_confirmation' | 'playbook_options' | string;
  /** Structured data for the selected card renderer */
  data: StructuredResponseData;
  /**
   * Callback for entity card navigation.
   * MUST NOT call Xrm.Navigation directly — delegate to Code Page layer.
   */
  onNavigate?: (entityType: string, entityId: string) => void;
  /** Callback for diff card — receives the proposed text to open in diff viewer */
  onOpenDiff?: (proposedText: string) => void;

  /**
   * chat-routing-redesign-r1 task 117b (FR-50). Called when the user clicks a
   * candidate-playbook link button on a `playbook_options` card.
   * When omitted, buttons render disabled (defensive UX).
   */
  onSelectPlaybook?: (playbookId: string, sessionAttachmentIds: string[]) => void;
  /**
   * chat-routing-redesign-r1 task 117b (FR-51). Called when the user clicks the
   * "Open Library" link on a `playbook_options` card.
   * When omitted, link renders disabled (defensive UX).
   */
  onOpenLibraryModal?: (sessionAttachmentIds: string[]) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // ── Markdown renderer ────────────────────────────────────────────────────
  markdownRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  markdownText: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },

  // ── Citations renderer ────────────────────────────────────────────────────
  citationsRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  citationsText: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  citationsSuper: {
    fontSize: tokens.fontSizeBase100,
    verticalAlign: 'super',
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    textDecoration: 'none',
    fontWeight: tokens.fontWeightSemibold,
    ':hover': {
      textDecoration: 'underline',
    },
  },
  sourcesList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    ...shorthands.padding(tokens.spacingVerticalXS, 0, 0, 0),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
  },
  sourcesHeading: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    marginBottom: tokens.spacingVerticalXXS,
  },
  sourceItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  sourceIcon: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  webSourceIcon: {
    color: tokens.colorPaletteBerryForeground1,
    flexShrink: 0,
  },

  // ── Diff renderer ─────────────────────────────────────────────────────────
  diffRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  diffSummaryText: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },
  diffActions: {
    display: 'flex',
    justifyContent: 'flex-end',
  },

  // ── Entity card renderer ───────────────────────────────────────────────────
  entityCardRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  entityTypeBadge: {
    alignSelf: 'flex-start',
  },
  entityFieldsGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    marginTop: tokens.spacingVerticalXS,
  },
  entityField: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  entityFieldLabel: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    fontWeight: tokens.fontWeightSemibold,
  },
  entityFieldValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  entityCardActions: {
    display: 'flex',
    justifyContent: 'flex-end',
    marginTop: tokens.spacingVerticalXS,
  },

  // ── Action confirmation renderer ──────────────────────────────────────────
  confirmationRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  confirmationHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  confirmationActionName: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  confirmationSummary: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground2,
  },
  successIcon: {
    color: tokens.colorStatusSuccessForeground1,
  },
  failureIcon: {
    color: tokens.colorStatusDangerForeground1,
  },

  // ── Playbook options renderer (chat-routing-redesign-r1 task 117b) ──────────
  // Inline link-button card — NOT a "card" surface per user direction.
  // Fluent v9 semantic tokens only (ADR-021 dark-mode compliance).
  playbookOptionsRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  playbookOptionsPrompt: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },
  playbookOptionsButtons: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
  },
  playbookOptionsLibraryRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  playbookOptionsLink: {
    cursor: 'pointer',
  },
  playbookOptionsEmpty: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Citation marker regex (matches [1], [2], [12], etc.)
// ─────────────────────────────────────────────────────────────────────────────

const _CITATION_MARKER_REGEX = /\[(\d+)\]/g;

// ─────────────────────────────────────────────────────────────────────────────
// Sub-renderers
// ─────────────────────────────────────────────────────────────────────────────

function renderMarkdownCard(data: IMarkdownResponse, styles: ReturnType<typeof useStyles>): React.ReactElement {
  if (!data.text) {
    return React.createElement('div', { className: styles.markdownRoot });
  }

  const html = renderMarkdownHtml(data.text);
  return (
    <div className={styles.markdownRoot}>
      <div className={styles.markdownText} dangerouslySetInnerHTML={{ __html: html }} />
    </div>
  );
}

function renderCitations(data: ICitationsResponse, styles: ReturnType<typeof useStyles>): React.ReactElement {
  // Build index map for O(1) lookup
  const citationMap = new Map<number, ICitationRef>();
  for (const c of data.citations) {
    citationMap.set(c.index, c);
  }

  // Render the text body as markdown, then inject superscript citation links.
  // Citation markers [N] survive markdown parsing (they are not consumed by marked).
  // After rendering to HTML, we replace [N] with styled <sup> elements.
  const renderCitationsHtml = (text: string): string => {
    if (!text) return '';
    let html = renderMarkdownHtml(text);
    // Replace [N] markers in the rendered HTML with superscript citation links
    html = html.replace(/\[(\d+)\]/g, (_match, num) => {
      const citationIndex = parseInt(num, 10);
      const citation = citationMap.get(citationIndex);
      if (citation) {
        return `<sup class="${styles.citationsSuper}">[${citationIndex}]</sup>`;
      }
      return _match;
    });
    return html;
  };

  return (
    <div className={styles.citationsRoot}>
      <div className={styles.citationsText} dangerouslySetInnerHTML={{ __html: renderCitationsHtml(data.text) }} />
      {data.citations.length > 0 && (
        <div className={styles.sourcesList}>
          <div className={styles.sourcesHeading}>Sources</div>
          {data.citations.map(c => {
            const isWeb = c.sourceType === 'web';
            const key = isWeb ? `web-${c.index}` : (c.documentId ?? `doc-${c.index}`);

            return (
              <div key={key} className={styles.sourceItem}>
                {isWeb ? (
                  <GlobeRegular className={styles.webSourceIcon} />
                ) : (
                  <DocumentRegular className={styles.sourceIcon} />
                )}
                <span>
                  [{c.index}]&nbsp;
                  {isWeb && c.url ? (
                    <Link href={c.url} target="_blank" rel="noopener noreferrer" title={c.title}>
                      {c.title}
                    </Link>
                  ) : (
                    <Link href={`#doc-${c.documentId}`} title={c.title}>
                      {c.title}
                    </Link>
                  )}
                </span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function renderDiff(
  data: IDiffResponse,
  styles: ReturnType<typeof useStyles>,
  onOpenDiff?: (proposedText: string) => void
): React.ReactElement {
  const diffHtml = data.summary ? renderMarkdownHtml(data.summary) : '';

  return (
    <div className={styles.diffRoot}>
      {diffHtml ? (
        <div className={styles.diffSummaryText} dangerouslySetInnerHTML={{ __html: diffHtml }} />
      ) : (
        <Text className={styles.diffSummaryText}>{data.summary}</Text>
      )}
      <div className={styles.diffActions}>
        <Button
          appearance="outline"
          size="small"
          icon={React.createElement(OpenRegular)}
          onClick={() => onOpenDiff?.(data.proposedText)}
          disabled={!onOpenDiff}
        >
          Open in Diff Viewer
        </Button>
      </div>
    </div>
  );
}

function renderEntityCard(
  data: IEntityCardResponse,
  styles: ReturnType<typeof useStyles>,
  onNavigate?: (entityType: string, entityId: string) => void
): React.ReactElement {
  return (
    <Card>
      <CardHeader
        image={React.createElement(RecordRegular, { style: { fontSize: 20, color: tokens.colorBrandForeground1 } })}
        header={
          <Text weight="semibold" size={300}>
            {data.entityName}
          </Text>
        }
        description={
          <Badge className={styles.entityTypeBadge} appearance="tint" color="brand" size="small">
            {data.entityType}
          </Badge>
        }
      />
      {data.fields && data.fields.length > 0 && (
        <CardPreview>
          <div className={styles.entityFieldsGrid}>
            {data.fields.map(field => (
              <div key={field.label} className={styles.entityField}>
                <span className={styles.entityFieldLabel}>{field.label}</span>
                <span className={styles.entityFieldValue}>{field.value}</span>
              </div>
            ))}
          </div>
        </CardPreview>
      )}
      <div className={styles.entityCardActions}>
        <Button
          appearance="primary"
          size="small"
          onClick={() => onNavigate?.(data.entityType, data.entityId)}
          disabled={!onNavigate}
        >
          View Record
        </Button>
      </div>
    </Card>
  );
}

/**
 * Render the `playbook_options` card (chat-routing-redesign-r1 task 117b — FR-50 + FR-51).
 *
 * UX shape:
 *   - Intro line: "Which playbook would you like me to use?"
 *   - Inline `Button appearance="primary"` per candidate (label = displayName).
 *     Buttons are stacked horizontally and wrap on narrow chat bubbles.
 *   - "Open Library" link rendered below the buttons whenever
 *     `libraryModalCta === true`.
 *   - No "cards" surface — per user direction (chat link buttons only).
 *
 * Empty candidates (no-match graceful path): the intro changes to a short
 * "I couldn't find a confident match" message and only the library link renders.
 *
 * ADR-021 dark-mode: all colors via Fluent v9 semantic tokens.
 * ADR-015: no candidate values are logged here — display only.
 */
function renderPlaybookOptions(
  data: IPlaybookOptionsResponse,
  styles: ReturnType<typeof useStyles>,
  onSelectPlaybook?: (playbookId: string, sessionAttachmentIds: string[]) => void,
  onOpenLibraryModal?: (sessionAttachmentIds: string[]) => void
): React.ReactElement {
  const candidates = Array.isArray(data.candidates) ? data.candidates : [];
  const showLibraryLink = data.libraryModalCta !== false;
  const sessionAttachmentIds = Array.isArray(data.sessionAttachmentIds) ? data.sessionAttachmentIds : [];
  const hasCandidates = candidates.length > 0;

  return (
    <div className={styles.playbookOptionsRoot} role="group" aria-label="Suggested playbooks">
      <Text className={styles.playbookOptionsPrompt}>
        {hasCandidates
          ? 'Which playbook would you like me to use?'
          : "I couldn't find a confident match for your files. You can pick a playbook from the library."}
      </Text>

      {hasCandidates && (
        <div className={styles.playbookOptionsButtons}>
          {candidates.map((c: IPlaybookOptionCandidate) => (
            <Button
              key={c.playbookId}
              appearance="primary"
              size="small"
              disabled={!onSelectPlaybook}
              onClick={() => {
                if (onSelectPlaybook) {
                  onSelectPlaybook(c.playbookId, sessionAttachmentIds);
                }
              }}
              // Tier-1 safe — displayName is admin config, NOT user content.
              title={c.displayName}
              aria-label={`Use playbook: ${c.displayName}`}
            >
              {c.displayName}
            </Button>
          ))}
        </div>
      )}

      {showLibraryLink && (
        <div className={styles.playbookOptionsLibraryRow}>
          <Link
            as="button"
            type="button"
            className={styles.playbookOptionsLink}
            disabled={!onOpenLibraryModal}
            onClick={() => {
              if (onOpenLibraryModal) {
                onOpenLibraryModal(sessionAttachmentIds);
              }
            }}
            aria-label="Open the playbook library"
          >
            Open Library
          </Link>
        </div>
      )}

      {!hasCandidates && !showLibraryLink && (
        <Text className={styles.playbookOptionsEmpty}>No playbook options available.</Text>
      )}
    </div>
  );
}

function renderActionConfirmation(
  data: IActionConfirmationResponse,
  styles: ReturnType<typeof useStyles>
): React.ReactElement {
  const isSuccess = data.status === 'success';

  return (
    <div className={styles.confirmationRoot}>
      <div className={styles.confirmationHeader}>
        {isSuccess
          ? React.createElement(CheckmarkCircleRegular, {
              className: styles.successIcon,
              style: { fontSize: 20 },
            })
          : React.createElement(ErrorCircleRegular, {
              className: styles.failureIcon,
              style: { fontSize: 20 },
            })}
        <Text className={styles.confirmationActionName}>{data.actionName}</Text>
        <Badge appearance="tint" color={isSuccess ? 'success' : 'danger'} size="small">
          {isSuccess ? 'Completed' : 'Failed'}
        </Badge>
      </div>
      {data.summary ? (
        <div
          className={styles.confirmationSummary}
          dangerouslySetInnerHTML={{ __html: renderMarkdownHtml(data.summary) }}
        />
      ) : (
        <Text className={styles.confirmationSummary}>{data.summary}</Text>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatMessageRenderer - Renders structured AI response cards.
 *
 * Selects a card renderer based on `responseType`. Unknown types fall back to
 * the markdown renderer without errors.
 *
 * @example
 * ```tsx
 * <SprkChatMessageRenderer
 *   responseType="diff"
 *   data={{ summary: "Simplified 3 sentences", proposedText: "..." }}
 *   onOpenDiff={(text) => setDiffText(text)}
 * />
 *
 * <SprkChatMessageRenderer
 *   responseType="entity_card"
 *   data={{ entityName: "Smith v. Jones", entityType: "matter", entityId: "abc-123", fields: [] }}
 *   onNavigate={(type, id) => navigateToRecord(type, id)}
 * />
 * ```
 */
export const SprkChatMessageRenderer: React.FC<ISprkChatMessageRendererProps> = ({
  responseType,
  data,
  onNavigate,
  onOpenDiff,
  onSelectPlaybook,
  onOpenLibraryModal,
}) => {
  const styles = useStyles();

  // Inject the SPRK_MARKDOWN_CSS stylesheet once on first mount.
  // Uses an idempotent helper so multiple instances don't duplicate the <style> tag.
  React.useEffect(() => {
    ensureMarkdownCssInjected();
  }, []);

  switch (responseType) {
    case 'citations':
      return renderCitations(data as ICitationsResponse, styles);

    case 'diff':
      return renderDiff(data as IDiffResponse, styles, onOpenDiff);

    case 'entity_card':
      return renderEntityCard(data as IEntityCardResponse, styles, onNavigate);

    case 'action_confirmation':
      return renderActionConfirmation(data as IActionConfirmationResponse, styles);

    case 'playbook_options':
      // chat-routing-redesign-r1 task 117b — file-aware playbook routing card.
      return renderPlaybookOptions(data as IPlaybookOptionsResponse, styles, onSelectPlaybook, onOpenLibraryModal);

    case 'markdown':
    default:
      // Unknown responseType falls back to markdown — no error
      return renderMarkdownCard(data as IMarkdownResponse, styles);
  }
};

export default SprkChatMessageRenderer;
