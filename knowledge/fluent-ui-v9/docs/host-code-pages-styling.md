---
source: Spaarke-authored (no upstream Microsoft doc — Code Pages styling is project-specific)
fetched: 2026-05-28
summary: Spaarke Code Page (React 18 SPA delivered as Dataverse web resource) styling — Code Pages do NOT auto-inherit MDA modern theme; developer must pick theme at root + match visual standards manually.
loadWhen: building/modifying any React 18 SPA under src/client/code-pages/ OR designing how a Spaarke Code Page should visually fit inside an MDA shell.
---

# Spaarke Code Pages — Fluent v9 styling

> **Definition** — In this knowledge base and in Spaarke ADR-022, **Code Pages** = React 18 SPAs delivered as Dataverse web resources (`src/client/code-pages/*`), embedded as iframes or full-page inside MDA or canvas surfaces. This is **NOT** the same as Power Apps "custom pages" (canvas-style Power Fx pages embedded in MDA — those are a different concept).

## Why Code Pages need their own styling notes

Code Pages are structurally different from PCFs in three load-bearing ways:

| Dimension | PCF | Code Page |
|---|---|---|
| React | **16.14** platform-provided (virtual + platform-library) | **18.x** bundled |
| Host theme | `context.fluentDesignLanguage.tokenTheme` auto-passed | **NO** — Code Pages have no PCF context, no auto-inheritance |
| Lifecycle | `init` / `updateView` / `destroy` | Standard React 18 — `createRoot()` + lifecycle hooks |

**Consequence**: a Code Page running inside MDA does NOT automatically receive the modern theme. The developer must pick the theme explicitly at the SPA root.

This is the SAME constraint that applies to Microsoft's own "custom pages" — per [`host-mda-modern-look.md`](./host-mda-modern-look.md) FAQ: *"Currently, custom pages don't use the modern theme."*

## Spaarke convention — Code Page theming

### Default theme at SPA root

```tsx
// src/client/code-pages/{PageName}/src/main.tsx
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { createRoot } from 'react-dom/client';

const isDarkMode = window.matchMedia('(prefers-color-scheme: dark)').matches;
//                ^── browser-level dark mode preference (MDA dark mode toggle is NOT
//                    currently supported per host-mda-modern-look.md "Known limitations")

const root = createRoot(document.getElementById('root')!);
root.render(
  <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
    <App />
  </FluentProvider>
);
```

> ⚠️ Until MDA supports dark mode (currently it doesn't — see [`host-mda-modern-look.md`](./host-mda-modern-look.md) "Known limitations"), Code Pages rendering inside MDA should default to `webLightTheme` to match the host. Browser-preference detection still makes sense for Code Pages rendered outside MDA (Power Pages embed, standalone web URL).

### Spaarke brand theme (if applicable)

If `Spaarke.UI.Components` exports a brand theme, use that instead of `webLightTheme`:

```tsx
import { spaarkeLightTheme, spaarkeDarkTheme } from '@spaarke/ui-components';
// (Or whatever the canonical Spaarke brand theme export becomes — currently TBD; see NOTES.md)
```

### Visual standards to match

When a Code Page renders inside MDA — apply the same visual conventions documented in [`host-mda-modern-look.md`](./host-mda-modern-look.md):

- **Input controls**: `appearance="filled-darker"` matches the modern MDA field-control look.
- **Field labels**: icons on the **right** of the label, not the left.
- **Section containers**: streamlined design — minimal borders, rely on whitespace + light backgrounds.
- **Floating-card aesthetic**: drop shadows + brighter backgrounds to "float" content.
- **Dialogs**: auto-resize height based on content (Fluent v9 `Dialog` does this natively — don't fix the height).

### Portal-component gotcha (still applies)

Code Pages use `Popover`, `Tooltip`, `Dialog`, `Menu`, `Toast`, `Combobox` dropdown — same Fluent v9 components as PCFs. Same portal re-wrap rule applies:

```tsx
<Popover>
  <PopoverTrigger>...</PopoverTrigger>
  <PopoverSurface>
    <FluentProvider theme={theme}>  {/* ← MUST re-wrap to propagate theme into portal */}
      <MyPopoverContent />
    </FluentProvider>
  </PopoverSurface>
</Popover>
```

See [`../../.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../../.claude/patterns/ui/fluent-v9-portal-gotcha.md).

## Boundaries

| Scenario | Code Page handling |
|---|---|
| Code Page hosted inside MDA | Default `webLightTheme` (MDA doesn't expose tokenTheme to Code Pages). Match modern-look visual conventions manually. |
| Code Page hosted outside MDA (e.g., direct Dataverse URL) | Pick brand theme + browser dark-mode detection. |
| Code Page embedded in Power Pages | Power Pages is Bootstrap-based — Code Page uses Fluent v9 inside its own iframe; the iframe boundary keeps Fluent v9 + Bootstrap from clashing. No special handling needed beyond the standard Code Page setup. |
| Code Page used as a wizard or dialog | Spaarke-specific — see `.claude/patterns/webresource/code-page-wizard-wrapper.md`. |

## What this means for ADR-021 / ADR-022

- ADR-021 (Fluent v9 only) — applies in full to Code Pages. No `@fluentui/react` (v8). Token-only colors.
- ADR-022 (React versions) — Code Pages are React 18 bundle territory. May use `createRoot`, `useId`, `useTransition`, etc.
- Cross-surface components shipped via `Spaarke.UI.Components` STILL need to be React-16.14-safe (consumed by PCFs). Code Pages just happen to also run them under React 18 — same code, different runtime.
