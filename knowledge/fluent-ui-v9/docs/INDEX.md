# `knowledge/fluent-ui-v9/docs/` — Index

> Use this index to decide WHICH doc to load. Most routine Fluent v9 work is covered by the **6 pattern files** in `.claude/patterns/ui/` and `.claude/patterns/pcf/` — only drill into these docs when the pattern punts to one with a `→` link, or when you need provenance / verbatim Microsoft text.

## Microsoft official — Fluent UI v9 (`react.fluentui.dev` MDX source)

| Doc | Lines | When to load |
|---|---:|---|
| [`overview.md`](./overview.md) | 30 | Orientation only — first-time context. |
| [`quickstart.md`](./quickstart.md) | 82 | Building a new React surface from scratch. |
| [`theming.md`](./theming.md) | 158 | Deep drill-down on theme construction OR designing a Spaarke brand theme. |
| [`styling-griffel.md`](./styling-griffel.md) | 160 | Drill-down on Griffel internals OR debugging style-precedence issues. |
| [`slots-architecture.md`](./slots-architecture.md) | 230 | Authoring a NEW component in `Spaarke.UI.Components` (hooks-API section essential). |
| [`react-version-support.md`](./react-version-support.md) | 137 | Drill-down on React 19 cross-version TS types OR bumping React in a surface. |
| [`accessibility.md`](./accessibility.md) | 67 | Any UI feature with user interaction OR accessibility audit (§2 checklist is the lift). |

## Microsoft official — PCF + Fluent v9 integration

| Doc | Lines | When to load |
|---|---:|---|
| [`pcf-modern-theming.md`](./pcf-modern-theming.md) | 102 | Drill-down on the 4 theming approaches OR setting up a NEW virtual PCF. |
| [`pcf-virtual-controls.md`](./pcf-virtual-controls.md) | 74 | Orientation only — background context for platform-library PCFs. |
| [`modern-theming-api-control-sample.md`](./modern-theming-api-control-sample.md) | 42 | Orienting around the `FluentThemingAPIControl` sample (mirrored in `../samples/`). |

## Host visual standards — "make it look native"

| Doc | Lines | When to load |
|---|---:|---|
| [`host-mda-modern-look.md`](./host-mda-modern-look.md) | 95 | Anything rendering inside MDA — the visual target. Floating command bar, Fluent field controls, Power Apps grid, custom-pages caveat (no auto-theme). |
| [`host-canvas-modern-theming.md`](./host-canvas-modern-theming.md) | 100 | Canvas Apps modern controls + themes — enabling, 16-slot brand ramp, YAML format, `App.Theme` Power Fx binding. Cross-surface brand consistency. |

Spaarke-specific Code Page convention (root `FluentProvider` mount, no auto-inheritance) lives in [`.claude/patterns/ui/fluent-v9-host-visual-fit.md`](../../../.claude/patterns/ui/fluent-v9-host-visual-fit.md) — there's no Microsoft doc for React 18 SPA web resources, so it lives with the pattern.

## Microsoft official — Fluent 2 design system (cross-platform)

| Doc | Lines | When to load |
|---|---:|---|
| [`fluent2-overview.md`](./fluent2-overview.md) | 45 | Orientation only — design-system context. |
| [`fluent2-develop.md`](./fluent2-develop.md) | 189 | Bringing Fluent 2 to a non-React surface (iOS/Android/WinUI). |
| [`fluent2-design-principles.md`](./fluent2-design-principles.md) | 38 | Design-review discussions OR justifying a UX choice. |
| [`fluent2-whats-new.md`](./fluent2-whats-new.md) | 29 | Orientation only — historical context. |

## Community / MVP (verify against live post before quoting verbatim)

| Doc | Author | Lines | When to load |
|---|---|---:|---|
| [`community/birkelbach-style-fluent-ui-9-pcfs.md`](./community/birkelbach-style-fluent-ui-9-pcfs.md) | Diana Birkelbach | 213 | **Canvas-vs-MDA decisions** OR disabled-state styling drift. |
| [`community/birkelbach-virtual-pcfs-after-ga.md`](./community/birkelbach-virtual-pcfs-after-ga.md) | Diana Birkelbach | 63 | Power Pages compatibility question OR validating virtual PCF baseline versions. |
| [`community/birkelbach-standard-custom-theming.md`](./community/birkelbach-standard-custom-theming.md) | Diana Birkelbach | 66 | Designing a Spaarke brand-palette theme OR adding dark-mode detection. |
| [`community/itmustbecode-develop-pcf-fluentui-v9.md`](./community/itmustbecode-develop-pcf-fluentui-v9.md) | David Rivard | 131 | Orientation context OR justifying the v8 → v9 migration boundary. |
| [`community/itmustbecode-adapting-pcf-modern-look.md`](./community/itmustbecode-adapting-pcf-modern-look.md) | David Rivard | 64 | **PCF must render correctly under BOTH old + new MDA looks** during transition. |
| [`community/ariclevin-virtual-pcf-fluent-v9.md`](./community/ariclevin-virtual-pcf-fluent-v9.md) | Aric Levin | 48 | Bootstrapping a brand-new PCF project (initial scaffold only). |
| [`community/gildea-whats-new-with-fluent-ui-react-v9.md`](./community/gildea-whats-new-with-fluent-ui-react-v9.md) | Paul Gildea | 71 | Orientation only — v9 launch overview. |
| [`community/gildea-creating-custom-variants.md`](./community/gildea-creating-custom-variants.md) | Paul Gildea | 41 | Designing a `Spaarke.UI.Components` variant of an upstream Fluent component. |

## Pattern files that load FROM these docs

| Pattern (in `.claude/patterns/`) | Loads from |
|---|---|
| `ui/fluent-v9-component-authoring.md` | `slots-architecture.md`, `styling-griffel.md`, `quickstart.md` |
| `ui/fluent-v9-theming.md` | `theming.md`, `community/birkelbach-standard-custom-theming.md` |
| `ui/fluent-v9-portal-gotcha.md` | `theming.md` §FluentProvider, `community/birkelbach-style-fluent-ui-9-pcfs.md` §portal, sample `samples/fluentui_react-v9/Provider/FluentProviderApplyStylesToPortals.stories.tsx` |
| `ui/fluent-v9-react-version-boundaries.md` | `react-version-support.md` |
| `ui/fluent-v9-host-visual-fit.md` | `host-mda-modern-look.md`, `host-canvas-modern-theming.md` (Code Page convention lives in the pattern itself) |
| `pcf/fluent-v9-modern-theming.md` | `pcf-modern-theming.md`, sample `samples/PowerApps-Samples_FluentThemingAPIControl/` |
| `pcf/fluent-v9-canvas-vs-mda-disabled.md` | `community/birkelbach-style-fluent-ui-9-pcfs.md` |
