/**
 * SprkChatCitationPopover - Citation superscript marker + popover
 *
 * Two sub-components:
 *
 * 1. **CitationMarker** - Inline clickable superscript [N] rendered in
 *    brand color. Triggers the popover on click.
 *
 * 2. **SprkChatCitationPopover** - Fluent UI v9 Popover showing source
 *    name, page, excerpt (truncated to 200 chars), and an "Open Source"
 *    link. Dismisses on click-outside or Escape.
 *
 * Supports two citation source types:
 * - **document** (default) — internal SPE file reference with page + excerpt
 * - **web** — external web search result with title, clickable URL, snippet,
 *   and an "[External Source]" badge (ADR-015)
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - Data governance: mark external sources
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Text,
  Link,
  Badge,
} from '@fluentui/react-components';
import { Open16Regular, Globe16Regular, Document16Regular } from '@fluentui/react-icons';
import { ICitation, ICitationMarkerProps, ISprkChatCitationPopoverProps } from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Maximum excerpt length before truncation. */
const MAX_EXCERPT_LENGTH = 200;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useMarkerStyles = makeStyles({
  marker: {
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    verticalAlign: 'super',
    lineHeight: '1',
    textDecorationLine: 'none',
    ...shorthands.padding('0px', '2px'),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    ':hover': {
      color: tokens.colorBrandForeground2,
      backgroundColor: tokens.colorNeutralBackground1Hover,
      textDecorationLine: 'underline',
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '1px',
    },
  },
});

const usePopoverStyles = makeStyles({
  surface: {
    maxWidth: '320px',
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
  documentIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  webIcon: {
    color: tokens.colorPaletteBerryForeground1,
    flexShrink: 0,
  },
  sourceName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  pageInfo: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  excerpt: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },
  snippet: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    fontStyle: 'italic',
  },
  linkRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
  urlText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '250px',
  },
  badgeRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Truncate text to `maxLen` characters with ellipsis.
 */
function truncateExcerpt(text: string, maxLen: number): string {
  if (text.length <= maxLen) {
    return text;
  }
  return text.slice(0, maxLen - 1).trimEnd() + '\u2026';
}

/**
 * Determine if a citation is a web source.
 * Defaults to 'document' when sourceType is absent for backward compatibility.
 */
function isWebCitation(citation: ICitation): boolean {
  return citation.sourceType === 'web';
}

// ─────────────────────────────────────────────────────────────────────────────
// CitationMarker
// ─────────────────────────────────────────────────────────────────────────────

/**
 * CitationMarker - Inline clickable superscript [N] for citations.
 *
 * Designed to be embedded inside message text. Renders as a brand-colored
 * superscript that opens the citation popover on click.
 *
 * Keyboard accessible: Tab to focus, Enter or Space to activate.
 *
 * @example
 * ```tsx
 * <CitationMarker citation={citation} />
 * ```
 */
export const CitationMarker: React.FC<ICitationMarkerProps> = ({ citation }) => {
  const styles = useMarkerStyles();
  const isWeb = isWebCitation(citation);

  return (
    <Popover positioning="below" withArrow trapFocus>
      <PopoverTrigger disableButtonEnhancement>
        <span
          className={styles.marker}
          role="button"
          tabIndex={0}
          aria-label={`Citation ${citation.id}, ${isWeb ? 'web' : 'document'} source: ${citation.source}`}
          data-testid={`citation-marker-${citation.id}`}
        >
          [{citation.id}]
        </span>
      </PopoverTrigger>

      <CitationPopoverContent citation={citation} />
    </Popover>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// CitationPopoverContent (internal)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Internal popover surface content. Extracted so that both CitationMarker
 * (self-contained) and SprkChatCitationPopover (controlled) can reuse it.
 *
 * Renders differently based on citation sourceType:
 * - 'document' (default): source name, page, excerpt, optional open link
 * - 'web': globe icon, source title, [External Source] badge, snippet, clickable URL
 */
const CitationPopoverContent: React.FC<{ citation: ICitation }> = ({ citation }) => {
  const styles = usePopoverStyles();
  const isWeb = isWebCitation(citation);

  if (isWeb) {
    return (
      <PopoverSurface
        className={styles.surface}
        aria-label={`Web citation details for source: ${citation.source}`}
        data-testid={`citation-popover-${citation.id}`}
      >
        {/* Header: Globe icon + source title */}
        <div className={styles.headerRow}>
          <Globe16Regular className={styles.webIcon} />
          <Text className={styles.sourceName}>{citation.source}</Text>
        </div>

        {/* External Source badge (ADR-015) */}
        <div className={styles.badgeRow}>
          <Badge
            appearance="tint"
            color="important"
            size="small"
            data-testid={`citation-external-badge-${citation.id}`}
          >
            External Source
          </Badge>
        </div>

        {/* Snippet (web search result preview) */}
        {citation.snippet && (
          <Text className={styles.snippet}>
            {truncateExcerpt(citation.snippet, MAX_EXCERPT_LENGTH)}
          </Text>
        )}

        {/* Clickable URL (opens in new tab — constraint from spec) */}
        {citation.url && (
          <div className={styles.linkRow}>
            <Open16Regular />
            <Link
              href={citation.url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open web source: ${citation.source}`}
              data-testid={`citation-link-${citation.id}`}
            >
              Visit Source
            </Link>
          </div>
        )}

        {/* URL preview text */}
        {citation.url && (
          <Text className={styles.urlText} title={citation.url}>
            {citation.url}
          </Text>
        )}
      </PopoverSurface>
    );
  }

  // ── Document citation (default / backward-compatible) ──────────────────────

  const excerptText = truncateExcerpt(citation.excerpt, MAX_EXCERPT_LENGTH);

  return (
    <PopoverSurface
      className={styles.surface}
      aria-label={`Citation details for source: ${citation.source}`}
      data-testid={`citation-popover-${citation.id}`}
    >
      {/* Header: Document icon + source name */}
      <div className={styles.headerRow}>
        <Document16Regular className={styles.documentIcon} />
        <Text className={styles.sourceName}>{citation.source}</Text>
      </div>

      {/* Page number (optional) */}
      {citation.page !== undefined && <Text className={styles.pageInfo}>Page {citation.page}</Text>}

      {/* Excerpt */}
      <Text className={styles.excerpt}>{excerptText}</Text>

      {/* Open Source link (optional) */}
      {citation.sourceUrl && (
        <div className={styles.linkRow}>
          <Open16Regular />
          <Link
            href={citation.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`Open source: ${citation.source}`}
            data-testid={`citation-link-${citation.id}`}
          >
            Open Source
          </Link>
        </div>
      )}
    </PopoverSurface>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// SprkChatCitationPopover
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatCitationPopover - Controlled popover for citation details.
 *
 * Use this when you need explicit open/close control (e.g., the parent
 * manages popover state). For the simpler self-contained version, use
 * `CitationMarker` which wraps its own Popover.
 *
 * Supports both document and web citation types — the popover content
 * adapts automatically based on `citation.sourceType`.
 *
 * @example
 * ```tsx
 * const [open, setOpen] = React.useState(false);
 *
 * <SprkChatCitationPopover
 *   citation={citation}
 *   open={open}
 *   onOpenChange={setOpen}
 * >
 *   <span onClick={() => setOpen(true)}>[1]</span>
 * </SprkChatCitationPopover>
 * ```
 */
export const SprkChatCitationPopover: React.FC<ISprkChatCitationPopoverProps> = ({
  citation,
  open,
  onOpenChange,
  children,
}) => {
  const handleOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (onOpenChange) {
        onOpenChange(data.open);
      }
    },
    [onOpenChange]
  );

  return (
    <Popover positioning="below" withArrow trapFocus open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger disableButtonEnhancement>{children}</PopoverTrigger>

      <CitationPopoverContent citation={citation} />
    </Popover>
  );
};

export default SprkChatCitationPopover;
