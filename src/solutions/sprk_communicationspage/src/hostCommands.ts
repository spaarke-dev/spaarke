/**
 * Record scoping and the "+ New Email" / "+ New Message" commands of the communications grid page (#2908, ISS-008).
 *
 * Moved out of `main.tsx` unchanged in behaviour so the branches can be tested: `main.tsx` passes in the URL and the
 * host functions (form context, composer launcher, Messages panel launcher, alert). See `main.tsx` for where the
 * `data=` envelope comes from (the form `onLoad` script `sprk_/scripts/communications_grid_onload.js`).
 */
import type { ReactElement } from "react";
import {
  cleanGuid,
  type ComposeRegarding,
  type DataGridHostCommand,
  type HostFilterCondition,
} from "@spaarke/ui-components";

/** Outcome of parsing the `regardingAttribute`/`regardingValue` envelope. */
export type RegardingEnvelope =
  | { kind: "none" }
  | { kind: "valid"; hostFilters: HostFilterCondition[] }
  | { kind: "malformed" };

/**
 * Parse the record-scoping hostFilter pushed by the form `onLoad` script's `data=` envelope. Checks the top-level
 * query string first; only falls back to unwrapping a `data=` param (the `WebResourceControl.setData()` /
 * `setSrc` convention) when NEITHER key is present at the top level — never mixes one key from the outer query string
 * with the other from the `data` envelope.
 *
 * Three outcomes:
 *   - `none` — neither key present anywhere (the global/launcher entry point). Correct, intentional no-op.
 *   - `valid` — both keys present. Scoped `hostFilters` condition.
 *   - `malformed` — exactly ONE key present. Only a wiring bug produces this (the onLoad script always sets both or
 *     neither), so the caller fails CLOSED instead of falling back to an unfiltered/global render.
 */
export function parseRegardingEnvelope(search: string): RegardingEnvelope {
  try {
    const outer = new URLSearchParams(search);
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
    // An unparseable query string never breaks the page; treated as "no envelope" (global mode).
    return { kind: "none" };
  }
}

/** Never matches a `sprk_communication` row: the fail-closed filter for a malformed envelope. */
export const IMPOSSIBLE_HOST_FILTER: HostFilterCondition[] = [
  { attribute: "sprk_communicationid", operator: "eq", value: "00000000-0000-0000-0000-000000000000" },
];

/**
 * The grid's host filters: the envelope's condition when valid, an empty grid when malformed (never the unfiltered
 * view), none in global mode.
 */
export function hostFiltersFor(envelope: RegardingEnvelope): HostFilterCondition[] | undefined {
  if (envelope.kind === "valid") return envelope.hostFilters;
  if (envelope.kind === "malformed") return IMPOSSIBLE_HOST_FILTER;
  return undefined;
}

/** The slice of the form's `Xrm.Page.data.entity` this page reads. Every member is optional. */
export interface FormEntityLike {
  getEntityName?: () => unknown;
  getId?: () => unknown;
  getPrimaryAttributeValue?: () => unknown;
}

/**
 * The record whose form hosts this grid: its table, id and display name, read from the form context. Only for a
 * valid envelope, and only when the form's id is the id the envelope scopes to (otherwise undefined: nothing is
 * attached to a record the grid does not show).
 */
export function resolveHostRecord(
  envelope: RegardingEnvelope,
  readFormEntity: () => FormEntityLike | undefined
): ComposeRegarding | undefined {
  if (envelope.kind !== "valid") return undefined;
  const scopedId = cleanGuid(String(envelope.hostFilters[0]?.value ?? "")).toLowerCase();
  try {
    const entity = readFormEntity();
    const entityType = String(entity?.getEntityName?.() ?? "").toLowerCase();
    const id = cleanGuid(String(entity?.getId?.() ?? "")).toLowerCase();
    if (!entityType || !id || id !== scopedId) return undefined;
    const name = entity?.getPrimaryAttributeValue?.();
    return { entityType, id, name: typeof name === "string" && name ? name : undefined };
  } catch {
    return undefined;
  }
}

/** The host functions the commands call. `main.tsx` passes the real ones; tests pass fakes. */
export interface HostCommandDeps {
  readFormEntity: () => FormEntityLike | undefined;
  openEmailCompose: (options: { mode: "new"; regarding?: ComposeRegarding }) => unknown;
  /** Returns `"no-panel"` when no CommunicationConversationPanel on the form answered. */
  openConversationPanel: (target: { entityType: string; id: string }) => unknown;
  alert: (text: string) => void;
  warn: (message: string) => void;
}

export const NEW_MESSAGE_UNAVAILABLE_TEXT = "Messages could not be opened for this record. Reload the page and try again.";
export const NEW_MESSAGE_NO_PANEL_TEXT =
  "The Messages panel did not respond, so a message cannot be started from here. Open Messages from the panel on this record's form.";

/** "+ New Email": the composer, "Related to" the host record when the grid sits on its form. */
export function newEmail(envelope: RegardingEnvelope, deps: HostCommandDeps): void {
  const regarding = resolveHostRecord(envelope, deps.readFormEntity);
  if (envelope.kind === "valid" && !regarding) {
    deps.warn('[sprk_communicationspage] Could not read the host record; the new email opens without "Related to".');
  }
  void deps.openEmailCompose({ mode: "new", regarding });
}

/**
 * "+ New Message": opens, in place, the Messages modal of the CommunicationConversationPanel already on the form.
 * When no panel answers it says so; it never reloads the page or opens another surface.
 */
export function newMessage(envelope: RegardingEnvelope, deps: HostCommandDeps): void {
  const host = resolveHostRecord(envelope, deps.readFormEntity);
  if (!host) {
    deps.warn("[sprk_communicationspage] Could not read the host record; the Messages modal cannot open.");
    deps.alert(NEW_MESSAGE_UNAVAILABLE_TEXT);
    return;
  }
  // "no-panel": the form may have none, its panel may not be ready, or it may be an older panel build.
  if (deps.openConversationPanel({ entityType: host.entityType, id: host.id }) === "no-panel") {
    deps.alert(NEW_MESSAGE_NO_PANEL_TEXT);
  }
}

/** The grid's own commands. "New Message" needs a host record, so it is offered only for a valid envelope. */
export function buildHostCommands(
  envelope: RegardingEnvelope,
  deps: HostCommandDeps,
  icon?: ReactElement
): DataGridHostCommand[] {
  const commands: DataGridHostCommand[] = [
    { id: "new-email", label: "New Email", icon, onInvoke: () => newEmail(envelope, deps) },
  ];
  if (envelope.kind === "valid") {
    commands.push({ id: "new-message", label: "New Message", icon, onInvoke: () => newMessage(envelope, deps) });
  }
  return commands;
}
