/**
 * sprk_communicationspage — All-Communications DataGrid Custom Page.
 *
 * **messaging-communication-app-r2 task 040 (FR-11)**: a thin ~50-line shell
 * copying `src/solutions/sprk_invoicespage/src/main.tsx`. Mounts the shipped
 * `<DataGridPageShell>` from `@spaarke/ui-components` bound to the existing
 * "Active Communications" grid config (entity `sprk_communication`). The shell
 * handles FluentProvider + theme listener + box-sizing reset + `XrmDataverseClient`.
 *
 * Unlike `sprk_invoicespage` (a matter-scoped drill-through that parses a
 * `matterId` → `parentContext`), this page's DEFAULT mode (no `data` envelope
 * present) is GLOBAL — all communications, no host filter. Channel / person /
 * date / regarding filter chips auto-derive from the config's curated views
 * (task 041). No business logic lives here.
 *
 * REUSE the shipped config `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` — do NOT create
 * a second `sprk_gridconfiguration` default for `sprk_communication` (NFR-05/NFR-06).
 *
 * **messaging-communication-app-r3 task 033 (FR-15)**: this page is ALSO
 * mounted directly on a record form's "Email & Messages" tab, as a "Web
 * Resource" control. The companion form `onLoad` script
 * (`src/dataverse/forms/sprk_matter/communicationsGridOnLoad.ts`, Matter
 * pilot; entity-agnostic, generalizes across all 11 ADR-024 regarding-family
 * entities; deployed as `sprk_/scripts/communications_grid_onload.js`) pushes a
 * `data=` envelope onto that control (`setData()` where the control has it,
 * else `setSrc`) — `regardingAttribute` (the host
 * record's `sprk_regarding{type}` lookup field name) + `regardingValue` (the
 * host record's id). `parseRegardingEnvelope` (`hostCommands.ts`) reads that envelope and
 * forwards it as `additionalHostFilters` to `<DataGridPageShell>`, which the
 * shared DataGrid framework overlays into the grid's FetchXML via
 * `overlayHostFilters` (`fetchXmlOverlay.ts`) — the SANCTIONED `hostFilters`
 * imperative composition layer (ADR-006 Path C: web-resource framework, not a
 * PCF, not a bespoke JS filter; no second regarding mechanism — ADR-024).
 *
 * Absent envelope (the pre-existing global/launcher entry point) is a
 * no-op — `additionalHostFilters` resolves to `undefined` and the grid
 * renders exactly as it did before this task (unfiltered). This is the SAME
 * page serving both modes; no second Code Page, no second grid config.
 *
 * Reached via widget / launcher / deep link (global mode) OR the "Email &
 * Messages" form tab (record-scoped mode) — NO permanent sitemap entry
 * (owner Q-B, R2). See `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`.
 */
import * as React from "react";
import { createRoot } from "react-dom/client";
import { Add20Regular } from "@fluentui/react-icons";
import {
  DataGridPageShell,
  getXrm,
  openConversationPanel,
  openEmailCompose,
  type CommandBarAction,
  type DataGridHostCommand,
} from "@spaarke/ui-components";
import { buildHostCommands, hostFiltersFor, parseRegardingEnvelope, type FormEntityLike } from "./hostCommands";

const CONFIG_ID = "e1826c4c-9575-f111-ab0e-7ced8ddc4a05";

// Record scoping, the fail-closed filter and the "+ New Email" / "+ New Message" logic live in `hostCommands.ts`
// (tested in `__tests__/hostCommands.test.ts`); this file only supplies the browser and host functions.
const regardingEnvelope = parseRegardingEnvelope(typeof window === "undefined" ? "" : window.location.search);

if (regardingEnvelope.kind === "malformed") {
  // eslint-disable-next-line no-console
  console.error(
    "[sprk_communicationspage] Malformed regarding envelope (regardingAttribute/regardingValue — only one present). " +
      "Rendering a fail-closed empty grid instead of falling back to unfiltered. Check the host onLoad script."
  );
}

const hostFilters = hostFiltersFor(regardingEnvelope);

// Record-scoped mode (embedded on a form tab) has no "back" affordance to
// offer — there is no dialog/window to close. Suppress it whenever ANY
// regarding envelope was detected (valid or malformed) — only the pure
// global/launcher entry point (no envelope at all) shows the back arrow.
const isRecordScoped = regardingEnvelope.kind !== "none";

/** The built-in commands this page does not offer (the OOB "+ New" form). Module scope, so the array is stable. */
const HIDDEN_BUILT_IN_COMMANDS: ReadonlyArray<CommandBarAction> = ["create-form"];

function alertNewMessage(text: string): void {
  // The nearest frame that can open an alert (the typed XrmContext does not declare openAlertDialog).
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm: any = getXrm((x: any) => typeof x.Navigation?.openAlertDialog === "function");
  if (typeof xrm?.Navigation?.openAlertDialog === "function") {
    void xrm.Navigation.openAlertDialog({ title: "New Message", text });
  } else {
    window.alert(text);
  }
}

const HOST_COMMANDS: ReadonlyArray<DataGridHostCommand> = buildHostCommands(
  regardingEnvelope,
  {
    // `any` view: the typed XrmPageLike does not declare `data.entity`.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    readFormEntity: () => (getXrm("page") as any)?.Page?.data?.entity as FormEntityLike | undefined,
    openEmailCompose,
    openConversationPanel,
    alert: alertNewMessage,
    // eslint-disable-next-line no-console
    warn: message => console.warn(message),
  },
  <Add20Regular />
);

const App: React.FC = () => (
  <DataGridPageShell
    configId={CONFIG_ID}
    additionalHostFilters={hostFilters}
    hostCommands={HOST_COMMANDS}
    hiddenBuiltInCommands={HIDDEN_BUILT_IN_COMMANDS}
    onBack={isRecordScoped ? undefined : () => window.close()}
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
  console.error("[sprk_communicationspage] Root element not found");
}
