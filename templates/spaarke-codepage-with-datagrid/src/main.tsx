/**
 * <your-page-name> — Spaarke DataGrid Code Page.
 *
 * See ../README.md and docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md.
 *
 * The default mount handles the COMMON case: a drill-through dialog filtered
 * by a single matterId from the `data=` envelope. Adjust as needed for your
 * page; comments below highlight the swap points.
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import {
  DataGridPageShell,
  type HostFilterCondition,
} from "@spaarke/ui-components";

// 1) Replace with your sprk_gridconfiguration record GUID.
const CONFIG_ID = "__YOUR_CONFIG_ID__";

// 2) Optional — drop this block if your page doesn't take a parent context.
//    The shell parses the URL `?data=filterValue=<matterId>` envelope and
//    builds the parent context. Your configjson's `behavior.parentContextFilter`
//    consumes it.
const URL_PARENT_CONTEXT = {
  key: "matterId",
  // entityType: "sprk_matter",     // override if your child entity points at something else
};

// 3) Optional — drop this block if your page doesn't have a side pane.
//    The pane web resource sends `sendSidePaneFilter({ paneId, payload })`;
//    your translator converts payload → HostFilterCondition[] for the grid.
const SIDE_PANE_FILTER = {
  paneId: "your-pane-id",
  translator: (payload: { from?: string; to?: string; field?: string }): HostFilterCondition[] => {
    if (!payload?.from || !payload?.to) return [];
    return [
      {
        attribute: payload.field ?? "sprk_duedate",
        operator: "between",
        value: [payload.from, payload.to],
      },
    ];
  },
};

const App: React.FC = () => (
  <DataGridPageShell
    configId={CONFIG_ID}
    useUrlParentContext={URL_PARENT_CONTEXT}
    sidePaneFilter={SIDE_PANE_FILTER}
    onBack={() => window.close()}
  />
);

const rootEl = document.getElementById("root");
if (rootEl) {
  createRoot(rootEl).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  // eslint-disable-next-line no-console
  console.error("[your-page-name] Root element not found");
}
