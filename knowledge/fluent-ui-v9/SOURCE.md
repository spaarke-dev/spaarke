# SOURCE — `fluent-ui-v9`

> Provenance for everything in this folder. Do not modify curated samples or doc snapshots without updating this file.

## Scope

Microsoft **Fluent 2 design system** + **Fluent UI React v9** (`@fluentui/react-components`) — the React component library, Griffel CSS-in-JS engine, theming/tokens, accessibility, and Power Apps PCF integration. Cross-platform Fluent 2 docs (iOS / Android / Windows / Web Components) are included at overview level for design-system context but Spaarke's curated implementation samples are React-only.

## Source repositories

| Field | Value |
|---|---|
| **Repo (Fluent UI)** | [`microsoft/fluentui`](https://github.com/microsoft/fluentui) |
| Branch | `master` |
| Commit SHA | `0aa62de59fe5845eeba40c9028d527fd93d88f27` |
| Commit date | 2026-05-26 17:28:50 +0200 |
| Commit subject | `feat: add triage-issues + triage-board skills (+ visual-test hardening) (#36012)` |
| Pulled | 2026-05-26 |
| Method | `git clone --depth 1` |
| License | MIT |

| Field | Value |
|---|---|
| **Repo (PowerApps Samples)** | [`microsoft/PowerApps-Samples`](https://github.com/microsoft/PowerApps-Samples) |
| Branch | `master` |
| Commit SHA | `a6d30c10d17938fbeb85245e57a4a2cb435c71c8` |
| Commit date | 2026-04-02 18:47:44 -0700 |
| Commit subject | `Minor change` |
| Pulled | 2026-05-26 |
| Method | `git clone --depth 1` |
| License | MIT |

## Curated samples

All paths are relative to this folder. Each is copied verbatim from the upstream repo at the SHA above.

| Path | Upstream path | What it demonstrates |
|---|---|---|
| `samples/PowerApps-Samples_FluentThemingAPIControl/` | `microsoft/PowerApps-Samples/component-framework/FluentThemingAPIControl/` | **The canonical PCF + Fluent v9 + modern theming sample.** Four `components/Fluent*` TSX files showing the four theming approaches (auto v9, v8 via `createV8Theme`, non-Fluent token consumption, custom `FluentProvider`). The `ControlManifest.Input.xml` shows the `<platform-library>` declarations that wire React + Fluent into the PCF platform runtime. |
| `samples/fluentui_react-v9/Provider/FluentProviderDefault.stories.tsx` | `microsoft/fluentui/packages/react-components/react-provider/stories/src/Provider/FluentProviderDefault.stories.tsx` | Minimal `<FluentProvider theme={webLightTheme}>` setup. |
| `samples/fluentui_react-v9/Provider/FluentProviderNested.stories.tsx` | same path | Nested providers — `targetDocument` + scoped theme override. |
| `samples/fluentui_react-v9/Provider/FluentProviderApplyStylesToPortals.stories.tsx` | same path | **Critical for PCF**: `applyStylesToPortals` controls how styles propagate to React `Portal` children (`Popover`, `Tooltip`, `Toast`, `Dialog`, `Menu`). The single most-cited Fluent v9 + PCF gotcha. |
| `samples/fluentui_react-v9/Button/ButtonAppearance.stories.tsx` | `…/react-button/stories/src/Button/ButtonAppearance.stories.tsx` | The `appearance` prop (`primary` / `outline` / `subtle` / `transparent`). |
| `samples/fluentui_react-v9/Button/ButtonIcon.stories.tsx` | `…/react-button/stories/src/Button/ButtonIcon.stories.tsx` | Slot composition — `icon` slot pattern. |
| `samples/fluentui_react-v9/Theme/ThemeColors.stories.tsx` | `…/react-theme/stories/src/Theme/colors/ThemeColors.stories.tsx` | The full color-token grid generated from the built-in themes. |
| `samples/fluentui_react-v9/Theme/ThemeSpacing.stories.tsx` | `…/react-theme/stories/src/Theme/spacing/ThemeSpacing.stories.tsx` | Spacing tokens (`spacingHorizontal*` / `spacingVertical*`). |

Total curated size: ~120 KB (well under the 300 KB budget).

## Reference docs snapshot

All under `docs/`. Each file has a YAML frontmatter block with `source:` URL and `fetched:` date. Where the source is an MDX file in the Fluent UI repo (the Storybook source), the frontmatter also pins `upstream_commit:`.

### Microsoft official

| Path | Source | Notes |
|---|---|---|
| `docs/overview.md` | `apps/public-docsite-v9/src/Concepts/Introduction.mdx` (microsoft/fluentui) | v9 concepts/introduction. |
| `docs/quickstart.md` | `apps/public-docsite-v9/src/Concepts/QuickStart.mdx` | Install + `FluentProvider` root setup for React 17 + 18. |
| `docs/theming.md` | `apps/public-docsite-v9/src/Concepts/Theming.mdx` | Full theming reference — tokens, providers, custom brand ramps, extending tokens. |
| `docs/styling-griffel.md` | `apps/public-docsite-v9/src/Concepts/StylingComponents.mdx` | `makeStyles` + `mergeClasses` + shorthands + Griffel limitations + anti-patterns. |
| `docs/slots-architecture.md` | `apps/public-docsite-v9/src/Concepts/Slots/Slots.mdx` | Slot API for callers + slot internals for component authors. |
| `docs/react-version-support.md` | `apps/public-docsite-v9/src/Concepts/ReactVersionSupport.mdx` | React 17/18/19 support boundaries — **critical for PCF (React 16.14 floor)**. |
| `docs/accessibility.md` | `Concepts/Accessibility/AccessibleComponents.mdx` + `AccessibleExperiences.mdx` | WCAG 2.1 scope, axe-core testing, tabster, app-level accessibility checklist. |
| `docs/pcf-modern-theming.md` | `learn.microsoft.com/en-us/power-apps/developer/component-framework/fluent-modern-theming` | The four ways to apply modern theming in a PCF (v9 controls / v8 controls / non-Fluent / custom provider). Upstream MD: `MicrosoftDocs/powerapps-docs-pr` @ `671936ad01d60e3e499143a29cd1343349b01659`. |
| `docs/pcf-virtual-controls.md` | `microsoft.com/en-us/power-platform/blog/.../virtual-code-components-...` | The April 2022 announcement of platform-library React + Fluent PCF — semi-summarized capture (JS-rendered source). |
| `docs/modern-theming-api-control-sample.md` | `learn.microsoft.com/.../sample-controls/modern-theming-api-control` | Reference index for the `FluentThemingAPIControl` sample (also mirrored under `samples/`). |
| `docs/fluent2-overview.md` | `fluent2.microsoft.design/` | Fluent 2 design system home. |
| `docs/fluent2-develop.md` | `fluent2.microsoft.design/get-started/develop` | Cross-platform install guides — React, Web Components, iOS, Android, WinUI. |
| `docs/fluent2-design-principles.md` | `fluent2.microsoft.design/design-principles` | The four principles (Natural / Built for focus / One for all / Unmistakably Microsoft). |
| `docs/fluent2-whats-new.md` | `fluent2.microsoft.design/get-started/whatisnew` | System-level + platform-level changes from Fluent 1 → 2. |

### Community / MVP

All MVP captures are stored under `docs/community/`. They are WebFetch captures — short, semi-summarized — and should be **verified against the live post** before quoting code verbatim. Each has provenance YAML with author, original URL, and a `notes:` line stating the verification caveat.

| Path | Author | Why curated |
|---|---|---|
| `docs/community/birkelbach-style-fluent-ui-9-pcfs.md` | Diana Birkelbach | **Canonical reference** for styling v9 PCFs to match modern Power Apps look. Canvas vs MDA disabled-state handling, portal re-wrapping, Universal control pattern. |
| `docs/community/birkelbach-virtual-pcfs-after-ga.md` | Diana Birkelbach | Virtual PCF GA (2024-12); React 16.14 + Fluent 9.46.2 baseline; **still no Power Pages support**; bundle-size results. |
| `docs/community/birkelbach-standard-custom-theming.md` | Diana Birkelbach | Standard + custom theming for PCFs; brand-variant generation via `tinycolor2`; dark-mode detection via `isDarkTheme`. |
| `docs/community/itmustbecode-develop-pcf-fluentui-v9.md` | David Rivard | The "v9 is NOT an upgrade from v8" article. Griffel basics, Slots intro, performance notes, two referenced production PCFs (Badge, Slider). |
| `docs/community/itmustbecode-adapting-pcf-modern-look.md` | David Rivard | The dual-look detection pattern — undocumented `context.fluentDesignLanguage` presence signals new look; conditional v8/v9 rendering. |
| `docs/community/ariclevin-virtual-pcf-fluent-v9.md` | Aric Levin | PCF Builder VS Code workflow for `pac pcf init -fw react` virtual control. |
| `docs/community/gildea-whats-new-with-fluent-ui-react-v9.md` | Paul Gildea | High-level v9 launch overview — performance (Griffel), accessibility, slots, theming, migration. |
| `docs/community/gildea-creating-custom-variants.md` | Paul Gildea | Three variant patterns: pure-style overrides, wrapper, wrapper-with-override (`mergeClasses` for consumer customization). |

## GAPs and known issues

- **`https://react.fluentui.dev/`** — JS-rendered Storybook; WebFetch returns only the page title. We snapshotted the upstream MDX files (which are the storybook source) instead. URL retained in frontmatter `notes:` lines for human reference.
- **`https://www.microsoft.com/en-us/power-platform/blog/...`** — JS-heavy; WebFetch returned a semi-summarized capture. Treat `docs/pcf-virtual-controls.md` as orientation, not verbatim authority.
- **Aric Levin post is short** — image-heavy; minimal text. We captured what's there honestly; for production work, also consult Birkelbach's deeper PCF posts.
- **Clavin Fernandes "Create a React Virtual Code Component"** (https://clavinfernandes.wordpress.com/2025/01/21/create-a-react-virtual-code-component-with-power-apps-component-framework-pcf/) — duplicated coverage with Birkelbach + Levin + Rivard; not curated to avoid redundancy. Add at next refresh if it grows distinctive content.
- **Fluent 2 design tokens cross-platform reference** (`fluent2.microsoft.design/design-tokens/...`) — not curated; the React-side tokens are documented under `theming.md`. Add at next refresh if cross-platform consumers emerge.

## Why this curation, briefly

Spaarke ships Fluent UI v9 across 10+ PCFs, 4+ Code Pages, the external SPA, and the office add-ins, with `Spaarke.UI.Components` as the shared component library (`@fluentui/react-components ^9.73.2`). The curation prioritises:

1. **The PCF integration story** — `pcf-modern-theming.md` + `pcf-virtual-controls.md` + the `FluentThemingAPIControl` sample, complemented by Birkelbach's Canvas-vs-MDA disabled-state handling and Rivard's dual-look detection pattern. These are the topics where Spaarke engineers most often need to converge from rough first-pass code to platform-aligned output.
2. **The portal-styling gotcha** — `FluentProviderApplyStylesToPortals.stories.tsx` + Birkelbach's wrapping pattern. The single most-cited Fluent v9 + PCF bug surface.
3. **Griffel mechanics** — `styling-griffel.md` is the difference between a Spaarke component that integrates well into customer-tenant themes and one that hard-codes colors that look wrong outside `webLightTheme`.
4. **Slot architecture** — required reading before authoring any new component in `Spaarke.UI.Components`.
5. **React version support** — explicitly documents the React 16.14 ↔ 17 ↔ 18 ↔ 19 boundaries that Spaarke crosses (PCF Canvas is still 16.14; Code Pages are 18+).

Cross-platform Fluent 2 (iOS, Android, WinUI) is included at overview level only; Spaarke has no current implementation surface there but the design-system context informs visual consistency choices.
