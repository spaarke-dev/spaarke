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

/** Parse matterId from URL: ?matterId= OR ?filterValue= OR JSON-encoded ?data=... envelope. */
function parseMatterId(): string {
  const params = new URLSearchParams(window.location.search);
  let id = params.get("matterId") ?? params.get("filterValue") ?? "";
  if (!id) {
    const raw = params.get("data");
    if (raw) {
      try {
        const obj = JSON.parse(raw) as { matterId?: string };
        if (obj?.matterId) id = obj.matterId;
      } catch { /* graceful */ }
    }
  }
  return id;
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
      />
    </FluentProvider>
  );
};

const root = document.getElementById("root");
if (root) createRoot(root).render(<React.StrictMode><App /></React.StrictMode>);
else console.error("[sprk_kpiassessmentspage] Root element not found");
