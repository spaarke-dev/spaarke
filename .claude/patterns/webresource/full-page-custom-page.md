# Full-Page Custom Page

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Building a full navigation page or side pane hosted in a Dataverse Model-Driven App, deployed as a single self-contained HTML web resource. Use PCF instead if the UI is embedded in a form (field, subgrid, tab).

## Read These Files
1. `src/solutions/EventsPage/vite.config.ts` — canonical Vite config: `viteSingleFile`, `assetsInlineLimit`, `manualChunks: undefined`
2. `src/solutions/EventsPage/src/main.tsx` — React 18 `createRoot` entry point
3. `src/solutions/EventsPage/src/App.tsx` — `FluentProvider` + theme wiring skeleton
4. `src/solutions/EventsPage/src/providers/ThemeProvider.ts` — `resolveTheme()` + `setupThemeListener()` (copy for new pages)
5. `src/solutions/EventsPage/index.html` — CSS reset: `html, body, #root { margin:0; height:100%; overflow:hidden }`
6. `.claude/patterns/auth/spaarke-auth-initialization.md` — auth bootstrap for pages calling BFF API

## Constraints
- **ADR-006**: Full-page surfaces MUST be Code Pages; PCF platform injects React 16 — `createRoot` and `useId()` fail at runtime
- **ADR-021**: All UI must use Fluent UI v9; use theme tokens, not hard-coded colors
- **ADR-022**: Code Pages bundle React 18 themselves; never rely on platform-provided React
- **ADR-026**: Single self-contained HTML output required; no code splitting, no separate asset files

## Key Rules
- `viteSingleFile()` + `assetsInlineLimit: 100000000` + `manualChunks: undefined` are all mandatory for single-file output
- Never use React Router — client-side routing has no meaning inside Dataverse
- Always guard Xrm access: `const xrm = getXrm(); if (!xrm) return;` — code also runs in dev server
- BFF-calling pages MUST bootstrap auth before `createRoot`; never use module-level `getMsalClientId()` calls
