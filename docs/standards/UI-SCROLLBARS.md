# UI Standard — Thin Scrollbars

> **Created**: 2026-08-26 by `sdap-SPE-admin-app-r2` (UAT round 7)
> **Status**: implemented in `SpeAdminApp`; **not yet** promoted to `@spaarke/ui-components`
> **Reference implementation**: [`src/solutions/SpeAdminApp/src/components/layout/scrollbarStyles.ts`](../../src/solutions/SpeAdminApp/src/components/layout/scrollbarStyles.ts)

---

## The rule

Scrollable regions in Spaarke surfaces SHOULD use the modern thin scrollbar, not the host OS
default.

## Why this needs a standard at all

**Fluent UI v9 does not style scrollbars.** It leaves them to the platform. A Dataverse-hosted code
page therefore inherits Windows' classic wide bar with stepper arrows, which reads as a decade older
than the Fluent surface around it. Nothing in the design system will fix this for you, and nothing
warns you it is unstyled — you only notice when you look at a screenshot.

## The two mechanisms — both are required

They are **not** interchangeable, and shipping only one gives a bar whose appearance depends on the
viewer's browser version:

| Mechanism | Covers |
|---|---|
| `scrollbar-width` / `scrollbar-color` | Firefox; Chromium **121+** |
| `::-webkit-scrollbar-*` pseudo-elements | Safari; Chromium **before 121** |

Chromium ignores the standard properties when the pseudo-elements are present, so the two
declarations must describe the same appearance or the bar will change between versions.

## Colours

Use Fluent tokens — `tokens.colorNeutralStroke1` for the thumb, `transparent` for the track. Tokens
are CSS custom properties that `FluentProvider` sets on its root element, so they resolve for any
scrollable descendant **and follow dark mode with no second rule** (ADR-021). Never hard-code a
scrollbar colour; a fixed grey inverts wrongly in dark theme.

A transparent track is deliberate: it lets the bar sit over whatever surface owns it instead of
cutting a grey channel through panes and grids.

## How to apply it

`makeStaticStyles` emits global CSS, so it is called **once**, at the app root, inside the
`FluentProvider` subtree:

```tsx
export const App: React.FC = () => {
  useThinScrollbars();
  return <FluentProvider theme={theme}>{/* … */}</FluentProvider>;
};
```

## Why it is not in `@spaarke/ui-components` yet

`@spaarke/ui-components` is consumed by roughly sixteen solutions and every PCF control. A global
scrollbar rule shipped from there would restyle **all** of them at once, none of which would have
been UAT'd for it.

Promoting it is a small, deliberate change:

1. Move `scrollbarStyles.ts` into `@spaarke/ui-components` and export `useThinScrollbars` from the
   barrel.
2. Call it in each host's root component.
3. Visually spot-check one PCF control and one code page in **both** themes before merging.

Do that as its own change with its own test pass — not as a side effect of an unrelated one. Until
then, a new surface that wants thin scrollbars copies the file; it is ~30 lines with no
dependencies.

## Related

- [ADR-021 — Fluent Design System](../../.claude/adr/ADR-021-fluent-design-system.md) — tokens only,
  no hard-coded colours
- [`docs/standards/MODAL-DESIGN-SYSTEM.md`](MODAL-DESIGN-SYSTEM.md) — the comparable
  component-layer standard, already promoted to the shared library
