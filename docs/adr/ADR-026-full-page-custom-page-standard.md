# ADR-026: Full-Page Custom Page Standard

| Field | Value |
|-------|-------|
| **Status** | Accepted |
| **Date** | 2026-02-18 |
| **Decision Makers** | Ralph Schroeder |
| **Supersedes** | None |
| **Related** | ADR-006, ADR-021, ADR-022 |

---

## Context

Spaarke builds custom UI surfaces for Dataverse Model-Driven Apps. These surfaces fall into two distinct categories:

1. **Form-level controls**: Fields, subgrids, and tabs within existing Dataverse forms. These are well-served by PCF controls (ADR-006) using the platform-provided React 16 runtime (ADR-022).

2. **Full-page surfaces**: Complete navigation pages, side panes, and dashboards that replace or extend the Model-Driven App's standard views. These need a richer UI framework (React 18, concurrent rendering, full Fluent UI v9 support).

### The Problem

The project team initially built full-page surfaces as PCF controls with a documented "ADR exception" claiming Custom Pages could use React 18. This assumption proved incorrect:

- PCF controls declare `<platform-library name="React" version="16.14.0" />` in their manifest
- The Dataverse platform **always** injects React 16 at runtime when this declaration is present, regardless of where the PCF is hosted (form or Custom Page)
- React 18 APIs (`createRoot`, `useId()`) do not exist in React 16
- This produces `TypeError: createRoot is not a function` at runtime

**Affected controls**: LegalWorkspace, AnalysisWorkspace, AnalysisBuilder, SpeDocumentViewer — all declare platform-library React 16 but import from `react-dom/client` (React 18).

### The Existing Solution

The codebase already had a working pattern for full-page React 18 surfaces: the **EventsPage** and its companion side panes (CalendarSidePane, EventDetailSidePane). These use standalone HTML web resources built with Vite, bundling their own React 18 — completely independent of the PCF platform-library mechanism.

---

## Decision

**Full-page Dataverse surfaces use standalone HTML web resources built with Vite + React 18.**

PCF controls remain the standard for form-level UI per ADR-006.

### Surface Decision Matrix

| Surface Type | Technology | React Version | Project Location |
|-------------|-----------|---------------|-----------------|
| Form field, subgrid, tab | PCF control | 16 (platform-provided) | `src/client/pcf/` |
| Full navigation page | Standalone HTML web resource | 18 (self-bundled) | `src/solutions/{PageName}/` |
| Side pane | Standalone HTML web resource | 18 (self-bundled) | `src/solutions/{PaneName}/` |
| Dialog (simple, 2-4 options) | PCF + Fluent v9 | 16 (platform-provided) | `src/client/pcf/` |
| Ribbon/command script | Minimal JS | N/A | `src/dataverse/webresources/` |

---

## Technology Choices

### Build Tool: Vite 5.x

**Why Vite over alternatives:**

| Factor | Vite | webpack | Create React App | esbuild |
|--------|------|---------|-----------------|---------|
| Single-file HTML output | `vite-plugin-singlefile` (native) | Requires custom plugin | Not supported | Not supported |
| Dev server start | <200ms | 2-10s | 5-15s | <100ms |
| HMR speed | Near-instant | 1-3s | 2-5s | Near-instant |
| Config complexity | ~15 lines | 50+ lines | Ejected: 200+ lines | ~30 lines |
| TypeScript transpilation | ESBuild (10-100x faster) | ts-loader/babel | babel | Native |
| Tree-shaking | Rollup (excellent) | Good | Good | Excellent |
| Status | Active, standard | Active, heavy | **Deprecated** | Active, low-level |

The decisive factor is `vite-plugin-singlefile`. Dataverse web resources are deployed as **single files** — there is no CDN, no separate .js/.css file hosting. This plugin inlines all JavaScript, CSS, fonts, and assets into one self-contained HTML file. No other build tool has a production-quality equivalent.

### React 18 (Self-Bundled)

React 18 is listed in `devDependencies` (not `dependencies`) because Vite bundles it into the output at build time. There is no runtime npm dependency.

React 18 enables:
- `createRoot` — the modern mounting API (required by Fluent v9 internally)
- `useId()` — native unique ID generation (required by Fluent v9 for accessible labels)
- Concurrent rendering — `useTransition`, `useDeferredValue` for responsive large lists
- `Suspense` for data fetching — lazy loading of heavy components

### Fluent UI v9 (Bundled)

Fluent UI v9 is also bundled rather than using the PCF platform-provided version. This:
- Avoids version mismatch between the bundled code and platform-provided library
- Allows using the latest Fluent v9 features immediately
- Increases bundle size by ~200KB but is acceptable for full-page surfaces

### TypeScript Strict Mode

Standard `tsconfig.json` with:
- `moduleResolution: "bundler"` — Vite-specific module resolution
- `noEmit: true` — TypeScript is type-checking only; Vite/ESBuild handles transpilation
- `jsx: "react"` — classic transform requiring `import * as React from "react"`
- Full strict mode: `strict`, `noUnusedLocals`, `noUnusedParameters`

---

## Authentication

**Neither PCF nor standalone HTML has an authentication advantage.** Both approaches inherit the user's Dataverse session:

| API Target | PCF Mechanism | Standalone HTML Mechanism | Underlying Auth |
|-----------|---------------|--------------------------|-----------------|
| Dataverse data | `context.webAPI` | `Xrm.WebApi` (frame-walk) | Same Dataverse session token |
| User identity | `context.userSettings.userId` | `Xrm.Utility.getGlobalContext().userSettings` | Same user identity |
| BFF API | `fetch()` + MSAL token | `fetch()` + MSAL token | Identical — PCF provides nothing |
| External APIs | Application code | Application code | Identical |

The Xrm frame-walk pattern (`window.parent.Xrm`) is used by all existing standalone web resources and is well-proven in production.

---

## Deployment

### Build Output

```bash
cd src/solutions/{PageName}
npm install && npm run build
# Produces: dist/index.html (single file, ~500KB-1.5MB depending on component count)
```

### Deploy to Dataverse

```powershell
pac webresource push `
  --path "dist/index.html" `
  --name "sprk_{pagename}" `
  --type "Webpage" `
  --solution-unique-name "SpaarkeCore"
```

### Web Resource Naming Convention

| Type | Pattern | Example |
|------|---------|---------|
| Full page | `sprk_{pagename}` | `sprk_legalworkspace`, `sprk_eventspage` |
| Side pane | `sprk_{panename}` | `sprk_calendarsidepane`, `sprk_eventdetailsidepane` |

---

## Constraints

### MUST

- Use Vite + `vite-plugin-singlefile` for all full-page Custom Page builds
- Place projects under `src/solutions/{PageName}/`
- Use React 18 via `devDependencies` (Vite bundles it)
- Use `createRoot` from `react-dom/client`
- Wrap all UI in `<FluentProvider theme={theme}>` as outermost element
- Implement ADR-021 theme detection (4-level priority chain)
- Include CSS reset in `index.html` for Dataverse iframe context
- Use Xrm frame-walk pattern with null guards for Dataverse API access
- Name web resources `sprk_{pagename}`

### MUST NOT

- Use PCF for full-page surfaces
- Use `<platform-library>` manifest declarations (self-bundle everything)
- Use React Router (navigation targets are Dataverse entities, not client routes)
- Produce multiple output files
- Hard-code colors (use Fluent design tokens per ADR-021)

---

## Consequences

### Positive

- React 18 features available (createRoot, useId, concurrent rendering)
- Consistent pattern across all full-page surfaces
- Fast development cycle (Vite HMR)
- No dependency on PCF platform-library versioning
- Proven in production (EventsPage, CalendarSidePane, EventDetailSidePane)

### Negative

- Larger bundle size (~500KB+ vs ~50KB for PCF without React)
- No PCF `context` object — must use Xrm frame-walk
- Separate build toolchain from PCF controls
- Must maintain Fluent UI v9 version independently (no platform auto-updates)

### Migration Required

| Control | Current | Surface Type | Action |
|---------|---------|-------------|--------|
| LegalWorkspace | PCF + React 18 (broken) | Full page | Migrate to standalone HTML |
| AnalysisWorkspace | PCF + React 18 (broken) | Full page | Migrate to standalone HTML |
| AnalysisBuilder | PCF + React 18 (broken) | Full page | Migrate to standalone HTML |
| SpeDocumentViewer | PCF + React 18 (broken) | Document viewer | Evaluate surface type, then migrate or downgrade |

---

## Reference Implementations

| Surface | Location | Status | Notes |
|---------|----------|--------|-------|
| EventsPage | `src/solutions/EventsPage/` | Production | Canonical reference — grid + side panes |
| CalendarSidePane | `src/solutions/CalendarSidePane/` | Production | Side pane pattern |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/` | Production | Side pane + entity form integration |

---

## Pattern Template

See [`.claude/patterns/webresource/full-page-custom-page.md`](../../.claude/patterns/webresource/full-page-custom-page.md) for the complete copy-paste-ready project template with all configuration files.

---

*Accepted: 2026-02-18*
