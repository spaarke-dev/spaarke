# ADR-026: Full-Page Custom Page Standard

> **Status**: Accepted
> **Date**: 2026-02-18
> **Supersedes**: None
> **Related**: ADR-006 (PCF over webresources), ADR-021 (Fluent v9), ADR-022 (PCF platform libraries)

---

## Decision

Full-page Dataverse surfaces (sitemap entries, navigation pages, side panes) use **standalone HTML web resources** built with Vite + React 18, not PCF controls.

PCF controls remain the standard for **form-level** UI (fields, subgrids, tabs) per ADR-006.

---

## Context

PCF controls declare `<platform-library name="React" version="16.14.0" />`, which causes Dataverse to inject React 16 at runtime. React 18 APIs (`createRoot`, `useId()`, concurrent features) are unavailable regardless of where the PCF is placed — form or Custom Page.

Full-page surfaces need React 18 because:
- Fluent UI v9 requires `useId()` (React 18 native) — workarounds for React 16 are fragile
- Full-page apps benefit from concurrent rendering and Suspense
- No PCF lifecycle benefits apply (no field binding, no `notifyOutputChanged`)

---

## Surface Decision Matrix

| Surface Type | Technology | React | Location |
|-------------|-----------|-------|----------|
| Form field/subgrid/tab | PCF control | 16 (platform) | `src/client/pcf/` |
| Full navigation page | Standalone HTML | 18 (bundled) | `src/solutions/{PageName}/` |
| Side pane | Standalone HTML | 18 (bundled) | `src/solutions/{PaneName}/` |
| Dialog (simple) | PCF + Fluent | 16 (platform) | `src/client/pcf/` |
| Ribbon/command script | Minimal JS | N/A | `src/dataverse/webresources/` |

---

## Technology Stack (Standardized)

| Component | Choice | Rationale |
|-----------|--------|-----------|
| **Build tool** | Vite 5.x | `vite-plugin-singlefile` inlines all JS/CSS into one HTML file — required for Dataverse web resource deployment. Fastest dev server. ESBuild transpilation. |
| **Single-file plugin** | `vite-plugin-singlefile` | Dataverse web resources are single files — no CDN, no separate .js/.css. This plugin is the only production-quality solution for this constraint. |
| **React** | 18.x (self-bundled) | Enables `createRoot`, `useId()`, concurrent features. Bundled in `devDependencies` — Vite tree-shakes and inlines. |
| **UI library** | Fluent UI v9 (`@fluentui/react-components`) | ADR-021 mandate. Bundled (not platform-provided) to avoid version mismatch. |
| **TypeScript** | 5.x strict mode | `moduleResolution: "bundler"` for Vite compatibility. `noEmit: true` — Vite handles transpilation. |
| **Xrm access** | Frame-walk pattern | `window.parent.Xrm` — web resources run in iframes, must walk frame hierarchy to find Xrm global. |
| **Theme** | ADR-021 4-level detection | localStorage → URL param → navbar DOM color → system `prefers-color-scheme`. |
| **Navigation** | `Xrm.Navigation` / `postMessage` | No React Router — navigation targets are Dataverse forms/pages, not client-side routes. |
| **Deployment** | `pac webresource push` | Single HTML file uploaded as Webpage (HTML) type web resource. |

### Why Not Alternatives?

| Alternative | Why Not |
|------------|---------|
| PCF control | Platform injects React 16 — no React 18 APIs. No form lifecycle benefits for full pages. |
| webpack | Requires 50+ lines of config for single-file output. No native plugin equivalent to `viteSingleFile`. |
| Create React App | Deprecated. No single-file output. |
| Next.js / Remix | Server-side rendering is irrelevant — this runs inside Dataverse iframes. |
| esbuild (standalone) | No HTML inlining. Would require custom post-processing. |

---

## MUST Rules

- **MUST** use Vite + `vite-plugin-singlefile` for all full-page Custom Page builds
- **MUST** place projects under `src/solutions/{PageName}/`
- **MUST** use React 18 via `devDependencies` (Vite bundles it)
- **MUST** use `createRoot` from `react-dom/client` (React 18 entry point)
- **MUST** wrap all UI in `<FluentProvider theme={theme}>` as outermost element
- **MUST** implement ADR-021 theme detection (4-level priority chain)
- **MUST** include CSS reset in `index.html` for Dataverse iframe context
- **MUST** use Xrm frame-walk pattern for Dataverse API access
- **MUST** name web resources `sprk_{pagename}` (e.g., `sprk_legalworkspace`)
- **MUST** guard Xrm access with null checks (fails outside Dataverse host)
- **MUST** use `declare const Xrm: any` for TypeScript (no `@types/xrm` package)

## MUST NOT Rules

- **MUST NOT** use PCF for full-page surfaces (React 16 limitation)
- **MUST NOT** use `<platform-library>` manifest declarations (self-bundle everything)
- **MUST NOT** use React Router for navigation (use Xrm.Navigation or postMessage)
- **MUST NOT** produce multiple output files (single HTML only)
- **MUST NOT** use `@fluentui/react` (v8) — Fluent v9 only per ADR-021
- **MUST NOT** hard-code colors — use Fluent design tokens per ADR-021

---

## Affected Controls (Migration Required)

These PCF controls declare `platform-library React 16` but use `createRoot` (React 18). They must be migrated to this standard if they are full-page surfaces, or downgraded to React 16 APIs if they are form-level controls:

| Control | Current State | Surface Type | Action |
|---------|--------------|--------------|--------|
| LegalWorkspace | PCF + createRoot | Full page | Migrate to standalone HTML |
| AnalysisWorkspace | PCF + createRoot | Full page | Migrate to standalone HTML |
| AnalysisBuilder | PCF + createRoot | Full page | Migrate to standalone HTML |
| SpeDocumentViewer | PCF + createRoot | Document viewer | Evaluate: full-page or form-level? |

---

## Reference Implementations

| Surface | Location | Status |
|---------|----------|--------|
| EventsPage | `src/solutions/EventsPage/` | Production (canonical reference) |
| CalendarSidePane | `src/solutions/CalendarSidePane/` | Production |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/` | Production |

---

## Pattern File

See [`.claude/patterns/webresource/full-page-custom-page.md`](../../patterns/webresource/full-page-custom-page.md) for the complete project template with all file contents.

---

**Lines**: ~130
**Purpose**: Standardize full-page Dataverse Custom Page architecture
