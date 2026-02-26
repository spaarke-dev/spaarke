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
