/**
 * Event Service - Load and update Event records from Dataverse
 *
 * Provides WebAPI operations for Event records in the EventDetailSidePane.
 * Uses the global Xrm.WebApi available in Custom Pages.
 *
 * @see design.md - Event Detail Side Pane specification
 */

import { cleanGuid, getXrm } from '@spaarke/ui-components';
import { splitFilingPayload } from "@spaarke/ui-components";
import { refileEventThroughBff } from "./childRecordWrites";
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
 * Get the Xrm.WebApi object via the shared cross-frame walker (task 081 / C-8).
 */
function getXrmWebApi(): IXrmWebApi | null {
  const webApi = getXrm()?.WebApi;
  if (webApi) {
    return webApi as unknown as IXrmWebApi;
  }

  console.warn("[EventService] Xrm.WebApi not available");
  return null;
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
    const normalizedId = cleanGuid(eventId);

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
    const normalizedId = cleanGuid(eventId);

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
    const normalizedId = cleanGuid(eventId);

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
 * Uses generic Record to support dynamic form fields from Approach A config.
 */
export type DirtyFields = Record<string, unknown>;

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

  const normalizedId = cleanGuid(eventId);

  // A field cleared in the pane arrives as `undefined`; Dataverse clears it on `null`.
  const payload: Record<string, unknown> = {};
  for (const [field, value] of Object.entries(dirtyFields)) {
    payload[field] = value === undefined ? null : value;
  }

  // Split the payload into the event's FILING (its `sprk_regarding…` lookups and regarding fields) and everything else.
  // UAC-r2 task 147 r1c (owner round 36): the filing goes through the BFF's ONE event re-file route, which takes nothing
  // else; every other field — scalars and the other lookups (completed by, approved by, …) — stays the caller's own
  // Xrm.WebApi update. ONE rule for which key is filing: the seam's `splitFilingPayload` (the server's IsFilingColumn).
  const { filing, rest } = splitFilingPayload(payload);
  const filingFields = Object.keys(filing);
  const restFields = Object.keys(rest);

  console.log(`[EventService] Saving ${filingFields.length + restFields.length} field(s):`, [...filingFields, ...restFields]);

  // The FILING FIRST, through the BFF (UAC-r2 task 147, owner rounds 28 and 36): a change to what the event is filed under
  // is a re-file — its owner is re-derived (the Secure Record Owners team under a secure record), F3 applies to a move out
  // of one, and what is filed under the event follows it. Task 147 r1c-v1 (verifier item 6): the seam's order
  // (`updateFilingThenRest`) — a refused or failed re-file writes NOTHING, so the pane never leaves the other fields saved
  // around a filing the server refused.
  if (filingFields.length > 0) {
    try {
      await refileEventThroughBff(normalizedId, filing);
      console.log("[EventService] Filing saved");
    } catch (filingError) {
      const filingMsg = messageOf(filingError);
      console.error("[EventService] Filing save failed; nothing was saved:", filingMsg);
      return { success: false, error: filingMsg, savedFields: [] };
    }
  }

  // Then every other field — the caller's own update.
  if (restFields.length > 0) {
    try {
      await webApi.updateRecord(EVENT_ENTITY, normalizedId, rest);
      console.log("[EventService] Fields saved");
    } catch (restError) {
      const restMsg = messageOf(restError);
      console.error("[EventService] Failed to save event fields:", restMsg);
      return {
        success: false,
        error: filingFields.length > 0
          ? `What the event is filed under was saved, but the other changes were not: ${restMsg}`
          : restMsg,
        savedFields: filingFields,
      };
    }
  }

  console.log("[EventService] Save successful");

  return {
    success: true,
    savedFields: [...filingFields, ...restFields],
  };
}

/** The message of a failed write: the server's (or Dataverse's) own words when there are any. */
function messageOf(error: unknown): string {
  return error instanceof Error
    ? error.message
    : (typeof error === "object" ? JSON.stringify(error) : String(error));
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
