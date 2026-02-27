/**
 * todoService — Load and update records for the Todo Detail side pane.
 *
 * Data spans TWO entities:
 *   - sprk_event: core event fields (description, scores, lookups)
 *   - sprk_eventtodo: to-do extension fields (notes, completed, statuscode)
 */

import { getXrmWebApi } from "../utils/xrmAccess";
import {
  ITodoRecord,
  ITodoExtension,
  TODO_DETAIL_SELECT,
  TODO_EXTENSION_SELECT,
} from "../types/TodoRecord";

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ILoadResult {
  success: boolean;
  data: ITodoRecord | null;
  error?: string;
}

export interface ILoadExtensionResult {
  success: boolean;
  data: ITodoExtension | null;
  error?: string;
}

export interface ISaveResult {
  success: boolean;
  error?: string;
}

// ---------------------------------------------------------------------------
// Load sprk_event
// ---------------------------------------------------------------------------

/**
 * Load a single event record for the detail pane.
 */
export async function loadTodoRecord(eventId: string): Promise<ILoadResult> {
  const webApi = getXrmWebApi();
  if (!webApi) {
    return { success: false, data: null, error: "Xrm.WebApi not available" };
  }

  try {
    const record = await webApi.retrieveRecord(
      "sprk_event",
      eventId,
      `?$select=${TODO_DETAIL_SELECT}`
    );
    return { success: true, data: record as ITodoRecord };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoService] Failed to load event record:", message);
    return { success: false, data: null, error: message };
  }
}

// ---------------------------------------------------------------------------
// Load sprk_eventtodo (by event ID)
// ---------------------------------------------------------------------------

/**
 * Load the sprk_eventtodo record associated with a given event.
 * Returns null data (success=true) if no todo extension exists for the event.
 */
export async function loadTodoExtension(
  eventId: string
): Promise<ILoadExtensionResult> {
  const webApi = getXrmWebApi();
  if (!webApi) {
    return { success: false, data: null, error: "Xrm.WebApi not available" };
  }

  try {
    const filter = `_sprk_event_value eq ${eventId}`;
    const result = await webApi.retrieveMultipleRecords(
      "sprk_eventtodo",
      `?$select=${TODO_EXTENSION_SELECT}&$filter=${filter}&$top=1`
    );
    const entities = result.entities ?? [];
    if (entities.length === 0) {
      return { success: true, data: null };
    }
    return { success: true, data: entities[0] as ITodoExtension };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.warn("[todoService] Failed to load todo extension:", message);
    // Non-fatal: the side pane can still show event fields
    return { success: true, data: null };
  }
}

// ---------------------------------------------------------------------------
// Save sprk_event fields
// ---------------------------------------------------------------------------

/** Fields that can be saved on sprk_event. */
export interface IEventFieldUpdates {
  sprk_description?: string;
  sprk_duedate?: string | null;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  /** Boolean flag — set to false to remove event from To Do board. */
  sprk_todoflag?: boolean;
  /** OData bind for the Assigned To lookup (contact table). */
  "sprk_AssignedTo@odata.bind"?: string | null;
}

/**
 * Save one or more editable fields on the sprk_event record.
 */
export async function saveTodoFields(
  eventId: string,
  fields: IEventFieldUpdates
): Promise<ISaveResult> {
  const webApi = getXrmWebApi();
  if (!webApi) {
    return { success: false, error: "Xrm.WebApi not available" };
  }

  try {
    await webApi.updateRecord("sprk_event", eventId, fields);
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoService] Failed to save event fields:", message);
    return { success: false, error: message };
  }
}

// ---------------------------------------------------------------------------
// Save sprk_eventtodo fields
// ---------------------------------------------------------------------------

/** Fields that can be saved on sprk_eventtodo. */
export interface ITodoExtensionUpdates {
  sprk_todonotes?: string;
  sprk_completed?: boolean;
  sprk_completeddate?: string;
  statuscode?: number;
}

/**
 * Save fields on the sprk_eventtodo record.
 */
export async function saveTodoExtensionFields(
  todoId: string,
  fields: ITodoExtensionUpdates
): Promise<ISaveResult> {
  const webApi = getXrmWebApi();
  if (!webApi) {
    return { success: false, error: "Xrm.WebApi not available" };
  }

  try {
    await webApi.updateRecord("sprk_eventtodo", todoId, fields);
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoService] Failed to save todo extension:", message);
    return { success: false, error: message };
  }
}

// ---------------------------------------------------------------------------
// Contact search for Assigned To lookup
// ---------------------------------------------------------------------------

export interface IContactOption {
  id: string;
  name: string;
}

/**
 * Search standard contact records by name for the Assigned To picker.
 */
export async function searchContacts(query: string): Promise<IContactOption[]> {
  const webApi = getXrmWebApi();
  if (!webApi || !query.trim()) return [];

  try {
    const filter = `contains(fullname,'${query.replace(/'/g, "''")}')`;
    const result = await webApi.retrieveMultipleRecords(
      "contact",
      `?$select=contactid,fullname&$filter=${filter}&$top=10&$orderby=fullname asc`
    );
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (result.entities ?? []).map((e: any) => ({
      id: e.contactid as string,
      name: (e.fullname ?? "") as string,
    }));
  } catch (err) {
    console.warn("[todoService] Contact search failed:", err);
    return [];
  }
}
