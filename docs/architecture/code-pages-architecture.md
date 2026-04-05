# Code Pages Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Documents the React 19 Code Page subsystem -- entry patterns, auth bootstrap, build pipeline, and deployment model.

---

## Overview

Code Pages are standalone React 19 single-page applications deployed as Dataverse HTML web resources. They serve use cases that require full-page or dialog-sized UI beyond what a field-bound PCF control can provide -- analysis workspaces, visual builders, search interfaces, and relationship graphs.

ADR-006 established the architectural boundary: field-bound form controls use PCF (React 16, platform-provided); standalone dialogs and pages use Code Pages (React 19, self-bundled). This avoids the anti-pattern of wrapping a PCF control in a Custom Page just to get a dialog.

Each Code Page is a self-contained webpack project that bundles everything (React, Fluent UI, MSAL, shared libraries) into a single `bundle.js`, which is then inlined into an HTML file and uploaded to Dataverse as a web resource.

## Component Structure

| Code Page | Path | Web Resource Name | Purpose |
|-----------|------|-------------------|---------|
| AnalysisWorkspace | `src/client/code-pages/AnalysisWorkspace/` | `sprk_analysisworkspace` | Full-page AI analysis collaboration workspace with editor, chat, source viewer, and diff review panels |
| PlaybookBuilder | `src/client/code-pages/PlaybookBuilder/` | `sprk_playbookbuilder` | Visual node-based AI playbook builder with AI assistant, scope configuration, and JPS editing |
| SemanticSearch | `src/client/code-pages/SemanticSearch/` | `sprk_semanticsearch` | System-wide AI-powered semantic search across documents, matters, projects, and invoices |
| DocumentRelationshipViewer | `src/client/code-pages/DocumentRelationshipViewer/` | `sprk_documentrelationshipviewer` | Force-directed graph visualization of document relationships |

### Common File Structure

Each Code Page follows this layout:

```
{CodePage}/
  index.html              # Shell HTML with <div id="root"> and <script src="bundle.js">
  package.json            # React 19, Fluent UI v9, @spaarke/auth, @spaarke/ui-components
  webpack.config.js       # Production build -> out/bundle.js (single chunk)
  build-webresource.ps1   # Inlines bundle.js into index.html -> out/sprk_{name}.html
  tsconfig.json
  src/
    index.tsx             # Entry point: URL parsing, bootstrap, createRoot, ThemeRoot
    App.tsx               # Application shell component
    components/           # Page-specific components
    hooks/                # Page-specific hooks
    services/             # Auth init, API clients
    config/ or context/   # MSAL config, React contexts
    types/                # Type definitions including xrm.d.ts
```

## Data Flow

### Auth Bootstrap Sequence

Every Code Page follows the same async bootstrap pattern before rendering. The sequence resolves runtime configuration from Dataverse Environment Variables -- no build-time `.env.production` values are used for BFF URL or MSAL client ID.

1. **Parse URL parameters**: Unwrap Dataverse `?data=encodedString` envelope into flat params
2. **Resolve record context**: Extract entity IDs from URL params, Dataverse form pass-through (`?id=`), or parent Xrm frame-walk
3. **`resolveRuntimeConfig()`**: Queries Dataverse Environment Variables for BFF base URL, MSAL client ID, and OAuth scope (from `@spaarke/auth`)
4. **Set window globals**: `window.__SPAARKE_MSAL_CLIENT_ID__` and `window.__SPAARKE_BFF_BASE_URL__` so `@spaarke/auth` internal `resolveConfig()` picks them up
5. **Initialize MSAL**: Auth provider initialization (timing varies per page -- some initialize before render, others defer to AuthProvider context)
6. **`createRoot(container).render(<ThemeRoot />)`**: React 19 render with FluentProvider theme wrapper

### Theme Detection

Code Pages use `resolveCodePageTheme()` and `setupCodePageThemeListener()` from `@spaarke/ui-components` with a 3-level cascade:

1. **localStorage** (`spaarke-theme` key) -- user's explicit preference
2. **URL flags** (`flags` param with `themeOption=dark|light`)
3. **Navbar DOM** -- Dataverse navbar `background-color` luminance detection

OS `prefers-color-scheme` is intentionally NOT consulted (ADR-021). Default is light theme.

Body background is synced with the resolved theme's `colorNeutralBackground1` to prevent white flash in dark mode.

### Hosting Modes

Code Pages support two hosting modes:

| Mode | Trigger | Context Resolution |
|------|---------|-------------------|
| **Embedded on form** | Added as Web Resource control on Dataverse form | Record ID from `?id=` param (form pass-through) or parent `Xrm.Page.data.entity.getId()` |
| **Dialog via navigateTo** | `Xrm.Navigation.navigateTo({ pageType: "webresource", data: "key=value" })` | Params from `data` envelope |

## Build Pipeline

### Step 1: Webpack Bundle

All four Code Pages use webpack 5 with esbuild-loader (not ts-loader) for fast TypeScript transpilation.

| Setting | Value | Rationale |
|---------|-------|-----------|
| `mode` | `production` | Minified output |
| `entry` | `./src/index.tsx` | Single entry point |
| `output` | `out/bundle.js` | Single output file |
| `splitChunks` | `false` | No code splitting -- Dataverse cannot load separate chunks |
| `LimitChunkCountPlugin` | `maxChunks: 1` | Prevents MSAL and other libraries from creating async chunks via `import()` |
| `externals` | None | Everything is bundled (unlike PCF which uses platform-provided React) |
| `alias @spaarke/ui-components` | Points to shared library `src/` | Enables tree-shaking from source |
| `alias @spaarke/auth` | Points to shared auth library `src/` | Same pattern |
| `alias react, react-dom` | Points to page's own `node_modules/` | Deduplicates React instances (shared lib has React 18 in its devDeps) |
| `TerserPlugin` | Drops `console.debug`, `console.info` | Keeps `console.warn` and `console.error` for diagnostics |

### Step 2: HTML Inlining

`build-webresource.ps1` reads `index.html` and `out/bundle.js`, replaces `<script src="bundle.js"></script>` with an inline `<script>` block, and writes the final single-file HTML to `out/sprk_{name}.html`.

### Deployment

The resulting `sprk_{name}.html` file is uploaded to Dataverse as a Webpage (HTML) web resource. Deployment is handled by `code-page-deploy` skill or manual upload via Power Apps maker portal.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | `@spaarke/auth` | `resolveRuntimeConfig()`, MSAL initialization | Runtime config from Dataverse Environment Variables |
| Depends on | `@spaarke/ui-components` | Components, hooks, theme utilities | Webpack-aliased to source for tree-shaking |
| Depends on | BFF API | `fetch()` with Bearer token | Auth tokens acquired via MSAL; URL from runtime config |
| Depends on | Dataverse host | `Xrm.Navigation.navigateTo()` to open | Xrm frame-walk for context |
| Consumed by | Dataverse forms | Embedded as web resource control | Form designer "Pass record object-type code" checkbox |
| Consumed by | Ribbon commands | `navigateTo` from command bar JS | Launcher scripts (e.g., `sprk_AnalysisWorkspaceLauncher.js`) |
| Consumed by | Side panes | `Xrm.App.sidePanes.createPane()` then `navigate()` | Loads Code Page in side pane |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| React 19 for Code Pages | Bundle own React, not platform-provided | Full React 19 features (createRoot, concurrent); PCF platform only provides React 16 | ADR-006, ADR-022 |
| Single-file HTML web resource | Inline bundle.js into HTML | Dataverse has no chunk-loading mechanism; all assets must be in one file | ADR-006 |
| Runtime config from Dataverse | `resolveRuntimeConfig()` at bootstrap | Eliminates build-time environment coupling; same artifact works in dev and prod | -- |
| Webpack (not Vite) | webpack 5 + esbuild-loader | Mature single-chunk control; LimitChunkCountPlugin prevents async chunk creation | -- |
| Window globals for auth config | `window.__SPAARKE_MSAL_CLIENT_ID__` | Bridge between async runtime config resolution and sync MSAL config initialization | -- |
| No OS theme detection | Exclude `prefers-color-scheme` | Spaarke theme system (localStorage + Dataverse sync) controls all surfaces | ADR-021 |

## Constraints

- **MUST**: Use React 19 `createRoot()` for rendering -- not `ReactDOM.render()` (ADR-022)
- **MUST**: Resolve BFF URL and MSAL client ID at runtime from Dataverse Environment Variables -- no build-time `.env` values for these settings
- **MUST**: Bundle everything into a single chunk -- `splitChunks: false` plus `LimitChunkCountPlugin({ maxChunks: 1 })`
- **MUST**: Wrap application in `<FluentProvider theme={...}>` with dynamic theme from `resolveCodePageTheme()` (ADR-021)
- **MUST**: Sync body background color with resolved theme to prevent white flash in dark mode
- **MUST NOT**: Use module-level constants that call runtime config getters (throws before bootstrap completes)
- **MUST NOT**: Create Custom Page + PCF wrapper for standalone dialogs -- use a Code Page instead (ADR-006)
- **MUST NOT**: Reference `@fluentui/react` (v8) -- Fluent UI v9 only (ADR-021)

## Known Pitfalls

- **Module-level config access**: `const CLIENT_ID = getMsalClientId()` at module scope throws because runtime config has not resolved yet. Use lazy getter functions: `export function getMsalConfig() { return { clientId: getMsalClientId() }; }`.
- **Multiple React instances**: The shared UI library has React 18 in its devDependencies. Without the webpack `react` / `react-dom` alias pointing to the Code Page's own `node_modules/`, two React instances load and hooks break with "Invalid hook call."
- **MSAL async chunks**: MSAL Browser uses `import()` internally. Without `LimitChunkCountPlugin({ maxChunks: 1 })`, webpack creates separate chunk files (e.g., `357.bundle.js`) that 404 when served from Dataverse.
- **Dataverse data envelope**: `navigateTo` wraps the caller's data string in a single `?data=encodedString` query param. Entry points must unwrap: `new URLSearchParams(decodeURIComponent(urlParams.get('data')))`.
- **Cross-origin frame access**: Xrm frame-walk (`window.parent.Xrm`) can fail with cross-origin errors in certain hosting configurations. All frame access must be wrapped in try/catch.
- **Theme token resolution outside FluentProvider**: `tokens.colorNeutralBackground1` is a CSS variable reference that only works inside FluentProvider. For `document.body.style`, read the resolved value from the theme object directly.

## Related

- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) -- Anti-legacy-JS: PCF for form controls, Code Pages for dialogs
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) -- Fluent UI v9 design system
- [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) -- PCF platform libraries (React 16 for PCF, React 19 for Code Pages)
- [shared-ui-components-architecture.md](shared-ui-components-architecture.md) -- Shared component library consumed by Code Pages
- [AUTH-AND-BFF-URL-PATTERN.md](AUTH-AND-BFF-URL-PATTERN.md) -- Runtime auth and BFF URL resolution pattern
- [ui-dialog-shell-architecture.md](ui-dialog-shell-architecture.md) -- Dialog shell patterns
