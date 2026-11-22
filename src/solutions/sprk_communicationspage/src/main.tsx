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
 * entities) pushes a `data=` envelope onto that control via the documented
 * `WebResourceControl.setData()` Client API — `regardingAttribute` (the host
 * record's `sprk_regarding{type}` lookup field name) + `regardingValue` (the
 * host record's id). `parseRegardingHostFilter` below reads that envelope and
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
  cleanGuid,
  getXrm,
  openConversationPanel,
  openEmailCompose,
  type CommandBarAction,
  type ComposeRegarding,
  type DataGridHostCommand,
  type HostFilterCondition,
} from "@spaarke/ui-components";

const CONFIG_ID = "e1826c4c-9575-f111-ab0e-7ced8ddc4a05";

/** Outcome of parsing the `regardingAttribute`/`regardingValue` envelope. */
type RegardingEnvelope =
  | { kind: "none" }
  | { kind: "valid"; hostFilters: HostFilterCondition[] }
  | { kind: "malformed" };

/**
 * Parse the record-scoping hostFilter pushed by the form `onLoad` script's
 * `data=` envelope (see `communicationsGridOnLoad.ts`). Checks the top-level
 * query string first; only falls back to unwrapping a `data=` param (the
 * `WebResourceControl.setData()` convention — mirrors
 * `DataGridPageShell.tsx`'s `parseUrlParentContext` fallback shape) when
 * NEITHER key is present at the top level — never mixes one key from the
 * outer query string with the other from the `data` envelope.
 *
 * Three outcomes:
 *   - `none` — neither key present anywhere (the pre-existing global/launcher
 *     entry point). Correct, intentional no-op.
 *   - `valid` — both keys present. Scoped `hostFilters` condition.
 *   - `malformed` — exactly ONE key present. This can only happen from a
 *     wiring bug (the onLoad script always sets both together or neither —
 *     see `communicationsGridOnLoad.ts`). Deliberately distinguished from
 *     `none` so the caller can fail CLOSED instead of silently falling back
 *     to an unfiltered/global render (over-disclosure risk).
 */
function parseRegardingEnvelope(): RegardingEnvelope {
  if (typeof window === "undefined") return { kind: "none" };
  try {
    const outer = new URLSearchParams(window.location.search);
    const readBoth = (params: URLSearchParams): { attribute: string | null; value: string | null } => ({
      attribute: params.get("regardingAttribute"),
      value: params.get("regardingValue"),
    });

    let { attribute, value } = readBoth(outer);
    if (!attribute && !value) {
      const data = outer.get("data");
      if (data) {
        ({ attribute, value } = readBoth(new URLSearchParams(data)));
      }
    }

    if (!attribute && !value) return { kind: "none" };
    if (!attribute || !value) return { kind: "malformed" };

    return { kind: "valid", hostFilters: [{ attribute, operator: "eq", value }] };
  } catch {
    // Graceful degradation — malformed/unexpected query string never breaks
    // the page; treated as "no envelope" (global mode), NOT as a scoping
    // failure — an exception here means the URL couldn't be parsed at all
    // (no attacker-controlled partial envelope to fail closed against).
    return { kind: "none" };
  }
}

const regardingEnvelope = parseRegardingEnvelope();

// Fail CLOSED on a malformed envelope: an impossible condition (never
// matches any `sprk_communication` row) renders an EMPTY grid rather than
// risk falling back to the unfiltered/global view (over-disclosure). A
// malformed envelope only indicates a host wiring bug — see
// `communicationsGridOnLoad.ts`'s `buildDataEnvelope`, which always sets
// both keys together.
const IMPOSSIBLE_HOST_FILTER: HostFilterCondition[] = [
  { attribute: "sprk_communicationid", operator: "eq", value: "00000000-0000-0000-0000-000000000000" },
];

if (regardingEnvelope.kind === "malformed") {
  // eslint-disable-next-line no-console
  console.error(
    "[sprk_communicationspage] Malformed regarding envelope (regardingAttribute/regardingValue — only one present). " +
      "Rendering a fail-closed empty grid instead of falling back to unfiltered. Check the host onLoad script."
  );
}

const hostFilters: HostFilterCondition[] | undefined =
  regardingEnvelope.kind === "valid"
    ? regardingEnvelope.hostFilters
    : regardingEnvelope.kind === "malformed"
      ? IMPOSSIBLE_HOST_FILTER
      : undefined;

// Record-scoped mode (embedded on a form tab) has no "back" affordance to
// offer — there is no dialog/window to close. Suppress it whenever ANY
// regarding envelope was detected (valid or malformed) — only the pure
// global/launcher entry point (no envelope at all) shows the back arrow.
const isRecordScoped = regardingEnvelope.kind !== "none";

// ── "+ New Email" / "+ New Message" (#2908) ─────────────────────────────────
// The grid never offers the platform "+ New" (the OOB sprk_communication form). In its place:
//   - "New Email" opens the sprk_emailpage composer (the same surface as everywhere else), "Related to" the host
//     record when the grid sits on a record form;
//   - "New Message" (record form only) opens, in place, the Messages modal of the CommunicationConversationPanel
//     already on the form. When no panel answers, it says so; it never reloads the page or opens another surface.

/** The built-in commands this page does not offer. Module scope, so the array is stable. */
const HIDDEN_BUILT_IN_COMMANDS: ReadonlyArray<CommandBarAction> = ["create-form"];

/**
 * The record whose form hosts this grid: its table, id and display name, read from the form context. Only for a
 * valid envelope, and only when the form's id is the id the envelope scopes to (otherwise undefined: nothing is
 * attached to a record the grid does not show).
 */
function resolveHostRecord(): ComposeRegarding | undefined {
  if (regardingEnvelope.kind !== "valid") return undefined;
  const scopedId = cleanGuid(String(regardingEnvelope.hostFilters[0]?.value ?? "")).toLowerCase();
  try {
    // `any` view: the typed XrmPageLike does not declare `data.entity`.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const entity = (getXrm("page") as any)?.Page?.data?.entity;
    const entityType = String(entity?.getEntityName?.() ?? "").toLowerCase();
    const id = cleanGuid(String(entity?.getId?.() ?? "")).toLowerCase();
    if (!entityType || !id || id !== scopedId) return undefined;
    const name = entity?.getPrimaryAttributeValue?.();
    return { entityType, id, name: typeof name === "string" && name ? name : undefined };
  } catch {
    return undefined;
  }
}

function newEmail(): void {
  const regarding = resolveHostRecord();
  if (regardingEnvelope.kind === "valid" && !regarding) {
    // eslint-disable-next-line no-console
    console.warn("[sprk_communicationspage] Could not read the host record; the new email opens without \"Related to\".");
  }
  void openEmailCompose({ mode: "new", regarding });
}

function alertNewMessage(text: string): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrm("navigation") as any;
  if (typeof xrm?.Navigation?.openAlertDialog === "function") {
    void xrm.Navigation.openAlertDialog({ title: "New Message", text });
  } else {
    window.alert(text);
  }
}

function newMessage(): void {
  const host = resolveHostRecord();
  if (!host) {
    // eslint-disable-next-line no-console
    console.warn("[sprk_communicationspage] Could not read the host record; the Messages modal cannot open.");
    alertNewMessage("Messages could not be opened for this record. Reload the page and try again.");
    return;
  }
  // "no-panel" means no panel answered: the form may have none, its panel may not be ready (or not loaded on this
  // tab), or it may be an older panel build. Say only what is known.
  if (openConversationPanel({ entityType: host.entityType, id: host.id }) === "no-panel") {
    alertNewMessage(
      "The Messages panel did not respond, so a message cannot be started from here. Open Messages from the panel on this record's form."
    );
  }
}

/** "New Message" needs a host record, so it is offered only on a record form with a valid envelope. */
const HOST_COMMANDS: ReadonlyArray<DataGridHostCommand> = [
  { id: "new-email", label: "New Email", icon: <Add20Regular />, onInvoke: newEmail },
  ...(regardingEnvelope.kind === "valid"
    ? [{ id: "new-message", label: "New Message", icon: <Add20Regular />, onInvoke: newMessage }]
    : []),
];

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
