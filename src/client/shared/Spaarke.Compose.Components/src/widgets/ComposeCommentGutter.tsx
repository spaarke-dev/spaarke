/**
 * ComposeCommentGutter.tsx — right-rail comment layout aligned to anchors
 * (ai-advanced-capabilities-nda-r1 task 032).
 *
 * Renders one card per comment thread in a RIGHT-SIDE column, absolutely positioned inside
 * `editorScrollWrap` (already `position: relative` — FIX #9), each vertically aligned to the CURRENT
 * ProseMirror position of its `commentAnchor` mark (spec FR-16: "comments on the RIGHT, aligned with
 * suggested edits"). Today wired to the NDA-REVIEW advisory-comment thread instance
 * ({@link ComposeEditorHandle.getAdvisoryCommentThreads}, task 031) — the session Comments panel
 * (`ComposeCommentThread.tsx`) is unaffected and keeps its own docked-panel presentation.
 *
 * LIVE POSITION (ADR-049 binding constraint — "do not trust stale stored positions"): a thread's
 * `anchorText` is a CREATION-TIME snapshot, never updated as the document is edited. This component
 * NEVER reads it for placement. Instead it resolves each thread's `commentAnchor` mark's CURRENT span
 * via {@link findCommentAnchorRange} (`ComposeCommentThread.types.ts`) — the SAME live-position
 * primitive `composeSessionCommentThreadsToAnchoredComments` (task 040) already uses for save-time
 * export — then reads `editor.view.coordsAtPos(span.from)` for the Y coordinate. A thread whose anchor
 * mark is no longer present (a later edit deleted the anchored text) is OMITTED, never guessed a
 * fallback position (mirrors {@link findCommentAnchorRange}'s own never-mis-map discipline).
 *
 * WIDGET-DECORATION PRECEDENT (binding design rationale, per the task's knowledge pointer): this
 * reuses the SAME coordsAtPos-driven positioning approach `TrackChangesExtension.ts` proved for its
 * live redline overlay — a VIEW-layer concern computed from the current doc, recomputed on every
 * transaction, never a content mutation. `TrackChangesExtension` renders ProseMirror `Decoration`s
 * (inside the editor's own DecorationSet); this component instead renders a SEPARATE React overlay
 * (absolutely positioned cards, not itself a decoration) because a comment card is real Fluent v9 UI
 * (badge, buttons, text) — decorations render raw DOM nodes with no React reconciliation, which would
 * make a rich interactive card unmaintainable. The POSITIONING technique (coordsAtPos, relative to the
 * scroll wrap, recomputed on transaction/scroll) is the part reused verbatim; the RENDERING technique
 * (React overlay vs. ProseMirror decoration) differs because the content differs.
 *
 * COLLISION / STACKING: raw Y positions are sorted ascending, then a greedy pass pushes any card whose
 * raw position would overlap the previous card's rendered bottom (+ a fixed gap) down to clear it — the
 * same "no two adjacent cards overlap" rule Google Docs' / Word's margin-comment rails use. Card
 * heights are read from measured DOM refs (falling back to a fixed estimate before first paint), so
 * the stacking math accounts for each card's REAL rendered height, not a guess.
 *
 * REFLOW: recomputed on doc change (`editor.on('transaction', …)`, mirroring the FIX #9 down-arrow
 * FAB's own transaction-listener convention in `ComposeEditor.tsx`), on scroll of the editor's
 * scrollable surface (rAF-throttled), and on window resize.
 *
 * ADR-021: Fluent v9 semantic tokens only (no hex); dark-mode compliant.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `ComposeCommentThread.tsx` is the docked SESSION comments panel (open/create/reply/
 *     resolve) — a different capability (thread MANAGEMENT UI) from this component (thread
 *     PLACEMENT/alignment). `TrackChangesExtension.ts` proves the positioning technique but renders
 *     raw decorations, not interactive Fluent cards — no overlap to extend.
 *   - Extension: folding this into `ComposeCommentThread.tsx` would blur that panel's SCOPE GUARD
 *     (view/create/reply/resolve, no positioning concern) with a live-position/collision-layout
 *     concern; folding it into `TrackChangesExtension.ts` would blur a pure-decoration overlay with
 *     interactive React UI. A new, focused component is the reuse-first move — it reuses the
 *     underlying primitives ({@link findCommentAnchorRange}, `coordsAtPos`, `riskBadgeColor`) rather
 *     than reinventing them.
 *   - Cost-of-doing-nothing: without it, NDA-REVIEW advisory comments have no vertically-aligned,
 *     at-a-glance right-rail presentation — only an in-document underline + the top-of-column
 *     `NdaReviewSummaryPanel` list (spec FR-16 unmet).
 *
 * @see ./ComposeCommentThread.types.ts — `findCommentAnchorRange` (live position), `ComposeCommentThreadModel`
 * @see ./marks/TrackChangesExtension.ts — the coordsAtPos/widget-decoration precedent this reuses
 * @see ./ComposeEditor.tsx — mount point (inside `editorScrollWrap`) + `advisoryComments.threads` source
 * @see ./NdaReviewSummaryPanel.tsx — `riskBadgeColor` (reused for the per-card risk badge)
 * @see projects/ai-advanced-capabilities-nda-r1/spec.md FR-16
 */
import * as React from 'react';
import { type Editor } from '@tiptap/react';
import {
  Badge,
  Text,
  Spinner,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Button,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { ChevronDoubleDown16Regular, MoreVertical20Regular, SparkleRegular } from '@fluentui/react-icons';
import { findCommentAnchorRange, type ComposeCommentThreadModel } from './ComposeCommentThread.types';
import { riskBadgeColor, formatClauseLocation } from './NdaReviewSummaryPanel';
import { deriveClauseLocationLabel } from './ndaClauseLocation';

/** Right-rail column DEFAULT width — cards clear of the document's own right margin. */
export const COMMENT_GUTTER_WIDTH_PX = 220;
/** Resize bounds (UAT round-3 D1) — the user can drag the rail between these. */
export const MIN_COMMENT_GUTTER_WIDTH_PX = 160;
export const MAX_COMMENT_GUTTER_WIDTH_PX = 480;
/** Minimum height (UAT round-6 #2) the Review Notes container can be dragged down to. */
export const MIN_COMMENT_GUTTER_HEIGHT_PX = 120;
/** sessionStorage key persisting the user's Review Notes container height (UAT round-6 #2). */
const GUTTER_HEIGHT_STORAGE_KEY = 'spaarke.compose.notesGutterHeight';
/** Fixed vertical gap enforced between stacked cards (collision-avoidance pass). */
const CARD_GAP_PX = 8;
/** Fallback card height used ONLY before a card's real height has been measured (first paint). */
const DEFAULT_CARD_HEIGHT_PX = 96;
/**
 * Collapsed-card body character budget (UAT round-2 item #5). Advisory explanations frequently run
 * well past this in the narrow rail, so a collapsed card shows a preview + a "Show more" toggle; the
 * expanded card shows the full text. Kept short so the default (collapsed) rail stays scannable.
 */
const COLLAPSED_BODY_MAX_CHARS = 140;

const useStyles = makeStyles({
  rail: {
    position: 'absolute',
    top: 0,
    right: 0,
    // Width + height are applied inline (user-resizable — width UAT round-3 D1, height UAT round-6 #2);
    // these are the fallback defaults.
    width: `${COMMENT_GUTTER_WIDTH_PX}px`,
    // UAT round-6 #2 — the Review Notes are their OWN bounded container: clip cards to the rail box so a
    // card whose anchored clause has scrolled ABOVE the fold (negative computed top) can NEVER render up
    // into the Review Summary above (the reviewer's "notes scroll into the summary" complaint), and a
    // card past the (resizable) bottom edge is clipped too.
    overflow: 'hidden',
    // The rail itself never intercepts clicks over the document beneath it — only the resize handles +
    // individual cards (which re-enable pointer events) are interactive.
    pointerEvents: 'none',
    zIndex: 1,
  },
  // UAT round-3 D1 — a thin drag strip on the rail's LEFT edge to resize the comment pane WIDTH.
  resizeHandle: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
    width: '6px',
    cursor: 'col-resize',
    pointerEvents: 'auto',
    zIndex: 2,
    ':hover': {
      backgroundColor: tokens.colorNeutralStroke1,
    },
  },
  // UAT round-6 #3 — the notes scroll-down affordance, CENTERED on the notes container's bottom (not
  // off to a corner). Circular, elevated; nudges the document down to reveal notes clipped below.
  scrollNotesDown: {
    position: 'absolute',
    bottom: tokens.spacingVerticalM,
    left: '50%',
    transform: 'translateX(-50%)',
    zIndex: 3,
    pointerEvents: 'auto',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    padding: 0,
    borderRadius: '9999px',
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    boxShadow: tokens.shadow8,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  // UAT round-6 #2 — a drag strip on the rail's BOTTOM edge to resize the Review Notes container HEIGHT.
  bottomResizeHandle: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    height: '6px',
    cursor: 'row-resize',
    pointerEvents: 'auto',
    zIndex: 2,
    ':hover': {
      backgroundColor: tokens.colorNeutralStroke1,
    },
  },
  card: {
    position: 'absolute',
    right: tokens.spacingHorizontalS,
    left: tokens.spacingHorizontalS,
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow4,
    pointerEvents: 'auto',
    // UAT round-5 #8 — NO `top` transition. The rail recomputes each card's `top` from coordsAtPos on
    // every scroll frame; animating `top` made each frame chase the last, producing the "choppy" scroll
    // the reviewer reported. Tracking the scroll instantly (no transition) is smooth.
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalXS,
  },
  // UAT round-8 #3 — the risk badge + the ⋮ tools menu sit together on the header's right.
  cardHeaderRight: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
  toolsButton: {
    minWidth: '24px',
    width: '24px',
    height: '24px',
    padding: 0,
  },
  sectionRef: {
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  body: {
    color: tokens.colorNeutralForeground1,
  },
  // UAT round-5 #7 — the advisory note's "Grounded fact" / "Advisory judgment" aspects render as
  // separate, labelled paragraphs so the two halves of the analysis are easy to tell apart.
  noteSegments: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  noteSegment: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: '1px',
  },
  noteSegmentLabel: {
    color: tokens.colorNeutralForeground2,
  },
  noteSegmentBody: {
    color: tokens.colorNeutralForeground1,
  },
  standardRef: {
    color: tokens.colorNeutralForeground3,
  },
  // UAT round-3 D3 — the "Standard: {ref}" line becomes a clickable link that opens a popover with the
  // full clause text. Reset the native button to a left-aligned link look.
  standardRefButton: {
    alignSelf: 'flex-start',
    padding: 0,
    border: 'none',
    background: 'none',
    font: 'inherit',
    textAlign: 'left',
    cursor: 'pointer',
    color: tokens.colorBrandForegroundLink,
    fontSize: tokens.fontSizeBase100,
    textDecorationLine: 'underline',
    textDecorationStyle: 'dotted',
    ':hover': { color: tokens.colorBrandForegroundLinkHover },
  },
  standardPopover: {
    maxWidth: '340px',
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  standardPopoverTitle: {
    color: tokens.colorNeutralForeground1,
  },
  standardPopoverBody: {
    color: tokens.colorNeutralForeground2,
  },
  // UAT round-3 D2: a truncatable card is itself the expand/collapse click target (no separate
  // "Show more" text button) — the whole block toggles, with a chevron cue.
  cardClickable: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      border: `1px solid ${tokens.colorNeutralStroke1}`,
    },
  },
  // UAT round-5 #5: the SELECTED review note turns YELLOW, coordinated with its in-document clause (also
  // yellow when selected) — the reviewer sees the section and its note as one linked, yellow pair, while
  // UNselected clauses read light gray. Persists until re-clicked or another note is selected.
  cardSelected: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
    border: `1px solid ${tokens.colorPaletteYellowBorderActive}`,
    ':hover': {
      backgroundColor: tokens.colorPaletteYellowBackground2,
      border: `1px solid ${tokens.colorPaletteYellowBorderActive}`,
    },
  },
  // UAT round-4 #8: when selection is wired, the double-arrow cue becomes the dedicated expand control
  // (card click selects instead) — reset it to a bare, centered icon button.
  expandCueButton: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    width: '100%',
    padding: 0,
    border: 'none',
    background: 'none',
    cursor: 'pointer',
    color: tokens.colorNeutralForeground3,
    ':hover': { color: tokens.colorNeutralForeground2 },
  },
  // The double-down-arrow expandability cue (UAT round-3 D2), centered under the card body; rotates
  // 180° when the card is expanded (points up = "collapse").
  expandCue: {
    display: 'flex',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    marginTop: '2px',
  },
  expandCueOpen: {
    transform: 'rotate(180deg)',
  },
});

/** Truncated, log-safe-length label (mirrors ComposeCommentThread's / NdaReviewSummaryPanel's helper). */
function truncate(text: string, max: number): string {
  const trimmed = text.trim();
  return trimmed.length > max ? `${trimmed.slice(0, max)}…` : trimmed;
}

/** One labelled aspect of an advisory note ("Grounded fact" / "Advisory judgment"), or an unlabelled run. */
export interface AdvisoryNoteSegment {
  /** The aspect label (e.g. "Grounded fact"), or undefined for text with no recognized label. */
  label?: string;
  /** The aspect's prose. */
  body: string;
}

/** The labels the NDA-REVIEW Action authors its advisory explanations with (case-insensitive). */
const ADVISORY_NOTE_LABELS = ['Grounded fact', 'Advisory judgment', 'Judgment'] as const;

/**
 * UAT round-6 #5 — the DISPLAY labels the reviewer asked for, mapped from the model's authored markers:
 * "Grounded fact" → "Flagged clause", "Advisory judgment" / "Judgment" → "Assessment says". Detection
 * still keys on the model's original words; only the rendered label changes.
 */
const ADVISORY_NOTE_DISPLAY_LABEL: Record<string, string> = {
  'grounded fact': 'Flagged clause',
  'advisory judgment': 'Assessment says',
  judgment: 'Assessment says',
};

/**
 * UAT round-5 #7 — split an advisory note into its "Grounded fact" / "Advisory judgment" aspects so each
 * renders as its own labelled, separated paragraph (the reviewer asked what the labels mean + for them to
 * be bold + separate for readability). The Action authors explanations as
 * "Grounded fact: … Advisory judgment: …" (also older "Judgment: …"); we split on those markers. Text
 * with no recognized label returns a single unlabelled segment (rendered plainly, unchanged) — so a note
 * that doesn't follow the convention never gets mangled.
 */
export function parseAdvisoryNote(text: string): AdvisoryNoteSegment[] {
  const trimmed = (text ?? '').trim();
  if (!trimmed) return [];
  // Find every label occurrence (label + following “:”/“—”/“-”), in document order.
  const pattern = new RegExp(`\\b(${ADVISORY_NOTE_LABELS.join('|')})\\b\\s*[:—-]\\s*`, 'gi');
  const marks: { label: string; start: number; bodyStart: number }[] = [];
  for (let m = pattern.exec(trimmed); m !== null; m = pattern.exec(trimmed)) {
    marks.push({ label: m[1], start: m.index, bodyStart: m.index + m[0].length });
  }
  if (marks.length === 0) return [{ body: trimmed }];
  const segments: AdvisoryNoteSegment[] = [];
  // Any prose before the first label is kept as an unlabelled lead segment (never dropped).
  if (marks[0].start > 0) {
    const lead = trimmed.slice(0, marks[0].start).trim();
    if (lead) segments.push({ body: lead });
  }
  for (let i = 0; i < marks.length; i++) {
    const end = i + 1 < marks.length ? marks[i + 1].start : trimmed.length;
    const body = trimmed.slice(marks[i].bodyStart, end).trim();
    // UAT round-6 #5 — display the reviewer's preferred label ("Flagged clause" / "Assessment says"),
    // falling back to the model's own word for any unmapped label.
    const label = ADVISORY_NOTE_DISPLAY_LABEL[marks[i].label.toLowerCase()] ?? marks[i].label;
    segments.push({ label, body });
  }
  return segments;
}

export interface ComposeCommentGutterProps {
  /** The live TipTap editor. `null` while unmounted — the gutter renders nothing. */
  editor: Editor | null;
  /** Comment threads to place — today `ComposeEditorHandle.getAdvisoryCommentThreads()`'s source. */
  threads: readonly ComposeCommentThreadModel[];
  /**
   * The editor's SCROLLABLE surface (`editorScrollRef` in `ComposeEditor.tsx`) — its `scroll` events
   * drive reflow. A ref (not the element itself) so the gutter reads the CURRENT DOM node each time,
   * matching the mount-order contract `editorScrollWrap`'s children already rely on (siblings commit
   * together, so the ref is populated before this component's effects run).
   */
  scrollContainerRef: React.RefObject<HTMLDivElement | null>;
  /** Current rail width in px (UAT round-3 D1 — user-resizable). Defaults to {@link COMMENT_GUTTER_WIDTH_PX}. */
  width?: number;
  /** Called (during a drag on the left-edge handle) with the new, clamped rail width. Omit to make the rail fixed-width. */
  onWidthChange?: (width: number) => void;
  /**
   * UAT round-3 D3 — resolves the full standard-clause text for a finding's `standardRef` (e.g. "B5 -
   * Use & disclosure obligations"), fetched on demand when the reviewer clicks the "Standard: …" line.
   * Returns null when the clause can't be found. Omit to render `standardRef` as plain (non-clickable)
   * text — a standalone/library mount with no BFF wiring.
   */
  resolveStandardText?: (standardRef: string) => Promise<string | null>;
  /**
   * UAT round-4 #8 — the currently-selected advisory thread id (shared with the in-document anchor
   * highlight). The matching card renders a gray "selected" state. `null`/omitted = no selection.
   */
  selectedThreadId?: string | null;
  /**
   * UAT round-4 #8 — called with a thread id when the reviewer clicks a card, to SELECT it (the host
   * toggles selection + scrolls the document to the clause + paints its anchor yellow). When wired, a
   * card CLICK selects (rather than expands) and the double-arrow cue becomes the dedicated expand
   * control. Omit for a standalone/library mount with no selection wiring — a card click then keeps its
   * original expand/collapse behavior.
   */
  onSelectThread?: (threadId: string) => void;
  /**
   * UAT round-8 #3/#4/#5 — the AI edit tools offered per Review Note (a ⋮ overflow menu), e.g. "Draft
   * compliant alternative". Driven by the host from the binding-wired compose edit actions, so the list
   * grows automatically as more tools are seeded (extensibility #5). Omit / empty → no ⋮ menu.
   */
  noteTools?: readonly NoteTool[];
  /** UAT round-8 #4 — run a note tool against the thread's clause span (host dispatches the AI edit). */
  onRunNoteTool?: (threadId: string, toolId: string) => void;
}

/** One AI edit tool offered on a Review Note's ⋮ menu (UAT round-8 #3/#4). */
export interface NoteTool {
  /** The compose action id (e.g. `compose-draft-alternative`). */
  id: string;
  /** The menu label shown to the reviewer (e.g. "Draft compliant alternative"). */
  label: string;
}

/**
 * UAT round-3 D3 — the clickable "Standard: {ref}" line inside a gutter card. Opens a Fluent Popover
 * that fetches the clause text once on first open. `stopPropagation` keeps a click/keypress here from
 * also toggling the card's expand/collapse (the card is itself a button).
 */
function StandardRefChip(props: {
  standardRef: string;
  resolve: (ref: string) => Promise<string | null>;
  className: string;
  surfaceClassName: string;
  titleClassName: string;
  bodyClassName: string;
  threadId: string;
}): React.JSX.Element {
  const { standardRef, resolve, className, surfaceClassName, titleClassName, bodyClassName, threadId } = props;
  const [open, setOpen] = React.useState(false);
  const [text, setText] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<'idle' | 'loading' | 'done' | 'error'>('idle');
  const fetchedRef = React.useRef(false);

  const handleOpenChange = React.useCallback(
    (_e: unknown, data: { open: boolean }): void => {
      setOpen(data.open);
      if (data.open && !fetchedRef.current) {
        fetchedRef.current = true;
        setStatus('loading');
        resolve(standardRef)
          .then(t => {
            setText(t);
            setStatus(t === null ? 'error' : 'done');
          })
          .catch(() => setStatus('error'));
      }
    },
    [resolve, standardRef]
  );

  return (
    <Popover open={open} onOpenChange={handleOpenChange} withArrow positioning="before" size="small">
      <PopoverTrigger disableButtonEnhancement>
        <button
          type="button"
          className={className}
          onClick={e => e.stopPropagation()}
          onKeyDown={e => e.stopPropagation()}
          data-testid={`compose-comment-gutter-standard-${threadId}`}
        >
          Standard: {standardRef}
        </button>
      </PopoverTrigger>
      <PopoverSurface
        className={surfaceClassName}
        onClick={e => e.stopPropagation()}
        data-testid={`compose-comment-gutter-standard-popover-${threadId}`}
      >
        <Text weight="semibold" size={200} className={titleClassName}>
          {standardRef}
        </Text>
        {status === 'loading' ? (
          <Spinner size="tiny" label="Loading standard…" labelPosition="after" />
        ) : status === 'error' ? (
          <Text size={200} className={bodyClassName}>
            The standard clause text is unavailable right now.
          </Text>
        ) : (
          <Text size={200} className={bodyClassName}>
            {text}
          </Text>
        )}
      </PopoverSurface>
    </Popover>
  );
}

/** One thread's resolved raw (pre-collision) Y position, `top` relative to the rail's own top edge. */
interface RawCardPosition {
  id: string;
  top: number;
}

/**
 * Pure collision/stacking layout: given raw (unresolved-collision) Y positions and each card's
 * rendered (or fallback-estimated) height, returns the FINAL top offset per thread id such that no
 * two cards overlap — a card is pushed down (never up) just far enough to clear the previous one.
 * Exported for direct unit testing (no editor/DOM dependency).
 */
export function layoutCommentGutterCards(
  raw: readonly RawCardPosition[],
  heights: ReadonlyMap<string, number>
): Record<string, number> {
  const sorted = [...raw].sort((a, b) => a.top - b.top);
  const result: Record<string, number> = {};
  let cursor = Number.NEGATIVE_INFINITY;
  for (const { id, top } of sorted) {
    const height = heights.get(id) ?? DEFAULT_CARD_HEIGHT_PX;
    const placed = cursor === Number.NEGATIVE_INFINITY ? top : Math.max(top, cursor);
    result[id] = placed;
    cursor = placed + height + CARD_GAP_PX;
  }
  return result;
}

export function ComposeCommentGutter(props: ComposeCommentGutterProps): React.JSX.Element | null {
  const {
    editor,
    threads,
    scrollContainerRef,
    width = COMMENT_GUTTER_WIDTH_PX,
    onWidthChange,
    resolveStandardText,
    selectedThreadId = null,
    onSelectThread,
    noteTools,
    onRunNoteTool,
  } = props;
  const styles = useStyles();
  const railRef = React.useRef<HTMLDivElement | null>(null);

  // UAT round-3 D1 — left-edge drag to resize. The gutter sits on the RIGHT, so dragging the handle
  // LEFT (decreasing clientX) WIDENS the pane; the new width is clamped to [MIN, MAX] and reported to
  // the host, which owns the width state (and persists it). Pointer capture keeps the drag tracking
  // even if the cursor leaves the 6px strip.
  const dragStateRef = React.useRef<{ startX: number; startWidth: number } | null>(null);
  const onResizePointerDown = React.useCallback(
    (e: React.PointerEvent<HTMLDivElement>): void => {
      if (!onWidthChange) return;
      e.preventDefault();
      e.currentTarget.setPointerCapture(e.pointerId);
      dragStateRef.current = { startX: e.clientX, startWidth: width };
    },
    [onWidthChange, width]
  );
  const onResizePointerMove = React.useCallback(
    (e: React.PointerEvent<HTMLDivElement>): void => {
      const drag = dragStateRef.current;
      if (!drag || !onWidthChange) return;
      const delta = drag.startX - e.clientX; // drag left → positive → wider
      const next = Math.min(
        MAX_COMMENT_GUTTER_WIDTH_PX,
        Math.max(MIN_COMMENT_GUTTER_WIDTH_PX, drag.startWidth + delta)
      );
      onWidthChange(next);
    },
    [onWidthChange]
  );
  const onResizePointerUp = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    dragStateRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }, []);

  // UAT round-6 #2 — the Review Notes container is now bounded (overflow:hidden) with a RESIZABLE bottom
  // edge. `null` = fill the available height (default); a number caps it. Dragging the bottom handle
  // down grows it, up shrinks it (clamped to [MIN, the editorScrollWrap height]). Persisted in
  // sessionStorage so it survives a remount, exactly like the width.
  const [railHeight, setRailHeight] = React.useState<number | null>(() => {
    if (typeof window === 'undefined') return null;
    const stored = window.sessionStorage.getItem(GUTTER_HEIGHT_STORAGE_KEY);
    const parsed = stored ? Number.parseInt(stored, 10) : Number.NaN;
    return Number.isFinite(parsed) ? parsed : null;
  });
  const heightDragRef = React.useRef<{ startY: number; startHeight: number } | null>(null);
  const onHeightPointerDown = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    const current = railRef.current?.offsetHeight ?? 0;
    heightDragRef.current = { startY: e.clientY, startHeight: current };
  }, []);
  const onHeightPointerMove = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    const drag = heightDragRef.current;
    if (!drag) return;
    const delta = e.clientY - drag.startY; // drag down → positive → taller
    const max = railRef.current?.parentElement?.clientHeight ?? Number.MAX_SAFE_INTEGER;
    const next = Math.min(max, Math.max(MIN_COMMENT_GUTTER_HEIGHT_PX, drag.startHeight + delta));
    setRailHeight(next);
    if (typeof window !== 'undefined') window.sessionStorage.setItem(GUTTER_HEIGHT_STORAGE_KEY, String(next));
  }, []);
  const onHeightPointerUp = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    heightDragRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }, []);

  const cardElementsRef = React.useRef<Map<string, HTMLDivElement>>(new Map());
  const [cardTops, setCardTops] = React.useState<Record<string, number>>({});
  // UAT round-6 #3 — true when at least one card sits at/below the (bounded) rail's bottom edge, so the
  // centered scroll-down affordance appears.
  const [hasClippedBelow, setHasClippedBelow] = React.useState(false);
  const onScrollNotesDown = React.useCallback((): void => {
    const el = scrollContainerRef.current;
    if (!el) return;
    const step = railRef.current?.clientHeight ?? el.clientHeight;
    el.scrollTo({ top: el.scrollTop + step * 0.8, behavior: 'smooth' });
  }, [scrollContainerRef]);
  // Per-card expand/collapse (item #5). A Set of thread ids whose full text is shown.
  const [expandedIds, setExpandedIds] = React.useState<ReadonlySet<string>>(() => new Set());

  const toggleExpanded = React.useCallback((id: string): void => {
    setExpandedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const recompute = React.useCallback((): void => {
    if (!editor || !railRef.current) return;
    const railRect = railRef.current.getBoundingClientRect();
    const raw: RawCardPosition[] = [];
    for (const thread of threads) {
      // Live position (ADR-049) — resolve the mark's CURRENT span; never trust `thread.anchorText`.
      const span = findCommentAnchorRange(editor.state.doc, thread.id);
      if (!span) continue; // anchor deleted by a later edit — omit, never guess a fallback position
      try {
        const coords = editor.view.coordsAtPos(span.from);
        raw.push({ id: thread.id, top: coords.top - railRect.top });
      } catch {
        // `coordsAtPos` measures real DOM layout (`Range.getClientRects`) and can throw in an
        // environment without full layout support (e.g. a detached/not-yet-painted view). Skip this
        // thread's placement for this pass rather than crash the whole editor — the next transaction
        // or scroll/resize recompute retries it.
        continue;
      }
    }
    const heights = new Map<string, number>();
    let allMeasured = true;
    for (const { id } of raw) {
      const el = cardElementsRef.current.get(id);
      if (el) heights.set(id, el.offsetHeight);
      else allMeasured = false; // not yet mounted — this pass falls back to DEFAULT_CARD_HEIGHT_PX for it
    }
    const next = layoutCommentGutterCards(raw, heights);

    setCardTops(prev => {
      const prevKeys = Object.keys(prev);
      const nextKeys = Object.keys(next);
      const unchanged =
        prevKeys.length === nextKeys.length &&
        nextKeys.every(id => Math.abs((prev[id] ?? Number.NaN) - next[id]) < 0.5);
      return unchanged ? prev : next;
    });

    // UAT round-6 #3 — does any card sit at/below the bounded rail's bottom edge (clipped)? If so, show
    // the centered scroll-down affordance. `clientHeight` reflects the current (possibly resized) rail.
    const railClientHeight = railRef.current.clientHeight;
    const clipped = Object.values(next).some(top => top > railClientHeight - 24);
    setHasClippedBelow(prev => (prev === clipped ? prev : clipped));

    // A card that just got its FIRST position (this pass) wasn't in `cardElementsRef` yet (it only
    // mounts once `cardTops` includes it), so this pass used the DEFAULT_CARD_HEIGHT_PX estimate for
    // it — not its real rendered height. Nothing else automatically re-triggers `recompute` once that
    // card mounts (`cardTops` isn't a dependency of the effects below, by design, to avoid a
    // recompute-loop), so without this, the estimate-based stacking would persist until the next
    // scroll/edit. Schedule exactly one follow-up pass after paint — by then the card is mounted and
    // `cardElementsRef` has its real height, so `allMeasured` is true and this doesn't reschedule again.
    if (!allMeasured) {
      requestAnimationFrame(() => recompute());
    }
  }, [editor, threads]);

  // Reflow on doc change — mirrors the FIX #9 down-arrow FAB's `editor.on('transaction', …)` pattern.
  React.useEffect(() => {
    if (!editor) return;
    recompute();
    editor.on('transaction', recompute);
    return () => {
      editor.off('transaction', recompute);
    };
  }, [editor, recompute]);

  // Reflow on scroll of the editor's scrollable surface + window resize (rAF-throttled).
  React.useEffect(() => {
    const el = scrollContainerRef.current;
    if (!el) return;
    let rafId = 0;
    const onScrollOrResize = (): void => {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(recompute);
    };
    el.addEventListener('scroll', onScrollOrResize, { passive: true });
    window.addEventListener('resize', onScrollOrResize, { passive: true });
    return () => {
      el.removeEventListener('scroll', onScrollOrResize);
      window.removeEventListener('resize', onScrollOrResize);
      cancelAnimationFrame(rafId);
    };
  }, [scrollContainerRef, recompute]);

  // Re-measure after cards render (real heights may differ from DEFAULT_CARD_HEIGHT_PX, e.g. a long
  // explanation wraps to more lines) — a second layout pass corrects the initial estimate-based math.
  // Also re-runs when the container height changes (UAT round-6 #2 resize) so the clipped-state (#3) and
  // stacking reflect the new bounds.
  React.useLayoutEffect(() => {
    recompute();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-run whenever the thread SET / height changes
  }, [threads.length, railHeight, recompute]);

  // Expanding/collapsing a card changes its measured height — re-run the collision layout so cards
  // below reflow to clear (or reclaim) the space (item #5). Runs after the new text has committed to
  // the DOM (useLayoutEffect) so `offsetHeight` reflects the expanded/collapsed size.
  React.useLayoutEffect(() => {
    recompute();
  }, [expandedIds, recompute]);

  if (!editor || threads.length === 0) return null;

  return (
    <div
      ref={railRef}
      className={styles.rail}
      style={{ width: `${width}px`, height: railHeight != null ? `${railHeight}px` : '100%' }}
      data-testid="compose-comment-gutter"
    >
      {onWidthChange ? (
        <div
          className={styles.resizeHandle}
          onPointerDown={onResizePointerDown}
          onPointerMove={onResizePointerMove}
          onPointerUp={onResizePointerUp}
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize comments pane"
          data-testid="compose-comment-gutter-resize"
        />
      ) : null}
      {/* UAT round-6 #2 — drag the bottom edge to resize the Review Notes container height. */}
      <div
        className={styles.bottomResizeHandle}
        onPointerDown={onHeightPointerDown}
        onPointerMove={onHeightPointerMove}
        onPointerUp={onHeightPointerUp}
        role="separator"
        aria-orientation="horizontal"
        aria-label="Resize the Review Notes height"
        data-testid="compose-comment-gutter-resize-bottom"
      />
      {/* UAT round-6 #3 — a down-arrow, centered on the notes container, appears when cards are clipped
          below the (resized) bottom edge; it nudges the document down to bring the next note into view. */}
      {hasClippedBelow ? (
        <button
          type="button"
          className={styles.scrollNotesDown}
          aria-label="Scroll down for more notes"
          onClick={onScrollNotesDown}
          data-testid="compose-comment-gutter-scroll-down"
        >
          <ChevronDoubleDown16Regular />
        </button>
      ) : null}
      {threads.map(thread => {
        const top = cardTops[thread.id];
        if (top === undefined) return null; // anchor unresolved (deleted) — omitted, never guessed
        const fullText = thread.text.trim();
        const isExpanded = expandedIds.has(thread.id);
        const isTruncatable = fullText.length > COLLAPSED_BODY_MAX_CHARS;
        const showStructured = isExpanded || !isTruncatable;
        // UAT round-6 #5 + round-7 follow-up: ALWAYS render the structured "Flagged clause" / "Assessment
        // says" segments (with the renamed labels) — including when COLLAPSED (the reviewer saw the raw
        // model labels "Grounded fact" / "Advisory judgment" in the collapsed preview). Collapsed shows
        // just the FIRST segment with a truncated body; expanding reveals every segment in full.
        const allSegments = parseAdvisoryNote(fullText);
        const segments = showStructured
          ? allSegments
          : allSegments.length > 0
            ? [{ ...allSegments[0], body: truncate(allSegments[0].body, COLLAPSED_BODY_MAX_CHARS) }]
            : [{ body: truncate(fullText, COLLAPSED_BODY_MAX_CHARS) }];
        // UAT round-5 #6/#1 — the note's location, resolved from the LIVE document (section heading +
        // ordinal that the model's sectionRef lacks) via the thread's current anchor. Identical to the
        // summary row. Only advisory notes carry a `sectionRef`; a plain session comment has none and
        // keeps the generic "Comment" label. Falls back to the model-only label when no editor.
        const anchorSpan = thread.sectionRef && editor ? findCommentAnchorRange(editor.state.doc, thread.id) : null;
        const loc = thread.sectionRef
          ? editor
            ? deriveClauseLocationLabel(editor.state.doc, anchorSpan?.from ?? null, thread.sectionRef)
            : formatClauseLocation(thread.sectionRef)
          : null;
        const isSelected = selectedThreadId === thread.id; // UAT round-4 #8
        // UAT round-4 #8: when selection is wired the card CLICK selects (and the cue button expands);
        // otherwise it keeps the round-3 D2 behavior (card click toggles expand, truncatable only).
        const selectable = Boolean(onSelectThread);
        const isInteractive = selectable || isTruncatable;
        const activate = selectable ? () => onSelectThread?.(thread.id) : isTruncatable ? () => toggleExpanded(thread.id) : undefined;
        return (
          <div
            key={thread.id}
            ref={el => {
              if (el) cardElementsRef.current.set(thread.id, el);
              else cardElementsRef.current.delete(thread.id);
            }}
            className={mergeClasses(
              styles.card,
              isInteractive ? styles.cardClickable : undefined,
              isSelected ? styles.cardSelected : undefined
            )}
            style={{ top: `${top}px` }}
            role={isInteractive ? 'button' : 'complementary'}
            tabIndex={isInteractive ? 0 : undefined}
            aria-pressed={selectable ? isSelected : undefined}
            aria-expanded={!selectable && isTruncatable ? isExpanded : undefined}
            aria-label={
              `Comment${thread.sectionRef ? `: ${thread.sectionRef}` : ''}` +
              (selectable
                ? isSelected
                  ? ' — selected, activate to deselect'
                  : ' — activate to select and go to the clause'
                : isTruncatable
                  ? isExpanded
                    ? ' — expanded, activate to collapse'
                    : ' — truncated, activate to expand'
                  : '')
            }
            onClick={activate}
            onKeyDown={
              activate
                ? e => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      activate();
                    }
                  }
                : undefined
            }
            data-testid={`compose-comment-gutter-card-${thread.id}`}
          >
            <div className={styles.cardHeader}>
              {/* UAT round-5 #6 — one clear location line (no § / ¶ glyph soup), identical to the summary. */}
              <Text weight="semibold" size={200} className={styles.sectionRef}>
                {loc ?? 'Comment'}
              </Text>
              <div className={styles.cardHeaderRight}>
                {thread.riskLevel ? (
                  <Badge
                    appearance="tint"
                    size="small"
                    color={riskBadgeColor(thread.riskLevel)}
                    data-testid={`compose-comment-gutter-risk-${thread.id}`}
                  >
                    {thread.riskLevel}
                  </Badge>
                ) : null}
                {/* UAT round-8 #3/#4/#5 — the ⋮ AI-tools menu for THIS Review Note. stopPropagation keeps
                    opening the menu from selecting/expanding the card. */}
                {onRunNoteTool && noteTools && noteTools.length > 0 ? (
                  <Menu positioning="below-end">
                    <MenuTrigger disableButtonEnhancement>
                      <Tooltip content="AI tools for this note" relationship="label" withArrow>
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<MoreVertical20Regular />}
                          className={styles.toolsButton}
                          aria-label="AI tools for this note"
                          onClick={e => e.stopPropagation()}
                          onKeyDown={e => e.stopPropagation()}
                          data-testid={`compose-comment-gutter-tools-${thread.id}`}
                        />
                      </Tooltip>
                    </MenuTrigger>
                    <MenuPopover onClick={e => e.stopPropagation()}>
                      <MenuList>
                        {noteTools.map(tool => (
                          <MenuItem
                            key={tool.id}
                            icon={<SparkleRegular />}
                            onClick={e => {
                              e.stopPropagation();
                              onRunNoteTool(thread.id, tool.id);
                            }}
                            data-testid={`compose-comment-gutter-tool-${thread.id}-${tool.id}`}
                          >
                            {tool.label}
                          </MenuItem>
                        ))}
                      </MenuList>
                    </MenuPopover>
                  </Menu>
                ) : null}
              </div>
            </div>
            {/* UAT round-5 #7 / round-6 #5 / round-7 — ALWAYS split into the renamed "Flagged clause" /
                "Assessment says" labelled paragraphs, collapsed (first segment, truncated) AND expanded
                (every segment, full). The reviewer must never see the model's raw labels. */}
            <div className={styles.noteSegments} data-testid={`compose-comment-gutter-body-${thread.id}`}>
              {segments.map((seg, i) => (
                <div key={i} className={styles.noteSegment}>
                  {seg.label ? (
                    <Text size={200} weight="semibold" className={styles.noteSegmentLabel}>
                      {seg.label}
                    </Text>
                  ) : null}
                  <Text size={200} className={styles.noteSegmentBody}>
                    {seg.body}
                  </Text>
                </div>
              ))}
            </div>
            {thread.standardRef ? (
              resolveStandardText ? (
                <StandardRefChip
                  standardRef={thread.standardRef}
                  resolve={resolveStandardText}
                  className={styles.standardRefButton}
                  surfaceClassName={styles.standardPopover}
                  titleClassName={styles.standardPopoverTitle}
                  bodyClassName={styles.standardPopoverBody}
                  threadId={thread.id}
                />
              ) : (
                <Text size={100} className={styles.standardRef}>
                  Standard: {thread.standardRef}
                </Text>
              )
            ) : null}
            {/* UAT round-3 D2: double-down-arrow expandability cue. When selection is wired (round-4 #8)
                the card click SELECTS, so the cue becomes the dedicated expand button (stopPropagation
                keeps it from also selecting); otherwise it stays the passive cue and the whole card
                toggles expand. */}
            {isTruncatable ? (
              selectable ? (
                <button
                  type="button"
                  className={styles.expandCueButton}
                  aria-label={isExpanded ? 'Collapse comment' : 'Expand comment'}
                  aria-expanded={isExpanded}
                  onClick={e => {
                    e.stopPropagation();
                    toggleExpanded(thread.id);
                  }}
                  onKeyDown={e => e.stopPropagation()}
                  data-testid={`compose-comment-gutter-expand-${thread.id}`}
                >
                  <ChevronDoubleDown16Regular className={isExpanded ? styles.expandCueOpen : undefined} />
                </button>
              ) : (
                <div
                  className={styles.expandCue}
                  aria-hidden
                  data-testid={`compose-comment-gutter-expand-${thread.id}`}
                >
                  <ChevronDoubleDown16Regular className={isExpanded ? styles.expandCueOpen : undefined} />
                </div>
              )
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

ComposeCommentGutter.displayName = 'ComposeCommentGutter';

export default ComposeCommentGutter;
