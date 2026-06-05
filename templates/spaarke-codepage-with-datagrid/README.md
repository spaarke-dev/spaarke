# Spaarke Code Page with DataGrid — starter template

> Copy-paste starting point for any new Custom Page that hosts the `<DataGrid>` framework.
> **Companion contract**: [`docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md)

---

## Quick start

1. **Copy this directory** to `src/solutions/<your-page-name>/`.
2. **Rename**:
   - `package.json` → set `"name"` to your-page-name.
   - `index.html` → set `<title>` to something user-visible.
3. **Create the Dataverse `sprk_gridconfiguration` record** for your entity (see [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md)) and copy its GUID into `src/main.tsx` (replace `__YOUR_CONFIG_ID__`).
4. **Create the web resource in the maker portal** (name it `sprk_<your-page-name>.html`) — empty placeholder is fine; the deploy script PATCHes it.
5. **Register your page** in `scripts/Deploy-AllDataGridConsumers.ps1` — add a new entry to the `$Consumers` array at the top of the file with:
   - `Name = 'YourPage'`
   - `SolutionDir = 'src\solutions\<your-page-name>'`
   - `DistFile = 'index.html'`
   - `WebResource = 'sprk_<your-page-name>.html'`
6. **Build + deploy**:
   ```powershell
   $env:DATAVERSE_URL = "https://your-env.crm.dynamics.com"
   .\scripts\Deploy-AllDataGridConsumers.ps1 -Only YourPage
   ```

That's it. Sort, filter chips, command bar, lazy paging, dark mode, parent-context drill-through, side-pane filtering — all inherited from the framework. No scaffolding to maintain per page.

---

## What's in this template

| File | Purpose |
|---|---|
| `package.json` | Vite + React 19 + `@spaarke/ui-components` workspace dep. Build script renames `dist/index.html` to whatever you want for clarity (default: leaves `index.html`). |
| `tsconfig.json` + `tsconfig.node.json` | TypeScript strict + path alias for `@spaarke/ui-components`. |
| `vite.config.ts` | Single-file bundler config (vite-plugin-singlefile). React 19 plugin. |
| `index.html` | Required CSS reset (box-sizing + height chain + max-height 100vh + #root > div). DO NOT remove. |
| `src/main.tsx` | Mounts `<DataGridPageShell>` with `configId` + optional `useUrlParentContext` + optional `sidePaneFilter`. |

---

## The 4 wire-up scenarios

### A. Simple standalone page (no parent context, no side pane)

```tsx
<DataGridPageShell configId="…" onBack={() => window.close()} />
```

### B. Drill-through dialog (parent context from `data=` envelope)

```tsx
<DataGridPageShell
  configId="…"
  useUrlParentContext={{ key: 'matterId' }}
  onBack={() => window.close()}
/>
```

The shell parses `?data=filterValue=<matterId>` from the URL and builds the parent context. Your configjson's `behavior.parentContextFilter` consumes it. See [`src/solutions/sprk_invoicespage/`](../../src/solutions/sprk_invoicespage/) for the canonical example.

### C. Drill-through dialog + side-pane filter

```tsx
<DataGridPageShell
  configId="…"
  useUrlParentContext={{ key: 'matterId' }}
  sidePaneFilter={{
    paneId: 'date-filter',
    translator: (payload: { from: string; to: string; field: string }) => [
      { attribute: payload.field, operator: 'between', value: [payload.from, payload.to] },
    ],
  }}
/>
```

The translator's payload type matches what your side-pane web resource sends via `sendSidePaneFilter({ paneId: 'date-filter', payload: {...} })`. See [`src/solutions/EventsPage/`](../../src/solutions/EventsPage/) for the canonical example with the CalendarSidePane.

### D. Side pane LIFECYCLE (host opens the pane via `Xrm.App.sidePanes`)

When MDA doesn't auto-open the pane via its sitemap, the host manages the lifecycle. Mount `<DataGrid>` directly (skip the shell) and combine `useSidePaneFilter` + `DataGridSidePaneOrchestrator`. See `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §5 for the full pattern.

---

## What the shell deliberately does NOT cover

| Scenario | Why | Workaround |
|---|---|---|
| Custom row-open behavior (open a detail side pane instead of the configjson default) | Page-specific business logic | Pass `onRecordOpen` prop — same signature as `<DataGrid>` |
| Custom command handler registration (e.g. EventsPage's bulk-status commands) | Module-side-effect timing matters | Call `registerCommandHandler(...)` at module load BEFORE mounting; configjson `commandBar.primary` entries with `action: 'custom'` resolve them |
| Complex parent-context parsing (multi-key, JSON envelope, Xrm.Page fallback) | One-off per page | Compute `parentContext` yourself, pass via the `parentContext` prop. `useUrlParentContext` covers the common single-matterId case. |
| MDA sub-grids (system grids on a form) | Out of scope | The framework targets Code Pages, not sub-grids. |

---

## After your first build

Open your page in MDA and run the visual smoke checklist in [`docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md) §7. If anything looks different from sprk_invoicespage or sprk_kpiassessmentspage, you've drifted from the contract — fix the divergence before shipping.
