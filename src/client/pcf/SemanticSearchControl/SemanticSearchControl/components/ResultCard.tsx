/**
 * ResultCard — search result card (prototype redesign, v1.1.47).
 *
 * Replaces the v1.1.46 RecordCardShell-based 2-row layout with the
 * card-grid prototype (`projects/spaarke-matter-ui-enhancement-r1/screenshots/documents-section-2.png`).
 *
 * Structure (top → bottom):
 *
 *   ┌────────────────────────────────────┐
 *   │ [☐]                          [⋯] │  ← preview placeholder
 *   │            ┊                      │     - hatched grey background
 *   │           [📄]                    │     - top-L: selection checkbox
 *   │            ┊                      │     - top-R: 3-dot menu trigger
 *   │            ┊                      │     - center: large file-type icon
 *   ├────────────────────────────────────┤
 *   │  Engagement Letter.docx           │  ← info area (white, flex-1)
 *   │                                    │     - 2-line name + ellipsis (top)
 *   │  May 6, 2026                      │     - date meta line
 *   │                                    │     - whitespace gap (flex spacer)
 *   │  100%  [Same Matter]              │     - pillRow (bottom: % chip +
 *   └────────────────────────────────────┘     Relationship pill, v1.1.54)
 *
 * Interaction:
 *   - Click anywhere on the card EXCEPT the 3-dot menu trigger → opens the
 *     preview dialog.
 *   - 3-dot menu shows the v1.1.54 (Item 6) standardized set: Preview, Open
 *     File, Find Similar, Download, Copy link, Email, Open Record, Pin to
 *     top, Delete. Hidden: AI Summary, Toggle workspace, Rename.
 *
 * Similarity chip colors (Fluent v9 semantic tokens only):
 *   - 'associated' / "100%"     → green     (matches the "Same Matter" pill)
 *   - 'semantic'   / "<pct>%"   → marigold
 *
 * @see ADR-012 - Shared component library (DocumentRowMenu, AiSummaryPopover)
 * @see ADR-021 - Fluent UI v9 requirements (tokens only, dark-mode safe)
 * @see ADR-022 - React 16/17 compatible (no React 18-only APIs)
 * @see spec.md FR-DOC-01 - 3-dot menu consolidation
 * @see projects/spaarke-matter-ui-enhancement-r1/screenshots/documents-section-2.png — visual contract
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  Text,
  Checkbox,
  Tooltip,
  Badge,
} from '@fluentui/react-components';
import { Document48Regular, DocumentPdf32Regular, DocumentText48Regular } from '@fluentui/react-icons';
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
// v1.1.53 (Item 3) — Row-icon (small, beside the filename) was removed from
// the title row, so only the hero icon (center of the hatched preview area,
// 32-48 px) remains. PDF tops out at 32 in @fluentui/react-icons; Word/Text/
// Generic have a 48 variant. We accept the minor size delta (32 vs 48) —
// visually balanced once tinted.
// ---------------------------------------------------------------------------

type IconComponent = typeof Document48Regular;

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

// v1.1.54 (Item 1) — `tierFromScore` + `ScoreTier` removed alongside the
// top-LEFT % pill. The single bottom % chip uses similaritySemantic /
// similarityAssociated (relationship-driven, not tier-driven).

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
  // v1.1.49 — Selection checkbox overlay (Item 1). Top-left of the preview
  // area, with `pointerEvents: auto` so the card's `onClick` doesn't swallow
  // the toggle. The inner Checkbox calls `stopPropagation` defensively, but
  // separating the absolute positioning here lets it overlap the badge area
  // cleanly without re-flowing other corners.
  checkboxWrap: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    left: tokens.spacingHorizontalXS,
    zIndex: 2,
  },
  // v1.1.54 (Item 1) — Top-LEFT % pill (previously rendered via `badgeWrap`)
  // is removed. The % is now ONLY shown in the bottom pill row next to the
  // Relationship pill (see `pillRow` + `similarityBase`). The ScoreBadge
  // component + its tier styles (`badgeBase`/`badgeHigh`/`badgeMid`/
  // `badgeLow`) are also removed below since they're no longer referenced.
  toolsWrap: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalXS,
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  // v1.1.49 — Selected-state card chrome (Item 1). Brand-strokes the border
  // + slight shadow lift so the user sees the toggle effect clearly. Token-
  // only per ADR-021.
  cardSelected: {
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    boxShadow: tokens.shadow4,
  },
  // The hero icon sits centered in the preview placeholder. Its color is set
  // inline (heroColor() above) so the same component supports type tinting.
  heroIcon: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  // Bottom "info" half — white surface with name row + meta (date) row +
  // bottom pill row.
  // v1.1.52 (Item 1) — Reverts v1.1.51 Item 6 (inline date in title row).
  // Date moves back to its own `meta` row below the name; the v1.1.51
  // Item 5 bottom Relationship pill row is preserved.
  //
  // v1.1.54 (Item 3) — `flex: 1` so the info area grows to fill the card's
  // remaining height (the card is flex-direction: column). Combined with
  // `marginTop: 'auto'` on `pillRow`, the title+date sit at the top and
  // the pill row is pushed to the BOTTOM of the card, creating a clean
  // visual separation from the title block. `minHeight` bumped 96 → 104
  // so cards still have a consistent footprint when pillRow has minimal
  // content.
  info: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    gap: tokens.spacingVerticalS,
    minHeight: '104px',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
  // v1.1.53 (Item 3) — `nameRow` no longer includes the small file-type
  // icon next to the file name. The hero icon already sits in the preview
  // area above; the small row icon was redundant. The Text for the name
  // now takes the full row width.
  nameRow: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
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
  // v1.1.52 (Item 1) — Restored standalone date row (was removed in
  // v1.1.51 Item 6 when date was inlined into title row). Mirrors the
  // pre-v1.1.51 `meta` style: neutral-3 foreground, base-200 font/line,
  // ellipsised on overflow, no wrap.
  meta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // v1.1.51 (Item 5) — bottom row of the info area: Relationship pill +
  // optional Similarity badge.
  // v1.1.54 (Item 3) — `marginTop: 'auto'` pushes the pill row to the
  // BOTTOM of the info area (the parent `info` div is `flex: 1` and
  // `flexDirection: 'column'`). Visual outcome: title + date at top,
  // whitespace gap, pill row anchored at the bottom of the card.
  pillRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    flexWrap: 'wrap',
    marginTop: 'auto',
  },
  // v1.1.53 (Items 1 + 2) — Similarity slot is now ALWAYS rendered as
  // a chip (LEFT of the Relationship pill). 'associated' rows render a
  // blank blue (brand) chip — same chrome the ListView's COL_SIMILARITY
  // shows on direct-association rows. 'semantic' / 'both' rows render
  // the Marigold % chip. Padding/typography is shared via
  // `similarityBase`; per-relationship colors layer on top via
  // `similarityAssociated` or `similaritySemantic` (mirrors the
  // ListView styles exactly). ADR-021 tokens only.
  similarityBase: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    paddingTop: '1px',
    paddingBottom: '1px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase100,
    whiteSpace: 'nowrap',
    // v1.1.53 — give the blank `associated` chip visible presence so
    // it occupies an equivalent footprint to the % chip even when no
    // text is rendered. Matches the ListView's blank-chip footprint.
    minWidth: '44px',
  },
  // v1.1.54 (Item 2) — Switched 'associated' chip from brand-blue to green
  // (matches the Relationship 'success' pill so cards reading "Same Matter
  // 100%" share one cohesive green family). Tokens only per ADR-021.
  similarityAssociated: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  similaritySemantic: {
    backgroundColor: tokens.colorPaletteMarigoldBackground2,
    color: tokens.colorPaletteMarigoldForeground2,
  },
  // v1.1.54 (Item 6) — `toolButton` style removed; the AI sparkle button
  // it styled is gone (AiSummary hidden across all surfaces). The 3-dot
  // menu trigger uses DocumentRowMenu's built-in styling.
});

// v1.1.54 (Item 1) — `ScoreBadge` removed. The match-score % previously
// rendered top-LEFT of the preview area is gone; the % now appears ONLY
// in the bottom pill row (rendered inline below via similarityBase/
// similaritySemantic for semantic rows; "100%" via similarityBase/
// similarityAssociated for direct-association rows). Removing the
// duplicate top-left pill simplifies the visual hierarchy.

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ResultCard: React.FC<IResultCardProps> = ({
  result,
  onClick,
  onOpenFile,
  onOpenRecord,
  onFindSimilar,
  onOpenPreview,
  isSelected,
  onToggleSelect,
  onPreview,
  // v1.1.54 (Item 6) — `onSummary` accepted for back-compat (parent still
  // passes it) but unused now that the sparkle popover is removed.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
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
  // v1.1.49 — When `onOpenPreview` is supplied, the host owns the
  // FilePreviewDialog (shared with the list view, Item 6) and we route
  // preview-open through it. The local `previewOpen` path is back-compat
  // only — kept so any caller that does NOT pass `onOpenPreview` still
  // works (legacy unit tests, standalone card use).
  const [previewOpen, setPreviewOpen] = useState(false);
  const useHostPreview = typeof onOpenPreview === 'function';
  const openPreview = useCallback(() => {
    if (useHostPreview) {
      onOpenPreview!();
    } else {
      setPreviewOpen(true);
    }
  }, [useHostPreview, onOpenPreview]);

  const fileKind = classifyFileType(result.fileType);
  const HeroIcon = getHeroIcon(fileKind);

  const handleCardClick = useCallback(
    (ev: React.MouseEvent) => {
      // Existing guard: clicks bubbling from any button (sparkle, menu trigger,
      // menu item buttons) MUST NOT open the preview dialog. DocumentRowMenu
      // trigger also calls stopPropagation internally — defense-in-depth here.
      if ((ev.target as HTMLElement).closest('button')) return;
      // v1.1.49 — also skip if the click came from inside the checkbox input
      // (the Checkbox stops propagation, but we belt-and-braces this).
      if ((ev.target as HTMLElement).closest('input[type="checkbox"]')) return;
      openPreview();
      // Mirror v1.1.46 behavior: the parent's `onClick` writes the selectedDocumentId
      // output so other surfaces (parent form, downstream selections) can react.
      onClick();
    },
    [onClick, openPreview]
  );

  const handleCardKeyDown = useCallback(
    (ev: React.KeyboardEvent) => {
      // Open preview on Enter/Space when the card itself has focus (not a
      // nested button). Matches v1.1.46's RecordCardShell keyboard contract.
      if (ev.key !== 'Enter' && ev.key !== ' ') return;
      if ((ev.target as HTMLElement).closest('button')) return;
      if ((ev.target as HTMLElement).closest('input[type="checkbox"]')) return;
      ev.preventDefault();
      openPreview();
      onClick();
    },
    [onClick, openPreview]
  );

  const handleOpenRecord = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

  // -------------------------------------------------------------------------
  // 3-dot menu dispatch
  // v1.1.54 (Item 6) — Menu is now standardized across card + row + dialog
  // surfaces. Visible: Preview, Open File, Find Similar, Download, Copy
  // link, Email, Open Record, Pin to top, Delete. Hidden via
  // `disabledActions`: AI Summary, Toggle workspace, Rename. The
  // `aiSummary` / `toggleWorkspace` / `rename` cases are still listed
  // here as no-ops so the exhaustive `never` check stays valid.
  // -------------------------------------------------------------------------
  const target = React.useMemo<IDocumentRowMenuTarget>(
    () => ({
      id: result.documentId,
      name: result.name,
      documentType: result.documentType,
    }),
    [result.documentId, result.name, result.documentType]
  );

  const handleRowAction = useCallback(
    (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          openPreview();
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
        case 'aiSummary':
        case 'toggleWorkspace':
        case 'rename':
          // v1.1.54 (Item 6) — hidden via `disabledActions`; defensive
          // no-ops here keep the exhaustive `never` check valid.
          return;
        case 'pinToTop':
        case 'delete':
          // Visible in the menu, but not yet wired in the PCF card surface
          // (Phase 4 follow-on tasks).
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
    [openPreview, onOpenFile, onFindSimilar, onCopyLink, onEmailDocument, onOpenRecord]
  );

  const formattedDate = formatShortDate(result.modifiedAt ?? result.createdAt);

  // v1.1.51 (Items 5 + 7) / v1.1.53 (Items 1 + 2) — Classify the row's
  // relationship so the bottom pill row mirrors ListView's
  // COL_RELATIONSHIP + COL_SIMILARITY rendering.
  // 'both' rows show Same Matter pill + similarity %; 'associated' rows
  // show Same Matter pill + a BLANK blue chip (matches ListView's blank
  // chip). Fallback to score-inference (zero → associated) on legacy
  // single-path responses with no `relationship` tag.
  // v1.1.53 (Item 1) — Similarity chip is always rendered LEFT of the
  // Relationship pill (was: conditional + right). Layout is consistent
  // across all cards.
  const rel: 'associated' | 'semantic' | 'both' =
    result.relationship ?? ((result.combinedScore ?? 0) === 0 ? 'associated' : 'semantic');
  const showRelationshipPill = true;
  const similarityPct = Math.round((result.combinedScore ?? 0) * 100);

  const ariaLabel = [result.name, result.documentType, formattedDate ? `Modified: ${formattedDate}` : '']
    .filter(Boolean)
    .join(', ');

  return (
    <>
      <div
        className={mergeClasses(styles.card, isSelected ? styles.cardSelected : undefined)}
        role="button"
        tabIndex={0}
        aria-label={ariaLabel}
        aria-pressed={isSelected ? 'true' : undefined}
        onClick={handleCardClick}
        onKeyDown={handleCardKeyDown}
      >
        {/* ─── Top preview placeholder ─────────────────────────────────── */}
        <div className={styles.preview}>
          {/* v1.1.49 — Selection checkbox overlay (Item 1).
              Rendered only when the parent wired `onToggleSelect`; defensive
              `stopPropagation` on click + keydown so card-open does not also
              fire. Tooltip wraps for accessibility. */}
          {typeof onToggleSelect === 'function' && (
            <div className={styles.checkboxWrap}>
              <Tooltip content={isSelected ? 'Deselect' : 'Select'} relationship="label">
                <Checkbox
                  checked={!!isSelected}
                  onChange={() => onToggleSelect()}
                  onClick={ev => ev.stopPropagation()}
                  onKeyDown={ev => {
                    // Space toggles via native checkbox; intercept here so the
                    // card's Enter/Space handler doesn't ALSO fire.
                    if (ev.key === ' ' || ev.key === 'Enter') {
                      ev.stopPropagation();
                    }
                  }}
                  aria-label={isSelected ? `Deselect ${result.name}` : `Select ${result.name}`}
                />
              </Tooltip>
            </div>
          )}
          {/* v1.1.54 (Item 1) — Top-LEFT % pill (`badgeWrap` / ScoreBadge)
              removed; the % now appears ONLY in the bottom pill row next
              to the Relationship pill. */}
          <div className={styles.toolsWrap}>
            {/* v1.1.54 (Item 6) — AiSummaryPopover sparkle removed from the
                card. AI Summary is hidden across all surfaces this round
                (also hidden via `disabledActions` in the menu below). */}
            {/* 3-dot menu — DocumentRowMenu handles its own stopPropagation
                internally so card-click doesn't fire when interacting with
                the menu's trigger or items.
                v1.1.54 (Item 6) — `disabledActions` standardized: hide
                AI Summary, Toggle workspace, Rename. Visible: Preview,
                Open File, Find Similar, Download, Copy link, Email,
                Open Record, Pin to top, Delete. */}
            <DocumentRowMenu
              document={target}
              onAction={handleRowAction}
              disabledActions={['aiSummary', 'toggleWorkspace', 'rename']}
            />
          </div>
          <div className={styles.heroIcon} style={{ color: heroColor(fileKind) }}>
            <HeroIcon aria-label={result.fileType || 'Document'} />
          </div>
        </div>

        {/* ─── Bottom info area ────────────────────────────────────────── */}
        {/* v1.1.52 (Item 1) — Reverts v1.1.51 Item 6 (inline date).
              Layout now: nameRow → meta (date) → pillRow.
              - Date back on its own row beneath the name.
              - v1.1.51 Item 5 bottom Relationship pill row is preserved.
              - v1.1.51 Item 7 `tint` Badge variants for Relationship +
                Marigold similarity chip stay exactly as-is. */}
        <div className={styles.info}>
          {/* v1.1.53 (Item 3) — Row icon removed; the hero icon in the
              preview area carries the file-type signal. Card aria-label
              still includes documentType for screen-reader parity. */}
          <div className={styles.nameRow}>
            <Text as="span" size={300} className={styles.name} title={result.name}>
              {result.name}
            </Text>
          </div>
          {formattedDate && (
            <Text as="span" size={200} className={styles.meta}>
              {formattedDate}
            </Text>
          )}
          {showRelationshipPill && (
            <div className={styles.pillRow} aria-hidden="false">
              {/* v1.1.54 (Item 2) — Reverses v1.1.53 Item 2: 'associated'
                  rows now render "100%" text in a GREEN chip (was: blank
                  blue chip). The green palette matches the Relationship
                  "Same Matter" tint so cards reading "Same Matter 100%"
                  share one cohesive green family. 'both' rows surface the
                  SEMANTIC %, not 100% — the semantic match score is the
                  more interesting signal when both relationships exist.
                  Similarity chip stays LEFT of the Relationship pill
                  (v1.1.53 Item 1 layout). */}
              {rel === 'associated' ? (
                <span
                  className={mergeClasses(styles.similarityBase, styles.similarityAssociated)}
                  role="img"
                  aria-label="Direct association: 100%"
                >
                  100%
                </span>
              ) : (
                <span
                  className={mergeClasses(styles.similarityBase, styles.similaritySemantic)}
                  role="img"
                  aria-label={`Semantic similarity: ${similarityPct}%`}
                >
                  {similarityPct}%
                </span>
              )}
              {rel === 'associated' || rel === 'both' ? (
                <Badge appearance="tint" color="success" size="medium" shape="rounded">
                  Same Matter
                </Badge>
              ) : (
                <Badge appearance="tint" color="brand" size="medium" shape="rounded">
                  Semantic
                </Badge>
              )}
            </div>
          )}
        </div>
      </div>

      {/* v1.1.49 — Local FilePreviewDialog is back-compat only. When the host
          supplies `onOpenPreview` (Item 6), the host owns the dialog and the
          local instance is suppressed so the navigation set + Prev/Next are
          shared with the list view. */}
      {!useHostPreview && (
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
      )}
    </>
  );
};

export default ResultCard;
