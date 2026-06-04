/**
 * sprk_kpiassessmentspage — KPI Assessments Matter Health drill-through Custom Page.
 *
 * **Task 035 hardening (2026-06-04)**: migrated to `<DataGridPageShell>` from
 * `@spaarke/ui-components`. The shell handles FluentProvider + theme listener
 * + box-sizing reset + `XrmDataverseClient` instantiation. Page-specific code
 * shrinks to: configId, parentContext parsing (Xrm.Page fallback retained),
 * and onBack. See `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`.
 *
 * The configjson's `behavior.parentContextFilter` (attribute=sprk_matter,
 * parentContextKey=matterId, operator=eq) overlays the FetchXML at runtime
 * (task 020 D-020-02). No business logic lives here.
 */
import * as React from "react";
import { createRoot } from "react-dom/client";
import {
  DataGridPageShell,
  type DataGridParentContext,
} from "@spaarke/ui-components";

const CONFIG_ID = "3019a06e-9b5e-f111-ab0c-7c1e521545d7";

function parseMatterId(): string {
  const params = new URLSearchParams(window.location.search);
  let id = params.get("matterId") ?? params.get("filterValue") ?? "";
  if (!id) {
    const raw = params.get("data");
    if (raw) {
      const inner = new URLSearchParams(raw);
      id = inner.get("filterValue") ?? inner.get("matterId") ?? "";
      if (!id) {
        try {
          const obj = JSON.parse(raw) as { matterId?: string };
          if (obj?.matterId) id = obj.matterId;
        } catch { /* graceful */ }
      }
    }
  }
  if (!id) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const candidateWindows: any[] = [];
    try { candidateWindows.push(window.parent); } catch { /* same-origin only */ }
    try { candidateWindows.push(window.top); } catch { /* same-origin only */ }
    for (const w of candidateWindows) {
      if (!w) continue;
      try {
        const xrm = w.Xrm;
        const legacyId: string | undefined = xrm?.Page?.data?.entity?.getId?.();
        if (legacyId) { id = legacyId; break; }
        const pageCtx = xrm?.Utility?.getPageContext?.();
        const entityId: string | undefined = pageCtx?.input?.entityId;
        if (entityId) { id = entityId; break; }
      } catch { /* cross-origin or unavailable */ }
    }
  }
  // eslint-disable-next-line no-console
  console.info("[sprk_kpiassessmentspage] parseMatterId resolved to:", id);
  return id.replace(/[{}]/g, "");
}

function buildParentContext(): DataGridParentContext | undefined {
  const matterId = parseMatterId();
  if (!matterId) return undefined;
  return {
    entityType: "sprk_matter",
    id: matterId,
    name: "",
    matterId,
  } as DataGridParentContext;
}

const App: React.FC = () => (
  <DataGridPageShell
    configId={CONFIG_ID}
    parentContext={buildParentContext()}
    onBack={() => window.close()}
  />
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
  console.error("[sprk_kpiassessmentspage] Root element not found");
}
