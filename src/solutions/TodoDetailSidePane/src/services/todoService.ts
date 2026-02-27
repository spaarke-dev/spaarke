/**
 * todoService — Load and update event records for the Todo Detail side pane.
 */

import { getXrmWebApi } from "../utils/xrmAccess";
import { ITodoRecord, TODO_DETAIL_SELECT } from "../types/TodoRecord";

export interface ILoadResult {
  success: boolean;
  data: ITodoRecord | null;
  error?: string;
}

export interface ISaveResult {
  success: boolean;
  error?: string;
}

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
    console.error("[todoService] Failed to load record:", message);
    return { success: false, data: null, error: message };
  }
}

/** Fields that can be saved from the detail pane. */
export interface ITodoFieldUpdates {
  sprk_description?: string;
  sprk_duedate?: string | null;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  /** Boolean flag — set to false to remove event from To Do board. */
  sprk_todoflag?: boolean;
  /** OData bind for the Assigned To lookup (sprk_contact). */
  "sprk_AssignedTo@odata.bind"?: string | null;
}

/**
 * Save one or more editable fields on an event record.
 */
export async function saveTodoFields(
  eventId: string,
  fields: ITodoFieldUpdates
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
    console.error("[todoService] Failed to save fields:", message);
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
 * Search sprk_contact records by name for the Assigned To picker.
 */
export async function searchContacts(query: string): Promise<IContactOption[]> {
  const webApi = getXrmWebApi();
  if (!webApi || !query.trim()) return [];

  try {
    const filter = `contains(sprk_name,'${query.replace(/'/g, "''")}')`;
    const result = await webApi.retrieveMultipleRecords(
      "sprk_contact",
      `?$select=sprk_contactid,sprk_name&$filter=${filter}&$top=10&$orderby=sprk_name asc`
    );
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (result.entities ?? []).map((e: any) => ({
      id: e.sprk_contactid as string,
      name: (e.sprk_name ?? "") as string,
    }));
  } catch (err) {
    console.warn("[todoService] Contact search failed:", err);
    return [];
  }
}
