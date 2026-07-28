/**
 * Fluent v9 styles for the ASSOCIATIONS review view (task 035). Split out of
 * `EmailConnectionsReview.tsx` at code-review time (Step 9.5) purely to keep
 * the main component file under this repo's review-metric line thresholds —
 * no behavior change. Semantic tokens only (ADR-021) — no hardcoded colors,
 * so both light and dark themes resolve correctly.
 */
import { makeStyles, tokens } from '@fluentui/react-components';

export const useConnectionsReviewStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  secHead: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  dot: { width: '8px', height: '8px', borderRadius: '50%', flexShrink: 0 },
  dotDecide: { backgroundColor: tokens.colorPaletteMarigoldForeground1 },
  dotFiled: { backgroundColor: tokens.colorPaletteGreenForeground1 },
  dotSuggest: { backgroundColor: tokens.colorBrandForeground1 },
  secTitle: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300, color: tokens.colorNeutralForeground1 },
  secCount: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  secHint: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200, lineHeight: tokens.lineHeightBase200 },

  options: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  opt: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `1px solid ${tokens.colorPaletteMarigoldBorder2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteMarigoldBackground1,
    cursor: 'pointer',
  },
  optSel: {
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
    boxShadow: `inset 0 0 0 1px ${tokens.colorBrandStroke1}`,
  },
  optRec: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  optName: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300, color: tokens.colorNeutralForeground1 },
  confLbl: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3, whiteSpace: 'nowrap' },

  row: {
    display: 'grid',
    gridTemplateColumns: '20px 1fr auto auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusMedium,
  },
  rowBorder: { borderTop: `1px solid ${tokens.colorNeutralStroke3}` },
  ico: { color: tokens.colorNeutralForeground3, display: 'flex', flexShrink: 0 },
  rec: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  recName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  recNum: { color: tokens.colorNeutralForeground2, fontVariantNumeric: 'tabular-nums', fontWeight: tokens.fontWeightRegular },
  typeTag: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase100, fontWeight: tokens.fontWeightRegular },
  rowActs: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end', alignItems: 'center' },
  recWhy: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  linkRow: { paddingTop: tokens.spacingVerticalXS },
  empty: { color: tokens.colorNeutralForeground3 },
});

export type ConnectionsReviewStyles = ReturnType<typeof useConnectionsReviewStyles>;
