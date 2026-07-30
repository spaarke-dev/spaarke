/**
 * Fluent v9 styles for the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, single-primary reading-pane redesign 2026-07-29).
 * Semantic tokens only (ADR-021) — no hardcoded colors, so both light and dark
 * themes resolve correctly. The resolver makes ONE primary association in three
 * states (🔴 requires review · 🟡 needs confirmation · 🟢 confirmed); these
 * styles back the 2-line candidate cards, the per-card Confirm slot, the
 * confirmed chip, and the "Link another record" row.
 */
import { makeStyles, tokens } from '@fluentui/react-components';

export const useConnectionsReviewStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },

  // ── Cards + "Link another" on ONE row (link to the right — saves vertical space) ──
  cardsRow: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'flex-start', flexWrap: 'wrap' },

  // ── Candidate card grid (3 across when wide; wraps on narrow panes) ──
  cards: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
    gap: tokens.spacingHorizontalM,
    alignItems: 'start',
  },
  // Right-hand column holding the "Link another record" affordance / picker.
  linkCol: { flexShrink: 0, alignSelf: 'flex-start', display: 'flex', alignItems: 'center', maxWidth: '100%' },
  // "Link another record" rendered as a CARD matching the candidate tiles, with a
  // search icon on the right (owner UAT) → opens the type dropdown + right-pane lookup.
  linkCard: {
    boxSizing: 'border-box',
    width: '200px',
    maxWidth: '100%',
    minHeight: '64px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    textAlign: 'left',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  linkCardLabel: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    // Not bold (owner UAT) — the link tile is a quiet affordance, not a match.
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
  },
  linkCardIcon: { flexShrink: 0, color: tokens.colorNeutralForeground3, fontSize: '20px' },
  // One grid cell — the card itself plus its own Confirm slot directly beneath it.
  cardCell: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, minWidth: 0 },

  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    textAlign: 'left',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  // 🔵 Selected (requires-review pick) — brand border/fill.
  cardSelected: {
    border: `${tokens.strokeWidthThin} solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': { backgroundColor: tokens.colorBrandBackground2Hover },
  },
  // 🟢 Auto-matched primary (needs-confirmation top card) — green border/fill.
  cardPrimary: {
    border: `${tokens.strokeWidthThin} solid ${tokens.colorPaletteGreenBorder2}`,
    backgroundColor: tokens.colorPaletteGreenBackground1,
    ':hover': { backgroundColor: tokens.colorPaletteGreenBackground2 },
  },
  // Blank slot — a candidate below the 70% floor (legitimately "no match here").
  cardBlank: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '64px',
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} dashed ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },

  // ── Card line 1 — "{REC#} : {name}" + % tag ──
  cardHeadRow: {
    display: 'flex',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  cardIdentity: {
    minWidth: 0,
    flex: '1 1 auto',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  recNum: {
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorNeutralForeground1,
  },
  cardName: { color: tokens.colorNeutralForeground2 },
  pctTag: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorNeutralForeground3,
  },
  // ── Card line 2 — plain-English match reason ──
  cardReason: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical',
  },

  // ── Per-card Confirm slot (shows directly under the selected card) ──
  confirmSlot: { display: 'flex' },

  // ── Confirmed chip: "{Type}: {number}" + remove (×) ──
  chipRow: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS, alignItems: 'center' },
  chip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    maxWidth: '100%',
    paddingBlock: tokens.spacingVerticalXXS,
    paddingInline: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteGreenBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorPaletteGreenBorder1}`,
  },
  chipType: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, flexShrink: 0 },
  chipValue: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Clickable variant of the chip value — opens the associated record.
  chipLink: {
    background: 'none',
    border: 'none',
    padding: 0,
    cursor: 'pointer',
    fontFamily: 'inherit',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorBrandForegroundLink,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '220px',
    ':hover': { color: tokens.colorBrandForegroundLinkHover, textDecorationLine: 'underline' },
  },
  chipRemove: { minWidth: 'auto', paddingInline: 0, height: '20px' },

  // ── Shared affordances ──
  actionsRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  linkRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, paddingTop: tokens.spacingVerticalXS },
  linkBtn: { paddingInline: 0 },
  hint: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  empty: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
});

export type ConnectionsReviewStyles = ReturnType<typeof useConnectionsReviewStyles>;
