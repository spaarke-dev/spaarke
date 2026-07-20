/**
 * sprk_communicationspage — global All-Communications DataGrid Custom Page.
 *
 * **messaging-communication-app-r2 task 040 (FR-11)**: a thin ~50-line shell
 * copying `src/solutions/sprk_invoicespage/src/main.tsx`. Mounts the shipped
 * `<DataGridPageShell>` from `@spaarke/ui-components` bound to the existing
 * "Active Communications" grid config (entity `sprk_communication`). The shell
 * handles FluentProvider + theme listener + box-sizing reset + `XrmDataverseClient`.
 *
 * Unlike `sprk_invoicespage` (a matter-scoped drill-through that parses a
 * `matterId` → `parentContext`), this page is GLOBAL — all communications, no
 * `parentContext`. Channel / person / date / regarding filter chips auto-derive
 * from the config's curated views (task 041). No business logic lives here.
 *
 * REUSE the shipped config `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` — do NOT create
 * a second `sprk_gridconfiguration` default for `sprk_communication` (NFR-05).
 *
 * Reached via widget / launcher / deep link — NO permanent sitemap entry in R2
 * (owner Q-B). See `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`.
 */
import * as React from "react";
import { createRoot } from "react-dom/client";
import { DataGridPageShell } from "@spaarke/ui-components";

const CONFIG_ID = "e1826c4c-9575-f111-ab0e-7ced8ddc4a05";

const App: React.FC = () => (
  <DataGridPageShell configId={CONFIG_ID} onBack={() => window.close()} />
);

const root = document.getElementById("root");
if (root) {
  createRoot(root).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  // eslint-disable-next-line no-console
  console.error("[sprk_communicationspage] Root element not found");
}
