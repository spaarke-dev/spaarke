/**
 * @spaarke/ai-widgets — InsightSummaryCard Griffel styles
 *
 * Module-scope `makeStyles` per `.claude/patterns/ui/fluent-v9-component-authoring.md`.
 * All colors / spacing / radius via `tokens.*` — NO hex codes, NO rgba literals,
 * NO `var(--...)` — per ADR-021. Dark mode is by-construction (semantic tokens
 * resolve correctly under both light + dark FluentProvider themes).
 *
 * Task 030 scaffold: the styles below cover the placeholder Card render
 * (root, header, body, footer). Task 031+ will extend with:
 *   - 6-state machine visuals (idle / loading / loaded / error / decline / stale)
 *   - Dialog modal expand surface (with Fluent v9 portal re-wrap per
 *     `.claude/patterns/ui/fluent-v9-portal-gotcha.md`)
 *   - Citation chip styles
 *   - KPI slot wrapper
 *
 * @see ADR-021 — Fluent v9 + semantic tokens (binding)
 */

import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

/**
 * Griffel style hook for {@link InsightSummaryCard}.
 *
 * Hook MUST be called inside the component body; the underlying `makeStyles`
 * registration is module-scope (not re-created per render).
 */
export const useInsightSummaryCardStyles = makeStyles({
  // ── Root surface ──────────────────────────────────────────────────────────
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    // Card primitive owns its own background + border tokens; we add the
    // gap + minimum height so the placeholder render is recognisable in
    // dev sandboxes (Task 035 SpaarkeAi-code-page playground).
    gap: tokens.spacingVerticalS,
    minHeight: '120px',
  },

  // ── Header row (topic label + optional KPI slot) ──────────────────────────
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    // Header sits on the Card's built-in surface — no additional background.
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  headerLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
  },

  // KPI slot is host-controlled content; we provide alignment + spacing only.
  kpiSlot: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
  },

  // ── Body region (placeholder for state-machine output in Task 031+) ───────
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    minHeight: '48px',
  },

  // Scaffold-only placeholder hint — distinct visual signal that this is
  // the pre-Task-031 state. Uses Foreground3 token so it reads as secondary
  // copy in both light + dark themes.
  scaffoldHint: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },

  // ── Footer row (metadata band — topic / subject / mode echo) ──────────────
  footerRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },

  footerCell: {
    display: 'flex',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    color: tokens.colorNeutralForeground3,
  },

  footerKey: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },

  footerValue: {
    // Use monospace-ish token for subject GUIDs / topic identifiers when
    // useful — Fluent v9 doesn't ship a true mono token, so we fall back to
    // a light neutral foreground; do NOT introduce a hard-coded font-family.
    color: tokens.colorNeutralForeground2,
    // Long subject GUIDs / parameter blobs must wrap, not overflow the card.
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    wordBreak: 'break-all',
  },

  // ── Popover surface (re-wrapped in FluentProvider — portal-gotcha) ────────
  popoverSurface: {
    width: '480px',
    maxWidth: '90vw',
    maxHeight: '480px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },

  popoverFooter: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'flex-end',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },

  // ── Dialog body (re-wrapped in FluentProvider — portal-gotcha) ────────────
  dialogBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minHeight: '240px',
    maxHeight: '60vh',
    overflowY: 'auto',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  // ── State-specific renderers ──────────────────────────────────────────────

  // Loading skeleton — pulsing token-driven blocks per FR-06.
  skeleton: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  skeletonBar: {
    height: '12px',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
  },

  skeletonBarShort: {
    width: '40%',
  },

  skeletonBarMedium: {
    width: '70%',
  },

  skeletonBarLong: {
    width: '95%',
  },

  // Error state — graceful message per FR-06 + ADR-032 (FeatureDisabled → 503).
  errorBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  errorDiagnostic: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },

  // Decline state — owner-confirmed FR-06 text + recommended action.
  declineBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  declineRecommendation: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  // Loaded / stale narrative.
  narrative: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  narrativeBody: {
    whiteSpace: 'pre-wrap',
  },

  // Stale banner — subtle warning that content is past TTL.
  staleBanner: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },

  // Idle affordance — visible inside the card body when no fetch has happened.
  idleAffordance: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },

  // ── Citation rendering (FR-07 / Task 033) ─────────────────────────────────

  // Container for the inline citations list; placed inside the loaded/stale
  // narrative block under the narrative body.
  citationsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },

  // "Citations" header label above the list.
  citationsHeader: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },

  // Single citation row — semantic token spacing + foreground only.
  citationItem: {
    display: 'flex',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground2,
  },

  // Type pill — visually marks `assessment` vs `document` (renders next
  // to the link). Uses Background3 / Foreground3 so it reads as secondary
  // in both light + dark themes.
  citationType: {
    paddingTop: '0px',
    paddingBottom: '0px',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusSmall,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    textTransform: 'lowercase',
  },

  // Unknown-type fallback — plain text label, no link affordance.
  citationUnknown: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});
