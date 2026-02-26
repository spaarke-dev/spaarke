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

/**
 * Save the description field on an event record.
 */
export async function saveDescription(
  eventId: string,
  description: string
): Promise<ISaveResult> {
  const webApi = getXrmWebApi();
  if (!webApi) {
    return { success: false, error: "Xrm.WebApi not available" };
  }

  try {
    await webApi.updateRecord("sprk_event", eventId, {
      sprk_description: description,
    });
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error("[todoService] Failed to save description:", message);
    return { success: false, error: message };
  }
}
