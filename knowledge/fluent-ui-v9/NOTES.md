> ⚠️ STUB — senior engineer review pending

# NOTES — `fluent-ui-v9`

Project-specific commentary on Microsoft Fluent UI React v9 and Fluent 2. Annotate from real Spaarke project experience; don't fabricate. Section structure:

- **§1. How this fits Spaarke's architecture** — surface map, version inventory, decision criteria (v8 vs v9, native v9 vs platform-library v9, custom theme vs `tokenTheme`)
- **§2. How we build with it** — component-authoring conventions, theming conventions, PCF integration, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. Remove the `⚠️ STUB` banner only after both §1 and §2 have substantive content. Substance comes from the senior engineer annotation pass — do not fabricate guidance; replace each `TODO` with project-specific commentary backed by reading the curated samples + docs and tracing against Spaarke code:

- `src/client/shared/Spaarke.UI.Components/` (shared lib, `@fluentui/react-components ^9.73.2`)
- `src/client/pcf/{ControlName}/control/index.ts` + `src/client/pcf/{ControlName}/control/components/`
- `src/client/code-pages/{Page}/src/main.tsx` (FluentProvider bootstrap)
- `src/client/external-spa/`
- `src/client/office-addins/`

---

## 1. How this fits Spaarke's architecture

### Spaarke surface map for Fluent UI v9

_TODO: Map every Spaarke surface that uses `@fluentui/react-components`. Note for each: hosting model (Code Page SPA / PCF virtual / PCF non-virtual / Office Add-in / external SPA), React version, Fluent version, theming approach (platform-provided `fluentDesignLanguage.tokenTheme` vs custom `FluentProvider`). Mark drift candidates — e.g. `DocumentRelationshipViewer` PCF on `^9.46.2` vs shared lib on `^9.73.2`._

### `Spaarke.UI.Components` as the shared component library

_TODO: When to consume vs extend vs author de-novo. The line between "this belongs in `Spaarke.UI.Components`" (cross-surface reuse) vs "this stays local to the consumer" (single-PCF or single-page). Touch on Slot vs hook-API choice for custom components — see `slots-architecture.md` "When to use slots / When NOT to use slots"._

### PCF decision tree — virtual vs non-virtual

_TODO: Default to `pac pcf init -fw react` virtual controls (platform libraries, ~234 KB production bundle per [`docs/community/birkelbach-virtual-pcfs-after-ga.md`](docs/community/birkelbach-virtual-pcfs-after-ga.md)). Document the Spaarke exceptions if any (e.g., Power Pages still requires non-virtual — track when that constraint lifts)._

### Theming approach — platform vs custom

_TODO: Default to `context.fluentDesignLanguage.tokenTheme` so customer-tenant themes (including dark mode + accessibility-driven contrast) propagate automatically. Document the Spaarke exceptions where a `FluentProvider` with a custom theme is justified (Spaarke brand palette? particular widget context?). Cross-reference [`docs/pcf-modern-theming.md`](docs/pcf-modern-theming.md) sections 1–4._

### Fluent UI v8 — only where forced

_TODO: Spaarke is v9-first. Where v8 still appears (legacy bits in Model-Driven form-side rendering for some field types — see Rivard's [`docs/community/itmustbecode-adapting-pcf-modern-look.md`](docs/community/itmustbecode-adapting-pcf-modern-look.md)), document the exact files and the migration trigger (e.g., when the new look is GA-default, retire the v8 path)._

### Cross-platform Fluent 2 (iOS, Android, WinUI)

_TODO: Currently no Spaarke implementation. If/when a native mobile or Windows surface is introduced, return to [`docs/fluent2-develop.md`](docs/fluent2-develop.md) for installation guidance and align design-token consumption with the React surfaces._

---

## 2. How we build with it

### `FluentProvider` placement conventions

_TODO: Where to mount `FluentProvider` per surface — Code Pages root (post-auth-bootstrap), PCF `updateView` return, Office Add-in entry, external SPA root. Cite the [`docs/quickstart.md`](docs/quickstart.md) React-18 pattern and Spaarke's `IdPrefixProvider` story (if it exists in any Spaarke PCF) for collision avoidance per Rivard._

### Portal-component wrapping (CRITICAL gotcha)

_TODO: ANY use of `Popover`, `Tooltip`, `Toast`, `Dialog`, `Menu` requires explicit `<FluentProvider>` re-wrap around the surface body, OR `applyStylesToPortals` configuration on the outer provider. Reference [`samples/fluentui_react-v9/Provider/FluentProviderApplyStylesToPortals.stories.tsx`](samples/fluentui_react-v9/Provider/FluentProviderApplyStylesToPortals.stories.tsx) and Birkelbach's Canvas/MDA example in [`docs/community/birkelbach-style-fluent-ui-9-pcfs.md`](docs/community/birkelbach-style-fluent-ui-9-pcfs.md). Add to code review checklist below._

### `makeStyles` conventions

_TODO: Module-scope hook definition (never inside the component), `tokens` for all color/spacing/radius values (NEVER raw hex / CSS variables — see [`docs/styling-griffel.md`](docs/styling-griffel.md) "Do not use CSS variables directly"). Mark Spaarke-specific naming conventions for `useStyles` slot keys if any. Note the `shorthands.*` helper requirement for `border`, `padding`, `margin` (Griffel rejects shorthand CSS properties)._

### `mergeClasses` ordering — parent overrides last

_TODO: `mergeClasses(componentClasses, props.className)` — props.className must come last so callers can override. Add to code review checklist._

### Disabled-state handling — Canvas vs MDA

_TODO: Canvas uses native `disabled`; MDA uses `readOnly` + a neutral-stroke theme override (Birkelbach pattern). Spaarke convention for `Spaarke.UI.Components` controls that ship to both: either (a) the Universal Input Control pattern from Birkelbach with an `isCanvas` manifest property, or (b) split control variants. Pick one and document._

### Custom themes (Spaarke brand palette)

_TODO: If Spaarke has its own brand ramp, document where the `BrandVariants` live (one file in `Spaarke.UI.Components`?), how `createLightTheme` / `createDarkTheme` are exported, and when surfaces opt-in (e.g., external SPA) vs default to `tokenTheme` (PCF in customer tenants)._

### Component authorship via slots vs hooks API

_TODO: For new `Spaarke.UI.Components` components — default to slot composition for "wrapper" components; fall back to the hooks API only when slots can't express the structure (see `slots-architecture.md` "When NOT to use slots"). Cite the slot composition pattern from [`samples/fluentui_react-v9/Button/ButtonAppearance.stories.tsx`](samples/fluentui_react-v9/Button/ButtonAppearance.stories.tsx)._

### React-version boundary

_TODO: PCF Canvas runtime is React 16.14 — see [`docs/react-version-support.md`](docs/react-version-support.md). Document Spaarke's stance: which packages can rely on React 17+ features (Code Pages, external SPA) vs which must stay 16.14-safe (PCF). If `Spaarke.UI.Components` is shared across both, it must be 16.14-safe → no `createRoot`, no React 18-only hooks._

### Accessibility checklist (added to PR template)

_TODO: Use [`docs/accessibility.md`](docs/accessibility.md) §2 to add a Spaarke-specific PR checklist for any UI changes — labels for icon-only buttons, focus-handling on async UI changes, high-contrast verification, keyboard tab-order check. Note: Fluent v9 + tabster handle most of this for free, but the **application-level** items (landmarks, programmatic focus moves, focus traps for dialogs) are still on us._

### Code review checklist (Spaarke-specific)

_TODO: Compile from above. Suggested starter:_

- [ ] All color/spacing/radius values use `tokens.*`; no raw hex / CSS variables.
- [ ] `makeStyles` is at module scope; never inside the component body.
- [ ] `mergeClasses(componentClasses, props.className)` — props.className is last.
- [ ] Any `Popover` / `Tooltip` / `Toast` / `Dialog` / `Menu` is re-wrapped in `FluentProvider` (or outer provider has `applyStylesToPortals`).
- [ ] PCFs use `pac pcf init -fw react` virtual mode unless explicitly documented exception.
- [ ] PCF reads `context.fluentDesignLanguage?.tokenTheme` and applies via `FluentProvider`; falls back gracefully when modern theming isn't enabled.
- [ ] If the component renders in MDA, `disabled` is `readOnly` + neutral-stroke theme (not native `disabled`).
- [ ] If shipping in `Spaarke.UI.Components`, no React 18-only APIs (must run under React 16.14 in PCF Canvas).
- [ ] No direct use of `@fluentui/react` (v8) unless the component lives in a legacy-only path with an explicit migration ticket.

### Gotchas to capture from real implementation

_TODO: Fill from first production refresh — Fluent version drift across PCFs (the `^9.46.2` vs `^9.73.2` split visible at curation time), Power Pages compatibility blockers (still no virtual PCF support per Birkelbach 2024-12), bundle-size budgets per surface, the `IdPrefixProvider` story if Spaarke ever hits DOM ID collision in nested controls, customer-tenant theming surprises._
