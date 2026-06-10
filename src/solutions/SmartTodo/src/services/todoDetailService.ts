/**
 * todoDetailService — Load and update `sprk_todo` records for the TodoDetail panel.
 *
 * Single-entity model (R3 FR-09 / task 011). The legacy two-entity
 * (`sprk_event` + `sprk_eventtodo`) loaders + the `loadTodoExtension`,
 * `saveTodoExtensionFields`, `deactivateTodoExtension`, `removeTodoFlag`
 * exports were removed per OS-1 (no compat shims).
 *
 * Uses the SmartTodo xrmProvider for Xrm.WebApi access.
 */

import { getWebApi } from "./xrmProvider";
import type {
  ITodoRecord,
  ITodoFieldUpdates,
  IContactOption,
} from "@spaarke/ui-components/TodoDetail";
import { TODO_DETAIL_SELECT } from "@spaarke/ui-components/TodoDetail";

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ILoadResult {
  success: boolean;
  data: ITodoRecord | null;
  error?: string;
}

export interface ISaveResult {
  success: boolean;
  error?: string;
}

// ---------------------------------------------------------------------------
// Load sprk_todo
// ---------------------------------------------------------------------------

/**
 * Load a single `sprk_todo` record for the detail panel.
 */
export async function loadTodoRecord(todoId: string): Promise<ILoadResult> {
  const webApi = getWebApi();
  if (!webApi) {
    return { success: false, data: null, error: "Xrm.WebApi not available" };
  }

  try {
    const record = await webApi.retrieveRecord(
      "sprk_todo",
      todoId,
      `?$select=${TODO_DETAIL_SELECT}`,
    );
    return { success: true, data: record as ITodoRecord };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoDetailService] Failed to load todo record:", message);
    return { success: false, data: null, error: message };
  }
}

// ---------------------------------------------------------------------------
// Save sprk_todo fields
// ---------------------------------------------------------------------------

/**
 * Save one or more editable fields on a `sprk_todo` record. Single
 * Xrm.WebApi.updateRecord call (no parallel two-entity save).
 *
 * Callers must use PolymorphicResolverService when changing any
 * `sprk_regarding*` specific lookup; this helper is intended for the simple
 * field updates (name, description, notes, due date, scores, statuscode).
 */
export async function saveTodoFields(
  todoId: string,
  fields: ITodoFieldUpdates,
): Promise<ISaveResult> {
  const webApi = getWebApi();
  if (!webApi) {
    return { success: false, error: "Xrm.WebApi not available" };
  }

  try {
    await webApi.updateRecord("sprk_todo", todoId, fields);
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoDetailService] Failed to save todo fields:", message);
    return { success: false, error: message };
  }
}

// ---------------------------------------------------------------------------
// Dismiss sprk_todo (statecode=1 Inactive, statuscode=659490002 Dismissed)
// ---------------------------------------------------------------------------

/**
 * Dismiss a `sprk_todo` record. Sets statecode=1 / statuscode=659490002 per
 * task 009 — preserved in the DismissedSection and restorable later.
 *
 * Per the task 011 follow-up note (`tododetail-consumer-breakage-task011.md`),
 * Dismiss is the recommended host implementation for `TodoDetail.onDismissTodo`
 * in SmartTodo so users can recover dismissed items from the DismissedSection.
 * The alternative is hard delete via `webApi.deleteRecord('sprk_todo', id)`.
 */
export async function dismissTodo(todoId: string): Promise<ISaveResult> {
  const webApi = getWebApi();
  if (!webApi) {
    return { success: false, error: "Xrm.WebApi not available" };
  }

  try {
    await webApi.updateRecord("sprk_todo", todoId, {
      statecode: 1,             // Inactive
      statuscode: 659490002,    // Dismissed
    });
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoDetailService] Failed to dismiss todo:", message);
    return { success: false, error: message };
  }
}

// ---------------------------------------------------------------------------
// Search systemusers for the Assigned To lookup
// ---------------------------------------------------------------------------

/**
 * Search `systemuser` records by name for the Assigned To picker.
 *
 * Per `sprk_todo` entity schema (R3 D-1 revised 2026-06-07),
 * `sprk_assignedto` is a `systemuser` lookup — not contact. The
 * IContactOption shape is generic ({id, name}) and reused for the picker.
 */
export async function searchContacts(
  query: string,
): Promise<IContactOption[]> {
  const webApi = getWebApi();
  if (!webApi || !query.trim()) return [];

  try {
    const filter = `contains(fullname,'${query.replace(/'/g, "''")}') and isdisabled eq false`;
    const result = await webApi.retrieveMultipleRecords(
      "systemuser",
      `?$select=systemuserid,fullname&$filter=${filter}&$top=10&$orderby=fullname asc`,
    );
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (result.entities ?? []).map((e: any) => ({
      id: e.systemuserid as string,
      name: (e.fullname ?? "") as string,
    }));
  } catch (err) {
    console.warn("[todoDetailService] systemuser search failed:", err);
    return [];
  }
}
