/**
 * sprk_kpiassessmentspage - KPI Assessments Matter Health drill-through Custom Page.
 *
 * Shell-only per ADR-026: parses URL params, builds parentContext, mounts FluentProvider
 * + <DataGrid configId=... /> from @spaarke/ui-components. Configjson behavior.parentContextFilter
 * (attribute=sprk_matter, parentContextKey=matterId, operator=eq) drives FetchXML overlay
 * inside the framework (per task 020 D-020-02 + commit fe4f675d). NO business logic here.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/023-drill-through-custom-pages.poml
 * @see projects/spaarke-datagrid-framework-r1/notes/drafts/021-config-record-id.md
 */
import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import {
  DataGrid,
  XrmDataverseClient,
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";

// Configuration ID from task 021 (sprk_gridconfiguration record for KPI Assessments).
const CONFIG_ID = "3019a06e-9b5e-f111-ab0c-7c1e521545d7";

/**
 * Parse matterId from URL.
 *
 * VisualHost CardChrome opens this page via `Xrm.Navigation.navigateTo({
 *   pageType: 'webresource', webresourceName, data: '<form-encoded string>'
 * })` — the `data` envelope is a URL-encoded form string (NOT JSON) with keys
 * `entityName`, `filterField`, `filterValue`, `viewId`, `mode`. The Matter
 * record id arrives as `filterValue` inside that envelope.
 *
 * Also tolerates direct-launch URLs (`?matterId=…`) and a JSON envelope for
 * legacy callers.
 */
function parseMatterId(): string {
  const params = new URLSearchParams(window.location.search);
  let id = params.get("matterId") ?? params.get("filterValue") ?? "";
  if (!id) {
    const raw = params.get("data");
    if (raw) {
      // 1) Form-encoded envelope (VisualHost CardChrome path).
      const inner = new URLSearchParams(raw);
      id = inner.get("filterValue") ?? inner.get("matterId") ?? "";
      // 2) JSON envelope (legacy fallback).
      if (!id) {
        try {
          const obj = JSON.parse(raw) as { matterId?: string };
          if (obj?.matterId) id = obj.matterId;
        } catch { /* graceful */ }
      }
    }
  }
  // 3) Fallback: read the active parent Matter form's record id via Xrm.
  //    The Custom Page dialog is nested deeper than one frame
  //    (dialog → VisualHost iframe → Matter form → MDA shell), so we walk
  //    BOTH `window.parent` AND `window.top` and try multiple Xrm APIs.
  if (!id) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const candidateWindows: any[] = [];
    try { candidateWindows.push(window.parent); } catch { /* same-origin only */ }
    try { candidateWindows.push(window.top); } catch { /* same-origin only */ }
    for (const w of candidateWindows) {
      if (!w) continue;
      try {
        const xrm = w.Xrm;
        // Legacy: Xrm.Page.data.entity.getId()
        const legacyId: string | undefined = xrm?.Page?.data?.entity?.getId?.();
        if (legacyId) { id = legacyId; break; }
        // Modern: Xrm.Utility.getPageContext().input.entityId
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

const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  React.useEffect(() => setupCodePageThemeListener(() => setTheme(resolveCodePageTheme())), []);
  const parentContext = React.useMemo(() => ({ matterId: parseMatterId() }), []);
  return (
    <FluentProvider theme={theme} applyStylesToPortals={true} style={{ height: "100%" }}>
      <DataGrid
        configId={CONFIG_ID}
        parentContext={parentContext}
        dataverseClient={new XrmDataverseClient()}
        theme={theme}
        onBack={() => window.close()}
      />
    </FluentProvider>
  );
};

const root = document.getElementById("root");
if (root) createRoot(root).render(<React.StrictMode><App /></React.StrictMode>);
else console.error("[sprk_kpiassessmentspage] Root element not found");
