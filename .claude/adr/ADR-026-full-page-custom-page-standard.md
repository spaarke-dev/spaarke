# ADR-026: Full-Page Custom Page Standard

> **Status**: Accepted (Revised 2026-03-19)
> **Date**: 2026-02-18
> **Supersedes**: None
> **Related**: ADR-006 (UI Surface Architecture), ADR-021 (Fluent v9), ADR-022 (PCF platform libraries)

---

## Decision

Full-page Dataverse surfaces (sitemap entries, navigation pages, side panes) and standalone dialogs/wizards use **standalone HTML web resources** built with Vite + React 19, not PCF controls.

PCF controls remain the standard for **form-level** UI (fields, subgrids, tabs) per ADR-006.

---

## Context

PCF controls declare `<platform-library name="React" version="16.14.0" />`, which causes Dataverse to inject React 16 at runtime. React 19 APIs (`createRoot`, `useId()`, concurrent features) are unavailable regardless of where the PCF is placed — form or Custom Page.

Full-page surfaces and standalone dialogs need React 19 because:
- Fluent UI v9 requires `useId()` (React 18+ native) — workarounds for React 16 are fragile
- Full-page apps benefit from concurrent rendering and Suspense
- Wizard dialogs need to be independently deployable and reusable across contexts
- No PCF lifecycle benefits apply (no field binding, no `notifyOutputChanged`)

---

## Surface Decision Matrix

| Surface Type | Technology | React | Location |
|-------------|-----------|-------|----------|
| Form field/subgrid/tab | PCF control | 16 (platform) | `src/client/pcf/` |
| Full navigation page | Standalone HTML | 19 (bundled) | `src/solutions/{PageName}/` |
| Side pane | Standalone HTML | 19 (bundled) | `src/solutions/{PaneName}/` |
| Wizard / dialog (standalone) | Standalone HTML | 19 (bundled) | `src/solutions/{WizardName}/` |
| Ribbon/command script | Minimal JS | N/A | `src/dataverse/webresources/` |

---

## Technology Stack (Standardized)

| Component | Choice | Rationale |
|-----------|--------|-----------|
| **Build tool** | Vite 5.x | `vite-plugin-singlefile` inlines all JS/CSS into one HTML file — required for Dataverse web resource deployment. Fastest dev server. ESBuild transpilation. |
| **Single-file plugin** | `vite-plugin-singlefile` | Dataverse web resources are single files — no CDN, no separate .js/.css. This plugin is the only production-quality solution for this constraint. |
| **React** | 19.x (self-bundled) | Enables `createRoot`, `useId()`, concurrent features. Bundled in `devDependencies` — Vite tree-shakes and inlines. |
| **UI library** | Fluent UI v9 (`@fluentui/react-components`) | ADR-021 mandate. Bundled (not platform-provided) to avoid version mismatch. |
| **TypeScript** | 5.x strict mode | `moduleResolution: "bundler"` for Vite compatibility. `noEmit: true` — Vite handles transpilation. |
| **Xrm access** | Frame-walk pattern | `window.parent.Xrm` — web resources run in iframes, must walk frame hierarchy to find Xrm global. |
| **Theme** | ADR-021 4-level detection | localStorage → URL param → navbar DOM color → system `prefers-color-scheme`. |
| **Navigation** | `Xrm.Navigation` / `postMessage` | No React Router — navigation targets are Dataverse forms/pages, not client-side routes. |
| **Deployment** | `pac webresource push` or deploy scripts | Single HTML file uploaded as Webpage (HTML) type web resource. |

### Why Not Alternatives?

| Alternative | Why Not |
|------------|---------|
| PCF control | Platform injects React 16 — no React 19 APIs. No form lifecycle benefits for full pages. |
| webpack | Requires 50+ lines of config for single-file output. No native plugin equivalent to `viteSingleFile`. Existing webpack-based Code Pages (DocumentUploadWizard) should migrate to Vite. |
| Create React App | Deprecated. No single-file output. |
| Next.js / Remix | Server-side rendering is irrelevant — this runs inside Dataverse iframes. |
| esbuild (standalone) | No HTML inlining. Would require custom post-processing. |

---

## MUST Rules

- **MUST** use Vite + `vite-plugin-singlefile` for all Code Page builds
- **MUST** place projects under `src/solutions/{PageName}/`
- **MUST** use React 19 via `devDependencies` (Vite bundles it)
- **MUST** use `createRoot` from `react-dom/client` (React 19 entry point)
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

## Code Page package.json (React 19)

```json
{
  "dependencies": {
    "@spaarke/ui-components": "file:../../client/shared/Spaarke.UI.Components",
    "@spaarke/auth": "file:../../client/shared/Spaarke.Auth"
  },
  "devDependencies": {
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "@fluentui/react-components": "^9.54.0",
    "@fluentui/react-icons": "^2.0.0",
    "typescript": "~5.7.0",
    "vite": "^5.4.0",
    "vite-plugin-singlefile": "^2.0.0"
  }
}
```

---

## React 19 Migration Note

Some existing Code Pages still reference React 18.x in their `package.json`. React 19 is backward-compatible — existing Code Pages using `createRoot` and standard hooks will work without code changes. Upgrade `package.json` versions opportunistically when touching these solutions:

| Status | Solutions |
|--------|-----------|
| Already on React 19 | `DocumentRelationshipViewer`, `SprkChatPane`, `AnalysisWorkspace`, `SemanticSearch` |
| Still on React 18 (upgrade when touched) | `LegalWorkspace`, `EventsPage`, `SpeAdminApp`, `AnalysisBuilder`, `DocumentUploadWizard`, `CalendarSidePane`, `TodoDetailSidePane` |

---

## Reference Implementations

| Surface | Location | Status |
|---------|----------|--------|
| EventsPage | `src/solutions/EventsPage/` | Production (canonical reference) |
| CalendarSidePane | `src/solutions/CalendarSidePane/` | Production |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/` | Production |
| LegalWorkspace | `src/solutions/LegalWorkspace/` | Production |
| DocumentUploadWizard | `src/solutions/DocumentUploadWizard/` | Production (migrate webpack → Vite) |

---

## Pattern File

See [`.claude/patterns/webresource/full-page-custom-page.md`](../patterns/webresource/full-page-custom-page.md) for the complete project template with all file contents.

---

**Lines**: ~130
**Purpose**: Standardize Code Page architecture (full pages, side panes, dialogs/wizards)
