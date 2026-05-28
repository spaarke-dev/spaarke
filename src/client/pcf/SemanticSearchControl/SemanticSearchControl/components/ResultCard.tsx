/**
 * ResultCard — search result card (prototype redesign, v1.1.47).
 *
 * Replaces the v1.1.46 RecordCardShell-based 2-row layout with the
 * card-grid prototype (`projects/spaarke-matter-ui-enhancement-r1/screenshots/documents-section-2.png`).
 *
 * Structure (top → bottom):
 *
 *   ┌────────────────────────────────────┐
 *   │ [82%]              ┊         [⋯] │  ← preview placeholder
 *   │            ┊                      │     - hatched grey background
 *   │           [📄]                    │     - top-L: tier-colored score badge
 *   │            ┊                      │     - top-R: 3-dot menu trigger
 *   │            ┊                      │     - center: large file-type icon
 *   ├────────────────────────────────────┤
 *   │ 📄  Engagement Letter.docx        │  ← info area (white)
 *   │                                    │     - small icon + 2-line name + ellipsis
 *   │ May 6, 2026 · 215 KB              │     - date · size meta line
 *   └────────────────────────────────────┘
 *
 * Interaction:
 *   - Click anywhere on the card EXCEPT the 3-dot menu trigger or the
 *     sparkle (AI summary) icon → opens the preview dialog.
 *   - 3-dot menu retains the full FR-DOC-01 13-action set, dispatched via
 *     the shared DocumentRowMenu (deep-path import per Lexical/React 16
 *     barrel constraint — same rationale as v1.1.46).
 *
 * Match-score tier colors (Fluent v9 semantic tokens only):
 *   - ≥80%   green     (colorPaletteGreenBackground2 / colorPaletteGreenForeground2)
 *   - 60-79% marigold  (colorPaletteMarigoldBackground2 / colorPaletteMarigoldForeground2)
 *   - <60%   red       (colorPaletteRedBackground2 / colorPaletteRedForeground2)
 *
 * @see ADR-012 - Shared component library (DocumentRowMenu, AiSummaryPopover)
 * @see ADR-021 - Fluent UI v9 requirements (tokens only, dark-mode safe)
 * @see ADR-022 - React 16/17 compatible (no React 18-only APIs)
 * @see spec.md FR-DOC-01 - 3-dot menu consolidation
 * @see projects/spaarke-matter-ui-enhancement-r1/screenshots/documents-section-2.png — visual contract
 */

import * as React from 'react';
import { useCallback, useRef, useState } from 'react';
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  Text,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import {
  Document20Regular,
  Document48Regular,
  DocumentPdf20Regular,
  DocumentPdf32Regular,
  DocumentText20Regular,
  DocumentText48Regular,
  TableRegular,
  SlideTextRegular,
  ImageRegular,
  MailRegular,
  Sparkle20Regular,
} from '@fluentui/react-icons';
import { AiSummaryPopover } from '@spaarke/ui-components/dist/components/AiSummaryPopover';
// Deep-path import (not the barrel) — the barrel pulls in RichTextEditor →
// `@lexical/react` ESM modules that don't resolve `react/jsx-runtime` under
// React 16's resolution (PCF target per ADR-022). Matches the existing
// pattern used in v1.1.46 for `RecordCardShell` and `AiSummaryPopover`.
import {
  DocumentRowMenu,
  type DocumentRowAction,
  type IDocumentRowMenuTarget,
} from '@spaarke/ui-components/dist/components/DocumentRowMenu';
import { IResultCardProps } from '../types';
import { FilePreviewDialog } from './FilePreviewDialog';

// ---------------------------------------------------------------------------
// File-type icon mapping
//
// Two sizes: a "preview-hero" (center of the hatched area, ~32-48 px) and a
// "row-icon" (small, beside the filename, 20 px). PDF tops out at 32 in
// @fluentui/react-icons; Word/Text/Generic have a 48 variant. We accept the
// minor size delta (32 vs 48) — visually balanced once tinted.
// ---------------------------------------------------------------------------

type IconComponent = typeof Document20Regular;

type FileIconKind = 'pdf' | 'word' | 'spreadsheet' | 'slide' | 'image' | 'mail' | 'default';

function classifyFileType(fileType: string): FileIconKind {
  const ext = fileType?.toLowerCase().trim() ?? '';
  switch (ext) {
    case 'pdf':
      return 'pdf';
    case 'doc':
    case 'docx':
    case 'rtf':
    case 'odt':
    case 'txt':
      return 'word';
    case 'xls':
    case 'xlsx':
    case 'csv':
      return 'spreadsheet';
    case 'ppt':
    case 'pptx':
      return 'slide';
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'bmp':
    case 'svg':
      return 'image';
    case 'msg':
    case 'eml':
      return 'mail';
    default:
      return 'default';
  }
}

function getRowIcon(kind: FileIconKind): IconComponent {
  switch (kind) {
    case 'pdf':
      return DocumentPdf20Regular;
    case 'word':
      return DocumentText20Regular;
    case 'spreadsheet':
      return TableRegular;
    case 'slide':
      return SlideTextRegular;
    case 'image':
      return ImageRegular;
    case 'mail':
      return MailRegular;
    default:
      return Document20Regular;
  }
}

function getHeroIcon(kind: FileIconKind): IconComponent {
  switch (kind) {
    case 'pdf':
      // PDF's largest variant is 32 — visually compensated by red tinting
      // (see heroColor() below) so it still reads as the dominant element.
      return DocumentPdf32Regular;
    case 'word':
      return DocumentText48Regular;
    case 'spreadsheet':
    case 'slide':
    case 'image':
    case 'mail':
      // These icons don't have a 48 variant in @fluentui/react-icons; fall
      // back to a generic Document48 in the hero slot so the card still has
      // a properly-sized visual. The small row-icon (getRowIcon) still
      // carries the type-specific glyph.
      return Document48Regular;
    default:
      return Document48Regular;
  }
}

function heroColor(kind: FileIconKind): string {
  // Type-specific tinting for the hero icon. Tokens only (ADR-021) so dark
  // mode + Spaarke brand themes still resolve correctly.
  switch (kind) {
    case 'pdf':
      return tokens.colorPaletteRedForeground2;
    case 'word':
      return tokens.colorBrandForeground1;
    default:
      return tokens.colorNeutralForeground3;
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatShortDate(dateString: string | null): string {
  if (!dateString) return '';
  try {
    const d = new Date(dateString);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return '';
  }
}

type ScoreTier = 'high' | 'mid' | 'low';

function tierFromScore(score: number): ScoreTier {
  // `score` is the canonical combinedScore in [0, 1]; round to a whole-percent
  // tier (matches the badge text shown to the user — no off-by-one drift).
  const pct = Math.round(score * 100);
  if (pct >= 80) return 'high';
  if (pct >= 60) return 'mid';
  return 'low';
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.overflow('hidden'),
    cursor: 'pointer',
    // Smooth hover/focus surface elevation — token-driven shadows keep this
    // dark-mode safe.
    transitionProperty: 'box-shadow, border-color',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':hover': {
      boxShadow: tokens.shadow4,
      ...shorthands.borderColor(tokens.colorNeutralStroke1),
    },
    ':focus-within': {
      ...shorthands.borderColor(tokens.colorBrandStroke1),
      boxShadow: tokens.shadow4,
    },
  },
  // Top "preview placeholder" half — hatched grey via a repeating gradient.
  // Token-only colors keep dark mode + brand themes consistent.
  preview: {
    position: 'relative',
    minHeight: '120px',
    height: '120px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundImage: `repeating-linear-gradient(45deg, ${tokens.colorNeutralBackground2}, ${tokens.colorNeutralBackground2} 8px, ${tokens.colorNeutralBackground3} 8px, ${tokens.colorNeutralBackground3} 9px)`,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  badgeWrap: {
    position: 'absolute',
    top: tokens.spacingVerticalS,
    left: tokens.spacingHorizontalS,
  },
  toolsWrap: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalXS,
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  // The hero icon sits centered in the preview placeholder. Its color is set
  // inline (heroColor() above) so the same component supports type tinting.
  heroIcon: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  // Bottom "info" half — white surface with name + meta. Slightly taller
  // than the preview to comfortably fit a 2-line ellipsised name + meta row.
  info: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    minHeight: '88px',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
  nameRow: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  nameIcon: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground2,
    marginTop: '2px',
  },
  // 2-line ellipsised name — `-webkit-line-clamp` is widely supported in the
  // browsers Power Platform targets; falls back to single-line ellipsis in
  // legacy renderers (still readable).
  name: {
    minWidth: 0,
    flex: 1,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical',
    lineHeight: tokens.lineHeightBase300,
    wordBreak: 'break-word',
  },
  meta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // ── Badge tiers — each gets a token-driven bg + fg pair (ADR-021). ────
  badgeBase: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    paddingTop: '1px',
    paddingBottom: '1px',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase200,
    whiteSpace: 'nowrap',
  },
  badgeHigh: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  badgeMid: {
    backgroundColor: tokens.colorPaletteMarigoldBackground2,
    color: tokens.colorPaletteMarigoldForeground2,
  },
  badgeLow: {
    backgroundColor: tokens.colorPaletteRedBackground2,
    color: tokens.colorPaletteRedForeground2,
  },
  // Tools (sparkle + menu) — small subtle icon buttons. Tighter padding than
  // the default so they don't dominate the corner of the card.
  toolButton: {
    minWidth: 'auto',
    ...shorthands.padding('2px'),
  },
});

// ---------------------------------------------------------------------------
// Score badge
// ---------------------------------------------------------------------------

const ScoreBadge: React.FC<{ score: number; className: string; tierClassName: string }> = ({
  score,
  className,
  tierClassName,
}) => {
  const pct = Math.round(score * 100);
  return (
    <span
      role="img"
      aria-label={`Relevance: ${pct}%`}
      className={mergeClasses(className, tierClassName)}
    >
      {pct}%
    </span>
  );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ResultCard: React.FC<IResultCardProps> = ({
  result,
  onClick,
  onOpenFile,
  onOpenRecord,
  onFindSimilar,
  onPreview,
  onSummary,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  // compactMode reserved for future grid-density variants — unused in v1.1.47.
  // Keeping the prop accepted so callers don't need a shape change.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  compactMode,
}) => {
  const styles = useStyles();
  const [previewOpen, setPreviewOpen] = useState(false);
  const sparkleTriggerRef = useRef<HTMLButtonElement | null>(null);

  const fileKind = classifyFileType(result.fileType);
  const RowIcon = getRowIcon(fileKind);
  const HeroIcon = getHeroIcon(fileKind);
  const tier = tierFromScore(result.combinedScore);
  const tierClassName =
    tier === 'high' ? styles.badgeHigh : tier === 'mid' ? styles.badgeMid : styles.badgeLow;

  const handleCardClick = useCallback(
    (ev: React.MouseEvent) => {
      // Existing guard: clicks bubbling from any button (sparkle, menu trigger,
      // menu item buttons) MUST NOT open the preview dialog. DocumentRowMenu
      // trigger also calls stopPropagation internally — defense-in-depth here.
      if ((ev.target as HTMLElement).closest('button')) return;
      setPreviewOpen(true);
      // Mirror v1.1.46 behavior: the parent's `onClick` writes the selectedDocumentId
      // output so other surfaces (parent form, downstream selections) can react.
      onClick();
    },
    [onClick]
  );

  const handleCardKeyDown = useCallback(
    (ev: React.KeyboardEvent) => {
      // Open preview on Enter/Space when the card itself has focus (not a
      // nested button). Matches v1.1.46's RecordCardShell keyboard contract.
      if (ev.key !== 'Enter' && ev.key !== ' ') return;
      if ((ev.target as HTMLElement).closest('button')) return;
      ev.preventDefault();
      setPreviewOpen(true);
      onClick();
    },
    [onClick]
  );

  const handleOpenRecord = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

  // -------------------------------------------------------------------------
  // 3-dot menu dispatch — mirrors v1.1.46. AI summary still wires through the
  // sparkle popover ref so the menu and hover paths share one popover surface.
  // -------------------------------------------------------------------------
  const target = React.useMemo<IDocumentRowMenuTarget>(
    () => ({
      id: result.documentId,
      name: result.name,
      documentType: result.documentType,
    }),
    [result.documentId, result.name, result.documentType]
  );

  const handleAiSummaryFromMenu = useCallback(() => {
    sparkleTriggerRef.current?.click();
  }, []);

  const handleRowAction = useCallback(
    (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          setPreviewOpen(true);
          return;
        case 'aiSummary':
          handleAiSummaryFromMenu();
          return;
        case 'openFile':
          onOpenFile('desktop');
          return;
        case 'findSimilar':
          onFindSimilar();
          return;
        case 'download':
          // Download = open in desktop app (existing platform convention).
          onOpenFile('desktop');
          return;
        case 'copyLink':
          onCopyLink();
          return;
        case 'email':
          onEmailDocument();
          return;
        case 'openRecord':
          onOpenRecord(false);
          return;
        case 'toggleWorkspace':
          onToggleWorkspace();
          return;
        case 'pinToTop':
        case 'rename':
        case 'delete':
          // Not yet wired in the PCF card surface (Phase 4 follow-on tasks).
          return;
        default: {
          // Exhaustiveness check — any new DocumentRowAction must be handled
          // here at compile time.
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [
      handleAiSummaryFromMenu,
      onOpenFile,
      onFindSimilar,
      onCopyLink,
      onEmailDocument,
      onOpenRecord,
      onToggleWorkspace,
    ]
  );

  const formattedDate = formatShortDate(result.modifiedAt ?? result.createdAt);

  // Meta line — date only for now. The SearchResult shape does not currently
  // expose a file size; the prototype's "215 KB" portion is intentionally
  // omitted until FR-BFF-01 surfaces a size field (or until the upstream
  // search projection adds it). Date alone still satisfies the prototype's
  // information density goal.
  const metaLine = formattedDate;

  const ariaLabel = [result.name, result.documentType, formattedDate ? `Modified: ${formattedDate}` : '']
    .filter(Boolean)
    .join(', ');

  return (
    <>
      <div
        className={styles.card}
        role="button"
        tabIndex={0}
        aria-label={ariaLabel}
        onClick={handleCardClick}
        onKeyDown={handleCardKeyDown}
      >
        {/* ─── Top preview placeholder ─────────────────────────────────── */}
        <div className={styles.preview} aria-hidden="true">
          <div className={styles.badgeWrap}>
            <ScoreBadge
              score={result.combinedScore}
              className={styles.badgeBase}
              tierClassName={tierClassName}
            />
          </div>
          <div className={styles.toolsWrap}>
            {/* AiSummaryPopover sparkle is RETAINED per FR-DOC-01 Owner
                Clarification — hover quick-glance + menu item for keyboard
                access. The sparkle button is also the ref target for the
                menu's "AI summary" item (programmatic click). */}
            <AiSummaryPopover
              onFetchSummary={onSummary}
              trigger={
                <Tooltip content="AI Summary" relationship="label">
                  <Button
                    ref={sparkleTriggerRef}
                    appearance="subtle"
                    size="small"
                    className={styles.toolButton}
                    icon={<Sparkle20Regular aria-hidden="true" />}
                    aria-label="AI Summary"
                  />
                </Tooltip>
              }
            />
            {/* 3-dot menu — DocumentRowMenu handles its own stopPropagation
                internally so card-click doesn't fire when interacting with
                the menu's trigger or items. */}
            <DocumentRowMenu document={target} onAction={handleRowAction} />
          </div>
          <div
            className={styles.heroIcon}
            style={{ color: heroColor(fileKind) }}
          >
            <HeroIcon aria-label={result.fileType || 'Document'} />
          </div>
        </div>

        {/* ─── Bottom info area ────────────────────────────────────────── */}
        <div className={styles.info}>
          <div className={styles.nameRow}>
            <RowIcon
              className={styles.nameIcon}
              aria-hidden="true"
            />
            <Text as="span" size={300} className={styles.name} title={result.name}>
              {result.name}
            </Text>
          </div>
          {metaLine && (
            <Text as="span" size={200} className={styles.meta}>
              {metaLine}
            </Text>
          )}
        </div>
      </div>

      <FilePreviewDialog
        open={previewOpen}
        documentName={result.name}
        documentId={result.documentId}
        documentType={result.documentType}
        onClose={() => setPreviewOpen(false)}
        fetchPreviewUrl={onPreview}
        onOpenFile={onOpenFile}
        onOpenRecord={handleOpenRecord}
        onEmailDocument={onEmailDocument}
        onCopyLink={onCopyLink}
        onToggleWorkspace={onToggleWorkspace}
        isInWorkspace={isInWorkspace}
      />
    </>
  );
};

export default ResultCard;
