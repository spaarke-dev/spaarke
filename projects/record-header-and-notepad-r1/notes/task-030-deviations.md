# Task 030 — Notepad Vite Scaffold — Deviations

> **Task**: 030-notepad-vite-scaffold
> **Completed**: 2026-07-02
> **Status**: ✅

---

## Summary

Scaffolded `src/solutions/Notepad/` as a Vite React 18 SPA adapted from `src/solutions/SmartTodo/`. Build passes; placeholder App.tsx renders inside FluentProvider with the 3-level theme cascade.

---

## Files Created

- `src/solutions/Notepad/package.json`
- `src/solutions/Notepad/vite.config.ts`
- `src/solutions/Notepad/tsconfig.json`
- `src/solutions/Notepad/tsconfig.node.json`
- `src/solutions/Notepad/index.html`
- `src/solutions/Notepad/jest.config.cjs`
- `src/solutions/Notepad/jest.setup.cjs`
- `src/solutions/Notepad/src/main.tsx`
- `src/solutions/Notepad/src/App.tsx`

---

## Deviations from SmartTodo Baseline

### Removed Dependencies (Notepad does not need them)

| Package removed | Reason |
|---|---|
| `@spaarke/auth` | Spec NFR-05 — host-context surface, `Xrm.WebApi` only |
| `@spaarke/sdap-client` | Spec NFR-07 — zero BFF surface in R1 |
| `@spaarke/smart-todo-components` | SmartTodo-specific; Notepad is entity-agnostic |
| `@azure/msal-browser` | No auth needed (NFR-05); MSAL was a transitive of `@spaarke/auth` |
| `@lexical/*` (10 packages) | Notepad v1 is plain textarea (spec OS-08 excludes rich-text formatting) |
| `lexical` | Same — no rich-text editor in v1 |
| `marked` | No markdown rendering in Notepad v1 |
| `d3-force` | SmartTodo Kanban graph layout; not applicable to Notepad |
| `@hello-pangea/dnd` | SmartTodo Kanban drag-drop; not applicable to Notepad |

### Version Adjustment

- **React**: SmartTodo pins `^19.0.0`; Notepad pins **`^18.3.0`** per spec S-05 (Notepad is a "Vite React 18 SPA") + task 030 enforcement checklist (`React 18 in devDependencies AND runtime deps`). ADR-022 confirms Code Pages may use React 18 (PCF React 16/17 boundary does not apply here).

### Vite Config Simplifications

- Removed `@spaarke/auth` and `@spaarke/sdap-client` aliases from `resolve.alias` (Notepad doesn't consume them).
- Removed `authLibRoot` and its inclusion in `resolveSharedLibDeps` + `react({ include: [...] })`.
- Kept `PolymorphicResolverService` reachable via `@spaarke/ui-components/services` alias — task 033 (`useSprkMemoRepository`) will consume it.

### tsconfig Simplifications

- Removed `@spaarke/auth` and `TodoDetail` / `AssociateToStep` path aliases (Notepad-irrelevant).
- Kept `@spaarke/ui-components/services`, `/utils`, `/hooks`, `/PanelSplitter` (Notepad may consume `services` for the resolver + `utils` for theming).

---

## Install + Build Results

- **`npm install --legacy-peer-deps --no-audit --no-fund`**: 478 packages in 42s. Standard deprecation warnings for `inflight`, `glob@7/10`, `whatwg-encoding` (transitive; no action needed — these are jest-tree dependencies matching SmartTodo).
- **`npm run build`**: 2314 modules transformed; build succeeded in 7.61s.
- **Output**: `dist/notepad.html` = 195.33 kB (gzip: 61.79 kB), single-file inline HTML ready for Dataverse webresource upload in task 039.

### Rollup Warnings (non-blocking)

Three `/* #__PURE__*/` comment-position warnings from transitive `@microsoft/applicationinsights-*` packages under `Spaarke.UI.Components`. Same warnings would occur for SmartTodo. Not a functional issue.

---

## Enforcement Checklist — All Passed

- [x] Vite build succeeds with 0 errors
- [x] `App.tsx` placeholder renders inside `FluentProvider theme={resolveCodePageTheme()}` (3-level cascade)
- [x] NO new npm packages beyond SmartTodo's baseline (NFR-08) — Notepad's `package.json` is a strict subset
- [x] NO `@spaarke/auth` in dependencies (NFR-05)
- [x] NO BFF client (`@spaarke/sdap-client`) in dependencies (NFR-07)
- [x] Zero hex/rgb literals in main.tsx / App.tsx (Fluent v9 semantic tokens via theme)
- [x] React 18 in devDependencies AND runtime deps (`react`, `react-dom` at `^18.3.0`)

---

## Next Steps

- Task 031 — `types/*` + `deriveTitle` utility (parallel-group D with 032)
- Task 032 — `useLaunchContext` hook (parallel-group D with 031)
- Task 033 — `useSprkMemoRepository` (depends on 001 ✅ + 031 + 032)
