# UI Design Standards — Section Header & List Row Token Spec

> **Status**: Active
> **Created**: 2026-07-20 by `email-communication-solution-r4` task 101 (UAT R2 A2)
> **Audience**: Anyone building a section header or a scannable list/row surface on any Spaarke client surface (PCF controls, Code Pages, shared `@spaarke/ui-components`, Office Add-ins)
> **Binding rule**: [ADR-021](../adr/ADR-021-fluent-ui-design-system.md) — Fluent v9 **theme tokens only**. Never hardcode the hex/px literals below into component styles; use the **token names** so both light and dark themes resolve correctly.
> **Reference implementation**: `src/client/pcf/CommunicationAttachments/` — `CommunicationAttachmentsApp.tsx` (`sectionHeader` style) + `AttachmentList.tsx` (`row` style).

---

## Why this exists

Spaarke surfaces (PCF lists, workspace widgets, Code Page panels) were drifting on
header typography and row density — some used uppercase "kicker" labels, some used
`fontSizeBase200`, some hardcoded `#242424` or `14px`. This standard fixes the
canonical **section header** and **list row** token spec so every list-style surface
reads as one system and stays theme-correct in dark mode.

The values below are expressed as **Fluent v9 token names**. The px/hex in the
"resolves to (light)" column is informational only — you MUST reference the token,
never the literal.

---

## Section header

The label that titles a list section (e.g. "Attachments", "Connections", "Related").

| Property | Token | Resolves to (light) | Notes |
|---|---|---|---|
| Font size | `tokens.fontSizeBase300` | `14px` | Segoe UI base body size |
| Font weight | `tokens.fontWeightSemibold` | `600` | Semibold, not bold |
| Color | `tokens.colorNeutralForeground1` | `#242424` | Primary neutral foreground; inverts correctly in dark |
| Line height | `tokens.lineHeightBase300` | `20px` | Pairs with `fontSizeBase300` |

**Do NOT**: uppercase the label, add letter-spacing, use `colorNeutralForeground3`
(the muted "kicker" treatment), or hardcode `#242424` / `14px`.

```ts
// makeStyles — section header
sectionHeader: {
  fontSize: tokens.fontSizeBase300,
  fontWeight: tokens.fontWeightSemibold,
  color: tokens.colorNeutralForeground1,
  lineHeight: tokens.lineHeightBase300,
},
```

---

## List row

A single scannable row in a dense list surface (icon + name + type/badge + actions).

| Property | Token / value | Resolves to | Notes |
|---|---|---|---|
| Min row height | `20px` (`minHeight`) | `20px` | Compact, dense list line |
| Padding top | `tokens.spacingVerticalXS` | `4px` | |
| Padding bottom | `tokens.spacingVerticalXS` | `4px` | |
| Horizontal padding | `tokens.spacingHorizontalL` | `16px` | Section gutter (tune per surface) |
| Row separator | `tokens.colorNeutralStroke2` + `tokens.strokeWidthThin` | — | Optional bottom border for scan-ability |
| Hover background | `tokens.colorNeutralBackground1Hover` | — | For interactive (openable) rows only |

`minHeight: '20px'` is the row-content floor with `4px` top + `4px` bottom padding
of breathing room. `20px` is the only raw px literal in this spec — it is a layout
dimension (not a color or type token), so it is theme-invariant and acceptable to
express directly; everything else MUST be a token.

```ts
// makeStyles — list row
row: {
  display: 'flex',
  alignItems: 'center',
  gap: tokens.spacingHorizontalM,
  minHeight: '20px',
  paddingTop: tokens.spacingVerticalXS,
  paddingBottom: tokens.spacingVerticalXS,
  paddingInline: tokens.spacingHorizontalL,
  borderBottomWidth: tokens.strokeWidthThin,
  borderBottomStyle: 'solid',
  borderBottomColor: tokens.colorNeutralStroke2,
  ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
},
```

---

## Dark-mode correctness

Because every color above is a **semantic token**, the surface inverts automatically
when the host `FluentProvider` is given a dark theme (Spaarke PCFs resolve this via
`resolveThemeWithUserPreference`). `colorNeutralForeground1` → light foreground on a
dark background; `colorNeutralStroke2` / `colorNeutralBackground1Hover` follow suit.
If you hardcode `#242424`, the header goes invisible-on-dark — that is exactly the
regression this standard prevents.

---

## Related

- [ADR-021 — Fluent UI design system](../adr/ADR-021-fluent-ui-design-system.md) (semantic tokens, dark-mode parity)
- [`.claude/constraints/pcf.md`](../../.claude/constraints/pcf.md) — "no hard-coded colors" MUST rule
- [`src/client/pcf/CLAUDE.md`](../../src/client/pcf/CLAUDE.md) — PCF module standards (links here)
