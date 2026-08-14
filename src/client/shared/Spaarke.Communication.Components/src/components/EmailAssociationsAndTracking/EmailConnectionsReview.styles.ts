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

  // ── Reconcile variant (owner UAT 2026-08-14): candidate cards STACK vertically as
  //    full-width rows, exactly like the prototype's `tabBody > cand` list. NOT the
  //    multi-column grid below — that produced a cramped horizontal strip (owner
  //    screenshot 2026-08-14). Each compact card fills the row width. ──
  cardsStack: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 0 },

  // ── Candidate card grid (3 across when wide; wraps on narrow panes) ──
  cards: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
    gap: tokens.spacingHorizontalM,
    // Cells sized to content; every card box carries the same `minHeight: 72px` floor
    // and its text stays ONE line (identity is nowrap + ellipsis, the reason is clamped
    // to one line and the wording is streamlined), so all four card boxes render the
    // same size (owner UAT 2026-07-31). A selected card's Confirm button hangs BELOW
    // its box in the extra cell space without changing the box heights.
    alignItems: 'start',
  },
  // Right-hand column holding the "Link another record" affordance / picker.
  linkCol: { flexShrink: 0, alignSelf: 'flex-start', display: 'flex', alignItems: 'center', maxWidth: '100%' },
  // "Link another record" — a VISUAL SIBLING of the candidate cards: identical box
  // (border / radius / padding / neutral surface / hover). Layout per owner UAT
  // 2026-07-31: the LABEL is top-left aligned (like a candidate card's primary line)
  // and the SEARCH ICON is centered in the remaining space below. `flexGrow: 1` +
  // the grid's `align-items: stretch` make it exactly as tall as the sibling cards.
  linkCard: {
    boxSizing: 'border-box',
    width: '100%',
    minWidth: 0,
    minHeight: '72px',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'stretch',
    gap: tokens.spacingVerticalXXS,
    textAlign: 'left',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    // A native <button> does NOT inherit font-family (it defaults to the UA font,
    // which rendered as Arial) — the sibling cards are <div>s that inherit Segoe UI.
    // Force inheritance so the label matches the candidate cards (owner UAT item 3).
    fontFamily: 'inherit',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  // Top-left label — same size/weight as a candidate card's primary identity line.
  linkCardLabel: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // Search icon CENTERED in the space beneath the label (owner UAT 2026-07-31).
  linkCardIconRow: {
    display: 'flex',
    flexGrow: 1,
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
  },
  linkCardIcon: { flexShrink: 0, color: tokens.colorBrandForeground1, fontSize: '20px' },
  // One grid cell — the card itself plus its own Confirm slot directly beneath it.
  // Stretches to the row height (grid `align-items: stretch`); the card inside fills it.
  cardCell: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, minWidth: 0 },

  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    // Shared height floor so candidate / blank / link card boxes all render the same
    // size (content stays one line, so it never exceeds the floor) (owner UAT item 3).
    minHeight: '72px',
    textAlign: 'left',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  // ── Compact (reconcile variant) candidate card — the prototype's single-row layout
  //    (owner UAT 2026-08-14): `{name}` + `{type} · {n}% match` on the left, an inline
  //    "Confirm" button on the right, content-height (NO 72px floor) so the cards are the
  //    same compact size as the prototype. Selected/green highlight reuses the shared
  //    cardSelected/cardPrimary border+fill classes.
  candCompact: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    minWidth: 0,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  compactMeta: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    flex: '1 1 auto',
    cursor: 'pointer',
  },
  compactName: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  compactScore: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
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
    minHeight: '72px',
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
  // ── Card line 2 — plain-English match reason (clamped to ONE line so every card
  //    box stays the same height; the wording is streamlined to fit) ──
  cardReason: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    display: '-webkit-box',
    WebkitLineClamp: 1,
    WebkitBoxOrient: 'vertical',
  },

  // ── Per-card Confirm slot (shows directly under the selected card) ──
  confirmSlot: { display: 'flex' },

  // ── Confirmed primary chip: "{Type}: {number}" + remove (×). Styled clearly
  //    GREEN so the confirmed primary reads as distinct from any non-primary
  //    entry (owner UAT #9). Green tokens only — dark-mode correct (ADR-021). ──
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
    border: `${tokens.strokeWidthThin} solid ${tokens.colorPaletteGreenBorder2}`,
  },
  chipType: { color: tokens.colorPaletteGreenForeground2, fontSize: tokens.fontSizeBase200, flexShrink: 0 },
  chipValue: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Clickable variant of the chip value — opens the associated record (green to
  // match the confirmed-primary treatment; owner UAT #9).
  chipLink: {
    background: 'none',
    border: 'none',
    padding: 0,
    cursor: 'pointer',
    fontFamily: 'inherit',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorPaletteGreenForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '220px',
    ':hover': { color: tokens.colorPaletteGreenForeground2, textDecorationLine: 'underline' },
  },
  chipRemove: { minWidth: 'auto', paddingInline: 0, height: '20px' },

  // ── Reconcile variant (owner UAT round-3 2026-08-13) — prototype-parity layout ──
  // "Look up another record" as a LABELLED FIELD (owner: "lookup record as more of a
  // field"): a caption label above a full-width input-styled control that opens the
  // record-type menu → host polymorphic picker. Reads like the prototype's
  // `Field label="Look up another record"` row.
  lookupField: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  lookupFieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
  },
  // Input-look control: full width, neutral field surface + stroke, placeholder text
  // left, search glyph right. A button (opens the type menu) styled as a text field.
  lookupControl: {
    boxSizing: 'border-box',
    width: '100%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    fontFamily: 'inherit',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  lookupPlaceholder: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
  },
  lookupControlIcon: { flexShrink: 0, color: tokens.colorBrandForeground1, fontSize: '20px' },
  // "New record" as a FULL-WIDTH button (owner: "+New record as a full width button").
  newRecordFullWidth: { width: '100%' },

  // ── Shared affordances ──
  actionsRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  linkRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
  },
  linkBtn: { paddingInline: 0 },
  hint: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  empty: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
});

export type ConnectionsReviewStyles = ReturnType<typeof useConnectionsReviewStyles>;
