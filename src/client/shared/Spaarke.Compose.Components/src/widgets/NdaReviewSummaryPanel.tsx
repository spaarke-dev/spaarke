/**
 * NdaReviewSummaryPanel.tsx — review-summary docked panel (ai-advanced-capabilities-nda-r1 task 030,
 * FR-07).
 *
 * Renders the fuller advisory review inside Compose — overall risk + the flagged-section list, each
 * with its citation (sectionRef + quotedText + standardRef) — directly in the Compose column.
 * **Compose is the single surface** (design.md "Surface" decision, spec.md resolved item): there is
 * NO separate Analysis widget in r1; this panel is the entire review-summary UI.
 *
 * A Fluent v9 docked panel mirroring {@link ./ComposeCommentThread.tsx}'s conventions verbatim:
 * `open` / `onClose` props, `makeStyles` + semantic `tokens.*` only (ADR-021), a header with a
 * dismiss button, and a scrollable list body. This component is presentational only — all data
 * (findings, the placement-failure count) is supplied by the host (`ComposeWorkspace.tsx`), which
 * captures it from the SAME `compose_advisory_comments` PaneEventBus event task 031's
 * `onAdvisoryComments` receiver already handles (ADR-040 render-follows-store: one ledgered
 * NDA-REVIEW result, two renderings — in-document Comments (031) and this panel (030) — never a
 * second server disposition or model call).
 *
 * DISCLAIMER (binding, per the NDA-REVIEW Action's `$comment-disclaimer`,
 * `infra/dataverse/actions/nda-review.action.json`): the not-legal-advice framing is a FIXED
 * platform constant this panel renders as a standing banner — deliberately NOT a model-generated
 * output field (a per-run disclaimer would be ungrounded free-form text, breaking the closed
 * `{overallRisk, flaggedSections[]}` output contract tasks 020/030/031/041 share).
 *
 * OVERALL RISK — task 032 (right-gutter comment layout) threaded the Action's own `overallRisk`
 * string across the wire (the `compose_advisory_comments` event now carries it —
 * `useNdaReviewAdvisoryCommentsBridge.ts` reads `result.overallRisk`, which it already typed but
 * previously dropped when dispatching). This panel now PREFERS the real, server-asserted
 * `overallRisk` prop when present; it falls back to deriving the max severity among the rendered
 * findings' `riskLevel` (the SAME rule the Action's own rubric uses server-side — "Derive overallRisk
 * from the flagged findings — it is at least as severe as the most severe finding",
 * `nda-review.action.json` systemPrompt) only when the real field is unavailable (an older event
 * payload, or a caller that hasn't wired the field). The banner label distinguishes the two: "Overall
 * risk" for the real field, "Overall risk (from findings)" for the derived fallback — never conflating
 * a derived value with a server-asserted one.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `ComposeCommentThread`/`ComposeFindReplace` are the only docked panels in Compose;
 *     neither renders a risk-rated, citation-bearing finding list — no overlap to extend.
 *   - Extension: the review summary is a DIFFERENT capability (read-only advisory digest of a whole-
 *     document AI review) from either sibling (comment threads / find-replace); folding it into
 *     either would blur their scope guards. A new docked panel, mirroring the SAME conventions, is
 *     the reuse-first move (no new panel framework — spec.md resolved item / task constraint).
 *   - Cost-of-doing-nothing: without it, the ONLY place the review's cited findings are visible is
 *     the Assistant's short bullet summary — an attorney reviewing IN Compose has no fuller,
 *     citation-bearing digest alongside the document (FR-07 / C7 fails).
 *
 * @see ./ComposeCommentThread.tsx — the docked-panel convention this mirrors
 * @see ./ComposeWorkspace.tsx — mount + data wiring (captures the same event 031 handles)
 * @see ./useComposeWorkspaceReceivers.ts — `onAdvisoryComments` (031) — the event this panel's data
 *      is a sibling rendering of
 * @see infra/dataverse/outputschemas/nda-review.schema.json — the closed `{overallRisk,
 *      flaggedSections[]}` output contract this panel renders
 * @see projects/ai-advanced-capabilities-nda-r1/spec.md FR-07
 */
import * as React from 'react';
import {
  Badge,
  Text,
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemRadio,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss16Regular, ArrowSort16Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
  // UAT round-5 #1 — the OUTER shell. The panel now lives INSIDE the editor's top region (below the
  // toolbar), IN-FLOW: opening the Review Summary EXPANDS this area and pushes the document down;
  // closing removes it. `position: relative` makes it the containing block for the absolutely-positioned
  // down-arrow FAB (so the FAB stays pinned at the panel's bottom edge, not scrolling with the findings).
  // UAT round-7 #6 — a clearly DISTINCT floating card, plainly separate from the document below: a
  // brand accent strip, a full border, rounded corners, an inset margin, and a strong shadow, on a
  // tinted surface. Reads unmistakably as "the review panel", not part of the document.
  wrapper: {
    position: 'relative',
    flexShrink: 0,
    margin: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderLeft: `4px solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow16,
    overflow: 'hidden', // keep the sticky header's corners inside the rounded card
  },
  panel: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    // UAT round-7 #3 — height is applied inline (user-resizable via the bottom handle); this is the
    // default cap. A compact TL;DR strip, not a full findings dump.
    maxHeight: '32vh',
    overflowY: 'auto',
    // UAT round-8 #1 — a MODERN thin scrollbar (the round-4 scrollbar-hidden + down-arrow FAB was
    // reverted per reviewer request). Thin, rounded, low-contrast thumb; transparent track.
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    '::-webkit-scrollbar': { width: '8px', height: '8px' },
    '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
    '::-webkit-scrollbar-thumb': {
      backgroundColor: tokens.colorNeutralStroke1,
      borderRadius: '4px',
    },
    '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStrokeAccessible },
  },
  // UAT round-4 #2 / round-7 #2 — the title + sort control sit in a STICKY light-gray header bar that
  // stays pinned at the top of the card while the findings scroll beneath it.
  headerBar: {
    position: 'sticky',
    top: `calc(-1 * ${tokens.spacingHorizontalS})`, // cancel the scroller's top padding so it pins flush
    zIndex: 2,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalS,
    paddingInline: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalXS,
    marginInline: `calc(-1 * ${tokens.spacingHorizontalS})`,
    marginBlockStart: `calc(-1 * ${tokens.spacingHorizontalS})`,
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // UAT round-7 #3 — the bottom drag strip to resize the Review Summary height.
  bottomResizeHandle: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    height: '6px',
    cursor: 'row-resize',
    zIndex: 4,
    ':hover': { backgroundColor: tokens.colorNeutralStroke1 },
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  disclaimer: {
    display: 'flex',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
  disclaimerIcon: {
    flexShrink: 0,
    marginTop: '2px',
    color: tokens.colorNeutralForeground2,
  },
  failureNotice: {
    color: tokens.colorPaletteYellowForeground1,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  // One TL;DR row per flagged finding. A native button (reset to inherit) so the WHOLE row is a
  // click target that jumps to the clause; the static variant (no `onNavigate` wired) drops the
  // affordances. Two lines max: header (section + risk + chevron) then a one-line explanation.
  findingRow: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: '2px',
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    font: 'inherit',
    textAlign: 'left',
    width: '100%',
    boxSizing: 'border-box',
  },
  findingRowClickable: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      border: `1px solid ${tokens.colorNeutralStroke1}`,
    },
    ':active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  // UAT round-4 #6 / round-5 #5 — the row the reader last navigated to reads as ACTIVE with a YELLOW
  // fill, coordinated with the selected clause highlight + selected review note (round-5 #5: selected =
  // yellow everywhere) so the summary, the document, and the gutter stay visually in sync.
  findingRowActive: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
    border: `1px solid ${tokens.colorPaletteYellowBorderActive}`,
    ':hover': {
      backgroundColor: tokens.colorPaletteYellowBackground2,
      border: `1px solid ${tokens.colorPaletteYellowBorderActive}`,
    },
  },
  // UAT round-4 #5 / round-6 #3 — the down-arrow FAB pinned at the panel's bottom, CENTERED (round-6:
  // no longer off in the right corner); appears only while more findings sit below the fold.
  scrollDownFab: {
    position: 'absolute',
    left: '50%',
    transform: 'translateX(-50%)',
    bottom: tokens.spacingVerticalS,
    zIndex: 3,
    minWidth: '28px',
    width: '28px',
    height: '28px',
    padding: 0,
    boxShadow: tokens.shadow8,
  },
  findingHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalXS,
  },
  findingHeaderLeft: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  sectionRef: {
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  chevron: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
  },
  explanation: {
    color: tokens.colorNeutralForeground2,
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical',
    overflow: 'hidden',
  },
});

/** The not-legal-advice framing — a FIXED platform constant (see file header). */
export const NDA_REVIEW_DISCLAIMER_TEXT =
  'AI-generated advisory review — not legal advice. Verify every finding before relying on it; ' +
  'only High/Critical items are the attorney-review signal.';

/** UAT round-7 #3 — minimum draggable height + sessionStorage key for the Review Summary. */
const MIN_SUMMARY_HEIGHT_PX = 96;
const SUMMARY_HEIGHT_STORAGE_KEY = 'spaarke.compose.reviewSummaryHeight';

/** Closed severity order the Action's rubric uses ("at least as severe as the most severe finding"). */
const RISK_SEVERITY_ORDER = ['Low', 'Medium', 'High', 'Critical'] as const;
type RiskSeverity = (typeof RISK_SEVERITY_ORDER)[number];

function isRiskSeverity(value: string | undefined): value is RiskSeverity {
  return value !== undefined && (RISK_SEVERITY_ORDER as readonly string[]).includes(value);
}

/**
 * Maps a riskLevel/overallRisk string to the Fluent v9 semantic Badge color (ADR-021 — no hex).
 * Exported (task 032, right-gutter comment layout) — `ComposeCommentGutter.tsx` reuses this SAME
 * mapping for its per-card risk badge rather than re-deriving the severity→color rule a second time
 * (CLAUDE.md §11 reuse-first).
 */
export function riskBadgeColor(risk: string | undefined): 'success' | 'warning' | 'severe' | 'danger' | 'subtle' {
  switch (risk) {
    case 'Low':
      return 'success';
    case 'Medium':
      return 'warning';
    case 'High':
      return 'severe';
    case 'Critical':
      return 'danger';
    default:
      return 'subtle';
  }
}

/**
 * UAT round-4 (#3/#9) — split a free-form `sectionRef` into a section label + optional paragraph number
 * so the summary row and the review-note card can render the § (section) / ¶ (paragraph) markers. The
 * model's sectionRef is free text ("Section 4.2, para 2 (p. 3)" or "LIMITED USE…, para 1 (p. 1)"): pull
 * the paragraph number, strip the "para…"/"(p. N)" tail + a leading "Section " to get the section label.
 * Robust to any shape — an unparseable ref falls back to the whole string as the section label.
 */
export function formatSectionRef(sectionRef?: string): { section: string; paragraph?: string } {
  const text = (sectionRef ?? '').trim();
  if (!text) return { section: 'Unreferenced' };
  const paraMatch = /\bpara(?:graph)?\.?\s*(\d+)/i.exec(text);
  const paragraph = paraMatch ? paraMatch[1] : undefined;
  let section = text
    .replace(/,?\s*para(?:graph)?\.?\s*\d+.*$/i, '')
    .replace(/\s*\(p\.?\s*\d+\)\s*$/i, '')
    .replace(/^section\s+/i, '')
    .trim();
  if (!section) section = text;
  return { section, paragraph };
}

/**
 * UAT round-5 (#3/#6) — a single, clear location label from the model's free-text `sectionRef`,
 * REPLACING the confusing "§ Paragraph N (p. 1) ¶ N" glyph soup. Parses the page + paragraph and
 * renders "Pg {page} · Para {paragraph}" (omitting whichever part is absent). Used verbatim in BOTH the
 * summary rows and the right-gutter review-note cards so the two surfaces read identically (the UAT ask
 * "also use same in the Review Notes"). Falls back to the raw ref when nothing parses (never blank).
 *
 * NOTE: the section HEADING (e.g. "Agreement Not To Disclose Confidential Information") and section
 * number are NOT in the model output — deriving them needs the live editor document, which this
 * presentational panel does not have. They are added when the summary is hosted inside the editor
 * (round-5 #1 relocation). This helper covers the page/paragraph the model DOES emit, cleanly.
 */
export function formatClauseLocation(sectionRef?: string): string {
  const text = (sectionRef ?? '').trim();
  if (!text) return 'Unreferenced';
  const pageMatch = /\(?\bp(?:g|age)?\.?\s*(\d+)\)?/i.exec(text);
  const paraMatch = /\bpara(?:graph)?\.?\s*(\d+)/i.exec(text);
  // Whatever remains after stripping the page + paragraph tails and a leading "Section " is the
  // section identity — either a number ("4.2") or, when the model put the heading in the ref, the
  // section HEADING ("LIMITED USE OF CONFIDENTIAL INFORMATION"). Numeric labels get a "Sec " prefix;
  // heading labels render verbatim.
  const label = text
    .replace(/\(?\bp(?:g|age)?\.?\s*\d+\)?/i, '') // drop "(p. N)"
    .replace(/,?\s*para(?:graph)?\.?\s*\d+/i, '') // drop ", para N" / "Paragraph N"
    .replace(/^section\s+/i, '') // drop a leading "Section "
    .replace(/[,;\s]+$/, '')
    .trim();
  const parts: string[] = [];
  if (pageMatch) parts.push(`Pg ${pageMatch[1]}`);
  if (label) parts.push(/^[\d.]+$/.test(label) ? `Sec ${label}` : label);
  if (paraMatch) parts.push(`Para ${paraMatch[1]}`);
  return parts.length > 0 ? parts.join(' · ') : text;
}

/** Sort order for the summary findings (UAT round-4 #2/#4). */
export type NdaSummarySort = 'section' | 'risk';

/** One flagged-section finding — the CLOSED contract's 5 fields (nda-review.schema.json), minus the
 *  array-position identity. `quotedText`/`explanation` required (a finding is never emitted without
 *  them — see `projectFlaggedSectionsToAdvisoryComments`'s guard); the rest are optional so a
 *  partially-grounded finding never crashes the panel. */
export interface NdaReviewFindingSummary {
  /** Page/section/paragraph locator (e.g. "Section 4.2, para 2 (p. 3)"). */
  sectionRef?: string;
  /** Verbatim excerpt from the NDA — the document-span citation. */
  quotedText: string;
  /** Low | Medium | High | Critical severity of this finding. */
  riskLevel?: string;
  /** Advisory explanation — grounded fact + reasoned judgment. */
  explanation: string;
  /** The firm-standard clause this finding is measured against — the standard-side citation. */
  standardRef?: string;
  /**
   * UAT round-3 (S3/S4) — a short, crisp TL;DR headline for this finding (the takeaway, NOT the full
   * explanation). When the model supplies it (a future NDA_Review `takeaway` field), the summary
   * renders it verbatim; when absent, the panel derives one from {@link explanation} (see
   * {@link deriveTakeaway}). Kept distinct from the in-document comment, which carries the full
   * grounded-fact + judgment text.
   */
  takeaway?: string;
  /**
   * UAT round-5 #1/#3 — a fully-resolved location label ("Pg 1 · Sec 3 · Para 1 · Agreement Not To
   * Disclose Confidential Information") the HOST computes from the LIVE editor document (section
   * heading + ordinal, which the model's `sectionRef` lacks). When present the row renders it verbatim;
   * when absent the panel falls back to {@link formatClauseLocation} on `sectionRef`. See
   * {@link ../widgets/ndaClauseLocation.ts}.
   */
  locationLabel?: string;
  /**
   * UAT round-8 #2 — the resolved DOCUMENT POSITION of this finding's clause (the ProseMirror `from` of
   * its strict-matched span), supplied by the host from the live editor. "By section / paragraph" sorts
   * on this so findings run top→bottom in true document order — the model emits them out of order (e.g.
   * Preamble findings last), so sorting on emission index alone put late document sections first.
   */
  docPosition?: number;
}

/**
 * UAT round-3 (S2/S3/S4) — derive a short, scannable takeaway from a full advisory `explanation` when
 * the model didn't supply a dedicated {@link NdaReviewFindingSummary.takeaway}. The explanation is
 * authored as "Grounded fact — … Judgment — …"; the takeaway IS the judgment (the risk), with the
 * "Grounded fact — …" preamble dropped (S2), reduced to its first sentence and de-"This"-ed so it
 * reads as a headline rather than a copy of the comment (S3/S4). Falls back to the explanation itself
 * if neither marker is present.
 */
export function deriveTakeaway(explanation: string): string {
  const text = (explanation ?? '').trim();
  if (!text) return '';
  // Prefer the "Judgment —" portion (the takeaway); else drop a leading "Grounded fact —" preamble.
  const judgment = /judgment\s*[—:\-]\s*/i.exec(text);
  let core = judgment
    ? text.slice(judgment.index + judgment[0].length)
    : text.replace(/^grounded fact\s*[—:\-]\s*/i, '');
  core = core.trim();
  // First sentence only (keep it to one line) — clamp at the first sentence break that isn't
  // trivially short (avoids cutting a tiny lead like "Yes.").
  const sentenceEnd = core.search(/[.;]\s/);
  if (sentenceEnd >= 15) core = core.slice(0, sentenceEnd);
  // Drop a leading "This clause/agreement/NDA " and any trailing sentence punctuation so the line
  // reads as a headline, then capitalize.
  core = core
    .replace(/^this\s+(clause\s+|agreement\s+|nda\s+)?/i, '')
    .replace(/[.;]+\s*$/, '')
    .trim();
  if (!core) return text;
  return core.charAt(0).toUpperCase() + core.slice(1);
}

/** Derives an overall-risk band from the rendered findings — see the file header's derivation note. */
export function deriveOverallRisk(
  findings: readonly Pick<NdaReviewFindingSummary, 'riskLevel'>[]
): RiskSeverity | undefined {
  let worst: RiskSeverity | undefined;
  for (const finding of findings) {
    if (!isRiskSeverity(finding.riskLevel)) continue;
    if (worst === undefined || RISK_SEVERITY_ORDER.indexOf(finding.riskLevel) > RISK_SEVERITY_ORDER.indexOf(worst)) {
      worst = finding.riskLevel;
    }
  }
  return worst;
}

export interface NdaReviewSummaryPanelProps {
  /** Whether the panel is mounted/visible. When false, this component renders nothing. */
  open: boolean;
  /** Called when the user dismisses the panel (close button). */
  onClose: () => void;
  /** The flagged-section findings from the ledgered NDA-REVIEW result (task 020's closed contract). */
  findings: readonly NdaReviewFindingSummary[];
  /**
   * Count of advisory comments that could NOT be anchored in the live document (from
   * `ComposeEditorHandle.placeAdvisoryComments`'s `failed` list — task 031). Optional/omit when
   * unavailable or zero; the panel simply omits the notice (nice-to-have, not an acceptance
   * criterion).
   */
  placementFailureCount?: number;
  /**
   * @deprecated UAT round-5 #2 — the "Overall risk" banner was REMOVED (low value, UI space). The prop
   * is retained (ignored) so existing callers compile unchanged; it no longer renders anything.
   */
  overallRisk?: string;

  /**
   * UAT round-2 item #3 — invoked when the reader clicks a TL;DR row, to jump the document to that
   * flagged clause. The host (`ComposeWorkspace`) wires this to the editor's cited-span primitive
   * (`highlightCitedSpan`: strict resolve + ephemeral highlight + scrollIntoView) keyed on the
   * finding's `quotedText`. Omit for a non-interactive summary (rows render as static cards).
   */
  onNavigate?: (finding: NdaReviewFindingSummary) => void;
}

export function NdaReviewSummaryPanel(props: NdaReviewSummaryPanelProps): React.JSX.Element | null {
  const { open, onClose, findings, placementFailureCount, onNavigate } = props;
  const styles = useStyles();
  // UAT round-4 #4 — DEFAULT to document order (section/paragraph/sentence), not risk. The model emits
  // findings in document order, so the original index IS that order. #2 lets the reader switch to risk.
  const [sortBy, setSortBy] = React.useState<NdaSummarySort>('section');
  // UAT round-4 #6 — the finding index the reader last navigated to; that row renders as ACTIVE so the
  // summary and the document stay in sync about "which finding are we on".
  const [activeIndex, setActiveIndex] = React.useState<number | null>(null);

  // UAT round-8 #1 — the panel scrolls with a modern thin scrollbar; the down-arrow FAB + its
  // scroll-position measurement were removed. `scrollRef` is retained for the bottom-resize drag.
  const scrollRef = React.useRef<HTMLDivElement | null>(null);

  // UAT round-7 #3 — the Review Summary is resizable from the bottom (like the Review Notes). `null` =
  // the default 32vh cap; a number caps the scroller height. Persisted in sessionStorage.
  const [panelHeight, setPanelHeight] = React.useState<number | null>(() => {
    if (typeof window === 'undefined') return null;
    const stored = window.sessionStorage.getItem(SUMMARY_HEIGHT_STORAGE_KEY);
    const parsed = stored ? Number.parseInt(stored, 10) : Number.NaN;
    return Number.isFinite(parsed) ? parsed : null;
  });
  const heightDragRef = React.useRef<{ startY: number; startHeight: number } | null>(null);
  const onHeightPointerDown = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    heightDragRef.current = { startY: e.clientY, startHeight: scrollRef.current?.offsetHeight ?? 0 };
  }, []);
  const onHeightPointerMove = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    const drag = heightDragRef.current;
    if (!drag) return;
    const next = Math.max(MIN_SUMMARY_HEIGHT_PX, drag.startHeight + (e.clientY - drag.startY));
    setPanelHeight(next);
    if (typeof window !== 'undefined') window.sessionStorage.setItem(SUMMARY_HEIGHT_STORAGE_KEY, String(next));
  }, []);
  const onHeightPointerUp = React.useCallback((e: React.PointerEvent<HTMLDivElement>): void => {
    heightDragRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }, []);

  if (!open) return null;

  // UAT round-4 #2/#4 + round-8 #2 — sort by DOCUMENT POSITION (default) or by risk severity. "By
  // section" now orders on each finding's resolved `docPosition` (top→bottom in the real document), NOT
  // the model's emission order (which is out of document order). Findings whose clause couldn't be
  // resolved (no docPosition) fall to the end, stable on emission index. Non-mutating.
  const docOrder = (f: NdaReviewFindingSummary): number =>
    f.docPosition ?? Number.MAX_SAFE_INTEGER;
  const sortedFindings = findings
    .map((finding, index) => ({ finding, index }))
    .sort((a, b) => {
      if (sortBy === 'risk') {
        const sa = isRiskSeverity(a.finding.riskLevel) ? RISK_SEVERITY_ORDER.indexOf(a.finding.riskLevel) : -1;
        const sb = isRiskSeverity(b.finding.riskLevel) ? RISK_SEVERITY_ORDER.indexOf(b.finding.riskLevel) : -1;
        if (sb !== sa) return sb - sa;
        return docOrder(a.finding) - docOrder(b.finding); // risk tiebreak = document order
      }
      // "By section / paragraph" = true document order, then emission index as a stable tiebreak.
      const byDoc = docOrder(a.finding) - docOrder(b.finding);
      return byDoc !== 0 ? byDoc : a.index - b.index;
    });

  return (
    <div className={styles.wrapper}>
      <div
        ref={scrollRef}
        className={styles.panel}
        style={panelHeight != null ? { maxHeight: `${panelHeight}px` } : undefined}
        role="complementary"
        aria-label="NDA review summary"
        data-testid="nda-review-summary-panel"
      >
        <div className={styles.headerBar}>
        <Text weight="semibold">Review Summary</Text>
        <div className={styles.headerActions}>
          {/* UAT round-4 #2 — sort by section (default) or risk. */}
          <Menu
            checkedValues={{ sort: [sortBy] }}
            onCheckedValueChange={(_e, data) => {
              const next = data.checkedItems[0];
              if (next === 'section' || next === 'risk') setSortBy(next);
            }}
          >
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content={`Sort by ${sortBy === 'risk' ? 'risk' : 'section'}`} relationship="label" withArrow>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ArrowSort16Regular />}
                  aria-label="Sort findings"
                  data-testid="nda-review-summary-sort"
                />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItemRadio name="sort" value="section" data-testid="nda-review-summary-sort-section">
                  By section / paragraph
                </MenuItemRadio>
                <MenuItemRadio name="sort" value="risk" data-testid="nda-review-summary-sort-risk">
                  By risk
                </MenuItemRadio>
              </MenuList>
            </MenuPopover>
          </Menu>
          <Tooltip content="Close review summary" relationship="description" withArrow>
            <Button
              appearance="subtle"
              size="small"
              icon={<Dismiss16Regular />}
              aria-label="Close review summary"
              onClick={onClose}
              data-testid="nda-review-summary-close"
            />
          </Tooltip>
        </div>
      </div>

      {/* UAT round-6 #4 — the not-legal-advice disclaimer banner was REMOVED from the panel body; the
          warning now lives behind the info (ⓘ) button on the far right of the editor toolbar (the
          `NDA_REVIEW_DISCLAIMER_TEXT` constant is still exported for it). */}

      {placementFailureCount && placementFailureCount > 0 ? (
        <Text size={200} className={styles.failureNotice} data-testid="nda-review-summary-placement-failures">
          {placementFailureCount} finding{placementFailureCount === 1 ? '' : 's'} could not be anchored as an
          in-document comment — the citation below still shows what was flagged.
        </Text>
      ) : null}

      <div className={styles.list}>
        {sortedFindings.length === 0 ? (
          <Text size={200} className={styles.empty} data-testid="nda-review-summary-empty">
            No flagged sections yet. Run NDA Review on this document to see findings here.
          </Text>
        ) : (
          sortedFindings.map(({ finding, index }) => {
            // Concise TL;DR row: section + risk on line 1, a 2-line-clamped explanation on line 2. The
            // full quote + firm-standard citation live on the in-document Review Note (gutter card) —
            // the summary is a scan strip, deliberately NOT a second copy of the comments (item #3).
            const navigable = Boolean(onNavigate && finding.quotedText);
            // UAT round-5 #3 — one clear location line (no § / ¶ glyph soup); #4 — no chevron (the
            // whole row is obviously clickable). Uses the SAME formatter the gutter notes use (#6).
            const header = (
              <div className={styles.findingHeader}>
                <Text weight="semibold" size={200} className={styles.sectionRef}>
                  {finding.locationLabel ?? formatClauseLocation(finding.sectionRef)}
                </Text>
                {finding.riskLevel ? (
                  <Badge appearance="tint" color={riskBadgeColor(finding.riskLevel)}>
                    {finding.riskLevel}
                  </Badge>
                ) : null}
              </div>
            );
            // S2/S3/S4: render the short takeaway (model-supplied when available, else derived) — NOT
            // the full grounded-fact+judgment explanation, which lives on the in-document comment.
            const explanation = (
              <Text size={200} className={styles.explanation}>
                {finding.takeaway ?? deriveTakeaway(finding.explanation)}
              </Text>
            );
            const testId = `nda-review-summary-finding-${index}`;
            const isActive = activeIndex === index; // UAT round-4 #6
            return navigable ? (
              <button
                key={index}
                type="button"
                className={`${styles.findingRow} ${styles.findingRowClickable}${isActive ? ` ${styles.findingRowActive}` : ''}`}
                onClick={() => {
                  setActiveIndex(index); // UAT round-4 #6 — mark this the active/navigated-to row
                  onNavigate?.(finding);
                }}
                aria-current={isActive ? 'true' : undefined}
                aria-label={`Go to ${finding.sectionRef ?? 'flagged section'} in the document`}
                data-testid={testId}
              >
                {header}
                {explanation}
              </button>
            ) : (
              <div key={index} className={styles.findingRow} data-testid={testId}>
                {header}
                {explanation}
              </div>
            );
          })
        )}
        </div>
      </div>
      {/* UAT round-8 #1 — the down-arrow FAB was REMOVED; the panel now shows a modern thin scrollbar. */}
      {/* UAT round-7 #3 — drag the bottom edge to resize the Review Summary height. */}
      <div
        className={styles.bottomResizeHandle}
        onPointerDown={onHeightPointerDown}
        onPointerMove={onHeightPointerMove}
        onPointerUp={onHeightPointerUp}
        role="separator"
        aria-orientation="horizontal"
        aria-label="Resize the Review Summary height"
        data-testid="nda-review-summary-resize-bottom"
      />
    </div>
  );
}

NdaReviewSummaryPanel.displayName = 'NdaReviewSummaryPanel';

export default NdaReviewSummaryPanel;
