/**
 * Fluent v9 styles for the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, reading-pane MAIN-AREA redesign, section #6).
 * Semantic tokens only (ADR-021) — no hardcoded colors, so both light and dark
 * themes resolve correctly. The resolver answers ONE plain question with an
 * obvious action per state (clear-match / ambiguous / filed / suggested /
 * unmatched); these styles back that presentation.
 */
import { makeStyles, tokens } from '@fluentui/react-components';

export const useConnectionsReviewStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },

  // ── Shared lead-in question / labels ──
  question: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  groupLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.03em',
  },
  block: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },

  // ── "This email looks like it's about …" lead sentence (clear/possible match) ──
  leadText: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  strongName: { fontWeight: tokens.fontWeightSemibold },
  why: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  pct: { color: tokens.colorNeutralForeground2, fontVariantNumeric: 'tabular-nums', fontWeight: tokens.fontWeightSemibold },

  actionsRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  linkBtn: { paddingInline: 0 },

  // ── Ambiguous — ranked, selectable options (best pre-selected) ──
  options: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  opt: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
  },
  optSel: {
    border: `${tokens.strokeWidthThin} solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
    boxShadow: `inset 0 0 0 1px ${tokens.colorBrandStroke1}`,
  },
  optRec: { display: 'flex', flexDirection: 'column', minWidth: 0, gap: tokens.spacingVerticalXXS },
  optName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  recNum: { color: tokens.colorNeutralForeground2, fontVariantNumeric: 'tabular-nums', fontWeight: tokens.fontWeightRegular },

  // ── Filed — calm, silent confirmed rows ("✓ Filed to X" + Change / Remove) ──
  filedRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexWrap: 'wrap',
  },
  filedLead: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    flex: '1 1 auto',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  filedCheck: { color: tokens.colorPaletteGreenForeground1, display: 'flex', flexShrink: 0 },
  filedName: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground1 },
  typeTag: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase100, fontWeight: tokens.fontWeightRegular },
  rowActs: { display: 'flex', gap: tokens.spacingHorizontalS, justifyContent: 'flex-end', alignItems: 'center' },

  // ── Unmatched empty state ──
  unmatched: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  unmatchedText: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase300 },

  empty: { color: tokens.colorNeutralForeground3 },
  linkRow: { paddingTop: tokens.spacingVerticalXS },
});

export type ConnectionsReviewStyles = ReturnType<typeof useConnectionsReviewStyles>;
