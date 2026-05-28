---
description: Author or modify any Fluent UI v9 React component across Spaarke surfaces (Spaarke.UI.Components, code-pages, external-spa, office-addins, PCF). Loads patterns + Spaarke conventions.
tags: [ui, fluent-ui, fluent-v9, react, component, theming, griffel, accessibility, pcf-ui]
techStack: [react, typescript, fluent-ui-v9, griffel]
appliesTo: ["**/Spaarke.UI.Components/**/*.tsx", "**/Spaarke.UI.Components/**/*.ts", "**/code-pages/**/components/**/*.tsx", "**/external-spa/**/*.tsx", "**/office-addins/**/*.tsx", "**/pcf/**/components/**/*.tsx"]
alwaysApply: false
---

# fluent-v9-component

> **Category**: Development
> **Last Updated**: 2026-05-26

---

## Purpose

Anchor every Fluent UI v9 component-authoring task — new component, theming change, accessibility audit, portal-component use, React-version bump — in Spaarke's curated patterns and the live Microsoft + MVP reference. Without this skill, generated UI drifts toward: raw hex colors instead of `tokens.*`, hooks-API where slots suffice, missing FluentProvider re-wrap on portal components (the #1 dark-mode regression), or React-18-only APIs in code that has to run inside a PCF.

This skill is the loader for `.claude/patterns/ui/fluent-v9-*.md` + `.claude/patterns/pcf/fluent-v9-*.md`. It complements:
- `widget-design` (MCP App widgets specifically — uses Fluent v9 but has extra sandbox + host-bridge concerns)
- `pcf-deploy` (build + pack + import — runs AFTER the component is correct)

---

## Applies When

- Creating or modifying a component in `src/client/shared/Spaarke.UI.Components/`
- Editing a `.tsx` under `src/client/{code-pages,external-spa,office-addins}/`
- Editing a PCF `components/` `.tsx` (PCF lifecycle itself → use `pcf-deploy` for deploy)
- User prompts mentioning: "Fluent", "Fluent UI", "Fluent v9", "build a component", "theming", "FluentProvider", "Griffel", "makeStyles", "dark mode", "Portal", "Popover/Tooltip/Dialog/Menu/Toast"
- **NOT applicable** for:
  - MCP App widgets specifically → `widget-design` (sandbox + host-bridge specific concerns)
  - PCF build/pack/deploy mechanics → `pcf-deploy`
  - Choice dialog UI patterns → existing `.claude/patterns/ui/choice-dialog-pattern.md`

---

## Workflow

### Step 0 — Identify intent (route to the right patterns)

| Intent | Patterns to load |
|---|---|
| Author a new component | `ui/fluent-v9-component-authoring.md` + `ui/fluent-v9-theming.md` + `ui/fluent-v9-host-visual-fit.md` |
| Add/fix theming on existing component | `ui/fluent-v9-theming.md` + `ui/fluent-v9-host-visual-fit.md` (+ `ui/fluent-v9-portal-gotcha.md` if portal component) |
| Add a `Popover` / `Tooltip` / `Dialog` / `Menu` / `Toast` / `Combobox dropdown` / `Drawer` | `ui/fluent-v9-portal-gotcha.md` **MANDATORY** + `ui/fluent-v9-theming.md` |
| Author in `Spaarke.UI.Components` (cross-surface library) | `ui/fluent-v9-component-authoring.md` + `ui/fluent-v9-react-version-boundaries.md` + `ui/fluent-v9-host-visual-fit.md` |
| New PCF setup OR PCF manifest changes | `pcf/fluent-v9-modern-theming.md` + `pcf/fluent-v9-canvas-vs-mda-disabled.md` (if dual-target) + `ui/fluent-v9-host-visual-fit.md` |
| Code Page (React 18 SPA) styling | `ui/fluent-v9-host-visual-fit.md` (Code Pages have NO auto-theme inheritance) + drill into `knowledge/fluent-ui-v9/docs/host-code-pages-styling.md` |
| Surface-specific visual standards / "make it look native" | `ui/fluent-v9-host-visual-fit.md` + drill into `knowledge/fluent-ui-v9/docs/host-mda-modern-look.md` or `host-canvas-modern-theming.md` per surface |
| Bump React in any surface | `ui/fluent-v9-react-version-boundaries.md` |
| Accessibility audit | Drill into `knowledge/fluent-ui-v9/docs/accessibility.md` §2 checklist |

### Step 1 — Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/fluent-ui-v9/NOTES.md`** — Spaarke-specific commentary (currently a structured stub; still indicates the Spaarke-specific surface map + binding ADRs).
2. **The pattern files identified in Step 0** (typically 2-3 files, ~150 lines total).
3. **Only if a pattern's `→` link punts you there**, drill into `knowledge/fluent-ui-v9/docs/*.md` — use [`docs/INDEX.md`](../../../knowledge/fluent-ui-v9/docs/INDEX.md) to pick the right one. Don't load all 22 docs.
4. **Only when authoring something genuinely novel** (a wrapper around a Fluent v9 component the Spaarke codebase has never used), look at the upstream sample first: `knowledge/fluent-ui-v9/samples/fluentui_react-v9/<Category>/`.

**Budget**: typical activation loads ~400 lines / 4 KB. If you find yourself loading more, you're probably over-drilling — re-check Step 0.

### Step 2 — Apply Spaarke contracts (ADR-021, ADR-022, ADR-012)

- **ADR-021 (Fluent UI v9)**: NO `@fluentui/react` (v8). NO hard-coded colors. Dark mode required for MDA surfaces.
- **ADR-022 (React versions)**: PCF = React 16.14 (platform-provided), Code Pages = React 18 (bundled). NEVER mix in a single bundle. `Spaarke.UI.Components` MUST be 16.14-safe.
- **ADR-012 (Shared component library)**: If the component fits >1 surface, it lives in `Spaarke.UI.Components`. Don't duplicate.

### Step 3 — Code review checklist (apply to every PR touching Fluent v9 code)

- [ ] All colors / spacing / radius via `tokens.*`. No raw hex / `var(--...)` / rgb literals.
- [ ] `makeStyles` at module scope (not inside the component body).
- [ ] `mergeClasses(componentClasses, props.className)` — `props.className` LAST.
- [ ] CSS shorthand properties (`border`, `padding`, `margin`) use `shorthands.*` helpers.
- [ ] Any `Popover` / `Tooltip` / `Toast` / `Dialog` / `Menu` / `Combobox dropdown` / `Drawer` is paired with a `FluentProvider` re-wrap inside the portal surface, OR `applyStylesToPortals={true}` on the root (be explicit either way).
- [ ] PCF surfaces read `context.fluentDesignLanguage?.tokenTheme` and apply via `FluentProvider`; falls back to `webLightTheme` only when undefined.
- [ ] If the component renders in MDA AND supports disabled state, it uses `readOnly` + neutral-stroke theme override (not native `disabled`). See `pcf/fluent-v9-canvas-vs-mda-disabled.md`.
- [ ] If shipping in `Spaarke.UI.Components`: no `createRoot`, no React-18-only hooks, no `JSX.Element` (use `JSXElement` from `@fluentui/react-components` for cross-version compat).
- [ ] No `teamsHighContrastTheme` — Windows High Contrast is automatic in Fluent v9.

### Step 4 — Verify before claiming done

For UI changes specifically, type-checking and tests prove **code correctness**, not **feature correctness**. State explicitly in the PR description whether the change was verified visually (in a browser / MDA / Canvas), and on which themes (light + dark + the customer-tenant theme if relevant). If visual verification wasn't done, say so honestly — don't claim "ready" until someone validates the rendering.

---

## Related skills

- `widget-design` — MCP App widget design (uses Fluent v9 + this skill's patterns, adds sandbox + host-bridge contract)
- `pcf-deploy` — PCF build/pack/deploy mechanics; loads `pcf/fluent-v9-modern-theming.md` for manifest setup
- `spaarke-conventions` — cross-cutting code conventions; tags overlap intentionally

## Related references

- [`.claude/patterns/ui/INDEX.md`](../../patterns/ui/INDEX.md)
- [`.claude/patterns/pcf/INDEX.md`](../../patterns/pcf/INDEX.md)
- [`knowledge/fluent-ui-v9/docs/INDEX.md`](../../../knowledge/fluent-ui-v9/docs/INDEX.md) — verbose archive (drill-down)
- [`knowledge/fluent-ui-v9/NOTES.md`](../../../knowledge/fluent-ui-v9/NOTES.md) — Spaarke-specific commentary (Phase 4 stub)
- [`knowledge/fluent-ui-v9/SOURCE.md`](../../../knowledge/fluent-ui-v9/SOURCE.md) — provenance + GAPs
