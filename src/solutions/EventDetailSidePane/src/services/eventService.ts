/**
 * Event Service - Load and update Event records from Dataverse
 *
 * Provides WebAPI operations for Event records in the EventDetailSidePane.
 * Uses the global Xrm.WebApi available in Custom Pages.
 *
 * @see design.md - Event Detail Side Pane specification
 */

import {
  IEventRecord,
  EVENT_HEADER_SELECT_FIELDS,
  EVENT_FULL_SELECT_FIELDS,
} from "../types/EventRecord";

/**
 * Event entity logical name
 */
const EVENT_ENTITY = "sprk_event";

/**
 * Xrm.WebApi type definition (subset needed for this service)
 */
interface IXrmWebApi {
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;
  updateRecord(
    entityType: string,
    id: string,
    data: Record<string, unknown>
  ): Promise<{ entityType: string; id: string }>;
}

/**
 * Get the Xrm.WebApi object from window context
 *
 * In Custom Pages, Xrm is available from window.parent.Xrm (when in iframe)
 * or window.Xrm (when running directly).
 */
function getXrmWebApi(): IXrmWebApi | null {
  try {
    // Try window.parent.Xrm first (Custom Page in iframe)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi) {
      return parentXrm.WebApi as IXrmWebApi;
    }

    // Try window.Xrm (direct access)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.WebApi) {
      return windowXrm.WebApi as IXrmWebApi;
    }

    console.warn("[EventService] Xrm.WebApi not available");
    return null;
  } catch (error) {
    console.error("[EventService] Error accessing Xrm.WebApi:", error);
    return null;
  }
}

/**
 * Result of loadEvent operation
 */
export interface ILoadEventResult {
  success: boolean;
  event: IEventRecord | null;
  error?: string;
}

/**
 * Load Event record header fields by ID
 *
 * Loads minimal fields needed for the header section:
 * - Event name, status, Event Type, parent record info
 *
 * @param eventId - Event record GUID
 * @returns Promise with event data or error
 */
export async function loadEventHeader(eventId: string): Promise<ILoadEventResult> {
  if (!eventId) {
    return {
      success: false,
      event: null,
      error: "Event ID is required",
    };
  }

  const webApi = getXrmWebApi();
  if (!webApi) {
    return {
      success: false,
      event: null,
      error: "Xrm.WebApi not available - ensure running in Dataverse context",
    };
  }

  try {
    // Normalize GUID (remove braces if present)
    const normalizedId = eventId.replace(/[{}]/g, "");

    const record = await webApi.retrieveRecord(
      EVENT_ENTITY,
      normalizedId,
      `?$select=${EVENT_HEADER_SELECT_FIELDS}`
    );

    return {
      success: true,
      event: record as unknown as IEventRecord,
    };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error("[EventService] Failed to load event header:", errorMessage);

    return {
      success: false,
      event: null,
      error: errorMessage,
    };
  }
}

/**
 * Load full Event record by ID
 *
 * Loads all fields needed for the side pane form.
 *
 * @param eventId - Event record GUID
 * @returns Promise with event data or error
 */
export async function loadEventFull(eventId: string): Promise<ILoadEventResult> {
  if (!eventId) {
    return {
      success: false,
      event: null,
      error: "Event ID is required",
    };
  }

  const webApi = getXrmWebApi();
  if (!webApi) {
    return {
      success: false,
      event: null,
      error: "Xrm.WebApi not available - ensure running in Dataverse context",
    };
  }

  try {
    const normalizedId = eventId.replace(/[{}]/g, "");

    const record = await webApi.retrieveRecord(
      EVENT_ENTITY,
      normalizedId,
      `?$select=${EVENT_FULL_SELECT_FIELDS}`
    );

    return {
      success: true,
      event: record as unknown as IEventRecord,
    };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error("[EventService] Failed to load event:", errorMessage);

    return {
      success: false,
      event: null,
      error: errorMessage,
    };
  }
}

/**
 * Update Event record field
 *
 * @param eventId - Event record GUID
 * @param fieldName - Field schema name to update
 * @param value - New value for the field
 * @returns Promise with success status
 */
export async function updateEventField(
  eventId: string,
  fieldName: string,
  value: unknown
): Promise<{ success: boolean; error?: string }> {
  if (!eventId) {
    return { success: false, error: "Event ID is required" };
  }

  const webApi = getXrmWebApi();
  if (!webApi) {
    return {
      success: false,
      error: "Xrm.WebApi not available - ensure running in Dataverse context",
    };
  }

  try {
    const normalizedId = eventId.replace(/[{}]/g, "");

    await webApi.updateRecord(EVENT_ENTITY, normalizedId, {
      [fieldName]: value,
    });

    return { success: true };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error(`[EventService] Failed to update ${fieldName}:`, errorMessage);

    return { success: false, error: errorMessage };
  }
}

/**
 * Update Event name field
 *
 * @param eventId - Event record GUID
 * @param newName - New event name
 * @returns Promise with success status
 */
export async function updateEventName(
  eventId: string,
  newName: string
): Promise<{ success: boolean; error?: string }> {
  return updateEventField(eventId, "sprk_eventname", newName);
}

// ─────────────────────────────────────────────────────────────────────────────
// Dirty Field Tracking and Save Operations
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Map of field names to their dirty (changed) values.
 * Only fields that have been modified are included.
 */
export type DirtyFields = Partial<Record<keyof IEventRecord, unknown>>;

/**
 * Result of saveEvent operation
 */
export interface ISaveEventResult {
  success: boolean;
  error?: string;
  /** Fields that were saved */
  savedFields?: string[];
}

/**
 * Compares original and current values to determine which fields are dirty.
 * Returns a map of only the changed fields.
 *
 * @param original - Original event record from load
 * @param current - Current event values after edits
 * @returns DirtyFields object with only changed fields
 */
export function getDirtyFields(
  original: IEventRecord,
  current: Partial<IEventRecord>
): DirtyFields {
  const dirty: DirtyFields = {};

  // List of editable fields to check
  const editableFields: (keyof IEventRecord)[] = [
    "sprk_eventname",
    "sprk_description",
    "sprk_duedate",
    "sprk_basedate",
    "sprk_finalduedate",
    "sprk_completeddate",
    "scheduledstart",
    "scheduledend",
    "sprk_location",
    "sprk_remindat",
    "statuscode",
    "sprk_priority",
    "sprk_source",
  ];

  for (const field of editableFields) {
    if (field in current) {
      const originalValue = original[field];
      const currentValue = current[field];

      // Compare values - handle null/undefined equivalence
      const originalNormalized = originalValue === null ? undefined : originalValue;
      const currentNormalized = currentValue === null ? undefined : currentValue;

      if (originalNormalized !== currentNormalized) {
        dirty[field] = currentValue;
      }
    }
  }

  return dirty;
}

/**
 * Check if there are any dirty (changed) fields
 *
 * @param dirtyFields - DirtyFields object to check
 * @returns true if there are unsaved changes
 */
export function hasDirtyFields(dirtyFields: DirtyFields): boolean {
  return Object.keys(dirtyFields).length > 0;
}

/**
 * Save Event record with only the changed (dirty) fields.
 * Uses PATCH request to update only modified fields.
 *
 * @param eventId - Event record GUID
 * @param dirtyFields - Only the fields that have changed
 * @returns Promise with save result
 */
export async function saveEvent(
  eventId: string,
  dirtyFields: DirtyFields
): Promise<ISaveEventResult> {
  // Validate inputs
  if (!eventId) {
    return {
      success: false,
      error: "Event ID is required",
    };
  }

  // Check if there are any changes to save
  if (!hasDirtyFields(dirtyFields)) {
    return {
      success: true,
      savedFields: [],
    };
  }

  const webApi = getXrmWebApi();
  if (!webApi) {
    return {
      success: false,
      error: "Xrm.WebApi not available - ensure running in Dataverse context",
    };
  }

  try {
    const normalizedId = eventId.replace(/[{}]/g, "");

    // Build the update payload with only dirty fields
    const updatePayload: Record<string, unknown> = {};
    const savedFieldNames: string[] = [];

    for (const [field, value] of Object.entries(dirtyFields)) {
      // Convert undefined to null for WebAPI
      updatePayload[field] = value === undefined ? null : value;
      savedFieldNames.push(field);
    }

    console.log(
      `[EventService] Saving ${savedFieldNames.length} field(s):`,
      savedFieldNames
    );

    // PATCH request to update only changed fields
    await webApi.updateRecord(EVENT_ENTITY, normalizedId, updatePayload);

    console.log("[EventService] Save successful");

    return {
      success: true,
      savedFields: savedFieldNames,
    };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error("[EventService] Failed to save event:", errorMessage);

    return {
      success: false,
      error: errorMessage,
    };
  }
}

/**
 * Parse Dataverse WebAPI error response to get user-friendly message
 *
 * @param error - Error from WebAPI call
 * @returns User-friendly error message
 */
export function parseWebApiError(error: unknown): string {
  if (error instanceof Error) {
    // Try to extract message from Dataverse error structure
    const message = error.message;

    // Common Dataverse error patterns
    if (message.includes("privilege")) {
      return "You do not have permission to save this record.";
    }
    if (message.includes("validation") || message.includes("required")) {
      return "Validation failed. Please check required fields.";
    }
    if (message.includes("locked") || message.includes("conflict")) {
      return "Record is locked by another user. Please try again later.";
    }
    if (message.includes("network") || message.includes("fetch")) {
      return "Network error. Please check your connection and try again.";
    }

    return message;
  }

  return "An unexpected error occurred while saving.";
}
